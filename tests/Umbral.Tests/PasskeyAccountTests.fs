/// A person's passkeys (spec 008, Story 3). This part of the file tests the
/// store behind the passkeys page, which has no endpoint of its own to reach it
/// through yet: the last-way-in rule and a ceremony's single use. The page's own
/// tests are added with the page.
module Umbral.Tests.PasskeyAccountTests

open System
open System.Threading.Tasks
open Xunit
open Umbral.Domains
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure

let private seedUser (app: Fixtures.TestApp) (id: string) =
    Users.insertForTesting
        app.DbConfig
        app.Clock
        { Id = id
          DisplayName = $"User {id}"
          Email = None
          EmailVerified = false
          AvatarUrl = None
          IsEnabled = true
          FirstSeenUtc = app.Clock.GetUtcNow()
          LastSeenUtc = app.Clock.GetUtcNow() }

let private passkeyFor (userId: string) (id: string) (credential: byte array) : Passkeys.Passkey =
    { Id = id
      UserId = userId
      CredentialId = credential
      PublicKey = [| 1uy; 2uy; 3uy |]
      SignCount = 0u
      Nickname = $"Key {id}"
      AaGuid = None
      Transports = [ "internal"; "hybrid" ]
      BackupEligible = Some true
      CreatedUtc = DateTimeOffset.UnixEpoch
      LastUsedUtc = None }

let private insert (app: Fixtures.TestApp) (passkey: Passkeys.Passkey) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use transaction = connection.BeginTransaction()
        do! Passkeys.insertIn connection transaction app.Clock passkey
        do! transaction.CommitAsync()
    }

let private linkProvider (app: Fixtures.TestApp) (userId: string) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()

        command.CommandText <-
            "INSERT INTO linked_accounts (provider_kind, provider_subject, user_id, linked_utc, last_used_utc)
             VALUES ('github', @subject, @userId, @now, @now)"

        command.Parameters.AddWithValue("@subject", $"gh-{userId}") |> ignore
        command.Parameters.AddWithValue("@userId", userId) |> ignore
        command.Parameters.AddWithValue("@now", app.Clock.GetUtcNow().ToString "o") |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

// ---------------------------------------------------------------------------
// The passkey rows
// ---------------------------------------------------------------------------

[<Fact>]
let ``an inserted passkey reads back by id, by credential id and in the user's list`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedUser app "user-1"
        do! insert app { passkeyFor "user-1" "pk-1" [| 9uy; 8uy |] with AaGuid = Some(Guid.Parse "11111111-2222-3333-4444-555555555555") }

        let! byId = Passkeys.findById app.DbConfig "pk-1"
        let! byCredential = Passkeys.findByCredentialId app.DbConfig [| 9uy; 8uy |]
        let! listed = Passkeys.listForUser app.DbConfig "user-1"

        Assert.Equal(byId, byCredential)
        Assert.Equal(1, listed.Length)

        let stored = byId.Value
        Assert.Equal<byte array>([| 9uy; 8uy |], stored.CredentialId)
        Assert.Equal<byte array>([| 1uy; 2uy; 3uy |], stored.PublicKey)
        Assert.Equal(0u, stored.SignCount)
        Assert.Equal("Key pk-1", stored.Nickname)
        Assert.Equal(Some(Guid.Parse "11111111-2222-3333-4444-555555555555"), stored.AaGuid)
        Assert.Equal<string list>([ "internal"; "hybrid" ], stored.Transports)
        Assert.Equal(Some true, stored.BackupEligible)

        // Created now, by the clock, and never used.
        Assert.Equal(app.Clock.GetUtcNow(), stored.CreatedUtc)
        Assert.Equal(None, stored.LastUsedUtc)

        let! unknown = Passkeys.findByCredentialId app.DbConfig [| 1uy |]
        Assert.True unknown.IsNone
    }

[<Fact>]
let ``counting and the uniqueness check see every passkey`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedUser app "user-1"
        do! seedUser app "user-2"
        do! insert app (passkeyFor "user-1" "pk-1" [| 1uy |])
        do! insert app (passkeyFor "user-1" "pk-2" [| 2uy |])
        do! insert app (passkeyFor "user-2" "pk-3" [| 3uy |])

        let! one = Passkeys.countForUser app.DbConfig "user-1"
        let! two = Passkeys.countForUser app.DbConfig "user-2"
        let! none = Passkeys.countForUser app.DbConfig "nobody"
        Assert.Equal((2, 1, 0), (one, two, none))

        let! taken = Passkeys.credentialIdExists app.DbConfig [| 3uy |]
        let! free = Passkeys.credentialIdExists app.DbConfig [| 4uy |]
        Assert.True taken
        Assert.False free
    }

[<Fact>]
let ``a second passkey with a credential id already stored is refused by the constraint`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedUser app "user-1"
        do! seedUser app "user-2"
        do! insert app (passkeyFor "user-1" "pk-1" [| 1uy |])

        let! error = Record.ExceptionAsync(fun () -> insert app (passkeyFor "user-2" "pk-2" [| 1uy |]) :> Task)

        match error with
        | null -> failwith "the second insert was accepted"
        | error ->
            Assert.True(Passkeys.isCredentialIdConflict error)
            Assert.False(Passkeys.isCredentialIdConflict (InvalidOperationException "other"))
    }

