/// The Connectors screen as a list and a detail pane (the master-detail contract):
/// what each URL shows, which entry is selected, where every POST lands, and
/// that the list stays a list.
///
/// Asserts on URLs, `data-*` hooks and ARIA state, never on the wording of a text.
/// Page titles are built from the same `Texts` the screen uses.
module Umbral.Tests.ConnectorsMasterDetailTests

open System
open System.Net
open System.Text.RegularExpressions
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows
open Umbral.Tests.Infrastructure.PasskeyFlows

/// An instance with the operator signed in and the fake connector (shared).
let private operatorApp () =
    task {
        let! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app
        return app, operator
    }

/// What the add form posts for a fake connector.
let private addFields (slug: string) (shared: bool) =
    [ "kind", "fake"
      "slug", slug
      "displayName", "Fake " + slug
      "clientId", "client-" + slug
      "clientSecret", "secret-" + slug ]
    @ (if shared then [ "shared", "true" ] else [])

let private add (operator: Browser) (slug: string) (shared: bool) =
    operator.PostForm("/admin/connectors/new", "/admin/connectors", addFields slug shared)

/// A connector's own form, as its pane posts it.
let private paneFields (displayName: string) (clientId: string) =
    [ "displayName", displayName; "clientId", clientId; "shared", "true" ]

let private titleOf (html: string) =
    WebUtility.HtmlDecode(Regex.Match(html, "<title>(.*?)</title>").Groups[1].Value)

let private texts () = Umbral.Views.Texts.current ()

/// Everything between the opening of the list column and its closing `</nav>`.
let private listColumn (html: string) =
    let m = Regex.Match(html, "<nav[^>]*data-frame-list[^>]*>.*?</nav>", RegexOptions.Singleline)
    Assert.True(m.Success, "no list column")
    m.Value

/// The opening tags of the list's entries, the pinned domains entry included.
let private entryTags (html: string) =
    [ for m in Regex.Matches(listColumn html, "<a\\b[^>]*(?:data-connector-entry|data-domains-entry)[^>]*>") -> m.Value ]

let private entryName (tag: string) =
    let m = Regex.Match(tag, "data-connector-entry=\"([^\"]*)\"")
    if m.Success then m.Groups[1].Value else "domains"

/// The entries of the list that carry `aria-current="page"` (`domains` for the pinned one).
let private selectedEntries (html: string) =
    [ for tag in entryTags html do
          if tag.Contains "aria-current=\"page\"" then
              yield entryName tag ]

/// The slugs of the entries, in the order of the list; `domains` is the pinned one.
let private entryOrder (html: string) = entryTags html |> List.map entryName

let private entryTag (html: string) (slug: string) =
    entryTags html |> List.find (fun tag -> entryName tag = slug)

let private connectorPanes (html: string) =
    [ for m in Regex.Matches(html, "<section[^>]*data-connector=\"([^\"]*)\"") -> m.Groups[1].Value ]

/// The detail pane: from its `<section` to its `</section>`.
let private pane (html: string) =
    let m = Regex.Match(html, "<section[^>]*um-frame-pane[^>]*>.*?</section>", RegexOptions.Singleline)
    Assert.True(m.Success, "no pane")
    m.Value

let private addButton (html: string) =
    let m = Regex.Match(html, "<a\\b[^>]*href=\"/admin/connectors/new\"[^>]*data-frame-nav[^>]*>|<a\\b[^>]*data-frame-nav[^>]*href=\"/admin/connectors/new\"[^>]*>")
    Assert.True(m.Success, "no add button")
    m.Value

let private pathOf (target: string) = Uri(Uri "http://localhost", target).AbsolutePath

/// Where a redirect lands, checked to carry no fragment and no `app` hint.
let private landing (response: Net.Http.HttpResponseMessage) =
    let target = location response
    Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
    Assert.DoesNotContain("#", target)
    Assert.DoesNotContain("&app=", target)
    pathOf target

// ---------------------------------------------------------------------------
// Routes
// ---------------------------------------------------------------------------

