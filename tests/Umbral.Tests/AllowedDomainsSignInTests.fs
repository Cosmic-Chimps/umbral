/// Allowed email domains at the sign-in callback (specs/006, User Stories 1 and
/// the sign-in half of 2).
///
/// Driven through the real endpoints with the fake connector: the correlation
/// cookie, the linking rule, the session and the token issuance are the product's
/// own. Only the hop to a third party is replaced, and the fake provider says
/// whatever email and verified flag the test types into its form.
module Umbral.Tests.AllowedDomainsSignInTests

open System
open System.Net
open System.Net.Http
open System.Security.Claims
open System.Text.RegularExpressions
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Options
open Xunit
open Umbral.Domains
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure

/// The exact page text of contracts/sign-in.md for `account-not-allowed`.
[<Literal>]
let private RefusalText =
    "This account can't be used to sign in here. Try a different account or another option below, or contact whoever runs this Umbral."

/// What the callback answered, and where it sent the browser.
type private Attempt =
    { Callback: HttpResponseMessage
      Location: string }

/// One whole sign-in through the fake provider, up to and including the
/// callback. Does not follow the callback's redirect: the caller decides whether
/// that is the replayed authorize request (admitted) or the sign-in page (refused).
let private attemptVia
    (slug: string)
    (browser: Journeys.Browser)
    (clientId: string)
    (subject: string)
    (email: string)
    (verified: bool)
    (displayName: string)
    =
    task {
        let! toPicker = browser.Get(Journeys.authorizeUrl clientId "openid")
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
                  "subject", subject
                  "email", email
                  "displayName", displayName ]
                @ (if verified then [ "emailVerified", "on" ] else [])
            )

        let! back = browser.Get(approved.Headers.Location |> string)
        return { Callback = back; Location = back.Headers.Location |> string }
    }

let private attempt = attemptVia "fake"

let private signIn (browser: Journeys.Browser) (clientId: string) (subject: string) (email: string) (verified: bool) =
    attempt browser clientId subject email verified "Test User"

/// A prepared app with the fake connector, an app registered, and the lists the
/// test asks for already stored.
let private withApp
    (connectorList: string list)
    (instanceList: string list)
    (body: Fixtures.TestApp -> Journeys.Browser -> Journeys.Prepared -> Task<unit>)
    =
    task {
        use! app = Fixtures.createAppWith Journeys.fakeSettings ignore
        let browser = Journeys.Browser app.Client
        let! prepared = Journeys.prepare app browser

        if not (List.isEmpty connectorList) then
            let! stored = ConnectorStore.findBySlug app.DbConfig "fake"
            do! ConnectorStore.save app.DbConfig app.Clock { stored.Value with AllowedEmailDomains = connectorList } None

        if not (List.isEmpty instanceList) then
            do! EmailDomainRules.setInstanceList app.DbConfig app.Clock instanceList

        // The operator's admin cookie is not an end user's: a fresh browser
        // signs in, as a person at an app would.
        let person = Journeys.Browser app.Client
        do! body app person prepared
    }

let private count (app: Fixtures.TestApp) (statement: string) =
    task {
        let! value = Flows.scalar app statement []
        return int value
    }

let private expectCount (app: Fixtures.TestApp) (statement: string) (expected: int) =
    task {
        let! actual = count app statement
        Assert.Equal(expected, actual)
    }

/// Every `sign_in_failed` detail, with its user id and connector.
let private failures (app: Fixtures.TestApp) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()

        command.CommandText <-
            "SELECT ifnull(detail, ''), ifnull(user_id, ''), ifnull(connector_slug, ''), ifnull(client_id, '')
             FROM security_events WHERE event_type = 'sign_in_failed' ORDER BY id"

        use! reader = command.ExecuteReaderAsync()
        let rows = ResizeArray()

        while reader.Read() do
            rows.Add((reader.GetString 0, reader.GetString 1, reader.GetString 2, reader.GetString 3))

        return List.ofSeq rows
    }

let private sessionOf (app: Fixtures.TestApp) (browser: Journeys.Browser) =
    let options =
        app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get Sessions.Scheme

    match Option.ofObj (options.TicketDataFormat.Unprotect browser.Cookies[Sessions.CookieName]) with
    | Some ticket -> ticket.Principal
    | None -> failwith "the session cookie could not be read"

let private admittedCode (browser: Journeys.Browser) (landed: Attempt) =
    task {
        Assert.Contains("/connect/authorize", landed.Location)
        let! issued = browser.Get landed.Location
        let final = issued.Headers.Location |> string
        Assert.Contains("code=", final)
    }

// ---------------------------------------------------------------------------
// Admitted
// ---------------------------------------------------------------------------

[<Fact>]
let ``a verified address at a listed domain gets a session and a code`` () =
    withApp [ "acme.com" ] [] (fun app browser prepared ->
        task {
            let! landed = signIn browser prepared.ClientId "ann" "ann@acme.com" true
            do! admittedCode browser landed

            Assert.True(browser.Cookies.ContainsKey Sessions.CookieName)
            do! expectCount app "SELECT count(*) FROM users" 1
            let! noFailures = failures app
            Assert.Empty noFailures

            // The session remembers the domain it was admitted on, not the address.
            let principal = sessionOf app browser
            Assert.Equal(Some "acme.com", Sessions.currentEmailDomain principal)
            Assert.Equal<string list>([], principal.Claims |> Seq.filter (fun c -> c.Value.Contains "ann@") |> Seq.map _.Value |> List.ofSeq)
        })

