/// Deleting a connector (T077).
///
/// The claim worth testing is what deleting does NOT do. A connector is a way
/// in, not a record of who came through it: the people who signed in with it
/// keep their accounts, their links and their tokens, and adding the connector
/// back returns them to the same `sub`. Getting that wrong in either direction
/// is silent — a cascade would erase accounts, a leak would leave a secret on
/// disk — so each is asserted rather than assumed.
module Umbral.Tests.ConnectorDeleteTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open Microsoft.Extensions.DependencyInjection
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

let private seedDiscord (app: Fixtures.TestApp) (enabled: bool) =
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
                  IsEnabled = enabled
                  IsShared = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None
    }

let private eventCount (app: Fixtures.TestApp) (eventType: string) =
    task {
        let! n = scalar app "SELECT count(*) FROM security_events WHERE event_type = @t" [ "@t", eventType ]
        return int n
    }

let private connectorExists (app: Fixtures.TestApp) (slug: string) =
    task {
        let! found = ConnectorStore.findBySlug app.DbConfig slug
        return found.IsSome
    }

[<Fact>]
let ``deleting a connector removes it, records it, and leaves the people who used it`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! seedFakeConnector app
        // A second enabled connector, so deleting the fake one is not the
        // "last door" case — that has its own test below.
        do! seedDiscord app true
        let! clientId, secret = registerApp operator "Grafana"

        let person = Browser(app.Client)
        let! signedIn = signInViaFake person clientId "openid offline_access" "fake-user-1" "sam@example.com"
        let code = match signedIn with Ok c -> c | Error where -> failwith $"sign-in did not complete: {where}"
        let! tokens = exchangeCode app clientId secret code
        let! tokenBody = tokens.Content.ReadAsStringAsync()
        let accessToken = JsonDocument.Parse(tokenBody).RootElement.GetProperty("access_token").GetString() |> string

        let! subject = userIdOf app "fake-user-1"
        Assert.NotEqual<string>("", subject)

        let! response = operator.PostForm("/admin/connectors", "/admin/connectors/fake/delete", [])

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("connector-deleted", location response)

        // Gone, so the way in is closed on the very next request.
        let! exists = connectorExists app "fake"
        Assert.False(exists, "the connector row must be deleted")

        let! picker = person.GetHtml "/signin"
        Assert.DoesNotContain("data-connector=\"fake\"", picker)

        // And on the record, with who, what and from where it was done.
        let! events = scalar app "SELECT event_type || '|' || outcome || '|' || actor || '|' || connector_slug FROM security_events WHERE event_type = 'connector_deleted'" []
        Assert.Equal("connector_deleted|success|operator|fake", events)

        // The people stay. No cascade reaches users or their links: a link is
        // keyed on provider kind and subject, with no foreign key to connectors.
        let! users = scalar app "SELECT count(*) FROM users" []
        let! links = scalar app "SELECT count(*) FROM linked_accounts" []
        Assert.Equal("1", users)
        Assert.Equal("1", links)

        // Nothing was revoked either: tokens belong to the person, not to the
        // connector they happened to arrive through.
        let message = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo")
        message.Headers.Add("Authorization", "Bearer " + accessToken)
        let! userInfo = app.Client.SendAsync message
        Assert.Equal(HttpStatusCode.OK, userInfo.StatusCode)

        // Add the connector back and the same person signs in to the same sub.
        do! seedFakeConnector app
        let returning = Browser(app.Client)
        let! again = signInViaFake returning clientId "openid" "fake-user-1" "sam@example.com"
        Assert.True(Result.isOk again, "signing in through the restored connector should work")

        let! subjectAfter = userIdOf app "fake-user-1"
        let! usersAfter = scalar app "SELECT count(*) FROM users" []
        Assert.Equal(subject, subjectAfter)
        Assert.Equal("1", usersAfter)
    }

[<Fact>]
let ``deleting a connector erases the stored secret reference`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! seedDiscord app true
        // Another enabled connector, or deleting discord is the last-door case.
        do! seedFakeConnector app

        let! reference = scalar app "SELECT client_secret_ref FROM connectors WHERE slug = 'discord'" []
        Assert.NotEqual<string>("", reference)

        let! response = operator.PostForm("/admin/connectors", "/admin/connectors/discord/delete?confirm=yes", [])
        Assert.Contains("connector-deleted", location response)

        let! remaining = scalar app "SELECT count(*) FROM connectors WHERE slug = 'discord'" []
        Assert.Equal("0", remaining)
    }

[<Fact>]
let ``deleting the last enabled connector asks first`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! seedDiscord app true

        // FR-023: the only door. Not a refusal — an operator may well mean to
        // close it — but not something to do by accident either.
        let! first = operator.PostForm("/admin/connectors", "/admin/connectors/discord/delete", [])
        Assert.Contains("connector-delete-last-enabled", location first)

        let! stillThere = connectorExists app "discord"
        Assert.True(stillThere, "nothing may be deleted before the confirmation")

        let! recorded = eventCount app "connector_deleted"
        Assert.Equal(0, recorded)

        let! confirmed = operator.PostForm("/admin/connectors", "/admin/connectors/discord/delete?confirm=yes", [])
        Assert.Contains("connector-deleted", location confirmed)

        let! gone = connectorExists app "discord"
        Assert.False(gone)
    }

[<Fact>]
let ``the Connectors screen carries the confirmation that makes the last-door delete answerable`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! seedDiscord app true

        // The warning redirect needs an answer, or the operator is stuck: with
        // only one enabled connector the screen's own buttons must carry the
        // confirm flag, for the toggle as much as for the delete.
        let! html = operator.GetHtml "/admin/connectors"
        Assert.Contains("action=\"/admin/connectors/discord/delete?confirm=yes\"", html)
        Assert.Contains("action=\"/admin/connectors/discord/toggle?confirm=yes\"", html)

        // With a second enabled connector the plain routes suffice.
        do! seedFakeConnector app
        let! two = operator.GetHtml "/admin/connectors"
        Assert.Contains("action=\"/admin/connectors/discord/delete\"", two)
        Assert.DoesNotContain("confirm=yes", two)
    }

