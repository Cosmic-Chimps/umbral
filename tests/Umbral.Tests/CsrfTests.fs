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
/// A signed-in person's routes (/account/*) and the passkey ceremonies'
/// JSON endpoints (/passkey/*, which take the token in the
/// `RequestVerificationToken` header and `{}` as the body here) are walked too:
/// the operator, a person and a stranger are each held to "no token, no change".
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

/// Path prefixes whose routes take a JSON body and the antiforgery token in
/// the `RequestVerificationToken` header, as a script sends it, rather than
/// a form. The walk posts `{}` to them and moves any token field into that
/// header.
let private jsonRoutes = [ "/passkey/" ]

let private isJson (route: MutatingRoute) =
    jsonRoutes |> List.exists route.Pattern.StartsWith

let private tokenField = "__RequestVerificationToken"

/// Path prefixes of a signed-in person's own routes. Like the JSON ones they
/// are walked, so a new route under them is covered the day it lands.
let private personRoutes = [ "/account/" ]

let private isWalkedOutsideAdmin (route: MutatingRoute) =
    personRoutes @ jsonRoutes |> List.exists route.Pattern.StartsWith

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
      "appType", "web"
      // The branding accent form (spec 006): a valid colour, so a handler that
      // skipped its antiforgery check would save it.
      "accent", "#f26b1d" ]

let private send (browser: Browser) (route: MutatingRoute) (path: string) (fields: (string * string) list) =
    let message = new HttpRequestMessage(HttpMethod route.Method, path)

    if isJson route then
        message.Content <- new StringContent("{}", Encoding.UTF8, "application/json")

        match fields |> List.tryFind (fun (name, _) -> name = tokenField) with
        | Some(_, token) -> message.Headers.Add("RequestVerificationToken", token)
        | None -> ()
    else
        message.Content <- new FormUrlEncodedContent(dict fields)

    browser.Send message

/// Who sends the forged request. It decides which answers count as a refusal.
type private Visitor =
    /// No session: a redirect to either sign-in gate is a refusal.
    | Stranger
    /// Signed in through a connector (not the operator): /admin may send them
    /// to its own sign-in; their own routes must answer 400.
    | Person
    /// The operator: 400 everywhere, except /admin/setup's "already done".
    | Operator

/// Statuses that mean "refused": the 400 of a rejected token, or a redirect
/// to sign in / set up for a visitor who is not the operator. Anything else —
/// above all a 2xx, or a redirect somewhere real — means the request got
/// through.
let private refusedAsExpected (visitor: Visitor) (route: MutatingRoute) (response: HttpResponseMessage) =
    let location = response.Headers.Location |> Option.ofObj |> Option.map string |> Option.defaultValue ""

    match response.StatusCode with
    | HttpStatusCode.BadRequest -> true
    | HttpStatusCode.Redirect ->
        // /admin/setup short-circuits on a configured instance before reading
        // the form — "already done, go sign in" — and requireOperator sends a
        // visitor to the sign-in form. Both change nothing by construction;
        // the snapshot is what proves it.
        let adminGate = location.StartsWith "/admin/signin" || location.StartsWith "/admin/setup"
        // The gate of a person's route outside /admin, e.g.
        // `/signin?returnUrl=%2Faccount%2Fpasskeys`.
        let personGate = location.StartsWith "/signin?" && not (route.Pattern.StartsWith "/admin")

        match visitor with
        // A signed-in operator must be refused with a 400. Only setup answers
        // them with a redirect, because it never reads the form at all.
        | Operator -> route.Pattern = "/admin/setup" && adminGate
        // A person is not the operator, so /admin may send them to its own
        // sign-in; their own routes read the form first and must answer 400.
        | Person -> route.Pattern.StartsWith "/admin" && adminGate
        | Stranger -> adminGate || personGate
    | _ -> false

let private rejectionFailures
    (app: Fixtures.TestApp)
    (browser: Browser)
    (visitor: Visitor)
    (targets: Targets)
    (fields: (string * string) list)
    (routes: MutatingRoute list)
    =
    task {
        let failures = List<string>()

        for route in routes do
            let! before = snapshot app
            let! response = send browser route (fill targets route.Pattern) fields
            let! after = snapshot app
            let changed = changedTables before after

            if not (List.isEmpty changed) then
                failures.Add(route.Name + " changed " + String.Join(", ", changed))
            elif not (refusedAsExpected visitor route response) then
                failures.Add(
                    route.Name + " answered " + string (int response.StatusCode)
                    + " (location: " + string response.Headers.Location + ") instead of refusing"
                )

        return List.ofSeq failures
    }

let private assertNone (what: string) (failures: string list) =
    Assert.True(List.isEmpty failures, what + Environment.NewLine + String.Join(Environment.NewLine, failures))

