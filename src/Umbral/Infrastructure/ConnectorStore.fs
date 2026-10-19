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
open Umbral.Domains
open Umbral.Domains.Connectors
open Umbral.Domains.Passkeys

[<Literal>]
let private Columns =
    "SELECT slug, kind, display_name, client_id, client_secret_ref, client_secret_hint,
            is_enabled, is_shared, created_utc, updated_utc, issuer_url, allowed_email_domains, passkey_enrolment FROM connectors"

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
      IssuerUrl = readOptional reader 10
      IsEnabled = reader.GetInt32 6 = 1
      IsShared = reader.GetInt32 7 = 1
      AllowedEmailDomains = EmailDomains.deserialise (reader.GetString 11)
      PasskeyEnrolment =
        // A word this build does not know (a hand-edited row) reads as the safe
        // mode: nobody can enrol without an invitation.
        parseEnrolment (reader.GetString 12) |> Option.defaultValue InvitationsOnly
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

/// The connector configured for an issuer, if any. `issuer` is the normalised
/// form (`normaliseIssuer`), which is also what is stored: two spellings of one
/// address must not become two connectors.
let findByIssuer (config: Database.DbConfig) (issuer: string) =
    task {
        let! rows = query config " WHERE issuer_url = @issuer" [ "@issuer", issuer ]
        return List.tryHead rows
    }

let private issuerParameter (connector: Connector) : obj =
    match connector.IssuerUrl with
    | Some issuer -> issuer :> obj
    | None -> DBNull.Value :> obj

/// Create or update, keyed on the immutable slug. The kind is written when the
/// row is created and never touched again: it is read back from the row, not
/// worked out from the slug, because `discord-frunchy` is a Discord connector
/// only because its row says so (FR-012). The secret is written only
/// when a new one is supplied, so saving the form without retyping it does not
/// wipe the stored credential — the screen cannot show it back, so an operator
/// editing a display name has nothing to retype. The issuer is written the same
/// way as the kind — at creation, never updated — because it is part of every
/// person's identity.
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
                  is_enabled, is_shared, created_utc, updated_utc, issuer_url, allowed_email_domains,
                  passkey_enrolment)
             VALUES (@slug, @kind, @name, @clientId, @secretRef, @secretHint, @enabled, @shared, @now, @now, @issuer, @domains,
                     @enrolment)
             ON CONFLICT (slug) DO UPDATE SET
                 display_name      = @name,
                 client_id         = @clientId,
                 client_secret_ref = @secretRef,
                 client_secret_hint= @secretHint,
                 is_enabled        = @enabled,
                 is_shared         = @shared,
                 allowed_email_domains = @domains,
                 passkey_enrolment = @enrolment,
                 updated_utc       = @now"

        command.Parameters.AddWithValue("@slug", connector.Slug) |> ignore
        command.Parameters.AddWithValue("@kind", connector.Kind.Value) |> ignore
        command.Parameters.AddWithValue("@name", connector.DisplayName) |> ignore
        command.Parameters.AddWithValue("@clientId", connector.ClientId) |> ignore
        command.Parameters.AddWithValue("@secretRef", secretRef) |> ignore
        command.Parameters.AddWithValue("@secretHint", secretHint) |> ignore
        command.Parameters.AddWithValue("@enabled", (if connector.IsEnabled then 1 else 0)) |> ignore
        command.Parameters.AddWithValue("@shared", (if connector.IsShared then 1 else 0)) |> ignore
        command.Parameters.AddWithValue("@issuer", issuerParameter connector) |> ignore
        command.Parameters.AddWithValue("@domains", EmailDomains.serialise connector.AllowedEmailDomains) |> ignore
        command.Parameters.AddWithValue("@enrolment", connector.PasskeyEnrolment.Value) |> ignore
        command.Parameters.AddWithValue("@now", now) |> ignore

        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

