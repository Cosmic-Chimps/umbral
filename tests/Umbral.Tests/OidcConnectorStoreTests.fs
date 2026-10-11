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

        let! created = ConnectorStore.create app.DbConfig app.Clock (connector "home" Oidc (Some issuer)) secret
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

        let! _ = ConnectorStore.create app.DbConfig app.Clock (connector "discord-two" Discord None) secret
        let! stored = ConnectorStore.findBySlug app.DbConfig "discord-two"
        Assert.Equal(None, stored.Value.IssuerUrl)
    }

[<Fact>]
let ``a second connector on the same issuer is refused as issuer taken and nothing is written`` () =
    task {
        use! app = Fixtures.createApp ()
        let issuer = "https://auth.example.com/o"

        let! _ = ConnectorStore.create app.DbConfig app.Clock (connector "home" Oidc (Some issuer)) secret
        let! second = ConnectorStore.create app.DbConfig app.Clock (connector "work" Oidc (Some issuer)) secret

        Assert.Equal(ConnectorStore.IssuerTaken, second)

        let! stored = ConnectorStore.findBySlug app.DbConfig "work"
        Assert.True stored.IsNone
    }

[<Fact>]
let ``a taken slug is still reported as slug taken, not issuer taken`` () =
    task {
        use! app = Fixtures.createApp ()

        let! _ = ConnectorStore.create app.DbConfig app.Clock (connector "home" Oidc (Some "https://one.example.com")) secret
        let! again = ConnectorStore.create app.DbConfig app.Clock (connector "home" Oidc (Some "https://two.example.com")) secret

        Assert.Equal(ConnectorStore.SlugTaken, again)

        // The first connector is untouched, issuer included.
        let! stored = ConnectorStore.findBySlug app.DbConfig "home"
        Assert.Equal(Some "https://one.example.com", stored.Value.IssuerUrl)
    }

[<Fact>]
let ``saving a connector again never changes its issuer`` () =
    task {
        use! app = Fixtures.createApp ()

        let! _ = ConnectorStore.create app.DbConfig app.Clock (connector "home" Oidc (Some "https://one.example.com")) secret

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
