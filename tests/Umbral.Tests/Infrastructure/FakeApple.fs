/// A fake Apple for the tests of Sign in with Apple (specs/013).
///
/// It is built on `StubOidcProvider`, so the real adapter, the real discovery
/// cache and the real pipeline talk to it over the real `HttpClient` pipeline
/// and nothing in the product knows it is a fake. What this file adds to the
/// stub is what is Apple's own:
///
/// - the address and the document: issuer `https://appleid.apple.com`, a
///   discovery document shaped like Apple's (no `userinfo_endpoint`, no
///   `code_challenge_methods_supported`, `client_secret_post` only);
/// - the operator's key pair, and a token endpoint that judges the client secret
///   the way Apple does, answering `400 invalid_client` for one that is wrong,
///   and keeping every client secret it received so a test can decode them;
/// - Apple's page, as a function: the form it posts back to Umbral, with the
///   `state`, the `code`, and the `user` field that carries the name once;
/// - the connector the operator would have saved, written straight to the store
///   because the admin screen for Apple is not what these tests are about.
///
/// Test-only crypto: the keys are generated per instance and mean nothing. The
/// Team ID, Key ID and Services ID are made up.
///
/// About claim types: `IdTokenSpec.Claims` takes `(name, obj)` and the library
/// writes each value by its runtime type. A boxed `bool` comes out as the JSON
/// boolean and a boxed `string` as the JSON string, so `true`, `"true"`, `"TRUE"`
/// and `1` (a boxed `int`, a JSON number) are all expressible; a test in
/// AppleSignInTests pins that down so a library change cannot turn a string into
/// a boolean behind the `email_verified` tests' backs.
module Umbral.Tests.Infrastructure.FakeApple

open System
open System.Collections.Concurrent
open System.Net.Http
open System.Security.Cryptography
open System.Threading.Tasks
open Microsoft.IdentityModel.JsonWebTokens
open Microsoft.IdentityModel.Tokens
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.StubOidcProvider
open Umbral.Tests.Infrastructure.Journeys

[<Literal>]
let Issuer = "https://appleid.apple.com"

[<Literal>]
let TeamId = "TEAM123456"

[<Literal>]
let KeyId = "KEY1234567"

[<Literal>]
let ServicesId = "com.example.umbral.signin"

/// What Apple posts in the `user` field, once, on the first authorization.
/// The `email` in it is deliberately not the one in the `id_token`'s default.
let mariaUser = """{"name":{"firstName":"Maria","lastName":"Ruiz"},"email":"maria@example.com"}"""

/// Apple's discovery document, field for field what appleid.apple.com serves:
/// note what is not there (userinfo, PKCE methods).
let private discoveryDocument () =
    let document = System.Text.Json.Nodes.JsonObject()
    document["issuer"] <- node Issuer
    document["authorization_endpoint"] <- node (Issuer + "/auth/authorize")
    document["token_endpoint"] <- node (Issuer + "/auth/token")
    document["jwks_uri"] <- node (Issuer + "/auth/keys")
    document["response_types_supported"] <- strings [ "code" ]
    document["response_modes_supported"] <- strings [ "query"; "fragment"; "form_post" ]
    document["subject_types_supported"] <- strings [ "pairwise" ]
    document["id_token_signing_alg_values_supported"] <- strings [ "RS256" ]
    document["scopes_supported"] <- strings [ "openid"; "email"; "name" ]
    document["token_endpoint_auth_methods_supported"] <- strings [ "client_secret_post" ]
    document

/// Apple allows a client secret to live six months at most.
let private longestLifetime = TimeSpan.FromDays 183.0

