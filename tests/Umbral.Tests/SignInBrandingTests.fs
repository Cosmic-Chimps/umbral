/// User Story 1 of spec 006: an app's sign-in page carries its logo and colour,
/// and nothing else does.
///
/// Asserts on the markers of contracts/public-endpoints.md (`data-accent`,
/// `data-signin-logo`, `data-secured-by`, `data-connector`) and on the classes
/// the contract names, never on the wording of a text. Every sign-in starts the
/// way a real one does, with an authorize request that redirects to `/signin`,
/// and branding is seeded straight through `BrandingStore`: the upload path is a
/// later story.
module Umbral.Tests.SignInBrandingTests

open System
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Text.RegularExpressions
open Xunit
open Umbral.Domains.Branding
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows
open Umbral.Views

let private accent (hex: string) =
    match Accent.parse hex with
    | Ok a -> a
    | Error e -> failwith $"{hex}: {e}"

/// A logo whose bytes are not a PNG: the picker only links to it.
let private logoOf (seed: byte) : Logo =
    let bytes = [| 0x89uy; 0x50uy; seed |]

    { Png = bytes
      Sha256 = Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
      Width = 240
      Height = 80 }

let private setAccent (app: Fixtures.TestApp) (clientId: string) (hex: string) =
    BrandingStore.setAccent app.DbConfig (app.Clock.GetUtcNow()) clientId (Some(accent hex))

let private setLogo (app: Fixtures.TestApp) (clientId: string) (logo: Logo) =
    BrandingStore.setLogo app.DbConfig (app.Clock.GetUtcNow()) clientId logo

let private pickerFor (clientId: string) =
    "/signin?returnUrl=" + Uri.EscapeDataString(Journeys.authorizeUrl clientId "openid")

/// An instance with the fake connector and two registered apps, A and B,
/// registered by `browser`, which is left signed in as the operator.
let private twoAppsAs (app: Fixtures.TestApp) (browser: Browser) =
    task {
        do! claimOperator app browser
        do! seedFakeConnector app
        let! idA, _ = registerApp browser "App A"
        let! idB, _ = registerApp browser "App B"
        return idA, idB
    }

let private twoApps (app: Fixtures.TestApp) = twoAppsAs app (Browser app.Client)

// ---------------------------------------------------------------------------
// Reading a page
// ---------------------------------------------------------------------------

/// HTML entities turned back into text, so a page is compared with the wording
/// as written and not as encoded.
let private decode (html: string) : string =
    match Net.WebUtility.HtmlDecode html with
    | null -> html
    | decoded -> decoded

/// The opening tag of the first element carrying `marker`.
let private tagWith (marker: string) (html: string) : string option =
    let m = Regex.Match(html, "<[a-zA-Z][^>]*" + Regex.Escape marker + "[^>]*>")
    if m.Success then Some m.Value else None

let private attribute (name: string) (tag: string) : string option =
    let m = Regex.Match(tag, "\\s" + Regex.Escape name + "=\"([^\"]*)\"")
    if m.Success then Some(decode m.Groups[1].Value) else None

/// The connector buttons' opening tags, in page order.
let private connectorButtons (html: string) =
    [ for m in Regex.Matches(html, "<a [^>]*data-connector=\"[^\"]*\"[^>]*>") -> m.Value ]

let private bodyTag (html: string) = Regex.Match(html, "<body[^>]*>").Value

/// Every `src` and `href` in the page, with what each is allowed to be: a path
/// on this origin, a `data:` URI (the favicon) or a fragment (the skip link),
/// and never `//host`. SC-005: nothing the page loads leaves this origin.
let private urlsOf (html: string) =
    [ for m in Regex.Matches(html, "\\s(?:src|href)=\"([^\"]*)\"") -> decode m.Groups[1].Value ]

let private sameOrigin (url: string) =
    (url.StartsWith "/" && not (url.StartsWith "//") && not (url.StartsWith "/\\"))
    || url.StartsWith "#"
    || url.StartsWith "data:"

/// What holds for every page here, branded or not (case h).
let private assertInvariants (what: string) (response: HttpResponseMessage) (html: string) =
    match response.Headers.TryGetValues "Content-Security-Policy" with
    | true, values ->
        Assert.Equal<string list>([ Layout.contentSecurityPolicy ], values |> Option.ofObj |> Option.defaultValue Seq.empty |> List.ofSeq)
    | _ -> Assert.Fail $"{what}: no Content-Security-Policy header"

    for url in urlsOf html do
        Assert.True(sameOrigin url, $"{what}: {url} leaves this origin")

    // Nothing inline, whatever the branding says (the same rule PageAssetsTests holds every page to).
    Assert.Empty(PageAssetsTests.externalOrInlineViolations html)

