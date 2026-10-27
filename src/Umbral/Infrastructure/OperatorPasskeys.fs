/// The operator credential's passkeys, and the switch that turns password
/// sign-in off (specs/011-operator-passkeys, data-model.md).
///
/// They belong to the operator credential (spec 001 FR-017, constitution 3.0.1
/// Principle VII), not to a user: the operator is not a person in `users`, so
/// they are kept apart from `passkeys`, with no foreign key. Only reads and
/// writes live here; whether an answer is genuine is the library's to say
/// (`WebAuthn.fs`). Every rule that two requests could race (the limit of ten,
/// the last passkey while password sign-in is off, the switch needing a passkey)
/// is inside one statement, because a check made a moment earlier could be
/// outrun by a second request.
module Umbral.Infrastructure.OperatorPasskeys

open System
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Umbral.Domains

/// A stored operator passkey. The public key is a COSE key as the library
/// returned it and is not a secret; the credential id never goes into a URL
/// (`Id` is what a form action carries).
type OperatorPasskey =
    { Id: string
      CredentialId: byte array
      PublicKey: byte array
      SignCount: uint32
      Name: string
      /// The library's transport names (`internal`, `usb`, ...), echoed in the
      /// next registration's `excludeCredentials`.
      Transports: string list
      CreatedUtc: DateTimeOffset
      LastUsedUtc: DateTimeOffset option }

/// The most passkeys the credential can hold (FR-002).
[<Literal>]
let MaxPasskeys = 10

/// A passkey's own random id: 128 bits, base64url.
let newId () : string = Ids.randomToken 16

[<Literal>]
let private Columns =
    "SELECT id, credential_id, public_key, sign_count, name, transports, created_utc, last_used_utc
     FROM operator_passkeys"

let private parseTime (text: string) =
    DateTimeOffset.Parse(text, Globalization.CultureInfo.InvariantCulture)

let private read (reader: SqliteDataReader) : OperatorPasskey =
    { Id = reader.GetString 0
      CredentialId = reader.GetFieldValue<byte array> 1
      PublicKey = reader.GetFieldValue<byte array> 2
      SignCount = uint32 (reader.GetInt64 3)
      Name = reader.GetString 4
      Transports =
        reader.GetString 5
        |> fun joined -> joined.Split(' ', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
        |> List.ofArray
      CreatedUtc = parseTime (reader.GetString 6)
      LastUsedUtc = if reader.IsDBNull 7 then None else Some(parseTime (reader.GetString 7)) }

let private query (config: Database.DbConfig) (where: string) (parameters: (string * obj) list) =
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

/// The operator's passkeys, oldest first.
let list (config: Database.DbConfig) : Task<OperatorPasskey list> =
    query config " ORDER BY created_utc, id" []

let count (config: Database.DbConfig) : Task<int> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT count(*) FROM operator_passkeys"
        let! total = command.ExecuteScalarAsync()
        return Convert.ToInt32 total
    }

let findById (config: Database.DbConfig) (id: string) : Task<OperatorPasskey option> =
    task {
        let! rows = query config " WHERE id = @id" [ "@id", id :> obj ]
        return List.tryHead rows
    }

let findByCredentialId (config: Database.DbConfig) (credentialId: byte array) : Task<OperatorPasskey option> =
    task {
        let! rows = query config " WHERE credential_id = @credentialId" [ "@credentialId", credentialId :> obj ]
        return List.tryHead rows
    }

/// Whether the operator already holds this credential id. What the library's
/// uniqueness callback asks; the `UNIQUE` column is the second line of defence.
let credentialIdExists (config: Database.DbConfig) (credentialId: byte array) : Task<bool> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT count(*) FROM operator_passkeys WHERE credential_id = @credentialId"
        command.Parameters.AddWithValue("@credentialId", credentialId) |> ignore
        let! total = command.ExecuteScalarAsync()
        return Convert.ToInt32 total > 0
    }

type AddOutcome =
    | Added
    | LimitReached
    | CredentialNotUnique

/// Adds a passkey while fewer than `MaxPasskeys` exist. The limit is in the
/// INSERT's own condition, so two simultaneous registrations cannot both see
/// "nine". `CreatedUtc` and `LastUsedUtc` of the record are not read: a new
/// passkey was created now and has never been used.
let add (config: Database.DbConfig) (clock: TimeProvider) (passkey: OperatorPasskey) : Task<AddOutcome> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "INSERT INTO operator_passkeys
                 (id, credential_id, public_key, sign_count, name, transports, created_utc, last_used_utc)
             SELECT @id, @credentialId, @publicKey, @signCount, @name, @transports, @now, NULL
             WHERE (SELECT count(*) FROM operator_passkeys) < @max"

        command.Parameters.AddWithValue("@id", passkey.Id) |> ignore
        command.Parameters.AddWithValue("@credentialId", passkey.CredentialId) |> ignore
        command.Parameters.AddWithValue("@publicKey", passkey.PublicKey) |> ignore
        command.Parameters.AddWithValue("@signCount", int64 passkey.SignCount) |> ignore
        command.Parameters.AddWithValue("@name", passkey.Name) |> ignore
        command.Parameters.AddWithValue("@transports", String.concat " " passkey.Transports) |> ignore
        command.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore
        command.Parameters.AddWithValue("@max", MaxPasskeys) |> ignore

        try
            let! changed = command.ExecuteNonQueryAsync()
            return if changed > 0 then Added else LimitReached
        with error when Umbral.Infrastructure.Passkeys.isCredentialIdConflict error ->
            return CredentialNotUnique
    }

