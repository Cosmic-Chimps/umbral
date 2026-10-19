/// The OpenID Connect adapter against the stub identity provider
/// (specs/002 Phase 3): the authorize URL it builds, how it authenticates to the
/// token endpoint, the identity it accepts, and — one test per row of the
/// contract's "Accepted identity" table — every identity it refuses, each with
/// the reason the security log would record.
///
/// The adapter, the discovery cache and the stub share one fake clock, so token
/// lifetimes are judged on the time the test says it is.
module Umbral.Tests.OidcAdapterTests

open System
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Threading.Tasks
open Microsoft.Extensions.Time.Testing
open Microsoft.IdentityModel.Tokens
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure.OidcDiscovery
open Umbral.Infrastructure.ProviderAdapters
open Umbral.Infrastructure.Providers.Oidc
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.StubOidcProvider

let private issuer = "https://auth.example.test/application/o/umbral"
let private clientId = "umbral-client"
let private clientSecret = "a-client-secret-with-a:colon-and-%percent"
let private redirectUri = Uri "https://umbral.test/connect/home/callback"
let private nonce = "nonce-0123456789"
let private state = "state-0123456789"

let private connector: Connector =
    { Slug = "home"
      Kind = Oidc
      DisplayName = "Home"
      ClientId = clientId
      ClientSecretRef = "local:unused"
      ClientSecretHint = "ount"
      IssuerUrl = Some issuer
      AllowedEmailDomains = []
      PasskeyEnrolment = InvitationsOnly
      IsEnabled = true
      IsShared = true
      CreatedUtc = DateTimeOffset.UtcNow
      UpdatedUtc = DateTimeOffset.UtcNow }

/// A stub provider, the clock everything agrees on, and the adapter over them.
type private Rig =
    { Stub: StubOidcProvider
      Clock: FakeTimeProvider
      Adapter: IProviderAdapter }

let private newRig () =
    let clock = FakeTimeProvider(DateTimeOffset.UtcNow)
    let stub = create issuer
    stub.Now <- fun () -> clock.GetUtcNow()

    // `false`: the client does not own the stub, so disposing it leaves it be.
    let discovery = OidcDiscovery(new HttpClient(stub, false), false, TimeSpan.FromSeconds 10.0, clock)
    let adapter = OidcAdapter(new HttpClient(stub, false), discovery, clock)

    { Stub = stub
      Clock = clock
      Adapter = adapter :> IProviderAdapter }

let private authorizeRequest (endpoints: ProviderEndpoints) : AuthorizeRequest =
    { Connector = connector
      Endpoints = endpoints
      RedirectUri = redirectUri
      State = state
      CodeChallenge = Flows.Challenge
      Nonce = nonce
      HostedDomainHint = None }

let private resolved (rig: Rig) =
    task {
        match! rig.Adapter.Resolve connector with
        | Ok endpoints -> return endpoints
        | Error error -> return failwith $"expected the provider to resolve, got {error.Detail}"
    }

/// The browser's whole trip for one login: resolve, authorize, hand the code
/// back, exchange it. The profile Umbral would sign in, or why it would not.
let private login (rig: Rig) =
    task {
        let! endpoints = resolved rig
        let url = rig.Adapter.BuildAuthorizeUrl(authorizeRequest endpoints)
        let authorization = rig.Stub.Authorize url

        return!
            rig.Adapter.ExchangeAndFetch
                { Connector = connector
                  ClientSecret = clientSecret
                  RedirectUri = redirectUri
                  Code = authorization.Code
                  CodeVerifier = Flows.Verifier
                  Nonce = nonce }
    }

let private accepted (result: Result<ProviderProfile, ConnectorError>) =
    match result with
    | Ok profile -> profile
    | Error error -> failwith $"expected the sign-in to be accepted, got {error.Detail}"

/// The recorded reason is what the operator reads in the security log, so it is
/// what is asserted — and never anything about the token itself.
let private refusedAs (reason: string) (result: Result<ProviderProfile, ConnectorError>) =
    match result with
    | Error error -> Assert.Contains(reason, error.Detail)
    | Ok _ -> failwith $"expected the sign-in to be refused as {reason}, but it was accepted"

/// One whole login, expected to be refused for `reason`.
let private refuses (reason: string) (rig: Rig) =
    task {
        let! result = login rig
        refusedAs reason result
    }

let private withToken (rig: Rig) (change: IdTokenSpec -> IdTokenSpec) =
    rig.Stub.IdToken <- change rig.Stub.IdToken

