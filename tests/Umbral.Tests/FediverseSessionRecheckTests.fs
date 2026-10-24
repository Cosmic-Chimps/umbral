module Umbral.Tests.FediverseSessionRecheckTests

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

let private scope = "openid profile offline_access"

/// Signs in through the stub server and returns the browser (with its session) and the tokens.
let private signIn () =
    task {
        let! stub, _, app, prepared = setup ()
        let browser = Browser(app.Client)
        let! toPicker = browser.Get(authorizeUrl prepared.ClientId scope)
        let pickerUrl = toPicker.Headers.Location |> string
        let returnUrl = Uri.UnescapeDataString(Regex.Match(pickerUrl, "returnUrl=([^&]+)").Groups[1].Value)
        let step = "/connect/fediverse/start?returnUrl=" + Uri.EscapeDataString returnUrl

        let! posted =
            browser.PostForm(step, "/connect/fediverse/start", [ "account", "ana@mastodon.test"; "returnUrl", returnUrl ])

        let approval = stub.Authorize(Uri(posted.Headers.Location |> string), "109")

        let! back =
            browser.Get(
                $"/connect/fediverse/callback?code={Uri.EscapeDataString approval.Code}&state={Uri.EscapeDataString approval.State}"
            )

        let! issued = browser.Get(back.Headers.Location |> string)
        let code = Regex.Match(issued.Headers.Location |> string, "code=([^&]+)").Groups[1].Value |> Uri.UnescapeDataString
        let! exchanged = exchangeCode app.Client prepared.ClientId prepared.ClientSecret code
        let! tokens = json exchanged
        return app, prepared, browser, strOf tokens "refresh_token"
    }

let private tighten (app: Fixtures.TestApp) servers =
    ConnectorStore.setFediverseAccess app.DbConfig app.Clock "fediverse" { OnlyListed = true; Servers = servers }

let private failureDetails (app: Fixtures.TestApp) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT ifnull(detail, '') FROM security_events WHERE event_type = 'sign_in_failed' ORDER BY id"
        use! reader = command.ExecuteReaderAsync()
        let rows = ResizeArray<string>()

        while reader.Read() do
            rows.Add(reader.GetString 0)

        return List.ofSeq rows
    }

[<Fact>]
let ``after the server is removed from the list, the session is signed out at authorize`` () =
    task {
        let! app, prepared, browser, _ = signIn ()
        let! _ = tighten app [ "other.test" ]

        let! response = browser.Get(authorizeUrl prepared.ClientId "openid")
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.StartsWith("/signin?returnUrl=", response.Headers.Location |> string)

        let! details = failureDetails app
        Assert.Equal("fediverse:server_not_allowed;server=mastodon.test", Assert.Single details)
    }

[<Fact>]
let ``after the server is removed from the list, a refresh is invalid_grant`` () =
    task {
        let! app, prepared, _, refreshToken = signIn ()
        let! _ = tighten app [ "other.test" ]

        let! response = Journeys.refresh app.Client prepared.ClientId prepared.ClientSecret refreshToken
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        let! body = json response
        Assert.Equal("invalid_grant", strOf body "error")

        let! details = failureDetails app
        Assert.Equal("fediverse:server_not_allowed;server=mastodon.test", Assert.Single details)
    }

[<Fact>]
let ``while the server is still listed, the session and the refresh are honoured`` () =
    task {
        let! app, prepared, browser, refreshToken = signIn ()
        let! _ = tighten app [ "mastodon.test"; "other.test" ]

        let! response = browser.Get(authorizeUrl prepared.ClientId "openid")
        Assert.Contains("code=", response.Headers.Location |> string)

        let! refreshed = Journeys.refresh app.Client prepared.ClientId prepared.ClientSecret refreshToken
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode)
    }
