/// The operator's side of the passkey connector (spec 008, Story 6). Only the
/// part that already exists in the foundation is here: a connector of the
/// passkey kind goes through the generic enable, disable and delete paths that
/// every connector uses. The card and the Users rows are tested with their
/// screens.
module Umbral.Tests.PasskeyAdminTests

open System.Net
open System.Text.RegularExpressions
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Views
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.Flows
open Umbral.Tests.Infrastructure.PasskeyFlows

let private seedPasskey (app: Fixtures.TestApp) =
    ConnectorStore.save
        app.DbConfig
        app.Clock
        { Slug = "passkey"
          Kind = Passkey
          DisplayName = "Passkey"
          ClientId = ""
          ClientSecretRef = ""
          ClientSecretHint = ""
          IssuerUrl = None
          AllowedEmailDomains = []
          PasskeyEnrolment = InvitationsOnly
          FediverseAccess = FediverseAccess.Any
          IsEnabled = true
          IsShared = true
          CreatedUtc = app.Clock.GetUtcNow()
          UpdatedUtc = app.Clock.GetUtcNow() }
        None

[<Fact>]
let ``deleting a passkey connector, which has no secret, deletes the row and nothing else`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser(app.Client)
        do! claimOperator app operator
        // A second enabled connector, so this is not the "last door" case.
        do! seedFakeConnector app
        do! seedPasskey app

        let! response = operator.PostForm("/admin/connectors", "/admin/connectors/passkey/delete", [])

        // The empty reference means "no secret to remove", not a store failure.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("connector-deleted", location response)
        Assert.DoesNotContain("left-behind", location response)

        let! stored = ConnectorStore.findBySlug app.DbConfig "passkey"
        Assert.True stored.IsNone

        let! failures = scalar app "SELECT count(*) FROM security_events WHERE event_type = 'secret_store_failed'" []
        Assert.Equal("0", failures)

        let! deleted = scalar app "SELECT count(*) FROM security_events WHERE event_type = 'connector_deleted' AND connector_slug = 'passkey'" []
        Assert.Equal("1", deleted)
    }

[<Fact>]
let ``the passkey pane enables the connector, invites someone once and revokes the invitation`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser(app.Client)
        do! claimOperator app operator

        let! html = operator.GetHtml "/admin/connectors/passkey"
        Assert.Contains("data-connector-kind=\"passkey\"", html)
        Assert.DoesNotContain("data-invitation-link", html)

        let! saved = operator.PostForm("/admin/connectors", "/admin/connectors/passkey", [ "shared", "true"; "enrolment", "invitations" ])
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode)
        Assert.Contains("passkey-connector-saved", location saved)

        let! stored = ConnectorStore.findBySlug app.DbConfig "passkey"
        Assert.True(stored.IsSome && stored.Value.IsEnabled && stored.Value.IsShared)

        let! created = scalar app "SELECT count(*) FROM security_events WHERE event_type = 'connector_created' AND detail = 'enrolment=invitations'" []
        Assert.Equal("1", created)

        let! invited = operator.PostForm("/admin/connectors", "/admin/connectors/passkey/invitations", [ "note", "Ana" ])
        Assert.Equal(HttpStatusCode.Redirect, invited.StatusCode)
        Assert.Contains("passkey-invitation-created", location invited)

        let! shown = operator.GetHtml "/admin/connectors/passkey"
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(shown, "data-invitation-link").Count)
        let m = System.Text.RegularExpressions.Regex.Match(shown, "data-invitation=\"([^\"]+)\"")
        Assert.True m.Success

        let! again = operator.GetHtml "/admin/connectors/passkey"
        Assert.DoesNotContain("data-invitation-link", again)

        let! revoked = operator.PostForm("/admin/connectors", $"/admin/connectors/passkey/invitations/{m.Groups[1].Value}/revoke", [])
        Assert.Equal(HttpStatusCode.Redirect, revoked.StatusCode)
        Assert.Contains("passkey-invitation-revoked", location revoked)

        let! after = operator.GetHtml "/admin/connectors/passkey"
        Assert.DoesNotContain("data-invitation=\"", after)
    }

