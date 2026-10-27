/// The operator's passkeys through the real endpoints (spec 011): adding one,
/// signing in with one, every refusal, and that no user flow and no operator
/// flow can complete the other's ceremony.
module Umbral.Tests.OperatorPasskeyTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks
open Xunit
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.Flows
open Umbral.Tests.Infrastructure.PasskeyFlows
open Umbral.Tests.Infrastructure.OperatorFlows

let private noOverrides = SoftwareAuthenticator.AssertOverrides.None

/// A scalar of the app's database equals what the test expects.
let private assertScalar (app: Fixtures.TestApp) (statement: string) (expected: string) =
    task {
        let! actual = scalar app statement []
        Assert.Equal(expected, actual)
    }

let private assertRows (app: Fixtures.TestApp) (expected: int) =
    assertScalar app "SELECT count(*) FROM operator_passkeys" (string expected)

/// An app with an operator signed in and one passkey already added.
let private withOnePasskey (app: Fixtures.TestApp) =
    task {
        let! operator, token = signedInOperator app
        let authenticator = operatorAuthenticator ()
        let! response, _, credentialId = addOperatorPasskey operator token authenticator "Laptop"
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        return operator, token, authenticator, credentialId
    }

/// A refused sign-in: 400 with the notice, no admin session, the event with
/// the reason word.
let private assertSignInRefused
    (app: Fixtures.TestApp)
    (browser: Browser)
    (response: HttpResponseMessage)
    (body: Text.Json.JsonElement)
    (reason: string)
    =
    task {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        Assert.Equal("operator-passkey-signin-failed", strOf body "error")
        let! admin = browser.Get "/admin/connectors"
        Assert.NotEqual(HttpStatusCode.OK, admin.StatusCode)
        let! failed = outcomes app "operator_sign_in_failed"
        Assert.Equal(("failure", Some $"passkey_refused;reason={reason};stage=assertion"), List.last failed)
    }

/// One refused sign-in by the operator's own authenticator with `overrides`.
let private refusedWith (overrides: SoftwareAuthenticator.AssertOverrides) (reason: string) =
    task {
        use! app = Fixtures.createApp ()
        let! _, _, authenticator, credentialId = withOnePasskey app
        let! browser, response, body = signInWithOperatorPasskey app authenticator credentialId overrides
        do! assertSignInRefused app browser response body reason
    }

// ---------------------------------------------------------------------------
// Adding
// ---------------------------------------------------------------------------

[<Fact>]
let ``adding a passkey with the password stores it under the typed name`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, token = signedInOperator app
        let authenticator = operatorAuthenticator ()

        let! response, body, _ = addOperatorPasskey operator token authenticator "Work laptop"

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        Assert.Equal("/admin/credential?msg=operator-passkey-added", strOf body "redirect")
        do! assertRows app 1
        do! assertScalar app "SELECT name FROM operator_passkeys" "Work laptop"
        let! added = outcomes app "operator_passkey_added"
        Assert.Equal<(string * string option) list>([ "success", None ], added)
    }

[<Fact>]
let ``adding a second passkey can be confirmed with the first`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, token, first, firstId = withOnePasskey app
        let second = operatorAuthenticator ()

        let! response, body, _ = addOperatorPasskeyConfirmedBy operator token second "Phone" first firstId

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        Assert.Equal("/admin/credential?msg=operator-passkey-added", strOf body "redirect")
        do! assertRows app 2
        do! assertScalar app "SELECT count(*) FROM operator_passkeys WHERE name = 'Phone'" "1"
        // The confirming passkey was used, so it is dated.
        do! assertScalar app "SELECT count(*) FROM operator_passkeys WHERE name = 'Laptop' AND last_used_utc IS NOT NULL" "1"
    }

