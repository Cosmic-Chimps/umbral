/// Reading and writing a person's passkeys, and the single-use state of a
/// ceremony in progress (specs/008-passkey-connector, data-model.md, research R3).
///
/// Only reads and writes. Whether a response is genuine is fido2-net-lib's to say
/// (`WebAuthn.fs`); whether a person may remove their last way in is decided here,
/// in the same statement that deletes, because a check made a moment earlier
/// could be outrun by a second request.
module Umbral.Infrastructure.Passkeys

open System
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Umbral.Domains

module Rules = Umbral.Domains.Passkeys

/// A stored passkey. The public key is a COSE key as the library returned it and
/// is not a secret; the credential id identifies the person's authenticator and
/// so never goes into a URL (`Id` is what URLs carry).
type Passkey =
    { /// Random, minted by `newId`; what a form action carries.
      Id: string
      UserId: string
      CredentialId: byte array
      PublicKey: byte array
      SignCount: uint32
      Nickname: string
      AaGuid: Guid option
      /// The library's transport names (`internal`, `usb`, ...), echoed in the
      /// next registration's `excludeCredentials` so the browser can skip them.
      Transports: string list
      /// As the library reported it at registration; None for a row that predates
      /// the report.
      BackupEligible: bool option
      CreatedUtc: DateTimeOffset
      LastUsedUtc: DateTimeOffset option }

/// A passkey's own random id: 128 bits, base64url.
let newId () : string = Ids.randomToken 16

[<Literal>]
let private Columns =
    "SELECT id, user_id, credential_id, public_key, sign_count, nickname, aaguid, transports,
            backup_eligible, created_utc, last_used_utc FROM passkeys"

let private parseTime (text: string) =
    DateTimeOffset.Parse(text, Globalization.CultureInfo.InvariantCulture)

let private read (reader: SqliteDataReader) : Passkey =
    { Id = reader.GetString 0
      UserId = reader.GetString 1
      CredentialId = reader.GetFieldValue<byte array> 2
      PublicKey = reader.GetFieldValue<byte array> 3
      SignCount = uint32 (reader.GetInt64 4)
      Nickname = reader.GetString 5
      AaGuid =
        if reader.IsDBNull 6 then
            None
        else
            match Guid.TryParse(reader.GetString 6) with
            | true, guid -> Some guid
            | _ -> None
      Transports =
        reader.GetString 7
        |> fun joined -> joined.Split(' ', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
        |> List.ofArray
      BackupEligible = if reader.IsDBNull 8 then None else Some(reader.GetInt32 8 = 1)
      CreatedUtc = parseTime (reader.GetString 9)
      LastUsedUtc = if reader.IsDBNull 10 then None else Some(parseTime (reader.GetString 10)) }

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

/// A user's passkeys, oldest first.
let listForUser (config: Database.DbConfig) (userId: string) : Task<Passkey list> =
    query config " WHERE user_id = @userId ORDER BY created_utc, id" [ "@userId", userId :> obj ]

let findByCredentialId (config: Database.DbConfig) (credentialId: byte array) : Task<Passkey option> =
    task {
        let! rows = query config " WHERE credential_id = @credentialId" [ "@credentialId", credentialId :> obj ]
        return List.tryHead rows
    }

let findById (config: Database.DbConfig) (id: string) : Task<Passkey option> =
    task {
        let! rows = query config " WHERE id = @id" [ "@id", id :> obj ]
        return List.tryHead rows
    }

let countForUser (config: Database.DbConfig) (userId: string) : Task<int> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT count(*) FROM passkeys WHERE user_id = @userId"
        command.Parameters.AddWithValue("@userId", userId) |> ignore
        let! count = command.ExecuteScalarAsync()
        return Convert.ToInt32 count
    }

