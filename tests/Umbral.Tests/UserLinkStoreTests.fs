/// Removing one linked provider account from a user (specs/018, T001): the store
/// functions `UserStore.removeLink`, `isRemoved`, `credentialStillHeld` and
/// `linkedAccountDetails`, against a real database.
module Umbral.Tests.UserLinkStoreTests

open Xunit
open Umbral.Domains
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure

let private run (app: Fixtures.TestApp) (sql: string) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- sql
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

let private value (app: Fixtures.TestApp) (sql: string) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- sql
        let! result = command.ExecuteScalarAsync()
        return string result
    }

let private expectEq (expected: string) (actual: System.Threading.Tasks.Task<string>) =
    task {
        let! actualValue = actual
        Assert.Equal(expected, actualValue)
    }

let private addUser (app: Fixtures.TestApp) (id: string) (email: string) (verified: bool) =
    run
        app
        $"INSERT INTO users (id, display_name, email, email_verified, first_seen_utc, last_seen_utc)
          VALUES ('{id}', '{id}', '{email}', {(if verified then 1 else 0)}, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')"

let private addLink (app: Fixtures.TestApp) (kind: string) (subject: string) (userId: string) (email: string) (verified: bool) =
    run
        app
        $"INSERT INTO linked_accounts (provider_kind, provider_subject, user_id, asserted_email, asserted_email_verified, linked_utc, last_used_utc)
          VALUES ('{kind}', '{subject}', '{userId}', '{email}', {(if verified then 1 else 0)}, '2026-01-01T00:00:00Z', '2026-01-02T00:00:00Z')"

let private addPasskey (app: Fixtures.TestApp) (userId: string) =
    run
        app
        $"INSERT INTO passkeys (id, user_id, credential_id, public_key, nickname, created_utc)
          VALUES ('pk-{userId}', '{userId}', x'0102', x'AA', 'Phone', '2026-01-01T00:00:00Z')"

/// A passkey with an id of its own and the given origin (None: NULL).
let private addPasskeyVia (app: Fixtures.TestApp) (userId: string) (id: string) (via: string option) =
    task {
        do!
            run
                app
                $"INSERT INTO passkeys (id, user_id, credential_id, public_key, nickname, created_utc)
                  VALUES ('{id}', '{userId}', x'{System.Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes id)}', x'AA', 'P', '2026-01-01T00:00:00Z')"

        match via with
        | Some v -> do! run app $"UPDATE passkeys SET added_via = '{v}' WHERE id = '{id}'"
        | None -> ()
    }

let private connector (slug: string) (kind: ProviderKind) (issuer: string option) : Connector =
    { Slug = slug
      Kind = kind
      DisplayName = slug
      ClientId = "client-id"
      ClientSecretRef = ""
      ClientSecretHint = ""
      IssuerUrl = issuer
      AllowedEmailDomains = []
      PasskeyEnrolment = InvitationsOnly
      FediverseAccess = FediverseAccess.Any
      AppleKey = None
      MicrosoftAudience = None
      EmailTrust = (if kind = ProviderKind.Oidc then Some true else None)
      IsEnabled = true
      IsShared = true
      CreatedUtc = System.DateTimeOffset.UnixEpoch
      UpdatedUtc = System.DateTimeOffset.UnixEpoch }

let private addConnector (app: Fixtures.TestApp) (c: Connector) =
    task {
        let! created =
            ConnectorStore.create app.DbConfig (app.Clock.GetUtcNow()) c ("local:ciphertext", "cret")

        Assert.Equal(ConnectorStore.Created, created)
    }

let private fp = Linking.fingerprint

[<Fact>]
let ``linkedAccountDetails lists the links with their dates, oldest first`` () =
    task {
        use! app = Fixtures.createApp ()
        do! addUser app "u1" "a@example.com" true
        do! addLink app "github" "g-1" "u1" "a@example.com" true
        do! addLink app "google" "o-1" "u1" "a@example.com" true
        do! run app "UPDATE linked_accounts SET linked_utc = '2026-02-01T00:00:00Z' WHERE provider_kind = 'google'"

        let! links = UserStore.linkedAccountDetails app.DbConfig "u1"

        Assert.Equal<string list>([ "github"; "google" ], links |> List.map _.Kind)
        Assert.Equal<string list>([ "g-1"; "o-1" ], links |> List.map _.Subject)
        Assert.Equal(System.DateTimeOffset.Parse "2026-01-01T00:00:00Z", links.Head.LinkedUtc)
        Assert.Equal(System.DateTimeOffset.Parse "2026-01-02T00:00:00Z", links.Head.LastUsedUtc)
    }

[<Fact>]
let ``removeLink removes the link, records the provider account and keeps the user`` () =
    task {
        use! app = Fixtures.createApp ()
        do! addUser app "u1" "a@example.com" false
        do! addLink app "github" "g-1" "u1" "a@example.com" false
        do! addLink app "google" "o-1" "u1" "b@example.com" false

        let! outcome = UserStore.removeLink app.DbConfig app.Clock "u1" (fp "github" "g-1")

        Assert.Equal(UserStore.LinkRemoved("github", "g-1", false, 0), outcome)

        let! remaining = value app "SELECT group_concat(provider_kind) FROM linked_accounts WHERE user_id = 'u1'"
        Assert.Equal("google", remaining)

        let! recorded = value app "SELECT provider_kind || '|' || provider_subject || '|' || removed_utc FROM removed_links"
        Assert.Equal("github|g-1|" + app.Clock.GetUtcNow().ToString "o", recorded)

        let! users = value app "SELECT count(*) FROM users WHERE id = 'u1'"
        Assert.Equal("1", users)
    }

[<Fact>]
let ``removeLink refuses the last link when the user has no passkey and changes nothing`` () =
    task {
        use! app = Fixtures.createApp ()
        do! addUser app "u1" "a@example.com" true
        do! addLink app "github" "g-1" "u1" "a@example.com" true

        let! outcome = UserStore.removeLink app.DbConfig app.Clock "u1" (fp "github" "g-1")

        Assert.Equal(UserStore.LinkLastWayIn, outcome)
        do! expectEq "1" (value app "SELECT count(*) FROM linked_accounts")
        do! expectEq "0" (value app "SELECT count(*) FROM removed_links")
        do! expectEq "1" (value app "SELECT email_verified FROM users WHERE id = 'u1'")
    }

[<Fact>]
let ``removeLink allows the last link when the user has a passkey`` () =
    task {
        use! app = Fixtures.createApp ()
        do! addUser app "u1" "a@example.com" false
        do! addLink app "github" "g-1" "u1" "a@example.com" false
        do! addPasskey app "u1"

        let! outcome = UserStore.removeLink app.DbConfig app.Clock "u1" (fp "github" "g-1")

        Assert.Equal(UserStore.LinkRemoved("github", "g-1", false, 0), outcome)
        do! expectEq "0" (value app "SELECT count(*) FROM linked_accounts")
        do! expectEq "1" (value app "SELECT count(*) FROM removed_links")
    }

[<Fact>]
let ``removeLink says not found for an unknown fingerprint and for another user's link`` () =
    task {
        use! app = Fixtures.createApp ()
        do! addUser app "u1" "a@example.com" false
        do! addLink app "github" "g-1" "u1" "a@example.com" false
        do! addLink app "google" "o-1" "u1" "a@example.com" false
        do! addUser app "u2" "b@example.com" false
        do! addLink app "github" "g-2" "u2" "b@example.com" false
        do! addLink app "google" "o-2" "u2" "b@example.com" false

        let! unknown = UserStore.removeLink app.DbConfig app.Clock "u1" (fp "github" "nobody")
        Assert.Equal(UserStore.LinkNotFound, unknown)

        let! other = UserStore.removeLink app.DbConfig app.Clock "u1" (fp "github" "g-2")
        Assert.Equal(UserStore.LinkNotFound, other)

        do! expectEq "4" (value app "SELECT count(*) FROM linked_accounts")
        do! expectEq "0" (value app "SELECT count(*) FROM removed_links")
    }

[<Fact>]
let ``removeLink lowers a flag only the removed link backed`` () =
    task {
        use! app = Fixtures.createApp ()
        do! addUser app "u1" "a@example.com" true
        do! addLink app "github" "g-1" "u1" "a@example.com" true
        // The other link verifies nothing.
        do! addLink app "google" "o-1" "u1" "z@example.com" false

        let! outcome = UserStore.removeLink app.DbConfig app.Clock "u1" (fp "github" "g-1")

        Assert.Equal(UserStore.LinkRemoved("github", "g-1", true, 0), outcome)
        do! expectEq "0" (value app "SELECT email_verified FROM users WHERE id = 'u1'")
    }

[<Fact>]
let ``removeLink keeps a flag another link still backs`` () =
    task {
        use! app = Fixtures.createApp ()
        do! addUser app "u1" "a@example.com" true
        do! addLink app "github" "g-1" "u1" "a@example.com" true
        // Same address, other case: the comparison is the linking query's own.
        do! addLink app "google" "o-1" "u1" "A@example.com" true

        let! outcome = UserStore.removeLink app.DbConfig app.Clock "u1" (fp "github" "g-1")

        Assert.Equal(UserStore.LinkRemoved("github", "g-1", false, 0), outcome)
        do! expectEq "1" (value app "SELECT email_verified FROM users WHERE id = 'u1'")
    }

[<Fact>]
let ``the removed_links row outlives the user and isRemoved reads it`` () =
    task {
        use! app = Fixtures.createApp ()
        do! addUser app "u1" "a@example.com" false
        do! addLink app "github" "g-1" "u1" "a@example.com" false
        do! addLink app "google" "o-1" "u1" "a@example.com" false

        let! before = UserStore.isRemoved app.DbConfig "github" "g-1"
        Assert.False before

        let! _ = UserStore.removeLink app.DbConfig app.Clock "u1" (fp "github" "g-1")

        let! after = UserStore.isRemoved app.DbConfig "github" "g-1"
        Assert.True after

        // Another kind with the same subject, and another subject, are not removed.
        let! otherKind = UserStore.isRemoved app.DbConfig "google" "g-1"
        Assert.False otherKind
        let! otherSubject = UserStore.isRemoved app.DbConfig "github" "g-2"
        Assert.False otherSubject

        do! run app "PRAGMA foreign_keys = ON; DELETE FROM users WHERE id = 'u1'"

        do! expectEq "0" (value app "SELECT count(*) FROM users")
        let! survived = UserStore.isRemoved app.DbConfig "github" "g-1"
        Assert.True survived
    }

let private issuer = "https://auth.example.com/o"

let private seedHeld (app: Fixtures.TestApp) =
    task {
        do! addConnector app (connector "home" ProviderKind.Oidc (Some issuer))
        do! addConnector app (connector "discord" ProviderKind.Discord None)
        do! run app "INSERT INTO connectors (slug, kind, display_name, client_id, client_secret_ref, client_secret_hint, created_utc, updated_utc)
                     VALUES ('passkey', 'passkey', 'Passkey', '', '', '', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')"
        do! addUser app "u1" "a@example.com" false
        do! addLink app "oidc" $"{issuer}#1" "u1" "a@example.com" false
        do! addLink app "github" "g-1" "u1" "a@example.com" false
    }

[<Fact>]
let ``credentialStillHeld with no connector slug still judges a reference`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedHeld app

        // Nothing to judge: the connector re-checks own this case.
        let! none = UserStore.credentialStillHeld app.DbConfig "u1" None None
        Assert.True none

        // A reference is judged with or without a slug.
        let! notHeld = UserStore.credentialStillHeld app.DbConfig "u1" None (Some("link:" + fp "github" "nobody"))
        Assert.False notHeld

        let! held = UserStore.credentialStillHeld app.DbConfig "u1" None (Some("link:" + fp "github" "g-1"))
        Assert.True held
    }

[<Fact>]
let ``credentialStillHeld with a fingerprint is true only when the user holds that link`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedHeld app

        let! held = UserStore.credentialStillHeld app.DbConfig "u1" (Some "discord") (Some("link:" + fp "github" "g-1"))
        Assert.True held

        let! heldOidc = UserStore.credentialStillHeld app.DbConfig "u1" (Some "home") (Some("link:" + fp "oidc" $"{issuer}#1"))
        Assert.True heldOidc

        let! notHeld = UserStore.credentialStillHeld app.DbConfig "u1" (Some "discord") (Some("link:" + fp "github" "g-2"))
        Assert.False notHeld

        // Removed: the fingerprint is no longer the user's.
        let! _ = UserStore.removeLink app.DbConfig app.Clock "u1" (fp "github" "g-1")
        let! afterRemoval = UserStore.credentialStillHeld app.DbConfig "u1" (Some "discord") (Some("link:" + fp "github" "g-1"))
        Assert.False afterRemoval
    }

[<Fact>]
let ``credentialStillHeld with no fingerprint judges an oidc connector by its issuer`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedHeld app

        let! held = UserStore.credentialStillHeld app.DbConfig "u1" (Some "home") None
        Assert.True held

        do! addUser app "u2" "b@example.com" false
        // An oidc link of another issuer, and the same prefix without the `#`.
        do! addLink app "oidc" "https://other.example.com#1" "u2" "b@example.com" false
        do! addLink app "oidc" $"{issuer}x#1" "u2" "b@example.com" false

        let! otherIssuer = UserStore.credentialStillHeld app.DbConfig "u2" (Some "home") None
        Assert.False otherIssuer
    }

