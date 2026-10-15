/// The `move-secrets` command: bring every connector secret into the store
/// `UMBRAL__SECRET_STORE` names (specs/005 research R8, contracts/move-secrets-cli.md).
///
/// This is a function over the registry, the database and the clock, not a piece
/// of `Program.fs`, so that the test of its central promise (interrupted by a
/// failing store at any call and run again, no connector is ever left without a
/// readable secret) runs in process against an in-memory store that fails on
/// its Nth call.
///
/// The order per connector is what makes that promise true. Every step up to the
/// switch leaves the old reference in place and readable; the switch is one
/// guarded statement; the old copy is deleted only after the new reference is
/// committed. So a crash, an outage or a Ctrl-C leaves, at worst, an unused copy
/// in one store, and a re-run starts again from what the references say. The
/// write goes to the same derived name every time (`SecretName.derive`), so a
/// retry adds a version to the copy it already made, not a second one.
module Umbral.Infrastructure.SecretMove

open System.Threading
open System.Threading.Tasks
open Umbral.Domains.Connectors
open Umbral.Infrastructure.Secrets
open Umbral.Infrastructure.SecretStores.Registry
open Umbral.Infrastructure.SecretStores.Settings
open Umbral.Views

/// How one connector ended.
type private Outcome =
    | Moved
    | WouldMove
    | InPlace
    | ChangedMeanwhile
    /// Somebody else held the connector's lease: the admin screen (an edit) or
    /// another run of this command.
    | Busy
    | Failed

/// Moves one connector whose secret is in `source`, while holding its lease.
/// Writes its own report line.
let private moveLocked
    (stores: SecretStores)
    (settings: SecretStoreSettings)
    (dbConfig: Database.DbConfig)
    (clock: System.TimeProvider)
    (write: string -> unit)
    (connector: Connector)
    (source: SecretRef)
    : Task<Outcome> =
    task {
        let t = Texts.current ()
        let slug = connector.Slug
        let target = stores.Active
        let none = CancellationToken.None

        // A failure before the switch: said in plain words, recorded without
        // anything the store said, and the connector keeps its old copy.
        let failed (store: string) (op: string) (cause: StoreFailure) =
            task {
                do! SecurityEvents.recordStoreFailure dbConfig clock SecurityEvents.Actor.System slug None store op cause
                write (t.MoveFailed slug source.Store target.Id (t.MoveStoreFailure store cause))
                return Failed
            }

        match stores.For source.Store with
        | Error cause -> return! failed source.Store "get" cause
        | Ok sourceStore ->

        match! sourceStore.Get(source, none) with
        | Error cause -> return! failed source.Store "get" cause
        | Ok value ->

        // Held across three calls, so it is a `Protected` and not a string.
        let plaintext = Protected.Of value
        let name = SecretName.derive settings.Prefix slug connector.CreatedUtc

        match! target.Put(name, plaintext.Reveal, none) with
        | Error cause -> return! failed target.Id "put" cause
        | Ok copy ->

        match! target.Get(copy, none) with
        | Error cause -> return! failed target.Id "get" cause
        | Ok readBack when readBack <> plaintext.Reveal ->
            do!
                SecurityEvents.recordStoreFailure
                    dbConfig
                    clock
                    SecurityEvents.Actor.System
                    slug
                    None
                    target.Id
                    "get"
                    (Rejected "read back differs")

            write (t.MoveFailed slug source.Store target.Id (t.MoveMismatch target.Id))
            return Failed
        | Ok _ ->

        let moved =
            { SecurityEvents.event SecurityEvents.ConnectorSecretMoved SecurityEvents.Success SecurityEvents.Actor.System with
                ConnectorSlug = Some slug
                Detail = Some $"from={source.Store};to={target.Id}" }

        match! ConnectorStore.switchSecretRef dbConfig clock slug connector.ClientSecretRef copy.Encoded moved with
        | ConnectorStore.Changed ->
            // Somebody replaced the secret while this was copying (the lease
            // makes that rare: the admin screen declines while it is held). What
            // was just written is left alone rather than deleted: a replace
            // derives the very same name, so deleting it could remove the secret
            // the operator has just entered.
            write (t.MoveChangedMeanwhile slug)
            return ChangedMeanwhile
        | ConnectorStore.Gone ->
            // The connector was deleted while this was copying, and nothing
            // refers to the copy: remove it, or it outlives the connector in the
            // manager. Safe to delete, because a connector created again under
            // this slug gets another name (its `created_utc` differs). Best
            // effort: a store that refuses leaves an unreferenced secret.
            let! _ = target.Delete(copy, none)
            stores.Evict copy
            write (t.MoveChangedMeanwhile slug)
            return ChangedMeanwhile
        | ConnectorStore.Switched ->
            stores.Evict source

            match! sourceStore.Delete(source, none) with
            | Ok() ->
                write (t.MoveMoved slug source.Store target.Id)
                return Moved
            | Error cause ->
                // Already moved, so nothing is lost and the exit stays 0; the
                // operator is told what to remove by hand and where.
                let leftover = (SecretName.derive settings.Prefix slug connector.CreatedUtc).In source.Store
                write (t.MoveLeftBehind slug source.Store target.Id cause leftover)
                return Moved
    }

