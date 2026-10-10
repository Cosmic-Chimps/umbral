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