/// Apple, for one test. `key` is the operator's key: the private half is what the
/// test stores as the connector's secret, the public half is all Apple keeps.
type FakeApple(key: ECDsa) =
    let stub = StubOidcProvider.create Issuer
    let received = ConcurrentQueue<string>()

    let publicKey =
        let parameters = key.ExportParameters false
        let half = ECDsaSecurityKey(ECDsa.Create parameters)
        half.KeyId <- KeyId
        half

    /// Judges the client secret of a token request as Apple does. Every client
    /// secret posted is kept first, whether or not it is any good.
    let judge (request: TokenRequest) : Task<bool> =
        task {
            let secret = request.Fields |> Map.tryFind "client_secret"
            let clientId = request.Fields |> Map.tryFind "client_id"

            match secret with
            | None -> return false
            | Some secret ->
                received.Enqueue secret

                if clientId <> Some ServicesId then
                    return false
                else
                    let parameters = TokenValidationParameters()
                    parameters.ValidIssuer <- TeamId
                    parameters.ValidAudience <- Issuer
                    parameters.IssuerSigningKey <- publicKey
                    parameters.ValidAlgorithms <- [ SecurityAlgorithms.EcdsaSha256 ]
                    // The lifetime is judged below against the app's clock,
                    // which is the one the stub reads, not the library's.
                    parameters.ValidateLifetime <- false

                    let! result = JsonWebTokenHandler().ValidateTokenAsync(secret, parameters)

                    match result.SecurityToken with
                    | :? JsonWebToken as token when result.IsValid ->
                        let now = stub.Now()
                        let issuedAt = DateTimeOffset token.IssuedAt
                        let expires = DateTimeOffset token.ValidTo

                        return
                            token.Kid = KeyId
                            && token.Subject = ServicesId
                            && expires > now
                            && issuedAt <= now.AddMinutes 1.0
                            && expires - issuedAt <= longestLifetime
                    | _ -> return false
        }

    do
        stub.Discovery <- discoveryDocument ()
        stub.TokenPath <- "/auth/token"
        stub.JwksPath <- "/auth/keys"
        stub.ClientCheck <- Some judge

    new() = FakeApple(ECDsa.Create ECCurve.NamedCurves.nistP256)

    /// The handler to put behind every provider call of the app.
    member _.Handler: HttpMessageHandler = stub :> HttpMessageHandler

    /// The stub underneath, for `Now`, `IdToken`, `LastTokenRequest` and the
    /// refusal switches.
    member _.Stub = stub

    /// The operator's private key as the `.p8` file holds it: what a test stores
    /// as the connector's secret.
    member _.PrivateKeyPem = key.ExportPkcs8PrivateKeyPem()

    /// Every client secret the token endpoint received, oldest first.
    member _.ClientSecrets = received |> Seq.toList

    /// How many requests reached the token endpoint, accepted or not.
    member _.TokenRequests = stub.CountOf "/auth/token"

    /// Apple's page after the person approves: the form it posts to Umbral. It
    /// asserts nothing. `authorizeUri` is the URL Umbral redirected to. The code
    /// is bound to that request's nonce and PKCE challenge, as the stub's
    /// `Authorize` does it. `user` is the JSON of the `user` field, which Apple
    /// sends on the first authorization only.
    member _.Approve(authorizeUri: Uri, user: string option) : (string * string) list =
        let authorization = stub.Authorize authorizeUri

        [ "state", authorization.State
          "code", authorization.Code ]
        @ (user |> Option.map (fun json -> "user", json) |> Option.toList)

    /// Apple's page after the person presses Cancel: the `state` and the error,
    /// and no code.
    member _.Cancel(authorizeUri: Uri) : (string * string) list =
        let parameters = System.Web.HttpUtility.ParseQueryString authorizeUri.Query

        [ "state", (match parameters["state"] with | null -> "" | state -> state)
          "error", "user_cancelled_authorize" ]

    /// The `id_token` Apple asserts for the next sign-in: `sub`, and the claims
    /// that are present. A claim given as None is left out, which is the case a
    /// test about "no `email_verified`" needs.
    member _.Asserts(subject: string, email: string option, verified: obj option, privateEmail: obj option) =
        let claims =
            [ yield! email |> Option.map (fun address -> "email", (address :> obj)) |> Option.toList
              yield! verified |> Option.map (fun value -> "email_verified", value) |> Option.toList
              yield! privateEmail |> Option.map (fun value -> "is_private_email", value) |> Option.toList ]

        stub.IdToken <- { defaultIdToken with Subject = subject; Claims = claims }

    /// The usual person: an address of their own, verified, not a relay.
    member this.AssertsVerifiedEmail(subject: string, email: string) =
        this.Asserts(subject, Some email, Some(("true" :> obj)), Some(("false" :> obj)))

/// A fake Apple with the operator's key, with the stub reading the app's clock
/// once the app exists (see `attach`).
let create () = FakeApple()

/// Point the fake at the app's clock, so the lifetimes of the tokens it mints
/// and judges agree with the clock Umbral validates against.
let attach (app: Fixtures.TestApp) (fake: FakeApple) =
    fake.Stub.Now <- fun () -> app.Clock.GetUtcNow()

/// The Apple connector the operator would have saved, with `pem` as its stored
/// key: `fake.PrivateKeyPem` for a working one, anything else to make it wrong.
/// Written straight to the store, with the key through the real secret store as
/// the callback reads it.
let seedConnectorWith (app: Fixtures.TestApp) (pem: string) =
    task {
        let! reference = Fixtures.putSecret app "apple" pem

        do!
            ConnectorStore.save
                app.DbConfig
                app.Clock
                { Slug = "apple"
                  Kind = ProviderKind.Apple
                  DisplayName = "Apple"
                  ClientId = ServicesId
                  ClientSecretRef = reference.Encoded
                  ClientSecretHint = ""
                  IssuerUrl = None
                  AllowedEmailDomains = []
                  PasskeyEnrolment = InvitationsOnly
                  FediverseAccess = FediverseAccess.Any
                  AppleKey = Some { TeamId = TeamId; KeyId = KeyId }
                  MicrosoftAudience = None
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None
    }

/// `seedConnectorWith` the key Apple knows.
let seedConnector (app: Fixtures.TestApp) (fake: FakeApple) = seedConnectorWith app fake.PrivateKeyPem

/// The browser posts Apple's form to Umbral's callback, with its cookies, as a
/// browser does after Apple's page.
let post (browser: Browser) (fields: (string * string) list) : Task<HttpResponseMessage> =
    browser.PostWithoutToken("/connect/apple/callback", fields)