[<Fact>]
let ``recording a use replaces the counter and sets the time of use`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedUser app "user-1"
        do! insert app (passkeyFor "user-1" "pk-1" [| 1uy |])

        app.Clock.Advance(TimeSpan.FromHours 2.)
        do! Passkeys.recordUse app.DbConfig app.Clock "pk-1" 7u

        let! stored = Passkeys.findById app.DbConfig "pk-1"
        Assert.Equal(7u, stored.Value.SignCount)
        Assert.Equal(Some(app.Clock.GetUtcNow()), stored.Value.LastUsedUtc)
    }

[<Fact>]
let ``a counter above the signed 32-bit range is stored and read back whole`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedUser app "user-1"
        do! insert app (passkeyFor "user-1" "pk-1" [| 1uy |])

        do! Passkeys.recordUse app.DbConfig app.Clock "pk-1" UInt32.MaxValue

        let! stored = Passkeys.findById app.DbConfig "pk-1"
        Assert.Equal(UInt32.MaxValue, stored.Value.SignCount)
    }

[<Fact>]
let ``remove deletes a passkey whoever holds it and says whether it was there`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedUser app "user-1"
        do! insert app (passkeyFor "user-1" "pk-1" [| 1uy |])

        let! first = Passkeys.remove app.DbConfig "pk-1"
        let! second = Passkeys.remove app.DbConfig "pk-1"

        Assert.True first
        Assert.False second
        let! count = Passkeys.countForUser app.DbConfig "user-1"
        Assert.Equal(0, count)
    }

// ---------------------------------------------------------------------------
// The last way in (FR-020)
// ---------------------------------------------------------------------------

[<Fact>]
let ``a person with two passkeys can remove one`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedUser app "user-1"
        do! insert app (passkeyFor "user-1" "pk-1" [| 1uy |])
        do! insert app (passkeyFor "user-1" "pk-2" [| 2uy |])

        let! outcome = Passkeys.removeOwnIfNotLast app.DbConfig "user-1" "pk-1"

        Assert.Equal(Passkeys.Removed, outcome)
        let! left = Passkeys.listForUser app.DbConfig "user-1"
        Assert.Equal<string list>([ "pk-2" ], left |> List.map _.Id)
    }

[<Fact>]
let ``a person with one passkey and no provider cannot remove it`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedUser app "user-1"
        do! insert app (passkeyFor "user-1" "pk-1" [| 1uy |])

        let! outcome = Passkeys.removeOwnIfNotLast app.DbConfig "user-1" "pk-1"

        Assert.Equal(Passkeys.LastWayIn, outcome)
        let! count = Passkeys.countForUser app.DbConfig "user-1"
        Assert.Equal(1, count)
    }

[<Fact>]
let ``a person with one passkey and a linked provider can remove it`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedUser app "user-1"
        do! linkProvider app "user-1"
        do! insert app (passkeyFor "user-1" "pk-1" [| 1uy |])

        let! outcome = Passkeys.removeOwnIfNotLast app.DbConfig "user-1" "pk-1"

        Assert.Equal(Passkeys.Removed, outcome)
        let! count = Passkeys.countForUser app.DbConfig "user-1"
        Assert.Equal(0, count)
    }

[<Fact>]
let ``someone else's passkey and an unknown id are both not found, and nothing is removed`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedUser app "user-1"
        do! seedUser app "user-2"
        do! insert app (passkeyFor "user-1" "pk-1" [| 1uy |])
        do! insert app (passkeyFor "user-1" "pk-2" [| 2uy |])
        do! insert app (passkeyFor "user-2" "pk-3" [| 3uy |])

        // user-2 names user-1's passkey: it is theirs to remove only if it is theirs.
        let! others = Passkeys.removeOwnIfNotLast app.DbConfig "user-2" "pk-1"
        let! unknown = Passkeys.removeOwnIfNotLast app.DbConfig "user-1" "nope"

        Assert.Equal(Passkeys.NotFound, others)
        Assert.Equal(Passkeys.NotFound, unknown)

        let! count = Passkeys.countForUser app.DbConfig "user-1"
        Assert.Equal(2, count)
    }

