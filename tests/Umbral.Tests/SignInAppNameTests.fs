/// User Story 1 of feature 003: the sign-in page names the app it serves.
///
/// Asserts on the `data-signin-app` and `data-connector` markers, never on the
/// wording of the title, so a copy change cannot break these.
module Umbral.Tests.SignInAppNameTests

open System
open System.Text.RegularExpressions
open Microsoft.Extensions.DependencyInjection
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

/// The app the page names, or None when it names no app.
let private signInApp (html: string) =
    let m = Regex.Match(html, "data-signin-app=\"([^\"]*)\"")
    if m.Success then Some m.Groups[1].Value else None

/// The slugs of the sign-in buttons, in page order.
let private connectorsOn (html: string) =
    [ for m in Regex.Matches(html, "data-connector=\"([^\"]*)\"") -> m.Groups[1].Value ]

let private authorizeUrl (clientId: string) = Journeys.authorizeUrl clientId "openid"

/// The page an app's authorize request sends someone to.
let private pickerFor (clientId: string) = "/signin?returnUrl=" + Uri.EscapeDataString(authorizeUrl clientId)

/// Gives an instance the fake connector and two registered apps, A and B.
let private twoApps (app: Fixtures.TestApp) =
    task {
        let browser = Browser app.Client
        do! claimOperator app browser
        do! seedFakeConnector app
        let! idA, _ = registerApp browser "App A"
        let! idB, _ = registerApp browser "App B"
        return idA, idB
    }

[<Fact>]
let ``a sign-in started from an app names that app`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! idA, idB = twoApps app
        let visitor = Browser app.Client

        let! pageA = visitor.GetHtml(pickerFor idA)
        let! pageB = visitor.GetHtml(pickerFor idB)

        Assert.Equal(Some idA, signInApp pageA)
        Assert.Equal(Some idB, signInApp pageB)
        // Both apps use every shared connector by default.
        Assert.Equal<string list>([ "fake" ], connectorsOn pageA)
        Assert.Equal<string list>([ "fake" ], connectorsOn pageB)
    }

[<Fact>]
let ``the sign-in page alone names no app and offers every shared connector`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _ = twoApps app
        let! html = (Browser app.Client).GetHtml "/signin"

        Assert.Equal(None, signInApp html)
        Assert.Equal<string list>([ "fake" ], connectorsOn html)
    }

[<Fact>]
let ``an unknown client_id names no app`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _ = twoApps app
        let! html = (Browser app.Client).GetHtml(pickerFor "no-such-app")

        Assert.Equal(None, signInApp html)
        Assert.Equal<string list>([ "fake" ], connectorsOn html)
    }

[<Fact>]
let ``a returnUrl that is not the authorize endpoint names no app`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! idA, _ = twoApps app
        let visitor = Browser app.Client

        // Same query, a different path: only /connect/authorize says which app
        // the person will be sent back to.
        let other = "/admin/apps?client_id=" + Uri.EscapeDataString idA
        let! html = visitor.GetHtml("/signin?returnUrl=" + Uri.EscapeDataString other)
        Assert.Equal(None, signInApp html)

        // And an authorize request with no client_id at all.
        let! bare = visitor.GetHtml("/signin?returnUrl=" + Uri.EscapeDataString "/connect/authorize?scope=openid")
        Assert.Equal(None, signInApp bare)
    }

[<Fact>]
let ``a client_id on the sign-in page itself is ignored`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! idA, idB = twoApps app
        let visitor = Browser app.Client

        // A crafted link: the page's own query says A, the request it returns to
        // says B. The page must follow the request it will actually return to.
        let! crafted = visitor.GetHtml("/signin?client_id=" + Uri.EscapeDataString idA + "&returnUrl=" + Uri.EscapeDataString(authorizeUrl idB))
        Assert.Equal(Some idB, signInApp crafted)

        let! alone = visitor.GetHtml("/signin?client_id=" + Uri.EscapeDataString idA)
        Assert.Equal(None, signInApp alone)
    }

[<Fact>]
let ``an app offering nothing says so and keeps its name`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! idA, _ = twoApps app

        // The only connector stops being shared, so AllShared offers nothing.
        let! stored = ConnectorStore.findBySlug app.DbConfig "fake"
        do! ConnectorStore.save app.DbConfig app.Clock { stored.Value with IsShared = false } None

        let visitor = Browser app.Client
        let! forApp = visitor.GetHtml(pickerFor idA)
        Assert.Equal(Some idA, signInApp forApp)
        Assert.Contains("data-no-connectors", forApp)
        Assert.Empty(connectorsOn forApp)

        // With no app named, the same instance offers nothing shared either.
        let! general = visitor.GetHtml "/signin"
        Assert.Equal(None, signInApp general)
        Assert.Contains("data-no-connectors", general)
    }

[<Fact>]
let ``after a failed callback the page still names the app`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! idA, _ = twoApps app
        let visitor = Browser app.Client

        let! toPicker = visitor.Get(authorizeUrl idA)
        let picker = location toPicker
        let! _ = visitor.Get(picker.Replace("/signin", "/connect/fake/start"))

        // The person pressed cancel at the provider.
        let! declined = visitor.Get "/connect/fake/callback?error=access_denied"
        let back = location declined
        Assert.Contains("msg=access-denied", back)

        let! html = visitor.GetHtml back
        Assert.Equal(Some idA, signInApp html)
        Assert.Equal<string list>([ "fake" ], connectorsOn html)
    }
