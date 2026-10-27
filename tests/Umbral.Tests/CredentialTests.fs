/// FR-017a: the operator credential is replaceable by an operator who is
/// already signed in, and replacing it ends every other admin session.
module Umbral.Tests.CredentialTests

open System.Net
open System.Net.Http
open System.Threading.Tasks
open Xunit
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

let private replacement = "a-brand-new-credential"

let private change (browser: Browser) (current: string) (next: string) (confirm: string) =
    browser.PostForm(
        "/admin/credential",
        "/admin/credential",
        [ "current", current
          "credential", next
          "confirm", confirm ]
    )

let private signInAs (app: Fixtures.TestApp) (credential: string) =
    task {
        let browser = Browser(app.Client)
        let! response = browser.PostForm("/admin/signin", "/admin/signin", [ "credential", credential ])
        return browser, response
    }

/// Whether `browser` is still the operator: an admin screen answers 200, and
/// anything else is a redirect to sign-in.
let private stillSignedIn (browser: Browser) =
    task {
        let! response = browser.Get "/admin/apps"
        return response.StatusCode = HttpStatusCode.OK
    }

let private events (app: Fixtures.TestApp) (eventType: string) =
    scalar
        app
        "SELECT COALESCE(GROUP_CONCAT(actor || ':' || outcome || ':' || COALESCE(detail, ''), ' '), '') FROM security_events WHERE event_type = @t"
        [ "@t", eventType ]

[<Fact>]
let ``the screen is for the operator only`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator

        let stranger = Browser(app.Client)
        let! page = stranger.Get "/admin/credential"
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode)
        Assert.Contains("/admin/signin", location page)

        // And the POST, even with a real token, changes nothing.
        let! token = operator.TokenFrom "/admin/credential"
        use message = new HttpRequestMessage(HttpMethod.Post, "/admin/credential")

        message.Content <-
            new FormUrlEncodedContent(
                dict
                    [ "__RequestVerificationToken", token
                      "current", OperatorCredential
                      "credential", replacement
                      "confirm", replacement ]
            )

        let! posted = stranger.Send message
        Assert.NotEqual(HttpStatusCode.OK, posted.StatusCode)
        let! still = AdminAuth.verify app.DbConfig OperatorCredential
        Assert.True still
    }

[<Fact>]
let ``the operator can reach the screen from the navigation`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator

        let! html = operator.GetHtml "/admin/apps"
        Assert.Contains("href=\"/admin/credential\"", html)

        let! screen = operator.GetHtml "/admin/credential"
        Assert.Contains("data-screen=\"credential\"", screen)
    }

[<Fact>]
let ``replacing the credential works, and only the new one signs in afterwards`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator

        let! response = change operator OperatorCredential replacement replacement
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("credential-replaced", location response)

        // The person who did it is still signed in: nothing sends them to the
        // sign-in form to prove what they just typed.
        let! stillIn = stillSignedIn operator
        Assert.True stillIn

        let! _, oldTry = signInAs app OperatorCredential
        Assert.Contains("signin-incorrect", location oldTry)

        let! fresh, newTry = signInAs app replacement
        Assert.DoesNotContain("signin-incorrect", location newTry)
        let! freshIn = stillSignedIn fresh
        Assert.True freshIn

        let! recorded = events app "operator_credential_changed"
        Assert.Contains("operator:success:replaced_signed_in", recorded)
    }

[<Fact>]
let ``every OTHER admin session is signed out and the current one stays`` () =
    task {
        use! app = Fixtures.createApp ()
        let laptop = Browser(app.Client)
        do! claimOperator app laptop

        // A second, older sign-in: the one someone who held the credential opened.
        let! other, _ = signInAs app OperatorCredential
        let! otherBefore = stillSignedIn other
        Assert.True otherBefore

        let! _ = change laptop OperatorCredential replacement replacement

        let! otherAfter = stillSignedIn other
        Assert.False(otherAfter, "a session opened before the change must not survive it")

        let! laptopAfter = stillSignedIn laptop
        Assert.True(laptopAfter, "the session that made the change must survive it")
    }

[<Fact>]
let ``an offline reset ends every session too`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator

        // What `reset-credential` calls. The reason to run it is that the
        // credential is lost or exposed, so a session must not outlive it.
        let! _ = AdminAuth.replaceCredential app.DbConfig app.Clock replacement
        let! stillIn = stillSignedIn operator
        Assert.False stillIn
    }

