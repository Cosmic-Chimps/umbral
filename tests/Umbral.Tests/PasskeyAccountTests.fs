/// A person's passkeys (spec 008, Story 3). This part of the file tests the
/// store behind the passkeys page, which has no endpoint of its own to reach it
/// through yet: the last-way-in rule and a ceremony's single use. The page's own
/// tests are added with the page.
module Umbral.Tests.PasskeyAccountTests

open System
open System.Net
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading.Tasks
open Xunit
open Umbral.Domains
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.Flows
open Umbral.Tests.Infrastructure.PasskeyFlows

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

// ---------------------------------------------------------------------------
// The passkeys page and its endpoints
// ---------------------------------------------------------------------------

let private newAuthenticator () =
    SoftwareAuthenticator.SoftwareAuthenticator("umbral.test", Fixtures.TestOrigin)

type private World =
    { App: Fixtures.TestApp
      Operator: Browser
      ClientId: string
      Secret: string
      Sam: Browser
      SamId: string
      Ana: SoftwareAuthenticator.SoftwareAuthenticator
      AnaId: string }

/// An operator, the fake and passkey connectors, an app, Sam (signed in through
/// the fake provider) and Ana (enrolled through an invitation).
let private world (app: Fixtures.TestApp) =
    task {
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! seedFakeConnector app
        do! enablePasskeyConnector operator "invitations"
        let! clientId, secret = registerApp operator "Grafana"

        let sam = Browser(app.Client)
        let! signedIn = signInViaFake sam clientId "openid" "fake-user-1" "sam@example.com"
        Assert.True(Result.isOk signedIn)
        let! samId = userIdOf app "fake-user-1"

        let! link = inviteNewPerson operator "Ana"
        let ana = newAuthenticator ()
        let! r, _ = registerThroughInvitation (Browser(app.Client)) ana (secretOf link) (Some "Ana") "Phone"
        Assert.Equal(HttpStatusCode.OK, r.StatusCode)
        let! anaId = scalar app "SELECT id FROM users WHERE display_name = 'Ana'" []

        return
            { App = app
              Operator = operator
              ClientId = clientId
              Secret = secret
              Sam = sam
              SamId = samId
              Ana = ana
              AnaId = anaId }
    }

let private addOptionsFor (browser: Browser) (nickname: string) =
    task {
        let! token = browser.TokenFrom "/account/passkeys"
        let! response, started = postJson browser "/passkey/registration/options" token {| mode = "add"; nickname = nickname |}
        return token, response, started
    }

let private addPasskey (browser: Browser) (authenticator: SoftwareAuthenticator.SoftwareAuthenticator) (nickname: string) =
    task {
        let! token, response, started = addOptionsFor browser nickname
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        let credential = authenticator.Register(started.GetProperty "options")
        return! postJson browser "/passkey/registration" token {| ceremony = strOf started "ceremony"; credential = credential |}
    }

let private idOfPasskey (app: Fixtures.TestApp) (nickname: string) =
    scalar app "SELECT id FROM passkeys WHERE nickname = @n" [ "@n", nickname ]

let private rows (html: string) = Regex.Matches(html, "<article[^>]*data-passkey=").Count

let private waysIn (html: string) =
    Regex.Match(html, "<p[^>]*data-ways-in[^>]*>(.*?)</p>", RegexOptions.Singleline).Groups[1].Value

[<Fact>]
let ``without a session the page sends to sign-in and a fake sign-in lands back on it`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app
        let visitor = Browser(app.Client)
        let! response = visitor.Get "/account/passkeys"
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Equal("/signin?returnUrl=%2Faccount%2Fpasskeys", location response)

        let! picker = visitor.Get "/signin?returnUrl=%2Faccount%2Fpasskeys"
        Assert.Equal(HttpStatusCode.OK, picker.StatusCode)
        let! toProvider = visitor.Get "/connect/fake/start?returnUrl=%2Faccount%2Fpasskeys"
        let providerUrl = location toProvider
        let decoded = Uri.UnescapeDataString providerUrl
        let redirect = Regex.Match(decoded, "redirect_uri=([^&]+)").Groups[1].Value
        let state = Regex.Match(decoded, "state=([^&]+)").Groups[1].Value

        let! approved =
            visitor.PostForm(
                providerUrl,
                "/dev/fake-provider/authorize",
                [ "redirect_uri", redirect
                  "state", state
                  "subject", "fake-user-1"
                  "email", "sam@example.com"
                  "displayName", "Test User"
                  "emailVerified", "on" ]
            )

        let! back = visitor.Get(location approved)
        Assert.Equal("/account/passkeys", location back)
        let! page = visitor.Get "/account/passkeys"
        Assert.Equal(HttpStatusCode.OK, page.StatusCode)
        let! html = page.Content.ReadAsStringAsync()
        Assert.Contains("data-screen=\"passkeys\"", html)
        ignore w
    }

[<Fact>]
let ``a person with no passkey sees the provider they signed in with and the add form`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app
        let! html = w.Sam.GetHtml "/account/passkeys"
        Assert.Contains("Fake", waysIn html)
        Assert.DoesNotContain("Passkey", waysIn html)
        Assert.DoesNotContain("data-passkey=", html)
        Assert.Contains("data-passkey-add", html)
        Assert.Contains("<noscript data-javascript-required", html)
    }

