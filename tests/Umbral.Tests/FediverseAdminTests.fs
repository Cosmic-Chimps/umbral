/// The operator's pane of the Fediverse connector (spec 010) and the server
/// scope it sets on the sign-in side.
module Umbral.Tests.FediverseAdminTests

open System
open System.Net
open System.Text.RegularExpressions
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.Flows
open Umbral.Tests.Infrastructure.StubMastodon

let private texts () = Umbral.Views.Texts.current ()
let private path = "/admin/connectors/fediverse"

let private entryTags (html: string) =
    [ for m in Regex.Matches(html, "<a\\b[^>]*data-connector-entry=\"([^\"]*)\"[^>]*>") -> m.Groups[1].Value, m.Value ]

let private save (operator: Browser) shared scope servers =
    operator.PostForm(path, path, [ "shared", shared; "scope", scope; "servers", servers ])

let private columns (app: Fixtures.TestApp) =
    task {
        let! scope = scalar app "SELECT fediverse_scope FROM connectors WHERE slug='fediverse'" []
        let! servers = scalar app "SELECT fediverse_servers FROM connectors WHERE slug='fediverse'" []
        return string scope, string servers
    }

let private operatorApp () =
    task {
        let! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser(app.Client)
        do! claimOperator app operator
        return app, operator
    }

[<Fact>]
let ``not set up the pane offers to enable and the list entry is muted`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! list = operator.GetHtml "/admin/connectors"
        let tags = entryTags list
        let names = tags |> List.map fst
        let f = List.findIndex ((=) "fediverse") names
        Assert.True(f > List.findIndex ((=) "google") names)
        Assert.True(f < List.findIndex ((=) "passkey") names)
        Assert.Contains("data-entry-muted", snd tags[f])
        let! html = operator.GetHtml path
        Assert.Contains("data-connector=\"fediverse\"", html)
        Assert.Contains("data-connector-kind=\"fediverse\"", html)
        Assert.DoesNotContain("data-connector-state=\"enabled\"", html)
        Assert.Contains((texts ()).NotConfigured, html)
        Assert.Matches("<input[^>]*data-callback-url[^>]*/connect/fediverse/callback\"|<input[^>]*/connect/fediverse/callback\"[^>]*data-callback-url", html)
        Assert.Contains("data-no-email-note", html)
        Assert.Contains("data-instance-scope", html)
        Assert.Matches("value=\"any\"[^>]*checked|checked[^>]*value=\"any\"", html)
        Assert.Contains("data-instance-list", html)
        Assert.Contains("Enable Fediverse", html)
    }

[<Fact>]
let ``enabling creates the row enabled and shared`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! r = save operator "true" "any" ""
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode)
        Assert.Contains("msg=fediverse-connector-saved", location r)
        let! stored = ConnectorStore.findBySlug app.DbConfig "fediverse"
        Assert.True(stored.IsSome && stored.Value.IsEnabled && stored.Value.IsShared)
        let! ev = scalar app "SELECT count(*) FROM security_events WHERE event_type='connector_created' AND connector_slug='fediverse'" []
        Assert.Equal("1", string ev)
        let! html = operator.GetHtml path
        Assert.Contains("data-connector-state=\"enabled\"", html)
        Assert.Contains((texts ()).SaveConnector, html)
    }

[<Fact>]
let ``a listed scope stores the normalised servers`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! r = save operator "true" "listed" "Social.Example.org, fosstodon.org"
        Assert.Contains("fediverse-connector-saved", location r)
        let! scope, servers = columns app
        Assert.Equal("listed", scope)
        Assert.Equal("social.example.org fosstodon.org", servers)
        let! html = operator.GetHtml path
        Assert.Contains("social.example.org, fosstodon.org", html)
    }

[<Fact>]
let ``refused server entries are 422 and nothing is saved`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! r = save operator "true" "any" ""
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode)
        let! wild = save operator "true" "listed" "*.example.social"
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wild.StatusCode)
        let body = WebUtility.HtmlDecode(Assert.IsType<string>(wild.Content.ReadAsStringAsync().Result))
        Assert.Contains((texts ()).FediverseServerWildcard "*.example.social", body)
        Assert.Contains("*.example.social", body)
        let! bad = save operator "true" "listed" "not a server/path"
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode)
        let body2 = WebUtility.HtmlDecode(bad.Content.ReadAsStringAsync().Result)
        Assert.Contains("is not a server address.", body2)
        let! scope, servers = columns app
        Assert.Equal("any", scope)
        Assert.Equal("", servers)
    }

