/// Reading and writing which connectors an app offers (FR-005 to FR-011).
///
/// Two Umbral tables hold it, never OpenIddict's: `app_connector_choices` has a
/// marker row for each app limited to a chosen set, and `app_connectors` has the
/// chosen slugs. An app with no marker is `AllShared`. The marker is separate
/// from the set so an app stays limited when its set becomes empty, rather than
/// silently widening to everything shared (FR-011).
///
/// What a choice offers is decided by `Connectors.offeredTo`, not here: this
/// module only moves the value in and out of the database.
module Umbral.Infrastructure.AppConnectors

open System.Collections.Generic
open System.Threading.Tasks
open Umbral.Domains.Connectors

/// One app's choice. No marker row means AllShared, whether or not the app
/// exists: an unknown client id is simply an app with the default.
let choiceFor (config: Database.DbConfig) (clientId: string) : Task<AppChoice> =
    task {
        use! connection = Database.openConnection config
        use marker = connection.CreateCommand()
        marker.CommandText <- "SELECT count(*) FROM app_connector_choices WHERE client_id = @clientId"
        marker.Parameters.AddWithValue("@clientId", clientId) |> ignore
        let! limited = marker.ExecuteScalarAsync()

        if System.Convert.ToInt32 limited = 0 then
            return AllShared
        else
            use members = connection.CreateCommand()
            members.CommandText <- "SELECT connector_slug FROM app_connectors WHERE client_id = @clientId"
            members.Parameters.AddWithValue("@clientId", clientId) |> ignore
            use! reader = members.ExecuteReaderAsync()
            let slugs = HashSet<string>()
            let mutable go = true

            while go do
                let! hasRow = reader.ReadAsync()
                if hasRow then slugs.Add(reader.GetString 0) |> ignore else go <- false

            return Only(Set.ofSeq slugs)
    }

/// Every limited app's choice, keyed by client id. An app absent from the map is
/// AllShared. One read for a whole screen or a whole `strandedApps` check,
/// instead of one per app.
let allChoices (config: Database.DbConfig) : Task<Map<string, AppChoice>> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        // The LEFT JOIN keeps a limited app whose set is empty: it has a
        // marker and no members, and must still come out as `Only Set.empty`.
        command.CommandText <-
            "SELECT c.client_id, m.connector_slug
             FROM app_connector_choices c
             LEFT JOIN app_connectors m ON m.client_id = c.client_id"

        use! reader = command.ExecuteReaderAsync()
        let sets = Dictionary<string, HashSet<string>>()
        let mutable go = true

        while go do
            let! hasRow = reader.ReadAsync()

            if hasRow then
                let clientId = reader.GetString 0

                if not (sets.ContainsKey clientId) then
                    sets[clientId] <- HashSet<string>()

                if not (reader.IsDBNull 1) then
                    sets[clientId].Add(reader.GetString 1) |> ignore
            else
                go <- false

        return sets |> Seq.map (fun kv -> kv.Key, Only(Set.ofSeq kv.Value)) |> Map.ofSeq
    }

/// Store an app's choice in one transaction. `Only` writes the marker and
/// replaces the set; `AllShared` deletes the marker, whose cascade removes the
/// set with it. Saving the same choice twice leaves the same rows.
let setChoice (config: Database.DbConfig) (clientId: string) (choice: AppChoice) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use transaction = connection.BeginTransaction()

        match choice with
        | AllShared ->
            use delete = connection.CreateCommand()
            delete.Transaction <- transaction
            delete.CommandText <- "DELETE FROM app_connector_choices WHERE client_id = @clientId"
            delete.Parameters.AddWithValue("@clientId", clientId) |> ignore
            let! _ = delete.ExecuteNonQueryAsync()
            ()

        | Only slugs ->
            use marker = connection.CreateCommand()
            marker.Transaction <- transaction
            marker.CommandText <- "INSERT INTO app_connector_choices (client_id) VALUES (@clientId) ON CONFLICT DO NOTHING"
            marker.Parameters.AddWithValue("@clientId", clientId) |> ignore
            let! _ = marker.ExecuteNonQueryAsync()

            use clear = connection.CreateCommand()
            clear.Transaction <- transaction
            clear.CommandText <- "DELETE FROM app_connectors WHERE client_id = @clientId"
            clear.Parameters.AddWithValue("@clientId", clientId) |> ignore
            let! _ = clear.ExecuteNonQueryAsync()

            for slug in slugs do
                use insert = connection.CreateCommand()
                insert.Transaction <- transaction

                insert.CommandText <-
                    "INSERT INTO app_connectors (client_id, connector_slug) VALUES (@clientId, @slug)"

                insert.Parameters.AddWithValue("@clientId", clientId) |> ignore
                insert.Parameters.AddWithValue("@slug", slug) |> ignore
                let! _ = insert.ExecuteNonQueryAsync()
                ()

        do! transaction.CommitAsync()
    }

/// Remove an app's choice rows when the app is deleted. No foreign key points
/// into OpenIddict's tables, so the handler that deletes the app calls this
/// itself; deleting the marker cascades to the set.
let deleteFor (config: Database.DbConfig) (clientId: string) : Task<unit> =
    setChoice config clientId AllShared
