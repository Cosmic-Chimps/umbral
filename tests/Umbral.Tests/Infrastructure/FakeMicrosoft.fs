/// A fake login.microsoftonline.com for the tests of the Microsoft connector
/// (specs/016-microsoft-connector).
///
/// It is built on `StubOidcProvider`, so the real adapter, the real discovery
/// cache and the real pipeline talk to it over the real `HttpClient` pipeline and
/// nothing in the product knows it is a fake. What this file adds is what is
/// Microsoft's own:
///
/// - the shape of its discovery document: read at `<authority>/<segment>/v2.0`,
///   naming an `issuer` that is NOT that address (the template for
///   `organizations` and `common`, the personal tenant's issuer for `consumers`,
///   the tenant's own for one organization), endpoints at Microsoft's real paths
///   (`/<segment>/oauth2/v2.0/authorize`, `.../token`, `/<segment>/discovery/v2.0/keys`),
///   no `code_challenge_methods_supported`, a userinfo endpoint on another host;
/// - a key set whose keys carry an `issuer` member: the template by default, and
///   helpers for a key issued to the personal tenant, a key with no issuer, and a
///   key that was never published;
/// - the claims of a Microsoft `id_token` (`tid`, `oid`, a pairwise `sub`, an
///   `iss` built from `tid`, `xms_edov`), each of which a test can bend or leave
///   out;
/// - the connector the operator would have saved, written straight to the store
///   because the admin pane is not what these tests are about.
///
/// One fake serves one audience: its stub answers only below that audience's
/// segment, so a request for another segment fails as a host that does not
/// resolve would. Microsoft's real endpoints are not all under the discovery
/// document's address (`/consumers/oauth2/v2.0/token` is not under
/// `/consumers/v2.0`), so the handler maps them onto the stub's own paths.
///
/// Test-only crypto: the keys are generated per instance and mean nothing. The
/// client ID, tenant IDs and object ID are made up.
module Umbral.Tests.Infrastructure.FakeMicrosoft

open System
open System.Net.Http
open System.Security.Cryptography
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
open Microsoft.IdentityModel.Tokens
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.StubOidcProvider

module Ms = Umbral.Domains.Microsoft

/// The tenant of personal Microsoft accounts, in its canonical spelling.
let PersonalTenant = Ms.personalTenant.ToString "D"

/// Two work tenants.
[<Literal>]
let WorkTenantA = "4f5b3d6e-27b0-4c3d-a6a3-5c9a1d0fb2e7"

[<Literal>]
let WorkTenantB = "c1d2e3f4-0a1b-4c2d-8e3f-a4b5c6d7e8f9"

/// An object ID: one person in a tenant.
[<Literal>]
let Oid = "0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d"

/// Another person.
[<Literal>]
let OtherOid = "5d6e7f80-1a2b-4c3d-9e4f-5a6b7c8d9e0f"

/// The Application (client) ID of the operator's app registration.
[<Literal>]
let ClientId = "11111111-2222-3333-4444-555555555555"

/// The client ID of a replaced registration.
[<Literal>]
let ReplacedClientId = "66666666-7777-4888-9999-aaaaaaaaaaaa"

/// What the operator pasted as the client secret.
[<Literal>]
let ClientSecret = "microsoft-client-secret-value"

/// The person's address and name, when a test gives them.
[<Literal>]
let Email = "maria@example.com"

[<Literal>]
let Name = "Maria Ruiz"

