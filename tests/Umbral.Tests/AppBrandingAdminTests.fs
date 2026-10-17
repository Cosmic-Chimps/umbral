/// The operator's branding forms on the Apps screen (spec 006,
/// contracts/admin-branding.md). This file holds the accent colour (User
/// Story 1); the logo and removal routes add their own tests as they land.
///
/// Asserts on notice codes in redirects, on `data-*` markers and on what the
/// database holds, never on the wording of a text.
module Umbral.Tests.AppBrandingAdminTests

open System
open System.Collections.Concurrent
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.RegularExpressions
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Xunit
open Umbral.Domains
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows
open Umbral.Tests.Infrastructure.PngBuilder
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

// ===========================================================================
// The logo (User Story 2)
// ===========================================================================

/// A multipart post to the logo route: the antiforgery token (taken from
/// `formPage`) as a field, and `file`, if there is one, as the `logo` part.
let private postMultipart
    (operator: Browser)
    (formPage: string)
    (action: string)
    (file: (string * byte[]) option)
    =
    task {
        let! token = operator.TokenFrom formPage
        let content = new MultipartFormDataContent()
        content.Add(new StringContent(token), "__RequestVerificationToken")

        match file with
        | Some(fileName, bytes) ->
            let part = new ByteArrayContent(bytes)
            part.Headers.ContentType <- MediaTypeHeaderValue "application/octet-stream"
            content.Add(part, "logo", fileName)
        | None -> ()

        use message = new HttpRequestMessage(HttpMethod.Post, action)
        message.Content <- content
        return! operator.Send message
    }

let private postLogo (operator: Browser) (clientId: string) (file: (string * byte[]) option) =
    postMultipart operator "/admin/apps" $"/admin/apps/{clientId}/branding/logo" file

/// A PNG of this size as the browser's canvas would send it, or as an editor
/// would, with a text chunk to be stripped.
let private pngOf (width: int) (height: int) =
    build
        { png width height 6 8 with
            Extra = [ "tEXt", Encoding.ASCII.GetBytes "Comment\000the operator's private note", BeforeIdat ] }

let private storedLogo (app: Fixtures.TestApp) (clientId: string) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT logo_png FROM app_branding WHERE client_id = @c"
        command.Parameters.AddWithValue("@c", clientId) |> ignore
        let! value = command.ExecuteScalarAsync()

        return
            match value with
            | :? (byte[]) as bytes -> Some bytes
            | _ -> None
    }

let private logoAddress (bytes: byte[]) =
    let logo = match PngLogo.validate bytes with Ok l -> l | Error e -> failwith $"{e}"
    $"/branding/logo/{logo.Sha256}.png"

[<Fact>]
let ``a valid PNG is saved as the file Umbral wrote, announced, logged once and shown`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app
        let upload = pngOf 200 100

        let! response = postLogo operator idA (Some("logo.png", upload))

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Equal(cardRedirect "app-logo-saved" idA, location response)

        // Stored: the rewritten file, not the upload (which carried a text chunk).
        let expected = (match PngLogo.validate upload with Ok l -> l | Error e -> failwith $"{e}")
        let! stored = storedLogo app idA
        Assert.Equal<byte array>(expected.Png, stored.Value)
        Assert.NotEqual<byte array>(upload, stored.Value)
        Assert.DoesNotContain("private note", Encoding.ASCII.GetString stored.Value)

        let! events = eventCount app idA "branding_logo_set"
        Assert.Equal(1, events)

        // Served at its address, which is the hash of what was stored.
        let! served = app.Client.GetAsync(logoAddress upload)
        let! servedBytes = served.Content.ReadAsByteArrayAsync()
        Assert.Equal(HttpStatusCode.OK, served.StatusCode)
        Assert.Equal<byte array>(expected.Png, servedBytes)

        // On the card, and on the sign-in page.
        let! html = operator.GetHtml "/admin/apps"
        Assert.Contains($"<img", html)
        Assert.Contains(logoAddress upload, html)

        let visitor = Browser app.Client
        let! picker = visitor.GetHtml("/signin?returnUrl=" + Uri.EscapeDataString(Journeys.authorizeUrl idA "openid"))
        Assert.Contains($"data-signin-logo=\"{idA}\"", picker)
        Assert.Contains(logoAddress upload, picker)
    }

[<Fact>]
let ``a logo saved next to an accent keeps the accent`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app
        let! _ = postAccent operator idA "#f26b1d"
        let! _ = postLogo operator idA (Some("logo.png", pngOf 64 64))

        let! accent = storedAccent app idA
        Assert.Equal("f26b1d", accent)
    }

