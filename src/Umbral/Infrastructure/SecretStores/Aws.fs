/// The `aws` store, over AWS Secrets Manager (specs/005 research R4).
///
/// Umbral keeps only the secret's ARN. The ARN carries the region and the
/// account, so a reference written while `AWS_REGION` was `eu-west-1` still
/// reads from `eu-west-1` after the operator changes it: Get and Delete take the
/// region from the locator, and the configured region matters only for new
/// secrets and for the probe. That is what keeps a changed setting from quietly
/// pointing a connector at a different secret.
///
/// Credentials come from the SDK's default chain (environment keys,
/// `AWS_PROFILE`, web identity, ECS/EKS, the instance metadata service). Nothing
/// here reads or stores a key.
module Umbral.Infrastructure.SecretStores.AwsStore

open System
open System.Collections.Concurrent
open System.Net
open System.Net.Http
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open Amazon
open Amazon.Runtime
open Amazon.SecretsManager
open Amazon.SecretsManager.Model
open Umbral.Infrastructure.Secrets

[<Literal>]
let StoreId = "aws"

/// What the store needs to be built. `ServiceUrl`, `HttpMessageHandler` and
/// `Credentials` exist for tests (a fake handler returning canned AWS JSON and
/// fixed keys, so no account is touched); production leaves them `None` and the
/// SDK reads its endpoint override (`AWS_ENDPOINT_URL_SECRETS_MANAGER`) and its
/// credential chain itself.
type AwsStoreOptions =
    { /// `AWS_REGION`: where new secrets are created.
      Region: string
      /// Only to name the probe secret, `{prefix}-probe`, in the same style as
      /// every other name this store uses.
      Prefix: string
      ServiceUrl: string option
      HttpMessageHandler: HttpMessageHandler option
      Credentials: AWSCredentials option }

/// Hands the SDK one `HttpClient` over a given handler. The SDK would otherwise
/// cache clients by a key derived from the configuration, which two tests with
/// different handlers would share, so caching is turned off and the one client
/// is held here.
type private HandlerFactory(handler: HttpMessageHandler) =
    inherit HttpClientFactory()
    let client = new HttpClient(handler, disposeHandler = false)
    override _.CreateHttpClient(_config) = client
    override _.UseSDKHttpClientCaching(_config) = false
    override _.DisposeHttpClientsAfterUse(_config) = false

/// `arn:aws:secretsmanager:{region}:{account}:secret:{name}-{suffix}`. The
/// region is the fourth field; anything that is not that shape is not a locator
/// this store wrote.
let private regionOfArn (arn: string) =
    match arn.Split ':' with
    | [| "arn"; _; "secretsmanager"; region; _; "secret"; _ |] when region <> "" -> Some region
    | _ -> None

/// A name AWS keeps for its recovery window refuses both a create and a new
/// version with `InvalidRequestException`; the text is the only thing that tells
/// that case from other invalid requests. It is read, never copied anywhere.
let private mentionsDeletion (ex: exn) =
    ex.Message.Contains("deletion", StringComparison.OrdinalIgnoreCase)

/// The SDK reports an absent credential chain as a client or service exception
/// whose text says so; there is no code to match.
let private mentionsCredentials (ex: exn) =
    ex.Message.Contains("credential", StringComparison.OrdinalIgnoreCase)

let private codePattern = Regex("^[A-Za-z]{1,64}$", RegexOptions.Compiled)

