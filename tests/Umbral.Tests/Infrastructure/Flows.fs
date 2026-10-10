/// The flows several suites need to drive end to end: claim an instance, add a
/// connector, register an app, sign a person in, exchange the code.
///
/// The older suites each carry a private copy of a browser and these steps,
/// which is fine while each only needs its own variant. The hardening suites
/// (log redaction, CSRF, restart) all need the SAME journey and need to look at
/// what it produced, so the journey lives here once and hands back everything it
/// saw — every secret a test might later go looking for.
module Umbral.Tests.Infrastructure.Flows

open System
open System.Collections.Generic
open System.Net.Http
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure

[<Literal>]
let Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"

[<Literal>]
let Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"

[<Literal>]
let RedirectUri = "https://grafana.example.com/callback"

[<Literal>]
let OperatorCredential = "a-very-good-credential"

/// The settings that let the Fake connector exist: non-Production AND the
/// explicit opt-in. Both are required by design (see FakeConnectorGuardTests).
let fakeSettings =
    [ "ASPNETCORE_ENVIRONMENT", "Development"
      "UMBRAL:ALLOW_FAKE_CONNECTOR", "true" ]

/// A browser: keeps cookies, does not follow redirects, scrapes antiforgery
/// tokens. Following redirects is left to the test so each hop can be asserted.
type Browser(client: HttpClient) =
    let cookies = Dictionary<string, string>()

    member _.Cookies = cookies

    member _.Send(message: HttpRequestMessage) =
        task {
            if cookies.Count > 0 then
                let header = cookies |> Seq.map (fun kv -> $"{kv.Key}={kv.Value}") |> String.concat "; "
                message.Headers.Add("Cookie", header)

            let! response = client.SendAsync message

            match response.Headers.TryGetValues "Set-Cookie" with
            | true, values ->
                for value in Option.defaultValue Seq.empty (Option.ofObj values) do
                    match value.Split(';').[0].Split('=', 2) with
                    | [| name; v |] -> cookies[name] <- v
                    | _ -> ()
            | _ -> ()

            return response
        }

    member this.Get(url: string) =
        this.Send(new HttpRequestMessage(HttpMethod.Get, url))

    member this.GetHtml(url: string) =
        task {
            let! response = this.Get url
            return! response.Content.ReadAsStringAsync()
        }

    /// `head 1` matters: the layout renders its own sign-out form, so a page
    /// carries two tokens, and submitting both is "could not be decrypted".
    member this.TokenFrom(url: string) =
        task {
            let! html = this.GetHtml url
            let m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")
            return if m.Success then m.Groups[1].Value else failwith $"no antiforgery token on {url}"
        }

    /// A form post carrying exactly `fields` — no token unless the caller adds one.
    member this.PostWithoutToken(action: string, fields: (string * string) list) =
        let message = new HttpRequestMessage(HttpMethod.Post, action)
        message.Content <- new FormUrlEncodedContent(dict fields)
        this.Send message

    /// A form post with a token scraped from `formPage`, as a browser would.
    member this.PostForm(formPage: string, action: string, fields: (string * string) list) =
        task {
            let! token = this.TokenFrom formPage
            return! this.PostWithoutToken(action, ("__RequestVerificationToken", token) :: fields)
        }

let json (response: HttpResponseMessage) =
    task {
        let! body = response.Content.ReadAsStringAsync()
        return JsonDocument.Parse(body).RootElement
    }

/// JsonElement.GetString() is nullable; narrow once here rather than at every use.
let text (element: JsonElement) =
    element.GetString() |> Option.ofObj |> Option.defaultValue ""

let strOf (element: JsonElement) (name: string) = text (element.GetProperty name)

let base64UrlDecode (value: string) =
    Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + String('=', (4 - value.Length % 4) % 4))

/// Decode a JWT segment (0 = header, 1 = payload) without verifying anything.
let segmentOf (index: int) (jwt: string) =
    JsonDocument.Parse(Encoding.UTF8.GetString(base64UrlDecode (jwt.Split('.')[index]))).RootElement

/// Claim the instance as its operator. Leaves the browser holding the admin cookie.
let claimInstance (app: Fixtures.TestApp) (browser: Browser) =
    task {
        let token = app.Services.GetRequiredService<AdminAuth.SetupToken>()

        let! response =
            browser.PostForm(
                "/admin/setup",
                "/admin/setup",
                [ "token", token.Value
                  "credential", OperatorCredential
                  "confirm", OperatorCredential ]
            )

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode)
    }