/// Moves one connector, under the lease on its secret. The move reads the
/// source, writes the target and switches the row over several calls to a
/// manager, and an operator replacing the secret writes the very same derived
/// name; without the lease a move that read the old value could write it after
/// the replace and leave it as the manager's latest version while the row names
/// the new one. The row is read again once the lease is held, because the one
/// the run listed may be minutes old.
let private moveOne
    (stores: SecretStores)
    (settings: SecretStoreSettings)
    (dbConfig: Database.DbConfig)
    (clock: System.TimeProvider)
    (write: string -> unit)
    (connector: Connector)
    (source: SecretRef)
    : Task<Outcome> =
    task {
        let t = Texts.current ()

        let! outcome =
            ConnectorStore.withLease dbConfig clock connector.Slug (fun () ->
                task {
                    let! current = ConnectorStore.findBySlug dbConfig connector.Slug

                    match current with
                    | Some row when row.ClientSecretRef = connector.ClientSecretRef ->
                        return! moveLocked stores settings dbConfig clock write row source
                    | _ ->
                        // Replaced or deleted since the run listed it.
                        write (t.MoveChangedMeanwhile connector.Slug)
                        return ChangedMeanwhile
                })

        match outcome with
        | Some result -> return result
        | None ->
            write (t.MoveBusy connector.Slug)
            return Busy
    }

/// Runs the command. `dryRun` makes no call to any store and writes nothing: it
/// reports what a real run would do from the references alone. Returns the exit
/// code: 1 when at least one connector failed, else 0.
let run
    (stores: SecretStores)
    (settings: SecretStoreSettings)
    (dbConfig: Database.DbConfig)
    (clock: System.TimeProvider)
    (dryRun: bool)
    (write: string -> unit)
    : Task<int> =
    task {
        let t = Texts.current ()
        let targetId = stores.Active.Id
        let! connectors = ConnectorStore.listAll dbConfig
        let results = ResizeArray<Outcome>()

        for connector in connectors do
            match SecretRef.Decode connector.ClientSecretRef with
            | Error _ ->
                // Cannot happen through the product, which always writes a
                // prefix; a hand-edited row is reported, not guessed at.
                write (t.MoveFailed connector.Slug "?" targetId t.MoveReferenceUnreadable)
                results.Add Failed
            | Ok source when source.Store = targetId ->
                write (t.MoveAlreadyIn connector.Slug targetId)
                results.Add InPlace
            | Ok source when dryRun ->
                write (t.MoveWouldMove connector.Slug source.Store targetId)
                results.Add WouldMove
            | Ok source ->
                let! result = moveOne stores settings dbConfig clock write connector source
                results.Add result

        let count (wanted: Outcome list) = results |> Seq.filter (fun r -> List.contains r wanted) |> Seq.length
        let failed = count [ Failed ]

        write ""

        write (
            t.MoveSummary dryRun results.Count (count [ Moved; WouldMove ]) (count [ InPlace ]) (count [ ChangedMeanwhile; Busy ]) failed
        )

        return if failed > 0 then 1 else 0
    }
