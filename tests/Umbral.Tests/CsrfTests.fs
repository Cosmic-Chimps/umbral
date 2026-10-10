/// T095 — every state-changing admin route refuses a request with no
/// antiforgery token, and changes nothing (Constitution IV).
///
/// The routes are NOT listed here. They are read off the running app's endpoint
/// table, so a route added next month is covered the day it lands, and a handler
/// that forgets `WebForms.readValidated` — which compiles, serves, and is
/// silent — fails this test by name instead of shipping.
///
/// What is deliberately outside the rule, and why:
///
///   /connect/*   The OIDC protocol endpoints (authorize, userinfo, logout).
///                OpenIddict protects them with the protocol's own machinery —
///                `redirect_uri` registration, PKCE, `state`, bearer tokens —
///                and a browser form cannot supply an antiforgery token for a
///                request an arbitrary third-party app is meant to make.
///   /dev/*       The Fake provider, a stand-in for somebody else's login page.
///                It exists only behind two gates and changes no Umbral state.
///
/// Anything else that mutates and is NOT under /admin fails the test until it
/// is either protected or consciously added to that list: a new kind of route
/// should be a decision, not a default.
module Umbral.Tests.CsrfTests

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions
open System.Threading.Tasks
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open Xunit
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys

// ---------------------------------------------------------------------------
// The routes, read off the running app
// ---------------------------------------------------------------------------

let private mutating = set [ "POST"; "PUT"; "PATCH"; "DELETE" ]

type private MutatingRoute =
    { Method: string
      Pattern: string }

    member this.Name = $"{this.Method} {this.Pattern}"

/// Path prefixes that are not Umbral's to protect with antiforgery. See the
/// module comment for the reasons.
let private outOfScope = [ "/connect/"; "/dev/" ]

let private mutatingRoutes (app: Fixtures.TestApp) =
    (app.App :> IEndpointRouteBuilder).DataSources
    |> Seq.collect (fun source -> source.Endpoints)
    |> Seq.choose (fun endpoint ->
        match endpoint with
        | :? RouteEndpoint as route ->
            match route.Metadata.GetMetadata<IHttpMethodMetadata>() with
            | null -> None
            | methods ->
                methods.HttpMethods
                |> Seq.filter mutating.Contains
                |> Seq.map (fun verb ->
                    { Method = verb
                      Pattern = route.RoutePattern.RawText |> Option.ofObj |> Option.defaultValue "" })
                |> Some
        | _ -> None)
    |> Seq.collect id
    |> Seq.distinct
    |> List.ofSeq

let private isOutOfScope (route: MutatingRoute) =
    outOfScope |> List.exists route.Pattern.StartsWith

// ---------------------------------------------------------------------------
// A fingerprint of everything Umbral stores
// ---------------------------------------------------------------------------

/// One hash per table, for EVERY table in the file — Umbral's six and
/// OpenIddict's four alike — so "changed nothing" is checked against the whole
/// database rather than against the tables a route is expected to touch. The
/// table is named in a failure, which says what the request did.
let private snapshot (app: Fixtures.TestApp) : Task<Map<string, string>> =
    task {
        use! connection = Database.openConnection app.DbConfig

        let tables = List<string>()
        use list = connection.CreateCommand()
        list.CommandText <- "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name"
        use! names = list.ExecuteReaderAsync()

        while! names.ReadAsync() do
            tables.Add(names.GetString 0)

        do! names.CloseAsync()

        let mutable result = Map.empty

        for table in tables do
            use dump = connection.CreateCommand()
            dump.CommandText <- $"SELECT * FROM \"{table}\" ORDER BY rowid"
            use! reader = dump.ExecuteReaderAsync()
            let text = StringBuilder()

            while! reader.ReadAsync() do
                for i in 0 .. reader.FieldCount - 1 do
                    match reader.GetValue i with
                    | :? (byte array) as bytes -> text.Append(Convert.ToHexString bytes) |> ignore
                    | value -> text.Append(string value) |> ignore

                    text.Append('\u001f') |> ignore

                text.Append('\u001e') |> ignore

            result <- result.Add(table, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))))

        return result
    }

let private changedTables (before: Map<string, string>) (after: Map<string, string>) =
    [ for KeyValue(table, hash) in after do
          if before.TryFind table <> Some hash then
              yield table ]

// ---------------------------------------------------------------------------
// Driving a route
// ---------------------------------------------------------------------------

/// What the instance needs to hold for every route to have something real to
/// act on: a connector, an app, a person.
type private Targets =
    { ClientId: string
      UserId: string }