let private claimsWithout (names: string list) (spec: IdTokenSpec) =
    { spec with Claims = spec.Claims |> List.filter (fun (name, _) -> not (List.contains name names)) }

// ---------------------------------------------------------------------------
// The authorize URL
// ---------------------------------------------------------------------------

[<Fact>]
let ``the authorize URL carries the scopes, the S256 challenge, nonce, state and the exact redirect`` () =
    task {
        let rig = newRig ()
        let! endpoints = resolved rig
        let url = rig.Adapter.BuildAuthorizeUrl(authorizeRequest endpoints)

        let expected =
            issuer
            + "/authorize?client_id=umbral-client"
            + "&redirect_uri=https%3A%2F%2Fumbral.test%2Fconnect%2Fhome%2Fcallback"
            + "&response_type=code"
            + "&scope=openid%20email%20profile"
            + "&state=state-0123456789"
            + $"&code_challenge={Flows.Challenge}"
            + "&code_challenge_method=S256"
            + "&nonce=nonce-0123456789"

        Assert.Equal(expected, url.AbsoluteUri)
    }

[<Fact>]
let ``an OpenID Connect provider is never sent Google's hosted-domain hint`` () =
    task {
        let rig = newRig ()
        let! endpoints = resolved rig
        let url = rig.Adapter.BuildAuthorizeUrl { authorizeRequest endpoints with HostedDomainHint = Some "acme.com" }
        Assert.DoesNotContain("hd=", url.ToString())
    }

[<Fact>]
let ``an authorization endpoint that already has a query keeps it and gains the parameters`` () =
    task {
        let rig = newRig ()
        rig.Stub.Discovery["authorization_endpoint"] <- node (issuer + "/authorize?tenant=home")
        let! endpoints = resolved rig
        let url = rig.Adapter.BuildAuthorizeUrl(authorizeRequest endpoints)

        Assert.StartsWith(issuer + "/authorize?tenant=home&client_id=umbral-client&", url.AbsoluteUri)
    }

[<Fact>]
let ``PKCE is left out when the provider lists only plain`` () =
    task {
        let rig = newRig ()
        rig.Stub.Discovery["code_challenge_methods_supported"] <- strings [ "plain" ]

        let! endpoints = resolved rig
        let url = rig.Adapter.BuildAuthorizeUrl(authorizeRequest endpoints)

        Assert.DoesNotContain("code_challenge", url.Query)
        // State and nonce still protect the round trip.
        Assert.Contains("state=", url.Query)
        Assert.Contains("nonce=", url.Query)

        let! result = login rig
        accepted result |> ignore

        // No challenge was sent, so no verifier is: a provider that does not do
        // PKCE would not know what to do with one.
        let token = rig.Stub.LastTokenRequest.Value
        Assert.False(token.Fields.ContainsKey "code_verifier")
    }

[<Fact>]
let ``PKCE is sent when the provider does not say what it supports`` () =
    task {
        let rig = newRig ()
        rig.Stub.Discovery.Remove "code_challenge_methods_supported" |> ignore

        let! endpoints = resolved rig
        let url = rig.Adapter.BuildAuthorizeUrl(authorizeRequest endpoints)
        Assert.Contains("code_challenge_method=S256", url.Query)
    }

[<Fact>]
let ``resolving a provider that cannot be reached says so, and one that is wrong says what is wrong`` () =
    task {
        let rig = newRig ()
        rig.Stub.Mode <- Down

        match! rig.Adapter.Resolve connector with
        | Error(ProviderUnreachable _) -> ()
        | other -> failwith $"expected ProviderUnreachable, got {other}"

        let wrong = newRig ()
        wrong.Stub.Discovery["issuer"] <- node "https://elsewhere.example.test"

        match! wrong.Adapter.Resolve connector with
        | Error(MalformedResponse reason) -> Assert.Contains("elsewhere.example.test", reason)
        | other -> failwith $"expected MalformedResponse, got {other}"
    }

// ---------------------------------------------------------------------------
// The token request
// ---------------------------------------------------------------------------

[<Fact>]
let ``the token request authenticates with client_secret_basic by default`` () =
    task {
        let rig = newRig ()
        let! result = login rig
        accepted result |> ignore

        let token = rig.Stub.LastTokenRequest.Value

        // Form-encoded before it is joined, so a `:` or `%` in the secret
        // survives (RFC 6749 §2.3.1).
        Assert.Equal(Some clientId, token.BasicClientId)
        Assert.Equal(Some clientSecret, token.BasicClientSecret)
        Assert.False(token.Fields.ContainsKey "client_secret")
        Assert.Equal("authorization_code", token.Fields["grant_type"])
        Assert.Equal(redirectUri.ToString(), token.Fields["redirect_uri"])
        Assert.Equal(Flows.Verifier, token.Fields["code_verifier"])
    }

