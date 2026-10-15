/// The cache in front of the external stores and its grace period
/// (specs/005-external-secret-stores User Story 2, research R6).
///
/// The registry is driven directly, over the in-memory store and a
/// `FakeTimeProvider`, so a test advances minutes without waiting and counts
/// the calls the store actually received. The handlers' side of it (the event
/// the callback records, a restart, a replaced secret) is in
/// ExternalSecretConnectorTests.
module Umbral.Tests.SecretCacheTests

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.DataProtection
open Microsoft.Extensions.Time.Testing
open Xunit
open Umbral.Infrastructure.Secrets
open Umbral.Infrastructure.SecretStores.Registry
open Umbral.Tests.Infrastructure.InMemorySecretStore

let private lifetime = TimeSpan.FromSeconds 300.
let private grace = TimeSpan.FromSeconds 3600.
let private secret = "cached-client-secret-51de09"

type private Rig =
    { Store: InMemorySecretStore
      Clock: FakeTimeProvider
      Registry: SecretStores
      Reference: SecretRef
      /// The failures `Read` reported as "served stale", oldest first.
      Stale: ResizeArray<StoreFailure> }

let private setupWith (storeId: string) (cacheLifetime: TimeSpan) (gracePeriod: TimeSpan) : Rig =
    let store = InMemorySecretStore storeId
    let clock = FakeTimeProvider(DateTimeOffset.UtcNow)
    let local = LocalSecretStore(EphemeralDataProtectionProvider())

    let registry =
        SecretStores(
            local,
            storeId,
            Map [ storeId, lazy (Ok(store :> ISecretStore)) ],
            TimeSpan.FromSeconds 5.,
            clock,
            cacheLifetime,
            gracePeriod
        )

    let name = SecretName.derive "umbral" "home" (clock.GetUtcNow())

    let reference =
        match (store :> ISecretStore).Put(name, secret, CancellationToken.None).Result with
        | Ok reference -> reference
        | Error failure -> failwith $"seeding failed: {failure.Cause}"

    { Store = store
      Clock = clock
      Registry = registry
      Reference = reference
      Stale = ResizeArray() }

let private setup () = setupWith "aws" lifetime grace

/// One read, counting a served-stale report.
let private read (rig: Rig) =
    rig.Registry.Read(
        rig.Reference,
        (fun failure ->
            rig.Stale.Add failure
            Task.FromResult()),
        CancellationToken.None
    )

let private gets (rig: Rig) =
    rig.Store.Calls |> List.filter (fun call -> call.Op = "get") |> List.length

let private seconds (n: float) = TimeSpan.FromSeconds n

[<Fact>]
let ``a fresh entry is served without a call to the store`` () =
    task {
        let rig = setup ()

        let! first = read rig
        Assert.Equal(Ok secret, first)
        Assert.Equal(1, gets rig)

        rig.Clock.Advance(seconds 299.)
        let! second = read rig
        Assert.Equal(Ok secret, second)
        Assert.Equal(1, gets rig)
        Assert.Empty rig.Stale
    }

[<Fact>]
let ``an expired entry is refreshed, and the new value replaces the old`` () =
    task {
        let rig = setup ()
        let! _ = read rig

        // A new version under the same locator, as a replace in the manager
        // does: the clock has not moved since the seeding, so the name is the same.
        let name = SecretName.derive "umbral" "home" (rig.Clock.GetUtcNow())
        let! _ = (rig.Store :> ISecretStore).Put(name, "second-version-77", CancellationToken.None)

        rig.Clock.Advance(seconds 301.)
        let! refreshed = read rig
        Assert.Equal(Ok "second-version-77", refreshed)
        Assert.Equal(2, gets rig)

        // And the refreshed one is fresh again.
        rig.Clock.Advance(seconds 10.)
        let! again = read rig
        Assert.Equal(Ok "second-version-77", again)
        Assert.Equal(2, gets rig)
        Assert.Empty rig.Stale
    }

