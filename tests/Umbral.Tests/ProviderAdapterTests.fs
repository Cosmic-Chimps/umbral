/// Golden authorize URLs and fixture-driven parsing for the three social
/// connectors, with every network call stubbed via `HttpMessageHandler`.
///
/// Every fixture here is authored to the documented shape of each provider's
/// response — none are captured from a live call — so a failing assertion
/// means either our parsing or our understanding of the shape is wrong, not
/// necessarily that the provider changed.
module Umbral.Tests.ProviderAdapterTests

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure.ProviderAdapters
open Umbral.Infrastructure.Providers.Discord
open Umbral.Infrastructure.Providers.GitHub
open Umbral.Infrastructure.Providers.Google

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

let private fixturesDirectory = Path.Combine(__SOURCE_DIRECTORY__, "Fixtures")

let private readFixture (relativePath: string) : string =
    File.ReadAllText(Path.Combine(fixturesDirectory, relativePath))

// ---------------------------------------------------------------------------
// HTTP stubbing
// ---------------------------------------------------------------------------

type private StubHandler(responder: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()

    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        Task.FromResult(responder request)

let private clientWith (responder: HttpRequestMessage -> HttpResponseMessage) =
    new HttpClient(new StubHandler(responder))

let private jsonResponse (status: HttpStatusCode) (body: string) =
    let response = new HttpResponseMessage(status)
    response.Content <- new StringContent(body, Encoding.UTF8, "application/json")
    response

let private formResponse (status: HttpStatusCode) (body: string) =
    let response = new HttpResponseMessage(status)
    response.Content <- new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded")
    response

let private requestUri (request: HttpRequestMessage) : string =
    match request.RequestUri with
    | null -> ""
    | uri -> uri.AbsoluteUri

let private contentTypeOf (request: HttpRequestMessage) : string option =
    match request.Content with
    | null -> None
    | content ->
        match content.Headers.ContentType with
        | null -> None
        | header -> Option.ofObj header.MediaType

// ---------------------------------------------------------------------------
// Shared builders
// ---------------------------------------------------------------------------

let private testConnector (kind: ProviderKind) : Connector =
    { Slug = kind.Value
      Kind = kind
      DisplayName = kind.DisplayName
      ClientId = "test-client-id"
      ClientSecretRef = "local:unused"
      ClientSecretHint = "1234"
      IssuerUrl = None
      AllowedEmailDomains = []
      PasskeyEnrolment = InvitationsOnly
      FediverseAccess = FediverseAccess.Any
      IsEnabled = true
      IsShared = true
      CreatedUtc = DateTimeOffset.UnixEpoch
      UpdatedUtc = DateTimeOffset.UnixEpoch }

let private authorizeRequestFor (connector: Connector) : AuthorizeRequest =
    { Connector = connector
      Endpoints = connector.Endpoints
      RedirectUri = Uri $"https://umbral.test/connect/{connector.Slug}/callback"
      State = "STATE123"
      CodeChallenge = "CHALLENGE123"
      Nonce = "NONCE123"
      HostedDomainHint = None }

let private exchangeRequestFor (connector: Connector) (nonce: string) : ExchangeRequest =
    { Connector = connector
      ClientSecret = "test-client-secret"
      RedirectUri = Uri $"https://umbral.test/connect/{connector.Slug}/callback"
      Code = "test-authorization-code"
      CodeVerifier = "test-code-verifier"
      Nonce = nonce }

let private expectOk (result: Result<ProviderProfile, ConnectorError>) : ProviderProfile =
    match result with
    | Ok profile -> profile
    | Error e -> failwith $"expected success, got {e.Detail}"

let private expectError (result: Result<ProviderProfile, ConnectorError>) : ConnectorError =
    match result with
    | Error e -> e
    | Ok profile -> failwith $"expected an error, got a profile for subject {profile.Subject}"

// ---------------------------------------------------------------------------
// Golden authorize URLs
// ---------------------------------------------------------------------------

[<Fact>]
let ``Discord golden authorize URL`` () =
    let connector = testConnector Discord
    use httpClient = new HttpClient()
    let adapter = DiscordAdapter(httpClient) :> IProviderAdapter

    let url = adapter.BuildAuthorizeUrl(authorizeRequestFor connector)

    let expected =
        "https://discord.com/oauth2/authorize?client_id=test-client-id&redirect_uri=https%3A%2F%2Fumbral.test%2Fconnect%2Fdiscord%2Fcallback&response_type=code&scope=identify email&state=STATE123&code_challenge=CHALLENGE123&code_challenge_method=S256"

    Assert.Equal(expected, url.ToString())

[<Fact>]
let ``GitHub golden authorize URL`` () =
    let connector = testConnector GitHub
    use httpClient = new HttpClient()
    let adapter = GitHubAdapter(httpClient) :> IProviderAdapter

    let url = adapter.BuildAuthorizeUrl(authorizeRequestFor connector)

    let expected =
        "https://github.com/login/oauth/authorize?client_id=test-client-id&redirect_uri=https%3A%2F%2Fumbral.test%2Fconnect%2Fgithub%2Fcallback&response_type=code&scope=read%3Auser user%3Aemail&state=STATE123&code_challenge=CHALLENGE123&code_challenge_method=S256"

    Assert.Equal(expected, url.ToString())

[<Fact>]
let ``Google golden authorize URL`` () =
    let connector = testConnector Google
    use httpClient = new HttpClient()
    let adapter = GoogleAdapter(httpClient) :> IProviderAdapter

    let url = adapter.BuildAuthorizeUrl(authorizeRequestFor connector)

    let expected =
        "https://accounts.google.com/o/oauth2/v2/auth?client_id=test-client-id&redirect_uri=https%3A%2F%2Fumbral.test%2Fconnect%2Fgoogle%2Fcallback&response_type=code&scope=openid email profile&state=STATE123&code_challenge=CHALLENGE123&code_challenge_method=S256&nonce=NONCE123"

    Assert.Equal(expected, url.ToString())

[<Fact>]
let ``Google carries the hosted-domain hint when given one`` () =
    let connector = testConnector Google
    use httpClient = new HttpClient()
    let adapter = GoogleAdapter(httpClient) :> IProviderAdapter

    let url = adapter.BuildAuthorizeUrl { authorizeRequestFor connector with HostedDomainHint = Some "acme.com" }

    let expected =
        "https://accounts.google.com/o/oauth2/v2/auth?client_id=test-client-id&redirect_uri=https%3A%2F%2Fumbral.test%2Fconnect%2Fgoogle%2Fcallback&response_type=code&scope=openid email profile&state=STATE123&code_challenge=CHALLENGE123&code_challenge_method=S256&nonce=NONCE123&hd=acme.com"

    Assert.Equal(expected, url.ToString())

[<Fact>]
let ``Google carries no hosted-domain parameter without a hint`` () =
    let connector = testConnector Google
    use httpClient = new HttpClient()
    let adapter = GoogleAdapter(httpClient) :> IProviderAdapter

    let url = adapter.BuildAuthorizeUrl(authorizeRequestFor connector)

    Assert.DoesNotContain("hd=", url.ToString())

[<Fact>]
let ``GitHub and Discord ignore the hosted-domain hint`` () =
    use httpClient = new HttpClient()

    for connector, adapter in
        [ testConnector GitHub, (GitHubAdapter(httpClient) :> IProviderAdapter)
          testConnector Discord, (DiscordAdapter(httpClient) :> IProviderAdapter) ] do
        let plain = adapter.BuildAuthorizeUrl(authorizeRequestFor connector)
        let hinted = adapter.BuildAuthorizeUrl { authorizeRequestFor connector with HostedDomainHint = Some "acme.com" }
        Assert.Equal(plain, hinted)
        Assert.DoesNotContain("hd=", hinted.ToString())

// ---------------------------------------------------------------------------
// Discord
// ---------------------------------------------------------------------------

[<Fact>]
let ``Discord token exchange is sent as form-urlencoded, never JSON`` () =
    task {
        let connector = testConnector Discord
        let mutable capturedContentType: string option = None

        let responder (request: HttpRequestMessage) =
            if requestUri request = connector.Endpoints.TokenUrl then
                capturedContentType <- contentTypeOf request
                jsonResponse HttpStatusCode.OK (readFixture "discord/token_success.json")
            else
                jsonResponse HttpStatusCode.OK (readFixture "discord/user_migrated_null_avatar.json")

        let adapter = DiscordAdapter(clientWith responder) :> IProviderAdapter
        let! result = adapter.ExchangeAndFetch(exchangeRequestFor connector "unused")

        // The #1 hand-rolled-Discord mistake: sending the token request as
        // JSON, which Discord rejects outright.
        Assert.Equal(Some "application/x-www-form-urlencoded", capturedContentType)
        expectOk result |> ignore
    }

[<Fact>]
let ``Discord token error is surfaced without exposing the raw body as a crash`` () =
    task {
        let connector = testConnector Discord

        let responder (request: HttpRequestMessage) =
            if requestUri request = connector.Endpoints.TokenUrl then
                jsonResponse HttpStatusCode.BadRequest (readFixture "discord/token_error.json")
            else
                failwith "user info must not be called after a token error"

        let adapter = DiscordAdapter(clientWith responder) :> IProviderAdapter
        let! result = adapter.ExchangeAndFetch(exchangeRequestFor connector "unused")

        match expectError result with
        | ProviderRejected reason -> Assert.Equal("invalid_grant", reason)
        | other -> failwith $"expected ProviderRejected, got {other}"
    }

[<Fact>]
let ``Discord a migrated account with no avatar gets the snowflake-based default`` () =
    task {
        let connector = testConnector Discord

        let responder (request: HttpRequestMessage) =
            if requestUri request = connector.Endpoints.TokenUrl then
                jsonResponse HttpStatusCode.OK (readFixture "discord/token_success.json")
            else
                jsonResponse HttpStatusCode.OK (readFixture "discord/user_migrated_null_avatar.json")

        let adapter = DiscordAdapter(clientWith responder) :> IProviderAdapter
        let! result = adapter.ExchangeAndFetch(exchangeRequestFor connector "unused")
        let profile = expectOk result

        Assert.Equal("80351110224678912", profile.Subject)
        Assert.Equal(Some "Nelly", profile.DisplayName)
        // (80351110224678912 >> 22) % 6 = 5
        Assert.Equal(Some "https://cdn.discordapp.com/embed/avatars/5.png", profile.AvatarUrl)
        Assert.Equal(Some "nelly@example.com", profile.Email)
        Assert.True profile.EmailVerified
    }

[<Fact>]
let ``Discord a legacy account with no avatar gets the discriminator-based default`` () =
    task {
        let connector = testConnector Discord

        let responder (request: HttpRequestMessage) =
            if requestUri request = connector.Endpoints.TokenUrl then
                jsonResponse HttpStatusCode.OK (readFixture "discord/token_success.json")
            else
                jsonResponse HttpStatusCode.OK (readFixture "discord/user_legacy_null_avatar.json")

        let adapter = DiscordAdapter(clientWith responder) :> IProviderAdapter
        let! result = adapter.ExchangeAndFetch(exchangeRequestFor connector "unused")
        let profile = expectOk result

        // 1234 % 5 = 4
        Assert.Equal(Some "https://cdn.discordapp.com/embed/avatars/4.png", profile.AvatarUrl)
        // No global_name: falls back to username.
        Assert.Equal(Some "oldschool", profile.DisplayName)
        // No email at all: never reported verified.
        Assert.Equal(None, profile.Email)
        Assert.False profile.EmailVerified
    }

[<Fact>]
let ``Discord an animated avatar hash composes a gif URL`` () =
    task {
        let connector = testConnector Discord

        let responder (request: HttpRequestMessage) =
            if requestUri request = connector.Endpoints.TokenUrl then
                jsonResponse HttpStatusCode.OK (readFixture "discord/token_success.json")
            else
                jsonResponse HttpStatusCode.OK (readFixture "discord/user_animated_avatar.json")

        let adapter = DiscordAdapter(clientWith responder) :> IProviderAdapter
        let! result = adapter.ExchangeAndFetch(exchangeRequestFor connector "unused")
        let profile = expectOk result

        Assert.Equal(
            Some "https://cdn.discordapp.com/avatars/80351110224678914/a_1269e74af4df7417b13759eae50c83dc.gif",
            profile.AvatarUrl
        )
    }

// ---------------------------------------------------------------------------
// GitHub
// ---------------------------------------------------------------------------

[<Fact>]
let ``GitHub token request asks for JSON and parses a private-email user via the emails endpoint`` () =
    task {
        let connector = testConnector GitHub
        let mutable sawAcceptJson = false

        let responder (request: HttpRequestMessage) =
            match requestUri request with
            | uri when uri = connector.Endpoints.TokenUrl ->
                sawAcceptJson <- request.Headers.Accept |> Seq.exists (fun h -> h.MediaType = "application/json")
                jsonResponse HttpStatusCode.OK (readFixture "github/token_success.json")
            | "https://api.github.com/user" -> jsonResponse HttpStatusCode.OK (readFixture "github/user_private_email.json")
            | "https://api.github.com/user/emails" ->
                jsonResponse HttpStatusCode.OK (readFixture "github/emails_primary_verified.json")
            | other -> failwith $"unexpected request to {other}"

        let adapter = GitHubAdapter(clientWith responder) :> IProviderAdapter
        let! result = adapter.ExchangeAndFetch(exchangeRequestFor connector "unused")
        let profile = expectOk result

        Assert.True sawAcceptJson
        Assert.Equal("1", profile.Subject)
        Assert.Equal(Some "The Octocat", profile.DisplayName)
        Assert.Equal(Some "octocat@github.com", profile.Email)
        Assert.True profile.EmailVerified
    }

[<Fact>]
let ``GitHub asks /user-emails even when /user carries a public address`` () =
    task {
        // GitHub does not document the public profile address as necessarily
        // verified, and the linking rule's safety rests on EmailVerified
        // meaning "the provider asserted this" rather than "we inferred it".
        // So the inline address is never trusted, and the canonical primary
        // verified one is used instead — note it differs from the inline one
        // here, which is exactly the case inference would get wrong.
        let connector = testConnector GitHub
        let mutable askedForEmails = false

        let responder (request: HttpRequestMessage) =
            match requestUri request with
            | uri when uri = connector.Endpoints.TokenUrl -> jsonResponse HttpStatusCode.OK (readFixture "github/token_success.json")
            | "https://api.github.com/user" -> jsonResponse HttpStatusCode.OK (readFixture "github/user_public_email.json")
            | "https://api.github.com/user/emails" ->
                askedForEmails <- true
                jsonResponse HttpStatusCode.OK (readFixture "github/emails_primary_verified.json")
            | other -> failwith $"unexpected request to {other}"

        let adapter = GitHubAdapter(clientWith responder) :> IProviderAdapter
        let! result = adapter.ExchangeAndFetch(exchangeRequestFor connector "unused")
        let profile = expectOk result

        Assert.True(askedForEmails, "/user/emails must be consulted on every sign-in")
        Assert.Equal(Some "octocat@github.com", profile.Email)
        Assert.True profile.EmailVerified
    }

[<Fact>]
let ``GitHub an unreadable address list signs in without an email rather than failing`` () =
    task {
        // GitHub already authenticated this person. Refusing the whole sign-in
        // because the address list was unreadable — a denied user:email scope,
        // a transient 5xx — is worse than signing them in with no email, and
        // no email means no linking, which is the safe direction.
        let connector = testConnector GitHub

        let responder (request: HttpRequestMessage) =
            match requestUri request with
            | uri when uri = connector.Endpoints.TokenUrl -> jsonResponse HttpStatusCode.OK (readFixture "github/token_success.json")
            | "https://api.github.com/user" -> jsonResponse HttpStatusCode.OK (readFixture "github/user_public_email.json")
            | "https://api.github.com/user/emails" -> jsonResponse HttpStatusCode.Forbidden """{"message":"Requires authentication"}"""
            | other -> failwith $"unexpected request to {other}"

        let adapter = GitHubAdapter(clientWith responder) :> IProviderAdapter
        let! result = adapter.ExchangeAndFetch(exchangeRequestFor connector "unused")
        let profile = expectOk result

        Assert.Equal(None, profile.Email)
        Assert.False profile.EmailVerified
    }

[<Fact>]
let ``GitHub a verified-but-not-primary address is not used as the email`` () =
    task {
        let connector = testConnector GitHub

        let responder (request: HttpRequestMessage) =
            match requestUri request with
            | uri when uri = connector.Endpoints.TokenUrl -> jsonResponse HttpStatusCode.OK (readFixture "github/token_success.json")
            | "https://api.github.com/user" -> jsonResponse HttpStatusCode.OK (readFixture "github/user_private_email.json")
            | "https://api.github.com/user/emails" ->
                jsonResponse HttpStatusCode.OK (readFixture "github/emails_verified_not_primary.json")
            | other -> failwith $"unexpected request to {other}"

        let adapter = GitHubAdapter(clientWith responder) :> IProviderAdapter
        let! result = adapter.ExchangeAndFetch(exchangeRequestFor connector "unused")
        let profile = expectOk result

        Assert.Equal(None, profile.Email)
        Assert.False profile.EmailVerified
    }

[<Fact>]
let ``GitHub a token error arrives as HTTP 200 with an error field`` () =
    task {
        let connector = testConnector GitHub

        let responder (request: HttpRequestMessage) =
            if requestUri request = connector.Endpoints.TokenUrl then
                // The status line lies: GitHub answers 200 OK even for a bad code.
                jsonResponse HttpStatusCode.OK (readFixture "github/token_error.json")
            else
                failwith "user info must not be called after a token error"

        let adapter = GitHubAdapter(clientWith responder) :> IProviderAdapter
        let! result = adapter.ExchangeAndFetch(exchangeRequestFor connector "unused")

        match expectError result with
        | ProviderRejected reason -> Assert.Equal("bad_verification_code", reason)
        | other -> failwith $"expected ProviderRejected, got {other}"
    }

[<Fact>]
let ``GitHub a form-encoded token response is rejected rather than crashing`` () =
    task {
        // Defends against exactly the trap: if `Accept: application/json` were
        // ever dropped (a proxy, a regression), GitHub's default form-encoded
        // body must not reach an end user as an unhandled exception.
        let connector = testConnector GitHub

        let responder (_: HttpRequestMessage) =
            formResponse HttpStatusCode.OK (readFixture "github/token_success_form_encoded.txt")

        let adapter = GitHubAdapter(clientWith responder) :> IProviderAdapter
        let! result = adapter.ExchangeAndFetch(exchangeRequestFor connector "unused")

        match expectError result with
        | MalformedResponse _ -> ()
        | other -> failwith $"expected MalformedResponse, got {other}"
    }

// ---------------------------------------------------------------------------
// Google
// ---------------------------------------------------------------------------

let private base64UrlEncode (bytes: byte[]) =
    Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')

type private GoogleClaims =
    { Iss: string
      Aud: string
      Sub: string
      Exp: int64
      Nonce: string option }

/// Mints an unsigned token with the given claims. Producing a real signed JWT
/// is unnecessary and would be misleading: the adapter under test deliberately
/// never checks the signature (OIDC Core §3.1.3.7), only the claims.
let private mintIdToken (claims: GoogleClaims) : string =
    let headerJson = """{"alg":"none","typ":"JWT"}"""

    let payload = Dictionary<string, obj>()
    payload["iss"] <- claims.Iss
    payload["aud"] <- claims.Aud
    payload["sub"] <- claims.Sub
    payload["exp"] <- claims.Exp

    match claims.Nonce with
    | Some nonce -> payload["nonce"] <- nonce
    | None -> ()

    let payloadJson = JsonSerializer.Serialize payload
    let encode (s: string) = base64UrlEncode (Encoding.UTF8.GetBytes s)
    let signature = encode "unsigned"
    $"{encode headerJson}.{encode payloadJson}.{signature}"

let private googleTokenResponse (idToken: string) : string =
    (readFixture "google/token_response_template.json").Replace("{{ID_TOKEN}}", idToken)

let private defaultClaims (nonce: string) : GoogleClaims =
    { Iss = "https://accounts.google.com"
      Aud = "test-client-id"
      Sub = "110169484474386276334"
      Exp = DateTimeOffset.UtcNow.AddHours(1.0).ToUnixTimeSeconds()
      Nonce = Some nonce }

let private googleResponder (idToken: string) (userinfoFixture: string) (connector: Connector) (request: HttpRequestMessage) =
    if requestUri request = connector.Endpoints.TokenUrl then
        jsonResponse HttpStatusCode.OK (googleTokenResponse idToken)
    else
        jsonResponse HttpStatusCode.OK (readFixture userinfoFixture)

[<Theory>]
[<InlineData("https://accounts.google.com")>]
[<InlineData("accounts.google.com")>]
let ``Google accepts both documented issuer spellings`` (iss: string) =
    task {
        let connector = testConnector Google
        let nonce = "test-nonce-value"
        let idToken = mintIdToken { defaultClaims nonce with Iss = iss }

        let adapter =
            GoogleAdapter(clientWith (googleResponder idToken "google/userinfo_success.json" connector))
            :> IProviderAdapter

        let! result = adapter.ExchangeAndFetch(exchangeRequestFor connector nonce)
        let profile = expectOk result

        Assert.Equal("110169484474386276334", profile.Subject)
    }

[<Fact>]
let ``Google an unverified email is reported as such`` () =
    task {
        let connector = testConnector Google
        let nonce = "test-nonce-value"
        let idToken = mintIdToken (defaultClaims nonce)

        let adapter =
            GoogleAdapter(clientWith (googleResponder idToken "google/userinfo_email_unverified.json" connector))
            :> IProviderAdapter

        let! result = adapter.ExchangeAndFetch(exchangeRequestFor connector nonce)
        let profile = expectOk result

        Assert.Equal(Some "janeq@example.com", profile.Email)
        Assert.False profile.EmailVerified
    }

[<Fact>]
let ``Google a nonce that does not round-trip is rejected`` () =
    task {
        let connector = testConnector Google
        let idToken = mintIdToken (defaultClaims "the-nonce-we-sent")

        let adapter =
            GoogleAdapter(clientWith (googleResponder idToken "google/userinfo_success.json" connector))
            :> IProviderAdapter

        // We ask for a different nonce than the one baked into the token.
        let! result = adapter.ExchangeAndFetch(exchangeRequestFor connector "a-different-nonce")

        match expectError result with
        | MalformedResponse _ -> ()
        | other -> failwith $"expected MalformedResponse, got {other}"
    }

[<Fact>]
let ``Google an expired id_token is rejected`` () =
    task {
        let connector = testConnector Google
        let nonce = "test-nonce-value"
        let claims = { defaultClaims nonce with Exp = DateTimeOffset.UtcNow.AddHours(-1.0).ToUnixTimeSeconds() }
        let idToken = mintIdToken claims

        let adapter =
            GoogleAdapter(clientWith (googleResponder idToken "google/userinfo_success.json" connector))
            :> IProviderAdapter

        let! result = adapter.ExchangeAndFetch(exchangeRequestFor connector nonce)

        match expectError result with
        | MalformedResponse _ -> ()
        | other -> failwith $"expected MalformedResponse, got {other}"
    }

[<Fact>]
let ``Google a token error is surfaced as ProviderRejected`` () =
    task {
        let connector = testConnector Google

        let responder (request: HttpRequestMessage) =
            if requestUri request = connector.Endpoints.TokenUrl then
                jsonResponse HttpStatusCode.BadRequest (readFixture "google/token_error.json")
            else
                failwith "userinfo must not be called after a token error"

        let adapter = GoogleAdapter(clientWith responder) :> IProviderAdapter
        let! result = adapter.ExchangeAndFetch(exchangeRequestFor connector "unused")

        match expectError result with
        | ProviderRejected reason -> Assert.Equal("invalid_grant", reason)
        | other -> failwith $"expected ProviderRejected, got {other}"
    }

[<Fact>]
let ``Google a token response with no id_token is rejected`` () =
    task {
        let connector = testConnector Google

        let responder (request: HttpRequestMessage) =
            if requestUri request = connector.Endpoints.TokenUrl then
                jsonResponse HttpStatusCode.OK """{"access_token":"google-access-token","token_type":"Bearer"}"""
            else
                failwith "userinfo must not be called when there is no id_token"

        let adapter = GoogleAdapter(clientWith responder) :> IProviderAdapter
        let! result = adapter.ExchangeAndFetch(exchangeRequestFor connector "unused")

        match expectError result with
        | MalformedResponse _ -> ()
        | other -> failwith $"expected MalformedResponse, got {other}"
    }
