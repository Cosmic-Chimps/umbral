module Umbral.Tests.SignInFlowTests

open System
open System.Net
open System.Security.Claims
open System.Text.RegularExpressions
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Options
open Xunit
open Umbral.Domains.Connectors
open Umbral.Handlers.Connect
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure

// ---------------------------------------------------------------------------
// The open-redirect guard
//
// `returnUrl` reaches the picker straight from a query string, so it is
// attacker-supplied by construction. Anything but a path on this instance would
// make the sign-in page a redirector — the classic phishing primitive, and far
// more convincing on a login domain than anywhere else.
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("/connect/authorize?client_id=x", "/connect/authorize?client_id=x")>]
[<InlineData("/", "/")>]
[<InlineData("/admin/apps", "/admin/apps")>]
let ``a local path is kept`` (input: string) (expected: string) =
    Assert.Equal(expected, Picker.sanitizeReturnUrl input)

[<Theory>]
[<InlineData("https://evil.example.com/steal")>]
[<InlineData("http://evil.example.com")>]
// Protocol-relative: the browser reads this as a host, not a path.
[<InlineData("//evil.example.com/steal")>]
[<InlineData("javascript:alert(1)")>]
[<InlineData("evil.example.com")>]
[<InlineData("")>]
let ``anything that could leave this instance becomes the root`` (input: string) =
    Assert.Equal("/", Picker.sanitizeReturnUrl input)

[<Fact>]
let ``a missing returnUrl becomes the root`` () =
    Assert.Equal("/", Picker.sanitizeReturnUrl null)

// ---------------------------------------------------------------------------
// The picker
// ---------------------------------------------------------------------------

[<Fact>]
let ``the picker says so when no connector is configured`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/signin"
        let! body = response.Content.ReadAsStringAsync()

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        // The end user cannot fix this, so the message has to point at the
        // person who can rather than imply they did something wrong.
        Assert.Contains("data-no-connectors", body)
    }

[<Fact>]
let ``an unknown connector start is a 404`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/connect/nope/start"
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)
    }

// ---------------------------------------------------------------------------
// The callback, with no provider involved at all
// ---------------------------------------------------------------------------

[<Fact>]
let ``a callback with no correlation cookie is refused`` () =
    task {
        use! app = Fixtures.createApp ()

        // Arriving cold — a replayed link, or someone poking at the route.
        let! response = app.Client.GetAsync "/connect/discord/callback?code=abc&state=xyz"

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("/signin", response.Headers.Location |> string)
        Assert.Contains("msg=sign-in-failed", response.Headers.Location |> string)
    }

