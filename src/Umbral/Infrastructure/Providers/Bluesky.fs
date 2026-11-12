/// The calls Umbral makes for a Bluesky (AT Protocol) sign-in (specs/015-bluesky-connector,
/// research R1, R3 to R5, R9, R10).
///
/// Like the Fediverse client, every call goes to an address Umbral did not
/// choose: a handle's host, a DID's host, a PDS, an authorization server. They
/// all go through the guarded client of `Providers.Fediverse` (no non-public
/// addresses, no redirects, 10 seconds, 64 KiB), under a client name of their own.
/// What a server says is judged by the pure rules of `Umbral.Domains.Bluesky`;
/// this module moves bytes, signs the two DPoP-bound POSTs and parses a little
/// JSON.
///
/// Nothing here logs or returns a code, a token or a body: errors are fixed words.
module Umbral.Infrastructure.Providers.Bluesky

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Threading
open System.Threading.Tasks
open DnsClient
open Duende.IdentityModel.OidcClient.DPoP
open Umbral.Domains.Bluesky
open Umbral.Infrastructure.Providers

/// The named client of every Bluesky call.
[<Literal>]
let HttpClientName = "umbral-bluesky"

/// `AllowDev` is the existing development setting; `PlcDirectory` is the base
/// address of the PLC directory, without a trailing slash.
type Options = { AllowDev: bool; PlcDirectory: string }

/// The TXT lookup of `_atproto.<handle>`. A delegate so a test can answer for
/// names that do not exist, as `Fediverse.HostResolver` does.
type TxtResolver = delegate of string * CancellationToken -> Task<string array>

/// Created on the first Bluesky sign-in, never at startup (research R12).
///
/// Typed `obj` on purpose. An F# program runs every file's top-level bindings
/// when it starts, so a `lazy` of `LookupClient`, or a field of that type, would
/// load DnsClient at startup even though the client is built later. Two first
/// lookups at once may each build one; one is kept, and both work.
let mutable private lookupClient: obj | null = null

let private lookup () : LookupClient =
    match lookupClient with
    | :? LookupClient as client -> client
    | _ ->
        let client =
            LookupClient(LookupClientOptions(Timeout = TimeSpan.FromSeconds 3.0, Retries = 0, UseCache = true))

        lookupClient <- box client
        client

/// Every TXT record of `name`, each record's strings joined. A DNS failure or a
/// timeout is an empty array, never an exception.
let dnsResolver =
    TxtResolver(fun name token ->
        task {
            try
                let! answer = (lookup ()).QueryAsync(name, QueryType.TXT, QueryClass.IN, token)

                return
                    answer.Answers
                    |> Seq.choose (fun record ->
                        match record with
                        | :? Protocol.TxtRecord as txt -> Some(String.Join("", txt.Text |> Seq.map string))
                        | _ -> None)
                    |> Array.ofSeq
            with _ ->
                return Array.empty<string>
        })

/// The host pre-check of FR-008: before a request leaves for any host (a handle's,
/// a did:web's, a PDS, an authorization server learnt on the way) the name is
/// resolved, and one that resolves only to non-public addresses is refused as
/// `NonPublicAddress`, which `send` reads as `NonPublic`. It sits in front of the
/// connect-time guard, which stays in production: that one judges the address the
/// socket really opens, this one makes the refusal testable where a stub replaces
/// the connect. Not applied under the development setting.
type HostCheckHandler(allowDev: bool, resolver: Fediverse.HostResolver) =
    inherit DelegatingHandler()

    /// `base` cannot be reached from inside the task below.
    member private _.Next(request: HttpRequestMessage, cancellationToken: CancellationToken) : Task<HttpResponseMessage> =
        base.SendAsync(request, cancellationToken)

    override this.SendAsync(request: HttpRequestMessage, cancellationToken: CancellationToken) : Task<HttpResponseMessage> =
        task {
            match Option.ofObj request.RequestUri with
            | Some uri when not allowDev ->
                let! addresses =
                    task {
                        try
                            return! resolver.Invoke(uri.DnsSafeHost, cancellationToken)
                        with
                        | :? OperationCanceledException as e -> return raise (HttpRequestException("name resolution timed out", e))
                        | e -> return raise (HttpRequestException("name resolution failed", e))
                    }

                if not (addresses |> Array.exists Umbral.Domains.Fediverse.isPublicAddress) then
                    raise (Fediverse.NonPublicAddress uri.Host)
            | _ -> ()

            return! this.Next(request, cancellationToken)
        }

