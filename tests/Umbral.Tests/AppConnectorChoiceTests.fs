/// User Story 2 of feature 003: an app limited to some connectors.
///
/// Asserts on `data-connector`, `data-app-choice`, `data-app-connector` and
/// `data-stranded-app`, and on notice codes in redirects, never on the wording
/// of any text, so a copy change cannot break these.
module Umbral.Tests.AppConnectorChoiceTests

open System
open System.Net
open System.Text.RegularExpressions
open Microsoft.Extensions.DependencyInjection
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

/// A stored Discord connector, written straight to the store with a real secret.
let private seedDiscord (app: Fixtures.TestApp) (enabled: bool) (shared: bool) =
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
                  PasskeyEnrolment = InvitationsOnly
                  IsEnabled = enabled
                  IsShared = shared
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None
    }

/// An instance with the operator signed in, the fake and the Discord connectors
/// enabled and shared, and two registered apps, A ("Photos") and B ("Recipes").
let private twoAppsTwoConnectors () =
    task {
        let! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app
        do! seedDiscord app true true
        let! idA, _ = registerApp operator "Photos"
        let! idB, _ = registerApp operator "Recipes"
        return app, operator, idA, idB
    }

let private connectorsOn (html: string) =
    [ for m in Regex.Matches(html, "data-connector=\"([^\"]*)\"") -> m.Groups[1].Value ] |> List.sort

let private pickerFor (clientId: string) =
    "/signin?returnUrl=" + Uri.EscapeDataString(Journeys.authorizeUrl clientId "openid")

let private authorize (clientId: string) = Journeys.authorizeUrl clientId "openid"

/// The part of an app's page that belongs to it: its pane, which is all a detail page shows.
let private rowOf (html: string) (clientId: string) =
    let start = html.IndexOf($"data-app=\"{clientId}\"")
    Assert.True(start >= 0, $"no pane for app {clientId}")
    html.Substring start

/// An app's page: the choice is read where the operator sees it, in that app's pane.
let private paneOf (operator: Browser) (clientId: string) = operator.GetHtml $"/admin/apps/{clientId}"

let private choiceOf (html: string) (clientId: string) =
    Regex.Match(rowOf html clientId, "data-app-choice=\"([^\"]*)\"").Groups[1].Value

let private membersOf (html: string) (clientId: string) =
    [ for m in Regex.Matches(rowOf html clientId, "data-app-connector=\"([^\"]*)\"") -> m.Groups[1].Value ]
    |> List.sort

let private setChoice (operator: Browser) (clientId: string) (query: string) (fields: (string * string) list) =
    operator.PostForm("/admin/apps", $"/admin/apps/{clientId}/connectors{query}", fields)

let private chosen (slugs: string list) =
    ("connectorChoice", "chosen") :: (slugs |> List.map (fun slug -> "connectors", slug))

[<Fact>]
let ``an app limited to one connector offers only it while another app offers every shared one`` () =
    task {
        let! app, operator, idA, idB = twoAppsTwoConnectors ()
        use _ = app

        let! saved = setChoice operator idA "" (chosen [ "discord" ])
        Assert.Contains("app-connectors-saved", location saved)

        let visitor = Browser app.Client
        let! pageA = visitor.GetHtml(pickerFor idA)
        let! pageB = visitor.GetHtml(pickerFor idB)

        Assert.Equal<string list>([ "discord" ], connectorsOn pageA)
        Assert.Equal<string list>([ "discord"; "fake" ], connectorsOn pageB)
    }

[<Fact>]
let ``a limited app offers a connector that is not shared`` () =
    task {
        let! app, operator, idA, idB = twoAppsTwoConnectors ()
        use _ = app

        // Discord stops being shared: only an app that chooses it offers it.
        let! stored = ConnectorStore.findBySlug app.DbConfig "discord"
        do! ConnectorStore.save app.DbConfig app.Clock { stored.Value with IsShared = false } None
        let! _ = setChoice operator idA "" (chosen [ "discord" ])

        let visitor = Browser app.Client
        let! pageA = visitor.GetHtml(pickerFor idA)
        let! pageB = visitor.GetHtml(pickerFor idB)

        Assert.Equal<string list>([ "discord" ], connectorsOn pageA)
        Assert.Equal<string list>([ "fake" ], connectorsOn pageB)
    }