[<Fact>]
let ``a configured provider's own URL is a page with that connector's pane`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! _ = add operator "fake-two" true

        let! response = operator.Get "/admin/connectors/fake-two"
        let! html = response.Content.ReadAsStringAsync()

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        Assert.Equal<string list>([ "fake-two" ], connectorPanes html)
        Assert.DoesNotContain("data-connector=\"fake\"", html)
        Assert.Contains("id=\"connector-heading\"", html)
        Assert.Contains("data-pane-heading", html)
        Assert.Contains("data-connector-actions", html)
        Assert.Equal($"Fake fake-two · {(texts ()).ConnectorsTitle} | Umbral", titleOf html)

        let! list = operator.GetHtml "/admin/connectors"
        Assert.Equal($"{(texts ()).ConnectorsTitle} | Umbral", titleOf list)
    }

[<Fact>]
let ``a kind not set up has its set-up form on its own URL, posting to the kind, with no closing row`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! response = operator.Get "/admin/connectors/github"
        let! html = response.Content.ReadAsStringAsync()

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        Assert.Equal<string list>([ "github" ], connectorPanes html)
        Assert.Contains("data-connector-kind=\"github\"", html)
        Assert.Matches("<form[^>]*action=\"/admin/connectors/github\"", pane html)
        Assert.DoesNotContain("data-connector-actions", html)
        Assert.Contains("data-entry-muted", entryTag html "github")
        Assert.Equal<string list>([ "github" ], selectedEntries html)
        Assert.Equal($"{ProviderKind.GitHub.DisplayName} · {(texts ()).ConnectorsTitle} | Umbral", titleOf html)
    }

[<Fact>]
let ``Passkey has a pane before and after it is set up`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! before = operator.GetHtml "/admin/connectors/passkey"
        Assert.Equal<string list>([ "passkey" ], connectorPanes before)
        Assert.Contains("data-connector-kind=\"passkey\"", before)
        Assert.Equal<string list>([ "passkey" ], selectedEntries before)
        Assert.Contains("data-entry-muted", entryTag before "passkey")
        Assert.Matches("<form[^>]*action=\"/admin/connectors/passkey\"", pane before)

        do! enablePasskeyConnector operator "invitations"

        let! after = operator.GetHtml "/admin/connectors/passkey"
        Assert.Equal<string list>([ "passkey" ], connectorPanes after)
        Assert.Contains("data-connector-state=\"enabled\"", after)
        Assert.Equal<string list>([ "passkey" ], selectedEntries after)
        Assert.DoesNotContain("data-entry-muted", entryTag after "passkey")
    }

[<Fact>]
let ``the add URL is a page with the add form in the pane, and the add button is the current one`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! response = operator.Get "/admin/connectors/new"
        let! html = response.Content.ReadAsStringAsync()

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        Assert.Contains("data-add-connector", pane html)
        Assert.Empty(connectorPanes html)
        Assert.Empty(selectedEntries html)
        Assert.Contains("aria-current=\"page\"", addButton html)
        Assert.Equal($"{(texts ()).ConnectorAddTitle} | Umbral", titleOf html)

        // On any other URL the button is a plain link.
        let! other = operator.GetHtml "/admin/connectors/fake"
        Assert.DoesNotContain("aria-current", addButton other)
    }

[<Fact>]
let ``the domains URL is a page with the instance list in the pane and its pinned entry selected`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! response = operator.Get "/admin/connectors/domains"
        let! html = response.Content.ReadAsStringAsync()

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        Assert.Contains("data-card=\"allowed-domains\"", pane html)
        Assert.Matches("<form[^>]*action=\"/admin/allowed-domains\"", pane html)
        Assert.Empty(connectorPanes html)
        Assert.Equal<string list>([ "domains" ], selectedEntries html)
        Assert.Equal($"{(texts ()).AllowedDomainsTitle} · {(texts ()).ConnectorsTitle} | Umbral", titleOf html)
    }

[<Fact>]
let ``an unknown slug is a 404 with a page notice, no pane and no selected entry`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! response = operator.Get "/admin/connectors/no-such-connector"
        let! html = response.Content.ReadAsStringAsync()

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)
        Assert.Contains("data-notice=\"connector-not-found\"", html)
        Assert.DoesNotContain("data-connector=", html)
        Assert.Empty(selectedEntries html)
        Assert.Empty(entryTags html |> List.filter (fun tag -> tag.Contains "aria-current"))
    }

// ---------------------------------------------------------------------------
// The selected entry, the order and the shape of the list
// ---------------------------------------------------------------------------

[<Fact>]
let ``exactly one entry is current on a detail URL, and it is that slug's`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! _ = add operator "fake-two" true

        for slug in [ "fake"; "fake-two"; "github"; "passkey" ] do
            let! html = operator.GetHtml $"/admin/connectors/{slug}"
            Assert.Equal<string list>([ slug ], selectedEntries html)
    }

