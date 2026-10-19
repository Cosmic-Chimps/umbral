module Umbral.Tests.PasskeyInvitationTests

open System
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure

let private seed (app: Fixtures.TestApp) =
    task {
        do!
            ConnectorStore.save
                app.DbConfig
                app.Clock
                { Slug = "passkey"
                  Kind = Passkey
                  DisplayName = "Passkey"
                  ClientId = ""
                  ClientSecretRef = ""
                  ClientSecretHint = ""
                  IssuerUrl = None
                  AllowedEmailDomains = []
                  PasskeyEnrolment = InvitationsOnly
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None
    }

let private user (app: Fixtures.TestApp) (id: string) =
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

let private stateOf (app: Fixtures.TestApp) (secret: string) =
    task {
        let! found = PasskeyInvitations.findBySecret app.DbConfig app.Clock secret
        return found |> Option.map snd
    }

[<Fact>]
let ``a created invitation is open and listed`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seed app
        let! invitation, secret = PasskeyInvitations.create app.DbConfig app.Clock "passkey" None "note"
        let! state = stateOf app secret
        Assert.Equal(Some PasskeyInvitations.Open, state)
        let! open' = PasskeyInvitations.listOpen app.DbConfig app.Clock "passkey"
        Assert.Equal<string list>([ invitation.Id ], open' |> List.map _.Id)
    }

[<Fact>]
let ``consuming twice succeeds once`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seed app
        let! invitation, secret = PasskeyInvitations.create app.DbConfig app.Clock "passkey" None ""

        let consume () =
            task {
                use! connection = Database.openConnection app.DbConfig
                use transaction = connection.BeginTransaction()
                let! spent = PasskeyInvitations.consumeIn connection transaction app.Clock invitation.Id
                do! transaction.CommitAsync()
                return spent
            }

        let! first = consume ()
        let! second = consume ()
        Assert.True first
        Assert.False second
        let! state = stateOf app secret
        Assert.Equal(Some PasskeyInvitations.Used, state)
        let! open' = PasskeyInvitations.listOpen app.DbConfig app.Clock "passkey"
        Assert.Empty open'
    }

[<Fact>]
let ``an invitation expires after seven days`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seed app
        let! _, secret = PasskeyInvitations.create app.DbConfig app.Clock "passkey" None ""
        app.Clock.Advance(TimeSpan.FromDays 7. + TimeSpan.FromSeconds 1.)
        let! state = stateOf app secret
        Assert.Equal(Some PasskeyInvitations.Expired, state)
        let! open' = PasskeyInvitations.listOpen app.DbConfig app.Clock "passkey"
        Assert.Empty open'
    }

[<Fact>]
let ``a revoked invitation is revoked`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seed app
        let! invitation, secret = PasskeyInvitations.create app.DbConfig app.Clock "passkey" None ""
        let! revoked = PasskeyInvitations.revoke app.DbConfig app.Clock invitation.Id
        Assert.True revoked
        let! state = stateOf app secret
        Assert.Equal(Some PasskeyInvitations.Revoked, state)
    }

[<Fact>]
let ``an invitation for a disabled user is user-gone`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seed app
        do! user app "u1"
        let! _, secret = PasskeyInvitations.create app.DbConfig app.Clock "passkey" (Some "u1") ""
        do! Users.setEnabled app.DbConfig "u1" false
        let! state = stateOf app secret
        Assert.Equal(Some PasskeyInvitations.UserGone, state)
        let! open' = PasskeyInvitations.listOpen app.DbConfig app.Clock "passkey"
        Assert.Empty open'
    }

[<Fact>]
let ``an invitation for a deleted user is user-gone`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seed app
        do! user app "u2"
        let! _, secret = PasskeyInvitations.create app.DbConfig app.Clock "passkey" (Some "u2") ""

        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- "PRAGMA foreign_keys = OFF; DELETE FROM users WHERE id = 'u2'"
        let! _ = command.ExecuteNonQueryAsync()

        let! state = stateOf app secret
        Assert.Equal(Some PasskeyInvitations.UserGone, state)
    }

[<Fact>]
let ``an unknown secret finds nothing`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seed app
        let! found = PasskeyInvitations.findBySecret app.DbConfig app.Clock "nope"
        Assert.True found.IsNone
    }
