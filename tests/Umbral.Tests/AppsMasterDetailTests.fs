/// The Apps screen as a list and a detail pane (the master-detail contract):
/// what each URL shows, which entry is selected, where every POST lands, and
/// that the list stays a list.
///
/// Asserts on URLs, `data-*` hooks and ARIA state, never on the wording of a text.
module Umbral.Tests.AppsMasterDetailTests

open System.Net
open System.Text.RegularExpressions
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

/// An instance with the operator signed in and the fake connector (shared).
let private operatorApp () =
    task {
        let! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app
        return app, operator
    }

/// Discord, limited to acme.com, so that the fake connector next to it is the open one.
let private seedLimitedDiscord (app: Fixtures.TestApp) =
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
                  AllowedEmailDomains = [ "acme.com" ]
                  PasskeyEnrolment = InvitationsOnly
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None
    }

let private detail (clientId: string) = $"/admin/apps/{clientId}"

/// Everything between the opening of the list column and its closing `</nav>`.
let private listColumn (html: string) =
    let m = Regex.Match(html, "<nav[^>]*data-frame-list[^>]*>.*?</nav>", RegexOptions.Singleline)
    Assert.True(m.Success, "no list column")
    m.Value

/// The entries of the list that carry `aria-current="page"`, as their client ids.
let private selectedEntries (html: string) =
    [ for m in Regex.Matches(listColumn html, "<a\\b[^>]*data-app-entry=\"([^\"]*)\"[^>]*>") do
          if m.Value.Contains "aria-current=\"page\"" then
              yield m.Groups[1].Value ]

let private appPanes (html: string) =
    [ for m in Regex.Matches(html, "data-app=\"([^\"]*)\"") -> m.Groups[1].Value ]

let private accentLinks (html: string) =
    [ for m in Regex.Matches(html, "<link[^>]*href=\"(/branding/accent/[^\"]*)\"") -> m.Groups[1].Value ]

let private noFragment (response: System.Net.Http.HttpResponseMessage) =
    let target = location response
    Assert.DoesNotContain("#", target)
    Assert.DoesNotContain("&app=", target)
    target

// ---------------------------------------------------------------------------
// Routes
// ---------------------------------------------------------------------------

[<Fact>]
let ``an app's own URL is a page with that app's pane`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! idA, _ = registerApp operator "App A"
        let! idB, _ = registerApp operator "App B"

        let! response = operator.Get(detail idB)
        let! html = response.Content.ReadAsStringAsync()

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        Assert.Equal<string list>([ idB ], appPanes html)
        Assert.DoesNotContain($"data-app=\"{idA}\"", html)
        Assert.Matches("<section[^>]*data-app=\"" + idB + "\"", html)
        Assert.Contains("id=\"app-heading\"", html)

        // The address is named after the app; the list and the create form keep theirs.
        let titleOf (page: string) =
            WebUtility.HtmlDecode(Regex.Match(page, "<title>(.*?)</title>").Groups[1].Value)

        Assert.Equal("App B · Apps | Umbral", titleOf html)
        let! list = operator.GetHtml "/admin/apps"
        Assert.Equal("Apps | Umbral", titleOf list)
        let! create = operator.GetHtml "/admin/apps/new"
        Assert.Equal("Create app | Umbral", titleOf create)
    }

[<Fact>]
let ``the create URL is a page with the form in the pane, and the Create link is the current one`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! _ = registerApp operator "App A"

        let! response = operator.Get "/admin/apps/new"
        let! html = response.Content.ReadAsStringAsync()

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        Assert.Contains("data-screen=\"app-new\"", html)
        Assert.Matches("<form[^>]*action=\"/admin/apps\"", html)
        Assert.Empty(appPanes html)
        Assert.Empty(selectedEntries html)
        Assert.Matches("<a\\b[^>]*href=\"/admin/apps/new\"[^>]*aria-current=\"page\"|<a\\b[^>]*aria-current=\"page\"[^>]*href=\"/admin/apps/new\"", html)
    }

[<Fact>]
let ``with no connector the create URL shows only the add a connector first prompt`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator

        let! html = operator.GetHtml "/admin/apps/new"

        Assert.DoesNotContain("name=\"redirectUri\"", html)
        Assert.Contains("href=\"/admin/connectors\"", html)
    }

[<Fact>]
let ``an unknown app is a 404 naming app-not-found, with no pane and no entry selected`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! idA, _ = registerApp operator "App A"

        let! response = operator.Get(detail "no-such-app")
        let! html = response.Content.ReadAsStringAsync()

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)
        Assert.Contains("data-notice=\"app-not-found\"", html)
        Assert.Empty(appPanes html)
        Assert.Empty(selectedEntries html)
        // The list is still there to go on from.
        Assert.Contains($"data-app-entry=\"{idA}\"", html)
    }

