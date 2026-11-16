/// `Umbral clear-users`: every user and everything they hold goes, the
/// setup stays, and the clearing and its `users_cleared` event commit together.
///
/// The instance under test is a real one, so the tokens and authorizations are
/// OpenIddict's own (two people signed in through the fake provider and
/// exchanged their codes). The rest of what a user holds, and what must stay,
/// is seeded with plain SQL so that every table is non-empty before the clear.
module Umbral.Tests.ClearUsersTests

open System
open System.IO
open System.Net
open System.Security.Cryptography
open System.Threading.Tasks
open Xunit
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

/// Deleted by a clear, in the order the command deletes them.
let private deletedTables =
    [ "OpenIddictTokens"
      "OpenIddictAuthorizations"
      "passkey_ceremonies"
      "passkey_invitations"
      "passkeys"
      "linked_accounts"
      "legacy_sessions_ended"
      "removed_links"
      "users" ]

/// Kept by a clear: the rows of each must be exactly as they were.
let private keptTables =
    [ "connectors"
      "fediverse_servers"
      "app_branding"
      "app_connector_choices"
      "app_connectors"
      "settings"
      "operator_passkeys"
      "declared_items"
      "OpenIddictApplications" ]

let private execute (app: Fixtures.TestApp) (statement: string) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- statement
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

let private count (app: Fixtures.TestApp) (table: string) =
    task {
        let! value = scalar app $"SELECT COUNT(*) FROM {table}" []
        return int value
    }

/// Every row of a table, every column, as text: a blob as hex, NULL as `~`.
let private dump (app: Fixtures.TestApp) (table: string) : Task<string list> =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- $"SELECT * FROM {table}"
        use! reader = command.ExecuteReaderAsync()
        let rows = ResizeArray<string>()

        while reader.Read() do
            let cells =
                [ for i in 0 .. reader.FieldCount - 1 ->
                      if reader.IsDBNull i then "~"
                      else
                          match reader.GetValue i with
                          | :? (byte array) as bytes -> Convert.ToHexString bytes
                          | other -> string other ]

            rows.Add(String.Join("|", cells))

        return rows |> Seq.sort |> List.ofSeq
    }

let private dumpAll (app: Fixtures.TestApp) (tables: string list) =
    task {
        let result = Collections.Generic.Dictionary<string, string list>()

        for table in tables do
            let! rows = dump app table
            result[table] <- rows

        return result
    }

let private eventRows (app: Fixtures.TestApp) (eventType: string) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()

        command.CommandText <-
            "SELECT actor, outcome, user_id, connector_slug, client_id, remote_ip, detail
             FROM security_events WHERE event_type = @t"

        command.Parameters.AddWithValue("@t", eventType) |> ignore
        use! reader = command.ExecuteReaderAsync()
        let rows = ResizeArray<string list>()

        while reader.Read() do
            rows.Add [ for i in 0 .. reader.FieldCount - 1 -> if reader.IsDBNull i then "~" else reader.GetString i ]

        return List.ofSeq rows
    }

/// The signing, encryption and data-protection key files, by name, with a hash of their bytes.
let private keyFiles (app: Fixtures.TestApp) =
    let directory = Path.Combine(app.DataDirectory, "keys")

    Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
    |> Array.sort
    |> Array.map (fun path -> Path.GetRelativePath(directory, path), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes path)))
    |> List.ofArray

type private Seeded =
    { Users: int
      Tokens: int
      Passkeys: int
      LinkedAccounts: int
      SecurityEvents: int }

