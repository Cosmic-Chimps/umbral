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
