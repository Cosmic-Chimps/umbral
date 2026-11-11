/// A native app on loopback may choose its port when it asks (RFC 8252 §7.3),
/// through the real authorize, token and end-session endpoints.
///
/// The rule itself is pinned case by case in RedirectUriTests. What is pinned
/// here is the wiring: that the rule is the one OpenIddict consults, that it is
/// open to public apps and to nobody else, and that OpenIddict's own looser rule
/// (which accepts `localhost` and `https`) is switched off.
module Umbral.Tests.LoopbackRedirectTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading.Tasks
open Xunit
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

/// The operator claimed and the fake connector in place, on an app the test owns.
let private prepare (app: Fixtures.TestApp) =
    task {
        let browser = Browser(app.Client)
        do! claimOperator app browser
        do! seedFakeConnector app
        return browser
    }

/// Register an app through the Apps screen and return its client id. A public
/// app has no secret, so the id comes from the address the screen redirects to.
let private register (browser: Browser) (appType: string) (redirectUri: string) (postLogoutRedirectUri: string) =
    task {
        let! created =
            browser.PostForm(
                "/admin/apps",
                "/admin/apps",
                [ "name", "Loopback"
                  "redirectUri", redirectUri
                  "postLogoutRedirectUri", postLogoutRedirectUri
                  "appType", appType ]
            )

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode)
        let path = (location created).Split('?')[0]
        return path.Substring(path.LastIndexOf '/' + 1)
    }

let private authorizeUrl (clientId: string) (redirectUri: string) =
    "/connect/authorize?client_id=" + Uri.EscapeDataString clientId
    + "&response_type=code&redirect_uri=" + Uri.EscapeDataString redirectUri
    + "&scope=openid&code_challenge=" + Challenge
    + "&code_challenge_method=S256&state=app-state"

/// What an authorize request that OpenIddict accepts looks like from outside:
/// it is sent on to the sign-in picker.
let private reachesSignIn (response: HttpResponseMessage) =
    response.StatusCode = HttpStatusCode.Redirect && (location response).Contains "/signin"

/// The assertion that matters for a refusal: whatever Umbral does, it does not
/// send the browser to the address that was asked for, which would hand the
/// code to whoever asked.
let private assertRefused (requested: string) (response: HttpResponseMessage) =
    Assert.False(reachesSignIn response, $"{requested} was accepted: it was sent to the sign-in")
    Assert.False((location response).StartsWith(requested, StringComparison.Ordinal), $"redirected to {requested}")
    Assert.DoesNotContain(requested, location response)

/// The whole sign-in through the fake provider for `redirectUri`, ending with
/// the redirect that delivers the code.
let private signIn (browser: Browser) (clientId: string) (redirectUri: string) =
    task {
        let! toPicker = browser.Get(authorizeUrl clientId redirectUri)
        Assert.True(reachesSignIn toPicker, $"authorize answered {toPicker.StatusCode} {location toPicker}")
        let! toProvider = browser.Get((location toPicker).Replace("/signin", "/connect/fake/start"))

        let providerUrl = location toProvider
        let decoded = Uri.UnescapeDataString providerUrl
        let redirect = Regex.Match(decoded, "redirect_uri=([^&]+)").Groups[1].Value
        let state = Regex.Match(decoded, "state=([^&]+)").Groups[1].Value

        let! approved =
            browser.PostForm(
                providerUrl,
                "/dev/fake-provider/authorize",
                [ "redirect_uri", redirect
                  "state", state
                  "subject", "loopback-subject"
                  "email", "loop@example.com"
                  "displayName", "Test User"
                  "emailVerified", "on" ]
            )

        let! back = browser.Get(location approved)
        let replayed = location back
        Assert.Contains("/connect/authorize", replayed)
        return! browser.Get replayed
    }

/// Redeem a code as a public client: no secret, the verifier instead.
let private exchange (app: Fixtures.TestApp) (clientId: string) (code: string) (redirectUri: string) =
    let message = new HttpRequestMessage(HttpMethod.Post, "/connect/token")

    message.Content <-
        new FormUrlEncodedContent(
            dict
                [ "grant_type", "authorization_code"
                  "code", Uri.UnescapeDataString code
                  "redirect_uri", redirectUri
                  "code_verifier", Verifier
                  "client_id", clientId ]
        )

    app.Client.SendAsync message

// ---------------------------------------------------------------------------
// Authorize and token
// ---------------------------------------------------------------------------

