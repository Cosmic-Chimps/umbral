/// The `bellabaxter` store (specs/005 research R4, R7). Named `BellaBaxterStore` for the same reason as `AzureStore`: the vendor's namespace is `BellaBaxter.*`.
///
/// Umbral talks to BellaBaxter through `BellaBaxter.Client`, the Kiota client
/// the vendor's own tools use, and not through the configuration package: that
/// one cannot write and starts polling the moment it is added (research R4).
///
/// What the client does that this file leans on, all read from the decompiled
/// package (0.1.1-preview.131) because its README no longer matches the code:
///
///   - the factory builds ONE `HttpClient` through a service collection, with
///     `outerHandler` first in the chain (outside the signing, the E2E
///     envelope handler and the resilience handler), so a handler a test hands
///     in answers before any retry or backoff could start;
///   - the resilience handler retries 3 times with backoff and jitter, and POST
///     is retried too. The registry's 10 second bound is what caps that here,
///     which is why `Put` has to be safe to repeat (a retried POST can come back
///     409 after the first attempt had in fact created the key);
///   - the HMAC handler is constructed when the client is, so a malformed API
///     key throws from the factory, not from the first request.
module Umbral.Infrastructure.SecretStores.BellaBaxterStore

open System
open System.Net.Http
open System.Security.Cryptography
open System.Threading
open System.Threading.Tasks
open BellaBaxter.Client
open BellaBaxter.Client.Models
open Microsoft.Kiota.Abstractions
open Microsoft.Kiota.Abstractions.Serialization
open Polly.CircuitBreaker
open Polly.Timeout
open Umbral.Infrastructure.Secrets

[<Literal>]
let StoreId = "bellabaxter"

/// How Umbral authenticates. An API key (`bax-{id}-{hex}`) is signed per request
/// with HMAC and knows its own project and environment; an access token is a
/// bearer token and does not.
type BellaAuth =
    | ApiKey of Protected
    | AccessToken of Protected

type BellaStoreOptions =
    { BaseUrl: string
      Auth: BellaAuth
      /// `None` with an API key means "ask the server which project the key is for".
      Project: string option
      Environment: string option
      /// The provider's slug. `None` means the environment's single secrets provider.
      Provider: string option
      /// The device key of a zero-knowledge environment, as PEM.
      PrivateKeyPem: Protected option
      /// For the probe key.
      Prefix: string
      /// Tests only: answers every request before the client's own handlers.
      OuterHandler: HttpMessageHandler option }

/// The client's `outerHandler` has to be a `DelegatingHandler`, and a test
/// wants to hand in a plain `HttpMessageHandler`. This one forwards to it and
/// never to the handler after it, which is what makes it the end of the chain.
type private Terminal(handler: HttpMessageHandler) =
    inherit DelegatingHandler()

    let invoker = new HttpMessageInvoker(handler, false)

    override _.SendAsync(request, cancellationToken) =
        invoker.SendAsync(request, cancellationToken)

/// Where the secrets of this instance live in BellaBaxter.
type private Target =
    { Project: string
      Environment: string
      Provider: string }

/// The pieces of a locator, `{project}/{env}/{provider}/{key}`. Slugs and keys
/// never contain `/`, so a plain split is exact.
let private locate (target: Target) (key: string) =
    $"{target.Project}/{target.Environment}/{target.Provider}/{key}"

let private parseLocator (locator: string) =
    match locator.Split '/' with
    | [| project; environment; provider; key |] when
        project <> "" && environment <> "" && provider <> "" && key <> ""
        ->
        Some(
            { Project = project
              Environment = environment
              Provider = provider },
            key
        )
    | _ -> None

/// What this file could not do with the answer the vendor gave. Closed set of
/// short codes: a `Rejected` reason is never a vendor message.
let private shapeError = Rejected "unexpected_response"

/// The HTTP status a failure carries, if it carries one. Kiota puts it on
/// `ApiException` for a mapped error (`ProblemDetails`, a subclass) and for an
/// unmapped one alike.
let private statusOf (ex: exn) =
    match ex with
    | :? ApiException as api ->
        match api.ResponseStatusCode with
        | 0 ->
            match ex with
            | :? ProblemDetails as problem when problem.Status.HasValue -> Some problem.Status.Value
            | _ -> None
        | code -> Some code
    | _ -> None