/// Whether any user holds this credential id. What the library's
/// uniqueness callback asks; the `UNIQUE` column is the second line of defence.
let credentialIdExists (config: Database.DbConfig) (credentialId: byte array) : Task<bool> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT count(*) FROM passkeys WHERE credential_id = @credentialId"
        command.Parameters.AddWithValue("@credentialId", credentialId) |> ignore
        let! count = command.ExecuteScalarAsync()
        return Convert.ToInt32 count > 0
    }

/// SQLITE_CONSTRAINT, the primary result code for any violated constraint.
[<Literal>]
let private SqliteConstraint = 19

/// Whether `error` is the `UNIQUE` constraint on `passkeys.credential_id`
/// refusing an insert: two people (or one person twice) registering the same
/// authenticator credential at once. The caller maps it to
/// `Reason.CredentialNotUnique` and rolls the transaction back.
let isCredentialIdConflict (error: exn) : bool =
    match error with
    | :? SqliteException as sqlite ->
        sqlite.SqliteErrorCode = SqliteConstraint && sqlite.Message.Contains "credential_id"
    | _ -> false

/// Inserts a passkey inside the caller's transaction, so a registration commits
/// its user, its passkey, the invitation it consumed and its events together or
/// not at all. `CreatedUtc` and `LastUsedUtc` of the record are not read: a new
/// passkey was created now and has never been used.
let insertIn
    (connection: SqliteConnection)
    (transaction: SqliteTransaction)
    (clock: TimeProvider)
    (passkey: Passkey)
    : Task<unit> =
    task {
        use command = connection.CreateCommand()
        command.Transaction <- transaction

        command.CommandText <-
            "INSERT INTO passkeys
                 (id, user_id, credential_id, public_key, sign_count, nickname, aaguid, transports,
                  backup_eligible, created_utc, last_used_utc)
             VALUES (@id, @userId, @credentialId, @publicKey, @signCount, @nickname, @aaguid, @transports,
                     @backupEligible, @now, NULL)"

        command.Parameters.AddWithValue("@id", passkey.Id) |> ignore
        command.Parameters.AddWithValue("@userId", passkey.UserId) |> ignore
        command.Parameters.AddWithValue("@credentialId", passkey.CredentialId) |> ignore
        command.Parameters.AddWithValue("@publicKey", passkey.PublicKey) |> ignore
        command.Parameters.AddWithValue("@signCount", int64 passkey.SignCount) |> ignore
        command.Parameters.AddWithValue("@nickname", passkey.Nickname) |> ignore

        command.Parameters.AddWithValue(
            "@aaguid",
            (match passkey.AaGuid with
             | Some guid -> box (guid.ToString())
             | None -> box DBNull.Value)
        )
        |> ignore

        command.Parameters.AddWithValue("@transports", String.concat " " passkey.Transports) |> ignore

        command.Parameters.AddWithValue(
            "@backupEligible",
            (match passkey.BackupEligible with
             | Some eligible -> box (if eligible then 1 else 0)
             | None -> box DBNull.Value)
        )
        |> ignore

        command.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

/// After a successful sign-in: the authenticator's new signature counter and the
/// time of use. An authenticator that never counts reports 0 every time, and the
/// verifier accepts that (`WebAuthn.fs`); the stored value then stays 0.
let recordUse (config: Database.DbConfig) (clock: TimeProvider) (id: string) (newCount: uint32) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <- "UPDATE passkeys SET sign_count = @signCount, last_used_utc = @now WHERE id = @id"

        command.Parameters.AddWithValue("@signCount", int64 newCount) |> ignore
        command.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore
        command.Parameters.AddWithValue("@id", id) |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

/// Removes a passkey whoever owns it: the operator's removal, which may take a
/// user's last one (they can delete the user too). False when it was not there.
let remove (config: Database.DbConfig) (id: string) : Task<bool> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "DELETE FROM passkeys WHERE id = @id"
        command.Parameters.AddWithValue("@id", id) |> ignore
        let! changed = command.ExecuteNonQueryAsync()
        return changed > 0
    }

