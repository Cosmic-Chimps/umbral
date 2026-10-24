/// Fediverse servers that live inside the test process (specs/010-fediverse-connector).
///
/// One `HttpMessageHandler` plays any number of servers, told apart by the host
/// of the request, so the Fediverse client, the account step and the callback
/// talk to them over the real `HttpClient` pipeline, registered through the
/// product's own seam (`Fediverse.PrimaryHandler`). A request to a host nobody
/// registered fails as a name that does not resolve would.
///
/// Each server has a mode (what software it pretends to be, or that it is down
/// or slow), the accounts it knows, its WebFinger answers, and a log of what it
/// received. It issues a new client id and secret on every registration, checks
/// PKCE the way a conforming server does (the SHA-256 of the verifier must equal
/// the challenge it saw, one use per code) and can be told to forget every client
/// registered so far (`Forget`), so the `invalid_client` paths can be walked.
///
/// The browser's trip to `/oauth/authorize` is not a request Umbral makes, so
/// `Authorize` plays the person approving: given the URL Umbral produced, it
/// records state, challenge and redirect address and returns a code bound to them.
///
/// Test-only: the ids and secrets mean nothing.
module Umbral.Tests.Infrastructure.StubMastodon

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
open Umbral.Tests.Infrastructure.StubOidcProvider

/// What a server pretends to be. `Down` answers 500 everywhere; `Slow` never
/// answers before the caller gives up.
type ServerMode =
    /// Mastodon 4.3: OAuth metadata with S256 and an app registration endpoint.
    | Mastodon
    /// No metadata, NodeInfo `gotosocial`.
    | GoToSocial
    /// No metadata, NodeInfo with that software name.
    | Akkoma
    | Pleroma
    | Misskey
    | Sharkey
    /// Metadata, but without S256.
    | MastodonNoPkce
    | Down
    | Slow

/// An account a server knows, as `verify_credentials` shows it.
type StubAccount =
    { Id: string
      Username: string
      DisplayName: string
      Avatar: string
      Url: string }

/// One `POST /api/v1/apps`: what was sent and what was issued.
type StubRegistration =
    { ClientId: string
      ClientSecret: string
      Form: Map<string, string> }

/// What `Authorize` hands back: what a browser would carry to Umbral's callback.
type Approval =
    { Code: string
      State: string
      RedirectUri: string
      ClientId: string }

type private IssuedCode =
    { ClientId: string
      RedirectUri: string
      Challenge: string
      Account: StubAccount
      mutable Used: bool }

