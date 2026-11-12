/// The operator's pane of the Bluesky connector (spec 015, T009) and the picker's
/// button for it (T010): what the pane shows and saves, and what it refuses where
/// the instance's address cannot work for Bluesky.
module Umbral.Tests.BlueskyAdminTests

open System
open System.Net
open System.Text.RegularExpressions
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.FakeAtproto
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.Flows
open Umbral.Tests.BlueskySignInTests

let private texts () = Umbral.Views.Texts.current ()
let private path = "/admin/connectors/bluesky"
/// A page with a form of its own, for the token: the Bluesky pane has none where
/// the address cannot work.
let private tokenPage = "/admin/connectors/fediverse"

let private entryTags (html: string) =
    [ for m in Regex.Matches(html, "<a\\b[^>]*data-connector-entry=\"([^\"]*)\"[^>]*>") -> m.Groups[1].Value, m.Value ]

let private save (operator: Browser) shared scope servers =
    operator.PostForm(tokenPage, path, [ "shared", shared; "scope", scope; "servers", servers ])

let private columns (app: Fixtures.TestApp) =
    task {
        let! scope = scalar app "SELECT fediverse_scope FROM connectors WHERE slug='bluesky'" []
        let! servers = scalar app "SELECT fediverse_servers FROM connectors WHERE slug='bluesky'" []
        return string scope, string servers
    }

let private operatorAppAt (origin: string) (development: bool) =
    task {
        let settings =
            fakeSettings
            @ [ if development then "UMBRAL:INSECURE_ALLOW_HTTP", "true"
                if origin <> "https://umbral.test" then "UMBRAL:PUBLIC_ORIGIN", origin ]

        let! app = Fixtures.createAppWith settings ignore
        app.Client.BaseAddress <- Uri origin
        let operator = Browser(app.Client)
        do! claimOperator app operator
        return app, operator
    }

let private operatorApp () = operatorAppAt "https://umbral.test" false

let private countOf (app: Fixtures.TestApp) =
    task {
        let! count = scalar app "SELECT count(*) FROM connectors WHERE slug='bluesky'" []
        return Convert.ToInt64 count
    }

[<Fact>]
let ``not set up the pane shows the metadata address and no callback URL`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! list = operator.GetHtml "/admin/connectors"
        let tags = entryTags list
        let names = tags |> List.map fst
        let b = List.findIndex ((=) "bluesky") names
        Assert.True(b > List.findIndex ((=) "google") names)
        Assert.Contains("data-entry-muted", snd tags[b])
        Assert.DoesNotContain("data-attention", snd tags[b])
        let! html = operator.GetHtml path
        Assert.Contains("data-connector=\"bluesky\"", html)
        Assert.Contains("data-connector-kind=\"bluesky\"", html)
        Assert.DoesNotContain("data-connector-state=\"enabled\"", html)
        Assert.Contains((texts ()).NotConfigured, html)
        Assert.Matches("<input[^>]*data-client-metadata-url[^>]*/connect/bluesky/client-metadata.json\"|<input[^>]*/connect/bluesky/client-metadata.json\"[^>]*data-client-metadata-url", html)
        Assert.Contains("value=\"https://umbral.test/connect/bluesky/client-metadata.json\"", html)
        Assert.DoesNotContain("/connect/bluesky/callback", html)
        Assert.DoesNotContain("data-callback-url", html)
        Assert.DoesNotContain("data-dev-loopback-note", html)
        Assert.DoesNotContain("data-origin-blocked", html)
        Assert.Contains("data-no-email-note", html)
        Assert.Contains("data-instance-scope", html)
        Assert.Matches("value=\"any\"[^>]*checked|checked[^>]*value=\"any\"", html)
        Assert.Contains("data-instance-list", html)
        Assert.Contains("Enable Bluesky", html)
        let decoded = WebUtility.HtmlDecode html
        let t = texts ()
        Assert.Contains(t.BlueskyCardLead, decoded)
        Assert.Contains(t.BlueskyMetadataLabel, decoded)
        Assert.Contains(t.BlueskyMetadataHint, decoded)
        Assert.Contains(t.BlueskyNoEmailNote, decoded)
        Assert.Contains(t.BlueskyServersHint, decoded)
    }

[<Fact>]
let ``the list order is Fediverse, Bluesky, Passkey`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! list = operator.GetHtml "/admin/connectors"
        let names = entryTags list |> List.map fst
        let f = List.findIndex ((=) "fediverse") names
        let b = List.findIndex ((=) "bluesky") names
        let p = List.findIndex ((=) "passkey") names
        Assert.True(f < b && b < p)
        Assert.Equal(f + 1, b)
        Assert.Equal(b + 1, p)
    }

