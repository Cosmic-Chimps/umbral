/// Removing one linked provider account: what the removal does to the sessions
/// and tokens made through it (specs/018 T002b, FR-009 and FR-012). Every place
/// that accepts a remembered end-user session (the authorize endpoint, the
/// token endpoint, /account/passkeys, the passkey ceremony of purpose `Add`)
/// asks whether the user still holds the credential the session was made with.
///
/// Driven through the real endpoints, with the fake provider and a software
/// authenticator in place of the browser's.
module Umbral.Tests.UserLinkRecheckTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Options
open Xunit
open Umbral.Domains
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.Flows
open Umbral.Tests.Infrastructure.PasskeyFlows

let private newAuthenticator () =
    SoftwareAuthenticator.SoftwareAuthenticator("umbral.test", Fixtures.TestOrigin)

/// An operator, the fake connector, the passkey connector open to sign-up, and
/// an app registered. Returns the client id and the secret.
let private setup (app: Fixtures.TestApp) =
    task {
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! seedFakeConnector app
        do! enablePasskeyConnector operator "open"
        let! clientId, secret = registerApp operator "Grafana"
        return operator, clientId, secret
    }

let private scope = "openid email offline_access"

/// A fresh browser signed in through the fake provider; its code is exchanged
/// and the refresh token refreshed once, so the credential is proven to survive
/// a refresh. Returns the browser and its newest refresh token.
let private signInWithTokens (app: Fixtures.TestApp) (clientId: string) (secret: string) (subject: string) (email: string) =
    task {
        let browser = Browser(app.Client)
        let! signedIn = signInViaFake browser clientId scope subject email
        let code = Uri.UnescapeDataString(Result.defaultWith failwith signedIn)
        let! exchanged = exchangeCode app clientId secret code
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode)
        let! tokens = json exchanged
        let! refreshed = refresh app.Client clientId secret (strOf tokens "refresh_token")
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode)
        let! refreshedTokens = json refreshed
        return browser, strOf refreshedTokens "refresh_token"
    }

let private signInThroughFake (app: Fixtures.TestApp) (clientId: string) (subject: string) (email: string) =
    task {
        let browser = Browser(app.Client)
        let! signedIn = signInViaFake browser clientId "openid email" subject email
        Assert.True(Result.isOk signedIn, "the fake sign-in reaches the app")
        return browser
    }

let private run (app: Fixtures.TestApp) (sql: string) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- sql
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

let private removeLinkOf (app: Fixtures.TestApp) (userId: string) (subject: string) =
    task {
        let! outcome =
            UserStore.removeLink app.DbConfig app.Clock userId (Linking.fingerprint "fake" subject)

        match outcome with
        | UserStore.LinkRemoved _ -> ()
        | other -> failwith $"the link was not removed: {other}"
    }

let private signInWithPasskeyOn (app: Fixtures.TestApp) (clientId: string) (authenticator: SoftwareAuthenticator.SoftwareAuthenticator) =
    task {
        let browser = Browser(app.Client)
        let! returnUrl, _, token = startPasskeySignIn browser clientId "openid"
        let! response, _ = signInWithPasskey browser authenticator (List.head authenticator.Credentials).Id returnUrl token
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        return browser
    }

let private addPasskeyAs (browser: Browser) (authenticator: SoftwareAuthenticator.SoftwareAuthenticator) (nickname: string) =
    task {
        let! token = browser.TokenFrom "/account/passkeys"

        let! response, started =
            postJson browser "/passkey/registration/options" token {| mode = "add"; nickname = nickname |}

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        let credential = authenticator.Register(started.GetProperty "options")

        let! finished, _ =
            postJson browser "/passkey/registration" token {| ceremony = strOf started "ceremony"; credential = credential |}

        Assert.Equal(HttpStatusCode.OK, finished.StatusCode)
    }

/// The `detail` of every `sign_in_failed` event, oldest first.
let private failureDetails (app: Fixtures.TestApp) =
    task {
        let! failures = events app "sign_in_failed"
        return failures |> List.choose (fun (_, _, detail) -> detail)
    }

let private cookieOptions (app: Fixtures.TestApp) =
    app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(Sessions.Scheme)

/// The ticket of the end-user session cookie a browser holds.
let private sessionTicket (app: Fixtures.TestApp) (browser: Browser) : AuthenticationTicket =
    match (cookieOptions app).TicketDataFormat.Unprotect(Uri.UnescapeDataString browser.Cookies[Sessions.CookieName]) with
    | null -> failwith "the session cookie does not unprotect"
    | ticket -> ticket