/// Route parameters resolved to rows that exist, by the NAME the route gives
/// them. If a parameter ever appears that is not here, it gets a plausible
/// placeholder and the route is still exercised — an unknown id must not hide
/// a missing check, since the check comes first.
let private fill (targets: Targets) (pattern: string) =
    Regex.Replace(
        pattern,
        @"\{([^}:?*]+)[^}]*\}",
        fun m ->
            match m.Groups[1].Value with
            | "slug" -> "fake"
            | "clientId" -> targets.ClientId
            | "id" -> targets.UserId
            | other -> other
    )

/// One body carrying EVERY field any admin form posts, valid for each: a real
/// setup token, a matching credential pair, a well-formed app. A handler that
/// skipped its antiforgery check would therefore act on it — that is the point
/// of using a valid body rather than an empty one.
let private bodyFor (setupToken: string) (credential: string) =
    [ "token", setupToken
      "credential", credential
      "confirm", credential
      "clientId", "123456789012345678"
      "clientSecret", "a-brand-new-client-secret-9001"
      "name", "Forged app"
      "redirectUri", "https://forged.example.com/callback"
      "postLogoutRedirectUri", ""
      "appType", "web" ]

let private send (browser: Browser) (method': string) (path: string) (fields: (string * string) list) =
    let message = new HttpRequestMessage(HttpMethod method', path)
    message.Content <- new FormUrlEncodedContent(dict fields)
    browser.Send message

/// Statuses that mean "refused": the 400 of a rejected token, or a redirect
/// to sign in / set up for a visitor who is not the operator. Anything else —
/// above all a 2xx, or a redirect somewhere real — means the request got
/// through.
let private refusedAsExpected (signedIn: bool) (route: MutatingRoute) (response: HttpResponseMessage) =
    let location = response.Headers.Location |> Option.ofObj |> Option.map string |> Option.defaultValue ""

    match response.StatusCode with
    | HttpStatusCode.BadRequest -> true
    | HttpStatusCode.Redirect ->
        // /admin/setup short-circuits on a configured instance before reading
        // the form — "already done, go sign in" — and requireOperator sends a
        // visitor to the sign-in form. Both change nothing by construction;
        // the snapshot is what proves it.
        let toAGate = location.StartsWith "/admin/signin" || location.StartsWith "/admin/setup"
        // A signed-in operator must be refused with a 400. Only setup answers
        // them with a redirect, because it never reads the form at all.
        if signedIn then route.Pattern = "/admin/setup" && toAGate else toAGate
    | _ -> false

let private rejectionFailures
    (app: Fixtures.TestApp)
    (browser: Browser)
    (signedIn: bool)
    (targets: Targets)
    (fields: (string * string) list)
    (routes: MutatingRoute list)
    =
    task {
        let failures = List<string>()

        for route in routes do
            let! before = snapshot app
            let! response = send browser route.Method (fill targets route.Pattern) fields
            let! after = snapshot app
            let changed = changedTables before after

            if not (List.isEmpty changed) then
                failures.Add(route.Name + " changed " + String.Join(", ", changed))
            elif not (refusedAsExpected signedIn route response) then
                failures.Add(
                    route.Name + " answered " + string (int response.StatusCode)
                    + " (location: " + string response.Headers.Location + ") instead of refusing"
                )

        return List.ofSeq failures
    }

let private assertNone (what: string) (failures: string list) =
    Assert.True(List.isEmpty failures, what + Environment.NewLine + String.Join(Environment.NewLine, failures))

/// A configured instance with an operator, a connector, an app and a person.
let private populated (app: Fixtures.TestApp) =
    task {
        let operator = Browser(app.Client)
        let! prepared = prepare app operator
        let person = Browser(app.Client)
        let! _ = signInThroughFake person prepared.ClientId "openid email" "fake-user-1" "sam@example.com"

        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT id FROM users LIMIT 1"
        let! userId = command.ExecuteScalarAsync()

        return
            operator,
            { ClientId = prepared.ClientId
              UserId = string userId }
    }

// ---------------------------------------------------------------------------
// The tests
// ---------------------------------------------------------------------------

[<Fact>]
let ``the endpoint table has state-changing admin routes to protect`` () =
    task {
        // If route discovery ever quietly returns nothing, every test below
        // passes over an empty list. Naming a few that MUST be there is the
        // cheap guard; the rest are found, not listed.
        use! app = Fixtures.createApp ()
        let names = mutatingRoutes app |> List.map _.Name

        for expected in [ "POST /admin/apps"; "POST /admin/setup"; "POST /admin/signout" ] do
            Assert.Contains(expected, names)

        Assert.True(names.Length >= 10, $"only {names.Length} state-changing routes were discovered")
    }

[<Fact>]
let ``every state-changing route is either protected or consciously out of scope`` () =
    task {
        use! app = Fixtures.createApp ()

        let stray =
            mutatingRoutes app
            |> List.filter (fun r -> not (r.Pattern.StartsWith "/admin") && not (isOutOfScope r))
            |> List.map _.Name

        assertNone
            "state-changing routes outside /admin that this test neither covers nor excludes — protect them, or add them to `outOfScope` with a reason:"
            stray
    }

[<Fact>]
let ``the operator cannot change anything without an antiforgery token`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, targets = populated app

        let routes = mutatingRoutes app |> List.filter (isOutOfScope >> not)
        let fields = bodyFor "not-the-token" OperatorCredential

        // 1. No token at all — the forged cross-site POST.
        let! missing = rejectionFailures app operator true targets fields routes
        assertNone "a signed-in operator's request with NO antiforgery token got through:" missing

        // 2. A token that was never issued: present, well-formed, wrong.
        let forged = ("__RequestVerificationToken", "CfDJ8-this-token-was-never-issued") :: fields
        let! wrong = rejectionFailures app operator true targets forged routes
        assertNone "a signed-in operator's request with a FORGED antiforgery token got through:" wrong

        // Refusing a sign-out must not have signed anybody out.
        let! stillIn = operator.Get "/admin/connectors"
        Assert.Equal(HttpStatusCode.OK, stillIn.StatusCode)

        // The control. The body above is valid, so the same request WITH a
        // token must succeed — otherwise "refused" could mean "malformed".
        let! created =
            operator.PostForm(
                "/admin/apps",
                "/admin/apps",
                [ "name", "Control app"
                  "redirectUri", "https://control.example.com/callback"
                  "postLogoutRedirectUri", ""
                  "appType", "web" ]
            )

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode)
    }

[<Fact>]
let ``a visitor who is not signed in cannot change anything either`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, targets = populated app

        // A browser that has never authenticated, with and without a token.
        let stranger = Browser(app.Client)
        let routes = mutatingRoutes app |> List.filter (isOutOfScope >> not)
        let fields = bodyFor "not-the-token" OperatorCredential

        let! bare = rejectionFailures app stranger false targets fields routes
        assertNone "an anonymous request with no token changed something or was not refused:" bare

        // A real, valid token — a stranger can fetch one from the sign-in page —
        // must still not unlock an operator-only route.
        let! token = stranger.TokenFrom "/admin/signin"
        let withToken = ("__RequestVerificationToken", token) :: fields

        let operatorOnly =
            routes |> List.filter (fun r -> r.Pattern <> "/admin/signin" && r.Pattern <> "/admin/signout" && r.Pattern <> "/admin/setup")

        let! tokenOnly = rejectionFailures app stranger false targets withToken operatorOnly
        assertNone "a valid token without an operator session unlocked an operator route:" tokenOnly
    }

[<Fact>]
let ``an unclaimed instance cannot be claimed without an antiforgery token`` () =
    task {
        // The one route that is reachable before there is any operator, and so
        // the one a cross-site POST could hijack: whoever sets the credential
        // owns the instance. The setup token is required too, but it is a
        // separate defence — this asserts the antiforgery one on its own, by
        // sending the CORRECT setup token.
        use! app = Fixtures.createAppWith fakeSettings ignore
        let browser = Browser(app.Client)
        let setupToken = app.Services.GetRequiredService<AdminAuth.SetupToken>().Value
        let! before = snapshot app

        let! response =
            browser.PostWithoutToken(
                "/admin/setup",
                [ "token", setupToken
                  "credential", OperatorCredential
                  "confirm", OperatorCredential ]
            )

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)

        let! after = snapshot app
        Assert.Empty(changedTables before after)

        let! configured = AdminAuth.isConfigured app.DbConfig
        Assert.False(configured, "the instance was claimed by a POST with no antiforgery token")

        // Every other mutating route, on an instance that has no operator yet.
        let routes = mutatingRoutes app |> List.filter (isOutOfScope >> not)
        let targets = { ClientId = "x"; UserId = "x" }
        let! failures = rejectionFailures app browser false targets (bodyFor setupToken OperatorCredential) routes
        assertNone "on an unclaimed instance a request with no token changed something or was not refused:" failures

        let! stillConfigured = AdminAuth.isConfigured app.DbConfig
        Assert.False(stillConfigured, "the instance was claimed during the sweep")

        // The control: the same body with a token claims it.
        let! claimed =
            browser.PostForm(
                "/admin/setup",
                "/admin/setup",
                [ "token", setupToken
                  "credential", OperatorCredential
                  "confirm", OperatorCredential ]
            )

        Assert.Equal(HttpStatusCode.Redirect, claimed.StatusCode)
        let! nowConfigured = AdminAuth.isConfigured app.DbConfig
        Assert.True nowConfigured
    }
