/// The `aws` store against a fake HTTP handler that answers in AWS's JSON 1.1
/// protocol, so no account, credentials or network are involved (FR-025). What
/// is asserted is what the store asks AWS for and what it makes of the answer.
module Umbral.Tests.AwsSecretStoreTests

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Amazon.Runtime
open Xunit
open Umbral.Infrastructure.Secrets
open Umbral.Infrastructure.SecretStores
open Umbral.Infrastructure.SecretStores.AwsStore

let private none = CancellationToken.None

let private name = SecretName.derive "umbral" "discord" (DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero))

let private arn = "arn:aws:secretsmanager:eu-west-1:123456789012:secret:umbral-discord-abcdefgh-AbCdEf"

/// One request the store sent: the AWS operation, its JSON body, and the
/// `Authorization` header (whose credential scope names the signing region).
type private Sent =
    { Operation: string
      Body: JsonElement
      Authorization: string }

/// An AWS JSON 1.1 answer, or an AWS error: status, error type and body.
type private Answer =
    | Json of string
    | AwsError of status: int * errorType: string * message: string
    | Status of int
    | Hang

/// Answers each request by `respond`, which sees the operation name, and keeps
/// what it was sent. The handler is the whole "AWS" of these tests.
type private FakeAws(respond: string -> Answer) =
    inherit HttpMessageHandler()

    let sent = List<Sent>()

    member _.Sent = lock sent (fun () -> List.ofSeq sent)
    member this.Operations = this.Sent |> List.map (fun s -> s.Operation)

    override _.SendAsync(request, ct) =
        task {
            let! text = (nonNull request.Content).ReadAsStringAsync ct
            let operation = request.Headers.GetValues("X-Amz-Target") |> Seq.head
            let operation = operation.Substring(operation.IndexOf '.' + 1)

            lock sent (fun () ->
                sent.Add
                    { Operation = operation
                      Body = JsonDocument.Parse(text).RootElement.Clone()
                      Authorization = String.Join(" ", request.Headers.GetValues "Authorization") })

            let reply (status: int) (body: string) =
                let response = new HttpResponseMessage(enum<HttpStatusCode> status)
                response.Content <- new StringContent(body, Encoding.UTF8, "application/x-amz-json-1.1")
                response

            match respond operation with
            | Json body -> return reply 200 body
            | AwsError(status, errorType, message) ->
                let response = reply status (JsonSerializer.Serialize {| ``__type`` = errorType; message = message |})
                response.Headers.Add("x-amzn-ErrorType", errorType)
                return response
            | Status status -> return reply status "{}"
            | Hang ->
                do! Task.Delay(Timeout.Infinite, ct)
                return reply 200 "{}"
        }

let private options (handler: HttpMessageHandler) : AwsStoreOptions =
    { Region = "eu-west-1"
      Prefix = "umbral"
      ServiceUrl = Some "http://localhost"
      HttpMessageHandler = Some handler
      Credentials = Some(BasicAWSCredentials("test", "test") :> AWSCredentials) }

let private storeOver (fake: FakeAws) = AwsStore.create (options fake)

let private created = Json $"""{{"ARN":"{arn}","Name":"umbral-discord-abcdefgh","VersionId":"v1"}}"""