[<Fact>]
let ``enabling creates the row enabled and shared with any server`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! r = save operator "true" "any" ""
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode)
        Assert.Contains("msg=bluesky-connector-saved", location r)
        let! stored = ConnectorStore.findBySlug app.DbConfig "bluesky"
        Assert.True(stored.IsSome && stored.Value.IsEnabled && stored.Value.IsShared)
        Assert.Equal(Bluesky, stored.Value.Kind)
        Assert.Equal("Bluesky", stored.Value.DisplayName)
        Assert.False stored.Value.FediverseAccess.OnlyListed
        let! ev = scalar app "SELECT count(*) FROM security_events WHERE event_type='connector_created' AND connector_slug='bluesky'" []
        Assert.Equal("1", string ev)
        let! html = operator.GetHtml path
        Assert.Contains("data-connector-state=\"enabled\"", html)
        Assert.Contains((texts ()).SaveConnector, html)
        Assert.DoesNotContain("data-origin-blocked", html)

        // Saving again updates it and records the update.
        let! again = save operator "true" "listed" "bsky.social"
        Assert.Contains("bluesky-connector-saved", location again)
        let! updated = scalar app "SELECT count(*) FROM security_events WHERE event_type='connector_updated' AND connector_slug='bluesky'" []
        Assert.Equal("1", string updated)
    }

[<Fact>]
let ``a listed scope is shown back normalised`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! r = save operator "true" "listed" "BSKY.social, pds.example.org"
        Assert.Contains("bluesky-connector-saved", location r)
        let! scope, servers = columns app
        Assert.Equal("listed", scope)
        Assert.Equal("bsky.social pds.example.org", servers)
        let! html = operator.GetHtml path
        Assert.Contains("bsky.social, pds.example.org", html)
    }

[<Fact>]
let ``a wildcard or a non-address entry is 422 with the entry named and the typed value kept`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! wild = save operator "true" "listed" "*.example.social"
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wild.StatusCode)
        let body = WebUtility.HtmlDecode(wild.Content.ReadAsStringAsync().Result)
        Assert.Contains((texts ()).FediverseServerWildcard "*.example.social", body)
        Assert.Contains("value=\"*.example.social\"", body)
        let! bad = save operator "true" "listed" "not a server/path"
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode)
        let body2 = WebUtility.HtmlDecode(bad.Content.ReadAsStringAsync().Result)
        Assert.Contains("is not a server address.", body2)
        Assert.Contains("value=\"not a server/path\"", body2)
        let! count = countOf app
        Assert.Equal(0L, count)
    }

[<Fact>]
let ``a listed scope with an empty list warns`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! r = save operator "true" "listed" ""
        Assert.Contains("bluesky-connector-saved", location r)
        let! html = operator.GetHtml path
        Assert.Contains("data-servers-empty", html)
        Assert.Contains((texts ()).BlueskyServersEmptyNote, WebUtility.HtmlDecode html)
    }

[<Fact>]
let ``an unknown scope word redirects as invalid`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! r = save operator "true" "everyone" ""
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode)
        Assert.Contains("bluesky-connector-invalid", location r)
        let! count = countOf app
        Assert.Equal(0L, count)
    }

[<Fact>]
let ``no antiforgery token changes nothing`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        let! r = operator.PostWithoutToken(path, [ "shared", "true"; "scope", "any"; "servers", "" ])
        Assert.NotEqual(HttpStatusCode.Redirect, r.StatusCode)
        let! count = countOf app
        Assert.Equal(0L, count)

        let! _ = save operator "true" "any" ""
        let! t = operator.PostWithoutToken(path + "/toggle", [])
        Assert.NotEqual(HttpStatusCode.Redirect, t.StatusCode)
        let! stored = ConnectorStore.findBySlug app.DbConfig "bluesky"
        Assert.True stored.Value.IsEnabled
    }

