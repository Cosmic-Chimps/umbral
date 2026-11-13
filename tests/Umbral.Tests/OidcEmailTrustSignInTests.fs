/// Trust in an OpenID Connect provider's verified emails, at sign-in and at the
/// re-check (specs/017 T002). An `oidc` connector that does not trust its
/// provider reads every address as unverified: the user is stored unverified, is
/// never linked by that address, the domain rules refuse the sign-in as `email_not_verified`, and an
/// earlier admission through a domain rule stops holding.
///
/// Driven through the real endpoints with the stub OIDC provider behind the real
/// adapter, as OidcConnectorTests does. The stub says `email_verified: true`
/// throughout, so every unverified outcome here is the connector's choice.
module Umbral.Tests.OidcEmailTrustSignInTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading.Tasks
open Xunit
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.StubOidcProvider

let private issuer = "https://auth.example.test/application/o/umbral"
let private providerSecret = "the-provider-client-secret-7c1e9a"

[<Literal>]
let private Scopes = "openid email profile"

[<Literal>]
let private OfflineScopes = "openid email profile offline_access"

/// The app with the stub behind its provider calls, an operator signed in, an
/// OpenID Connect connector `home` added (trusting, as a new one is today) and
/// an app registered. A fresh browser is the person signing in.
let private withHome
    (stub: StubOidcProvider)
    (body: Fixtures.TestApp -> Browser -> string -> string -> Task<unit>)
    =
    task {
        use! app = Fixtures.createAppWithProviderHandler stub fakeSettings ignore
        stub.Now <- fun () -> app.Clock.GetUtcNow()

        let operator = Browser app.Client
        do! claimInstance app operator
        do! addFakeConnector app "fake-connector-secret-value"

        let! _ =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors",
                [ "kind", "oidc"
                  "slug", "home"
                  "displayName", "Home"
                  "issuer", issuer
                  "emailTrust", "on"
                  "clientId", "umbral-client"
                  "clientSecret", providerSecret
                  "shared", "true" ]
            )

        let! clientId, clientSecret = createClientApp operator
        do! body app (Browser app.Client) clientId clientSecret
    }

let private sql (app: Fixtures.TestApp) (statement: string) =
    task {
        let! _ = Flows.scalar app statement []
        return ()
    }

let private count (app: Fixtures.TestApp) (statement: string) =
    task {
        let! value = Flows.scalar app statement []
        return int value
    }

/// What the OpenID Connect pane would save: the trust and, when given, the
/// connector's own list of allowed domains.
let private configure (app: Fixtures.TestApp) (trust: bool) (domains: string list option) =
    task {
        let! saved =
            ConnectorStore.saveOidc app.DbConfig app.Clock "home" "Home" "umbral-client" true domains None trust

        Assert.True saved.IsSome
    }

/// One whole sign-in through the stub at `home`: Umbral's authorization code for
/// the app, or where the browser landed instead.
let private signInHome (browser: Browser) (stub: StubOidcProvider) (clientId: string) (scope: string) =
    task {
        let! toPicker = browser.Get(authorizeUrl clientId scope)

        let! toProvider =
            browser.Get((toPicker.Headers.Location |> string).Replace("/signin", "/connect/home/start"))

        let authorization = stub.Authorize(Uri(toProvider.Headers.Location |> string))

        let! back =
            browser.Get(
                $"/connect/home/callback?code={Uri.EscapeDataString authorization.Code}&state={Uri.EscapeDataString authorization.State}"
            )

        let replayed = back.Headers.Location |> string

        if not (replayed.Contains "/connect/authorize") then
            return Error replayed
        else
            let! issued = browser.Get replayed
            let code = Regex.Match(issued.Headers.Location |> string, "code=([^&]+)").Groups[1].Value
            return Ok(Uri.UnescapeDataString code)
    }

let private codeOf (result: Result<string, string>) =
    match result with
    | Ok code -> code
    | Error landed -> failwith $"the sign-in did not reach the app: {landed}"

/// The person the provider describes: `sub`, address and the verified flag it
/// asserts (always true here).
let private says (stub: StubOidcProvider) (subject: string) (email: string) =
    stub.IdToken <-
        { defaultIdToken with
            Subject = subject
            Claims =
                [ "email", (email :> obj)
                  "email_verified", (true :> obj)
                  "name", ("Jane Doe" :> obj) ] }

