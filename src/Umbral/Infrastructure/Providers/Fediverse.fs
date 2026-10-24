/// The calls Umbral makes to a Fediverse server (specs/010-fediverse-connector,
/// contract section 2).
///
/// A server is whatever a stranger typed, so every call here is made to an
/// address Umbral did not choose: the client refuses non-public addresses at
/// connect time, never follows a redirect, gives each call 10 seconds and reads
/// no more than 64 KiB. What a server says is untrusted input and is judged by
/// the pure rules of `Umbral.Domains.Fediverse`; this module only moves bytes
/// and parses JSON.
///
/// Nothing here logs or puts in an error detail a token, a code or a secret:
/// the details are fixed words, never an echo of what the server sent.
module Umbral.Infrastructure.Providers.Fediverse

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Net.Sockets
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Umbral.Domains.Fediverse
open Umbral.Infrastructure
open Umbral.Infrastructure.ProviderAdapters

/// The named client of every call to a server. Not `umbral-provider`: that one
/// reaches an operator's own OpenID Connect server, which may sit on a LAN, and
/// the guard below would block it (research R6).
[<Literal>]
let HttpClientName = "umbral-fediverse"

/// The existing development setting (ASPNETCORE_ENVIRONMENT=Development and
/// UMBRAL__INSECURE_ALLOW_HTTP=true). It lets http and non-public addresses
/// through, so a local fake server works.
type Options = { AllowDev: bool }

/// Builds the primary handler under the named client, a registered factory so a
/// test can put a stub server behind the real client, like
/// `ProviderAdapters.PrimaryHandler`.
type PrimaryHandler = delegate of unit -> HttpMessageHandler

/// The DNS lookup of the account step's address pre-check (FR-008). A delegate
/// so a test can answer for names that do not exist, as `PrimaryHandler` does
/// for the network.
type HostResolver = delegate of string * CancellationToken -> Task<IPAddress array>

let systemResolver = HostResolver(fun name token -> Dns.GetHostAddressesAsync(name, token))

[<Literal>]
let private MaxResponseBytes = 65536

let private requestTimeout = TimeSpan.FromSeconds 10.0

/// Raised by the connect callback when a name resolves to no public address.
exception NonPublicAddress of host: string

/// Connects to the first reachable address of `host` that may be connected to.
let private connect (allowDev: bool) (context: SocketsHttpConnectionContext) (cancellationToken: CancellationToken) : Task<Stream> =
    task {
        let host = context.DnsEndPoint.Host
        let! resolved = Dns.GetHostAddressesAsync(host, cancellationToken)

        let usable =
            resolved |> Array.filter (fun address -> allowDev || isPublicAddress address)

        if usable.Length = 0 then
            raise (NonPublicAddress host)

        let mutable stream: Stream option = None
        let mutable failure: exn option = None
        let mutable index = 0

        while stream.IsNone && index < usable.Length do
            let socket = new Socket(SocketType.Stream, ProtocolType.Tcp, NoDelay = true)

            try
                do! socket.ConnectAsync(usable[index], context.DnsEndPoint.Port, cancellationToken)
                stream <- Some(new NetworkStream(socket, true) :> Stream)
            with e ->
                socket.Dispose()
                failure <- Some e
                // A cancelled or timed-out connect ends the attempt; only a
                // refused or unreachable address moves on to the next one.
                if cancellationToken.IsCancellationRequested then
                    index <- usable.Length

            index <- index + 1

        match stream, failure with
        | Some opened, _ -> return opened
        | None, Some e -> return raise e
        | None, None -> return raise (NonPublicAddress host)
    }

/// The handler of the named client: no redirects, a 5 second connect, and a
/// connect callback that keeps only addresses `isPublicAddress` accepts (all of
/// them under `allowDev`).
///
/// The check is at connect time, not before the request, because only then is it
/// about the address the socket really opens: a name that answered with a public
/// address a moment ago can answer with `127.0.0.1` now (DNS rebinding).
/// Redirects are off so a server cannot send an authorization code, or a
/// registration POST, to an address of its choosing.
let guardedHandler (allowDev: bool) : HttpMessageHandler =
    let handler = new SocketsHttpHandler(AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds 5.0)

    handler.ConnectCallback <-
        Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>>(fun context cancellationToken ->
            ValueTask<Stream>(connect allowDev context cancellationToken))

    handler :> HttpMessageHandler

