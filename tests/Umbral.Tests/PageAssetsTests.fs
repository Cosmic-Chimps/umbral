/// What a page loads, and how: nothing from a third party, nothing inline, and
/// the Content-Security-Policy that makes the browser enforce it.
///
/// An identity provider's pages carry credentials, so every script, stylesheet
/// and image comes from Umbral's own origin (Views/Assets.fs) and no page holds
/// inline script, inline style or an inline event handler. The policy in
/// Layout.contentSecurityPolicy refuses all of that anyway — these tests are
/// what notices a view that would only break in a browser (a CDN link, an
/// `onclick`) before anyone opens one.
///
/// Same shape as MetaTests: the rule is a pure function over plain data, tested
/// against samples that MUST be flagged, so a checker that quietly stops
/// matching fails here instead of passing every page.
module Umbral.Tests.PageAssetsTests

open System
open System.Net
open System.Net.Http
open System.Text.RegularExpressions
open Microsoft.Extensions.DependencyInjection
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows
open Umbral.Views

// ---------------------------------------------------------------------------
// The rule
// ---------------------------------------------------------------------------

type private Tag =
    { Name: string
      Attributes: (string * string) list
      /// Where the tag ends in the document
      End: int }

/// An opening tag, with its attributes parsed so that a value such as
/// `data-confirm="Delete x? on y=z"` is never mistaken for an attribute name.
let private tagPattern =
    Regex(
        "<([a-zA-Z][a-zA-Z0-9-]*)((?:\\s+[^\\s\"'<>/=]+(?:\\s*=\\s*(?:\"[^\"]*\"|'[^']*'|[^\\s\"'>]+))?)*)\\s*/?>",
        RegexOptions.Compiled
    )

let private attributePattern =
    Regex(
        "([^\\s\"'<>/=]+)(?:\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s\"'>]+)))?",
        RegexOptions.Compiled
    )

let private tagsOf (html: string) : (int * Tag) list =
    [ for m in tagPattern.Matches html ->
          let attributes =
              [ for a in attributePattern.Matches m.Groups[2].Value ->
                    a.Groups[1].Value.ToLowerInvariant(),
                    (if a.Groups[2].Success then a.Groups[2].Value
                     elif a.Groups[3].Success then a.Groups[3].Value
                     else a.Groups[4].Value) ]

          m.Index,
          { Name = m.Groups[1].Value.ToLowerInvariant()
            Attributes = attributes
            End = m.Index + m.Length } ]

/// Same origin means a path (`/assets/x.css`), not a protocol-relative `//host`
/// or a `/\host`, which browsers read as one.
let private isLocal (url: string) =
    let u = url.Trim()

    (u.StartsWith "/" && not (u.StartsWith "//") && not (u.StartsWith "/\\"))
    || u.StartsWith("data:", StringComparison.OrdinalIgnoreCase)

let private loadsResource =
    set [ "script"; "link"; "img"; "iframe"; "source"; "embed"; "object" ]

/// Everything in `html` that a Content-Security-Policy of `'self'` would refuse
/// or that reaches outside this origin: an inline script, a script, stylesheet,
/// image, frame or embed that does not come from a same-origin path (or a
/// `data:` URI), a `<style>` element, a `style=` attribute and an `on…=` event
/// handler attribute. Navigation links (`<a href="https://…">`) are fine.
let externalOrInlineViolations (html: string) : string list =
    [ for position, tag in tagsOf html do
          if tag.Name = "style" then
              yield "a <style> element"

          if tag.Name = "script" then
              if not (tag.Attributes |> List.exists (fun (n, _) -> n = "src")) then
                  yield "a <script> with no src (inline)"
              else
                  let closing = html.IndexOf("</script>", tag.End, StringComparison.OrdinalIgnoreCase)
                  let body = if closing < 0 then html.Substring tag.End else html.Substring(tag.End, closing - tag.End)

                  if body.Trim() <> "" then
                      yield $"a <script src> with an inline body at {position}"

          for name, value in tag.Attributes do
              if name = "style" then
                  yield $"a style attribute on <{tag.Name}>"

              if name.Length > 2 && name.StartsWith "on" && name |> Seq.forall Char.IsAsciiLetterLower then
                  yield $"an inline event handler ({name}) on <{tag.Name}>"

              if loadsResource.Contains tag.Name && (name = "src" || name = "href") && not (isLocal value) then
                  yield $"<{tag.Name} {name}=\"{value}\"> reaches outside this origin" ]