// ---------------------------------------------------------------------------
// Where every POST lands
// ---------------------------------------------------------------------------

[<Fact>]
let ``creating an app lands on its page and a failed create lands on the create page`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! created =
            operator.PostForm(
                "/admin/apps/new",
                "/admin/apps",
                [ "name", "Fresh"
                  "redirectUri", RedirectUri
                  "postLogoutRedirectUri", ""
                  "appType", "web" ]
            )

        let target = noFragment created
        Assert.Matches("^/admin/apps/[^/?]+\\?msg=app-created$", target)
        Assert.NotEqual<string>("/admin/apps/new?msg=app-created", target)

        let! refused =
            operator.PostForm(
                "/admin/apps/new",
                "/admin/apps",
                [ "name", "Broken"
                  "redirectUri", "not a uri"
                  "postLogoutRedirectUri", ""
                  "appType", "web" ]
            )

        Assert.Equal("/admin/apps/new?msg=app-redirect-uri-invalid", noFragment refused)

        // A refusal sits above the form it is about, as a warning, not as good news.
        let! refusedPage = operator.GetHtml "/admin/apps/new?msg=app-name-required"
        let notice = Regex.Match(refusedPage, "data-notice=\"app-name-required\".*?</div>", RegexOptions.Singleline)
        Assert.True(notice.Success, "no notice")
        Assert.Contains("warning", notice.Value)
        Assert.DoesNotContain("success", notice.Value)
    }

[<Fact>]
let ``every change to an app lands on that app's page and never carries a fragment or &app=`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        do! seedLimitedDiscord app
        let! id, _ = registerApp operator "Photos"
        let page = detail id

        let! saved =
            operator.PostForm(
                "/admin/apps",
                $"{page}/connectors",
                [ "connectorChoice", "chosen"; "connectors", "discord" ]
            )

        Assert.Equal($"{page}?msg=app-connectors-saved", noFragment saved)

        let! unrestricted = operator.PostForm("/admin/apps", $"{page}/connectors", [ "connectorChoice", "all-shared" ])
        Assert.Equal($"{page}?msg=app-connectors-unrestricted", noFragment unrestricted)

        let! strand = operator.PostForm("/admin/apps", $"{page}/connectors", [ "connectorChoice", "chosen" ])
        let target = noFragment strand
        Assert.StartsWith($"{page}?msg=app-connectors-strand", target)

        let! accent = operator.PostForm("/admin/apps", $"{page}/branding/accent", [ "accent", "#d9480f" ])
        Assert.Equal($"{page}?msg=app-accent-saved", noFragment accent)

        let! refusedAccent = operator.PostForm("/admin/apps", $"{page}/branding/accent", [ "accent", "orange" ])
        Assert.Equal($"{page}?msg=app-accent-invalid", noFragment refusedAccent)

        // No file at all is the logo's plainest refusal.
        let! token = operator.TokenFrom "/admin/apps"
        use content = new System.Net.Http.MultipartFormDataContent()
        content.Add(new System.Net.Http.StringContent(token), "__RequestVerificationToken")
        use message = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"{page}/branding/logo")
        message.Content <- content
        let! refusedLogo = operator.Send message
        Assert.Equal(HttpStatusCode.Redirect, refusedLogo.StatusCode)
        let logoTarget = noFragment refusedLogo
        Assert.StartsWith($"{page}?msg=app-logo-", logoTarget)

        let! removed = operator.PostForm("/admin/apps", $"{page}/branding/logo/delete", [])
        Assert.Equal($"{page}?msg=app-logo-removed", noFragment removed)

        let! regenerated = operator.PostForm("/admin/apps", $"{page}/secret", [])
        Assert.Equal($"{page}?msg=app-secret-regenerated", noFragment regenerated)
    }