// ---------------------------------------------------------------------------
// Story 6: the operator's view of passkeys, and what disabling or deleting does
// ---------------------------------------------------------------------------

type private W =
    { App: Fixtures.TestApp
      Operator: Browser
      ClientId: string
      Secret: string
      AnaId: string
      Ana: SoftwareAuthenticator.SoftwareAuthenticator
      SamId: string
      Sam: SoftwareAuthenticator.SoftwareAuthenticator }

let private authn () =
    SoftwareAuthenticator.SoftwareAuthenticator("umbral.test", Fixtures.TestOrigin)

let private inviteFromRow (operator: Browser) (id: string) =
    task {
        let! response = operator.PostForm("/admin/users", $"/admin/users/{id}/invite", [])
        let! html = operator.GetHtml(location response)
        let m = Regex.Match(html, "<input[^>]*data-invitation-link[^>]*>")
        Assert.True m.Success
        let v = Regex.Match(m.Value, "value=\"([^\"]+)\"")
        return secretOf (string (WebUtility.HtmlDecode v.Groups[1].Value))
    }

let private world (app: Fixtures.TestApp) =
    task {
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! seedFakeConnector app
        do! enablePasskeyConnector operator "invitations"
        let! clientId, secret = registerApp operator "Grafana"

        let! link = inviteNewPerson operator "Ana"
        let ana = authn ()
        let! r, _ = registerThroughInvitation (Browser(app.Client)) ana (secretOf link) (Some "Ana") "Phone"
        Assert.Equal(HttpStatusCode.OK, r.StatusCode)
        let! anaId = scalar app "SELECT id FROM users WHERE display_name = 'Ana'" []

        let! viaFake = signInViaFake (Browser(app.Client)) clientId "openid" "fake-user-1" "sam@example.com"
        Assert.True(Result.isOk viaFake)
        let! samId = userIdOf app "fake-user-1"
        let! secretSam = inviteFromRow operator samId
        let sam = authn ()
        let! r2, _ = registerThroughInvitation (Browser(app.Client)) sam secretSam None "Laptop"
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode)

        return
            { App = app
              Operator = operator
              ClientId = clientId
              Secret = secret
              AnaId = anaId
              Ana = ana
              SamId = samId
              Sam = sam }
    }

let private articleOf (html: string) (id: string) =
    Regex.Match(html, "<article[^>]*data-user=\"" + id + "\".*?</article>", RegexOptions.Singleline).Value

let private passkeyOf (app: Fixtures.TestApp) (userId: string) =
    scalar app "SELECT id FROM passkeys WHERE user_id = @u" [ "@u", userId ]

let private count (app: Fixtures.TestApp) (table: string) = scalar app $"SELECT count(*) FROM {table}" []

