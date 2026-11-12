/// AT Protocol servers that live inside the test process (specs/015-bluesky-connector).
///
/// One `HttpMessageHandler` plays every host of a Bluesky sign-in, told apart by
/// the URL: the PLC directory (`https://plc.test/<did>`), did:web documents, a
/// handle's own `/.well-known/atproto-did`, any number of PDSes (protected
/// resource and `getRecord`) and any number of authorization servers (metadata,
/// PAR, authorize, token). The product's real client talks to it through its
/// own seam (`Fediverse.PrimaryHandler`), so the account step, the callback and
/// the whole HTTP pipeline are the real ones.
///
/// The authorization server checks what a conforming one does:
/// - every PAR and token request carries a DPoP proof whose signature is checked
///   with the key in its own header, whose `htm` is POST, whose `htu` is the
///   request URL without its query, and whose `nonce` is the server's current
///   one (it starts as `n1`; a proof without it gets `use_dpop_nonce` and the
///   nonce to use). The token proof must come from the key of the PAR proof;
/// - PAR takes `client_id`, `redirect_uri`, `scope` with `atproto`, S256 and `state`;
/// - the code is single use, bound to the redirect address, the client and the
///   PKCE verifier.
///
/// The browser's trip to the authorization endpoint is not a request Umbral
/// makes, so `Approve` plays the person agreeing: given the address Umbral
/// redirected to, it returns the callback address with a code, the state and
/// `iss`. Its modes make the server misbehave: a wrong or missing `iss`, a
/// refusal, a token answer for another DID, a Bearer token, a scope without
/// `atproto`, a 500.
///
/// Test-only: the keys, nonces and codes mean nothing.
module Umbral.Tests.Infrastructure.FakeAtproto

open System
open System.Collections.Concurrent
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
open Microsoft.IdentityModel.JsonWebTokens
open Microsoft.IdentityModel.Tokens
open Umbral.Infrastructure.Providers

/// An account: its DID and handle, what its profile record says, the origin of
/// its PDS and the origin (the issuer) of the authorization server its PDS names.
type AtprotoAccount =
    { Did: string
      Handle: string
      DisplayName: string option
      AvatarCid: string option
      Pds: string
      Issuer: string }

/// What the authorization server answers when the person agrees.
type ApprovalMode =
    | Agree
    | WrongIss of string
    | NoIss
    | AccessDenied

/// What the token endpoint answers for a good code.
type TokenMode =
    | Honest
    /// The answer names another DID: the malicious server.
    | SubOverride of string
    | TokenType of string
    | NoAtprotoScope
    | ServerError

/// A DPoP proof the server verified, for assertions.
type RecordedProof =
    { Endpoint: string
      Url: string
      Method: string
      Nonce: string option
      Jti: string
      /// The key's RFC 7638 thumbprint.
      Thumbprint: string
      Jwk: string }

type ParRecord =
    { ClientId: string
      RedirectUri: string
      State: string
      Challenge: string
      LoginHint: string
      Scope: string
      ChallengeMethod: string
      Thumbprint: string }

type CodeRecord =
    { Par: ParRecord
      Sub: string
      mutable Used: bool }

let private reply (status: int) (contentType: string) (body: string) =
    new HttpResponseMessage(enum<HttpStatusCode> status, Content = new StringContent(body, Encoding.UTF8, contentType))

let private json (status: int) (body: string) = reply status "application/json" body

let private withNonce (nonce: string) (response: HttpResponseMessage) =
    response.Headers.Add("DPoP-Nonce", nonce)
    response

let private node (value: JsonNode | null) : JsonNode =
    match value with
    | null -> failwith "null node"
    | value -> value

let private jobj (fields: (string * JsonNode) list) : JsonObject =
    let o = JsonObject()

    for name, value in fields do
        o[name] <- value

    o

let private oauthError (status: int) (error: string) =
    json status ((jobj [ "error", node (JsonValue.Create error) ]).ToJsonString())

