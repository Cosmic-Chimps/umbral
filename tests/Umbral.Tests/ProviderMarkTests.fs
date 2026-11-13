/// The mark each connector's button leads with on the sign-in page (specs/014,
/// task b6d7). The kind decides, never the display name: GitHub wears its own
/// mark, a passkey wears Umbral's Key icon, and Google and Discord keep the link
/// icon. These tests read the page's markup and the generated stylesheet, never
/// a colour.
module Umbral.Tests.ProviderMarkTests

open System
open System.Net
open System.Net.Http
open System.Text.RegularExpressions
open Xunit
open Umbral.Domains.Branding
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows
open Umbral.Views

let private decode (html: string) =
    WebUtility.HtmlDecode html |> Option.ofObj |> Option.defaultValue html

let private pickerFor (clientId: string) =
    "/signin?returnUrl=" + Uri.EscapeDataString(Journeys.authorizeUrl clientId "openid")

/// The whole `<a>` of one connector's button.
let private buttonOf (html: string) (slug: string) =
    Regex.Match(html, "<a [^>]*data-connector=\"" + slug + "\".*?</a>", RegexOptions.Singleline).Value

/// The slugs of the page's connector buttons, in page order.
let private order (html: string) =
    [ for m in Regex.Matches(html, "<a [^>]*data-connector=\"([^\"]*)\"") -> m.Groups[1].Value ]

let private classesOf (button: string) =
    Regex.Match(button, "class=\"([^\"]*)\"").Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
    |> List.ofArray

/// A connector row without a secret: the picker only reads its kind and name.
let private seed (app: Fixtures.TestApp) (slug: string) (kind: ProviderKind) (displayName: string) =
    ConnectorStore.save
        app.DbConfig
        app.Clock
        { Slug = slug
          Kind = kind
          DisplayName = displayName
          ClientId = slug
          ClientSecretRef = ""
          ClientSecretHint = ""
          IssuerUrl = None
          AllowedEmailDomains = []
          PasskeyEnrolment = InvitationsOnly
          FediverseAccess = FediverseAccess.Any
          AppleKey = None
          MicrosoftAudience = None
          IsEnabled = true
          IsShared = true
          CreatedUtc = app.Clock.GetUtcNow()
          UpdatedUtc = app.Clock.GetUtcNow() }
        None

/// An instance with a GitHub, a second GitHub named "Company GitHub", a Google
/// connector named "GitHub", a Discord and a passkey connector, and one app; the
/// page of that app. `accent` is the app's colour, if any.
let private pageWith (accent: string option) (cookies: string list) =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seed app "github" GitHub "GitHub"
        do! seed app "company" GitHub "Company GitHub"
        do! seed app "google" Google "GitHub"
        do! seed app "discord" Discord "Discord"
        do! seed app "passkey" Passkey "Passkey"

        let! clientId, _ = registerApp operator "Grafana"

        match accent with
        | Some hex ->
            match Accent.parse hex with
            | Ok parsed -> do! BrandingStore.setAccent app.DbConfig (app.Clock.GetUtcNow()) clientId (Some parsed)
            | Error e -> failwith $"{hex}: {e}"
        | None -> ()

        use request = new HttpRequestMessage(HttpMethod.Get, pickerFor clientId)

        if not cookies.IsEmpty then
            request.Headers.Add("Cookie", String.concat "; " cookies)

        let! response = app.Client.SendAsync request
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        let! html = response.Content.ReadAsStringAsync()
        return decode html
    }

[<Fact>]
let ``the GitHub button leads with the GitHub mark and not the link icon`` () =
    task {
        let! html = pageWith None []
        let button = buttonOf html "github"
        Assert.Contains(Theme.GitHubMarkPath, button)
        Assert.Contains("um-mark-github", button)
        Assert.DoesNotContain(Theme.Link.Path, button)
    }

[<Fact>]
let ``the kind decides: a GitHub connector named something else keeps the mark, another kind named GitHub gets the link icon`` () =
    task {
        let! html = pageWith None []

        let renamed = buttonOf html "company"
        Assert.Contains("Company GitHub", renamed)
        Assert.Contains(Theme.GitHubMarkPath, renamed)
        Assert.DoesNotContain(Theme.Link.Path, renamed)

        let impostor = buttonOf html "google"
        Assert.Contains("GitHub", impostor)
        Assert.Contains(Theme.Link.Path, impostor)
        Assert.DoesNotContain("um-mark-github", impostor)
        Assert.DoesNotContain(Theme.GitHubMarkPath, impostor)
    }

[<Fact>]
let ``the passkey button leads with the Key icon and not the link icon`` () =
    task {
        let! html = pageWith None []
        let button = buttonOf html "passkey"
        Assert.Contains(Theme.Key.Path, button)
        Assert.DoesNotContain(Theme.Link.Path, button)
        Assert.DoesNotContain("um-mark-github", button)
    }

[<Fact>]
let ``the Google and Discord buttons keep the link icon`` () =
    task {
        let! html = pageWith None []

        for slug in [ "google"; "discord" ] do
            let button = buttonOf html slug
            Assert.Contains(Theme.Link.Path, button)
            Assert.DoesNotContain("um-mark-github", button)
    }

[<Fact>]
let ``every mark in the buttons is hidden from assistive technology`` () =
    task {
        let! html = pageWith None []

        for slug in [ "github"; "company"; "google"; "discord"; "passkey" ] do
            let button = buttonOf html slug
            let svgs = Regex.Matches(button, "<svg [^>]*>")
            Assert.Equal(1, svgs.Count)

            for svg in svgs do
                Assert.Contains("aria-hidden=\"true\"", svg.Value)
    }

[<Fact>]
let ``on an app with an accent the GitHub button is primary and keeps its mark`` () =
    task {
        let! html = pageWith (Some "#f26b1d") []
        Assert.Contains("data-accent=\"f26b1d\"", html)

        let button = buttonOf html "github"
        Assert.Contains("um-btn-primary", classesOf button)
        Assert.Contains(Theme.GitHubMarkPath, button)
        Assert.Contains("um-mark-github", button)
    }

[<Fact>]
let ``when GitHub was used last it is first, with the badge and the mark`` () =
    task {
        let! html = pageWith None [ $"{BrowserCookies.LastConnectorCookie}=github" ]
        Assert.Equal("github", List.head (order html))

        let button = buttonOf html "github"
        Assert.Contains("data-last-used", button)
        Assert.Contains(Theme.GitHubMarkPath, button)
        Assert.Contains("um-mark-github", button)
    }

[<Fact>]
let ``the generated stylesheet carries the GitHub mark's rules`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! response = Browser(app.Client).Get "/assets/umbral.css"
        let! css = response.Content.ReadAsStringAsync()
        Assert.Contains(".um-mark-github", css)
    }