[<Fact>]
let ``a session through a connector the app does not offer is not honoured and not signed out`` () =
    task {
        let! app, operator, idA, idB = twoAppsTwoConnectors ()
        use _ = app

        // The person signs in through the fake connector, for the app that
        // offers every shared connector.
        let person = Browser app.Client
        let! signedIn = signInViaFake person idB "openid" "fake-user-1" "sam@example.com"
        Assert.True(Result.isOk signedIn, "the first sign-in should complete")

        let! _ = setChoice operator idA "" (chosen [ "discord" ])

        // App A offers only Discord: the fake session does not count there, so
        // the person is sent to A's sign-in page rather than getting a code.
        let! toPicker = person.Get(authorize idA)
        Assert.Equal(HttpStatusCode.Redirect, toPicker.StatusCode)
        Assert.StartsWith("/signin?returnUrl=", location toPicker)
        Assert.DoesNotContain("code=", location toPicker)

        let! page = person.GetHtml(location toPicker)
        Assert.Equal<string list>([ "discord" ], connectorsOn page)

        // With prompt=none there is no page to show: the app is told.
        let! silent = person.Get(authorize idA + "&prompt=none")
        Assert.Contains("error=login_required", location silent)
        Assert.StartsWith(RedirectUri, location silent)

        // The session was not signed out: another app still honours it.
        let! other = person.Get(authorize idB)
        Assert.Contains("code=", location other)
    }

[<Fact>]
let ``a session through a connector the app offers is honoured`` () =
    task {
        let! app, operator, idA, _ = twoAppsTwoConnectors ()
        use _ = app

        let person = Browser app.Client
        let! signedIn = signInViaFake person idA "openid" "fake-user-1" "sam@example.com"
        Assert.True(Result.isOk signedIn)

        // Limited to the connector the session came through: nothing changes.
        let! _ = setChoice operator idA "" (chosen [ "fake" ])
        let! offered = person.Get(authorize idA)
        Assert.Contains("code=", location offered)

        // And prompt=none still gets its code.
        let! silent = person.Get(authorize idA + "&prompt=none")
        Assert.Contains("code=", location silent)
    }

[<Fact>]
let ``an app's pane saves both choices and shows them`` () =
    task {
        let! app, operator, idA, idB = twoAppsTwoConnectors ()
        use _ = app

        let! initial = paneOf operator idA
        Assert.Equal("all-shared", choiceOf initial idA)
        Assert.Empty(membersOf initial idA)

        let! _ = setChoice operator idA "" (chosen [ "discord"; "fake" ])
        let! limited = paneOf operator idA
        let! limitedB = paneOf operator idB
        Assert.Equal("chosen", choiceOf limited idA)
        Assert.Equal<string list>([ "discord"; "fake" ], membersOf limited idA)
        Assert.Equal("all-shared", choiceOf limitedB idB)

        // Back to all shared: `connectorChoice` absent means the default.
        let! back = setChoice operator idA "" []
        Assert.Contains("app-connectors-saved", location back)
        let! widened = paneOf operator idA
        Assert.Equal("all-shared", choiceOf widened idA)
        Assert.Empty(membersOf widened idA)

        let! events = scalar app "SELECT count(*) FROM security_events WHERE event_type = 'app_updated' AND client_id = @c" [ "@c", idA ]
        Assert.Equal("2", events)
    }

[<Fact>]
let ``the create form carries the choice fields and the app is created with its choice`` () =
    task {
        let! app, operator, _, _ = twoAppsTwoConnectors ()
        use _ = app

        let! html = operator.GetHtml "/admin/apps/new"
        Assert.Contains("name=\"connectorChoice\"", html)
        Assert.Contains("value=\"all-shared\"", html)
        Assert.Contains("value=\"chosen\"", html)
        Assert.Contains("name=\"connectors\" value=\"discord\"", html)

        let! created =
            operator.PostForm(
                "/admin/apps",
                "/admin/apps",
                [ "name", "Frunchy"
                  "redirectUri", RedirectUri
                  "postLogoutRedirectUri", ""
                  "appType", "web" ]
                @ chosen [ "discord" ]
            )

        Assert.Contains("app-created", location created)
        let! clientId = scalar app "SELECT client_id FROM app_connector_choices" []
        Assert.NotEqual<string>("", clientId)

        let! screen = paneOf operator clientId
        Assert.Equal("chosen", choiceOf screen clientId)
        Assert.Equal<string list>([ "discord" ], membersOf screen clientId)

        // A plain create, with no choice field posted, is "all shared".
        let! plain, _ = registerApp operator "Plain"
        let! all = paneOf operator plain
        Assert.Equal("all-shared", choiceOf all plain)
    }