[<Fact>]
let ``credentialStillHeld with no fingerprint judges another kind by a link of that kind`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedHeld app

        // u1 has a github link but the connector is discord.
        let! missing = UserStore.credentialStillHeld app.DbConfig "u1" (Some "discord") None
        Assert.False missing

        do! addLink app "discord" "d-1" "u1" "a@example.com" false
        let! held = UserStore.credentialStillHeld app.DbConfig "u1" (Some "discord") None
        Assert.True held
    }

[<Fact>]
let ``credentialStillHeld with no fingerprint admits the passkey connector and an unknown slug`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedHeld app
        do! addUser app "u3" "c@example.com" false

        let! passkey = UserStore.credentialStillHeld app.DbConfig "u3" (Some "passkey") None
        Assert.True passkey

        let! unknown = UserStore.credentialStillHeld app.DbConfig "u3" (Some "deleted") None
        Assert.True unknown
    }

[<Fact>]
let ``removeLink removes passkeys added via the link, transitively, and counts them`` () =
    task {
        use! app = Fixtures.createApp ()
        do! addUser app "u1" "a@example.com" false
        do! addLink app "github" "g-1" "u1" "a@example.com" false
        do! addLink app "google" "o-1" "u1" "b@example.com" false
        let gh = "link:" + fp "github" "g-1"
        do! addPasskeyVia app "u1" "a" (Some gh)
        do! addPasskeyVia app "u1" "b" (Some "passkey:a")
        do! addPasskeyVia app "u1" "c" None
        do! addPasskeyVia app "u1" "d" (Some("link:" + fp "google" "o-1"))
        do! addPasskeyVia app "u1" "e" (Some "passkey:d")

        let! outcome = UserStore.removeLink app.DbConfig app.Clock "u1" (fp "github" "g-1")

        Assert.Equal(UserStore.LinkRemoved("github", "g-1", false, 2), outcome)
        do! expectEq "c,d,e" (value app "SELECT group_concat(id) FROM (SELECT id FROM passkeys ORDER BY id)")
    }