[<Fact>]
let ``deleting an app and any change to an unknown app land on the list`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! id, _ = registerApp operator "Photos"

        let! unknown = operator.PostForm("/admin/apps", "/admin/apps/no-such-app/secret", [])
        Assert.Equal("/admin/apps?msg=app-not-found", noFragment unknown)

        let! unknownAccent = operator.PostForm("/admin/apps", "/admin/apps/no-such-app/branding/accent", [ "accent", "#d9480f" ])
        Assert.Equal("/admin/apps?msg=app-not-found", noFragment unknownAccent)

        let! deleted = operator.PostForm("/admin/apps", $"{detail id}/delete", [])
        Assert.Equal("/admin/apps?msg=app-deleted", noFragment deleted)

        // The notice is the page's, drawn at its top, and the app's URL is now unknown.
        let! html = operator.GetHtml "/admin/apps?msg=app-deleted"
        Assert.Contains("data-notice=\"app-deleted\"", html)
        let! gone = operator.Get(detail id)
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode)
    }

[<Fact>]
let ``an item notice is drawn inside the pane, not at the top of the page`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! id, _ = registerApp operator "Photos"

        let! html = operator.GetHtml $"{detail id}?msg=app-accent-saved"

        let pane = Regex.Match(html, "<section[^>]*data-app=.*?</section>", RegexOptions.Singleline)
        Assert.True(pane.Success, "no pane")
        Assert.Contains("data-notice=\"app-accent-saved\"", pane.Value)
        Assert.Equal(1, Regex.Matches(html, "data-notice=").Count)
    }

// ---------------------------------------------------------------------------
// Selection
// ---------------------------------------------------------------------------

[<Fact>]
let ``on an app's URL exactly its own entry is current`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! idA, _ = registerApp operator "App A"
        let! idB, _ = registerApp operator "App B"

        for id in [ idA; idB ] do
            let! html = operator.GetHtml(detail id)
            Assert.Equal<string list>([ id ], selectedEntries html)
            Assert.Equal(1, Regex.Matches(listColumn html, "aria-current=\"page\"").Count)
    }

[<Fact>]
let ``on the list URL the first app by name is selected and its pane is the only one`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        // Created in the other order, and with a lower-case first letter: the order is the name's, not the creation's.
        let! idB, _ = registerApp operator "b-app"
        let! idA, _ = registerApp operator "A-app"

        let! html = operator.GetHtml "/admin/apps"

        Assert.Equal<string list>([ idA ], selectedEntries html)
        Assert.Equal<string list>([ idA ], appPanes html)
        let entries = [ for m in Regex.Matches(html, "data-app-entry=\"([^\"]*)\"") -> m.Groups[1].Value ]
        Assert.Equal<string list>([ idA; idB ], entries)
    }

// ---------------------------------------------------------------------------
// The list is a list; the pane is one app
// ---------------------------------------------------------------------------

[<Fact>]
let ``the list column holds no forms, no inputs and no notices`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! idA, _ = registerApp operator "App A"
        let! _ = registerApp operator "App B"

        for url in [ "/admin/apps"; detail idA; "/admin/apps/new"; $"{detail idA}?msg=app-accent-saved"; "/admin/apps?msg=app-deleted" ] do
            let! html = operator.GetHtml url
            let list = listColumn html
            Assert.DoesNotContain("<form", list)
            Assert.DoesNotContain("<input", list)
            Assert.DoesNotContain("data-notice", list)
            Assert.DoesNotContain("data-copy", list)
    }

[<Fact>]
let ``a detail page shows one app: one data-app and at most the selected app's accent link`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! idA, _ = registerApp operator "App A"
        let! idB, _ = registerApp operator "App B"
        let! _ = operator.PostForm("/admin/apps", $"{detail idA}/branding/accent", [ "accent", "#d9480f" ])
        let! _ = operator.PostForm("/admin/apps", $"{detail idB}/branding/accent", [ "accent", "#1f6fa8" ])

        let! pageA = operator.GetHtml(detail idA)
        let! pageB = operator.GetHtml(detail idB)
        let! list = operator.GetHtml "/admin/apps"

        Assert.Equal(1, Regex.Matches(pageA, "data-app=\"").Count)
        Assert.Equal<string list>([ "/branding/accent/d9480f.css" ], accentLinks pageA)
        Assert.Equal(1, Regex.Matches(pageB, "data-app=\"").Count)
        Assert.Equal<string list>([ "/branding/accent/1f6fa8.css" ], accentLinks pageB)

        // The list URL shows the first app's pane, so that app's colour and no other.
        Assert.Equal<string list>([ "/branding/accent/d9480f.css" ], accentLinks list)
        // And it is in the pane, not the head.
        Assert.DoesNotContain("/branding/accent/", Regex.Match(pageA, "<head.*?</head>", RegexOptions.Singleline).Value)
    }

