/// An OpenID Connect provider through the whole product (specs/002 User Story 1):
/// the operator adds it on the Connectors screen, people see it on the sign-in
/// page, a full authorization-code login reaches an app with Umbral tokens, and
/// nothing secret is logged on the way. The identity provider is the stub, behind
/// the real adapter, the real discovery cache and the real pipeline.
module Umbral.Tests.OidcConnectorTests

open System
open System.Collections.Generic
open System.Net
open System.Text.RegularExpressions
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.StubOidcProvider
open Umbral.Tests.LogRedactionTests

let private issuer = "https://auth.example.test/application/o/umbral"
let private providerSecret = "the-provider-client-secret-7c1e9a"

/// The add form's fields for an OpenID Connect provider, as a browser posts them.
let private addFields (slug: string) (issuerUrl: string) =
    [ "kind", "oidc"
      "slug", slug
      "displayName", "Home"
      "issuer", issuerUrl
      "clientId", "umbral-client"
      "clientSecret", providerSecret
      "shared", "true" ]

let private without (names: string list) (fields: (string * string) list) =
    fields |> List.filter (fun (name, _) -> not (List.contains name names))

let private replacing (name: string) (value: string) (fields: (string * string) list) =
    fields |> List.map (fun (n, v) -> if n = name then n, value else n, v)

/// The app, with the stub behind its provider calls and the stub reading the
/// app's own clock, and an operator signed in.
let private start (stubs: StubOidcProvider list) (customize: Microsoft.AspNetCore.Builder.WebApplicationBuilder -> unit) (settings: (string * string) list) =
    task {
        let handler: Net.Http.HttpMessageHandler =
            match stubs with
            | [ single ] -> single :> Net.Http.HttpMessageHandler
            | several -> new StubRouter(several) :> Net.Http.HttpMessageHandler

        let! app = Fixtures.createAppWithProviderHandler handler (fakeSettings @ settings) customize

        for stub in stubs do
            stub.Now <- fun () -> app.Clock.GetUtcNow()

        let operator = Browser app.Client
        do! claimInstance app operator
        do! addFakeConnector app "fake-connector-secret-value"
        return app, operator
    }

let private scalarCount (app: Fixtures.TestApp) (statement: string) =
    task {
        let! value = Flows.scalar app statement []
        return int value
    }

let private addProvider (operator: Browser) (slug: string) (issuerUrl: string) =
    operator.PostForm("/admin/connectors", "/admin/connectors", addFields slug issuerUrl)

/// The app's sign-in page, as a person arriving from it sees it.
let private pickerFor (browser: Browser) (clientId: string) =
    task {
        let! toPicker = browser.Get(authorizeUrl clientId "openid email profile")
        return! browser.GetHtml(toPicker.Headers.Location |> string)
    }

/// One whole sign-in through the provider at `slug`, ending with Umbral's
/// authorization code for the app, or where it landed instead.
let private signInThroughProvider (browser: Browser) (stub: StubOidcProvider) (clientId: string) (slug: string) =
    task {
        let! toPicker = browser.Get(authorizeUrl clientId "openid email profile")
        let! toProvider = browser.Get((toPicker.Headers.Location |> string).Replace("/signin", $"/connect/{slug}/start"))

        // The provider's authorization endpoint, as Umbral sent the browser to it.
        let authorization = stub.Authorize(Uri(toProvider.Headers.Location |> string))

        let! back =
            browser.Get(
                $"/connect/{slug}/callback?code={Uri.EscapeDataString authorization.Code}&state={Uri.EscapeDataString authorization.State}"
            )

        let replayed = back.Headers.Location |> string

        if not (replayed.Contains "/connect/authorize") then
            return Error replayed
        else
            let! issued = browser.Get replayed
            let code = Regex.Match(issued.Headers.Location |> string, "code=([^&]+)").Groups[1].Value
            return Ok(Uri.UnescapeDataString code, authorization)
    }

let private signedIn (result: Result<string * Authorization, string>) =
    match result with
    | Ok(code, _) -> code
    | Error landed -> failwith $"the sign-in did not reach the app: {landed}"

let private idTokenOf (app: Fixtures.TestApp) (clientId: string) (secret: string) (code: string) =
    task {
        let! exchanged = exchangeCode app.Client clientId secret code
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode)
        let! tokens = json exchanged
        return segmentOf 1 (strOf tokens "id_token"), tokens
    }

