/// The `azure` store, over Azure Key Vault (specs/005 research R4). Named
/// `AzureStore`, not `Azure`: a module of that name in this namespace would
/// shadow the vendor's `Azure.*` namespaces for the files after it.
///
/// Key Vault is the one manager here where the vault is part of the secret's
/// address: a vault URI is configured only for where NEW secrets go, and every
/// reference carries its own (`https://{vault}/secrets/{name}`). Reading a
/// reference therefore builds a client for the vault the locator names, which
/// is what keeps a connector working after the operator points
/// `UMBRAL__AZURE_KEY_VAULT_URI` at another vault (FR-011), and keeps a changed
/// setting from silently pointing it at a different secret.
module Umbral.Infrastructure.SecretStores.AzureStore

open System
open System.Collections.Concurrent
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Azure
open Azure.Core
open Azure.Core.Pipeline
open Azure.Identity
open Azure.Security.KeyVault.Secrets
open Umbral.Infrastructure.Secrets

[<Literal>]
let StoreId = "azure"

type AzureStoreOptions =
    {
        /// `UMBRAL__AZURE_KEY_VAULT_URI`: where `Put` writes. Reads and deletes
        /// go to the vault in the reference instead.
        VaultUri: Uri
        /// The first part of every name; the probe reads `{prefix}-probe`.
        Prefix: string
        /// `None` means `defaultCredential ()`. A test passes a static one.
        Credential: TokenCredential option
        /// Tests only: the fake the SDK's pipeline sends through.
        Transport: HttpMessageHandler option
    }

/// The production credential: an environment service principal, then workload
/// identity (AKS, GitHub), then managed identity.
///
/// Not `DefaultAzureCredential`, which would also try Visual Studio, the CLI,
/// PowerShell and a browser: in a container that means child processes and slow
/// timeouts before the first useful error (research R4). The chain is built
/// here and not in the SDK's default so it stays the same three things.
///
/// `AZURE_CLIENT_ID` on its own names a USER-assigned managed identity; with
/// `AZURE_CLIENT_SECRET` beside it, it names a service principal that
/// `EnvironmentCredential` already uses, and the managed identity stays
/// system-assigned (the variable is not for it then).
let defaultCredential () : TokenCredential =
    let clientId = Environment.GetEnvironmentVariable "AZURE_CLIENT_ID"
    let clientSecret = Environment.GetEnvironmentVariable "AZURE_CLIENT_SECRET"

    let identity =
        if String.IsNullOrEmpty clientId || not (String.IsNullOrEmpty clientSecret) then
            ManagedIdentityId.SystemAssigned
        else
            ManagedIdentityId.FromUserAssignedClientId clientId

    ChainedTokenCredential(EnvironmentCredential(), WorkloadIdentityCredential(), ManagedIdentityCredential(identity))
    :> TokenCredential

/// What went wrong, as a `StoreFailure`, from an exception of the SDK or of the
/// network under it. Never puts the exception's message anywhere: the SDK's
/// messages echo the request URI (so the secret's name) and the service's own
/// text, and `Rejected` reasons are fixed codes by contract.
let rec private toFailure (ex: exn) : StoreFailure =
    match ex with
    // What the SDK's retry policy throws when every attempt failed: the
    // attempts' exceptions in one. The last attempt is the state of the vault
    // now, so it decides.
    | :? AggregateException as e when e.InnerExceptions.Count > 0 -> toFailure e.InnerExceptions[e.InnerExceptions.Count - 1]
    // Before RequestFailedException on purpose: some credential failures reach
    // us wrapped as one, and the credential is the thing the operator fixes.
    | :? AuthenticationFailedException -> AccessDenied // includes CredentialUnavailableException
    | :? RequestFailedException as e ->
        match e.Status with
        | 404 -> NotFound
        | 401
        | 403 -> AccessDenied
        | 409 when e.ErrorCode = "ObjectIsDeletedButRecoverable" ->
            // A name in the soft-deleted state cannot be written; the derived
            // names make this unreachable for Umbral's own secrets, so seeing it
            // means somebody else's deletion is in the way.
            Rejected "soft_deleted"
        | 409 -> Rejected "conflict"
        // Status 0 is "no response at all": the SDK wraps a socket or DNS
        // failure and its own network timeout as this.
        | 0
        | 429 -> Unreachable
        | status when status >= 500 -> Unreachable
        | status -> Rejected $"http_{status}"
    | :? HttpRequestException
    | :? System.IO.IOException
    | :? TimeoutException -> Unreachable
    // Cancelled by something other than the caller's token (the SDK's own
    // timeout): to a caller, a store that does not answer cannot be reached.
    | :? OperationCanceledException -> Unreachable
    | other -> Rejected(other.GetType().Name)

