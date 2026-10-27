/// Every page says where Umbral's source and documentation live (task rp0l):
/// the footer's two links, on the home page, the sign-in page and the operator's
/// pages, but never on an app's branded sign-in page, where "Secured by Umbral"
/// stays alone. And the home page shows its issuer URL whole, as text.
module Umbral.Tests.FooterTests

open System
open System.Text.RegularExpressions
open Xunit
open Umbral.Domains.Branding
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows
open Umbral.Views

let private get (browser: Browser) (url: string) =
    task {
        let! response = browser.Get url
        return! response.Content.ReadAsStringAsync()
    }

/// The footer's links landmark, or "" when the page has none.
let private footerLinks (html: string) =
    let m = Regex.Match(html, "<nav[^>]*data-footer-links[^>]*>.*?</nav>", RegexOptions.Singleline)
    if m.Success then m.Value else ""

/// Both links, as plain anchors to the constants and nothing else: no target,
/// no address built from the request.
let private assertFooterLinks (html: string) =
    let nav = footerLinks html
    Assert.Contains("aria-label=\"About Umbral\"", nav)
    Assert.Contains($"<a href=\"{Texts.RepositoryUrl}\" class=\"um-footer-link\">Source on GitHub</a>", nav)
    Assert.Contains($"<a href=\"{Texts.DocumentationUrl}\" class=\"um-footer-link\">Documentation</a>", nav)
    Assert.Equal(2, Regex.Matches(nav, "<a ").Count)
    Assert.DoesNotContain("target=", nav)

let private pickerFor (clientId: string) =
    "/signin?returnUrl=" + Uri.EscapeDataString(Journeys.authorizeUrl clientId "openid")

[<Fact>]
let ``the repository and documentation addresses are the project's`` () =
    Assert.Equal("https://github.com/Cosmic-Chimps/umbral", Texts.RepositoryUrl)
    Assert.Equal("https://github.com/Cosmic-Chimps/umbral#readme", Texts.DocumentationUrl)

[<Fact>]
let ``the home page's footer links to the source and the documentation`` () =
    task {
        use! app = Fixtures.createApp ()
        let! html = get (Browser app.Client) "/"
        assertFooterLinks html
    }

[<Fact>]
let ``the sign-in page's footer links to the source and the documentation`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let browser = Browser app.Client
        do! claimOperator app browser
        do! seedFakeConnector app
        let! clientId, _ = registerApp browser "App A"

        let! html = get (Browser app.Client) (pickerFor clientId)
        Assert.Contains("data-connector", html)
        assertFooterLinks html
    }

[<Fact>]
let ``the operator's sign-in page's footer links to the source and the documentation`` () =
    task {
        use! app = Fixtures.createApp ()
        let browser = Browser app.Client
        do! claimOperator app browser
        let! html = get (Browser app.Client) "/admin/signin"
        assertFooterLinks html
    }

[<Fact>]
let ``an admin page's footer links to the source and the documentation`` () =
    task {
        use! app = Fixtures.createApp ()
        let browser = Browser app.Client
        do! claimOperator app browser
        let! html = get browser "/admin/apps"
        Assert.Contains("/admin/signout", html)
        assertFooterLinks html
    }

[<Fact>]
let ``an app's branded sign-in page keeps "Secured by Umbral" alone`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let browser = Browser app.Client
        do! claimOperator app browser
        do! seedFakeConnector app
        let! clientId, _ = registerApp browser "App A"

        let accent =
            match Accent.parse "#f26b1d" with
            | Ok a -> a
            | Error e -> failwith $"{e}"

        do! BrandingStore.setAccent app.DbConfig (app.Clock.GetUtcNow()) clientId (Some accent)

        let! html = get (Browser app.Client) (pickerFor clientId)
        Assert.Contains("data-secured-by", html)
        Assert.Equal("", footerLinks html)
        Assert.DoesNotContain(Texts.RepositoryUrl, html)
    }

[<Fact>]
let ``the home page shows the whole issuer URL as text, with Copy carrying it`` () =
    task {
        use! app = Fixtures.createApp ()
        let! html = get (Browser app.Client) "/"

        let shown = Regex.Match(html, "<p id=\"home-issuer\"[^>]*>([^<]*)</p>")
        Assert.True(shown.Success, "the issuer URL is a paragraph of text, not a field that cuts it")
        Assert.Equal(Fixtures.TestOrigin, shown.Groups[1].Value)

        Assert.Contains($"data-copy-value=\"{Fixtures.TestOrigin}\"", html)
        Assert.Contains("aria-label=\"Copy the issuer URL\"", html)
    }