/// Why a call failed. The handler turns each into the contract's message.
type ClientError =
    /// The name resolves only to addresses Umbral will not connect to.
    | NonPublic
    /// No answer in time, a network failure, a 5xx or a body over the bound;
    /// the host that did not answer.
    | Unreachable of host: string
    /// A handle or a DID that does not resolve.
    | NotFound
    /// The server does not do what Umbral needs; the word of the security log.
    | Unsupported of word: string

type private Answer =
    { Status: int
      Body: string
      /// The `DPoP-Nonce` header of the answer.
      Nonce: string option }

/// One call: its own 10 second limit, a bounded body, `Accept: application/json`.
/// A transport failure is an `Error`; an HTTP status is not, the caller reads it.
let private send (client: HttpClient) (build: unit -> HttpRequestMessage) : Task<Result<Answer, ClientError>> =
    task {
        use cts = new CancellationTokenSource(Fediverse.requestTimeout)
        let mutable host = ""

        try
            use request = build ()
            host <- request.RequestUri |> Option.ofObj |> Option.map _.Host |> Option.defaultValue ""
            request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue "application/json")
            use! response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
            let! body = Fediverse.readBounded response.Content cts.Token

            let nonce =
                match response.Headers.TryGetValues "DPoP-Nonce" with
                | true, values ->
                    Option.ofObj values
                    |> Option.bind Seq.tryHead
                    |> Option.filter (fun v -> v <> "")
                | _ -> None

            return Ok { Status = int response.StatusCode; Body = body; Nonce = nonce }
        with
        | e when Fediverse.involvesNonPublic e -> return Error NonPublic
        | Fediverse.ResponseTooLarge -> return Error(Unreachable host)
        | :? OperationCanceledException -> return Error(Unreachable host)
        | :? HttpRequestException
        | :? IO.IOException -> return Error(Unreachable host)
    }

let private scheme (allowDev: bool) = if allowDev then "http" else "https"

// ---------------------------------------------------------------------------
// Handle -> DID (research R3)
// ---------------------------------------------------------------------------

/// The DID of `handle` (already normalised): the TXT record first, then
/// `https://<handle>/.well-known/atproto-did`, whose body must be a DID.
/// Neither is `NotFound`. The caller still checks the DID document claims the
/// handle.
let resolveHandle
    (client: HttpClient)
    (txt: TxtResolver)
    (allowDev: bool)
    (handle: string)
    : Task<Result<string, ClientError>> =
    task {
        let! records = txt.Invoke("_atproto." + handle, CancellationToken.None)

        match atprotoDidOfTxt records with
        | Some did -> return Ok did
        | None ->
            let url = $"{scheme allowDev}://{handle}/.well-known/atproto-did"
            let! answer = send client (Fediverse.get url)

            match answer with
            | Ok { Status = 200; Body = body } ->
                match parseDid (body.Trim()) with
                | Some did -> return Ok did
                | None -> return Error NotFound
            // A handle with no web server is a handle with no answer there.
            | Ok _
            | Error(Unreachable _) -> return Error NotFound
            | Error error -> return Error error
    }

// ---------------------------------------------------------------------------
// DID -> document (research R4)
// ---------------------------------------------------------------------------

