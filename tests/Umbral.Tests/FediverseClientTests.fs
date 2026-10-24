/// The client of a Fediverse server (specs/010, T015) against fake servers that
/// live in the test process (`StubMastodon`), through a real `HttpClient`. The
/// connect guard is the exception: it is the real handler against a real socket.
module Umbral.Tests.FediverseClientTests

open System
open System.Diagnostics
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Threading.Tasks
open Xunit
open Umbral.Infrastructure
open Umbral.Infrastructure.Providers.Fediverse
open Umbral.Domains.Fediverse
open Umbral.Tests.Infrastructure.StubMastodon

let private callback = "https://umbral.test/connect/fediverse/callback"

let private ana: StubAccount =
    { Id = "109"
      Username = "ana"
      DisplayName = "Ana Lopez"
      Avatar = "https://files.test/ana.png"
      Url = "https://mastodon.test/@ana" }

/// A stub with one server, and a client on it.
let private serverOf (mode: ServerMode) =
    let stub = new StubMastodon()
    let server = stub.AddServer("mastodon.test", mode)
    server.AddAccount ana
    let client = new HttpClient(stub, false)
    stub, server, client

let private endpointsOf (client: HttpClient) =
    task {
        match! discover client false "mastodon.test" with
        | Ok endpoints -> return endpoints
        | Error error -> return failwith $"discovery should have worked: {error}"
    }

let private registered (client: HttpClient) =
    task {
        let! endpoints = endpointsOf client

        match! register client endpoints callback "https://umbral.test" with
        | Ok(id, secret) -> return endpoints, id, secret
        | Error error -> return failwith $"registration should have worked: {error}"
    }

// ---------------------------------------------------------------------------
// discover
// ---------------------------------------------------------------------------

[<Fact>]
let ``discover reads Mastodon's metadata`` () =
    task {
        let _, _, client = serverOf Mastodon
        let! result = discover client false "mastodon.test"

        match result with
        | Ok endpoints ->
            Assert.Equal("https://mastodon.test/oauth/authorize", endpoints.AuthorizeUrl)
            Assert.Equal("https://mastodon.test/oauth/token", endpoints.TokenUrl)
            Assert.Equal("https://mastodon.test/api/v1/apps", endpoints.RegistrationUrl)
        | Error error -> failwith $"{error}"
    }

[<Fact>]
let ``discover falls back to NodeInfo for GoToSocial`` () =
    task {
        let _, server, client = serverOf GoToSocial
        let! result = discover client false "mastodon.test"

        match result with
        | Ok endpoints -> Assert.Equal("https://mastodon.test/api/v1/apps", endpoints.RegistrationUrl)
        | Error error -> failwith $"{error}"

        Assert.Equal(1, server.CountOf "/nodeinfo/2.0")
    }

[<Theory>]
[<InlineData("Akkoma")>]
[<InlineData("Pleroma")>]
[<InlineData("Misskey")>]
[<InlineData("Sharkey")>]
[<InlineData("MastodonNoPkce")>]
let ``discover refuses software that cannot do PKCE or app registration`` (name: string) =
    task {
        let mode =
            match name with
            | "Akkoma" -> Akkoma
            | "Pleroma" -> Pleroma
            | "Misskey" -> Misskey
            | "Sharkey" -> Sharkey
            | _ -> MastodonNoPkce

        let _, server, client = serverOf mode
        let! result = discover client false "mastodon.test"
        Assert.Equal(Error Unsupported, result)

        // Metadata that fails the rule is final: NodeInfo is not asked.
        if mode = MastodonNoPkce then
            Assert.Equal(0, server.CountOf "/.well-known/nodeinfo")
    }

[<Fact>]
let ``discover on a server that is down is unreachable`` () =
    task {
        let _, _, client = serverOf Down
        let! result = discover client false "mastodon.test"

        match result with
        | Error(Unreachable _ as error) -> Assert.Equal("unreachable", error.Reason)
        | other -> failwith $"{other}"
    }

[<Fact>]
let ``discover on a host nobody answers for is unreachable`` () =
    task {
        let _, _, client = serverOf Mastodon

        match! discover client false "other.test" with
        | Error(Unreachable _) -> ()
        | other -> failwith $"{other}"
    }

[<Fact>]
let ``discover on a slow server is unreachable within the limit`` () =
    task {
        let _, _, client = serverOf Slow
        let clock = Stopwatch.StartNew()
        let! result = discover client false "mastodon.test"
        clock.Stop()

        match result with
        | Error(Unreachable _) -> ()
        | other -> failwith $"{other}"

        Assert.InRange(clock.Elapsed.TotalSeconds, 9.0, 15.0)
    }

