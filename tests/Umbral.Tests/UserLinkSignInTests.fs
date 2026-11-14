/// Removing one linked provider account: what the sign-in side of it does
/// (specs/018 T002a). A provider account recorded in `removed_links` signs in
/// again as an unverified, unlinked new user (FR-006); every end-user session
/// carries the reference of the credential it started with, and a passkey added
/// on /account/passkeys records the reference of the session that added it
/// (FR-007); the reference never leaves in a token or a response (FR-007, FR-008).
///
/// Driven through the real endpoints, with the fake provider and a software
/// authenticator in place of the browser's.
module Umbral.Tests.UserLinkSignInTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Options
open Microsoft.AspNetCore.Authentication.Cookies
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
/// an app registered. Returns the operator, the client id and the secret.
let private setup (app: Fixtures.TestApp) =
    task {
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! seedFakeConnector app
        do! enablePasskeyConnector operator "open"
        let! clientId, secret = registerApp operator "Grafana"
        return operator, clientId, secret
    }

/// The claims of the end-user session cookie a browser holds, read through the
/// cookie scheme's own ticket format.
let private sessionClaims (app: Fixtures.TestApp) (browser: Browser) =
    let options =
        app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(Sessions.Scheme)

    let ticket =
        options.TicketDataFormat.Unprotect(Uri.UnescapeDataString browser.Cookies[Sessions.CookieName])

    match ticket with
    | null -> failwith "the session cookie does not unprotect"
    | ticket -> ticket.Principal

let private credentialOf (app: Fixtures.TestApp) (browser: Browser) =
    Sessions.currentCredential (sessionClaims app browser)

let private signedInThroughFake
    (app: Fixtures.TestApp)
    (clientId: string)
    (subject: string)
    (email: string)
    =
    task {
        let browser = Browser(app.Client)
        let! signedIn = signInViaFake browser clientId "openid email" subject email
        Assert.True(Result.isOk signedIn, "the fake sign-in reaches the app")
        return browser
    }

let private addPasskey (browser: Browser) (authenticator: SoftwareAuthenticator.SoftwareAuthenticator) (nickname: string) =
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

/// The `added_via` of the passkey with this nickname: None for NULL.
let private addedVia (app: Fixtures.TestApp) (nickname: string) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT added_via FROM passkeys WHERE nickname = @n"
        command.Parameters.AddWithValue("@n", nickname) |> ignore
        let! value = command.ExecuteScalarAsync()
        return if isNull value || value :? DBNull then None else Some(string value)
    }

let private passkeyId (app: Fixtures.TestApp) (nickname: string) =
    scalar app "SELECT id FROM passkeys WHERE nickname = @n" [ "@n", nickname ]

// ---------------------------------------------------------------------------
// FR-006: a removed provider account signs in unverified
// ---------------------------------------------------------------------------