[<Fact>]
let ``an unreachable store within the grace period serves the stale value and reports it`` () =
    task {
        let rig = setup ()
        let! _ = read rig

        rig.Store.FailEveryCall Unreachable
        rig.Clock.Advance(seconds 301.)

        let! served = read rig
        Assert.Equal(Ok secret, served)
        Assert.Equal(2, gets rig)
        Assert.Equal<StoreFailure list>([ Unreachable ], List.ofSeq rig.Stale)
    }

[<Fact>]
let ``during an outage the store is asked and the failure reported at most once per 30 seconds`` () =
    task {
        let rig = setup ()
        let! _ = read rig

        rig.Store.FailEveryCall Unreachable
        rig.Clock.Advance(seconds 301.)
        let! _ = read rig
        Assert.Equal(2, gets rig)

        rig.Clock.Advance(seconds 10.)
        let! quiet = read rig
        Assert.Equal(Ok secret, quiet)
        Assert.Equal(2, gets rig)
        Assert.Equal(1, rig.Stale.Count)

        rig.Clock.Advance(seconds 21.)
        let! retried = read rig
        Assert.Equal(Ok secret, retried)
        Assert.Equal(3, gets rig)
        Assert.Equal(2, rig.Stale.Count)
    }

[<Fact>]
let ``a recovered store replaces the stale value and ends the outage`` () =
    task {
        let rig = setup ()
        let! _ = read rig

        rig.Store.FailEveryCall Unreachable
        rig.Clock.Advance(seconds 301.)
        let! _ = read rig

        rig.Store.Recover()
        rig.Clock.Advance(seconds 31.)
        let! recovered = read rig
        Assert.Equal(Ok secret, recovered)
        Assert.Equal(1, rig.Stale.Count)

        // Fresh again: no further call.
        let before = gets rig
        let! _ = read rig
        Assert.Equal(before, gets rig)
    }

[<Fact>]
let ``past the lifetime plus the grace period an outage fails the read`` () =
    task {
        let rig = setup ()
        let! _ = read rig

        rig.Store.FailEveryCall Unreachable
        rig.Clock.Advance(seconds 3901.)

        let! failed = read rig
        Assert.Equal(Error Unreachable, failed)
        Assert.Empty rig.Stale
    }

[<Theory>]
[<InlineData("not_found")>]
[<InlineData("access_denied")>]
let ``a store that says the secret is gone or forbidden evicts the entry and fails at once`` (cause: string) =
    task {
        let rig = setup ()
        let! _ = read rig

        let failure = if cause = "not_found" then NotFound else AccessDenied
        rig.Store.FailEveryCall failure
        rig.Clock.Advance(seconds 301.)

        let! failed = read rig
        Assert.Equal(Error failure, failed)
        Assert.Empty rig.Stale

        // Evicted, so once the store answers again the next read is a miss that
        // calls it, not a stale value brought back.
        rig.Store.Recover()
        let before = gets rig
        let! next = read rig
        Assert.Equal(Ok secret, next)
        Assert.Equal(before + 1, gets rig)
    }

[<Fact>]
let ``evicting forces the next read to call the store`` () =
    task {
        let rig = setup ()
        let! _ = read rig
        let! _ = read rig
        Assert.Equal(1, gets rig)

        rig.Registry.Evict rig.Reference

        let! _ = read rig
        Assert.Equal(2, gets rig)
    }

[<Fact>]
let ``a cache lifetime of zero calls the store on every read`` () =
    task {
        let rig = setupWith "aws" TimeSpan.Zero grace

        for _ in 1..3 do
            let! value = read rig
            Assert.Equal(Ok secret, value)

        Assert.Equal(3, gets rig)
    }