[<Fact>]
let ``removeLink refuses as last way in when its passkey is the only one and rolls it back`` () =
    task {
        use! app = Fixtures.createApp ()
        do! addUser app "u1" "a@example.com" false
        do! addLink app "github" "g-1" "u1" "a@example.com" false
        do! addPasskeyVia app "u1" "a" (Some("link:" + fp "github" "g-1"))

        let! outcome = UserStore.removeLink app.DbConfig app.Clock "u1" (fp "github" "g-1")

        Assert.Equal(UserStore.LinkLastWayIn, outcome)
        do! expectEq "1" (value app "SELECT count(*) FROM passkeys")
        do! expectEq "1" (value app "SELECT count(*) FROM linked_accounts")
        do! expectEq "0" (value app "SELECT count(*) FROM removed_links")
    }

[<Fact>]
let ``credentialStillHeld with a passkey reference is true only for the user's own passkey`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedHeld app
        do! addUser app "u2" "b@example.com" false
        do! addPasskeyVia app "u1" "pk1" None

        let! held = UserStore.credentialStillHeld app.DbConfig "u1" (Some "passkey") (Some "passkey:pk1")
        Assert.True held
        let! other = UserStore.credentialStillHeld app.DbConfig "u2" (Some "passkey") (Some "passkey:pk1")
        Assert.False other
        let! missing = UserStore.credentialStillHeld app.DbConfig "u1" (Some "passkey") (Some "passkey:nope")
        Assert.False missing
        // Judged without a slug too: another user's passkey never counts.
        let! noSlug = UserStore.credentialStillHeld app.DbConfig "u2" None (Some "passkey:pk1")
        Assert.False noSlug
    }