let private get (browser: Browser) (url: string) =
    task {
        let! response = browser.Get url
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        let! html = response.Content.ReadAsStringAsync()
        assertInvariants url response html
        return html
    }

// ---------------------------------------------------------------------------
// (a) An unbranded app
// ---------------------------------------------------------------------------

/// What an unbranded sign-in page is, in markers: Umbral's own header and
/// footer, secondary buttons, and nothing of branding.
let private assertUnbranded (html: string) =
    Assert.DoesNotContain("data-accent", html)
    Assert.DoesNotContain("data-signin-logo", html)
    Assert.DoesNotContain("data-secured-by", html)
    Assert.DoesNotContain("/branding/", html)

    Assert.Contains("<header", html)
    Assert.Contains("<nav", html)
    Assert.Contains(Texts.en.Footer, decode html)
    Assert.DoesNotContain(Texts.en.SecuredByUmbral, html)

    let buttons = connectorButtons html
    Assert.NotEmpty buttons

    for button in buttons do
        Assert.Contains("um-btn-secondary", button)
        Assert.DoesNotContain("um-btn-primary", button)

[<Fact>]
let ``(a) an app with no branding gets Umbral's own sign-in page`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! idA, _ = twoApps app
        let visitor = Browser app.Client
        let! html = get visitor (pickerFor idA)

        assertUnbranded html
        Assert.Contains($"data-signin-app=\"{idA}\"", html)
    }

[<Fact>]
let ``(a) the page with no app at all is unbranded too`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _ = twoApps app
        let! html = get (Browser app.Client) "/signin"
        assertUnbranded html
    }

// ---------------------------------------------------------------------------
// (b) Accent only
// ---------------------------------------------------------------------------

[<Fact>]
let ``(b) an accent alone brands the page with a stylesheet, primary buttons and the mark`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! idA, _ = twoApps app
        do! setAccent app idA "#f26b1d"
        let! html = get (Browser app.Client) (pickerFor idA)

        Assert.Contains("<body", html)
        Assert.Contains("data-accent=\"f26b1d\"", bodyTag html)

        // The accent sheet comes after umbral.css, so its rule wins on specificity ties.
        let umbral = html.IndexOf "/assets/umbral.css"
        let branded = html.IndexOf "/branding/accent/f26b1d.css"
        Assert.True(umbral >= 0 && branded > umbral, "the accent stylesheet must follow umbral.css")
        Assert.Matches("<link[^>]*href=\"/branding/accent/f26b1d.css\"", html)

        for button in connectorButtons html do
            Assert.Contains("um-btn-primary", button)

        // No wordmark bar, and the mark's own footer instead.
        Assert.DoesNotContain("<header", html)
        Assert.DoesNotContain("<nav", html)
        Assert.Contains("data-secured-by", html)
        Assert.Contains(Texts.en.SecuredByUmbral, html)
        Assert.DoesNotContain(Texts.en.Footer, decode html)

        // No logo was set.
        Assert.DoesNotContain("data-signin-logo", html)
        Assert.Contains($"data-signin-app=\"{idA}\"", html)
    }

