/// An identity provider that lives inside the test process.
///
/// It is an `HttpMessageHandler` registered through the provider-HTTP seam
/// (`ProviderAdapters.PrimaryHandler`, specs/002 research R9), so the OpenID
/// Connect adapter, the discovery cache and the admin screens talk to it over
/// the real `HttpClient` pipeline and nothing in the product knows it is a stub.
///
/// What it serves, under its issuer URL: the discovery document, the JWKS, an
/// authorization endpoint (called as a function, since no browser is involved),
/// a token endpoint that mints `id_token`s with Microsoft.IdentityModel, and a
/// userinfo endpoint. Everything a refusal test needs to bend is a public
/// mutable property: the discovery fields, the key that signs, the claims.
///
/// Test-only crypto: the keys are generated per instance and mean nothing.
module Umbral.Tests.Infrastructure.StubOidcProvider

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
open Microsoft.IdentityModel.JsonWebTokens
open Microsoft.IdentityModel.Tokens

/// How the provider answers at all. `Down` is a refused connection, `Hanging`
/// is a host that accepts and never replies (so a timeout, not a failure).
type StubMode =
    | Up
    | Down
    | Hanging

/// What the `nonce` claim of a minted token holds.
type NonceClaim =
    /// The nonce the authorize request carried, as a correct provider does.
    | Echo
    | Replace of string
    | Omit

/// Everything about the `id_token` a test may want different. Defaults describe
/// a correct token; a refusal test changes the one field it is about.
type IdTokenSpec =
    { Subject: string
      /// Extra claims by name. A value may be a bool, a string or a number: the
      /// JSON type is the point of the `email_verified` tests.
      Claims: (string * obj) list
      /// None: the provider's own issuer.
      Issuer: string option
      /// None: the client ID that authenticated at the token endpoint. Some []
      /// leaves the claim out.
      Audience: string list option
      Nonce: NonceClaim
      /// None: five minutes after the stub's clock.
      ExpiresAt: DateTimeOffset option
      Algorithm: string
      /// None: the provider's current signing key.
      Key: SecurityKey option
      /// Sign nothing: the header says `alg: none`.
      Unsigned: bool }

let defaultIdToken =
    { Subject = "user-1"
      Claims =
        [ "email", ("jane@example.com" :> obj)
          "email_verified", (true :> obj)
          "name", ("Jane Doe" :> obj) ]
      Issuer = None
      Audience = None
      Nonce = Echo
      ExpiresAt = None
      Algorithm = SecurityAlgorithms.RsaSha256
      Key = None
      Unsigned = false }

/// What the token endpoint last received, for the assertions about client
/// authentication and PKCE.
type TokenRequest =
    { Fields: Map<string, string>
      /// From the `Authorization: Basic` header, when one was sent.
      BasicClientId: string option
      BasicClientSecret: string option }

/// What `Authorize` hands back: the pieces a test needs to call Umbral's
/// callback as the browser would.
type Authorization =
    { Code: string
      State: string
      RedirectUri: string }

/// A code the stub issued, and what it must remember to honour at the token
/// endpoint.
type private IssuedCode =
    { Nonce: string
      CodeChallenge: string option }

let private newKey (keyId: string) =
    let key = RsaSecurityKey(RSA.Create 2048)
    key.KeyId <- keyId
    key

/// `JsonValue.Create` is annotated as possibly null; for a string it never is.
let node (value: string) : JsonNode =
    match JsonValue.Create value with
    | null -> failwith "JsonValue.Create returned null for a string"
    | created -> created :> JsonNode

let private json (status: HttpStatusCode) (body: string) =
    new HttpResponseMessage(status, Content = new StringContent(body, Encoding.UTF8, "application/json"))

let private query (text: string) =
    let parsed = System.Web.HttpUtility.ParseQueryString text

    // Null in the framework's annotations on both the keys and the values; a key
    // with no value reads as empty.
    parsed.AllKeys
    |> Seq.choose Option.ofObj
    |> Seq.map (fun key -> key, (match parsed[key] with | null -> "" | value -> value))
    |> Map.ofSeq

/// A JSON array of strings, for the discovery fields that are lists.
let strings (values: string list) = JsonArray(values |> List.map node |> Array.ofList)

