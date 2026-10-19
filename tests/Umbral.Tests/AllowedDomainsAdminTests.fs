/// Allowed email domains on the admin screens (specs/006, User Stories 2 and 4):
/// the instance card on the Connectors screen and its removal warning, a
/// connector's own list on the add form and on its card, the rule label of each
/// card, and the Apps screen's warning about a mix of limited and open
/// connectors.
///
/// Asserts on the `data-*` markers of contracts/admin-surface.md and on the
/// stored lists and events. The only wording checked is what the contract fixes
/// (the "none" of the help, the OpenID Connect note) and that an operator's own
/// entry is named in the message and encoded.
module Umbral.Tests.AllowedDomainsAdminTests

open System
open System.Net
open System.Text.RegularExpressions
open System.Threading.Tasks
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

// ---------------------------------------------------------------------------
// Setup and reading
// ---------------------------------------------------------------------------

/// A stored Discord connector, written straight to the store with a real secret.
let private seedDiscord (app: Fixtures.TestApp) (enabled: bool) (allowed: string list) =
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
                  AllowedEmailDomains = allowed
                  PasskeyEnrolment = InvitationsOnly
                  IsEnabled = enabled
                  IsShared = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None
    }

/// An instance with the operator signed in and the fake connector (open, shared).
let private operatorApp () =
    task {
        let! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app
        return app, operator
    }

let private setConnectorList (app: Fixtures.TestApp) (slug: string) (list: string list) =
    task {
        let! stored = ConnectorStore.findBySlug app.DbConfig slug
        do! ConnectorStore.save app.DbConfig app.Clock { stored.Value with AllowedEmailDomains = list } None
    }

let private connectorList (app: Fixtures.TestApp) (slug: string) =
    task {
        let! stored = ConnectorStore.findBySlug app.DbConfig slug
        return stored.Value.AllowedEmailDomains
    }

/// Every `allowed_domains_changed` event as (actor, connector slug, detail).
let private domainEvents (app: Fixtures.TestApp) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()

        command.CommandText <-
            "SELECT actor, ifnull(connector_slug, ''), ifnull(detail, '')
             FROM security_events WHERE event_type = 'allowed_domains_changed' ORDER BY id"

        use! reader = command.ExecuteReaderAsync()
        let rows = ResizeArray()

        while reader.Read() do
            rows.Add((reader.GetString 0, reader.GetString 1, reader.GetString 2))

        return List.ofSeq rows
    }

/// The instance card of the Connectors screen: from its marker to the end of
/// its section.
let private instanceCard (html: string) =
    let start = html.IndexOf "data-card=\"allowed-domains\""
    Assert.True(start >= 0, "the Connectors screen has no instance card")
    html.Substring(start, html.IndexOf("</section>", start) - start)

/// One connector's card: from its marker to the next card or the end of the page.
let private cardOf (html: string) (slug: string) =
    let start = html.IndexOf $"data-connector=\"{slug}\""
    Assert.True(start >= 0, $"no card for {slug}")
    let next = html.IndexOf("<article", start)
    let finish = if next < 0 then html.IndexOf("<section", start) else next
    if finish < 0 then html.Substring start else html.Substring(start, finish - start)

/// What the first `allowedDomains` input of a fragment holds, decoded.
let private fieldValue (fragment: string) =
    let m = Regex.Match(fragment, "name=\"allowedDomains\"[^>]*value=\"([^\"]*)\"")
    Assert.True(m.Success, "no allowedDomains input")
    WebUtility.HtmlDecode m.Groups[1].Value

/// The decoded text of the alert that follows the element carrying `marker`.
let private textAfter (html: string) (marker: string) =
    let start = html.IndexOf marker
    Assert.True(start >= 0, $"no {marker}")
    let inner = html.IndexOf('>', start) + 1
    let finish = html.IndexOf("</div></div>", inner)
    let fragment = html.Substring(inner, (if finish < 0 then html.Length else finish) - inner)
    let stripped = Regex.Replace(fragment, "<[^>]*>", "")
    (WebUtility.HtmlDecode stripped |> string).Trim()