let private form (body: string) : Map<string, string> =
    let parsed = Web.HttpUtility.ParseQueryString body

    parsed.AllKeys
    |> Seq.choose Option.ofObj
    |> Seq.map (fun key -> key, (match parsed[key] with | null -> "" | value -> value))
    |> Map.ofSeq

let private b64text (encoded: string) =
    Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes encoded)

let private challengeOf (verifier: string) =
    Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes verifier))

let private randomToken () =
    Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes 24)

let private strings (items: string list) =
    let array = JsonArray()

    for item in items do
        array.Add(JsonValue.Create item)

    array

/// An authorization server at `issuer` (an origin, e.g. `https://bsky.test`).
type FakeAuthorizationServer(issuer: string) =
    let metadata =
        let m = JsonObject()
        m["issuer"] <- JsonValue.Create issuer
        m["require_pushed_authorization_requests"] <- JsonValue.Create true
        m["pushed_authorization_request_endpoint"] <- JsonValue.Create $"{issuer}/oauth/par"
        m["authorization_endpoint"] <- JsonValue.Create $"{issuer}/oauth/authorize"
        m["token_endpoint"] <- JsonValue.Create $"{issuer}/oauth/token"
        m["code_challenge_methods_supported"] <- strings [ "S256" ]
        m["dpop_signing_alg_values_supported"] <- strings [ "ES256" ]
        m["authorization_response_iss_parameter_supported"] <- JsonValue.Create true
        m["scopes_supported"] <- strings [ "atproto" ]
        m["client_id_metadata_document_supported"] <- JsonValue.Create true
        m["token_endpoint_auth_methods_supported"] <- strings [ "none" ]
        m

    member _.Issuer = issuer

    /// The metadata document. A test mutates it to break a condition.
    member _.Metadata: JsonObject = metadata

    /// The DPoP nonce a proof must carry. Starts as `n1`.
    member val Nonce = "n1" with get, set

    /// Refuses every nonce, so the retry fails too.
    member val RefuseNonces = false with get, set

    member val Approval = Agree with get, set
    member val Token = Honest with get, set

    member val Pars = ConcurrentDictionary<string, ParRecord>()

    /// Every access token this server issued, for a test that looks for one in a log.
    member val AccessTokens = ConcurrentQueue<string>()
    member val Codes = ConcurrentDictionary<string, CodeRecord>()

