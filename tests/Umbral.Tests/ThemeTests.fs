/// The theme choice (specs/007-umbra-look): the `umbral_theme` cookie, what
/// `<html data-theme>` says about it, and (later in the feature) `POST /theme`.
///
/// The choice is decided by the server and the stylesheet alone, so the first
/// byte of every page already carries it (FR-014); these tests read it straight
/// out of the markup.
module Umbral.Tests.ThemeTests

open System.Net.Http
open System.Text.RegularExpressions
open Xunit
open Umbral.Tests.Infrastructure
open Umbral.Views
open Umbral.Views.Theme

/// GET `url` carrying `Cookie: umbral_theme=<value>` (or no cookie when `None`).
let private getWithTheme (client: HttpClient) (cookie: string option) (url: string) =
    task {
        use request = new HttpRequestMessage(HttpMethod.Get, url)

        match cookie with
        | Some value -> request.Headers.Add("Cookie", $"{ThemeCookie}={value}")
        | None -> ()

        let! response = client.SendAsync request
        return! response.Content.ReadAsStringAsync()
    }

let private htmlTag (html: string) = Regex.Match(html, "<html[^>]*>").Value

let private metas (html: string) (name: string) =
    [ for m in Regex.Matches(html, $"<meta[^>]*name=\"{name}\"[^>]*>") -> m.Value ]

// ---------------------------------------------------------------------------
// The cookie, in the pure function
// ---------------------------------------------------------------------------

[<Fact>]
let ``a posted choice is exactly system, light or dark`` () =
    Assert.Equal(Some System, ThemeChoice.parseChoice "system")
    Assert.Equal(Some Light, ThemeChoice.parseChoice "light")
    Assert.Equal(Some Dark, ThemeChoice.parseChoice "dark")

    for other in [ ""; "Light"; "DARK"; "blue"; "light;x"; " light"; "light " ] do
        Assert.Equal(None, ThemeChoice.parseChoice other)

[<Fact>]
let ``each choice writes its own name on the html element`` () =
    Assert.Equal<string list>([ "system"; "light"; "dark" ], [ System; Light; Dark ] |> List.map ThemeChoice.attr)

// ---------------------------------------------------------------------------
// <html data-theme> on every kind of page (FR-015)
// ---------------------------------------------------------------------------

/// The pages a person can reach without being signed in, each from its own app
/// so one test's state cannot colour another's.
let private pages = [ "/"; "/admin/setup"; "/signin"; "/this-page-does-not-exist" ]

[<Fact>]
let ``without a cookie every page follows the system and offers both schemes`` () =
    task {
        use! app = Fixtures.createApp ()

        for url in pages do
            let! html = getWithTheme app.Client None url

            Assert.True(Regex.IsMatch(htmlTag html, "data-theme=\"system\""), $"{url}: {htmlTag html}")
            Assert.Equal<string list>([ "<meta name=\"color-scheme\" content=\"dark light\">" ], metas html "color-scheme" |> List.map (fun m -> m.Replace(" />", ">")))

            let themeColors = metas html "theme-color"
            Assert.Equal(2, themeColors.Length)
            Assert.Contains(themeColors, fun m -> m.Contains "#0b0b14" && m.Contains "(prefers-color-scheme: dark)")
            Assert.Contains(themeColors, fun m -> m.Contains "#f4f1ea" && m.Contains "(prefers-color-scheme: light)")
    }

[<Fact>]
let ``the light cookie themes every page light, with one theme colour`` () =
    task {
        use! app = Fixtures.createApp ()

        for url in pages do
            let! html = getWithTheme app.Client (Some "light") url

            Assert.True(Regex.IsMatch(htmlTag html, "data-theme=\"light\""), $"{url}: {htmlTag html}")
            Assert.Contains("content=\"light\"", String.concat "" (metas html "color-scheme"))

            let themeColors = metas html "theme-color"
            Assert.Equal(1, themeColors.Length)
            Assert.Contains("#f4f1ea", themeColors.Head)
            Assert.DoesNotContain("media=", themeColors.Head)
    }

[<Fact>]
let ``the dark cookie themes every page dark, with one theme colour`` () =
    task {
        use! app = Fixtures.createApp ()

        for url in pages do
            let! html = getWithTheme app.Client (Some "dark") url

            Assert.True(Regex.IsMatch(htmlTag html, "data-theme=\"dark\""), $"{url}: {htmlTag html}")
            Assert.Contains("content=\"dark\"", String.concat "" (metas html "color-scheme"))

            let themeColors = metas html "theme-color"
            Assert.Equal(1, themeColors.Length)
            Assert.Contains("#0b0b14", themeColors.Head)
    }

