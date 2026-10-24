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

            // Every step ran in order: 1 -> 2 (is_shared), 2 -> 3 (issuer_url),
            // 3 -> 4 (app_branding), 4 -> 5 (allowed_email_domains).
            let! version = scalar config "SELECT value FROM settings WHERE key = 'schema_version'"
            Assert.Equal(string SchemaVersion, version)
            Assert.Equal("7", version)

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
            Assert.Equal("7", version)

            let! columns = scalar config "SELECT group_concat(name, ',') FROM pragma_table_info('connectors')"
            Assert.Contains("is_shared", columns)
            Assert.Contains("issuer_url", columns)
            Assert.Contains("allowed_email_domains", columns)

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
            Assert.Equal("7", version)

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
            do! exec config "UPDATE settings SET value = '8' WHERE key = 'schema_version'"

            let! error = Record.ExceptionAsync(fun () -> initializeSchema config :> Task)

            // Both versions, and what to do about it.
            let message = messageOf error
            Assert.Contains("version 8", message)
            Assert.Contains($"version {SchemaVersion}", message)
            Assert.Contains("newer image", message)
            Assert.Contains("backup", message)

            // Refusing is not partly starting: the stamp is still the newer one.
            let! version = scalar config "SELECT value FROM settings WHERE key = 'schema_version'"
            Assert.Equal("8", version)
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

// ---------------------------------------------------------------------------
// Schema version 4 (spec 006, app sign-in branding): app_branding
// ---------------------------------------------------------------------------

[<Fact>]
let ``a fresh database is at the current version and has the app_branding table`` () =
    withDb (fun config ->
        task {
            do! initializeSchema config

            let! version = scalar config "SELECT value FROM settings WHERE key = 'schema_version'"
            Assert.Equal("7", version)
            Assert.Equal(7, SchemaVersion)

            let! columns = scalar config "SELECT group_concat(name, ',') FROM pragma_table_info('app_branding')"

            for expected in
                [ "client_id"; "accent_color"; "logo_png"; "logo_sha256"; "logo_width"; "logo_height"; "updated_at" ] do
                Assert.Contains(expected, columns)

            let! rows = scalar config "SELECT count(*) FROM app_branding"
            Assert.Equal("0", rows)
        })

