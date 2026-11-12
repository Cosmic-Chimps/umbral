/// The AT Protocol client (specs/015, T005) against a stub `HttpMessageHandler`
/// that records every request and answers by URL, through a real `HttpClient`.
/// The connect guard is not under test here: it is `Fediverse.guardedHandler`,
/// which `FediverseClientTests` covers against real sockets.
module Umbral.Tests.BlueskyClientTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.IdentityModel.JsonWebTokens
open Microsoft.IdentityModel.Tokens
open Xunit
open Umbral.Domains.Bluesky
open Umbral.Infrastructure.Providers
open Umbral.Infrastructure.Providers.Bluesky

// ---------------------------------------------------------------------------
// A stub server
// ---------------------------------------------------------------------------

type private Recorded =
    { Method: string
      Url: string
      Dpop: string option
      Body: string }

    /// A field of a form body.
    member this.Field(name: string) : string option =
        let query = Web.HttpUtility.ParseQueryString this.Body
        Option.ofObj query[name]

let private reply (status: int) (body: string) =
    new HttpResponseMessage(enum<HttpStatusCode> status, Content = new StringContent(body, Encoding.UTF8, "application/json"))

let private replyWithNonce (status: int) (nonce: string) (body: string) =
    let response = reply status body
    response.Headers.Add("DPoP-Nonce", nonce)
    response

/// Answers by what the request was, and records every one.
type private Stub(answer: Recorded -> HttpResponseMessage) =
    inherit HttpMessageHandler()
    member val Requests = ResizeArray<Recorded>()

    override this.SendAsync(request: HttpRequestMessage, cancellationToken: CancellationToken) : Task<HttpResponseMessage> =
        task {
            let! body =
                match Option.ofObj request.Content with
                | Some content -> content.ReadAsStringAsync cancellationToken
                | None -> Task.FromResult ""

            let dpop =
                match request.Headers.TryGetValues "DPoP" with
                | true, values -> Option.ofObj values |> Option.bind Seq.tryHead
                | _ -> None

            let recorded =
                { Method = request.Method.Method
                  Url = (Option.ofObj request.RequestUri |> Option.map _.AbsoluteUri |> Option.defaultValue "")
                  Dpop = dpop
                  Body = body }

            this.Requests.Add recorded
            return answer recorded
        }

let private clientOn (stub: Stub) = new HttpClient(stub, false)

/// A handler whose every request fails with `error`.
type private Failing(error: exn) =
    inherit HttpMessageHandler()

    override _.SendAsync(_: HttpRequestMessage, _: CancellationToken) : Task<HttpResponseMessage> =
        Task.FromException<HttpResponseMessage> error

let private failing (error: exn) = new HttpClient(new Failing(error), false)

/// The answers in order, whatever the URL; the last one repeats.
let private inOrder (answers: (unit -> HttpResponseMessage) list) =
    let mutable index = 0

    fun (_: Recorded) ->
        let answer = answers[min index (answers.Length - 1)]
        index <- index + 1
        answer ()

// ---------------------------------------------------------------------------
// What a proof says
// ---------------------------------------------------------------------------

let private part (encoded: string) : JsonElement =
    use document = JsonDocument.Parse(Base64UrlEncoder.Decode encoded)
    document.RootElement.Clone()

let private headerOf (proof: string) = part (JsonWebToken proof).EncodedHeader
let private payloadOf (proof: string) = part (JsonWebToken proof).EncodedPayload

let private text (element: JsonElement) (name: string) : string option =
    match element.TryGetProperty name with
    | true, value -> Option.ofObj (value.GetString())
    | _ -> None

let private has (element: JsonElement) (name: string) : bool =
    match element.TryGetProperty name with
    | true, _ -> true
    | _ -> false

let private proofOf (recorded: Recorded) =
    match recorded.Dpop with
    | Some proof -> proof
    | None -> failwith "the request carried no DPoP header"

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

let private did = "did:plc:ewvi7nxzyoun6zhxrhs64oiz"
let private clientId = "https://umbral.test/connect/bluesky/client-metadata.json"
let private callback = "https://umbral.test/connect/bluesky/callback"

