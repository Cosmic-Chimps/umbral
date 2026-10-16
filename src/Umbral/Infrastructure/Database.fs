/// SQLite connections and the schema Umbral owns.
///
/// One file holds two lanes that never join: these tables, written with
/// hand-written SQL, and OpenIddict's four, owned by EF Core and migrated from
/// `src/Umbral.Migrations`. The only thing crossing between them is the `sub`
/// claim — `users.id` written into a token as an opaque string, with no foreign
/// key, which is what lets the two evolve independently.
module Umbral.Infrastructure.Database

open System.Threading.Tasks
open Microsoft.Data.Sqlite

type DbConfig = { DataSource: string }

let connectionString (config: DbConfig) =
    SqliteConnectionStringBuilder(DataSource = config.DataSource, Pooling = true)
        .ToString()

/// Open a connection with the pragmas Umbral needs.
///
/// WAL is not optional. The default journal mode serialises readers against the
/// writer, and this process serves an admin UI, a login flow and token traffic
/// at once — without WAL that is `SQLITE_BUSY` under the mildest concurrency.
/// `busy_timeout` covers the brief exclusive moments WAL still has.
let openConnection (config: DbConfig) : Task<SqliteConnection> =
    task {
        let connection = new SqliteConnection(connectionString config)
        do! connection.OpenAsync()

        use pragma = connection.CreateCommand()
        pragma.CommandText <- "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;"
        let! _ = pragma.ExecuteNonQueryAsync()

        return connection
    }

/// The schema Umbral owns, from specs/001-umbral-mvp/data-model.md.
///
/// Idempotent and applied at every start, following the reference project's
/// convention. Deliberately no migration framework on this side: these are flat
/// tables with no relationships worth one, and the appliance has a single
/// writer. A schema change edits this string and data-model.md together.
[<Literal>]
let private schema =
    """
    CREATE TABLE IF NOT EXISTS users (
        id               TEXT    NOT NULL PRIMARY KEY,
        display_name     TEXT    NOT NULL,
        email            TEXT        NULL,
        email_verified   INTEGER NOT NULL DEFAULT 0,
        avatar_url       TEXT        NULL,
        is_enabled       INTEGER NOT NULL DEFAULT 1,
        first_seen_utc   TEXT    NOT NULL,
        last_seen_utc    TEXT    NOT NULL
    );

    -- The lookup FR-012 performs on every first sign-in from a new provider
    -- account. Partial, because an unverified email must never match: linking
    -- on one is the account-takeover vector in every social-login system.
    CREATE INDEX IF NOT EXISTS ix_users_verified_email
        ON users (email) WHERE email_verified = 1;

    CREATE TABLE IF NOT EXISTS linked_accounts (
        provider_kind           TEXT    NOT NULL,
        provider_subject        TEXT    NOT NULL,
        user_id                 TEXT    NOT NULL,
        asserted_email          TEXT        NULL,
        asserted_email_verified INTEGER NOT NULL DEFAULT 0,
        linked_utc              TEXT    NOT NULL,
        last_used_utc           TEXT    NOT NULL,
        PRIMARY KEY (provider_kind, provider_subject),
        FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );

    CREATE INDEX IF NOT EXISTS ix_linked_accounts_user ON linked_accounts (user_id);

    CREATE TABLE IF NOT EXISTS connectors (
        slug                    TEXT    NOT NULL PRIMARY KEY,
        kind                    TEXT    NOT NULL,
        display_name            TEXT    NOT NULL,
        client_id               TEXT    NOT NULL,
        -- Not a ciphertext column. `client_secret_ref` is "<store>:<locator>",
        -- opaque to everything but the ISecretStore that wrote it: for the
        -- local store the Data-Protection ciphertext, for an external manager
        -- an ARN or URI. Shaped this way now so adding a backend later is
        -- additive rather than a migration on every deployed instance.
        client_secret_ref       TEXT    NOT NULL,
        client_secret_hint      TEXT    NOT NULL,
        is_enabled              INTEGER NOT NULL DEFAULT 1,
        -- A shared connector is offered to every app set to "all shared
        -- connectors" (FR-014). A database from schema version 1 gets this
        -- column from `upgradeSteps`, with the same default.
        is_shared               INTEGER NOT NULL DEFAULT 1,
        created_utc             TEXT    NOT NULL,
        updated_utc             TEXT    NOT NULL,
        -- Only for kind `oidc`: the normalised issuer URL, which is part of
        -- every person's identity there (specs/002 data-model). NULL for every
        -- other kind. A database from before schema version 3 gets this column
        -- from `upgradeSteps`; its unique index is `issuerIndex`, below.
        issuer_url              TEXT        NULL,
        -- The verified email domains allowed to sign in through this connector
        -- (specs/006), normalised ASCII joined by one space. Empty: the
        -- instance list applies, if there is one. A database from before schema
        -- version 5 gets this column from `upgradeSteps`.
        allowed_email_domains   TEXT    NOT NULL DEFAULT ''
    );

    -- A marker: one row per app limited to a chosen set. No row means "all
    -- shared connectors". Separate from the set so an app stays limited when
    -- its set empties (FR-011). `client_id` is an OpenIddict application's id,
    -- with no foreign key: OpenIddict's tables belong to its EF migrations, so
    -- AppDelete removes this row itself.
    CREATE TABLE IF NOT EXISTS app_connector_choices (
        client_id TEXT NOT NULL PRIMARY KEY
    );

    -- The chosen set. Deleting a connector removes it from every set through
    -- the cascade and leaves the marker, so the app stays limited (FR-011).
    CREATE TABLE IF NOT EXISTS app_connectors (
        client_id      TEXT NOT NULL REFERENCES app_connector_choices (client_id) ON DELETE CASCADE,
        connector_slug TEXT NOT NULL REFERENCES connectors (slug) ON DELETE CASCADE,
        PRIMARY KEY (client_id, connector_slug)
    );

    -- The lookup the FR-010 warning performs when a connector is about to be
    -- disabled or deleted.
    CREATE INDEX IF NOT EXISTS ix_app_connectors_slug ON app_connectors (connector_slug);

    -- An app's sign-in branding (spec 006): its accent colour and its logo, one
    -- row while either is set. The logo is the rewritten PNG, never the upload,
    -- stored here so the image and its row change in one transaction. The accent
    -- is six lowercase hex digits, no `#`, and `logo_sha256` is the logo's
    -- address, hence unique. No foreign key, like `app_connector_choices`:
    -- `client_id` is an OpenIddict application's id and AppDelete removes the
    -- row itself.
    CREATE TABLE IF NOT EXISTS app_branding (
        client_id     TEXT PRIMARY KEY,
        accent_color  TEXT NULL
            CHECK (accent_color IS NULL
                   OR (length(accent_color) = 6 AND accent_color NOT GLOB '*[^0-9a-f]*')),
        logo_png      BLOB NULL,
        logo_sha256   TEXT NULL UNIQUE,
        logo_width    INTEGER NULL,
        logo_height   INTEGER NULL,
        updated_at    TEXT NOT NULL,
        CHECK ((logo_png IS NULL) = (logo_sha256 IS NULL)),
        CHECK ((logo_png IS NULL) = (logo_width IS NULL) AND (logo_png IS NULL) = (logo_height IS NULL))
    );

    CREATE TABLE IF NOT EXISTS settings (
        key         TEXT NOT NULL PRIMARY KEY,
        value       TEXT NOT NULL,
        updated_utc TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS security_events (
        id             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
        occurred_utc   TEXT    NOT NULL,
        event_type     TEXT    NOT NULL,
        outcome        TEXT    NOT NULL,
        actor          TEXT    NOT NULL,
        user_id        TEXT        NULL,
        connector_slug TEXT        NULL,
        client_id      TEXT        NULL,
        remote_ip      TEXT        NULL,
        detail         TEXT        NULL
    );

    CREATE INDEX IF NOT EXISTS ix_security_events_time ON security_events (occurred_utc DESC);
    """

