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
let private attempt
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
        let! toProvider = browser.Get(picker.Replace("/signin", "/connect/fake/start"))

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
