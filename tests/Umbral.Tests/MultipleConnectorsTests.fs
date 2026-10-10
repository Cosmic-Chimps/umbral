/// User Story 3 of feature 003: several connectors of one kind.
///
/// Asserts on `data-connector`, `data-connector-kind`, `data-connector-shared`,
/// `data-naming-advice`, `data-offered-nowhere`, `data-add-connector` and on
/// notice codes in redirects, never on the wording of any text, so a copy change
/// cannot break these. The `fake` kind stands in for Discord: it is the one kind
/// that needs no network, and the screen treats every kind alike.
module Umbral.Tests.MultipleConnectorsTests

open System
open System.Text.RegularExpressions
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

/// An instance with the operator signed in, the fake connector stored, and one
/// registered app ("Photos").
let private instance () =
    task {
        let! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app
        let! clientId, secret = registerApp operator "Photos"
        return app, operator, clientId, secret
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
    operator.PostForm("/admin/connectors", "/admin/connectors", addFields slug shared)

let private connectorsOn (html: string) =
    [ for m in Regex.Matches(html, "data-connector=\"([^\"]*)\"") -> m.Groups[1].Value ] |> List.sort

/// The part of the Connectors screen that belongs to one card: from its
/// `<article` to the next one.
let private cardOf (html: string) (slug: string) =
    let marker = $"data-connector=\"{slug}\""
    let at = html.IndexOf marker
    Assert.True(at >= 0, $"no card for connector {slug}")
    let start = html.LastIndexOf("<article", at)
    let next = html.IndexOf("<article", at)
    if next < 0 then html.Substring start else html.Substring(start, next - start)

let private pickerFor (clientId: string) =
    "/signin?returnUrl=" + Uri.EscapeDataString(Journeys.authorizeUrl clientId "openid")

let private chosen (slugs: string list) =
    ("connectorChoice", "chosen") :: (slugs |> List.map (fun slug -> "connectors", slug))

/// The card form of a connector, as the screen posts it.
let private cardFields (displayName: string) (clientId: string) (shared: bool) =
    [ "displayName", displayName; "clientId", clientId ]
    @ (if shared then [ "shared", "true" ] else [])

[<Fact>]
let ``a second connector of one kind is added through the add form and shows its own card`` () =
    task {
        let! app, operator, _, _ = instance ()
        use _ = app

        let! screen = operator.GetHtml "/admin/connectors"
        Assert.Contains("data-add-connector", screen)

        let! added = add operator "fake-two" true
        Assert.Contains("connector-added", location added)

        let! stored = ConnectorStore.findBySlug app.DbConfig "fake-two"
        Assert.True stored.IsSome
        Assert.Equal(ProviderKind.Fake, stored.Value.Kind)
        Assert.Equal("Fake fake-two", stored.Value.DisplayName)
        Assert.Equal("client-fake-two", stored.Value.ClientId)
        Assert.True stored.Value.IsEnabled
        Assert.True stored.Value.IsShared

        // The first connector is untouched by the second.
        let! first = ConnectorStore.findBySlug app.DbConfig "fake"
        Assert.Equal("fake", first.Value.ClientId)

        let! html = operator.GetHtml "/admin/connectors"
        let card = cardOf html "fake-two"
        Assert.Contains("data-connector-kind=\"fake\"", card)
        Assert.Contains("data-connector-shared=\"shared\"", card)
        // Its own callback address, built from its own slug.
        Assert.Contains("/connect/fake-two/callback", card)
        Assert.DoesNotContain("/connect/fake/callback", card)

        // The first card keeps its own.
        Assert.Contains("/connect/fake/callback", cardOf html "fake")

        let! events =
            scalar
                app
                "SELECT count(*) FROM security_events WHERE event_type = 'connector_created' AND connector_slug = @s"
                [ "@s", "fake-two" ]

        Assert.Equal("1", events)
    }

[<Fact>]
let ``the secret of an added connector is stored under its own slug and never replaces another's`` () =
    task {
        let! app, operator, _, _ = instance ()
        use _ = app

        let! _ = add operator "fake-two" true
        let! first = ConnectorStore.findBySlug app.DbConfig "fake"
        let! second = ConnectorStore.findBySlug app.DbConfig "fake-two"

        Assert.NotEqual<string>(first.Value.ClientSecretRef, second.Value.ClientSecretRef)
        Assert.Equal(Secrets.hint "secret-fake-two", second.Value.ClientSecretHint)
    }

[<Fact>]
let ``an invalid slug is refused and nothing is stored`` () =
    task {
        let! app, operator, _, _ = instance ()
        use _ = app

        for slug in [ "Bad Slug"; "UPPER"; "under_score"; "has.dot"; String('a', 33) ] do
            let! refused = add operator slug true
            Assert.Contains("connector-slug-invalid", location refused)

        // An empty slug is refused the same way.
        let! empty = operator.PostForm("/admin/connectors", "/admin/connectors", addFields "" true)
        Assert.Contains("connector-slug-invalid", location empty)

        let! all = ConnectorStore.listAll app.DbConfig
        Assert.Equal<string list>([ "fake" ], all |> List.map (fun c -> c.Slug))
    }

[<Fact>]
let ``a taken slug is refused and the connector that owns it is left alone`` () =
    task {
        let! app, operator, _, _ = instance ()
        use _ = app

        let! before = ConnectorStore.findBySlug app.DbConfig "fake"
        let! refused = add operator "fake" true
        Assert.Contains("connector-slug-taken", location refused)

        let! after = ConnectorStore.findBySlug app.DbConfig "fake"
        Assert.Equal(before.Value.ClientId, after.Value.ClientId)
        Assert.Equal(before.Value.ClientSecretRef, after.Value.ClientSecretRef)
        Assert.Equal(before.Value.ClientSecretHint, after.Value.ClientSecretHint)
    }

[<Fact>]
let ``a slug that is another kind's name is refused so the empty cards keep their meaning`` () =
    task {
        let! app, operator, _, _ = instance ()
        use _ = app

        // `discord` is where the empty Discord card posts. A fake connector
        // under that slug would be edited as a Discord one.
        let! refused = add operator "discord" true
        Assert.Contains("connector-slug-taken", location refused)

        let! stored = ConnectorStore.findBySlug app.DbConfig "discord"
        Assert.True stored.IsNone
    }

[<Fact>]
let ``the add form refuses a missing display name, client id or secret and an unknown kind`` () =
    task {
        let! app, operator, _, _ = instance ()
        use _ = app

        let without (name: string) = addFields "fake-two" true |> List.filter (fun (n, _) -> n <> name)

        let post fields =
            operator.PostForm("/admin/connectors", "/admin/connectors", fields)

        let! noName = post (without "displayName")
        Assert.Contains("connector-display-name-required", location noName)
        let! noClient = post (without "clientId")
        Assert.Contains("connector-client-id-required", location noClient)
        let! noSecret = post (without "clientSecret")
        Assert.Contains("connector-secret-required", location noSecret)
        let! noKind = post ([ "kind", "myspace" ] @ (without "kind"))
        Assert.Contains("connector-unknown", location noKind)

        let! stored = ConnectorStore.findBySlug app.DbConfig "fake-two"
        Assert.True stored.IsNone
    }

[<Fact>]
let ``the add form is not an antiforgery bypass`` () =
    task {
        let! app, operator, _, _ = instance ()
        use _ = app

        let! response = operator.PostWithoutToken("/admin/connectors", addFields "fake-two" true)
        Assert.NotEqual(System.Net.HttpStatusCode.Redirect, response.StatusCode)

        let! stored = ConnectorStore.findBySlug app.DbConfig "fake-two"
        Assert.True stored.IsNone
    }

[<Fact>]
let ``the fake kind cannot be added where the fake connector is not allowed`` () =
    task {
        // Production-shaped instance: no ALLOW_FAKE_CONNECTOR. A fake
        // connector stored here would stop the next start.
        use! app = Fixtures.createApp ()
        let operator = Browser app.Client
        do! claimOperator app operator

        let! refused = add operator "fake-two" true
        Assert.Contains("connector-unknown", location refused)

        let! stored = ConnectorStore.findBySlug app.DbConfig "fake-two"
        Assert.True stored.IsNone
    }

[<Fact>]
let ``every card carries the naming advice and the kind and shared markers`` () =
    task {
        let! app, operator, _, _ = instance ()
        use _ = app
        let! _ = add operator "fake-two" false

        let! html = operator.GetHtml "/admin/connectors"

        // Every stored connector (the fake one has a card now) and an empty
        // card for each kind with none.
        Assert.Equal<string list>([ "discord"; "fake"; "fake-two"; "github"; "google" ], connectorsOn html)

        for slug in connectorsOn html do
            Assert.Contains("data-naming-advice", cardOf html slug)
            Assert.Contains("data-connector-kind=", cardOf html slug)
            Assert.Contains("data-connector-shared=", cardOf html slug)

        Assert.Contains("data-connector-shared=\"shared\"", cardOf html "fake")
        Assert.Contains("data-connector-shared=\"not-shared\"", cardOf html "fake-two")
        Assert.Contains("data-connector-kind=\"github\"", cardOf html "github")
    }

[<Fact>]
let ``an empty card for a kind with no connector still posts to its kind and creates the first one`` () =
    task {
        let! app, operator, _, _ = instance ()
        use _ = app

        let! html = operator.GetHtml "/admin/connectors"
        Assert.Contains("action=\"/admin/connectors/discord\"", cardOf html "discord")
        Assert.Contains("action=\"/admin/connectors/github\"", cardOf html "github")

        // The first-run path: no add step, the slug is the kind's name.
        let! saved =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors/github",
                [ "displayName", "GitHub"; "clientId", "gh-1"; "clientSecret", "gh-secret"; "shared", "true" ]
            )

        Assert.Contains("connector-saved", location saved)

        let! stored = ConnectorStore.findBySlug app.DbConfig "github"
        Assert.Equal(ProviderKind.GitHub, stored.Value.Kind)
        Assert.Equal("gh-1", stored.Value.ClientId)
        Assert.True stored.Value.IsShared

        // A slug that is no kind's name cannot be created from a card.
        let! unknown =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors/made-up",
                [ "displayName", "X"; "clientId", "x"; "clientSecret", "x" ]
            )

        Assert.Contains("connector-unknown", location unknown)
    }