let private json (status: HttpStatusCode) (body: JsonNode) =
    new HttpResponseMessage(status, Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"))

let private jsonText (status: HttpStatusCode) (body: string) =
    new HttpResponseMessage(status, Content = new StringContent(body, Encoding.UTF8, "application/json"))

let private obj (fields: (string * JsonNode) list) : JsonNode =
    let o = JsonObject()

    for name, value in fields do
        o[name] <- value

    o :> JsonNode

let private oauthError (status: HttpStatusCode) (error: string) =
    json status (obj [ "error", node error ])

let private parseQuery (text: string) =
    let parsed = System.Web.HttpUtility.ParseQueryString text

    parsed.AllKeys
    |> Seq.choose Option.ofObj
    |> Seq.map (fun key -> key, (match parsed[key] with | null -> "" | value -> value))
    |> Map.ofSeq

let private base64Url (bytes: byte[]) =
    Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')

/// The PKCE challenge of a verifier (S256), for tests that start a sign-in by hand.
let challengeOf (verifier: string) =
    base64Url (SHA256.HashData(Encoding.ASCII.GetBytes verifier))

let private softwareName (mode: ServerMode) =
    match mode with
    | Mastodon
    | MastodonNoPkce -> "mastodon"
    | GoToSocial -> "gotosocial"
    | Akkoma -> "akkoma"
    | Pleroma -> "pleroma"
    | Misskey -> "misskey"
    | Sharkey -> "sharkey"
    | Down
    | Slow -> "unknown"

/// One fake server at `host` (the authority of its requests, e.g. `mastodon.test`).
type StubServer(host: string, mode: ServerMode) =
    let root = $"https://{host}"
    let accounts = ConcurrentDictionary<string, StubAccount>()
    let webfinger = ConcurrentDictionary<string, string>()
    let registrations = ConcurrentQueue<StubRegistration>()
    let forgotten = ConcurrentDictionary<string, bool>()
    let codes = ConcurrentDictionary<string, IssuedCode>()
    let tokens = ConcurrentDictionary<string, StubAccount>()
    let requests = ConcurrentQueue<string>()
    let mutable counter = 0

    member _.Host = host
    member _.Root = root
    member val Mode = mode with get, set

    /// Characters appended to every client id issued, to test the length bound.
    member val ClientIdPadding = 0 with get, set

    /// An account this server knows.
    member _.AddAccount(account: StubAccount) = accounts[account.Id] <- account

    /// WebFinger: `acct:user@domain` has its `self` link at `href`. A handle can
    /// point to another host.
    member _.SetWebFinger(resource: string, href: string) = webfinger[resource] <- href

    /// Every registration so far, oldest first.
    member _.Registrations = registrations |> Seq.toList

    /// Every request received, as `METHOD /path`, oldest first.
    member _.Requests = requests |> Seq.toList

    /// How many requests ended with `suffix`, e.g. `/oauth/token`.
    member _.CountOf(suffix: string) =
        requests |> Seq.filter (fun r -> r.EndsWith suffix) |> Seq.length

    /// From now on every client registered so far is unknown: the token endpoint
    /// answers `invalid_client` for it, as a server whose database was reset does.
    member _.Forget() =
        for registration in registrations do
            forgotten[registration.ClientId] <- true

    /// The person approves: a code bound to the request's client, redirect
    /// address and challenge, issued for `accountId`.
    member _.Approve(authorize: Uri, accountId: string) : Approval =
        let parameters = parseQuery authorize.Query
        let find name = parameters |> Map.tryFind name |> Option.defaultValue ""
        let code = Guid.NewGuid().ToString "N"

        codes[code] <-
            { ClientId = find "client_id"
              RedirectUri = find "redirect_uri"
              Challenge = find "code_challenge"
              Account = accounts[accountId]
              Used = false }

        { Code = code
          State = find "state"
          RedirectUri = find "redirect_uri"
          ClientId = find "client_id" }

    member private _.Metadata() =
        match mode with
        | Mastodon
        | MastodonNoPkce ->
            let methods = if mode = Mastodon then [ "S256" ] else [ "plain" ]

            json
                HttpStatusCode.OK
                (obj
                    [ "issuer", node root
                      "authorization_endpoint", node $"{root}/oauth/authorize"
                      "token_endpoint", node $"{root}/oauth/token"
                      "app_registration_endpoint", node $"{root}/api/v1/apps"
                      "code_challenge_methods_supported", (strings methods :> JsonNode) ])
        | _ -> oauthError HttpStatusCode.NotFound "not_found"

    member private _.NodeInfoIndex() =
        let link = obj [ "rel", node "http://nodeinfo.diaspora.software/ns/schema/2.0"; "href", node $"{root}/nodeinfo/2.0" ]
        json HttpStatusCode.OK (obj [ "links", (JsonArray(link) :> JsonNode) ])

    member private this.NodeInfo() =
        json
            HttpStatusCode.OK
            (obj [ "version", node "2.0"; "software", obj [ "name", node (softwareName this.Mode); "version", node "1.0" ] ])

    member private _.WebFingerAnswer(resource: string) =
        match webfinger.TryGetValue resource with
        | true, href ->
            let link = obj [ "rel", node "self"; "type", node "application/activity+json"; "href", node href ]
            json HttpStatusCode.OK (obj [ "subject", node resource; "links", (JsonArray(link) :> JsonNode) ])
        | _ -> oauthError HttpStatusCode.NotFound "not_found"

    member private this.Register(form: Map<string, string>) =
        let n = Interlocked.Increment &counter
        let registration = { ClientId = $"client-{n}" + String('x', this.ClientIdPadding); ClientSecret = $"secret-{n}"; Form = form }
        registrations.Enqueue registration

        json
            HttpStatusCode.OK
            (obj
                [ "id", node (string n)
                  "name", node (form |> Map.tryFind "client_name" |> Option.defaultValue "")
                  "client_id", node registration.ClientId
                  "client_secret", node registration.ClientSecret ])

    member private _.Token(form: Map<string, string>) =
        let find name = form |> Map.tryFind name |> Option.defaultValue ""

        let known =
            registrations
            |> Seq.exists (fun r ->
                r.ClientId = find "client_id"
                && r.ClientSecret = find "client_secret"
                && not (forgotten.ContainsKey r.ClientId))

        if not known then
            oauthError HttpStatusCode.Unauthorized "invalid_client"
        else
            let issue (account: StubAccount option) =
                let token = Guid.NewGuid().ToString "N"
                account |> Option.iter (fun a -> tokens[token] <- a)

                json
                    HttpStatusCode.OK
                    (obj [ "access_token", node token; "token_type", node "Bearer"; "scope", node "read:accounts" ])

            match find "grant_type" with
            | "client_credentials" -> issue None
            | "authorization_code" ->
                match codes.TryGetValue(find "code") with
                | true, issued when
                    not issued.Used
                    && issued.ClientId = find "client_id"
                    && issued.RedirectUri = find "redirect_uri"
                    && issued.Challenge = challengeOf (find "code_verifier")
                    ->
                    issued.Used <- true
                    issue (Some issued.Account)
                | true, issued ->
                    // Any failed attempt burns the code, as a careful server does.
                    issued.Used <- true
                    oauthError HttpStatusCode.BadRequest "invalid_grant"
                | _ -> oauthError HttpStatusCode.BadRequest "invalid_grant"
            | _ -> oauthError HttpStatusCode.BadRequest "unsupported_grant_type"

    member private _.VerifyCredentials(request: HttpRequestMessage) =
        let bearer =
            match request.Headers.Authorization with
            | null -> ""
            | header when header.Scheme = "Bearer" -> header.Parameter |> Option.ofObj |> Option.defaultValue ""
            | _ -> ""

        match tokens.TryGetValue bearer with
        | true, account ->
            json
                HttpStatusCode.OK
                (obj
                    [ "id", node account.Id
                      "username", node account.Username
                      "acct", node account.Username
                      "display_name", node account.DisplayName
                      "avatar", node account.Avatar
                      "url", node account.Url ])
        | _ -> oauthError HttpStatusCode.Unauthorized "The access token is invalid"

    /// Answers one request addressed to this server.
    member this.Handle(request: HttpRequestMessage, cancellationToken: CancellationToken) : Task<HttpResponseMessage> =
        task {
            let uri = nonNull request.RequestUri
            let path = uri.AbsolutePath
            requests.Enqueue $"{request.Method} {path}"

            match this.Mode with
            | Down -> return oauthError HttpStatusCode.InternalServerError "down"
            | Slow ->
                do! Task.Delay(Timeout.Infinite, cancellationToken)
                return new HttpResponseMessage()
            | _ ->
                let! form =
                    task {
                        match request.Method = HttpMethod.Post, request.Content with
                        | true, NonNull content ->
                            let! text = content.ReadAsStringAsync cancellationToken
                            return parseQuery text
                        | _ -> return Map.empty
                    }

                match request.Method.Method, path with
                | "GET", "/.well-known/oauth-authorization-server" -> return this.Metadata()
                | "GET", "/.well-known/nodeinfo" -> return this.NodeInfoIndex()
                | "GET", "/nodeinfo/2.0" -> return this.NodeInfo()
                | "GET", "/.well-known/webfinger" ->
                    let resource = parseQuery uri.Query |> Map.tryFind "resource" |> Option.defaultValue ""
                    return this.WebFingerAnswer resource
                | "POST", "/api/v1/apps" -> return this.Register form
                | "POST", "/oauth/token" -> return this.Token form
                | "GET", "/api/v1/accounts/verify_credentials" -> return this.VerifyCredentials request
                | _ -> return oauthError HttpStatusCode.NotFound "not_found"
        }

/// Any number of servers behind one handler, by the authority of the request.
type StubMastodon() =
    inherit HttpMessageHandler()

    let servers = ConcurrentDictionary<string, StubServer>()

    /// A new server at `host` (for example `mastodon.test`).
    member _.AddServer(host: string, mode: ServerMode) : StubServer =
        let server = StubServer(host, mode)
        servers[host] <- server
        server

    member _.Server(host: string) : StubServer = servers[host]

    /// The person approving, for the server the authorize URL points at.
    member this.Authorize(authorize: Uri, accountId: string) : Approval =
        servers[authorize.Authority].Approve(authorize, accountId)

    override _.SendAsync(request: HttpRequestMessage, cancellationToken: CancellationToken) : Task<HttpResponseMessage> =
        match request.RequestUri with
        | null -> Task.FromException<HttpResponseMessage>(HttpRequestException "no stub server has that host")
        | uri ->
            match servers.TryGetValue uri.Authority with
            | true, server -> server.Handle(request, cancellationToken)
            | _ -> Task.FromException<HttpResponseMessage>(HttpRequestException "no stub server has that host")