[<Fact>]
let ``a listed scope with an empty list warns`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! r = save operator "true" "listed" ""
        Assert.Contains("fediverse-connector-saved", location r)
        let! html = operator.GetHtml path
        Assert.Contains("data-servers-empty", html)
    }

[<Fact>]
let ``an unknown scope word redirects as invalid`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! r = save operator "true" "everyone" ""
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode)
        Assert.Contains("fediverse-connector-invalid", location r)
    }

[<Fact>]
let ``toggle and delete use the generic routes`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        do! seedFakeConnector app
        let! _ = save operator "true" "any" ""
        let! t = operator.PostForm(path, path + "/toggle", [])
        Assert.Equal(HttpStatusCode.Redirect, t.StatusCode)
        let! html = operator.GetHtml path
        Assert.Contains("data-connector-state=\"disabled\"", html)
        let! d = operator.PostForm(path, path + "/delete", [])
        Assert.Equal(HttpStatusCode.Redirect, d.StatusCode)
        let! stored = ConnectorStore.findBySlug app.DbConfig "fediverse"
        Assert.True stored.IsNone
    }

let private ana =
    { Id = "109"
      Username = "ana"
      DisplayName = "Ana"
      Avatar = "https://mastodon.test/a.png"
      Url = "https://mastodon.test/@ana" }

let private signInSetup scope servers =
    task {
        let stub = new StubMastodon()
        let server = stub.AddServer("mastodon.test", Mastodon)
        server.AddAccount ana
        stub.AddServer("other.test", Mastodon) |> ignore
        let! app = Fixtures.createAppWithFediverseHandler stub fakeSettings ignore
        let browser = Journeys.Browser(app.Client)
        let! prepared = prepare app browser
        let! _ = browser.PostForm(path, path, [ "shared", "true"; "scope", scope; "servers", servers ])
        return stub, app, prepared
    }

let private start (app: Fixtures.TestApp) (prepared: Prepared) (account: string) =
    task {
        let browser = Journeys.Browser(app.Client)
        let! toPicker = browser.Get(authorizeUrl prepared.ClientId "openid profile")
        let pickerUrl = toPicker.Headers.Location |> string
        let returnUrl = Uri.UnescapeDataString(Regex.Match(pickerUrl, "returnUrl=([^&]+)").Groups[1].Value)
        let step = "/connect/fediverse/start?returnUrl=" + Uri.EscapeDataString returnUrl
        let! posted = browser.PostForm(step, "/connect/fediverse/start", [ "account", account; "returnUrl", returnUrl ])
        return browser, posted
    }

[<Fact>]
let ``a listed scope lets a listed server through and stops another before any request`` () =
    task {
        let! stub, app, prepared = signInSetup "listed" "mastodon.test"
        use _ = app
        let! _, ok = start app prepared "@ana@mastodon.test"
        Assert.Equal(HttpStatusCode.Found, ok.StatusCode)
        Assert.StartsWith("https://mastodon.test/", ok.Headers.Location |> string)
        let! _, no = start app prepared "@bob@other.test"
        Assert.Equal(HttpStatusCode.UnprocessableEntity, no.StatusCode)
        let body = WebUtility.HtmlDecode(no.Content.ReadAsStringAsync().Result)
        Assert.Contains("Accounts on other.test cannot sign in here. Use another way to sign in.", body)
        Assert.Empty((stub.Server "other.test").Requests)
    }

[<Fact>]
let ``a scope narrowed after the start refuses the callback`` () =
    task {
        let! stub, app, prepared = signInSetup "any" ""
        use _ = app
        let! browser, posted = start app prepared "@ana@mastodon.test"
        Assert.Equal(HttpStatusCode.Found, posted.StatusCode)
        do! ConnectorStore.setFediverseAccess app.DbConfig app.Clock "fediverse" { OnlyListed = true; Servers = [ "fediverse.example" ] }
        let approval = stub.Authorize(Uri(posted.Headers.Location |> string), "109")
        let! back =
            browser.Get(
                $"/connect/fediverse/callback?code={Uri.EscapeDataString approval.Code}&state={Uri.EscapeDataString approval.State}"
            )
        Assert.Equal(HttpStatusCode.Found, back.StatusCode)
        Assert.Contains("msg=account-not-allowed", back.Headers.Location |> string)
    }
