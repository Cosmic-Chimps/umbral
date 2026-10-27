/// Links and addresses an operator can be led to: a notice code in a URL must
/// never draw a button that changes something. The confirming forms of the
/// Connectors and Apps screens are drawn only after the POST that refused wrote
/// its single-use cookie; the one-time values (a new client secret, a passkey
/// invitation link) are not burned by another page; and nothing under /admin is
/// cached.
///
/// Asserts on `data-*` markers, form actions and notice codes, never on wording.
module Umbral.Tests.CraftedLinkTests

open System.Net
open System.Net.Http
open System.Text.RegularExpressions
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

let private seedDiscord (app: Fixtures.TestApp) =
    task {
        let! reference = Fixtures.putSecret app "discord" "discord-secret"

        do!
            ConnectorStore.save
                app.DbConfig
                app.Clock
                { Slug = "discord"
                  Kind = Discord
                  DisplayName = "Discord"
                  ClientId = "1234"
                  ClientSecretRef = reference.Encoded
                  ClientSecretHint = Secrets.hint "discord-secret"
                  IssuerUrl = None
                  AllowedEmailDomains = []
                  PasskeyEnrolment = InvitationsOnly
                  FediverseAccess = FediverseAccess.Any
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None
    }

/// Fake and Discord stored and shared, an app limited to Discord alone (so
/// disabling or deleting Discord strands it), and a signed-in operator.
let private instance () =
    task {
        let! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app
        do! seedDiscord app
        let! clientId, _ = registerApp operator "Photos"
        do! AppConnectors.setChoice app.DbConfig clientId (Only(Set.ofList [ "discord" ]))
        return app, operator, clientId
    }

/// Every `action` of every form on the page, as written.
let private actionsOf (html: string) =
    [ for m in Regex.Matches(html, "<form\\b[^>]*\\baction=\"([^\"]*)\"") -> string (System.Net.WebUtility.HtmlDecode m.Groups[1].Value) ]

/// A crafted link must draw no confirming form, no named stranded app, and no
/// form that reaches another screen.
let private assertNoConfirm (url: string) (html: string) =
    Assert.DoesNotContain("confirm=yes", html)
    Assert.DoesNotContain("data-stranded-app", html)

    for action in actionsOf html do
        Assert.True(not (action.Contains "..") , $"{url}: form action {action} holds ..")
        Assert.True(not (action.StartsWith "/admin/apps"), $"{url}: form action {action} reaches Apps")
        Assert.True(not (action.StartsWith "/admin/users"), $"{url}: form action {action} reaches Users")

/// The tag of the form with this action.
let private formTag (html: string) (action: string) =
    let m = Regex.Match(html, "<form\\b[^>]*\\baction=\"" + Regex.Escape action + "\"[^>]*>")
    if m.Success then Some m.Value else None

/// The hidden inputs of the form with this action, but for the antiforgery
/// token (which the browser helper sends itself).
let private hiddenFieldsOf (html: string) (action: string) =
    let m =
        Regex.Match(
            html,
            "<form\\b[^>]*\\baction=\"" + Regex.Escape action + "\"[^>]*>(.*?)</form>",
            RegexOptions.Singleline
        )

    Assert.True(m.Success, $"no form with action {action}")

    [ for i in Regex.Matches(m.Groups[1].Value, "<input\\b[^>]*type=\"hidden\"[^>]*>") do
          let name = Regex.Match(i.Value, "name=\"([^\"]*)\"").Groups[1].Value
          let value = Regex.Match(i.Value, "value=\"([^\"]*)\"").Groups[1].Value
          if name <> "__RequestVerificationToken" then
              name, string (System.Net.WebUtility.HtmlDecode value) ]

let private discordPane = "/admin/connectors/discord"

[<Fact>]
let ``a crafted Connectors link draws no confirming form and no form that reaches another screen`` () =
    task {
        let! app, operator, clientId = instance ()
        use _ = app

        let links =
            [ $"{discordPane}?msg=connector-delete-strands-apps&slug=../apps/{clientId}"
              $"{discordPane}?msg=connector-strands-apps&slug=../users/some-id"
              $"{discordPane}?msg=connector-strands-apps&slug=fake"
              $"{discordPane}?msg=connector-strands-apps&slug=fake&change=unshare"
              $"{discordPane}?msg=connector-delete-strands-apps&slug=nope"
              // Its own slug, but no refusal wrote the cookie.
              $"{discordPane}?msg=connector-delete-strands-apps&slug=discord"
              $"{discordPane}?msg=connector-strands-apps&slug=discord&change=unshare"
              "/admin/connectors?msg=connector-delete-strands-apps&slug=discord"
              "/admin/connectors?msg=connector-strands-apps&slug=discord" ]

        // The operator's own client holds the session and no notice cookie:
        // nothing here has been refused.
        for link in links do
            let! html = operator.GetHtml link
            assertNoConfirm link html
    }

[<Fact>]
let ``a refused delete draws the confirming form once, on the connector's own pane`` () =
    task {
        let! app, operator, _ = instance ()
        use _ = app

        let! warned = operator.PostForm("/admin/connectors", $"{discordPane}/delete", [])
        let back = location warned
        Assert.Contains("connector-delete-strands-apps", back)

        let! screen = operator.GetHtml back
        Assert.Contains("data-stranded-app", screen)
        Assert.Contains($"action=\"{discordPane}/delete?confirm=yes\"", screen)

        // A refresh no longer has the cookie: the fixed text, no button.
        let! again = operator.GetHtml back
        assertNoConfirm back again
    }

[<Fact>]
let ``a refused disable draws the confirming form once`` () =
    task {
        let! app, operator, _ = instance ()
        use _ = app

        let! warned = operator.PostForm("/admin/connectors", $"{discordPane}/toggle", [])
        let back = location warned
        Assert.Contains("connector-strands-apps", back)

        let! screen = operator.GetHtml back
        Assert.Contains("data-stranded-app", screen)
        Assert.Contains($"action=\"{discordPane}/toggle?confirm=yes\"", screen)

        let! again = operator.GetHtml back
        assertNoConfirm back again
    }

[<Fact>]
let ``a refused delete does not draw its form on another connector's pane or the list`` () =
    task {
        let! app, operator, _ = instance ()
        use _ = app

        let! warned = operator.PostForm("/admin/connectors", $"{discordPane}/delete", [])
        let back = location warned

        // The cookie is for discord's pane: another pane or the list reading
        // the same link draws nothing, and does not use the cookie up.
        let! other = operator.GetHtml(back.Replace("/admin/connectors/discord", "/admin/connectors/fake"))
        assertNoConfirm "fake pane" other
        let! list = operator.GetHtml(back.Replace("/admin/connectors/discord", "/admin/connectors"))
        assertNoConfirm "list" list

        let! own = operator.GetHtml back
        Assert.Contains($"action=\"{discordPane}/delete?confirm=yes\"", own)
    }

[<Fact>]
let ``a crafted Apps link draws no confirming form, and a refused choice does, with a confirm prompt`` () =
    task {
        let! app, operator, clientId = instance ()
        use _ = app

        let! crafted = operator.GetHtml $"/admin/apps/{clientId}?msg=app-connectors-strand&choice=chosen&connectors="
        Assert.DoesNotContain("connectors?confirm=yes", crafted)
        Assert.DoesNotContain("data-stranded-app", crafted)

        let! warned =
            operator.PostForm(
                "/admin/apps",
                $"/admin/apps/{clientId}/connectors",
                [ "connectorChoice", "chosen" ]
            )

        let back = location warned
        Assert.Contains("app-connectors-strand", back)

        let! screen = operator.GetHtml back
        let action = $"/admin/apps/{clientId}/connectors?confirm=yes"
        Assert.Contains($"action=\"{action}\"", screen)
        Assert.Contains("data-confirm", (formTag screen action).Value)

        let! again = operator.GetHtml back
        Assert.DoesNotContain("connectors?confirm=yes", again)
    }

[<Fact>]
let ``a crafted removal link draws no allowed-domains confirm, the refused post does, once`` () =
    task {
        let! app, operator, _ = instance ()
        use _ = app
        do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "acme.com" ]

        let! crafted = operator.GetHtml "/admin/connectors/domains?msg=allowed-domains-removal"
        Assert.DoesNotContain("/admin/allowed-domains?confirm=yes", crafted)
        Assert.DoesNotContain("data-opened-connector", crafted)

        let! warned = operator.PostForm("/admin/connectors", "/admin/allowed-domains", [ "allowedDomains", "" ])
        let back = location warned
        Assert.Contains("allowed-domains-removal", back)

        let! screen = operator.GetHtml back
        Assert.Contains("action=\"/admin/allowed-domains?confirm=yes\"", screen)

        let! again = operator.GetHtml back
        Assert.DoesNotContain("/admin/allowed-domains?confirm=yes", again)

        let! stored = EmailDomainRules.instanceList app.DbConfig
        Assert.Equal<string list>([ "acme.com" ], stored)
    }