[<Fact>]
let ``a wrong current credential changes nothing and is recorded`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator

        let! response = change operator "not-the-credential" replacement replacement
        Assert.Contains("credential-current-incorrect", location response)

        let! unchanged = AdminAuth.verify app.DbConfig OperatorCredential
        Assert.True unchanged

        let! recorded = events app "operator_sign_in_failed"
        Assert.Contains("operator:failure:credential_change_mismatch", recorded)
    }

[<Fact>]
let ``two different new values, or a short one, change nothing`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator

        let! mismatch = change operator OperatorCredential replacement "something-else-entirely"
        Assert.Contains("setup-mismatch", location mismatch)

        let! tooShort = change operator OperatorCredential "short" "short"
        Assert.Contains("setup-too-short", location tooShort)

        let! unchanged = AdminAuth.verify app.DbConfig OperatorCredential
        Assert.True unchanged
    }

[<Fact>]
let ``a post without an antiforgery token is refused and changes nothing`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator

        let! response =
            operator.PostWithoutToken(
                "/admin/credential",
                [ "current", OperatorCredential
                  "credential", replacement
                  "confirm", replacement ]
            )

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        let! unchanged = AdminAuth.verify app.DbConfig OperatorCredential
        Assert.True unchanged
    }

[<Fact>]
let ``guessing the current credential here is rate limited like sign-in`` () =
    task {
        use! app = Fixtures.createApp ()
        do! AdminAuth.setInitialCredential app.DbConfig app.Clock OperatorCredential :> Task

        let send () =
            task {
                use request = new HttpRequestMessage(HttpMethod.Post, "/admin/credential")
                request.Headers.Add("X-Forwarded-For", "203.0.113.50")
                request.Content <- new FormUrlEncodedContent(dict [ "current", "wrong" ])
                return! app.Client.SendAsync request
            }

        // RateLimits.AdminAuth allows 10 per window (see RateLimitTests).
        for _ in 1..10 do
            let! r = send ()
            Assert.NotEqual(HttpStatusCode.TooManyRequests, r.StatusCode)

        let! over = send ()
        Assert.Equal(HttpStatusCode.TooManyRequests, over.StatusCode)
    }

// The screen itself (spec 011, design/contracts/operator-passkeys-ui.md).

let private passkey (seed: int) (lastUsed: System.DateTimeOffset option) : OperatorPasskeys.OperatorPasskey =
    { Id = OperatorPasskeys.newId ()
      CredentialId = [| byte seed; 7uy; 7uy; 7uy |]
      PublicKey = [| 1uy; byte seed |]
      SignCount = 0u
      Name = $"Key number {seed}"
      Transports = [ "usb" ]
      CreatedUtc = System.DateTimeOffset(2026, 10, 26, 8, 0, 0, System.TimeSpan.Zero)
      LastUsedUtc = lastUsed }

let private addPasskey (app: Fixtures.TestApp) (key: OperatorPasskeys.OperatorPasskey) =
    task {
        let! outcome = OperatorPasskeys.add app.DbConfig app.Clock key
        Assert.Equal(OperatorPasskeys.Added, outcome)
    }

[<Fact>]
let ``with no passkey the screen says so, offers the add form and blocks the off switch`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator

        let! html = operator.GetHtml "/admin/credential"
        Assert.Contains("data-section=\"passkeys\"", html)
        Assert.Contains("data-passkeys-empty", html)
        Assert.Contains("No passkey yet.", html)
        Assert.Contains("data-operator-passkey-add", html)
        Assert.Contains("data-section=\"password-signin\"", html)
        Assert.Contains("data-section=\"password\"", html)
        Assert.Contains("Turn password sign-in off", html)
        Assert.Contains("disabled", html)
        Assert.Contains("data-add-passkey-first", html)
        Assert.Contains("Add a passkey first.", html)
        // With none yet there is nothing to use instead.
        Assert.DoesNotContain("data-operator-confirm-passkey", html)
        Assert.Contains("/assets/passkeys.js", html)
    }