/// How adding a connector ended. A taken slug and a taken issuer are different
/// answers because the operator fixes them differently.
type CreateResult =
    | Created
    /// The slug already belongs to a connector; nothing was written.
    | SlugTaken
    /// Another connector already holds this issuer; nothing was written. Two
    /// connectors on one issuer would share every person under two names
    /// (`ux_connectors_issuer`).
    | IssuerTaken

/// SQLITE_CONSTRAINT, the primary result code for any violated constraint.
[<Literal>]
let private SqliteConstraint = 19

/// Add a connector under a slug the operator chose (FR-012). Writes nothing and
/// says why when the slug already belongs to a connector — the slug is the
/// primary key and sits in the callback URL, so an existing one is never
/// replaced here — or when another connector already holds the issuer. The
/// caller has already checked `Ids.isValidSlug`.
///
/// `now` is written as `created_utc`. The caller takes it once and uses it for
/// the secret's name too: the name's discriminator derives from `created_utc`,
/// and a replace later re-derives the same name from the stored value
/// (specs/005 research R5).
let create
    (config: Database.DbConfig)
    (now: DateTimeOffset)
    (connector: Connector)
    (secretRef: string, secretHint: string)
    : Task<CreateResult> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "INSERT INTO connectors
                 (slug, kind, display_name, client_id, client_secret_ref, client_secret_hint,
                  is_enabled, is_shared, created_utc, updated_utc, issuer_url, allowed_email_domains,
                  passkey_enrolment)
             VALUES (@slug, @kind, @name, @clientId, @secretRef, @secretHint, @enabled, @shared, @now, @now, @issuer, @domains,
                     @enrolment)
             ON CONFLICT (slug) DO NOTHING"

        command.Parameters.AddWithValue("@slug", connector.Slug) |> ignore
        command.Parameters.AddWithValue("@kind", connector.Kind.Value) |> ignore
        command.Parameters.AddWithValue("@name", connector.DisplayName) |> ignore
        command.Parameters.AddWithValue("@clientId", connector.ClientId) |> ignore
        command.Parameters.AddWithValue("@secretRef", secretRef) |> ignore
        command.Parameters.AddWithValue("@secretHint", secretHint) |> ignore
        command.Parameters.AddWithValue("@enabled", (if connector.IsEnabled then 1 else 0)) |> ignore
        command.Parameters.AddWithValue("@shared", (if connector.IsShared then 1 else 0)) |> ignore
        command.Parameters.AddWithValue("@issuer", issuerParameter connector) |> ignore
        command.Parameters.AddWithValue("@domains", EmailDomains.serialise connector.AllowedEmailDomains) |> ignore
        command.Parameters.AddWithValue("@enrolment", connector.PasskeyEnrolment.Value) |> ignore
        command.Parameters.AddWithValue("@now", now.ToUniversalTime().ToString "o") |> ignore

        try
            let! inserted = command.ExecuteNonQueryAsync()
            return if inserted > 0 then Created else SlugTaken
        with :? SqliteException as error when error.SqliteErrorCode = SqliteConstraint && error.Message.Contains "issuer_url" ->
            // `ON CONFLICT (slug)` covers only the primary key; the unique
            // index on the issuer raises instead of being skipped.
            return IssuerTaken
    }

/// Change what the card form edits on a stored connector: display name, client
/// id, the shared setting, the allowed email domains and, when one is supplied,
/// the secret. Slug, kind and the enabled flag are left alone: the slug and kind
/// never change (FR-013) and enabling has its own action. `allowedDomains` None
/// leaves the stored list as it is (a form that does not carry the field must not
/// clear it); Some replaces it, and Some [] clears it. Returns false when no
/// connector has that slug.
let update
    (config: Database.DbConfig)
    (clock: TimeProvider)
    (slug: string)
    (displayName: string)
    (clientId: string)
    (isShared: bool)
    (allowedDomains: string list option)
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
                 allowed_email_domains = coalesce(@domains, allowed_email_domains),
                 client_secret_ref  = coalesce(@secretRef, client_secret_ref),
                 client_secret_hint = coalesce(@secretHint, client_secret_hint),
                 updated_utc        = @now
             WHERE slug = @slug"

        command.Parameters.AddWithValue("@slug", slug) |> ignore
        command.Parameters.AddWithValue("@name", displayName) |> ignore
        command.Parameters.AddWithValue("@clientId", clientId) |> ignore
        command.Parameters.AddWithValue("@shared", (if isShared then 1 else 0)) |> ignore

        match allowedDomains with
        | Some list -> command.Parameters.AddWithValue("@domains", EmailDomains.serialise list) |> ignore
        | None -> command.Parameters.AddWithValue("@domains", DBNull.Value) |> ignore

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

