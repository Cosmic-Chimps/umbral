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
module Umbral.Tests.SecurityEventCoverageTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Infrastructure.SecurityEvents
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

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
        use! app = Fixtures.createAppWith fakeSettings ignore

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

        // -- Apps: change a connector choice (an update), rotate the secret, delete one
        let! _ =
            operator.PostForm(
                "/admin/apps",
                $"/admin/apps/{clientId}/connectors",
                [ "connectorChoice", "chosen"; "connectors", "fake" ]
            )

        let! _ = operator.PostForm("/admin/apps", $"/admin/apps/{clientId}/secret", [])
        let! _ = operator.PostForm("/admin/apps", $"/admin/apps/{clientId}/delete", [])

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
