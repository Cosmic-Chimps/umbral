/// FR-035, and the test `SecurityEvents.allEventTypes` promises (T092).
///
/// The security log is the only record an operator has, and an event type
/// nobody writes is worse than a missing feature: it looks like coverage. So
/// this drives the real product through every action that causes one and then
/// fails with the NAMES of whatever was never seen — a new case added to the
/// union without a call site fails here, naming itself.
///
/// The per-event shape tests below it exist because "an event was written" is a
/// weaker claim than "the right event was written, about the right person": an
/// earlier version of the callback recorded a disabled user's sign-in as a
/// success and then again as a failure.
///
/// The journey runs with an in-memory store registered as `aws` and active,
/// because two of the event types (`secret_store_failed`, `connector_secret_moved`)
/// exist only when an external store does. Nothing else about it changes: every
/// event still comes from the product's own handlers and the `move-secrets`
/// command, and the store only answers the way a manager would.
module Umbral.Tests.SecurityEventCoverageTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open Microsoft.AspNetCore.DataProtection
open Microsoft.Extensions.DependencyInjection
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Infrastructure.Secrets
open Umbral.Infrastructure.SecretStores.Registry
open Umbral.Infrastructure.SecretStores.Settings
open Umbral.Infrastructure.SecurityEvents
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows
open Umbral.Tests.Infrastructure.InMemorySecretStore

type private Row =
    { EventType: string
      Outcome: string
      Actor: string
      UserId: string option
      ConnectorSlug: string option
      ClientId: string option
      Detail: string option }

let private events (app: Fixtures.TestApp) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()

        command.CommandText <-
            "SELECT event_type, outcome, actor, user_id, connector_slug, client_id, detail
             FROM security_events ORDER BY id"

        use! reader = command.ExecuteReaderAsync()
        let rows = ResizeArray()
        let opt (i: int) = if reader.IsDBNull i then None else Some(reader.GetString i)
        let mutable go = true

        while go do
            let! has = reader.ReadAsync()

            if has then
                rows.Add
                    { EventType = reader.GetString 0
                      Outcome = reader.GetString 1
                      Actor = reader.GetString 2
                      UserId = opt 3
                      ConnectorSlug = opt 4
                      ClientId = opt 5
                      Detail = opt 6 }
            else
                go <- false

        return List.ofSeq rows
    }

let private ofType (eventType: EventType) (rows: Row list) =
    rows |> List.filter (fun r -> r.EventType = eventType.Value)

let private signedInOk (result: Result<string, string>) =
    match result with
    | Ok code -> code
    | Error where -> failwith $"sign-in did not complete: {where}"

// ---------------------------------------------------------------------------
// An instance with an external store
// ---------------------------------------------------------------------------

let private awsSettings =
    [ "UMBRAL:SECRET_STORE", "aws"
      "AWS_REGION", "eu-west-1" ]

/// An app whose active store is the in-memory `aws` one.
let private startOnAws (store: InMemorySecretStore) =
    Fixtures.createAppWith (fakeSettings @ awsSettings) (Fixtures.withSecretStores [ store ] "aws")

/// What an operator runs after unsetting `UMBRAL__SECRET_STORE`: the same
/// stores with `local` as the target. The app's own registry cannot be changed
/// once built, so this builds the second one over the same store and key ring.
let private moveToLocal (app: Fixtures.TestApp) (store: InMemorySecretStore) =
    let local = LocalSecretStore(app.Services.GetRequiredService<IDataProtectionProvider>())
    let registry = SecretStores(local, "local", Map [ "aws", lazy (Ok(store :> ISecretStore)) ])

    SecretMove.run registry (app.Services.GetRequiredService<SecretStoreSettings>()) app.DbConfig app.Clock false ignore


// ---------------------------------------------------------------------------
// Sign-in events say the right thing about the right person
// ---------------------------------------------------------------------------

[<Fact>]
let ``a first sign-in records the user created and the sign-in, both naming the person`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! seedFakeConnector app
        let! clientId, _ = registerApp operator "Grafana"

        let! result = signInViaFake (Browser(app.Client)) clientId "openid" "fake-user-1" "sam@example.com"
        signedInOk result |> ignore

        let! subject = userIdOf app "fake-user-1"
        let! rows = events app

        let created = Assert.Single(ofType UserCreated rows)
        Assert.Equal(Some subject, created.UserId)
        Assert.Equal(Some "fake", created.ConnectorSlug)

        let signedIn = Assert.Single(ofType SignInSucceeded rows)
        Assert.Equal(Some subject, signedIn.UserId)
        Assert.Equal("user", signedIn.Actor)
    }

[<Fact>]
let ``an ordinary return visit records one sign-in, not two`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! seedFakeConnector app
        let! clientId, _ = registerApp operator "Grafana"

        let! first = signInViaFake (Browser(app.Client)) clientId "openid" "fake-user-1" "sam@example.com"
        signedInOk first |> ignore
        let! second = signInViaFake (Browser(app.Client)) clientId "openid" "fake-user-1" "sam@example.com"
        signedInOk second |> ignore

        let! rows = events app

        // One creation, two sign-ins: the second visit created and linked
        // nothing, so it must not look like it did.
        Assert.Single(ofType UserCreated rows) |> ignore
        Assert.Empty(ofType UserLinked rows)
        Assert.Equal(2, (ofType SignInSucceeded rows).Length)
    }