type RemoveOutcome =
    | Removed
    /// No passkey with that id belongs to that user: unknown, or someone else's.
    /// Said the same way for both, so an id cannot be probed.
    | NotFound
    /// It is the user's only passkey and they have no linked provider account:
    /// removing it would leave nobody able to sign in as them (FR-020).
    | LastWayIn

/// A person removing one of their own passkeys, unless it is their last way to
/// sign in. The rule is in the DELETE's own condition, so counting and deleting
/// are one statement and two simultaneous removals cannot each see "two left"
/// and leave none. The transaction exists to read why nothing was deleted from
/// the same state.
let removeOwnIfNotLast (config: Database.DbConfig) (userId: string) (id: string) : Task<RemoveOutcome> =
    task {
        use! connection = Database.openConnection config
        use transaction = connection.BeginTransaction()

        use delete = connection.CreateCommand()
        delete.Transaction <- transaction

        delete.CommandText <-
            "DELETE FROM passkeys
             WHERE id = @id AND user_id = @userId
               AND ((SELECT count(*) FROM passkeys WHERE user_id = @userId) > 1
                    OR EXISTS (SELECT 1 FROM linked_accounts WHERE user_id = @userId))"

        delete.Parameters.AddWithValue("@id", id) |> ignore
        delete.Parameters.AddWithValue("@userId", userId) |> ignore
        let! changed = delete.ExecuteNonQueryAsync()

        if changed > 0 then
            do! transaction.CommitAsync()
            return Removed
        else
            use owned = connection.CreateCommand()
            owned.Transaction <- transaction
            owned.CommandText <- "SELECT count(*) FROM passkeys WHERE id = @id AND user_id = @userId"
            owned.Parameters.AddWithValue("@id", id) |> ignore
            owned.Parameters.AddWithValue("@userId", userId) |> ignore
            let! count = owned.ExecuteScalarAsync()
            do! transaction.RollbackAsync()
            return if Convert.ToInt32 count > 0 then LastWayIn else NotFound
    }

// ---------------------------------------------------------------------------
// Ceremonies (research R3)
// ---------------------------------------------------------------------------

/// What a started ceremony will become when it is completed. The words are the
/// stored form.
type Purpose =
    /// A sign-in: an assertion, no one signed in yet.
    | Signin
    /// A new person creating an account in open sign-up.
    | Signup
    /// Redeeming an invitation link: a new person or an existing user.
    | Invite
    /// A signed-in person adding a passkey to their own account.
    | Add

    member this.Value =
        match this with
        | Signin -> "signin"
        | Signup -> "signup"
        | Invite -> "invitation"
        | Add -> "add"

let private parsePurpose (word: string) =
    match word with
    | "signin" -> Some Signin
    | "signup" -> Some Signup
    | "invitation" -> Some Invite
    | "add" -> Some Add
    | _ -> None

/// A ceremony in progress: the options the browser was given, kept as the
/// library serialised them so the response can be verified against exactly the
/// challenge that was issued, plus what the completion needs to know.
type Ceremony =
    { /// Random, minted by `newCeremonyId`; the page echoes it back.
      Id: string
      Purpose: Purpose
      OptionsJson: string
      /// `Add` and a user-bound `Invite`: the account the passkey joins.
      UserId: string option
      InvitationId: string option
      /// `Signup` and a new-person `Invite`: the name typed.
      DisplayName: string option
      /// Every registration: as typed, possibly empty.
      Nickname: string option
      /// `Signin` and `Signup`: the authorize request to replay afterwards.
      ReturnUrl: string option
      CreatedUtc: DateTimeOffset
      ExpiresUtc: DateTimeOffset }

/// A ceremony's random id: 128 bits, base64url.
let newCeremonyId () : string = Ids.randomToken 16

let private optional (value: string option) : obj =
    match value with
    | Some text -> text :> obj
    | None -> DBNull.Value :> obj