[<Fact>]
let ``a cookie that is not exactly light or dark is the system`` () =
    task {
        use! app = Fixtures.createApp ()

        // A literal `;` would end the cookie, so the "light;x" of the contract is its escaped form.
        for value in [ "Light"; "DARK"; "blue"; ""; "light%3Bx"; "light-x"; "system" ] do
            let! html = getWithTheme app.Client (Some value) "/"
            Assert.True(Regex.IsMatch(htmlTag html, "data-theme=\"system\""), $"cookie '{value}': {htmlTag html}")
            Assert.Equal(2, (metas html "theme-color").Length)
    }

[<Fact>]
let ``the cookie's value is never echoed into the page`` () =
    task {
        use! app = Fixtures.createApp ()
        let! html = getWithTheme app.Client (Some "<script>alert(1)</script>") "/"

        Assert.DoesNotContain("alert(1)", html)
        Assert.True(Regex.IsMatch(htmlTag html, "data-theme=\"system\""))
    }

// ---------------------------------------------------------------------------
// POST /theme (contracts/theme.md)
// ---------------------------------------------------------------------------

/// Every `Set-Cookie` header of a response, lower-cased for comparing attributes.
let private setCookies (response: HttpResponseMessage) =
    match response.Headers.TryGetValues "Set-Cookie" with
    | true, values -> values |> Option.ofObj |> Option.defaultValue Seq.empty |> Seq.map (fun v -> v.ToLowerInvariant()) |> List.ofSeq
    | _ -> []

let private themeCookies (response: HttpResponseMessage) =
    setCookies response |> List.filter (fun c -> c.StartsWith $"{ThemeCookie}=")

/// Post the control's form as a browser on `page` would: token from the page,
/// then `theme` and `returnUrl` as given.
let private postTheme (browser: Flows.Browser) (page: string) (theme: string) (returnUrl: string option) =
    browser.PostForm(
        page,
        "/theme",
        [ "theme", theme
          match returnUrl with
          | Some r -> "returnUrl", r
          | None -> () ]
    )

[<Fact>]
let ``a choice posted without an antiforgery token is refused and sets nothing`` () =
    task {
        use! app = Fixtures.createApp ()
        let browser = Flows.Browser app.Client
        let! response = browser.PostWithoutToken("/theme", [ "theme", "dark"; "returnUrl", "/" ])

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode)
        Assert.Empty(themeCookies response)
    }

[<Fact>]
let ``light and dark are kept for a year in a cookie scripts cannot read`` () =
    task {
        use! app = Fixtures.createApp ()

        for choice in [ "light"; "dark" ] do
            let browser = Flows.Browser app.Client
            let! response = postTheme browser "/" choice (Some "/signin?returnUrl=%2Fconnect%2Fauthorize%3Fclient_id%3Dx")

            Assert.Equal(System.Net.HttpStatusCode.SeeOther, response.StatusCode)
            Assert.Equal("/signin?returnUrl=%2Fconnect%2Fauthorize%3Fclient_id%3Dx", Flows.location response)

            let cookie = themeCookies response |> List.exactlyOne
            Assert.StartsWith($"{ThemeCookie}={choice};", cookie)
            Assert.Contains("max-age=31536000", cookie)
            Assert.Contains("path=/", cookie)
            Assert.Contains("samesite=lax", cookie)
            Assert.Contains("httponly", cookie)
            // The test server answers on https, so the cookie is Secure.
            Assert.Contains("secure", cookie)

            // The next page is in that theme.
            let! html = browser.GetHtml "/"
            Assert.True(Regex.IsMatch(htmlTag html, $"data-theme=\"{choice}\""), htmlTag html)
    }

[<Fact>]
let ``system deletes the cookie, from the same path`` () =
    task {
        use! app = Fixtures.createApp ()
        let browser = Flows.Browser app.Client
        let! _ = postTheme browser "/" "dark" None
        let! response = postTheme browser "/" "system" (Some "/")

        Assert.Equal(System.Net.HttpStatusCode.SeeOther, response.StatusCode)
        let cookie = themeCookies response |> List.exactlyOne
        Assert.StartsWith($"{ThemeCookie}=;", cookie)
        Assert.Contains("path=/", cookie)
        Assert.Contains("expires=", cookie)

        let! html = browser.GetHtml "/"
        Assert.True(Regex.IsMatch(htmlTag html, "data-theme=\"system\""), htmlTag html)
    }

