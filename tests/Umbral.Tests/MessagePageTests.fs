/// Who a message page is for decides where it sends them (task: message page's
/// back link always pointed at /admin).
module Umbral.Tests.MessagePageTests

open System.Net
open System.Net.Http
open Xunit
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

/// Sends `attempts` requests from `source` and returns the last response, the
/// one the limiter refused.
let private refusal (app: Fixtures.TestApp) (source: string) (verb: HttpMethod) (url: string) (attempts: int) =
    task {
        let mutable last = new HttpResponseMessage()

        for _ in 1..attempts do
            use request = new HttpRequestMessage(verb, url)
            request.Headers.Add("X-Forwarded-For", source)

            if verb = HttpMethod.Post then
                request.Content <- new FormUrlEncodedContent(dict [ "credential", "wrong" ])

            let! response = app.Client.SendAsync request
            last <- response

        return last
    }

[<Fact>]
let ``an end user's rate-limit page does not link to the operator sign-in`` () =
    task {
        use! app = Fixtures.createApp ()

        // 31 requests: the 31st is over the 30 a minute that RateLimits.fs allows.
        let! response = refusal app "203.0.113.20" HttpMethod.Get "/connect/nope/start" 31
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode)

        let! html = response.Content.ReadAsStringAsync()
        Assert.DoesNotContain("href=\"/admin\"", html)
        Assert.Contains("data-back-to-app", html)
        Assert.Contains("Go back to the app", html)
    }

[<Fact>]
let ``an operator's rate-limit page links back to /admin`` () =
    task {
        use! app = Fixtures.createApp ()

        let! response = refusal app "203.0.113.21" HttpMethod.Post "/admin/signin" 11
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode)

        let! html = response.Content.ReadAsStringAsync()
        Assert.Contains("href=\"/admin\"", html)
        Assert.DoesNotContain("data-back-to-app", html)
    }

[<Fact>]
let ``an end user's protocol-error page does not link to the operator sign-in`` () =
    task {
        use! app = Fixtures.createApp ()
        // No client_id at all: OpenIddict rejects it and passes through to /connect/error.
        let! response = app.Client.GetAsync "/connect/authorize?response_type=code"
        let! html = response.Content.ReadAsStringAsync()

        Assert.Contains("data-protocol-error", html)
        Assert.DoesNotContain("href=\"/admin\"", html)
    }

[<Fact>]
let ``the end-user picker shows no operator navigation`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/signin"
        let! html = response.Content.ReadAsStringAsync()

        Assert.Contains("data-screen=\"signin\"", html)
        Assert.DoesNotContain("/admin/", html)
        Assert.DoesNotContain("<form", html)
    }

[<Fact>]
let ``setup and operator sign-in show the brand without the operator navigation`` () =
    task {
        use! app = Fixtures.createApp ()
        let! setup = app.Client.GetAsync "/admin/setup"
        let! setupHtml = setup.Content.ReadAsStringAsync()
        Assert.DoesNotContain("href=\"/admin/apps\"", setupHtml)
        Assert.DoesNotContain("href=\"/admin/connectors\"", setupHtml)
        Assert.DoesNotContain("/admin/signout", setupHtml)

        let browser = Browser app.Client
        do! claimOperator app browser

        let! signIn = app.Client.GetAsync "/admin/signin"
        let! signInHtml = signIn.Content.ReadAsStringAsync()
        Assert.Contains("data-screen=\"operator-signin\"", signInHtml)
        Assert.DoesNotContain("href=\"/admin/apps\"", signInHtml)
        Assert.DoesNotContain("/admin/signout", signInHtml)
    }

[<Fact>]
let ``an end user's message page shows no operator navigation`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/connect/authorize?response_type=code"
        let! html = response.Content.ReadAsStringAsync()

        Assert.DoesNotContain("href=\"/admin/apps\"", html)
        Assert.DoesNotContain("/admin/signout", html)
    }

[<Fact>]
let ``a signed-in operator's screens keep the operator navigation`` () =
    task {
        use! app = Fixtures.createApp ()
        let browser = Browser app.Client
        do! claimOperator app browser

        let! response = browser.Get "/admin/connectors"
        let! html = response.Content.ReadAsStringAsync()

        Assert.Contains("href=\"/admin/apps\"", html)
        Assert.Contains("action=\"/admin/signout\"", html)
    }