[<Fact>]
let ``a wrong password at add options adds nothing and is recorded`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, token = signedInOperator app

        let! response, body =
            postJson operator "/admin/credential/passkeys/options" token {| name = "Laptop"; current = "not-the-credential" |}

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        Assert.Equal("credential-current-incorrect", strOf body "error")
        do! assertRows app 0
        let! added = outcomes app "operator_passkey_added"
        Assert.Equal<(string * string option) list>([ "failure", Some "recheck_password_mismatch" ], added)
    }

[<Fact>]
let ``add options with no re-check at all is refused`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, token = signedInOperator app

        let! response, body = postJson operator "/admin/credential/passkeys/options" token {| name = "Laptop" |}

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        Assert.Equal("credential-current-incorrect", strOf body "error")
        do! assertRows app 0
        let! added = outcomes app "operator_passkey_added"
        Assert.Equal<(string * string option) list>([ "failure", Some "recheck_missing" ], added)
    }

[<Fact>]
let ``an eleventh passkey is refused at add options`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, token = signedInOperator app

        for seed in 1..10 do
            let! outcome =
                OperatorPasskeys.add
                    app.DbConfig
                    app.Clock
                    { Id = OperatorPasskeys.newId ()
                      CredentialId = [| byte seed; 7uy |]
                      PublicKey = [| 9uy; byte seed |]
                      SignCount = 0u
                      Name = $"Key {seed}"
                      Transports = []
                      CreatedUtc = DateTimeOffset.MinValue
                      LastUsedUtc = None }

            Assert.Equal(OperatorPasskeys.Added, outcome)

        let! response, body =
            postJson operator "/admin/credential/passkeys/options" token {| name = "One too many"; current = OperatorCredential |}

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        Assert.Equal("operator-passkey-limit", strOf body "error")
        do! assertRows app 10
        let! added = outcomes app "operator_passkey_added"
        Assert.Equal<(string * string option) list>([ "failure", Some "limit_reached" ], added)
    }

// ---------------------------------------------------------------------------
// Signing in
// ---------------------------------------------------------------------------

[<Fact>]
let ``a passkey signs the operator in`` () =
    task {
        use! app = Fixtures.createApp ()
        let! _, _, authenticator, credentialId = withOnePasskey app

        let! browser, response, body = signInWithOperatorPasskey app authenticator credentialId noOverrides

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        Assert.Equal("/admin/connectors", strOf body "redirect")
        let! users = browser.Get "/admin/users"
        Assert.Equal(HttpStatusCode.OK, users.StatusCode)
        do! assertScalar app "SELECT count(*) FROM operator_passkeys WHERE last_used_utc IS NOT NULL" "1"
        let! signedIn = outcomes app "operator_signed_in"
        Assert.Equal(("success", Some "passkey"), List.last signedIn)
    }

[<Fact>]
let ``the same sign-in answer sent twice is refused the second time`` () =
    task {
        use! app = Fixtures.createApp ()
        let! _, _, authenticator, credentialId = withOnePasskey app
        let! browser, token, started = startOperatorSignIn app
        let credential = authenticator.Assert(started.GetProperty "options", credentialId)
        let answer = {| ceremony = strOf started "ceremony"; credential = credential |}

        let! first, _ = postJson browser "/admin/signin/passkey" token answer
        Assert.Equal(HttpStatusCode.OK, first.StatusCode)

        let replayer = Browser(app.Client)
        let! replayToken = replayer.TokenFrom "/admin/signin"
        let! second, body = postJson replayer "/admin/signin/passkey" replayToken answer
        do! assertSignInRefused app replayer second body "ceremony_unknown_or_expired"
    }

[<Fact>]
let ``an answer for another origin is refused`` () =
    refusedWith { noOverrides with Origin = Some "https://evil.example" } "origin_mismatch"

[<Fact>]
let ``an answer without user verification is refused`` () =
    refusedWith { noOverrides with Flags = Some 0x01uy } "user_verification_missing"

[<Fact>]
let ``a signature counter that did not advance is refused`` () =
    task {
        use! app = Fixtures.createApp ()
        let! _, _, authenticator, credentialId = withOnePasskey app

        let! _, first, _ = signInWithOperatorPasskey app authenticator credentialId noOverrides
        Assert.Equal(HttpStatusCode.OK, first.StatusCode)

        // The stored counter is now 1; an answer that says 1 again is a clone's.
        let! browser, response, body =
            signInWithOperatorPasskey app authenticator credentialId { noOverrides with Counter = Some 1u }

        do! assertSignInRefused app browser response body "sign_count_regressed"
    }

// ---------------------------------------------------------------------------
// Isolation between the operator's passkeys and the users'
// ---------------------------------------------------------------------------

[<Fact>]
let ``an operator passkey cannot sign a user in, and a user's cannot sign the operator in`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, authenticator, credentialId = withOnePasskey app
        do! enablePasskeyConnector operator "invitations"

        let! link = inviteNewPerson operator "Ana"
        let userAuthenticator = operatorAuthenticator ()
        let! registered, _ = registerThroughInvitation (Browser(app.Client)) userAuthenticator (secretOf link) (Some "Ana") "Phone"
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode)
        let userCredentialId = (List.head userAuthenticator.Credentials).Id

        // The operator's passkey answers the user sign-in's ceremony.
        let visitor = Browser(app.Client)
        let! token = visitor.TokenFrom "/connect/passkey/start?returnUrl=%2F"
        let! response, body = signInWithPasskey visitor authenticator credentialId "/" token
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        Assert.Equal("passkey-sign-in-failed", strOf body "error")
        Assert.False(visitor.Cookies.ContainsKey "umbral_session", "no user session was started")

        // The user's passkey answers the operator sign-in's ceremony.
        let! browser, operatorToken, started = startOperatorSignIn app

        let credential = userAuthenticator.Assert(started.GetProperty "options", userCredentialId)

        let! refused, refusedBody =
            postJson browser "/admin/signin/passkey" operatorToken {| ceremony = strOf started "ceremony"; credential = credential |}

        do! assertSignInRefused app browser refused refusedBody "unknown_credential"

        // The operator's passkey never shows among the people.
        let! users = operator.GetHtml "/admin/users"
        Assert.DoesNotContain("Laptop", users)
    }