[<Fact>]
let ``two removals racing for the last two passkeys leave one`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedUser app "user-1"
        do! insert app (passkeyFor "user-1" "pk-1" [| 1uy |])
        do! insert app (passkeyFor "user-1" "pk-2" [| 2uy |])

        let! outcomes =
            Task.WhenAll [| Passkeys.removeOwnIfNotLast app.DbConfig "user-1" "pk-1"
                            Passkeys.removeOwnIfNotLast app.DbConfig "user-1" "pk-2" |]

        let removed = outcomes |> Array.filter ((=) Passkeys.Removed) |> Array.length
        Assert.Equal(1, removed)
        Assert.Contains(Passkeys.LastWayIn, outcomes)

        let! count = Passkeys.countForUser app.DbConfig "user-1"
        Assert.Equal(1, count)
    }

// ---------------------------------------------------------------------------
// Ceremonies: single use, five minutes
// ---------------------------------------------------------------------------

let private ceremony (id: string) (purpose: Passkeys.Purpose) : Passkeys.Ceremony =
    { Id = id
      Purpose = purpose
      OptionsJson = """{"challenge":"abc"}"""
      UserId = Some "user-1"
      InvitationId = Some "inv-1"
      DisplayName = Some "Ada"
      Nickname = Some ""
      ReturnUrl = Some "/connect/authorize?x=1"
      CreatedUtc = DateTimeOffset.UnixEpoch
      ExpiresUtc = DateTimeOffset.UnixEpoch }

[<Fact>]
let ``a started ceremony is consumed once, with everything it was started with`` () =
    task {
        use! app = Fixtures.createApp ()
        let started = ceremony (Passkeys.newCeremonyId ()) Passkeys.Invite
        do! Passkeys.startCeremony app.DbConfig app.Clock started

        let! first = Passkeys.consumeCeremony app.DbConfig app.Clock started.Id
        let! second = Passkeys.consumeCeremony app.DbConfig app.Clock started.Id

        let taken = first.Value
        Assert.Equal(started.Id, taken.Id)
        Assert.Equal(Passkeys.Invite, taken.Purpose)
        Assert.Equal(started.OptionsJson, taken.OptionsJson)
        Assert.Equal(Some "user-1", taken.UserId)
        Assert.Equal(Some "inv-1", taken.InvitationId)
        Assert.Equal(Some "Ada", taken.DisplayName)
        Assert.Equal(Some "", taken.Nickname)
        Assert.Equal(Some "/connect/authorize?x=1", taken.ReturnUrl)

        // Begun now, and good for the ceremony lifetime.
        Assert.Equal(app.Clock.GetUtcNow(), taken.CreatedUtc)
        Assert.Equal(app.Clock.GetUtcNow() + Umbral.Domains.Passkeys.CeremonyLifetime, taken.ExpiresUtc)

        Assert.True second.IsNone
    }

[<Fact>]
let ``a ceremony without context keeps every optional part empty`` () =
    task {
        use! app = Fixtures.createApp ()

        let started =
            { ceremony "c1" Passkeys.Signin with
                UserId = None
                InvitationId = None
                DisplayName = None
                Nickname = None
                ReturnUrl = None }

        do! Passkeys.startCeremony app.DbConfig app.Clock started
        let! taken = Passkeys.consumeCeremony app.DbConfig app.Clock "c1"

        Assert.Equal(Passkeys.Signin, taken.Value.Purpose)
        Assert.Equal((None, None, None, None, None), (taken.Value.UserId, taken.Value.InvitationId, taken.Value.DisplayName, taken.Value.Nickname, taken.Value.ReturnUrl))
    }

[<Fact>]
let ``a ceremony is no longer there after five minutes`` () =
    task {
        use! app = Fixtures.createApp ()
        do! Passkeys.startCeremony app.DbConfig app.Clock (ceremony "c1" Passkeys.Signin)

        app.Clock.Advance(TimeSpan.FromMinutes 5.)

        let! late = Passkeys.consumeCeremony app.DbConfig app.Clock "c1"
        Assert.True late.IsNone
    }

[<Fact>]
let ``a ceremony just inside the five minutes still completes`` () =
    task {
        use! app = Fixtures.createApp ()
        do! Passkeys.startCeremony app.DbConfig app.Clock (ceremony "c1" Passkeys.Signin)

        app.Clock.Advance(TimeSpan.FromMinutes 5. - TimeSpan.FromSeconds 1.)

        let! inTime = Passkeys.consumeCeremony app.DbConfig app.Clock "c1"
        Assert.True inTime.IsSome
    }

[<Fact>]
let ``an unknown ceremony id is nothing`` () =
    task {
        use! app = Fixtures.createApp ()
        let! none = Passkeys.consumeCeremony app.DbConfig app.Clock "never-started"
        Assert.True none.IsNone
    }

[<Fact>]
let ``starting a ceremony removes the ones that have expired`` () =
    task {
        use! app = Fixtures.createApp ()
        do! Passkeys.startCeremony app.DbConfig app.Clock (ceremony "old" Passkeys.Signin)

        app.Clock.Advance(TimeSpan.FromMinutes 6.)
        do! Passkeys.startCeremony app.DbConfig app.Clock (ceremony "new" Passkeys.Signin)

        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT group_concat(id) FROM passkey_ceremonies"
        let! ids = command.ExecuteScalarAsync()
        Assert.Equal("new", string ids)
    }

[<Fact>]
let ``two simultaneous completions of one ceremony give it to exactly one`` () =
    task {
        use! app = Fixtures.createApp ()
        do! Passkeys.startCeremony app.DbConfig app.Clock (ceremony "c1" Passkeys.Signin)

        let! results =
            Task.WhenAll [| for _ in 1..8 -> Passkeys.consumeCeremony app.DbConfig app.Clock "c1" |]

        Assert.Equal(1, results |> Array.filter Option.isSome |> Array.length)
    }
