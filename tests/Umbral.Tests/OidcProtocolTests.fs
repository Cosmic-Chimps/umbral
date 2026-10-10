/// The contract an integrating app depends on (T037–T041).
///
/// The one that matters most is the claim-content test. The T001 spike proved
/// that a claim without a destination vanishes from both tokens in TOTAL
/// silence — HTTP 200, a syntactically valid JWT, a valid signature, nothing in
/// any log. The only way to notice is to decode a real token and look, so that
/// is what this does.
module Umbral.Tests.OidcProtocolTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let private json (response: HttpResponseMessage) =
    task {
        let! body = response.Content.ReadAsStringAsync()
        return JsonDocument.Parse(body).RootElement
    }

/// JsonElement.GetString() is nullable; narrow once here rather than at every
/// assertion.
let private text (element: JsonElement) =
    element.GetString() |> Option.ofObj |> Option.defaultValue ""

let private strOf (element: JsonElement) (name: string) = text (element.GetProperty name)

let private strings (element: JsonElement) (name: string) =
    element.GetProperty(name).EnumerateArray() |> Seq.map text |> List.ofSeq

/// Decode a JWT payload without verifying it. The point here is what the token
/// SAYS, not whether it is trustworthy — the signature has its own test.
let private payloadOf (jwt: string) =
    let part = jwt.Split('.')[1]
    let padded = part.Replace('-', '+').Replace('_', '/') + String('=', (4 - part.Length % 4) % 4)
    JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String padded)).RootElement

[<Literal>]
let private Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"

[<Literal>]
let private Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"

[<Literal>]
let private RedirectUri = "https://grafana.example.com/callback"

type private Client(http: HttpClient) =
    let cookies = Collections.Generic.Dictionary<string, string>()

    member _.Send(message: HttpRequestMessage) =
        task {
            if cookies.Count > 0 then
                let header = cookies |> Seq.map (fun kv -> $"{kv.Key}={kv.Value}") |> String.concat "; "
                message.Headers.Add("Cookie", header)

            let! response = http.SendAsync message

            match response.Headers.TryGetValues "Set-Cookie" with
            | true, values ->
                for value in Option.defaultValue Seq.empty (Option.ofObj values) do
                    match value.Split(';').[0].Split('=', 2) with
                    | [| name; v |] -> cookies[name] <- v
                    | _ -> ()
            | _ -> ()

            return response
        }

    member this.Get(url: string) = this.Send(new HttpRequestMessage(HttpMethod.Get, url))

    member this.TokenFrom(url: string) =
        task {
            let! response = this.Get url
            let! html = response.Content.ReadAsStringAsync()
            let m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")
            return if m.Success then m.Groups[1].Value else failwith $"no antiforgery token on {url}"
        }

    member this.PostForm(formPage: string, action: string, fields: (string * string) list) =
        task {
            let! token = this.TokenFrom formPage
            let message = new HttpRequestMessage(HttpMethod.Post, action)
            message.Content <- new FormUrlEncodedContent(dict (("__RequestVerificationToken", token) :: fields))
            return! this.Send message
        }

let private fakeSettings =
    [ "ASPNETCORE_ENVIRONMENT", "Development"
      "UMBRAL:ALLOW_FAKE_CONNECTOR", "true" ]