let private meta: ServerMetadata =
    { Issuer = "https://as.test"
      Par = "https://as.test/oauth/par"
      Authorize = "https://as.test/oauth/authorize"
      Token = "https://as.test/oauth/token" }

let private nonceChallenge (nonce: string) =
    replyWithNonce 400 nonce """{"error":"use_dpop_nonce"}"""

let private pushed = """{"request_uri":"urn:ietf:params:oauth:request_uri:req-1","expires_in":60}"""

let private push (client: HttpClient) (meta: ServerMetadata) (jwk: string) =
    pushAuthorization client meta clientId callback "the-state" "the-challenge" "alice.test" jwk

let private options: Options =
    { AllowDev = false
      PlcDirectory = "https://plc.test" }

let private didDocument (id: string) =
    $$"""{"id":"{{id}}","alsoKnownAs":["at://alice.test"],"service":[{"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"https://pds.test"}]}"""

let private noTxt = TxtResolver(fun _ _ -> Task.FromResult Array.empty<string>)

// ---------------------------------------------------------------------------
// DPoP proofs
// ---------------------------------------------------------------------------

[<Fact>]
let ``the htu of a proof has no query and no fragment`` () =
    task {
        let stub = new Stub(fun _ -> reply 201 pushed)
        let client = clientOn stub
        let par = { meta with Par = "https://as.test/oauth/par?x=1#f" }

        let! result = push client par (newKey ())
        Assert.True result.IsOk

        let payload = payloadOf (proofOf stub.Requests[0])
        Assert.Equal(Some "https://as.test/oauth/par", text payload "htu")
        Assert.Equal(Some "POST", text payload "htm")
    }

[<Fact>]
let ``a proof is a dpop+jwt signed with ES256 whose header key is public`` () =
    task {
        let stub = new Stub(fun _ -> reply 201 pushed)
        let! _ = push (clientOn stub) meta (newKey ())

        let header = headerOf (proofOf stub.Requests[0])
        Assert.Equal(Some "dpop+jwt", text header "typ")
        Assert.Equal(Some "ES256", text header "alg")

        let jwk = header.GetProperty "jwk"
        Assert.Equal(Some "EC", text jwk "kty")
        Assert.Equal(Some "P-256", text jwk "crv")
        Assert.False((has jwk "d"), "the private part of the key must not be in the proof")
    }

[<Fact>]
let ``every proof is fresh and carries no access token hash`` () =
    task {
        let stub = new Stub(inOrder [ (fun () -> nonceChallenge "n1"); (fun () -> reply 201 pushed) ])
        let! _ = push (clientOn stub) meta (newKey ())

        let proofs = stub.Requests |> Seq.map proofOf |> List.ofSeq
        Assert.Equal(2, proofs.Length)

        let jtis = proofs |> List.map (fun proof -> text (payloadOf proof) "jti")
        Assert.All(jtis, fun jti -> Assert.True jti.IsSome)
        Assert.Equal(2, jtis |> List.distinct |> List.length)

        for proof in proofs do
            Assert.False((has (payloadOf proof) "ath"))
            Assert.True((has (payloadOf proof) "iat"))
    }

[<Fact>]
let ``newKey makes a different private key each time`` () =
    let one = newKey ()
    let two = newKey ()
    Assert.NotEqual<string>(one, two)
    use document = JsonDocument.Parse one
    Assert.True(has document.RootElement "d")

// ---------------------------------------------------------------------------
// pushAuthorization
// ---------------------------------------------------------------------------

