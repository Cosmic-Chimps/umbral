/// What the Bluesky connector keeps and what it must not show (specs/015, T008):
/// the server of a link, judged again when a sign-in is reused; no secret in the
/// security log; the correlation cookie encrypted.
module Umbral.Tests.BlueskySessionTests

open System
open System.Net
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.FakeAtproto
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.BlueskySignInTests

let private scope = "openid profile offline_access"

/// Ana signs in asking for a refresh token.
let private signedIn () =
    task {
        let! fake, app, prepared = setup ()
        let! completed = signInWith fake app prepared scope "ana.bsky.social"
        return fake, app, prepared, completed, strOf completed.Tokens "refresh_token"
    }

let private tighten (app: Fixtures.TestApp) servers =
    ConnectorStore.setFediverseAccess app.DbConfig app.Clock "bluesky" { OnlyListed = true; Servers = servers }

[<Fact>]
let ``the link remembers the authorization server's normalised host`` () =
    task {
        let! _, app, _, _, _ = signedIn ()
        let! host = Flows.scalar app "SELECT provider_server FROM linked_accounts WHERE provider_kind = 'bluesky'" []
        Assert.Equal("bsky.test", host)
    }

[<Fact>]
let ``after the server is removed from the list, a remembered sign-in is refused at authorize`` () =
    task {
        let! _, app, prepared, completed, _ = signedIn ()
        let! _ = tighten app [ "other.test" ]

        let! response = completed.Browser.Get(authorizeUrl prepared.ClientId "openid")
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.StartsWith("/signin?returnUrl=", response.Headers.Location |> string)

        let! details = failures app
        Assert.Equal("bluesky:server_not_allowed;server=bsky.test", Assert.Single details)
    }

[<Fact>]
let ``after the server is removed from the list, a refresh token grant is invalid_grant`` () =
    task {
        let! _, app, prepared, _, refreshToken = signedIn ()
        let! _ = tighten app [ "other.test" ]

        let! response = Journeys.refresh app.Client prepared.ClientId prepared.ClientSecret refreshToken
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        let! body = json response
        Assert.Equal("invalid_grant", strOf body "error")

        let! details = failures app
        Assert.Equal("bluesky:server_not_allowed;server=bsky.test", Assert.Single details)
    }

[<Fact>]
let ``while the server is still listed, the remembered sign-in and the refresh are honoured`` () =
    task {
        let! _, app, prepared, completed, refreshToken = signedIn ()
        let! _ = tighten app [ "bsky.test"; "other.test" ]

        let! response = completed.Browser.Get(authorizeUrl prepared.ClientId "openid")
        Assert.Contains("code=", response.Headers.Location |> string)

        let! refreshed = Journeys.refresh app.Client prepared.ClientId prepared.ClientSecret refreshToken
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode)
    }

[<Fact>]
let ``no code, token, key or nonce reaches the security log, after a sign-in or after a failed exchange`` () =
    task {
        let! fake, app, prepared = setup ()
        let server = fake.Server ana.Issuer
        server.Nonce <- "nonce-8d41c7e2"

        // A full sign-in.
        let! completed = signIn fake app prepared "ana.bsky.social"

        // A sign-in whose exchange fails at the token endpoint.
        server.Token <- ServerError
        let! browser, posted = start app prepared "ana.bsky.social"
        let callback = Uri(fake.Approve(posted.Headers.Location |> string))
        let failedCode = Web.HttpUtility.ParseQueryString(callback.Query)["code"] |> Option.ofObj |> Option.defaultValue ""
        let! back = browser.Get callback.PathAndQuery
        Assert.Contains("msg=sign-in-failed", back.Headers.Location |> string)

        let! details = allDetails app
        Assert.NotEmpty details
        Assert.NotEmpty server.AccessTokens

        let secrets =
            [ completed.ProviderCode; completed.AppCode; failedCode; "nonce-8d41c7e2" ]
            @ List.ofSeq server.AccessTokens
            // The public half of every key made, and the markers of a private JWK.
            @ (fake.Proofs |> Array.map _.Jwk |> Array.distinct |> List.ofArray)
            @ [ "\"d\""; "\"kty\""; "\"crv\"" ]

        Assert.All(secrets, fun secret -> Assert.NotEqual<string>("", secret))

        for detail in details do
            for secret in secrets do
                Assert.DoesNotContain(secret, detail)
    }

[<Fact>]
let ``the correlation cookie is encrypted: no key, no DID, no handle in the clear`` () =
    task {
        let! _, app, prepared = setup ()
        let! browser, _ = start app prepared "ana.bsky.social"
        let value = browser.Cookies["umbral_connect"]
        Assert.NotEmpty value
        let decoded = Uri.UnescapeDataString value

        for clear in [ "\"d\":"; "\"kty\""; ana.Did; "ewvi7nxzyoun6zhxrhs64oiz"; "ana.bsky.social"; "tokenEndpoint"; "bsky.test" ] do
            Assert.DoesNotContain(clear, value)
            Assert.DoesNotContain(clear, decoded)
    }

[<Fact>]
let ``once an oidc connector takes the slug bluesky, a remembered sign-in is refused at authorize`` () =
    task {
        let! _, app, prepared, completed, _ = signedIn ()
        do! replaceWithOidc app "bluesky"

        let! response = completed.Browser.Get(authorizeUrl prepared.ClientId "openid")
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.StartsWith("/signin?returnUrl=", response.Headers.Location |> string)

        let! again = completed.Browser.Get(authorizeUrl prepared.ClientId "openid")
        Assert.StartsWith("/signin?returnUrl=", again.Headers.Location |> string)

        let! details = failures app
        Assert.Equal("connector_changed;reason=other_kind;stage=authorize;kind=oidc", Assert.Single details)
    }

[<Fact>]
let ``once an oidc connector takes the slug bluesky, a refresh token grant is invalid_grant`` () =
    task {
        let! _, app, prepared, _, refreshToken = signedIn ()
        do! replaceWithOidc app "bluesky"

        let! response = Journeys.refresh app.Client prepared.ClientId prepared.ClientSecret refreshToken
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        let! body = json response
        Assert.Equal("invalid_grant", strOf body "error")

        let! details = failures app
        Assert.Equal("connector_changed;reason=other_kind;stage=token;kind=oidc", Assert.Single details)
    }
