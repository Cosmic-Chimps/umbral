/// FR-010 and D11: a connector changes while the process keeps running, and
/// every htmx-enhanced action works with htmx absent.
///
/// These two belong together because they are both claims about the same
/// handlers, and both are the kind of claim that quietly stops being true.
module Umbral.Tests.ConnectorRuntimeTests

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

type private Browser(client: HttpClient) =
    let cookies = Collections.Generic.Dictionary<string, string>()

    let send (message: HttpRequestMessage) =
        task {
            if cookies.Count > 0 then
                let header = cookies |> Seq.map (fun kv -> $"{kv.Key}={kv.Value}") |> String.concat "; "
                message.Headers.Add("Cookie", header)

            let! response = client.SendAsync message

            match response.Headers.TryGetValues "Set-Cookie" with
            | true, values ->
                for value in Option.defaultValue Seq.empty (Option.ofObj values) do
                    let first = value.Split(';').[0]

                    match first.Split('=', 2) with
                    | [| name; v |] -> cookies[name] <- v
                    | _ -> ()
            | _ -> ()

            return response
        }

    member _.Get(url: string) = send (new HttpRequestMessage(HttpMethod.Get, url))

    member this.GetHtml(url: string) =
        task {
            let! response = this.Get url
            return! response.Content.ReadAsStringAsync()
        }

    /// `head 1`: the layout renders its own sign-out form, so a page carries
    /// two tokens and posting both decrypts as neither.
    member this.TokenFrom(url: string) =
        task {
            let! html = this.GetHtml url
            let m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")
            return if m.Success then m.Groups[1].Value else failwith $"no antiforgery token on {url}"
        }

    member this.PostForm(formPage: string, action: string, fields: (string * string) list, ?htmx: bool) =
        task {
            let! token = this.TokenFrom formPage
            let message = new HttpRequestMessage(HttpMethod.Post, action)
            message.Content <- new FormUrlEncodedContent(dict (("__RequestVerificationToken", token) :: fields))

            if defaultArg htmx false then
                message.Headers.Add("HX-Request", "true")

            return! send message
        }

    /// The actual cross-site scenario: the victim's cookies ARE sent, because
    /// the browser attaches them to any request to this origin. The token is
    /// the only thing the attacker cannot supply.
    member _.PostWithoutToken(url: string, fields: (string * string) list) =
        let message = new HttpRequestMessage(HttpMethod.Post, url)
        message.Content <- new FormUrlEncodedContent(dict fields)
        send message

let private claim (app: Fixtures.TestApp) (browser: Browser) =
    task {
        let token = app.Services.GetRequiredService<AdminAuth.SetupToken>()

        let! response =
            browser.PostForm(
                "/admin/setup",
                "/admin/setup",
                [ "token", token.Value
                  "credential", "a-very-good-credential"
                  "confirm", "a-very-good-credential" ]
            )

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
    }

let private seedConnector (app: Fixtures.TestApp) (enabled: bool) =
    task {
        let secrets = app.Services.GetRequiredService<Secrets.ISecretStore>()
        let! reference = secrets.Put("connector/discord/client_secret", "the-first-secret")

        do!
            ConnectorStore.save
                app.DbConfig
                app.Clock
                { Slug = "discord"
                  Kind = Discord
                  DisplayName = "Discord"
                  ClientId = "1234"
                  ClientSecretRef = reference.Encoded
                  ClientSecretHint = Secrets.hint "the-first-secret"
                  ExtraScopes = []
                  IssuerUrl = None
                  IsEnabled = enabled
                  IsShared = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None
    }

[<Fact>]
let ``disabling removes the button on the very next picker load`` () =
    task {
        use! app = Fixtures.createApp ()
        let browser = Browser(app.Client)
        do! claim app browser
        do! seedConnector app true

        let! before = browser.GetHtml "/signin"
        Assert.Contains("data-connector=\"discord\"", before)

        // confirm=yes because this is the only connector and FR-023 asks
        // before closing the last door. The guard has its own test below.
        let! toggled =
            browser.PostForm("/admin/connectors", "/admin/connectors/discord/toggle?confirm=yes", [])

        Assert.Equal(HttpStatusCode.Redirect, toggled.StatusCode)

        // The very next load, with the process never restarted. This is the
        // whole reason the connectors are read per-request rather than bound
        // to startup options.
        let! after = browser.GetHtml "/signin"
        Assert.DoesNotContain("data-connector=\"discord\"", after)
    }

[<Fact>]
let ``a disabled connector's start route stops working immediately`` () =
    task {
        use! app = Fixtures.createApp ()
        let browser = Browser(app.Client)
        do! claim app browser
        do! seedConnector app true

        let! working = browser.Get "/connect/discord/start"
        Assert.Equal(HttpStatusCode.Redirect, working.StatusCode)

        let! _ =
            browser.PostForm("/admin/connectors", "/admin/connectors/discord/toggle?confirm=yes", [])

        // 404, not a message: a connector that is turned off should not be
        // discoverable by probing.
        let! refused = browser.Get "/connect/discord/start"
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode)
    }

[<Fact>]
let ``replacing the secret takes effect without a restart`` () =
    task {
        use! app = Fixtures.createApp ()
        let browser = Browser(app.Client)
        do! claim app browser
        do! seedConnector app true

        let! saved =
            browser.PostForm(
                "/admin/connectors",
                "/admin/connectors/discord",
                [ "clientId", "1234"; "clientSecret", "the-second-secret" ]
            )

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode)

        // Read back the way the callback does, in the same process.
        let secrets = app.Services.GetRequiredService<Secrets.ISecretStore>()
        let! connector = ConnectorStore.findBySlug app.DbConfig "discord"

        match connector with
        | None -> failwith "the connector vanished"
        | Some connector ->
            match Secrets.SecretRef.Decode connector.ClientSecretRef with
            | Error e -> failwith e
            | Ok reference ->
                let! recovered = secrets.Get reference
                Assert.Equal(Some "the-second-secret", recovered)
    }