[<Fact>]
let ``the PAR body carries the request and the DPoP header`` () =
    task {
        let stub = new Stub(fun _ -> reply 201 pushed)
        let! result = push (clientOn stub) meta (newKey ())

        match result with
        | Ok pushedResult ->
            Assert.Equal("urn:ietf:params:oauth:request_uri:req-1", pushedResult.RequestUri)
            Assert.Equal(None, pushedResult.Nonce)
        | Error error -> failwith $"{error}"

        let request = stub.Requests |> Seq.exactlyOne
        Assert.Equal("POST", request.Method)
        Assert.Equal("https://as.test/oauth/par", request.Url)
        Assert.Equal(Some clientId, request.Field "client_id")
        Assert.Equal(Some "code", request.Field "response_type")
        Assert.Equal(Some callback, request.Field "redirect_uri")
        Assert.Equal(Some "atproto", request.Field "scope")
        Assert.Equal(Some "the-state", request.Field "state")
        Assert.Equal(Some "the-challenge", request.Field "code_challenge")
        Assert.Equal(Some "S256", request.Field "code_challenge_method")
        Assert.Equal(Some "alice.test", request.Field "login_hint")
        Assert.True request.Dpop.IsSome
    }

[<Fact>]
let ``PAR retries once with the nonce the server sent`` () =
    task {
        let stub =
            new Stub(inOrder [ (fun () -> nonceChallenge "n1"); (fun () -> replyWithNonce 201 "n2" pushed) ])

        let! result = push (clientOn stub) meta (newKey ())

        Assert.Equal(2, stub.Requests.Count)
        Assert.Equal(None, text (payloadOf (proofOf stub.Requests[0])) "nonce")
        Assert.Equal(Some "n1", text (payloadOf (proofOf stub.Requests[1])) "nonce")

        match result with
        | Ok pushedResult -> Assert.Equal(Some "n2", pushedResult.Nonce)
        | Error error -> failwith $"{error}"
    }

[<Fact>]
let ``PAR returns the retry's nonce when the answer to it sends none`` () =
    task {
        let stub = new Stub(inOrder [ (fun () -> nonceChallenge "n1"); (fun () -> reply 201 pushed) ])
        let! result = push (clientOn stub) meta (newKey ())

        match result with
        | Ok pushedResult -> Assert.Equal(Some "n1", pushedResult.Nonce)
        | Error error -> failwith $"{error}"
    }

[<Fact>]
let ``a second use_dpop_nonce at PAR is unsupported after exactly two requests`` () =
    task {
        let stub =
            new Stub(inOrder [ (fun () -> nonceChallenge "n1"); (fun () -> nonceChallenge "n2") ])

        let! result = push (clientOn stub) meta (newKey ())

        Assert.Equal(2, stub.Requests.Count)

        match result with
        | Error(Unsupported "dpop_nonce") -> ()
        | other -> failwith $"unexpected: {other}"
    }

[<Fact>]
let ``PAR with another status is unreachable, naming the host`` () =
    task {
        let stub = new Stub(fun _ -> reply 500 "{}")

        match! push (clientOn stub) meta (newKey ()) with
        | Error(Unreachable "as.test") -> ()
        | other -> failwith $"unexpected: {other}"

        // 201 without a request_uri is no answer either.
        let stub = new Stub(fun _ -> reply 201 "{}")

        match! push (clientOn stub) meta (newKey ()) with
        | Error(Unreachable "as.test") -> ()
        | other -> failwith $"unexpected: {other}"
    }

[<Fact>]
let ``the authorize address has only client_id and request_uri, encoded`` () =
    let url = authorizeUrl meta clientId "urn:ietf:params:oauth:request_uri:req-1"

    Assert.Equal(
        "https://as.test/oauth/authorize?client_id="
        + Uri.EscapeDataString clientId
        + "&request_uri="
        + Uri.EscapeDataString "urn:ietf:params:oauth:request_uri:req-1",
        url.AbsoluteUri
    )

[<Fact>]
let ``the authorize address keeps a query the endpoint already has`` () =
    let url = authorizeUrl { meta with Authorize = "https://as.test/oauth/authorize?prompt=login" } "cid" "urn:x"
    Assert.Equal("https://as.test/oauth/authorize?prompt=login&client_id=cid&request_uri=urn%3Ax", url.AbsoluteUri)

// ---------------------------------------------------------------------------
// exchange
// ---------------------------------------------------------------------------

let private tokenAnswer =
    $$"""{"access_token":"secret-access","token_type":"DPoP","scope":"atproto","sub":"{{did}}","expires_in":300}"""