/// Passkey shared with the given enrolment, and an app that has it only
/// through "all shared": unsharing strands it.
let private passkeyInstance (enrolment: string) =
    task {
        let! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator
        do! PasskeyFlows.enablePasskeyConnector operator enrolment
        let! clientId, _ = registerApp operator "Photos"
        return app, operator, clientId
    }

[<Fact>]
let ``confirming that the passkey connector stops being shared saves it, with its enrolment kept`` () =
    task {
        let! app, operator, _ = passkeyInstance "open"
        use _ = app

        let! warned =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors/passkey",
                [ "enrolment", "open" ]
            )

        Assert.Contains("connector-strands-apps", location warned)
        let! stillShared = ConnectorStore.findBySlug app.DbConfig "passkey"
        Assert.True stillShared.Value.IsShared

        let! screen = operator.GetHtml(location warned)
        let action = "/admin/connectors/passkey?confirm=yes"
        Assert.Contains($"action=\"{action}\"", screen)
        let fields = hiddenFieldsOf screen action
        Assert.Contains(("enrolment", "open"), fields)

        let! confirmed = operator.PostForm("/admin/connectors/passkey", action, fields)
        Assert.Contains("passkey-connector-saved", location confirmed)
        Assert.DoesNotContain("passkey-connector-invalid", location confirmed)

        let! stored = ConnectorStore.findBySlug app.DbConfig "passkey"
        Assert.False stored.Value.IsShared
        Assert.Equal(OpenSignUp, stored.Value.PasskeyEnrolment)
    }