[<Fact>]
let ``a removed provider account signs in as a new unverified user and does not join the verified one`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, _ = setup app

        let! _ = signedInThroughFake app clientId "fake-user-1" "sam@example.com"
        let! samId = userIdOf app "fake-user-1"
        let! samVerified = scalar app "SELECT email_verified FROM users WHERE id = @u" [ "@u", samId ]
        Assert.Equal("1", samVerified)

        // The control: a second, unremoved account asserting the same verified
        // address joins Sam, so the difference below is the removal's.
        let! _ = signedInThroughFake app clientId "fake-user-2" "sam@example.com"
        let! joinedId = userIdOf app "fake-user-2"
        Assert.Equal(samId, joinedId)

        do!
            task {
                let! _ =
                    scalar
                        app
                        "INSERT INTO removed_links (provider_kind, provider_subject, removed_utc)
                         VALUES ('fake', 'fake-user-3', '2026-11-14T00:00:00.0000000+00:00') RETURNING 1"
                        []

                return ()
            }

        let! stranger = signedInThroughFake app clientId "fake-user-3" "sam@example.com"
        let! strangerId = userIdOf app "fake-user-3"

        Assert.NotEqual<string>(samId, strangerId)
        let! strangerVerified = scalar app "SELECT email_verified FROM users WHERE id = @u" [ "@u", strangerId ]
        Assert.Equal("0", strangerVerified)
        let! samStill = scalar app "SELECT email_verified FROM users WHERE id = @u" [ "@u", samId ]
        Assert.Equal("1", samStill)
        let! users = scalar app "SELECT count(*) FROM users" []
        Assert.Equal("2", users)

        let! created = events app "user_created"
        Assert.Contains((Some samId, Some "fake", None), created)
        Assert.Contains((Some strangerId, Some "fake", Some "reason=link_removed"), created)
        let! linked = events app "user_linked"
        Assert.Equal(1, List.length linked)

        // The sign-in itself went through, with a session of its own.
        Assert.True(stranger.Cookies.ContainsKey Sessions.CookieName)
        Assert.Equal(
            Some(Linking.credentialValue (Linking.LinkCredential(Linking.fingerprint "fake" "fake-user-3"))),
            credentialOf app stranger
        )
    }

// ---------------------------------------------------------------------------
// FR-007: the credential claim of a session
// ---------------------------------------------------------------------------

[<Fact>]
let ``a provider sign-in, a passkey sign-in, an invitation and a sign-up each start a session with their credential`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, clientId, _ = setup app

        // A provider sign-in: link:<fingerprint>.
        let! sam = signedInThroughFake app clientId "fake-user-1" "sam@example.com"

        Assert.Equal(
            Some(Linking.credentialValue (Linking.LinkCredential(Linking.fingerprint "fake" "fake-user-1"))),
            credentialOf app sam
        )

        // An invitation: passkey:<id of the passkey it created>.
        let! link = inviteNewPerson operator "Ana"
        let ana = newAuthenticator ()
        let anaBrowser = Browser(app.Client)
        let! r, _ = registerThroughInvitation anaBrowser ana (secretOf link) (Some "Ana") "Phone"
        Assert.Equal(HttpStatusCode.OK, r.StatusCode)
        let! phoneId = passkeyId app "Phone"

        Assert.Equal(Some(Linking.credentialValue (Linking.PasskeyCredential phoneId)), credentialOf app anaBrowser)

        // A passkey sign-in on a fresh browser: passkey:<id of the one used>.
        let again = Browser(app.Client)
        let! returnUrl, _, token = startPasskeySignIn again clientId "openid"
        let! response, _ = signInWithPasskey again ana (List.head ana.Credentials).Id returnUrl token
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        Assert.Equal(Some(Linking.credentialValue (Linking.PasskeyCredential phoneId)), credentialOf app again)

        // A sign-up: passkey:<id of the passkey it created>.
        let visitor = Browser(app.Client)
        let! returnUrl2, _, token2 = startPasskeySignIn visitor clientId "openid"

        let! _, started =
            postJson visitor "/passkey/registration/options" token2 {| mode = "signup"; displayName = "Noor"; nickname = "Key"; returnUrl = returnUrl2 |}

        let credential = (newAuthenticator ()).Register(started.GetProperty "options")

        let! signedUp, _ =
            postJson visitor "/passkey/registration" token2 {| ceremony = strOf started "ceremony"; credential = credential |}

        Assert.Equal(HttpStatusCode.OK, signedUp.StatusCode)
        let! keyId = passkeyId app "Key"
        Assert.Equal(Some(Linking.credentialValue (Linking.PasskeyCredential keyId)), credentialOf app visitor)
    }