[<Fact>]
let ``toggle and delete use the generic routes`` () =
    task {
        let! app, operator = operatorApp ()
        use _ = app
        do! seedFakeConnector app
        let! _ = save operator "true" "any" ""
        let! off = operator.PostForm(path, path + "/toggle", [])
        Assert.Equal(HttpStatusCode.Redirect, off.StatusCode)
        let! html = operator.GetHtml path
        Assert.Contains("data-connector-state=\"disabled\"", html)
        let! on = operator.PostForm(path, path + "/toggle", [])
        Assert.Contains("msg=connector-enabled", location on)
        let! d = operator.PostForm(path, path + "/delete", [])
        Assert.Equal(HttpStatusCode.Redirect, d.StatusCode)
        let! stored = ConnectorStore.findBySlug app.DbConfig "bluesky"
        Assert.True stored.IsNone
    }

// ---------------------------------------------------------------------------
// An address Bluesky cannot work at
// ---------------------------------------------------------------------------

let private blockedOrigins =
    [ "http://localhost:5000", true
      "https://umbral.test:8443", false ]

[<Fact>]
let ``an unusable origin shows the alert and no form`` () =
    task {
        for origin, development in blockedOrigins do
            let! app, operator = operatorAppAt origin development
            use _ = app
            let! html = operator.GetHtml path
            let decoded = WebUtility.HtmlDecode html
            Assert.Contains("data-origin-blocked", html)
            Assert.Contains((texts ()).BlueskyOriginBlocked origin, decoded)
            Assert.Contains((texts ()).BlueskyCardLead, decoded)
            Assert.DoesNotContain("action=\"/admin/connectors/bluesky\"", html)
            Assert.DoesNotContain("data-instance-scope", html)
            Assert.DoesNotContain("data-connector-actions", html)
            Assert.DoesNotContain("data-client-metadata-url", html)
            let! list = operator.GetHtml "/admin/connectors"
            let tag = entryTags list |> List.find (fun (n, _) -> n = "bluesky") |> snd
            Assert.DoesNotContain("data-attention", tag)
            Assert.Contains("data-entry-muted", tag)
    }

[<Fact>]
let ``a crafted save on an unusable origin is refused and stores nothing`` () =
    task {
        for origin, development in blockedOrigins do
            let! app, operator = operatorAppAt origin development
            use _ = app
            let! r = save operator "true" "any" ""
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode)
            Assert.Contains("data-origin-blocked", r.Content.ReadAsStringAsync().Result)
            let! count = countOf app
            Assert.Equal(0L, count)
            let! ev = scalar app "SELECT count(*) FROM security_events WHERE connector_slug='bluesky'" []
            Assert.Equal("0", string ev)
    }

[<Fact>]
let ``a crafted enable of an existing connector on an unusable origin is refused with the notice`` () =
    task {
        for origin, development in blockedOrigins do
            let! app, operator = operatorAppAt origin development
            use _ = app
            do! connector app false
            let! r = operator.PostForm(tokenPage, path + "/toggle", [])
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode)
            Assert.Contains("msg=connector-enable-bluesky-origin", location r)
            let! stored = ConnectorStore.findBySlug app.DbConfig "bluesky"
            Assert.False stored.Value.IsEnabled
            let! html = operator.GetHtml(location r)
            Assert.Contains("data-notice=\"connector-enable-bluesky-origin\"", html)
            Assert.Contains((texts ()).Notice "connector-enable-bluesky-origin", WebUtility.HtmlDecode html)
            let! ev = scalar app "SELECT count(*) FROM security_events WHERE event_type='connector_enabled'" []
            Assert.Equal("0", string ev)
    }