[<Fact>]
let ``the token request falls back to client_secret_post when basic is not listed`` () =
    task {
        let rig = newRig ()
        rig.Stub.Discovery["token_endpoint_auth_methods_supported"] <- strings [ "client_secret_post" ]

        let! result = login rig
        accepted result |> ignore

        let token = rig.Stub.LastTokenRequest.Value
        Assert.Equal(None, token.BasicClientId)
        Assert.Equal(clientId, token.Fields["client_id"])
        Assert.Equal(clientSecret, token.Fields["client_secret"])
    }

[<Fact>]
let ``the token request uses basic when the document does not say`` () =
    task {
        let rig = newRig ()
        rig.Stub.Discovery.Remove "token_endpoint_auth_methods_supported" |> ignore

        let! result = login rig
        accepted result |> ignore
        Assert.Equal(Some clientId, rig.Stub.LastTokenRequest.Value.BasicClientId)
    }

[<Fact>]
let ``a token endpoint that rejects the code is a provider rejection, not an identity`` () =
    task {
        let rig = newRig ()
        rig.Stub.TokenResponse <- Some(Net.HttpStatusCode.BadRequest, """{"error":"invalid_client"}""")

        match! login rig with
        | Error(ProviderRejected reason) -> Assert.Equal("invalid_client", reason)
        | other -> failwith $"expected ProviderRejected, got {other}"
    }

[<Fact>]
let ``a token endpoint that is down is unreachable, one that refuses without saying why is rejected`` () =
    task {
        let rig = newRig ()
        rig.Stub.TokenResponse <- Some(Net.HttpStatusCode.BadGateway, "<html>Bad gateway</html>")

        match! login rig with
        | Error(ProviderUnreachable reason) -> Assert.Contains("502", reason)
        | other -> failwith $"expected ProviderUnreachable, got {other}"

        rig.Stub.TokenResponse <- Some(Net.HttpStatusCode.Forbidden, "<html>Forbidden</html>")

        match! login rig with
        | Error(ProviderRejected reason) -> Assert.Contains("403", reason)
        | other -> failwith $"expected ProviderRejected, got {other}"
    }

// ---------------------------------------------------------------------------
// What is accepted
// ---------------------------------------------------------------------------

[<Fact>]
let ``a valid login yields the issuer-qualified subject, email, name and picture`` () =
    task {
        let rig = newRig ()

        withToken rig (fun spec ->
            { spec with
                Claims = spec.Claims @ [ "picture", ("https://auth.example.test/avatars/jane.png" :> obj) ] })

        let! result = login rig
        let profile = accepted result

        Assert.Equal($"{issuer}#user-1", profile.Subject)
        Assert.Equal(Some "jane@example.com", profile.Email)
        Assert.True profile.EmailVerified
        Assert.Equal(Some "Jane Doe", profile.DisplayName)
        Assert.Equal(Some "https://auth.example.test/avatars/jane.png", profile.AvatarUrl)
    }

[<Fact>]
let ``the name falls back to preferred_username`` () =
    task {
        let rig = newRig ()

        withToken rig (fun spec ->
            { claimsWithout [ "name" ] spec with Claims = (claimsWithout [ "name" ] spec).Claims @ [ "preferred_username", ("jane" :> obj) ] })

        let! result = login rig
        Assert.Equal(Some "jane", (accepted result).DisplayName)
    }

[<Fact>]
let ``an issuer claim with a trailing slash is accepted for an issuer stored without one`` () =
    task {
        let rig = newRig ()
        withToken rig (fun spec -> { spec with Issuer = Some(issuer + "/") })

        let! result = login rig
        accepted result |> ignore
    }

[<Fact>]
let ``several audiences are accepted when azp names this client`` () =
    task {
        let rig = newRig ()

        withToken rig (fun spec ->
            { spec with
                Audience = Some [ clientId; "another-client" ]
                Claims = spec.Claims @ [ "azp", (clientId :> obj) ] })

        let! result = login rig
        accepted result |> ignore
    }

