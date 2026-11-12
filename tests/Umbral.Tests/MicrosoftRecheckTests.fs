/// The tenant re-check of a Microsoft session, through the real pipeline
/// (specs/016-microsoft-connector FR-011, research R3 last paragraph).
///
/// The operator can narrow the audience after people have signed in. Like a
/// Fediverse or Bluesky server list, the narrowing reaches the people already
/// signed in: at their next authorization the session ends, and a refresh token
/// is refused with `invalid_grant`. Widening the audience again lets the same
/// work account in as the same user.
module Umbral.Tests.MicrosoftRecheckTests

open System
open System.Net
open System.Text.RegularExpressions
open System.Threading.Tasks
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys

let private scope = "openid email profile offline_access"

type private World =
    { App: Fixtures.TestApp
      Fake: FakeMicrosoft.FakeMicrosoft
      ClientId: string
      ClientSecret: string }

let private start () =
    task {
        // Everyone signs in under the widest audience; each test narrows after.
        let fake = FakeMicrosoft.create PersonalAndAnyOrganization
        let! app = Fixtures.createAppWithProviderHandler fake.Handler fakeSettings ignore
        FakeMicrosoft.attach app fake
        let! prepared = prepare app (Browser app.Client)
        do! FakeMicrosoft.seedConnector app PersonalAndAnyOrganization

        return
            { App = app
              Fake = fake
              ClientId = prepared.ClientId
              ClientSecret = prepared.ClientSecret }
    }

let private location (response: Net.Http.HttpResponseMessage) = response.Headers.Location |> string

/// One whole sign-in through Microsoft as the person of `tid`; returns the
/// browser (with its session) and the tokens the app got.
let private signInAs (world: World) (tid: string) =
    task {
        world.Fake.Asserts(tid, FakeMicrosoft.Oid, email = FakeMicrosoft.Email)
        let browser = Browser world.App.Client
        let! toPicker = browser.Get(authorizeUrl world.ClientId scope)
        let! toMicrosoft = browser.Get((location toPicker).Replace("/signin", "/connect/microsoft/start"))
        let authorization = world.Fake.Approve(Uri(location toMicrosoft))

        let! back =
            browser.Get
                $"/connect/microsoft/callback?code={Uri.EscapeDataString authorization.Code}&state={Uri.EscapeDataString authorization.State}"

        let! issued = browser.Get(location back)
        let code = Regex.Match(location issued, "code=([^&]+)").Groups[1].Value |> Uri.UnescapeDataString
        let! exchanged = exchangeCode world.App.Client world.ClientId world.ClientSecret code
        let! tokens = json exchanged
        return browser, strOf tokens "refresh_token"
    }

let private narrowTo (world: World) (audience: MicrosoftAudience) =
    task {
        let! saved =
            ConnectorStore.saveMicrosoft
                world.App.DbConfig
                world.App.Clock
                "microsoft"
                "Microsoft"
                FakeMicrosoft.ClientId
                true
                []
                None
                audience

        Assert.True saved
    }

let private failureDetails (world: World) =
    task {
        use! connection = Database.openConnection world.App.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT ifnull(detail, '') FROM security_events WHERE event_type = 'sign_in_failed' ORDER BY id"
        use! reader = command.ExecuteReaderAsync()
        let rows = ResizeArray<string>()

        while reader.Read() do
            rows.Add(reader.GetString 0)

        return List.ofSeq rows
    }

let private userIds (world: World) =
    Flows.scalar
        world.App
        "SELECT coalesce(group_concat(user_id, ','), '') FROM linked_accounts WHERE provider_kind = 'microsoft'"
        []

[<Fact>]
let ``a work account's session ends at authorize once the audience is narrowed to personal accounts`` () =
    task {
        let! world = start ()
        use _ = world.App
        use _ = world.Fake

        let! browser, _ = signInAs world FakeMicrosoft.WorkTenantA

        // Still admitted while the audience admits it.
        let! before = browser.Get(authorizeUrl world.ClientId "openid")
        Assert.Contains("code=", location before)

        do! narrowTo world PersonalAccounts

        let! response = browser.Get(authorizeUrl world.ClientId "openid")
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.StartsWith("/signin?returnUrl=", location response)

        // The session is gone: even with the audience widened again, the same
        // browser is sent to sign in rather than served a code.
        do! narrowTo world PersonalAndAnyOrganization
        let! again = browser.Get(authorizeUrl world.ClientId "openid")
        Assert.StartsWith("/signin?returnUrl=", location again)

        let! details = failureDetails world
        Assert.Equal($"microsoft:tenant_not_allowed;tenant={FakeMicrosoft.WorkTenantA}", Assert.Single details)
    }

[<Fact>]
let ``a work account's refresh token is invalid_grant once the audience is narrowed to personal accounts`` () =
    task {
        let! world = start ()
        use _ = world.App
        use _ = world.Fake

        let! _, refreshToken = signInAs world FakeMicrosoft.WorkTenantA
        do! narrowTo world PersonalAccounts

        let! response = refresh world.App.Client world.ClientId world.ClientSecret refreshToken
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        let! body = json response
        Assert.Equal("invalid_grant", strOf body "error")

        let! details = failureDetails world
        Assert.Equal($"microsoft:tenant_not_allowed;tenant={FakeMicrosoft.WorkTenantA}", Assert.Single details)
    }

[<Fact>]
let ``a personal account's session and refresh keep working under the same narrowing`` () =
    task {
        let! world = start ()
        use _ = world.App
        use _ = world.Fake

        let! browser, refreshToken = signInAs world FakeMicrosoft.PersonalTenant
        do! narrowTo world PersonalAccounts

        let! response = browser.Get(authorizeUrl world.ClientId "openid")
        Assert.Contains("code=", location response)

        let! refreshed = refresh world.App.Client world.ClientId world.ClientSecret refreshToken
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode)

        let! details = failureDetails world
        Assert.Empty details
    }

[<Fact>]
let ``widening the audience again admits the same work account as the same user`` () =
    task {
        let! world = start ()
        use _ = world.App
        use _ = world.Fake

        let! browser, _ = signInAs world FakeMicrosoft.WorkTenantA
        let! userBefore = userIds world

        do! narrowTo world PersonalAccounts
        let! refused = browser.Get(authorizeUrl world.ClientId "openid")
        Assert.StartsWith("/signin?returnUrl=", location refused)

        do! narrowTo world PersonalAndAnyOrganization

        let! _, refreshToken = signInAs world FakeMicrosoft.WorkTenantA
        let! userAfter = userIds world
        Assert.Equal(userBefore, userAfter)
        Assert.False(userAfter.Contains ",")

        let! users = Flows.scalar world.App "SELECT count(*) FROM users" []
        Assert.Equal("1", users)

        let! refreshed = refresh world.App.Client world.ClientId world.ClientSecret refreshToken
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode)
    }