[<Fact>]
let ``(b) the branded footer holds the Gnomon and the theme control, and no accent rule recolours the mark`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! idA, _ = twoApps app
        do! setAccent app idA "#d9480f"
        let visitor = Browser app.Client
        let! html = get visitor (pickerFor idA)

        // The footer is the one marked `data-secured-by`; the Gnomon (spec 007)
        // is in it, and so is the control (the page has no `<nav>` to put it in).
        let footer = Regex.Match(html, "<footer[^>]*data-secured-by[^>]*>.*?</footer>", RegexOptions.Singleline)
        Assert.True(footer.Success, "no branded footer")
        Assert.Contains("um-mark-upright", footer.Value)
        Assert.Contains("data-theme-switch", footer.Value)
        Assert.DoesNotContain("<header", html)
        Assert.DoesNotContain("<nav", html)

        // The mark's colours are theme tokens (`--mark-*`); the accent sheet
        // sets the accent's own properties and never those.
        let! sheet = visitor.Get "/branding/accent/d9480f.css"
        let! css = sheet.Content.ReadAsStringAsync()
        Assert.Contains("[data-accent=", css)
        Assert.DoesNotContain("--mark-", css)

        let! stylesheet = visitor.Get "/assets/umbral.css"
        let! umbralCss = stylesheet.Content.ReadAsStringAsync()
        Assert.DoesNotContain("data-accent", umbralCss)
    }

// ---------------------------------------------------------------------------
// (c) Logo only
// ---------------------------------------------------------------------------

[<Fact>]
let ``(c) a logo alone shows it first in the card and keeps the buttons secondary`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! idA, _ = twoApps app
        let logo = logoOf 1uy
        do! setLogo app idA logo
        let! html = get (Browser app.Client) (pickerFor idA)

        let tag = tagWith $"data-signin-logo=\"{idA}\"" html
        Assert.True(tag.IsSome, "no logo on the page")
        let tag = tag.Value

        Assert.StartsWith("<img", tag)
        Assert.Equal(Some $"/branding/logo/{logo.Sha256}.png", attribute "src" tag)
        Assert.Equal(Some "App A", attribute "alt" tag)
        Assert.Equal(Some "240", attribute "width" tag)
        Assert.Equal(Some "80", attribute "height" tag)

        // First in the card: nothing but the opening tags between the card and the logo.
        let card = html.IndexOf "data-screen=\"signin\""
        let logoAt = html.IndexOf "data-signin-logo"
        let heading = html.IndexOf "data-signin-app"
        Assert.True(card >= 0 && card < logoAt && logoAt < heading, "the logo must come before the heading")

        // A logo alone sets no colour: no accent attribute, no accent stylesheet.
        Assert.DoesNotContain("data-accent", html)
        Assert.DoesNotContain("/branding/accent/", html)

        for button in connectorButtons html do
            Assert.Contains("um-btn-secondary", button)
            Assert.DoesNotContain("um-btn-primary", button)

        // Still a branded page: the mark replaces the header and footer text.
        Assert.DoesNotContain("<header", html)
        Assert.Contains("data-secured-by", html)
    }

[<Fact>]
let ``a logo with an accent shows both`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! idA, _ = twoApps app
        let logo = logoOf 2uy
        do! setLogo app idA logo
        do! setAccent app idA "#1f6fa8"
        let! html = get (Browser app.Client) (pickerFor idA)

        Assert.Contains($"/branding/logo/{logo.Sha256}.png", html)
        Assert.Contains("data-accent=\"1f6fa8\"", bodyTag html)

        for button in connectorButtons html do
            Assert.Contains("um-btn-primary", button)
    }

// ---------------------------------------------------------------------------
// (d) A failure notice
// ---------------------------------------------------------------------------

[<Fact>]
let ``(d) the page a failed sign-in returns to is still branded and shows the alert`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! idA, _ = twoApps app
        do! setAccent app idA "#f26b1d"
        do! setLogo app idA (logoOf 3uy)

        let url = pickerFor idA + "&msg=access-denied"
        let! html = get (Browser app.Client) url

        Assert.Contains("data-accent=\"f26b1d\"", bodyTag html)
        Assert.Contains("data-signin-logo", html)
        Assert.Contains("role=\"alert\"", html)
        Assert.Contains(Texts.en.Notice "access-denied", decode html)
    }

// ---------------------------------------------------------------------------
// (e) A crafted link
// ---------------------------------------------------------------------------

[<Fact>]
let ``(e) a client_id on the sign-in page itself never brands it`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! idA, idB = twoApps app
        do! setAccent app idA "#f26b1d"
        do! setLogo app idA (logoOf 4uy)
        do! setAccent app idB "#1f6fa8"

        let visitor = Browser app.Client

        // The page's own query says A, the request it returns to says B. The
        // person will be signed in to B, so B's branding (or none) is what they see.
        let crafted = "/signin?client_id=" + Uri.EscapeDataString idA + "&returnUrl=" + Uri.EscapeDataString(Journeys.authorizeUrl idB "openid")
        let! html = get visitor crafted

        Assert.Contains("data-accent=\"1f6fa8\"", bodyTag html)
        Assert.DoesNotContain("f26b1d", html)
        Assert.DoesNotContain("data-signin-logo", html)
        Assert.Contains($"data-signin-app=\"{idB}\"", html)

        // And B without branding is unbranded, never A's.
        do! BrandingStore.setAccent app.DbConfig (app.Clock.GetUtcNow()) idB None
        let! plain = get visitor crafted
        assertUnbranded plain
        Assert.DoesNotContain("f26b1d", plain)

        // A client_id with nothing to return to names no app, so it brands nothing.
        let! alone = get visitor ("/signin?client_id=" + Uri.EscapeDataString idA)
        assertUnbranded alone
    }

// ---------------------------------------------------------------------------
// (f) An unknown client
// ---------------------------------------------------------------------------

[<Fact>]
let ``(f) an unknown client in the return address is unbranded`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! idA, _ = twoApps app
        do! setAccent app idA "#f26b1d"
        let! html = get (Browser app.Client) (pickerFor "no-such-app")

        assertUnbranded html
    }