/// The unique index on the issuer, kept out of `schema`: that script also runs
/// against a database whose `connectors` table predates the column, where the
/// CREATE INDEX would fail before `upgradeSteps` had added it. Run after them,
/// on every start, and a no-op once it exists. Partial because every
/// non-`oidc` connector has a NULL issuer. Two connectors on one issuer would
/// share every person under two names, which is confusion with no use.
[<Literal>]
let private issuerIndex =
    "CREATE UNIQUE INDEX IF NOT EXISTS ux_connectors_issuer
         ON connectors (issuer_url) WHERE issuer_url IS NOT NULL"

/// The version this build understands. Bumped when `schema` changes in a way an
/// older build could not read, so a downgrade fails loudly instead of corrupting:
/// `initializeSchema` refuses a database stamped higher than this.
[<Literal>]
let SchemaVersion = 5

/// Spec 003. Every existing connector becomes shared through the column
/// default and no app has a choice row, so every app offers what it offered
/// before (FR-019). No data is rewritten.
[<Literal>]
let private UpgradeToVersion2 =
    "ALTER TABLE connectors ADD COLUMN is_shared INTEGER NOT NULL DEFAULT 1;"

/// Spec 002. NULL for every connector that exists today: an OpenID Connect
/// connector is the only kind with an issuer, and none can predate it. The
/// unique index follows in `issuerIndex`.
[<Literal>]
let private UpgradeToVersion3 = "ALTER TABLE connectors ADD COLUMN issuer_url TEXT NULL;"