[<Fact>]
let ``confirming that the fediverse connector stops being shared keeps its scope and servers`` () =
    task {
        let! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator
        let path = "/admin/connectors/fediverse"

        let! saved =
            operator.PostForm(
                path,
                path,
                [ "shared", "true"; "scope", "listed"; "servers", "mastodon.social" ]
            )

        Assert.Contains("saved", location saved)
        let! clientId, _ = registerApp operator "Photos"
        ignore clientId

        let! warned =
            operator.PostForm(path, path, [ "scope", "listed"; "servers", "mastodon.social" ])

        Assert.Contains("connector-strands-apps", location warned)

        let! screen = operator.GetHtml(location warned)
        let action = path + "?confirm=yes"
        Assert.Contains($"action=\"{action}\"", screen)
        let fields = hiddenFieldsOf screen action
        Assert.Contains(("scope", "listed"), fields)

        let! confirmed = operator.PostForm(path, action, fields)
        Assert.DoesNotContain("invalid", location confirmed)

        let! stored = ConnectorStore.findBySlug app.DbConfig "fediverse"
        Assert.False stored.Value.IsShared
        Assert.True stored.Value.FediverseAccess.OnlyListed
        Assert.Equal<string list>([ "mastodon.social" ], stored.Value.FediverseAccess.Servers)
    }

[<Fact>]
let ``another page does not burn a new client secret, its own page shows it`` () =
    task {
        let! app = Fixtures.createAppWith fakeSettings ignore
        use _ = app
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app

        let! created =
            operator.PostForm(
                "/admin/apps",
                "/admin/apps",
                [ "name", "Photos"
                  "redirectUri", RedirectUri
                  "postLogoutRedirectUri", ""
                  "appType", "web" ]
            )

        // The list URL selects the first app implicitly, which is this one.
        let! list = operator.GetHtml "/admin/apps"
        Assert.DoesNotContain("id=\"revealed-secret\"", list)

        let! own = operator.GetHtml(location created)
        Assert.Contains("id=\"revealed-secret\"", own)

        let! again = operator.GetHtml(location created)
        Assert.DoesNotContain("id=\"revealed-secret\"", again)
    }

[<Fact>]
let ``another page does not burn a passkey invitation link, the passkey pane shows it`` () =
    task {
        let! app, operator, _ = passkeyInstance "invitations"
        use _ = app

        let! invited =
            operator.PostForm("/admin/connectors", "/admin/connectors/passkey/invitations", [ "note", "Ana" ])

        Assert.Contains("passkey-invitation-created", location invited)

        let! list = operator.GetHtml "/admin/connectors"
        Assert.DoesNotContain("data-invitation-link", list)
        let! other = operator.GetHtml "/admin/connectors/domains"
        Assert.DoesNotContain("data-invitation-link", other)

        let! own = operator.GetHtml "/admin/connectors/passkey"
        Assert.Contains("data-invitation-link", own)

        let! again = operator.GetHtml "/admin/connectors/passkey"
        Assert.DoesNotContain("data-invitation-link", again)
    }

let private noStore (response: HttpResponseMessage) =
    match response.Headers.CacheControl with
    | null -> false
    | header -> header.NoStore

[<Fact>]
let ``every page under admin is no-store and a static asset is not`` () =
    task {
        let! app, operator, clientId = passkeyInstance "invitations"
        use _ = app

        for url in [ $"/admin/apps/{clientId}"; "/admin/connectors/passkey"; "/admin/apps"; "/admin/users" ] do
            let! response = operator.Get url
            Assert.Equal(HttpStatusCode.OK, response.StatusCode)
            Assert.True(noStore response, $"{url} is not no-store")

        // A refusal and a redirect too.
        let! posted = operator.PostForm("/admin/connectors", "/admin/connectors/passkey/delete", [])
        Assert.True(noStore posted, "a redirect under /admin is not no-store")

        let! asset = operator.Get "/assets/umbral.css"
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode)
        Assert.False(noStore asset, "a static asset must stay cacheable")
    }