[<Fact>]
let ``a public app registered on 127.0.0.1 signs in on the port it picked, and only that port redeems`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! browser = prepare app
        let! clientId = register browser "spa" "http://127.0.0.1/callback" ""
        let requested = "http://127.0.0.1:53124/callback"

        let! issued = signIn browser clientId requested
        let delivered = location issued
        Assert.StartsWith(requested + "?", delivered)
        let code = Regex.Match(delivered, "code=([^&]+)").Groups[1].Value
        Assert.NotEmpty code

        // The token endpoint compares against the address the code was issued
        // for, so a different port is a different redirect_uri.
        let! wrongPort = exchange app clientId code "http://127.0.0.1:53125/callback"
        Assert.Equal(HttpStatusCode.BadRequest, wrongPort.StatusCode)

        // A refused redemption may have spent the code, so ask for another.
        // The browser holds a session now, so authorize answers with the code.
        let! again = browser.Get(authorizeUrl clientId requested)
        Assert.StartsWith(requested + "?", location again)
        let code = Regex.Match(location again, "code=([^&]+)").Groups[1].Value

        let! redeemed = exchange app clientId code requested
        Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode)
        let! body = redeemed.Content.ReadAsStringAsync()
        use json = JsonDocument.Parse body
        Assert.True(json.RootElement.TryGetProperty("access_token") |> fst)
        Assert.True(json.RootElement.TryGetProperty("id_token") |> fst)
    }

[<Fact>]
let ``a public app registered on [::1] is accepted on a port of its choosing`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! browser = prepare app
        let! clientId = register browser "spa" "http://[::1]/callback" ""
        let! response = browser.Get(authorizeUrl clientId "http://[::1]:8400/callback")
        Assert.True(reachesSignIn response, $"answered {response.StatusCode} {location response}")
    }

[<Theory>]
// localhost is a name: this is the case OpenIddict's own rule would accept.
[<InlineData("http://localhost/callback", "http://localhost:53124/callback")>]
[<InlineData("https://127.0.0.1/callback", "https://127.0.0.1:53124/callback")>]
// Only the port may differ.
[<InlineData("http://127.0.0.1/callback", "http://127.0.0.1:53124/other")>]
[<InlineData("http://127.0.0.1/callback", "http://127.0.0.1:53124/callback?x=1")>]
// A registered port is the port.
[<InlineData("http://127.0.0.1:8400/callback", "http://127.0.0.1:8401/callback")>]
// Not a loopback address that was registered.
[<InlineData("http://127.0.0.1/callback", "http://127.0.0.2:53124/callback")>]
let ``a public app is refused a loopback redirect the rule does not allow`` (registered: string) (requested: string) =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! browser = prepare app
        let! clientId = register browser "spa" registered ""
        let! response = browser.Get(authorizeUrl clientId requested)
        assertRefused requested response
    }

[<Fact>]
let ``a web app does not get the loopback port rule`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! browser = prepare app
        let! clientId = register browser "web" "http://127.0.0.1/callback" ""
        let requested = "http://127.0.0.1:53124/callback"
        let! response = browser.Get(authorizeUrl clientId requested)
        assertRefused requested response

        // And the exact registration still works for it.
        let! exact = browser.Get(authorizeUrl clientId "http://127.0.0.1/callback")
        Assert.True(reachesSignIn exact, $"answered {exact.StatusCode} {location exact}")
    }

// ---------------------------------------------------------------------------
// End session
// ---------------------------------------------------------------------------

let private logoutUrl (clientId: string option) (postLogout: string) =
    "/connect/logout?"
    + (match clientId with
       | Some id -> "client_id=" + Uri.EscapeDataString id + "&"
       | None -> "")
    + "post_logout_redirect_uri=" + Uri.EscapeDataString postLogout
    + "&state=s"

[<Theory>]
[<InlineData(true)>]
[<InlineData(false)>]
let ``a public app's post-logout redirect may also pick its port`` (withClientId: bool) =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! browser = prepare app
        let! clientId = register browser "spa" "http://127.0.0.1/callback" "http://127.0.0.1/signed-out"
        let requested = "http://127.0.0.1:53124/signed-out"

        let! response = browser.Get(logoutUrl (if withClientId then Some clientId else None) requested)
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.StartsWith(requested, location response)
    }

[<Theory>]
[<InlineData("http://localhost/signed-out", "http://localhost:53124/signed-out")>]
[<InlineData("https://127.0.0.1/signed-out", "https://127.0.0.1:53124/signed-out")>]
[<InlineData("http://127.0.0.1/signed-out", "http://127.0.0.1:53124/elsewhere")>]
[<InlineData("http://127.0.0.1:8400/signed-out", "http://127.0.0.1:8401/signed-out")>]
let ``a post-logout redirect the rule does not allow is not honoured`` (registered: string) (requested: string) =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! browser = prepare app
        let! clientId = register browser "spa" "http://127.0.0.1/callback" registered

        let! response = browser.Get(logoutUrl (Some clientId) requested)
        Assert.DoesNotContain(requested, location response)
    }