/// How `switchSecretRef` ended. A row that changed and a row that is gone are
/// different answers: a changed row has a secret an operator just entered (whose
/// name the move's copy shares), a gone row has nothing left that refers to the
/// copy at all.
type SwitchResult =
    | Switched
    /// The row exists but no longer holds `oldRef`.
    | Changed
    /// No connector has that slug any more.
    | Gone

/// Points a connector at a new secret reference, but only if it still points at
/// `oldRef`, and records `event` in the same transaction. The guard is the whole
/// safety of the move command (specs/005 research R8): if an operator replaced
/// the secret (a new reference) or deleted the connector while the move was
/// copying, the move's copy is stale and must not overwrite that, so no row
/// changes, no event is written, and the caller reports the connector skipped.
/// Event and change commit together, so a switch nobody can see in the log
/// cannot exist. The hint is left alone: the plaintext is the same.
let switchSecretRef
    (config: Database.DbConfig)
    (clock: TimeProvider)
    (slug: string)
    (oldRef: string)
    (newRef: string)
    (event: SecurityEvents.SecurityEvent)
    : Task<SwitchResult> =
    task {
        use! connection = Database.openConnection config
        use transaction = connection.BeginTransaction()
        use command = connection.CreateCommand()
        command.Transaction <- transaction

        command.CommandText <-
            "UPDATE connectors SET client_secret_ref = @new, updated_utc = @now
             WHERE slug = @slug AND client_secret_ref = @old"

        command.Parameters.AddWithValue("@new", newRef) |> ignore
        command.Parameters.AddWithValue("@old", oldRef) |> ignore
        command.Parameters.AddWithValue("@slug", slug) |> ignore
        command.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore

        let! changed = command.ExecuteNonQueryAsync()

        if changed > 0 then
            do! SecurityEvents.recordIn connection transaction clock event
            transaction.Commit()
            return Switched
        else
            use exists = connection.CreateCommand()
            exists.Transaction <- transaction
            exists.CommandText <- "SELECT count(*) FROM connectors WHERE slug = @slug"
            exists.Parameters.AddWithValue("@slug", slug) |> ignore
            let! count = exists.ExecuteScalarAsync()
            transaction.Rollback()
            return if Convert.ToInt32 count > 0 then Changed else Gone
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

/// Changes who may enrol through the passkey connector (specs/008). Its own
/// statement, as `setEnabled` is, so the card's save cannot touch a secret or a
/// name that the passkey kind does not have. Returns false when no connector has
/// that slug.
let setEnrolment
    (config: Database.DbConfig)
    (clock: TimeProvider)
    (slug: string)
    (enrolment: PasskeyEnrolment)
    : Task<bool> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "UPDATE connectors SET passkey_enrolment = @enrolment, updated_utc = @now WHERE slug = @slug"

        command.Parameters.AddWithValue("@enrolment", enrolment.Value) |> ignore
        command.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore
        command.Parameters.AddWithValue("@slug", slug) |> ignore
        let! changed = command.ExecuteNonQueryAsync()
        return changed > 0
    }