// Each case is one way an upload can be wrong, and the notice it gets.
let private refusals () : (string * string * (string * byte[]) option) list =
    let jpeg = Array.append [| 0xFFuy; 0xD8uy; 0xFFuy; 0xE0uy |] (Array.zeroCreate 200)

    [ "no file", "app-logo-missing", None
      "an empty file", "app-logo-missing", Some("logo.png", [||])
      "a JPEG", "app-logo-not-png", Some("logo.jpg", jpeg)
      "a damaged PNG", "app-logo-corrupt", Some("logo.png", build { png 64 64 6 8 with BadCrc = Some "IDAT" })
      "an interlaced PNG", "app-logo-interlaced", Some("logo.png", build { png 64 64 6 8 with Interlace = 1 })
      "a PNG too small", "app-logo-too-small", Some("logo.png", pngOf 16 16)
      "a PNG too big", "app-logo-too-big", Some("logo.png", build { png 600 100 6 8 with Inflated = Some(Array.zeroCreate 8) }) ]

[<Fact>]
let ``every refused upload leaves the existing logo byte-identical and logs nothing`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app

        // A logo already there, and an accent beside it.
        let! _ = postAccent operator idA "#f26b1d"
        let! first = postLogo operator idA (Some("logo.png", pngOf 100 100))
        Assert.Equal(cardRedirect "app-logo-saved" idA, location first)
        let! before = storedLogo app idA
        let! eventsBefore = allEvents app

        for what, notice, file in refusals () do
            let! response = postLogo operator idA file

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
            Assert.Equal(cardRedirect notice idA, location response)

            let! after = storedLogo app idA
            Assert.Equal<byte array>(before.Value, after.Value)
            let! eventsAfter = allEvents app
            Assert.True((eventsBefore = eventsAfter), $"{what} wrote a security event")

        let! accent = storedAccent app idA
        Assert.Equal("f26b1d", accent)
    }

[<Fact>]
let ``a refused first upload creates no row`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app

        for _, _, file in refusals () do
            let! _ = postLogo operator idA file
            ()

        let! rows = scalar app "SELECT count(*) FROM app_branding" []
        Assert.Equal("0", rows)
    }

[<Fact>]
let ``an unknown client is app-not-found for an upload`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, _ = prepare app
        let! response = postLogo operator "no-such-app" (Some("logo.png", pngOf 64 64))

        Assert.Equal("/admin/apps?msg=app-not-found", location response)
        let! rows = scalar app "SELECT count(*) FROM app_branding" []
        Assert.Equal("0", rows)
    }

[<Fact>]
let ``the size a refusal names is the measured one, shown once, from the cookie`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app

        // Too small: 16 x 20 pixels.
        let! small = postLogo operator idA (Some("logo.png", pngOf 16 20))
        Assert.Equal(cardRedirect "app-logo-too-small" idA, location small)
        // The size is not in the redirect.
        Assert.DoesNotContain("16", location small)
        let! html = operator.GetHtml(location small)
        Assert.Contains(Texts.en.AppLogoTooSmall 16 20, decode html)

        // Shown once: the cookie went with the read, and the fixed text is what a refresh says.
        let! again = operator.GetHtml(location small)
        Assert.DoesNotContain(Texts.en.AppLogoTooSmall 16 20, decode again)
        Assert.Contains(Texts.en.Notice "app-logo-too-small", decode again)

        // Too big: 600 x 100 pixels.
        let! big = postLogo operator idA (Some("logo.png", build { png 600 100 6 8 with Inflated = Some(Array.zeroCreate 8) }))
        let! bigHtml = operator.GetHtml(location big)
        Assert.Contains(Texts.en.AppLogoTooBig 600 100, decode bigHtml)
    }

