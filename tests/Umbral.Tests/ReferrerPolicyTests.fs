/// Every response carries `Layout.referrerPolicy`, so no page sends the
/// instance's address to another site: public pages, sign-in pages, admin pages
/// and the redirects to providers and apps. Two answers differ: the invitation
/// page keeps its stricter `no-referrer`, and OpenIddict's `form_post` answer
/// has none (see `Layout.referrerPolicy`).
module Umbral.Tests.ReferrerPolicyTests

open System
open System.Net
open System.Net.Http
open Xunit
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows
open Umbral.Views

let private referrerPolicy (response: HttpResponseMessage) =
    match response.Headers.TryGetValues "Referrer-Policy" with
    | true, values -> values |> Option.ofObj |> Option.defaultValue Seq.empty |> List.ofSeq
    | _ -> []

let private assertSiteWide (what: string) (response: HttpResponseMessage) =
    Assert.True(
        [ Layout.referrerPolicy ] = referrerPolicy response,
        $"{what} ({int response.StatusCode}): Referrer-Policy {referrerPolicy response}"
    )

[<Fact>]
let ``public pages carry the site-wide policy`` () =
    task {
        use! app = Fixtures.createApp ()

        let! setup = app.Client.GetAsync "/admin/setup"
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode)
        assertSiteWide "the setup page" setup

        let! missing = app.Client.GetAsync "/no-such-page"
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode)
        assertSiteWide "the not-found page" missing
    }

[<Fact>]
let ``sign-in pages and the redirects to a provider and back to the app carry the site-wide policy`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app
        let! clientId, _ = registerApp operator "Grafana"

        let! operatorSignIn = (Browser app.Client).Get "/admin/signin"
        Assert.Equal(HttpStatusCode.OK, operatorSignIn.StatusCode)
        assertSiteWide "the operator sign-in page" operatorSignIn

        let person = Browser app.Client

        let authorizeUrl =
            "/connect/authorize?client_id=" + Uri.EscapeDataString clientId
            + "&response_type=code&redirect_uri=" + Uri.EscapeDataString RedirectUri
            + "&scope=openid&code_challenge=" + Challenge
            + "&code_challenge_method=S256&state=app-state"

        let! toPicker = person.Get authorizeUrl
        let! picker = person.Get(location toPicker)
        Assert.Equal(HttpStatusCode.OK, picker.StatusCode)
        assertSiteWide "the picker" picker

        let! toProvider = person.Get((location toPicker).Replace("/signin", "/connect/fake/start"))
        Assert.Equal(HttpStatusCode.Redirect, toProvider.StatusCode)
        assertSiteWide "the redirect to the provider" toProvider

        let! signedIn = signInViaFake person clientId "openid" "fake-user-1" "sam@example.com"
        Assert.True(Result.isOk signedIn, $"sign-in refused: {signedIn}")

        let! toApp = person.Get authorizeUrl
        Assert.Equal(HttpStatusCode.Redirect, toApp.StatusCode)
        Assert.StartsWith(RedirectUri, location toApp)
        assertSiteWide "the redirect back to the app" toApp
    }

[<Fact>]
let ``admin pages and the redirect to sign in carry the site-wide policy`` () =
    task {
        use! app = Fixtures.createApp ()
        let operator = Browser app.Client
        do! claimOperator app operator

        let! apps = operator.Get "/admin/apps"
        Assert.Equal(HttpStatusCode.OK, apps.StatusCode)
        assertSiteWide "the Apps screen" apps

        let! anonymous = (Browser app.Client).Get "/admin/apps"
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode)
        assertSiteWide "the redirect to sign in" anonymous
    }

[<Fact>]
let ``the invitation page keeps its stricter policy, once`` () =
    task {
        use! app = Fixtures.createApp ()
        let! invitation = app.Client.GetAsync "/invite/no-such-secret"
        Assert.Equal<string list>([ "no-referrer" ], referrerPolicy invitation)
    }

[<Fact>]
let ``OpenIddict's form_post answer carries no policy, so its post to the app keeps its Origin`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app
        let! clientId, _ = registerApp operator "Grafana"

        let person = Browser app.Client
        let! signedIn = signInViaFake person clientId "openid" "fake-user-1" "sam@example.com"
        Assert.True(Result.isOk signedIn, $"sign-in refused: {signedIn}")

        let! formPost =
            person.Get(
                "/connect/authorize?client_id=" + Uri.EscapeDataString clientId
                + "&response_type=code&response_mode=form_post&redirect_uri=" + Uri.EscapeDataString RedirectUri
                + "&scope=openid&code_challenge=" + Challenge
                + "&code_challenge_method=S256&state=app-state"
            )

        Assert.Equal(HttpStatusCode.OK, formPost.StatusCode)
        let! html = formPost.Content.ReadAsStringAsync()
        Assert.Contains(RedirectUri, html)
        Assert.Empty(referrerPolicy formPost)
    }