/// Records a ceremony that is about to be handed to a browser. `CreatedUtc` and
/// `ExpiresUtc` of the record are not read: it begins now and lives for
/// `Passkeys.CeremonyLifetime`. Rows already past their expiry are removed first,
/// so the table holds minutes of work and no job is needed to keep it small.
let startCeremony (config: Database.DbConfig) (clock: TimeProvider) (ceremony: Ceremony) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use transaction = connection.BeginTransaction()
        let now = clock.GetUtcNow()

        use prune = connection.CreateCommand()
        prune.Transaction <- transaction
        prune.CommandText <- "DELETE FROM passkey_ceremonies WHERE expires_utc <= @now"
        prune.Parameters.AddWithValue("@now", now.ToString "o") |> ignore
        let! _ = prune.ExecuteNonQueryAsync()

        use insert = connection.CreateCommand()
        insert.Transaction <- transaction

        insert.CommandText <-
            "INSERT INTO passkey_ceremonies
                 (id, purpose, options_json, user_id, invitation_id, display_name, nickname, return_url,
                  created_utc, expires_utc)
             VALUES (@id, @purpose, @options, @userId, @invitationId, @displayName, @nickname, @returnUrl,
                     @now, @expires)"

        insert.Parameters.AddWithValue("@id", ceremony.Id) |> ignore
        insert.Parameters.AddWithValue("@purpose", ceremony.Purpose.Value) |> ignore
        insert.Parameters.AddWithValue("@options", ceremony.OptionsJson) |> ignore
        insert.Parameters.AddWithValue("@userId", optional ceremony.UserId) |> ignore
        insert.Parameters.AddWithValue("@invitationId", optional ceremony.InvitationId) |> ignore
        insert.Parameters.AddWithValue("@displayName", optional ceremony.DisplayName) |> ignore
        insert.Parameters.AddWithValue("@nickname", optional ceremony.Nickname) |> ignore
        insert.Parameters.AddWithValue("@returnUrl", optional ceremony.ReturnUrl) |> ignore
        insert.Parameters.AddWithValue("@now", now.ToString "o") |> ignore
        insert.Parameters.AddWithValue("@expires", (now + Rules.CeremonyLifetime).ToString "o") |> ignore
        let! _ = insert.ExecuteNonQueryAsync()

        do! transaction.CommitAsync()
    }

/// Completes a ceremony's single use: the row is deleted and returned by one
/// statement, so of two simultaneous completions exactly one gets it, and a
/// replay of a captured response finds nothing. Unknown, already used and
/// expired are the same `None`, and the caller refuses all three the same way.
let consumeCeremony (config: Database.DbConfig) (clock: TimeProvider) (id: string) : Task<Ceremony option> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "DELETE FROM passkey_ceremonies WHERE id = @id AND expires_utc > @now
             RETURNING id, purpose, options_json, user_id, invitation_id, display_name, nickname, return_url,
                       created_utc, expires_utc"

        command.Parameters.AddWithValue("@id", id) |> ignore
        command.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore

        use! reader = command.ExecuteReaderAsync()
        let! hasRow = reader.ReadAsync()

        if not hasRow then
            return None
        else
            let text (ordinal: int) =
                if reader.IsDBNull ordinal then None else Some(reader.GetString ordinal)

            match parsePurpose (reader.GetString 1) with
            | None ->
                // A purpose this build does not know: the row is already gone,
                // which is the safe end for a ceremony nobody can complete.
                return None
            | Some purpose ->
                return
                    Some
                        { Id = reader.GetString 0
                          Purpose = purpose
                          OptionsJson = reader.GetString 2
                          UserId = text 3
                          InvitationId = text 4
                          DisplayName = text 5
                          Nickname = text 6
                          ReturnUrl = text 7
                          CreatedUtc = parseTime (reader.GetString 8)
                          ExpiresUtc = parseTime (reader.GetString 9) }
    }