[<Fact>]
let ``a size written into the address is never shown`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app

        // A link a person could be sent. No cookie came with it, so there is no measured value.
        for code in [ "app-logo-too-small"; "app-logo-too-big"; "app-logo-too-large" ] do
            let! html = operator.GetHtml($"/admin/apps?msg={code}&app={idA}&width=9999&height=8888&bytes=777777777#app-{idA}")
            // Without the theme control, whose hidden return address is this very URL.
            let text = decode (Umbral.Tests.Infrastructure.Flows.withoutThemeControl html)
            Assert.DoesNotContain("9999", text)
            Assert.DoesNotContain("8888", text)
            Assert.DoesNotContain("777777777", text)
            Assert.Contains(Texts.en.Notice code, text)
    }

[<Fact>]
let ``a measured value for one app is not shown on another's card`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, idB = prepare app

        let! small = postLogo operator idA (Some("logo.png", pngOf 16 20))
        // The cookie is for A; a page that says B was refused must not use it.
        let! html = operator.GetHtml($"/admin/apps?msg=app-logo-too-small&app={idB}")
        Assert.DoesNotContain(Texts.en.AppLogoTooSmall 16 20, decode html)
        ignore small
    }

// ---------------------------------------------------------------------------
// Size limits, and what the form read does with a large body (research R3)
// ---------------------------------------------------------------------------

/// A PNG of about this many bytes: random image data does not compress, so the
/// file is the size of the pixels.
let private incompressiblePng (width: int) (height: int) =
    let spec = png width height 6 8
    let data = imageData spec
    let random = Random 7

    for i in 0 .. data.Length - 1 do
        if i % (1 + width * 4) <> 0 then
            data[i] <- byte (random.Next 256)

    build { spec with Inflated = Some data }

/// Where ASP.NET Core spills a body that does not fit in memory.
let private tempDirectory () =
    match Environment.GetEnvironmentVariable "ASPNETCORE_TEMP" with
    | null
    | "" -> Path.GetTempPath()
    | dir -> dir

let private spillFiles () =
    Directory.GetFiles(tempDirectory (), "ASPNETCORE_*.tmp") |> Set.ofArray

/// A middleware outside the whole pipeline, which notes, for each request, the
/// temp files that exist when the response has been written and the request is
/// still alive (a spilled body's file is deleted when the request is disposed,
/// just after). Registered as a startup filter so it wraps the antiforgery
/// middleware and the endpoint alike.
type private SpillLog() =
    member val Spills = ConcurrentDictionary<string, int>()

type private SpillProbe(log: SpillLog) =
    interface IStartupFilter with
        member _.Configure(next) =
            Action<IApplicationBuilder>(fun app ->
                app.Use(fun (ctx: HttpContext) (following: RequestDelegate) ->
                    task {
                        let before = spillFiles ()
                        do! following.Invoke ctx
                        let created = Set.difference (spillFiles ()) before |> Set.count

                        if created > 0 then
                            log.Spills[string ctx.Request.Path] <- created
                    }
                    :> Task)
                |> ignore

                next.Invoke app)

let private withSpillProbe () =
    let log = SpillLog()

    let app =
        Fixtures.createAppWith fakeSettings (fun builder ->
            builder.Services.AddSingleton<SpillLog>(log) |> ignore
            builder.Services.AddSingleton<IStartupFilter, SpillProbe>() |> ignore)

    log, app

[<Fact>]
let ``the probe sees a body over 64 KB spilled to disk on an endpoint with the default form options`` () =
    task {
        // The control: `POST /admin/apps` has no form options of its own, so a
        // 300 KB file in its form spills. If this did not, the probe would be
        // blind and the next test would prove nothing.
        let log, appTask = withSpillProbe ()
        use! app = appTask
        let! operator, _, _ = prepare app

        let! _ = postMultipart operator "/admin/apps" "/admin/apps" (Some("big.bin", Array.zeroCreate 300_000))

        Assert.True(log.Spills.ContainsKey "/admin/apps", "the default read did not spill a 300 KB body; the probe cannot see spills")
    }