[<Fact>]
let ``the Users rows show the passkey pill and list`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app
        let! html = w.Operator.GetHtml "/admin/users"
        let ana = articleOf html w.AnaId
        Assert.Contains("data-linked-provider=\"passkey\"", ana)
        Assert.Contains("data-user-passkeys", ana)
        Assert.Equal(1, Regex.Matches(ana, "data-passkey=").Count)
        Assert.Contains("Phone", ana)
        Assert.Contains("Added", ana)
        let sam = articleOf html w.SamId
        Assert.Contains("data-linked-provider=\"passkey\"", sam)
        Assert.Contains("data-linked-provider=\"fake\"", sam)
    }

let private inviteButtonOf (article: string) =
    (string (WebUtility.HtmlDecode(Regex.Match(article, "<button[^>]*data-user-invite[^>]*>(.*?)</button>", RegexOptions.Singleline).Groups[1].Value))).Trim()

let private linkLabelOf (html: string) (id: string) =
    (string (WebUtility.HtmlDecode(Regex.Match(articleOf html id, "<label for=\"invite-" + id + "\"[^>]*>(.*?)</label>", RegexOptions.Singleline).Groups[1].Value))).Trim()

[<Fact>]
let ``on a row with a passkey the invitation is a recovery link, on a provider user's row an invitation`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app
        let! viaFake = signInViaFake (Browser(app.Client)) w.ClientId "openid" "fake-user-2" "lea@example.com"
        Assert.True(Result.isOk viaFake)
        let! leaId = userIdOf app "fake-user-2"

        let! html = w.Operator.GetHtml "/admin/users"
        let ana = articleOf html w.AnaId
        Assert.Equal(Texts.en.UserRecoveryLinkButton, inviteButtonOf ana)
        Assert.Contains(Texts.en.UserRecoveryLinkHint, string (WebUtility.HtmlDecode ana))
        Assert.Contains($"aria-describedby=\"invite-{w.AnaId}-hint\"", ana)
        Assert.Contains($"id=\"invite-{w.AnaId}-hint\"", ana)

        let lea = articleOf html leaId
        Assert.Equal(Texts.en.UserInvitePasskeyButton, inviteButtonOf lea)
        Assert.DoesNotContain("data-user-invite-hint", lea)
        Assert.DoesNotContain("aria-describedby", Regex.Match(lea, "<button[^>]*data-user-invite[^>]*>").Value)

        // Same route and the same single reveal for both; only the label of the
        // revealed link follows the row.
        let! r = w.Operator.PostForm("/admin/users", $"/admin/users/{w.AnaId}/invite", [])
        let! shownAna = w.Operator.GetHtml(location r)
        Assert.Equal(Texts.en.UserRecoveryLinkLabel, linkLabelOf shownAna w.AnaId)

        let! r2 = w.Operator.PostForm("/admin/users", $"/admin/users/{leaId}/invite", [])
        let! shownLea = w.Operator.GetHtml(location r2)
        Assert.Equal(Texts.en.UserInvitationLinkLabel, linkLabelOf shownLea leaId)
        Assert.Equal(1, Regex.Matches(shownLea, "data-invitation-link").Count)
    }

[<Fact>]
let ``the operator removes a passkey, even the last one, and the fake link stays`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app
        let! pid = passkeyOf app w.SamId
        let! r = w.Operator.PostForm("/admin/users", $"/admin/users/{w.SamId}/passkeys/{pid}/remove", [])
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode)
        Assert.Contains("passkey-removed", location r)
        let! gone = scalar app "SELECT count(*) FROM passkeys WHERE id = @p" [ "@p", pid ]
        Assert.Equal("0", gone)
        let! links = scalar app "SELECT count(*) FROM linked_accounts WHERE user_id = @u" [ "@u", w.SamId ]
        Assert.Equal("1", links)
        let! html = w.Operator.GetHtml "/admin/users"
        Assert.DoesNotContain($"data-passkey=\"{pid}\"", html)
        Assert.DoesNotContain("data-user-passkeys", articleOf html w.SamId)
        let! evs = events app "passkey_removed"
        Assert.Equal<(string option * string option * string option) list>([ Some w.SamId, Some "passkey", Some "by=operator" ], evs)
        let! actor = scalar app "SELECT actor FROM security_events WHERE event_type = 'passkey_removed'" []
        Assert.Equal("operator", actor)

        let! anaPid = passkeyOf app w.AnaId
        let! r2 = w.Operator.PostForm("/admin/users", $"/admin/users/{w.AnaId}/passkeys/{anaPid}/remove", [])
        Assert.Contains("passkey-removed", location r2)
        let! n = count app "passkeys"
        Assert.Equal("0", n)
    }

[<Fact>]
let ``removing another user's or an unknown passkey changes nothing`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app
        let! anaPid = passkeyOf app w.AnaId
        let! r = w.Operator.PostForm("/admin/users", $"/admin/users/{w.SamId}/passkeys/{anaPid}/remove", [])
        Assert.Contains("passkey-not-found", location r)
        let! r2 = w.Operator.PostForm("/admin/users", $"/admin/users/{w.SamId}/passkeys/nope/remove", [])
        Assert.Contains("passkey-not-found", location r2)
        let! n = count app "passkeys"
        Assert.Equal("2", n)
    }

