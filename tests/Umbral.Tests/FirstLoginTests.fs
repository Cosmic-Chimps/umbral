/// User Story 1, end to end: an operator claims a fresh instance, configures a
/// connector, registers an app, and a person signs in.
///
/// The only thing replaced is the hop to a third party. The correlation cookie,
/// the `state` comparison, the linking rule, the OIDC authorization request and
/// the token issuance are all the real ones.
module Umbral.Tests.FirstLoginTests

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

/// A browser: keeps cookies, does not follow redirects, scrapes antiforgery
/// tokens. Following redirects is left to the test so each hop can be asserted.
type private Browser(client: HttpClient) =
    let cookies = Collections.Generic.Dictionary<string, string>()

    let capture (response: HttpResponseMessage) =
        match response.Headers.TryGetValues "Set-Cookie" with
        | true, values ->
            for value in values do
                let first = value.Split(';').[0]

                match first.Split('=', 2) with
                | [| name; v |] -> cookies[name] <- v
                | _ -> ()
        | _ -> ()

        response

    let request (message: HttpRequestMessage) =
        task {
            if cookies.Count > 0 then
                let header = cookies |> Seq.map (fun kv -> $"{kv.Key}={kv.Value}") |> String.concat "; "
                message.Headers.Add("Cookie", header)

            let! response = client.SendAsync message
            return capture response
        }

    member _.Get(url: string) = request (new HttpRequestMessage(HttpMethod.Get, url))

    member this.GetHtml(url: string) =
        task {
            let! response = this.Get url
            return! response.Content.ReadAsStringAsync()
        }

    /// Scrape the antiforgery token from a page.
    ///
    /// `head 1` matters: the layout renders its own sign-out form, so a page
    /// carries two tokens. Submitting both produces "the antiforgery token
    /// could not be decrypted", which names neither the count nor the cause.
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
            message.Content <- new FormUrlEncodedContent(dict (("__RequestVerificationToken", token) :: fields))
            return! request message
        }

let private fakeSettings =
    [ "ASPNETCORE_ENVIRONMENT", "Development"
      "UMBRAL:ALLOW_FAKE_CONNECTOR", "true" ]

let private location (response: HttpResponseMessage) = response.Headers.Location |> string

/// The security log says why a sign-in failed. Folding it into the assertion
/// turns "expected /connect/authorize, got /signin?msg=sign-in-failed" — which
/// names no cause — into something that does.
let private lastFailure (config: Database.DbConfig) =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "SELECT coalesce(detail, event_type) FROM security_events
             WHERE outcome = 'failure' ORDER BY id DESC LIMIT 1"

        let! value = command.ExecuteScalarAsync()
        return if isNull value then "(no failure recorded)" else string value
    }

/// Everything up to "a connector exists and an app is registered".
/// Returns the client id Umbral generated for the app — an operator reads it
/// off the Apps screen, so the test does too rather than assuming one.
let private claimAndConfigure (app: Fixtures.TestApp) (browser: Browser) : Task<string> =
    task {
        let token = app.Services.GetRequiredService<AdminAuth.SetupToken>()

        let! setup =
            browser.PostForm(
                "/admin/setup",
                "/admin/setup",
                [ "token", token.Value
                  "credential", "a-very-good-credential"
                  "confirm", "a-very-good-credential" ]
            )

        Assert.Equal(HttpStatusCode.Redirect, setup.StatusCode)

        // The connector the operator would create on the Connectors screen.
        // Written directly because the fake kind has no card there — it is not
        // a provider anyone configures deliberately.
        // The secret goes through the real ISecretStore rather than being
        // faked. The callback unprotects it on every sign-in, and a bogus
        // reference fails the login with "connector_secret_unreadable" — which
        // is correct behaviour, and special-casing it away would make this test
        // pass for the wrong reason.
        let secrets = app.Services.GetRequiredService<Secrets.ISecretStore>()
        let! secretRef = secrets.Put("connector/fake/client_secret", "fake-secret")

        do!
            ConnectorStore.save
                app.DbConfig
                app.Clock
                { Slug = "fake"
                  Kind = ProviderKind.Fake
                  DisplayName = "Fake"
                  ClientId = "fake-client"
                  ClientSecretRef = secretRef.Encoded
                  ClientSecretHint = "cret"
                  ExtraScopes = []
                  IsEnabled = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None

        let! created =
            browser.PostForm(
                "/admin/apps",
                "/admin/apps",
                [ "name", "Grafana"
                  "redirectUri", "https://grafana.example.com/callback"
                  "postLogoutRedirectUri", ""
                  "appType", "web" ]
            )

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode)

        let! appsHtml = browser.GetHtml "/admin/apps"
        let m = Regex.Match(appsHtml, "data-app=\"([^\"]+)\"")
        Assert.True(m.Success, "the Apps screen did not list the app that was just created")
        return m.Groups[1].Value
    }