/// Claim the instance, add a fake connector, register an app. Returns the
/// generated client id and secret.
let private prepare (app: Fixtures.TestApp) (client: Client) =
    task {
        let token = app.Services.GetRequiredService<AdminAuth.SetupToken>()

        let! _ =
            client.PostForm(
                "/admin/setup",
                "/admin/setup",
                [ "token", token.Value
                  "credential", "a-very-good-credential"
                  "confirm", "a-very-good-credential" ]
            )

        let secrets = app.Services.GetRequiredService<Secrets.ISecretStore>()
        let! reference = secrets.Put("connector/fake/client_secret", "unused")

        do!
            ConnectorStore.save
                app.DbConfig
                app.Clock
                { Slug = "fake"
                  Kind = ProviderKind.Fake
                  DisplayName = "Fake"
                  ClientId = "fake"
                  ClientSecretRef = reference.Encoded
                  ClientSecretHint = "used"
                  ExtraScopes = []
                  IsEnabled = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None

        let! created =
            client.PostForm(
                "/admin/apps",
                "/admin/apps",
                [ "name", "Grafana"
                  "redirectUri", RedirectUri
                  "postLogoutRedirectUri", ""
                  "appType", "web" ]
            )

        // The secret is shown exactly once, on the page the redirect lands on.
        let! revealResponse = client.Get(created.Headers.Location |> string)
        let! revealHtml = revealResponse.Content.ReadAsStringAsync()

        // Anchored to each field's id. Matching on the value shape alone picks
        // up the antiforgery token, which the layout renders first and which
        // looks exactly like a long opaque secret.
        let field (id: string) =
            let m = Regex.Match(revealHtml, "id=\"" + id + "\"[^>]*value=\"([^\"]+)\"")
            if m.Success then m.Groups[1].Value else failwith $"no {id} on the reveal page"

        return field "revealed-client-id", field "revealed-secret"
    }

/// Sign in through the fake provider and return the URL the app is redirected to.
let private authorizationRedirect (client: Client) (clientId: string) (scope: string) =
    task {
        let authorizeUrl =
            "/connect/authorize?client_id=" + Uri.EscapeDataString clientId
            + "&response_type=code&redirect_uri=" + Uri.EscapeDataString RedirectUri
            + "&scope=" + Uri.EscapeDataString scope
            + "&code_challenge=" + Challenge
            + "&code_challenge_method=S256&state=app-state"

        let! toPicker = client.Get authorizeUrl
        let picker = toPicker.Headers.Location |> string
        let! toProvider = client.Get(picker.Replace("/signin", "/connect/fake/start"))

        let providerUrl = toProvider.Headers.Location |> string
        let decoded = Uri.UnescapeDataString providerUrl
        let redirect = Regex.Match(decoded, "redirect_uri=([^&]+)").Groups[1].Value
        let state = Regex.Match(decoded, "state=([^&]+)").Groups[1].Value

        let! approved =
            client.PostForm(
                providerUrl,
                "/dev/fake-provider/authorize",
                [ "redirect_uri", redirect
                  "state", state
                  "subject", "fake-user-1"
                  "email", "sam@example.com"
                  "displayName", "Sam Rivera"
                  "emailVerified", "on" ]
            )

        let! back = client.Get(approved.Headers.Location |> string)
        let! issued = client.Get(back.Headers.Location |> string)
        return issued.Headers.Location |> string
    }

/// Sign in through the fake provider and return the authorization code.
let private authorizationCode (client: Client) (clientId: string) (scope: string) =
    task {
        let! final = authorizationRedirect client clientId scope
        return Regex.Match(final, "code=([^&]+)").Groups[1].Value
    }

let private exchange (app: Fixtures.TestApp) (clientId: string) (secret: string) (code: string) =
    task {
        let message = new HttpRequestMessage(HttpMethod.Post, "/connect/token")

        message.Content <-
            new FormUrlEncodedContent(
                dict
                    [ "grant_type", "authorization_code"
                      "code", Uri.UnescapeDataString code
                      "redirect_uri", RedirectUri
                      "code_verifier", Verifier
                      "client_id", clientId
                      "client_secret", secret ]
            )

        return! app.Client.SendAsync message
    }

let private refresh (app: Fixtures.TestApp) (clientId: string) (secret: string) (refreshToken: string) =
    task {
        let message = new HttpRequestMessage(HttpMethod.Post, "/connect/token")

        message.Content <-
            new FormUrlEncodedContent(
                dict
                    [ "grant_type", "refresh_token"
                      "refresh_token", refreshToken
                      "client_id", clientId
                      "client_secret", secret ]
            )

        return! app.Client.SendAsync message
    }

/// Run one statement against the app's own database. Used to put a user in a
/// state the admin screens would not leave them in — disabled WITHOUT their
/// authorizations revoked — so a test reaches the check Umbral itself makes
/// instead of the one OpenIddict would have made first.
let private sql (app: Fixtures.TestApp) (statement: string) (parameters: (string * string) list) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- statement

        for name, value in parameters do
            command.Parameters.AddWithValue(name, value) |> ignore

        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

let private countEvents (app: Fixtures.TestApp) (where: string) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT count(*) FROM security_events WHERE " + where
        let! value = command.ExecuteScalarAsync()
        return Convert.ToInt32 value
    }

