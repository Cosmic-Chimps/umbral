module Umbral.Tests.FediverseReRegistrationTests

open System
open System.Net
open System.Text.RegularExpressions
open System.Threading.Tasks
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.StubMastodon

let private connector (app: Fixtures.TestApp) enabled =
    ConnectorStore.save
        app.DbConfig
        app.Clock
        { Slug = "fediverse"
          Kind = Fediverse
          DisplayName = "Fediverse"
          ClientId = ""
          ClientSecretRef = ""
          ClientSecretHint = ""
          IssuerUrl = None
          AllowedEmailDomains = []
          PasskeyEnrolment = InvitationsOnly
          FediverseAccess = FediverseAccess.Any
          IsEnabled = enabled
          IsShared = true
          CreatedUtc = app.Clock.GetUtcNow()
          UpdatedUtc = app.Clock.GetUtcNow() }
        None

let private ana =
    { Id = "109"
      Username = "ana"
      DisplayName = "Ana"
      Avatar = "https://mastodon.test/a.png"
      Url = "https://mastodon.test/@ana" }

let private setup () =
    task {
        let stub = new StubMastodon()
        let server = stub.AddServer("mastodon.test", Mastodon)
        server.AddAccount ana
        let! app = Fixtures.createAppWithFediverseHandler stub fakeSettings ignore
        do! connector app true
        let browser = Browser(app.Client)
        let! prepared = prepare app browser
        return stub, server, app, prepared
    }

/// Starts a sign-in from the app; returns the browser, the return url and the POST response.
let private start (app: Fixtures.TestApp) (prepared: Prepared) (account: string) =
    task {
        let browser = Browser(app.Client)
        let! toPicker = browser.Get(authorizeUrl prepared.ClientId "openid profile")
        let pickerUrl = toPicker.Headers.Location |> string
        let returnUrl = Uri.UnescapeDataString(Regex.Match(pickerUrl, "returnUrl=([^&]+)").Groups[1].Value)
        let step = "/connect/fediverse/start?returnUrl=" + Uri.EscapeDataString returnUrl

        let! posted =
            browser.PostForm(step, "/connect/fediverse/start", [ "account", account; "returnUrl", returnUrl ])

        return browser, posted
    }

let private finish (stub: StubMastodon) (app: Fixtures.TestApp) (prepared: Prepared) (browser: Browser) (posted: Net.Http.HttpResponseMessage) accountId =
    task {
        let approval = stub.Authorize(Uri(posted.Headers.Location |> string), accountId)

        let! back =
            browser.Get(
                $"/connect/fediverse/callback?code={Uri.EscapeDataString approval.Code}&state={Uri.EscapeDataString approval.State}"
            )

        let replayed = back.Headers.Location |> string
        Assert.Contains("/connect/authorize", replayed)
        let! issued = browser.Get replayed
        let code = Regex.Match(issued.Headers.Location |> string, "code=([^&]+)").Groups[1].Value |> Uri.UnescapeDataString
        let! exchanged = exchangeCode app.Client prepared.ClientId prepared.ClientSecret code
        let! tokens = json exchanged
        return segmentOf 1 (strOf tokens "id_token"), approval
    }

let private signIn stub app prepared account accountId =
    task {
        let! browser, posted = start app prepared account
        Assert.Equal(HttpStatusCode.Found, posted.StatusCode)
        return! finish stub app prepared browser posted accountId
    }

let private clientId (app: Fixtures.TestApp) =
    Flows.scalar app "SELECT client_id FROM fediverse_servers" []

let private registeredEvents (app: Fixtures.TestApp) =
    Flows.scalar app "SELECT count(*) FROM security_events WHERE event_type = 'fediverse_server_registered'" []

[<Fact>]
let ``a forgotten registration is made again after a day`` () =
    task {
        let! stub, server, app, prepared = setup ()
        let! _ = signIn stub app prepared "@ana@mastodon.test" "109"
        let! first = clientId app
        app.Clock.Advance(TimeSpan.FromHours 25.0)
        server.Forget()
        let! _ = signIn stub app prepared "@ana@mastodon.test" "109"
        let! second = clientId app
        Assert.Equal(2, server.CountOf "/api/v1/apps")
        Assert.NotEqual<string>(first, second)
        let! n = registeredEvents app
        Assert.Equal("2", n)
    }

[<Fact>]
let ``a forgotten registration with the server down shows the unreachable message`` () =
    task {
        let! stub, server, app, prepared = setup ()
        let! _ = signIn stub app prepared "@ana@mastodon.test" "109"
        app.Clock.Advance(TimeSpan.FromHours 25.0)
        server.Forget()
        server.Mode <- Down
        let before = server.CountOf "/api/v1/apps"
        let! _, posted = start app prepared "@ana@mastodon.test"
        Assert.Equal(HttpStatusCode.UnprocessableEntity, posted.StatusCode)
        let! html = posted.Content.ReadAsStringAsync()
        Assert.Contains("mastodon.test did not answer. Try again in a moment.", html)
        Assert.True(server.CountOf "/api/v1/apps" - before <= 1)
    }

[<Fact>]
let ``within a day the registration is not checked again`` () =
    task {
        let! stub, server, app, prepared = setup ()
        let! _ = signIn stub app prepared "@ana@mastodon.test" "109"
        let before = server.CountOf "/oauth/token"
        app.Clock.Advance(TimeSpan.FromHours 1.0)
        let! _ = signIn stub app prepared "@ana@mastodon.test" "109"
        Assert.Equal(1, server.CountOf "/oauth/token" - before)
        Assert.Equal(1, server.CountOf "/api/v1/apps")
    }

[<Fact>]
let ``a registration made for another callback address is made again`` () =
    task {
        let! stub, server, app, prepared = setup ()
        let! _ = signIn stub app prepared "@ana@mastodon.test" "109"
        let current = (List.head server.Registrations).Form["redirect_uris"]

        let! _ =
            Flows.scalar app "UPDATE fediverse_servers SET redirect_uri = 'https://old.example/connect/fediverse/callback'" []

        let! _ = signIn stub app prepared "@ana@mastodon.test" "109"
        Assert.Equal(2, server.CountOf "/api/v1/apps")
        Assert.Equal(current, (List.last server.Registrations).Form["redirect_uris"])
        let! stored = Flows.scalar app "SELECT redirect_uri FROM fediverse_servers" []
        Assert.Equal(current, stored)
    }

[<Fact>]
let ``invalid_client at the code exchange deletes the registration`` () =
    task {
        let! stub, server, app, prepared = setup ()
        let! browser, posted = start app prepared "@ana@mastodon.test"
        Assert.Equal(HttpStatusCode.Found, posted.StatusCode)
        server.Forget()
        let approval = stub.Authorize(Uri(posted.Headers.Location |> string), "109")

        let! back =
            browser.Get(
                $"/connect/fediverse/callback?code={Uri.EscapeDataString approval.Code}&state={Uri.EscapeDataString approval.State}"
            )

        Assert.Equal(HttpStatusCode.Found, back.StatusCode)
        Assert.Contains("msg=sign-in-failed", back.Headers.Location |> string)
        let! n = Flows.scalar app "SELECT count(*) FROM fediverse_servers" []
        Assert.Equal("0", n)
        let! _ = signIn stub app prepared "@ana@mastodon.test" "109"
        Assert.Equal(2, server.CountOf "/api/v1/apps")
    }