[<Fact>]
let ``the case of the address does not matter`` () =
    withApp [ "acme.com" ] [] (fun app browser prepared ->
        task {
            let! landed = signIn browser prepared.ClientId "erin" "ERIN@ACME.COM" true
            do! admittedCode browser landed
            Assert.Equal(Some "acme.com", Sessions.currentEmailDomain (sessionOf app browser))
        })

[<Fact>]
let ``with no list anywhere nobody is refused, whatever their email`` () =
    withApp [] [] (fun app browser prepared ->
        task {
            // An unverified address and no address at all: both got in before
            // this feature and still do (SC-006).
            let! first = signIn browser prepared.ClientId "u1" "bob@gmail.com" false
            do! admittedCode browser first

            let second = Journeys.Browser app.Client
            let! landed = signIn second prepared.ClientId "u2" "" false
            do! admittedCode second landed

            let! noFailures = failures app
            Assert.Empty noFailures
            do! expectCount app "SELECT count(*) FROM users" 2

            // No verified email, so no domain to remember.
            Assert.Equal(None, Sessions.currentEmailDomain (sessionOf app second))
        })

// ---------------------------------------------------------------------------
// Refused
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("dave@eu.acme.com", true, "domain_not_allowed", "eu.acme.com")>]
[<InlineData("bob@gmail.com", true, "domain_not_allowed", "gmail.com")>]
[<InlineData("carol@acme.com", false, "email_not_verified", "acme.com")>]
[<InlineData("", true, "no_email", "-")>]
[<InlineData("", false, "no_email", "-")>]
let ``a person the rule does not admit is refused before anything is written``
    (email: string, verified: bool, reason: string, domain: string)
    =
    withApp [ "acme.com" ] [] (fun app browser prepared ->
        task {
            let! landed = signIn browser prepared.ClientId "stranger" email verified

            // The failure mechanism of `account-disabled`: back to the sign-in
            // page of the app, with a code.
            Assert.Equal(HttpStatusCode.Redirect, landed.Callback.StatusCode)
            Assert.StartsWith("/signin?", landed.Location)
            Assert.Contains("msg=account-not-allowed", landed.Location)

            // Nothing about them exists afterwards.
            Assert.False(browser.Cookies.ContainsKey Sessions.CookieName)
            do! expectCount app "SELECT count(*) FROM users" 0
            do! expectCount app "SELECT count(*) FROM linked_accounts" 0
            do! expectCount app "SELECT count(*) FROM security_events WHERE event_type IN ('user_created', 'user_linked', 'sign_in_succeeded')" 0

            // One event, in the closed shape, without the address or a user.
            let! events = failures app
            let detail, userId, connector, _ = Assert.Single events
            Assert.Equal($"email_domain_refused;reason={reason};rule=connector;stage=callback;domain={domain}", detail)
            Assert.Equal("", userId)
            Assert.Equal("fake", connector)

            if email <> "" then
                Assert.DoesNotContain(email, detail)
        })

[<Fact>]
let ``the refusal page says the exact text and names no domain`` () =
    withApp [ "acme.com" ] [] (fun app browser prepared ->
        task {
            let! landed = signIn browser prepared.ClientId "bob" "bob@gmail.com" true
            let! html = browser.GetHtml landed.Location
            let text = WebUtility.HtmlDecode html

            Assert.Contains(RefusalText, text)

            // Neither the listed domain, the person's, nor the reason.
            Assert.DoesNotContain("acme.com", html)
            Assert.DoesNotContain("gmail.com", html)
            Assert.DoesNotContain("domain_not_allowed", html)
            Assert.DoesNotContain("not verified", html)
        })

[<Fact>]
let ``a refused existing user is left exactly as they were`` () =
    withApp [] [] (fun app browser prepared ->
        task {
            // They sign in while nothing limits the connector.
            let! first = attempt browser prepared.ClientId "old-hand" "old@example.com" true "Old Name"
            do! admittedCode browser first

            let row () =
                Flows.scalar
                    app
                    "SELECT display_name || '|' || ifnull(email, '') || '|' || email_verified || '|' || last_seen_utc FROM users"
                    []

            let! before = row ()

            // Later the operator limits the connector, and they come back with
            // a changed name from a domain that is no longer allowed.
            let! stored = ConnectorStore.findBySlug app.DbConfig "fake"
            do! ConnectorStore.save app.DbConfig app.Clock { stored.Value with AllowedEmailDomains = [ "acme.com" ] } None
            app.Clock.Advance(TimeSpan.FromHours 3.)

            let again = Journeys.Browser app.Client
            let! landed = attempt again prepared.ClientId "old-hand" "old@example.com" true "New Name"
            Assert.Contains("msg=account-not-allowed", landed.Location)

            let! after = row ()
            Assert.Equal(before, after)
            Assert.False(again.Cookies.ContainsKey Sessions.CookieName)
            do! expectCount app "SELECT count(*) FROM users" 1
        })

// ---------------------------------------------------------------------------
// The instance list, and the connector's list replacing it (US2, sign-in half)
// ---------------------------------------------------------------------------