let private ruleOf (card: string) =
    Regex.Match(card, "data-rule=\"([^\"]*)\"").Groups[1].Value

let private saveInstance (operator: Browser) (query: string) (typed: string) =
    operator.PostForm("/admin/connectors", "/admin/allowed-domains" + query, [ "allowedDomains", typed ])

/// A card's own save: what a card posts, with the list as typed.
let private saveCard (operator: Browser) (slug: string) (fields: (string * string) list) =
    operator.PostForm("/admin/connectors", $"/admin/connectors/{slug}", fields)

let private cardFields (displayName: string) (clientId: string) (typed: string) =
    [ "displayName", displayName
      "clientId", clientId
      "shared", "true"
      "allowedDomains", typed ]

let private addFields (slug: string) (typed: string) =
    [ "kind", "github"
      "slug", slug
      "displayName", "GitHub for Acme"
      "clientId", "acme-client"
      "clientSecret", "acme-client-secret"
      "shared", "true"
      "allowedDomains", typed ]

// ---------------------------------------------------------------------------
// The instance card (T018)
// ---------------------------------------------------------------------------

[<Fact>]
let ``the instance card shows the stored list in its Unicode form`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! empty = operator.GetHtml "/admin/connectors"
        Assert.Equal("", fieldValue (instanceCard empty))

        do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "xn--mnchen-3ya.de"; "acme.com" ]

        let! screen = operator.GetHtml "/admin/connectors"
        Assert.Equal("münchen.de, acme.com", fieldValue (instanceCard screen))
        Assert.Contains("name=\"allowedDomains\"", instanceCard screen)
        Assert.Contains("action=\"/admin/allowed-domains\"", instanceCard screen)
    }

[<Fact>]
let ``saving a valid list stores it, shows the notice and records one event without a slug`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! saved = saveInstance operator "" "ACME.com, @eu.acme.com."
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode)
        Assert.Contains("allowed-domains-saved", location saved)

        let! stored = EmailDomainRules.instanceList app.DbConfig
        Assert.Equal<string list>([ "acme.com"; "eu.acme.com" ], stored)

        let! screen = operator.GetHtml(location saved)
        Assert.Contains("data-notice=\"allowed-domains-saved\"", screen)
        Assert.Equal("acme.com, eu.acme.com", fieldValue (instanceCard screen))

        let! events = domainEvents app
        Assert.Equal<(string * string * string) list>([ "operator", "", "count=2" ], events)
    }

[<Fact>]
let ``saving the same list again records no second event`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! _ = saveInstance operator "" "acme.com"
        // Spelled differently, the same list once normalised.
        let! again = saveInstance operator "" "@ACME.com."
        Assert.Contains("allowed-domains-saved", location again)

        let! events = domainEvents app
        Assert.Equal(1, events.Length)
    }

[<Theory>]
[<InlineData("*.acme.com")>]
[<InlineData("localhost")>]
let ``a wildcard or a non-domain saves nothing, names the entry and keeps what was typed`` (entry: string) =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "acme.com" ]

        let typed = $"example.org, {entry}"
        let! refused = saveInstance operator "" typed
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode)

        let! page = refused.Content.ReadAsStringAsync()
        let card = instanceCard page
        Assert.Contains($"'{entry}'", WebUtility.HtmlDecode card)
        Assert.Equal(typed, fieldValue card)

        let! stored = EmailDomainRules.instanceList app.DbConfig
        Assert.Equal<string list>([ "acme.com" ], stored)
        let! events = domainEvents app
        Assert.Empty events
    }

[<Fact>]
let ``the operator's own entry is encoded in the message`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! refused = saveInstance operator "" "<b>x</b>.com"
        let! page = refused.Content.ReadAsStringAsync()

        Assert.DoesNotContain("<b>x</b>", page)
        Assert.Contains("&lt;b&gt;x&lt;/b&gt;.com", page)
    }

