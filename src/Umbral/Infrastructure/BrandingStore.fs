/// Reading and writing an app's sign-in branding (spec 006, research R6): one
/// Umbral table, `app_branding`, never OpenIddict's.
///
/// A row exists while the app has a logo, an accent colour, or both. Removing
/// the last remaining part deletes the row, so "no branding" is always "no
/// row" and a reader never has to ask whether a row is empty. The logo is the
/// rewritten PNG `PngLogo.validate` returned, and its SHA-256 is its address.
///
/// Reads that serve a page (`forApp`, `allForApps`) never select `logo_png`: a
/// logo can be 1.25 MB and the sign-in page asks on every visit. Only
/// `logoBytes`, behind the logo's own address, reads the BLOB.
///
/// Every write takes `now` for `updated_at`, from the host's `TimeProvider`, so
/// a test can fix it.
module Umbral.Infrastructure.BrandingStore

open System
open System.Collections.Generic
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Umbral.Domains.Branding

/// The branding in four columns read from `first`: the accent, then the logo's
/// address, width and height. The page-serving reads select exactly these and
/// not the bytes. The CHECK on `accent_color` makes
/// a stored value always parse, so a failure here would mean the column was
/// written around the store; it reads as no accent rather than an error page on
/// someone's sign-in.
let private readBranding (reader: SqliteDataReader) (first: int) : AppBranding =
    let accent =
        if reader.IsDBNull first then
            None
        else
            Accent.parse ("#" + reader.GetString first) |> Result.toOption

    // The table's CHECKs make the hash, width and height present together.
    let logo =
        if reader.IsDBNull(first + 1) then
            None
        else
            Some
                { Sha256 = reader.GetString(first + 1)
                  Width = reader.GetInt32(first + 2)
                  Height = reader.GetInt32(first + 3) }

    { Accent = accent; Logo = logo }

/// An app's branding, or `None` when it has none. Picker and Apps card.
let forApp (config: Database.DbConfig) (clientId: string) : Task<AppBranding option> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "SELECT accent_color, logo_sha256, logo_width, logo_height FROM app_branding WHERE client_id = @clientId"

        command.Parameters.AddWithValue("@clientId", clientId) |> ignore
        use! reader = command.ExecuteReaderAsync()
        let! hasRow = reader.ReadAsync()
        return if hasRow then Some(readBranding reader 0) else None
    }

/// Every branded app, keyed by client id. An app absent from the map has no
/// branding. One read for a whole Apps screen, instead of one per card.
let allForApps (config: Database.DbConfig) : Task<Map<string, AppBranding>> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "SELECT client_id, accent_color, logo_sha256, logo_width, logo_height FROM app_branding"

        use! reader = command.ExecuteReaderAsync()
        let found = Dictionary<string, AppBranding>()
        let mutable go = true

        while go do
            let! hasRow = reader.ReadAsync()

            if hasRow then
                found[reader.GetString 0] <- readBranding reader 1
            else
                go <- false

        return found |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
    }

/// The logo with this hash, for its own address. `None` for a hash no app holds
/// any more: a replaced or removed logo's address stops resolving at once.
let logoBytes (config: Database.DbConfig) (sha256: string) : Task<byte[] option> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT logo_png FROM app_branding WHERE logo_sha256 = @sha256"
        command.Parameters.AddWithValue("@sha256", sha256) |> ignore
        let! value = command.ExecuteScalarAsync()

        match value with
        | :? (byte[]) as bytes -> return Some bytes
        | _ -> return None
    }

/// Drop the row when nothing is left in it. Run inside the transaction of the
/// write that may have emptied it, so a reader never sees an empty row.
let private deleteIfEmpty (connection: SqliteConnection) (transaction: SqliteTransaction) (clientId: string) =
    task {
        use delete = connection.CreateCommand()
        delete.Transaction <- transaction

        delete.CommandText <-
            "DELETE FROM app_branding WHERE client_id = @clientId AND accent_color IS NULL AND logo_png IS NULL"

        delete.Parameters.AddWithValue("@clientId", clientId) |> ignore
        let! _ = delete.ExecuteNonQueryAsync()
        ()
    }