/// A fresh login that asked for a refresh token: the token response, already
/// parsed.
let private loginWithRefresh (app: Fixtures.TestApp) (client: Client) =
    task {
        let! clientId, secret = prepare app client
        let! code = authorizationCode client clientId "openid email profile offline_access"
        let! response = exchange app clientId secret code
        let! body = json response
        return clientId, secret, body
    }

// ---------------------------------------------------------------------------
// T037 — discovery
// ---------------------------------------------------------------------------

[<Fact>]
let ``the discovery document promises exactly what Umbral accepts`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/.well-known/openid-configuration"
        let! document = json response

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)

        // Derived from UMBRAL__PUBLIC_ORIGIN and nothing else. Every client
        // library compares the iss claim against this.
        Assert.StartsWith(Fixtures.TestOrigin, strOf document "issuer")

        let grants = strings document "grant_types_supported"
        Assert.Contains("authorization_code", grants)
        Assert.Contains("refresh_token", grants)
        // FR-002.
        Assert.DoesNotContain("implicit", grants)
        Assert.DoesNotContain("password", grants)

        // T105: rejecting `plain` is only half the job — a client library that
        // read this and chose it would fail for a reason nothing explains.
        let methods = strings document "code_challenge_methods_supported"
        Assert.Equal<string list>([ "S256" ], methods)

        Assert.Equal<string list>([ "code" ], strings document "response_types_supported")
    }

/// Every URL string in the discovery document, with its name.
let private urlsIn (document: JsonElement) =
    document.EnumerateObject()
    |> Seq.choose (fun p ->
        match p.Value.ValueKind with
        | JsonValueKind.String ->
            p.Value.GetString()
            |> Option.ofObj
            |> Option.filter (fun text -> text.StartsWith "http")
            |> Option.map (fun text -> p.Name, text)
        | _ -> None)
    |> List.ofSeq

[<Fact>]
let ``every URL in the discovery document comes from the configured origin, whatever Host the request carries`` () =
    task {
        use! app = Fixtures.createApp ()

        // A proxy that does not forward Host would say http://umbral:8080; one
        // that forwards a different host would say that. Neither may leak into
        // what clients are told to call (FR-031, research.md D4).
        let! plain = app.Client.GetAsync "/.well-known/openid-configuration"
        let! baseline = json plain

        for host, forwardedHost, proto in
            [ "umbral:8080", None, None
              "umbral:8080", Some "other.example:18443", Some "https"
              "evil.example", Some "evil.example", None ] do
            use request = new HttpRequestMessage(HttpMethod.Get, "/.well-known/openid-configuration")
            request.Headers.Host <- host
            forwardedHost |> Option.iter (fun h -> request.Headers.Add("X-Forwarded-Host", h))
            proto |> Option.iter (fun p -> request.Headers.Add("X-Forwarded-Proto", p))

            let! response = app.Client.SendAsync request
            let! document = json response
            let urls = urlsIn document

            // The ones a client actually calls must be there, or this is vacuous.
            for name in [ "issuer"; "authorization_endpoint"; "token_endpoint"; "userinfo_endpoint"; "end_session_endpoint"; "jwks_uri" ] do
                Assert.True(urls |> List.exists (fun (n, _) -> n = name), $"{name} missing under Host {host}")

            for name, url in urls do
                Assert.True(
                    url.StartsWith(Fixtures.TestOrigin + "/") || url = Fixtures.TestOrigin,
                    $"{name} is {url} under Host {host}, expected it under {Fixtures.TestOrigin}"
                )

            // And the same document as without the odd headers.
            Assert.Equal<(string * string) list>(urlsIn baseline, urls)
    }

[<Fact>]
let ``the iss parameter on the redirect to the app is the configured issuer`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let client = Client(app.Client)
        let! clientId, _ = prepare app client
        let! final = authorizationRedirect client clientId "openid"

        // RFC 9207: the app checks this against the issuer it discovered.
        let issuer = Regex.Match(final, "[?&]iss=([^&]+)").Groups[1].Value
        Assert.StartsWith(Fixtures.TestOrigin, Uri.UnescapeDataString issuer)
    }

