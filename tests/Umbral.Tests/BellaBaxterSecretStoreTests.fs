/// The `bellabaxter` store against a fake BellaBaxter (specs/005 T014).
///
/// The fake is the factory's `outerHandler`, which sits first in the client's
/// chain, in front of the request signing and the resilience handler. So these
/// tests run the real Kiota client, its URL templates and its serialisation, and
/// need no account, no network and no waiting for a retry backoff: a 5xx here is
/// answered once, where the real chain would retry it three times first (the
/// registry's 10 second bound is what caps that in production).
module Umbral.Tests.BellaBaxterSecretStoreTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Polly.CircuitBreaker
open Polly.Timeout
open Xunit
open Umbral.Infrastructure.Secrets
open Umbral.Infrastructure.SecretStores
open Umbral.Infrastructure.SecretStores.BellaBaxterStore

let private none = CancellationToken.None

let private name = SecretName.derive "umbral" "github" (DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero))

let private apiKey = ApiKey(Protected.Of "bax-testkey-0123456789abcdef")

/// One request the fake received.
type private Seen = { Method: string; Path: string; Body: string }

let private json (status: HttpStatusCode) (body: string) =
    new HttpResponseMessage(status, Content = new StringContent(body, Encoding.UTF8, "application/json"))

let private status (code: HttpStatusCode) = json code "{}"

/// A fake server: `route` answers each request, and every request is kept.
type private Fake(route: Seen -> CancellationToken -> Task<HttpResponseMessage>) =
    inherit HttpMessageHandler()

    let seen = ResizeArray<Seen>()

    member _.Requests = lock seen (fun () -> List.ofSeq seen)

    member this.Count(httpMethod: string) =
        this.Requests |> List.filter (fun r -> r.Method = httpMethod) |> List.length

    override _.SendAsync(request, cancellationToken) =
        task {
            let! body =
                match request.Content with
                | null -> Task.FromResult ""
                | content -> content.ReadAsStringAsync cancellationToken

            let entry =
                { Method = request.Method.Method
                  Path = (request.RequestUri |> Option.ofObj |> Option.map (fun u -> u.AbsolutePath) |> Option.defaultValue "")
                  Body = body }

            lock seen (fun () -> seen.Add entry)
            return! route entry cancellationToken
        }

let private synchronous (route: Seen -> HttpResponseMessage) =
    new Fake(fun seen _ -> Task.FromResult(route seen))

let private environmentPath = "/api/v1/projects/proj/environments/prod"
let private providersPath = $"{environmentPath}/providers"

let private secretPath (provider: string) (key: string) =
    $"{providersPath}/{provider}/secrets/{key}"

let private providerList (providers: (string * string) list) =
    providers
    |> List.map (fun (slug, metaType) -> $"{{\"providerSlug\":\"{slug}\",\"providerMetaType\":\"{metaType}\"}}")
    |> String.concat ","
    |> sprintf "[%s]"

let private options (handler: HttpMessageHandler) : BellaStoreOptions =
    { BaseUrl = "https://bella.test"
      Auth = apiKey
      Project = Some "proj"
      Environment = Some "prod"
      Provider = None
      PrivateKeyPem = None
      Prefix = "umbral"
      OuterHandler = Some handler }

let private storeWith (opts: BellaStoreOptions) = BellaBaxterStore.create opts

/// A server with one `Secrets` provider (`vault`) that routes the rest to `rest`.
let private withVault (rest: Seen -> HttpResponseMessage) (seen: Seen) =
    if seen.Method = "GET" && seen.Path = providersPath then
        json HttpStatusCode.OK (providerList [ "vault", "Secrets"; "certs", "Certificates" ])
    else
        rest seen

