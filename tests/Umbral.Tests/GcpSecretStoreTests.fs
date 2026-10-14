/// The `gcp` store against a fake HTTP handler (specs/005 T012). No account, no
/// network: the credential is a static access token and the handler answers.
module Umbral.Tests.GcpSecretStoreTests

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Google.Apis.Auth.OAuth2
open Xunit
open Umbral.Infrastructure.Secrets
open Umbral.Infrastructure.SecretStores
open Umbral.Infrastructure.SecretStores.GcpStore

/// What the store sent, with the body read before the request is disposed.
type private Seen =
    { Method: string
      Path: string
      Query: string
      Authorization: string
      Body: string }

/// Answers each request with `respond` and keeps what it was sent.
type private FakeHandler(respond: Seen -> HttpResponseMessage) =
    inherit HttpMessageHandler()
    let seen = List<Seen>()
    member _.Requests = seen |> List.ofSeq

    override _.SendAsync(request, _cancellationToken) =
        task {
            let! body =
                match request.Content with
                | null -> Task.FromResult ""
                | content -> content.ReadAsStringAsync()

            let entry =
                let uri = request.RequestUri |> Option.ofObj |> Option.get

                { Method = request.Method.Method
                  Path = uri.AbsolutePath
                  Query = uri.Query
                  Authorization = string request.Headers.Authorization
                  Body = body }

            lock seen (fun () -> seen.Add entry)
            return respond entry
        }

/// Never answers until the token is cancelled, like a manager that hangs.
type private HangingHandler() =
    inherit HttpMessageHandler()

    override _.SendAsync(_request, cancellationToken) =
        task {
            do! Task.Delay(Timeout.Infinite, cancellationToken)
            return new HttpResponseMessage(HttpStatusCode.OK)
        }

/// A handler that fails the way a dead network does.
type private DeadHandler() =
    inherit HttpMessageHandler()

    override _.SendAsync(_request, _cancellationToken) =
        Task.FromException<HttpResponseMessage>(HttpRequestException "boom")

let private fake respond = new FakeHandler(respond)

let private json (status: HttpStatusCode) (body: string) =
    new HttpResponseMessage(status, Content = new StringContent(body, Encoding.UTF8, "application/json"))

let private status (code: int) = json (enum<HttpStatusCode> code) "{}"

let private none = CancellationToken.None

let private name = SecretName.derive "umbral" "github" (DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero))

let private options (handler: HttpMessageHandler) =
    { ProjectId = "proj-1"
      Prefix = "umbral"
      Credential = Some(GoogleCredential.FromAccessToken "test-token")
      HttpMessageHandler = Some handler
      BaseUri = Some(Uri "https://secrets.test/v1/") }

let private storeOver (handler: HttpMessageHandler) = create (options handler)