[<Fact>]
let ``credentialStillHeld refuses a value that does not parse`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedHeld app

        for bad in [ ""; "link:"; "passkey:"; "garbage"; "LINK:x" ] do
            let! r = UserStore.credentialStillHeld app.DbConfig "u1" (Some "passkey") (Some bad)
            Assert.False r
    }

// ---------------------------------------------------------------------------
// FR-013: a removal ends the sessions from before schema 15 (legacy_sessions_ended)
// ---------------------------------------------------------------------------

let private endedRows (app: Fixtures.TestApp) (userId: string) =
    value app $"SELECT count(*) FROM legacy_sessions_ended WHERE user_id = '{userId}'"

[<Fact>]
let ``removeLink records the user in legacy_sessions_ended with the removal's time`` () =
    task {
        use! app = Fixtures.createApp ()
        do! addUser app "u1" "a@example.com" false
        do! addLink app "github" "g-1" "u1" "a@example.com" false
        do! addLink app "google" "o-1" "u1" "a@example.com" false

        do! expectEq "0" (endedRows app "u1")

        let! _ = UserStore.removeLink app.DbConfig app.Clock "u1" (fp "github" "g-1")

        do! expectEq "1" (endedRows app "u1")
        let! at = value app "SELECT ended_utc FROM legacy_sessions_ended WHERE user_id = 'u1'"
        Assert.Equal(app.Clock.GetUtcNow().ToString "o", at)

        // A second removal does not fail on the existing row and keeps one.
        let! _ = UserStore.removeLink app.DbConfig app.Clock "u1" (fp "google" "o-1")
        do! expectEq "1" (endedRows app "u1")
    }

[<Fact>]
let ``a refused removeLink records nothing, for the last way in and for a link not found`` () =
    task {
        use! app = Fixtures.createApp ()
        do! addUser app "u1" "a@example.com" false
        do! addLink app "github" "g-1" "u1" "a@example.com" false
        do! addUser app "u2" "b@example.com" false
        do! addLink app "github" "g-2" "u2" "b@example.com" false

        let! last = UserStore.removeLink app.DbConfig app.Clock "u1" (fp "github" "g-1")
        Assert.Equal(UserStore.LinkLastWayIn, last)

        // u2's link named by u1: not found.
        let! notFound = UserStore.removeLink app.DbConfig app.Clock "u1" (fp "github" "g-2")
        Assert.Equal(UserStore.LinkNotFound, notFound)

        do! expectEq "0" (value app "SELECT count(*) FROM legacy_sessions_ended")
    }

[<Fact>]
let ``the operator's passkey removal records the owner, and only when a passkey was removed`` () =
    task {
        use! app = Fixtures.createApp ()
        do! addUser app "u1" "a@example.com" false
        do! addPasskeyVia app "u1" "pk-1" None
        do! addUser app "u2" "b@example.com" false

        let! missing = Umbral.Infrastructure.Passkeys.remove app.DbConfig app.Clock "nope"
        Assert.False missing
        do! expectEq "0" (value app "SELECT count(*) FROM legacy_sessions_ended")

        let! removed = Umbral.Infrastructure.Passkeys.remove app.DbConfig app.Clock "pk-1"
        Assert.True removed

        do! expectEq "1" (endedRows app "u1")
        do! expectEq "0" (endedRows app "u2")
        let! at = value app "SELECT ended_utc FROM legacy_sessions_ended WHERE user_id = 'u1'"
        Assert.Equal(app.Clock.GetUtcNow().ToString "o", at)
    }

[<Fact>]
let ``a person's own passkey removal records them, and a refused one records nothing`` () =
    task {
        use! app = Fixtures.createApp ()
        do! addUser app "u1" "a@example.com" false
        do! addPasskeyVia app "u1" "pk-1" None
        do! addPasskeyVia app "u1" "pk-2" None
        do! addUser app "u2" "b@example.com" false
        do! addPasskeyVia app "u2" "pk-3" None

        // Not found (someone else's id, an unknown id) and last way in (u2's only passkey).
        let! others = Umbral.Infrastructure.Passkeys.removeOwnIfNotLast app.DbConfig app.Clock "u2" "pk-1"
        let! unknown = Umbral.Infrastructure.Passkeys.removeOwnIfNotLast app.DbConfig app.Clock "u1" "nope"
        let! last = Umbral.Infrastructure.Passkeys.removeOwnIfNotLast app.DbConfig app.Clock "u2" "pk-3"
        Assert.Equal(Umbral.Infrastructure.Passkeys.NotFound, others)
        Assert.Equal(Umbral.Infrastructure.Passkeys.NotFound, unknown)
        Assert.Equal(Umbral.Infrastructure.Passkeys.LastWayIn, last)
        do! expectEq "0" (value app "SELECT count(*) FROM legacy_sessions_ended")

        let! removed = Umbral.Infrastructure.Passkeys.removeOwnIfNotLast app.DbConfig app.Clock "u1" "pk-1"
        Assert.Equal(Umbral.Infrastructure.Passkeys.Removed, removed)

        do! expectEq "1" (endedRows app "u1")
        do! expectEq "0" (endedRows app "u2")
    }

[<Fact>]
let ``credentialStillHeld refuses a claimless session of a recorded user through every kind of connector`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedHeld app
        do! addLink app "discord" "d-1" "u1" "a@example.com" false

        // Before the record the fallback admits all of them: the user holds links of these kinds.
        let! oidcBefore = UserStore.credentialStillHeld app.DbConfig "u1" (Some "home") None
        let! discordBefore = UserStore.credentialStillHeld app.DbConfig "u1" (Some "discord") None
        let! passkeyBefore = UserStore.credentialStillHeld app.DbConfig "u1" (Some "passkey") None
        Assert.True oidcBefore
        Assert.True discordBefore
        Assert.True passkeyBefore

        do! run app "INSERT INTO legacy_sessions_ended (user_id, ended_utc) VALUES ('u1', '2026-01-03T00:00:00Z')"

        let! oidc = UserStore.credentialStillHeld app.DbConfig "u1" (Some "home") None
        let! discord = UserStore.credentialStillHeld app.DbConfig "u1" (Some "discord") None
        let! passkey = UserStore.credentialStillHeld app.DbConfig "u1" (Some "passkey") None
        Assert.False oidc
        Assert.False discord
        Assert.False passkey

        // The record is per user: another user's claimless session keeps the fallback.
        do! addUser app "u4" "d@example.com" false
        do! addLink app "discord" "d-4" "u4" "d@example.com" false
        let! other = UserStore.credentialStillHeld app.DbConfig "u4" (Some "discord") None
        Assert.True other
    }