let private ok (result: Result<'a, StoreFailure>) =
    match result with
    | Ok value -> value
    | Error failure -> failwith $"expected Ok, got {failure.Cause}"

let private keyOf = name.Bella

let private reference =
    { Store = StoreId
      Locator = $"proj/prod/vault/{keyOf}" }

let private secretResponse (key: string) (value: string) =
    json HttpStatusCode.OK (JsonSerializer.Serialize {| secrets = dict [ key, value ] |})

// --- naming and locator ------------------------------------------------------

[<Fact>]
let ``the key is upper case with the UMBRAL_ lead and the locator is project env provider key`` () =
    task {
        let fake = synchronous (withVault (fun _ -> status HttpStatusCode.OK))
        let! put = (storeWith (options fake)).Put(name, "s3cr3t", none)
        let written = ok put

        Assert.StartsWith("UMBRAL_UMBRAL_GITHUB_", keyOf)
        Assert.Equal(keyOf.ToUpperInvariant(), keyOf)
        Assert.DoesNotContain('-', keyOf)
        Assert.Equal(StoreId, written.Store)
        Assert.Equal($"proj/prod/vault/{keyOf}", written.Locator)
    }

// --- provider resolution -----------------------------------------------------

[<Fact>]
let ``the single secrets provider of the environment is used`` () =
    task {
        let fake = synchronous (withVault (fun _ -> status HttpStatusCode.OK))
        let! _ = (storeWith (options fake)).Put(name, "s3cr3t", none)

        Assert.Contains(fake.Requests, fun r -> r.Method = "PUT" && r.Path = secretPath "vault" keyOf)
        // The other provider (Certificates) was never written to.
        Assert.DoesNotContain(fake.Requests, fun r -> r.Path.Contains "/certs/")
    }

[<Fact>]
let ``the provider is looked up once for any number of calls`` () =
    task {
        let fake = synchronous (withVault (fun _ -> status HttpStatusCode.OK))
        let store = storeWith (options fake)
        let! _ = store.Put(name, "one", none)
        let! _ = store.Put(name, "two", none)
        let! _ = store.Probe none

        let listings = fake.Requests |> List.filter (fun r -> r.Path = providersPath)
        Assert.Single listings |> ignore
    }

[<Theory>]
[<InlineData(0)>]
[<InlineData(2)>]
let ``zero or several secrets providers without a configured one are rejected as ambiguous`` (count: int) =
    task {
        let providers = List.init count (fun i -> $"vault{i}", "Secrets") @ [ "certs", "Certificates" ]
        let fake = synchronous (fun _ -> json HttpStatusCode.OK (providerList providers))
        let! put = (storeWith (options fake)).Put(name, "s3cr3t", none)

        // The settings notice for this reason names UMBRAL__BELLABAXTER_PROVIDER.
        Assert.Equal(Error(Rejected "provider_ambiguous"), put)
        // Nothing was written anywhere.
        Assert.Equal(0, fake.Count "PUT")
        Assert.Equal(0, fake.Count "POST")
    }

[<Fact>]
let ``a configured provider is used without listing the environment's`` () =
    task {
        let fake = synchronous (fun _ -> status HttpStatusCode.OK)
        let! put = (storeWith { options fake with Provider = Some "chosen" }).Put(name, "s3cr3t", none)

        Assert.Equal($"proj/prod/chosen/{keyOf}", (ok put).Locator)
        Assert.DoesNotContain(fake.Requests, fun r -> r.Path = providersPath)
    }

[<Fact>]
let ``an API key with no project or environment asks the server once`` () =
    task {
        let fake =
            synchronous (fun seen ->
                if seen.Path = "/api/v1/keys/me" then
                    json HttpStatusCode.OK """{"projectSlug":"proj","environmentSlug":"prod"}"""
                else
                    withVault (fun _ -> status HttpStatusCode.OK) seen)

        let store = storeWith { options fake with Project = None; Environment = None }
        let! first = store.Put(name, "one", none)
        let! _ = store.Put(name, "two", none)

        Assert.Equal($"proj/prod/vault/{keyOf}", (ok first).Locator)
        Assert.Single(fake.Requests |> List.filter (fun r -> r.Path = "/api/v1/keys/me")) |> ignore
    }

[<Fact>]
let ``an outage while resolving is not remembered`` () =
    task {
        let mutable up = false

        let fake =
            synchronous (fun seen ->
                if seen.Path = providersPath && not up then
                    status HttpStatusCode.ServiceUnavailable
                else
                    withVault (fun _ -> status HttpStatusCode.OK) seen)

        let store = storeWith (options fake)
        let! down = store.Put(name, "s3cr3t", none)
        Assert.Equal(Error Unreachable, down)

        up <- true
        let! back = store.Put(name, "s3cr3t", none)
        Assert.Equal($"proj/prod/vault/{keyOf}", (ok back).Locator)
    }

// --- write -------------------------------------------------------------------

[<Fact>]
let ``a write is a PUT of the value to the key`` () =
    task {
        let fake = synchronous (withVault (fun _ -> status HttpStatusCode.OK))
        let! put = (storeWith (options fake)).Put(name, "s3cr3t", none)
        ok put |> ignore

        let write = fake.Requests |> List.find (fun r -> r.Method = "PUT")
        Assert.Equal(secretPath "vault" keyOf, write.Path)
        Assert.Equal("s3cr3t", JsonDocument.Parse(write.Body).RootElement.GetProperty("value").GetString())
        Assert.Equal(0, fake.Count "POST")
    }

[<Fact>]
let ``a PUT that finds nothing is followed by a POST that creates the key`` () =
    task {
        let fake =
            synchronous (
                withVault (fun seen ->
                    if seen.Method = "PUT" then
                        status HttpStatusCode.NotFound
                    else
                        status HttpStatusCode.OK)
            )

        let! put = (storeWith (options fake)).Put(name, "s3cr3t", none)

        Assert.Equal($"proj/prod/vault/{keyOf}", (ok put).Locator)
        let create = fake.Requests |> List.find (fun r -> r.Method = "POST")
        Assert.Equal($"{providersPath}/vault/secrets", create.Path)
        let body = JsonDocument.Parse(create.Body).RootElement
        Assert.Equal(keyOf, body.GetProperty("key").GetString())
        Assert.Equal("s3cr3t", body.GetProperty("value").GetString())
    }

[<Fact>]
let ``a POST that conflicts is answered with a PUT again`` () =
    task {
        // The client's resilience handler retries a POST, so a create that timed
        // out may already have happened: the second PUT finds the key.
        let mutable puts = 0

        let fake =
            synchronous (
                withVault (fun seen ->
                    match seen.Method with
                    | "PUT" ->
                        puts <- puts + 1
                        if puts = 1 then status HttpStatusCode.NotFound else status HttpStatusCode.OK
                    | _ -> status HttpStatusCode.Conflict)
            )

        let! put = (storeWith (options fake)).Put(name, "s3cr3t", none)

        Assert.Equal($"proj/prod/vault/{keyOf}", (ok put).Locator)
        Assert.Equal(2, fake.Count "PUT")
        Assert.Equal(1, fake.Count "POST")
    }

[<Fact>]
let ``writing the same name twice gives the same reference`` () =
    task {
        let fake = synchronous (withVault (fun _ -> status HttpStatusCode.OK))
        let store = storeWith (options fake)
        let! first = store.Put(name, "one", none)
        let! second = store.Put(name, "two", none)
        Assert.Equal(ok first, ok second)
    }

[<Fact>]
let ``a write to a place that does not exist is not found`` () =
    task {
        let fake = synchronous (withVault (fun _ -> status HttpStatusCode.NotFound))
        let! put = (storeWith (options fake)).Put(name, "s3cr3t", none)
        Assert.Equal(Error NotFound, put)
    }

// --- read --------------------------------------------------------------------

[<Fact>]
let ``a read unwraps the value from the secrets object`` () =
    task {
        let fake = synchronous (fun _ -> secretResponse keyOf "s3cr3t-value")
        let! got = (storeWith (options fake)).Get(reference, none)

        Assert.Equal(Ok "s3cr3t-value", got)
        Assert.Equal(secretPath "vault" keyOf, (Assert.Single fake.Requests).Path)
    }

[<Fact>]
let ``a read uses the project environment and provider of the reference not the settings`` () =
    task {
        let fake = synchronous (fun _ -> secretResponse keyOf "old-home")
        let elsewhere = { reference with Locator = $"other/staging/legacy/{keyOf}" }
        let! got = (storeWith (options fake)).Get(elsewhere, none)

        Assert.Equal(Ok "old-home", got)
        Assert.Equal($"/api/v1/projects/other/environments/staging/providers/legacy/secrets/{keyOf}", (Assert.Single fake.Requests).Path)
    }

[<Fact>]
let ``a read whose answer lacks the key is not found`` () =
    task {
        let fake = synchronous (fun _ -> secretResponse "SOME_OTHER_KEY" "x")
        let! got = (storeWith (options fake)).Get(reference, none)
        Assert.Equal(Error NotFound, got)
    }

[<Fact>]
let ``a read of an answer of another shape is rejected without quoting it`` () =
    task {
        let fake = synchronous (fun _ -> json HttpStatusCode.OK """{"secrets":"leaked-text"}""")
        let! got = (storeWith (options fake)).Get(reference, none)
        Assert.Equal(Error(Rejected "unexpected_response"), got)
    }

[<Fact>]
let ``a reference of another store or a malformed locator is not this store's to read`` () =
    task {
        let fake = synchronous (fun _ -> secretResponse keyOf "x")
        let store = storeWith (options fake)
        let! foreign = store.Get({ Store = "aws"; Locator = "arn" }, none)
        let! malformed = store.Get({ Store = StoreId; Locator = "no-slashes" }, none)

        Assert.Equal(Error NotConfigured, foreign)
        Assert.Equal(Error(Rejected "locator_malformed"), malformed)
        Assert.Empty fake.Requests
    }

// --- delete and probe --------------------------------------------------------

[<Fact>]
let ``a delete is a DELETE of the locator's key`` () =
    task {
        let fake = synchronous (fun _ -> status HttpStatusCode.OK)
        let! deleted = (storeWith (options fake)).Delete(reference, none)

        Assert.Equal(Ok(), deleted)
        let request = Assert.Single fake.Requests
        Assert.Equal("DELETE", request.Method)
        Assert.Equal(secretPath "vault" keyOf, request.Path)
    }

[<Fact>]
let ``deleting what is already gone is Ok`` () =
    task {
        let fake = synchronous (fun _ -> status HttpStatusCode.NotFound)
        let! deleted = (storeWith (options fake)).Delete(reference, none)
        Assert.Equal(Ok(), deleted)
    }

[<Fact>]
let ``the probe reads the probe key and a 404 means healthy`` () =
    task {
        let fake = synchronous (withVault (fun _ -> status HttpStatusCode.NotFound))
        let! probe = (storeWith (options fake)).Probe none

        Assert.Equal(Ok(), probe)
        Assert.Contains(fake.Requests, fun r -> r.Method = "GET" && r.Path = secretPath "vault" "UMBRAL_UMBRAL_PROBE")
    }

[<Fact>]
let ``the probe fails when the credentials are refused`` () =
    task {
        let fake = synchronous (withVault (fun _ -> status HttpStatusCode.Unauthorized))
        let! probe = (storeWith (options fake)).Probe none
        Assert.Equal(Error AccessDenied, probe)
    }

// --- error mapping -----------------------------------------------------------

[<Theory>]
[<InlineData(401)>]
[<InlineData(403)>]
let ``a refusal of the credentials is access denied on every operation`` (code: int) =
    task {
        let fake = synchronous (fun _ -> status (enum code))
        let store = storeWith (options fake)
        let! put = store.Put(name, "s3cr3t", none)
        let! get = store.Get(reference, none)
        let! delete = store.Delete(reference, none)

        Assert.Equal(Error AccessDenied, put)
        Assert.Equal(Error AccessDenied, get)
        Assert.Equal(Error AccessDenied, delete)
    }

[<Theory>]
[<InlineData(429)>]
[<InlineData(500)>]
[<InlineData(502)>]
[<InlineData(503)>]
let ``throttling and server errors are unreachable`` (code: int) =
    task {
        let fake = synchronous (fun _ -> status (enum code))
        let store = storeWith (options fake)
        let! get = store.Get(reference, none)
        let! delete = store.Delete(reference, none)

        Assert.Equal(Error Unreachable, get)
        Assert.Equal(Error Unreachable, delete)
    }

[<Fact>]
let ``a 404 on a read is not found`` () =
    task {
        let fake = synchronous (fun _ -> status HttpStatusCode.NotFound)
        let! got = (storeWith (options fake)).Get(reference, none)
        Assert.Equal(Error NotFound, got)
    }

[<Fact>]
let ``a 404 that carries a problem document is still not found`` () =
    task {
        let fake = synchronous (fun _ -> json HttpStatusCode.NotFound """{"title":"Not Found","status":404}""")
        let! got = (storeWith (options fake)).Get(reference, none)
        Assert.Equal(Error NotFound, got)
    }

[<Theory>]
[<InlineData("http")>]
[<InlineData("circuit")>]
[<InlineData("timeout")>]
let ``a network failure or a tripped resilience policy is unreachable`` (kind: string) =
    task {
        let fake =
            new Fake(fun _ _ ->
                match kind with
                | "http" -> raise (HttpRequestException "connection refused")
                | "circuit" -> raise (BrokenCircuitException())
                | _ -> raise (TimeoutRejectedException "attempt timed out"))

        let! got = (storeWith (options fake)).Get(reference, none)
        Assert.Equal(Error Unreachable, got)
    }

[<Fact>]
let ``a rejection never carries the vendor's message or the value`` () =
    task {
        let plaintext = "s3cr3t-that-must-not-leak"

        let fake =
            synchronous (fun _ ->
                json HttpStatusCode.BadRequest $"{{\"title\":\"bad value {plaintext}\",\"detail\":\"{plaintext}\"}}")

        let! put = (storeWith (options fake)).Put(name, plaintext, none)

        match put with
        | Error(Rejected reason) -> Assert.DoesNotContain(plaintext, reason)
        | other -> failwith $"expected Rejected, got {other}"
    }

[<Fact>]
let ``an API key of the wrong shape is access denied`` () =
    task {
        // The client's signing handler refuses it when the client is built.
        let fake = synchronous (fun _ -> status HttpStatusCode.OK)
        let store = storeWith { options fake with Auth = ApiKey(Protected.Of "not-an-api-key") }
        let! put = store.Put(name, "s3cr3t", none)
        let! get = store.Get(reference, none)

        Assert.Equal(Error AccessDenied, put)
        Assert.Equal(Error AccessDenied, get)
        Assert.Empty fake.Requests
    }

[<Fact>]
let ``an unreadable private key is rejected without echoing it`` () =
    task {
        let fake = synchronous (fun _ -> status HttpStatusCode.OK)
        let store = storeWith { options fake with PrivateKeyPem = Some(Protected.Of "-----BEGIN NOPE-----") }
        let! get = store.Get(reference, none)
        Assert.Equal(Error(Rejected "private_key_unreadable"), get)
    }

[<Fact>]
let ``a bearer token works with a project and environment`` () =
    task {
        let fake = synchronous (fun _ -> secretResponse keyOf "via-token")
        let store = storeWith { options fake with Auth = AccessToken(Protected.Of "a-token") }
        let! got = store.Get(reference, none)
        Assert.Equal(Ok "via-token", got)
    }

[<Fact>]
let ``a bearer token with no project is rejected, not guessed`` () =
    task {
        let fake = synchronous (fun _ -> status HttpStatusCode.OK)
        let store = storeWith { options fake with Auth = AccessToken(Protected.Of "a-token"); Project = None }
        let! put = store.Put(name, "s3cr3t", none)
        Assert.Equal(Error(Rejected "project_or_environment_missing"), put)
    }

// --- hang --------------------------------------------------------------------

[<Fact>]
let ``a hung BellaBaxter is unreachable through the registry's bound`` () =
    task {
        let fake =
            new Fake(fun _ cancellationToken ->
                task {
                    do! Task.Delay(Timeout.Infinite, cancellationToken)
                    return status HttpStatusCode.OK
                })

        let inner = storeWith (options fake)
        let local = LocalSecretStore(Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create "Umbral.Tests")

        let registry =
            Registry.SecretStores(
                local,
                StoreId,
                Map [ StoreId, lazy (Ok inner) ],
                TimeSpan.FromMilliseconds 200.
            )

        let store = ok (registry.For StoreId |> Result.mapError (fun f -> f))
        let! got = store.Get(reference, none)
        let! put = store.Put(name, "s3cr3t", none)

        Assert.Equal(Error Unreachable, got)
        Assert.Equal(Error Unreachable, put)
    }

[<Fact>]
let ``the caller's own cancellation is theirs and not an answer`` () =
    task {
        let fake =
            new Fake(fun _ cancellationToken ->
                task {
                    do! Task.Delay(Timeout.Infinite, cancellationToken)
                    return status HttpStatusCode.OK
                })

        use source = new CancellationTokenSource(TimeSpan.FromMilliseconds 100.)

        let! outcome =
            task {
                try
                    let! _ = (storeWith (options fake)).Get(reference, source.Token)
                    return "answered"
                with :? OperationCanceledException ->
                    return "cancelled"
            }

        Assert.Equal("cancelled", outcome)
    }