[<Fact>]
let ``the instance list applies to a connector without a list of its own`` () =
    withApp [] [ "acme.com" ] (fun app browser prepared ->
        task {
            let! landed = signIn browser prepared.ClientId "bob" "bob@gmail.com" true
            Assert.Contains("msg=account-not-allowed", landed.Location)

            let! events = failures app
            let detail, _, _, _ = Assert.Single events
            Assert.Equal("email_domain_refused;reason=domain_not_allowed;rule=instance;stage=callback;domain=gmail.com", detail)

            let ann = Journeys.Browser app.Client
            let! admitted = signIn ann prepared.ClientId "ann" "ann@acme.com" true
            do! admittedCode ann admitted
        })

[<Fact>]
let ``a connector's own list replaces the instance list for that connector`` () =
    withApp [ "example.org" ] [ "acme.com" ] (fun app browser prepared ->
        task {
            // acme.com is on the instance list, but this connector has its own.
            let! landed = signIn browser prepared.ClientId "ann" "ann@acme.com" true
            Assert.Contains("msg=account-not-allowed", landed.Location)

            let! events = failures app
            let detail, _, _, _ = Assert.Single events
            Assert.Equal("email_domain_refused;reason=domain_not_allowed;rule=connector;stage=callback;domain=acme.com", detail)

            let carol = Journeys.Browser app.Client
            let! admitted = signIn carol prepared.ClientId "carol" "carol@example.org" true
            do! admittedCode carol admitted
        })

[<Fact>]
let ``a change to a list is in effect on the next sign-in with no restart`` () =
    withApp [] [] (fun app browser prepared ->
        task {
            let! open' = signIn browser prepared.ClientId "u1" "bob@gmail.com" true
            do! admittedCode browser open'

            do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "acme.com" ]
            let second = Journeys.Browser app.Client
            let! refused = signIn second prepared.ClientId "u2" "bob@gmail.com" true
            Assert.Contains("msg=account-not-allowed", refused.Location)

            do! EmailDomainRules.setInstanceList app.DbConfig app.Clock []
            let third = Journeys.Browser app.Client
            let! again = signIn third prepared.ClientId "u3" "bob@gmail.com" true
            do! admittedCode third again
        })

// ---------------------------------------------------------------------------
// EmailDomainRules: what applies, read from the database
// ---------------------------------------------------------------------------

let private connectorWith (app: Fixtures.TestApp) (list: string list) =
    task {
        let! stored = ConnectorStore.findBySlug app.DbConfig "fake"
        return { stored.Value with AllowedEmailDomains = list }
    }

[<Fact>]
let ``the instance list is stored as one setting and removed when emptied`` () =
    task {
        use! app = Fixtures.createApp ()
        let config = app.DbConfig

        let! none = EmailDomainRules.instanceList config
        Assert.Empty none

        do! EmailDomainRules.setInstanceList config app.Clock [ "acme.com"; "xn--mnchen-3ya.de" ]
        let! stored = Flows.scalar app "SELECT value FROM settings WHERE key = 'allowed_email_domains'" []
        Assert.Equal("acme.com xn--mnchen-3ya.de", stored)

        let! read = EmailDomainRules.instanceList config
        Assert.Equal<string list>([ "acme.com"; "xn--mnchen-3ya.de" ], read)

        // A second save replaces the first, in one row.
        do! EmailDomainRules.setInstanceList config app.Clock [ "example.org" ]
        do! expectCount app "SELECT count(*) FROM settings WHERE key = 'allowed_email_domains'" 1

        do! EmailDomainRules.setInstanceList config app.Clock []
        do! expectCount app "SELECT count(*) FROM settings WHERE key = 'allowed_email_domains'" 0
        let! gone = EmailDomainRules.instanceList config
        Assert.Empty gone
    }

[<Fact>]
let ``ruleFor gives the connector's list, else the instance's, else none`` () =
    task {
        use! app = Fixtures.createAppWith Journeys.fakeSettings ignore
        let browser = Journeys.Browser app.Client
        do! Journeys.claimInstance app browser
        do! Journeys.addFakeConnector app "unused"
        let config = app.DbConfig

        let! plain = connectorWith app []
        let! limited = connectorWith app [ "acme.com" ]

        let! nothing = EmailDomainRules.ruleFor config plain
        Assert.Equal(EmailDomains.NoRule, nothing)

        let! own = EmailDomainRules.ruleFor config limited
        Assert.Equal(EmailDomains.Rule(EmailDomains.Connector, [ "acme.com" ]), own)

        do! EmailDomainRules.setInstanceList config app.Clock [ "example.org" ]

        let! inherited = EmailDomainRules.ruleFor config plain
        Assert.Equal(EmailDomains.Rule(EmailDomains.Instance, [ "example.org" ]), inherited)

        let! stillOwn = EmailDomainRules.ruleFor config limited
        Assert.Equal(EmailDomains.Rule(EmailDomains.Connector, [ "acme.com" ]), stillOwn)

        let! instanceOnly = EmailDomainRules.instanceRule config
        Assert.Equal(EmailDomains.Rule(EmailDomains.Instance, [ "example.org" ]), instanceOnly)
    }

[<Fact>]
let ``anyListSet sees the instance list and any connector's list`` () =
    task {
        use! app = Fixtures.createAppWith Journeys.fakeSettings ignore
        let browser = Journeys.Browser app.Client
        do! Journeys.claimInstance app browser
        do! Journeys.addFakeConnector app "unused"
        let config = app.DbConfig

        let! none = EmailDomainRules.anyListSet config
        Assert.False none

        do! EmailDomainRules.setInstanceList config app.Clock [ "acme.com" ]
        let! instance = EmailDomainRules.anyListSet config
        Assert.True instance

        do! EmailDomainRules.setInstanceList config app.Clock []
        let! limited = connectorWith app [ "acme.com" ]
        do! ConnectorStore.save config app.Clock limited None
        let! connector = EmailDomainRules.anyListSet config
        Assert.True connector
    }