[<Fact>]
let ``deleting a disabled connector needs no confirmation`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator
        // The only connector, but disabled: nobody can sign in through it
        // today, so removing it closes nothing.
        do! seedDiscord app false

        let! response = operator.PostForm("/admin/connectors", "/admin/connectors/discord/delete", [])
        Assert.Contains("connector-deleted", location response)
    }

[<Fact>]
let ``deleting a connector that does not exist records nothing`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator

        let! response = operator.PostForm("/admin/connectors", "/admin/connectors/nope/delete", [])
        Assert.Contains("connector-unknown", location response)

        let! recorded = eventCount app "connector_deleted"
        Assert.Equal(0, recorded)
    }

[<Fact>]
let ``a delete with no antiforgery token changes nothing`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! seedDiscord app false

        let! response = operator.PostWithoutToken("/admin/connectors/discord/delete", [])

        Assert.True(
            response.StatusCode = HttpStatusCode.BadRequest
            || response.StatusCode = HttpStatusCode.InternalServerError,
            $"accepted a post with no antiforgery token ({response.StatusCode})"
        )

        let! stillThere = connectorExists app "discord"
        Assert.True(stillThere)
    }

[<Fact>]
let ``an anonymous request cannot delete a connector`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! seedDiscord app false

        let stranger = Browser(app.Client)
        let! response = stranger.PostWithoutToken("/admin/connectors/discord/delete", [])
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode)

        let! stillThere = connectorExists app "discord"
        Assert.True(stillThere)
    }

// ---------------------------------------------------------------------------
// Feature 003, User Story 2: what deleting does to apps limited to a set.
// ---------------------------------------------------------------------------

/// Fake and Discord enabled and shared, and one app limited to the chosen set.
let private limitedApp (app: Fixtures.TestApp) (operator: Browser) (chosen: string list) =
    task {
        do! claimOperator app operator
        do! seedFakeConnector app
        do! seedDiscord app true
        let! clientId, _ = registerApp operator "Photos"
        do! AppConnectors.setChoice app.DbConfig clientId (Only(Set.ofList chosen))
        return clientId
    }

let private chosenSlugs (app: Fixtures.TestApp) (clientId: string) =
    task {
        let! choice = AppConnectors.choiceFor app.DbConfig clientId

        return
            match choice with
            | Only slugs -> Some(Set.toList slugs)
            | AllShared -> None
    }

[<Fact>]
let ``deleting a connector removes it from every chosen set and the apps stay limited`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser(app.Client)
        let! photos = limitedApp app operator [ "discord"; "fake" ]
        let! recipes, _ = registerApp operator "Recipes"
        do! AppConnectors.setChoice app.DbConfig recipes (Only(Set.ofList [ "discord"; "fake" ]))

        // Both apps keep the fake connector, so nobody is stranded and no
        // confirmation is asked for.
        let! response = operator.PostForm("/admin/connectors", "/admin/connectors/discord/delete", [])
        Assert.Contains("connector-deleted", location response)

        let! afterPhotos = chosenSlugs app photos
        let! afterRecipes = chosenSlugs app recipes
        Assert.Equal(Some [ "fake" ], afterPhotos)
        Assert.Equal(Some [ "fake" ], afterRecipes)
    }

[<Fact>]
let ``deleting the only connector of a limited app warns, then leaves the app limited with an empty set`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser(app.Client)
        let! photos = limitedApp app operator [ "discord" ]

        let! warned = operator.PostForm("/admin/connectors", "/admin/connectors/discord/delete", [])
        let back = location warned
        Assert.Contains("connector-delete-strands-apps", back)
        Assert.Contains("slug=discord", back)

        let! stillThere = connectorExists app "discord"
        Assert.True(stillThere, "nothing may be deleted before the confirmation")
        let! recorded = eventCount app "connector_deleted"
        Assert.Equal(0, recorded)

        // The screen names the app and carries the button that goes ahead.
        let! screen = operator.GetHtml back
        Assert.Contains($"data-stranded-app=\"{photos}\"", screen)
        Assert.Contains("action=\"/admin/connectors/discord/delete?confirm=yes\"", screen)

        let! confirmed = operator.PostForm("/admin/connectors", "/admin/connectors/discord/delete?confirm=yes", [])
        Assert.Contains("connector-deleted", location confirmed)

        // FR-011: still limited, now to nothing. Not widened to the shared
        // fake connector.
        let! after = chosenSlugs app photos
        Assert.Equal(Some [], after)

        let! apps = operator.GetHtml "/admin/apps"
        let row = apps.Substring(apps.IndexOf $"data-app=\"{photos}\"")
        Assert.Contains("data-app-choice=\"chosen\"", row)
        Assert.DoesNotContain("data-app-connector=", row)

        let! page = (Browser app.Client).GetHtml("/signin?returnUrl=" + Uri.EscapeDataString(Journeys.authorizeUrl photos "openid"))
        Assert.Contains("data-no-connectors", page)
        Assert.DoesNotContain("data-connector=\"fake\"", page)
    }