[<Fact>]
let ``the key set is published and usable`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/.well-known/jwks"
        let! document = json response

        let keys = document.GetProperty("keys").EnumerateArray() |> List.ofSeq
        Assert.NotEmpty keys
        Assert.Equal("RSA", strOf keys.Head "kty")
    }

// ---------------------------------------------------------------------------
// T038, T039 — a real token, and what it carries
// ---------------------------------------------------------------------------

[<Fact>]
let ``an authorization code exchanges for tokens`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let client = Client(app.Client)
        let! clientId, secret = prepare app client
        let! code = authorizationCode client clientId "openid email profile"

        let! response = exchange app clientId secret code
        let! body = json response

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        Assert.Equal("Bearer", strOf body "token_type")
        Assert.True(body.TryGetProperty("id_token") |> fst)

        // D3: the access token is an opaque reference, not a JWT. If this ever
        // starts parsing as three dot-separated segments, revocation has
        // quietly stopped being immediate.
        let accessToken = strOf body "access_token"
        Assert.DoesNotContain(".", accessToken)
    }

[<Fact>]
let ``THE claims test: a real id_token carries what the scopes asked for`` () =
    task {
        // The regression test for SetDestinations. Without that one call every
        // claim below vanishes, with no error anywhere — so this asserts the
        // content of an issued token rather than how the identity was built.
        use! app = Fixtures.createAppWith fakeSettings ignore
        let client = Client(app.Client)
        let! clientId, secret = prepare app client
        let! code = authorizationCode client clientId "openid email profile"

        let! response = exchange app clientId secret code
        let! body = json response
        let payload = payloadOf (strOf body "id_token")

        Assert.Equal(clientId, strOf payload "aud")
        Assert.False(String.IsNullOrEmpty(strOf payload "sub"))

        // email scope
        Assert.Equal("sam@example.com", strOf payload "email")

        // A real JSON boolean, not the string "true". SetClaim has 15+
        // overloads and the string one is the easy mistake, after which an app
        // doing `if (claims.email_verified)` treats "false" as true.
        Assert.Equal(JsonValueKind.True, payload.GetProperty("email_verified").ValueKind)

        // profile scope
        Assert.Equal("Sam Rivera", strOf payload "name")

        // Umbral's one non-standard claim, and the only honest way for an app
        // to show "signed in with Discord".
        Assert.Equal("fake", strOf payload "idp")
    }

