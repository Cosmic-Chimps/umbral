/// The operator's branding forms on the Apps screen (spec 006,
/// contracts/admin-branding.md). This file holds the accent colour (User
/// Story 1); the logo and removal routes add their own tests as they land.
///
/// Asserts on notice codes in redirects, on `data-*` markers and on what the
/// database holds, never on the wording of a text.
module Umbral.Tests.AppBrandingAdminTests

open System
open System.Net
open System.Text.RegularExpressions
open System.Threading.Tasks
open Xunit
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows
open Umbral.Views

let private decode (html: string) : string =
    match Net.WebUtility.HtmlDecode html with
    | null -> html
    | decoded -> decoded

let private scalar (app: Fixtures.TestApp) (sql: string) (parameters: (string * string) list) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- sql

        for name, value in parameters do
            command.Parameters.AddWithValue(name, value) |> ignore

        let! value = command.ExecuteScalarAsync()
        return string value
    }

/// The accent stored for an app, or "" when it has none.
let private storedAccent (app: Fixtures.TestApp) (clientId: string) =
    scalar app "SELECT COALESCE((SELECT accent_color FROM app_branding WHERE client_id = @c), '')" [ "@c", clientId ]

let private eventCount (app: Fixtures.TestApp) (clientId: string) (detail: string) =
    task {
        let! n =
            scalar
                app
                "SELECT count(*) FROM security_events WHERE event_type = 'app_updated' AND client_id = @c AND detail = @d"
                [ "@c", clientId; "@d", detail ]

        return int n
    }

let private allEvents (app: Fixtures.TestApp) =
    task {
        let! n = scalar app "SELECT count(*) FROM security_events" []
        return int n
    }

let private cardRedirect (notice: string) (clientId: string) =
    $"/admin/apps?msg={notice}&app={clientId}#app-{clientId}"

/// The operator signed in, the fake connector, and two apps: the operator's
/// browser and the two client ids.
let private prepare (app: Fixtures.TestApp) =
    task {
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app
        let! idA, _ = registerApp operator "App A"
        let! idB, _ = registerApp operator "App B"
        return operator, idA, idB
    }

let private postAccent (operator: Browser) (clientId: string) (value: string) =
    operator.PostForm("/admin/apps", $"/admin/apps/{clientId}/branding/accent", [ "accent", value ])

// ---------------------------------------------------------------------------
// Access
// ---------------------------------------------------------------------------

[<Fact>]
let ``without an operator session the accent route redirects to sign-in and changes nothing`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, idA, _ = prepare app
        let visitor = Browser app.Client

        let! response = visitor.PostWithoutToken($"/admin/apps/{idA}/branding/accent", [ "accent", "#f26b1d" ])

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.StartsWith("/admin/signin", location response)
        let! stored = storedAccent app idA
        Assert.Equal("", stored)
    }

[<Fact>]
let ``without an antiforgery token the accent route is a 400 and changes nothing`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app
        let! before = allEvents app
        let! response = operator.PostWithoutToken($"/admin/apps/{idA}/branding/accent", [ "accent", "#f26b1d" ])

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        let! stored = storedAccent app idA
        Assert.Equal("", stored)
        let! after = allEvents app
        Assert.Equal(before, after)
    }

// ---------------------------------------------------------------------------
// Save
// ---------------------------------------------------------------------------

[<Fact>]
let ``a colour is saved lowercase, announced, and logged once`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, idB = prepare app

        let! response = postAccent operator idA "#F26B1D"

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Equal(cardRedirect "app-accent-saved" idA, location response)

        let! stored = storedAccent app idA
        Assert.Equal("f26b1d", stored)

        // Only that app.
        let! other = storedAccent app idB
        Assert.Equal("", other)

        let! events = eventCount app idA "branding_accent_set"
        Assert.Equal(1, events)

        // The event is the operator's, with the client id and Success.
        let! row =
            scalar
                app
                "SELECT actor || '|' || outcome FROM security_events WHERE detail = 'branding_accent_set' AND client_id = @c"
                [ "@c", idA ]

        Assert.Equal("operator|success", row)
    }

[<Fact>]
let ``a colour that is hard to see on the card is saved and the notice says so`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app

        let! response = postAccent operator idA "#0a0a10"

        Assert.Equal(cardRedirect "app-accent-low-contrast" idA, location response)
        let! stored = storedAccent app idA
        Assert.Equal("0a0a10", stored)
        let! events = eventCount app idA "branding_accent_set"
        Assert.Equal(1, events)
    }