[<Fact>]
let ``saving a card edits the stored connector by its row and keeps its secret`` () =
    task {
        let! app, operator, _, _ = instance ()
        use _ = app
        let! _ = add operator "fake-two" true
        let! before = ConnectorStore.findBySlug app.DbConfig "fake-two"

        // No new secret: the stored one stays. The kind comes from the row, not
        // from the slug, which is not a kind's name.
        let! saved =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors/fake-two",
                cardFields "Second fake" "client-renamed" true
            )

        Assert.Contains("connector-saved", location saved)
        let! after = ConnectorStore.findBySlug app.DbConfig "fake-two"
        Assert.Equal(ProviderKind.Fake, after.Value.Kind)
        Assert.Equal("Second fake", after.Value.DisplayName)
        Assert.Equal("client-renamed", after.Value.ClientId)
        Assert.Equal(before.Value.ClientSecretRef, after.Value.ClientSecretRef)
        Assert.Equal(before.Value.ClientSecretHint, after.Value.ClientSecretHint)
        Assert.True after.Value.IsEnabled

        // A blank label is refused on the card as on the add form.
        let! blank =
            operator.PostForm("/admin/connectors", "/admin/connectors/fake-two", cardFields "" "client-renamed" true)

        Assert.Contains("connector-display-name-required", location blank)
        let! unchanged = ConnectorStore.findBySlug app.DbConfig "fake-two"
        Assert.Equal("Second fake", unchanged.Value.DisplayName)

        // A new secret replaces the stored one.
        let! replaced =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors/fake-two",
                cardFields "Second fake" "client-renamed" true @ [ "clientSecret", "a-new-secret" ]
            )

        Assert.Contains("connector-saved", location replaced)
        let! rotated = ConnectorStore.findBySlug app.DbConfig "fake-two"
        Assert.Equal(Secrets.hint "a-new-secret", rotated.Value.ClientSecretHint)
    }