/// Maps what the SDK threw to a `StoreFailure`, or `None` for the caller's own
/// cancellation, which is not a failure of the store and must keep propagating
/// (the registry decides what a cancelled call means).
///
/// Missing credentials are `AccessDenied` and not `Unreachable`: waiting does
/// not help, the operator has to fix the credentials, and `Unreachable` is the
/// one cause the cache answers by serving a stale value.
///
/// The `Rejected` reason is an exception type name or an AWS error code, both
/// fixed vocabularies; never `Message`, which can echo a secret's name.
let private classify (ct: CancellationToken) (ex: exn) : StoreFailure option =
    match ex with
    | :? OperationCanceledException when ct.IsCancellationRequested -> None
    // Not the caller's token: the SDK's own timeout, or a dropped connection
    // surfacing as a cancellation. From outside, a store that does not answer
    // is a store that cannot be reached.
    | :? OperationCanceledException -> Some Unreachable
    | :? AmazonServiceException as e when mentionsCredentials e -> Some AccessDenied
    | :? AmazonServiceException as e ->
        let status = int e.StatusCode

        match e.ErrorCode with
        | "AccessDeniedException"
        | "UnrecognizedClientException"
        | "InvalidSignatureException"
        | "ExpiredTokenException"
        | "DecryptionFailure" -> Some AccessDenied
        | "ResourceNotFoundException" -> Some NotFound
        | "ThrottlingException"
        | "InternalServiceError"
        | "ServiceUnavailable" -> Some Unreachable
        | _ when status = 401 || status = 403 -> Some AccessDenied
        // 0 is "no HTTP answer at all": the connection failed under the SDK.
        | _ when status = 0 || status = 429 || status >= 500 -> Some Unreachable
        | _ when (e :? InvalidRequestException) && mentionsDeletion e -> Some(Rejected "soft_deleted")
        | code when codePattern.IsMatch(code |> Option.ofObj |> Option.defaultValue "") -> Some(Rejected code)
        | _ -> Some(Rejected(e.GetType().Name))
    | :? AmazonClientException as e when mentionsCredentials e -> Some AccessDenied
    | :? AmazonClientException
    | :? HttpRequestException
    | :? IO.IOException
    | :? Sockets.SocketException -> Some Unreachable
    | _ -> Some(Rejected(ex.GetType().Name))