[<Fact>]
let ``a 1 MB logo is read into memory, with no temp file, and saved`` () =
    task {
        let log, appTask = withSpillProbe ()
        use! app = appTask
        let! operator, idA, _ = prepare app
        let upload = incompressiblePng 512 256
        Assert.InRange(upload.Length, 500_000, PngLogo.maxBytes)

        let! response = postLogo operator idA (Some("logo.png", upload))

        Assert.Equal(cardRedirect "app-logo-saved" idA, location response)
        Assert.False(log.Spills.ContainsKey $"/admin/apps/{idA}/branding/logo", "the logo upload spilled to a temp file")
        let! stored = storedLogo app idA
        Assert.True(stored.IsSome)
    }

[<Fact>]
let ``a 2.5 MB body is read and judged by the validator, and a 3.5 MB body is refused as too large`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app

        // 2.5 MB of PNG-shaped data: inside the 3 MB request limit, so the form
        // is read in full and `PngLogo` sees it. It is over the 1.25 MB logo
        // limit, so the notice is `too-large` and carries the measured size.
        let under = Array.append signature (Array.zeroCreate (2_621_440 - signature.Length))
        let! accepted = postLogo operator idA (Some("logo.png", under))
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode)
        Assert.Equal(cardRedirect "app-logo-too-large" idA, location accepted)
        let! sized = operator.GetHtml(location accepted)
        Assert.Contains(Texts.en.AppLogoTooLarge 2_621_440L, decode sized)

        // 3.5 MB: past the request limit. The form cannot be read, which is a
        // redirect to the same notice, without a size because none was measured.
        // Never a 413, and never a 500.
        let over = Array.append signature (Array.zeroCreate (3_670_016 - signature.Length))
        let! refused = postLogo operator idA (Some("logo.png", over))
        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode)
        Assert.Equal(cardRedirect "app-logo-too-large" idA, location refused)
        let! plain = operator.GetHtml(location refused)
        Assert.Contains(Texts.en.Notice "app-logo-too-large", decode plain)

        let! rows = scalar app "SELECT count(*) FROM app_branding" []
        Assert.Equal("0", rows)
    }

/// What Kestrel does with `MaxRequestBodySize` and TestServer does not: a body
/// stream that throws `BadHttpRequestException` (413) once more than the limit
/// has been read, and a feature that endpoint metadata can set the limit on.
/// Installed as a startup filter, so it is in place before routing. The default
/// is Kestrel's own, 30 MB.
type private SimulatedKestrel() =
    member val Limit = 30_000_000L with get, set
    member val LimitSeenAtEndpoint = 0L with get, set

type private LimitedBody(inner: Stream, limit: unit -> int64) =
    inherit Stream()
    let mutable total = 0L

    override _.CanRead = true
    override _.CanSeek = false
    override _.CanWrite = false
    override _.Length = raise (NotSupportedException())
    override _.Position with get () = raise (NotSupportedException()) and set _ = raise (NotSupportedException())
    override _.Flush() = ()
    override _.Seek(_, _) = raise (NotSupportedException())
    override _.SetLength _ = raise (NotSupportedException())
    override _.Write(_, _, _) = raise (NotSupportedException())

    override _.Read(buffer: byte[], offset: int, count: int) =
        let read = inner.Read(buffer, offset, count)
        total <- total + int64 read
        if total > limit () then raise (BadHttpRequestException("Request body too large.", 413))
        read

    override _.ReadAsync(buffer: Memory<byte>, ct: Threading.CancellationToken) =
        task {
            let! read = inner.ReadAsync(buffer, ct)
            total <- total + int64 read
            if total > limit () then raise (BadHttpRequestException("Request body too large.", 413))
            return read
        }
        |> ValueTask<int>

type private MaxBodyFeature(state: SimulatedKestrel) =
    interface Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature with
        member _.IsReadOnly = false

        member _.MaxRequestBodySize
            with get () = Nullable state.Limit
            and set (value: Nullable<int64>) =
                state.Limit <- value.GetValueOrDefault()
                state.LimitSeenAtEndpoint <- value.GetValueOrDefault()

type private KestrelFilter(state: SimulatedKestrel) =
    interface IStartupFilter with
        member _.Configure(next) =
            Action<IApplicationBuilder>(fun app ->
                app.Use(fun (ctx: HttpContext) (following: RequestDelegate) ->
                    ctx.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>(MaxBodyFeature state)
                    ctx.Request.Body <- new LimitedBody(ctx.Request.Body, fun () -> state.Limit)
                    following.Invoke ctx)
                |> ignore

                next.Invoke app)

[<Fact>]
let ``a body past Kestrel's request limit is the too-large notice, not a bare 413`` () =
    task {
        // In production the 413 comes from Kestrel when the endpoint's limit
        // (3 MB, set by the endpoint's metadata before the handler runs) is
        // passed. TestServer has no such limit, so it is simulated: a body
        // stream that throws what Kestrel throws, and a feature the metadata
        // is applied to.
        let state = SimulatedKestrel()

        use! app =
            Fixtures.createAppWith fakeSettings (fun builder ->
                builder.Services.AddSingleton<IStartupFilter>(KestrelFilter state) |> ignore)

        let! operator, idA, _ = prepare app

        let over = Array.append signature (Array.zeroCreate (3_200_000 - signature.Length))
        let! response = postLogo operator idA (Some("logo.png", over))

        // The metadata reached the feature: the limit in force at the endpoint is 3 MB.
        Assert.Equal(3_145_728L, state.LimitSeenAtEndpoint)

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Equal(cardRedirect "app-logo-too-large" idA, location response)
        let! rows = scalar app "SELECT count(*) FROM app_branding" []
        Assert.Equal("0", rows)

        // And a body under it still goes through to the validator.
        let under = Array.append signature (Array.zeroCreate (2_000_000 - signature.Length))
        let! second = postLogo operator idA (Some("logo.png", under))
        Assert.Equal(cardRedirect "app-logo-too-large" idA, location second)
        let! sized = operator.GetHtml(location second)
        Assert.Contains(Texts.en.AppLogoTooLarge 2_000_000L, decode sized)
    }

// ---------------------------------------------------------------------------
// Access
// ---------------------------------------------------------------------------

[<Fact>]
let ``without an operator session the logo route redirects to sign-in and stores nothing`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, idA, _ = prepare app
        let visitor = Browser app.Client

        use content = new MultipartFormDataContent()
        content.Add(new ByteArrayContent(pngOf 64 64), "logo", "logo.png")
        use message = new HttpRequestMessage(HttpMethod.Post, $"/admin/apps/{idA}/branding/logo")
        message.Content <- content
        let! response = visitor.Send message

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.StartsWith("/admin/signin", location response)
        let! rows = scalar app "SELECT count(*) FROM app_branding" []
        Assert.Equal("0", rows)
    }