[<Fact>]
let ``a connector that is not shared is not offered to an all-shared app and is offered nowhere until an app chooses it`` () =
    task {
        let! app, operator, clientId, _ = instance ()
        use _ = app
        let! _ = add operator "fake-two" false

        let visitor = Browser app.Client
        let! picker = visitor.GetHtml(pickerFor clientId)
        Assert.Equal<string list>([ "fake" ], connectorsOn picker)

        let! screen = operator.GetHtml "/admin/connectors"
        Assert.Contains("data-offered-nowhere", cardOf screen "fake-two")
        Assert.DoesNotContain("data-offered-nowhere", cardOf screen "fake")

        // The app chooses it: now it is offered there, and no longer nowhere.
        let! _ = operator.PostForm("/admin/apps", $"/admin/apps/{clientId}/connectors", chosen [ "fake-two" ])
        let! chosenPicker = visitor.GetHtml(pickerFor clientId)
        Assert.Equal<string list>([ "fake-two" ], connectorsOn chosenPicker)

        let! after = operator.GetHtml "/admin/connectors"
        Assert.DoesNotContain("data-offered-nowhere", cardOf after "fake-two")
    }

[<Fact>]
let ``a connector an app chose can stop being shared without stranding it`` () =
    task {
        let! app, operator, clientId, _ = instance ()
        use _ = app
        let! _ = add operator "fake-two" true
        let! _ = operator.PostForm("/admin/apps", $"/admin/apps/{clientId}/connectors", chosen [ "fake-two" ])

        // A chosen connector is offered shared or not, so nothing is stranded.
        let! saved =
            operator.PostForm("/admin/connectors", "/admin/connectors/fake-two", cardFields "Fake" "client-fake-two" false)

        Assert.Contains("connector-saved", location saved)
        let! stored = ConnectorStore.findBySlug app.DbConfig "fake-two"
        Assert.False stored.Value.IsShared
    }