[<Fact>]
let ``emptying the list while connectors have none of their own warns, names them and stores nothing`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        // One open connector that is switched off still counts: it can be turned on.
        do! seedDiscord app false []
        do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "acme.com" ]

        let! warned = saveInstance operator "" ""
        Assert.Equal(HttpStatusCode.Redirect, warned.StatusCode)
        Assert.Contains("allowed-domains-removal", location warned)

        let! stillThere = EmailDomainRules.instanceList app.DbConfig
        Assert.Equal<string list>([ "acme.com" ], stillThere)

        let! screen = operator.GetHtml(location warned)
        Assert.Contains("data-notice=\"allowed-domains-removal\"", screen)
        Assert.Contains("data-opened-connector=\"fake\"", screen)
        Assert.Contains("data-opened-connector=\"discord\"", screen)
        Assert.Contains("action=\"/admin/allowed-domains?confirm=yes\"", screen)

        let! events = domainEvents app
        Assert.Empty events

        // The button posts the empty list with the confirmation.
        let! confirmed = saveInstance operator "?confirm=yes" ""
        Assert.Contains("allowed-domains-saved", location confirmed)

        let! cleared = EmailDomainRules.instanceList app.DbConfig
        Assert.Empty cleared

        let! after = domainEvents app
        Assert.Equal<(string * string * string) list>([ "operator", "", "count=0" ], after)
    }

[<Fact>]
let ``the warning names only the connectors with no list of their own`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        do! seedDiscord app true [ "example.org" ]
        do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "acme.com" ]

        let! warned = saveInstance operator "" ""
        let! screen = operator.GetHtml(location warned)

        Assert.Contains("data-opened-connector=\"fake\"", screen)
        Assert.DoesNotContain("data-opened-connector=\"discord\"", screen)
    }

[<Fact>]
let ``emptying the list when every connector has its own list needs no warning`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        do! setConnectorList app "fake" [ "acme.com" ]
        do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "example.org" ]

        let! saved = saveInstance operator "" ""
        Assert.Contains("allowed-domains-saved", location saved)

        let! cleared = EmailDomainRules.instanceList app.DbConfig
        Assert.Empty cleared
        let! events = domainEvents app
        Assert.Equal(1, events.Length)
    }

[<Fact>]
let ``emptying a list that is already empty warns nothing and records nothing`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! saved = saveInstance operator "" ""
        Assert.Contains("allowed-domains-saved", location saved)
        let! events = domainEvents app
        Assert.Empty events
    }

[<Fact>]
let ``the instance list needs an antiforgery token and the operator`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "acme.com" ]

        let! forged = operator.PostWithoutToken("/admin/allowed-domains", [ "allowedDomains", "evil.example" ])
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode)

        let stranger = Browser app.Client
        let! turnedAway = stranger.PostWithoutToken("/admin/allowed-domains", [ "allowedDomains", "evil.example" ])
        Assert.Equal(HttpStatusCode.Redirect, turnedAway.StatusCode)
        Assert.StartsWith("/admin/signin", location turnedAway)

        let! stored = EmailDomainRules.instanceList app.DbConfig
        Assert.Equal<string list>([ "acme.com" ], stored)
    }

// ---------------------------------------------------------------------------
// A connector's own list (T021)
// ---------------------------------------------------------------------------

[<Fact>]
let ``the add form and every card carry the field, the help and the one-app note`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! none = operator.GetHtml "/admin/connectors"

        // The add form, which serves every kind (OpenID Connect included).
        let addForm = none.Substring(none.IndexOf "data-add-connector-section")
        Assert.Contains("name=\"allowedDomains\"", addForm)
        Assert.Contains("data-note=\"limit-one-app\"", addForm)

        // Each card: the field, the help saying there is no instance list, the note.
        for slug in [ "discord"; "github"; "google"; "fake" ] do
            let card = cardOf none slug
            Assert.Contains("name=\"allowedDomains\"", card)
            Assert.Contains("data-note=\"limit-one-app\"", card)
            Assert.Contains("none: anyone can sign in", card)

        // With an instance list the help names it, in Unicode.
        do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "xn--mnchen-3ya.de" ]
        let! named = operator.GetHtml "/admin/connectors"
        let card = WebUtility.HtmlDecode(cardOf named "github")
        Assert.Contains("münchen.de", card)
        Assert.DoesNotContain("none: anyone can sign in", card)
    }