/// Drive one whole sign-in, from the app's authorization request to the
/// authorization code coming back. Returns the `code`.
let private signIn (app: Fixtures.TestApp) (browser: Browser) (clientId: string) (subject: string) (email: string) =
    task {
        let authorizeUrl =
            "/connect/authorize?client_id=" + Uri.EscapeDataString clientId
            + "&response_type=code"
            + "&redirect_uri=https%3A%2F%2Fgrafana.example.com%2Fcallback"
            + "&scope=openid%20email%20profile"
            + "&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"
            + "&code_challenge_method=S256&state=app-state"

        // No session yet, so the authorize endpoint sends us to the picker,
        // carrying the whole authorization request to replay.
        let! toPicker = browser.Get authorizeUrl
        Assert.Equal(HttpStatusCode.Redirect, toPicker.StatusCode)
        Assert.Contains("/signin", location toPicker)

        let! toProvider = browser.Get (location toPicker |> fun l -> l.Replace("/signin", "/connect/fake/start"))
        Assert.Equal(HttpStatusCode.Redirect, toProvider.StatusCode)

        // The fake provider's own page, standing in for Discord's.
        let providerUrl = location toProvider
        let decoded = Uri.UnescapeDataString providerUrl
        let redirectUri = Regex.Match(decoded, "redirect_uri=([^&]+)").Groups[1].Value
        let state = Regex.Match(decoded, "state=([^&]+)").Groups[1].Value

        let! approved =
            browser.PostForm(
                providerUrl,
                "/dev/fake-provider/authorize",
                [ "redirect_uri", redirectUri
                  "state", state
                  "subject", subject
                  "email", email
                  "displayName", "Test User"
                  "emailVerified", "on" ]
            )

        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode)

        // Umbral's real callback: validates the correlation, exchanges, resolves
        // the user, establishes the session, replays the authorization request.
        let! backToUmbral = browser.Get(location approved)
        Assert.Equal(HttpStatusCode.Redirect, backToUmbral.StatusCode)

        let replayed = location backToUmbral
        let! why = lastFailure app.DbConfig
        Assert.True(replayed.Contains "/connect/authorize", $"sign-in did not complete: {why} (landed on {replayed})")

        let! issued = browser.Get replayed
        Assert.Equal(HttpStatusCode.Redirect, issued.StatusCode)

        let final = location issued

        // Back at the app, with a code, exactly where the whole thing started.
        Assert.StartsWith("https://grafana.example.com/callback", final)
        Assert.Contains("code=", final)
        Assert.Contains("state=app-state", final)

        return final
    }

[<Fact>]
let ``an operator claims an instance and a person signs in`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let browser = Browser(app.Client)

        let! clientId = claimAndConfigure app browser
        let! _ = signIn app browser clientId "fake-user-1" "sam@example.com"

        // One user, created by signing in — never by an operator.
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT count(*) FROM users"
        let! value = command.ExecuteScalarAsync()
        Assert.Equal(1, Convert.ToInt32 value)
    }

[<Fact>]
let ``signing in twice is the same person, not two`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let browser = Browser(app.Client)

        let! clientId = claimAndConfigure app browser
        let! _ = signIn app browser clientId "fake-user-1" "sam@example.com"

        // A second sign-in from a browser with no Umbral session, as a
        // different device would be.
        let second = Browser(app.Client)

        let! setupAgain = second.Get "/admin/setup"
        ignore setupAgain

        let! _ = signIn app second clientId "fake-user-1" "sam@example.com"

        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT count(*) FROM users"
        let! value = command.ExecuteScalarAsync()

        // FR-004: the subject is stable, so the second sign-in must find the
        // first user rather than mint another.
        Assert.Equal(1, Convert.ToInt32 value)
    }

[<Fact>]
let ``a disabled user cannot sign in`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let browser = Browser(app.Client)

        let! clientId = claimAndConfigure app browser
        let! _ = signIn app browser clientId "fake-user-1" "sam@example.com"

        // Disable them, as the operator would from the Users screen.
        use! connection = Database.openConnection app.DbConfig
        use read = connection.CreateCommand()
        read.CommandText <- "SELECT id FROM users LIMIT 1"
        let! idValue = read.ExecuteScalarAsync()
        do! Users.setEnabled app.DbConfig (string idValue) false

        // FR-022: a fresh browser, a fresh attempt, and they do not get in.
        let after = Browser(app.Client)
        // Concatenated, not interpolated: F# reads '%' in an interpolated
        // string as a format specifier, and this URL is full of percent-encoding.
        let authorizeUrl =
            "/connect/authorize?client_id=" + Uri.EscapeDataString clientId
            + "&response_type=code&redirect_uri=https%3A%2F%2Fgrafana.example.com%2Fcallback"
            + "&scope=openid&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"
            + "&code_challenge_method=S256&state=s"

        let! toPicker = after.Get authorizeUrl

        let! toProvider = after.Get(location toPicker |> fun l -> l.Replace("/signin", "/connect/fake/start"))
        let providerUrl = location toProvider
        let decoded = Uri.UnescapeDataString providerUrl
        let redirectUri = Regex.Match(decoded, "redirect_uri=([^&]+)").Groups[1].Value
        let state = Regex.Match(decoded, "state=([^&]+)").Groups[1].Value

        let! approved =
            after.PostForm(
                providerUrl,
                "/dev/fake-provider/authorize",
                [ "redirect_uri", redirectUri
                  "state", state
                  "subject", "fake-user-1"
                  "email", "sam@example.com"
                  "displayName", "Test User"
                  "emailVerified", "on" ]
            )

        let! backToUmbral = after.Get(location approved)

        // Refused at the callback, before a session is ever established.
        Assert.Contains("account-disabled", location backToUmbral)
    }