[<Fact>]
let ``turning a connector to not shared warns when an all-shared app would be left with nothing`` () =
    task {
        let! app, operator, clientId, _ = instance ()
        use _ = app

        // `fake` is the only shared connector and Photos offers all shared ones.
        let! warned = operator.PostForm("/admin/connectors", "/admin/connectors/fake", cardFields "Fake" "fake" false)
        let where = location warned
        Assert.Contains("connector-strands-apps", where)
        Assert.Contains("slug=fake", where)

        // Nothing changed.
        let! stored = ConnectorStore.findBySlug app.DbConfig "fake"
        Assert.True stored.Value.IsShared

        // The screen names the app and draws the button that confirms.
        let! screen = operator.GetHtml where
        Assert.Contains($"data-stranded-app=\"{clientId}\"", screen)
        Assert.Contains("data-notice=\"connector-strands-apps\"", screen)
        Assert.Contains("/admin/connectors/fake?confirm=yes", screen)

        // Confirming goes ahead.
        let! confirmed =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors/fake?confirm=yes",
                cardFields "Fake" "fake" false
            )

        Assert.Contains("connector-saved", location confirmed)
        let! after = ConnectorStore.findBySlug app.DbConfig "fake"
        Assert.False after.Value.IsShared
    }

[<Fact>]
let ``the confirming button of the not-shared warning carries the stored values and unshares only`` () =
    task {
        let! app, operator, clientId, _ = instance ()
        use _ = app

        let! warned = operator.PostForm("/admin/connectors", "/admin/connectors/fake", cardFields "Typed label" "typed-id" false)
        let! screen = operator.GetHtml(location warned)
        Assert.Contains($"data-stranded-app=\"{clientId}\"", screen)

        // The warning's own form posts the STORED label and client id, so what
        // was typed on the card (and discarded: "nothing was changed") does not
        // sneak in through the confirmation.
        let! confirmed =
            operator.PostForm(
                location warned,
                "/admin/connectors/fake?confirm=yes",
                [ "displayName", "Fake"; "clientId", "fake" ]
            )

        Assert.Contains("connector-saved", location confirmed)
        let! after = ConnectorStore.findBySlug app.DbConfig "fake"
        Assert.False after.Value.IsShared
        Assert.Equal("fake", after.Value.ClientId)
        Assert.Equal("Fake", after.Value.DisplayName)
    }