/// The DID document of `did`: `did:plc` from the PLC directory, `did:web` from
/// the DID's own host.
let resolveDid (client: HttpClient) (options: Options) (did: string) : Task<Result<DidDocument, ClientError>> =
    task {
        let url =
            match didWebHost did with
            | Some host -> Some $"{scheme options.AllowDev}://{host}/.well-known/did.json"
            | None when did.StartsWith("did:plc:", StringComparison.Ordinal) -> Some $"{options.PlcDirectory}/{did}"
            | None -> None

        match url with
        | None -> return Error NotFound
        | Some url ->
            let! answer = send client (Fediverse.get url)

            match answer with
            | Ok { Status = 200; Body = body } ->
                match parseDidDocument options.AllowDev did body with
                | Ok document -> return Ok document
                | Error _ -> return Error NotFound
            | Ok { Status = status } when status >= 500 ->
                return Error(Unreachable(Uri(url).Host))
            | Ok _ -> return Error NotFound
            | Error error -> return Error error
    }

// ---------------------------------------------------------------------------
// PDS -> authorization server (research R5)
// ---------------------------------------------------------------------------

/// The authorization server of `pds` (an origin): the protected-resource
/// document names it, its metadata is read and judged.
let discover (client: HttpClient) (allowDev: bool) (pds: string) : Task<Result<ServerMetadata, ClientError>> =
    task {
        let! resource = send client (Fediverse.get $"{pds}/.well-known/oauth-protected-resource")

        match resource with
        | Error error -> return Error error
        | Ok { Status = 200; Body = body } ->
            match parseProtectedResource allowDev body with
            | None -> return Error(Unsupported "resource")
            | Some origin ->
                let! metadata = send client (Fediverse.get $"{origin}/.well-known/oauth-authorization-server")

                match metadata with
                | Error error -> return Error error
                | Ok { Status = 200; Body = body } ->
                    match parseServerMetadata allowDev origin body with
                    | Ok server -> return Ok server
                    | Error word -> return Error(Unsupported word)
                | Ok _ -> return Error(Unsupported "malformed")
        | Ok _ -> return Error(Unsupported "resource")
    }

// ---------------------------------------------------------------------------
// DPoP (research R1)
// ---------------------------------------------------------------------------

/// A new ES256 key for one attempt: the private JWK, as JSON.
let newKey () : string = JsonWebKeys.CreateECDsaJson "ES256"

/// RFC 9449 section 4.2: the `htu` is the endpoint without its query and fragment.
let private htuOf (url: string) : string =
    Uri(url).GetLeftPart UriPartial.Path

/// A fresh proof for a POST to `url`; Duende makes a new `jti` each time, and
/// there is no `ath` because no access token is presented.
let private proofFor (jwk: string) (url: string) (nonce: string option) : string =
    let factory = DefaultDPoPProofTokenFactory jwk
    let request = DPoPProofRequest(Url = htuOf url, Method = "POST", DPoPNonce = Option.toObj nonce)
    factory.CreateProofToken(request).ProofToken

let private errorWord (body: string) : string option =
    Fediverse.parseObject body |> Option.bind (fun json -> Fediverse.optString json "error")

/// A POST of a form with a DPoP proof. A `use_dpop_nonce` answer with a fresh
/// nonce is repeated once with it; a second one is `Unsupported "dpop_nonce"`.
/// The answer's `Nonce` is the last one seen.
let private postDpop
    (client: HttpClient)
    (url: string)
    (fields: (string * string) list)
    (jwk: string)
    (nonce: string option)
    : Task<Result<Answer, ClientError>> =
    let attempt (nonce: string option) =
        send client (fun () ->
            let request = Fediverse.postForm url fields ()
            request.Headers.Add("DPoP", proofFor jwk url nonce)
            request)

    let challenged (answer: Answer) =
        (answer.Status = 400 || answer.Status = 401)
        && answer.Nonce.IsSome
        && errorWord answer.Body = Some "use_dpop_nonce"

    task {
        match! attempt nonce with
        | Error error -> return Error error
        | Ok first when challenged first ->
            let retryNonce = first.Nonce

            match! attempt retryNonce with
            | Error error -> return Error error
            | Ok second when challenged second -> return Error(Unsupported "dpop_nonce")
            | Ok second -> return Ok { second with Nonce = second.Nonce |> Option.orElse retryNonce }
        | Ok first -> return Ok { first with Nonce = first.Nonce |> Option.orElse nonce }
    }