/// Every `sign_in_failed` detail.
let private failures (app: Fixtures.TestApp) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()

        command.CommandText <-
            "SELECT ifnull(detail, '') FROM security_events WHERE event_type = 'sign_in_failed' ORDER BY id"

        use! reader = command.ExecuteReaderAsync()
        let rows = ResizeArray()

        while reader.Read() do
            rows.Add(reader.GetString 0)

        return List.ofSeq rows
    }

/// A verified user with a stored address and another provider that verifies it:
/// what an earlier sign-in through some other connector leaves behind.
let private addExistingUser (app: Fixtures.TestApp) (email: string) =
    task {
        do!
            sql
                app
                $"INSERT INTO users (id, display_name, email, email_verified, first_seen_utc, last_seen_utc)
                  VALUES ('existing', 'Existing', '{email}', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')"

        do!
            sql
                app
                $"INSERT INTO linked_accounts (provider_kind, provider_subject, user_id, asserted_email, asserted_email_verified, linked_utc, last_used_utc)
                  VALUES ('google', 'g-1', 'existing', '{email}', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')"
    }

let private expectInvalidGrant (response: HttpResponseMessage) =
    task {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        let! body = json response
        Assert.Equal("invalid_grant", strOf body "error")
    }

let private oidcUser (app: Fixtures.TestApp) =
    Flows.scalar app $"SELECT user_id FROM linked_accounts WHERE provider_subject = '{issuer}#user-1'" []

// ---------------------------------------------------------------------------
// Trust off
// ---------------------------------------------------------------------------

[<Fact>]
let ``off: a new person is stored unverified, with the address, and so is their link`` () =
    task {
        let stub = create issuer

        do!
            withHome stub (fun app browser clientId _ ->
                task {
                    do! configure app false None

                    let! result = signInHome browser stub clientId Scopes
                    codeOf result |> ignore

                    let! user = Flows.scalar app "SELECT email || '|' || email_verified FROM users" []
                    Assert.Equal("jane@example.com|0", user)

                    let! link =
                        Flows.scalar
                            app
                            "SELECT asserted_email || '|' || asserted_email_verified FROM linked_accounts WHERE provider_kind = 'oidc'"
                            []

                    Assert.Equal("jane@example.com|0", link)
                })
    }

[<Fact>]
let ``trust turned off during the code exchange is honoured by that sign-in`` () =
    // Code review N1: the connector is read before the exchange; the operator's
    // save lands while the provider is being asked. The sign-in must not raise
    // what that save just lowered.
    task {
        let stub = create issuer

        do!
            withHome stub (fun app browser clientId _ ->
                task {
                    stub.ClientCheck <-
                        Some(fun _ ->
                            task {
                                do! configure app false None
                                return true
                            })

                    let! result = signInHome browser stub clientId Scopes
                    codeOf result |> ignore

                    let! trust = Flows.scalar app "SELECT email_trust FROM connectors WHERE slug = 'home'" []
                    Assert.Equal("0", string trust)

                    let! user = Flows.scalar app "SELECT email || '|' || email_verified FROM users" []
                    Assert.Equal("jane@example.com|0", user)

                    let! link =
                        Flows.scalar
                            app
                            "SELECT asserted_email_verified FROM linked_accounts WHERE provider_kind = 'oidc'"
                            []

                    Assert.Equal("0", string link)
                })
    }

[<Fact>]
let ``off: an existing verified user with the same address is not linked, a second user is created`` () =
    task {
        let stub = create issuer

        do!
            withHome stub (fun app browser clientId _ ->
                task {
                    do! configure app false None
                    do! addExistingUser app "jane@example.com"

                    let! result = signInHome browser stub clientId Scopes
                    codeOf result |> ignore

                    let! users = count app "SELECT count(*) FROM users"
                    Assert.Equal(2, users)

                    let! owner = oidcUser app
                    Assert.True(owner <> "existing")

                    // The existing user is as they were.
                    let! existing = Flows.scalar app "SELECT email_verified FROM users WHERE id = 'existing'" []
                    Assert.Equal("1", existing)
                })
    }

[<Fact>]
let ``on: the same existing verified user is linked, as today`` () =
    task {
        let stub = create issuer

        do!
            withHome stub (fun app browser clientId _ ->
                task {
                    do! addExistingUser app "jane@example.com"

                    let! result = signInHome browser stub clientId Scopes
                    codeOf result |> ignore

                    let! users = count app "SELECT count(*) FROM users"
                    Assert.Equal(1, users)

                    let! owner = oidcUser app
                    Assert.Equal("existing", owner)
                })
    }

[<Fact>]
let ``off: the connector's own domain list refuses the sign-in as not verified and writes nothing`` () =
    task {
        let stub = create issuer

        do!
            withHome stub (fun app browser clientId _ ->
                task {
                    do! configure app false (Some [ "example.com" ])

                    let! result = signInHome browser stub clientId Scopes

                    match result with
                    | Error landed -> Assert.Contains("account-not-allowed", landed)
                    | Ok _ -> failwith "an unverified address was admitted by a domain rule"

                    let! events = failures app

                    Assert.Equal(
                        "email_domain_refused;reason=email_not_verified;rule=connector;stage=callback;domain=example.com",
                        Assert.Single events
                    )

                    let! users = count app "SELECT count(*) FROM users"
                    Assert.Equal(0, users)
                })
    }

[<Fact>]
let ``off: the instance domain list refuses the sign-in as not verified`` () =
    task {
        let stub = create issuer

        do!
            withHome stub (fun app browser clientId _ ->
                task {
                    do! configure app false None
                    do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "example.com" ]

                    let! result = signInHome browser stub clientId Scopes

                    match result with
                    | Error landed -> Assert.Contains("account-not-allowed", landed)
                    | Ok _ -> failwith "an unverified address was admitted by a domain rule"

                    let! events = failures app

                    Assert.Equal(
                        "email_domain_refused;reason=email_not_verified;rule=instance;stage=callback;domain=example.com",
                        Assert.Single events
                    )

                    let! users = count app "SELECT count(*) FROM users"
                    Assert.Equal(0, users)
                })
    }

[<Fact>]
let ``on: a domain rule admits the verified address, as today`` () =
    task {
        let stub = create issuer

        do!
            withHome stub (fun app browser clientId _ ->
                task {
                    do! configure app true (Some [ "example.com" ])

                    let! result = signInHome browser stub clientId Scopes
                    codeOf result |> ignore

                    let! user = Flows.scalar app "SELECT email_verified FROM users" []
                    Assert.Equal("1", user)
                })
    }

[<Fact>]
let ``off: the id_token and userinfo the app receives say email_verified false`` () =
    task {
        let stub = create issuer

        do!
            withHome stub (fun app browser clientId clientSecret ->
                task {
                    do! configure app false None

                    let! result = signInHome browser stub clientId Scopes
                    let! exchanged = exchangeCode app.Client clientId clientSecret (codeOf result)
                    Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode)
                    let! tokens = json exchanged

                    let payload = segmentOf 1 (strOf tokens "id_token")
                    Assert.Equal("jane@example.com", strOf payload "email")
                    Assert.Equal(JsonValueKind.False, payload.GetProperty("email_verified").ValueKind)

                    let! info = userInfo app.Client (strOf tokens "access_token")
                    Assert.Equal(HttpStatusCode.OK, info.StatusCode)
                    let! body = json info
                    Assert.Equal(JsonValueKind.False, body.GetProperty("email_verified").ValueKind)
                })
    }