let private sessionClaims (app: Fixtures.TestApp) (browser: Browser) = (sessionTicket app browser).Principal

/// The session cookie, as a browser holds it, with its credential claim taken
/// out: a session made before specs/018.
let private withoutCredential (app: Fixtures.TestApp) (browser: Browser) =
    let ticket = sessionTicket app browser
    let identity = Seq.head ticket.Principal.Identities

    match identity.FindFirst Sessions.CredentialClaim with
    | null -> failwith "the session has no credential claim to take out"
    | claim -> identity.RemoveClaim claim

    let stripped = AuthenticationTicket(ticket.Principal, ticket.Properties, ticket.AuthenticationScheme)
    browser.Cookies[Sessions.CookieName] <- Uri.EscapeDataString((cookieOptions app).TicketDataFormat.Protect stripped)
    Assert.Equal(None, Sessions.currentCredential (sessionClaims app browser))

let private assertSentToSignIn (response: HttpResponseMessage) =
    Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
    Assert.StartsWith("/signin?returnUrl=", response.Headers.Location |> string)

let private assertSignedOut (browser: Browser) =
    Assert.True(
        not (browser.Cookies.ContainsKey Sessions.CookieName) || browser.Cookies[Sessions.CookieName] = "",
        "the session cookie is cleared"
    )

let private assertCodeIssued (response: HttpResponseMessage) =
    Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
    Assert.StartsWith(RedirectUri, location response)
    Assert.Contains("code=", location response)

let private assertInvalidGrant (response: HttpResponseMessage) =
    task {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        let! body = json response
        Assert.Equal("invalid_grant", strOf body "error")
    }

// ---------------------------------------------------------------------------
// The sessions and tokens made through a removed link
// ---------------------------------------------------------------------------

[<Fact>]
let ``after a removal the stranger's session is signed out at authorize and the remaining link's session is honoured`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, _ = setup app

        let! sam = signInThroughFake app clientId "fake-user-1" "sam@example.com"
        let! stranger = signInThroughFake app clientId "fake-user-2" "sam@example.com"
        let! samId = userIdOf app "fake-user-1"
        let! joinedId = userIdOf app "fake-user-2"
        Assert.Equal(samId, joinedId)

        // Before the removal both sessions are honoured.
        let! before = stranger.Get(authorizeUrl clientId "openid")
        assertCodeIssued before

        do! removeLinkOf app samId "fake-user-2"

        let! refused = stranger.Get(authorizeUrl clientId "openid")
        assertSentToSignIn refused
        assertSignedOut stranger

        // Signed out for good: the next request is simply a request with no session.
        let! again = stranger.Get(authorizeUrl clientId "openid")
        assertSentToSignIn again

        let! details = failureDetails app
        Assert.Equal("credential_removed;stage=authorize", Assert.Single details)

        let! failures = events app "sign_in_failed"
        Assert.Equal((Some samId, Some "fake", Some "credential_removed;stage=authorize"), Assert.Single failures)

        let! honoured = sam.Get(authorizeUrl clientId "openid")
        assertCodeIssued honoured
    }

[<Fact>]
let ``after a removal the stranger's refresh token is invalid_grant and the remaining link's refresh token works, also after a refresh`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, secret = setup app

        // Each signs in, exchanges the code and refreshes once before the removal:
        // the credential must survive a refresh to be judged after it.
        let! _, samRefresh = signInWithTokens app clientId secret "fake-user-1" "sam@example.com"
        let! _, strangerRefresh = signInWithTokens app clientId secret "fake-user-2" "sam@example.com"
        let! samId = userIdOf app "fake-user-1"

        do! removeLinkOf app samId "fake-user-2"

        let! refused = refresh app.Client clientId secret strangerRefresh
        do! assertInvalidGrant refused

        let! details = failureDetails app
        Assert.Equal("credential_removed;stage=token", Assert.Single details)

        let! honoured = refresh app.Client clientId secret samRefresh
        Assert.Equal(HttpStatusCode.OK, honoured.StatusCode)

        // And the refresh token it returned is honoured in turn.
        let! tokens = json honoured
        let! next = refresh app.Client clientId secret (strOf tokens "refresh_token")
        Assert.Equal(HttpStatusCode.OK, next.StatusCode)

        let! stillOne = failureDetails app
        Assert.Equal(1, List.length stillOne)
    }