[<Fact>]
let ``an unknown choice changes nothing and still returns`` () =
    task {
        use! app = Fixtures.createApp ()

        for choice in [ "blue"; "Light"; ""; "light;x" ] do
            let browser = Flows.Browser app.Client
            let! response = postTheme browser "/" choice (Some "/signin")

            Assert.Equal(System.Net.HttpStatusCode.SeeOther, response.StatusCode)
            Assert.Equal("/signin", Flows.location response)
            Assert.Empty(themeCookies response)
    }

[<Fact>]
let ``only a local path is returned to, anything else goes home`` () =
    task {
        use! app = Fixtures.createApp ()

        let cases =
            [ Some "/admin/apps", "/admin/apps"
              Some "/", "/"
              Some "//evil.example", "/"
              Some "/\\evil.example", "/"
              Some "https://evil.example/", "/"
              Some "evil.example", "/"
              Some "", "/"
              None, "/"
              Some "/a\r\nSet-Cookie:x", "/"
              Some "/a b", "/" ]

        for returnUrl, expected in cases do
            let browser = Flows.Browser app.Client
            let! response = postTheme browser "/" "dark" returnUrl

            Assert.Equal(System.Net.HttpStatusCode.SeeOther, response.StatusCode)
            Assert.Equal(expected, Flows.location response)
    }

[<Fact>]
let ``the local-path rule`` () =
    for good in [ "/"; "/admin/apps"; "/signin?returnUrl=%2Fconnect%2Fauthorize%3Fa%3Db&c=d"; "/a#b" ] do
        Assert.True(Umbral.Infrastructure.WebForms.isLocalPath good, good)

    for bad in
        [ ""
          "evil"
          "//evil.example"
          "/\\evil.example"
          "https://evil.example"
          "/a\nb"
          "/a\rb"
          "/a b"
          "/a\tb"
          "/a\u0000b"
          "/a\u00a0b"
          "/caf\u00e9"
          " /a" ] do
        Assert.False(Umbral.Infrastructure.WebForms.isLocalPath bad, bad)

// ---------------------------------------------------------------------------
// The control, on every kind of page
// ---------------------------------------------------------------------------

let private controls (html: string) =
    [ for m in Regex.Matches(html, "<form[^>]*data-theme-switch[^>]*>.*?</form>", RegexOptions.Singleline) -> m.Value ]

/// The `value`s of the buttons that say `aria-pressed="true"`.
let private pressed (control: string) =
    [ for m in Regex.Matches(control, "<button[^>]*>") do
          if m.Value.Contains "aria-pressed=\"true\"" then
              yield Regex.Match(m.Value, "\\svalue=\"([^\"]*)\"").Groups[1].Value ]

let private returnUrlOf (control: string) =
    let m = Regex.Match(control, "name=\"returnUrl\"[^>]*value=\"([^\"]*)\"")
    if m.Success then System.Net.WebUtility.HtmlDecode m.Groups[1].Value else failwith "no returnUrl"

let private assertControl (where: string) (html: string) (expectedPressed: string) (expectedReturn: string) =
    let found = controls html
    Assert.True(found.Length = 1, $"{where}: expected one theme control, found {found.Length}")
    let control = found.Head

    Assert.Matches("<form[^>]*method=\"post\"[^>]*action=\"/theme\"|<form[^>]*action=\"/theme\"[^>]*method=\"post\"", control)
    Assert.Contains("name=\"__RequestVerificationToken\"", control)
    Assert.Matches("role=\"group\"[^>]*aria-labelledby=\"theme-label\"|aria-labelledby=\"theme-label\"[^>]*role=\"group\"", control)
    Assert.Contains("id=\"theme-label\"", control)
    Assert.Contains("data-theme-note", control)

    for value in [ "system"; "light"; "dark" ] do
        Assert.Matches($"<button[^>]*type=\"submit\"[^>]*name=\"theme\"[^>]*value=\"{value}\"|<button[^>]*name=\"theme\"[^>]*value=\"{value}\"[^>]*type=\"submit\"", control)

    Assert.Equal(3, Regex.Matches(control, "<button[^>]*aria-label=\"[^\"]+\"[^>]*aria-pressed=\"(true|false)\"|<button[^>]*aria-pressed=\"(true|false)\"[^>]*aria-label=\"[^\"]+\"").Count)
    Assert.Equal<string list>([ expectedPressed ], pressed control)
    Assert.Equal(expectedReturn, returnUrlOf control)
    // One id each, so the page's ids stay unique.
    Assert.Equal(1, Regex.Matches(html, "id=\"theme-label\"").Count)