[<Fact>]
let ``on: the id_token says email_verified true`` () =
    task {
        let stub = create issuer

        do!
            withHome stub (fun app browser clientId clientSecret ->
                task {
                    let! result = signInHome browser stub clientId Scopes
                    let! exchanged = exchangeCode app.Client clientId clientSecret (codeOf result)
                    let! tokens = json exchanged

                    let payload = segmentOf 1 (strOf tokens "id_token")
                    Assert.Equal(JsonValueKind.True, payload.GetProperty("email_verified").ValueKind)
                })
    }

[<Fact>]
let ``off: an address the provider's userinfo verified is still unverified`` () =
    task {
        let stub = create issuer

        do!
            withHome stub (fun app browser clientId _ ->
                task {
                    do! configure app false None

                    // The id_token carries neither an address nor a name; the
                    // userinfo endpoint supplies both, with the flag.
                    stub.IdToken <- { defaultIdToken with Claims = [] }
                    stub.UserInfo["email"] <- node "jane@example.com"
                    stub.UserInfo["email_verified"] <- (System.Text.Json.Nodes.JsonValue.Create true :> System.Text.Json.Nodes.JsonNode)
                    stub.UserInfo["name"] <- node "Jane From Userinfo"

                    let! result = signInHome browser stub clientId Scopes
                    codeOf result |> ignore
                    Assert.Equal(1, stub.CountOf "/userinfo")

                    let! user = Flows.scalar app "SELECT email || '|' || email_verified FROM users" []
                    Assert.Equal("jane@example.com|0", user)

                    let! link = Flows.scalar app "SELECT asserted_email_verified FROM linked_accounts WHERE provider_kind = 'oidc'" []
                    Assert.Equal("0", link)
                })
    }

