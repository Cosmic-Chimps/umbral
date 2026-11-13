module Umbral.Tests.MicrosoftAccountsTests

open System
open System.IO
open System.Threading.Tasks
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure

// MicrosoftAccounts (specs/016 FR-011, research R3 last paragraph): the re-check
// of a remembered Microsoft sign-in against the connector's audience, by the
// tenant in the `provider_subject` of the user's `microsoft` links.

let private personal = "9188040d-6c67-4c5b-b112-36a304b66dad"
let private workA = "4f5b3d6e-27b0-4c3d-a6a3-5c9a1d0fb2e7"
let private workB = "c1d2e3f4-0a1b-4c2d-8e3f-a4b5c6d7e8f9"
let private oid1 = "0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d"
let private oid2 = "5d6e7f80-1a2b-4c3d-9e4f-5a6b7c8d9e0f"

let private withDb (body: Database.DbConfig -> Task<unit>) =
    task {
        let path = Path.Combine(Path.GetTempPath(), $"umbral-microsoft-{Guid.NewGuid():N}.db")
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

let private now = DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero)

let private connector slug kind audience : Connector =
    { Slug = slug
      Kind = kind
      DisplayName = slug
      ClientId = ""
      ClientSecretRef = ""
      ClientSecretHint = ""
      IssuerUrl = None
      AllowedEmailDomains = []
      PasskeyEnrolment = InvitationsOnly
      FediverseAccess = FediverseAccess.Any
      AppleKey = None
      MicrosoftAudience = audience
      IsEnabled = true
      IsShared = true
      CreatedUtc = now
      UpdatedUtc = now }

let private saveConnector (config: Database.DbConfig) c =
    task {
        let clock = Microsoft.Extensions.Time.Testing.FakeTimeProvider(now) :> TimeProvider
        do! ConnectorStore.save config clock c None
    }

let private link kind subject =
    $"INSERT INTO linked_accounts (provider_kind, provider_subject, user_id, asserted_email_verified, linked_utc, last_used_utc)
      VALUES ('{kind}', '{subject}', 'u-1', 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');"

let private addUser (config: Database.DbConfig) =
    exec
        config
        "INSERT INTO users (id, display_name, email, email_verified, first_seen_utc, last_seen_utc)
         VALUES ('u-1', 'Maria', NULL, 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');"

/// One user with the links given as tenant and object, and the Microsoft
/// connector `microsoft` with the audience.
let private setup (config: Database.DbConfig) (audience: MicrosoftAudience option) (links: (string * string) list) =
    task {
        do! saveConnector config (connector "microsoft" Microsoft audience)
        do! addUser config

        for tid, oid in links do
            do! exec config (link "microsoft" $"{tid}#{oid}")
    }

[<Fact>]
let ``recheckTenant admits when there is no connector slug or the slug is not Microsoft`` () =
    withDb (fun config ->
        task {
            do! setup config (Some PersonalAccounts) [ workA, oid1 ]
            do! saveConnector config (connector "fediverse" Fediverse None)

            let! noSlug = MicrosoftAccounts.recheckTenant config None "u-1"
            Assert.Equal(None, noSlug)

            let! otherKind = MicrosoftAccounts.recheckTenant config (Some "fediverse") "u-1"
            Assert.Equal(None, otherKind)

            let! unknown = MicrosoftAccounts.recheckTenant config (Some "missing") "u-1"
            Assert.Equal(None, unknown)
        })

[<Fact>]
let ``recheckTenant admits a personal link under personal accounts`` () =
    withDb (fun config ->
        task {
            do! setup config (Some PersonalAccounts) [ personal, oid1 ]
            let! refused = MicrosoftAccounts.recheckTenant config (Some "microsoft") "u-1"
            Assert.Equal(None, refused)
        })

[<Fact>]
let ``recheckTenant admits a personal link under personal and any organization`` () =
    withDb (fun config ->
        task {
            do! setup config (Some PersonalAndAnyOrganization) [ personal, oid1 ]
            let! refused = MicrosoftAccounts.recheckTenant config (Some "microsoft") "u-1"
            Assert.Equal(None, refused)
        })

[<Fact>]
let ``recheckTenant refuses a work link under personal accounts and returns its tenant`` () =
    withDb (fun config ->
        task {
            do! setup config (Some PersonalAccounts) [ workA, oid1 ]
            let! refused = MicrosoftAccounts.recheckTenant config (Some "microsoft") "u-1"
            Assert.Equal(Some workA, refused)
        })

[<Fact>]
let ``recheckTenant admits when any one of two links is admitted`` () =
    withDb (fun config ->
        task {
            do! setup config (Some PersonalAccounts) [ workA, oid1; personal, oid2 ]
            let! refused = MicrosoftAccounts.recheckTenant config (Some "microsoft") "u-1"
            Assert.Equal(None, refused)
        })

[<Fact>]
let ``recheckTenant refuses under one organization for another tenant`` () =
    withDb (fun config ->
        task {
            do! setup config (Some(OneOrganization(Guid.Parse workB))) [ workA, oid1 ]
            let! refused = MicrosoftAccounts.recheckTenant config (Some "microsoft") "u-1"
            Assert.Equal(Some workA, refused)
        })

[<Fact>]
let ``recheckTenant refuses when the connector has no audience, fail closed`` () =
    withDb (fun config ->
        task {
            do! setup config None [ workA, oid1 ]
            let! refused = MicrosoftAccounts.recheckTenant config (Some "microsoft") "u-1"
            Assert.Equal(Some workA, refused)
        })

[<Fact>]
let ``recheckTenant refuses a user with no readable Microsoft link, fail closed`` () =
    withDb (fun config ->
        task {
            do! setup config (Some PersonalAndAnyOrganization) []
            do! exec config (link "microsoft" "not-a-subject")
            let! refused = MicrosoftAccounts.recheckTenant config (Some "microsoft") "u-1"
            Assert.Equal(Some "", refused)
        })

[<Fact>]
let ``recheckTenant reads only the microsoft links of the user`` () =
    withDb (fun config ->
        task {
            do! setup config (Some PersonalAccounts) [ workA, oid1 ]
            // A personal-looking subject under another kind does not admit.
            do! exec config (link "github" $"{personal}#{oid2}")
            let! refused = MicrosoftAccounts.recheckTenant config (Some "microsoft") "u-1"
            Assert.Equal(Some workA, refused)
        })