/// Why a call to a server failed. `Reason` is the word of the security log.
type ServerError =
    /// No answer in time, a network failure, a 5xx or a body over the bound.
    | Unreachable of detail: string
    /// The name resolves only to addresses Umbral will not connect to.
    | NonPublic
    /// The server does not do what Umbral needs (PKCE, app registration).
    | Unsupported
    /// The server does not know the client Umbral holds (`invalid_client`).
    | ClientRejected
    /// The server refused a registration or a code.
    | Refused of detail: string
    /// The server answered, but not with what the call needs.
    | Malformed of detail: string

    member this.Reason =
        match this with
        | Unreachable _ -> "unreachable"
        | NonPublic -> "non_public_address"
        | Unsupported -> "unsupported"
        | ClientRejected -> "client_rejected"
        | Refused _ -> "registration_failed"
        | Malformed _ -> "account_unreadable"

type private Answer = { Status: int; Body: string }

exception private ResponseTooLarge

/// A server is untrusted input: reading its body without a cap turns a 200 OK
/// into a memory-exhaustion vector.
let private readBounded (content: HttpContent) (cancellationToken: CancellationToken) : Task<string> =
    task {
        use! stream = content.ReadAsStreamAsync cancellationToken
        use buffer = new MemoryStream()
        let chunk = Array.zeroCreate<byte> 8192
        let mutable readCount = 1

        while readCount > 0 do
            let! n = stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken)
            readCount <- n

            if n > 0 then
                if buffer.Length + int64 n > int64 MaxResponseBytes then
                    raise ResponseTooLarge

                buffer.Write(chunk, 0, n)

        return Encoding.UTF8.GetString(buffer.ToArray())
    }

let rec private involvesNonPublic (error: exn) : bool =
    match error with
    | NonPublicAddress _ -> true
    | :? AggregateException as aggregate -> aggregate.InnerExceptions |> Seq.exists involvesNonPublic
    | _ ->
        match Option.ofObj error.InnerException with
        | Some inner -> involvesNonPublic inner
        | None -> false

/// One call: its own 10 second limit, a bounded body, `Accept: application/json`.
/// A transport failure is an `Error`; an HTTP status is not, the caller reads it.
let private send (client: HttpClient) (build: unit -> HttpRequestMessage) : Task<Result<Answer, ServerError>> =
    task {
        use cts = new CancellationTokenSource(requestTimeout)

        try
            use request = build ()
            request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue "application/json")
            use! response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
            let! body = readBounded response.Content cts.Token
            return Ok { Status = int response.StatusCode; Body = body }
        with
        | e when involvesNonPublic e -> return Error NonPublic
        | ResponseTooLarge -> return Error(Unreachable "the answer was too large")
        | :? OperationCanceledException -> return Error(Unreachable "the server did not answer in time")
        | :? HttpRequestException
        | :? IOException -> return Error(Unreachable "the server could not be reached")
    }

let private get (url: string) () = new HttpRequestMessage(HttpMethod.Get, url)

let private postForm (url: string) (fields: (string * string) list) () =
    new HttpRequestMessage(
        HttpMethod.Post,
        url,
        Content = new FormUrlEncodedContent(fields |> List.map (fun (k, v) -> Collections.Generic.KeyValuePair(k, v)))
    )

/// The body as a JSON object, or None. Cloned so the document can be disposed.
let private parseObject (body: string) : JsonElement option =
    try
        use document = JsonDocument.Parse body

        if document.RootElement.ValueKind = JsonValueKind.Object then
            Some(document.RootElement.Clone())
        else
            None
    with :? JsonException ->
        None

let private optString (element: JsonElement) (name: string) : string option =
    match element.TryGetProperty name with
    | true, prop when prop.ValueKind = JsonValueKind.String ->
        Option.ofObj (prop.GetString()) |> Option.filter (fun s -> s <> "")
    | _ -> None