[<Fact>]
let ``a connector's list survives being read back and a card save without the field`` () =
    task {
        use! app = Fixtures.createAppWith Journeys.fakeSettings ignore
        let browser = Journeys.Browser app.Client
        do! Journeys.claimInstance app browser
        do! Journeys.addFakeConnector app "unused"

        let! limited = connectorWith app [ "acme.com"; "xn--mnchen-3ya.de" ]
        do! ConnectorStore.save app.DbConfig app.Clock limited None

        let! read = ConnectorStore.findBySlug app.DbConfig "fake"
        Assert.Equal<string list>([ "acme.com"; "xn--mnchen-3ya.de" ], read.Value.AllowedEmailDomains)

        // `update` with None keeps it; Some replaces it; Some [] clears it.
        let! _ = ConnectorStore.update app.DbConfig app.Clock "fake" "Fake" "fake" true None None
        let! kept = ConnectorStore.findBySlug app.DbConfig "fake"
        Assert.Equal<string list>([ "acme.com"; "xn--mnchen-3ya.de" ], kept.Value.AllowedEmailDomains)

        let! _ = ConnectorStore.update app.DbConfig app.Clock "fake" "Fake" "fake" true (Some [ "example.org" ]) None
        let! replaced = ConnectorStore.findBySlug app.DbConfig "fake"
        Assert.Equal<string list>([ "example.org" ], replaced.Value.AllowedEmailDomains)

        let! _ = ConnectorStore.update app.DbConfig app.Clock "fake" "Fake" "fake" true (Some []) None
        let! cleared = ConnectorStore.findBySlug app.DbConfig "fake"
        Assert.Empty cleared.Value.AllowedEmailDomains
    }

// ---------------------------------------------------------------------------
// The session's domain claim
// ---------------------------------------------------------------------------

[<Fact>]
let ``currentEmailDomain reads the claim and is none without one`` () =
    let withClaim = ClaimsIdentity(Sessions.Scheme)
    withClaim.AddClaim(Claim(Sessions.EmailDomainClaim, "acme.com"))
    Assert.Equal(Some "acme.com", Sessions.currentEmailDomain (ClaimsPrincipal withClaim))

    // A session from before this feature has none.
    Assert.Equal(None, Sessions.currentEmailDomain (ClaimsPrincipal(ClaimsIdentity(Sessions.Scheme))))

// ---------------------------------------------------------------------------
// Tightening a rule locks out the people already signed in (US3)
// ---------------------------------------------------------------------------

[<Literal>]
let private OfflineScopes = "openid email profile offline_access"

let private setConnectorList (app: Fixtures.TestApp) (list: string list) =
    task {
        let! stored = ConnectorStore.findBySlug app.DbConfig "fake"
        do! ConnectorStore.save app.DbConfig app.Clock { stored.Value with AllowedEmailDomains = list } None
    }

/// A sign-in that ends in an authorization code, plus the tokens it buys when
/// redeemed at once.
let private tokensFor (app: Fixtures.TestApp) (browser: Journeys.Browser) (prepared: Journeys.Prepared) (email: string) =
    task {
        let! signedIn = Journeys.signInThroughFake browser prepared.ClientId OfflineScopes "ann" email
        let! response = Journeys.exchangeCode app.Client prepared.ClientId prepared.ClientSecret signedIn.Code
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        return! Journeys.json response
    }

let private refreshWith (app: Fixtures.TestApp) (prepared: Journeys.Prepared) (token: string) =
    Journeys.refresh app.Client prepared.ClientId prepared.ClientSecret token

let private expectInvalidGrant (response: HttpResponseMessage) =
    task {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        let! body = Journeys.json response
        Assert.Equal("invalid_grant", Journeys.strOf body "error")
    }

let private userRow (app: Fixtures.TestApp) =
    Flows.scalar app "SELECT id || '|' || display_name || '|' || is_enabled || '|' || last_seen_utc FROM users" []

let private userId (app: Fixtures.TestApp) =
    Flows.scalar app "SELECT id FROM users" []

[<Fact>]
let ``after tightening, the session is signed out at authorize and sent to sign in`` () =
    withApp [] [] (fun app browser prepared ->
        task {
            let! first = signIn browser prepared.ClientId "bob" "bob@gmail.com" true
            do! admittedCode browser first
            let! before = userRow app

            do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "acme.com" ]

            let! response = browser.Get(Journeys.authorizeUrl prepared.ClientId "openid")
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
            Assert.StartsWith("/signin?returnUrl=", response.Headers.Location |> string)

            // The cookie was removed, not just ignored.
            Assert.Equal("", browser.Cookies[Sessions.CookieName])

            let! events = failures app
            let detail, subject, _, clientId = Assert.Single events
            Assert.Equal("email_domain_refused;reason=domain_not_allowed;rule=instance;stage=authorize;domain=gmail.com", detail)
            let! expectedUser = userId app
            Assert.Equal(expectedUser, subject)
            Assert.Equal(prepared.ClientId, clientId)

            // Nothing about the user changed: they are still enabled.
            let! after = userRow app
            Assert.Equal(before, after)
        })