/// Microsoft's discovery document for `audience`: every field shaped as
/// login.microsoftonline.com shapes it. Note what is not there
/// (`code_challenge_methods_supported`) and what is odd (the `issuer`).
let private discoveryDocument (audience: MicrosoftAudience) =
    let segment = Ms.segment audience
    let endpoints = $"{Ms.authority}/{segment}"
    let document = JsonObject()
    document["issuer"] <- node (Ms.discoveryIssuer audience)
    document["authorization_endpoint"] <- node $"{endpoints}/oauth2/v2.0/authorize"
    document["token_endpoint"] <- node $"{endpoints}/oauth2/v2.0/token"
    document["jwks_uri"] <- node $"{endpoints}/discovery/v2.0/keys"
    document["userinfo_endpoint"] <- node "https://graph.microsoft.com/oidc/userinfo"
    document["end_session_endpoint"] <- node $"{endpoints}/oauth2/v2.0/logout"
    document["response_types_supported"] <- strings [ "code"; "id_token"; "code id_token"; "id_token token" ]
    document["response_modes_supported"] <- strings [ "query"; "fragment"; "form_post" ]
    document["subject_types_supported"] <- strings [ "pairwise" ]
    document["id_token_signing_alg_values_supported"] <- strings [ "RS256" ]
    document["scopes_supported"] <- strings [ "openid"; "profile"; "email"; "offline_access" ]
    document["token_endpoint_auth_methods_supported"] <- strings [ "client_secret_post"; "client_secret_basic" ]
    document["claims_supported"] <- strings [ "sub"; "iss"; "aud"; "exp"; "iat"; "name"; "email"; "tid"; "oid" ]
    document

/// Microsoft for one test, serving `audience`'s segment.
type FakeMicrosoft(audience: MicrosoftAudience) =
    inherit HttpMessageHandler()

    let segment = Ms.segment audience
    let stub = StubOidcProvider.create (Ms.documentBase audience)
    let invoker = new HttpMessageInvoker(stub, false)

    do
        stub.Discovery <- discoveryDocument audience
        // Microsoft's own paths are mapped onto these in `SendAsync`.
        stub.TokenPath <- "/token"
        stub.JwksPath <- "/keys"
        // Every key Microsoft publishes carries its issuer; the usual one is the
        // template, good for any tenant once `{tenantid}` is substituted.
        stub.KeyIssuers[stub.SigningKey.KeyId] <- Ms.issuerTemplate

    /// Microsoft's real endpoints are not all below the address the discovery
    /// document is read from; the stub's are. Rewritten here, so the document can
    /// say what Microsoft's says.
    override _.SendAsync(request: HttpRequestMessage, cancellationToken: CancellationToken) : Task<HttpResponseMessage> =
        match request.RequestUri with
        | null -> Task.FromException<HttpResponseMessage>(HttpRequestException "the fake Microsoft has no such host")
        | uri ->
            let mapped =
                uri.AbsoluteUri
                    .Replace($"/{segment}/oauth2/v2.0/token", $"/{segment}/v2.0/token")
                    .Replace($"/{segment}/discovery/v2.0/keys", $"/{segment}/v2.0/keys")

            request.RequestUri <- Uri mapped
            invoker.SendAsync(request, cancellationToken)

    /// The handler to put behind every provider call of the app.
    member this.Handler: HttpMessageHandler = this :> HttpMessageHandler

    /// The stub underneath, for `Now`, `IdToken`, `LastTokenRequest`,
    /// `PublishedKeys`, `KeyIssuers` and `CountOf`.
    member _.Stub = stub

    member _.Audience = audience

    /// Where Umbral must send the browser: the document's `authorization_endpoint`.
    member _.AuthorizationEndpoint = $"{Ms.authority}/{segment}/oauth2/v2.0/authorize"

    /// Microsoft's page after the person approves: the code and the state to
    /// bring back. `authorizeUri` is the URL Umbral redirected to.
    member _.Approve(authorizeUri: Uri) : Authorization = stub.Authorize authorizeUri

    /// How many times the key set was fetched.
    member _.KeyFetches = stub.CountOf "/keys"

    /// Publish one more key under `kid`, with `issuer` as its `issuer` member
    /// (None: the key has none). It does not become the signing key: a test
    /// passes it to `Asserts` to sign with it.
    member _.PublishKey(kid: string, issuer: string option) : RsaSecurityKey =
        let key = RsaSecurityKey(RSA.Create 2048)
        key.KeyId <- kid
        stub.PublishedKeys.Add key

        match issuer with
        | Some issuer -> stub.KeyIssuers[kid] <- issuer
        | None -> ()

        key

    /// A key issued to the personal tenant alone: its `issuer` is that tenant's
    /// concrete issuer, no template. Microsoft publishes two like it at `common`.
    member this.PublishPersonalKey() : RsaSecurityKey =
        this.PublishKey("key-personal", Some(Ms.issuerFor PersonalTenant))

    /// A key Microsoft signs with that its key set does not list, as after a
    /// rotation the cache has not heard of, or from a forger.
    member _.UnpublishedKey() : RsaSecurityKey =
        let key = RsaSecurityKey(RSA.Create 2048)
        key.KeyId <- "key-unpublished"
        key

    /// Microsoft rotates: a new key signs, and only it is published, with the
    /// template as its issuer as every real key has one.
    member _.Rotate() : RsaSecurityKey =
        let key = stub.Rotate()
        stub.KeyIssuers[key.KeyId] <- Ms.issuerTemplate
        key

    /// The `id_token` Microsoft asserts for the next sign-in.
    ///
    /// `iss` is the template with `tid` unless `iss` says otherwise; `sub` is some
    /// pairwise string unless `sub` says otherwise. `email` and `xmsEdov` are left
    /// out when not given (`xmsEdov` is any JSON type: a boxed `bool` is the JSON
    /// boolean, a boxed `string` the JSON string, a boxed `int` the JSON number).
    /// `omit` names claims to leave out whatever else says (`"oid"`, `"tid"`), and
    /// `claims` sets claims by name, replacing the ones above. `key` signs; None is
    /// the current key.
    member _.Asserts
        (
            tid: string,
            oid: string,
            ?email: string,
            ?xmsEdov: obj,
            ?iss: string,
            ?key: SecurityKey,
            ?sub: string,
            ?omit: string list,
            ?claims: (string * obj) list
        ) =
        let replaced = defaultArg claims [] |> List.map fst
        let omitted = defaultArg omit []

        let made: (string * obj) list =
            [ yield "tid", (tid :> obj)
              yield "oid", (oid :> obj)
              yield "name", (Name :> obj)

              match email with
              | Some address -> yield "email", (address :> obj)
              | None -> ()

              match xmsEdov with
              | Some value -> yield "xms_edov", value
              | None -> () ]

        let all =
            (made |> List.filter (fun (name, _) -> not (List.contains name replaced)))
            @ defaultArg claims []
            |> List.filter (fun (name, _) -> not (List.contains name omitted))

        stub.IdToken <-
            { defaultIdToken with
                Subject = defaultArg sub $"pairwise-{oid}"
                Claims = all
                Issuer = Some(defaultArg iss (Ms.issuerFor tid))
                Key = key }