/// An instance with two people who signed in and exchanged their codes (tokens
/// and authorizations), and a row in every table a clear deletes or keeps.
let private seed (app: Fixtures.TestApp) =
    task {
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app
        let! clientId, secret = registerApp operator "Grafana"

        for subject, email in [ "fake-user-1", "ana@example.com"; "fake-user-2", "bea@example.com" ] do
            let person = Browser app.Client
            let! signedIn = signInViaFake person clientId "openid offline_access" subject email

            let code =
                match signedIn with
                | Ok code -> code
                | Error where' -> failwith $"sign-in refused: {where'}"

            let! exchanged = exchangeCode app clientId secret code
            Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode)

        let! ana = userIdOf app "fake-user-1"
        let! bea = userIdOf app "fake-user-2"
        let now = app.Clock.GetUtcNow().ToString "o"
        let later = app.Clock.GetUtcNow().AddMinutes(5.0).ToString "o"

        // What a user holds, beyond the link and the tokens the sign-ins made.
        do!
            execute
                app
                $"INSERT INTO passkeys (id, user_id, credential_id, public_key, nickname, created_utc)
                  VALUES ('pk-1', '{ana}', X'0102', X'03', 'Phone', '{now}'),
                         ('pk-2', '{bea}', X'0405', X'06', 'Laptop', '{now}');
                  INSERT INTO passkey_invitations (id, secret_hash, connector_slug, user_id, note, created_utc, expires_utc)
                  VALUES ('inv-1', 'hash-1', 'fake', '{ana}', 'another device', '{now}', '{later}'),
                         ('inv-2', 'hash-2', 'fake', NULL, 'for Carla', '{now}', '{later}');
                  INSERT INTO passkey_ceremonies (id, purpose, options_json, user_id, created_utc, expires_utc)
                  VALUES ('cer-1', 'register', '{{}}', '{ana}', '{now}', '{later}'),
                         ('cer-2', 'register', '{{}}', NULL, '{now}', '{later}');
                  INSERT INTO legacy_sessions_ended (user_id, ended_utc) VALUES ('{bea}', '{now}');
                  INSERT INTO removed_links (provider_kind, provider_subject, removed_utc) VALUES ('github', 'gh-9', '{now}');"

        // What stays.
        do!
            execute
                app
                $"INSERT INTO fediverse_servers (host, client_id, client_secret, authorize_url, token_url, redirect_uri, registered_utc)
                  VALUES ('mastodon.test', 'c', 's', 'https://mastodon.test/a', 'https://mastodon.test/t', 'https://umbral.test/cb', '{now}');
                  INSERT INTO app_branding (client_id, accent_color, updated_at) VALUES ('{clientId}', 'ff0000', '{now}');
                  INSERT INTO app_connector_choices (client_id) VALUES ('{clientId}');
                  INSERT INTO app_connectors (client_id, connector_slug) VALUES ('{clientId}', 'fake');
                  INSERT INTO operator_passkeys (id, credential_id, public_key, name, created_utc)
                  VALUES ('op-1', X'0A0B', X'0C', 'Key', '{now}');
                  INSERT INTO declared_items (kind, id, is_declared, first_declared_utc, changed_utc)
                  VALUES ('app', '{clientId}', 1, '{now}', '{now}');"

        let! users = count app "users"
        let! tokens = count app "OpenIddictTokens"
        let! passkeys = count app "passkeys"
        let! links = count app "linked_accounts"
        let! events = count app "security_events"

        // Every table is non-empty, so an emptied table means the clear did it.
        for table in deletedTables @ keptTables do
            let! n = count app table
            Assert.True(n > 0, $"the test needs a row in {table}")

        return
            { Users = users
              Tokens = tokens
              Passkeys = passkeys
              LinkedAccounts = links
              SecurityEvents = events }
    }