[<Fact>]
let ``a grace period of zero fails on the first expired outage`` () =
    task {
        let rig = setupWith "aws" lifetime TimeSpan.Zero
        let! _ = read rig

        rig.Store.FailEveryCall Unreachable
        rig.Clock.Advance(seconds 301.)

        let! failed = read rig
        Assert.Equal(Error Unreachable, failed)
        Assert.Empty rig.Stale
    }

[<Fact>]
let ``a store that is not configured here is not served from the cache`` () =
    task {
        let rig = setup ()
        let! _ = read rig

        let missing = { rig.Reference with Store = "gcp" }

        let! failed =
            rig.Registry.Read(missing, (fun _ -> Task.FromResult()), CancellationToken.None)

        Assert.Equal(Error NotConfigured, failed)
    }

[<Fact>]
let ``local references never go through the cache`` () =
    task {
        let clock = FakeTimeProvider(DateTimeOffset.UtcNow)
        let provider = EphemeralDataProtectionProvider()
        let local = LocalSecretStore provider
        let counted = InMemorySecretStore "aws"

        let registry =
            SecretStores(local, "local", Map [ "aws", lazy (Ok(counted :> ISecretStore)) ], TimeSpan.FromSeconds 5., clock, lifetime, grace)

        let name = SecretName.derive "umbral" "home" (clock.GetUtcNow())

        let! put = (local :> ISecretStore).Put(name, secret, CancellationToken.None)
        let reference = match put with Ok r -> r | Error _ -> failwith "local put"

        let! first = registry.Read(reference, CancellationToken.None)
        let! second = registry.Read(reference, CancellationToken.None)
        Assert.Equal(Ok secret, first)
        Assert.Equal(Ok secret, second)

        // Nothing was ever asked of an external store, and nothing is held for
        // a local reference: a key ring that is lost is felt on the next read.
        Assert.Equal(0, counted.CallCount)
    }

/// A store whose next read waits for the test to answer it, so a test can
/// evict while a read is in flight.
type private GatedStore(id: string) =
    let mutable gate = TaskCompletionSource<Result<string, StoreFailure>>()
    member val Gets = 0 with get, set
    member _.Answer(value: Result<string, StoreFailure>) = gate.TrySetResult value |> ignore
    member _.Rearm() = gate <- TaskCompletionSource<Result<string, StoreFailure>>()

    interface ISecretStore with
        member _.Id = id
        member _.Put(name, _, _) = Task.FromResult(Ok { Store = id; Locator = name.Cloud })
        member this.Get(_, _) =
            this.Gets <- this.Gets + 1
            gate.Task
        member _.Delete(_, _) = Task.FromResult(Ok())
        member _.Probe _ = Task.FromResult(Ok())

[<Fact>]
let ``a read in flight when the secret is replaced does not put the old value back`` () =
    task {
        let clock = FakeTimeProvider(DateTimeOffset.UtcNow)
        let store = GatedStore "aws"
        let local = LocalSecretStore(EphemeralDataProtectionProvider())

        let registry =
            SecretStores(local, "aws", Map [ "aws", lazy (Ok(store :> ISecretStore)) ], TimeSpan.FromSeconds 5., clock, lifetime, grace)

        let reference = { Store = "aws"; Locator = "umbral-home-aaaaaaaa" }

        // A sign-in starts reading the old value; the operator replaces the
        // secret (which evicts) before the store answers.
        let inFlight = registry.Read(reference, CancellationToken.None)
        registry.Evict reference
        store.Answer(Ok "old-value")
        let! first = inFlight
        Assert.Equal(Ok "old-value", first)

        // The next sign-in must ask the store, not get the old value from the cache.
        store.Rearm()
        store.Answer(Ok "new-value")
        let! second = registry.Read(reference, CancellationToken.None)
        Assert.Equal(Ok "new-value", second)
        Assert.Equal(2, store.Gets)
    }