[<Fact>]
let ``after tightening, prompt=none gets login_required and the session is gone`` () =
    withApp [] [] (fun app browser prepared ->
        task {
            let! first = signIn browser prepared.ClientId "bob" "bob@gmail.com" true
            do! admittedCode browser first
            do! setConnectorList app [ "acme.com" ]

            let! response = browser.Get(Journeys.authorizeUrl prepared.ClientId "openid" + "&prompt=none")
            let location = response.Headers.Location |> string
            Assert.StartsWith(Journeys.RedirectUri, location)
            Assert.Contains("error=login_required", location)
            Assert.Equal("", browser.Cookies[Sessions.CookieName])

            let! events = failures app
            let detail, _, _, _ = Assert.Single events
            Assert.Equal("email_domain_refused;reason=domain_not_allowed;rule=connector;stage=authorize;domain=gmail.com", detail)
        })

[<Fact>]
let ``a session still admitted by the tightened rule is honoured`` () =
    withApp [] [] (fun app browser prepared ->
        task {
            let! first = signIn browser prepared.ClientId "ann" "ann@acme.com" true
            do! admittedCode browser first
            do! setConnectorList app [ "acme.com" ]

            let! response = browser.Get(Journeys.authorizeUrl prepared.ClientId "openid")
            Assert.Contains("code=", response.Headers.Location |> string)
            Assert.True(browser.Cookies[Sessions.CookieName] <> "")
            let! events = failures app
            Assert.Empty events
        })

[<Fact>]
let ``a session admitted without an email is refused only while a rule applies`` () =
    withApp [] [] (fun app browser prepared ->
        task {
            // No rule: an unverified address gets in, and the session has no
            // email_domain claim, as every session did before this feature.
            let! first = signIn browser prepared.ClientId "bob" "bob@gmail.com" false
            do! admittedCode browser first
            Assert.Equal(None, Sessions.currentEmailDomain (sessionOf app browser))

            let! stillHonoured = browser.Get(Journeys.authorizeUrl prepared.ClientId "openid")
            Assert.Contains("code=", stillHonoured.Headers.Location |> string)

            // A rule appears: the same session no longer counts.
            do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "acme.com" ]
            let! refused = browser.Get(Journeys.authorizeUrl prepared.ClientId "openid")
            Assert.StartsWith("/signin?returnUrl=", refused.Headers.Location |> string)

            let! events = failures app
            let detail, _, _, _ = Assert.Single events
            Assert.Equal("email_domain_refused;reason=no_email;rule=instance;stage=authorize;domain=-", detail)
        })

[<Fact>]
let ``after tightening, a refresh is invalid_grant and the user is untouched`` () =
    withApp [] [] (fun app browser prepared ->
        task {
            let! tokens = tokensFor app browser prepared "bob@gmail.com"
            let! before = userRow app

            do! setConnectorList app [ "acme.com" ]

            let! response = refreshWith app prepared (Journeys.strOf tokens "refresh_token")
            do! expectInvalidGrant response

            let! events = failures app
            let detail, subject, connector, clientId = Assert.Single events
            Assert.Equal("email_domain_refused;reason=domain_not_allowed;rule=connector;stage=token;domain=gmail.com", detail)
            let! expectedUser = userId app
            Assert.Equal(expectedUser, subject)
            Assert.Equal("fake", connector)
            Assert.Equal(prepared.ClientId, clientId)

            let! after = userRow app
            Assert.Equal(before, after)
        })

[<Fact>]
let ``a code issued before the tightening is refused when redeemed`` () =
    withApp [] [] (fun app browser prepared ->
        task {
            let! signedIn = Journeys.signInThroughFake browser prepared.ClientId "openid" "bob" "bob@gmail.com"
            do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "acme.com" ]

            let! response = Journeys.exchangeCode app.Client prepared.ClientId prepared.ClientSecret signedIn.Code
            do! expectInvalidGrant response

            let! events = failures app
            let detail, _, _, _ = Assert.Single events
            Assert.Equal("email_domain_refused;reason=domain_not_allowed;rule=instance;stage=token;domain=gmail.com", detail)
        })

[<Fact>]
let ``the connector and domain claims ride every refresh, so a later tightening reaches the third token`` () =
    withApp [ "gmail.com" ] [ "example.org" ] (fun app browser prepared ->
        task {
            let! tokens = tokensFor app browser prepared "bob@gmail.com"

            // Code -> refresh -> refresh, all admitted.
            let! first = refreshWith app prepared (Journeys.strOf tokens "refresh_token")
            Assert.Equal(HttpStatusCode.OK, first.StatusCode)
            let! firstBody = Journeys.json first

            let! second = refreshWith app prepared (Journeys.strOf firstBody "refresh_token")
            Assert.Equal(HttpStatusCode.OK, second.StatusCode)
            let! secondBody = Journeys.json second

            // Tightened on that connector only. Were the connector claim lost
            // along the way, the refusal would be rule=instance with
            // reason=no_email; were the domain claim lost, reason=no_email.
            do! setConnectorList app [ "acme.com" ]
            let! third = refreshWith app prepared (Journeys.strOf secondBody "refresh_token")
            do! expectInvalidGrant third

            let! events = failures app
            let detail, _, _, _ = Assert.Single events
            Assert.Equal("email_domain_refused;reason=domain_not_allowed;rule=connector;stage=token;domain=gmail.com", detail)
        })

