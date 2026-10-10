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
        extra_scopes            TEXT        NULL,
        is_enabled              INTEGER NOT NULL DEFAULT 1,
        -- A shared connector is offered to every app set to "all shared
        -- connectors" (FR-014). A database from schema version 1 gets this
        -- column from the ALTER in `initializeSchema`, with the same default.
        is_shared               INTEGER NOT NULL DEFAULT 1,
        created_utc             TEXT    NOT NULL,
        updated_utc             TEXT    NOT NULL
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

/// The version this build understands. Bumped when `schema` changes in a way an
/// older build could not read, so a downgrade fails loudly instead of corrupting.
[<Literal>]
let SchemaVersion = 2

let initializeSchema (config: DbConfig) : Task<unit> =
    task {
        use! connection = openConnection config
        use transaction = connection.BeginTransaction()

        use create = connection.CreateCommand()
        create.Transaction <- transaction
        create.CommandText <- schema
        let! _ = create.ExecuteNonQueryAsync()

        // The stored version decides whether the v1 -> v2 column is added:
        // SQLite has no `ADD COLUMN IF NOT EXISTS`, and the CREATE TABLE above
        // is a no-op on a database that already has `connectors`. No row means
        // a fresh database, which got the column from CREATE TABLE.
        use stored = connection.CreateCommand()
        stored.Transaction <- transaction
        stored.CommandText <- "SELECT value FROM settings WHERE key = 'schema_version'"
        let! storedVersion = stored.ExecuteScalarAsync()

        // A newer build wrote this database: carrying on would ignore columns
        // and tables it relies on. Throwing before the commit rolls back the
        // CREATE script above, so the refusal leaves the file as it was.
        match System.Int32.TryParse(string storedVersion) with
        | true, newer when newer > SchemaVersion ->
            failwith
                $"The database {config.DataSource} has schema version {newer}, but this build of Umbral only understands version {SchemaVersion}. A newer Umbral wrote it. Run that newer image again, or restore a backup taken before the upgrade. The database was not changed."
        | _ -> ()

        if string storedVersion = "1" then
            // Every existing connector becomes shared through the default and
            // no app has a choice row, so every app offers what it offered
            // before (FR-019). No data is rewritten.
            use upgrade = connection.CreateCommand()
            upgrade.Transaction <- transaction

            upgrade.CommandText <-
                "ALTER TABLE connectors ADD COLUMN is_shared INTEGER NOT NULL DEFAULT 1;
                 UPDATE settings SET value = @version, updated_utc = @now WHERE key = 'schema_version';"

            upgrade.Parameters.AddWithValue("@version", string SchemaVersion) |> ignore
            upgrade.Parameters.AddWithValue("@now", System.DateTimeOffset.UtcNow.ToString "o") |> ignore
            let! _ = upgrade.ExecuteNonQueryAsync()
            ()

        use version = connection.CreateCommand()
        version.Transaction <- transaction

        version.CommandText <-
            "INSERT INTO settings (key, value, updated_utc) VALUES ('schema_version', @version, @now)
             ON CONFLICT (key) DO NOTHING"

        version.Parameters.AddWithValue("@version", string SchemaVersion) |> ignore
        version.Parameters.AddWithValue("@now", System.DateTimeOffset.UtcNow.ToString "o") |> ignore
        let! _ = version.ExecuteNonQueryAsync()

        do! transaction.CommitAsync()
    }