[<Fact>]
let ``passkeys are listed by name with their dates and never by credential id`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator

        // The store stamps both dates from the clock, whatever the record says.
        let today = app.Clock.GetUtcNow().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
        let first = passkey 1 None
        let second = passkey 2 None
        do! addPasskey app first
        do! addPasskey app second
        let! _ = OperatorPasskeys.recordUse app.DbConfig app.Clock second.Id 1u
        ()

        let! html = operator.GetHtml "/admin/credential"
        Assert.Contains("Key number 1", html)
        Assert.Contains("Key number 2", html)
        // The separator between the two is encoded by the view engine, so the
        // parts are checked on their own.
        Assert.Contains($"Added {today}", html)
        Assert.Contains("Never used", html)
        Assert.Contains($"Last used {today}", html)
        Assert.Contains($"data-passkey-row=\"{first.Id}\"", html)
        Assert.Contains($"/admin/credential/passkeys/{first.Id}/remove", html)
        Assert.DoesNotContain("data-passkeys-empty", html)
        Assert.DoesNotContain("Add a passkey first.", html)
        // The re-check field and the passkey alternative sit on the add form.
        Assert.Contains("data-operator-confirm-passkey", html)

        for key in [ first; second ] do
            let credentialId = System.Convert.ToBase64String key.CredentialId
            let urlSafe = credentialId.TrimEnd('=').Replace('+', '-').Replace('/', '_')
            Assert.DoesNotContain(credentialId, html)
            Assert.DoesNotContain(urlSafe, html)
    }

[<Fact>]
let ``with password sign-in off there is no password field and no Password card`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! addPasskey app (passkey 1 None)

        let! off = OperatorPasskeys.turnPasswordSignInOff app.DbConfig app.Clock
        Assert.Equal(OperatorPasskeys.SwitchedOff, off)

        let! html = operator.GetHtml "/admin/credential"
        Assert.DoesNotContain("type=\"password\"", html)
        Assert.DoesNotContain("data-section=\"password\"", html)
        Assert.Contains("data-section=\"passkeys\"", html)
        Assert.Contains("Turn password sign-in on", html)
        Assert.Contains("data-password-signin-state=\"off\"", html)
        // The only way to re-check is the ceremony, so no "instead" button.
        Assert.DoesNotContain("data-operator-confirm-passkey", html)
    }

[<Fact>]
let ``a refusal notice renders as a warning and a success as a success`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator

        let! refused = operator.GetHtml "/admin/credential?msg=operator-passkey-last"
        Assert.Contains("data-notice=\"operator-passkey-last\"", refused)
        Assert.Contains("um-alert-warning", refused)
        Assert.DoesNotContain("um-alert-success", refused)

        let! added = operator.GetHtml "/admin/credential?msg=operator-passkey-added"
        Assert.Contains("um-alert-success", added)
        Assert.Contains("Passkey added.", added)

        let! unknown = operator.GetHtml "/admin/credential?msg=not-a-code"
        Assert.DoesNotContain("data-notice", unknown)
    }

[<Fact>]
let ``at ten passkeys the add form gives way to the limit line`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator

        for seed in 1 .. OperatorPasskeys.MaxPasskeys do
            do! addPasskey app (passkey seed None)

        let! html = operator.GetHtml "/admin/credential"
        Assert.Contains("data-passkeys-limit", html)
        Assert.DoesNotContain("data-operator-passkey-add", html)
        Assert.Contains("Remove one to add another.", html)
    }

[<Fact>]
let ``the replace form still posts from the Password card while password sign-in is on`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator

        let! html = operator.GetHtml "/admin/credential"
        Assert.Contains("action=\"/admin/credential\"", html)
        Assert.Contains("Replace the password", html)
        Assert.Contains("data-password-signin-state=\"on\"", html)
    }