// ---------------------------------------------------------------------------
// The limit
// ---------------------------------------------------------------------------

[<Fact>]
let ``the passkey sign-in completion shares the admin limit with the password sign-in`` () =
    task {
        use! app = Fixtures.createApp ()
        do! AdminAuth.setInitialCredential app.DbConfig app.Clock "correct-horse-battery-staple" :> Task

        let send (path: string) (content: HttpContent) =
            task {
                use request = new HttpRequestMessage(HttpMethod.Post, path)
                request.Headers.Add("X-Forwarded-For", "203.0.113.7")
                request.Content <- content
                return! app.Client.SendAsync request
            }

        // The admin window is 10; the password sign-in spends all of it.
        for _ in 1..10 do
            let! response = send "/admin/signin" (new FormUrlEncodedContent(dict [ "credential", "wrong" ]))
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode)

        let! over = send "/admin/signin/passkey" (new StringContent("{}", Encoding.UTF8, "application/json"))
        Assert.Equal(HttpStatusCode.TooManyRequests, over.StatusCode)
    }

// ---------------------------------------------------------------------------
// No credential material
// ---------------------------------------------------------------------------

[<Fact>]
let ``the credential id appears in no event and not on the Credential screen`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, _, authenticator, credentialId = withOnePasskey app
        let! _, signedIn, _ = signInWithOperatorPasskey app authenticator credentialId noOverrides
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode)
        // A refusal as well, since its event carries a detail.
        let! _, refused, _ = signInWithOperatorPasskey app authenticator credentialId { noOverrides with Counter = Some 1u }
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode)

        let id = SoftwareAuthenticator.toBase64Url credentialId

        let! details = scalar app "SELECT group_concat(coalesce(detail, ''), ' ') FROM security_events" []
        Assert.DoesNotContain(id, details)

        let! page = operator.GetHtml "/admin/credential"
        Assert.DoesNotContain(id, page)
    }

// The sign-in page and the switch (spec 011, FR-006 and FR-008).

/// One stored passkey, so that password sign-in can be switched off.
let private storeOnePasskey (app: Fixtures.TestApp) =
    task {
        let key: OperatorPasskeys.OperatorPasskey =
            { Id = OperatorPasskeys.newId ()
              CredentialId = [| 4uy; 5uy; 6uy |]
              PublicKey = [| 1uy |]
              SignCount = 0u
              Name = "Phone"
              Transports = []
              CreatedUtc = DateTimeOffset.MinValue
              LastUsedUtc = None }

        let! added = OperatorPasskeys.add app.DbConfig app.Clock key
        Assert.Equal(OperatorPasskeys.Added, added)
    }

let private switchPasswordOff (app: Fixtures.TestApp) =
    task {
        do! storeOnePasskey app
        let! off = OperatorPasskeys.turnPasswordSignInOff app.DbConfig app.Clock
        Assert.Equal(OperatorPasskeys.SwitchedOff, off)
    }

[<Fact>]
let ``while password sign-in is on the page offers both, the passkey button hidden until scripted`` () =
    task {
        use! app = Fixtures.createApp ()
        do! claimOperator app (Browser app.Client)

        let! html = Browser(app.Client).GetHtml "/admin/signin"
        Assert.Contains("type=\"password\"", html)
        Assert.Contains("Use your password, or a passkey.", html)
        Assert.Contains("data-operator-passkey-signin", html)
        Assert.Contains("data-scripted", html)
        Assert.Contains("hidden", html)
        Assert.Contains("um-or", html)
        Assert.Contains("A passkey needs JavaScript. Turn it on for this page, or sign in with your password.", html)
        Assert.Contains("__RequestVerificationToken", html)
        Assert.Contains("/assets/passkeys.js", html)
    }

[<Fact>]
let ``while password sign-in is off the page has the passkey button alone`` () =
    task {
        use! app = Fixtures.createApp ()
        do! claimOperator app (Browser app.Client)
        do! switchPasswordOff app

        let! html = Browser(app.Client).GetHtml "/admin/signin"
        Assert.DoesNotContain("type=\"password\"", html)
        Assert.DoesNotContain("action=\"/admin/signin\"", html)
        Assert.DoesNotContain("um-or", html)
        Assert.Contains("Sign in with your passkey.", html)
        Assert.Contains("data-operator-passkey-signin", html)
        Assert.Contains("um-btn-primary", html)
        Assert.Contains("A passkey needs JavaScript. Turn it on for this page.", html)
        Assert.DoesNotContain("or sign in with your password", html)
        // The script reads the token from the page even with no form on it.
        Assert.Contains("__RequestVerificationToken", html)
    }

[<Fact>]
let ``while password sign-in is off the right password is refused like a wrong one`` () =
    task {
        use! app = Fixtures.createApp ()
        do! claimOperator app (Browser app.Client)
        do! switchPasswordOff app

        let browser = Browser(app.Client)
        let! response = browser.PostForm("/admin/signin", "/admin/signin", [ "credential", OperatorCredential ])
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("/admin/signin?msg=signin-incorrect", location response)

        let! screen = browser.Get "/admin/apps"
        Assert.Equal(HttpStatusCode.Redirect, screen.StatusCode)
        Assert.Contains("/admin/signin", location screen)

        do!
            assertScalar
                app
                "SELECT count(*) FROM security_events WHERE event_type = 'operator_sign_in_failed' AND detail = 'credential_mismatch'"
                "1"
    }