let private exchangeWith (client: HttpClient) (nonce: string option) =
    exchange client meta.Token clientId callback "the-code" "the-verifier" (newKey ()) nonce

[<Fact>]
let ``the token exchange sends its fields and parses the answer`` () =
    task {
        let stub = new Stub(fun _ -> reply 200 tokenAnswer)
        let! result = exchangeWith (clientOn stub) (Some "n0")

        match result with
        | Ok answer ->
            Assert.Equal(did, answer.Sub)
            Assert.Equal("atproto", answer.Scope)
            Assert.Equal("DPoP", answer.TokenType)
        | Error word -> failwith word

        let request = stub.Requests |> Seq.exactlyOne
        Assert.Equal("https://as.test/oauth/token", request.Url)
        Assert.Equal(Some "authorization_code", request.Field "grant_type")
        Assert.Equal(Some "the-code", request.Field "code")
        Assert.Equal(Some callback, request.Field "redirect_uri")
        Assert.Equal(Some "the-verifier", request.Field "code_verifier")
        Assert.Equal(Some clientId, request.Field "client_id")

        let payload = payloadOf (proofOf request)
        Assert.Equal(Some "https://as.test/oauth/token", text payload "htu")
        Assert.Equal(Some "n0", text payload "nonce")
        Assert.False((has payload "ath"))
    }

[<Fact>]
let ``the token exchange retries once with the new nonce`` () =
    task {
        let stub = new Stub(inOrder [ (fun () -> nonceChallenge "n1"); (fun () -> reply 200 tokenAnswer) ])
        let! result = exchangeWith (clientOn stub) None

        Assert.True result.IsOk
        Assert.Equal(2, stub.Requests.Count)
        Assert.Equal(None, text (payloadOf (proofOf stub.Requests[0])) "nonce")
        Assert.Equal(Some "n1", text (payloadOf (proofOf stub.Requests[1])) "nonce")

        let jtis = stub.Requests |> Seq.map (fun r -> text (payloadOf (proofOf r)) "jti") |> List.ofSeq
        Assert.Equal(2, jtis |> List.distinct |> List.length)
        Assert.Equal(stub.Requests[0].Body, stub.Requests[1].Body)
    }

[<Fact>]
let ``the token exchange gives up on a second nonce challenge`` () =
    task {
        let stub = new Stub(inOrder [ (fun () -> nonceChallenge "n1"); (fun () -> nonceChallenge "n2") ])
        let! result = exchangeWith (clientOn stub) None

        Assert.Equal(2, stub.Requests.Count)
        Assert.Equal(Error "exchange_failed", result)
    }

[<Fact>]
let ``a failed token exchange returns only the fixed word`` () =
    task {
        let secret = "the-code-and-a-token-the-server-echoed"

        for answer in
            [ (fun () -> reply 400 $$"""{"error":"invalid_grant","error_description":"{{secret}}"}""")
              (fun () -> reply 200 "not json")
              (fun () -> reply 500 secret) ] do
            let stub = new Stub(fun _ -> answer ())
            let! result = exchangeWith (clientOn stub) None

            match result with
            | Error word -> Assert.Equal("exchange_failed", word)
            | Ok _ -> failwith "expected a failure"
    }

[<Fact>]
let ``a transport failure at the token endpoint is the same word`` () =
    task {
        let client = failing (HttpRequestException "down")
        let! result = exchangeWith client None
        Assert.Equal(Error "exchange_failed", result)
    }

// ---------------------------------------------------------------------------
// resolveHandle
// ---------------------------------------------------------------------------

[<Fact>]
let ``a TXT record wins over the well-known file`` () =
    task {
        let stub = new Stub(fun _ -> reply 200 "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa")
        let txt = TxtResolver(fun _ _ -> Task.FromResult [| $"did={did}" |])

        let! result = resolveHandle (clientOn stub) txt false "alice.test"

        Assert.Equal(Ok did, result)
        Assert.Empty stub.Requests
    }

