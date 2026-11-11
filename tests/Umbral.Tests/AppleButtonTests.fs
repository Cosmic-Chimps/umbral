/// The Apple connector's button on the sign-in page (specs/013 User Story 4,
/// FR-022).
///
/// Apple's rules for a "Sign in with Apple" button fix its title and its colours,
/// so this one button is drawn by its own class, `um-btn-apple`, whatever the
/// connector is named and whatever accent the app chose. These tests read the
/// page's markers and classes, and the generated stylesheet, never a colour.
module Umbral.Tests.AppleButtonTests

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

/// An instance with the Apple connector (named `displayName`), the fake one and
/// one app, and the page of that app. `accent` is the app's colour, if any.
let private pageWith (displayName: string) (accent: string option) (cookies: string list) =
    task {
        let fake = FakeApple.create ()
        let! app = Fixtures.createAppWithProviderHandler fake.Handler fakeSettings ignore
        use _ = app
        FakeApple.attach app fake
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app
        do! FakeApple.seedConnector app fake

        if displayName <> "Apple" then
            let! stored = ConnectorStore.findBySlug app.DbConfig "apple"

            match stored with
            | Some connector ->
                do!
                    ConnectorStore.save
                        app.DbConfig
                        app.Clock
                        { connector with DisplayName = displayName }
                        None
            | None -> failwith "the Apple connector was not seeded"

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
let ``the Apple button says Sign in with Apple`` () =
    task {
        let! html = pageWith "Apple" None []
        Assert.Contains(Texts.en.SignInWithApple, buttonOf html "apple")
        Assert.Equal("Sign in with Apple", Texts.en.SignInWithApple)
    }

[<Fact>]
let ``the Apple button keeps its title when the connector is named something else`` () =
    task {
        let! html = pageWith "Apple (work)" None []
        Assert.Contains("Sign in with Apple", buttonOf html "apple")
        Assert.DoesNotContain("Apple (work)", html)
    }

[<Fact>]
let ``on an unbranded page the Apple button is um-btn-apple and neither primary nor secondary`` () =
    task {
        let! html = pageWith "Apple" None []
        let classes = classesOf (buttonOf html "apple")
        Assert.Contains("um-btn", classes)
        Assert.Contains("um-btn-apple", classes)
        Assert.DoesNotContain("um-btn-primary", classes)
        Assert.DoesNotContain("um-btn-secondary", classes)

        // The other connector is the secondary button it always was.
        Assert.Contains("um-btn-secondary", classesOf (buttonOf html "fake"))
    }

[<Fact>]
let ``an app's accent makes the other buttons primary and never the Apple button`` () =
    task {
        let! html = pageWith "Apple" (Some "#f26b1d") []
        Assert.Contains("data-accent=\"f26b1d\"", html)
        Assert.Contains("um-btn-primary", classesOf (buttonOf html "fake"))

        let classes = classesOf (buttonOf html "apple")
        Assert.Contains("um-btn-apple", classes)
        Assert.DoesNotContain("um-btn-primary", classes)
        Assert.DoesNotContain("um-btn-secondary", classes)
    }

[<Fact>]
let ``the Apple button has no link icon and an empty slot for Apple's logo`` () =
    task {
        let! html = pageWith "Apple" None []
        let button = buttonOf html "apple"
        Assert.DoesNotContain("<svg", button)
        Assert.Matches("<span class=\"um-apple-logo\"[^>]*data-apple-logo[^>]*aria-hidden=\"true\"[^>]*></span>", button)

        // The others keep their link icon.
        Assert.Contains("<svg", buttonOf html "fake")
    }

[<Fact>]
let ``when Apple was used last it is first, with the badge and the accessible name`` () =
    task {
        let! html = pageWith "Apple" None [ $"{BrowserCookies.LastConnectorCookie}=apple" ]
        Assert.Equal("apple", List.head (order html))

        let button = buttonOf html "apple"
        Assert.Contains("data-last-used", button)
        Assert.Contains($"<span class=\"um-last-used\">{Texts.en.PickerLastUsed}</span>", button)
        Assert.Contains($"aria-label=\"{Texts.en.PickerLastUsedName Texts.en.SignInWithApple}\"", button)
        Assert.Contains("um-btn-apple", classesOf button)
        Assert.DoesNotContain("data-last-used", buttonOf html "fake")
    }

[<Fact>]
let ``the generated stylesheet carries the Apple button's rules`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! response = Browser(app.Client).Get "/assets/umbral.css"
        let! css = response.Content.ReadAsStringAsync()
        Assert.Contains(".um-btn-apple{", css)
        Assert.Contains(".um-apple-logo:empty{", css)
    }