[<Fact>]
let ``the toggle fragment draws the passkey list`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app
        let! token = w.Operator.TokenFrom "/admin/users"
        let message = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"/admin/users/{w.AnaId}/toggle")
        message.Headers.Add("HX-Request", "true")
        message.Headers.Add("RequestVerificationToken", token)
        message.Content <- new System.Net.Http.FormUrlEncodedContent([ System.Collections.Generic.KeyValuePair("__RequestVerificationToken", token) ])
        let! response = w.Operator.Send message
        let! html = response.Content.ReadAsStringAsync()
        Assert.Contains("data-user-passkeys", html)
    }

[<Fact>]
let ``revoking an invitation for a user, and one that is used`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app
        let! r = w.Operator.PostForm("/admin/users", $"/admin/users/{w.SamId}/invite", [])
        Assert.Contains("passkey-user-invited", location r)
        let! link = w.Operator.GetHtml(location r)
        let secret =
            secretOf (string (WebUtility.HtmlDecode (Regex.Match(Regex.Match(link, "<input[^>]*data-invitation-link[^>]*>").Value, "value=\"([^\"]+)\"").Groups[1].Value)))
        let! card = w.Operator.GetHtml "/admin/connectors/passkey"
        let id = Regex.Match(card, "data-invitation=\"([^\"]+)\"").Groups[1].Value
        Assert.True(id <> "")

        let! revoked = w.Operator.PostForm("/admin/connectors", $"/admin/connectors/passkey/invitations/{id}/revoke", [])
        Assert.Contains("passkey-invitation-revoked", location revoked)
        let! evs = events app "passkey_invitation_revoked"
        Assert.Equal(Some w.SamId, (let u, _, _ = List.last evs in u))
        let! page = Browser(app.Client).GetHtml $"/invite/{secret}"
        Assert.Contains("invitation-unusable", page)
        let! after = w.Operator.GetHtml "/admin/connectors/passkey"
        Assert.DoesNotContain($"data-invitation=\"{id}\"", after)

        let! usedId = scalar app "SELECT id FROM passkey_invitations WHERE used_utc IS NOT NULL LIMIT 1" []
        let! again = w.Operator.PostForm("/admin/connectors", $"/admin/connectors/passkey/invitations/{usedId}/revoke", [])
        Assert.Contains("passkey-invitation-not-open", location again)
    }

