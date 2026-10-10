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
            is_enabled, is_shared, created_utc, updated_utc FROM connectors"

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
      IsEnabled = reader.GetInt32 6 = 1
      IsShared = reader.GetInt32 7 = 1
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

/// Create or update, keyed on the immutable slug. The kind is written when the
/// row is created and never touched again: it is read back from the row, not
/// worked out from the slug, because `discord-frunchy` is a Discord connector
/// only because its row says so (FR-012). The secret is written only
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
                  is_enabled, is_shared, created_utc, updated_utc)
             VALUES (@slug, @kind, @name, @clientId, @secretRef, @secretHint, @enabled, @shared, @now, @now)
             ON CONFLICT (slug) DO UPDATE SET
                 display_name      = @name,
                 client_id         = @clientId,
                 client_secret_ref = @secretRef,
                 client_secret_hint= @secretHint,
                 is_enabled        = @enabled,
                 is_shared         = @shared,
                 updated_utc       = @now"

        command.Parameters.AddWithValue("@slug", connector.Slug) |> ignore
        command.Parameters.AddWithValue("@kind", connector.Kind.Value) |> ignore
        command.Parameters.AddWithValue("@name", connector.DisplayName) |> ignore
        command.Parameters.AddWithValue("@clientId", connector.ClientId) |> ignore
        command.Parameters.AddWithValue("@secretRef", secretRef) |> ignore
        command.Parameters.AddWithValue("@secretHint", secretHint) |> ignore
        command.Parameters.AddWithValue("@enabled", (if connector.IsEnabled then 1 else 0)) |> ignore
        command.Parameters.AddWithValue("@shared", (if connector.IsShared then 1 else 0)) |> ignore
        command.Parameters.AddWithValue("@now", now) |> ignore

        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

/// Add a connector under a slug the operator chose (FR-012). Returns false and
/// writes nothing when the slug already belongs to a connector: the slug is the
/// primary key and sits in the callback URL, so an existing one is never
/// replaced here. The caller has already checked `Ids.isValidSlug`.
let create
    (config: Database.DbConfig)
    (clock: TimeProvider)
    (connector: Connector)
    (secretRef: string, secretHint: string)
    : Task<bool> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "INSERT INTO connectors
                 (slug, kind, display_name, client_id, client_secret_ref, client_secret_hint,
                  is_enabled, is_shared, created_utc, updated_utc)
             VALUES (@slug, @kind, @name, @clientId, @secretRef, @secretHint, @enabled, @shared, @now, @now)
             ON CONFLICT (slug) DO NOTHING"

        command.Parameters.AddWithValue("@slug", connector.Slug) |> ignore
        command.Parameters.AddWithValue("@kind", connector.Kind.Value) |> ignore
        command.Parameters.AddWithValue("@name", connector.DisplayName) |> ignore
        command.Parameters.AddWithValue("@clientId", connector.ClientId) |> ignore
        command.Parameters.AddWithValue("@secretRef", secretRef) |> ignore
        command.Parameters.AddWithValue("@secretHint", secretHint) |> ignore
        command.Parameters.AddWithValue("@enabled", (if connector.IsEnabled then 1 else 0)) |> ignore
        command.Parameters.AddWithValue("@shared", (if connector.IsShared then 1 else 0)) |> ignore
        command.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore

        let! inserted = command.ExecuteNonQueryAsync()
        return inserted > 0
    }

/// Change what the card form edits on a stored connector: display name, client
/// id, the shared setting and, when one is supplied, the secret. Slug, kind,
/// scopes and the enabled flag are left alone: the slug and kind never change
/// (FR-013) and enabling has its own action. Returns false when no connector has
/// that slug.
let update
    (config: Database.DbConfig)
    (clock: TimeProvider)
    (slug: string)
    (displayName: string)
    (clientId: string)
    (isShared: bool)
    (newSecretRef: (string * string) option)
    : Task<bool> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        // Without a new secret the stored reference and hint stay as they are.
        command.CommandText <-
            "UPDATE connectors SET
                 display_name       = @name,
                 client_id          = @clientId,
                 is_shared          = @shared,
                 client_secret_ref  = coalesce(@secretRef, client_secret_ref),
                 client_secret_hint = coalesce(@secretHint, client_secret_hint),
                 updated_utc        = @now
             WHERE slug = @slug"

        command.Parameters.AddWithValue("@slug", slug) |> ignore
        command.Parameters.AddWithValue("@name", displayName) |> ignore
        command.Parameters.AddWithValue("@clientId", clientId) |> ignore
        command.Parameters.AddWithValue("@shared", (if isShared then 1 else 0)) |> ignore

        match newSecretRef with
        | Some(ref, hint) ->
            command.Parameters.AddWithValue("@secretRef", ref) |> ignore
            command.Parameters.AddWithValue("@secretHint", hint) |> ignore
        | None ->
            command.Parameters.AddWithValue("@secretRef", DBNull.Value) |> ignore
            command.Parameters.AddWithValue("@secretHint", DBNull.Value) |> ignore

        command.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore

        let! changed = command.ExecuteNonQueryAsync()
        return changed > 0
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
