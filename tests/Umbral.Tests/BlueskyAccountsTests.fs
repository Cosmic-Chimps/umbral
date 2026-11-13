module Umbral.Tests.BlueskyAccountsTests

open System
open System.IO
open System.Threading.Tasks
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure

// BlueskyAccounts (specs/015, data-model "Storage, schema version 12"): the
// authorization server a Bluesky link last signed in through, and the re-check of
// "Only these servers" against it.

let private withDb (body: Database.DbConfig -> Task<unit>) =
    task {
        let path = Path.Combine(Path.GetTempPath(), $"umbral-bluesky-{Guid.NewGuid():N}.db")
        let config: Database.DbConfig = { DataSource = path }

        try
            do! Database.initializeSchema config
            do! body config
        finally
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            for suffix in [ ""; "-wal"; "-shm" ] do
                File.Delete(path + suffix)
    }

let private exec (config: Database.DbConfig) (sql: string) =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- sql
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

let private scalar (config: Database.DbConfig) (sql: string) =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- sql
        let! value = command.ExecuteScalarAsync()
        return string value
    }

let private now = DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero)

let private connector slug kind access : Connector =
    { Slug = slug
      Kind = kind
      DisplayName = slug
      ClientId = ""
      ClientSecretRef = ""
      ClientSecretHint = ""
      IssuerUrl = None
      AllowedEmailDomains = []
      PasskeyEnrolment = InvitationsOnly
      FediverseAccess = access
      AppleKey = None
      MicrosoftAudience = None
      IsEnabled = true
      IsShared = true
      CreatedUtc = now
      UpdatedUtc = now }

let private saveConnector (config: Database.DbConfig) c =
    task {
        let clock = Microsoft.Extensions.Time.Testing.FakeTimeProvider(now) :> TimeProvider
        do! ConnectorStore.save config clock c None
    }

let private link kind subject server =
    let server' = match server with Some(s: string) -> $"'{s}'" | None -> "NULL"

    $"INSERT INTO linked_accounts (provider_kind, provider_subject, user_id, asserted_email_verified, linked_utc, last_used_utc, provider_server)
      VALUES ('{kind}', '{subject}', 'u-1', 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z', {server'});"

let private addUser (config: Database.DbConfig) =
    exec
        config
        "INSERT INTO users (id, display_name, email, email_verified, first_seen_utc, last_seen_utc)
         VALUES ('u-1', 'Ana', NULL, 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');"

let private onlyListed servers = { OnlyListed = true; Servers = servers }

[<Fact>]
let ``recordServer writes the server of the bluesky row only`` () =
    withDb (fun config ->
        task {
            do! addUser config

            do!
                exec
                    config
                    (link "bluesky" "did:plc:abc" None
                     + link "bluesky" "did:plc:other" None
                     + link "github" "did:plc:abc" None)

            do! BlueskyAccounts.recordServer config "did:plc:abc" "bsky.social"

            let! written = scalar config "SELECT provider_server FROM linked_accounts WHERE provider_kind = 'bluesky' AND provider_subject = 'did:plc:abc'"
            Assert.Equal("bsky.social", written)

            // The other DID and the other kind (same subject text) stay NULL.
            let! unset = scalar config "SELECT count(*) FROM linked_accounts WHERE provider_server IS NULL"
            Assert.Equal("2", unset)
        })

[<Fact>]
let ``recordServer replaces the server of a link that signed in elsewhere before`` () =
    withDb (fun config ->
        task {
            do! addUser config
            do! exec config (link "bluesky" "did:plc:abc" (Some "old.example"))

            do! BlueskyAccounts.recordServer config "did:plc:abc" "bsky.social"

            let! written = scalar config "SELECT provider_server FROM linked_accounts WHERE provider_kind = 'bluesky'"
            Assert.Equal("bsky.social", written)
        })

[<Fact>]
let ``recheckServer admits when the connector admits any server`` () =
    withDb (fun config ->
        task {
            do! saveConnector config (connector "bluesky" Bluesky FediverseAccess.Any)
            do! addUser config
            do! exec config (link "bluesky" "did:plc:abc" (Some "pds.example"))

            let! refused = BlueskyAccounts.recheckServer config (Some "bluesky") "u-1"
            Assert.Equal(None, refused)
        })

[<Fact>]
let ``recheckServer admits a listed server`` () =
    withDb (fun config ->
        task {
            do! saveConnector config (connector "bluesky" Bluesky (onlyListed [ "bsky.social" ]))
            do! addUser config
            do! exec config (link "bluesky" "did:plc:abc" (Some "bsky.social"))

            let! refused = BlueskyAccounts.recheckServer config (Some "bluesky") "u-1"
            Assert.Equal(None, refused)
        })

[<Fact>]
let ``recheckServer refuses a link with no server recorded, fail closed`` () =
    withDb (fun config ->
        task {
            do! saveConnector config (connector "bluesky" Bluesky (onlyListed [ "bsky.social" ]))
            do! addUser config
            do! exec config (link "bluesky" "did:plc:abc" None)

            let! refused = BlueskyAccounts.recheckServer config (Some "bluesky") "u-1"
            Assert.Equal(Some "", refused)
        })

[<Fact>]
let ``recheckServer admits a link with no server recorded when any server is allowed`` () =
    withDb (fun config ->
        task {
            do! saveConnector config (connector "bluesky" Bluesky FediverseAccess.Any)
            do! addUser config
            do! exec config (link "bluesky" "did:plc:abc" None)

            let! refused = BlueskyAccounts.recheckServer config (Some "bluesky") "u-1"
            Assert.Equal(None, refused)
        })

[<Fact>]
let ``recheckServer admits when there is no connector slug or the slug is not Bluesky`` () =
    withDb (fun config ->
        task {
            do! saveConnector config (connector "fediverse" Fediverse (onlyListed [ "mastodon.test" ]))
            do! addUser config
            do! exec config (link "bluesky" "did:plc:abc" (Some "pds.example"))

            let! noSlug = BlueskyAccounts.recheckServer config None "u-1"
            Assert.Equal(None, noSlug)

            // A Fediverse connector with a list: not Bluesky's to judge.
            let! otherKind = BlueskyAccounts.recheckServer config (Some "fediverse") "u-1"
            Assert.Equal(None, otherKind)

            let! unknown = BlueskyAccounts.recheckServer config (Some "missing") "u-1"
            Assert.Equal(None, unknown)
        })

[<Fact>]
let ``recheckServer refuses an unlisted server and returns it`` () =
    withDb (fun config ->
        task {
            do! saveConnector config (connector "bluesky" Bluesky (onlyListed [ "bsky.social" ]))
            do! addUser config
            do! exec config (link "bluesky" "did:plc:abc" (Some "pds.example"))

            let! refused = BlueskyAccounts.recheckServer config (Some "bluesky") "u-1"
            Assert.Equal(Some "pds.example", refused)
        })

[<Fact>]
let ``recheckServer admits when any one of the user's links is on the list`` () =
    withDb (fun config ->
        task {
            do! saveConnector config (connector "bluesky" Bluesky (onlyListed [ "bsky.social" ]))
            do! addUser config

            do!
                exec
                    config
                    (link "bluesky" "did:plc:abc" (Some "pds.example")
                     + link "bluesky" "did:plc:def" (Some "bsky.social"))

            let! refused = BlueskyAccounts.recheckServer config (Some "bluesky") "u-1"
            Assert.Equal(None, refused)
        })