/// Runs one vendor call, with every failure turned into a `StoreFailure`.
///
/// The caller's own cancellation is the exception: it is rethrown so the
/// registry's bound can tell it from a timeout (see `Bounded` in Registry.fs).
let private guard (cancellationToken: CancellationToken) (call: unit -> Task<Result<'a, StoreFailure>>) =
    task {
        try
            return! call ()
        with ex ->
            cancellationToken.ThrowIfCancellationRequested()
            return Error(toFailure ex)
    }

type AzureSecretStore(options: AzureStoreOptions) =

    let credential =
        lazy (options.Credential |> Option.defaultWith defaultCredential)

    let clientOptions () =
        let o = SecretClientOptions()
        // The registry's 10 s bound is what ends a call; two retries keep the
        // SDK from spending that bound on a retry storm (research R3).
        o.Retry.MaxRetries <- 2

        options.Transport
        |> Option.iter (fun handler -> o.Transport <- new HttpClientTransport(handler))

        o

    // One client per vault, built on first use and kept: a client owns an
    // HTTP pipeline and a token cache, so building one per call would fetch a
    // token per call.
    let clients = ConcurrentDictionary<string, Lazy<SecretClient>>()

    let clientFor (vault: Uri) =
        clients
            .GetOrAdd(
                vault.GetLeftPart UriPartial.Authority,
                fun key -> lazy (SecretClient(Uri key, credential.Value, clientOptions ()))
            )
            .Value

    /// The vault and the name a reference stands for. `None` for a locator that
    /// is not a Key Vault secret id, which only a hand-edited row can produce.
    let parse (reference: SecretRef) =
        if reference.Store <> StoreId then
            None
        else
            // `TryCreate` throws for a relative Uri, so only an absolute one
            // reaches it.
            match Uri.TryCreate(reference.Locator, UriKind.Absolute) with
            | true, uri ->
                match KeyVaultSecretIdentifier.TryCreate uri with
                | true, id -> Some(id.VaultUri, id.Name)
                | _ -> None
            | _ -> None

    /// Runs `call` against the vault and name of a reference.
    let onReference reference cancellationToken (call: SecretClient -> string -> Task<Result<'a, StoreFailure>>) =
        match parse reference with
        | Some(vault, name) -> guard cancellationToken (fun () -> call (clientFor vault) name)
        | None ->
            // The row says `azure` but holds no vault and name. Retrying cannot
            // help and neither can the credentials, so it is not those.
            Task.FromResult(Error(Rejected "malformed_locator"))

    interface ISecretStore with
        member _.Id = StoreId

        member _.Put(name, plaintext, cancellationToken) =
            guard cancellationToken (fun () ->
                task {
                    // `SetSecret` creates the secret or adds a version to it, so
                    // a repeated call lands on the same name.
                    let! response = (clientFor options.VaultUri).SetSecretAsync(name.Cloud, plaintext, cancellationToken)
                    let id = response.Value.Id
                    let versionless = KeyVaultSecretIdentifier id

                    // The reference omits the version: it must keep reading the
                    // newest one after a replace adds another (FR-008).
                    return
                        Ok
                            { Store = StoreId
                              Locator = $"{versionless.VaultUri.GetLeftPart UriPartial.Authority}/secrets/{versionless.Name}" }
                })

        member _.Get(reference, cancellationToken) =
            onReference reference cancellationToken (fun client name ->
                task {
                    let! response = client.GetSecretAsync(name, cancellationToken = cancellationToken)
                    return Ok response.Value.Value
                })

        member _.Delete(reference, cancellationToken) =
            onReference reference cancellationToken (fun client name ->
                task {
                    try
                        // Started, not awaited: the secret is soft-deleted the
                        // moment the request is accepted, and waiting for the
                        // vault to finish would spend the bound on nothing
                        // Umbral needs. Purge is never called; recovery is the
                        // operator's safety net (FR-010).
                        let! _ = client.StartDeleteSecretAsync(name, cancellationToken)
                        return Ok()
                    with :? RequestFailedException as e when e.Status = 404 ->
                        return Ok()
                })

        member _.Probe cancellationToken =
            guard cancellationToken (fun () ->
                task {
                    try
                        let! _ =
                            (clientFor options.VaultUri)
                                .GetSecretAsync($"{options.Prefix}-probe", cancellationToken = cancellationToken)

                        return Ok()
                    with :? RequestFailedException as e when e.Status = 404 ->
                        // The probe secret does not exist and need not: being
                        // told so proves the vault answered and let us in.
                        return Ok()
                })

let create (options: AzureStoreOptions) : ISecretStore = AzureSecretStore options :> ISecretStore