[<Fact>]
let ``adding passkeys lists them, signs in with the same sub and removing one revokes it`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app
        let laptop = newAuthenticator ()
        let! r, body = addPasskey w.Sam laptop "Laptop"
        Assert.Equal(HttpStatusCode.OK, r.StatusCode)
        Assert.Equal("/account/passkeys?msg=passkey-added", strOf body "redirect")

        let! html = w.Sam.GetHtml "/account/passkeys"
        Assert.Equal(1, rows html)
        Assert.Contains("Laptop", html)
        Assert.Contains(app.Clock.GetUtcNow().ToString("yyyy-MM-dd"), html)
        Assert.Contains("Passkey", waysIn html)
        Assert.Contains("Fake", waysIn html)

        let! added = events app "passkey_registered"
        Assert.Equal<(string option * string option * string option) list>(
            [ Some w.SamId, Some "passkey", Some "via=add" ],
            added |> List.filter (fun (_, _, d) -> d = Some "via=add")
        )

        let second = newAuthenticator ()
        let! r2, _ = addPasskey w.Sam second "Phone"
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode)
        let! html2 = w.Sam.GetHtml "/account/passkeys"
        Assert.Equal(2, rows html2)

        // Signing in with the Laptop credential: Sam's sub, the passkey idp, the stored email.
        let credentialId = (List.head laptop.Credentials).Id
        let browser = Browser(app.Client)
        let! returnUrl, _, token = startPasskeySignIn browser w.ClientId "openid email profile"
        let! response, signed = signInWithPasskey browser laptop credentialId returnUrl token
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        let! code = codeFromRedirect browser (strOf signed "redirect")
        let! exchanged = exchangeCode app w.ClientId w.Secret code
        let! tokens = json exchanged
        let idToken = segmentOf 1 (strOf tokens "id_token")
        Assert.Equal(w.SamId, strOf idToken "sub")
        Assert.Equal("passkey", strOf idToken "idp")
        let! email = scalar app "SELECT email FROM users WHERE id = @u" [ "@u", w.SamId ]
        Assert.Equal(email, strOf idToken "email")
        Assert.True(idToken.GetProperty("email_verified").GetBoolean())

        // Remove it.
        let! id = idOfPasskey app "Laptop"
        let! removed = w.Sam.PostForm("/account/passkeys", $"/account/passkeys/{id}/remove", [])
        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode)
        Assert.Contains("passkey-removed", location removed)
        let! gone = scalar app "SELECT count(*) FROM passkeys WHERE id = @i" [ "@i", id ]
        Assert.Equal("0", gone)

        let again = Browser(app.Client)
        let! returnUrl2, _, token2 = startPasskeySignIn again w.ClientId "openid"
        let! _, started = postJson again "/passkey/assertion/options" token2 {| returnUrl = returnUrl2 |}
        let credential = laptop.Assert(started.GetProperty "options", credentialId)
        let! refused, refusedBody = postJson again "/passkey/assertion" token2 {| ceremony = strOf started "ceremony"; credential = credential |}
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode)
        Assert.Equal("passkey-sign-in-failed", strOf refusedBody "error")
        let! failed = events app "sign_in_failed"
        let _, _, detail = List.last failed
        Assert.Equal(Some "passkey_refused;reason=unknown_credential;stage=assertion", detail)

        let! removedEvents = events app "passkey_removed"
        Assert.Equal<(string option * string option * string option) list>([ Some w.SamId, Some "passkey", Some "by=user" ], removedEvents)
    }

[<Fact>]
let ``removing another person's passkey is not found and changes nothing`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app
        let! anaPasskey = idOfPasskey app "Phone"
        let! response = w.Sam.PostForm("/account/passkeys", $"/account/passkeys/{anaPasskey}/remove", [])
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("passkey-not-found", location response)
        let! count = scalar app "SELECT count(*) FROM passkeys WHERE id = @i" [ "@i", anaPasskey ]
        Assert.Equal("1", count)
    }

