/// The discovery cache against the stub identity provider: what counts as
/// reachable, what counts as wrong, and that a good answer is asked for once
/// (specs/002 research R2). Every check is on the outcome, and on the word the
/// operator needs in the reason, never on the whole sentence.
module Umbral.Tests.OidcDiscoveryTests

open System
open System.Net
open System.Net.Http
open System.Text.Json.Nodes
open Xunit
open Umbral.Infrastructure.OidcDiscovery
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.StubOidcProvider

let private issuer = "https://auth.example.test/application/o/umbral"

let private discovery (stub: StubOidcProvider) allowLocalhostHttp timeout =
    // `false`: the client does not own the stub, so disposing it leaves it be.
    OidcDiscovery(new HttpClient(stub, false), allowLocalhostHttp, timeout, TimeProvider.System)

let private ordinary (stub: StubOidcProvider) = discovery stub false (TimeSpan.FromSeconds 10.0)

let private text (value: string) : JsonNode = node value

let private strings (values: string list) : JsonNode = StubOidcProvider.strings values :> JsonNode

let private wrongAbout (word: string) (result: DiscoveryResult) =
    match result with
    | Wrong reason -> Assert.Contains(word, reason)
    | other -> failwith $"expected a wrong document naming '{word}', got {other}"

let private unreachableAbout (word: string) (result: DiscoveryResult) =
    match result with
    | Unreachable reason -> Assert.Contains(word, reason)
    | other -> failwith $"expected an unreachable provider naming '{word}', got {other}"

[<Fact>]
let ``a provider that answers correctly is discovered, with its endpoints and keys`` () =
    task {
        let stub = create issuer

        match! (ordinary stub).Check issuer with
        | Discovered document ->
            Assert.Equal(issuer, document.Issuer)
            Assert.Equal(issuer + "/token", document.TokenEndpoint)
            Assert.Equal(issuer + "/authorize", document.AuthorizationEndpoint)
            Assert.Equal(issuer + "/jwks", document.JwksUri)
            // The keys came with it: the signature check needs no second trip.
            Assert.Contains(document.SigningKeys, fun key -> key.KeyId = "key-1")
        | other -> failwith $"expected Discovered, got {other}"
    }

[<Fact>]
let ``a provider that cannot be reached is unreachable, not wrong`` () =
    task {
        let stub = create issuer
        stub.Mode <- Down

        let! result = (ordinary stub).Check issuer
        unreachableAbout "connection" result
    }

[<Fact>]
let ``a provider that never answers is unreachable once the timeout passes`` () =
    task {
        let stub = create issuer
        stub.Mode <- Hanging

        let! result = (discovery stub false (TimeSpan.FromMilliseconds 300.0)).Check issuer
        unreachableAbout "no answer" result
    }

[<Fact>]
let ``an error status is unreachable and says the provider answered with an error`` () =
    task {
        let stub = create issuer
        stub.DiscoveryResponse <- Some(HttpStatusCode.InternalServerError, "boom", "text/plain")

        let! result = (ordinary stub).Check issuer
        unreachableAbout "error" result
    }

[<Fact>]
let ``an html page where the document should be is unreachable`` () =
    task {
        let stub = create issuer
        stub.DiscoveryResponse <- Some(HttpStatusCode.OK, "<html><body>Sign in to your proxy</body></html>", "text/html")

        let! result = (ordinary stub).Check issuer
        unreachableAbout "discovery document" result
    }

[<Fact>]
let ``a json body that is not an object is unreachable`` () =
    task {
        let stub = create issuer
        stub.DiscoveryResponse <- Some(HttpStatusCode.OK, "[1, 2, 3]", "application/json")

        match! (ordinary stub).Check issuer with
        | Unreachable _ -> ()
        | other -> failwith $"expected Unreachable, got {other}"
    }

[<Fact>]
let ``a document that names another issuer is wrong, and says both`` () =
    task {
        let stub = create issuer
        stub.Discovery["issuer"] <- text "https://other.example.test/o"

        match! (ordinary stub).Check issuer with
        | Wrong reason ->
            Assert.Contains("https://other.example.test/o", reason)
            Assert.Contains(issuer, reason)
        | other -> failwith $"expected Wrong, got {other}"
    }

[<Fact>]
let ``a document with no issuer is wrong`` () =
    task {
        let stub = create issuer
        stub.Discovery.Remove "issuer" |> ignore

        let! result = (ordinary stub).Check issuer
        wrongAbout "describes itself" result
    }

[<Fact>]
let ``a missing required endpoint is wrong and the field is named`` () =
    task {
        for field in [ "authorization_endpoint"; "token_endpoint"; "jwks_uri" ] do
            let stub = create issuer
            stub.Discovery.Remove field |> ignore

            let! result = (ordinary stub).Check issuer
            wrongAbout field result
    }