// ---------------------------------------------------------------------------
// lookup
// ---------------------------------------------------------------------------

[<Fact>]
let ``lookup follows a handle on one domain to the server on another`` () =
    task {
        let stub = new StubMastodon()
        let home = stub.AddServer("ana.example", Mastodon)
        home.SetWebFinger("acct:ana@ana.example", "https://social.example.net/users/ana")
        use client = new HttpClient(stub, false)

        let! found = lookup client false "ana" "ana.example"
        Assert.Equal(Some "social.example.net", found)
    }

[<Fact>]
let ``lookup is None when the domain has no answer for the handle`` () =
    task {
        let _, _, client = serverOf Mastodon
        let! found = lookup client false "nobody" "mastodon.test"
        Assert.Equal(None, found)
    }

// ---------------------------------------------------------------------------
// register and checkClient
// ---------------------------------------------------------------------------

[<Fact>]
let ``register returns credentials and sends Umbral's name and scope`` () =
    task {
        let _, server, client = serverOf Mastodon
        let! _, id, secret = registered client

        Assert.Equal("client-1", id)
        Assert.Equal("secret-1", secret.Reveal)
        Assert.DoesNotContain("secret-1", $"{secret}")

        let form = (List.head server.Registrations).Form
        Assert.Equal("Umbral", form["client_name"])
        Assert.Equal("read:accounts", form["scopes"])
        Assert.Equal(callback, form["redirect_uris"])
        Assert.Equal("https://umbral.test", form["website"])
    }

[<Fact>]
let ``each registration gets new credentials`` () =
    task {
        let _, _, client = serverOf Mastodon
        let! _, first, _ = registered client
        let! _, second, _ = registered client
        Assert.NotEqual<string>(first, second)
    }

[<Fact>]
let ``checkClient passes for a known client and rejects a forgotten one`` () =
    task {
        let _, server, client = serverOf Mastodon
        let! endpoints, id, secret = registered client

        Assert.Equal(Ok(), (checkClient client endpoints id secret).Result)

        server.Forget()

        match! checkClient client endpoints id secret with
        | Error ClientRejected -> ()
        | other -> failwith $"{other}"
    }

[<Fact>]
let ``checkClient on a server that is down is unreachable`` () =
    task {
        let _, server, client = serverOf Mastodon
        let! endpoints, id, secret = registered client
        server.Mode <- Down

        match! checkClient client endpoints id secret with
        | Error(Unreachable _) -> ()
        | other -> failwith $"{other}"
    }

// ---------------------------------------------------------------------------
// authorizeUrl and exchange
// ---------------------------------------------------------------------------

let private verifier = "a-code-verifier-of-sufficient-length-0123456789abcdef"

let private approved (stub: StubMastodon) (endpoints: ServerEndpoints) (id: string) =
    let url = authorizeUrl endpoints id callback "the-state" (challengeOf verifier)
    stub.Authorize(url, "109"), url

[<Fact>]
let ``authorizeUrl carries every parameter`` () =
    task {
        let _, _, client = serverOf Mastodon
        let! endpoints, id, _ = registered client
        let url = authorizeUrl endpoints id callback "the-state" "the-challenge"
        let query = System.Web.HttpUtility.ParseQueryString url.Query

        Assert.Equal("https://mastodon.test/oauth/authorize", url.GetLeftPart UriPartial.Path)
        Assert.Equal("code", query["response_type"])
        Assert.Equal(id, query["client_id"])
        Assert.Equal(callback, query["redirect_uri"])
        Assert.Equal("read:accounts", query["scope"])
        Assert.Equal("the-state", query["state"])
        Assert.Equal("the-challenge", query["code_challenge"])
        Assert.Equal("S256", query["code_challenge_method"])
    }

[<Fact>]
let ``exchange with the right verifier returns a token, once`` () =
    task {
        let stub, _, client = serverOf Mastodon
        let! endpoints, id, secret = registered client
        let approval, _ = approved stub endpoints id

        match! exchange client endpoints id secret callback approval.Code verifier with
        | Ok token -> Assert.False(String.IsNullOrEmpty token)
        | Error error -> failwith $"{error}"

        // A code works once.
        match! exchange client endpoints id secret callback approval.Code verifier with
        | Error(Refused _) -> ()
        | other -> failwith $"{other}"
    }