[<Fact>]
let ``an OpenID Connect card carries the one-connector-per-issuer note instead`` () =
    task {
        let issuer = "https://auth.example.test/application/o/umbral"
        let stub = StubOidcProvider.create issuer
        use! app = Fixtures.createAppWithProviderHandler stub fakeSettings ignore
        stub.Now <- fun () -> app.Clock.GetUtcNow()
        let operator = Browser app.Client
        do! claimOperator app operator

        let! added =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors",
                [ "kind", "oidc"
                  "slug", "home"
                  "displayName", "Home"
                  "issuer", issuer
                  "clientId", "umbral-client"
                  "clientSecret", "the-provider-client-secret"
                  "shared", "true"
                  "allowedDomains", "Acme.com" ]
            )

        Assert.Contains("connector-added-oidc", location added)
        Assert.Equal<string list>([ "acme.com" ], (ConnectorStore.findBySlug app.DbConfig "home").Result.Value.AllowedEmailDomains)

        let! events = domainEvents app
        Assert.Equal<(string * string * string) list>([ "operator", "home", "count=1" ], events)

        let! screen = operator.GetHtml "/admin/connectors"
        let card = cardOf screen "home"
        Assert.Contains("data-note=\"limit-one-app\"", card)
        Assert.Contains("one connector per issuer", card)

        // Built-in kinds keep the general recipe.
        Assert.DoesNotContain("one connector per issuer", cardOf screen "github")
        Assert.Equal("acme.com", fieldValue card)
    }

[<Fact>]
let ``adding a connector with a list stores it and records the event after the creation event`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! added = operator.PostForm("/admin/connectors", "/admin/connectors", addFields "github-acme" "acme.com, eu.acme.com")
        Assert.Contains("connector-added", location added)

        let! list = connectorList app "github-acme"
        Assert.Equal<string list>([ "acme.com"; "eu.acme.com" ], list)

        let! events = domainEvents app
        Assert.Equal<(string * string * string) list>([ "operator", "github-acme", "count=2" ], events)

        let! order =
            Flows.scalar
                app
                "SELECT group_concat(event_type, ',') FROM
                    (SELECT event_type FROM security_events
                     WHERE connector_slug = 'github-acme' ORDER BY id)"
                []

        Assert.Equal("connector_created,allowed_domains_changed", order)
    }

[<Fact>]
let ``adding a connector with no list records no domain event`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", addFields "github-acme" "")
        let! list = connectorList app "github-acme"
        Assert.Empty list
        let! events = domainEvents app
        Assert.Empty events
    }

[<Fact>]
let ``an invalid entry on the add form creates nothing and keeps what was typed`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! refused = operator.PostForm("/admin/connectors", "/admin/connectors", addFields "github-acme" "*.acme.com")
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode)

        let! page = refused.Content.ReadAsStringAsync()
        Assert.Contains("data-add-error=\"allowedDomains\"", page)
        Assert.Contains("'*.acme.com'", WebUtility.HtmlDecode page)
        Assert.Contains("value=\"github-acme\"", page)
        // The secret is never drawn again.
        Assert.DoesNotContain("acme-client-secret", page)

        let! stored = ConnectorStore.findBySlug app.DbConfig "github-acme"
        Assert.True stored.IsNone
    }

[<Fact>]
let ``saving a card with a list stores it, records the event with the slug and an unchanged save does not`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app

        let! saved = saveCard operator "fake" (cardFields "Fake" "fake" "Acme.com")
        Assert.Contains("connector-saved", location saved)

        let! list = connectorList app "fake"
        Assert.Equal<string list>([ "acme.com" ], list)

        let! _ = saveCard operator "fake" (cardFields "Fake" "fake" "@acme.com")
        let! events = domainEvents app
        Assert.Equal<(string * string * string) list>([ "operator", "fake", "count=1" ], events)
    }