[<Fact>]
let ``a token requested without the email scope does not carry the email`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let client = Client(app.Client)
        let! clientId, secret = prepare app client
        let! code = authorizationCode client clientId "openid"

        let! response = exchange app clientId secret code
        let! body = json response
        let payload = payloadOf (strOf body "id_token")

        // Destinations are scope-gated, so asking for less really gets less.
        Assert.False(payload.TryGetProperty("email") |> fst)
        Assert.False(payload.TryGetProperty("name") |> fst)
        Assert.True(payload.TryGetProperty("sub") |> fst)
    }

// ---------------------------------------------------------------------------
// T041 — user info agrees with the token
// ---------------------------------------------------------------------------

[<Fact>]
let ``user info returns the same identity as the id_token`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let client = Client(app.Client)
        let! clientId, secret = prepare app client
        let! code = authorizationCode client clientId "openid email profile"

        let! tokenResponse = exchange app clientId secret code
        let! tokens = json tokenResponse
        let payload = payloadOf (strOf tokens "id_token")

        let message = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo")
        message.Headers.Add("Authorization", "Bearer " + strOf tokens "access_token")
        let! response = app.Client.SendAsync message
        let! info = json response

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        Assert.Equal(strOf payload "sub", strOf info "sub")
        Assert.Equal(strOf payload "email", strOf info "email")
    }

[<Fact>]
let ``user info refuses an unknown token`` () =
    task {
        use! app = Fixtures.createApp ()
        let message = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo")
        message.Headers.Add("Authorization", "Bearer not-a-real-token")
        let! response = app.Client.SendAsync message
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode)
    }

// ---------------------------------------------------------------------------
// T040 — what the protocol must refuse
// ---------------------------------------------------------------------------

/// A rejected authorization request has two correct shapes, and which one you
/// get is itself part of the protocol:
///
///   - the client and its redirect_uri are known, so the error goes BACK to
///     the client as a redirect carrying `error=` — that is how an app learns
///     it sent something wrong;
///   - the redirect_uri is not registered, so there is nowhere safe to send
///     anyone and Umbral answers on its own page.
///
/// What must never happen is the request succeeding.
let private assertRejectedToClient (response: HttpResponseMessage) =
    let location = response.Headers.Location |> string

    if response.StatusCode = HttpStatusCode.Found then
        Assert.StartsWith(RedirectUri, location)
        Assert.Contains("error=", location)
    else
        // Answered here instead, which is also fine — but it must not be a
        // redirect anywhere else, and must not be a success.
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode)

[<Fact>]
let ``an authorization code cannot be used twice`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let client = Client(app.Client)
        let! clientId, secret = prepare app client
        let! code = authorizationCode client clientId "openid"

        let! first = exchange app clientId secret code
        Assert.Equal(HttpStatusCode.OK, first.StatusCode)

        // FR-008. A replayed code is the classic interception attack.
        let! second = exchange app clientId secret code
        let! body = json second
        Assert.Equal("invalid_grant", strOf body "error")

        // And it is on the record: a replay is either an interception or a
        // buggy client, and either way the operator should be able to see it.
        let! replays = countEvents app $"event_type = 'authorization_code_replayed' AND outcome = 'failure' AND client_id = '{clientId}'"
        Assert.Equal(1, replays)

        // The ordinary first use is not a replay.
        let! first = countEvents app "event_type = 'authorization_code_replayed'"
        Assert.Equal(1, first)
    }

[<Fact>]
let ``a rejected code that was never redeemed is not recorded as a replay`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let client = Client(app.Client)
        let! clientId, secret = prepare app client

        let! response = exchange app clientId secret "a-code-nobody-issued"
        let! body = json response
        Assert.Equal("invalid_grant", strOf body "error")

        let! replays = countEvents app "event_type = 'authorization_code_replayed'"
        Assert.Equal(0, replays)
    }