[<Fact>]
let ``without an antiforgery token the logo route is a 400 and stores nothing`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app

        use content = new MultipartFormDataContent()
        content.Add(new ByteArrayContent(pngOf 64 64), "logo", "logo.png")
        use message = new HttpRequestMessage(HttpMethod.Post, $"/admin/apps/{idA}/branding/logo")
        message.Content <- content
        let! response = operator.Send message

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        let! rows = scalar app "SELECT count(*) FROM app_branding" []
        Assert.Equal("0", rows)
    }

// ===========================================================================
// Replace, remove and clear (User Story 3)
// ===========================================================================

let private postRemoveLogo (operator: Browser) (clientId: string) =
    operator.PostForm("/admin/apps", $"/admin/apps/{clientId}/branding/logo/delete", [])

let private rowCount (app: Fixtures.TestApp) =
    task {
        let! n = scalar app "SELECT count(*) FROM app_branding" []
        return int n
    }

let private getStatus (app: Fixtures.TestApp) (url: string) =
    task {
        let! response = app.Client.GetAsync url
        return response.StatusCode
    }

let private pickerOf (app: Fixtures.TestApp) (clientId: string) =
    (Browser app.Client).GetHtml("/signin?returnUrl=" + Uri.EscapeDataString(Journeys.authorizeUrl clientId "openid"))

[<Fact>]
let ``replacing a logo serves the new one, retires the old address, and changes the sign-in image`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app
        let first = pngOf 100 100
        let second = pngOf 120 90

        let! _ = postLogo operator idA (Some("logo.png", first))
        let! pickerBefore = pickerOf app idA
        Assert.Contains(logoAddress first, pickerBefore)

        let! replaced = postLogo operator idA (Some("logo.png", second))
        Assert.Equal(cardRedirect "app-logo-saved" idA, location replaced)

        // The new address serves the new logo.
        let expected = (match PngLogo.validate second with Ok l -> l | Error e -> failwith $"{e}")
        let! served = app.Client.GetAsync(logoAddress second)
        let! servedBytes = served.Content.ReadAsByteArrayAsync()
        Assert.Equal(HttpStatusCode.OK, served.StatusCode)
        Assert.Equal<byte array>(expected.Png, servedBytes)

        // The old one is gone, and nothing keeps its bytes: one row, one logo.
        let! oldStatus = getStatus app (logoAddress first)
        Assert.Equal(HttpStatusCode.NotFound, oldStatus)
        let! rows = rowCount app
        Assert.Equal(1, rows)

        // The sign-in page and the card point at the new one.
        let! pickerAfter = pickerOf app idA
        Assert.Contains(logoAddress second, pickerAfter)
        Assert.DoesNotContain(logoAddress first, pickerAfter)
        let! card = operator.GetHtml "/admin/apps"
        Assert.Contains(logoAddress second, card)
        Assert.DoesNotContain(logoAddress first, card)

        // Each save is a change, so each is logged.
        let! events = eventCount app idA "branding_logo_set"
        Assert.Equal(2, events)
    }