/// A configured instance with an operator, a connector, an app and a person.
/// Returns the operator's browser, the person's (signed in through the fake
/// provider) and the rows the routes act on.
let private populated (app: Fixtures.TestApp) =
    task {
        let operator = Browser(app.Client)
        let! prepared = prepare app operator
        let person = Browser(app.Client)
        let! _ = signInThroughFake person prepared.ClientId "openid email" "fake-user-1" "sam@example.com"

        // The app has branding (spec 006), so that a branding route which let a
        // forged request through would change a row and be seen to: removing a
        // logo that is not there changes nothing, and would pass either way.
        let logoBytes = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 1uy |]

        do!
            BrandingStore.setLogo
                app.DbConfig
                (app.Clock.GetUtcNow())
                prepared.ClientId
                { Png = logoBytes
                  Sha256 = Convert.ToHexString(SHA256.HashData logoBytes).ToLowerInvariant()
                  Width = 64
                  Height = 64 }

        do!
            BrandingStore.setAccent
                app.DbConfig
                (app.Clock.GetUtcNow())
                prepared.ClientId
                (Umbral.Domains.Branding.Accent.parse "#1f6fa8" |> Result.toOption)

        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT id FROM users LIMIT 1"
        let! userId = command.ExecuteScalarAsync()

        return
            operator,
            person,
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

        for expected in [ "POST /admin/apps"; "POST /admin/setup"; "POST /admin/signout"; "POST /passkey/assertion" ] do
            Assert.Contains(expected, names)

        // The three branding routes (spec 006) are in the table the other tests
        // walk, so each is held to the same rule: no token, no change. The logo
        // upload is the one that switches the antiforgery middleware off and
        // validates in its handler, which is exactly why it is named here.
        for suffix in [ "/branding/accent"; "/branding/logo"; "/branding/logo/delete" ] do
            Assert.True(
                names |> List.exists (fun name -> name.StartsWith "POST /admin/apps/" && name.EndsWith suffix),
                $"no POST route ending {suffix} in the endpoint table"
            )

        // A signed-in person's route and the passkey ceremonies (spec 008) are in
        // the table the walks cover too, the person's one named here.
        Assert.True(
            names |> List.exists (fun name -> name.StartsWith "POST /account/passkeys/" && name.EndsWith "/remove"),
            "no POST /account/passkeys/{id}/remove route in the endpoint table"
        )

        Assert.True(names.Length >= 10, $"only {names.Length} state-changing routes were discovered")
    }

[<Fact>]
let ``every state-changing route is either protected or consciously out of scope`` () =
    task {
        use! app = Fixtures.createApp ()

        let stray =
            mutatingRoutes app
            // `POST /theme` is public on purpose (it keeps a display choice in a
            // cookie, no operator needed) but it is protected by the token: it is
            // in `routes` of every walk below, so it is held to "no token, no
            // change", and ThemeTests covers it by name.
            |> List.filter (fun r ->
                not (r.Pattern.StartsWith "/admin")
                && r.Pattern <> "/theme"
                && not (isWalkedOutsideAdmin r)
                && not (isOutOfScope r))
            |> List.map _.Name

        assertNone
            "state-changing routes outside /admin that this test neither covers nor excludes — protect them, or add them to `outOfScope` with a reason:"
            stray
    }

[<Fact>]
let ``the operator cannot change anything without an antiforgery token`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, targets = populated app

        let routes = mutatingRoutes app |> List.filter (isOutOfScope >> not)
        let fields = bodyFor "not-the-token" OperatorCredential

        // 1. No token at all — the forged cross-site POST.
        let! missing = rejectionFailures app operator Operator targets fields routes
        assertNone "a signed-in operator's request with NO antiforgery token got through:" missing

        // 2. A token that was never issued: present, well-formed, wrong.
        let forged = (tokenField, "CfDJ8-this-token-was-never-issued") :: fields
        let! wrong = rejectionFailures app operator Operator targets forged routes
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
let ``a signed-in person cannot change anything without an antiforgery token`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, person, targets = populated app

        let routes = mutatingRoutes app |> List.filter (isOutOfScope >> not)
        let fields = bodyFor "not-the-token" OperatorCredential

        // 1. No token at all — the forged cross-site POST from a site the
        //    person is visiting while signed in to Umbral.
        let! missing = rejectionFailures app person Person targets fields routes
        assertNone "a signed-in person's request with NO antiforgery token got through:" missing

        // 2. A token that was never issued.
        let forged = (tokenField, "CfDJ8-this-token-was-never-issued") :: fields
        let! wrong = rejectionFailures app person Person targets forged routes
        assertNone "a signed-in person's request with a FORGED antiforgery token got through:" wrong

        // Refusing must not have ended the person's session.
        let! stillIn = person.Get "/account/passkeys"
        Assert.Equal(HttpStatusCode.OK, stillIn.StatusCode)

        // The control: the same removal WITH a token is accepted — answered
        // with the page's notice (there is no passkey of that id to remove),
        // not with a 400 — so "refused" above cannot mean "malformed".
        let! removed = person.PostForm("/account/passkeys", $"/account/passkeys/{targets.UserId}/remove", [])
        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode)
        Assert.StartsWith("/account/passkeys", string removed.Headers.Location)
    }

[<Fact>]
let ``a visitor who is not signed in cannot change anything either`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, _, targets = populated app

        // A browser that has never authenticated, with and without a token.
        let stranger = Browser(app.Client)
        let routes = mutatingRoutes app |> List.filter (isOutOfScope >> not)
        let fields = bodyFor "not-the-token" OperatorCredential

        let! bare = rejectionFailures app stranger Stranger targets fields routes
        assertNone "an anonymous request with no token changed something or was not refused:" bare

        // A real, valid token — a stranger can fetch one from the sign-in page —
        // must still not unlock an operator-only route. (`/theme` is not one: a
        // stranger may choose their own theme. Nor are a person's routes, which
        // send a stranger to sign in, or the passkey ceremonies, which a
        // stranger may start: they are open to a stranger with a token on purpose.)
        let! token = stranger.TokenFrom "/admin/signin"
        let withToken = (tokenField, token) :: fields

        let operatorOnly =
            routes
            |> List.filter (fun r ->
                r.Pattern.StartsWith "/admin/"
                && r.Pattern <> "/admin/signin"
                && r.Pattern <> "/admin/signout"
                && r.Pattern <> "/admin/setup")

        let! tokenOnly = rejectionFailures app stranger Stranger targets withToken operatorOnly
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
        let! failures = rejectionFailures app browser Stranger targets (bodyFor setupToken OperatorCredential) routes
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