/// The hrefs and srcs of the stylesheet and script tags, in document order.
let private stylesheetsAndScripts (html: string) : string list =
    [ for _, tag in tagsOf html do
          let get name = tag.Attributes |> List.tryFind (fun (n, _) -> n = name) |> Option.map snd

          match tag.Name with
          | "link" when get "rel" = Some "stylesheet" -> yield! Option.toList (get "href")
          | "script" -> yield! Option.toList (get "src")
          | _ -> () ]

let private clean =
    """<!DOCTYPE html><html lang="en"><head><meta charset="UTF-8">
<link rel="icon" href="data:image/svg+xml;base64,AAAA" type="image/svg+xml">
<link rel="stylesheet" href="/assets/umbral.css?v=0123456789ab">
<script src="/assets/htmx.min.js?v=0123456789ab"></script></head>
<body><a href="https://example.com/docs">docs</a><img src="/logo.png">
<form data-confirm="Delete x? onsubmit=y"><button class="um-btn">Go</button></form></body></html>"""

[<Fact>]
let ``a page that loads only its own assets and has nothing inline is clean`` () =
    Assert.Empty(externalOrInlineViolations clean)

/// Each sample is wrong in exactly one way; the checker has to see it.
let private samples =
    [ "a CDN script", """<script src="https://cdn.tailwindcss.com"></script>""", "reaches outside this origin"
      "a protocol-relative script", """<script src="//cdn.example.com/x.js"></script>""", "reaches outside this origin"
      "an inline script", "<script>alert(1)</script>", "inline"
      "an inline script after a good one", """<script src="/a.js"></script><script>x()</script>""", "inline"
      "a script with a src and a body", """<script src="/a.js">x()</script>""", "inline body"
      "a style element", "<style>body{color:red}</style>", "<style>"
      "a style attribute", """<p style="color:red">x</p>""", "style attribute"
      "an onclick attribute", """<button onclick="go()">x</button>""", "onclick"
      "an onsubmit attribute", """<form onsubmit="return confirm('x')"></form>""", "onsubmit"
      "an uppercase handler", """<body ONLOAD="x()"></body>""", "onload"
      "a font stylesheet", """<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Inter">""", "reaches outside this origin"
      "a preconnect", """<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>""", "reaches outside this origin"
      "a protocol-relative image", """<img src="//cdn.example.com/a.png">""", "reaches outside this origin"
      "an iframe", """<iframe src="https://example.com"></iframe>""", "reaches outside this origin"
      "a single-quoted external src", "<script src='https://evil.example/x.js'></script>", "reaches outside this origin" ]

[<Fact>]
let ``the checker flags every kind of external or inline code`` () =
    for what, html, expected in samples do
        let violations = externalOrInlineViolations html

        Assert.True(
            violations |> List.exists (fun v -> v.Contains expected),
            $"{what} was not flagged as '{expected}'; got: {violations}"
        )

[<Fact>]
let ``the checker does not mistake attribute values for attributes`` () =
    Assert.Empty(externalOrInlineViolations """<form data-confirm="Delete? onsubmit=x style=y"></form>""")

[<Fact>]
let ``the content security policy allows nothing inline and nothing evaluated`` () =
    Assert.DoesNotContain("unsafe-inline", Layout.contentSecurityPolicy)
    Assert.DoesNotContain("unsafe-eval", Layout.contentSecurityPolicy)
    Assert.Contains("default-src 'self'", Layout.contentSecurityPolicy)

// ---------------------------------------------------------------------------
// Real pages
// ---------------------------------------------------------------------------

/// The page is a page of ours: no violations, the policy header, and exactly
/// the three assets Views/Assets.fs names.
let private assertSelfContained (name: string) (response: HttpResponseMessage) =
    task {
        let! html = response.Content.ReadAsStringAsync()

        Assert.True(html.Contains "<html", $"{name}: not an HTML page ({int response.StatusCode}): {html}")
        Assert.Empty(externalOrInlineViolations html)

        match response.Headers.TryGetValues "Content-Security-Policy" with
        | true, values -> Assert.Equal<string list>([ Layout.contentSecurityPolicy ], values |> Option.ofObj |> Option.defaultValue Seq.empty |> List.ofSeq)
        | _ -> Assert.Fail $"{name}: no Content-Security-Policy header"

        Assert.Equal<string list>([ Assets.css.Href; Assets.htmx.Href; Assets.script.Href ], stylesheetsAndScripts html)
        return html
    }

let private getPage (browser: Browser) (url: string) =
    task {
        let! response = browser.Get url
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        return! assertSelfContained url response
    }

[<Fact>]
let ``the setup page on a fresh instance is self-contained`` () =
    task {
        use! app = Fixtures.createApp ()
        let! _ = getPage (Browser app.Client) "/admin/setup"
        ()
    }