[<Fact>]
let ``deleting a passkey ends the sessions made with it at authorize and leaves the link's session alone`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, _ = setup app

        let! sam = signInThroughFake app clientId "fake-user-1" "sam@example.com"
        let laptop = newAuthenticator ()
        do! addPasskeyAs sam laptop "Laptop"

        let! withPasskey = signInWithPasskeyOn app clientId laptop
        let! before = withPasskey.Get(authorizeUrl clientId "openid")
        assertCodeIssued before

        do! run app "DELETE FROM passkeys WHERE nickname = 'Laptop'"

        let! refused = withPasskey.Get(authorizeUrl clientId "openid")
        assertSentToSignIn refused
        assertSignedOut withPasskey

        let! details = failureDetails app
        Assert.Equal("credential_removed;stage=authorize", Assert.Single details)

        let! honoured = sam.Get(authorizeUrl clientId "openid")
        assertCodeIssued honoured
    }

// ---------------------------------------------------------------------------
// The account pages
// ---------------------------------------------------------------------------

[<Fact>]
let ``after a removal the passkeys page sends the stranger to sign in and signs them out, while the remaining link still sees it`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, _ = setup app

        let! sam = signInThroughFake app clientId "fake-user-1" "sam@example.com"
        let! stranger = signInThroughFake app clientId "fake-user-2" "sam@example.com"
        let! samId = userIdOf app "fake-user-1"

        let! visible = stranger.Get "/account/passkeys"
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode)

        do! removeLinkOf app samId "fake-user-2"

        let! refused = stranger.Get "/account/passkeys"
        assertSentToSignIn refused
        assertSignedOut stranger

        let! details = failureDetails app
        Assert.Equal("credential_removed;stage=account", Assert.Single details)

        let! samPage = sam.Get "/account/passkeys"
        Assert.Equal(HttpStatusCode.OK, samPage.StatusCode)
    }

[<Fact>]
let ``after a removal the stranger cannot remove the owner's passkey through the page's post`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, _ = setup app

        let! sam = signInThroughFake app clientId "fake-user-1" "sam@example.com"
        let! stranger = signInThroughFake app clientId "fake-user-2" "sam@example.com"
        let! samId = userIdOf app "fake-user-1"

        // A passkey the owner added with nothing to do with the stranger's link.
        do!
            run
                app
                $"INSERT INTO passkeys (id, user_id, credential_id, public_key, nickname, created_utc)
                  VALUES ('pk-owner', '{samId}', x'0102', x'AA', 'Owner key', '2026-01-01T00:00:00Z')"

        let! token = stranger.TokenFrom "/account/passkeys"
        do! removeLinkOf app samId "fake-user-2"

        let message = new HttpRequestMessage(HttpMethod.Post, "/account/passkeys/pk-owner/remove")

        message.Content <-
            new FormUrlEncodedContent(
                [ Collections.Generic.KeyValuePair("__RequestVerificationToken", token) ]
            )

        let! response = stranger.Send message
        assertSentToSignIn response
        assertSignedOut stranger

        let! kept = scalar app "SELECT count(*) FROM passkeys WHERE id = 'pk-owner'" []
        Assert.Equal("1", kept)
        ignore sam
    }

[<Fact>]
let ``after the passkey is deleted the passkeys page sends the session made with it to sign in`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, _ = setup app

        let! sam = signInThroughFake app clientId "fake-user-1" "sam@example.com"
        let laptop = newAuthenticator ()
        do! addPasskeyAs sam laptop "Laptop"
        let! withPasskey = signInWithPasskeyOn app clientId laptop

        let! visible = withPasskey.Get "/account/passkeys"
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode)

        do! run app "DELETE FROM passkeys WHERE nickname = 'Laptop'"

        let! refused = withPasskey.Get "/account/passkeys"
        assertSentToSignIn refused
        assertSignedOut withPasskey

        let! details = failureDetails app
        Assert.Equal("credential_removed;stage=account", Assert.Single details)
    }

// ---------------------------------------------------------------------------
// The passkey ceremony of purpose Add
// ---------------------------------------------------------------------------

[<Fact>]
let ``an add ceremony whose session lost its credential between start and finish stores nothing`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, _ = setup app

        let! _ = signInThroughFake app clientId "fake-user-1" "sam@example.com"
        let! stranger = signInThroughFake app clientId "fake-user-2" "sam@example.com"
        let! samId = userIdOf app "fake-user-1"

        let! token = stranger.TokenFrom "/account/passkeys"

        let! started, options =
            postJson stranger "/passkey/registration/options" token {| mode = "add"; nickname = "Sneaky" |}

        Assert.Equal(HttpStatusCode.OK, started.StatusCode)
        let credential = (newAuthenticator ()).Register(options.GetProperty "options")

        do! removeLinkOf app samId "fake-user-2"

        let! finished, _ =
            postJson stranger "/passkey/registration" token {| ceremony = strOf options "ceremony"; credential = credential |}

        Assert.Equal(HttpStatusCode.BadRequest, finished.StatusCode)
        // The finish ran the session check (it ended the session), not only the antiforgery one.
        assertSignedOut stranger

        let! stored = scalar app "SELECT count(*) FROM passkeys" []
        Assert.Equal("0", stored)

        let! details = failureDetails app
        Assert.Contains("credential_removed;stage=account", details)
    }

