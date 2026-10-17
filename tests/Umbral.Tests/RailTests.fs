/// The operator's side rail and the Create app dialog (specs/007-umbra-look,
/// User Story 3, research R7 and R8): the four screens and sign-out in an
/// `<aside>` with `aria-current` on the right one, nothing of the kind on a page
/// anyone else can see, and "Create app" as a link that is also a page.
module Umbral.Tests.RailTests

open System.Net
open System.Net.Http
open System.Text.RegularExpressions
open Xunit
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

/// The inner HTML of the first `<tag …>…</tag>`, or `None`. The tags used here
/// (`aside`, `nav`, `dialog`) are never nested in themselves.
let private inner (tag: string) (html: string) : string option =
    let m = Regex.Match(html, $"<{tag}[\\s>].*?</{tag}>", RegexOptions.Singleline)
    if m.Success then Some m.Value else None

let private hrefs (fragment: string) =
    [ for m in Regex.Matches(fragment, "<a\\b[^>]*\\shref=\"([^\"]*)\"[^>]*>") -> m.Groups[1].Value ]

/// The `href` of every link carrying `aria-current="page"`.
let private currentLinks (fragment: string) =
    [ for m in Regex.Matches(fragment, "<a\\b[^>]*>") do
          if m.Value.Contains "aria-current=\"page\"" then
              yield Regex.Match(m.Value, "\\shref=\"([^\"]*)\"").Groups[1].Value ]

let private operatorScreens =
    [ "/admin/apps"; "/admin/connectors"; "/admin/users"; "/admin/credential" ]

/// A claimed instance with the fake connector (so the create form is offered)
/// and a browser signed in as its operator.
let private signedIn () =
    task {
        let! app = Fixtures.createAppWith fakeSettings ignore
        let browser = Browser app.Client
        do! claimOperator app browser
        do! seedFakeConnector app
        return app, browser
    }

[<Fact>]
let ``each operator screen has the rail with the four screens and sign-out, and marks its own`` () =
    task {
        let! app, browser = signedIn ()
        use _ = app

        for screen in operatorScreens do
            let! html = browser.GetHtml screen

            let aside = inner "aside" html
            Assert.True(aside.IsSome, $"{screen}: no <aside>")

            let nav = inner "nav" aside.Value
            Assert.True(nav.IsSome, $"{screen}: no <nav> in the aside")
            Assert.Matches("<nav[^>]*aria-label=\"[^\"]+\"", nav.Value)

            Assert.Equal<string list>(operatorScreens, hrefs nav.Value)
            Assert.Contains("action=\"/admin/signout\"", nav.Value)
            Assert.Equal<string list>([ screen ], currentLinks nav.Value)

            // The rail replaces the top bar on these pages.
            Assert.DoesNotContain("<header", html)
    }

[<Fact>]
let ``pages that are not the operator's have no rail, no admin links and no sign-out`` () =
    task {
        // Before the operator exists: setup and a 404. (The landing page at `/`
        // names the admin area on purpose, so it is checked for the rail only.)
        use! fresh = Fixtures.createApp ()

        let! home = Browser(fresh.Client).GetHtml "/"
        Assert.DoesNotContain("<aside", home)
        Assert.DoesNotContain("/admin/signout", home)

        for url in [ "/admin/setup"; "/no-such-page" ] do
            let! html = Browser(fresh.Client).GetHtml url
            Assert.DoesNotContain("<aside", html)
            Assert.DoesNotContain("href=\"/admin", html)
            Assert.DoesNotContain("/admin/signout", html)

        // Once it exists: its sign-in page and the end-user picker.
        let! app, _ = signedIn ()
        use _ = app
        let stranger = Browser app.Client

        for url in [ "/admin/signin"; "/signin" ] do
            let! html = stranger.GetHtml url
            Assert.DoesNotContain("<aside", html)
            Assert.DoesNotContain("href=\"/admin", html)
            Assert.DoesNotContain("/admin/signout", html)
    }

[<Fact>]
let ``the Create app page is behind the operator sign-in like the other screens`` () =
    task {
        use! fresh = Fixtures.createApp ()
        let! unclaimed = fresh.Client.GetAsync "/admin/apps/new"
        let! unclaimedApps = fresh.Client.GetAsync "/admin/apps"
        Assert.Equal(HttpStatusCode.Redirect, unclaimed.StatusCode)
        Assert.Equal(location unclaimedApps, location unclaimed)

        let! app, _ = signedIn ()
        use _ = app
        let! signedOut = app.Client.GetAsync "/admin/apps/new"
        let! signedOutApps = app.Client.GetAsync "/admin/apps"
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode)
        Assert.Equal("/admin/signin", location signedOut)
        Assert.Equal(location signedOutApps, location signedOut)
    }

[<Fact>]
let ``the Create app page holds the create form and a way back`` () =
    task {
        let! app, browser = signedIn ()
        use _ = app
        let! html = browser.GetHtml "/admin/apps/new"

        Assert.Contains("data-screen=\"app-new\"", html)
        Assert.Matches("<form[^>]*action=\"/admin/apps\"", html)
        Assert.Contains("name=\"redirectUri\"", html)

        // The main content, not the rail, holds the Cancel link.
        let main = (inner "main" html).Value
        Assert.Contains("/admin/apps", hrefs main)

        let current = currentLinks (inner "nav" html).Value
        Assert.Equal<string list>([ "/admin/apps" ], current)
    }

[<Fact>]
let ``the Create app page says what to do when no connector is enabled`` () =
    task {
        use! app = Fixtures.createApp ()
        let browser = Browser app.Client
        do! claimOperator app browser
        let! html = browser.GetHtml "/admin/apps/new"

        Assert.Contains("data-screen=\"app-new\"", html)
        Assert.DoesNotContain("action=\"/admin/apps\"", html)
        Assert.Contains("/admin/connectors", hrefs (inner "main" html).Value)
    }

[<Fact>]
let ``the Apps screen opens Create app as a dialog and falls back to the page`` () =
    task {
        let! app, browser = signedIn ()
        use _ = app
        let! html = browser.GetHtml "/admin/apps"

        Assert.Matches("<a\\b[^>]*href=\"/admin/apps/new\"[^>]*data-dialog=\"create-app\"|<a\\b[^>]*data-dialog=\"create-app\"[^>]*href=\"/admin/apps/new\"", html)

        let dialog = inner "dialog" html
        Assert.True(dialog.IsSome, "no <dialog>")
        Assert.Matches("<dialog[^>]*\\sid=\"create-app\"", dialog.Value)
        Assert.Matches("<dialog[^>]*aria-labelledby=\"create-app-title\"", dialog.Value)
        Assert.Contains("id=\"create-app-title\"", dialog.Value)
        Assert.Matches("<form[^>]*action=\"/admin/apps\"", dialog.Value)
        Assert.Matches("<button[^>]*data-dialog-close", dialog.Value)

        // The form is in the dialog and nowhere else on the screen.
        Assert.Equal(1, Regex.Matches(html, "<form[^>]*action=\"/admin/apps\"").Count)
    }

[<Fact>]
let ``an app is created from the page as it is from the dialog`` () =
    task {
        let! app, browser = signedIn ()
        use _ = app

        let! created =
            browser.PostForm(
                "/admin/apps/new",
                "/admin/apps",
                [ "name", "From the page"
                  "redirectUri", RedirectUri
                  "postLogoutRedirectUri", ""
                  "appType", "web" ]
            )

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode)
        let! shown = browser.GetHtml(location created)
        Assert.Contains("From the page", shown)
    }