/// One whole sign-in through the connector `slug`, ending with the id_token's
/// claims. A fresh browser each time, so no session short-circuits the picker.
let private signInThrough (app: Fixtures.TestApp) (slug: string) (clientId: string) (secret: string) (subject: string) =
    task {
        let person = Browser app.Client

        let authorizeUrl =
            "/connect/authorize?client_id=" + Uri.EscapeDataString clientId
            + "&response_type=code&redirect_uri=" + Uri.EscapeDataString RedirectUri
            + "&scope=" + Uri.EscapeDataString "openid email profile"
            + "&code_challenge=" + Challenge
            + "&code_challenge_method=S256&state=app-state"

        let! toPicker = person.Get authorizeUrl
        let! toProvider = person.Get((location toPicker).Replace("/signin", $"/connect/{slug}/start"))
        let providerUrl = location toProvider
        let decoded = Uri.UnescapeDataString providerUrl
        let redirect = Regex.Match(decoded, "redirect_uri=([^&]+)").Groups[1].Value
        let state = Regex.Match(decoded, "state=([^&]+)").Groups[1].Value

        // The provider hands the person back to THIS connector's callback.
        Assert.Contains($"/connect/{slug}/callback", redirect)

        let! approved =
            person.PostForm(
                providerUrl,
                "/dev/fake-provider/authorize",
                [ "redirect_uri", redirect
                  "state", state
                  "subject", subject
                  "email", "sam@example.com"
                  "displayName", "Sam Rivera"
                  "emailVerified", "on" ]
            )

        let! back = person.Get(location approved)
        let! issued = person.Get(location back)
        let code = Regex.Match(location issued, "code=([^&]+)").Groups[1].Value
        Assert.NotEqual<string>("", code)

        let! response = exchangeCode app clientId secret code
        let! body = Journeys.json response
        return Journeys.segmentOf 1 (Journeys.strOf body "id_token")
    }

[<Fact>]
let ``one account through two connectors of one kind is one user with one sub and the same idp`` () =
    task {
        let! app, operator, clientId, secret = instance ()
        use _ = app
        let! _ = add operator "fake-two" true

        let! viaFirst = signInThrough app "fake" clientId secret "same-account-1"
        let! viaSecond = signInThrough app "fake-two" clientId secret "same-account-1"

        // FR-015: the person is the same person, whichever application of the
        // provider they came through, and the app sees the kind, not the slug.
        Assert.Equal(Journeys.strOf viaFirst "sub", Journeys.strOf viaSecond "sub")
        Assert.Equal("fake", Journeys.strOf viaFirst "idp")
        Assert.Equal("fake", Journeys.strOf viaSecond "idp")

        let! users = scalar app "SELECT count(*) FROM users" []
        Assert.Equal("1", users)
        let! linked = scalar app "SELECT count(*) FROM linked_accounts" []
        Assert.Equal("1", linked)
    }
