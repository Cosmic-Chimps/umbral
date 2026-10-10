module Umbral.Tests.DatabaseTests

open System
open System.IO
open System.Threading.Tasks
open Xunit
open Umbral.Infrastructure.Database

let private tempDb () =
    Path.Combine(Path.GetTempPath(), "umbral-" + Guid.NewGuid().ToString "N" + ".db")

let private withDb (body: DbConfig -> Task<unit>) =
    task {
        let path = tempDb ()
        let config = { DataSource = path }

        try
            do! body config
        finally
            // Pooling keeps the file handle open; without this the delete fails
            // on Windows and leaves temp files behind everywhere else.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            for suffix in [ ""; "-wal"; "-shm" ] do
                if File.Exists(path + suffix) then
                    File.Delete(path + suffix)
    }

let private scalar (config: DbConfig) (sql: string) =
    task {
        use! connection = openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- sql
        let! value = command.ExecuteScalarAsync()
        return string value
    }

[<Fact>]
let ``initializeSchema creates every table`` () =
    withDb (fun config ->
        task {
            do! initializeSchema config

            let! tables =
                scalar
                    config
                    "SELECT group_concat(name, ',') FROM (SELECT name FROM sqlite_master WHERE type='table' ORDER BY name)"

            for expected in
                [ "users"; "linked_accounts"; "connectors"; "settings"; "security_events" ] do
                Assert.Contains(expected, tables)
        })

[<Fact>]
let ``initializeSchema is idempotent`` () =
    withDb (fun config ->
        task {
            // It runs at every start, so running it twice must be a no-op
            // rather than an error. This is the no-migration-framework bargain.
            do! initializeSchema config
            do! initializeSchema config

            let! count = scalar config "SELECT count(*) FROM settings WHERE key = 'schema_version'"
            Assert.Equal("1", count)
        })

[<Fact>]
let ``journal mode is WAL`` () =
    withDb (fun config ->
        task {
            do! initializeSchema config
            let! mode = scalar config "PRAGMA journal_mode"
            // Without WAL, readers serialise against the writer and the admin
            // UI plus token traffic produce SQLITE_BUSY under mild concurrency.
            Assert.Equal("wal", mode.ToLowerInvariant())
        })

[<Fact>]
let ``foreign keys are enforced`` () =
    withDb (fun config ->
        task {
            do! initializeSchema config
            let! enabled = scalar config "PRAGMA foreign_keys"
            Assert.Equal("1", enabled)
        })