[<Fact>]
let ``an existing connector on an unusable origin still saves its list and stays disabled`` () =
    task {
        for origin, development in blockedOrigins do
            let! app, operator = operatorAppAt origin development
            use _ = app
            do! connector app false
            let! r = save operator "true" "listed" "bsky.social"
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode)
            Assert.Contains("msg=bluesky-connector-saved", location r)
            let! scope, servers = columns app
            Assert.Equal("listed", scope)
            Assert.Equal("bsky.social", servers)
            let! stored = ConnectorStore.findBySlug app.DbConfig "bluesky"
            Assert.False stored.Value.IsEnabled
    }

[<Fact>]
let ``an enabled connector on an unusable origin shows the marker and the alert above its sections`` () =
    task {
        for origin, development in blockedOrigins do
            let! app, operator = operatorAppAt origin development
            use _ = app
            do! connector app true
            let! list = operator.GetHtml "/admin/connectors"
            let tag = entryTags list |> List.find (fun (n, _) -> n = "bluesky") |> snd
            Assert.Contains("data-attention", tag)
            let! html = operator.GetHtml path
            Assert.Contains("data-origin-blocked", html)
            Assert.Contains("data-instance-scope", html)
            Assert.Contains("data-connector-actions", html)
            Assert.True(html.IndexOf "data-origin-blocked" < html.IndexOf "data-instance-scope")

            // Switching off asks nothing.
            let! off = operator.PostForm(path, path + "/toggle", [ ])
            Assert.Contains("msg=", location off)
            let! stored = ConnectorStore.findBySlug app.DbConfig "bluesky"
            Assert.True(stored.IsSome)
    }

[<Fact>]
let ``development on a loopback origin shows the info note in place of the address`` () =
    task {
        let! app, operator = operatorAppAt "http://127.0.0.1:5000" true
        use _ = app
        let! html = operator.GetHtml path
        Assert.Contains("data-dev-loopback-note", html)
        Assert.Contains((texts ()).BlueskyDevNote, WebUtility.HtmlDecode html)
        Assert.DoesNotContain("data-client-metadata-url", html)
        Assert.DoesNotContain("data-origin-blocked", html)
        Assert.Contains("data-instance-scope", html)
        let! r = save operator "true" "any" ""
        Assert.Contains("bluesky-connector-saved", location r)
    }

// ---------------------------------------------------------------------------
// The picker
// ---------------------------------------------------------------------------

[<Fact>]
let ``the picker offers Bluesky as an ordinary button with the link icon and the last used badge`` () =
    task {
        let! fake, app, prepared = setup ()
        use _ = fake
        use _ = app

        do!
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
                  AppleKey = None
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None

        let picker (browser: Journeys.Browser) =
            task {
                let! toPicker = browser.Get(authorizeUrl prepared.ClientId "openid profile")
                let! page = browser.Get(string toPicker.Headers.Location)
                return! page.Content.ReadAsStringAsync()
            }

        let buttonOf (html: string) (slug: string) =
            Regex.Match(html, $"<a\\b[^>]*data-connector=\"{slug}\"[^>]*>.*?</a>", RegexOptions.Singleline).Value

        let! first = picker (Journeys.Browser(app.Client))
        let bluesky = buttonOf first "bluesky"
        Assert.NotEmpty(buttonOf first "fediverse")
        Assert.NotEmpty bluesky
        Assert.Contains("href=\"/connect/bluesky/start?returnUrl=", bluesky)
        Assert.Contains("Sign in with Bluesky", bluesky)
        Assert.Contains(Umbral.Views.Theme.Link.Path, bluesky)
        Assert.DoesNotContain("data-last-used", bluesky)

        // After a Bluesky sign-in the browser remembers it, and the badge says so.
        let! completed = signIn fake app prepared "ana.bsky.social"
        let person = completed.Browser
        person.Cookies.Remove Umbral.Infrastructure.Sessions.CookieName |> ignore
        let! again = picker person
        Assert.Contains("data-last-used", buttonOf again "bluesky")
        Assert.DoesNotContain("data-last-used", buttonOf again "fediverse")
    }