[<Fact>]
let ``a callback with no state is refused`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/connect/discord/callback?code=abc"
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("msg=sign-in-failed", response.Headers.Location |> string)
    }

[<Fact>]
let ``a cold access_denied is NOT reported as a decline`` () =
    task {
        use! app = Fixtures.createApp ()

        // A real cancellation carries the correlation cookie, because /start
        // set it. Arriving without one means the query string is unattested —
        // anyone can type this URL — so nothing in it may be believed, not even
        // the claim that a person pressed cancel. The correlation is checked
        // before the provider's error for exactly this reason.
        //
        // The cookie-present decline path is covered by the round-trip state
        // machine tests, where it can be driven honestly.
        let! response = app.Client.GetAsync "/connect/discord/callback?error=access_denied&state=xyz"

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("msg=sign-in-failed", response.Headers.Location |> string)
    }

[<Fact>]
let ``the failure redirect never carries the provider's own words`` () =
    task {
        use! app = Fixtures.createApp ()

        let! response =
            app.Client.GetAsync
                "/connect/discord/callback?error=invalid_client&error_description=The%20client%20secret%20is%20wrong&state=xyz"

        // FR-037: the operator gets the cause in the security log; the end user
        // gets a code. A provider's text can name a misconfiguration.
        let location = response.Headers.Location |> string
        Assert.DoesNotContain("client", location)
        Assert.DoesNotContain("secret", location)
    }

// ---------------------------------------------------------------------------
// What a session and a token say about the connector (feature 003, R3)
//
// The slug and the kind used to be the same string, so nothing noticed that
// the code sent one and the README promised the other. A connector that is not
// named after its kind makes them differ.
// ---------------------------------------------------------------------------

/// A Fake connector whose slug is NOT its kind, so "idp is the kind" and "the
/// session carries the slug" cannot both pass by accident.
let private addLabConnector (app: Fixtures.TestApp) =
    task {
        let! reference = Fixtures.putSecret app "fake-lab" "unused"

        do!
            ConnectorStore.save
                app.DbConfig
                app.Clock
                { Slug = "fake-lab"
                  Kind = Fake
                  DisplayName = "Fake lab"
                  ClientId = "fake"
                  ClientSecretRef = reference.Encoded
                  ClientSecretHint = "used"
                  IssuerUrl = None
                  AllowedEmailDomains = []
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None
    }

/// A whole sign-in through the connector `slug`, ending with the authorization
/// code. Like `Journeys.signInThroughFake`, but the route is not fixed to /fake.
let private signInThrough (browser: Journeys.Browser) (slug: string) (clientId: string) (scope: string) =
    task {
        let! toPicker = browser.Get(Journeys.authorizeUrl clientId scope)
        let picker = toPicker.Headers.Location |> string
        let! toProvider = browser.Get(picker.Replace("/signin", $"/connect/{slug}/start"))

        let providerUrl = toProvider.Headers.Location |> string
        let decoded = Uri.UnescapeDataString providerUrl
        let redirect = Regex.Match(decoded, "redirect_uri=([^&]+)").Groups[1].Value
        let state = Regex.Match(decoded, "state=([^&]+)").Groups[1].Value

        let! approved =
            browser.PostForm(
                providerUrl,
                "/dev/fake-provider/authorize",
                [ "redirect_uri", redirect
                  "state", state
                  "subject", "lab-subject"
                  "email", "lab@example.com"
                  "displayName", "Lab User"
                  "emailVerified", "on" ]
            )

        let! back = browser.Get(approved.Headers.Location |> string)
        let! issued = browser.Get(back.Headers.Location |> string)
        let final = issued.Headers.Location |> string
        let code = Regex.Match(final, "code=([^&]+)").Groups[1].Value

        Assert.True(code <> "", $"sign-in did not end in an authorization code: {final}")
        return Uri.UnescapeDataString code
    }

/// Read what the Umbral session cookie carries, the way the app itself does.
let private sessionOf (app: Fixtures.TestApp) (browser: Journeys.Browser) =
    let options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get Sessions.Scheme
    match Option.ofObj (options.TicketDataFormat.Unprotect browser.Cookies[Sessions.CookieName]) with
    | Some ticket -> ticket.Principal
    | None -> failwith "the session cookie could not be read"

let private claimValue (principal: ClaimsPrincipal) (claimType: string) =
    match Option.ofObj (principal.FindFirst claimType) with
    | Some claim -> claim.Value
    | None -> failwith $"the session has no {claimType} claim"

[<Fact>]
let ``the token's idp is the connector's kind, not its slug`` () =
    task {
        use! app = Fixtures.createAppWith Journeys.fakeSettings ignore
        let browser = Journeys.Browser app.Client
        do! Journeys.claimInstance app browser
        do! addLabConnector app
        let! clientId, secret = Journeys.createClientApp browser

        let! code = signInThrough browser "fake-lab" clientId "openid"
        let! response = Journeys.exchangeCode app.Client clientId secret code
        let! body = Journeys.json response
        let payload = Journeys.segmentOf 1 (Journeys.strOf body "id_token")

        // The README promises the kind. With two connectors of one kind, the
        // slug would give an app a new value for the same person.
        Assert.Equal("fake", Journeys.strOf payload "idp")
    }

[<Fact>]
let ``the session carries the connector's slug in its own claim`` () =
    task {
        use! app = Fixtures.createAppWith Journeys.fakeSettings ignore
        let browser = Journeys.Browser app.Client
        do! Journeys.claimInstance app browser
        do! addLabConnector app
        let! clientId, _ = Journeys.createClientApp browser

        let! _ = signInThrough browser "fake-lab" clientId "openid"
        let principal = sessionOf app browser

        Assert.Equal("fake-lab", claimValue principal Sessions.ConnectorClaim)
        Assert.Equal("fake", claimValue principal Sessions.IdpClaim)
        Assert.Equal(Some "fake-lab", Sessions.currentConnector principal)
    }

[<Fact>]
let ``currentConnector falls back to idp for a session from before the upgrade`` () =
    // Before the upgrade `idp` held the slug, and every slug equalled its kind.
    let identity = ClaimsIdentity(Sessions.Scheme)
    identity.AddClaim(Claim(ClaimTypes.NameIdentifier, "u1"))
    identity.AddClaim(Claim(Sessions.IdpClaim, "discord"))

    Assert.Equal(Some "discord", Sessions.currentConnector (ClaimsPrincipal identity))
    Assert.Equal(None, Sessions.currentConnector (ClaimsPrincipal(ClaimsIdentity(Sessions.Scheme))))

[<Fact>]
let ``a session cookie with only an idp claim is still honoured`` () =
    task {
        use! app = Fixtures.createAppWith Journeys.fakeSettings ignore
        let browser = Journeys.Browser app.Client
        let! prepared = Journeys.prepare app browser

        // A real sign-in first, so the user exists in the database.
        let! _ = Journeys.signInThroughFake browser prepared.ClientId "openid" "old-subject" "old@example.com"
        let! userId = Flows.userIdOf app "old-subject"

        // The cookie a pre-upgrade build would have written: the subject and an
        // `idp` holding the slug, and no `connector` claim at all.
        let identity = ClaimsIdentity(Sessions.Scheme)
        identity.AddClaim(Claim(ClaimTypes.NameIdentifier, userId))
        identity.AddClaim(Claim(Sessions.IdpClaim, "fake"))

        let options =
            app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get Sessions.Scheme

        let ticket = AuthenticationTicket(ClaimsPrincipal identity, Sessions.Scheme)
        browser.Cookies[Sessions.CookieName] <- options.TicketDataFormat.Protect ticket

        let! response = browser.Get(Journeys.authorizeUrl prepared.ClientId "openid")

        // Honoured: a code, not a trip back to the picker.
        let location = response.Headers.Location |> string
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.StartsWith(Journeys.RedirectUri, location)
        Assert.Contains("code=", location)
    }

[<Fact>]
let ``a failed callback goes back to sign-in with the original returnUrl`` () =
    task {
        use! app = Fixtures.createAppWith Journeys.fakeSettings ignore
        let browser = Journeys.Browser app.Client
        let! prepared = Journeys.prepare app browser

        let authorize = Journeys.authorizeUrl prepared.ClientId "openid"
        let! toPicker = browser.Get authorize
        let picker = toPicker.Headers.Location |> string
        let! toProvider = browser.Get(picker.Replace("/signin", "/connect/fake/start"))

        let state =
            Regex.Match(Uri.UnescapeDataString(toProvider.Headers.Location |> string), "state=([^&]+)").Groups[1].Value

        // The correlation is valid (the cookie is there and the state matches)
        // but the provider sent no authorization code.
        let! failed = browser.Get $"/connect/fake/callback?state={Uri.EscapeDataString state}"

        Assert.Equal(HttpStatusCode.Redirect, failed.StatusCode)
        let location = failed.Headers.Location |> string
        Assert.StartsWith("/signin", location)
        Assert.Contains("msg=sign-in-failed", location)

        let returnUrl =
            Regex.Match(location, "returnUrl=([^&]+)").Groups[1].Value |> Uri.UnescapeDataString

        Assert.Equal(authorize, returnUrl)
    }

[<Fact>]
let ``a provider decline with a valid correlation also keeps the returnUrl`` () =
    task {
        use! app = Fixtures.createAppWith Journeys.fakeSettings ignore
        let browser = Journeys.Browser app.Client
        let! prepared = Journeys.prepare app browser

        let authorize = Journeys.authorizeUrl prepared.ClientId "openid"
        let! toPicker = browser.Get authorize
        let picker = toPicker.Headers.Location |> string
        let! _ = browser.Get(picker.Replace("/signin", "/connect/fake/start"))

        // The person pressed cancel at the provider.
        let! declined = browser.Get "/connect/fake/callback?error=access_denied"

        let location = declined.Headers.Location |> string
        Assert.Contains("msg=access-denied", location)

        let returnUrl =
            Regex.Match(location, "returnUrl=([^&]+)").Groups[1].Value |> Uri.UnescapeDataString

        Assert.Equal(authorize, returnUrl)
    }

[<Fact>]
let ``a failed callback with no correlation has no returnUrl to go back to`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/connect/discord/callback?code=abc&state=xyz"

        let location = response.Headers.Location |> string
        Assert.DoesNotContain("returnUrl", location)
    }