/// The document a conforming provider publishes. A test removes or replaces a
/// field on `StubOidcProvider.Discovery` to make it wrong.
let defaultDiscovery (issuer: string) =
    let root = issuer.TrimEnd '/'
    let document = JsonObject()
    document["issuer"] <- node (issuer)
    document["authorization_endpoint"] <- node ($"{root}/authorize")
    document["token_endpoint"] <- node ($"{root}/token")
    document["userinfo_endpoint"] <- node ($"{root}/userinfo")
    document["jwks_uri"] <- node ($"{root}/jwks")
    document["response_types_supported"] <- strings [ "code" ]
    document["subject_types_supported"] <- strings [ "public" ]
    document["id_token_signing_alg_values_supported"] <- strings [ "RS256" ]
    document["token_endpoint_auth_methods_supported"] <- strings [ "client_secret_basic"; "client_secret_post" ]
    document["code_challenge_methods_supported"] <- strings [ "S256" ]
    document["scopes_supported"] <- strings [ "openid"; "email"; "profile" ]
    document

/// A fresh object for every provider: a test that edits userinfo must not edit
/// the next test's.
let private defaultUserInfo () =
    let claims = JsonObject()
    claims["sub"] <- node ("user-1")
    claims

/// A provider at `issuer`, e.g. `https://auth.example.test/application/o/umbral`.
/// Requests for any other address fail as a host that does not resolve would.
type StubOidcProvider(issuer: string) =
    inherit HttpMessageHandler()

    let root = issuer.TrimEnd '/'
    let rootUri = Uri root
    let codes = ConcurrentDictionary<string, IssuedCode>()
    let requests = ConcurrentQueue<string>()
    let mutable keyCounter = 1
    let mutable lastToken: TokenRequest option = None

    /// The key that signs, and the one the JWKS publishes.
    let mutable signingKey = newKey "key-1"

    member _.Issuer = issuer
    member _.Root = root

    member val Mode = Up with get, set

    /// The discovery document as served. Mutate it to make it wrong.
    member val Discovery: JsonObject = defaultDiscovery issuer with get, set

    /// When set, the discovery URL answers with this instead of the document:
    /// a 500, or an HTML page where JSON was expected.
    member val DiscoveryResponse: (HttpStatusCode * string * string) option = None with get, set

    /// The keys the JWKS lists. Starts as `SigningKey` alone.
    member val PublishedKeys: List<RsaSecurityKey> = List<RsaSecurityKey>() with get

    /// A key that is NOT published, with the same `kid` as the real one so a
    /// forged signature is looked up and then fails, not skipped.
    member val Attacker: RsaSecurityKey = newKey "key-1" with get

    /// What `Now` reads; a test points it at the app's fake clock so token
    /// lifetimes agree with the clock Umbral validates against.
    member val Now: unit -> DateTimeOffset = (fun () -> DateTimeOffset.UtcNow) with get, set

    member val IdToken: IdTokenSpec = defaultIdToken with get, set

    /// The token endpoint omits the `id_token` altogether.
    member val OmitIdToken = false with get, set

    /// Replace the token endpoint's answer with this status and body.
    member val TokenResponse: (HttpStatusCode * string) option = None with get, set

    /// What userinfo returns. `sub` defaults to the `id_token`'s.
    member val UserInfo: JsonObject = defaultUserInfo () with get, set

    member _.SigningKey = signingKey

    member _.LastTokenRequest = lastToken

    /// The `id_token` the token endpoint last handed out, for a test that
    /// searches the logs for it.
    member val LastIdToken: string option = None with get, set

    /// Every request received, as `METHOD /path`, oldest first.
    member _.Requests = requests |> Seq.toList

    /// How many requests ended with `suffix`, e.g. `/jwks`.
    member _.CountOf(suffix: string) =
        requests |> Seq.filter (fun r -> r.EndsWith suffix) |> Seq.length

    /// Sign with a new key under a new `kid` and publish only that one: what a
    /// provider does on a key rotation (FR-013).
    member this.Rotate() =
        keyCounter <- keyCounter + 1
        signingKey <- newKey $"key-{keyCounter}"
        this.PublishedKeys.Clear()
        this.PublishedKeys.Add signingKey
        signingKey

    /// What the browser's trip to the authorization endpoint ends with: a code
    /// bound to this request's nonce and PKCE challenge, and the state to echo.
    /// `authorizeUri` is the URL Umbral redirected to.
    member _.Authorize(authorizeUri: Uri) : Authorization =
        let parameters = query authorizeUri.Query
        let code = Guid.NewGuid().ToString "N"

        codes[code] <-
            { Nonce = parameters |> Map.tryFind "nonce" |> Option.defaultValue ""
              CodeChallenge = parameters |> Map.tryFind "code_challenge" }

        { Code = code
          State = parameters |> Map.tryFind "state" |> Option.defaultValue ""
          RedirectUri = parameters |> Map.tryFind "redirect_uri" |> Option.defaultValue "" }

    /// A signed token as the token endpoint would mint it. Public so an adapter
    /// test can hand one straight to the code under test.
    member this.MintIdToken(spec: IdTokenSpec, clientId: string, requestNonce: string) : string =
        let now = this.Now()
        let expires = spec.ExpiresAt |> Option.defaultValue (now.AddMinutes 5.0)
        // Microsoft.IdentityModel refuses a token that expires before it
        // starts, so an already-expired one has to have started earlier too.
        let notBefore = if expires - TimeSpan.FromMinutes 1.0 < now then expires - TimeSpan.FromMinutes 1.0 else now

        let claims = Dictionary<string, obj>()

        for name, value in spec.Claims do
            claims[name] <- value

        claims["sub"] <- (spec.Subject :> obj)

        match spec.Nonce with
        | Echo -> claims["nonce"] <- (requestNonce :> obj)
        | Replace nonce -> claims["nonce"] <- (nonce :> obj)
        | Omit -> ()

        let descriptor = SecurityTokenDescriptor()
        descriptor.Issuer <- spec.Issuer |> Option.defaultValue issuer
        descriptor.IssuedAt <- Nullable notBefore.UtcDateTime
        descriptor.NotBefore <- Nullable notBefore.UtcDateTime
        descriptor.Expires <- Nullable expires.UtcDateTime

        match spec.Audience |> Option.defaultValue [ clientId ] with
        | [] -> ()
        | [ single ] -> descriptor.Audience <- single
        | several -> claims["aud"] <- (Array.ofList several :> obj)

        descriptor.Claims <- claims

        if not spec.Unsigned then
            let key: SecurityKey = spec.Key |> Option.defaultValue (signingKey :> SecurityKey)
            descriptor.SigningCredentials <- SigningCredentials(key, spec.Algorithm)

        JsonWebTokenHandler().CreateToken descriptor

    member private this.JwksJson() =
        let keys = JsonArray()

        for key in this.PublishedKeys do
            let parameters = key.Rsa.ExportParameters false
            let jwk = JsonObject()
            jwk["kty"] <- node ("RSA")
            jwk["use"] <- node ("sig")
            jwk["alg"] <- node ("RS256")
            jwk["kid"] <- node (key.KeyId)
            jwk["n"] <- node (Base64UrlEncoder.Encode parameters.Modulus)
            jwk["e"] <- node (Base64UrlEncoder.Encode parameters.Exponent)
            keys.Add jwk

        let set = JsonObject()
        set["keys"] <- keys
        set.ToJsonString()

    member private this.Token(request: HttpRequestMessage) =
        task {
            let! body =
                match request.Content with
                | null -> Task.FromResult ""
                | content -> content.ReadAsStringAsync()

            let fields = query body

            let basic =
                match request.Headers.Authorization with
                | null -> None
                | header when header.Scheme = "Basic" ->
                    match header.Parameter with
                    | null -> None
                    | encoded ->
                        match (Encoding.UTF8.GetString(Convert.FromBase64String encoded)).Split(':', 2) with
                        | [| id; secret |] -> Some(Uri.UnescapeDataString id, Uri.UnescapeDataString secret)
                        | _ -> None
                | _ -> None

            lastToken <-
                Some
                    { Fields = fields
                      BasicClientId = basic |> Option.map fst
                      BasicClientSecret = basic |> Option.map snd }

            match this.TokenResponse with
            | Some(status, text) -> return json status text
            | None ->

            let clientId =
                match basic with
                | Some(id, _) -> id
                | None -> fields |> Map.tryFind "client_id" |> Option.defaultValue ""

            match fields |> Map.tryFind "code" |> Option.bind (fun code -> match codes.TryRemove code with | true, issued -> Some issued | _ -> None) with
            | None -> return json HttpStatusCode.BadRequest """{"error":"invalid_grant"}"""
            | Some issued ->

            // A real provider checks the PKCE verifier against the challenge it
            // was given; so does this one, which makes a missing or wrong
            // verifier in Umbral a test failure rather than a silent pass.
            let pkceHolds =
                match issued.CodeChallenge with
                | None -> true
                | Some challenge ->
                    match fields |> Map.tryFind "code_verifier" with
                    | None -> false
                    | Some verifier ->
                        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes verifier)) = challenge

            if not pkceHolds then
                return json HttpStatusCode.BadRequest """{"error":"invalid_grant","error_description":"PKCE verification failed"}"""
            else

            let response = JsonObject()
            response["access_token"] <- node ("stub-access-token")
            response["token_type"] <- node ("Bearer")
            response["expires_in"] <- JsonValue.Create 3600

            if not this.OmitIdToken then
                let idToken = this.MintIdToken(this.IdToken, clientId, issued.Nonce)
                this.LastIdToken <- Some idToken
                response["id_token"] <- node idToken

            return json HttpStatusCode.OK (response.ToJsonString())
        }

    override this.SendAsync(request: HttpRequestMessage, cancellationToken: CancellationToken) : Task<HttpResponseMessage> =
        task {
            // Only this provider's own address answers. Anything else is a host
            // that does not resolve, which is what an operator who mistypes the
            // issuer would meet.
            let uri =
                match request.RequestUri with
                | null -> raise (HttpRequestException "the stub provider has no such host")
                | uri when not (uri.AbsoluteUri.StartsWith(root + "/", StringComparison.Ordinal)) ->
                    raise (HttpRequestException "the stub provider has no such host")
                | uri -> uri

            let path = uri.AbsolutePath.Substring(rootUri.AbsolutePath.TrimEnd('/').Length)
            requests.Enqueue $"{request.Method} {path}"

            match this.Mode with
            | Down -> return raise (HttpRequestException "the stub provider is down")
            | Hanging ->
                do! Task.Delay(Timeout.Infinite, cancellationToken)
                return new HttpResponseMessage()
            | Up ->

            match request.Method.Method, path with
            | "GET", "/.well-known/openid-configuration" ->
                match this.DiscoveryResponse with
                | Some(status, body, mediaType) ->
                    return new HttpResponseMessage(status, Content = new StringContent(body, Encoding.UTF8, mediaType))
                | None -> return json HttpStatusCode.OK (this.Discovery.ToJsonString())
            | "GET", "/jwks" -> return json HttpStatusCode.OK (this.JwksJson())
            | "POST", "/token" -> return! this.Token request
            | "GET", "/userinfo" -> return json HttpStatusCode.OK (this.UserInfo.ToJsonString())
            | _ -> return json HttpStatusCode.NotFound """{"error":"not_found"}"""
        }

    /// Published from the start: a provider with no keys would fail every test
    /// for a reason that has nothing to do with it.
    member this.Publish() =
        if this.PublishedKeys.Count = 0 then
            this.PublishedKeys.Add signingKey

        this

/// A new provider with its signing key already published.
let create (issuer: string) = (new StubOidcProvider(issuer)).Publish()

/// Several providers behind one handler, told apart by issuer address: what a
/// test with two identity providers registers through the seam.
type StubRouter(providers: StubOidcProvider list) =
    inherit HttpMessageHandler()

    override _.SendAsync(request: HttpRequestMessage, cancellationToken: CancellationToken) : Task<HttpResponseMessage> =
        let target =
            providers
            |> List.filter (fun p ->
                match request.RequestUri with
                | null -> false
                | uri -> uri.AbsoluteUri.StartsWith(p.Root + "/", StringComparison.Ordinal))
            |> List.sortByDescending (fun p -> p.Root.Length)
            |> List.tryHead

        match target with
        | Some provider ->
            // `SendAsync` is protected; the invoker is the public way in, and
            // `false` keeps the router from disposing providers it does not own.
            let invoker = new HttpMessageInvoker(provider, false)
            invoker.SendAsync(request, cancellationToken)
        | None -> Task.FromException<HttpResponseMessage>(HttpRequestException "no stub provider has that host")