// ---------------------------------------------------------------------------
// The re-check: trust turned off after the person was admitted
// ---------------------------------------------------------------------------

[<Fact>]
let ``a session admitted through a domain rule is refused at the next authorize once trust is off`` () =
    task {
        let stub = create issuer

        do!
            withHome stub (fun app browser clientId _ ->
                task {
                    do! configure app true (Some [ "example.com" ])

                    let! first = signInHome browser stub clientId Scopes
                    codeOf first |> ignore

                    // Still admitted while the trust is on.
                    let! stillIn = browser.Get(authorizeUrl clientId Scopes)
                    Assert.Contains("code=", stillIn.Headers.Location |> string)

                    do! configure app false (Some [ "example.com" ])

                    let! response = browser.Get(authorizeUrl clientId Scopes)
                    Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
                    Assert.StartsWith("/signin?returnUrl=", response.Headers.Location |> string)
                    Assert.Equal("", browser.Cookies[Sessions.CookieName])

                    let! events = failures app

                    Assert.Equal(
                        "email_domain_refused;reason=email_not_verified;rule=connector;stage=authorize;domain=example.com",
                        Assert.Single events
                    )
                })
    }

[<Fact>]
let ``a refresh token of a person admitted through a domain rule is invalid_grant once trust is off`` () =
    task {
        let stub = create issuer

        do!
            withHome stub (fun app browser clientId clientSecret ->
                task {
                    do! configure app true (Some [ "example.com" ])

                    let! result = signInHome browser stub clientId OfflineScopes
                    let! exchanged = exchangeCode app.Client clientId clientSecret (codeOf result)
                    Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode)
                    let! tokens = json exchanged
                    let refreshToken = strOf tokens "refresh_token"

                    do! configure app false (Some [ "example.com" ])

                    let! response = refresh app.Client clientId clientSecret refreshToken
                    do! expectInvalidGrant response
                })
    }

[<Fact>]
let ``with no domain rule a session and a refresh token go on once trust is off`` () =
    task {
        let stub = create issuer

        do!
            withHome stub (fun app browser clientId clientSecret ->
                task {
                    let! result = signInHome browser stub clientId OfflineScopes
                    let! exchanged = exchangeCode app.Client clientId clientSecret (codeOf result)
                    let! tokens = json exchanged
                    let refreshToken = strOf tokens "refresh_token"

                    do! configure app false None

                    let! response = browser.Get(authorizeUrl clientId Scopes)
                    Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
                    Assert.Contains("code=", response.Headers.Location |> string)

                    let! refreshed = refresh app.Client clientId clientSecret refreshToken
                    Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode)
                })
    }

[<Fact>]
let ``a session admitted through the instance list is refused once trust is off`` () =
    task {
        let stub = create issuer

        do!
            withHome stub (fun app browser clientId _ ->
                task {
                    do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "example.com" ]

                    let! first = signInHome browser stub clientId Scopes
                    codeOf first |> ignore

                    do! configure app false None

                    let! response = browser.Get(authorizeUrl clientId Scopes)
                    Assert.StartsWith("/signin?returnUrl=", response.Headers.Location |> string)

                    let! events = failures app
                    Assert.Contains("rule=instance;stage=authorize", Assert.Single events)
                })
    }