/// Add the Fake connector. Written straight to the store because it has no card
/// on the Connectors screen, but the secret goes through the real `ISecretStore`:
/// the callback unprotects it on every sign-in, so a bogus reference would fail
/// the login for a reason that has nothing to do with the test.
let addFakeConnector (app: Fixtures.TestApp) (secret: string) =
    task {
        let secrets = app.Services.GetRequiredService<Secrets.ISecretStore>()
        let! reference = secrets.Put("connector/fake/client_secret", secret)

        do!
            ConnectorStore.save
                app.DbConfig
                app.Clock
                { Slug = "fake"
                  Kind = ProviderKind.Fake
                  DisplayName = "Fake"
                  ClientId = "fake"
                  ClientSecretRef = reference.Encoded
                  ClientSecretHint = Secrets.hint secret
                  ExtraScopes = []
                  IsEnabled = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None
    }

/// Register an app through the Apps screen. Returns the generated client id and
/// secret, read off the single-use reveal page as an operator would.
let createClientApp (browser: Browser) =
    task {
        let! created =
            browser.PostForm(
                "/admin/apps",
                "/admin/apps",
                [ "name", "Grafana"
                  "redirectUri", RedirectUri
                  "postLogoutRedirectUri", ""
                  "appType", "web" ]
            )

        let! reveal = browser.Get(created.Headers.Location |> string)
        let! html = reveal.Content.ReadAsStringAsync()

        // Anchored to each field's id: matching on the value shape alone picks
        // up the antiforgery token, which looks exactly like a long secret.
        let field (id: string) =
            let m = Regex.Match(html, "id=\"" + id + "\"[^>]*value=\"([^\"]+)\"")
            if m.Success then m.Groups[1].Value else failwith $"no {id} on the reveal page"

        return field "revealed-client-id", field "revealed-secret"
    }

let authorizeUrl (clientId: string) (scope: string) =
    "/connect/authorize?client_id=" + Uri.EscapeDataString clientId
    + "&response_type=code&redirect_uri=" + Uri.EscapeDataString RedirectUri
    + "&scope=" + Uri.EscapeDataString scope
    + "&code_challenge=" + Challenge
    + "&code_challenge_method=S256&state=app-state"

/// What a sign-in produced, including the values that crossed the wire on the
/// way — the provider's own code and the correlation `state` are credentials too.
type SignIn =
    {
        /// Umbral's authorization code, as the app receives it.
        Code: string
        /// The code the (fake) provider handed back to Umbral's callback.
        ProviderCode: string
        /// The `state` Umbral minted for the provider round trip.
        ProviderState: string
    }

/// Sign in through the Fake provider, from the app's authorization request to
/// the authorization code coming back.
let signInThroughFake (browser: Browser) (clientId: string) (scope: string) (subject: string) (email: string) =
    task {
        let! toPicker = browser.Get(authorizeUrl clientId scope)
        let picker = toPicker.Headers.Location |> string
        let! toProvider = browser.Get(picker.Replace("/signin", "/connect/fake/start"))

        let providerUrl = toProvider.Headers.Location |> string
        let decoded = Uri.UnescapeDataString providerUrl
        let redirect = Regex.Match(decoded, "redirect_uri=([^&]+)").Groups[1].Value
        let state = Regex.Match(decoded, "state=([^&]+)").Groups[1].Value

        let! approved =
            browser.PostForm(
                providerUrl,
                "/dev/fake-provider/authorize",
                [ "redirect_uri", redirect
                  "state", state
                  "subject", subject
                  "email", email
                  "displayName", "Sam Rivera"
                  "emailVerified", "on" ]
            )

        let callbackUrl = approved.Headers.Location |> string

        let providerCode =
            Regex.Match(callbackUrl, "code=([^&]+)").Groups[1].Value |> Uri.UnescapeDataString

        let! back = browser.Get callbackUrl
        let! issued = browser.Get(back.Headers.Location |> string)
        let final = issued.Headers.Location |> string
        let code = Regex.Match(final, "code=([^&]+)").Groups[1].Value

        Assert.True(code <> "", $"sign-in did not end in an authorization code: {final}")

        return
            { Code = Uri.UnescapeDataString code
              ProviderCode = providerCode
              ProviderState = state }
    }

let private tokenRequest (client: HttpClient) (fields: (string * string) list) =
    task {
        let message = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        message.Content <- new FormUrlEncodedContent(dict fields)
        return! client.SendAsync message
    }

let exchangeCode (client: HttpClient) (clientId: string) (secret: string) (code: string) =
    tokenRequest
        client
        [ "grant_type", "authorization_code"
          "code", code
          "redirect_uri", RedirectUri
          "code_verifier", Verifier
          "client_id", clientId
          "client_secret", secret ]

let refresh (client: HttpClient) (clientId: string) (secret: string) (refreshToken: string) =
    tokenRequest
        client
        [ "grant_type", "refresh_token"
          "refresh_token", refreshToken
          "client_id", clientId
          "client_secret", secret ]

let userInfo (client: HttpClient) (accessToken: string) =
    let message = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo")
    message.Headers.Add("Authorization", "Bearer " + accessToken)
    client.SendAsync message

/// The app a journey registered.
type Prepared =
    { ClientId: string
      ClientSecret: string }

/// Claim the instance, add the Fake connector, register an app.
let prepare (app: Fixtures.TestApp) (browser: Browser) : Task<Prepared> =
    task {
        do! claimInstance app browser
        do! addFakeConnector app "fake-connector-secret-value"
        let! clientId, secret = createClientApp browser
        return { ClientId = clientId; ClientSecret = secret }
    }