[<Fact>]
let ``the operator sign-in page is self-contained`` () =
    task {
        use! app = Fixtures.createApp ()
        do! claimOperator app (Browser app.Client)
        let! _ = getPage (Browser app.Client) "/admin/signin"
        ()
    }

let private seedDiscord (app: Fixtures.TestApp) =
    task {
        let! reference = Fixtures.putSecret app "discord" "discord-secret"

        do!
            ConnectorStore.save
                app.DbConfig
                app.Clock
                { Slug = "discord"
                  Kind = Discord
                  DisplayName = "Discord"
                  ClientId = "1234"
                  ClientSecretRef = reference.Encoded
                  ClientSecretHint = Secrets.hint "discord-secret"
                  IssuerUrl = None
                  AllowedEmailDomains = []
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None
    }

[<Fact>]
let ``every admin screen is self-contained, and its confirmations are data attributes`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app
        do! seedDiscord app

        let userId = Umbral.Domains.Ids.newSubject ()

        do!
            Users.insertForTesting
                app.DbConfig
                app.Clock
                { Id = userId
                  DisplayName = "Alex Doe"
                  Email = Some "alex@example.com"
                  EmailVerified = true
                  AvatarUrl = None
                  IsEnabled = true
                  FirstSeenUtc = app.Clock.GetUtcNow()
                  LastSeenUtc = app.Clock.GetUtcNow() }

        // Creating an app lands on the page that reveals its secret, once.
        let! created =
            operator.PostForm(
                "/admin/apps",
                "/admin/apps",
                [ "name", "Grafana"
                  "redirectUri", RedirectUri
                  "postLogoutRedirectUri", ""
                  "appType", "web" ]
            )

        let! reveal = operator.Get(location created)
        Assert.Equal(HttpStatusCode.OK, reveal.StatusCode)
        let! revealHtml = assertSelfContained "the secret reveal page" reveal
        Assert.Contains("revealed-secret", revealHtml)

        // The three screens with a destructive form on them carry its confirmation.
        for screen in [ "/admin/apps"; "/admin/connectors"; "/admin/users" ] do
            let! html = getPage operator screen
            Assert.Contains("data-confirm=\"", html)
            Assert.DoesNotContain("onsubmit", html)

        let! _ = getPage operator "/admin/credential"
        let! _ = getPage operator "/admin/users?q=alex"
        ()
    }

[<Fact>]
let ``the end-user picker and the fake provider page are self-contained`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app
        do! seedDiscord app
        let! clientId, _ = registerApp operator "Grafana"

        let person = Browser app.Client

        let authorizeUrl =
            "/connect/authorize?client_id=" + Uri.EscapeDataString clientId
            + "&response_type=code&redirect_uri=" + Uri.EscapeDataString RedirectUri
            + "&scope=openid&code_challenge=" + Challenge
            + "&code_challenge_method=S256&state=app-state"

        let! toPicker = person.Get authorizeUrl
        Assert.Equal(HttpStatusCode.Redirect, toPicker.StatusCode)
        let picker = location toPicker
        let! pickerHtml = getPage person picker
        Assert.Contains("/connect/fake/start", pickerHtml)

        let! toProvider = person.Get(picker.Replace("/signin", "/connect/fake/start"))
        let! _ = getPage person (location toProvider)
        ()
    }

[<Fact>]
let ``the message pages are self-contained`` () =
    task {
        use! app = Fixtures.createApp ()

        // A protocol error OpenIddict passes through to /connect/error.
        let! protocolError = app.Client.GetAsync "/connect/authorize?response_type=code"
        Assert.Equal(HttpStatusCode.BadRequest, protocolError.StatusCode)
        let! _ = assertSelfContained "the protocol error page" protocolError

        // A form posted without its antiforgery token: the expired-form page.
        use post = new HttpRequestMessage(HttpMethod.Post, "/admin/signin")
        post.Content <- new FormUrlEncodedContent(dict [ "credential", "wrong" ])
        let! expired = app.Client.SendAsync post
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode)
        let! _ = assertSelfContained "the expired-form page" expired

        // The rate-limit page: the 31st request is over the 30 a minute allowed.
        let mutable last = new HttpResponseMessage()

        for _ in 1..31 do
            use request = new HttpRequestMessage(HttpMethod.Get, "/connect/nope/start")
            request.Headers.Add("X-Forwarded-For", "203.0.113.50")
            let! response = app.Client.SendAsync request
            last <- response

        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode)
        let! _ = assertSelfContained "the rate-limit page" last
        ()
    }

// ---------------------------------------------------------------------------
// The assets themselves
// ---------------------------------------------------------------------------