[<Fact>]
let ``the list URL selects the first entry and shows its pane alone`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! _ = add operator "fake-two" true

        let! html = operator.GetHtml "/admin/connectors"
        let first = entryOrder html |> List.head

        Assert.Equal<string list>([ first ], selectedEntries html)
        Assert.Equal<string list>([ first ], connectorPanes html)
    }

[<Fact>]
let ``the list holds the configured connectors, then the kinds not set up, then passkey, then the domains entry after a hairline`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! _ = add operator "fake-two" true

        let! html = operator.GetHtml "/admin/connectors?msg=connector-saved&slug=fake"
        let order = entryOrder html

        Assert.Equal<string list>([ "fake"; "fake-two"; "discord"; "github"; "google"; "fediverse"; "passkey"; "domains" ], order)

        // The configured ones are not muted; the kinds not set up are, and so is passkey.
        for slug in [ "fake"; "fake-two" ] do
            Assert.DoesNotContain("data-entry-muted", entryTag html slug)

        for slug in [ "discord"; "github"; "google"; "passkey" ] do
            Assert.Contains("data-entry-muted", entryTag html slug)

        // A hairline sits between the entries and the pinned one.
        let column = listColumn html
        let hairline = column.IndexOf "role=\"presentation\""
        Assert.True(hairline > column.IndexOf "data-connector-entry=\"passkey\"")
        Assert.True(hairline < column.IndexOf "data-domains-entry")

        // The list is a list: no form, no input, no notice. The notice of the
        // address is on the page, outside it.
        Assert.DoesNotContain("<form", column)
        Assert.DoesNotContain("<input", column)
        Assert.DoesNotContain("data-notice", column)
        Assert.Contains("data-notice=\"connector-saved\"", html)
    }

// ---------------------------------------------------------------------------
// Redirects
// ---------------------------------------------------------------------------

[<Fact>]
let ``creating a connector lands on its own URL, and a refused create on the add URL`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! created = add operator "fake-two" true
        Assert.Equal("/admin/connectors/fake-two", landing created)
        Assert.Contains("connector-added", location created)

        let! refused = add operator "Not A Slug" true
        Assert.Equal("/admin/connectors/new", landing refused)

        let! blank = operator.PostForm("/admin/connectors/new", "/admin/connectors", addFields "fake-three" true |> List.filter (fun (name, _) -> name <> "clientSecret"))
        Assert.Equal("/admin/connectors/new", landing blank)

        let! unknownKind = operator.PostForm("/admin/connectors/new", "/admin/connectors", [ "kind", "nope"; "slug", "x" ])
        Assert.Equal("/admin/connectors/new", landing unknownKind)
    }

[<Fact>]
let ``saving a connector lands on its own URL`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! _ = add operator "fake-two" true

        let! saved = operator.PostForm("/admin/connectors/fake-two", "/admin/connectors/fake-two", paneFields "Renamed" "client-renamed")
        Assert.Equal("/admin/connectors/fake-two", landing saved)
        Assert.Contains("connector-saved", location saved)
    }

[<Fact>]
let ``toggling a connector lands on its own URL, and deleting one on the list`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! _ = add operator "fake-two" true

        let! toggled = operator.PostForm("/admin/connectors/fake-two", "/admin/connectors/fake-two/toggle", [])
        Assert.Equal("/admin/connectors/fake-two", landing toggled)

        let! deleted = operator.PostForm("/admin/connectors/fake-two", "/admin/connectors/fake-two/delete", [])
        Assert.Equal("/admin/connectors", landing deleted)
        Assert.Contains("connector-deleted", location deleted)

        let! stored = ConnectorStore.findBySlug app.DbConfig "fake-two"
        Assert.True stored.IsNone
    }

[<Fact>]
let ``saving, toggling or deleting an unknown connector lands on the list`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! saved = operator.PostForm("/admin/connectors", "/admin/connectors/made-up", paneFields "X" "x")
        Assert.Equal("/admin/connectors", landing saved)
        Assert.Contains("connector-unknown", location saved)

        let! toggled = operator.PostForm("/admin/connectors", "/admin/connectors/made-up/toggle", [])
        Assert.Equal("/admin/connectors", landing toggled)
        Assert.Contains("connector-unknown", location toggled)

        let! deleted = operator.PostForm("/admin/connectors", "/admin/connectors/made-up/delete", [])
        Assert.Equal("/admin/connectors", landing deleted)
        Assert.Contains("connector-unknown", location deleted)
    }

[<Fact>]
let ``the passkey save, invite and revoke land on the passkey URL`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! saved = operator.PostForm("/admin/connectors/passkey", "/admin/connectors/passkey", [ "shared", "true"; "enrolment", "invitations" ])
        Assert.Equal("/admin/connectors/passkey", landing saved)

        let! invited = operator.PostForm("/admin/connectors/passkey", "/admin/connectors/passkey/invitations", [ "note", "Ana" ])
        Assert.Equal("/admin/connectors/passkey", landing invited)

        let! html = operator.GetHtml "/admin/connectors/passkey"
        let id = Regex.Match(html, "data-invitation=\"([^\"]+)\"").Groups[1].Value
        Assert.True(id <> "")

        let! revoked = operator.PostForm("/admin/connectors/passkey", $"/admin/connectors/passkey/invitations/{id}/revoke", [])
        Assert.Equal("/admin/connectors/passkey", landing revoked)
    }

[<Fact>]
let ``saving the allowed domains lands on the domains URL`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! saved = operator.PostForm("/admin/connectors/domains", "/admin/allowed-domains", [ "allowedDomains", "acme.com" ])
        Assert.Equal("/admin/connectors/domains", landing saved)
    }