[<Fact>]
let ``branding left behind for a client that is not registered is never shown`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _ = twoApps app
        // A row for an id no registered app has: the page resolves the app first.
        do! setAccent app "ghost-client" "#f26b1d"
        let! html = get (Browser app.Client) (pickerFor "ghost-client")

        assertUnbranded html
    }

// ---------------------------------------------------------------------------
// (g) The protocol error page
// ---------------------------------------------------------------------------

[<Fact>]
let ``(g) the protocol error page is never branded`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! idA, _ = twoApps app
        do! setAccent app idA "#f26b1d"
        do! setLogo app idA (logoOf 5uy)

        // A registered, branded client with a redirect_uri nobody registered:
        // Umbral cannot send the error back to the app, so it shows its own page.
        let url =
            "/connect/authorize?client_id=" + Uri.EscapeDataString idA
            + "&response_type=code&redirect_uri=" + Uri.EscapeDataString "https://evil.example.com/callback"
            + "&scope=openid&code_challenge=" + Challenge + "&code_challenge_method=S256&state=x"

        let visitor = Browser app.Client
        let! first = visitor.Get url

        let! response =
            task {
                if first.StatusCode = HttpStatusCode.Redirect then
                    return! visitor.Get(location first)
                else
                    return first
            }

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        let! html = response.Content.ReadAsStringAsync()
        Assert.Contains("<html", html)
        Assert.DoesNotContain("data-accent", html)
        Assert.DoesNotContain("data-signin-logo", html)
        Assert.DoesNotContain("data-secured-by", html)
        Assert.DoesNotContain("/branding/", html)
        assertInvariants "the protocol error page" response html
    }

// ---------------------------------------------------------------------------
// (h) Every other page
// ---------------------------------------------------------------------------

[<Fact>]
let ``the operator's pages carry no accent on their body, however many apps are branded`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        let! idA, _ = twoAppsAs app operator
        do! setAccent app idA "#f26b1d"

        for page in [ "/admin/apps"; "/admin/connectors"; "/admin/users"; "/admin/credential" ] do
            let! html = get operator page
            Assert.DoesNotContain("data-accent", bodyTag html)
            // The operator's chrome is the side rail now (spec 007), not a top bar.
            Assert.Contains("<aside", html)
    }

// ---------------------------------------------------------------------------
// Change or remove (User Story 3)
// ---------------------------------------------------------------------------

[<Fact>]
let ``after the logo is removed and the accent cleared the app's page is unbranded again`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! idA, _ = twoApps app
        do! setAccent app idA "#f26b1d"
        do! setLogo app idA (logoOf 6uy)

        let visitor = Browser app.Client
        let! branded = get visitor (pickerFor idA)
        Assert.Contains("data-signin-logo", branded)

        let now = app.Clock.GetUtcNow()
        let! _ = BrandingStore.removeLogo app.DbConfig now idA
        let! _ = BrandingStore.clearAccent app.DbConfig now idA

        // Case (a) again, in full: Umbral's header and footer, secondary buttons, no markers.
        let! html = get visitor (pickerFor idA)
        assertUnbranded html
        Assert.Contains($"data-signin-app=\"{idA}\"", html)
    }

[<Fact>]
let ``after only the logo is removed the accent still applies and the logo is gone`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! idA, _ = twoApps app
        do! setAccent app idA "#f26b1d"
        do! setLogo app idA (logoOf 7uy)

        let! removed = BrandingStore.removeLogo app.DbConfig (app.Clock.GetUtcNow()) idA
        Assert.True(removed)

        let! html = get (Browser app.Client) (pickerFor idA)
        Assert.DoesNotContain("data-signin-logo", html)
        Assert.DoesNotContain("/branding/logo/", html)
        Assert.Contains("data-accent=\"f26b1d\"", bodyTag html)

        for button in connectorButtons html do
            Assert.Contains("um-btn-primary", button)
    }

[<Fact>]
let ``after only the accent is cleared the logo still shows and the buttons go back to secondary`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! idA, _ = twoApps app
        let logo = logoOf 8uy
        do! setAccent app idA "#f26b1d"
        do! setLogo app idA logo

        let! cleared = BrandingStore.clearAccent app.DbConfig (app.Clock.GetUtcNow()) idA
        Assert.True(cleared)

        let! html = get (Browser app.Client) (pickerFor idA)
        Assert.Contains($"/branding/logo/{logo.Sha256}.png", html)
        Assert.DoesNotContain("data-accent", html)
        Assert.DoesNotContain("/branding/accent/", html)

        for button in connectorButtons html do
            Assert.Contains("um-btn-secondary", button)
    }
