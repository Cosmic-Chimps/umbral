/// The operator's side of the passkey connector (spec 008, Story 6). Only the
/// part that already exists in the foundation is here: a connector of the
/// passkey kind goes through the generic enable, disable and delete paths that
/// every connector uses. The card and the Users rows are tested with their
/// screens.
module Umbral.Tests.PasskeyAdminTests

open System.Net
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

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
let ``the passkey card enables the connector, invites someone once and revokes the invitation`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser(app.Client)
        do! claimOperator app operator

        let! html = operator.GetHtml "/admin/connectors"
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

        let! shown = operator.GetHtml "/admin/connectors"
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(shown, "data-invitation-link").Count)
        let m = System.Text.RegularExpressions.Regex.Match(shown, "data-invitation=\"([^\"]+)\"")
        Assert.True m.Success

        let! again = operator.GetHtml "/admin/connectors"
        Assert.DoesNotContain("data-invitation-link", again)

        let! revoked = operator.PostForm("/admin/connectors", $"/admin/connectors/passkey/invitations/{m.Groups[1].Value}/revoke", [])
        Assert.Equal(HttpStatusCode.Redirect, revoked.StatusCode)
        Assert.Contains("passkey-invitation-revoked", location revoked)

        let! after = operator.GetHtml "/admin/connectors"
        Assert.DoesNotContain("data-invitation=\"", after)
    }