type PushResult =
    { RequestUri: string
      /// The authorization server's last DPoP nonce, for the token request.
      Nonce: string option }

/// The pushed authorization request (RFC 9126). `Ok` carries the `request_uri`
/// and the last nonce the server sent.
let pushAuthorization
    (client: HttpClient)
    (meta: ServerMetadata)
    (clientId: string)
    (redirectUri: string)
    (state: string)
    (codeChallenge: string)
    (loginHint: string)
    (jwk: string)
    : Task<Result<PushResult, ClientError>> =
    task {
        let fields =
            [ "client_id", clientId
              "response_type", "code"
              "redirect_uri", redirectUri
              "scope", "atproto"
              "state", state
              "code_challenge", codeChallenge
              "code_challenge_method", "S256"
              "login_hint", loginHint ]

        match! postDpop client meta.Par fields jwk None with
        | Error error -> return Error error
        | Ok { Status = status; Body = body; Nonce = nonce } when status = 200 || status = 201 ->
            match Fediverse.parseObject body |> Option.bind (fun json -> Fediverse.optString json "request_uri") with
            | Some requestUri -> return Ok { RequestUri = requestUri; Nonce = nonce }
            | None -> return Error(Unreachable(Uri(meta.Par).Host))
        | Ok _ -> return Error(Unreachable(Uri(meta.Par).Host))
    }

/// The address the browser goes to: the authorization endpoint with `client_id`
/// and `request_uri` only, added to any query it already has.
let authorizeUrl (meta: ServerMetadata) (clientId: string) (requestUri: string) : Uri =
    let builder = UriBuilder(meta.Authorize)
    let existing = builder.Query.TrimStart '?'

    let added =
        $"client_id={Uri.EscapeDataString clientId}&request_uri={Uri.EscapeDataString requestUri}"

    builder.Query <- if existing = "" then added else existing + "&" + added
    builder.Uri

/// The code for tokens, bound to the key. The error word is `exchange_failed`
/// and nothing of the body: the code and the tokens never reach the log.
let exchange
    (client: HttpClient)
    (tokenEndpoint: string)
    (clientId: string)
    (redirectUri: string)
    (code: string)
    (verifier: string)
    (jwk: string)
    (nonce: string option)
    : Task<Result<TokenAnswer, string>> =
    task {
        let fields =
            [ "grant_type", "authorization_code"
              "code", code
              "redirect_uri", redirectUri
              "code_verifier", verifier
              "client_id", clientId ]

        match! postDpop client tokenEndpoint fields jwk nonce with
        | Ok { Status = 200; Body = body } ->
            match parseTokenAnswer body with
            | Some answer -> return Ok answer
            | None -> return Error "exchange_failed"
        | _ -> return Error "exchange_failed"
    }

// ---------------------------------------------------------------------------
// The profile (research R10)
// ---------------------------------------------------------------------------

/// The display name and the picture address of `did`, read from its PDS with no
/// DPoP and no authentication. Any failure is no name and no picture.
let profile (client: HttpClient) (pds: string) (did: string) : Task<string option * string option> =
    task {
        let url =
            $"{pds}/xrpc/com.atproto.repo.getRecord?repo={did}&collection=app.bsky.actor.profile&rkey=self"

        match! send client (Fediverse.get url) with
        | Ok { Status = 200; Body = body } ->
            let picture =
                avatarCid body
                |> Option.map (fun cid ->
                    $"{pds}/xrpc/com.atproto.sync.getBlob?did={Uri.EscapeDataString did}&cid={cid}")

            return displayName body, picture
        | _ -> return None, None
    }