/// Spec 006. The same statement `schema` runs, so an existing database gets the
/// table and a fresh one needs no step. Empty to begin with: no app has
/// branding, so every page looks as it did. No data is rewritten.
[<Literal>]
let private UpgradeToVersion4 =
    "CREATE TABLE IF NOT EXISTS app_branding (
         client_id     TEXT PRIMARY KEY,
         accent_color  TEXT NULL
             CHECK (accent_color IS NULL
                    OR (length(accent_color) = 6 AND accent_color NOT GLOB '*[^0-9a-f]*')),
         logo_png      BLOB NULL,
         logo_sha256   TEXT NULL UNIQUE,
         logo_width    INTEGER NULL,
         logo_height   INTEGER NULL,
         updated_at    TEXT NOT NULL,
         CHECK ((logo_png IS NULL) = (logo_sha256 IS NULL)),
         CHECK ((logo_png IS NULL) = (logo_width IS NULL) AND (logo_png IS NULL) = (logo_height IS NULL))
     );"

/// Spec 006 (allowed email domains). Empty for every connector that exists today, which means "use the
/// instance list", and an instance has none until the operator sets one: nothing
/// about who can sign in changes on upgrade.
[<Literal>]
let private UpgradeToVersion5 =
    "ALTER TABLE connectors ADD COLUMN allowed_email_domains TEXT NOT NULL DEFAULT '';"

/// What takes a database from the version before to the version named. Applied
/// in order, inside `initializeSchema`'s transaction, to a database stamped
/// lower than `SchemaVersion`; a fresh database skips them, because `schema`
/// already builds the newest shape and `initializeSchema` stamps it directly.
///
/// Append-only: a step already shipped is never edited, because deployed
/// instances have run it and will not run it again. The next change adds a
/// step N+1 here and bumps `SchemaVersion`.
///
/// Each step names its SQL as a `[<Literal>]` and sets it on the command
/// itself, which is the shape MetaTests accepts: SQL is a literal or a literal
/// constant, never a value that arrived by another route.
let private upgradeSteps: (int * (SqliteCommand -> unit)) list =
    [ 2, (fun command -> command.CommandText <- UpgradeToVersion2)
      3, (fun command -> command.CommandText <- UpgradeToVersion3)
      4, (fun command -> command.CommandText <- UpgradeToVersion4)
      5, (fun command -> command.CommandText <- UpgradeToVersion5) ]

let initializeSchema (config: DbConfig) : Task<unit> =
    task {
        use! connection = openConnection config
        use transaction = connection.BeginTransaction()

        use create = connection.CreateCommand()
        create.Transaction <- transaction
        create.CommandText <- schema
        let! _ = create.ExecuteNonQueryAsync()

        // The stored version decides which steps run: SQLite has no
        // `ADD COLUMN IF NOT EXISTS`, and the CREATE TABLE above is a no-op on a
        // database that already has `connectors`. No row means a fresh
        // database, which got every column from CREATE TABLE.
        use stored = connection.CreateCommand()
        stored.Transaction <- transaction
        stored.CommandText <- "SELECT value FROM settings WHERE key = 'schema_version'"
        let! storedValue = stored.ExecuteScalarAsync()

        let storedVersion =
            match storedValue with
            | null -> SchemaVersion
            | value ->
                match System.Int32.TryParse(string value) with
                | true, version -> version
                | _ -> failwith $"The database's schema version is '{value}', which is not a number. Restore a backup of the data directory."

        // Throws before the transaction commits, so nothing of this start is
        // kept. An older build that read on regardless would write rows a newer
        // one cannot interpret.
        if storedVersion > SchemaVersion then
            failwith (
                $"This database is at schema version {storedVersion}, but this build of Umbral only understands "
                + $"up to version {SchemaVersion}. It was written by a newer Umbral. Run the newer image, or restore "
                + "the backup of the data directory taken before the upgrade."
            )

        for version, describe in upgradeSteps do
            if version > storedVersion then
                use step = connection.CreateCommand()
                step.Transaction <- transaction
                describe step
                let! _ = step.ExecuteNonQueryAsync()
                ()

        use index = connection.CreateCommand()
        index.Transaction <- transaction
        index.CommandText <- issuerIndex
        let! _ = index.ExecuteNonQueryAsync()

        use version = connection.CreateCommand()
        version.Transaction <- transaction

        // A fresh database has no row yet and is stamped here; an upgraded one
        // has its row moved up. Nothing is written when the version is current.
        version.CommandText <-
            "INSERT INTO settings (key, value, updated_utc) VALUES ('schema_version', @version, @now)
             ON CONFLICT (key) DO UPDATE SET value = excluded.value, updated_utc = excluded.updated_utc
             WHERE CAST(settings.value AS INTEGER) < CAST(excluded.value AS INTEGER)"

        version.Parameters.AddWithValue("@version", string SchemaVersion) |> ignore
        version.Parameters.AddWithValue("@now", System.DateTimeOffset.UtcNow.ToString "o") |> ignore
        let! _ = version.ExecuteNonQueryAsync()

        do! transaction.CommitAsync()
    }