[<Fact>]
let ``removing a logo retires its address and keeps the accent and the row`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app
        let upload = pngOf 100 100
        let! _ = postAccent operator idA "#f26b1d"
        let! _ = postLogo operator idA (Some("logo.png", upload))

        let! response = postRemoveLogo operator idA

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Equal(cardRedirect "app-logo-removed" idA, location response)

        let! events = eventCount app idA "branding_logo_removed"
        Assert.Equal(1, events)
        let! status = getStatus app (logoAddress upload)
        Assert.Equal(HttpStatusCode.NotFound, status)

        // The accent is still there, so the row is.
        let! accent = storedAccent app idA
        Assert.Equal("f26b1d", accent)
        let! stored = storedLogo app idA
        Assert.True(stored.IsNone)
        let! rows = rowCount app
        Assert.Equal(1, rows)

        // And the card says what happened.
        let! card = operator.GetHtml(location response)
        Assert.Contains("data-notice=\"app-logo-removed\"", card)
    }

[<Fact>]
let ``removing the only thing an app has deletes its row`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app
        let! _ = postLogo operator idA (Some("logo.png", pngOf 100 100))

        let! response = postRemoveLogo operator idA

        Assert.Equal(cardRedirect "app-logo-removed" idA, location response)
        let! rows = rowCount app
        Assert.Equal(0, rows)
    }

[<Fact>]
let ``removing a logo that is not there gives the notice and no event`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app
        let! _ = postLogo operator idA (Some("logo.png", pngOf 100 100))
        let! _ = postRemoveLogo operator idA
        let! before = allEvents app

        // The second remove: nothing to remove, the same notice, no new event.
        let! second = postRemoveLogo operator idA
        Assert.Equal(cardRedirect "app-logo-removed" idA, location second)
        let! after = allEvents app
        Assert.Equal(before, after)

        // An app that never had branding.
        let! third = postRemoveLogo operator idA
        Assert.Equal(cardRedirect "app-logo-removed" idA, location third)
        let! events = eventCount app idA "branding_logo_removed"
        Assert.Equal(1, events)
    }

[<Fact>]
let ``an unknown client is app-not-found for a removal`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, _ = prepare app
        let! response = postRemoveLogo operator "no-such-app"
        Assert.Equal("/admin/apps?msg=app-not-found", location response)
    }

[<Fact>]
let ``the remove form is on the card only when the app has a logo, and asks first`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, idB = prepare app
        let! _ = postLogo operator idA (Some("logo.png", pngOf 100 100))

        let! html = operator.GetHtml "/admin/apps"

        Assert.Contains($"action=\"/admin/apps/{idA}/branding/logo/delete\"", html)
        Assert.DoesNotContain($"/admin/apps/{idB}/branding/logo/delete", html)
        // The confirmation travels as data-confirm, like every destructive form here.
        let form = Regex.Match(html, "<form[^>]*logo/delete[^>]*>").Value
        Assert.Contains("data-confirm=", form)
        Assert.Contains(Texts.en.AppLogoRemoveConfirm "App A", decode form)
    }

[<Fact>]
let ``without an operator session the remove route redirects to sign-in and keeps the logo`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app
        let! _ = postLogo operator idA (Some("logo.png", pngOf 100 100))
        let visitor = Browser app.Client

        let! response = visitor.PostWithoutToken($"/admin/apps/{idA}/branding/logo/delete", [])

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.StartsWith("/admin/signin", location response)
        let! stored = storedLogo app idA
        Assert.True(stored.IsSome)
    }