/// Deletes the row and hands back the secret reference it held, read by the
/// same statement that removed it: the reference to clean up is exactly the one
/// that was deleted, not one read a moment earlier that a move or a replace
/// may have changed since. `None` when no connector has that slug.
let delete (config: Database.DbConfig) (slug: string) : Task<string option> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "DELETE FROM connectors WHERE slug = @slug RETURNING client_secret_ref"
        command.Parameters.AddWithValue("@slug", slug) |> ignore
        let! value = command.ExecuteScalarAsync()
        return (match value with | :? string as reference -> Some reference | _ -> None)
    }

// ---------------------------------------------------------------------------
// The secret lease
// ---------------------------------------------------------------------------

/// How long a lease holds. It covers the move's four bounded calls to a store
/// (read, write, read back, delete, each at most 10 seconds) with room to spare,
/// and is also how long a lease left by a process that died blocks a connector.
let leaseLength = TimeSpan.FromMinutes 2.

let private leaseKey (slug: string) = $"secret-lease:{slug}"

/// Takes the lease on a connector's secret for `holder`, if nobody holds it or
/// the holder's `length` has run out. Returns whether it was taken.
///
/// Two writers of one connector's secret (an operator replacing it in the admin
/// screen, the move command copying it to another store) write the same derived
/// name, and `switchSecretRef`'s guard protects the row but not the store: a
/// move that read the old value, an operator who then replaced it, and a move
/// that then wrote would leave the old value as the manager's latest version
/// while the row says the new one. Whoever holds the lease is the only one
/// writing that name, so the order of the writes cannot invert.
///
/// It is a row in `settings` (key `secret-lease:{slug}`, value the holder, time
/// the instant it was taken), so it needs no schema of its own and is shared by
/// the web process and the command, which are two processes on one database.
/// The statement is one atomic upsert whose update only applies to an expired
/// lease, so of two simultaneous takers exactly one changes a row.
let tryLease
    (config: Database.DbConfig)
    (clock: TimeProvider)
    (slug: string)
    (holder: string)
    (length: TimeSpan)
    : Task<bool> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "INSERT INTO settings (key, value, updated_utc) VALUES (@key, @holder, @now)
             ON CONFLICT (key) DO UPDATE SET value = excluded.value, updated_utc = excluded.updated_utc
             WHERE settings.updated_utc < @expiredBefore"

        let now = clock.GetUtcNow()
        command.Parameters.AddWithValue("@key", leaseKey slug) |> ignore
        command.Parameters.AddWithValue("@holder", holder) |> ignore
        command.Parameters.AddWithValue("@now", now.ToString "o") |> ignore
        command.Parameters.AddWithValue("@expiredBefore", (now - length).ToString "o") |> ignore
        let! changed = command.ExecuteNonQueryAsync()
        return changed > 0
    }

/// Gives the lease back, but only if `holder` still holds it: a lease that
/// expired and was taken by someone else is not the releaser's to delete.
let releaseLease (config: Database.DbConfig) (slug: string) (holder: string) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "DELETE FROM settings WHERE key = @key AND value = @holder"
        command.Parameters.AddWithValue("@key", leaseKey slug) |> ignore
        command.Parameters.AddWithValue("@holder", holder) |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

/// Runs `work` under the lease on `slug`; `None`, without running it, when
/// somebody else holds the lease. The lease is released however `work` ends. The
/// holder is a fresh GUID per acquisition, so a release can only ever remove
/// what this acquisition took.
let withLease
    (config: Database.DbConfig)
    (clock: TimeProvider)
    (slug: string)
    (work: unit -> Task<'a>)
    : Task<'a option> =
    task {
        let holder = Guid.NewGuid().ToString "N"
        let! taken = tryLease config clock slug holder leaseLength

        if not taken then
            return None
        else
            // Released before the outcome is looked at, and awaited: a lease
            // that is still there when the caller's next line runs would make
            // the next attempt find it held.
            let! outcome =
                task {
                    try
                        let! result = work ()
                        return Ok result
                    with ex ->
                        return Error ex
                }

            do! releaseLease config slug holder

            match outcome with
            | Ok result -> return Some result
            | Error ex ->
                Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw()
                return None
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