[<Fact>]
let ``credentialStillHeld judges a credential reference whatever the record says`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedHeld app
        do! addPasskeyVia app "u1" "pk-1" None
        do! run app "INSERT INTO legacy_sessions_ended (user_id, ended_utc) VALUES ('u1', '2026-01-03T00:00:00Z')"

        let! link = UserStore.credentialStillHeld app.DbConfig "u1" (Some "discord") (Some("link:" + fp "github" "g-1"))
        let! gone = UserStore.credentialStillHeld app.DbConfig "u1" (Some "discord") (Some("link:" + fp "github" "g-2"))
        let! passkey = UserStore.credentialStillHeld app.DbConfig "u1" (Some "passkey") (Some "passkey:pk-1")
        let! noSlug = UserStore.credentialStillHeld app.DbConfig "u1" None (Some("link:" + fp "github" "g-1"))
        Assert.True link
        Assert.False gone
        Assert.True passkey
        Assert.True noSlug
    }

[<Fact>]
let ``deleting the user deletes their legacy_sessions_ended row`` () =
    task {
        use! app = Fixtures.createApp ()
        do! addUser app "u1" "a@example.com" false
        do! addLink app "github" "g-1" "u1" "a@example.com" false
        do! addLink app "google" "o-1" "u1" "a@example.com" false
        let! _ = UserStore.removeLink app.DbConfig app.Clock "u1" (fp "github" "g-1")
        do! expectEq "1" (endedRows app "u1")

        do! run app "PRAGMA foreign_keys = ON; DELETE FROM users WHERE id = 'u1'"

        do! expectEq "0" (value app "SELECT count(*) FROM legacy_sessions_ended")
    }