/// A registry over `store`, with the cache on, for the tests that gate a store.
let private gatedRegistry (clock: FakeTimeProvider) (store: ISecretStore) =
    SecretStores(
        LocalSecretStore(EphemeralDataProtectionProvider()),
        "aws",
        Map [ "aws", lazy (Ok store) ],
        TimeSpan.FromSeconds 5.,
        clock,
        lifetime,
        grace
    )

[<Fact>]
let ``while one refresh of an expired entry is in flight, other readers get the stale value without calling the store or raising an event`` () =
    task {
        let clock = FakeTimeProvider(DateTimeOffset.UtcNow)
        let store = GatedStore "aws"
        let registry = gatedRegistry clock store
        let reference = { Store = "aws"; Locator = "umbral-home-aaaaaaaa" }
        let events = ResizeArray<StoreFailure>()

        let read () =
            registry.Read(
                reference,
                (fun failure ->
                    events.Add failure
                    Task.FromResult()),
                CancellationToken.None
            )

        store.Answer(Ok "v1")
        let! first = read ()
        Assert.Equal(Ok "v1", first)

        // Expired, within the grace period; the manager is slow to answer.
        clock.Advance(seconds 301.)
        store.Rearm()
        let refreshing = read ()
        Assert.Equal(2, store.Gets)

        // Readers that arrive meanwhile do not wait for it and do not ask.
        let others = [ for _ in 1..5 -> read () ]
        Assert.True(others |> List.forall (fun t -> t.IsCompleted), "readers behind a refresh in flight must not wait for the store")

        for other in others do
            Assert.Equal(Ok "v1", other.Result)

        Assert.Equal(2, store.Gets)
        Assert.Empty events

        // The refresh fails: that reader reports the one event, and the entry
        // keeps serving without a call for the next 30 seconds.
        store.Answer(Error Unreachable)
        let! stale = refreshing
        Assert.Equal(Ok "v1", stale)
        Assert.Equal<StoreFailure list>([ Unreachable ], List.ofSeq events)

        let! quiet = read ()
        Assert.Equal(Ok "v1", quiet)
        Assert.Equal(2, store.Gets)
        Assert.Equal(1, events.Count)
    }

[<Fact>]
let ``a refresh in flight that never ended is ignored once the bound and a margin have passed`` () =
    task {
        let clock = FakeTimeProvider(DateTimeOffset.UtcNow)
        let store = GatedStore "aws"
        let registry = gatedRegistry clock store
        let reference = { Store = "aws"; Locator = "umbral-home-aaaaaaaa" }

        store.Answer(Ok "v1")
        let! _ = registry.Read(reference, CancellationToken.None)

        clock.Advance(seconds 301.)
        store.Rearm()
        let _abandoned = registry.Read(reference, CancellationToken.None)
        Assert.Equal(2, store.Gets)

        // Its reader is gone: after the 5 second bound and the margin the entry
        // is refreshed by the next reader.
        clock.Advance(seconds 11.)
        store.Rearm()
        store.Answer(Ok "v2")
        let! next = registry.Read(reference, CancellationToken.None)
        Assert.Equal(Ok "v2", next)
        Assert.Equal(3, store.Gets)
    }

[<Fact>]
let ``an eviction that lands while a read is in flight discards that read for any reference`` () =
    task {
        // One counter for the whole registry: a read overlapping ANY eviction
        // writes nothing back. That costs a miss, never a stale value.
        let clock = FakeTimeProvider(DateTimeOffset.UtcNow)
        let store = GatedStore "aws"
        let registry = gatedRegistry clock store
        let reference = { Store = "aws"; Locator = "umbral-home-aaaaaaaa" }
        let other = { Store = "aws"; Locator = "umbral-other-bbbbbbbb" }

        let inFlight = registry.Read(reference, CancellationToken.None)
        registry.Evict other
        store.Answer(Ok "v1")
        let! first = inFlight
        Assert.Equal(Ok "v1", first)

        store.Rearm()
        store.Answer(Ok "v2")
        let! second = registry.Read(reference, CancellationToken.None)
        Assert.Equal(Ok "v2", second)
        Assert.Equal(2, store.Gets)
    }