/// A fake Microsoft for `audience`, with its key published.
let create (audience: MicrosoftAudience) = new FakeMicrosoft(audience)

/// Point the fake at the app's clock, so the lifetimes of the tokens it mints
/// agree with the clock Umbral validates against.
let attach (app: Fixtures.TestApp) (fake: FakeMicrosoft) =
    fake.Stub.Now <- fun () -> app.Clock.GetUtcNow()

/// The Microsoft connector the operator would have saved: enabled, slug
/// `microsoft`, `clientId` as its Application ID, the secret in the real secret
/// store as the callback reads it, `audience` as its choice. Written straight to
/// the store; saving again with another client ID replaces it.
let seedConnectorWith (app: Fixtures.TestApp) (audience: MicrosoftAudience) (clientId: string) =
    task {
        let! reference = Fixtures.putSecret app "microsoft" ClientSecret

        do!
            ConnectorStore.save
                app.DbConfig
                app.Clock
                { Slug = "microsoft"
                  Kind = ProviderKind.Microsoft
                  DisplayName = "Microsoft"
                  ClientId = clientId
                  ClientSecretRef = reference.Encoded
                  ClientSecretHint = ""
                  IssuerUrl = None
                  AllowedEmailDomains = []
                  PasskeyEnrolment = InvitationsOnly
                  FediverseAccess = FediverseAccess.Any
                  AppleKey = None
                  MicrosoftAudience = Some audience
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None
    }

/// `seedConnectorWith` the usual client ID.
let seedConnector (app: Fixtures.TestApp) (audience: MicrosoftAudience) = seedConnectorWith app audience ClientId