[<Fact>]
let ``the TXT lookup is for the _atproto name`` () =
    task {
        let mutable asked = ""

        let txt =
            TxtResolver(fun name _ ->
                asked <- name
                Task.FromResult [| $"did={did}" |])

        let! _ = resolveHandle (clientOn (new Stub(fun _ -> reply 404 "")) ) txt false "alice.test"
        Assert.Equal("_atproto.alice.test", asked)
    }

[<Fact>]
let ``without a TXT record the well-known file answers`` () =
    task {
        let stub = new Stub(fun _ -> reply 200 $"{did}\n")
        let! result = resolveHandle (clientOn stub) noTxt false "alice.test"

        Assert.Equal(Ok did, result)
        let request = stub.Requests |> Seq.exactlyOne
        Assert.Equal("https://alice.test/.well-known/atproto-did", request.Url)
    }

[<Fact>]
let ``the well-known file is fetched over http only in development`` () =
    task {
        let stub = new Stub(fun _ -> reply 200 did)
        let! _ = resolveHandle (clientOn stub) noTxt true "alice.test"
        Assert.Equal("http://alice.test/.well-known/atproto-did", stub.Requests[0].Url)
    }

[<Fact>]
let ``neither a TXT record nor a file is not found`` () =
    task {
        // An unusable TXT, and a body that is no DID.
        let txt = TxtResolver(fun _ _ -> Task.FromResult [| "v=spf1 -all" |])

        let! junk = resolveHandle (clientOn (new Stub(fun _ -> reply 200 "<html>hello</html>"))) txt false "alice.test"
        Assert.Equal(Error NotFound, junk)

        let! missing = resolveHandle (clientOn (new Stub(fun _ -> reply 404 ""))) noTxt false "alice.test"
        Assert.Equal(Error NotFound, missing)

        // A handle with no web server at all.
        let down = failing (HttpRequestException "down")
        let! unreachable = resolveHandle down noTxt false "alice.test"
        Assert.Equal(Error NotFound, unreachable)
    }

[<Fact>]
let ``a handle that resolves only to non-public addresses is NonPublic`` () =
    task {
        let client =
            failing (HttpRequestException("blocked", Fediverse.NonPublicAddress "alice.test"))

        let! result = resolveHandle client noTxt false "alice.test"
        Assert.Equal(Error NonPublic, result)
    }

// ---------------------------------------------------------------------------
// resolveDid
// ---------------------------------------------------------------------------

[<Fact>]
let ``a did:plc is read from the PLC directory`` () =
    task {
        let stub = new Stub(fun _ -> reply 200 (didDocument did))
        let! result = resolveDid (clientOn stub) options did

        match result with
        | Ok document ->
            Assert.Equal(did, document.Id)
            Assert.Equal("https://pds.test", document.Pds)
            Assert.Equal<string list>([ "at://alice.test" ], document.AlsoKnownAs)
        | Error error -> failwith $"{error}"

        Assert.Equal($"https://plc.test/{did}", stub.Requests[0].Url)
    }

[<Fact>]
let ``a did:web is read from its own host`` () =
    task {
        let webDid = "did:web:owner.example.com"
        let stub = new Stub(fun _ -> reply 200 (didDocument webDid))
        let! result = resolveDid (clientOn stub) options webDid

        Assert.True result.IsOk
        Assert.Equal("https://owner.example.com/.well-known/did.json", stub.Requests[0].Url)
    }

[<Fact>]
let ``a missing or wrong DID document is not found`` () =
    task {
        let! missing = resolveDid (clientOn (new Stub(fun _ -> reply 404 "{}"))) options did
        Assert.Equal(Error NotFound, missing)

        // A document that names another DID.
        let! wrong = resolveDid (clientOn (new Stub(fun _ -> reply 200 (didDocument "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa")))) options did
        Assert.Equal(Error NotFound, wrong)

        let! junk = resolveDid (clientOn (new Stub(fun _ -> reply 200 "nope"))) options did
        Assert.Equal(Error NotFound, junk)
    }

