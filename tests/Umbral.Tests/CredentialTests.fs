/// FR-017a: the operator credential is replaceable by an operator who is
/// already signed in, and replacing it ends every other admin session.
module Umbral.Tests.CredentialTests

open System.Diagnostics
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

let private fileCredential = "the-credential-from-the-file"

/// The real subcommand, as a process: it must work with no data directory,
/// origin or file, because it runs wherever the file is being written.
let private runHashCredential (input: string) =
    task {
        let dll = typeof<AdminAuth.SetupToken>.Assembly.Location
        let info = ProcessStartInfo("dotnet", $"\"{dll}\" hash-credential")
        info.RedirectStandardInput <- true
        info.RedirectStandardOutput <- true
        info.RedirectStandardError <- true

        match Process.Start info with
        | null -> return failwith "could not start the process"
        | proc ->
            use proc = proc
            do! proc.StandardInput.WriteLineAsync input
            proc.StandardInput.Close()
            let! output = proc.StandardOutput.ReadToEndAsync()
            let! errors = proc.StandardError.ReadToEndAsync()
            do! proc.WaitForExitAsync()
            return proc.ExitCode, output, errors
    }

[<Fact>]
let ``hash-credential prints a hash that verifies, from standard input, with nothing configured`` () =
    task {
        let! exitCode, output, errors = runHashCredential fileCredential

        Assert.Equal(0, exitCode)
        Assert.True(AdminAuth.verifyCredential (output.Trim()) fileCredential)

        // The credential itself appears nowhere in either stream.
        Assert.DoesNotContain(fileCredential, output)
        Assert.DoesNotContain(fileCredential, errors)
    }

[<Fact>]
let ``hash-credential refuses a credential that is too short`` () =
    task {
        let! exitCode, output, _ = runHashCredential "short"
        Assert.Equal(1, exitCode)
        Assert.Equal("", output.Trim())
    }
