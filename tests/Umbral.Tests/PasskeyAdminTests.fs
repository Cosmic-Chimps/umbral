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
