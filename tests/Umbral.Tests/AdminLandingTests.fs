/// `GET /admin`, the address every operator-facing message page links back to.
module Umbral.Tests.AdminLandingTests

open System.Net
open Xunit
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

[<Fact>]
let ``/admin on an unclaimed instance goes to first-run setup`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/admin"
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Equal("/admin/setup", location response)
    }

[<Fact>]
let ``/admin without a session goes to sign-in`` () =
    task {
        use! app = Fixtures.createApp ()
        let setup = Browser app.Client
        do! claimOperator app setup

        // A different browser: claiming signed the first one in.
        let! response = app.Client.GetAsync "/admin"
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Equal("/admin/signin", location response)
    }

[<Fact>]
let ``/admin for a signed-in operator goes where sign-in lands`` () =
    task {
        use! app = Fixtures.createApp ()
        let browser = Browser app.Client
        do! claimOperator app browser

        let! response = browser.Get "/admin"
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Equal("/admin/connectors", location response)
    }

[<Fact>]
let ``/ stays a 404`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/"
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)

        // The themed page, not a bare "Not found" (Texts.NotFoundTitle).
        let! html = response.Content.ReadAsStringAsync()
        Assert.Contains("Page not found", html)
    }