[<Theory>]
[<InlineData("f26b1d")>]
[<InlineData("#fff")>]
[<InlineData("orange")>]
[<InlineData("rgb(1,2,3)")>]
[<InlineData("#f26b1dff")>]
// Empty is "clear" in a later story; until then it is refused.
[<InlineData("")>]
let ``a value that is not #rrggbb is refused, changing nothing and logging nothing`` (value: string) =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app

        // An accent already stored must survive a refusal.
        let! _ = postAccent operator idA "#1f6fa8"
        let! before = allEvents app

        let! response = postAccent operator idA value

        Assert.Equal(cardRedirect "app-accent-invalid" idA, location response)
        let! stored = storedAccent app idA
        Assert.Equal("1f6fa8", stored)
        let! after = allEvents app
        Assert.Equal(before, after)
    }

[<Fact>]
let ``a refusal on an app with no branding creates no row`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app

        let! response = postAccent operator idA "orange"
        Assert.Equal(cardRedirect "app-accent-invalid" idA, location response)

        let! rows = scalar app "SELECT count(*) FROM app_branding" []
        Assert.Equal("0", rows)
    }

[<Fact>]
let ``an unknown client is app-not-found and nothing is written`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, _ = prepare app
        let! before = allEvents app

        let! response = postAccent operator "no-such-app" "#f26b1d"

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Equal("/admin/apps?msg=app-not-found", location response)
        let! rows = scalar app "SELECT count(*) FROM app_branding" []
        Assert.Equal("0", rows)
        let! after = allEvents app
        Assert.Equal(before, after)
    }

// ---------------------------------------------------------------------------
// The Apps page
// ---------------------------------------------------------------------------

let private bodyTag (html: string) = Regex.Match(html, "<body[^>]*>").Value

[<Fact>]
let ``the Apps page links each accent once, shows the sample in it, and never colours its own body`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, idB = prepare app

        // Two apps, one colour: one stylesheet.
        let! _ = postAccent operator idA "#f26b1d"
        let! _ = postAccent operator idB "#F26B1D"

        let! html = operator.GetHtml "/admin/apps"

        let links = Regex.Matches(html, "<link[^>]*href=\"/branding/accent/f26b1d.css\"")
        Assert.Equal(1, links.Count)

        // After umbral.css, before the scripts.
        Assert.True(html.IndexOf "/assets/umbral.css" < html.IndexOf "/branding/accent/f26b1d.css")
        Assert.True(html.IndexOf "/branding/accent/f26b1d.css" < html.IndexOf "/assets/htmx.min.js")

        // The page itself keeps Umbral's colour (FR-005).
        Assert.DoesNotContain("data-accent", bodyTag html)

        // Each card's sample sits inside the accent it previews.
        let samples = Regex.Matches(html, "data-accent=\"f26b1d\"[^>]*>\\s*<button[^>]*data-branding-sample")
        Assert.Equal(2, samples.Count)
    }

[<Fact>]
let ``an app card has the branding section with its accent form, and an app with none previews Umbral's colour`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, idB = prepare app
        let! _ = postAccent operator idA "#f26b1d"

        let! html = operator.GetHtml "/admin/apps"

        for id in [ idA; idB ] do
            Assert.Contains($"id=\"app-{id}\"", html)
            Assert.Contains($"data-branding=\"{id}\"", html)
            Assert.Contains($"action=\"/admin/apps/{id}/branding/accent\"", html)

        Assert.Equal(2, Regex.Matches(html, "data-accent-form").Count)
        Assert.Equal(2, Regex.Matches(html, "data-branding-logo").Count)
        // The native colour picker has no name, so it is never posted, and starts hidden.
        Assert.Matches("<input[^>]*type=\"color\"[^>]*data-accent-picker[^>]*>", html)
        Assert.DoesNotMatch("<input[^>]*type=\"color\"[^>]*name=", html)
        Assert.DoesNotMatch("<input[^>]*name=[^>]*type=\"color\"", html)
        Assert.Contains("hidden", Regex.Match(html, "<input[^>]*data-accent-picker[^>]*>").Value)

        // The text field carries the colour as `#hex`, or nothing for an app with none.
        Assert.Contains("value=\"#f26b1d\"", html)

        // Only A has an accent, so only A's sample is wrapped.
        Assert.Equal(1, Regex.Matches(html, "data-accent=\"f26b1d\"").Count)
        Assert.Equal(2, Regex.Matches(html, "data-branding-sample").Count)

        // No logo upload yet: the card says so.
        Assert.Contains(Texts.en.NoLogo, decode html)
    }

[<Fact>]
let ``the saved notice is shown on the Apps page after the redirect`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app
        let! response = postAccent operator idA "#f26b1d"
        let! html = operator.GetHtml(location response)

        Assert.Contains("data-notice=\"app-accent-saved\"", html)
        Assert.Contains(Texts.en.Notice "app-accent-saved", decode html)
    }
