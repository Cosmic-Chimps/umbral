/// Which store a secret is written to, and which one a given reference is read
/// from (specs/005-external-secret-stores research R2).
///
/// A single `ISecretStore` singleton cannot do that job. A connector whose
/// secret stayed in AWS after the instance switched to the local store must
/// still sign in, and the move command needs the source and the target store at
/// the same time. So callers ask this registry: `Active` for where new secrets
/// go, `For` for the store a reference names.
///
/// External stores are built lazily, the first time they are asked for, so a
/// local-only instance never loads a vendor assembly (SC-004, SC-006). The
/// registry holds them as `Lazy` factories and knows nothing about any vendor,
/// which is also what lets a test register an in-memory store under any id.
module Umbral.Infrastructure.SecretStores.Registry

open System
open System.Threading
open System.Threading.Tasks
open Umbral.Infrastructure.Secrets

/// The longest any one call to a store may take. A hung manager must cost a
/// sign-in this much and no more (FR-012), and the vendor clients are
/// configured to retry at most twice so that the bound is reached by this
/// token rather than by a retry storm.
let callBound = TimeSpan.FromSeconds 10.

/// Runs every call of `inner` under the caller's token linked to a time bound.
///
/// Doing it here, in the one wrapper the registry hands out, means no caller
/// has to remember it and no adapter has to implement it. If it is the bound
/// that fires, the answer is `Unreachable`: from outside, a store that does not
/// answer in time is a store that cannot be reached. If it is the caller's own
/// token, the cancellation is theirs and is not turned into a failure.
type private Bounded(inner: ISecretStore, bound: TimeSpan) =

    let run (caller: CancellationToken) (call: CancellationToken -> Task<Result<'a, StoreFailure>>) =
        task {
            use linked = CancellationTokenSource.CreateLinkedTokenSource caller
            linked.CancelAfter bound

            try
                return! call linked.Token
            with :? OperationCanceledException when not caller.IsCancellationRequested ->
                return Error Unreachable
        }

    interface ISecretStore with
        member _.Id = inner.Id
        member _.Put(name, plaintext, ct) = run ct (fun t -> inner.Put(name, plaintext, t))
        member _.Get(reference, ct) = run ct (fun t -> inner.Get(reference, t))
        member _.Delete(reference, ct) = run ct (fun t -> inner.Delete(reference, t))
        member _.Probe ct = run ct inner.Probe

/// What `Active` is when the configured store cannot be built: every call says
/// so, instead of the process failing to start or a handler throwing.
type private Unavailable(id: string, failure: StoreFailure) =
    interface ISecretStore with
        member _.Id = id
        member _.Put(_, _, _) = Task.FromResult(Error failure)
        member _.Get(_, _) = Task.FromResult(Error failure)
        member _.Delete(_, _) = Task.FromResult(Error failure)
        member _.Probe _ = Task.FromResult(Error failure)

/// The registry itself.
///
/// `local` is always present. `others` maps a store id to the way to build that
/// store, run at most once and only when first needed; a factory that cannot
/// build its store (credentials gone, a setting missing) answers `Error` and
/// the registry reports `NotConfigured` for it, not an exception.
///
/// Registered with `TryAddSingleton` (as `TimeProvider` is), so a test puts in
/// its own before `configureServices` runs.
type SecretStores
    (
        local: ISecretStore,
        activeId: string,
        others: Map<string, Lazy<Result<ISecretStore, StoreFailure>>>,
        bound: TimeSpan
    ) =

    // Wrapped once, so every route to a store goes through the time bound.
    let boundedLocal = Bounded(local, bound) :> ISecretStore

    let bounded =
        others
        |> Map.map (fun _ built ->
            lazy
                (match built.Value with
                | Ok store -> Ok(Bounded(store, bound) :> ISecretStore)
                | Error failure -> Error failure))

    new(local: ISecretStore, activeId: string, others: Map<string, Lazy<Result<ISecretStore, StoreFailure>>>) =
        SecretStores(local, activeId, others, callBound)

    /// The store new and replaced secrets go to.
    member this.Active: ISecretStore =
        match this.For activeId with
        | Ok store -> store
        | Error failure -> Unavailable(activeId, failure) :> ISecretStore

    /// The store that reads, replaces and deletes a reference with this store
    /// id. `NotConfigured` when it cannot be built here.
    member _.For(storeId: string) : Result<ISecretStore, StoreFailure> =
        if storeId = LocalStoreId then
            Ok boundedLocal
        else
            match bounded.TryFind storeId with
            | Some store -> store.Value
            | None -> Error NotConfigured

    /// The plaintext a reference points at. Directly from its store for now;
    /// the cache with its grace period sits in front of this (specs/005 research R6).
    member this.Read(reference: SecretRef, cancellationToken: CancellationToken) : Task<Result<string, StoreFailure>> =
        match this.For reference.Store with
        | Ok store -> store.Get(reference, cancellationToken)
        | Error failure -> Task.FromResult(Error failure)

    /// Forgets anything held for this reference. Nothing is held yet.
    member _.Evict(_reference: SecretRef) : unit = ()

    /// Whether this instance could read a reference of this store, decided
    /// without a network call: the Connectors screen asks it for every card.
    member _.CanRead(storeId: string) : bool =
        storeId = LocalStoreId || others.ContainsKey storeId