[<Fact>]
let ``disabling the connector closes the sign-in door but not the account page removal`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app
        let ana = Browser(app.Client)
        let! returnUrl, _, token = startPasskeySignIn ana w.ClientId "openid"
        let! ok, _ = signInWithPasskey ana w.Ana (List.head w.Ana.Credentials).Id returnUrl token
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode)

        let! off = w.Operator.PostForm("/admin/connectors", "/admin/connectors/passkey/toggle", [])
        Assert.Contains("connector-disabled", location off)

        let visitor = Browser(app.Client)
        let! signin = visitor.GetHtml "/signin"
        Assert.DoesNotContain("data-connector=\"passkey\"", signin)
        let! start = visitor.Get "/connect/passkey/start?returnUrl=%2F"
        Assert.Equal(HttpStatusCode.NotFound, start.StatusCode)
        let! t = visitor.TokenFrom "/signin"
        let! refused, _ = postJson visitor "/passkey/assertion/options" t {| returnUrl = "/" |}
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode)
        let! failed = events app "sign_in_failed"
        let _, _, detail = List.last failed
        Assert.Contains("reason=connector_disabled", detail.Value)

        // Sam keeps a provider link, so he may remove his only passkey.
        let sam = Browser(app.Client)
        let! viaFake = signInViaFake sam w.ClientId "openid" "fake-user-1" "sam@example.com"
        Assert.True(Result.isOk viaFake)
        let! page = sam.GetHtml "/account/passkeys"
        Assert.Contains("data-passkeys-not-enabled", page)
        let! pid = passkeyOf app w.SamId
        let! removed = sam.PostForm("/account/passkeys", $"/account/passkeys/{pid}/remove", [])
        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode)
        let! n = scalar app "SELECT count(*) FROM passkeys WHERE id = @p" [ "@p", pid ]
        Assert.Equal("0", n)
    }

[<Fact>]
let ``deleting the connector drops invitations, keeps passkeys, and enabling again restores sign-in`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app
        let! oldLink = inviteNewPerson w.Operator "Later"
        let! sub1 = scalar app "SELECT id FROM users WHERE display_name = 'Ana'" []

        let! d = w.Operator.PostForm("/admin/connectors", "/admin/connectors/passkey/delete", [])
        let! d =
            if (location d).Contains "strands" then
                w.Operator.PostForm("/admin/connectors", "/admin/connectors/passkey/delete?confirm=yes", [])
            else
                task { return d }
        Assert.Contains("connector-deleted", location d)
        let! inv = count app "passkey_invitations"
        Assert.Equal("0", inv)
        let! pk = scalar app "SELECT count(*) FROM passkeys WHERE user_id = @u" [ "@u", w.AnaId ]
        Assert.Equal("1", pk)

        do! enablePasskeyConnector w.Operator "invitations"
        let ana = Browser(app.Client)
        let! returnUrl, _, token = startPasskeySignIn ana w.ClientId "openid"
        let! r, body = signInWithPasskey ana w.Ana (List.head w.Ana.Credentials).Id returnUrl token
        Assert.Equal(HttpStatusCode.OK, r.StatusCode)
        let! code = codeFromRedirect ana (strOf body "redirect")
        let! exchanged = exchangeCode app w.ClientId w.Secret code
        let! tokens = json exchanged
        Assert.Equal(sub1, strOf (segmentOf 1 (strOf tokens "id_token")) "sub")

        let! page = Browser(app.Client).GetHtml $"/invite/{secretOf oldLink}"
        Assert.Contains("invitation-unusable", page)
        let! failed = events app "sign_in_failed"
        let _, _, detail = List.last failed
        Assert.Contains("reason=invitation_unknown", detail.Value)
    }

[<Fact>]
let ``deleting a user removes their passkeys and invitations`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app
        let! _ = w.Operator.PostForm("/admin/users", $"/admin/users/{w.AnaId}/invite", [])
        let! before = scalar app "SELECT count(*) FROM passkey_invitations WHERE user_id = @u" [ "@u", w.AnaId ]
        Assert.Equal("1", before)

        let! d = w.Operator.PostForm("/admin/users", $"/admin/users/{w.AnaId}/delete", [])
        Assert.Equal(HttpStatusCode.Redirect, d.StatusCode)
        let! p = scalar app "SELECT count(*) FROM passkeys WHERE user_id = @u" [ "@u", w.AnaId ]
        let! i = scalar app "SELECT count(*) FROM passkey_invitations WHERE user_id = @u" [ "@u", w.AnaId ]
        Assert.Equal("0", p)
        Assert.Equal("0", i)

        let ana = Browser(app.Client)
        let! returnUrl, _, token = startPasskeySignIn ana w.ClientId "openid"
        let! r, _ = signInWithPasskey ana w.Ana (List.head w.Ana.Credentials).Id returnUrl token
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode)
        let! failed = events app "sign_in_failed"
        let _, _, detail = List.last failed
        Assert.Contains("reason=user_not_found", detail.Value)
    }

[<Fact>]
let ``the passkey connector as an app's only one warns before disabling or deleting`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! w = world app
        let! saved =
            w.Operator.PostForm("/admin/apps", $"/admin/apps/{w.ClientId}/connectors", [ "connectorChoice", "chosen"; "connectors", "passkey" ])
        Assert.Contains("app-connectors-saved", location saved)

        let! t = w.Operator.PostForm("/admin/connectors", "/admin/connectors/passkey/toggle", [])
        Assert.Contains("connector-strands-apps", location t)
        Assert.Contains("slug=passkey", location t)
        let! d = w.Operator.PostForm("/admin/connectors", "/admin/connectors/passkey/delete", [])
        Assert.Contains("connector-delete-strands-apps", location d)
    }