[<Fact>]
let ``a person with only one passkey and no provider cannot remove it`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app
        let anaBrowser = Browser(app.Client)
        let! returnUrl, _, token = startPasskeySignIn anaBrowser w.ClientId "openid"
        let! response, _ = signInWithPasskey anaBrowser w.Ana (List.head w.Ana.Credentials).Id returnUrl token
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        let! id = idOfPasskey app "Phone"
        let! removed = anaBrowser.PostForm("/account/passkeys", $"/account/passkeys/{id}/remove", [])
        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode)
        Assert.Contains("passkey-last-cannot-remove", location removed)
        let! count = scalar app "SELECT count(*) FROM passkeys WHERE id = @i" [ "@i", id ]
        Assert.Equal("1", count)
    }

[<Fact>]
let ``ten passkeys fill the page and an eleventh is refused`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app

        for i in 1..10 do
            let! r, _ = addPasskey w.Sam (newAuthenticator ()) $"Key {i}"
            Assert.Equal(HttpStatusCode.OK, r.StatusCode)

        let! html = w.Sam.GetHtml "/account/passkeys"
        Assert.Equal(10, rows html)
        Assert.Contains("data-passkeys-limit", html)
        Assert.DoesNotContain("data-passkey-add", html)

        let! _, response, body = addOptionsFor w.Sam "Eleventh"
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        ignore body
        let! detail =
            scalar app "SELECT detail FROM security_events WHERE detail LIKE '%passkey_limit_reached%' LIMIT 1" []
        Assert.Contains("passkey_limit_reached", detail)
    }

[<Fact>]
let ``a disabled person's session is cleared and sent to sign-in`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app
        let! toggled = w.Operator.PostForm("/admin/users", $"/admin/users/{w.SamId}/toggle", [])
        Assert.Equal(HttpStatusCode.Redirect, toggled.StatusCode)
        let! response = w.Sam.Get "/account/passkeys"
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Equal("/signin?returnUrl=%2Faccount%2Fpasskeys", location response)
        let cleared =
            match response.Headers.TryGetValues "Set-Cookie" with
            | true, values ->
                (Option.ofObj values |> Option.defaultValue Seq.empty)
                |> Seq.exists (fun v -> v.StartsWith "umbral_session=" && v.Contains "1970")
            | _ -> false
        Assert.True(cleared, "the session cookie is expired")
    }

[<Fact>]
let ``with the connector deleted the page says so, removing still works and the home link goes`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app
        let! _, _ = addPasskey w.Sam (newAuthenticator ()) "Laptop"
        let! _, _ = addPasskey w.Sam (newAuthenticator ()) "Spare"
        let! home = (Browser(app.Client)).GetHtml "/"
        Assert.Contains("data-passkeys-link", home)

        let! deleted = w.Operator.PostForm("/admin/connectors", "/admin/connectors/passkey/delete", [])
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode)

        let! html = w.Sam.GetHtml "/account/passkeys"
        Assert.Contains("data-passkeys-not-enabled", html)
        Assert.DoesNotContain("data-passkey-add", html)
        let! id = idOfPasskey app "Laptop"
        let! removed = w.Sam.PostForm("/account/passkeys", $"/account/passkeys/{id}/remove", [])
        Assert.Contains("passkey-removed", location removed)

        let! homeAfter = (Browser(app.Client)).GetHtml "/"
        Assert.DoesNotContain("data-passkeys-link", homeAfter)
    }

[<Fact>]
let ``the page wears the plain chrome even when the session came through a branded app`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app
        let! accent = w.Operator.PostForm("/admin/apps", $"/admin/apps/{w.ClientId}/branding/accent", [ "accent", "#f26b1d" ])
        Assert.Equal(HttpStatusCode.Redirect, accent.StatusCode)
        let sam = Browser(app.Client)
        let! signedIn = signInViaFake sam w.ClientId "openid" "fake-user-1" "sam@example.com"
        Assert.True(Result.isOk signedIn)
        let! html = sam.GetHtml "/account/passkeys"
        Assert.Contains("data-screen=\"passkeys\"", html)
        Assert.DoesNotContain("data-accent", html)
    }

[<Fact>]
let ``removing without an antiforgery token is refused and keeps the passkey`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app
        let! id = idOfPasskey app "Phone"
        let! response = w.Sam.PostWithoutToken($"/account/passkeys/{id}/remove", [])
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        let! count = scalar app "SELECT count(*) FROM passkeys WHERE id = @i" [ "@i", id ]
        Assert.Equal("1", count)
    }
