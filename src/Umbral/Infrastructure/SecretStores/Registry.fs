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

/// How long after a failed refresh the cache leaves a stale entry alone. An
/// outage during a busy hour then costs one call and one event per entry per
/// half minute, not one per sign-in (specs/005 research R6).
let refreshSpacing = TimeSpan.FromSeconds 30.

/// What the cache holds for one reference: the value protected, when it was
/// fetched, and when a refresh of it last failed. In process memory only; it is
/// never serialised, so a restart starts empty (FR-013).
type private Entry =
    { Value: Protected
      FetchedAt: DateTimeOffset
      LastFailedRefreshAt: DateTimeOffset option }

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
/// `Read` keeps what an external store returned for `cacheLifetime`, and for a
/// further `gracePeriod` after that serves it anyway when the store cannot be
/// reached, so an outage at the manager does not stop every sign-in (FR-013,
/// FR-014). A lifetime of zero caches nothing; a grace period of zero never
/// serves a value past its lifetime.
///
/// Registered with `TryAddSingleton` (as `TimeProvider` is), so a test puts in
/// its own before `configureServices` runs.
type SecretStores
    (
        local: ISecretStore,
        activeId: string,
        others: Map<string, Lazy<Result<ISecretStore, StoreFailure>>>,
        bound: TimeSpan,
        clock: TimeProvider,
        cacheLifetime: TimeSpan,
        gracePeriod: TimeSpan
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

    // Keyed by the encoded reference. Concurrent misses for one key may both
    // call the manager: at the scale of one instance that is cheaper than a
    // single-flight lock that has to be got right (research R6).
    let cache = Collections.Concurrent.ConcurrentDictionary<string, Entry>()

    // How many times each reference has been evicted. A read notes it before
    // calling the store and writes its answer back only if it has not moved:
    // otherwise a sign-in that was reading the old value while the operator
    // replaced the secret would put that value back for a whole cache lifetime,
    // and an operator replaces a secret precisely because the old one was
    // revoked (FR-013: replacing discards the cached value at once).
    let evictions = Collections.Concurrent.ConcurrentDictionary<string, int64>()

    let evictionsOf (key: string) =
        match evictions.TryGetValue key with
        | true, count -> count
        | _ -> 0L

    /// No cache: for a registry built by a test or a tool that reads each
    /// reference once and wants the store's answer every time.
    new(local: ISecretStore, activeId: string, others: Map<string, Lazy<Result<ISecretStore, StoreFailure>>>, bound: TimeSpan) =
        SecretStores(local, activeId, others, bound, TimeProvider.System, TimeSpan.Zero, TimeSpan.Zero)

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

    /// The plaintext a reference points at, from the cache while it is fresh.
    ///
    /// `onStaleRefreshFailed` runs when the answer is a value served from the
    /// grace period because the refresh failed, with that failure. The caller
    /// supplies it because it alone knows the connector and the request the
    /// security event belongs to, and this module compiles before the events.
    /// It runs at most once per entry per `refreshSpacing`, which is also how
    /// often the store is asked again.
    member this.Read
        (reference: SecretRef, onStaleRefreshFailed: StoreFailure -> Task<unit>, cancellationToken: CancellationToken)
        : Task<Result<string, StoreFailure>> =
        task {
            // The local store decrypts in microseconds and has no outage to
            // ride out; a lifetime of zero is the operator turning the cache off.
            if reference.Store = LocalStoreId || cacheLifetime <= TimeSpan.Zero then
                match this.For reference.Store with
                | Ok store -> return! store.Get(reference, cancellationToken)
                | Error failure -> return Error failure
            else
                let key = reference.Encoded
                let now = clock.GetUtcNow()
                let held = match cache.TryGetValue key with | true, entry -> Some entry | _ -> None

                let withinGrace (entry: Entry) (at: DateTimeOffset) =
                    at - entry.FetchedAt < cacheLifetime + gracePeriod

                match held with
                | Some entry when now - entry.FetchedAt < cacheLifetime -> return Ok entry.Value.Reveal

                // Expired, and a refresh failed a moment ago: keep serving the
                // value without asking again, and without another event.
                | Some entry when
                    withinGrace entry now
                    && (match entry.LastFailedRefreshAt with
                        | Some failedAt -> now - failedAt < refreshSpacing
                        | None -> false)
                    ->
                    return Ok entry.Value.Reveal

                | _ ->
                    let evictedBefore = evictionsOf key

                    let! fetched =
                        match this.For reference.Store with
                        | Ok store -> store.Get(reference, cancellationToken)
                        | Error failure -> Task.FromResult(Error failure)

                    // After the call, which may have taken up to the bound.
                    let at = clock.GetUtcNow()
                    let evictedMeanwhile = evictionsOf key <> evictedBefore

                    match fetched, held with
                    | Ok value, _ ->
                        if not evictedMeanwhile then
                            cache[key] <-
                                { Value = Protected.Of value
                                  FetchedAt = at
                                  LastFailedRefreshAt = None }

                        return Ok value

                    // An entry evicted while the store was being asked is a
                    // value the operator has replaced: it is not served, not
                    // even from the grace period.
                    | Error Unreachable, Some entry when withinGrace entry at && not evictedMeanwhile ->
                        cache[key] <- { entry with LastFailedRefreshAt = Some at }
                        do! onStaleRefreshFailed Unreachable
                        return Ok entry.Value.Reveal

                    | Error failure, _ ->
                        // A secret that is gone, a grant that is revoked, a
                        // store that is not set up, or the grace period over:
                        // waiting will not change the answer, so a stale value
                        // must not hide it (FR-014).
                        cache.TryRemove key |> ignore
                        return Error failure
        }

    /// `Read` for a caller with no event to record.
    member this.Read(reference: SecretRef, cancellationToken: CancellationToken) : Task<Result<string, StoreFailure>> =
        this.Read(reference, (fun _ -> Task.FromResult()), cancellationToken)

    /// Forgets what is held for this reference, so the next read asks the
    /// store. Replace, delete and move call it (FR-013). Counted before the
    /// removal, so that a read already in flight sees the count move and does
    /// not put its older value back.
    member _.Evict(reference: SecretRef) : unit =
        let key = reference.Encoded
        evictions.AddOrUpdate(key, 1L, (fun _ count -> count + 1L)) |> ignore
        cache.TryRemove key |> ignore

    /// Whether this instance could read a reference of this store, decided
    /// without a network call: the Connectors screen asks it for every card.
    member _.CanRead(storeId: string) : bool =
        storeId = LocalStoreId || others.ContainsKey storeId
