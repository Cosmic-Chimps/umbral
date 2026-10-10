/// Reading and writing the connectors table.
///
/// Every read happens inside the request that needs it. That is the whole
/// reason the connectors are hand-rolled rather than ASP.NET Core auth handlers
/// (research.md D1): a secret replaced in the admin form at 15:00 is in effect
/// at 15:00:01, with no options cache to evict and no restart.
module Umbral.Infrastructure.ConnectorStore

open System
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Umbral.Domains.Connectors

[<Literal>]
let private Columns =
    "SELECT slug, kind, display_name, client_id, client_secret_ref, client_secret_hint,
            extra_scopes, is_enabled, created_utc, updated_utc FROM connectors"

let private readOptional (reader: SqliteDataReader) (ordinal: int) =
    if reader.IsDBNull ordinal then None else Some(reader.GetString ordinal)

let private read (reader: SqliteDataReader) =
    let kind =
        match parseKind (reader.GetString 1) with
        | Some k -> k
        | None ->
            // A row whose kind this build does not know: a downgrade, or a
            // hand-edited database. Refusing beats guessing at a provider.
            failwith $"The connector '{reader.GetString 0}' has an unknown kind '{reader.GetString 1}'."

    { Slug = reader.GetString 0
      Kind = kind
      DisplayName = reader.GetString 2
      ClientId = reader.GetString 3
      ClientSecretRef = reader.GetString 4
      ClientSecretHint = reader.GetString 5
      ExtraScopes =
        readOptional reader 6
        |> Option.map (fun s -> s.Split(' ', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray)
        |> Option.defaultValue []
      IsEnabled = reader.GetInt32 7 = 1
      CreatedUtc = DateTimeOffset.Parse(reader.GetString 8, Globalization.CultureInfo.InvariantCulture)
      UpdatedUtc = DateTimeOffset.Parse(reader.GetString 9, Globalization.CultureInfo.InvariantCulture) }

let private query (config: Database.DbConfig) (where: string) (parameters: (string * string) list) =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- Columns + where

        for name, value in parameters do
            command.Parameters.AddWithValue(name, value) |> ignore

        use! reader = command.ExecuteReaderAsync()
        let results = ResizeArray()
        let mutable go = true

        while go do
            let! hasRow = reader.ReadAsync()
            if hasRow then results.Add(read reader) else go <- false

        return List.ofSeq results
    }

let listAll (config: Database.DbConfig) = query config " ORDER BY slug" []

/// What the sign-in picker offers. Disabled connectors are absent rather than
/// greyed out: an option that cannot work should not be presented.
let listEnabled (config: Database.DbConfig) = query config " WHERE is_enabled = 1 ORDER BY slug" []

let findBySlug (config: Database.DbConfig) (slug: string) =
    task {
        let! rows = query config " WHERE slug = @slug" [ "@slug", slug ]
        return List.tryHead rows
    }

/// Create or update, keyed on the immutable slug. The secret is written only
/// when a new one is supplied, so saving the form without retyping it does not
/// wipe the stored credential — the screen cannot show it back, so an operator
/// editing a display name has nothing to retype.
let save
    (config: Database.DbConfig)
    (clock: TimeProvider)
    (connector: Connector)
    (newSecretRef: (string * string) option)
    : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        let now = clock.GetUtcNow().ToString "o"

        let secretRef, secretHint =
            match newSecretRef with
            | Some(ref, hint) -> ref, hint
            | None -> connector.ClientSecretRef, connector.ClientSecretHint

        command.CommandText <-
            "INSERT INTO connectors
                 (slug, kind, display_name, client_id, client_secret_ref, client_secret_hint,
                  extra_scopes, is_enabled, created_utc, updated_utc)
             VALUES (@slug, @kind, @name, @clientId, @secretRef, @secretHint, @scopes, @enabled, @now, @now)
             ON CONFLICT (slug) DO UPDATE SET
                 display_name      = @name,
                 client_id         = @clientId,
                 client_secret_ref = @secretRef,
                 client_secret_hint= @secretHint,
                 extra_scopes      = @scopes,
                 is_enabled        = @enabled,
                 updated_utc       = @now"

        command.Parameters.AddWithValue("@slug", connector.Slug) |> ignore
        command.Parameters.AddWithValue("@kind", connector.Kind.Value) |> ignore
        command.Parameters.AddWithValue("@name", connector.DisplayName) |> ignore
        command.Parameters.AddWithValue("@clientId", connector.ClientId) |> ignore
        command.Parameters.AddWithValue("@secretRef", secretRef) |> ignore
        command.Parameters.AddWithValue("@secretHint", secretHint) |> ignore
        command.Parameters.AddWithValue("@scopes", String.concat " " connector.ExtraScopes) |> ignore
        command.Parameters.AddWithValue("@enabled", (if connector.IsEnabled then 1 else 0)) |> ignore
        command.Parameters.AddWithValue("@now", now) |> ignore

        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

let setEnabled (config: Database.DbConfig) (clock: TimeProvider) (slug: string) (isEnabled: bool) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "UPDATE connectors SET is_enabled = @enabled, updated_utc = @now WHERE slug = @slug"
        command.Parameters.AddWithValue("@enabled", (if isEnabled then 1 else 0)) |> ignore
        command.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore
        command.Parameters.AddWithValue("@slug", slug) |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

let delete (config: Database.DbConfig) (slug: string) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "DELETE FROM connectors WHERE slug = @slug"
        command.Parameters.AddWithValue("@slug", slug) |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

/// How many connectors would remain usable if `slug` were disabled or deleted.
/// Zero means nobody can sign in anywhere, which the operator must be warned
/// about before the fact rather than discover after it (FR-023).
let enabledCountExcluding (config: Database.DbConfig) (slug: string) : Task<int> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT count(*) FROM connectors WHERE is_enabled = 1 AND slug <> @slug"
        command.Parameters.AddWithValue("@slug", slug) |> ignore
        let! value = command.ExecuteScalarAsync()
        return Convert.ToInt32 value
    }
