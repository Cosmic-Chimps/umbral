/// FR-038: repeated attempts from one source are refused (T094).
///
/// No test waits for a window to pass. The limiter's own clock is not the
/// fake `TimeProvider` and driving it would need a seam in production code for
/// the sake of a test; what matters here is that the limit ENGAGES, for the
/// right routes, per source, and says what to do. The numbers below are
/// RateLimits.fs's, and a change to them should be a deliberate edit to both.
module Umbral.Tests.RateLimitTests

open System.Net
open System.Net.Http
open System.Threading.Tasks
open Xunit
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure

let private adminLimit = 10
let private signInLimit = 30

/// A request that claims to come from `source`. UseForwardedHeaders runs
/// first and trusts the header (see Program.configureServices), which is how a
/// TestServer, with no connection address of its own, gets distinct callers.
let private send (app: Fixtures.TestApp) (source: string) (verb: string) (url: string) =
    task {
        use request = new HttpRequestMessage(HttpMethod.Parse verb, url)
        request.Headers.Add("X-Forwarded-For", source)

        if verb = "POST" then
            request.Content <- new FormUrlEncodedContent(dict [ "credential", "wrong" ])

        return! app.Client.SendAsync request
    }

/// Sends `count` requests and returns every status.
let private burst app source verb url count =
    task {
        let statuses = ResizeArray<HttpStatusCode>()

        for _ in 1..count do
            let! response = send app source verb url
            statuses.Add response.StatusCode

        return List.ofSeq statuses
    }

[<Fact>]
let ``admin sign-in engages after the limit, for that source only`` () =
    task {
        use! app = Fixtures.createApp ()
        do! AdminAuth.setInitialCredential app.DbConfig app.Clock "correct-horse-battery-staple" :> Task

        let! first = burst app "203.0.113.1" "POST" "/admin/signin" adminLimit
        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, first)

        let! over = send app "203.0.113.1" "POST" "/admin/signin"
        Assert.Equal(HttpStatusCode.TooManyRequests, over.StatusCode)

        // Another address is a different partition and is untouched.
        let! other = send app "203.0.113.2" "POST" "/admin/signin"
        Assert.NotEqual(HttpStatusCode.TooManyRequests, other.StatusCode)

        // Only the POST is limited: the page itself still loads for the
        // blocked address, so the person is not shown a blank error.
        let! page = send app "203.0.113.1" "GET" "/admin/signin"
        Assert.Equal(HttpStatusCode.OK, page.StatusCode)
    }

[<Fact>]
let ``a rejection says how long to wait and sets Retry-After`` () =
    task {
        use! app = Fixtures.createApp ()
        let! _ = burst app "203.0.113.3" "POST" "/admin/signin" adminLimit
        let! over = send app "203.0.113.3" "POST" "/admin/signin"

        Assert.Equal(HttpStatusCode.TooManyRequests, over.StatusCode)

        let retryAfter = Assert.IsType<System.Net.Http.Headers.RetryConditionHeaderValue>(over.Headers.RetryAfter)
        Assert.InRange(retryAfter.Delta.Value.TotalSeconds, 1.0, 600.0)

        // A page that names the next action, not a bare status line.
        let! html = over.Content.ReadAsStringAsync()
        Assert.Contains("Too many attempts", html)
        Assert.Contains("minutes", html)
        Assert.Contains("try again", html)
    }

[<Fact>]
let ``first-run setup is limited too, because the setup token is the only guard`` () =
    task {
        use! app = Fixtures.createApp ()
        let! first = burst app "203.0.113.4" "POST" "/admin/setup" adminLimit
        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, first)

        let! over = send app "203.0.113.4" "POST" "/admin/setup"
        Assert.Equal(HttpStatusCode.TooManyRequests, over.StatusCode)
    }

[<Fact>]
let ``sign-in and setup share one budget per source`` () =
    task {
        use! app = Fixtures.createApp ()
        // Someone alternating between the two forms must not get twice the guesses.
        let! _ = burst app "203.0.113.5" "POST" "/admin/signin" (adminLimit / 2)
        let! _ = burst app "203.0.113.5" "POST" "/admin/setup" (adminLimit / 2)
        let! over = send app "203.0.113.5" "POST" "/admin/signin"
        Assert.Equal(HttpStatusCode.TooManyRequests, over.StatusCode)
    }

[<Fact>]
let ``an end user's sign-in start and callback are limited together`` () =
    task {
        use! app = Fixtures.createApp ()

        // An unknown slug: the limiter runs before the handler decides it is a 404.
        let! _ = burst app "203.0.113.6" "GET" "/connect/nope/start" (signInLimit / 2)
        let! _ = burst app "203.0.113.6" "GET" "/connect/nope/callback" (signInLimit / 2)

        let! overStart = send app "203.0.113.6" "GET" "/connect/nope/start"
        Assert.Equal(HttpStatusCode.TooManyRequests, overStart.StatusCode)

        let! overCallback = send app "203.0.113.6" "GET" "/connect/nope/callback"
        Assert.Equal(HttpStatusCode.TooManyRequests, overCallback.StatusCode)

        let! other = send app "203.0.113.7" "GET" "/connect/nope/start"
        Assert.NotEqual(HttpStatusCode.TooManyRequests, other.StatusCode)
    }

[<Fact>]
let ``the OIDC protocol endpoints are never limited`` () =
    task {
        use! app = Fixtures.createApp ()

        // Far past either limit. Every login of every user of a client app
        // arrives from that app's one address, so limiting these would fail
        // logins in the healthy case.
        for url in [ "/connect/authorize"; "/.well-known/openid-configuration"; "/.well-known/jwks" ] do
            let! statuses = burst app "203.0.113.8" "GET" url (signInLimit + 5)
            Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses)
    }

[<Fact>]
let ``a normal operator sign-in is not affected`` () =
    task {
        use! app = Fixtures.createApp ()
        do! AdminAuth.setInitialCredential app.DbConfig app.Clock "correct-horse-battery-staple" :> Task

        // The real flow, with a real antiforgery token: one failure, one success.
        let! page = send app "203.0.113.9" "GET" "/admin/signin"
        let! html = page.Content.ReadAsStringAsync()
        let cookies = page.Headers.GetValues "Set-Cookie" |> Seq.map (fun c -> c.Split(';')[0]) |> String.concat "; "

        let m =
            System.Text.RegularExpressions.Regex.Match(
                html,
                "<input[^>]*type=\"hidden\"[^>]*name=\"([^\"]+)\"[^>]*value=\"([^\"]+)\""
            )

        use request = new HttpRequestMessage(HttpMethod.Post, "/admin/signin")
        request.Headers.Add("X-Forwarded-For", "203.0.113.9")
        request.Headers.Add("Cookie", cookies)

        request.Content <-
            new FormUrlEncodedContent(
                dict [ m.Groups[1].Value, m.Groups[2].Value; "credential", "correct-horse-battery-staple" ]
            )

        let! response = app.Client.SendAsync request
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Equal("/admin/connectors", response.Headers.Location |> string)
    }