let private ok (result: Result<'a, StoreFailure>) =
    match result with
    | Ok value -> value
    | Error failure -> failwith $"expected Ok, got {failure.Cause}"

let private aws = { Store = "aws"; Locator = arn }

[<Fact>]
let ``creating a secret returns its ARN as the locator`` () =
    task {
        let fake = new FakeAws(fun _ -> created)
        let! put = (storeOver fake).Put(name, "s3cr3t-value", none)

        Assert.Equal(Ok aws, put)
        let call = fake.Sent |> List.exactlyOne
        Assert.Equal("CreateSecret", call.Operation)
        Assert.Equal(name.Cloud, call.Body.GetProperty("Name").GetString())
        Assert.Equal("s3cr3t-value", call.Body.GetProperty("SecretString").GetString())
    }

[<Fact>]
let ``creating over an existing name adds a version and returns the same reference`` () =
    task {
        let fake =
            new FakeAws(function
                | "CreateSecret" -> AwsError(400, "ResourceExistsException", "already exists")
                | _ -> Json $"""{{"ARN":"{arn}","Name":"umbral-discord-abcdefgh","VersionId":"v2"}}""")

        let! put = (storeOver fake).Put(name, "second", none)

        Assert.Equal(Ok aws, put)
        Assert.Equal<string list>([ "CreateSecret"; "PutSecretValue" ], fake.Operations)
        let version = fake.Sent[1]
        Assert.Equal(name.Cloud, version.Body.GetProperty("SecretId").GetString())
        Assert.Equal("second", version.Body.GetProperty("SecretString").GetString())
    }

[<Fact>]
let ``a name in its recovery window is rejected without the vendor text`` () =
    task {
        let fake =
            new FakeAws(fun _ ->
                AwsError(400, "InvalidRequestException", "secret umbral-discord-abcdefgh is scheduled for deletion"))

        let! put = (storeOver fake).Put(name, "s3cr3t-value", none)

        Assert.Equal(Error(Rejected "soft_deleted"), put)
    }

[<Fact>]
let ``a refused request carries the error code and never its message`` () =
    task {
        let fake = new FakeAws(fun _ -> AwsError(400, "InvalidParameterException", "bad value s3cr3t-value for umbral-discord"))

        let! put = (storeOver fake).Put(name, "s3cr3t-value", none)

        Assert.Equal(Error(Rejected "InvalidParameterException"), put)
        Assert.DoesNotContain("s3cr3t-value", $"%A{put}")
    }

[<Fact>]
let ``reading asks for the ARN and returns the secret string`` () =
    task {
        let fake = new FakeAws(fun _ -> Json $"""{{"ARN":"{arn}","Name":"n","SecretString":"s3cr3t-value","VersionId":"v1"}}""")
        let! got = (storeOver fake).Get(aws, none)

        Assert.Equal(Ok "s3cr3t-value", got)
        let call = fake.Sent |> List.exactlyOne
        Assert.Equal("GetSecretValue", call.Operation)
        Assert.Equal(arn, call.Body.GetProperty("SecretId").GetString())
    }

[<Fact>]
let ``a reference is read from the region in its ARN, not the configured one`` () =
    task {
        let fake = new FakeAws(fun _ -> Json $"""{{"ARN":"{arn}","SecretString":"v"}}""")
        let store = AwsStore.create { options fake with Region = "us-east-2" }
        let! _ = store.Get(aws, none)

        // SigV4 names the region it signs for in the credential scope.
        Assert.Contains("/eu-west-1/secretsmanager/", fake.Sent.Head.Authorization)
    }

[<Fact>]
let ``a secret that is not there is not found`` () =
    task {
        let fake = new FakeAws(fun _ -> AwsError(400, "ResourceNotFoundException", "no such secret"))
        let! got = (storeOver fake).Get(aws, none)

        Assert.Equal(Error NotFound, got)
    }

[<Fact>]
let ``a secret already scheduled for deletion reads as not found`` () =
    task {
        let fake =
            new FakeAws(fun _ ->
                AwsError(400, "InvalidRequestException", "You can't perform this operation on a secret that's marked for deletion"))

        let! got = (storeOver fake).Get(aws, none)

        Assert.Equal(Error NotFound, got)
    }

[<Fact>]
let ``deleting schedules deletion with a seven day recovery window`` () =
    task {
        let fake = new FakeAws(fun _ -> Json $"""{{"ARN":"{arn}","Name":"n","DeletionDate":1.0}}""")
        let! deleted = (storeOver fake).Delete(aws, none)

        Assert.Equal(Ok(), deleted)
        let call = fake.Sent |> List.exactlyOne
        Assert.Equal("DeleteSecret", call.Operation)
        Assert.Equal(arn, call.Body.GetProperty("SecretId").GetString())
        Assert.Equal(7, call.Body.GetProperty("RecoveryWindowInDays").GetInt32())
    }

[<Fact>]
let ``deleting something already gone is fine`` () =
    task {
        let fake = new FakeAws(fun _ -> AwsError(400, "ResourceNotFoundException", "no such secret"))
        let! deleted = (storeOver fake).Delete(aws, none)

        Assert.Equal(Ok(), deleted)
    }

[<Fact>]
let ``deleting something already scheduled for deletion is fine`` () =
    task {
        let fake =
            new FakeAws(fun _ ->
                AwsError(400, "InvalidRequestException", "The secret is already scheduled for deletion"))

        let! deleted = (storeOver fake).Delete(aws, none)

        Assert.Equal(Ok(), deleted)
    }

[<Theory>]
[<InlineData(400, "AccessDeniedException")>]
[<InlineData(400, "UnrecognizedClientException")>]
[<InlineData(403, "InvalidSignatureException")>]
[<InlineData(400, "ExpiredTokenException")>]
[<InlineData(400, "DecryptionFailure")>]
let ``a refused identity is access denied, whatever the operation`` (status: int, errorType: string) =
    task {
        let fake = new FakeAws(fun _ -> AwsError(status, errorType, "denied"))
        let store = storeOver fake

        let! put = store.Put(name, "v", none)
        let! got = store.Get(aws, none)
        let! deleted = store.Delete(aws, none)
        let! probe = store.Probe none

        Assert.Equal(Error AccessDenied, put)
        Assert.Equal(Error AccessDenied, got)
        Assert.Equal(Error AccessDenied, deleted)
        Assert.Equal(Error AccessDenied, probe)
    }

[<Fact>]
let ``throttling that outlasts the retries is unreachable, after exactly two retries`` () =
    task {
        let fake = new FakeAws(fun _ -> AwsError(400, "ThrottlingException", "slow down"))
        let! got = (storeOver fake).Get(aws, none)

        Assert.Equal(Error Unreachable, got)
        // One attempt and MaxErrorRetry = 2: the SDK's retries must not spend
        // the registry's bound.
        Assert.Equal(3, fake.Sent.Length)
    }

[<Fact>]
let ``a server error is unreachable`` () =
    task {
        let fake = new FakeAws(fun _ -> AwsError(500, "InternalServiceError", "boom"))
        let! got = (storeOver fake).Get(aws, none)

        Assert.Equal(Error Unreachable, got)
    }

[<Fact>]
let ``a bare 503 with no AWS body is unreachable`` () =
    task {
        let fake = new FakeAws(fun _ -> Status 503)
        let! put = (storeOver fake).Put(name, "v", none)

        Assert.Equal(Error Unreachable, put)
    }

[<Fact>]
let ``a handler that cannot connect is unreachable`` () =
    task {
        let handler =
            { new HttpMessageHandler() with
                member _.SendAsync(_, _) : Task<HttpResponseMessage> = raise (HttpRequestException "connection refused") }

        let! got = (AwsStore.create (options handler)).Get(aws, none)

        Assert.Equal(Error Unreachable, got)
    }

/// Credentials the SDK cannot resolve, as it reports them when the default
/// chain finds nothing.
type private NoCredentials() =
    inherit AWSCredentials()
    override _.GetCredentials() =
        raise (AmazonClientException "Unable to find credentials")

[<Fact>]
let ``no credentials at all is access denied, not an outage`` () =
    task {
        let fake = new FakeAws(fun _ -> created)
        let store = AwsStore.create { options fake with Credentials = Some(NoCredentials() :> AWSCredentials) }

        let! put = store.Put(name, "v", none)

        Assert.Equal(Error AccessDenied, put)
        Assert.Empty fake.Sent
    }

[<Fact>]
let ``the probe treats a missing probe secret as reachable`` () =
    task {
        let fake = new FakeAws(fun _ -> AwsError(400, "ResourceNotFoundException", "no such secret"))
        let! probe = (storeOver fake).Probe none

        Assert.Equal(Ok(), probe)
        let call = fake.Sent |> List.exactlyOne
        Assert.Equal("GetSecretValue", call.Operation)
        Assert.Equal("umbral-probe", call.Body.GetProperty("SecretId").GetString())
    }

[<Fact>]
let ``a reference with another store id is not configured here`` () =
    task {
        let fake = new FakeAws(fun _ -> created)
        let! got = (storeOver fake).Get({ Store = "gcp"; Locator = arn }, none)

        Assert.Equal(Error NotConfigured, got)
        Assert.Empty fake.Sent
    }

[<Fact>]
let ``a locator that is not an ARN is rejected without a call`` () =
    task {
        let fake = new FakeAws(fun _ -> created)
        let! got = (storeOver fake).Get({ Store = "aws"; Locator = "not-an-arn" }, none)

        Assert.Equal(Error(Rejected "malformed_locator"), got)
        Assert.Empty fake.Sent
    }

[<Fact>]
let ``a handler that hangs is unreachable once the registry's bound passes`` () =
    task {
        let fake = new FakeAws(fun _ -> Hang)
        let local = LocalSecretStore(Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create "Umbral.Tests")

        let registry =
            Registry.SecretStores(
                local,
                "aws",
                Map [ "aws", lazy (Ok(storeOver fake))],
                TimeSpan.FromMilliseconds 200.
            )

        let store = registry.Active
        let! put = store.Put(name, "v", none)
        let! got = store.Get(aws, none)
        let! probe = store.Probe none

        Assert.Equal(Error Unreachable, put)
        Assert.Equal(Error Unreachable, got)
        Assert.Equal(Error Unreachable, probe)
    }

[<Fact>]
let ``cancelling the token the store was given propagates as a cancellation`` () =
    task {
        let fake = new FakeAws(fun _ -> Hang)
        use cts = new CancellationTokenSource(TimeSpan.FromMilliseconds 100.)

        let! _ = Assert.ThrowsAnyAsync<OperationCanceledException>(fun () -> (storeOver fake).Get(aws, cts.Token) :> Task)
        ()
    }