[<Fact>]
let ``a notice after a save is inside the pane, not at the top of the page`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! _ = add operator "fake-two" true

        let! saved = operator.PostForm("/admin/connectors/fake-two", "/admin/connectors/fake-two", paneFields "Renamed" "client-renamed")
        let! html = operator.GetHtml(location saved)

        Assert.Equal(1, Regex.Matches(html, "data-notice=\"connector-saved\"").Count)
        Assert.Contains("data-notice=\"connector-saved\"", pane html)

        // Nothing of it before the pane opens.
        let before = html.Substring(0, html.IndexOf "<section")
        Assert.DoesNotContain("data-notice", before)
    }

// ---------------------------------------------------------------------------
// Reserved slugs
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("new")>]
[<InlineData("domains")>]
let ``the slugs of the screen's own addresses are refused and nothing is stored`` (slug: string) =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! refused = add operator slug true

        Assert.Equal("/admin/connectors/new", landing refused)
        Assert.Contains("connector-slug-taken", location refused)

        let! stored = ConnectorStore.findBySlug app.DbConfig slug
        Assert.True stored.IsNone
    }

// ---------------------------------------------------------------------------
// Attention, the toggle and the frame
// ---------------------------------------------------------------------------

[<Fact>]
let ``a connector offered nowhere has the attention marker and no state badge, an ordinary one the reverse`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        // Not shared, and no app has chosen it.
        let! _ = add operator "fake-two" false

        let! html = operator.GetHtml "/admin/connectors/fake-two"

        let attention = entryTag html "fake-two"
        let attentionEntry = Regex.Match(listColumn html, "<a\\b[^>]*data-connector-entry=\"fake-two\".*?</a>", RegexOptions.Singleline).Value
        Assert.Contains("data-attention", attention)
        Assert.DoesNotContain("um-pill", attentionEntry)
        Assert.Contains("role=\"img\"", attentionEntry)
        Assert.Contains("data-offered-nowhere", pane html)

        let ordinary = entryTag html "fake"
        let ordinaryEntry = Regex.Match(listColumn html, "<a\\b[^>]*data-connector-entry=\"fake\".*?</a>", RegexOptions.Singleline).Value
        Assert.DoesNotContain("data-attention", ordinary)
        Assert.Contains("um-pill", ordinaryEntry)

        let! fake = operator.GetHtml "/admin/connectors/fake"
        Assert.DoesNotContain("data-offered-nowhere", pane fake)
    }

[<Fact>]
let ``the toggle swaps the frame with htmx, and without htmx it still lands on the detail URL and flips the state`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! _ = add operator "fake-two" true

        let! html = operator.GetHtml "/admin/connectors/fake-two"
        let toggle = Regex.Match(html, "<button[^>]*hx-post=\"/admin/connectors/fake-two/toggle\"[^>]*>").Value
        Assert.True(toggle <> "", "no toggle button with hx-post")
        Assert.Contains("hx-target=\"[data-frame]\"", toggle)
        Assert.Contains("hx-select=\"[data-frame]\"", toggle)
        Assert.Contains("hx-swap=\"outerHTML\"", toggle)

        // A plain POST with the antiforgery token, as a browser without script sends it.
        let! before = ConnectorStore.findBySlug app.DbConfig "fake-two"
        Assert.True before.Value.IsEnabled

        let! toggled = operator.PostForm("/admin/connectors/fake-two", "/admin/connectors/fake-two/toggle", [])
        Assert.Equal("/admin/connectors/fake-two", landing toggled)

        let! after = ConnectorStore.findBySlug app.DbConfig "fake-two"
        Assert.False after.Value.IsEnabled
    }

[<Fact>]
let ``entries, the pinned entry and the add button swap the frame with htmx, and the page has one frame`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! _ = add operator "fake-two" true

        let! html = operator.GetHtml "/admin/connectors/fake"

        Assert.Equal(1, Regex.Matches(html, "data-frame=\"\"").Count)

        let links = entryTags html @ [ addButton html ]

        // Two entries, the three kinds not set up, fediverse, passkey, the pinned entry, the add button.
        Assert.Equal(9, links.Length)

        for link in links do
            let href = Regex.Match(link, "\\shref=\"([^\"]*)\"").Groups[1].Value
            Assert.Contains($"hx-get=\"{href}\"", link)
            Assert.Contains("hx-target=\"[data-frame]\"", link)
            Assert.Contains("hx-select=\"[data-frame]\"", link)
            Assert.Contains("hx-swap=\"outerHTML\"", link)
            Assert.Contains("hx-push-url=\"true\"", link)
    }