/// Runs `work`, turning anything the SDK throws into an `Error`. The caller's
/// own cancellation is let through unchanged.
let private guard (ct: CancellationToken) (work: unit -> Task<Result<'a, StoreFailure>>) : Task<Result<'a, StoreFailure>> =
    task {
        try
            return! work ()
        with ex when (classify ct ex).IsSome ->
            return Error((classify ct ex).Value)
    }

type AwsSecretStore(options: AwsStoreOptions) =

    // One client per region, built the first time that region is needed and
    // kept: a client owns connection state and signing caches. The ARN of a
    // reference picks the region, so this holds as many as the references in
    // use span, normally one. `PublicationOnly` so that a failed build (no
    // region configured, say) is tried again instead of being remembered.
    let clients =
        ConcurrentDictionary<string, Lazy<AmazonSecretsManagerClient>>(StringComparer.OrdinalIgnoreCase)

    let build (region: string) =
        let config = AmazonSecretsManagerConfig()
        // The bound is the token's (10 s, set by the registry); the SDK's own
        // retries must not spend it, so at most two (research R3).
        config.MaxErrorRetry <- 2

        match options.ServiceUrl with
        | Some url ->
            config.ServiceURL <- url
            config.AuthenticationRegion <- region
        | None when String.IsNullOrWhiteSpace region -> ()
        | None -> config.RegionEndpoint <- RegionEndpoint.GetBySystemName region

        options.HttpMessageHandler
        |> Option.iter (fun handler -> config.HttpClientFactory <- HandlerFactory handler)

        match options.Credentials with
        | Some credentials -> new AmazonSecretsManagerClient(credentials, config)
        | None -> new AmazonSecretsManagerClient(config)

    // A client that cannot be built is a store that is not set up here (no
    // region and no endpoint), which is the operator's to fix, not an outage.
    let clientFor (region: string) : Result<AmazonSecretsManagerClient, StoreFailure> =
        try
            let lazyClient =
                clients.GetOrAdd(
                    region,
                    fun r -> Lazy<AmazonSecretsManagerClient>((fun () -> build r), LazyThreadSafetyMode.PublicationOnly)
                )

            Ok lazyClient.Value
        with :? AmazonClientException ->
            Error NotConfigured

    let clientForLocator (reference: SecretRef) =
        if reference.Store <> StoreId then
            // The registry routes a reference to the store that wrote it, so
            // this is a caller's mistake; a message beats a stack trace.
            Error NotConfigured
        else
            match regionOfArn reference.Locator with
            | Some region -> clientFor region
            | None -> Error(Rejected "malformed_locator")

    let reference (arn: string) : SecretRef = { Store = StoreId; Locator = arn }

    interface ISecretStore with
        member _.Id = StoreId

        member _.Put(name, plaintext, ct) =
            match clientFor options.Region with
            | Error failure -> Task.FromResult(Error failure)
            | Ok client ->
                guard ct (fun () ->
                    task {
                        try
                            let! created =
                                client.CreateSecretAsync(CreateSecretRequest(Name = name.Cloud, SecretString = plaintext), ct)

                            return Ok(reference created.ARN)
                        with :? ResourceExistsException ->
                            // The name is ours already (a retried write, a
                            // re-run move, or the SDK retrying a create that
                            // did land): add a version, and the same ARN comes
                            // back, so the stored reference does not change.
                            let! added =
                                client.PutSecretValueAsync(
                                    PutSecretValueRequest(SecretId = name.Cloud, SecretString = plaintext),
                                    ct
                                )

                            return Ok(reference added.ARN)
                    })

        member _.Get(reference, ct) =
            match clientForLocator reference with
            | Error failure -> Task.FromResult(Error failure)
            | Ok client ->
                guard ct (fun () ->
                    task {
                        try
                            let! response =
                                client.GetSecretValueAsync(GetSecretValueRequest(SecretId = reference.Locator), ct)

                            // A binary secret is not one Umbral wrote.
                            if isNull response.SecretString then
                                return Error(Rejected "binary_secret")
                            else
                                return Ok response.SecretString
                        with :? InvalidRequestException as e when mentionsDeletion e ->
                            // Scheduled for deletion: it cannot be read and
                            // will not come back by waiting, which is what
                            // `NotFound` says.
                            return Error NotFound
                    })

        member _.Delete(reference, ct) =
            match clientForLocator reference with
            | Error failure -> Task.FromResult(Error failure)
            | Ok client ->
                guard ct (fun () ->
                    task {
                        try
                            // A recovery window, not a forced delete: a deleted
                            // connector's secret can still be recovered from
                            // the console for a week (research R4).
                            let! _ =
                                client.DeleteSecretAsync(
                                    DeleteSecretRequest(SecretId = reference.Locator, RecoveryWindowInDays = 7L),
                                    ct
                                )

                            return Ok()
                        with
                        // Gone already, or already scheduled: either way the
                        // outcome asked for, so a retried delete never fails.
                        | :? ResourceNotFoundException -> return Ok()
                        | :? InvalidRequestException as e when mentionsDeletion e -> return Ok()
                    })

        member _.Probe ct =
            match clientFor options.Region with
            | Error failure -> Task.FromResult(Error failure)
            | Ok client ->
                guard ct (fun () ->
                    task {
                        try
                            // Reading a name that is not there is the cheapest
                            // call that proves the credentials, the region and
                            // the permission at once: a missing secret is the
                            // expected answer and counts as reachable.
                            let! _ =
                                client.GetSecretValueAsync(GetSecretValueRequest(SecretId = $"{options.Prefix}-probe"), ct)

                            return Ok()
                        with
                        | :? ResourceNotFoundException -> return Ok()
                        | :? InvalidRequestException as e when mentionsDeletion e -> return Ok()
                    })

let create (options: AwsStoreOptions) : ISecretStore = AwsSecretStore options :> ISecretStore