[<Fact>]
let ``a version 3 database upgrades with an empty app_branding table and its rows untouched`` () =
    withDb (fun config ->
        task {
            // Version 3 is today's schema without the table and without
            // the columns of versions 5, 6 and 7, so build that: the newest shape,
            // the table and those columns dropped, the stamp moved back. Rows of the
            // older tables go in before the upgrade runs.
            do! initializeSchema config
            do! exec config "DROP TABLE app_branding"
            do! exec config "ALTER TABLE connectors DROP COLUMN allowed_email_domains"
            do! exec config "ALTER TABLE connectors DROP COLUMN passkey_enrolment"
            do! exec config "ALTER TABLE connectors DROP COLUMN fediverse_scope"
            do! exec config "ALTER TABLE connectors DROP COLUMN fediverse_servers"
            do! exec config "ALTER TABLE users DROP COLUMN preferred_username"
            do! exec config "ALTER TABLE users DROP COLUMN profile_url"
            do! exec config "UPDATE settings SET value = '3' WHERE key = 'schema_version'"

            do!
                exec
                    config
                    "INSERT INTO users (id, display_name, first_seen_utc, last_seen_utc)
                     VALUES ('user-1', 'Jane', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                     INSERT INTO app_connector_choices (client_id) VALUES ('grafana');"

            let! missing = scalar config "SELECT count(*) FROM sqlite_master WHERE name = 'app_branding'"
            Assert.Equal("0", missing)

            do! initializeSchema config

            let! version = scalar config "SELECT value FROM settings WHERE key = 'schema_version'"
            Assert.Equal("7", version)

            let! rows = scalar config "SELECT count(*) FROM app_branding"
            Assert.Equal("0", rows)

            let! user = scalar config "SELECT display_name FROM users WHERE id = 'user-1'"
            Assert.Equal("Jane", user)

            let! choice = scalar config "SELECT client_id FROM app_connector_choices"
            Assert.Equal("grafana", choice)

            // Running it again changes nothing: the step is idempotent as well as versioned.
            do! initializeSchema config
            let! again = scalar config "SELECT value FROM settings WHERE key = 'schema_version'"
            Assert.Equal("7", again)
        })

/// Each statement is one row that breaks one CHECK of `app_branding`. Both
/// sides of each rule are covered by the valid row below, which must go in.
let private brandingViolations =
    [ "an uppercase accent",
      "INSERT INTO app_branding (client_id, accent_color, updated_at) VALUES ('a', 'F26B1D', '2026-01-01T00:00:00Z')"
      "a 5-character accent",
      "INSERT INTO app_branding (client_id, accent_color, updated_at) VALUES ('a', 'f26b1', '2026-01-01T00:00:00Z')"
      "a 7-character accent",
      "INSERT INTO app_branding (client_id, accent_color, updated_at) VALUES ('a', 'f26b1dd', '2026-01-01T00:00:00Z')"
      "an accent that is not hex",
      "INSERT INTO app_branding (client_id, accent_color, updated_at) VALUES ('a', 'g26b1d', '2026-01-01T00:00:00Z')"
      "a logo without its hash",
      "INSERT INTO app_branding (client_id, logo_png, logo_width, logo_height, updated_at) VALUES ('a', x'89504e47', 32, 32, '2026-01-01T00:00:00Z')"
      "a hash without its logo",
      "INSERT INTO app_branding (client_id, logo_sha256, logo_width, logo_height, updated_at) VALUES ('a', 'abc', 32, 32, '2026-01-01T00:00:00Z')"
      "a logo without its width",
      "INSERT INTO app_branding (client_id, logo_png, logo_sha256, logo_height, updated_at) VALUES ('a', x'89504e47', 'abc', 32, '2026-01-01T00:00:00Z')"
      "a logo without its height",
      "INSERT INTO app_branding (client_id, logo_png, logo_sha256, logo_width, updated_at) VALUES ('a', x'89504e47', 'abc', 32, '2026-01-01T00:00:00Z')"
      "a width without a logo",
      "INSERT INTO app_branding (client_id, logo_width, updated_at) VALUES ('a', 32, '2026-01-01T00:00:00Z')" ]

[<Fact>]
let ``the app_branding constraints reject a malformed accent and a half-stored logo`` () =
    withDb (fun config ->
        task {
            do! initializeSchema config

            for what, sql in brandingViolations do
                let! error = Record.ExceptionAsync(fun () -> exec config sql :> Task)
                Assert.True((not (isNull error)), $"{what} was accepted")
                Assert.Contains("CHECK constraint failed", messageOf error)

            let! rows = scalar config "SELECT count(*) FROM app_branding"
            Assert.Equal("0", rows)

            // The shapes the store writes are accepted: an accent alone, a logo
            // alone with its hash and size, and both.
            do!
                exec
                    config
                    "INSERT INTO app_branding (client_id, accent_color, updated_at) VALUES ('accent', 'f26b1d', '2026-01-01T00:00:00Z');
                     INSERT INTO app_branding (client_id, logo_png, logo_sha256, logo_width, logo_height, updated_at)
                         VALUES ('logo', x'89504e47', 'h1', 32, 32, '2026-01-01T00:00:00Z');
                     INSERT INTO app_branding (client_id, accent_color, logo_png, logo_sha256, logo_width, logo_height, updated_at)
                         VALUES ('both', '000000', x'89504e48', 'h2', 64, 48, '2026-01-01T00:00:00Z');"

            let! accepted = scalar config "SELECT count(*) FROM app_branding"
            Assert.Equal("3", accepted)
        })

// ---------------------------------------------------------------------------
// Schema version 5 (feature 006, allowed email domains): `allowed_email_domains`
// on connectors.
// ---------------------------------------------------------------------------

/// A database as version 3 left it: a connector of each kind that matters (one
/// shared and enabled, one not shared and disabled, an oidc one with an issuer),
/// a user and a linked account, and the stored version 3.
let private seedVersionThree (config: DbConfig) =
    exec
        config
        "CREATE TABLE connectors (
             slug                    TEXT    NOT NULL PRIMARY KEY,
             kind                    TEXT    NOT NULL,
             display_name            TEXT    NOT NULL,
             client_id               TEXT    NOT NULL,
             client_secret_ref       TEXT    NOT NULL,
             client_secret_hint      TEXT    NOT NULL,
             is_enabled              INTEGER NOT NULL DEFAULT 1,
             is_shared               INTEGER NOT NULL DEFAULT 1,
             created_utc             TEXT    NOT NULL,
             updated_utc             TEXT    NOT NULL,
             issuer_url              TEXT        NULL
         );
         CREATE UNIQUE INDEX ux_connectors_issuer ON connectors (issuer_url) WHERE issuer_url IS NOT NULL;
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
         CREATE TABLE settings (
             key         TEXT NOT NULL PRIMARY KEY,
             value       TEXT NOT NULL,
             updated_utc TEXT NOT NULL
         );
         INSERT INTO connectors
             (slug, kind, display_name, client_id, client_secret_ref, client_secret_hint, is_enabled, is_shared, created_utc, updated_utc, issuer_url)
         VALUES ('discord', 'discord', 'Discord', 'd-id', 'local:x', 'abcd', 1, 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z', NULL),
                ('github', 'github', 'GitHub', 'g-id', 'local:y', 'efgh', 0, 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z', NULL),
                ('home', 'oidc', 'Home', 'h-id', 'local:z', 'ijkl', 1, 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z', 'https://auth.example.com/o/umbral');
         INSERT INTO users (id, display_name, email, email_verified, first_seen_utc, last_seen_utc)
         VALUES ('user-1', 'Jane', 'jane@example.com', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
         INSERT INTO settings (key, value, updated_utc) VALUES ('schema_version', '3', '2026-01-01T00:00:00Z');"

[<Fact>]
let ``a version 3 database upgrades in place with every allowed domains list empty`` () =
    withDb (fun config ->
        task {
            do! seedVersionThree config
            do! initializeSchema config

            let! version = scalar config "SELECT value FROM settings WHERE key = 'schema_version'"
            Assert.Equal("7", version)

            // Step 4 ran on the way: the branding table is there and empty.
            let! branding = scalar config "SELECT count(*) FROM app_branding"
            Assert.Equal("0", branding)

            // Every connector that existed has an empty list: "use the instance
            // list", and there is none, so nobody is newly refused (SC-006).
            let! total = scalar config "SELECT count(*) FROM connectors"
            Assert.Equal("3", total)

            let! empty = scalar config "SELECT count(*) FROM connectors WHERE allowed_email_domains = ''"
            Assert.Equal("3", empty)

            let! instanceList = scalar config "SELECT count(*) FROM settings WHERE key = 'allowed_email_domains'"
            Assert.Equal("0", instanceList)

            // Nothing else about them moved.
            let! github = scalar config "SELECT client_id || '|' || is_enabled || '|' || is_shared FROM connectors WHERE slug = 'github'"
            Assert.Equal("g-id|0|0", github)

            let! issuer = scalar config "SELECT issuer_url FROM connectors WHERE slug = 'home'"
            Assert.Equal("https://auth.example.com/o/umbral", issuer)

            let! user = scalar config "SELECT display_name || '|' || email FROM users WHERE id = 'user-1'"
            Assert.Equal("Jane|jane@example.com", user)

            // And the column takes a list.
            do! exec config "UPDATE connectors SET allowed_email_domains = 'acme.com example.org' WHERE slug = 'discord'"
            let! stored = scalar config "SELECT allowed_email_domains FROM connectors WHERE slug = 'discord'"
            Assert.Equal("acme.com example.org", stored)
        })

[<Fact>]
let ``upgrading a version 3 database twice changes nothing the second time`` () =
    withDb (fun config ->
        task {
            do! seedVersionThree config
            do! initializeSchema config
            do! exec config "UPDATE connectors SET allowed_email_domains = 'acme.com' WHERE slug = 'discord'"

            // The second start finds version 6 and runs no step, so the list
            // an operator set survives a restart.
            do! initializeSchema config

            let! stored = scalar config "SELECT allowed_email_domains FROM connectors WHERE slug = 'discord'"
            Assert.Equal("acme.com", stored)
        })

// ---------------------------------------------------------------------------
// Schema version 6 (spec 008, the passkey connector): `connectors.passkey_enrolment`
// and the passkeys, passkey_invitations and passkey_ceremonies tables.
// ---------------------------------------------------------------------------

/// A database as version 5 left it: `connectors` has `allowed_email_domains` and
/// no `passkey_enrolment`, none of the passkey tables exist, there are connector
/// rows and a user, and the stored version is 5.
let private seedVersionFive (config: DbConfig) =
    exec
        config
        "CREATE TABLE connectors (
             slug                    TEXT    NOT NULL PRIMARY KEY,
             kind                    TEXT    NOT NULL,
             display_name            TEXT    NOT NULL,
             client_id               TEXT    NOT NULL,
             client_secret_ref       TEXT    NOT NULL,
             client_secret_hint      TEXT    NOT NULL,
             is_enabled              INTEGER NOT NULL DEFAULT 1,
             is_shared               INTEGER NOT NULL DEFAULT 1,
             created_utc             TEXT    NOT NULL,
             updated_utc             TEXT    NOT NULL,
             issuer_url              TEXT        NULL,
             allowed_email_domains   TEXT    NOT NULL DEFAULT ''
         );
         CREATE UNIQUE INDEX ux_connectors_issuer ON connectors (issuer_url) WHERE issuer_url IS NOT NULL;
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
         CREATE TABLE settings (
             key         TEXT NOT NULL PRIMARY KEY,
             value       TEXT NOT NULL,
             updated_utc TEXT NOT NULL
         );
         INSERT INTO connectors
             (slug, kind, display_name, client_id, client_secret_ref, client_secret_hint, is_enabled, is_shared, created_utc, updated_utc, issuer_url, allowed_email_domains)
         VALUES ('discord', 'discord', 'Discord', 'd-id', 'local:x', 'abcd', 1, 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z', NULL, 'acme.com'),
                ('github', 'github', 'GitHub', 'g-id', 'local:y', 'efgh', 0, 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z', NULL, ''),
                ('home', 'oidc', 'Home', 'h-id', 'local:z', 'ijkl', 1, 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z', 'https://auth.example.com/o/umbral', '');
         INSERT INTO users (id, display_name, email, email_verified, first_seen_utc, last_seen_utc)
         VALUES ('user-1', 'Jane', 'jane@example.com', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
         INSERT INTO settings (key, value, updated_utc) VALUES ('schema_version', '5', '2026-01-01T00:00:00Z');"

let private passkeyTables = [ "passkeys"; "passkey_invitations"; "passkey_ceremonies" ]

[<Fact>]
let ``a version 5 database with connector rows upgrades to 6 with every enrolment set to invitations`` () =
    withDb (fun config ->
        task {
            do! seedVersionFive config
            do! initializeSchema config

            let! version = scalar config "SELECT value FROM settings WHERE key = 'schema_version'"
            Assert.Equal("7", version)

            // The column default gives every row that existed the safe mode.
            let! total = scalar config "SELECT count(*) FROM connectors"
            Assert.Equal("3", total)
            let! invitations = scalar config "SELECT count(*) FROM connectors WHERE passkey_enrolment = 'invitations'"
            Assert.Equal("3", invitations)

            // The three tables exist and are empty.
            for table in passkeyTables do
                let! rows = scalar config $"SELECT count(*) FROM pragma_table_info('{table}')"
                Assert.NotEqual<string>("0", rows)

            let! passkeys = scalar config "SELECT count(*) FROM passkeys"
            Assert.Equal("0", passkeys)
            let! invitationRows = scalar config "SELECT count(*) FROM passkey_invitations"
            Assert.Equal("0", invitationRows)
            let! ceremonies = scalar config "SELECT count(*) FROM passkey_ceremonies"
            Assert.Equal("0", ceremonies)

            // Nothing else about the rows moved.
            let! discord = scalar config "SELECT client_id || '|' || is_enabled || '|' || allowed_email_domains FROM connectors WHERE slug = 'discord'"
            Assert.Equal("d-id|1|acme.com", discord)
            let! issuer = scalar config "SELECT issuer_url FROM connectors WHERE slug = 'home'"
            Assert.Equal("https://auth.example.com/o/umbral", issuer)

            // And the column takes the other mode.
            do! exec config "UPDATE connectors SET passkey_enrolment = 'open' WHERE slug = 'github'"
            let! stored = scalar config "SELECT passkey_enrolment FROM connectors WHERE slug = 'github'"
            Assert.Equal("open", stored)
        })

[<Fact>]
let ``upgrading a version 5 database twice changes nothing the second time`` () =
    withDb (fun config ->
        task {
            do! seedVersionFive config
            do! initializeSchema config
            do! exec config "UPDATE connectors SET passkey_enrolment = 'open' WHERE slug = 'github'"

            do! initializeSchema config

            let! stored = scalar config "SELECT passkey_enrolment FROM connectors WHERE slug = 'github'"
            Assert.Equal("open", stored)
        })

[<Fact>]
let ``a fresh database is stamped 6 and has the passkey column and tables`` () =
    withDb (fun config ->
        task {
            do! initializeSchema config

            let! version = scalar config "SELECT value FROM settings WHERE key = 'schema_version'"
            Assert.Equal("7", version)

            let! columns = scalar config "SELECT group_concat(name, ',') FROM pragma_table_info('connectors')"
            Assert.Contains("passkey_enrolment", columns)

            let! passkeyColumns = scalar config "SELECT group_concat(name, ',') FROM pragma_table_info('passkeys')"

            for expected in
                [ "id"; "user_id"; "credential_id"; "public_key"; "sign_count"; "nickname"; "aaguid"; "transports"; "backup_eligible"; "created_utc"; "last_used_utc" ] do
                Assert.Contains(expected, passkeyColumns)

            let! invitationColumns = scalar config "SELECT group_concat(name, ',') FROM pragma_table_info('passkey_invitations')"

            for expected in
                [ "id"; "secret_hash"; "connector_slug"; "user_id"; "note"; "created_utc"; "expires_utc"; "used_utc"; "revoked_utc" ] do
                Assert.Contains(expected, invitationColumns)

            let! ceremonyColumns = scalar config "SELECT group_concat(name, ',') FROM pragma_table_info('passkey_ceremonies')"

            for expected in
                [ "id"; "purpose"; "options_json"; "user_id"; "invitation_id"; "display_name"; "nickname"; "return_url"; "created_utc"; "expires_utc" ] do
                Assert.Contains(expected, ceremonyColumns)

            // A connector row that does not name an enrolment gets the safe one.
            do! exec config "INSERT INTO connectors (slug, kind, display_name, client_id, client_secret_ref, client_secret_hint, created_utc, updated_utc)
                             VALUES ('passkey', 'passkey', 'Passkey', '', '', '', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')"
            let! enrolment = scalar config "SELECT passkey_enrolment FROM connectors WHERE slug = 'passkey'"
            Assert.Equal("invitations", enrolment)
        })

let private seedPasskeyRows (config: DbConfig) =
    exec
        config
        "INSERT INTO connectors (slug, kind, display_name, client_id, client_secret_ref, client_secret_hint, created_utc, updated_utc)
         VALUES ('passkey', 'passkey', 'Passkey', '', '', '', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
         INSERT INTO users (id, display_name, first_seen_utc, last_seen_utc)
         VALUES ('user-1', 'Jane', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
                ('user-2', 'Joe', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
         INSERT INTO passkeys (id, user_id, credential_id, public_key, nickname, created_utc)
         VALUES ('pk-1', 'user-1', x'0102', x'AA', 'Phone', '2026-01-01T00:00:00Z'),
                ('pk-2', 'user-2', x'0304', x'BB', 'Key', '2026-01-01T00:00:00Z');
         INSERT INTO passkey_invitations (id, secret_hash, connector_slug, user_id, created_utc, expires_utc)
         VALUES ('inv-1', 'hash-1', 'passkey', 'user-1', '2026-01-01T00:00:00Z', '2026-01-08T00:00:00Z'),
                ('inv-2', 'hash-2', 'passkey', NULL, '2026-01-01T00:00:00Z', '2026-01-08T00:00:00Z');"

[<Fact>]
let ``deleting a user cascades to the passkeys and the invitations bound to them`` () =
    withDb (fun config ->
        task {
            do! initializeSchema config
            do! seedPasskeyRows config

            do! exec config "DELETE FROM users WHERE id = 'user-1'"

            let! passkeys = scalar config "SELECT group_concat(id) FROM passkeys"
            Assert.Equal("pk-2", passkeys)

            // The invitation for a new person has no user and stays.
            let! invitations = scalar config "SELECT group_concat(id) FROM passkey_invitations"
            Assert.Equal("inv-2", invitations)
        })

[<Fact>]
let ``deleting the connector cascades to its invitations and leaves the passkeys`` () =
    withDb (fun config ->
        task {
            do! initializeSchema config
            do! seedPasskeyRows config

            do! exec config "DELETE FROM connectors WHERE slug = 'passkey'"

            let! invitations = scalar config "SELECT count(*) FROM passkey_invitations"
            Assert.Equal("0", invitations)

            // A passkey belongs to the person, not to the connector: turning
            // the connector back on finds them as they were.
            let! passkeys = scalar config "SELECT count(*) FROM passkeys"
            Assert.Equal("2", passkeys)
        })

[<Fact>]
let ``a credential id is unique across every user`` () =
    withDb (fun config ->
        task {
            do! initializeSchema config
            do! seedPasskeyRows config

            let! error =
                Record.ExceptionAsync(fun () ->
                    exec
                        config
                        "INSERT INTO passkeys (id, user_id, credential_id, public_key, nickname, created_utc)
                         VALUES ('pk-3', 'user-2', x'0102', x'CC', 'Copy', '2026-01-01T00:00:00Z')"
                    :> Task)

            Assert.Contains("UNIQUE", messageOf error)

            let! count = scalar config "SELECT count(*) FROM passkeys"
            Assert.Equal("2", count)
        })

[<Fact>]
let ``an invitation secret hash is unique`` () =
    withDb (fun config ->
        task {
            do! initializeSchema config
            do! seedPasskeyRows config

            let! error =
                Record.ExceptionAsync(fun () ->
                    exec
                        config
                        "INSERT INTO passkey_invitations (id, secret_hash, connector_slug, created_utc, expires_utc)
                         VALUES ('inv-3', 'hash-1', 'passkey', '2026-01-01T00:00:00Z', '2026-01-08T00:00:00Z')"
                    :> Task)

            Assert.Contains("UNIQUE", messageOf error)
        })