[<Fact>]
let ``the wrong PKCE verifier is refused`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let client = Client(app.Client)
        let! clientId, secret = prepare app client
        let! code = authorizationCode client clientId "openid"

        let message = new HttpRequestMessage(HttpMethod.Post, "/connect/token")

        message.Content <-
            new FormUrlEncodedContent(
                dict
                    [ "grant_type", "authorization_code"
                      "code", Uri.UnescapeDataString code
                      "redirect_uri", RedirectUri
                      "code_verifier", "a-completely-different-verifier-that-is-long-enough"
                      "client_id", clientId
                      "client_secret", secret ]
            )

        let! response = app.Client.SendAsync message
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
    }

[<Fact>]
let ``an unregistered redirect_uri is refused WITHOUT redirecting there`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let client = Client(app.Client)
        let! clientId, _ = prepare app client

        let! response =
            client.Get(
                "/connect/authorize?client_id=" + Uri.EscapeDataString clientId
                + "&response_type=code&redirect_uri=" + Uri.EscapeDataString "https://evil.example.com/steal"
                + "&scope=openid&code_challenge=" + Challenge
                + "&code_challenge_method=S256&state=s"
            )

        // FR-003, and the assertion that matters: whatever Umbral does, it
        // must not send the browser to an address the app never registered.
        // Doing so would hand the code to whoever asked for it.
        let location = response.Headers.Location |> string
        Assert.DoesNotContain("evil.example.com", location)
    }

[<Fact>]
let ``an authorization request without PKCE is refused`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let client = Client(app.Client)
        let! clientId, _ = prepare app client

        let! response =
            client.Get(
                "/connect/authorize?client_id=" + Uri.EscapeDataString clientId
                + "&response_type=code&redirect_uri=" + Uri.EscapeDataString RedirectUri
                + "&scope=openid&state=s"
            )

        // FR-002: PKCE is required of every client, not merely offered.
        assertRejectedToClient response
    }

[<Fact>]
let ``plain PKCE is refused even though a client may ask for it`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let client = Client(app.Client)
        let! clientId, _ = prepare app client

        // OpenIddict accepts `plain` out of the box, and `plain` sends the
        // verifier AS the challenge — so PKCE protects nothing. T105 is the
        // custom handler that refuses it; this is the test that it still does.
        let! response =
            client.Get(
                "/connect/authorize?client_id=" + Uri.EscapeDataString clientId
                + "&response_type=code&redirect_uri=" + Uri.EscapeDataString RedirectUri
                + "&scope=openid&code_challenge=" + Verifier
                + "&code_challenge_method=plain&state=s"
            )

        assertRejectedToClient response
    }

// ---------------------------------------------------------------------------
// T091 — the refresh grant
// ---------------------------------------------------------------------------

[<Fact>]
let ``a refresh issues new tokens that carry the same claims`` () =
    task {
        // The regression test for rebuilding the identity on refresh. Destinations
        // do not survive the round trip through a token, so a refresh that forgot
        // SetDestinations still answers 200 with a valid id_token — carrying `sub`
        // and nothing else. Only decoding the new token notices.
        use! app = Fixtures.createAppWith fakeSettings ignore
        let client = Client(app.Client)
        let! clientId, secret, first = loginWithRefresh app client
        let original = payloadOf (strOf first "id_token")

        let! response = refresh app clientId secret (strOf first "refresh_token")
        let! body = json response

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)

        let payload = payloadOf (strOf body "id_token")
        Assert.Equal(strOf original "sub", strOf payload "sub")
        Assert.Equal("sam@example.com", strOf payload "email")
        Assert.Equal("Sam Rivera", strOf payload "name")
        Assert.Equal("fake", strOf payload "idp")

        // Still a real boolean after the round trip, not "true".
        Assert.Equal(JsonValueKind.True, payload.GetProperty("email_verified").ValueKind)

        // Rolling refresh: a new refresh token, and a new access token.
        Assert.NotEqual<string>(strOf first "refresh_token", strOf body "refresh_token")
        Assert.NotEqual<string>(strOf first "access_token", strOf body "access_token")
    }

[<Fact>]
let ``a refresh reflects the person's current profile`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let client = Client(app.Client)
        let! clientId, secret, first = loginWithRefresh app client
        let subject = strOf (payloadOf (strOf first "id_token")) "sub"

        do! sql app "UPDATE users SET display_name = @name WHERE id = @id" [ "@name", "Samantha Rivera"; "@id", subject ]

        let! response = refresh app clientId secret (strOf first "refresh_token")
        let! body = json response

        Assert.Equal("Samantha Rivera", strOf (payloadOf (strOf body "id_token")) "name")
    }