[<Fact>]
let ``an endpoint that is not https is wrong and the field is named`` () =
    task {
        for field in [ "authorization_endpoint"; "token_endpoint"; "userinfo_endpoint" ] do
            let stub = create issuer
            stub.Discovery[field] <- text "http://auth.example.test/plain"

            let! result = (ordinary stub).Check issuer
            wrongAbout field result
            wrongAbout "https" result
    }

[<Fact>]
let ``a plain-http jwks_uri is refused before the keys are fetched`` () =
    task {
        let stub = create issuer
        stub.Discovery["jwks_uri"] <- text "http://auth.example.test/application/o/umbral/jwks"

        let! result = (ordinary stub).Check issuer
        wrongAbout "jwks_uri" result

        // Keys over plain http would be keys an attacker on the path could swap.
        Assert.Equal(0, stub.CountOf "/jwks")
    }

[<Fact>]
let ``a provider that only signs with a shared secret is wrong`` () =
    task {
        let stub = create issuer
        stub.Discovery["id_token_signing_alg_values_supported"] <- strings [ "HS256" ]

        let! result = (ordinary stub).Check issuer
        wrongAbout "shared secret" result
    }

[<Fact>]
let ``one asymmetric algorithm among others is enough`` () =
    task {
        let stub = create issuer
        stub.Discovery["id_token_signing_alg_values_supported"] <- strings [ "HS256"; "ES256" ]

        match! (ordinary stub).Check issuer with
        | Discovered _ -> ()
        | other -> failwith $"expected Discovered, got {other}"
    }

[<Fact>]
let ``a provider without the authorization code flow is wrong`` () =
    task {
        let stub = create issuer
        stub.Discovery["response_types_supported"] <- strings [ "id_token"; "token" ]

        let! result = (ordinary stub).Check issuer
        wrongAbout "response_types_supported" result
    }

[<Fact>]
let ``a provider with no supported client authentication is wrong`` () =
    task {
        let stub = create issuer
        stub.Discovery["token_endpoint_auth_methods_supported"] <- strings [ "private_key_jwt"; "tls_client_auth" ]

        let! result = (ordinary stub).Check issuer
        wrongAbout "client_secret_basic" result
    }

[<Fact>]
let ``optional fields that are absent are not held against the provider`` () =
    task {
        let stub = create issuer

        // The spec's defaults apply: no listed response types, algorithms or
        // authentication methods, and no userinfo endpoint.
        for field in
            [ "response_types_supported"
              "id_token_signing_alg_values_supported"
              "token_endpoint_auth_methods_supported"
              "code_challenge_methods_supported"
              "userinfo_endpoint" ] do
            stub.Discovery.Remove field |> ignore

        match! (ordinary stub).Check issuer with
        | Discovered _ -> ()
        | other -> failwith $"expected Discovered, got {other}"
    }

[<Fact>]
let ``a trailing slash on either side of the issuer comparison is accepted`` () =
    task {
        // The provider writes its issuer with a slash; the operator typed none.
        let withSlash = create issuer
        withSlash.Discovery["issuer"] <- text (issuer + "/")

        match! (ordinary withSlash).Check issuer with
        | Discovered _ -> ()
        | other -> failwith $"document with slash, typed without: {other}"

        // The operator typed a slash; the provider writes none.
        let withoutSlash = create issuer

        match! (ordinary withoutSlash).Check(issuer + "/") with
        | Discovered _ -> ()
        | other -> failwith $"document without slash, typed with: {other}"
    }

[<Fact>]
let ``the host of the issuer is compared without regard to case`` () =
    task {
        let stub = create issuer

        match! (ordinary stub).Check "https://AUTH.example.test/application/o/umbral" with
        | Discovered _ -> ()
        | other -> failwith $"expected Discovered, got {other}"
    }

[<Fact>]
let ``a second check is answered from the cache`` () =
    task {
        let stub = create issuer
        let cache = ordinary stub

        let! first = cache.Check issuer
        let! second = cache.Check issuer
        let! third = cache.Check(issuer + "/")

        for result in [ first; second; third ] do
            match result with
            | Discovered _ -> ()
            | other -> failwith $"expected Discovered, got {other}"

        // One fetch of the document and one of the keys, however many asked,
        // and however the issuer was spelled.
        Assert.Equal(1, stub.CountOf "/.well-known/openid-configuration")
        Assert.Equal(1, stub.CountOf "/jwks")
    }

[<Fact>]
let ``a provider that was down is asked again, not remembered as down`` () =
    task {
        let stub = create issuer
        let cache = ordinary stub
        stub.Mode <- Down

        let! down = cache.Check issuer
        unreachableAbout "connection" down

        // Back up, and the very next check sees it: the library alone would
        // answer from its memory of the failure for a moment.
        stub.Mode <- Up

        match! cache.Check issuer with
        | Discovered _ -> ()
        | other -> failwith $"expected Discovered after the provider came back, got {other}"
    }