// ---------------------------------------------------------------------------
// Adding a provider (T023) and what the screen shows (T022)
// ---------------------------------------------------------------------------

[<Fact>]
let ``adding a provider saves it enabled and the screen shows its callback URL and issuer`` () =
    task {
        let stub = create issuer
        let! app, operator = start [ stub ] ignore []
        use _ = app

        let! added = addProvider operator "home" issuer
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode)
        Assert.Contains("connector-added-oidc", added.Headers.Location |> string)

        let! stored = ConnectorStore.findBySlug app.DbConfig "home"
        let connector = stored.Value
        Assert.Equal(Oidc, connector.Kind)
        Assert.Equal(Some issuer, connector.IssuerUrl)
        Assert.True connector.IsEnabled
        Assert.True connector.IsShared
        Assert.Equal("Home", connector.DisplayName)

        let! screen = operator.GetHtml(added.Headers.Location |> string)
        Assert.Contains("data-connector-kind=\"oidc\"", screen)
        // Where to register it: exact, from the configured public origin.
        Assert.Contains("https://umbral.test/connect/home/callback", screen)
        // The issuer is shown, read-only, with why it cannot change.
        Assert.Contains(issuer, screen)
        Assert.Contains("data-issuer-fixed", screen)
        // The secret is never drawn back.
        Assert.DoesNotContain(providerSecret, screen)

        // It was asked about before it was saved, and that warmed the cache for
        // the first sign-in: a second check costs the provider nothing.
        Assert.Equal(1, stub.CountOf "/.well-known/openid-configuration")
    }

[<Fact>]
let ``the add form offers OpenID Connect, with the issuer field and the email-trust warning`` () =
    task {
        let! app, operator = start [ create issuer ] ignore []
        use _ = app

        let! screen = operator.GetHtml "/admin/connectors"
        Assert.Contains("value=\"oidc\"", screen)
        Assert.Contains("name=\"issuer\"", screen)
        Assert.Contains("data-email-trust-warning", screen)
        // `oidc` has no empty card: it is only ever added by issuer.
        Assert.DoesNotContain("data-connector-kind=\"oidc\"", screen)
        Assert.DoesNotContain("data-connector=\"oidc\"", screen)
    }

[<Fact>]
let ``a provider with a path in its issuer is added under the issuer as normalised`` () =
    task {
        let stub = create issuer
        let! app, operator = start [ stub ] ignore []
        use _ = app

        // Upper-case host and a trailing slash: one provider, one spelling.
        let! added = addProvider operator "home" "https://AUTH.example.test/application/o/umbral/"
        Assert.Contains("connector-added-oidc", added.Headers.Location |> string)

        let! stored = ConnectorStore.findBySlug app.DbConfig "home"
        Assert.Equal(Some issuer, stored.Value.IssuerUrl)
    }

// ---------------------------------------------------------------------------
// A refused add draws the form again with the input kept (T023, spec FR-005)
// ---------------------------------------------------------------------------

/// Everything the operator typed, as it must come back after a refusal.
let private assertInputKept (html: string) (issuerTyped: string) =
    Assert.Contains("value=\"Home\"", html)
    Assert.Contains("value=\"umbral-client\"", html)
    Assert.Contains($"value=\"{issuerTyped}\"", html)
    // The one thing never sent back to the browser.
    Assert.DoesNotContain(providerSecret, html)

/// A refused add: the status, the page, and that nothing was stored under `slug`.
let private refused (operator: Browser) (app: Fixtures.TestApp) (slug: string) (fields: (string * string) list) =
    task {
        let! response = operator.PostForm("/admin/connectors", "/admin/connectors", fields)
        let! html = response.Content.ReadAsStringAsync()

        Assert.Equal(enum<HttpStatusCode> 422, response.StatusCode)
        // The same screen, form and all, not a redirect.
        Assert.Contains("data-add-connector", html)

        let! stored = ConnectorStore.findBySlug app.DbConfig slug
        Assert.True(stored.IsNone, $"a connector was stored under {slug} despite the refusal")

        return html
    }