[<Fact>]
let ``the string "true" is not a verified email, only the JSON literal is`` () =
    task {
        let rig = newRig ()

        withToken rig (fun spec ->
            { spec with Claims = spec.Claims |> List.map (fun (name, value) -> if name = "email_verified" then name, ("true" :> obj) else name, value) })

        let! stringly = login rig
        Assert.False (accepted stringly).EmailVerified

        withToken rig (fun spec ->
            { spec with Claims = spec.Claims |> List.map (fun (name, value) -> if name = "email_verified" then name, (false :> obj) else name, value) })

        let! no = login rig
        Assert.False (accepted no).EmailVerified

        withToken rig (fun spec -> claimsWithout [ "email_verified" ] spec)
        let! absent = login rig
        Assert.False (accepted absent).EmailVerified
    }

[<Fact>]
let ``userinfo fills an email and a name the id_token left out, with the flag that came with the email`` () =
    task {
        let rig = newRig ()
        withToken rig (claimsWithout [ "email"; "email_verified"; "name" ])

        rig.Stub.UserInfo["email"] <- node "jane@home.example"
        rig.Stub.UserInfo["email_verified"] <- (System.Text.Json.Nodes.JsonValue.Create true :> System.Text.Json.Nodes.JsonNode)
        rig.Stub.UserInfo["name"] <- node "Jane From Userinfo"

        let! result = login rig
        let profile = accepted result

        Assert.Equal(Some "jane@home.example", profile.Email)
        Assert.True profile.EmailVerified
        Assert.Equal(Some "Jane From Userinfo", profile.DisplayName)
        Assert.Equal(1, rig.Stub.CountOf "/userinfo")
    }

[<Fact>]
let ``userinfo is not called when the id_token already has an email and a name`` () =
    task {
        let rig = newRig ()
        let! result = login rig
        accepted result |> ignore
        Assert.Equal(0, rig.Stub.CountOf "/userinfo")
    }

[<Fact>]
let ``a provider with no userinfo endpoint still signs a person in without an email`` () =
    task {
        let rig = newRig ()
        withToken rig (claimsWithout [ "email"; "email_verified"; "name" ])
        rig.Stub.Discovery.Remove "userinfo_endpoint" |> ignore

        let! result = login rig
        let profile = accepted result
        Assert.Equal(None, profile.Email)
        Assert.Equal(None, profile.DisplayName)
    }

// ---------------------------------------------------------------------------
// What is refused: one test per row of the contract's table
// ---------------------------------------------------------------------------

[<Fact>]
let ``a token response with no id_token is refused as id_token_missing`` () =
    task {
        let rig = newRig ()
        rig.Stub.OmitIdToken <- true
        do! refuses "id_token_missing" rig
    }

[<Fact>]
let ``an id_token signed by a key the provider does not publish is refused as a bad signature`` () =
    task {
        let rig = newRig ()
        // Same `kid` as the real key, so the forged signature is looked up and
        // then fails, rather than being skipped for an unknown key.
        withToken rig (fun spec -> { spec with Key = Some(rig.Stub.Attacker :> SecurityKey) })
        do! refuses "id_token_invalid: signature" rig
    }

[<Fact>]
let ``an unsigned id_token (alg none) is refused as a bad algorithm`` () =
    task {
        let rig = newRig ()
        withToken rig (fun spec -> { spec with Unsigned = true })
        do! refuses "id_token_invalid: algorithm" rig
    }

[<Fact>]
let ``an id_token signed with a shared secret (HS256) is refused as a bad algorithm`` () =
    task {
        let rig = newRig ()
        // What an attacker who knows the client secret would sign with.
        let shared = SymmetricSecurityKey(Encoding.UTF8.GetBytes(clientSecret.PadRight(48, 'x')))

        withToken rig (fun spec ->
            { spec with
                Algorithm = SecurityAlgorithms.HmacSha256
                Key = Some(shared :> SecurityKey) })

        do! refuses "id_token_invalid: algorithm" rig
    }

[<Fact>]
let ``an id_token from another issuer is refused`` () =
    task {
        let rig = newRig ()
        withToken rig (fun spec -> { spec with Issuer = Some "https://evil.example.test" })
        do! refuses "id_token_invalid: issuer" rig
    }

[<Fact>]
let ``an id_token addressed to another client is refused`` () =
    task {
        let rig = newRig ()
        withToken rig (fun spec -> { spec with Audience = Some [ "someone-else" ] })
        do! refuses "id_token_invalid: audience" rig
    }