[<Fact>]
let ``the control is in the footer of every page that is not the operator's`` () =
    task {
        use! app = Fixtures.createApp ()

        for url in [ "/"; "/admin/setup"; "/signin?x=1"; "/this-page-does-not-exist" ] do
            let! html = getWithTheme app.Client None url
            assertControl url html "system" url

            // In the footer, after the page's own content.
            Assert.True(html.IndexOf "data-theme-switch" > html.IndexOf "</main>", $"{url}: control is not after <main>")
    }

[<Fact>]
let ``the pressed option follows the cookie, and an invalid cookie is the system`` () =
    task {
        use! app = Fixtures.createApp ()

        for cookie, expected in [ None, "system"; Some "light", "light"; Some "dark", "dark"; Some "blue", "system"; Some "Light", "system" ] do
            let! html = getWithTheme app.Client cookie "/"
            assertControl $"cookie {cookie}" html expected "/"
    }

[<Fact>]
let ``the control is on the operator sign-in page and the operator's rail`` () =
    task {
        use! app = Fixtures.createAppWith Flows.fakeSettings ignore
        let browser = Flows.Browser app.Client
        do! Flows.claimOperator app browser

        // The notice is not carried back: it would show again, stale.
        let! signedIn = browser.GetHtml "/admin/apps?msg=app-created&app=x"
        assertControl "rail" signedIn "system" "/admin/apps"
        // In the rail, before the page's own content.
        Assert.True(signedIn.IndexOf "data-theme-switch" < signedIn.IndexOf "<main", "rail control is not before <main>")
        Assert.Contains("<aside", signedIn)
        let aside = Regex.Match(signedIn, "<aside.*?</aside>", RegexOptions.Singleline).Value
        Assert.Contains("data-theme-switch", aside)
        // The theme control is not a <nav>: the navigation stays one landmark,
        // beside the footer's links to the source and the documentation.
        Assert.Equal(1, Regex.Matches(signedIn, "<nav(?![^>]*data-footer-links)[\\s>]").Count)

        let! signedOut = getWithTheme app.Client None "/admin/signin?msg=signin-failed"
        assertControl "/admin/signin" signedOut "system" "/admin/signin"

        // A search is what the operator was looking at, so it is kept, alone.
        let! users = browser.GetHtml "/admin/users?msg=user-disabled&search=ann%20b"
        assertControl "/admin/users" users "system" "/admin/users?search=ann%20b"
    }

[<Fact>]
let ``the return address is written encoded, so a non-ASCII path comes back and is followed`` () =
    task {
        use! app = Fixtures.createApp ()

        // A 404 for any address carries the control; the path must reach the
        // Location header as it was typed, percent-encoded.
        let! html = getWithTheme app.Client None "/caf%C3%A9?q=%C3%A9"
        assertControl "non-ASCII 404" html "system" "/caf%C3%A9?q=%C3%A9"

        let browser = Flows.Browser app.Client
        let! response = postTheme browser "/" "dark" (Some "/caf%C3%A9?q=%C3%A9")
        Assert.Equal(System.Net.HttpStatusCode.SeeOther, response.StatusCode)
        Assert.Equal("/caf%C3%A9?q=%C3%A9", Flows.location response)

        // Decoded, it is not a path the server could write: home instead of a 500.
        let! decoded = postTheme browser "/" "dark" (Some "/café")
        Assert.Equal(System.Net.HttpStatusCode.SeeOther, decoded.StatusCode)
        Assert.Equal("/", Flows.location decoded)
    }

[<Fact>]
let ``a branded and an unbranded sign-in page both carry the control, and no header or nav`` () =
    task {
        use! app = Fixtures.createAppWith Flows.fakeSettings ignore
        let browser = Flows.Browser app.Client
        do! Flows.claimOperator app browser
        do! Flows.seedFakeConnector app
        let! plain, _ = Flows.registerApp browser "Plain app"
        let! branded, _ = Flows.registerApp browser "Branded app"

        match Umbral.Domains.Branding.Accent.parse "#d9480f" with
        | Ok accent -> do! Umbral.Infrastructure.BrandingStore.setAccent app.DbConfig (app.Clock.GetUtcNow()) branded (Some accent)
        | Error e -> failwith $"{e}"

        for clientId in [ plain; branded ] do
            let url = "/signin?returnUrl=" + System.Uri.EscapeDataString(Journeys.authorizeUrl clientId "openid")
            let! html = getWithTheme app.Client (Some "dark") url
            assertControl clientId html "dark" url

            if clientId = branded then
                Assert.DoesNotContain("<header", html)
                Assert.DoesNotContain("<nav", html)
    }