[<Fact>]
let ``a short name that breaks the rule is refused with the rule`` () =
    task {
        let! app, operator = start [ create issuer ] ignore []
        use _ = app

        let! html = refused operator app "Bad Slug!" (addFields "Bad Slug!" issuer)
        Assert.Contains("data-add-error=\"slug\"", html)
        Assert.Contains("lowercase letters, digits or hyphens", html)
        assertInputKept html issuer
    }

[<Theory>]
[<InlineData("discord")>]
[<InlineData("oidc")>]
let ``another kind's name is not an acceptable short name`` (name: string) =
    task {
        let! app, operator = start [ create issuer ] ignore []
        use _ = app

        let! html = refused operator app name (addFields name issuer)
        Assert.Contains("data-add-error=\"slug\"", html)
        Assert.Contains("name of a built-in provider", html)
        assertInputKept html issuer
    }

[<Fact>]
let ``a short name already in use is refused`` () =
    task {
        let! app, operator = start [ create issuer ] ignore []
        use _ = app

        // `fake` exists (the fixture seeds it); the form must not replace it.
        let! response = operator.PostForm("/admin/connectors", "/admin/connectors", addFields "fake" issuer)
        let! html = response.Content.ReadAsStringAsync()

        Assert.Equal(enum<HttpStatusCode> 422, response.StatusCode)
        Assert.Contains("data-add-error=\"slug\"", html)
        assertInputKept html issuer

        let! fake = ConnectorStore.findBySlug app.DbConfig "fake"
        Assert.Equal(Fake, fake.Value.Kind)
    }

[<Fact>]
let ``an issuer over plain http is refused and says to put the provider behind https`` () =
    task {
        let! app, operator = start [ create issuer ] ignore []
        use _ = app

        let typed = "http://auth.example.test/application/o/umbral"
        let! html = refused operator app "home" (addFields "home" typed)
        Assert.Contains("data-add-error=\"issuer\"", html)
        Assert.Contains("must start with https://", html)
        assertInputKept html typed
    }

[<Fact>]
let ``a relative issuer is refused`` () =
    task {
        let! app, operator = start [ create issuer ] ignore []
        use _ = app

        let typed = "/application/o/umbral"
        let! html = refused operator app "home" (addFields "home" typed)
        Assert.Contains("data-add-error=\"issuer\"", html)
        Assert.Contains("full address", html)
        assertInputKept html typed
    }

[<Fact>]
let ``a missing issuer is refused`` () =
    task {
        let! app, operator = start [ create issuer ] ignore []
        use _ = app

        let! html = refused operator app "home" (addFields "home" "")
        Assert.Contains("data-add-error=\"issuer\"", html)
        Assert.Contains("Enter the issuer URL", html)
    }

[<Fact>]
let ``an address that describes another issuer is refused naming both`` () =
    task {
        let stub = create issuer
        stub.Discovery["issuer"] <- node "https://auth.example.test/application/o/other"
        let! app, operator = start [ stub ] ignore []
        use _ = app

        let! html = refused operator app "home" (addFields "home" issuer)
        Assert.Contains("data-add-error=\"issuer\"", html)
        Assert.Contains("describes itself as", html)
        Assert.Contains("application/o/other", html)
        assertInputKept html issuer
    }

[<Fact>]
let ``a provider whose document lacks a token endpoint is refused naming the field`` () =
    task {
        let stub = create issuer
        stub.Discovery.Remove "token_endpoint" |> ignore
        let! app, operator = start [ stub ] ignore []
        use _ = app

        let! html = refused operator app "home" (addFields "home" issuer)
        Assert.Contains("token_endpoint", html)
        assertInputKept html issuer
    }

[<Fact>]
let ``a missing client secret is refused and nothing is stored`` () =
    task {
        let! app, operator = start [ create issuer ] ignore []
        use _ = app

        let! html = refused operator app "home" (addFields "home" issuer |> without [ "clientSecret" ])
        Assert.Contains("data-add-error=\"clientSecret\"", html)
        Assert.Contains("confidential client", html)
        // Typed once already: the form says the secret has to be typed again.
        Assert.Contains("type the client secret again", html.ToLowerInvariant())
    }

[<Fact>]
let ``a missing client id or display name is refused`` () =
    task {
        let! app, operator = start [ create issuer ] ignore []
        use _ = app

        let! noId = refused operator app "home" (addFields "home" issuer |> replacing "clientId" "")
        Assert.Contains("data-add-error=\"clientId\"", noId)

        let! noName = refused operator app "home" (addFields "home" issuer |> replacing "displayName" "")
        Assert.Contains("data-add-error=\"displayName\"", noName)
    }

