/// A browser-only app (a single-page app using `fetch`) can read the discovery
/// document and call the token, userinfo and revocation endpoints from its own
/// origin: those five paths answer with `Access-Control-Allow-Origin: *` and
/// answer the preflight themselves. Nothing else carries a CORS header: the
/// authorization and logout endpoints, the admin pages and the sign-in pages are
/// navigated to, never fetched cross-origin.
module Umbral.Tests.CorsTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open Xunit
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

let private origin = "http://localhost:5173"

let private header (name: string) (response: HttpResponseMessage) =
    match response.Headers.TryGetValues name with
    | true, values -> values |> Option.ofObj |> Option.defaultValue Seq.empty |> List.ofSeq
    | _ -> []

let private allowOrigin = header "Access-Control-Allow-Origin"

let private assertAnyOrigin (what: string) (response: HttpResponseMessage) =
    Assert.True(
        [ "*" ] = allowOrigin response,
        $"{what} ({int response.StatusCode}): Access-Control-Allow-Origin {allowOrigin response}"
    )

    Assert.Empty(header "Access-Control-Allow-Credentials" response)

let private assertNoCors (what: string) (response: HttpResponseMessage) =
    Assert.True(
        List.isEmpty (allowOrigin response),
        $"{what} ({int response.StatusCode}): Access-Control-Allow-Origin {allowOrigin response}"
    )

let private fromOrigin (meth: HttpMethod) (path: string) =
    let message = new HttpRequestMessage(meth, path)
    message.Headers.Add("Origin", origin)
    message

let private preflight (path: string) (requestHeaders: string) =
    let message = fromOrigin HttpMethod.Options path
    message.Headers.Add("Access-Control-Request-Method", "POST")
    message.Headers.Add("Access-Control-Request-Headers", requestHeaders)
    message

let private tokenForm (fields: (string * string) list) =
    new FormUrlEncodedContent(dict fields)

[<Fact>]
let ``discovery and the keys answer any origin, and only a request with an Origin gets the header`` () =
    task {
        use! app = Fixtures.createApp ()

        let! discovery = app.Client.SendAsync(fromOrigin HttpMethod.Get "/.well-known/openid-configuration")
        Assert.Equal(HttpStatusCode.OK, discovery.StatusCode)
        assertAnyOrigin "discovery" discovery

        let! keys = app.Client.SendAsync(fromOrigin HttpMethod.Get "/.well-known/jwks")
        Assert.Equal(HttpStatusCode.OK, keys.StatusCode)
        assertAnyOrigin "the keys" keys

        let! plain = app.Client.GetAsync "/.well-known/openid-configuration"
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode)
        assertNoCors "discovery without an Origin" plain
    }

[<Fact>]
let ``the preflight of the token endpoint is answered with 204 and what a browser app sends`` () =
    task {
        use! app = Fixtures.createApp ()

        let! response = app.Client.SendAsync(preflight "/connect/token" "content-type")
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode)
        assertAnyOrigin "the token preflight" response
        Assert.Contains("POST", String.Join(",", header "Access-Control-Allow-Methods" response))

        Assert.Contains(
            "content-type",
            String.Join(",", header "Access-Control-Allow-Headers" response).ToLowerInvariant()
        )

        Assert.NotEmpty(header "Access-Control-Max-Age" response)
    }

[<Fact>]
let ``the preflight of the userinfo endpoint allows the Authorization header`` () =
    task {
        use! app = Fixtures.createApp ()

        let! response = app.Client.SendAsync(preflight "/connect/userinfo" "authorization")
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode)
        assertAnyOrigin "the userinfo preflight" response

        Assert.Contains(
            "authorization",
            String.Join(",", header "Access-Control-Allow-Headers" response).ToLowerInvariant()
        )

        Assert.NotEmpty(header "Access-Control-Max-Age" response)
    }

[<Fact>]
let ``a refused token request and a refused revocation still carry the header, so the app can read the error`` () =
    task {
        use! app = Fixtures.createApp ()

        let token = fromOrigin HttpMethod.Post "/connect/token"

        token.Content <-
            tokenForm
                [ "grant_type", "authorization_code"
                  "client_id", "nope"
                  "code", "nope"
                  "redirect_uri", RedirectUri
                  "code_verifier", Verifier ]

        let! refused = app.Client.SendAsync token
        Assert.True(refused.StatusCode = HttpStatusCode.BadRequest || refused.StatusCode = HttpStatusCode.Unauthorized)
        assertAnyOrigin "the refused token request" refused

        let revoke = fromOrigin HttpMethod.Post "/connect/revoke"
        revoke.Content <- tokenForm [ "token", "nope"; "client_id", "nope" ]

        let! revoked = app.Client.SendAsync revoke
        assertAnyOrigin "the refused revocation" revoked
    }

[<Fact>]
let ``a whole exchange the way a browser app does it works from another origin`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app
        let! clientId, secret = registerApp operator "Grafana"

        let person = Browser app.Client
        let! signedIn = signInViaFake person clientId "openid" "fake-user-1" "sam@example.com"
        let code = match signedIn with Ok code -> code | Error why -> failwith $"sign-in refused: {why}"

        let token = fromOrigin HttpMethod.Post "/connect/token"

        token.Content <-
            tokenForm
                [ "grant_type", "authorization_code"
                  "code", Uri.UnescapeDataString code
                  "redirect_uri", RedirectUri
                  "code_verifier", Verifier
                  "client_id", clientId
                  "client_secret", secret ]

        let! tokens = app.Client.SendAsync token
        Assert.Equal(HttpStatusCode.OK, tokens.StatusCode)
        assertAnyOrigin "the token response" tokens

        let! json = tokens.Content.ReadAsStringAsync()
        use document = JsonDocument.Parse json
        let accessToken = document.RootElement.GetProperty("access_token").GetString()

        let userinfo = fromOrigin HttpMethod.Get "/connect/userinfo"
        userinfo.Headers.Add("Authorization", "Bearer " + accessToken)

        let! claims = app.Client.SendAsync userinfo
        Assert.Equal(HttpStatusCode.OK, claims.StatusCode)
        assertAnyOrigin "the userinfo response" claims
    }

[<Fact>]
let ``pages and endpoints a browser navigates to carry no CORS header`` () =
    task {
        use! app = Fixtures.createApp ()

        let! setup = app.Client.SendAsync(fromOrigin HttpMethod.Get "/admin/setup")
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode)
        assertNoCors "the setup page" setup

        let! root = app.Client.SendAsync(fromOrigin HttpMethod.Get "/")
        assertNoCors "the home page" root

        let! authorize =
            app.Client.SendAsync(
                fromOrigin
                    HttpMethod.Get
                    ("/connect/authorize?client_id=nope&response_type=code&redirect_uri="
                     + Uri.EscapeDataString RedirectUri
                     + "&scope=openid&code_challenge="
                     + Challenge
                     + "&code_challenge_method=S256")
            )

        assertNoCors "the authorization endpoint" authorize

        let! authorizePreflight = app.Client.SendAsync(preflight "/connect/authorize" "content-type")
        Assert.NotEqual(HttpStatusCode.NoContent, authorizePreflight.StatusCode)
        assertNoCors "the authorization preflight" authorizePreflight
    }