[<Fact>]
let ``several audiences without a matching azp are refused`` () =
    task {
        let rig = newRig ()
        withToken rig (fun spec -> { spec with Audience = Some [ clientId; "another-client" ] })
        do! refuses "id_token_invalid: audience" rig

        withToken rig (fun spec -> { spec with Claims = spec.Claims @ [ "azp", ("another-client" :> obj) ] })
        do! refuses "id_token_invalid: audience" rig
    }

[<Fact>]
let ``an id_token that expired more than two minutes ago is refused, one within the skew is not`` () =
    task {
        let rig = newRig ()
        let now = rig.Clock.GetUtcNow()

        withToken rig (fun spec -> { spec with ExpiresAt = Some(now - TimeSpan.FromMinutes 3.0) })
        do! refuses "id_token_invalid: expired" rig

        withToken rig (fun spec -> { spec with ExpiresAt = Some(now - TimeSpan.FromSeconds 90.0) })
        let! result = login rig
        accepted result |> ignore
    }

[<Fact>]
let ``token lifetime is judged on Umbral's clock, not the system's`` () =
    task {
        let rig = newRig ()

        // The provider mints at a fixed instant; only Umbral's clock moves.
        let minted = rig.Clock.GetUtcNow()
        rig.Stub.Now <- fun () -> minted

        let! fresh = login rig
        accepted fresh |> ignore

        // The token lives five minutes and the skew is two: eight minutes on,
        // the very same token is expired.
        rig.Clock.Advance(TimeSpan.FromMinutes 8.0)
        do! refuses "id_token_invalid: expired" rig

        // A provider whose clock is ten minutes ahead issues a token that is
        // not valid yet.
        rig.Stub.Now <- fun () -> rig.Clock.GetUtcNow() + TimeSpan.FromMinutes 10.0
        do! refuses "id_token_invalid: not_yet_valid" rig
    }

[<Fact>]
let ``an id_token for another sign-in attempt is refused as a nonce mismatch`` () =
    task {
        let rig = newRig ()
        withToken rig (fun spec -> { spec with Nonce = Replace "the-nonce-of-another-attempt" })
        do! refuses "id_token_invalid: nonce" rig

        withToken rig (fun spec -> { spec with Nonce = Omit })
        do! refuses "id_token_invalid: nonce" rig
    }

[<Fact>]
let ``a userinfo answer about another subject refuses the sign-in`` () =
    task {
        let rig = newRig ()
        withToken rig (claimsWithout [ "email"; "email_verified"; "name" ])
        rig.Stub.UserInfo["sub"] <- node "someone-else"
        rig.Stub.UserInfo["email"] <- node "victim@example.com"

        do! refuses "userinfo_subject_mismatch" rig
    }

[<Fact>]
let ``a reason never carries the token, the code or the secret`` () =
    task {
        let rig = newRig ()
        withToken rig (fun spec -> { spec with Key = Some(rig.Stub.Attacker :> SecurityKey) })

        match! login rig with
        | Error error ->
            Assert.DoesNotContain(clientSecret, error.Detail)
            Assert.DoesNotContain("eyJ", error.Detail)
        | Ok _ -> failwith "expected a refusal"
    }

// ---------------------------------------------------------------------------
// Key rotation (FR-013)
// ---------------------------------------------------------------------------

[<Fact>]
let ``a provider that rotates its signing key is followed after one refresh`` () =
    task {
        let rig = newRig ()

        let! before = login rig
        accepted before |> ignore
        Assert.Equal(1, rig.Stub.CountOf "/jwks")

        // New key, new `kid`, the old key no longer published.
        rig.Stub.Rotate() |> ignore

        let! after = login rig
        accepted after |> ignore

        Assert.Equal(2, rig.Stub.CountOf "/jwks")

        // And it stays followed: the new keys are the cached ones now.
        let! again = login rig
        accepted again |> ignore
        Assert.Equal(2, rig.Stub.CountOf "/jwks")
    }

[<Fact>]
let ``a token with a kid the provider never publishes is refused and does not make Umbral hammer the provider`` () =
    task {
        let rig = newRig ()

        let! warm = login rig
        accepted warm |> ignore

        let invented = RsaSecurityKey(RSA.Create 2048)
        invented.KeyId <- "key-that-was-never-published"
        withToken rig (fun spec -> { spec with Key = Some(invented :> SecurityKey) })

        do! refuses "id_token_invalid: signature" rig
        let afterFirst = rig.Stub.CountOf "/jwks"

        // Within the five-minute throttle a second invented kid costs the
        // provider nothing.
        do! refuses "id_token_invalid: signature" rig
        Assert.Equal(afterFirst, rig.Stub.CountOf "/jwks")
    }