[<Fact>]
let ``a provider that cannot be reached is refused with the reason, and no secret is stored`` () =
    task {
        let stub = create issuer
        stub.Mode <- Down
        let! app, operator = start [ stub ] ignore []
        use _ = app

        let! html = refused operator app "home" (addFields "home" issuer)
        Assert.Contains("data-add-error=\"issuer\"", html)
        Assert.Contains("could not reach", html)
        assertInputKept html issuer
    }

[<Fact>]
let ``a second connector for an issuer already in use is refused naming the first`` () =
    task {
        let! app, operator = start [ create issuer ] ignore []
        use _ = app

        let! added = addProvider operator "home" issuer
        Assert.Contains("connector-added-oidc", added.Headers.Location |> string)

        let! html = refused operator app "again" (addFields "again" issuer)
        Assert.Contains("data-add-error=\"issuer\"", html)
        Assert.Contains("Home already uses this issuer", html)
    }

[<Fact>]
let ``a refusal records nothing in the security log`` () =
    task {
        let! app, operator = start [ create issuer ] ignore []
        use _ = app

        let! before = scalarCount app "SELECT COUNT(*) FROM security_events WHERE event_type = 'connector_created'"
        let! _ = refused operator app "home" (addFields "home" "http://nope.example.test")
        let! after = scalarCount app "SELECT COUNT(*) FROM security_events WHERE event_type = 'connector_created'"
        Assert.Equal(before, after)
    }

// ---------------------------------------------------------------------------
// The sign-in page (T026, spec 003's choices)
// ---------------------------------------------------------------------------

[<Fact>]
let ``an app set to all shared connectors offers the provider under its display name`` () =
    task {
        let! app, operator = start [ create issuer ] ignore []
        use _ = app

        let! _ = addProvider operator "home" issuer
        let! clientId, _ = createClientApp operator

        let! html = pickerFor (Browser app.Client) clientId
        Assert.Contains("data-connector=\"home\"", html)
        Assert.Contains("Sign in with Home", html)
        Assert.Contains("href=\"/connect/home/start?", html)
    }

[<Fact>]
let ``a provider that is not shared is offered only to an app whose set contains it`` () =
    task {
        let! app, operator = start [ create issuer ] ignore []
        use _ = app

        // No `shared` field: the box is unticked.
        let! _ =
            operator.PostForm("/admin/connectors", "/admin/connectors", addFields "home" issuer |> without [ "shared" ])

        let! stored = ConnectorStore.findBySlug app.DbConfig "home"
        Assert.False stored.Value.IsShared

        let! everyone, _ = createClientApp operator
        let! chosen, _ = createClientApp operator
        let! another, _ = createClientApp operator
        do! AppConnectors.setChoice app.DbConfig chosen (Only(set [ "home" ]))
        do! AppConnectors.setChoice app.DbConfig another (Only(set [ "fake" ]))

        let! forEveryone = pickerFor (Browser app.Client) everyone
        let! forChosen = pickerFor (Browser app.Client) chosen
        let! forAnother = pickerFor (Browser app.Client) another

        Assert.DoesNotContain("data-connector=\"home\"", forEveryone)
        Assert.Contains("data-connector=\"home\"", forChosen)
        Assert.DoesNotContain("data-connector=\"home\"", forAnother)
    }

[<Fact>]
let ``a disabled provider is not offered`` () =
    task {
        let! app, operator = start [ create issuer ] ignore []
        use _ = app

        let! _ = addProvider operator "home" issuer
        let! clientId, _ = createClientApp operator

        let! toggled = operator.PostForm("/admin/connectors", "/admin/connectors/home/toggle", [])
        Assert.Equal(HttpStatusCode.Redirect, toggled.StatusCode)

        let! html = pickerFor (Browser app.Client) clientId
        Assert.DoesNotContain("data-connector=\"home\"", html)
    }

// ---------------------------------------------------------------------------
// A whole login (T019)
// ---------------------------------------------------------------------------