[<Fact>]
let ``without an antiforgery token the remove route is a 400 and keeps the logo`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app
        let! _ = postLogo operator idA (Some("logo.png", pngOf 100 100))
        let! before = allEvents app

        let! response = operator.PostWithoutToken($"/admin/apps/{idA}/branding/logo/delete", [])

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        let! stored = storedLogo app idA
        Assert.True(stored.IsSome)
        let! after = allEvents app
        Assert.Equal(before, after)
    }

[<Fact>]
let ``an empty accent clears it, logs once, and deletes the row when no logo is left`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app
        let! _ = postAccent operator idA "#f26b1d"

        let! response = postAccent operator idA ""

        Assert.Equal(cardRedirect "app-accent-cleared" idA, location response)
        let! accent = storedAccent app idA
        Assert.Equal("", accent)
        let! rows = rowCount app
        Assert.Equal(0, rows)
        let! events = eventCount app idA "branding_accent_cleared"
        Assert.Equal(1, events)

        // The card shows no colour, and the notice.
        let! card = operator.GetHtml(location response)
        Assert.Contains("data-notice=\"app-accent-cleared\"", card)
        Assert.DoesNotContain("/branding/accent/", card)
        Assert.DoesNotContain("value=\"#f26b1d\"", card)
    }

[<Fact>]
let ``clearing the accent keeps the logo and its row`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app
        let upload = pngOf 100 100
        let! _ = postAccent operator idA "#f26b1d"
        let! _ = postLogo operator idA (Some("logo.png", upload))

        // Whitespace is empty too: the field is trimmed.
        let! response = postAccent operator idA "   "

        Assert.Equal(cardRedirect "app-accent-cleared" idA, location response)
        let! rows = rowCount app
        Assert.Equal(1, rows)
        let! stored = storedLogo app idA
        Assert.True(stored.IsSome)
        let! status = getStatus app (logoAddress upload)
        Assert.Equal(HttpStatusCode.OK, status)
    }

[<Fact>]
let ``clearing an accent that is not set gives the notice and no event`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = prepare app
        let! before = allEvents app

        let! response = postAccent operator idA ""

        Assert.Equal(cardRedirect "app-accent-cleared" idA, location response)
        let! after = allEvents app
        Assert.Equal(before, after)
        let! rows = rowCount app
        Assert.Equal(0, rows)

        // Nor a second clear after one that did something.
        let! _ = postAccent operator idA "#1f6fa8"
        let! _ = postAccent operator idA ""
        let! _ = postAccent operator idA ""
        let! events = eventCount app idA "branding_accent_cleared"
        Assert.Equal(1, events)
    }

[<Fact>]
let ``deleting an app removes its branding and its logo address, and logs only the deletion`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, idB = prepare app
        let upload = pngOf 100 100
        let! _ = postAccent operator idA "#f26b1d"
        let! _ = postLogo operator idA (Some("logo.png", upload))
        let! _ = postAccent operator idB "#1f6fa8"
        let! before = allEvents app

        let! response = operator.PostForm("/admin/apps", $"/admin/apps/{idA}/delete", [])
        Assert.Equal("/admin/apps?msg=app-deleted", location response)

        // Its row and its logo's address are gone; the other app's is not.
        let! stored = scalar app "SELECT count(*) FROM app_branding WHERE client_id = @c" [ "@c", idA ]
        Assert.Equal("0", stored)
        let! status = getStatus app (logoAddress upload)
        Assert.Equal(HttpStatusCode.NotFound, status)
        let! other = storedAccent app idB
        Assert.Equal("1f6fa8", other)

        // One event, the existing one: the branding is not logged separately.
        let! after = allEvents app
        Assert.Equal(before + 1, after)
        let! deleted = scalar app "SELECT count(*) FROM security_events WHERE event_type = 'app_deleted' AND client_id = @c" [ "@c", idA ]
        Assert.Equal("1", deleted)
    }