/// A store whose Put fails the way the test says.
let private failingPutRegistry (failure: StoreFailure option) =
    let store = InMemorySecretStore "aws"
    let clock = FakeTimeProvider(DateTimeOffset.UtcNow)

    let registry =
        SecretStores(
            LocalSecretStore(EphemeralDataProtectionProvider()),
            "aws",
            Map [ "aws", lazy (Ok(store :> ISecretStore)) ],
            TimeSpan.FromSeconds 5.,
            clock,
            lifetime,
            grace
        )

    failure |> Option.iter store.FailEveryCall
    store, registry

[<Fact>]
let ``a failed save to the active store raises the notice, and a good one clears it`` () =
    task {
        let name = SecretName.derive "umbral" "home" DateTimeOffset.UtcNow

        for failure in [ Unreachable; AccessDenied; NotConfigured; Rejected "soft_deleted" ] do
            let store, registry = failingPutRegistry (Some failure)
            Assert.Equal(Healthy, registry.Status.Current)

            let! put = registry.Active.Put(name, secret, CancellationToken.None)
            Assert.Equal(Error failure, put)

            match registry.Status.Current with
            | Failed(raised, _) -> Assert.Equal(failure, raised)
            | Healthy -> failwith $"{failure} did not raise the notice"

            store.Recover()
            let! _ = registry.Active.Put(name, secret, CancellationToken.None)
            Assert.Equal(Healthy, registry.Status.Current)
    }

[<Fact>]
let ``a save that the store answers with not found leaves the notice alone`` () =
    task {
        let _, registry = failingPutRegistry (Some NotFound)
        let name = SecretName.derive "umbral" "home" DateTimeOffset.UtcNow
        let! _ = registry.Active.Put(name, secret, CancellationToken.None)
        Assert.Equal(Healthy, registry.Status.Current)
    }

/// A store that ignores the cancellation token, as an SDK may.
type private DeafStore(id: string) =
    member val Pending = TaskCompletionSource<Result<string, StoreFailure>>() with get

    interface ISecretStore with
        member _.Id = id
        member _.Put(name, _, _) = Task.FromResult(Ok { Store = id; Locator = name.Cloud })
        member this.Get(_, _) = this.Pending.Task
        member _.Delete(_, _) = Task.FromResult(Ok())
        member _.Probe _ = Task.FromResult(Ok())

[<Fact>]
let ``a store that ignores its token still ends as unreachable, and its late failure is observed`` () =
    task {
        let store = DeafStore "aws"

        let registry =
            SecretStores(
                LocalSecretStore(EphemeralDataProtectionProvider()),
                "aws",
                Map [ "aws", lazy (Ok(store :> ISecretStore)) ],
                TimeSpan.FromMilliseconds 50.
            )

        let unobserved = ResizeArray<exn>()
        let handler = EventHandler<UnobservedTaskExceptionEventArgs>(fun _ e -> unobserved.Add e.Exception)
        TaskScheduler.UnobservedTaskException.AddHandler handler

        try
            let reference = { Store = "aws"; Locator = "umbral-home-aaaaaaaa" }
            let reading = registry.Read(reference, CancellationToken.None)
            let! finished = Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds 10.))
            Assert.True(Object.ReferenceEquals(finished, reading), "the read must end by the bound plus a second")
            Assert.Equal(Error Unreachable, reading.Result)

            // The abandoned call fails afterwards; nobody awaits it.
            store.Pending.SetException(InvalidOperationException "late")

            for _ in 1..3 do
                GC.Collect()
                GC.WaitForPendingFinalizers()

            Assert.Empty unobserved
        finally
            TaskScheduler.UnobservedTaskException.RemoveHandler handler
    }
