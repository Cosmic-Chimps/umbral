/// A secret store that lives in a dictionary, for tests of everything around
/// the stores: the handlers, the cache, the move command.
///
/// The four real adapters are tested against canned vendor responses in their
/// own files; this one exists so the rest of the product can be driven through a
/// store that behaves like a manager (versions, a stable locator, failures on
/// demand) without any network, credentials or account (FR-025).
///
/// It is registered with `Fixtures.withSecretStores` under whatever id a test
/// wants (`aws`, say), so a test exercises the real handlers and the real
/// registry against it.
module Umbral.Tests.Infrastructure.InMemorySecretStore

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Umbral.Infrastructure.Secrets

/// One call a store received, for assertions on what the product asked of it.
/// `Target` is the rendered name for a put and the locator for the rest; it is
/// `None` for a probe. Never the plaintext.
type Call = { Op: string; Target: string option }

/// What the store does instead of answering normally.
type private Behaviour =
    | Answer
    | Fail of StoreFailure
    | Hang

type InMemorySecretStore(id: string) =
    let gate = obj ()

    // locator -> every value written under it, oldest first. A put to an
    // existing name adds a version, as a manager does.
    let versions = Dictionary<string, ResizeArray<string>>()
    let calls = ResizeArray<Call>()

    // The call number (1-based, counting every call) -> what happens to it.
    let scripted = Dictionary<int, Behaviour>()
    let mutable every = Answer
    let mutable count = 0

    /// Records the call, and says what it must do. Decided under the lock so
    /// that "the Nth call" is well defined when a test makes calls in parallel.
    let enter (op: string) (target: string option) =
        lock gate (fun () ->
            count <- count + 1
            calls.Add { Op = op; Target = target }

            match scripted.TryGetValue count with
            | true, behaviour ->
                scripted.Remove count |> ignore
                behaviour
            | _ -> every)

    let respond (behaviour: Behaviour) (ct: CancellationToken) (answer: unit -> Result<'a, StoreFailure>) =
        task {
            match behaviour with
            | Answer -> return answer ()
            | Fail failure -> return Error failure
            | Hang ->
                // Until the caller's token ends it. Through the registry that
                // is the call bound, reported as `Unreachable`; called directly
                // it ends in the cancellation itself.
                do! Task.Delay(Timeout.Infinite, ct)
                return Error Unreachable
        }

    /// The next call (whatever its operation) fails with `failure`.
    member this.FailNextCall(failure: StoreFailure) = this.FailCall(count + 1, failure)

    /// The `n`th call from now on, counted over the store's whole life (the
    /// first call it ever received is 1), fails once with `failure`.
    member _.FailCall(n: int, failure: StoreFailure) = lock gate (fun () -> scripted[n] <- Fail failure)

    /// Every call fails with `failure` until `Recover`.
    member _.FailEveryCall(failure: StoreFailure) = lock gate (fun () -> every <- Fail failure)

    /// Every call hangs until its token is cancelled, until `Recover`.
    member _.HangEveryCall() = lock gate (fun () -> every <- Hang)

    /// Back to answering normally; scripted single failures are dropped too.
    member _.Recover() =
        lock gate (fun () ->
            every <- Answer
            scripted.Clear())

    /// Every call so far, oldest first.
    member _.Calls = lock gate (fun () -> List.ofSeq calls)

    /// How many calls so far, for "this read made no call".
    member _.CallCount = lock gate (fun () -> count)

    /// The values written under a locator, oldest first; empty when none.
    member _.VersionsOf(locator: string) =
        lock gate (fun () ->
            match versions.TryGetValue locator with
            | true, all -> List.ofSeq all
            | _ -> [])

    /// Every locator that currently holds a secret.
    member _.Locators = lock gate (fun () -> List.ofSeq versions.Keys)

    /// Every value held, every version, for a test that must show a plaintext is
    /// (or is not) somewhere.
    member _.AllValues = lock gate (fun () -> versions.Values |> Seq.collect (fun (all: ResizeArray<string>) -> all :> seq<string>) |> List.ofSeq)

    /// The reference this store hands out for a name, so a test can look up what
    /// a put produced without calling the store.
    member _.ReferenceFor(name: SecretName) =
        { Store = id
          Locator = $"mem/{name.Cloud}" }

    interface ISecretStore with
        member _.Id = id

        member this.Put(name, plaintext, ct) =
            let reference = this.ReferenceFor name

            respond (enter "put" (Some name.Cloud)) ct (fun () ->
                lock gate (fun () ->
                    match versions.TryGetValue reference.Locator with
                    | true, all -> all.Add plaintext
                    | _ -> versions[reference.Locator] <- ResizeArray [ plaintext ])

                Ok reference)

        member _.Get(reference, ct) =
            respond (enter "get" (Some reference.Locator)) ct (fun () ->
                lock gate (fun () ->
                    match versions.TryGetValue reference.Locator with
                    | true, all when all.Count > 0 -> Ok all[all.Count - 1]
                    | _ -> Error NotFound))

        member _.Delete(reference, ct) =
            respond (enter "delete" (Some reference.Locator)) ct (fun () ->
                lock gate (fun () -> versions.Remove reference.Locator |> ignore)
                Ok())

        member _.Probe ct = respond (enter "probe" None) ct (fun () -> Ok())