[<Fact>]
let ``exchange with a wrong verifier is refused`` () =
    task {
        let stub, _, client = serverOf Mastodon
        let! endpoints, id, secret = registered client
        let approval, _ = approved stub endpoints id

        match! exchange client endpoints id secret callback approval.Code "not-the-verifier" with
        | Error(Refused _ as error) -> Assert.Equal("registration_failed", error.Reason)
        | other -> failwith $"{other}"
    }

[<Fact>]
let ``exchange with a forgotten client is rejected`` () =
    task {
        let stub, server, client = serverOf Mastodon
        let! endpoints, id, secret = registered client
        let approval, _ = approved stub endpoints id
        server.Forget()

        match! exchange client endpoints id secret callback approval.Code verifier with
        | Error ClientRejected -> ()
        | other -> failwith $"{other}"
    }

// ---------------------------------------------------------------------------
// account and toProfile
// ---------------------------------------------------------------------------

let private tokenFor (stub: StubMastodon) (client: HttpClient) =
    task {
        let! endpoints, id, secret = registered client
        let approval, _ = approved stub endpoints id

        match! exchange client endpoints id secret callback approval.Code verifier with
        | Ok token -> return token
        | Error error -> return failwith $"{error}"
    }

[<Fact>]
let ``account maps to a profile`` () =
    task {
        let stub, _, client = serverOf Mastodon
        let! token = tokenFor stub client

        match! account client false "mastodon.test" token with
        | Ok found ->
            Assert.Equal("109", found.Id)
            let profile = toProfile "mastodon.test" found
            Assert.Equal("mastodon.test#109", profile.Subject)
            Assert.Equal(None, profile.Email)
            Assert.False profile.EmailVerified
            Assert.Equal(Some "Ana Lopez", profile.DisplayName)
            Assert.Equal(Some "https://files.test/ana.png", profile.AvatarUrl)
            Assert.Equal(Some "ana@mastodon.test", profile.Username)
            Assert.Equal(Some "https://mastodon.test/@ana", profile.ProfileUrl)
        | Error error -> failwith $"{error}"
    }

[<Fact>]
let ``account with a bad token is unreadable`` () =
    task {
        let _, _, client = serverOf Mastodon

        match! account client false "mastodon.test" "not-a-token" with
        | Error(Malformed _ as error) -> Assert.Equal("account_unreadable", error.Reason)
        | other -> failwith $"{other}"
    }

[<Fact>]
let ``toProfile falls back to the username and drops an http avatar`` () =
    let profile =
        toProfile
            "mastodon.test"
            { Id = "7"
              Username = "bo"
              DisplayName = "  "
              Avatar = Some "http://files.test/bo.png"
              Url = Some "javascript:alert(1)" }

    Assert.Equal(Some "bo", profile.DisplayName)
    Assert.Equal(None, profile.AvatarUrl)
    Assert.Equal(None, profile.ProfileUrl)

// ---------------------------------------------------------------------------
// the connect guard, against a real socket
// ---------------------------------------------------------------------------

[<Fact>]
let ``the real handler refuses a loopback address at connect time`` () =
    task {
        let listener = new TcpListener(IPAddress.Loopback, 0)
        listener.Start()

        try
            let port = (listener.LocalEndpoint :?> IPEndPoint).Port
            use client = new HttpClient(guardedHandler false)

            // Under `false` the host is literal and https, which the domain rules
            // would refuse earlier; the guard is what this proves, so ask for the
            // address straight.
            match! discover client false $"127.0.0.1:{port}" with
            | Error NonPublic -> ()
            | other -> failwith $"{other}"

            Assert.False(listener.Pending(), "nothing may have connected")
        finally
            listener.Stop()
    }

[<Fact>]
let ``the development handler connects to loopback`` () =
    task {
        let listener = new TcpListener(IPAddress.Loopback, 0)
        listener.Start()

        try
            let port = (listener.LocalEndpoint :?> IPEndPoint).Port
            let accepted: Task<TcpClient> = listener.AcceptTcpClientAsync()
            use client = new HttpClient(guardedHandler true)

            // The listener closes the connection without an answer: unreachable,
            // but only after the connection was made.
            let! result = discover client true $"127.0.0.1:{port}"
            let! winner = Task.WhenAny(accepted :> Task, Task.Delay 5000)
            Assert.True(obj.ReferenceEquals(winner, accepted), "the handler should have connected")
            (accepted.Result).Dispose()

            match result with
            | Error(Unreachable _) -> ()
            | other -> failwith $"{other}"
        finally
            listener.Stop()
    }