[<Fact>]
let ``a document that was wrong is asked again, not remembered as wrong`` () =
    task {
        let stub = create issuer
        let cache = ordinary stub
        stub.Discovery["issuer"] <- text "https://other.example.test/o"

        let! wrong = cache.Check issuer
        wrongAbout "describes itself" wrong

        // The operator fixes the provider's setting.
        stub.Discovery["issuer"] <- text issuer

        match! cache.Check issuer with
        | Discovered _ -> ()
        | other -> failwith $"expected Discovered after the fix, got {other}"
    }

[<Fact>]
let ``a provider that goes down after a good answer is still served from the cache`` () =
    task {
        let stub = create issuer
        let cache = ordinary stub

        let! first = cache.Check issuer
        stub.Mode <- Down
        let! second = cache.Check issuer

        for result in [ first; second ] do
            match result with
            | Discovered _ -> ()
            | other -> failwith $"expected Discovered, got {other}"
    }

[<Fact>]
let ``an issuer that is not https is wrong and the provider is never contacted`` () =
    task {
        let stub = create "http://auth.example.test/o"

        let! result = (ordinary stub).Check "http://auth.example.test/o"
        wrongAbout "https://" result
        Assert.Empty stub.Requests
    }

[<Fact>]
let ``a relative issuer is wrong and the provider is never contacted`` () =
    task {
        let stub = create issuer

        let! result = (ordinary stub).Check "/application/o/umbral"
        wrongAbout "https://" result
        Assert.Empty stub.Requests
    }

[<Fact>]
let ``a loopback issuer over plain http is accepted only where the instance allows it`` () =
    task {
        let local = "http://localhost:8080/realms/home"

        // Not allowed: refused without a request.
        let refusing = create local
        let! refused = (discovery refusing false (TimeSpan.FromSeconds 10.0)).Check local
        wrongAbout "https://" refused
        Assert.Empty refusing.Requests

        // Allowed (Development with the insecure flag): discovered, jwks_uri included.
        let allowing = create local

        match! (discovery allowing true (TimeSpan.FromSeconds 10.0)).Check local with
        | Discovered document -> Assert.Equal(local, document.Issuer)
        | other -> failwith $"expected Discovered, got {other}"
    }

[<Fact>]
let ``two issuers are cached apart`` () =
    task {
        let one = create "https://one.example.test/o"
        let two = create "https://two.example.test/o"
        let cache = OidcDiscovery(new HttpClient(new StubRouter([ one; two ]), false), false)

        let! first = cache.Check "https://one.example.test/o"
        let! second = cache.Check "https://two.example.test/o"

        match first, second with
        | Discovered a, Discovered b ->
            Assert.Equal("https://one.example.test/o", a.Issuer)
            Assert.Equal("https://two.example.test/o", b.Issuer)
        | _ -> failwith "both providers should be discovered"

        Assert.Equal(1, one.CountOf "/.well-known/openid-configuration")
        Assert.Equal(1, two.CountOf "/.well-known/openid-configuration")
    }

[<Fact>]
let ``a refresh after a key rotation returns the new keys at once`` () =
    task {
        let stub = create issuer
        let cache = ordinary stub

        let! _ = cache.Check issuer
        let rotated = stub.Rotate()

        // The cache has not looked yet, and says what it last knew.
        match! cache.Check issuer with
        | Discovered document -> Assert.DoesNotContain(document.SigningKeys, fun key -> key.KeyId = rotated.KeyId)
        | other -> failwith $"expected Discovered, got {other}"

        // Asked to refresh, it answers with the new keys, not with the old ones
        // while it fetches in the background.
        match! cache.Refresh issuer with
        | Discovered document -> Assert.Contains(document.SigningKeys, fun key -> key.KeyId = rotated.KeyId)
        | other -> failwith $"expected Discovered, got {other}"
    }

[<Fact>]
let ``forced refreshes are limited to one in five minutes per issuer`` () =
    task {
        let stub = create issuer
        let clock = Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow)
        let cache = OidcDiscovery(new HttpClient(stub, false), false, TimeSpan.FromSeconds 10.0, clock)

        let! _ = cache.Check issuer
        Assert.Equal(1, stub.CountOf "/jwks")

        let! _ = cache.Refresh issuer
        Assert.Equal(2, stub.CountOf "/jwks")

        // A flood of tokens with invented key ids asks again and again.
        for _ in 1..5 do
            clock.Advance(TimeSpan.FromSeconds 30.0)
            let! _ = cache.Refresh issuer
            ()

        Assert.Equal(2, stub.CountOf "/jwks")

        // Past the window, it is allowed once more.
        clock.Advance(TimeSpan.FromMinutes 5.0)
        let! _ = cache.Refresh issuer
        Assert.Equal(3, stub.CountOf "/jwks")
    }

[<Fact>]
let ``refreshing an issuer that is not https is wrong and contacts nothing`` () =
    task {
        let stub = create issuer

        let! result = (ordinary stub).Refresh "http://auth.example.test/o"
        wrongAbout "https://" result
        Assert.Empty stub.Requests
    }
