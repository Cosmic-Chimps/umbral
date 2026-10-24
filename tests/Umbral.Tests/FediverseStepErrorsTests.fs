module Umbral.Tests.FediverseStepErrorsTests

open System
open System.Net
open System.Text.RegularExpressions
open System.Threading.Tasks
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Infrastructure.Providers
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

let private setupWith (resolver: Fediverse.HostResolver option) (hosts: (string * ServerMode) list) =
    task {
        let stub = new StubMastodon()

        for host, mode in hosts do
            let server = stub.AddServer(host, mode)
            server.AddAccount ana

        let! app =
            match resolver with
            | Some r -> Fixtures.createAppWithFediverseHandlerAndResolver stub r fakeSettings ignore
            | None -> Fixtures.createAppWithFediverseHandler stub fakeSettings ignore

        do! connector app true
        let browser = Browser(app.Client)
        let! prepared = prepare app browser
        return stub, app, prepared
    }

let private refused (app: Fixtures.TestApp) prepared (typed: string) =
    task {
        let! _, posted = start app prepared typed
        Assert.Equal(HttpStatusCode.UnprocessableEntity, posted.StatusCode)
        let! html = posted.Content.ReadAsStringAsync()
        Assert.Contains($"value=\"{System.Net.WebUtility.HtmlEncode typed}\"", html)
        Assert.Contains("aria-invalid=\"true\"", html)
        Assert.Contains("autofocus", html)
        return html
    }

let private events (app: Fixtures.TestApp) (like: string) =
    Flows.scalar app $"SELECT count(*) FROM security_events WHERE detail LIKE '{like}'" []

[<Fact>]
let ``not a handle is refused with the handle message and an event`` () =
    task {
        let! _, app, prepared = setupWith None [ "x.test", Mastodon ]
        let! html = refused app prepared "not a handle"
        Assert.Contains("Enter a handle like", html)
        let! n = Flows.scalar app "SELECT count(*) FROM security_events WHERE detail = 'fediverse:not_a_server'" []
        Assert.Equal("1", n)
    }

[<Theory>]
[<InlineData("Akkoma")>]
[<InlineData("Pleroma")>]
[<InlineData("Misskey")>]
[<InlineData("Sharkey")>]
[<InlineData("MastodonNoPkce")>]
let ``a server without the sign-in is refused as unsupported`` (name: string) =
    task {
        let mode =
            match name with
            | "Akkoma" -> Akkoma
            | "Pleroma" -> Pleroma
            | "Misskey" -> Misskey
            | "Sharkey" -> Sharkey
            | _ -> MastodonNoPkce

        let! stub, app, prepared = setupWith None [ "x.test", mode ]
        let! html = refused app prepared "x.test"
        Assert.Contains("does not offer the sign-in Umbral uses", html)
        Assert.Contains("x.test", html)
        Assert.Equal(0, (stub.Server "x.test").CountOf "/api/v1/apps")
    }

[<Fact>]
let ``a server that is down is unreachable`` () =
    task {
        let! _, app, prepared = setupWith None [ "x.test", Down ]
        let! html = refused app prepared "x.test"
        Assert.Contains("x.test did not answer. Try again in a moment.", html)
    }

[<Fact>]
let ``a slow server is unreachable`` () =
    task {
        let! _, app, prepared = setupWith None [ "x.test", Slow ]
        let! html = refused app prepared "x.test"
        Assert.Contains("x.test did not answer. Try again in a moment.", html)
    }

[<Theory>]
[<InlineData("localhost")>]
[<InlineData("127.0.0.1")>]
[<InlineData("10.0.0.5")>]
let ``local and private addresses are refused by parsing`` (typed: string) =
    task {
        let! _, app, prepared = setupWith None [ "x.test", Mastodon ]
        let! html = refused app prepared typed
        Assert.Contains("Enter a handle like", html)
    }

[<Fact>]
let ``a name that resolves only to a private address is refused`` () =
    task {
        let resolver = Fediverse.HostResolver(fun _ _ -> Task.FromResult [| IPAddress.Parse "10.1.2.3" |])
        let! stub, app, prepared = setupWith (Some resolver) [ "inside.test", Mastodon ]
        let! html = refused app prepared "inside.test"
        Assert.Contains("Enter a handle like", html)
        Assert.Empty((stub.Server "inside.test").Requests)
        let! n = events app "%non_public_address%"
        Assert.True((n <> "0"))
    }

[<Fact>]
let ``access denied at the server returns with access-denied`` () =
    task {
        let! _, app, prepared = setupWith None [ "x.test", Mastodon ]
        let! browser, posted = start app prepared "x.test"
        Assert.Equal(HttpStatusCode.Found, posted.StatusCode)
        let state = Regex.Match(posted.Headers.Location |> string, "state=([^&]+)").Groups[1].Value
        let! back = browser.Get $"/connect/fediverse/callback?error=access_denied&state={state}"
        Assert.Equal(HttpStatusCode.Found, back.StatusCode)
        Assert.Contains("msg=access-denied", back.Headers.Location |> string)
    }

[<Fact>]
let ``a post without the antiforgery token is refused and registers nothing`` () =
    task {
        let! stub, app, prepared = setupWith None [ "x.test", Mastodon ]
        let browser = Browser(app.Client)
        let! toPicker = browser.Get(authorizeUrl prepared.ClientId "openid profile")
        let pickerUrl = toPicker.Headers.Location |> string
        let returnUrl = Uri.UnescapeDataString(Regex.Match(pickerUrl, "returnUrl=([^&]+)").Groups[1].Value)

        let! posted =
            browser.PostWithoutToken("/connect/fediverse/start", [ "account", "x.test"; "returnUrl", returnUrl ])

        Assert.Equal(HttpStatusCode.BadRequest, posted.StatusCode)
        Assert.Equal(0, (stub.Server "x.test").CountOf "/api/v1/apps")
    }