[<Fact>]
let ``deleting a user cascades to its linked accounts`` () =
    withDb (fun config ->
        task {
            do! initializeSchema config
            use! connection = openConnection config

            use seed = connection.CreateCommand()

            seed.CommandText <-
                "INSERT INTO users (id, display_name, email_verified, is_enabled, first_seen_utc, last_seen_utc)
                 VALUES ('u1', 'Sam', 1, 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                 INSERT INTO linked_accounts
                     (provider_kind, provider_subject, user_id, asserted_email_verified, linked_utc, last_used_utc)
                 VALUES ('discord', '123', 'u1', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');"

            let! _ = seed.ExecuteNonQueryAsync()

            use delete = connection.CreateCommand()
            delete.CommandText <- "DELETE FROM users WHERE id = 'u1'"
            let! _ = delete.ExecuteNonQueryAsync()

            // FR-021.4: deleting a user removes its linked provider accounts.
            let! remaining = scalar config "SELECT count(*) FROM linked_accounts"
            Assert.Equal("0", remaining)
        })

[<Fact>]
let ``a provider account cannot be linked to two users`` () =
    withDb (fun config ->
        task {
            do! initializeSchema config
            use! connection = openConnection config

            use seed = connection.CreateCommand()

            seed.CommandText <-
                "INSERT INTO users (id, display_name, email_verified, is_enabled, first_seen_utc, last_seen_utc)
                 VALUES ('u1', 'Sam', 1, 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
                        ('u2', 'Alex', 1, 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                 INSERT INTO linked_accounts
                     (provider_kind, provider_subject, user_id, asserted_email_verified, linked_utc, last_used_utc)
                 VALUES ('discord', '123', 'u1', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');"

            let! _ = seed.ExecuteNonQueryAsync()

            use conflict = connection.CreateCommand()

            conflict.CommandText <-
                "INSERT INTO linked_accounts
                     (provider_kind, provider_subject, user_id, asserted_email_verified, linked_utc, last_used_utc)
                 VALUES ('discord', '123', 'u2', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')"

            // The primary key is the defence against a race creating two users
            // for one provider account (T064 covers the application side).
            let! ex =
                Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(fun () ->
                    conflict.ExecuteNonQueryAsync() :> Task)

            Assert.Contains("UNIQUE", ex.Message, StringComparison.OrdinalIgnoreCase)
        })

[<Fact>]
let ``the verified email index does not cover unverified rows`` () =
    withDb (fun config ->
        task {
            do! initializeSchema config

            let! sql =
                scalar config "SELECT sql FROM sqlite_master WHERE name = 'ix_users_verified_email'"

            // Linking on an unverified email is the account-takeover vector.
            // The index being partial is what makes the safe query the easy one.
            Assert.Contains("WHERE email_verified = 1", sql)
        })

// ---------------------------------------------------------------------------
// Schema version 2 (feature 003): the `is_shared` column and the two choice
// tables, and the upgrade from a version 1 database.
// ---------------------------------------------------------------------------

let private exec (config: DbConfig) (sql: string) =
    task {
        use! connection = openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- sql
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

/// A database as version 1 left it: `connectors` has no `is_shared`, the choice
/// tables do not exist, and the stored schema version is 1. Written by hand
/// rather than by calling `initializeSchema`, which builds the newest version.
let private seedVersionOne (config: DbConfig) =
    exec
        config
        "CREATE TABLE connectors (
             slug                    TEXT    NOT NULL PRIMARY KEY,
             kind                    TEXT    NOT NULL,
             display_name            TEXT    NOT NULL,
             client_id               TEXT    NOT NULL,
             client_secret_ref       TEXT    NOT NULL,
             client_secret_hint      TEXT    NOT NULL,
             extra_scopes            TEXT        NULL,
             is_enabled              INTEGER NOT NULL DEFAULT 1,
             created_utc             TEXT    NOT NULL,
             updated_utc             TEXT    NOT NULL
         );
         CREATE TABLE settings (
             key         TEXT NOT NULL PRIMARY KEY,
             value       TEXT NOT NULL,
             updated_utc TEXT NOT NULL
         );
         INSERT INTO connectors
             (slug, kind, display_name, client_id, client_secret_ref, client_secret_hint, is_enabled, created_utc, updated_utc)
         VALUES
             ('discord', 'discord', 'Discord', 'd-id', 'local:x', 'abcd', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
             ('github',  'github',  'GitHub',  'g-id', 'local:y', 'efgh', 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
         INSERT INTO settings (key, value, updated_utc) VALUES ('schema_version', '1', '2026-01-01T00:00:00Z');"

[<Fact>]
let ``a version 1 database comes up as the current version with every connector shared`` () =
    withDb (fun config ->
        task {
            do! seedVersionOne config
            do! initializeSchema config

            // Both steps ran in order, 1 -> 2 (is_shared) and 2 -> 3 (issuer_url).
            let! version = scalar config "SELECT value FROM settings WHERE key = 'schema_version'"
            Assert.Equal(string SchemaVersion, version)
            Assert.Equal("3", version)

            let! issuers = scalar config "SELECT count(*) FROM connectors WHERE issuer_url IS NULL"
            Assert.Equal("2", issuers)

            // FR-019: the column default makes every existing connector shared,
            // including a disabled one, and nothing else about them changed.
            let! shared = scalar config "SELECT count(*) FROM connectors WHERE is_shared = 1"
            Assert.Equal("2", shared)

            let! disabled = scalar config "SELECT is_enabled FROM connectors WHERE slug = 'github'"
            Assert.Equal("0", disabled)

            let! clientId = scalar config "SELECT client_id FROM connectors WHERE slug = 'discord'"
            Assert.Equal("d-id", clientId)

            // No choice rows: every existing app stays on "all shared connectors".
            let! choices = scalar config "SELECT count(*) FROM app_connector_choices"
            Assert.Equal("0", choices)

            let! members = scalar config "SELECT count(*) FROM app_connectors"
            Assert.Equal("0", members)
        })

[<Fact>]
let ``a fresh database is stamped with the current version and has every column and both choice tables`` () =
    withDb (fun config ->
        task {
            do! initializeSchema config

            let! version = scalar config "SELECT value FROM settings WHERE key = 'schema_version'"
            Assert.Equal("3", version)

            let! columns = scalar config "SELECT group_concat(name, ',') FROM pragma_table_info('connectors')"
            Assert.Contains("is_shared", columns)
            Assert.Contains("issuer_url", columns)

            let! issuerIndex = scalar config "SELECT sql FROM sqlite_master WHERE name = 'ux_connectors_issuer'"
            Assert.Contains("UNIQUE", issuerIndex)
            Assert.Contains("WHERE issuer_url IS NOT NULL", issuerIndex)

            let! tables =
                scalar
                    config
                    "SELECT group_concat(name, ',') FROM (SELECT name FROM sqlite_master WHERE type='table' ORDER BY name)"

            Assert.Contains("app_connector_choices", tables)
            Assert.Contains("app_connectors", tables)

            let! index = scalar config "SELECT count(*) FROM sqlite_master WHERE name = 'ix_app_connectors_slug'"
            Assert.Equal("1", index)
        })

[<Fact>]
let ``a second start after the upgrade changes nothing`` () =
    withDb (fun config ->
        task {
            do! seedVersionOne config
            do! initializeSchema config

            let! first = scalar config "SELECT value || '|' || updated_utc FROM settings WHERE key = 'schema_version'"
            let! connectors = scalar config "SELECT group_concat(slug || is_shared || updated_utc) FROM connectors"

            // Running the ALTER again would throw "duplicate column name".
            do! initializeSchema config

            let! second = scalar config "SELECT value || '|' || updated_utc FROM settings WHERE key = 'schema_version'"
            let! after = scalar config "SELECT group_concat(slug || is_shared || updated_utc) FROM connectors"
            let! rows = scalar config "SELECT count(*) FROM settings WHERE key = 'schema_version'"

            Assert.Equal(first, second)
            Assert.Equal(connectors, after)
            Assert.Equal("1", rows)
        })

[<Fact>]
let ``deleting a connector removes it from every chosen set and keeps the app limited`` () =
    withDb (fun config ->
        task {
            do! initializeSchema config

            do!
                exec
                    config
                    "INSERT INTO connectors
                         (slug, kind, display_name, client_id, client_secret_ref, client_secret_hint, created_utc, updated_utc)
                     VALUES ('discord', 'discord', 'Discord', 'id', 'ref', 'hint', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                     INSERT INTO app_connector_choices (client_id) VALUES ('app-1');
                     INSERT INTO app_connectors (client_id, connector_slug) VALUES ('app-1', 'discord');"

            do! exec config "DELETE FROM connectors WHERE slug = 'discord'"

            // FR-011: the set empties through the cascade, the marker stays.
            let! members = scalar config "SELECT count(*) FROM app_connectors"
            Assert.Equal("0", members)

            let! markers = scalar config "SELECT count(*) FROM app_connector_choices"
            Assert.Equal("1", markers)
        })

// ---------------------------------------------------------------------------
// Schema version 3 (feature 002): `issuer_url` and its unique index, the first
// step chained after 003's, and the refusal of a database newer than the build.
// ---------------------------------------------------------------------------

/// A database as version 2 (spec 003) left it, with one row in every table the
/// upgrade must leave alone: a connector, a user and a linked account.
let private seedVersionTwo (config: DbConfig) =
    exec
        config
        "CREATE TABLE connectors (
             slug                    TEXT    NOT NULL PRIMARY KEY,
             kind                    TEXT    NOT NULL,
             display_name            TEXT    NOT NULL,
             client_id               TEXT    NOT NULL,
             client_secret_ref       TEXT    NOT NULL,
             client_secret_hint      TEXT    NOT NULL,
             extra_scopes            TEXT        NULL,
             is_enabled              INTEGER NOT NULL DEFAULT 1,
             is_shared               INTEGER NOT NULL DEFAULT 1,
             created_utc             TEXT    NOT NULL,
             updated_utc             TEXT    NOT NULL
         );
         CREATE TABLE users (
             id               TEXT    NOT NULL PRIMARY KEY,
             display_name     TEXT    NOT NULL,
             email            TEXT        NULL,
             email_verified   INTEGER NOT NULL DEFAULT 0,
             avatar_url       TEXT        NULL,
             is_enabled       INTEGER NOT NULL DEFAULT 1,
             first_seen_utc   TEXT    NOT NULL,
             last_seen_utc    TEXT    NOT NULL
         );
         CREATE TABLE linked_accounts (
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
         CREATE TABLE settings (
             key         TEXT NOT NULL PRIMARY KEY,
             value       TEXT NOT NULL,
             updated_utc TEXT NOT NULL
         );
         INSERT INTO connectors
             (slug, kind, display_name, client_id, client_secret_ref, client_secret_hint, is_enabled, is_shared, created_utc, updated_utc)
         VALUES ('discord', 'discord', 'Discord', 'd-id', 'local:x', 'abcd', 1, 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
         INSERT INTO users (id, display_name, email, email_verified, first_seen_utc, last_seen_utc)
         VALUES ('user-1', 'Jane', 'jane@example.com', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
         INSERT INTO linked_accounts (provider_kind, provider_subject, user_id, linked_utc, last_used_utc)
         VALUES ('discord', '1234', 'user-1', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
         INSERT INTO settings (key, value, updated_utc) VALUES ('schema_version', '2', '2026-01-01T00:00:00Z');"

/// The message of an exception a test expects. `Record.ExceptionAsync` says
/// `exn | null`, and `Assert.NotNull` does not narrow it for the compiler.
let private messageOf (error: exn | null) =
    match error with
    | null -> failwith "expected an exception, got none"
    | error -> error.Message

let private insertOidc (config: DbConfig) (slug: string) (issuer: string) =
    task {
        use! connection = openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "INSERT INTO connectors
                 (slug, kind, display_name, client_id, client_secret_ref, client_secret_hint, created_utc, updated_utc, issuer_url)
             VALUES (@slug, 'oidc', 'Home', 'id', 'ref', 'hint', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z', @issuer)"

        command.Parameters.AddWithValue("@slug", slug) |> ignore
        command.Parameters.AddWithValue("@issuer", issuer) |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

[<Fact>]
let ``a version 2 database upgrades in place with its rows intact and accepts an oidc connector`` () =
    withDb (fun config ->
        task {
            do! seedVersionTwo config
            do! initializeSchema config

            let! version = scalar config "SELECT value FROM settings WHERE key = 'schema_version'"
            Assert.Equal("3", version)

            // Nothing that was there changed.
            let! connector = scalar config "SELECT client_id || '|' || is_shared || '|' || ifnull(issuer_url, 'none') FROM connectors WHERE slug = 'discord'"
            Assert.Equal("d-id|0|none", connector)

            let! user = scalar config "SELECT display_name || '|' || email FROM users WHERE id = 'user-1'"
            Assert.Equal("Jane|jane@example.com", user)

            let! linked = scalar config "SELECT user_id FROM linked_accounts WHERE provider_kind = 'discord' AND provider_subject = '1234'"
            Assert.Equal("user-1", linked)

            // And the new shape works.
            do! insertOidc config "home" "https://auth.example.com/application/o/umbral"

            let! issuer = scalar config "SELECT issuer_url FROM connectors WHERE slug = 'home'"
            Assert.Equal("https://auth.example.com/application/o/umbral", issuer)
        })

[<Fact>]
let ``two connectors cannot hold the same issuer, but any number can hold none`` () =
    withDb (fun config ->
        task {
            do! initializeSchema config
            do! insertOidc config "home" "https://auth.example.com/o/umbral"

            // The partial unique index: a second claim on one issuer is refused.
            let! error = Record.ExceptionAsync(fun () -> insertOidc config "work" "https://auth.example.com/o/umbral" :> Task)
            Assert.Contains("issuer_url", messageOf error)

            // NULL is not an issuer: every built-in connector has none.
            do!
                exec
                    config
                    "INSERT INTO connectors (slug, kind, display_name, client_id, client_secret_ref, client_secret_hint, created_utc, updated_utc)
                     VALUES ('discord', 'discord', 'D', 'i', 'r', 'h', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
                            ('github', 'github', 'G', 'i', 'r', 'h', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');"
        })

[<Fact>]
let ``a database written by a newer build refuses to start, naming both versions`` () =
    withDb (fun config ->
        task {
            do! initializeSchema config
            do! exec config "UPDATE settings SET value = '4' WHERE key = 'schema_version'"

            let! error = Record.ExceptionAsync(fun () -> initializeSchema config :> Task)

            // Both versions, and what to do about it.
            let message = messageOf error
            Assert.Contains("version 4", message)
            Assert.Contains($"version {SchemaVersion}", message)
            Assert.Contains("newer image", message)
            Assert.Contains("backup", message)

            // Refusing is not partly starting: the stamp is still the newer one.
            let! version = scalar config "SELECT value FROM settings WHERE key = 'schema_version'"
            Assert.Equal("4", version)
        })

[<Fact>]
let ``a schema version that is not a number refuses to start`` () =
    withDb (fun config ->
        task {
            do! initializeSchema config
            do! exec config "UPDATE settings SET value = 'three' WHERE key = 'schema_version'"

            let! error = Record.ExceptionAsync(fun () -> initializeSchema config :> Task)

            Assert.Contains("three", messageOf error)
        })
