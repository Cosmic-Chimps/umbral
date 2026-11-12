/// The Microsoft adapter's discovery and authorize halves against a stub
/// identity provider served at Microsoft's own address (specs/016). The sign-in
/// itself, with the fake Microsoft, is tested in its own file.
module Umbral.Tests.MicrosoftAdapterTests

open System
open System.Net.Http
open Microsoft.Extensions.Time.Testing
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure.OidcDiscovery
open Umbral.Infrastructure.ProviderAdapters
open Umbral.Infrastructure.Providers.Microsoft
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.StubOidcProvider

let private tenant = Guid.Parse "4f5b3d6e-27b0-4c3d-a6a3-5c9a1d0fb2e7"
let private redirectUri = Uri "https://umbral.test/connect/microsoft/callback"
let private nonce = "nonce-0123456789"
let private state = "state-0123456789"

let private connector (audience: MicrosoftAudience option) : Connector =
    { Slug = "microsoft"
      Kind = Microsoft
      DisplayName = "Microsoft"
      ClientId = "11111111-2222-3333-4444-555555555555"
      ClientSecretRef = "local:unused"
      ClientSecretHint = "ount"
      IssuerUrl = None
      AllowedEmailDomains = []
      PasskeyEnrolment = InvitationsOnly
      FediverseAccess = FediverseAccess.Any
      AppleKey = None
      MicrosoftAudience = audience
      IsEnabled = true
      IsShared = true
      CreatedUtc = DateTimeOffset.UtcNow
      UpdatedUtc = DateTimeOffset.UtcNow }

/// A stub served where Microsoft serves `audience`'s document, naming `issuer`.
let private rig (audience: MicrosoftAudience) (issuer: string) =
    let clock = FakeTimeProvider(DateTimeOffset.UtcNow)
    let stub = create (Umbral.Domains.Microsoft.documentBase audience)
    stub.Discovery["issuer"] <- node issuer
    stub.Now <- fun () -> clock.GetUtcNow()

    let discovery = OidcDiscovery(new HttpClient(stub, false), false, TimeSpan.FromSeconds 10.0, clock)
    let adapter = MicrosoftAdapter(new HttpClient(stub, false), discovery, clock)
    stub, adapter :> IProviderAdapter

let private authorizeRequest (audience: MicrosoftAudience) (endpoints: ProviderEndpoints) : AuthorizeRequest =
    { Connector = connector (Some audience)
      Endpoints = endpoints
      RedirectUri = redirectUri
      State = state
      CodeChallenge = Flows.Challenge
      Nonce = nonce
      HostedDomainHint = None }

[<Fact>]
let ``a connector with no audience does not resolve`` () =
    task {
        let _, adapter = rig PersonalAndAnyOrganization Umbral.Domains.Microsoft.issuerTemplate

        match! adapter.Resolve(connector None) with
        | Error(MalformedResponse reason) -> Assert.Equal("the connector has no audience", reason)
        | other -> failwith $"expected a malformed response, got {other}"
    }

[<Fact>]
let ``the endpoints come from the audience's own document, with PKCE and no userinfo`` () =
    task {
        let stub, adapter = rig PersonalAndAnyOrganization Umbral.Domains.Microsoft.issuerTemplate

        match! adapter.Resolve(connector (Some PersonalAndAnyOrganization)) with
        | Ok endpoints ->
            Assert.Equal(stub.Root + "/authorize", endpoints.AuthorizationUrl)
            Assert.Equal(stub.Root + "/token", endpoints.TokenUrl)
            Assert.Equal("", endpoints.UserInfoUrl)
            Assert.True endpoints.SupportsPkce
            Assert.Equal<string list>([ "openid"; "email"; "profile" ], endpoints.Scopes)
        | Error error -> failwith $"expected the audience to resolve, got {error.Detail}"
    }

[<Fact>]
let ``a document that names another issuer than the audience's does not resolve`` () =
    task {
        // A tenant's own document naming the template: not the tenant's issuer.
        let _, adapter = rig (OneOrganization tenant) Umbral.Domains.Microsoft.issuerTemplate

        match! adapter.Resolve(connector (Some(OneOrganization tenant))) with
        | Error(MalformedResponse _) -> ()
        | other -> failwith $"expected a malformed response, got {other}"
    }

[<Fact>]
let ``the personal accounts' document must name the personal tenant's issuer`` () =
    task {
        let issuer = Umbral.Domains.Microsoft.issuerFor "9188040d-6c67-4c5b-b112-36a304b66dad"
        let _, adapter = rig PersonalAccounts issuer

        match! adapter.Resolve(connector (Some PersonalAccounts)) with
        | Ok _ -> ()
        | Error error -> failwith $"expected the audience to resolve, got {error.Detail}"
    }

[<Fact>]
let ``the authorize URL carries the scopes, the S256 challenge, nonce, state and the exact redirect`` () =
    task {
        let stub, adapter = rig PersonalAndAnyOrganization Umbral.Domains.Microsoft.issuerTemplate

        match! adapter.Resolve(connector (Some PersonalAndAnyOrganization)) with
        | Error error -> failwith $"expected the audience to resolve, got {error.Detail}"
        | Ok endpoints ->
            let url = adapter.BuildAuthorizeUrl(authorizeRequest PersonalAndAnyOrganization endpoints)

            let expected =
                stub.Root
                + "/authorize?client_id=11111111-2222-3333-4444-555555555555"
                + "&redirect_uri=https%3A%2F%2Fumbral.test%2Fconnect%2Fmicrosoft%2Fcallback"
                + "&response_type=code"
                + "&scope=openid%20email%20profile"
                + "&state=state-0123456789"
                + $"&code_challenge={Flows.Challenge}"
                + "&code_challenge_method=S256"
                + "&nonce=nonce-0123456789"

            Assert.Equal(expected, url.AbsoluteUri)
    }