let private elements (element: JsonElement) (name: string) : JsonElement list =
    match element.TryGetProperty name with
    | true, prop when prop.ValueKind = JsonValueKind.Array -> prop.EnumerateArray() |> List.ofSeq
    | _ -> []

/// An absolute address with a scheme the server may use: https, or http under
/// `allowDev`.
let private absolute (allowDev: bool) (text: string) : Uri option =
    match Uri.TryCreate(text, UriKind.Absolute) with
    | true, uri ->
        Option.ofObj uri
        |> Option.filter (fun u -> u.Scheme = Uri.UriSchemeHttps || (allowDev && u.Scheme = Uri.UriSchemeHttp))
    | _ -> None

let private activityTypes =
    [ "application/activity+json"
      "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"" ]

/// WebFinger for a handle whose domain is not its server (research R2): the
/// authority of the `self` link. None on any failure; the caller then uses the
/// handle's domain as the server.
let lookup (client: HttpClient) (allowDev: bool) (user: string) (domain: string) : Task<string option> =
    task {
        let resource = Uri.EscapeDataString $"acct:{user}@{domain}"
        let url = $"{baseUrl allowDev domain}/.well-known/webfinger?resource={resource}"
        let! answer = send client (get url)

        match answer with
        | Ok { Status = 200; Body = body } ->
            return
                parseObject body
                |> Option.bind (fun document ->
                    elements document "links"
                    |> List.tryPick (fun link ->
                        match optString link "rel", optString link "type", optString link "href" with
                        | Some "self", Some linkType, Some href when List.contains linkType activityTypes ->
                            absolute allowDev href |> Option.map (fun uri -> uri.Authority)
                        | _ -> None))
        | _ -> return None
    }

let private readMetadata (document: JsonElement) : ServerMetadata =
    { CodeChallengeMethods =
        elements document "code_challenge_methods_supported"
        |> List.choose (fun e -> if e.ValueKind = JsonValueKind.String then Option.ofObj (e.GetString()) else None)
      AuthorizationEndpoint = optString document "authorization_endpoint"
      TokenEndpoint = optString document "token_endpoint"
      RegistrationEndpoint = optString document "app_registration_endpoint" }

/// GoToSocial publishes no metadata but names itself in NodeInfo. The document
/// is found through `/.well-known/nodeinfo`, and its address must be on the
/// server's own host: a server must not make Umbral read another machine.
let private nodeInfo (client: HttpClient) (allowDev: bool) (host: string) : Task<Result<ServerEndpoints, ServerError>> =
    task {
        let! index = send client (get $"{baseUrl allowDev host}/.well-known/nodeinfo")

        match index with
        | Error error -> return Error error
        | Ok { Status = status } when status >= 500 -> return Error(Unreachable "the server answered with an error")
        | Ok { Status = 200; Body = body } ->
            let linked =
                parseObject body
                |> Option.bind (fun document ->
                    let hrefs =
                        elements document "links"
                        |> List.choose (fun link ->
                            match optString link "rel", optString link "href" with
                            | Some rel, Some href -> Some(rel, href)
                            | _ -> None)

                    [ "/schema/2.1"; "/schema/2.0" ]
                    |> List.tryPick (fun suffix ->
                        hrefs
                        |> List.tryPick (fun (rel, href) -> if rel.EndsWith suffix then Some href else None)))
                |> Option.bind (absolute allowDev)
                |> Option.filter (fun uri -> uri.Authority.ToLowerInvariant() = host)

            match linked with
            | None -> return Error Unsupported
            | Some uri ->
                let! document = send client (get (uri.ToString()))

                match document with
                | Error error -> return Error error
                | Ok { Status = status } when status >= 500 -> return Error(Unreachable "the server answered with an error")
                | Ok { Status = 200; Body = body } ->
                    let software =
                        parseObject body
                        |> Option.bind (fun root ->
                            match root.TryGetProperty "software" with
                            | true, software when software.ValueKind = JsonValueKind.Object -> optString software "name"
                            | _ -> None)

                    match software |> Option.bind (fromNodeInfo allowDev host) with
                    | Some endpoints -> return Ok endpoints
                    | None -> return Error Unsupported
                | Ok _ -> return Error Unsupported
        | Ok _ -> return Error Unsupported
    }

/// Whether the server can sign Umbral's people in, and where (research R1).
/// OAuth metadata decides when the server publishes it; a server whose answer
/// is missing or not JSON falls back to NodeInfo. Metadata that fails the rule
/// is final: Mastodon before 4.3 and Akkoma never reach NodeInfo.
let discover (client: HttpClient) (allowDev: bool) (host: string) : Task<Result<ServerEndpoints, ServerError>> =
    task {
        let! answer = send client (get $"{baseUrl allowDev host}/.well-known/oauth-authorization-server")

        match answer with
        | Error error -> return Error error
        | Ok { Status = status } when status >= 500 -> return Error(Unreachable "the server answered with an error")
        | Ok { Status = 200; Body = body } ->
            match parseObject body with
            | Some document ->
                match fromMetadata allowDev host (readMetadata document) with
                | Some endpoints -> return Ok endpoints
                | None -> return Error Unsupported
            | None -> return! nodeInfo client allowDev host
        | Ok _ -> return! nodeInfo client allowDev host
    }

/// The `error` of an OAuth answer, when it is JSON that has one.
let private oauthError (body: string) : string option =
    parseObject body |> Option.bind (fun document -> optString document "error")

let private serverFailure (answer: Answer) =
    if answer.Status >= 500 then
        Some(Unreachable "the server answered with an error")
    else
        None

/// Registers Umbral as an app on the server. The answer must carry both a client
/// id and a secret; a 4xx is the server's refusal.
let register
    (client: HttpClient)
    (endpoints: ServerEndpoints)
    (redirectUri: string)
    (website: string)
    : Task<Result<string * Secrets.Protected, ServerError>> =
    task {
        let fields =
            [ "client_name", "Umbral"
              "redirect_uris", redirectUri
              "scopes", "read:accounts"
              "website", website ]

        let! answer = send client (postForm endpoints.RegistrationUrl fields)

        match answer with
        | Error error -> return Error error
        | Ok answer ->
            match serverFailure answer with
            | Some failure -> return Error failure
            | None when answer.Status < 200 || answer.Status > 299 ->
                return Error(Refused $"the server answered with status {answer.Status}")
            | None ->
                let credentials =
                    parseObject answer.Body
                    |> Option.bind (fun document ->
                        match optString document "client_id", optString document "client_secret" with
                        | Some id, Some secret -> Some(id, Secrets.Protected.Of secret)
                        | _ -> None)

                match credentials with
                | Some pair -> return Ok pair
                | None -> return Error(Malformed "the registration had no client id and secret")
    }

/// Asks the token endpoint for a client-credentials token, to learn whether the
/// server still knows the client (research R8). Only `invalid_client` says no:
/// a token, or any other OAuth error such as `unsupported_grant_type`, proves
/// the client exists, so a server that limits the grant is not locked out.
let checkClient
    (client: HttpClient)
    (endpoints: ServerEndpoints)
    (clientId: string)
    (secret: Secrets.Protected)
    : Task<Result<unit, ServerError>> =
    task {
        let fields =
            [ "grant_type", "client_credentials"
              "client_id", clientId
              "client_secret", secret.Reveal
              "scope", "read:accounts" ]

        let! answer = send client (postForm endpoints.TokenUrl fields)

        match answer with
        | Error error -> return Error error
        | Ok answer ->
            match serverFailure answer with
            | Some failure -> return Error failure
            | None ->
                match oauthError answer.Body, parseObject answer.Body with
                | Some "invalid_client", _ -> return Error ClientRejected
                | _, Some _ -> return Ok()
                | _, None -> return Error(Unreachable "the token endpoint sent an unreadable answer")
    }

/// Where the person's browser goes to approve the sign-in. Pure; the browser
/// makes the call, not Umbral.
let authorizeUrl
    (endpoints: ServerEndpoints)
    (clientId: string)
    (redirectUri: string)
    (state: string)
    (codeChallenge: string)
    : Uri =
    buildUrl
        endpoints.AuthorizeUrl
        [ "response_type", "code"
          "client_id", clientId
          "redirect_uri", redirectUri
          "scope", "read:accounts"
          "state", state
          "code_challenge", codeChallenge
          "code_challenge_method", "S256" ]

/// Trades the code for an access token, at the server the sign-in started with.
let exchange
    (client: HttpClient)
    (endpoints: ServerEndpoints)
    (clientId: string)
    (secret: Secrets.Protected)
    (redirectUri: string)
    (code: string)
    (verifier: string)
    : Task<Result<string, ServerError>> =
    task {
        let fields =
            [ "grant_type", "authorization_code"
              "code", code
              "redirect_uri", redirectUri
              "client_id", clientId
              "client_secret", secret.Reveal
              "code_verifier", verifier ]

        let! answer = send client (postForm endpoints.TokenUrl fields)

        match answer with
        | Error error -> return Error error
        | Ok answer ->
            match serverFailure answer with
            | Some failure -> return Error failure
            | None ->
                let document = parseObject answer.Body

                match oauthError answer.Body with
                | Some "invalid_client" -> return Error ClientRejected
                | _ when answer.Status < 200 || answer.Status > 299 ->
                    return Error(Refused $"the token endpoint answered with status {answer.Status}")
                | _ ->
                    match document |> Option.bind (fun d -> optString d "access_token") with
                    | Some token -> return Ok token
                    | None -> return Error(Malformed "the token answer had no access token")
    }

/// What `verify_credentials` says about the person, as Umbral reads it.
type Account =
    { Id: string
      Username: string
      DisplayName: string
      Avatar: string option
      Url: string option }

/// The account the access token belongs to. An absent or empty `id` or `username`
/// is `Malformed`: no user is created from it.
let account
    (client: HttpClient)
    (allowDev: bool)
    (host: string)
    (accessToken: string)
    : Task<Result<Account, ServerError>> =
    task {
        let build () =
            let request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl allowDev host}/api/v1/accounts/verify_credentials")
            request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", accessToken)
            request

        let! answer = send client build

        match answer with
        | Error error -> return Error error
        | Ok answer ->
            match serverFailure answer with
            | Some failure -> return Error failure
            | None when answer.Status <> 200 ->
                return Error(Malformed $"the server answered with status {answer.Status}")
            | None ->
                // Mastodon sends the id as a string; accept a number too, which
                // some compatible servers send.
                let id (document: JsonElement) =
                    match document.TryGetProperty "id" with
                    | true, prop when prop.ValueKind = JsonValueKind.String ->
                        Option.ofObj (prop.GetString()) |> Option.filter (fun s -> s <> "")
                    | true, prop when prop.ValueKind = JsonValueKind.Number -> Some(prop.GetRawText())
                    | _ -> None

                let read =
                    parseObject answer.Body
                    |> Option.bind (fun document ->
                        match id document, optString document "username" with
                        | Some id, Some username ->
                            Some
                                { Id = id
                                  Username = username
                                  DisplayName = optString document "display_name" |> Option.defaultValue ""
                                  Avatar = optString document "avatar"
                                  Url = optString document "url" }
                        | _ -> None)

                match read with
                | Some value -> return Ok value
                | None -> return Error(Malformed "the account had no id or username")
    }

/// Only an absolute https address is kept: an `http` avatar or page would be
/// mixed content on Umbral's own pages, and `javascript:` must never be a link.
let private httpsOnly (text: string option) : string option =
    text
    |> Option.filter (fun value ->
        match Uri.TryCreate(value, UriKind.Absolute) with
        | true, uri -> (match Option.ofObj uri with Some u -> u.Scheme = Uri.UriSchemeHttps | None -> false)
        | _ -> false)

/// The normalised profile (research R10). No email: a Fediverse identity is
/// never linked to another user by address.
let toProfile (host: string) (account: Account) : ProviderProfile =
    { Subject = fediverseSubject host account.Id
      Email = None
      EmailVerified = false
      DisplayName =
        Some(
            if String.IsNullOrWhiteSpace account.DisplayName then
                account.Username
            else
                account.DisplayName
        )
      AvatarUrl = httpsOnly account.Avatar
      Username = Some(preferredUsername account.Username host)
      ProfileUrl = httpsOnly account.Url }