[<Fact>]
let ``count returns the number of users and changes nothing`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! seeded = seed app
        let! before = dumpAll app (deletedTables @ keptTables)

        let! n = ClearUsers.count app.DbConfig
        Assert.Equal(2, n)
        Assert.Equal(seeded.Users, n)

        let! after = dumpAll app (deletedTables @ keptTables)

        for table in deletedTables @ keptTables do
            Assert.Equal<string list>(before[table], after[table])

        let! cleared = eventRows app "users_cleared"
        Assert.Empty cleared
    }

[<Fact>]
let ``clear deletes every user and what they hold, keeps the setup, and records one event`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! seeded = seed app
        let! keptBefore = dumpAll app keptTables
        let! eventsBefore = dump app "security_events"

        let! result = ClearUsers.clear app.DbConfig app.Clock false

        let expected: ClearUsers.Cleared =
            { Users = seeded.Users
              LinkedAccounts = seeded.LinkedAccounts
              Passkeys = seeded.Passkeys
              Tokens = seeded.Tokens
              SecurityLogCleared = false }

        Assert.Equal(expected, result)

        Assert.True(result.Users = 2 && result.Passkeys = 2 && result.Tokens > 0)

        for table in deletedTables do
            let! n = count app table
            Assert.True((n = 0), $"{table} should be empty, has {n} rows")

        let! keptAfter = dumpAll app keptTables

        for table in keptTables do
            Assert.Equal<string list>(keptBefore[table], keptAfter[table])

        // The log keeps every earlier row and gains exactly one.
        let! eventsAfter = dump app "security_events"
        Assert.Equal(eventsBefore.Length + 1, eventsAfter.Length)
        let! cleared = eventRows app "users_cleared"
        Assert.Equal<string list list>([ [ "system"; "success"; "~"; "~"; "~"; "~"; $"users={seeded.Users}" ] ], cleared)

        // The one earlier event that names a user still names them: the log is
        // history, and only `--security-log` empties it.
        let! named = scalar app "SELECT COUNT(*) FROM security_events WHERE user_id IS NOT NULL" []
        Assert.True(int named > 0)
    }

[<Fact>]
let ``clear with the security log leaves only the users_cleared event`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! seeded = seed app
        let! keptBefore = dumpAll app keptTables

        let! result = ClearUsers.clear app.DbConfig app.Clock true
        Assert.True result.SecurityLogCleared

        let! events = count app "security_events"
        Assert.Equal(1, events)
        let! cleared = eventRows app "users_cleared"

        Assert.Equal<string list list>(
            [ [ "system"; "success"; "~"; "~"; "~"; "~"; $"users={seeded.Users};security_log=cleared" ] ],
            cleared
        )

        let! keptAfter = dumpAll app keptTables

        for table in keptTables do
            Assert.Equal<string list>(keptBefore[table], keptAfter[table])
    }

[<Fact>]
let ``a failure in the middle rolls everything back and records nothing`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _ = seed app
        let everything = deletedTables @ keptTables @ [ "security_events" ]
        let! before = dumpAll app everything

        do!
            execute
                app
                "CREATE TRIGGER refuse_users BEFORE DELETE ON users BEGIN SELECT RAISE(ABORT, 'refused'); END"

        let! thrown = Assert.ThrowsAnyAsync<Exception>(fun () -> ClearUsers.clear app.DbConfig app.Clock true :> Task)
        Assert.Contains("refused", thrown.Message)

        let! after = dumpAll app everything

        for table in everything do
            Assert.Equal<string list>(before[table], after[table])

        let! cleared = eventRows app "users_cleared"
        Assert.Empty cleared
    }

[<Fact>]
let ``a data directory that never started has no OpenIddict tables and clears without them`` () =
    task {
        let path = Path.Combine(Path.GetTempPath(), "umbral-cu-" + Guid.NewGuid().ToString "N" + ".db")
        let config: Database.DbConfig = { DataSource = path }

        try
            do! Database.initializeSchema config
            let! users = ClearUsers.count config
            Assert.Equal(0, users)

            let! result = ClearUsers.clear config TimeProvider.System false
            Assert.Equal(0, result.Users)
            Assert.Equal(0, result.Tokens)
        finally
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            for suffix in [ ""; "-wal"; "-shm" ] do
                if File.Exists(path + suffix) then File.Delete(path + suffix)
    }

[<Fact>]
let ``the signing, encryption and data-protection keys are byte-identical afterwards`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _ = seed app
        let before = keyFiles app
        Assert.Contains(before, fun (name, _) -> name = "signing.pfx")
        Assert.Contains(before, fun (name, _) -> name = "encryption.pfx")
        Assert.Contains(before, fun (name, _) -> name.EndsWith ".xml")

        let! _ = ClearUsers.clear app.DbConfig app.Clock true

        Assert.Equal<(string * string) list>(before, keyFiles app)
    }