[<Fact>]
let ``a refresh still admitted by the tightened rule works`` () =
    withApp [] [] (fun app browser prepared ->
        task {
            let! tokens = tokensFor app browser prepared "ann@acme.com"
            do! setConnectorList app [ "acme.com" ]

            let! response = refreshWith app prepared (Journeys.strOf tokens "refresh_token")
            Assert.Equal(HttpStatusCode.OK, response.StatusCode)
            let! events = failures app
            Assert.Empty events
        })

[<Fact>]
let ``a refresh whose connector was deleted is judged by the instance list alone`` () =
    withApp [] [] (fun app browser prepared ->
        task {
            let! tokens = tokensFor app browser prepared "ann@acme.com"
            let! _ = ConnectorStore.delete app.DbConfig "fake"

            // No list anywhere: honoured, as before this feature.
            let! open' = refreshWith app prepared (Journeys.strOf tokens "refresh_token")
            Assert.Equal(HttpStatusCode.OK, open'.StatusCode)
            let! openBody = Journeys.json open'

            // The instance list admits the domain: still honoured.
            do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "acme.com" ]
            let! admitted = refreshWith app prepared (Journeys.strOf openBody "refresh_token")
            Assert.Equal(HttpStatusCode.OK, admitted.StatusCode)
            let! admittedBody = Journeys.json admitted

            // The instance list does not: refused, and the event says instance.
            do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "example.org" ]
            let! refused = refreshWith app prepared (Journeys.strOf admittedBody "refresh_token")
            do! expectInvalidGrant refused

            let! events = failures app
            let detail, _, _, _ = Assert.Single events
            Assert.Equal("email_domain_refused;reason=domain_not_allowed;rule=instance;stage=token;domain=acme.com", detail)
        })

[<Fact>]
let ``a grant with no connector claim is judged by whether any list is set`` () =
    task {
        use! app = Fixtures.createAppWith Journeys.fakeSettings ignore
        let browser = Journeys.Browser app.Client
        do! Journeys.claimInstance app browser
        do! Journeys.addFakeConnector app "unused"
        let config = app.DbConfig

        // The shape of a refresh token issued before this feature: no connector
        // and no domain. Nothing limits sign-in, so it is honoured.
        let! plain = EmailDomainRules.recheck config None None
        Assert.Equal(EmailDomains.Admitted, plain)

        // An instance list, or a list on any connector, closes it.
        do! EmailDomainRules.setInstanceList config app.Clock [ "acme.com" ]
        let! instance = EmailDomainRules.recheck config None None
        Assert.Equal(EmailDomains.Refused(EmailDomains.Instance, EmailDomains.NoEmail), instance)

        do! EmailDomainRules.setInstanceList config app.Clock []
        do! setConnectorList app [ "acme.com" ]
        let! connector = EmailDomainRules.recheck config None None
        Assert.Equal(EmailDomains.Refused(EmailDomains.Instance, EmailDomains.NoEmail), connector)
    }

/// Lets a test read the access token the way a resource server would: a
/// request to `/test/access-claims` answers with the claim types OpenIddict's
/// validation finds in the bearer token.
type private AccessClaimsProbe() =
    interface Microsoft.AspNetCore.Hosting.IStartupFilter with
        member _.Configure(next) =
            Action<Microsoft.AspNetCore.Builder.IApplicationBuilder>(fun app ->
                // OpenIddict's validation needs the authentication middleware
                // to have run before it can read a bearer token.
                Microsoft.AspNetCore.Builder.AuthAppBuilderExtensions.UseAuthentication app |> ignore

                app.Use(
                    Func<Microsoft.AspNetCore.Http.RequestDelegate, Microsoft.AspNetCore.Http.RequestDelegate>(fun following ->
                        Microsoft.AspNetCore.Http.RequestDelegate(fun ctx ->
                            if ctx.Request.Path = Microsoft.AspNetCore.Http.PathString "/test/access-claims" then
                                task {
                                    let! result =
                                        Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.AuthenticateAsync(
                                            ctx,
                                            OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme
                                        )

                                    let types =
                                        match Option.ofObj result.Principal with
                                        | Some principal -> principal.Claims |> Seq.map _.Type |> String.concat " "
                                        | None -> "unauthenticated"

                                    do! Microsoft.AspNetCore.Http.HttpResponseWritingExtensions.WriteAsync(ctx.Response, types)
                                }
                                :> Task
                            else
                                following.Invoke ctx))
                )
                |> ignore

                next.Invoke app)

[<Fact>]
let ``the private claims never reach an access token, an id token or userinfo`` () =
    task {
        use! app =
            Fixtures.createAppWith Journeys.fakeSettings (fun builder ->
                builder.Services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, AccessClaimsProbe>()
                |> ignore)

        let browser = Journeys.Browser app.Client
        let! prepared = Journeys.prepare app browser
        do! setConnectorList app [ "gmail.com" ]
        let person = Journeys.Browser app.Client
        let! first = tokensFor app person prepared "bob@gmail.com"

        // The first tokens, and the ones a refresh gives.
        let! refreshed = refreshWith app prepared (Journeys.strOf first "refresh_token")
        let! second = Journeys.json refreshed

        for tokens in [ first; second ] do
            let idToken = Journeys.strOf tokens "id_token"
            let payload = (Journeys.segmentOf 1 idToken).GetRawText()
            Assert.Contains("\"sub\"", payload)
            Assert.DoesNotContain("umbral_", payload)

            let accessToken = Journeys.strOf tokens "access_token"

            let probe = new HttpRequestMessage(HttpMethod.Get, "/test/access-claims")
            probe.Headers.Add("Authorization", "Bearer " + accessToken)
            let! probed = app.Client.SendAsync probe
            let! claimTypes = probed.Content.ReadAsStringAsync()
            // Not vacuous: the token validated and carries the profile claims.
            Assert.Contains("email", claimTypes)
            Assert.DoesNotContain("umbral_", claimTypes)

            let! info = Journeys.userInfo app.Client accessToken
            let! body = info.Content.ReadAsStringAsync()
            Assert.Equal(HttpStatusCode.OK, info.StatusCode)
            Assert.DoesNotContain("umbral_", body)
            Assert.DoesNotContain("gmail.com", body.Replace("bob@gmail.com", ""))
    }