/// After a successful sign-in or re-check: the authenticator's new signature
/// counter and the time of use.
let recordUse (config: Database.DbConfig) (clock: TimeProvider) (id: string) (newCount: uint32) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "UPDATE operator_passkeys SET sign_count = @signCount, last_used_utc = @now WHERE id = @id"

        command.Parameters.AddWithValue("@signCount", int64 newCount) |> ignore
        command.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore
        command.Parameters.AddWithValue("@id", id) |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

type RemoveOutcome =
    | Removed
    | NotFound
    /// It is the only passkey and password sign-in is off: removing it would
    /// leave no way into the admin area (FR-011).
    | LastWhilePasswordOff

/// Removes a passkey unless it is the last one while password sign-in is off.
/// The rule is in the DELETE's own condition, so counting and deleting are one
/// statement and two simultaneous removals cannot each see "two left". The
/// transaction exists to read why nothing was deleted from the same state.
let remove (config: Database.DbConfig) (id: string) : Task<RemoveOutcome> =
    task {
        use! connection = Database.openConnection config
        use transaction = connection.BeginTransaction()

        use delete = connection.CreateCommand()
        delete.Transaction <- transaction

        delete.CommandText <-
            "DELETE FROM operator_passkeys
             WHERE id = @id
               AND (COALESCE((SELECT value FROM settings WHERE key = 'operator_password_signin'), 'on') <> 'off'
                    OR (SELECT count(*) FROM operator_passkeys) > 1)"

        delete.Parameters.AddWithValue("@id", id) |> ignore
        let! changed = delete.ExecuteNonQueryAsync()

        if changed > 0 then
            do! transaction.CommitAsync()
            return Removed
        else
            use exists = connection.CreateCommand()
            exists.Transaction <- transaction
            exists.CommandText <- "SELECT count(*) FROM operator_passkeys WHERE id = @id"
            exists.Parameters.AddWithValue("@id", id) |> ignore
            let! total = exists.ExecuteScalarAsync()
            do! transaction.RollbackAsync()
            return if Convert.ToInt32 total > 0 then LastWhilePasswordOff else NotFound
    }

/// Removes every passkey, for `reset-credential --forget-passkeys` (FR-014).
/// The number removed.
let removeAll (config: Database.DbConfig) : Task<int> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "DELETE FROM operator_passkeys"
        let! changed = command.ExecuteNonQueryAsync()
        return changed
    }

// ---------------------------------------------------------------------------
// The password sign-in switch
// ---------------------------------------------------------------------------

/// Whether the password still signs the operator in. The settings row is absent
/// on every instance that never turned it off, which reads as on.
let passwordSignInEnabled (config: Database.DbConfig) : Task<bool> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT value FROM settings WHERE key = 'operator_password_signin'"
        let! value = command.ExecuteScalarAsync()
        return isNull value || string value <> "off"
    }

type SwitchOffOutcome =
    | SwitchedOff
    /// No passkey exists, so turning the password off would lock everyone out
    /// (FR-007).
    | NoPasskey

/// Turns password sign-in off, only while a passkey exists. The condition is in
/// the same statement as the write, so a passkey removed a moment earlier by
/// another request cannot leave the switch off with nothing to sign in with.
let turnPasswordSignInOff (config: Database.DbConfig) (clock: TimeProvider) : Task<SwitchOffOutcome> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "INSERT INTO settings (key, value, updated_utc)
             SELECT 'operator_password_signin', 'off', @now
             WHERE EXISTS (SELECT 1 FROM operator_passkeys)
             ON CONFLICT (key) DO UPDATE SET value = 'off', updated_utc = @now"

        command.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore
        let! changed = command.ExecuteNonQueryAsync()
        return if changed > 0 then SwitchedOff else NoPasskey
    }

let turnPasswordSignInOn (config: Database.DbConfig) (clock: TimeProvider) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "INSERT INTO settings (key, value, updated_utc) VALUES ('operator_password_signin', 'on', @now)
             ON CONFLICT (key) DO UPDATE SET value = 'on', updated_utc = @now"

        command.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

/// The user handle every operator passkey carries, minted once and kept. The
/// insert does nothing when one exists and the select reads whichever won, so
/// two first registrations agree on it. Users' ids never start with
/// `operator-`, so no user can match it (research R3).
let userHandle (config: Database.DbConfig) (clock: TimeProvider) : Task<string> =
    task {
        use! connection = Database.openConnection config

        use insert = connection.CreateCommand()

        insert.CommandText <-
            "INSERT INTO settings (key, value, updated_utc) VALUES ('operator_user_handle', @handle, @now)
             ON CONFLICT (key) DO NOTHING"

        insert.Parameters.AddWithValue("@handle", "operator-" + Ids.randomToken 16) |> ignore
        insert.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore
        let! _ = insert.ExecuteNonQueryAsync()

        use select = connection.CreateCommand()
        select.CommandText <- "SELECT value FROM settings WHERE key = 'operator_user_handle'"
        let! handle = select.ExecuteScalarAsync()
        return string handle
    }