[<Fact>]
let ``a full authorization-code login reaches the app with idp set to the kind`` () =
    task {
        let stub = create issuer
        let! app, operator = start [ stub ] ignore []
        use _ = app

        let! _ = addProvider operator "home" issuer
        let! clientId, clientSecret = createClientApp operator

        let! outcome = signInThroughProvider (Browser app.Client) stub clientId "home"
        let code = signedIn outcome

        let! payload, tokens = idTokenOf app clientId clientSecret code

        // The provider KIND, as spec 003 made it for every connector: the slug
        // is not a stable value to hand an app (a connector can be deleted and
        // added back under another).
        Assert.Equal("oidc", strOf payload "idp")
        Assert.Contains("email", (strOf tokens "scope").Split(' '))

        // The person Umbral minted for that identity, keyed by issuer and sub.
        let! linked =
            Flows.scalar
                app
                "SELECT provider_kind || '|' || provider_subject FROM linked_accounts WHERE provider_kind = 'oidc'"
                []

        Assert.Equal($"oidc|{issuer}#user-1", linked)

        let! stored = Flows.scalar app "SELECT email || '|' || display_name FROM users" []
        Assert.Equal("jane@example.com|Jane Doe", stored)
    }

[<Fact>]
let ``a second login keeps the same sub and one user`` () =
    task {
        let stub = create issuer
        let! app, operator = start [ stub ] ignore []
        use _ = app

        let! _ = addProvider operator "home" issuer
        let! clientId, clientSecret = createClientApp operator

        let! first = signInThroughProvider (Browser app.Client) stub clientId "home"
        let! firstPayload, _ = idTokenOf app clientId clientSecret (signedIn first)

        let! second = signInThroughProvider (Browser app.Client) stub clientId "home"
        let! secondPayload, _ = idTokenOf app clientId clientSecret (signedIn second)

        Assert.Equal(strOf firstPayload "sub", strOf secondPayload "sub")

        let! users = scalarCount app "SELECT COUNT(*) FROM users"
        Assert.Equal(1, users)

        // The discovery document was fetched once for both: it is cached.
        Assert.Equal(1, stub.CountOf "/.well-known/openid-configuration")
    }

[<Fact>]
let ``a forged id_token fails the sign-in with the usual notice and a recorded reason`` () =
    task {
        let stub = create issuer
        let! app, operator = start [ stub ] ignore []
        use _ = app

        let! _ = addProvider operator "home" issuer
        let! clientId, _ = createClientApp operator

        stub.IdToken <- { stub.IdToken with Key = Some(stub.Attacker :> Microsoft.IdentityModel.Tokens.SecurityKey) }

        let! outcome = signInThroughProvider (Browser app.Client) stub clientId "home"

        match outcome with
        | Error landed -> Assert.Contains("msg=sign-in-failed", landed)
        | Ok _ -> failwith "a forged token signed someone in"

        let! detail =
            Flows.scalar app "SELECT detail FROM security_events WHERE event_type = 'sign_in_failed' ORDER BY id DESC LIMIT 1" []

        Assert.Contains("id_token_invalid: signature", detail)

        let! users = scalarCount app "SELECT COUNT(*) FROM users"
        Assert.Equal(0, users)
    }

[<Fact>]
let ``a provider that goes down after it was added fails the sign-in with the provider-unreachable notice`` () =
    task {
        let stub = create issuer
        let! app, operator = start [ stub ] ignore []
        use _ = app

        let! _ = addProvider operator "home" issuer
        let! clientId, _ = createClientApp operator

        // The cache already has the document, so start works; the token
        // endpoint is what is down.
        stub.TokenResponse <- Some(HttpStatusCode.InternalServerError, "boom")

        let! outcome = signInThroughProvider (Browser app.Client) stub clientId "home"

        match outcome with
        | Error landed -> Assert.Contains("msg=sign-in-failed", landed)
        | Ok _ -> failwith "expected the sign-in to fail"
    }

// ---------------------------------------------------------------------------
// Editing a provider (T024)
// ---------------------------------------------------------------------------