[<Fact>]
let ``a passkey added while signed in through a provider records the link, while signed in with a passkey records that passkey, and a sign-up or an invitation records nothing`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, clientId, _ = setup app

        // Added while signed in through the provider.
        let! sam = signedInThroughFake app clientId "fake-user-1" "sam@example.com"
        let laptop = newAuthenticator ()
        do! addPasskey sam laptop "Laptop"

        let fingerprint = Linking.fingerprint "fake" "fake-user-1"
        let! viaLink = addedVia app "Laptop"
        Assert.Equal(Some $"link:{fingerprint}", viaLink)

        // Added while signed in with that passkey: the passkey's own id.
        let samWithPasskey = Browser(app.Client)
        let! returnUrl, _, token = startPasskeySignIn samWithPasskey clientId "openid"
        let! response, _ = signInWithPasskey samWithPasskey laptop (List.head laptop.Credentials).Id returnUrl token
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        do! addPasskey samWithPasskey (newAuthenticator ()) "Phone"

        let! laptopId = passkeyId app "Laptop"
        let! viaPasskey = addedVia app "Phone"
        Assert.Equal(Some $"passkey:{laptopId}", viaPasskey)

        // An invitation and a sign-up record nothing.
        let! link = inviteNewPerson operator "Ana"
        let! r, _ = registerThroughInvitation (Browser(app.Client)) (newAuthenticator ()) (secretOf link) (Some "Ana") "Invited"
        Assert.Equal(HttpStatusCode.OK, r.StatusCode)
        let! viaInvitation = addedVia app "Invited"
        Assert.Equal(None, viaInvitation)

        let visitor = Browser(app.Client)
        let! returnUrl2, _, token2 = startPasskeySignIn visitor clientId "openid"

        let! _, started =
            postJson visitor "/passkey/registration/options" token2 {| mode = "signup"; displayName = "Noor"; nickname = "Fresh"; returnUrl = returnUrl2 |}

        let credential = (newAuthenticator ()).Register(started.GetProperty "options")

        let! signedUp, _ =
            postJson visitor "/passkey/registration" token2 {| ceremony = strOf started "ceremony"; credential = credential |}

        Assert.Equal(HttpStatusCode.OK, signedUp.StatusCode)
        let! viaSignup = addedVia app "Fresh"
        Assert.Equal(None, viaSignup)
    }

// ---------------------------------------------------------------------------
// FR-007: the reference never leaves in a token or a response
// ---------------------------------------------------------------------------

let private mentionsCredential (text: string) =
    text.Contains Sessions.CredentialClaim
    || text.Contains OpenIddictSetup.CredentialClaim
    || text.Contains "link:"
    || text.Contains "passkey:"

[<Fact>]
let ``no token response, id token or userinfo response carries the credential, also after a refresh`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, secret = setup app

        let browser = Browser(app.Client)

        let! signedIn =
            signInViaFake browser clientId "openid profile email offline_access" "fake-user-1" "sam@example.com"

        let code = Uri.UnescapeDataString(Result.defaultWith failwith signedIn)
        let! exchanged = exchangeCode app clientId secret code
        let! body = exchanged.Content.ReadAsStringAsync()
        let tokens = JsonDocument.Parse(body).RootElement.Clone()
        Assert.False(mentionsCredential body, "the token response")

        let idToken = Journeys.segmentOf 1 (strOf tokens "id_token")
        Assert.False(mentionsCredential (idToken.GetRawText()), "the id token")

        let! info = userInfo app.Client (strOf tokens "access_token")
        let! infoBody = info.Content.ReadAsStringAsync()
        Assert.Equal(HttpStatusCode.OK, info.StatusCode)
        Assert.False(mentionsCredential infoBody, "the userinfo response")

        let! refreshed = refresh app.Client clientId secret (strOf tokens "refresh_token")
        let! refreshedBody = refreshed.Content.ReadAsStringAsync()
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode)
        Assert.False(mentionsCredential refreshedBody, "the refresh response")
        let refreshedTokens = JsonDocument.Parse(refreshedBody).RootElement.Clone()
        let newIdToken = Journeys.segmentOf 1 (strOf refreshedTokens "id_token")
        Assert.False(mentionsCredential (newIdToken.GetRawText()), "the refreshed id token")

        let! info2 = userInfo app.Client (strOf refreshedTokens "access_token")
        let! info2Body = info2.Content.ReadAsStringAsync()
        Assert.False(mentionsCredential info2Body, "the userinfo response after a refresh")
    }
