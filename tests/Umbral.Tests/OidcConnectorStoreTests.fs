/// What the connector store does with an issuer (specs/002 T010): it is written
/// once with the connector, read back, found by, and never rewritten; and two
/// connectors cannot hold one issuer.
module Umbral.Tests.OidcConnectorStoreTests

open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure

let private connector (slug: string) (kind: ProviderKind) (issuer: string option) : Connector =
    { Slug = slug
      Kind = kind
      DisplayName = "Home"
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

let private secret = "local:ciphertext", "cret"

[<Fact>]
let ``an issuer is stored with the connector, read back and found by`` () =
    task {
        use! app = Fixtures.createApp ()
        let issuer = "https://auth.example.com/application/o/umbral"

        let! created = ConnectorStore.create app.DbConfig (app.Clock.GetUtcNow()) (connector "home" Oidc (Some issuer)) secret
        Assert.Equal(ConnectorStore.Created, created)

        let! bySlug = ConnectorStore.findBySlug app.DbConfig "home"
        Assert.Equal(Some issuer, bySlug.Value.IssuerUrl)
        Assert.Equal(Oidc, bySlug.Value.Kind)

        let! byIssuer = ConnectorStore.findByIssuer app.DbConfig issuer
        Assert.Equal(Some "home", byIssuer |> Option.map _.Slug)

        let! unknown = ConnectorStore.findByIssuer app.DbConfig "https://other.example.com"
        Assert.True unknown.IsNone
    }

[<Fact>]
let ``a connector of another kind has no issuer`` () =
    task {
        use! app = Fixtures.createApp ()

        let! _ = ConnectorStore.create app.DbConfig (app.Clock.GetUtcNow()) (connector "discord-two" Discord None) secret
        let! stored = ConnectorStore.findBySlug app.DbConfig "discord-two"
        Assert.Equal(None, stored.Value.IssuerUrl)
    }

[<Fact>]
let ``a second connector on the same issuer is refused as issuer taken and nothing is written`` () =
    task {
        use! app = Fixtures.createApp ()
        let issuer = "https://auth.example.com/o"

        let! _ = ConnectorStore.create app.DbConfig (app.Clock.GetUtcNow()) (connector "home" Oidc (Some issuer)) secret
        let! second = ConnectorStore.create app.DbConfig (app.Clock.GetUtcNow()) (connector "work" Oidc (Some issuer)) secret

        Assert.Equal(ConnectorStore.IssuerTaken, second)

        let! stored = ConnectorStore.findBySlug app.DbConfig "work"
        Assert.True stored.IsNone
    }

[<Fact>]
let ``a taken slug is still reported as slug taken, not issuer taken`` () =
    task {
        use! app = Fixtures.createApp ()

        let! _ = ConnectorStore.create app.DbConfig (app.Clock.GetUtcNow()) (connector "home" Oidc (Some "https://one.example.com")) secret
        let! again = ConnectorStore.create app.DbConfig (app.Clock.GetUtcNow()) (connector "home" Oidc (Some "https://two.example.com")) secret

        Assert.Equal(ConnectorStore.SlugTaken, again)

        // The first connector is untouched, issuer included.
        let! stored = ConnectorStore.findBySlug app.DbConfig "home"
        Assert.Equal(Some "https://one.example.com", stored.Value.IssuerUrl)
    }

[<Fact>]
let ``saving a connector again never changes its issuer`` () =
    task {
        use! app = Fixtures.createApp ()

        let! _ = ConnectorStore.create app.DbConfig (app.Clock.GetUtcNow()) (connector "home" Oidc (Some "https://one.example.com")) secret

        // The issuer is part of every person's identity: a save that carries a
        // different one must not move it.
        do!
            ConnectorStore.save
                app.DbConfig
                app.Clock
                { connector "home" Oidc (Some "https://two.example.com") with DisplayName = "Renamed" }
                None

        let! stored = ConnectorStore.findBySlug app.DbConfig "home"
        Assert.Equal("Renamed", stored.Value.DisplayName)
        Assert.Equal(Some "https://one.example.com", stored.Value.IssuerUrl)
    }

// ---------------------------------------------------------------------------
// The passkey kind's enrolment mode (spec 008 T007)
// ---------------------------------------------------------------------------

let private passkeyConnector (enrolment: PasskeyEnrolment) : Connector =
    { connector "passkey" Passkey None with
        DisplayName = "Passkey"
        ClientId = ""
        PasskeyEnrolment = enrolment }

[<Fact>]
let ``a saved passkey connector reads back with its kind and its enrolment`` () =
    task {
        use! app = Fixtures.createApp ()

        do! ConnectorStore.save app.DbConfig app.Clock (passkeyConnector OpenSignUp) None
        let! stored = ConnectorStore.findBySlug app.DbConfig "passkey"

        Assert.Equal(Passkey, stored.Value.Kind)
        Assert.Equal(OpenSignUp, stored.Value.PasskeyEnrolment)
        Assert.Equal("", stored.Value.ClientId)
        Assert.Equal("", stored.Value.ClientSecretRef)
        Assert.Equal(None, stored.Value.IssuerUrl)
        Assert.Empty stored.Value.AllowedEmailDomains
    }

[<Fact>]
let ``creating a passkey connector writes its enrolment, and the default is invitations only`` () =
    task {
        use! app = Fixtures.createApp ()

        let! created =
            ConnectorStore.create app.DbConfig (app.Clock.GetUtcNow()) (passkeyConnector InvitationsOnly) ("", "")

        Assert.Equal(ConnectorStore.Created, created)
        let! stored = ConnectorStore.findBySlug app.DbConfig "passkey"
        Assert.Equal(InvitationsOnly, stored.Value.PasskeyEnrolment)

        // A connector of another kind is read as invitations only too.
        let! _ = ConnectorStore.create app.DbConfig (app.Clock.GetUtcNow()) (connector "discord" Discord None) secret
        let! discord = ConnectorStore.findBySlug app.DbConfig "discord"
        Assert.Equal(InvitationsOnly, discord.Value.PasskeyEnrolment)
    }

[<Fact>]
let ``setEnrolment changes the mode and nothing else, and says whether the connector exists`` () =
    task {
        use! app = Fixtures.createApp ()
        do! ConnectorStore.save app.DbConfig app.Clock (passkeyConnector InvitationsOnly) None
        let! before = ConnectorStore.findBySlug app.DbConfig "passkey"

        let! changed = ConnectorStore.setEnrolment app.DbConfig app.Clock "passkey" OpenSignUp
        Assert.True changed

        let! after = ConnectorStore.findBySlug app.DbConfig "passkey"
        Assert.Equal(OpenSignUp, after.Value.PasskeyEnrolment)
        Assert.Equal({ before.Value with PasskeyEnrolment = OpenSignUp; UpdatedUtc = after.Value.UpdatedUtc }, after.Value)

        let! missing = ConnectorStore.setEnrolment app.DbConfig app.Clock "nobody" OpenSignUp
        Assert.False missing
    }

[<Fact>]
let ``saving the form of another kind and updating a passkey connector keep its enrolment`` () =
    task {
        use! app = Fixtures.createApp ()
        do! ConnectorStore.save app.DbConfig app.Clock (passkeyConnector OpenSignUp) None

        // `update` is the generic card's write: it has no enrolment to say.
        let! _ = ConnectorStore.update app.DbConfig app.Clock "passkey" "Passkey" "" false None None None
        let! stored = ConnectorStore.findBySlug app.DbConfig "passkey"

        Assert.Equal(OpenSignUp, stored.Value.PasskeyEnrolment)
        Assert.False stored.Value.IsShared
    }

// ---------------------------------------------------------------------------
// The fediverse kind's server access (spec 010 T006-T007)
// ---------------------------------------------------------------------------

let private fediverseConnector (access: FediverseAccess) : Connector =
    { connector "fediverse" Fediverse None with
        DisplayName = "Fediverse"
        ClientId = ""
        FediverseAccess = access }

[<Fact>]
let ``a saved fediverse connector reads back its access, listed with two servers and listed with none`` () =
    task {
        use! app = Fixtures.createApp ()

        let two = { OnlyListed = true; Servers = [ "mastodon.social"; "fosstodon.org" ] }
        do! ConnectorStore.save app.DbConfig app.Clock (fediverseConnector two) None
        let! stored = ConnectorStore.findBySlug app.DbConfig "fediverse"
        Assert.Equal(Fediverse, stored.Value.Kind)
        Assert.Equal(two, stored.Value.FediverseAccess)

        // An empty list under `listed` is kept as listed: nobody can sign in.
        let none = { OnlyListed = true; Servers = [] }
        do! ConnectorStore.save app.DbConfig app.Clock (fediverseConnector none) None
        let! empty = ConnectorStore.findBySlug app.DbConfig "fediverse"
        Assert.Equal(none, empty.Value.FediverseAccess)

        do! ConnectorStore.save app.DbConfig app.Clock (fediverseConnector FediverseAccess.Any) None
        let! any = ConnectorStore.findBySlug app.DbConfig "fediverse"
        Assert.Equal(FediverseAccess.Any, any.Value.FediverseAccess)
    }

[<Fact>]
let ``setFediverseAccess changes the access and nothing else`` () =
    task {
        use! app = Fixtures.createApp ()
        do! ConnectorStore.save app.DbConfig app.Clock (fediverseConnector FediverseAccess.Any) None
        let! before = ConnectorStore.findBySlug app.DbConfig "fediverse"

        let listed = { OnlyListed = true; Servers = [ "mastodon.social"; "fosstodon.org" ] }
        do! ConnectorStore.setFediverseAccess app.DbConfig app.Clock "fediverse" listed

        let! after = ConnectorStore.findBySlug app.DbConfig "fediverse"
        Assert.Equal(listed, after.Value.FediverseAccess)
        Assert.Equal({ before.Value with FediverseAccess = listed; UpdatedUtc = after.Value.UpdatedUtc }, after.Value)

        let empty = { OnlyListed = true; Servers = [] }
        do! ConnectorStore.setFediverseAccess app.DbConfig app.Clock "fediverse" empty
        let! cleared = ConnectorStore.findBySlug app.DbConfig "fediverse"
        Assert.Equal(empty, cleared.Value.FediverseAccess)

        do! ConnectorStore.setFediverseAccess app.DbConfig app.Clock "fediverse" FediverseAccess.Any
        let! back = ConnectorStore.findBySlug app.DbConfig "fediverse"
        Assert.Equal(FediverseAccess.Any, back.Value.FediverseAccess)
    }

[<Fact>]
let ``an unknown stored scope word reads as only listed, the closed way`` () =
    task {
        use! app = Fixtures.createApp ()
        do! ConnectorStore.save app.DbConfig app.Clock (fediverseConnector FediverseAccess.Any) None

        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- "UPDATE connectors SET fediverse_scope = 'everyone' WHERE slug = 'fediverse'"
        let! _ = command.ExecuteNonQueryAsync()

        let! stored = ConnectorStore.findBySlug app.DbConfig "fediverse"
        Assert.True stored.Value.FediverseAccess.OnlyListed
    }

// ---------------------------------------------------------------------------
// The Apple key ids (spec 013 T002)
// ---------------------------------------------------------------------------

let private appleConnector (key: AppleKeyIds) : Connector =
    { connector "apple" Apple None with
        DisplayName = "Apple"
        ClientId = "com.example.signin"
        AppleKey = Some key }

[<Fact>]
let ``an apple connector is created with its Team ID and Key ID and read back equal`` () =
    task {
        use! app = Fixtures.createApp ()
        let key = { TeamId = "DEF123GHIJ"; KeyId = "ABC123DEFG" }
        let created = appleConnector key

        let! result = ConnectorStore.create app.DbConfig (app.Clock.GetUtcNow()) created secret
        Assert.Equal(ConnectorStore.Created, result)

        let! stored = ConnectorStore.findBySlug app.DbConfig "apple"

        let expected =
            { created with
                ClientSecretRef = fst secret
                ClientSecretHint = snd secret
                CreatedUtc = stored.Value.CreatedUtc
                UpdatedUtc = stored.Value.UpdatedUtc }

        Assert.Equal(expected, stored.Value)
        Assert.Equal(Some key, stored.Value.AppleKey)
    }

[<Fact>]
let ``updating an apple connector with new ids changes the Key ID and leaving them out keeps it`` () =
    task {
        use! app = Fixtures.createApp ()
        let key = { TeamId = "DEF123GHIJ"; KeyId = "ABC123DEFG" }
        let! _ = ConnectorStore.create app.DbConfig (app.Clock.GetUtcNow()) (appleConnector key) secret

        let rotated = { key with KeyId = "ZYX987WVUT" }
        let! changed = ConnectorStore.update app.DbConfig app.Clock "apple" "Apple" "com.example.signin" true None None (Some rotated)
        Assert.True changed

        let! after = ConnectorStore.findBySlug app.DbConfig "apple"
        Assert.Equal(Some rotated, after.Value.AppleKey)

        // A write that does not carry the ids (the display name alone) keeps them.
        let! _ = ConnectorStore.update app.DbConfig app.Clock "apple" "Apple ID" "com.example.signin" true None None None
        let! kept = ConnectorStore.findBySlug app.DbConfig "apple"
        Assert.Equal(Some rotated, kept.Value.AppleKey)
    }

[<Fact>]
let ``saving an apple connector writes and replaces its ids`` () =
    task {
        use! app = Fixtures.createApp ()
        let key = { TeamId = "DEF123GHIJ"; KeyId = "ABC123DEFG" }
        do! ConnectorStore.save app.DbConfig app.Clock (appleConnector key) (Some secret)

        let rotated = { key with TeamId = "KLM456NOPQ" }
        do! ConnectorStore.save app.DbConfig app.Clock (appleConnector rotated) None

        let! stored = ConnectorStore.findBySlug app.DbConfig "apple"
        Assert.Equal(Some rotated, stored.Value.AppleKey)
    }

[<Fact>]
let ``a connector of another kind reads no Apple key`` () =
    task {
        use! app = Fixtures.createApp ()

        let! _ = ConnectorStore.create app.DbConfig (app.Clock.GetUtcNow()) (connector "google" Google None) secret
        let! stored = ConnectorStore.findBySlug app.DbConfig "google"
        Assert.Equal(None, stored.Value.AppleKey)
    }

// ---------------------------------------------------------------------------
// Trust in the provider's verified emails (specs/017 T001).
// ---------------------------------------------------------------------------

let private issuerA = "https://auth.example.com/o"
let private issuerB = "https://other.example.com/o"

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

let private oidcWithTrust (slug: string) (issuer: string) (trust: bool option) : Connector =
    { connector slug Oidc (Some issuer) with EmailTrust = trust }

let private createOidc (app: Fixtures.TestApp) (slug: string) (issuer: string) (trust: bool option) =
    task {
        let! created =
            ConnectorStore.create app.DbConfig (app.Clock.GetUtcNow()) (oidcWithTrust slug issuer trust) secret

        Assert.Equal(ConnectorStore.Created, created)
    }

let private saveOidc (app: Fixtures.TestApp) (slug: string) (name: string) (trust: bool) =
    ConnectorStore.saveOidc app.DbConfig app.Clock slug name "client-id" true None None trust

let private addUser (app: Fixtures.TestApp) (id: string) (email: string) (verified: bool) =
    run
        app
        $"INSERT INTO users (id, display_name, email, email_verified, first_seen_utc, last_seen_utc)
          VALUES ('{id}', '{id}', '{email}', {(if verified then 1 else 0)}, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')"

let private addLink (app: Fixtures.TestApp) (kind: string) (subject: string) (userId: string) (email: string) (verified: bool) =
    run
        app
        $"INSERT INTO linked_accounts (provider_kind, provider_subject, user_id, asserted_email, asserted_email_verified, linked_utc, last_used_utc)
          VALUES ('{kind}', '{subject}', '{userId}', '{email}', {(if verified then 1 else 0)}, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')"

/// Two OpenID Connect connectors, both trusting. On issuer A: `only` (verified
/// through A's link alone), `shared` (A's link and a google link both verify the
/// address) and `unverified` (never verified). On issuer B: `elsewhere`.
let private seedPeople (app: Fixtures.TestApp) =
    task {
        do! createOidc app "home" issuerA (Some true)
        do! createOidc app "work" issuerB (Some true)

        do! addUser app "only" "only@example.com" true
        do! addLink app "oidc" $"{issuerA}#1" "only" "only@example.com" true

        do! addUser app "shared" "shared@example.com" true
        do! addLink app "oidc" $"{issuerA}#2" "shared" "shared@example.com" true
        do! addLink app "google" "g-2" "shared" "SHARED@example.com" true

        do! addUser app "unverified" "u@example.com" false
        do! addLink app "oidc" $"{issuerA}#3" "unverified" "u@example.com" false

        do! addUser app "elsewhere" "else@example.com" true
        do! addLink app "oidc" $"{issuerB}#9" "elsewhere" "else@example.com" true
    }

let private expectEq (expected: string) (actual: System.Threading.Tasks.Task<string>) =
    task {
        let! actualValue = actual
        Assert.Equal(expected, actualValue)
    }

let private flag (app: Fixtures.TestApp) (userId: string) =
    value app $"SELECT email_verified FROM users WHERE id = '{userId}'"

let private linkFlag (app: Fixtures.TestApp) (subject: string) =
    value app $"SELECT asserted_email_verified FROM linked_accounts WHERE provider_subject = '{subject}'"

[<Fact>]
let ``the email trust of an oidc connector is stored on and off and read back`` () =
    task {
        use! app = Fixtures.createApp ()
        do! createOidc app "on" "https://on.example.com" (Some true)
        do! createOidc app "off" "https://off.example.com" (Some false)

        let! on = ConnectorStore.findBySlug app.DbConfig "on"
        Assert.Equal(Some true, on.Value.EmailTrust)
        Assert.True(trustsProviderEmail on.Value)

        let! off = ConnectorStore.findBySlug app.DbConfig "off"
        Assert.Equal(Some false, off.Value.EmailTrust)
        Assert.False(trustsProviderEmail off.Value)

        let! raw = value app "SELECT group_concat(slug || '=' || email_trust, ',') FROM (SELECT * FROM connectors ORDER BY slug)"
        Assert.Equal("off=0,on=1", raw)
    }

[<Fact>]
let ``a null email trust on an oidc row reads as off`` () =
    task {
        use! app = Fixtures.createApp ()
        do! createOidc app "home" issuerA (Some true)
        do! run app "UPDATE connectors SET email_trust = NULL WHERE slug = 'home'"

        let! stored = ConnectorStore.findBySlug app.DbConfig "home"
        Assert.Equal(None, stored.Value.EmailTrust)
        Assert.False(trustsProviderEmail stored.Value)
    }

[<Fact>]
let ``a connector of another kind reads no email trust and trusts its provider's email`` () =
    task {
        use! app = Fixtures.createApp ()
        let! _ = ConnectorStore.create app.DbConfig (app.Clock.GetUtcNow()) (connector "google" Google None) secret
        // A stray value on a row that is not oidc is ignored.
        do! run app "UPDATE connectors SET email_trust = 0 WHERE slug = 'google'"

        let! stored = ConnectorStore.findBySlug app.DbConfig "google"
        Assert.Equal(None, stored.Value.EmailTrust)
        Assert.True(trustsProviderEmail stored.Value)

        let! raw = value app "SELECT count(*) FROM connectors WHERE email_trust IS NULL"
        Assert.Equal("0", raw)
    }

[<Fact>]
let ``creating a connector of another kind stores a null email trust`` () =
    task {
        use! app = Fixtures.createApp ()
        let! _ = ConnectorStore.create app.DbConfig (app.Clock.GetUtcNow()) (connector "google" Google None) secret
        let! raw = value app "SELECT count(*) FROM connectors WHERE slug = 'google' AND email_trust IS NULL"
        Assert.Equal("1", raw)
    }

[<Fact>]
let ``saveOidc off lowers the user only this connector verified and keeps the rest`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedPeople app

        let! result = saveOidc app "home" "Renamed" false
        Assert.Equal(Some 1, result)

        let! stored = ConnectorStore.findBySlug app.DbConfig "home"
        Assert.Equal(Some false, stored.Value.EmailTrust)
        Assert.Equal("Renamed", stored.Value.DisplayName)

        // Verified through this connector alone: lowered, and so is its link.
        do! expectEq "0" (flag app "only")
        do! expectEq "0" (linkFlag app $"{issuerA}#1")

        // Another link (google) still verifies the stored address, case-insensitively.
        do! expectEq "1" (flag app "shared")
        do! expectEq "0" (linkFlag app $"{issuerA}#2")
        do! expectEq "1" (linkFlag app "g-2")

        // Never verified: unchanged, and not counted.
        do! expectEq "0" (flag app "unverified")

        // Another connector's issuer: untouched.
        do! expectEq "1" (flag app "elsewhere")
        do! expectEq "1" (linkFlag app $"{issuerB}#9")
        let! other = ConnectorStore.findBySlug app.DbConfig "work"
        Assert.Equal(Some true, other.Value.EmailTrust)
    }

[<Fact>]
let ``saveOidc off from a null stored value lowers as from on`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedPeople app
        do! run app "UPDATE connectors SET email_trust = NULL WHERE slug = 'home'"

        let! result = saveOidc app "home" "Home" false
        Assert.Equal(Some 1, result)
        do! expectEq "0" (flag app "only")
    }

[<Fact>]
let ``saveOidc on to on and off to off lower nothing`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedPeople app

        let! onToOn = saveOidc app "home" "Home" true
        Assert.Equal(Some 0, onToOn)
        do! expectEq "1" (flag app "only")
        do! expectEq "1" (linkFlag app $"{issuerA}#1")

        // Turn it off by hand, then raise the flag again: saving off when the
        // stored value is already off must not touch it.
        do! run app "UPDATE connectors SET email_trust = 0 WHERE slug = 'home'"

        let! offToOff = saveOidc app "home" "Home" false
        Assert.Equal(Some 0, offToOff)
        do! expectEq "1" (flag app "only")
        do! expectEq "1" (linkFlag app $"{issuerA}#1")

        // And off to on changes the setting and nothing else.
        let! offToOn = saveOidc app "home" "Home" true
        Assert.Equal(Some 0, offToOn)
        let! stored = ConnectorStore.findBySlug app.DbConfig "home"
        Assert.Equal(Some true, stored.Value.EmailTrust)
        do! expectEq "1" (flag app "only")
    }

[<Fact>]
let ``saveOidc finds no connector of another kind or an unknown slug`` () =
    task {
        use! app = Fixtures.createApp ()
        let! _ = ConnectorStore.create app.DbConfig (app.Clock.GetUtcNow()) (connector "google" Google None) secret

        let! google = saveOidc app "google" "Changed" false
        Assert.Equal(None, google)
        let! unknown = saveOidc app "nope" "Changed" false
        Assert.Equal(None, unknown)

        let! stored = ConnectorStore.findBySlug app.DbConfig "google"
        Assert.Equal("Home", stored.Value.DisplayName)
    }

[<Fact>]
let ``saveOidc replaces a secret only when one is given and honours the domains option`` () =
    task {
        use! app = Fixtures.createApp ()
        do! createOidc app "home" issuerA (Some true)

        let! kept = ConnectorStore.saveOidc app.DbConfig app.Clock "home" "Home" "client-id" true (Some [ "example.com" ]) None true
        Assert.Equal(Some 0, kept)
        let! first = ConnectorStore.findBySlug app.DbConfig "home"
        Assert.Equal<string list>([ "example.com" ], first.Value.AllowedEmailDomains)
        Assert.Equal("local:ciphertext", first.Value.ClientSecretRef)

        let! replaced =
            ConnectorStore.saveOidc app.DbConfig app.Clock "home" "Home" "client-id" false None (Some("local:new", "new")) true

        Assert.Equal(Some 0, replaced)
        let! second = ConnectorStore.findBySlug app.DbConfig "home"
        // None leaves the stored domains; the new secret is written; the connector stays unshared.
        Assert.Equal<string list>([ "example.com" ], second.Value.AllowedEmailDomains)
        Assert.Equal("local:new", second.Value.ClientSecretRef)
        Assert.False second.Value.IsShared
    }

[<Fact>]
let ``a failure while lowering leaves the setting and every flag unchanged`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedPeople app

        do!
            run
                app
                "CREATE TRIGGER t BEFORE UPDATE OF email_verified ON users
                 BEGIN SELECT RAISE(ABORT, 'boom'); END;"

        let! thrown =
            Assert.ThrowsAnyAsync<exn>(fun () -> saveOidc app "home" "Renamed" false :> System.Threading.Tasks.Task)

        Assert.Contains("boom", thrown.Message)

        let! stored = ConnectorStore.findBySlug app.DbConfig "home"
        Assert.Equal(Some true, stored.Value.EmailTrust)
        Assert.Equal("Home", stored.Value.DisplayName)
        do! expectEq "1" (flag app "only")
        do! expectEq "1" (flag app "shared")
        do! expectEq "1" (linkFlag app $"{issuerA}#1")
        do! expectEq "1" (linkFlag app $"{issuerA}#2")
    }

[<Fact>]
let ``previewTrustOff returns the count saveOidc would lower and changes nothing`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedPeople app

        let! preview = ConnectorStore.previewTrustOff app.DbConfig "home"
        Assert.Equal(Some 1, preview)

        let! stored = ConnectorStore.findBySlug app.DbConfig "home"
        Assert.Equal(Some true, stored.Value.EmailTrust)
        do! expectEq "1" (flag app "only")
        do! expectEq "1" (flag app "shared")
        do! expectEq "1" (linkFlag app $"{issuerA}#1")
        do! expectEq "1" (linkFlag app $"{issuerA}#2")

        // The same number the save then lowers.
        let! saved = saveOidc app "home" "Home" false
        Assert.Equal(preview, saved)

        let! unknown = ConnectorStore.previewTrustOff app.DbConfig "nope"
        Assert.Equal(None, unknown)
    }