[<Fact>]
let ``an unknown connector slug is refused and nothing is saved or created`` () =
    task {
        let! app, operator, idA, _ = twoAppsTwoConnectors ()
        use _ = app

        let! refused = setChoice operator idA "" (chosen [ "no-such-connector" ])
        Assert.Contains("app-connector-unknown", location refused)

        let! rows = scalar app "SELECT count(*) FROM app_connector_choices" []
        Assert.Equal("0", rows)

        let! apps = scalar app "SELECT count(*) FROM \"OpenIddictApplications\"" []

        let! created =
            operator.PostForm(
                "/admin/apps",
                "/admin/apps",
                [ "name", "Ghost"
                  "redirectUri", RedirectUri
                  "postLogoutRedirectUri", ""
                  "appType", "web" ]
                @ chosen [ "no-such-connector" ]
            )

        Assert.Contains("app-connector-unknown", location created)
        let! appsAfter = scalar app "SELECT count(*) FROM \"OpenIddictApplications\"" []
        Assert.Equal(apps, appsAfter)
    }

[<Fact>]
let ``an empty chosen set warns, naming the app, and saves only once confirmed`` () =
    task {
        let! app, operator, idA, idB = twoAppsTwoConnectors ()
        use _ = app

        let! warned = setChoice operator idA "" (chosen [])
        let back = location warned
        Assert.Contains("app-connectors-strand", back)
        Assert.StartsWith($"/admin/apps/{idA}?", back)

        // Nothing changed, and the screen names the app and offers the way on.
        let! rows = scalar app "SELECT count(*) FROM app_connector_choices" []
        Assert.Equal("0", rows)

        let! screen = operator.GetHtml back
        Assert.Contains($"data-stranded-app=\"{idA}\"", screen)
        Assert.DoesNotContain($"data-stranded-app=\"{idB}\"", screen)
        Assert.Contains($"action=\"/admin/apps/{idA}/connectors?confirm=yes\"", screen)

        // The confirming button posts the choice that was attempted.
        let! confirmed = setChoice operator idA "?confirm=yes" (chosen [])
        Assert.Contains("app-connectors-saved", location confirmed)

        let! after = paneOf operator idA
        Assert.Equal("chosen", choiceOf after idA)
        Assert.Empty(membersOf after idA)

        // And the sign-in page of that app now offers nothing, not everything.
        let! page = (Browser app.Client).GetHtml(pickerFor idA)
        Assert.Contains("data-no-connectors", page)
        Assert.Empty(connectorsOn page)
    }

[<Fact>]
let ``a set of only disabled connectors warns too`` () =
    task {
        let! app, operator, idA, _ = twoAppsTwoConnectors ()
        use _ = app

        let! stored = ConnectorStore.findBySlug app.DbConfig "discord"
        do! ConnectorStore.setEnabled app.DbConfig app.Clock "discord" false

        let! warned = setChoice operator idA "" (chosen [ "discord" ])
        Assert.Contains("app-connectors-strand", location warned)

        let! rows = scalar app "SELECT count(*) FROM app_connector_choices" []
        Assert.Equal("0", rows)
        ignore stored
    }

