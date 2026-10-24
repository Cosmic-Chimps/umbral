module Umbral.Tests.FediverseSignInTests

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

[<Fact>]
let ``the picker offers Fediverse and the step has the account field`` () =
    task {
        let! _, _, app, prepared = setup ()
        let browser = Browser(app.Client)
        let! toPicker = browser.Get(authorizeUrl prepared.ClientId "openid profile")
        let! picker = browser.GetHtml(toPicker.Headers.Location |> string)
        Assert.Contains("Sign in with Fediverse", picker)
        Assert.Contains("/connect/fediverse/start?returnUrl=", picker)
        let! step = browser.GetHtml "/connect/fediverse/start?returnUrl=%2Fx"
        Assert.Contains("name=\"account\"", step)
        Assert.Contains("autocomplete=\"username\"", step)
        Assert.Contains("inputmode=\"email\"", step)
        Assert.Contains("autofocus", step)
        do! connector app false
        let! gone = browser.Get "/connect/fediverse/start?returnUrl=%2Fx"
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode)
    }

[<Fact>]
let ``first sign-in registers the server and issues the profile`` () =
    task {
        let! stub, server, app, prepared = setup ()
        let! browser, posted = start app prepared "@ana@mastodon.test"
        Assert.Equal(HttpStatusCode.Found, posted.StatusCode)
        let location = posted.Headers.Location |> string
        Assert.StartsWith("https://mastodon.test/oauth/authorize", location)
        Assert.Contains("code_challenge_method=S256", location)
        Assert.Matches("scope=read(%3A|:)accounts", location)
        Assert.Contains("state=", location)
        let registration = Assert.Single server.Registrations
        Assert.Equal("Umbral", registration.Form["client_name"])
        Assert.Equal("read:accounts", registration.Form["scopes"])
        Assert.EndsWith("/connect/fediverse/callback", registration.Form["redirect_uris"])
        let! count = Flows.scalar app "SELECT count(*) FROM fediverse_servers WHERE host='mastodon.test'" []
        Assert.Equal(1L, Convert.ToInt64 count)
        let! secret = Flows.scalar app "SELECT client_secret FROM fediverse_servers" []
        Assert.NotEqual<string>(registration.ClientSecret, string secret)
        let! events =
            Flows.scalar app "SELECT count(*) FROM security_events WHERE event_type='fediverse_server_registered'" []
        Assert.Equal(1L, Convert.ToInt64 events)
        let! idToken, _ = finish stub app prepared browser posted "109"
        Assert.Equal("Ana", strOf idToken "name")
        Assert.Equal("ana@mastodon.test", strOf idToken "preferred_username")
        Assert.Equal("https://mastodon.test/a.png", strOf idToken "picture")
        Assert.Equal("https://mastodon.test/@ana", strOf idToken "profile")
        Assert.False(fst (idToken.TryGetProperty "email"))
        Assert.Equal("fediverse", strOf idToken "idp")
    }

[<Fact>]
let ``a second account on the same server does not register again`` () =
    task {
        let! stub, server, app, prepared = setup ()
        server.AddAccount { ana with Id = "110"; Username = "bea"; DisplayName = "Bea" }
        let! _ = signIn stub app prepared "@ana@mastodon.test" "109"
        let! idToken, _ = signIn stub app prepared "@bea@mastodon.test" "110"
        Assert.Equal("bea@mastodon.test", strOf idToken "preferred_username")
        Assert.Equal(1, server.CountOf "/api/v1/apps")
    }

[<Fact>]
let ``a changed display name is refreshed on the next sign-in`` () =
    task {
        let! stub, server, app, prepared = setup ()
        let! first, _ = signIn stub app prepared "@ana@mastodon.test" "109"
        server.AddAccount { ana with DisplayName = "Ana B." }
        let! second, _ = signIn stub app prepared "@ana@mastodon.test" "109"
        Assert.Equal(strOf first "sub", strOf second "sub")
        Assert.Equal("Ana B.", strOf second "name")
    }

[<Fact>]
let ``a GoToSocial server signs in with a bare host`` () =
    task {
        let! stub, _, app, prepared = setup ()
        let gts = stub.AddServer("gts.test", GoToSocial)
        gts.AddAccount { ana with Url = "https://gts.test/@ana" }
        let! idToken, _ = signIn stub app prepared "gts.test" "109"
        Assert.Equal("Ana", strOf idToken "name")
    }

[<Fact>]
let ``a handle on an alias domain signs in at the server WebFinger names`` () =
    task {
        let! stub, server, app, prepared = setup ()
        let alias = stub.AddServer("alias.test", Mastodon)
        alias.SetWebFinger("acct:ana@alias.test", "https://mastodon.test/users/ana")
        let! browser, posted = start app prepared "@ana@alias.test"
        Assert.StartsWith("https://mastodon.test/", posted.Headers.Location |> string)
        Assert.Equal(0, alias.CountOf "/api/v1/apps")
        Assert.Equal(1, server.CountOf "/api/v1/apps")
    }

[<Fact>]
let ``replaying a callback fails`` () =
    task {
        let! stub, _, app, prepared = setup ()
        let! browser, posted = start app prepared "@ana@mastodon.test"
        let approval = stub.Authorize(Uri(posted.Headers.Location |> string), "109")
        let url =
            $"/connect/fediverse/callback?code={Uri.EscapeDataString approval.Code}&state={Uri.EscapeDataString approval.State}"
        let! first = browser.Get url
        Assert.Contains("/connect/authorize", first.Headers.Location |> string)
        let! second = browser.Get url
        Assert.Equal(HttpStatusCode.Found, second.StatusCode)
        Assert.Contains("msg=sign-in-failed", second.Headers.Location |> string)
    }

[<Fact>]
let ``a forged state fails`` () =
    task {
        let! stub, _, app, prepared = setup ()
        let! browser, posted = start app prepared "@ana@mastodon.test"
        let approval = stub.Authorize(Uri(posted.Headers.Location |> string), "109")
        let! response =
            browser.Get $"/connect/fediverse/callback?code={Uri.EscapeDataString approval.Code}&state=forged"
        Assert.Equal(HttpStatusCode.Found, response.StatusCode)
        Assert.Contains("msg=sign-in-failed", response.Headers.Location |> string)
    }