/// Store a logo, replacing the app's current one and keeping its accent. One
/// statement writes the bytes, hash and size together, so the row is never
/// half a logo, and the replaced logo's bytes are gone with the overwrite
/// (FR-020). A hash another app already holds is refused by the database's
/// unique index: the exception is the caller's to handle, and nothing is
/// written.
let setLogo (config: Database.DbConfig) (now: DateTimeOffset) (clientId: string) (logo: Logo) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "INSERT INTO app_branding (client_id, logo_png, logo_sha256, logo_width, logo_height, updated_at)
             VALUES (@clientId, @png, @sha256, @width, @height, @now)
             ON CONFLICT (client_id) DO UPDATE SET
                 logo_png = excluded.logo_png,
                 logo_sha256 = excluded.logo_sha256,
                 logo_width = excluded.logo_width,
                 logo_height = excluded.logo_height,
                 updated_at = excluded.updated_at"

        command.Parameters.AddWithValue("@clientId", clientId) |> ignore
        command.Parameters.AddWithValue("@png", logo.Png) |> ignore
        command.Parameters.AddWithValue("@sha256", logo.Sha256) |> ignore
        command.Parameters.AddWithValue("@width", logo.Width) |> ignore
        command.Parameters.AddWithValue("@height", logo.Height) |> ignore
        command.Parameters.AddWithValue("@now", now.ToUniversalTime().ToString "o") |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        ()
    }

/// Clear one part of an app's branding, and drop the row if that was the last of
/// it. Returns whether anything was cleared: the UPDATE's affected-row count,
/// read in the same transaction as the delete, so the answer is about this call
/// and not about a row another request changed a moment later. The handlers log
/// a security event only when it is true.
///
/// `describe` sets the statement on the command, so each statement is a
/// literal assigned to `CommandText` where it is written (MetaTests rule 3), not
/// a value passed through.
let private clearPart
    (config: Database.DbConfig)
    (now: DateTimeOffset)
    (clientId: string)
    (describe: SqliteCommand -> unit)
    : Task<bool> =
    task {
        use! connection = Database.openConnection config
        use transaction = connection.BeginTransaction()

        use clear = connection.CreateCommand()
        clear.Transaction <- transaction
        describe clear
        clear.Parameters.AddWithValue("@clientId", clientId) |> ignore
        clear.Parameters.AddWithValue("@now", now.ToUniversalTime().ToString "o") |> ignore
        let! changed = clear.ExecuteNonQueryAsync()

        do! deleteIfEmpty connection transaction clientId
        do! transaction.CommitAsync()
        return changed > 0
    }

/// Clear the app's logo. Keeps the row while an accent is set and deletes it
/// otherwise. Removing a logo that is not there is not an error and writes
/// nothing, and says so: the result is whether there was a logo to remove.
let removeLogo (config: Database.DbConfig) (now: DateTimeOffset) (clientId: string) : Task<bool> =
    clearPart config now clientId (fun command ->
        command.CommandText <-
            "UPDATE app_branding
             SET logo_png = NULL, logo_sha256 = NULL, logo_width = NULL, logo_height = NULL, updated_at = @now
             WHERE client_id = @clientId AND logo_png IS NOT NULL")

/// Clear the app's accent. Deletes the row when no logo is left. Clearing an
/// accent that is not there is not an error and writes nothing, and says so: the
/// result is whether there was an accent to clear.
let clearAccent (config: Database.DbConfig) (now: DateTimeOffset) (clientId: string) : Task<bool> =
    clearPart config now clientId (fun command ->
        command.CommandText <-
            "UPDATE app_branding SET accent_color = NULL, updated_at = @now
             WHERE client_id = @clientId AND accent_color IS NOT NULL")

/// Set the accent. Keeps the app's logo. With `None` it clears, as `clearAccent`
/// does, and drops the answer: a caller that must know whether an accent was
/// there (to log it) calls `clearAccent`.
let setAccent (config: Database.DbConfig) (now: DateTimeOffset) (clientId: string) (accent: Accent option) : Task<unit> =
    task {
        match accent with
        | None ->
            let! _ = clearAccent config now clientId
            ()
        | Some accent ->
            use! connection = Database.openConnection config
            use write = connection.CreateCommand()

            write.CommandText <-
                "INSERT INTO app_branding (client_id, accent_color, updated_at) VALUES (@clientId, @accent, @now)
                 ON CONFLICT (client_id) DO UPDATE SET
                     accent_color = excluded.accent_color,
                     updated_at = excluded.updated_at"

            write.Parameters.AddWithValue("@accent", Accent.hex accent) |> ignore
            write.Parameters.AddWithValue("@clientId", clientId) |> ignore
            write.Parameters.AddWithValue("@now", now.ToUniversalTime().ToString "o") |> ignore
            let! _ = write.ExecuteNonQueryAsync()
            ()
    }

/// Remove an app's branding when the app is deleted. No foreign key points into
/// OpenIddict's tables, so the handler that deletes the app calls this itself,
/// next to `AppConnectors.deleteFor`. An app with no row is not an error.
let deleteFor (config: Database.DbConfig) (clientId: string) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "DELETE FROM app_branding WHERE client_id = @clientId"
        command.Parameters.AddWithValue("@clientId", clientId) |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        ()
    }