/// A vendor or network failure as a `StoreFailure` (research R4's table).
let private failureOf (ex: exn) : StoreFailure =
    match statusOf ex with
    | Some 404 -> NotFound
    | Some(401 | 403) -> AccessDenied
    | Some(408 | 429) -> Unreachable
    | Some code when code >= 500 -> Unreachable
    | Some code -> Rejected $"http_{code}"
    | None ->
        match ex with
        | :? E2EEResponseException -> Rejected "e2ee"
        | :? HttpRequestException
        | :? TimeoutRejectedException
        | :? BrokenCircuitException
        | :? TimeoutException
        // A cancellation that is not the caller's: the client's own timeout.
        // The caller's is let through before this is reached.
        | :? OperationCanceledException -> Unreachable
        | _ -> Rejected(ex.GetType().Name)

/// Runs a call and hands back what it threw, except the caller's own
/// cancellation, which stays theirs (the registry turns its bound into
/// `Unreachable`; see Registry.fs).
let private attempt (cancellationToken: CancellationToken) (call: unit -> Task<'a>) : Task<Result<'a, exn>> =
    task {
        try
            let! value = call ()
            return Ok value
        with ex when not (ex :? OperationCanceledException && cancellationToken.IsCancellationRequested) ->
            return Error ex
    }

/// The one place that reads the secret out of a single-secret GET.
///
/// UNVERIFIED against a live server: the decompiled client types the response
/// as an E2E envelope, but once the transport handler has decrypted it the
/// body is `{"secrets": {"<key>": "<value>"}}`, which Kiota parses into
/// `AdditionalData["secrets"]` as an `UntypedObject` of `UntypedString`s (research
/// R7). specs/005-external-secret-stores/quickstart.md step 6 checks it against
/// a real BellaBaxter. If the shape differs, this is the only function to fix.
let private readSecret (key: string) (payload: E2EEncryptedPayload | null) : Result<string, StoreFailure> =
    match payload with
    | null -> Error shapeError
    | payload ->
        match payload.AdditionalData.TryGetValue "secrets" with
        | true, (:? UntypedObject as secrets) ->
            match secrets.GetValue().TryGetValue key with
            | true, (:? UntypedString as value) ->
                match value.GetValue() with
                | null -> Error shapeError
                | text -> Ok text
            // The server answered but the key is not in the answer: gone.
            | false, _ -> Error NotFound
            | _ -> Error shapeError
        | _ -> Error shapeError

/// Builds the client, or says why it cannot be.
let private buildClient (options: BellaStoreOptions) : Result<BellaClient, StoreFailure> =
    let outer: DelegatingHandler | null =
        match options.OuterHandler with
        | Some handler -> new Terminal(handler)
        | None -> null

    let loadKey (pem: Protected) =
        let ecdh = ECDiffieHellman.Create()

        try
            ecdh.ImportFromPem pem.Reveal
            Ok(new ZkeDekHandler(ecdh))
        with :? CryptographicException | :? ArgumentException ->
            ecdh.Dispose()
            // Neither the PEM nor the exception text goes into the reason.
            Error(Rejected "private_key_unreadable")

    let create (zke: ZkeDekHandler option) =
        try
            let client =
                match options.Auth, zke with
                | ApiKey key, None ->
                    BellaClientFactory.CreateWithHmacApiKey(options.BaseUrl, key.Reveal, outer, appClient = "umbral")
                | ApiKey key, Some handler ->
                    BellaClientFactory.CreateWithHmacApiKeyAndZke(
                        options.BaseUrl,
                        key.Reveal,
                        handler,
                        outer,
                        appClient = "umbral"
                    )
                | AccessToken token, None ->
                    BellaClientFactory.CreateWithBearerToken(
                        options.BaseUrl,
                        token.Reveal,
                        outer,
                        appClient = "umbral"
                    )
                | AccessToken token, Some handler ->
                    BellaClientFactory.CreateWithBearerTokenAndZke(
                        options.BaseUrl,
                        token.Reveal,
                        handler,
                        outer,
                        appClient = "umbral"
                    )

            Ok client
        // The HMAC handler checks the key's shape (`bax-{id}-{hex}`) when the
        // client is built and throws `ArgumentException` (or `FormatException`
        // for a non-hex secret). A key the server would never accept is a
        // credential that does not work, which is what `AccessDenied` says.
        with :? ArgumentException | :? FormatException ->
            Error AccessDenied

    match options.PrivateKeyPem with
    | None -> create None
    | Some pem ->
        match loadKey pem with
        | Ok handler -> create (Some handler)
        | Error failure -> Error failure

type BellaBaxterSecretStore(options: BellaStoreOptions) =

    // Built once, on first use, so a local-only instance never loads the vendor
    // assembly and a construction failure is an answer rather than an exception.
    // `Lazy` is thread-safe, and holding the `Result` means a failure is stored
    // as a value, not rethrown on every call.
    let client = lazy (buildClient options)

    // Project and environment come from the options or, for an API key that
    // names neither, from the server, once. The provider is resolved once as
    // well. Only a success is kept: an outage while resolving must not be
    // remembered, so the next call tries again.
    let resolveGate = new SemaphoreSlim(1, 1)
    let mutable resolved: Target option = None

    let probeKey = $"UMBRAL_{options.Prefix}_PROBE".ToUpperInvariant().Replace('-', '_')

    let secretAt (bella: BellaClient) (target: Target) (key: string) =
        bella.Api.V1.Projects[target.Project].Environments[target.Environment].Providers[target.Provider].Secrets[key]

    let withClient (f: BellaClient -> Task<Result<'a, StoreFailure>>) : Task<Result<'a, StoreFailure>> =
        match client.Value with
        | Ok bella -> f bella
        | Error failure -> Task.FromResult(Error failure)

    let resolveProjectAndEnvironment (bella: BellaClient) (cancellationToken: CancellationToken) =
        task {
            match options.Project, options.Environment with
            | Some project, Some environment -> return Ok(project, environment)
            | given ->
                match options.Auth with
                | AccessToken _ ->
                    // A bearer token does not say which project it is for; the
                    // settings require both, so this is a setting that slipped by.
                    return Error(Rejected "project_or_environment_missing")
                | ApiKey _ ->
                    let! me = attempt cancellationToken (fun () -> bella.Api.V1.Keys.Me.GetAsync(cancellationToken = cancellationToken))

                    match me with
                    | Error ex -> return Error(failureOf ex)
                    | Ok answer ->
                      match Option.ofObj answer with
                      | None -> return Error shapeError
                      | Some context ->
                        let pick (configured: string option) (fromServer: string | null) =
                            configured |> Option.orElse (Option.ofObj fromServer)

                        match pick (fst given) context.ProjectSlug, pick (snd given) context.EnvironmentSlug with
                        | Some project, Some environment -> return Ok(project, environment)
                        | _ -> return Error(Rejected "key_context_incomplete")
        }

    let resolveProvider (bella: BellaClient) (project: string) (environment: string) (cancellationToken: CancellationToken) =
        task {
            match options.Provider with
            | Some provider -> return Ok provider
            | None ->
                let! listed =
                    attempt cancellationToken (fun () ->
                        bella.Api.V1.Projects[project].Environments[environment].Providers.GetAsync(cancellationToken = cancellationToken))

                match listed with
                | Error ex -> return Error(failureOf ex)
                | Ok answer ->
                  match Option.ofObj answer with
                  | None -> return Error shapeError
                  | Some providers ->
                    let secretProviders =
                        providers
                        |> Seq.filter (fun p -> String.Equals(p.ProviderMetaType, "Secrets", StringComparison.OrdinalIgnoreCase))
                        |> Seq.choose (fun p -> Option.ofObj p.ProviderSlug)
                        |> List.ofSeq

                    match secretProviders with
                    | [ provider ] -> return Ok provider
                    // Zero or several: Umbral will not guess where connector
                    // secrets go. The settings notice for this reason names
                    // UMBRAL__BELLABAXTER_PROVIDER, which is how the operator
                    // chooses (Texts.fs, with the bellabaxter store's other
                    // notices).
                    | _ -> return Error(Rejected "provider_ambiguous")
        }

    let resolve (bella: BellaClient) (cancellationToken: CancellationToken) : Task<Result<Target, StoreFailure>> =
        task {
            match resolved with
            | Some target -> return Ok target
            | None ->
                do! resolveGate.WaitAsync cancellationToken

                try
                    match resolved with
                    | Some target -> return Ok target
                    | None ->
                        match! resolveProjectAndEnvironment bella cancellationToken with
                        | Error failure -> return Error failure
                        | Ok(project, environment) ->
                            match! resolveProvider bella project environment cancellationToken with
                            | Error failure -> return Error failure
                            | Ok provider ->
                                let target =
                                    { Project = project
                                      Environment = environment
                                      Provider = provider }

                                resolved <- Some target
                                return Ok target
                finally
                    resolveGate.Release() |> ignore
        }

    let put (bella: BellaClient) (name: SecretName) (plaintext: string) (cancellationToken: CancellationToken) =
        task {
            match! resolve bella cancellationToken with
            | Error failure -> return Error failure
            | Ok target ->
                let key = name.Bella
                let reference = { Store = StoreId; Locator = locate target key }
                let item = secretAt bella target key
                let update () = attempt cancellationToken (fun () -> item.PutAsync(UpdateSecretRequest(Value = plaintext), cancellationToken = cancellationToken))

                let create () =
                    attempt cancellationToken (fun () ->
                        bella.Api.V1.Projects[target.Project].Environments[target.Environment].Providers[target.Provider].Secrets
                            .PostAsync(CreateSecretRequest(Key = key, Value = plaintext), cancellationToken = cancellationToken))

                // Update first, create on 404, and update again on a 409 from
                // the create: the client's resilience handler retries a POST,
                // so a create that timed out may have succeeded the first time.
                match! update () with
                | Ok _ -> return Ok reference
                | Error ex when statusOf ex = Some 404 ->
                    match! create () with
                    | Ok _ -> return Ok reference
                    | Error ex when statusOf ex = Some 409 ->
                        match! update () with
                        | Ok _ -> return Ok reference
                        | Error ex -> return Error(failureOf ex)
                    | Error ex -> return Error(failureOf ex)
                | Error ex -> return Error(failureOf ex)
        }

    interface ISecretStore with
        member _.Id = StoreId

        member _.Put(name, plaintext, cancellationToken) =
            withClient (fun bella -> put bella name plaintext cancellationToken)

        member _.Get(reference, cancellationToken) =
            if reference.Store <> StoreId then
                Task.FromResult(Error NotConfigured)
            else
                match parseLocator reference.Locator with
                | None -> Task.FromResult(Error(Rejected "locator_malformed"))
                | Some(target, key) ->
                    withClient (fun bella ->
                        task {
                            // The locator carries its own project, environment
                            // and provider, so a reference keeps working after
                            // the settings move on (research R4).
                            let! answer = attempt cancellationToken (fun () -> (secretAt bella target key).GetAsync(cancellationToken = cancellationToken))

                            match answer with
                            | Error ex -> return Error(failureOf ex)
                            | Ok payload -> return readSecret key payload
                        })

        member _.Delete(reference, cancellationToken) =
            if reference.Store <> StoreId then
                Task.FromResult(Error NotConfigured)
            else
                match parseLocator reference.Locator with
                | None -> Task.FromResult(Error(Rejected "locator_malformed"))
                | Some(target, key) ->
                    withClient (fun bella ->
                        task {
                            let! answer = attempt cancellationToken (fun () -> (secretAt bella target key).DeleteAsync(cancellationToken = cancellationToken))

                            match answer with
                            | Ok _ -> return Ok()
                            // Already gone is what the caller wanted.
                            | Error ex when statusOf ex = Some 404 -> return Ok()
                            | Error ex -> return Error(failureOf ex)
                        })

        member _.Probe cancellationToken =
            withClient (fun bella ->
                task {
                    match! resolve bella cancellationToken with
                    | Error failure -> return Error failure
                    | Ok target ->
                        let! answer = attempt cancellationToken (fun () -> (secretAt bella target probeKey).GetAsync(cancellationToken = cancellationToken))

                        match answer with
                        | Ok _ -> return Ok()
                        // The probe key is not expected to exist: a 404 proves
                        // the credentials, the project and the provider all
                        // work, which is all a probe asks.
                        | Error ex when statusOf ex = Some 404 -> return Ok()
                        | Error ex -> return Error(failureOf ex)
                })

let create (options: BellaStoreOptions) : ISecretStore = BellaBaxterSecretStore options