[<Fact>]
let ``a disabled user's sign-in is recorded as a failure and never as a success`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! seedFakeConnector app
        let! clientId, _ = registerApp operator "Grafana"

        let! first = signInViaFake (Browser(app.Client)) clientId "openid" "fake-user-1" "sam@example.com"
        signedInOk first |> ignore
        let! subject = userIdOf app "fake-user-1"
        do! Users.setEnabled app.DbConfig subject false

        let! second = signInViaFake (Browser(app.Client)) clientId "openid" "fake-user-1" "sam@example.com"
        Assert.True(Result.isError second, "a disabled user must not get a code")

        let! rows = events app

        // Exactly one success — the first, before they were disabled.
        Assert.Single(ofType SignInSucceeded rows) |> ignore

        let failed = Assert.Single(ofType SignInFailed rows)
        Assert.Equal("failure", failed.Outcome)
        Assert.Equal(Some subject, failed.UserId)
        Assert.Equal(Some "user_disabled", failed.Detail)
    }

[<Fact>]
let ``a disabled user with a live session is turned away at authorize and it is recorded`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! seedFakeConnector app
        let! clientId, _ = registerApp operator "Grafana"

        let person = Browser(app.Client)
        let! first = signInViaFake person clientId "openid" "fake-user-1" "sam@example.com"
        signedInOk first |> ignore

        let! subject = userIdOf app "fake-user-1"
        do! Users.setEnabled app.DbConfig subject false

        // Same browser, so the Umbral session cookie is still there.
        let authorizeUrl =
            "/connect/authorize?client_id=" + Uri.EscapeDataString clientId
            + "&response_type=code&redirect_uri=" + Uri.EscapeDataString RedirectUri
            + "&scope=openid&code_challenge=" + Challenge
            + "&code_challenge_method=S256&state=s"

        let! _ = person.Get authorizeUrl
        let! rows = events app

        let failed = ofType SignInFailed rows |> List.filter (fun r -> r.ClientId.IsSome)
        let one = Assert.Single failed
        Assert.Equal(Some subject, one.UserId)
        Assert.Equal(Some clientId, one.ClientId)
        Assert.Equal(Some "user_disabled", one.Detail)
    }

// ---------------------------------------------------------------------------
// T092 — every event type, emitted by the product
// ---------------------------------------------------------------------------

[<Fact>]
let ``every event type in the data model is emitted by the real product`` () =
    task {
        let store = InMemorySecretStore "aws"
        use! app = startOnAws store

        // -- Operator: claim (credential set), sign out, sign in, a wrong credential
        let operator = Browser(app.Client)
        do! claimOperator app operator

        let! _ = operator.PostForm("/admin/connectors", "/admin/signout", [])
        let! _ = operator.PostForm("/admin/signin", "/admin/signin", [ "credential", "not-the-credential" ])
        let! signedBackIn = operator.PostForm("/admin/signin", "/admin/signin", [ "credential", OperatorCredential ])
        Assert.Equal(HttpStatusCode.Redirect, signedBackIn.StatusCode)

        // -- Apps: create two (rotated and deleted at the end, once the secret
        //    the sign-ins below redeem with is no longer needed)
        do! seedFakeConnector app
        let! clientId, secret = registerApp operator "Grafana"
        let! _ = registerApp operator "Throwaway"

        // -- People: a first sign-in (created), a second provider account with
        //    the same verified email (linked), a code replayed
        let sam = Browser(app.Client)
        let! first = signInViaFake sam clientId "openid" "fake-user-1" "sam@example.com"
        let code = signedInOk first
        let! redeemed = exchangeCode app clientId secret code
        Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode)

        let! replay = exchangeCode app clientId secret code
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode)

        let! linked = signInViaFake (Browser(app.Client)) clientId "openid" "fake-user-2" "sam@example.com"
        signedInOk linked |> ignore

        // A different person, to delete
        let! other = signInViaFake (Browser(app.Client)) clientId "openid" "fake-user-3" "kim@example.com"
        signedInOk other |> ignore

        let! samId = userIdOf app "fake-user-1"
        let! otherId = userIdOf app "fake-user-3"

        // -- Users: disable (and a refused sign-in), enable, delete
        let! _ = operator.PostForm("/admin/users", $"/admin/users/{samId}/toggle", [])
        let! refused = signInViaFake (Browser(app.Client)) clientId "openid" "fake-user-1" "sam@example.com"
        Assert.True(Result.isError refused)
        let! _ = operator.PostForm("/admin/users", $"/admin/users/{samId}/toggle", [])
        let! _ = operator.PostForm("/admin/users", $"/admin/users/{otherId}/delete", [])

        // -- Connectors: the screen creates discord, updates it, turns it off and
        //    on, and removes it. The fake connector keeps a door open
        //    throughout, so no confirmation is needed.
        let! _ =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors/discord",
                [ "clientId", "123"; "clientSecret", "discord-secret" ]
            )

        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors/discord", [ "clientId", "999" ])
        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors/discord/toggle", [])
        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors/discord/toggle", [])
        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors/discord/delete", [])

        // -- Another connector, created the same way and left in place
        let! _ =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors/github",
                [ "clientId", "gh-client"; "clientSecret", "gh-secret" ]
            )

        // -- A store that refuses a write: the connector is not saved, and the
        //    refusal is recorded (secret_store_failed)
        store.FailNextCall AccessDenied

        let! refusedSave =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors/discord",
                [ "clientId", "456"; "clientSecret", "refused-secret" ]
            )

        Assert.Contains("connector-secret-store-failed", location refusedSave)

        // -- Apps: change a connector choice (an update), rotate the secret, delete one
        let! _ =
            operator.PostForm(
                "/admin/apps",
                $"/admin/apps/{clientId}/connectors",
                [ "connectorChoice", "chosen"; "connectors", "fake" ]
            )

        let! _ = operator.PostForm("/admin/apps", $"/admin/apps/{clientId}/secret", [])
        let! _ = operator.PostForm("/admin/apps", $"/admin/apps/{clientId}/delete", [])

        // -- The offline command: the instance goes back to `local`, so every
        //    connector's secret moves (connector_secret_moved)
        let! exitCode = moveToLocal app store
        Assert.Equal(0, exitCode)

        let! rows = events app
        let seen = rows |> List.map (fun r -> r.EventType) |> Set.ofList

        let missing =
            allEventTypes
            |> List.filter (fun t -> not (seen.Contains t.Value))
            |> List.map (fun t -> t.Value)

        Assert.True(
            missing.IsEmpty,
            "These event types are declared in SecurityEvents.allEventTypes but the product never emitted them: "
            + String.Join(", ", missing)
        )

        // And nothing outside the closed set — a typo'd type cannot exist in
        // the union, but a row written by hand could.
        let known = allEventTypes |> List.map (fun t -> t.Value) |> Set.ofList
        let unknown = seen |> Set.filter (fun t -> not (known.Contains t))
        Assert.True(unknown.IsEmpty, "Unknown event types in the log: " + String.Join(", ", unknown))
    }

// ---------------------------------------------------------------------------
// The two events of an external store say the right thing about the right connector
// ---------------------------------------------------------------------------

let private githubFields (secret: string) =
    [ "clientId", "gh-client"; "clientSecret", secret ]

[<Fact>]
let ``a store that refuses a write records one failure naming the operator, the connector, the store and the cause`` () =
    task {
        let store = InMemorySecretStore "aws"
        use! app = startOnAws store
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! seedFakeConnector app

        let secret = "refused-client-secret-77c1e0"
        store.FailNextCall AccessDenied

        let! refused = operator.PostForm("/admin/connectors", "/admin/connectors/github", githubFields secret)
        Assert.Contains("connector-secret-store-failed", location refused)

        let! rows = events app
        let failed = Assert.Single(ofType SecretStoreFailed rows)
        Assert.Equal("failure", failed.Outcome)
        Assert.Equal("operator", failed.Actor)
        Assert.Equal(Some "github", failed.ConnectorSlug)
        // Fixed words only: a closed set of causes, never what the store said.
        Assert.Equal(Some "store=aws;op=put;cause=access_denied", failed.Detail)
        Assert.Equal(None, failed.UserId)
        Assert.Equal(None, failed.ClientId)

        // The refused write saved nothing, so nothing else was recorded about it.
        Assert.Empty(ofType ConnectorCreated rows)
        Assert.DoesNotContain(secret, String.Join(" ", rows |> List.map (fun r -> string r.Detail)))
    }

[<Fact>]
let ``moving a connector's secret records one move per connector, by the system, naming both stores`` () =
    task {
        let store = InMemorySecretStore "aws"
        use! app = startOnAws store
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! seedFakeConnector app

        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors/github", githubFields "gh-secret-for-move")

        let! code = moveToLocal app store
        Assert.Equal(0, code)

        let! rows = events app
        let moved = ofType ConnectorSecretMoved rows

        // `fake` was seeded through the registry's active store, so it moves too.
        Assert.Equal<string list>([ "fake"; "github" ], moved |> List.map (fun r -> r.ConnectorSlug.Value) |> List.sort)

        for row in moved do
            Assert.Equal("success", row.Outcome)
            Assert.Equal("system", row.Actor)
            Assert.Equal(Some "from=aws;to=local", row.Detail)
            Assert.Equal(None, row.UserId)
            Assert.Equal(None, row.ClientId)

        // A second run finds everything in place and records nothing more.
        let! again = moveToLocal app store
        Assert.Equal(0, again)
        let! after = events app
        Assert.Equal(moved.Length, (ofType ConnectorSecretMoved after).Length)
    }