[<Fact>]
let ``each asset is served from its versioned URL, immutable, and revalidates with a 304`` () =
    task {
        use! app = Fixtures.createApp ()

        // The three paths are the ones a page links to, and the browser has no session.
        Assert.Equal<string list>([ "/assets/umbral.css"; "/assets/umbral.js"; "/assets/htmx.min.js" ], Assets.all |> List.map _.Path)

        for asset in Assets.all do
            let! response = app.Client.GetAsync asset.Href
            Assert.Equal(HttpStatusCode.OK, response.StatusCode)
            Assert.Equal(asset.ContentType, string response.Content.Headers.ContentType)

            let cacheControl = string response.Headers.CacheControl
            Assert.Contains("max-age=31536000", cacheControl)
            Assert.Contains("immutable", cacheControl)
            Assert.Contains("public", cacheControl)

            Assert.Equal(asset.ETag, string response.Headers.ETag)
            Assert.Equal<string list>([ "nosniff" ], List.ofSeq (response.Headers.GetValues "X-Content-Type-Options"))

            let! body = response.Content.ReadAsByteArrayAsync()
            Assert.Equal<byte array>(asset.Bytes, body)

            use revalidation = new HttpRequestMessage(HttpMethod.Get, asset.Href)
            revalidation.Headers.TryAddWithoutValidation("If-None-Match", asset.ETag) |> ignore
            let! notModified = app.Client.SendAsync revalidation
            Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode)

            use stale = new HttpRequestMessage(HttpMethod.Get, asset.Href)
            stale.Headers.TryAddWithoutValidation("If-None-Match", "\"something-else\"") |> ignore
            let! changed = app.Client.SendAsync stale
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode)
    }

[<Fact>]
let ``an asset's URL changes when its content does`` () =
    for asset in Assets.all do
        Assert.StartsWith(asset.Path + "?v=", asset.Href)
        Assert.Equal(asset.Path.Length + 3 + 12, asset.Href.Length)

    Assert.Equal(Assets.all.Length, (Assets.all |> List.map _.Href |> List.distinct).Length)

[<Fact>]
let ``the stylesheet imports nothing and fetches nothing from elsewhere`` () =
    let css = Text.Encoding.UTF8.GetString Assets.css.Bytes

    Assert.DoesNotContain("@import", css)
    Assert.False(Regex.IsMatch(css, "url\\(\\s*[\"']?\\s*(https?:|//)", RegexOptions.IgnoreCase), "umbral.css fetches a URL from another origin")
    // Guards the checks above: the file is the compiled stylesheet, not an empty stub.
    Assert.Contains(".um-btn-primary", css)
    Assert.Contains("--color-primary", css)

// ---------------------------------------------------------------------------
// The accent is recolourable (spec 006, research R4)
// ---------------------------------------------------------------------------

[<Fact>]
let ``the stylesheet defines the accent as channel variables with Umbral's own colours`` () =
    let css = Text.Encoding.UTF8.GetString Assets.css.Bytes

    // Minified, so no space after the colon. An unbranded page computes the same
    // colours as before the accent became a variable (SC-001).
    Assert.Contains("--color-primary-rgb:95 179 232", css)
    Assert.Contains("--color-on-primary-rgb:7 38 59", css)

[<Fact>]
let ``the primary button reads both accent variables`` () =
    let css = Text.Encoding.UTF8.GetString Assets.css.Bytes
    let rule = Regex.Match(css, "\\.um-btn-primary\\{[^}]*\\}")

    Assert.True(rule.Success, "the stylesheet has no .um-btn-primary rule")
    Assert.Contains("var(--color-primary-rgb)", rule.Value)
    Assert.Contains("var(--color-on-primary-rgb)", rule.Value)

[<Fact>]
let ``no accent use hard-codes the colour, so every one follows a [data-accent] override`` () =
    let css = Text.Encoding.UTF8.GetString Assets.css.Bytes

    // The one place the value may appear is the variable's own definition. Both
    // spellings: the space-separated channels and the comma form a minifier
    // writes for `rgb(95 179 232 / 0.3)`.
    let withoutDefinition = css.Replace("--color-primary-rgb:95 179 232", "")

    Assert.DoesNotContain("95 179 232", withoutDefinition)
    Assert.DoesNotContain("95,179,232", withoutDefinition)
    Assert.DoesNotContain("#5fb3e8", withoutDefinition)

[<Fact>]
let ``the hidden attribute always hides, whatever display a class sets`` () =
    let css = Text.Encoding.UTF8.GetString Assets.css.Bytes

    // Tailwind's own [hidden] rule is :where()-wrapped, so `.um-btn` (inline-flex)
    // beat it and the logo form's "Use whole image" showed before a file was
    // chosen (spec 006 browser pass). The base layer's rule must win.
    Assert.Contains("[hidden]{display:none!important}", css)