[<Fact>]
let ``an add ceremony cannot start from a session whose credential was removed`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, _ = setup app

        let! _ = signInThroughFake app clientId "fake-user-1" "sam@example.com"
        let! stranger = signInThroughFake app clientId "fake-user-2" "sam@example.com"
        let! samId = userIdOf app "fake-user-1"

        let! token = stranger.TokenFrom "/account/passkeys"
        do! removeLinkOf app samId "fake-user-2"

        let! response, _ =
            postJson stranger "/passkey/registration/options" token {| mode = "add"; nickname = "Sneaky" |}

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        assertSignedOut stranger

        let! details = failureDetails app
        Assert.Contains("credential_removed;stage=account", details)
    }

[<Fact>]
let ``an add ceremony still finishes for a session that holds its credential`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, _ = setup app

        let! sam = signInThroughFake app clientId "fake-user-1" "sam@example.com"
        let! _ = signInThroughFake app clientId "fake-user-2" "sam@example.com"
        let! samId = userIdOf app "fake-user-1"

        // Another link is removed meanwhile: Sam's own is still held.
        let! token = sam.TokenFrom "/account/passkeys"

        let! _, options =
            postJson sam "/passkey/registration/options" token {| mode = "add"; nickname = "Fine" |}

        let credential = (newAuthenticator ()).Register(options.GetProperty "options")
        do! removeLinkOf app samId "fake-user-2"

        let! finished, _ =
            postJson sam "/passkey/registration" token {| ceremony = strOf options "ceremony"; credential = credential |}

        Assert.Equal(HttpStatusCode.OK, finished.StatusCode)
        let! stored = scalar app "SELECT count(*) FROM passkeys" []
        Assert.Equal("1", stored)
    }

// ---------------------------------------------------------------------------
// A session from before specs/018: no credential claim
// ---------------------------------------------------------------------------

[<Fact>]
let ``a session without a credential claim is honoured through a connector the user still holds a link of`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, _ = setup app

        let! sam = signInThroughFake app clientId "fake-user-1" "sam@example.com"
        withoutCredential app sam

        let! response = sam.Get(authorizeUrl clientId "openid")
        assertCodeIssued response
        let! details = failureDetails app
        Assert.Empty details
    }

[<Fact>]
let ``a session without a credential claim is refused through a connector the user no longer holds a link of`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, clientId, _ = setup app

        // Ana has a passkey (from an invitation, so no origin) and one provider link.
        let! invitation = inviteNewPerson operator "Ana"
        let! registered, _ = registerThroughInvitation (Browser(app.Client)) (newAuthenticator ()) (secretOf invitation) (Some "Ana") "Phone"
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode)
        let! anaId = scalar app "SELECT user_id FROM passkeys WHERE nickname = 'Phone'" []

        do!
            run
                app
                $"INSERT INTO linked_accounts (provider_kind, provider_subject, user_id, linked_utc, last_used_utc)
                  VALUES ('fake', 'fake-user-9', '{anaId}', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')"

        let! ana = signInThroughFake app clientId "fake-user-9" "ana@example.com"
        Assert.Equal(Some anaId, Sessions.currentSubject (sessionClaims app ana))
        withoutCredential app ana

        let! before = ana.Get(authorizeUrl clientId "openid")
        assertCodeIssued before

        do! removeLinkOf app anaId "fake-user-9"

        let! refused = ana.Get(authorizeUrl clientId "openid")
        assertSentToSignIn refused
        assertSignedOut ana

        let! details = failureDetails app
        Assert.Equal("credential_removed;stage=authorize", Assert.Single details)
    }

[<Fact>]
let ``a passkey session without a credential claim is honoured`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, _ = setup app

        let! sam = signInThroughFake app clientId "fake-user-1" "sam@example.com"
        let laptop = newAuthenticator ()
        do! addPasskeyAs sam laptop "Laptop"
        let! withPasskey = signInWithPasskeyOn app clientId laptop
        withoutCredential app withPasskey

        let! response = withPasskey.Get(authorizeUrl clientId "openid")
        assertCodeIssued response
    }