[<Fact>]
let ``an invalid entry on a card saves nothing and the card keeps what was typed`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        do! setConnectorList app "fake" [ "acme.com" ]

        let! refused = saveCard operator "fake" (cardFields "Renamed" "fake" "example.org, a*b.com")
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode)

        let! page = refused.Content.ReadAsStringAsync()
        let card = cardOf page "fake"
        Assert.Equal("example.org, a*b.com", fieldValue card)
        Assert.Contains("'a*b.com'", WebUtility.HtmlDecode card)

        // Nothing else of the post was saved either.
        let! stored = ConnectorStore.findBySlug app.DbConfig "fake"
        Assert.Equal<string list>([ "acme.com" ], stored.Value.AllowedEmailDomains)
        Assert.Equal("Fake", stored.Value.DisplayName)
    }

[<Fact>]
let ``a card post without the field keeps the list and an empty field clears it`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        do! setConnectorList app "fake" [ "acme.com" ]

        // What the unshare confirmation posts: no `allowedDomains`, no `shared`.
        let! kept = saveCard operator "fake" [ "displayName", "Fake"; "clientId", "fake" ]
        Assert.Contains("connector-saved", location kept)
        let! afterKeep = connectorList app "fake"
        Assert.Equal<string list>([ "acme.com" ], afterKeep)
        let! eventsAfterKeep = domainEvents app
        Assert.Empty eventsAfterKeep

        let! cleared = saveCard operator "fake" (cardFields "Fake" "fake" "")
        Assert.Contains("connector-saved", location cleared)
        let! afterClear = connectorList app "fake"
        Assert.Empty afterClear

        let! events = domainEvents app
        Assert.Equal<(string * string * string) list>([ "operator", "fake", "count=0" ], events)
    }