[<Fact>]
let ``a DID document that cannot be fetched is unreachable`` () =
    task {
        let down = failing (HttpRequestException "down")

        match! resolveDid down options did with
        | Error(Unreachable "plc.test") -> ()
        | other -> failwith $"unexpected: {other}"
    }

// ---------------------------------------------------------------------------
// discover
// ---------------------------------------------------------------------------

let private metadataJson =
    """{ "issuer": "https://as.test",
         "require_pushed_authorization_requests": true,
         "pushed_authorization_request_endpoint": "https://as.test/oauth/par",
         "authorization_endpoint": "https://as.test/oauth/authorize",
         "token_endpoint": "https://as.test/oauth/token",
         "code_challenge_methods_supported": ["S256"],
         "dpop_signing_alg_values_supported": ["ES256"],
         "authorization_response_iss_parameter_supported": true,
         "scopes_supported": ["atproto"],
         "client_id_metadata_document_supported": true,
         "token_endpoint_auth_methods_supported": ["none"] }"""

let private discoverStub (resource: string) =
    new Stub(fun request ->
        if request.Url = "https://pds.test/.well-known/oauth-protected-resource" then
            reply 200 resource
        elif request.Url = "https://as.test/.well-known/oauth-authorization-server" then
            reply 200 metadataJson
        else
            reply 404 "{}")

[<Fact>]
let ``discover follows the PDS to its authorization server`` () =
    task {
        let stub = discoverStub """{ "authorization_servers": ["https://as.test"] }"""
        let! result = discover (clientOn stub) false "https://pds.test"

        match result with
        | Ok server -> Assert.Equal(meta, server)
        | Error error -> failwith $"{error}"

        Assert.Equal(2, stub.Requests.Count)
    }

[<Fact>]
let ``a resource that names two servers is unsupported`` () =
    task {
        let stub =
            discoverStub """{ "authorization_servers": ["https://as.test", "https://other.test"] }"""

        let! result = discover (clientOn stub) false "https://pds.test"

        Assert.Equal(Error(Unsupported "resource"), result)
        Assert.Equal(1, stub.Requests.Count)
    }

[<Fact>]
let ``a server whose metadata fails a rule is unsupported with that word`` () =
    task {
        let stub =
            new Stub(fun request ->
                if request.Url.Contains "oauth-protected-resource" then
                    reply 200 """{ "authorization_servers": ["https://as.test"] }"""
                else
                    reply 200 (metadataJson.Replace("\"ES256\"", "\"RS256\"")))

        let! result = discover (clientOn stub) false "https://pds.test"
        Assert.Equal(Error(Unsupported "dpop_es256"), result)
    }

// ---------------------------------------------------------------------------
// profile
// ---------------------------------------------------------------------------

[<Fact>]
let ``the profile gives the display name and a blob address`` () =
    task {
        let record =
            """{ "value": { "displayName": "Alice A.", "avatar": { "ref": { "$link": "bafkreiabc123" } } } }"""

        let stub = new Stub(fun _ -> reply 200 record)
        let! name, picture = profile (clientOn stub) "https://pds.test" did

        Assert.Equal(Some "Alice A.", name)

        Assert.Equal(
            Some $"https://pds.test/xrpc/com.atproto.sync.getBlob?did={Uri.EscapeDataString did}&cid=bafkreiabc123",
            picture
        )

        let request = stub.Requests |> Seq.exactlyOne
        Assert.Equal("GET", request.Method)

        Assert.Equal(
            $"https://pds.test/xrpc/com.atproto.repo.getRecord?repo={did}&collection=app.bsky.actor.profile&rkey=self",
            request.Url
        )

        Assert.Equal(None, request.Dpop)
    }

[<Fact>]
let ``a failing profile read gives no name and no picture`` () =
    task {
        let! name, picture = profile (clientOn (new Stub(fun _ -> reply 500 "{}"))) "https://pds.test" did
        Assert.Equal(None, name)
        Assert.Equal(None, picture)

        let! name, picture = profile (clientOn (new Stub(fun _ -> reply 404 "{}"))) "https://pds.test" did
        Assert.Equal((None, None), (name, picture))
    }
