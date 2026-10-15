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

/// How much longer than the bound a call that ignores its token is waited for.
let private backstop = TimeSpan.FromSeconds 1.

/// Runs every call of `inner` under the caller's token linked to a time bound.
///
/// Doing it here, in the one wrapper the registry hands out, means no caller
/// has to remember it and no adapter has to implement it. If it is the bound
/// that fires, the answer is `Unreachable`: from outside, a store that does not
/// answer in time is a store that cannot be reached. If it is the caller's own
/// token, the cancellation is theirs and is not turned into a failure.
///
/// The token is a request, and an SDK may ignore it, so the call is also
/// waited for no longer than the bound plus a second. A call that outlives that
/// is abandoned: it ends as `Unreachable`, and what the abandoned task later
/// does (it may still fault) is observed so that it cannot surface as an
/// unobserved task exception.
type private Bounded(inner: ISecretStore, bound: TimeSpan) =

    let run (caller: CancellationToken) (call: CancellationToken -> Task<Result<'a, StoreFailure>>) =
        task {
            use linked = CancellationTokenSource.CreateLinkedTokenSource caller
            linked.CancelAfter bound
            let mutable started = Unchecked.defaultof<Task<Result<'a, StoreFailure>>>

            try
                started <- call linked.Token
                return! started.WaitAsync(bound + backstop, caller)
            with
            | :? OperationCanceledException when not caller.IsCancellationRequested -> return Error Unreachable
            | :? TimeoutException ->
                started.ContinueWith(
                    (fun (abandoned: Task<Result<'a, StoreFailure>>) -> abandoned.Exception |> ignore),
                    TaskContinuationOptions.OnlyOnFaulted
                )
                |> ignore

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

/// How long beyond the call bound the mark of a refresh in flight is believed.
let private refreshMargin = TimeSpan.FromSeconds 5.

/// What the cache holds for one reference: the value protected, when it was
/// fetched, and when a refresh of it last failed. In process memory only; it is
/// never serialised, so a restart starts empty (FR-013).
///
/// `RefreshingSince` is set, atomically, by the one reader that is refreshing an
/// entry that has expired: the others serve the value they hold and do not call
/// the store, so a store that is slow to answer is asked once and not once per
/// sign-in for the whole time it takes (research R6).
type private Entry =
    { Value: Protected
      FetchedAt: DateTimeOffset
      LastFailedRefreshAt: DateTimeOffset option
      RefreshingSince: DateTimeOffset option }

/// What the Connectors screen shows about the active store beyond its cards.
/// `Healthy` also covers "never checked" and a local store, which have nothing
/// to say.
type SecretStoreState =
    | Healthy
    | Failed of StoreFailure * at: DateTimeOffset

/// Whether the active store was last seen failing (specs/005 data-model,
/// Story 4). The startup probe sets it, and so does a failed `Put` on the active
/// store; a successful `Put` clears it, so the notice goes away once the
/// operator has fixed the cause and saved something, without a restart. In process memory only: a restart probes
/// again.
///
/// Owned by the registry (`SecretStores.Status`) rather than registered next to
/// it, because the clearing happens inside the registry's `Active` and a
/// registry a test builds must carry its own.
type SecretStoreStatus() =
    // A reference to an immutable value, so a reader never sees half a state.
    [<VolatileField>]
    let mutable state = Healthy

    member _.Current = state
    member _.Fail(failure: StoreFailure, at: DateTimeOffset) = state <- Failed(failure, at)
    member _.Clear() = state <- Healthy

/// The active store as callers get it: a write is the evidence the notice is
/// about. A successful one proves the store works and the credentials may write,
/// so it clears the notice; one that failed because the store is unreachable,
/// refuses the credentials, is not set up or rejected the request raises it, so
/// an operator who sees a save refused also sees the notice on the screen. A
/// `NotFound` is not about the store being down, so it leaves the notice alone.
type private TracksStatusOnPut(inner: ISecretStore, status: SecretStoreStatus, clock: TimeProvider) =
    interface ISecretStore with
        member _.Id = inner.Id

        member _.Put(name, plaintext, ct) =
            task {
                let! result = inner.Put(name, plaintext, ct)

                match result with
                | Ok _ -> status.Clear()
                | Error NotFound -> ()
                | Error failure -> status.Fail(failure, clock.GetUtcNow())

                return result
            }

        member _.Get(reference, ct) = inner.Get(reference, ct)
        member _.Delete(reference, ct) = inner.Delete(reference, ct)
        member _.Probe ct = inner.Probe ct

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

    // How many times anything has been evicted, over the registry's life. A read
    // notes it before calling the store and writes its answer back only if it has
    // not moved, and checks again after writing: otherwise a sign-in that was
    // reading the old value while the operator replaced the secret would put
    // that value back for a whole cache lifetime, and an operator replaces a
    // secret precisely because the old one was revoked (FR-013: replacing
    // discards the cached value at once). One counter for all references is
    // coarser than one per reference, and the cost is an occasional cache miss
    // for a read that overlapped an unrelated eviction; what it buys is that
    // there is nothing per key to look up or to grow.
    let mutable generation = 0L

    let status = SecretStoreStatus()

    /// No cache: for a registry built by a test or a tool that reads each
    /// reference once and wants the store's answer every time.
    new(local: ISecretStore, activeId: string, others: Map<string, Lazy<Result<ISecretStore, StoreFailure>>>, bound: TimeSpan) =
        SecretStores(local, activeId, others, bound, TimeProvider.System, TimeSpan.Zero, TimeSpan.Zero)

    new(local: ISecretStore, activeId: string, others: Map<string, Lazy<Result<ISecretStore, StoreFailure>>>) =
        SecretStores(local, activeId, others, callBound)

    /// The store new and replaced secrets go to.
    member this.Active: ISecretStore =
        let store =
            match this.For activeId with
            | Ok store -> store
            | Error failure -> Unavailable(activeId, failure) :> ISecretStore

        // The local store has no outage to report, so nothing to clear.
        if activeId = LocalStoreId then store else TracksStatusOnPut(store, status, clock) :> ISecretStore

    /// What the startup probe found, for the Connectors screen.
    member _.Status = status

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

                let withinGrace (entry: Entry) (at: DateTimeOffset) =
                    at - entry.FetchedAt < cacheLifetime + gracePeriod

                let recentlyFailed (entry: Entry) (at: DateTimeOffset) =
                    match entry.LastFailedRefreshAt with
                    | Some failedAt -> at - failedAt < refreshSpacing
                    | None -> false

                // A refresh that has not ended after the bound plus a margin is
                // one whose reader is gone (cancelled, or the process was starved
                // for a while): its mark no longer holds anyone else back.
                let refreshing (entry: Entry) (at: DateTimeOffset) =
                    match entry.RefreshingSince with
                    | Some since -> at - since < bound + refreshMargin
                    | None -> false

                let mutable answer: Result<string, StoreFailure> option = None

                while answer.IsNone do
                    let now = clock.GetUtcNow()
                    let held = match cache.TryGetValue key with | true, entry -> Some entry | _ -> None

                    match held with
                    | Some entry when now - entry.FetchedAt < cacheLifetime -> answer <- Some(Ok entry.Value.Reveal)

                    // Expired, and either a refresh failed a moment ago or one is
                    // in flight right now: keep serving the value without asking
                    // again, and without another event.
                    | Some entry when withinGrace entry now && (recentlyFailed entry now || refreshing entry now) ->
                        answer <- Some(Ok entry.Value.Reveal)

                    | _ ->
                        // Becoming the reader that refreshes an entry still within
                        // its grace period is a compare-and-swap on the entry, so
                        // exactly one of several simultaneous readers wins; the
                        // rest go round again and find the mark. A miss, or an
                        // entry past its grace period, has no stale value to
                        // protect and nothing to claim.
                        let claimed =
                            match held with
                            | Some entry when withinGrace entry now ->
                                cache.TryUpdate(key, { entry with RefreshingSince = Some now }, entry)
                            | _ -> true

                        if claimed then
                            let before = Interlocked.Read &generation
                            let ended = ref false

                            let! fetched =
                                task {
                                    try
                                        let! result =
                                            match this.For reference.Store with
                                            | Ok store -> store.Get(reference, cancellationToken)
                                            | Error failure -> Task.FromResult(Error failure)

                                        ended.Value <- true
                                        return result
                                    finally
                                        // A call that threw (the caller's own
                                        // cancellation) must not leave the mark up
                                        // for the whole bound.
                                        if not ended.Value then
                                            match held, cache.TryGetValue key with
                                            | Some _, (true, current) when current.RefreshingSince.IsSome ->
                                                cache.TryUpdate(key, { current with RefreshingSince = None }, current) |> ignore
                                            | _ -> ()
                                }

                            // After the call, which may have taken up to the bound.
                            let at = clock.GetUtcNow()
                            let evictedMeanwhile () = Interlocked.Read &generation <> before

                            // The entry is written and then the count is looked at
                            // once more: an eviction that landed between the first
                            // look and the write found nothing to remove, so the
                            // write has to be undone here.
                            let put (entry: Entry) =
                                if not (evictedMeanwhile ()) then
                                    cache[key] <- entry

                                    if evictedMeanwhile () then
                                        cache.TryRemove key |> ignore

                            match fetched, held with
                            | Ok value, _ ->
                                put
                                    { Value = Protected.Of value
                                      FetchedAt = at
                                      LastFailedRefreshAt = None
                                      RefreshingSince = None }

                                answer <- Some(Ok value)

                            // An entry evicted while the store was being asked is a
                            // value the operator has replaced: it is not served, not
                            // even from the grace period.
                            | Error Unreachable, Some entry when withinGrace entry at && not (evictedMeanwhile ()) ->
                                put
                                    { entry with
                                        LastFailedRefreshAt = Some at
                                        RefreshingSince = None }

                                do! onStaleRefreshFailed Unreachable
                                answer <- Some(Ok entry.Value.Reveal)

                            | Error failure, _ ->
                                // A secret that is gone, a grant that is revoked, a
                                // store that is not set up, or the grace period over:
                                // waiting will not change the answer, so a stale value
                                // must not hide it (FR-014).
                                cache.TryRemove key |> ignore
                                answer <- Some(Error failure)

                return answer.Value
        }

    /// `Read` for a caller with no event to record.
    member this.Read(reference: SecretRef, cancellationToken: CancellationToken) : Task<Result<string, StoreFailure>> =
        this.Read(reference, (fun _ -> Task.FromResult()), cancellationToken)

    /// Forgets what is held for this reference, so the next read asks the
    /// store. Replace, delete and move call it (FR-013). The count moves before
    /// the removal, so that a read already in flight sees it move and does not
    /// put its older value back.
    member _.Evict(reference: SecretRef) : unit =
        Interlocked.Increment &generation |> ignore
        cache.TryRemove reference.Encoded |> ignore

    /// Whether this instance could read a reference of this store: whether the
    /// store can be built here. Building makes no network call, but it does load
    /// the vendor's assembly, which is acceptable because the Connectors screen
    /// asks only about a store some card names, and such a store is about to be
    /// used anyway. A store whose credentials are present but unusable (a Bella
    /// key that does not parse, a credential file that cannot be read) is not
    /// readable, which is what the card should say (FR-021).
    member this.CanRead(storeId: string) : bool =
        match this.For storeId with
        | Ok _ -> true
        | Error _ -> false