[<Fact>]
let ``the card saves a new name, client id and secret, and ignores a posted slug or issuer`` () =
    task {
        let stub = create issuer
        let! app, operator = start [ stub ] ignore []
        use _ = app

        let! _ = addProvider operator "home" issuer
        let! clientId, clientSecret = createClientApp operator

        let newSecret = "a-replacement-secret-5d2b8e4f"

        let! saved =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors/home",
                [ "displayName", "Home Lab"
                  "clientId", "new-client-id"
                  "clientSecret", newSecret
                  "shared", "true"
                  // Neither is a field of the card: both are ignored.
                  "slug", "elsewhere"
                  "issuer", "https://evil.example.test/o"
                  "kind", "discord" ]
            )

        Assert.Contains("connector-saved", saved.Headers.Location |> string)

        let! stored = ConnectorStore.findBySlug app.DbConfig "home"
        let connector = stored.Value
        Assert.Equal("Home Lab", connector.DisplayName)
        Assert.Equal("new-client-id", connector.ClientId)
        Assert.Equal(Some issuer, connector.IssuerUrl)
        Assert.Equal(Oidc, connector.Kind)

        let! elsewhere = ConnectorStore.findBySlug app.DbConfig "elsewhere"
        Assert.True elsewhere.IsNone

        // The very next sign-in uses them, with no restart: the new client id
        // is the audience and the new secret is what authenticates.
        let! outcome = signInThroughProvider (Browser app.Client) stub clientId "home"
        signedIn outcome |> ignore

        let token = stub.LastTokenRequest.Value
        Assert.Equal(Some "new-client-id", token.BasicClientId)
        Assert.Equal(Some newSecret, token.BasicClientSecret)

        ignore clientSecret
    }

[<Fact>]
let ``saving the card without a secret keeps the saved one`` () =
    task {
        let stub = create issuer
        let! app, operator = start [ stub ] ignore []
        use _ = app

        let! _ = addProvider operator "home" issuer
        let! clientId, _ = createClientApp operator

        let! _ =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors/home",
                [ "displayName", "Home"; "clientId", "umbral-client"; "shared", "true" ]
            )

        let! outcome = signInThroughProvider (Browser app.Client) stub clientId "home"
        signedIn outcome |> ignore
        Assert.Equal(Some providerSecret, stub.LastTokenRequest.Value.BasicClientSecret)
    }

// ---------------------------------------------------------------------------
// Export (the config file cannot declare an oidc connector yet)
// ---------------------------------------------------------------------------

[<Fact>]
let ``an export with an oidc connector omits it, says so, and still round-trips through apply`` () =
    task {
        let stub = create issuer
        let! app, operator = start [ stub ] ignore []
        use _ = app

        let! _ = addProvider operator "home" issuer
        let! clientId, _ = createClientApp operator

        // An app that chose both the fake connector and the provider.
        do! AppConnectors.setChoice app.DbConfig clientId (Only(set [ "fake"; "home" ]))

        let! export = operator.Get "/admin/config"
        let! yaml = export.Content.ReadAsStringAsync()

        // What the file says about it, and nothing it would then refuse.
        Assert.Contains("OpenID Connect providers are configured in the admin area only for now", yaml)
        Assert.DoesNotContain("kind: oidc", yaml)
        Assert.DoesNotContain("slug: home", yaml)
        Assert.DoesNotContain(issuer, yaml)
        Assert.Contains("chosen in the admin area only: home", yaml)
        Assert.DoesNotContain(providerSecret, yaml)

        // It parses: the import accepts what the export wrote.
        let document =
            match ConfigFile.parse yaml with
            | Ok document -> document
            | Error error -> failwith error.Describe

        Assert.DoesNotContain(document.Connectors, fun c -> c.Slug = "home")
        let appEntry = document.Apps |> List.find (fun a -> a.ClientId = clientId)
        Assert.Equal<string list>([ "fake" ], appEntry.Connectors |> Option.defaultValue [])

        // And it applies, on the instance it came from, leaving the provider as it was.
        let environment (_: string) : string | null = "an-environment-secret-0123456789"

        let resolved =
            match ConfigFile.resolveSecrets environment document with
            | Ok resolved -> resolved
            | Error missing -> failwith (ConfigFile.describeMissing missing)

        let! _ = ConfigFile.applyConnectors app.DbConfig app.Clock (app.Services.GetRequiredService<Secrets.ISecretStore>()) resolved

        let! kept = ConnectorStore.findBySlug app.DbConfig "home"
        Assert.Equal(Some issuer, kept.Value.IssuerUrl)
        Assert.True kept.Value.IsEnabled
    }