[<Fact>]
let ``disabling the only connector of a limited app warns, naming it`` () =
    task {
        let! app, operator, idA, idB = twoAppsTwoConnectors ()
        use _ = app

        let! _ = setChoice operator idA "" (chosen [ "discord" ])

        // Fake stays enabled and shared, so app B is fine; app A is not.
        let! warned = operator.PostForm("/admin/connectors", "/admin/connectors/discord/toggle", [])
        let back = location warned
        Assert.Contains("connector-strands-apps", back)
        Assert.Contains("slug=discord", back)

        let! stillOn = ConnectorStore.findBySlug app.DbConfig "discord"
        Assert.True(stillOn.Value.IsEnabled, "nothing may change before the confirmation")

        let! screen = operator.GetHtml back
        Assert.Contains($"data-stranded-app=\"{idA}\"", screen)
        Assert.DoesNotContain($"data-stranded-app=\"{idB}\"", screen)
        Assert.Contains("action=\"/admin/connectors/discord/toggle?confirm=yes\"", screen)

        let! confirmed = operator.PostForm("/admin/connectors", "/admin/connectors/discord/toggle?confirm=yes", [])
        Assert.Contains("connector-disabled", location confirmed)

        let! off = ConnectorStore.findBySlug app.DbConfig "discord"
        Assert.False(off.Value.IsEnabled)
    }

[<Fact>]
let ``disabling a connector no app depends on needs no confirmation`` () =
    task {
        let! app, operator, idA, _ = twoAppsTwoConnectors ()
        use _ = app

        // A offers fake as well, so losing Discord strands nobody.
        let! _ = setChoice operator idA "" (chosen [ "discord"; "fake" ])

        let! response = operator.PostForm("/admin/connectors", "/admin/connectors/discord/toggle", [])
        Assert.Contains("connector-disabled", location response)
    }

[<Fact>]
let ``the last enabled connector still warns with its own code when no app is stranded`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app

        // No app is registered, so nobody is stranded, but the last door closes.
        let! response = operator.PostForm("/admin/connectors", "/admin/connectors/fake/toggle", [])
        Assert.Contains("connector-last-enabled", location response)
    }

[<Fact>]
let ``deleting an app removes its choice rows`` () =
    task {
        let! app, operator, idA, idB = twoAppsTwoConnectors ()
        use _ = app

        let! _ = setChoice operator idA "" (chosen [ "discord"; "fake" ])
        let! _ = setChoice operator idB "" (chosen [ "fake" ])

        let! response = operator.PostForm("/admin/apps", $"/admin/apps/{idA}/delete", [])
        Assert.Contains("app-deleted", location response)

        let! markers = scalar app "SELECT count(*) FROM app_connector_choices WHERE client_id = @c" [ "@c", idA ]
        let! members = scalar app "SELECT count(*) FROM app_connectors WHERE client_id = @c" [ "@c", idA ]
        Assert.Equal("0", markers)
        Assert.Equal("0", members)

        // The other app's choice is untouched.
        let! others = scalar app "SELECT count(*) FROM app_connectors WHERE client_id = @c" [ "@c", idB ]
        Assert.Equal("1", others)
    }

[<Fact>]
let ``with no connector shared the create form is replaced by the add a connector first prompt`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app

        let! withShared = operator.GetHtml "/admin/apps"
        Assert.Contains("name=\"redirectUri\"", withShared)

        // Enabled but not shared: a new app starts on "all shared", which now
        // offers nothing, so the screen sends the operator to Connectors first.
        let! stored = ConnectorStore.findBySlug app.DbConfig "fake"
        do! ConnectorStore.save app.DbConfig app.Clock { stored.Value with IsShared = false } None

        let! withoutShared = operator.GetHtml "/admin/apps"
        Assert.DoesNotContain("name=\"redirectUri\"", withoutShared)
    }

[<Fact>]
let ``a choice change with no antiforgery token changes nothing`` () =
    task {
        let! app, operator, idA, _ = twoAppsTwoConnectors ()
        use _ = app

        let! response =
            operator.PostWithoutToken($"/admin/apps/{idA}/connectors", [ "connectorChoice", "chosen"; "connectors", "discord" ])

        Assert.True(
            response.StatusCode = HttpStatusCode.BadRequest
            || response.StatusCode = HttpStatusCode.InternalServerError,
            $"accepted a post with no antiforgery token ({response.StatusCode})"
        )

        let! rows = scalar app "SELECT count(*) FROM app_connector_choices" []
        Assert.Equal("0", rows)
    }