/// Any number of AT Protocol hosts behind one handler.
type FakeAtproto() =
    inherit HttpMessageHandler()

    let plcHost = "plc.test"
    let accounts = ConcurrentDictionary<string, AtprotoAccount>()
    let viaDns = ConcurrentDictionary<string, bool>()
    let txt = ConcurrentDictionary<string, string array>()
    let servers = ConcurrentDictionary<string, FakeAuthorizationServer>()
    let pdsServer = ConcurrentDictionary<string, string>()
    let resourceOverride = ConcurrentDictionary<string, string>()
    let unreachable = ConcurrentDictionary<string, bool>()
    let privateHosts = ConcurrentDictionary<string, bool>()
    let requests = ConcurrentQueue<string>()
    let proofs = ConcurrentQueue<RecordedProof>()

    let originOf (uri: Uri) = $"{uri.Scheme}://{uri.Authority}"

    /// The origin of the PLC directory: tests set `UMBRAL:BLUESKY_PLC_DIRECTORY` to it.
    member _.PlcDirectory = $"https://{plcHost}"

    /// The profile record answers 500 (no name, no picture).
    member val ProfileFails = false with get, set

    /// The PLC directory answers 500 to every DID.
    member val PlcFails = false with get, set

    /// Every request, `METHOD url`, in order.
    member _.Requests = requests.ToArray()

    member this.CountOf(part: string) =
        this.Requests |> Array.filter (fun r -> r.Contains part) |> Array.length

    /// Every DPoP proof that passed its signature check.
    member _.Proofs = proofs.ToArray()

    /// A name for which the host check finds only a private address.
    member _.MarkPrivate(host: string) = privateHosts[host] <- true

    /// A host that does not answer: the request fails as a refused connection does.
    member _.MakeUnreachable(host: string) = unreachable[host] <- true

    /// The authorization server at `issuer`, made on the first call.
    member _.Server(issuer: string) : FakeAuthorizationServer =
        servers.GetOrAdd(issuer, fun issuer -> FakeAuthorizationServer issuer)

    /// Registers the account: its DID document (at the PLC directory, or at its own
    /// host for a did:web), its PDS and authorization server, and its handle, served
    /// by DNS (`viaDns`) or over HTTPS.
    member this.AddAccount(account: AtprotoAccount, viaDnsTxt: bool) =
        accounts[account.Did] <- account
        viaDns[account.Did] <- viaDnsTxt
        this.Server account.Issuer |> ignore
        pdsServer[account.Pds] <- account.Issuer

        if viaDnsTxt then
            txt["_atproto." + account.Handle] <- [| "did=" + account.Did |]

    /// The PDS at `pds` names `issuer` as its authorization server from now on.
    member this.SetPdsServer(pds: string, issuer: string) =
        this.Server issuer |> ignore
        pdsServer[pds] <- issuer

    /// What the PDS's protected-resource document says, verbatim.
    member _.SetResource(pds: string, body: string) = resourceOverride[pds] <- body

    /// The TXT records of `name` (e.g. `_atproto.example.org`), verbatim.
    member _.SetTxt(name: string, values: string array) = txt[name] <- values

    /// The TXT lookup, for `Providers.Bluesky.TxtResolver`.
    member _.TxtResolver() : Bluesky.TxtResolver =
        Bluesky.TxtResolver(fun name _ ->
            match txt.TryGetValue name with
            | true, values -> Task.FromResult values
            | _ -> Task.FromResult Array.empty<string>)

    /// Every name resolves to a public address, except the ones marked private.
    member _.HostResolver() : Fediverse.HostResolver =
        Fediverse.HostResolver(fun name _ ->
            let address = if privateHosts.ContainsKey name then "10.0.0.5" else "93.184.215.14"
            Task.FromResult [| IPAddress.Parse address |])

    /// The person agreeing at the authorization server: `authorizeUrl` is where Umbral
    /// redirected the browser. Checks `client_id` and `request_uri`, and returns the
    /// address of Umbral's callback with what the server sends back.
    member _.Approve(authorizeUrl: string) : string =
        let uri = Uri authorizeUrl
        let query = Web.HttpUtility.ParseQueryString uri.Query

        let server =
            servers.Values
            |> Seq.tryFind (fun s -> Uri(string s.Metadata["authorization_endpoint"]).GetLeftPart UriPartial.Path = uri.GetLeftPart UriPartial.Path)
            |> Option.defaultWith (fun () -> failwith $"no authorization server serves {uri.GetLeftPart UriPartial.Path}")

        let requestUri = query["request_uri"] |> Option.ofObj |> Option.defaultValue ""

        let par =
            match server.Pars.TryGetValue requestUri with
            | true, par -> par
            | _ -> failwith $"the authorize address names no pushed request: '{requestUri}'"

        let clientId = query["client_id"] |> Option.ofObj |> Option.defaultValue ""

        if clientId <> par.ClientId then
            failwith $"client_id '{clientId}' is not the pushed request's '{par.ClientId}'"

        let issParam =
            match server.Approval with
            | WrongIss other -> $"&iss={Uri.EscapeDataString other}"
            | NoIss -> ""
            | _ -> $"&iss={Uri.EscapeDataString server.Issuer}"

        let state = Uri.EscapeDataString par.State

        match server.Approval with
        | AccessDenied -> $"{par.RedirectUri}?error=access_denied&state={state}{issParam}"
        | _ ->
            let sub =
                accounts.Values
                |> Seq.tryFind (fun a -> a.Handle = par.LoginHint || a.Did = par.LoginHint)
                |> Option.map _.Did
                |> Option.defaultWith (fun () -> failwith $"no account for login_hint '{par.LoginHint}'")

            let code = randomToken ()
            server.Codes[code] <- { Par = par; Sub = sub; Used = false }
            $"{par.RedirectUri}?code={Uri.EscapeDataString code}&state={state}{issParam}"

    // -----------------------------------------------------------------------
    // DPoP
    // -----------------------------------------------------------------------

    /// Verifies the proof of a POST to `url`; the answer to send when it is not good.
    member private _.Verify(request: HttpRequestMessage, url: string, endpoint: string, server: FakeAuthorizationServer) : Task<Result<RecordedProof, HttpResponseMessage>> =
        task {
            let invalid () = oauthError 400 "invalid_dpop_proof"

            match (match request.Headers.TryGetValues "DPoP" with | true, values -> Option.ofObj values |> Option.map Seq.toList |> Option.defaultValue [] | _ -> []) with
            | [ proof ] ->
                try
                    let token = JsonWebToken proof
                    use header = JsonDocument.Parse(b64text token.EncodedHeader)
                    use payload = JsonDocument.Parse(b64text token.EncodedPayload)
                    let h = header.RootElement
                    let p = payload.RootElement

                    let prop (element: JsonElement) (name: string) =
                        match element.TryGetProperty name with
                        | true, value when value.ValueKind = JsonValueKind.String -> Option.ofObj (value.GetString())
                        | _ -> None

                    let jwk =
                        match h.TryGetProperty "jwk" with
                        | true, value when value.ValueKind = JsonValueKind.Object -> Some value
                        | _ -> None

                    match jwk with
                    | Some jwk when prop h "typ" = Some "dpop+jwt" && prop h "alg" = Some "ES256" && not (fst (jwk.TryGetProperty "d")) ->
                        let key = JsonWebKey(jwk.GetRawText())

                        let parameters =
                            TokenValidationParameters(
                                IssuerSigningKey = key,
                                ValidateIssuer = false,
                                ValidateAudience = false,
                                ValidateLifetime = false,
                                RequireExpirationTime = false
                            )

                        let! result = JsonWebTokenHandler().ValidateTokenAsync(proof, parameters)

                        if not result.IsValid then
                            return Error(invalid ())
                        elif prop p "htm" <> Some "POST" || prop p "htu" <> Some url then
                            return Error(invalid ())
                        else
                            let nonce = prop p "nonce"

                            let recorded =
                                { Endpoint = endpoint
                                  Url = url
                                  Method = "POST"
                                  Nonce = nonce
                                  Jti = prop p "jti" |> Option.defaultValue ""
                                  Thumbprint = Base64UrlEncoder.Encode(key.ComputeJwkThumbprint())
                                  Jwk = jwk.GetRawText() }

                            proofs.Enqueue recorded

                            if server.RefuseNonces || nonce <> Some server.Nonce then
                                return Error(withNonce server.Nonce (oauthError 400 "use_dpop_nonce"))
                            else
                                return Ok recorded
                    | _ -> return Error(invalid ())
                with _ ->
                    return Error(invalid ())
            | _ -> return Error(invalid ())
        }

    // -----------------------------------------------------------------------
    // The authorization server
    // -----------------------------------------------------------------------

    member private this.Par(request: HttpRequestMessage, server: FakeAuthorizationServer, body: string) : Task<HttpResponseMessage> =
        task {
            let url = $"{server.Issuer}/oauth/par"

            match! this.Verify(request, url, "par", server) with
            | Error response -> return response
            | Ok proof ->
                let f = form body
                let field name = f |> Map.tryFind name |> Option.defaultValue ""

                let good =
                    field "client_id" <> ""
                    && field "redirect_uri" <> ""
                    && field "response_type" = "code"
                    && (field "scope").Split(' ') |> Array.contains "atproto"
                    && field "code_challenge_method" = "S256"
                    && field "code_challenge" <> ""
                    && field "state" <> ""

                if not good then
                    return oauthError 400 "invalid_request"
                else
                    let requestUri = "urn:ietf:params:oauth:request_uri:" + randomToken ()

                    server.Pars[requestUri] <-
                        { ClientId = field "client_id"
                          RedirectUri = field "redirect_uri"
                          State = field "state"
                          Challenge = field "code_challenge"
                          LoginHint = field "login_hint"
                          Scope = field "scope"
                          ChallengeMethod = field "code_challenge_method"
                          Thumbprint = proof.Thumbprint }

                    return
                        withNonce
                            server.Nonce
                            (json 201 ((jobj [ "request_uri", node (JsonValue.Create requestUri); "expires_in", node (JsonValue.Create 90) ]).ToJsonString()))
        }

    member private this.Token(request: HttpRequestMessage, server: FakeAuthorizationServer, body: string) : Task<HttpResponseMessage> =
        task {
            let url = $"{server.Issuer}/oauth/token"

            match! this.Verify(request, url, "token", server) with
            | Error response -> return response
            | Ok proof ->
                let f = form body
                let field name = f |> Map.tryFind name |> Option.defaultValue ""

                match server.Codes.TryGetValue(field "code") with
                | true, issued when field "grant_type" = "authorization_code" && not issued.Used ->
                    issued.Used <- true

                    if proof.Thumbprint <> issued.Par.Thumbprint then
                        return withNonce server.Nonce (oauthError 400 "invalid_dpop_proof")
                    elif field "client_id" <> issued.Par.ClientId
                         || field "redirect_uri" <> issued.Par.RedirectUri
                         || challengeOf (field "code_verifier") <> issued.Par.Challenge then
                        return withNonce server.Nonce (oauthError 400 "invalid_grant")
                    else
                        match server.Token with
                        | ServerError -> return json 500 """{"error":"server_error"}"""
                        | mode ->
                            let sub =
                                match mode with
                                | SubOverride other -> other
                                | _ -> issued.Sub

                            let tokenType =
                                match mode with
                                | TokenType kind -> kind
                                | _ -> "DPoP"

                            let scope =
                                match mode with
                                | NoAtprotoScope -> "email"
                                | _ -> "atproto"

                            let answer = JsonObject()
                            let accessToken = randomToken ()
                            server.AccessTokens.Enqueue accessToken
                            answer["access_token"] <- JsonValue.Create accessToken
                            answer["token_type"] <- JsonValue.Create tokenType
                            answer["scope"] <- JsonValue.Create scope
                            answer["sub"] <- JsonValue.Create sub
                            answer["expires_in"] <- JsonValue.Create 300
                            return withNonce server.Nonce (json 200 (answer.ToJsonString()))
                | _ -> return withNonce server.Nonce (oauthError 400 "invalid_grant")
        }

    // -----------------------------------------------------------------------
    // Routing
    // -----------------------------------------------------------------------

    member private _.DidDocument(account: AtprotoAccount) =
        let service = JsonObject()
        service["id"] <- JsonValue.Create "#atproto_pds"
        service["type"] <- JsonValue.Create "AtprotoPersonalDataServer"
        service["serviceEndpoint"] <- JsonValue.Create account.Pds
        let services = JsonArray()
        services.Add service
        let document = JsonObject()
        document["id"] <- JsonValue.Create account.Did
        document["alsoKnownAs"] <- strings [ "at://" + account.Handle ]
        document["service"] <- services
        document.ToJsonString()

    member private this.Handle(request: HttpRequestMessage, uri: Uri) : Task<HttpResponseMessage> =
        task {
            let origin = originOf uri
            let path = uri.AbsolutePath

            let! body =
                match Option.ofObj request.Content with
                | Some content -> content.ReadAsStringAsync()
                | None -> Task.FromResult ""

            match request.Method.Method, path with
            | "GET", _ when uri.Host = plcHost && this.PlcFails -> return json 500 "{}"

            | "GET", _ when uri.Host = plcHost ->
                match accounts.TryGetValue(Uri.UnescapeDataString(path.TrimStart '/')) with
                | true, account -> return json 200 (this.DidDocument account)
                | _ -> return reply 404 "text/plain" "not found"

            | "GET", "/.well-known/did.json" ->
                match accounts.TryGetValue("did:web:" + uri.Host) with
                | true, account -> return json 200 (this.DidDocument account)
                | _ -> return reply 404 "text/plain" "not found"

            | "GET", "/.well-known/atproto-did" ->
                match accounts.Values |> Seq.tryFind (fun a -> a.Handle = uri.Host && not viaDns[a.Did]) with
                | Some account -> return reply 200 "text/plain" account.Did
                | None -> return reply 404 "text/plain" "not found"

            | "GET", "/.well-known/oauth-protected-resource" when pdsServer.ContainsKey origin ->
                match resourceOverride.TryGetValue origin with
                | true, text -> return json 200 text
                | _ -> return json 200 ((jobj [ "authorization_servers", (strings [ pdsServer[origin] ] :> JsonNode) ]).ToJsonString())

            | "GET", "/xrpc/com.atproto.repo.getRecord" when pdsServer.ContainsKey origin ->
                if this.ProfileFails then
                    return json 500 """{"error":"InternalServerError"}"""
                else
                    let repo = (Web.HttpUtility.ParseQueryString uri.Query)["repo"] |> Option.ofObj |> Option.defaultValue ""

                    match accounts.TryGetValue repo with
                    | true, account ->
                        let value = JsonObject()
                        account.DisplayName |> Option.iter (fun name -> value["displayName"] <- JsonValue.Create name)

                        account.AvatarCid
                        |> Option.iter (fun cid ->
                            let link = JsonObject()
                            link["$link"] <- JsonValue.Create cid
                            let avatar = JsonObject()
                            avatar["ref"] <- link
                            avatar["mimeType"] <- JsonValue.Create "image/jpeg"
                            value["avatar"] <- avatar)

                        return json 200 ((jobj [ "value", value :> JsonNode ]).ToJsonString())
                    | _ -> return json 400 """{"error":"RecordNotFound"}"""

            | "GET", "/.well-known/oauth-authorization-server" when servers.ContainsKey origin ->
                return json 200 (servers[origin].Metadata.ToJsonString())

            | "POST", "/oauth/par" when servers.ContainsKey origin -> return! this.Par(request, servers[origin], body)
            | "POST", "/oauth/token" when servers.ContainsKey origin -> return! this.Token(request, servers[origin], body)
            | _ -> return reply 404 "text/plain" "not found"
        }

    override this.SendAsync(request: HttpRequestMessage, cancellationToken: CancellationToken) : Task<HttpResponseMessage> =
        match request.RequestUri with
        | null -> Task.FromException<HttpResponseMessage>(HttpRequestException "no fake server has that host")
        | uri ->
            requests.Enqueue $"{request.Method.Method} {uri}"

            if unreachable.ContainsKey uri.Host then
                Task.FromException<HttpResponseMessage>(HttpRequestException "connection refused")
            elif
                uri.Host <> plcHost
                && not (servers.ContainsKey(originOf uri))
                && not (pdsServer.ContainsKey(originOf uri))
                && not (accounts.Values |> Seq.exists (fun a -> a.Handle = uri.Host || a.Did = "did:web:" + uri.Host))
            then
                Task.FromException<HttpResponseMessage>(HttpRequestException "no fake server has that host")
            else
                this.Handle(request, uri)