[<Fact>]
let ``tokens issued by a refresh stay bound to the authorization`` () =
    task {
        // Rebuilding the identity from scratch would drop the private oi_au_id
        // claim: the new tokens would work, and revoking the user's
        // authorizations would no longer reach them. So disable through the real
        // admin route, which revokes, and watch the REFRESHED access token die.
        use! app = Fixtures.createAppWith fakeSettings ignore
        let client = Client(app.Client)
        let! clientId, secret, first = loginWithRefresh app client
        let subject = strOf (payloadOf (strOf first "id_token")) "sub"

        let! response = refresh app clientId secret (strOf first "refresh_token")
        let! body = json response
        let accessToken = strOf body "access_token"

        let userInfo () =
            task {
                let message = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo")
                message.Headers.Add("Authorization", "Bearer " + accessToken)
                let! r = app.Client.SendAsync message
                return r.StatusCode
            }

        let! before = userInfo ()
        Assert.Equal(HttpStatusCode.OK, before)

        let! _ = client.PostForm("/admin/users", $"/admin/users/{subject}/toggle", [])

        let! after = userInfo ()
        Assert.Equal(HttpStatusCode.Unauthorized, after)
    }

[<Fact>]
let ``a refresh after the user is disabled through the admin screen fails`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let client = Client(app.Client)
        let! clientId, secret, first = loginWithRefresh app client
        let subject = strOf (payloadOf (strOf first "id_token")) "sub"

        let! _ = client.PostForm("/admin/users", $"/admin/users/{subject}/toggle", [])

        let! response = refresh app clientId secret (strOf first "refresh_token")
        let! body = json response

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        Assert.Equal("invalid_grant", strOf body "error")
    }

[<Fact>]
let ``a refresh fails for a disabled user even when nothing revoked their authorization`` () =
    task {
        // Umbral's own check, reached by disabling the row directly. Through the
        // admin screen OpenIddict refuses first because the authorization was
        // revoked; this is the case where it was not — a database edited by hand,
        // a future code path that forgets to revoke — and a refresh token must
        // still not become a new session.
        use! app = Fixtures.createAppWith fakeSettings ignore
        let client = Client(app.Client)
        let! clientId, secret, first = loginWithRefresh app client
        let subject = strOf (payloadOf (strOf first "id_token")) "sub"

        do! sql app "UPDATE users SET is_enabled = 0 WHERE id = @id" [ "@id", subject ]

        let! response = refresh app clientId secret (strOf first "refresh_token")
        let! body = json response

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        Assert.Equal("invalid_grant", strOf body "error")
    }

[<Fact>]
let ``a refresh fails for a deleted user`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let client = Client(app.Client)
        let! clientId, secret, first = loginWithRefresh app client
        let subject = strOf (payloadOf (strOf first "id_token")) "sub"

        do! sql app "DELETE FROM users WHERE id = @id" [ "@id", subject ]

        let! response = refresh app clientId secret (strOf first "refresh_token")
        let! body = json response

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        Assert.Equal("invalid_grant", strOf body "error")
    }

[<Fact>]
let ``a code exchange for a user disabled since the authorization fails`` () =
    task {
        // The same re-check on the other grant: disabled between the sign-in and
        // the exchange, which is a window of seconds but a real one.
        use! app = Fixtures.createAppWith fakeSettings ignore
        let client = Client(app.Client)
        let! clientId, secret = prepare app client
        let! code = authorizationCode client clientId "openid"

        do! sql app "UPDATE users SET is_enabled = 0" []

        let! response = exchange app clientId secret code
        let! body = json response

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        Assert.Equal("invalid_grant", strOf body "error")
    }
