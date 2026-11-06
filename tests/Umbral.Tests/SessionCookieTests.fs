/// The end user's session cookie, `umbral_session`, as the browser receives it
/// (task c5hn).
///
/// The cookie is persistent: it carries an expiry 14 days out, renewed on use, so
/// a session outlives a browser restart and iOS can share it between Safari and
/// an app's `ASWebAuthenticationSession` (docs/native-apps.md). These tests read
/// the raw `Set-Cookie` headers, because a cookie without an expiry looks the
/// same to a test browser that keeps every cookie it is given.
///
/// A longer-lived cookie must not keep anyone in who should be out, so the last
/// tests check the two ways a session ends before its expiry: the person is
/// disabled (FR-022, re-checked at every authorization), or signs out.
module Umbral.Tests.SessionCookieTests

open System
open System.Net.Http
open System.Text.RegularExpressions
open Microsoft.Net.Http.Headers
open Xunit
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

let private Lifetime = TimeSpan.FromDays 14.0

/// The session's `Set-Cookie` headers in a response, parsed. More than one when
/// the ticket is chunked (`umbral_session` plus `umbral_sessionC1`, ...).
let private sessionCookies (response: HttpResponseMessage) =
    match response.Headers.TryGetValues "Set-Cookie" with
    | true, values ->
        values
        |> Option.ofObj
        |> Option.defaultValue Seq.empty
        |> Seq.map SetCookieHeaderValue.Parse
        |> Seq.filter (fun c -> (string c.Name).StartsWith Sessions.CookieName)
        |> List.ofSeq
    | _ -> []

/// Assert that every session cookie in the response expires `Lifetime` after
/// `now`, give or take the second the header is rounded to.
let private assertExpiresFrom (now: DateTimeOffset) (response: HttpResponseMessage) =
    let cookies = sessionCookies response
    Assert.NotEmpty cookies

    for cookie in cookies do
        Assert.True(cookie.Expires.HasValue, $"{cookie.Name} has no expiry, so it is a browser-session cookie")
        let off = cookie.Expires.Value - (now + Lifetime)
        Assert.True(abs off.TotalSeconds < 2.0, $"{cookie.Name} expires at {cookie.Expires.Value}, {off} from {now + Lifetime}")
        Assert.True(cookie.HttpOnly)
        Assert.Equal(SameSiteMode.Lax, cookie.SameSite)

let private authorizeUrl (clientId: string) =
    "/connect/authorize?client_id=" + Uri.EscapeDataString clientId
    + "&response_type=code&redirect_uri=" + Uri.EscapeDataString RedirectUri
    + "&scope=openid&code_challenge=" + Challenge
    + "&code_challenge_method=S256&state=s"

/// A claimed instance with the fake connector and one app.
let private setup (app: Fixtures.TestApp) =
    task {
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! seedFakeConnector app
        let! clientId, _ = registerApp operator "Grafana"
        return clientId
    }

/// A sign-in through the fake provider, up to the provider's callback, which is
/// the response that establishes the session.
let private callbackOf (browser: Browser) (clientId: string) =
    task {
        let! toPicker = browser.Get(authorizeUrl clientId)
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
                  "subject", "fake-user-1"
                  "email", "sam@example.com"
                  "displayName", "Sam"
                  "emailVerified", "on" ]
            )

        let! callback = browser.Get(location approved)
        Assert.Contains("/connect/authorize", location callback)
        return callback
    }

/// An expired `Set-Cookie` for the session: what a response sends to delete it.
let private deletesSession (response: HttpResponseMessage) =
    sessionCookies response
    |> List.exists (fun c -> c.Expires.HasValue && c.Expires.Value < DateTimeOffset.UtcNow.AddYears -1)

[<Fact>]
let ``a sign-in sets a session cookie that expires 14 days later`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! clientId = setup app
        let person = Browser(app.Client)

        let now = app.Clock.GetUtcNow()
        let! callback = callbackOf person clientId

        assertExpiresFrom now callback
    }

[<Fact>]
let ``a session used after half its lifetime is renewed for another 14 days`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! clientId = setup app
        let person = Browser(app.Client)
        let! _ = callbackOf person clientId

        app.Clock.Advance(TimeSpan.FromDays 8.0)
        let later = app.Clock.GetUtcNow()
        let! again = person.Get(authorizeUrl clientId)

        // Still signed in: the code comes straight back.
        Assert.StartsWith(RedirectUri, location again)
        assertExpiresFrom later again
    }

[<Fact>]
let ``a session unused for longer than 14 days is not honoured`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! clientId = setup app
        let person = Browser(app.Client)
        let! _ = callbackOf person clientId

        app.Clock.Advance(Lifetime + TimeSpan.FromMinutes 1.0)
        let! again = person.Get(authorizeUrl clientId)

        Assert.Contains("/signin", location again)
    }

[<Fact>]
let ``a disabled person's persistent session is deleted at the next authorization`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! clientId = setup app
        let person = Browser(app.Client)
        let! _ = callbackOf person clientId

        let! subject = userIdOf app "fake-user-1"
        do! Users.setEnabled app.DbConfig subject false

        let! refused = person.Get(authorizeUrl clientId)

        Assert.DoesNotContain("code=", location refused)
        Assert.Contains("error=access_denied", location refused)
        Assert.True(deletesSession refused, "the refusal deletes the session cookie")
    }

[<Fact>]
let ``signing out deletes the persistent session cookie`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! clientId = setup app
        let person = Browser(app.Client)
        let! _ = callbackOf person clientId

        let! logout = person.Get "/connect/logout"

        Assert.True(deletesSession logout, "the sign-out deletes the session cookie")
    }