let private ok (result: Result<'a, StoreFailure>) =
    match result with
    | Ok value -> value
    | Error failure -> failwith $"expected Ok, got {failure.Cause}"

let private plaintext = "s3cr3t-value-ünï"

let private reference locator = { Store = StoreId; Locator = locator }

let private b64 (text: string) = Convert.ToBase64String(Encoding.UTF8.GetBytes text)

/// The body `:access` answers with for `text`, checksum included.
let private accessBody (text: string) =
    let bytes = Encoding.UTF8.GetBytes text
    "{\"name\":\"x\",\"payload\":{\"data\":\"" + Convert.ToBase64String bytes + "\",\"dataCrc32c\":\"" + string (crc32c bytes) + "\"}}"

let private failureOf (result: Result<'a, StoreFailure>) =
    match result with
    | Error failure -> failure
    | Ok _ -> failwith "expected a failure"

[<Fact>]
let ``crc32c matches the standard check value`` () =
    Assert.Equal(0xE3069283u, crc32c (Encoding.ASCII.GetBytes "123456789"))
    Assert.Equal(0u, crc32c [||])

[<Fact>]
let ``put creates the secret and adds a base64 version with its crc32c`` () =
    task {
        let handler = fake (fun _ -> json HttpStatusCode.OK "{}")
        let! result = (storeOver handler).Put(name, plaintext, none)

        let reference = ok result
        Assert.Equal("gcp", reference.Store)
        Assert.Equal($"projects/proj-1/secrets/{name.Cloud}", reference.Locator)

        match handler.Requests with
        | [ create; add ] ->
            Assert.Equal("POST", create.Method)
            Assert.Equal("/v1/projects/proj-1/secrets", create.Path)
            Assert.Equal($"?secretId={name.Cloud}", create.Query)
            Assert.Equal("Bearer test-token", create.Authorization)
            Assert.Equal("""{"replication":{"automatic":{}}}""", create.Body)

            Assert.Equal("POST", add.Method)
            Assert.Equal($"/v1/projects/proj-1/secrets/{name.Cloud}:addVersion", add.Path)
            Assert.Equal("Bearer test-token", add.Authorization)

            use doc = JsonDocument.Parse add.Body
            let payload = doc.RootElement.GetProperty "payload"
            Assert.Equal(b64 plaintext, payload.GetProperty("data").GetString())
            let expected = crc32c (Encoding.UTF8.GetBytes plaintext)
            Assert.Equal(string expected, payload.GetProperty("dataCrc32c").GetString())
        | other -> failwith $"expected two requests, got {other.Length}"
    }

[<Fact>]
let ``put onto an existing name only adds a version and returns the same reference`` () =
    task {
        let handler =
            fake (fun seen -> if seen.Path.EndsWith "/secrets" then status 409 else json HttpStatusCode.OK "{}")

        let store = storeOver handler
        let! first = store.Put(name, plaintext, none)
        let! second = store.Put(name, "another", none)

        Assert.Equal(ok first, ok second)
        // Two puts, each a create that answered 409 and then an addVersion.
        Assert.Equal(4, handler.Requests.Length)
        Assert.Equal(2, handler.Requests |> List.filter (fun r -> r.Path.EndsWith ":addVersion") |> List.length)
    }

[<Fact>]
let ``put never puts the plaintext in the url or the create body`` () =
    task {
        let handler = fake (fun _ -> json HttpStatusCode.OK "{}")
        let! _ = (storeOver handler).Put(name, plaintext, none)

        for r in handler.Requests do
            Assert.DoesNotContain(plaintext, r.Path + r.Query)

        Assert.DoesNotContain(plaintext, handler.Requests[0].Body)
    }

[<Fact>]
let ``get reads the latest version and decodes the base64 payload`` () =
    task {
        let handler = fake (fun _ -> json HttpStatusCode.OK (accessBody plaintext))
        let! result = (storeOver handler).Get(reference "projects/proj-1/secrets/umbral-github-abcd2345", none)

        Assert.Equal(Ok plaintext, result)
        let request = Assert.Single handler.Requests
        Assert.Equal("GET", request.Method)
        Assert.Equal("/v1/projects/proj-1/secrets/umbral-github-abcd2345/versions/latest:access", request.Path)
        Assert.Equal("Bearer test-token", request.Authorization)
    }

[<Fact>]
let ``get reads from the project in the locator, not the configured one`` () =
    task {
        let handler = fake (fun _ -> json HttpStatusCode.OK (accessBody plaintext))
        let! result = (storeOver handler).Get(reference "projects/other-proj/secrets/umbral-github-abcd2345", none)

        Assert.Equal(Ok plaintext, result)
        Assert.StartsWith("/v1/projects/other-proj/", (Assert.Single handler.Requests).Path)
    }

[<Fact>]
let ``get with a wrong checksum is rejected without the value`` () =
    task {
        let body = "{\"payload\":{\"data\":\"" + b64 plaintext + "\",\"dataCrc32c\":\"12345\"}}"
        let handler = fake (fun _ -> json HttpStatusCode.OK body)
        let! result = (storeOver handler).Get(reference "projects/proj-1/secrets/umbral-x-abcd2345", none)

        Assert.Equal(Error(Rejected "checksum"), result)
    }

[<Fact>]
let ``get of a body that is not a payload is rejected`` () =
    task {
        let handler = fake (fun _ -> json HttpStatusCode.OK """{"payload":{"data":"!!not base64!!"}}""")
        let! result = (storeOver handler).Get(reference "projects/proj-1/secrets/umbral-x-abcd2345", none)

        Assert.Equal(Error(Rejected "malformed_response"), result)
    }

[<Fact>]
let ``a locator that is not a secret name is rejected without a request`` () =
    task {
        let handler = fake (fun _ -> status 200)
        let! result = (storeOver handler).Get(reference "projects/proj-1/secrets/../../x", none)

        Assert.Equal(Error(Rejected "locator"), result)
        Assert.Empty handler.Requests
    }

[<Fact>]
let ``a reference of another store is a programming error`` () =
    task {
        use handler = fake (fun _ -> status 200)

        let! outcome =
            Record.ExceptionAsync(fun () ->
                (storeOver handler).Get({ Store = "aws"; Locator = "arn" }, none) :> Task)

        Assert.IsAssignableFrom<ArgumentException> outcome |> ignore
        Assert.Empty handler.Requests
    }

[<Fact>]
let ``delete removes the secret by its locator`` () =
    task {
        let handler = fake (fun _ -> json HttpStatusCode.OK "{}")
        let! result = (storeOver handler).Delete(reference "projects/proj-1/secrets/umbral-github-abcd2345", none)

        Assert.Equal(Ok(), result)
        let request = Assert.Single handler.Requests
        Assert.Equal("DELETE", request.Method)
        Assert.Equal("/v1/projects/proj-1/secrets/umbral-github-abcd2345", request.Path)
        Assert.Equal("Bearer test-token", request.Authorization)
    }

[<Fact>]
let ``delete of a secret that is gone is Ok`` () =
    task {
        let handler = fake (fun _ -> status 404)
        let! result = (storeOver handler).Delete(reference "projects/proj-1/secrets/umbral-github-abcd2345", none)

        Assert.Equal(Ok(), result)
    }

[<Fact>]
let ``get of a missing secret is NotFound`` () =
    task {
        let! result =
            (storeOver (fake (fun _ -> status 404))).Get(reference "projects/proj-1/secrets/umbral-x-abcd2345", none)

        Assert.Equal(Error NotFound, result)
    }

/// The outcome without the value, so calls of different types can be listed together.
let private forgetValue (call: Task<Result<'a, StoreFailure>>) : Task<Result<unit, StoreFailure>> =
    task {
        let! result = call
        return result |> Result.map ignore
    }

/// Every operation, so a status is checked against all of them and not only the
/// one a test happened to pick.
let private everyCall (store: ISecretStore) : (string * Task<Result<unit, StoreFailure>>) list =
    let r = reference "projects/proj-1/secrets/umbral-x-abcd2345"
    [ "put", forgetValue (store.Put(name, "v", none))
      "get", forgetValue (store.Get(r, none))
      "delete", store.Delete(r, none)
      "probe", store.Probe none ]

[<Theory>]
[<InlineData(401)>]
[<InlineData(403)>]
let ``401 and 403 are AccessDenied on every call`` (code: int) =
    task {
        for (op, call) in everyCall (storeOver (fake (fun _ -> status code))) do
            let! result = call
            Assert.True((result = Error AccessDenied), $"{op} answered {result}")
    }

[<Theory>]
[<InlineData(429)>]
[<InlineData(500)>]
[<InlineData(503)>]
let ``429 and 5xx are Unreachable on every call`` (code: int) =
    task {
        for (op, call) in everyCall (storeOver (fake (fun _ -> status code))) do
            let! result = call
            Assert.True((result = Error Unreachable), $"{op} answered {result}")
    }

[<Fact>]
let ``another 4xx is Rejected with the status code and nothing else`` () =
    task {
        // Google's error bodies can echo the secret name; the reason must not.
        let handler =
            fake (fun _ -> json HttpStatusCode.BadRequest """{"error":{"message":"secret umbral-github-abcd2345 s3cr3t"}}""")

        for (op, call) in everyCall (storeOver handler) do
            let! result = call
            Assert.True((result = Error(Rejected "400")), $"{op} answered {result}")
    }

[<Fact>]
let ``a dead network is Unreachable`` () =
    task {
        for (op, call) in everyCall (storeOver (new DeadHandler())) do
            let! result = call
            Assert.True((result = Error Unreachable), $"{op} answered {result}")
    }

[<Fact>]
let ``a failure to create the secret stops the put before any version is written`` () =
    task {
        let handler = fake (fun _ -> status 403)
        let! result = (storeOver handler).Put(name, plaintext, none)

        Assert.Equal(Error AccessDenied, result)
        Assert.Single handler.Requests |> ignore
    }

[<Fact>]
let ``a secret deleted between create and addVersion is NotFound`` () =
    task {
        let handler =
            fake (fun seen -> if seen.Path.EndsWith ":addVersion" then status 404 else status 409)

        let! result = (storeOver handler).Put(name, plaintext, none)
        Assert.Equal(Error NotFound, result)
    }

[<Fact>]
let ``probe treats a 404 for the probe secret as healthy`` () =
    task {
        let handler = fake (fun _ -> status 404)
        let! result = (storeOver handler).Probe none

        Assert.Equal(Ok(), result)
        let request = Assert.Single handler.Requests
        Assert.Equal("GET", request.Method)
        Assert.Equal("/v1/projects/proj-1/secrets/umbral-probe/versions/latest:access", request.Path)
        Assert.Equal("Bearer test-token", request.Authorization)
    }

[<Fact>]
let ``probe does not read the value when the probe secret happens to exist`` () =
    task {
        let handler = fake (fun _ -> json HttpStatusCode.OK (accessBody "whatever"))
        let! result = (storeOver handler).Probe none
        Assert.Equal(Ok(), result)
    }

[<Fact>]
let ``a store without a project is NotConfigured and sends nothing`` () =
    task {
        let handler = fake (fun _ -> status 200)
        let store = create { options handler with ProjectId = "" }

        for (op, call) in
            [ "put", forgetValue (store.Put(name, "v", none))
              "probe", store.Probe none ] do
            let! result = call
            Assert.True((result = Error NotConfigured), $"{op} answered {result}")

        Assert.Empty handler.Requests
    }

[<Fact>]
let ``no Application Default Credentials is AccessDenied`` () =
    task {
        // Only meaningful where ADC cannot be found; elsewhere the call may reach
        // the (unreachable) test host and fail some other way.
        let saved =
            [ "GOOGLE_APPLICATION_CREDENTIALS"; "CLOUDSDK_CONFIG"; "GCE_METADATA_HOST" ]
            |> List.map (fun k -> k, Environment.GetEnvironmentVariable k)

        try
            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", "/nonexistent/adc.json")
            let handler = fake (fun _ -> status 200)
            let store = create { options handler with Credential = None }
            let! result = store.Probe none

            Assert.Equal(Error AccessDenied, result)
            Assert.Empty handler.Requests
        finally
            for (k, v) in saved do
                Environment.SetEnvironmentVariable(k, v)
    }

[<Fact>]
let ``a hang is Unreachable through the registry's bound`` () =
    task {
        let local = LocalSecretStore(Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create "Umbral.Tests")
        let store = storeOver (new HangingHandler())

        let registry =
            Registry.SecretStores(local, "gcp", Map [ "gcp", lazy (Ok store) ], TimeSpan.FromMilliseconds 200.)

        let active = registry.Active
        let r = reference "projects/proj-1/secrets/umbral-x-abcd2345"

        let! put = active.Put(name, "v", none)
        let! get = active.Get(r, none)
        let! delete = active.Delete(r, none)
        let! probe = active.Probe none

        Assert.Equal(Error Unreachable, put)
        Assert.Equal(Error Unreachable, get)
        Assert.Equal(Error Unreachable, delete)
        Assert.Equal(Error Unreachable, probe)
    }

[<Fact>]
let ``the callers own cancellation is not turned into a failure`` () =
    task {
        use cts = new CancellationTokenSource()
        cts.Cancel()
        let! outcome = Record.ExceptionAsync(fun () -> (storeOver (new HangingHandler())).Probe cts.Token :> Task)

        Assert.IsAssignableFrom<OperationCanceledException> outcome |> ignore
    }