// ---------------------------------------------------------------------------
// The secret, once
// ---------------------------------------------------------------------------

[<Fact>]
let ``the secret is shown once on the new app's page and another app's page does not use it up`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! other, _ = registerApp operator "A-first"

        let! created =
            operator.PostForm(
                "/admin/apps/new",
                "/admin/apps",
                [ "name", "Fresh"
                  "redirectUri", RedirectUri
                  "postLogoutRedirectUri", ""
                  "appType", "web" ]
            )

        let fresh = location created

        // Another app's page, and the list (which shows the app sorted first, "A-first"), between the create and the reveal.
        let! otherHtml = operator.GetHtml(detail other)
        Assert.DoesNotContain("data-secret-once", otherHtml)
        let! listHtml = operator.GetHtml "/admin/apps"
        Assert.DoesNotContain("data-secret-once", listHtml)

        let! first = operator.GetHtml fresh
        Assert.Equal(1, Regex.Matches(first, "data-secret-once").Count)
        Assert.DoesNotContain("revealed-client-id", first)
        Assert.Contains("id=\"revealed-secret\"", first)

        let! second = operator.GetHtml fresh
        Assert.DoesNotContain("data-secret-once", second)
    }

// ---------------------------------------------------------------------------
// htmx
// ---------------------------------------------------------------------------

[<Fact>]
let ``entries and the Create link swap the frame with htmx and the page has one frame`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! idA, _ = registerApp operator "App A"
        let! _ = registerApp operator "App B"

        let! html = operator.GetHtml(detail idA)

        Assert.Equal(1, Regex.Matches(html, "data-frame=\"\"").Count)

        let links =
            [ for m in Regex.Matches(html, "<a\\b[^>]*>") do
                  if m.Value.Contains "data-app-entry=" || m.Value.Contains "href=\"/admin/apps/new\"" then
                      yield m.Value ]

        // Two entries and the Create link.
        Assert.Equal(3, links.Length)

        for link in links do
            let href = Regex.Match(link, "\\shref=\"([^\"]*)\"").Groups[1].Value
            Assert.Contains($"hx-get=\"{href}\"", link)
            Assert.Contains("hx-target=\"[data-frame]\"", link)
            Assert.Contains("hx-select=\"[data-frame]\"", link)
            Assert.Contains("hx-swap=\"outerHTML\"", link)
            Assert.Contains("hx-push-url=\"true\"", link)
    }

// ---------------------------------------------------------------------------
// The long redirect URI, and the warning marker
// ---------------------------------------------------------------------------

[<Fact>]
let ``a long redirect URI is in a list item that may break anywhere`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let uri = "https://app.example.com/" + String.replicate 200 "a" + "/callback"

        let! created =
            operator.PostForm(
                "/admin/apps/new",
                "/admin/apps",
                [ "name", "Long"
                  "redirectUri", uri
                  "postLogoutRedirectUri", ""
                  "appType", "web" ]
            )

        let! html = operator.GetHtml(location created |> fun l -> l.Substring(0, l.IndexOf '?'))

        let item = Regex.Match(html, "<li[^>]*>\\s*" + Regex.Escape uri)
        Assert.True(item.Success, "the redirect URI is not in a list item")
        Assert.Contains("[overflow-wrap:anywhere]", item.Value)
    }

[<Fact>]
let ``only an app that lets in an unrestricted connector has the warning marker in its entry`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        do! seedLimitedDiscord app
        let! mixed, _ = registerApp operator "Mixed"
        let! limited, _ = registerApp operator "Limited"

        // Mixed offers Discord (limited) and the fake (open); Limited offers only Discord.
        let! _ = operator.PostForm("/admin/apps", $"{detail limited}/connectors", [ "connectorChoice", "chosen"; "connectors", "discord" ])

        let! html = operator.GetHtml(detail mixed)

        let entry (clientId: string) =
            Regex.Match(listColumn html, "<a\\b[^>]*data-app-entry=\"" + clientId + "\".*?</a>", RegexOptions.Singleline).Value

        Assert.Contains("role=\"img\"", entry mixed)
        Assert.Contains("aria-label=", Regex.Match(entry mixed, "<span[^>]*role=\"img\"[^>]*>").Value)
        Assert.DoesNotContain("role=\"img\"", entry limited)
    }
