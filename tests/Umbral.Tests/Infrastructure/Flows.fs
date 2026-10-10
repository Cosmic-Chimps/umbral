/// Whole-product flows for tests that need to drive the app the way an
/// operator and a person do: claim the instance, register an app, sign in
/// through the fake provider, exchange the code.
///
/// Everything here goes through the real routes. The one shortcut is that a
/// test seeds the fake connector straight into the store, because the fake kind
/// has no card on the Connectors screen — it is not something anyone configures
/// deliberately.
module Umbral.Tests.Infrastructure.Flows

open System
open System.Net
open System.Net.Http
open System.Text.RegularExpressions
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure

[<Literal>]
let RedirectUri = "https://grafana.example.com/callback"

[<Literal>]
let Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"

[<Literal>]
let Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"

let OperatorCredential = "a-very-good-credential"

/// Settings under which the fake connector is allowed to exist.
let fakeSettings =
    [ "ASPNETCORE_ENVIRONMENT", "Development"
      "UMBRAL:ALLOW_FAKE_CONNECTOR", "true" ]

/// A browser: keeps cookies, does not follow redirects, scrapes antiforgery
/// tokens. Following redirects is left to the test so each hop can be asserted.
type Browser(client: HttpClient) =
    let cookies = Collections.Generic.Dictionary<string, string>()

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

    /// The FIRST token: the layout renders its own sign-out form, so a page can
    /// carry two, and submitting both decrypts as neither.
    member this.TokenFrom(url: string) =
        task {
            let! html = this.GetHtml url
            let m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")
            return if m.Success then m.Groups[1].Value else failwith $"no antiforgery token on {url}"
        }

    member this.PostForm(formPage: string, action: string, fields: (string * string) list) =
        task {
            let! token = this.TokenFrom formPage
            let message = new HttpRequestMessage(HttpMethod.Post, action)
            // A list of pairs, not a dictionary: a field may repeat, as the
            // `connectors` tick boxes of an app do.
            let pairs =
                (("__RequestVerificationToken", token) :: fields)
                |> List.map (fun (name, value) -> Collections.Generic.KeyValuePair(name, value))

            message.Content <- new FormUrlEncodedContent(pairs)
            return! this.Send message
        }

    /// The cross-site scenario: the victim's cookies ARE sent, the token is the
    /// one thing the attacker cannot supply.
    member this.PostWithoutToken(url: string, fields: (string * string) list) =
        let message = new HttpRequestMessage(HttpMethod.Post, url)
        message.Content <- new FormUrlEncodedContent(dict fields)
        this.Send message

let location (response: HttpResponseMessage) = response.Headers.Location |> string

/// Claim a fresh instance as its operator. Leaves `browser` signed in.
let claimOperator (app: Fixtures.TestApp) (browser: Browser) =
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

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
    }

/// The fake connector, written straight to the store with a real stored secret.
let seedFakeConnector (app: Fixtures.TestApp) =
    task {
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
                  IssuerUrl = None
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None
    }

/// Register a web app through the Apps screen. Returns its client id and the
/// secret revealed once on the page the redirect lands on.
let registerApp (browser: Browser) (name: string) =
    task {
        let! created =
            browser.PostForm(
                "/admin/apps",
                "/admin/apps",
                [ "name", name
                  "redirectUri", RedirectUri
                  "postLogoutRedirectUri", ""
                  "appType", "web" ]
            )

        let! revealHtml =
            task {
                let! r = browser.Get(location created)
                return! r.Content.ReadAsStringAsync()
            }

        // Anchored to each field's id: matching on the value shape alone picks
        // up the antiforgery token, which looks exactly like an opaque secret.
        let field (id: string) =
            let m = Regex.Match(revealHtml, "id=\"" + id + "\"[^>]*value=\"([^\"]+)\"")
            if m.Success then m.Groups[1].Value else failwith $"no {id} on the reveal page"

        return field "revealed-client-id", field "revealed-secret"
    }

/// One whole sign-in through the fake provider, ending with the authorization
/// code the app would receive. `browser` ends up with an Umbral session.
let signInViaFake
    (browser: Browser)
    (clientId: string)
    (scope: string)
    (subject: string)
    (email: string)
    =
    task {
        let authorizeUrl =
            "/connect/authorize?client_id=" + Uri.EscapeDataString clientId
            + "&response_type=code&redirect_uri=" + Uri.EscapeDataString RedirectUri
            + "&scope=" + Uri.EscapeDataString scope
            + "&code_challenge=" + Challenge
            + "&code_challenge_method=S256&state=app-state"

        let! toPicker = browser.Get authorizeUrl
        let! toProvider = browser.Get((location toPicker).Replace("/signin", "/connect/fake/start"))

        let providerUrl = location toProvider
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
                  "displayName", "Test User"
                  "emailVerified", "on" ]
            )

        let! back = browser.Get(location approved)
        let replayed = location back

        // A refused sign-in lands back on the picker instead of the
        // authorization endpoint; the caller decides what that means.
        if not (replayed.Contains "/connect/authorize") then
            return Error replayed
        else
            let! issued = browser.Get replayed
            return Ok(Regex.Match(location issued, "code=([^&]+)").Groups[1].Value)
    }

/// Redeem an authorization code at the token endpoint.
let exchangeCode (app: Fixtures.TestApp) (clientId: string) (secret: string) (code: string) =
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

/// One scalar from the app's own database.
let scalar (app: Fixtures.TestApp) (statement: string) (parameters: (string * string) list) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- statement

        for name, value in parameters do
            command.Parameters.AddWithValue(name, value) |> ignore

        let! value = command.ExecuteScalarAsync()
        return if isNull value then "" else string value
    }

/// The user id Umbral minted for a provider subject.
let userIdOf (app: Fixtures.TestApp) (subject: string) =
    scalar app "SELECT user_id FROM linked_accounts WHERE provider_subject = @s" [ "@s", subject ]
