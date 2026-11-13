/// `OidcDiscovery.CheckAt`: a document read at one address that names another
/// issuer, as Microsoft's shared endpoints do (specs/016 research R1). The
/// document is accepted for the issuer the caller expects and for nothing else,
/// and `Check`, which expects the address itself, still refuses it.
module Umbral.Tests.OidcDiscoveryCheckAtTests

open System
open System.Net.Http
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
open Xunit
open Umbral.Infrastructure.OidcDiscovery
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.StubOidcProvider

let private documentBase = "https://login.microsoftonline.com/common/v2.0"
let private template = "https://login.microsoftonline.com/{tenantid}/v2.0"

/// A provider served at `documentBase` whose document names the template.
let private templated () =
    let stub = create documentBase
    stub.Discovery["issuer"] <- node template
    stub

let private discoveryOver (stub: StubOidcProvider) =
    // `false`: the client does not own the stub, so disposing it leaves it be.
    OidcDiscovery(new HttpClient(stub, false), false, TimeSpan.FromSeconds 10.0, TimeProvider.System)

let private wrongAbout (word: string) (result: DiscoveryResult) =
    match result with
    | Wrong reason -> Assert.Contains(word, reason)
    | other -> failwith $"expected a wrong document naming '{word}', got {other}"

[<Fact>]
let ``a document at one address that names a template issuer is discovered when that template is expected`` () =
    task {
        let stub = templated ()

        match! (discoveryOver stub).CheckAt(documentBase, template) with
        | Discovered document ->
            Assert.Equal(template, document.Issuer)
            Assert.Equal(documentBase + "/authorize", document.AuthorizationEndpoint)
            Assert.Contains(document.SigningKeys, fun key -> key.KeyId = "key-1")
        | other -> failwith $"expected Discovered, got {other}"
    }

[<Fact>]
let ``the same document is wrong when another issuer is expected`` () =
    task {
        let stub = templated ()

        let! result = (discoveryOver stub).CheckAt(documentBase, "https://login.microsoftonline.com/consumers/v2.0")
        wrongAbout template result
    }

[<Fact>]
let ``the same document is wrong for Check, which expects the address it was read from`` () =
    task {
        let stub = templated ()

        let! result = (discoveryOver stub).Check documentBase
        wrongAbout template result
    }

[<Fact>]
let ``a wrong answer is not cached: the next check asks again`` () =
    task {
        let stub = templated ()
        let discovery = discoveryOver stub

        let! first = discovery.CheckAt(documentBase, "https://example.test/other")
        wrongAbout template first

        // The provider is the same, but the caller now expects the right issuer.
        match! discovery.CheckAt(documentBase, template) with
        | Discovered _ -> ()
        | other -> failwith $"expected Discovered, got {other}"

        Assert.Equal(2, stub.CountOf "/.well-known/openid-configuration")
    }

[<Fact>]
let ``a trailing slash on the expected issuer is not a difference`` () =
    task {
        let stub = templated ()

        match! (discoveryOver stub).CheckAt(documentBase, template + "/") with
        | Discovered _ -> ()
        | other -> failwith $"expected Discovered, got {other}"
    }

/// Adds Microsoft's `issuer` member to every key of the key set the stub serves.
type private KeyIssuerHandler(inner: StubOidcProvider, keyIssuer: string) =
    inherit HttpMessageHandler()

    override _.SendAsync(request: HttpRequestMessage, cancellationToken: CancellationToken) : Task<HttpResponseMessage> =
        task {
            // `SendAsync` is protected; the invoker is the public way in.
            let invoker = new HttpMessageInvoker(inner, false)
            let! response = invoker.SendAsync(request, cancellationToken)

            let path =
                match request.RequestUri with
                | null -> ""
                | uri -> uri.AbsolutePath

            if path.EndsWith "/jwks" then
                let! body = response.Content.ReadAsStringAsync cancellationToken

                match JsonNode.Parse body with
                | null -> ()
                | set ->
                    match set["keys"] with
                    | null -> ()
                    | keys ->
                        for key in keys.AsArray() do
                            match key with
                            | null -> ()
                            | key -> key["issuer"] <- node keyIssuer

                    response.Content <- new StringContent(set.ToJsonString(), Text.Encoding.UTF8, "application/json")

            return response
        }

/// The Microsoft adapter reads a key's `issuer` from `AdditionalData`
/// (specs/016 research R2): this is the library behaviour it relies on.
[<Fact>]
let ``a key's unknown issuer member is kept as a string on the discovered key set`` () =
    task {
        let stub = templated ()
        let handler = new KeyIssuerHandler(stub, template)
        let discovery = OidcDiscovery(new HttpClient(handler, false), false, TimeSpan.FromSeconds 10.0, TimeProvider.System)

        match! discovery.CheckAt(documentBase, template) with
        | Discovered document ->
            let key = Seq.exactlyOne document.JsonWebKeySet.Keys
            Assert.Equal("key-1", key.Kid)
            Assert.Equal(template, Assert.IsType<string>(key.AdditionalData["issuer"]))
        | other -> failwith $"expected Discovered, got {other}"
    }

let private invalidTenantBody =
    """{"error":"invalid_tenant","error_description":"AADSTS90002: Tenant 'x' not found. Trace ID: 0000 Correlation ID: 1111"}"""

/// Microsoft's answer to a tenant ID that names no tenant: HTTP 400 and
/// `invalid_tenant`. It is a wrong address, not an outage, and Microsoft's
/// description (trace IDs) is not kept.
[<Fact>]
let ``a 400 with the error invalid_tenant is wrong, without Microsoft's description`` () =
    task {
        let stub = templated ()
        stub.DiscoveryResponse <- Some(Net.HttpStatusCode.BadRequest, invalidTenantBody, "application/json")

        match! (discoveryOver stub).CheckAt(documentBase, template) with
        | Wrong reason ->
            Assert.Equal("No Microsoft tenant has this ID", reason)
            Assert.DoesNotContain("AADSTS", reason)
            Assert.DoesNotContain("Trace", reason)
        | other -> failwith $"expected Wrong, got {other}"
    }

/// Nothing but that exact pair reads as wrong: another status, another error,
/// a body that is not an object or not JSON, an error that is not a string.
[<Theory>]
[<InlineData(500, """{"error":"invalid_tenant"}""")>]
[<InlineData(404, """{"error":"invalid_tenant"}""")>]
[<InlineData(400, """{"error":"invalid_request"}""")>]
[<InlineData(400, """{"error":{"x":"invalid_tenant"}}""")>]
[<InlineData(400, """{"error_description":"invalid_tenant"}""")>]
[<InlineData(400, """["invalid_tenant"]""")>]
[<InlineData(400, "invalid_tenant")>]
[<InlineData(400, "")>]
let ``any other failed answer stays unreachable`` (status: int, body: string) =
    task {
        let stub = templated ()
        stub.DiscoveryResponse <- Some(enum<Net.HttpStatusCode> status, body, "application/json")

        match! (discoveryOver stub).CheckAt(documentBase, template) with
        | Unreachable _ -> ()
        | other -> failwith $"expected Unreachable, got {other}"
    }
