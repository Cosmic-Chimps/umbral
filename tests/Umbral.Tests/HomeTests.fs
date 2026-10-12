/// `GET /`, the public home page, and the 404 that every other unknown path keeps.
module Umbral.Tests.HomeTests

open System.Net
open Xunit
open Umbral.Tests.Infrastructure

[<Fact>]
let ``/ is a public home page with the way to the admin area`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/"
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)

        let! html = response.Content.ReadAsStringAsync()
        Assert.Contains("<title>Welcome | Umbral</title>", html)
        Assert.Contains("Signing in?", html)
        Assert.Contains("Building an app?", html)
        Assert.Contains("Running this instance?", html)
        Assert.Contains("href=\"/admin\"", html)
    }

[<Fact>]
let ``/ shows the issuer and links to the discovery document`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/"
        let! html = response.Content.ReadAsStringAsync()
        Assert.Contains(Fixtures.TestOrigin, html)
        Assert.Contains("/.well-known/openid-configuration", html)
    }

[<Fact>]
let ``/ carries no operator navigation`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/"
        let! html = response.Content.ReadAsStringAsync()
        Assert.DoesNotContain("/admin/signout", html)
        Assert.DoesNotContain("href=\"/admin/apps\"", html)
    }

[<Fact>]
let ``another unknown path is still the themed 404`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/nope"
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)

        // The themed page, not a bare "Not found" (Texts.NotFoundTitle).
        let! html = response.Content.ReadAsStringAsync()
        Assert.Contains("Page not found", html)
    }