[<Fact>]
let ``each card says which rule applies to it`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        do! seedDiscord app true [ "acme.com" ]

        let! open' = operator.GetHtml "/admin/connectors"
        Assert.Equal("connector", ruleOf (cardOf open' "discord"))
        Assert.Equal("anyone", ruleOf (cardOf open' "fake"))
        Assert.Contains("data-rule=\"anyone\"", cardOf open' "github")

        do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "example.org" ]
        let! limited = operator.GetHtml "/admin/connectors"

        // Its own list replaces the instance's; the others fall back to it.
        Assert.Equal("connector", ruleOf (cardOf limited "discord"))
        Assert.Equal("instance", ruleOf (cardOf limited "fake"))
        Assert.Contains("acme.com", cardOf limited "discord")
        Assert.Contains("example.org", cardOf limited "fake")
    }

// ---------------------------------------------------------------------------
// The Apps screen (T021, T024)
// ---------------------------------------------------------------------------

let private rowOf (html: string) (clientId: string) =
    let start = html.IndexOf $"data-app=\"{clientId}\""
    Assert.True(start >= 0, $"no row for app {clientId}")
    let next = html.IndexOf("<article", start)
    if next < 0 then html.Substring start else html.Substring(start, next - start)

let private warningMarker = "data-warning=\"unrestricted-connectors\""

/// The fake connector (open) and Discord limited to acme.com, both offered to a
/// registered app.
let private mixedApp () =
    task {
        let! app, operator = operatorApp ()
        do! seedDiscord app true [ "acme.com" ]
        let! clientId, _ = registerApp operator "Photos"
        return app, operator, clientId
    }

let private chooseAll (operator: Browser) (clientId: string) =
    operator.PostForm("/admin/apps", $"/admin/apps/{clientId}/connectors", [ "connectorChoice", "all-shared" ])

let private chooseOnly (operator: Browser) (clientId: string) (slugs: string list) =
    operator.PostForm(
        "/admin/apps",
        $"/admin/apps/{clientId}/connectors",
        ("connectorChoice", "chosen") :: (slugs |> List.map (fun s -> "connectors", s))
    )

[<Fact>]
let ``an app offering a limited and an open connector shows the warning naming the open one`` () =
    task {
        let! app, operator, clientId = mixedApp ()
        use _ = app

        let! screen = operator.GetHtml "/admin/apps"
        let row = rowOf screen clientId
        Assert.Contains(warningMarker, row)

        let text = textAfter row warningMarker
        Assert.Contains("Fake", text)
        Assert.DoesNotContain("Discord", text)
    }

[<Fact>]
let ``saving that choice succeeds and shows the same text as a warning notice`` () =
    task {
        let! app, operator, clientId = mixedApp ()
        use _ = app

        let! saved = chooseAll operator clientId
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode)
        Assert.Contains("app-connectors-unrestricted", location saved)

        let! screen = operator.GetHtml(location saved)
        Assert.Contains("data-notice=\"app-connectors-unrestricted\"", screen)
        let notice = textAfter screen "data-notice=\"app-connectors-unrestricted\""
        let row = textAfter (rowOf screen clientId) warningMarker
        Assert.Contains("Fake", notice)
        Assert.DoesNotContain("Discord", notice)
        // The notice says it saved, then carries the row's text.
        Assert.StartsWith("Sign-in methods saved.", notice)
        Assert.EndsWith(row, notice)

        // It is stored: the choice went through.
        let! choice = AppConnectors.choiceFor app.DbConfig clientId
        Assert.Equal(AllShared, choice)
    }

[<Fact>]
let ``no warning when every offered connector has a rule or none has`` () =
    task {
        let! app, operator, clientId = mixedApp ()
        use _ = app

        // Only the limited one is offered.
        let! saved = chooseOnly operator clientId [ "discord" ]
        Assert.Contains("app-connectors-saved", location saved)
        let! onlyLimited = operator.GetHtml "/admin/apps"
        Assert.DoesNotContain(warningMarker, onlyLimited)

        // Only the open one is offered: nothing to keep limited.
        let! _ = chooseOnly operator clientId [ "fake" ]
        let! onlyOpen = operator.GetHtml "/admin/apps"
        Assert.DoesNotContain(warningMarker, onlyOpen)

        // Both offered, but an instance list gives the open one a rule too.
        do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "example.org" ]
        let! both = chooseAll operator clientId
        Assert.Contains("app-connectors-saved", location both)
        let! screen = operator.GetHtml "/admin/apps"
        Assert.DoesNotContain(warningMarker, screen)
    }

[<Fact>]
let ``a connector that is switched off is not counted as unrestricted`` () =
    task {
        let! app, operator, clientId = mixedApp ()
        use _ = app

        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors/fake/toggle?confirm=yes", [])
        let! stored = ConnectorStore.findBySlug app.DbConfig "fake"
        Assert.False stored.Value.IsEnabled

        let! screen = operator.GetHtml "/admin/apps"
        Assert.DoesNotContain(warningMarker, rowOf screen clientId)
    }

[<Fact>]
let ``a session from an open connector at an app offering only the restricted one lands on its sign-in page`` () =
    task {
        // 003 FR-008: what a connector is allowed to admit is not the point
        // here, only that the app does not honour a session it does not offer.
        let! app, operator = operatorApp ()
        use _ = app
        do! seedDiscord app true [ "acme.com" ]

        let! everyone, _ = registerApp operator "Everyone"
        let! limited, _ = registerApp operator "Acme only"
        let! _ = chooseOnly operator limited [ "discord" ]

        let person = Browser app.Client
        let! first = signInViaFake person everyone "openid" "fake-user-1" "sam@example.com"
        Assert.True(Result.isOk first, "the sign-in through the open connector should complete")

        let authorize (clientId: string) =
            "/connect/authorize?client_id=" + Uri.EscapeDataString clientId
            + "&response_type=code&redirect_uri=" + Uri.EscapeDataString RedirectUri
            + "&scope=openid&code_challenge=" + Challenge
            + "&code_challenge_method=S256&state=app-state"

        let! toPicker = person.Get(authorize limited)
        Assert.Equal(HttpStatusCode.Redirect, toPicker.StatusCode)
        Assert.StartsWith("/signin?returnUrl=", location toPicker)
        Assert.DoesNotContain("code=", location toPicker)

        // And the app that offers the open connector still honours it.
        let! honoured = person.Get(authorize everyone)
        Assert.Contains("code=", location honoured)
    }