// ---------------------------------------------------------------------------
// Google's hosted-domain hint at the start of a sign-in (US5)
// ---------------------------------------------------------------------------

/// A Google connector alongside the fake one, started through the real endpoint.
/// Resolving Google's endpoints needs no network, so the redirect is the adapter's
/// own authorize URL.
let private googleStartUrl (connectorList: string list) (instanceList: string list) =
    task {
        use! app = Fixtures.createAppWith Journeys.fakeSettings ignore
        let browser = Journeys.Browser app.Client
        do! Journeys.claimInstance app browser
        let! reference = Fixtures.putSecret app "google" "google-client-secret"

        do!
            ConnectorStore.save
                app.DbConfig
                app.Clock
                { Slug = "google"
                  Kind = Connectors.ProviderKind.Google
                  DisplayName = "Google"
                  ClientId = "google-client-id"
                  ClientSecretRef = reference.Encoded
                  ClientSecretHint = Secrets.hint "google-client-secret"
                  IssuerUrl = None
                  AllowedEmailDomains = connectorList
                  PasskeyEnrolment = Connectors.InvitationsOnly
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None

        if not (List.isEmpty instanceList) then
            do! EmailDomainRules.setInstanceList app.DbConfig app.Clock instanceList

        let person = Journeys.Browser app.Client
        let! response = person.Get "/connect/google/start"
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        let location = response.Headers.Location |> string
        Assert.StartsWith("https://accounts.google.com/", location)
        return location
    }

[<Fact>]
let ``a Google connector limited to one domain starts with the hd hint`` () =
    task {
        let! url = googleStartUrl [ "acme.com" ] []
        Assert.Contains("&hd=acme.com", url)
    }

[<Fact>]
let ``a Google connector limited to one domain by the instance list starts with the hd hint`` () =
    task {
        let! url = googleStartUrl [] [ "acme.com" ]
        Assert.Contains("&hd=acme.com", url)
    }

[<Fact>]
let ``a Google connector with two domains starts with no hd`` () =
    task {
        let! url = googleStartUrl [ "acme.com"; "example.org" ] []
        Assert.DoesNotContain("hd=", url)

        let! byInstance = googleStartUrl [] [ "acme.com"; "example.org" ]
        Assert.DoesNotContain("hd=", byInstance)
    }

[<Fact>]
let ``a Google connector's own list replaces a one-domain instance list for the hint`` () =
    task {
        let! url = googleStartUrl [ "example.org"; "corp.net" ] [ "acme.com" ]
        Assert.DoesNotContain("hd=", url)
    }

[<Fact>]
let ``a Google connector with no list starts with no hd`` () =
    task {
        let! url = googleStartUrl [] []
        Assert.DoesNotContain("hd=", url)
    }

[<Fact>]
let ``a connector that is not Google is not sent hd whatever its list`` () =
    withApp [ "acme.com" ] [] (fun _ browser prepared ->
        task {
            let! toPicker = browser.Get(Journeys.authorizeUrl prepared.ClientId "openid")
            let picker = toPicker.Headers.Location |> string
            let! toProvider = browser.Get(picker.Replace("/signin", "/connect/fake/start"))
            Assert.DoesNotContain("hd=", toProvider.Headers.Location |> string)
        })

// ---------------------------------------------------------------------------
// The rule is the connector's, not the person's (US3.4)
// ---------------------------------------------------------------------------

/// A second connector of kind Fake, shared and enabled, with no list of its own.
let private addOpenConnector (app: Fixtures.TestApp) (slug: string) =
    task {
        let! reference = Fixtures.putSecret app slug "open-connector-secret"

        do!
            ConnectorStore.save
                app.DbConfig
                app.Clock
                { Slug = slug
                  Kind = Connectors.ProviderKind.Fake
                  DisplayName = "Open"
                  ClientId = slug
                  ClientSecretRef = reference.Encoded
                  ClientSecretHint = Secrets.hint "open-connector-secret"
                  IssuerUrl = None
                  AllowedEmailDomains = []
                  PasskeyEnrolment = Connectors.InvitationsOnly
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None
    }

/// `fake` limited to acme.com, `open` with no list, and no instance list.
let private withTwoConnectors (body: Fixtures.TestApp -> Journeys.Prepared -> Task<unit>) =
    withApp [ "acme.com" ] [] (fun app _ prepared ->
        task {
            do! addOpenConnector app "open"
            do! body app prepared
        })

[<Fact>]
let ``a person refused by one connector gets in through another, as the same user`` () =
    withTwoConnectors (fun app prepared ->
        task {
            // Through the open connector: admitted.
            let first = Journeys.Browser app.Client
            let! admitted = attemptVia "open" first prepared.ClientId "bob" "bob@gmail.com" true "Test User"
            do! admittedCode first admitted
            do! expectCount app "SELECT count(*) FROM users" 1
            let! id = userId app
            let! before = userRow app

            // Through the limited one: refused, and the user is left alone.
            let second = Journeys.Browser app.Client
            let! refused = attemptVia "fake" second prepared.ClientId "bob" "bob@gmail.com" true "Test User"
            Assert.StartsWith("/signin?", refused.Location)
            Assert.Contains("msg=account-not-allowed", refused.Location)
            Assert.False(second.Cookies.ContainsKey Sessions.CookieName)

            let! events = failures app
            let detail, userOfEvent, connector, _ = Assert.Single events
            Assert.Equal("email_domain_refused;reason=domain_not_allowed;rule=connector;stage=callback;domain=gmail.com", detail)
            Assert.Equal("", userOfEvent)
            Assert.Equal("fake", connector)

            do! expectCount app "SELECT count(*) FROM users" 1
            let! after = userRow app
            Assert.Equal(before, after)

            // Through the open one again: the same user, not a second.
            let third = Journeys.Browser app.Client
            let! again = attemptVia "open" third prepared.ClientId "bob" "bob@gmail.com" true "Test User"
            do! admittedCode third again
            do! expectCount app "SELECT count(*) FROM users" 1
            let! sameId = userId app
            Assert.Equal(id, sameId)
            do! expectCount app "SELECT count(*) FROM linked_accounts" 1
        })

[<Fact>]
let ``a person admitted by the limited connector is the same user through the open one`` () =
    withTwoConnectors (fun app prepared ->
        task {
            let first = Journeys.Browser app.Client
            let! admitted = attemptVia "fake" first prepared.ClientId "ann" "ann@acme.com" true "Test User"
            do! admittedCode first admitted
            let! id = userId app

            let second = Journeys.Browser app.Client
            let! other = attemptVia "open" second prepared.ClientId "ann" "ann@acme.com" true "Test User"
            do! admittedCode second other

            do! expectCount app "SELECT count(*) FROM users" 1
            let! sameId = userId app
            Assert.Equal(id, sameId)
            let! noFailures = failures app
            Assert.Empty noFailures
        })

[<Fact>]
let ``limiting one connector does not touch a session established through another`` () =
    withApp [] [] (fun app _ prepared ->
        task {
            do! addOpenConnector app "open"

            // The session comes through the open connector, while neither has a list.
            let browser = Journeys.Browser app.Client
            let! admitted = attemptVia "open" browser prepared.ClientId "bob" "bob@gmail.com" true "Test User"
            do! admittedCode browser admitted

            // Now `fake` is limited. `open` still has no list and there is no
            // instance list, so this session's rule has not changed.
            do! setConnectorList app [ "acme.com" ]

            let! response = browser.Get(Journeys.authorizeUrl prepared.ClientId "openid")
            Assert.Contains("code=", response.Headers.Location |> string)
            Assert.True(browser.Cookies[Sessions.CookieName] <> "")

            let! events = failures app
            Assert.Empty events
        })

[<Fact>]
let ``a passkey session and token are never refused by email-domain rules`` () =
    task {
        use! app = Fixtures.createAppWith Journeys.fakeSettings ignore
        let operator = Flows.Browser app.Client
        do! Flows.claimOperator app operator
        do! PasskeyFlows.enablePasskeyConnector operator "invitations"
        let! clientId, secret = Flows.registerApp operator "Grafana"
        let authenticator = SoftwareAuthenticator.SoftwareAuthenticator("umbral.test", Fixtures.TestOrigin)
        let! link = PasskeyFlows.inviteNewPerson operator "Ana"
        let ana = Flows.Browser app.Client

        let! registered, _ =
            PasskeyFlows.registerThroughInvitation ana authenticator (PasskeyFlows.secretOf link) (Some "Ana") "Phone"

        Assert.Equal(HttpStatusCode.OK, registered.StatusCode)

        // Registration started her session: authorize goes straight to the app.
        let! authorized = ana.Get(Journeys.authorizeUrl clientId "openid offline_access")
        let code = Uri.UnescapeDataString(Regex.Match(authorized.Headers.Location |> string, "code=([^&]+)").Groups[1].Value)
        let! exchanged = Flows.exchangeCode app clientId secret code
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode)
        let! tokens = Journeys.json exchanged

        do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "acme.com" ]

        // (a) the existing session still authorizes
        let! again = ana.Get(Journeys.authorizeUrl clientId "openid")
        Assert.Contains("code=", again.Headers.Location |> string)

        // (b) the refresh token still works
        let! refreshed = Journeys.refresh app.Client clientId secret (Journeys.strOf tokens "refresh_token")
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode)

        // (c) a fresh passkey sign-in succeeds
        let fresh = Flows.Browser app.Client
        let! returnUrl2, _, token2 = PasskeyFlows.startPasskeySignIn fresh clientId "openid"
        let credentialId = (List.head authenticator.Credentials).Id
        let! response, _ = PasskeyFlows.signInWithPasskey fresh authenticator credentialId returnUrl2 token2
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)

        let! refused = Flows.scalar app "SELECT count(*) FROM security_events WHERE event_type = 'sign_in_failed' AND detail LIKE 'email_domain_refused%'" []
        Assert.Equal("0", refused)
    }