[<Fact>]
let ``an export with no oidc connector has no such comment`` () =
    task {
        let! app, operator = start [ create issuer ] ignore []
        use _ = app

        let! export = operator.Get "/admin/config"
        let! yaml = export.Content.ReadAsStringAsync()
        Assert.DoesNotContain("OpenID Connect", yaml)
    }

// ---------------------------------------------------------------------------
// Nothing secret is logged (T019, FR-015)
// ---------------------------------------------------------------------------

[<Fact>]
let ``no token, code or secret from an OpenID Connect login reaches a log or the security log`` () =
    task {
        let provider = new CapturingProvider()

        let customize (builder: Microsoft.AspNetCore.Builder.WebApplicationBuilder) =
            builder.Logging.SetMinimumLevel LogLevel.Trace |> ignore
            builder.Logging.AddProvider provider |> ignore

        // TRACE on every category, the way an operator chasing a problem would
        // ask for it (see LogRedactionTests, whose capture this is).
        let verbose =
            [ for category in
                  [ "Default"
                    "Microsoft.AspNetCore"
                    "Microsoft.EntityFrameworkCore.Database.Command"
                    "OpenIddict"
                    "Quartz" ] -> $"Logging:LogLevel:{category}", "Trace" ]

        let stub = create issuer
        let! app, operator = start [ stub ] customize verbose
        use _ = app

        let secrets = List<Secret>()

        let remember label (value: string) =
            Assert.True(value.Length >= MinimumSecretLength, $"the test's own secret '{label}' is too short to search for")
            secrets.Add { Label = label; Value = value }

        remember "provider client secret" providerSecret

        let! _ = addProvider operator "home" issuer
        let! clientId, clientSecret = createClientApp operator
        remember "app client secret" clientSecret

        let browser = Browser app.Client
        let! outcome = signInThroughProvider browser stub clientId "home"
        let code = signedIn outcome

        // What crossed the wire between Umbral and the provider.
        let providerCode =
            match outcome with
            | Ok(_, authorization) -> authorization.Code
            | Error landed -> failwith landed

        remember "provider authorization code" providerCode
        remember "provider id_token" stub.LastIdToken.Value
        remember "provider access token" "stub-access-token"
        remember "Umbral authorization code" code
        remember "PKCE code verifier" Verifier

        let! _, tokens = idTokenOf app clientId clientSecret code
        remember "Umbral access token" (strOf tokens "access_token")
        remember "Umbral id_token" (strOf tokens "id_token")

        // A refused one as well: the failure path logs too.
        stub.IdToken <- { stub.IdToken with Key = Some(stub.Attacker :> Microsoft.IdentityModel.Tokens.SecurityKey) }
        // A new browser: the first one is signed in already.
        let! refusedOutcome = signInThroughProvider (Browser app.Client) stub clientId "home"
        Assert.True(Result.isError refusedOutcome)
        remember "forged provider id_token" stub.LastIdToken.Value

        for cookie in browser.Cookies do
            if cookie.Value.Length >= MinimumSecretLength then
                remember $"cookie {cookie.Key}" cookie.Value

        let entries = provider.Entries
        Assert.NotEmpty entries

        Assert.True(
            entries |> Array.exists (fun e -> e.Level <= LogLevel.Debug && e.Category.StartsWith "OpenIddict"),
            "no OpenIddict debug/trace output was captured, so this test is not looking where tokens tend to leak"
        )

        // The provider is called through HttpClient, whose own logging records
        // the request line: that is the place a code in a URL would leak.
        Assert.True(
            entries |> Array.exists (fun e -> e.Category.StartsWith "System.Net.Http.HttpClient"),
            "no HttpClient log output was captured, so the calls to the provider were not looked at"
        )

        let secrets = List.ofSeq secrets

        let leaks =
            [ for entry in entries do
                  match leaksIn secrets entry.Everything with
                  | [] -> ()
                  | labels -> yield String.Join(", ", labels) + $" in [{entry.Level}] {entry.Category}" ]
            |> List.distinct

        Assert.True(List.isEmpty leaks, "secrets reached a log sink:" + Environment.NewLine + String.Join(Environment.NewLine, leaks))

        let! rows = securityEventRows app
        Assert.NotEmpty rows

        let eventLeaks = rows |> List.collect (leaksIn secrets) |> List.distinct
        Assert.True(List.isEmpty eventLeaks, "secrets reached security_events: " + String.Join(", ", eventLeaks))
    }