[<Fact>]
let ``saving with a blank secret keeps the stored one`` () =
    task {
        use! app = Fixtures.createApp ()
        let browser = Browser(app.Client)
        do! claim app browser
        do! seedConnector app true

        // The screen cannot show a saved secret back, so demanding it on every
        // edit would mean an operator changing a client id has to go and find
        // the secret again.
        let! _ =
            browser.PostForm(
                "/admin/connectors",
                "/admin/connectors/discord",
                [ "clientId", "4321"; "clientSecret", "" ]
            )

        let secrets = app.Services.GetRequiredService<Secrets.ISecretStore>()
        let! connector = ConnectorStore.findBySlug app.DbConfig "discord"

        match connector with
        | None -> failwith "the connector vanished"
        | Some connector ->
            Assert.Equal("4321", connector.ClientId)

            match Secrets.SecretRef.Decode connector.ClientSecretRef with
            | Error e -> failwith e
            | Ok reference ->
                let! recovered = secrets.Get reference
                Assert.Equal(Some "the-first-secret", recovered)
    }

[<Fact>]
let ``a saved secret never appears in a response`` () =
    task {
        use! app = Fixtures.createApp ()
        let browser = Browser(app.Client)
        do! claim app browser
        do! seedConnector app true

        // SC-008. The Connectors screen is the one place a secret could leak
        // back out, and it is the page an operator screenshots for help.
        let! html = browser.GetHtml "/admin/connectors"
        Assert.DoesNotContain("the-first-secret", html)
        Assert.Contains("••••", html)
    }

[<Fact>]
let ``turning off the last enabled connector asks first`` () =
    task {
        use! app = Fixtures.createApp ()
        let browser = Browser(app.Client)
        do! claim app browser
        do! seedConnector app true

        // FR-023. Not a refusal — an operator may well mean to close the door,
        // they just should not do it by accident.
        let! first = browser.PostForm("/admin/connectors", "/admin/connectors/discord/toggle", [])
        Assert.Contains("connector-last-enabled", first.Headers.Location |> string)

        let! connector = ConnectorStore.findBySlug app.DbConfig "discord"
        Assert.True((connector |> Option.map (fun c -> c.IsEnabled)) = Some true, "it must still be enabled")

        let! confirmed =
            browser.PostForm("/admin/connectors", "/admin/connectors/discord/toggle?confirm=yes", [])

        Assert.Contains("connector-disabled", confirmed.Headers.Location |> string)
    }

// ---------------------------------------------------------------------------
// T082 — htmx is an enhancement, never a requirement
// ---------------------------------------------------------------------------

[<Fact>]
let ``the connector toggle works identically with and without htmx`` () =
    task {
        use! app = Fixtures.createApp ()
        let browser = Browser(app.Client)
        do! claim app browser
        do! seedConnector app true

        // Seed a second one so neither toggle trips the last-connector guard.
        let secrets = app.Services.GetRequiredService<Secrets.ISecretStore>()
        let! reference = secrets.Put("connector/github/client_secret", "gh-secret")

        do!
            ConnectorStore.save
                app.DbConfig
                app.Clock
                { Slug = "github"
                  Kind = GitHub
                  DisplayName = "GitHub"
                  ClientId = "gh"
                  ClientSecretRef = reference.Encoded
                  ClientSecretHint = "cret"
                  ExtraScopes = []
                  IssuerUrl = None
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None

        let! withoutHtmx = browser.PostForm("/admin/connectors", "/admin/connectors/discord/toggle", [])

        let! withHtmx =
            browser.PostForm("/admin/connectors", "/admin/connectors/discord/toggle", [], htmx = true)

        // Both change the state. Progressive enhancement is what lets one test
        // drive both paths, and what keeps the admin UI usable without JS.
        Assert.Equal(HttpStatusCode.Redirect, withoutHtmx.StatusCode)
        Assert.Equal(HttpStatusCode.Redirect, withHtmx.StatusCode)

        let! connector = ConnectorStore.findBySlug app.DbConfig "discord"
        Assert.True((connector |> Option.map (fun c -> c.IsEnabled)) = Some true, "two toggles return to enabled")
    }

[<Fact>]
let ``every admin POST refuses a request with no antiforgery token`` () =
    task {
        use! app = Fixtures.createApp ()
        let browser = Browser(app.Client)
        do! claim app browser
        do! seedConnector app true

        // Signed in, so the operator cookie is sent — exactly as a browser
        // would on a forged request from another site. The missing token is
        // the only thing standing between that and a state change.
        for url in
            [ "/admin/connectors/discord"
              "/admin/connectors/discord/toggle"
              "/admin/apps" ] do
            let! response = browser.PostWithoutToken(url, [ "clientId", "evil" ])

            Assert.True(
                response.StatusCode = HttpStatusCode.BadRequest
                || response.StatusCode = HttpStatusCode.InternalServerError,
                $"{url} accepted a post with no antiforgery token ({response.StatusCode})"
            )

        // And nothing changed.
        let! connector = ConnectorStore.findBySlug app.DbConfig "discord"
        Assert.True((connector |> Option.map (fun c -> c.ClientId)) = Some "1234")
    }
