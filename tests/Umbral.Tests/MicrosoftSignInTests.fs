/// Sign in with Microsoft through the real pipeline, against a fake Microsoft
/// (specs/016-microsoft-connector, SC-001 and the Edge Cases).
///
/// Every test starts at the app's authorization request, goes through the
/// sign-in page's Microsoft button, plays Microsoft's page with `FakeMicrosoft`
/// and calls Umbral's callback as the browser would. Only the network is
/// replaced: the adapter, the discovery cache, the correlation cookie, the token
/// validation and the user store are the product's own.
///
/// What is held to account most closely is who is let in (the choice of audience,
/// the issuer rules, the key's issuer), who a person is (`tid#oid`, not `sub`)
/// and `email_verified` (task h762: account linking rests on it). A refusal ends
/// at the sign-in page with no session, no user and a reason in the log, and no
/// token, secret, address or name is in the log.
module Umbral.Tests.MicrosoftSignInTests

open System
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading.Tasks
open Microsoft.IdentityModel.Tokens
open Xunit
open Umbral.Domains.Connectors
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.StubOidcProvider
open Umbral.Tests.Infrastructure.Journeys

module Ms = Umbral.Domains.Microsoft

// ---------------------------------------------------------------------------
// The world of a test
// ---------------------------------------------------------------------------

/// An app with the fake Microsoft behind its provider calls, the Microsoft
/// connector seeded for one audience, and one registered app to sign in to.
type private World =
    { App: Fixtures.TestApp
      Fake: FakeMicrosoft.FakeMicrosoft
      ClientId: string
      ClientSecret: string
      /// Every authorization code the fake's page handed out, for the search of
      /// the log.
      Codes: System.Collections.Generic.List<string> }

/// The four choices, by the label a theory can carry.
let private audienceOf (label: string) : MicrosoftAudience =
    match label with
    | "personal" -> PersonalAccounts
    | "one" -> OneOrganization(Guid.Parse FakeMicrosoft.WorkTenantA)
    | "any" -> AnyOrganization
    | "both" -> PersonalAndAnyOrganization
    | other -> failwith $"no such audience: {other}"

/// A tenant by label.
let private tenantOf (label: string) : string =
    match label with
    | "personal" -> FakeMicrosoft.PersonalTenant
    | "work-a" -> FakeMicrosoft.WorkTenantA
    | "work-b" -> FakeMicrosoft.WorkTenantB
    | other -> failwith $"no such tenant: {other}"

/// The usual person of each audience: one it admits.
let private usualTenant (audience: MicrosoftAudience) =
    match audience with
    | PersonalAccounts -> FakeMicrosoft.PersonalTenant
    | _ -> FakeMicrosoft.WorkTenantA

let private start (audience: MicrosoftAudience) =
    task {
        let fake = FakeMicrosoft.create audience
        let! app = Fixtures.createAppWithProviderHandler fake.Handler fakeSettings ignore
        FakeMicrosoft.attach app fake

        let operator = Browser app.Client
        let! prepared = prepare app operator
        do! FakeMicrosoft.seedConnector app audience

        // The usual person of this audience; a test that wants another says so.
        fake.Asserts(usualTenant audience, FakeMicrosoft.Oid, email = FakeMicrosoft.Email)

        return
            { App = app
              Fake = fake
              ClientId = prepared.ClientId
              ClientSecret = prepared.ClientSecret
              Codes = System.Collections.Generic.List<string>() }
    }

let private location (response: HttpResponseMessage) = response.Headers.Location |> string

let private newBrowser (world: World) = Browser world.App.Client

/// What the browser has seen of Microsoft's side of a sign-in so far.
type private Visit =
    { /// Where Umbral sent the browser.
      AuthorizeUri: Uri
      /// What Microsoft's page would send back.
      Authorization: Authorization }

/// From the app's authorization request, through the sign-in page's Microsoft
/// button, to Microsoft's page and the answer it would send. Nothing is sent back.
let private visit (browser: Browser) (world: World) =
    task {
        let! toPicker = browser.Get(authorizeUrl world.ClientId "openid email profile")
        let! toMicrosoft = browser.Get((location toPicker).Replace("/signin", "/connect/microsoft/start"))

        let authorizeUri = Uri(location toMicrosoft)
        let authorization = world.Fake.Approve authorizeUri
        world.Codes.Add authorization.Code

        return
            { AuthorizeUri = authorizeUri
              Authorization = authorization }
    }

/// Microsoft's redirect back to Umbral's callback, with the browser's cookies.
let private callback (browser: Browser) (authorization: Authorization) =
    browser.Get
        $"/connect/microsoft/callback?code={Uri.EscapeDataString authorization.Code}&state={Uri.EscapeDataString authorization.State}"

/// The authorization code the app is given, or where the browser landed instead,
/// from the answer to the callback.
let private codeFrom (browser: Browser) (back: HttpResponseMessage) =
    task {
        let landed = location back

        if not (landed.Contains "/connect/authorize") then
            return Error landed
        else
            let! issued = browser.Get landed
            let code = Regex.Match(location issued, "code=([^&]+)").Groups[1].Value
            return Ok(Uri.UnescapeDataString code)
    }

/// One whole sign-in through Microsoft, ending with Umbral's code for the app or
/// where the person landed instead.
let private signIn (browser: Browser) (world: World) =
    task {
        let! seen = visit browser world
        let! back = callback browser seen.Authorization
        return! codeFrom browser back
    }

let private signedIn (result: Result<string, string>) =
    match result with
    | Ok code -> code
    | Error landed -> failwith $"the sign-in did not reach the app: {landed}"

let private scalar (world: World) (statement: string) (parameters: (string * string) list) =
    Flows.scalar world.App statement parameters

let private count (world: World) (statement: string) =
    task {
        let! value = scalar world statement []
        return int value
    }

/// The Microsoft subjects linked so far, comma separated.
let private subjects (world: World) =
    scalar
        world
        "SELECT coalesce(group_concat(provider_subject, ','), '') FROM linked_accounts WHERE provider_kind = 'microsoft'"
        []

/// The user Umbral made for a Microsoft subject: name, email and the verified
/// flag.
let private userOf (world: World) (subject: string) =
    scalar
        world
        "SELECT u.display_name || '|' || coalesce(u.email, '') || '|' || u.email_verified FROM users u \
         JOIN linked_accounts l ON l.user_id = u.id WHERE l.provider_kind = 'microsoft' AND l.provider_subject = @s"
        [ "@s", subject ]

let private lastFailure (world: World) =
    scalar
        world
        "SELECT detail FROM security_events WHERE event_type = 'sign_in_failed' ORDER BY id DESC LIMIT 1"
        []

let private dumpEvents (world: World) =
    scalar
        world
        "SELECT coalesce(group_concat(occurred_utc || '|' || event_type || '|' || outcome || '|' || actor || '|' \
         || coalesce(user_id, '') || '|' || coalesce(connector_slug, '') || '|' || coalesce(client_id, '') || '|' \
         || coalesce(remote_ip, '') || '|' || coalesce(detail, ''), char(10)), '') FROM security_events"
        []

/// The sign-in page with the code the person sees, and no session: what every
/// refusal looks like to the person.
let private assertRefused (browser: Browser) (back: HttpResponseMessage) (code: string) =
    Assert.Equal(HttpStatusCode.Redirect, back.StatusCode)
    let landed = location back
    Assert.Contains("/signin", landed)
    Assert.Contains($"msg={code}", landed)
    Assert.False(browser.Cookies.ContainsKey Umbral.Infrastructure.Sessions.CookieName, "a session was started")

/// Nothing was made for a refused person: no user, no link.
let private assertNobodyMade (world: World) =
    task {
        let! linked = count world "SELECT count(*) FROM linked_accounts WHERE provider_kind = 'microsoft'"
        Assert.Equal(0, linked)
        let! created = count world "SELECT count(*) FROM security_events WHERE event_type = 'user_created'"
        Assert.Equal(0, created)
    }

/// No row of the log holds a code the fake issued, the `id_token` (whole or its
/// signature), the client secret, the person's address or name (FR-013).
let private assertNothingSensitiveLogged (world: World) =
    task {
        let! log = dumpEvents world

        let secrets =
            [ yield FakeMicrosoft.ClientSecret
              yield FakeMicrosoft.Email
              yield FakeMicrosoft.Name
              yield! world.Codes

              match world.Fake.Stub.LastIdToken with
              | Some idToken ->
                  yield idToken
                  yield idToken.Split('.')[2]
              | None -> () ]

        for secret in secrets do
            Assert.False(log.Contains secret, "something sensitive is in the security log")
    }

/// A sign-in that Umbral refuses as the person sees it (`code`), with the log's
/// detail (`detail`), no user and nothing sensitive in the log.
let private assertRefusedWith (world: World) (code: string) (detail: string) =
    task {
        let browser = newBrowser world
        let! seen = visit browser world
        let! back = callback browser seen.Authorization
        assertRefused browser back code

        let! logged = lastFailure world
        Assert.Equal(detail, logged)
        do! assertNobodyMade world
        do! assertNothingSensitiveLogged world
    }

/// What a token that fails Umbral's own validation is logged as.
let private invalidDetail (reason: string) = $"malformed_response: id_token_invalid: {reason}"

// ---------------------------------------------------------------------------
// Personal accounts
// ---------------------------------------------------------------------------

[<Fact>]
let ``a personal account signs in under personal accounts and is the person tid#oid`` () =
    task {
        let! world = start PersonalAccounts
        use _ = world.App
        use _ = world.Fake

        let! outcome = signIn (newBrowser world) world
        let code = signedIn outcome

        // The identity is the tenant and the object, lower case, under provider
        // `microsoft`; the pairwise `sub` is not part of it.
        let expected = $"{FakeMicrosoft.PersonalTenant}#{FakeMicrosoft.Oid}"
        Assert.Equal(expected, expected.ToLowerInvariant())
        let! linked = subjects world
        Assert.Equal(expected, linked)
        Assert.False(linked.Contains "pairwise")

        // The app gets the person through Umbral's own code, with the connector.
        let! exchanged = exchangeCode world.App.Client world.ClientId world.ClientSecret code
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode)
        let! tokens = json exchanged
        let payload = segmentOf 1 (strOf tokens "id_token")
        Assert.Equal("microsoft", strOf payload "idp")
        Assert.Equal(FakeMicrosoft.Email, strOf payload "email")
        Assert.Equal(FakeMicrosoft.Name, strOf payload "name")

        do! assertNothingSensitiveLogged world
    }

[<Fact>]
let ``the person's name falls back to preferred_username`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        world.Fake.Asserts(
            FakeMicrosoft.WorkTenantA,
            FakeMicrosoft.Oid,
            omit = [ "name" ],
            claims = [ "preferred_username", ("maria@contoso.example" :> obj) ]
        )

        let! outcome = signIn (newBrowser world) world
        signedIn outcome |> ignore

        let! stored = userOf world $"{FakeMicrosoft.WorkTenantA}#{FakeMicrosoft.Oid}"
        Assert.StartsWith("maria@contoso.example|", stored)
    }

// ---------------------------------------------------------------------------
// Who may sign in (SC-001; the choice of audience)
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("personal", "personal")>]
[<InlineData("any", "work-a")>]
[<InlineData("any", "work-b")>]
[<InlineData("one", "work-a")>]
[<InlineData("both", "personal")>]
[<InlineData("both", "work-a")>]
let ``an account the choice admits signs in as tid#oid`` (audience: string, tenant: string) =
    task {
        let! world = start (audienceOf audience)
        use _ = world.App
        use _ = world.Fake

        let tid = tenantOf tenant
        world.Fake.Asserts(tid, FakeMicrosoft.Oid, email = FakeMicrosoft.Email)

        let! outcome = signIn (newBrowser world) world
        signedIn outcome |> ignore

        let! linked = subjects world
        Assert.Equal($"{tid}#{FakeMicrosoft.Oid}", linked)
    }

[<Theory>]
[<InlineData("personal", "work-a")>]
[<InlineData("personal", "work-b")>]
[<InlineData("any", "personal")>]
[<InlineData("one", "work-b")>]
[<InlineData("one", "personal")>]
let ``an account the choice does not admit is refused as not allowed, and no one is made`` (audience: string, tenant: string) =
    task {
        let! world = start (audienceOf audience)
        use _ = world.App
        use _ = world.Fake

        let tid = tenantOf tenant
        world.Fake.Asserts(tid, FakeMicrosoft.Oid, email = FakeMicrosoft.Email)

        do! assertRefusedWith world "account-not-allowed" $"refused: tenant_not_allowed;tenant={tid}"
    }

[<Fact>]
let ``the app is not given a code for a person who was refused`` () =
    task {
        let! world = start PersonalAccounts
        use _ = world.App
        use _ = world.Fake

        world.Fake.Asserts(FakeMicrosoft.WorkTenantA, FakeMicrosoft.Oid, email = FakeMicrosoft.Email)

        let browser = newBrowser world
        let! outcome = signIn browser world

        match outcome with
        | Error landed -> Assert.Contains("msg=account-not-allowed", landed)
        | Ok _ -> failwith "a code was issued to a refused account"
    }

// ---------------------------------------------------------------------------
// The issuer (research R2)
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("personal")>]
[<InlineData("one")>]
[<InlineData("any")>]
[<InlineData("both")>]
let ``a token whose iss names another tenant than its own tid is refused under every choice`` (audience: string) =
    task {
        let audience = audienceOf audience
        let! world = start audience
        use _ = world.App
        use _ = world.Fake

        // The tid is one the choice admits; the iss is another tenant's.
        world.Fake.Asserts(
            usualTenant audience,
            FakeMicrosoft.Oid,
            email = FakeMicrosoft.Email,
            iss = Ms.issuerFor FakeMicrosoft.WorkTenantB
        )

        do! assertRefusedWith world "sign-in-failed" (invalidDetail "issuer")
    }

[<Fact>]
let ``a token whose iss is the template itself is refused`` () =
    task {
        let! world = start PersonalAndAnyOrganization
        use _ = world.App
        use _ = world.Fake

        world.Fake.Asserts(FakeMicrosoft.WorkTenantA, FakeMicrosoft.Oid, email = FakeMicrosoft.Email, iss = Ms.issuerTemplate)

        do! assertRefusedWith world "sign-in-failed" (invalidDetail "issuer")
    }

[<Theory>]
[<InlineData("missing")>]
[<InlineData("upper-case")>]
[<InlineData("not-a-guid")>]
[<InlineData("braces")>]
[<InlineData("a-number")>]
let ``a tid that is missing or not a canonical GUID is refused`` (label: string) =
    task {
        let! world = start PersonalAndAnyOrganization
        use _ = world.App
        use _ = world.Fake

        let fake = world.Fake
        let tenant = FakeMicrosoft.WorkTenantA

        match label with
        | "missing" -> fake.Asserts(tenant, FakeMicrosoft.Oid, email = FakeMicrosoft.Email, omit = [ "tid" ])
        | "upper-case" -> fake.Asserts(tenant.ToUpperInvariant(), FakeMicrosoft.Oid, email = FakeMicrosoft.Email)
        | "not-a-guid" -> fake.Asserts("not-a-tenant", FakeMicrosoft.Oid, email = FakeMicrosoft.Email)
        | "braces" -> fake.Asserts($"{{{tenant}}}", FakeMicrosoft.Oid, email = FakeMicrosoft.Email)
        | "a-number" -> fake.Asserts(tenant, FakeMicrosoft.Oid, email = FakeMicrosoft.Email, claims = [ "tid", (12345 :> obj) ])
        | other -> failwith $"no such case: {other}"

        do! assertRefusedWith world "sign-in-failed" (invalidDetail "tenant")
    }

// ---------------------------------------------------------------------------
// The signing key's issuer (research R2, "Validate the signing key issuer")
// ---------------------------------------------------------------------------

[<Fact>]
let ``a key issued to the personal tenant signs a personal account's token`` () =
    task {
        let! world = start PersonalAccounts
        use _ = world.App
        use _ = world.Fake

        let key = world.Fake.PublishPersonalKey()
        world.Fake.Asserts(FakeMicrosoft.PersonalTenant, FakeMicrosoft.Oid, email = FakeMicrosoft.Email, key = key)

        let! outcome = signIn (newBrowser world) world
        signedIn outcome |> ignore
    }

[<Fact>]
let ``a token signed by the personal tenant's key but carrying a work tenant is refused, though its iss fits its tid`` () =
    task {
        let! world = start PersonalAndAnyOrganization
        use _ = world.App
        use _ = world.Fake

        let key = world.Fake.PublishPersonalKey()
        world.Fake.Asserts(FakeMicrosoft.WorkTenantA, FakeMicrosoft.Oid, email = FakeMicrosoft.Email, key = key)

        do! assertRefusedWith world "sign-in-failed" (invalidDetail "key_issuer")
    }

[<Fact>]
let ``a key whose issuer is another tenant's is refused`` () =
    task {
        let! world = start PersonalAndAnyOrganization
        use _ = world.App
        use _ = world.Fake

        let key = world.Fake.PublishKey("key-b", Some(Ms.issuerFor FakeMicrosoft.WorkTenantB))
        world.Fake.Asserts(FakeMicrosoft.WorkTenantA, FakeMicrosoft.Oid, email = FakeMicrosoft.Email, key = key)

        do! assertRefusedWith world "sign-in-failed" (invalidDetail "key_issuer")
    }

[<Fact>]
let ``a key with no issuer member is refused`` () =
    task {
        let! world = start PersonalAndAnyOrganization
        use _ = world.App
        use _ = world.Fake

        world.Fake.Stub.KeyIssuers.Clear()

        do! assertRefusedWith world "sign-in-failed" (invalidDetail "key_issuer")
    }

[<Fact>]
let ``a key set where only another key carries an issuer does not vouch for the signing key`` () =
    task {
        let! world = start PersonalAndAnyOrganization
        use _ = world.App
        use _ = world.Fake

        // The signing key has none; a second key, unused, has the template.
        world.Fake.Stub.KeyIssuers.Clear()
        world.Fake.PublishKey("key-decoy", Some Ms.issuerTemplate) |> ignore

        do! assertRefusedWith world "sign-in-failed" (invalidDetail "key_issuer")
    }

// ---------------------------------------------------------------------------
// The identity: oid, and the nonce
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("missing")>]
[<InlineData("not-a-guid")>]
[<InlineData("upper-case")>]
[<InlineData("a-number")>]
let ``an oid that is missing or not a canonical GUID is refused`` (label: string) =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        let fake = world.Fake
        let tenant = FakeMicrosoft.WorkTenantA

        match label with
        | "missing" -> fake.Asserts(tenant, FakeMicrosoft.Oid, email = FakeMicrosoft.Email, omit = [ "oid" ])
        | "not-a-guid" -> fake.Asserts(tenant, "not-an-object", email = FakeMicrosoft.Email)
        | "upper-case" -> fake.Asserts(tenant, FakeMicrosoft.Oid.ToUpperInvariant(), email = FakeMicrosoft.Email)
        | "a-number" -> fake.Asserts(tenant, FakeMicrosoft.Oid, email = FakeMicrosoft.Email, claims = [ "oid", (7 :> obj) ])
        | other -> failwith $"no such case: {other}"

        do! assertRefusedWith world "sign-in-failed" (invalidDetail "subject")
    }

[<Theory>]
[<InlineData("another")>]
[<InlineData("absent")>]
let ``a token with a wrong or missing nonce is refused`` (label: string) =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        let nonce =
            match label with
            | "another" -> Replace "a-nonce-from-another-request"
            | _ -> Omit

        world.Fake.Stub.IdToken <- { world.Fake.Stub.IdToken with Nonce = nonce }

        do! assertRefusedWith world "sign-in-failed" (invalidDetail "nonce")
    }

[<Fact>]
let ``a token for another client is refused`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        world.Fake.Stub.IdToken <- { world.Fake.Stub.IdToken with Audience = Some [ FakeMicrosoft.ReplacedClientId ] }

        do! assertRefusedWith world "sign-in-failed" (invalidDetail "audience")
    }

[<Fact>]
let ``a token with no id_token is refused`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        world.Fake.Stub.OmitIdToken <- true

        do! assertRefusedWith world "sign-in-failed" "malformed_response: id_token_missing"
    }

// ---------------------------------------------------------------------------
// Keys that rotate (FR-006)
// ---------------------------------------------------------------------------

[<Fact>]
let ``an unknown kid makes one refresh of the key set, and the token is accepted when the new key is published`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        let browser = newBrowser world
        // The cache has heard of the first key only when Resolve read the document.
        let! seen = visit browser world
        Assert.Equal(1, world.Fake.KeyFetches)

        let rotated = world.Fake.Rotate()
        world.Fake.Asserts(FakeMicrosoft.WorkTenantA, FakeMicrosoft.Oid, email = FakeMicrosoft.Email, key = rotated)

        let! back = callback browser seen.Authorization
        let! outcome = codeFrom browser back
        signedIn outcome |> ignore

        Assert.Equal(2, world.Fake.KeyFetches)
        let! linked = subjects world
        Assert.Equal($"{FakeMicrosoft.WorkTenantA}#{FakeMicrosoft.Oid}", linked)
    }

[<Fact>]
let ``an unknown kid that the refreshed key set does not hold is refused, after one refresh and no more`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        let browser = newBrowser world
        let! seen = visit browser world
        Assert.Equal(1, world.Fake.KeyFetches)

        world.Fake.Asserts(
            FakeMicrosoft.WorkTenantA,
            FakeMicrosoft.Oid,
            email = FakeMicrosoft.Email,
            key = world.Fake.UnpublishedKey()
        )

        let! back = callback browser seen.Authorization
        assertRefused browser back "sign-in-failed"
        Assert.Equal(2, world.Fake.KeyFetches)

        let! logged = lastFailure world
        Assert.Equal(invalidDetail "signature", logged)

        // Again, at once: the refresh is throttled, so the keys are not fetched a third time.
        do! assertRefusedWith world "sign-in-failed" (invalidDetail "signature")
        Assert.Equal(2, world.Fake.KeyFetches)
    }

// ---------------------------------------------------------------------------
// The email flags (research R5; task h762)
// ---------------------------------------------------------------------------

/// `xms_edov` as the JSON value each label names; None is the claim absent.
let private edovClaim (label: string) : obj option =
    match label with
    | "boolean-true" -> Some((true :> obj))
    | "boolean-false" -> Some((false :> obj))
    | "string-true" -> Some(("true" :> obj))
    | "string-TRUE" -> Some(("TRUE" :> obj))
    | "number-1" -> Some((1 :> obj))
    | "absent" -> None
    | other -> failwith $"no such claim: {other}"

let private subjectOf (tenant: string) = $"{tenant}#{FakeMicrosoft.Oid}"

[<Fact>]
let ``xms_edov true as a JSON boolean with an email stores the email verified`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        world.Fake.Asserts(FakeMicrosoft.WorkTenantA, FakeMicrosoft.Oid, email = FakeMicrosoft.Email, xmsEdov = (true :> obj))

        let! outcome = signIn (newBrowser world) world
        let code = signedIn outcome

        let! stored = userOf world (subjectOf FakeMicrosoft.WorkTenantA)
        Assert.Equal($"{FakeMicrosoft.Name}|{FakeMicrosoft.Email}|1", stored)

        // And what the app is told: the address, verified.
        let! exchanged = exchangeCode world.App.Client world.ClientId world.ClientSecret code
        let! tokens = json exchanged
        let payload = segmentOf 1 (strOf tokens "id_token")
        Assert.Equal(FakeMicrosoft.Email, strOf payload "email")
        Assert.Equal(JsonValueKind.True, payload.GetProperty("email_verified").ValueKind)
    }

[<Theory>]
[<InlineData("absent")>]
[<InlineData("boolean-false")>]
[<InlineData("string-true")>]
[<InlineData("string-TRUE")>]
[<InlineData("number-1")>]
let ``an email without xms_edov as the JSON boolean true is stored not verified, whatever else it is`` (label: string) =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        world.Fake.Asserts(
            FakeMicrosoft.WorkTenantA,
            FakeMicrosoft.Oid,
            email = FakeMicrosoft.Email,
            claims = (edovClaim label |> Option.map (fun value -> [ "xms_edov", value ]) |> Option.defaultValue [])
        )

        let! outcome = signIn (newBrowser world) world
        let code = signedIn outcome

        let! stored = userOf world (subjectOf FakeMicrosoft.WorkTenantA)
        Assert.Equal($"{FakeMicrosoft.Name}|{FakeMicrosoft.Email}|0", stored)

        let! exchanged = exchangeCode world.App.Client world.ClientId world.ClientSecret code
        let! tokens = json exchanged
        let payload = segmentOf 1 (strOf tokens "id_token")

        match payload.TryGetProperty "email_verified" with
        | true, flag -> Assert.Equal(JsonValueKind.False, flag.ValueKind)
        | _ -> ()
    }

[<Fact>]
let ``a personal account's email is not verified either, without xms_edov`` () =
    task {
        let! world = start PersonalAccounts
        use _ = world.App
        use _ = world.Fake

        let! outcome = signIn (newBrowser world) world
        signedIn outcome |> ignore

        let! stored = userOf world (subjectOf FakeMicrosoft.PersonalTenant)
        Assert.Equal($"{FakeMicrosoft.Name}|{FakeMicrosoft.Email}|0", stored)
    }

[<Fact>]
let ``xms_edov true without an email verifies nothing`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        world.Fake.Asserts(FakeMicrosoft.WorkTenantA, FakeMicrosoft.Oid, xmsEdov = (true :> obj))

        let! outcome = signIn (newBrowser world) world
        signedIn outcome |> ignore

        let! stored = userOf world (subjectOf FakeMicrosoft.WorkTenantA)
        Assert.Equal($"{FakeMicrosoft.Name}||0", stored)
    }

/// A person who already exists with a verified address, made through the fake
/// connector, which is the way the other linking tests make one.
let private existingMaria (world: World) =
    task {
        let! _ = signInThroughFake (newBrowser world) world.ClientId "openid email profile" "fake-maria" FakeMicrosoft.Email
        let! users =
            count world $"SELECT count(*) FROM users WHERE email = '{FakeMicrosoft.Email}' AND email_verified = 1"
        Assert.Equal(1, users)
    }

[<Fact>]
let ``a Microsoft sign-in with the same verified email and xms_edov true joins the existing user`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake
        do! existingMaria world

        world.Fake.Asserts(FakeMicrosoft.WorkTenantA, FakeMicrosoft.Oid, email = FakeMicrosoft.Email, xmsEdov = (true :> obj))

        let! outcome = signIn (newBrowser world) world
        signedIn outcome |> ignore

        let! users = count world "SELECT count(*) FROM users"
        Assert.Equal(1, users)

        let! accounts = count world "SELECT count(*) FROM linked_accounts WHERE provider_kind = 'microsoft'"
        Assert.Equal(1, accounts)
    }

[<Fact>]
let ``a Microsoft sign-in with the same email and no xms_edov is never joined to the existing user`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake
        do! existingMaria world

        // Microsoft's word alone (nOAuth): the same address, nothing to vouch for it.
        let! outcome = signIn (newBrowser world) world
        signedIn outcome |> ignore

        let! users = count world "SELECT count(*) FROM users"
        Assert.Equal(2, users)

        let! linked = count world "SELECT count(*) FROM security_events WHERE event_type = 'user_linked'"
        Assert.Equal(0, linked)
    }

// ---------------------------------------------------------------------------
// The same person after the registration is replaced (research R4)
// ---------------------------------------------------------------------------

[<Fact>]
let ``the same tid and oid after the client ID changes is the same user, though sub is new`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        let! first = signIn (newBrowser world) world
        signedIn first |> ignore

        let! users = count world "SELECT count(*) FROM users"
        let! linked = subjects world

        // The operator replaced the app registration: a new client ID, a new
        // pairwise `sub` for everyone.
        do! FakeMicrosoft.seedConnectorWith world.App world.Fake.Audience FakeMicrosoft.ReplacedClientId

        world.Fake.Asserts(
            FakeMicrosoft.WorkTenantA,
            FakeMicrosoft.Oid,
            email = FakeMicrosoft.Email,
            sub = "pairwise-of-the-new-registration"
        )

        let! second = signIn (newBrowser world) world
        signedIn second |> ignore

        // The token was for the new client; the old one would have been refused.
        Assert.Equal(Some FakeMicrosoft.ReplacedClientId, world.Fake.Stub.LastTokenRequest |> Option.bind (fun r -> r.BasicClientId))

        let! usersAfter = count world "SELECT count(*) FROM users"
        let! linkedAfter = subjects world
        Assert.Equal(users, usersAfter)
        Assert.Equal(1, usersAfter)
        Assert.Equal(linked, linkedAfter)
        Assert.Equal($"{FakeMicrosoft.WorkTenantA}#{FakeMicrosoft.Oid}", linkedAfter)
    }

[<Fact>]
let ``two people of one tenant, and one person of two tenants, are different users`` () =
    task {
        let! world = start PersonalAndAnyOrganization
        use _ = world.App
        use _ = world.Fake

        let one (tid: string) (oid: string) =
            task {
                world.Fake.Asserts(tid, oid)
                let! outcome = signIn (newBrowser world) world
                signedIn outcome |> ignore
            }

        do! one FakeMicrosoft.WorkTenantA FakeMicrosoft.Oid
        do! one FakeMicrosoft.WorkTenantA FakeMicrosoft.OtherOid
        do! one FakeMicrosoft.WorkTenantB FakeMicrosoft.Oid
        do! one FakeMicrosoft.WorkTenantA FakeMicrosoft.Oid

        let! users = count world "SELECT count(*) FROM users"
        Assert.Equal(3, users)
    }

// ---------------------------------------------------------------------------
// The request to Microsoft (FR-004, FR-005)
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("personal", "consumers")>]
[<InlineData("one", FakeMicrosoft.WorkTenantA)>]
[<InlineData("any", "organizations")>]
[<InlineData("both", "common")>]
let ``the authorize redirect goes to the audience's own endpoint with PKCE S256, a nonce and a state`` (audience: string, segment: string) =
    task {
        let! world = start (audienceOf audience)
        use _ = world.App
        use _ = world.Fake

        let! seen = visit (newBrowser world) world

        let uri = seen.AuthorizeUri
        Assert.Equal($"https://login.microsoftonline.com/{segment}/oauth2/v2.0/authorize", uri.GetLeftPart UriPartial.Path)
        Assert.Equal(world.Fake.AuthorizationEndpoint, uri.GetLeftPart UriPartial.Path)

        let query = System.Web.HttpUtility.ParseQueryString uri.Query
        Assert.Equal("code", query["response_type"])
        Assert.Equal(FakeMicrosoft.ClientId, query["client_id"])
        Assert.Equal("openid email profile", query["scope"])
        Assert.Equal("S256", query["code_challenge_method"])
        Assert.False(String.IsNullOrEmpty query["code_challenge"])
        Assert.False(String.IsNullOrEmpty query["nonce"])
        Assert.False(String.IsNullOrEmpty query["state"])
        Assert.Equal("https://umbral.test/connect/microsoft/callback", query["redirect_uri"])
    }

[<Fact>]
let ``the token request carries the code, the PKCE verifier and the client's secret, at Microsoft's token endpoint`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        let browser = newBrowser world
        let! seen = visit browser world
        let! back = callback browser seen.Authorization
        let! outcome = codeFrom browser back
        signedIn outcome |> ignore

        let request = world.Fake.Stub.LastTokenRequest.Value
        let field name = request.Fields |> Map.tryFind name

        Assert.Equal(Some "authorization_code", field "grant_type")
        Assert.Equal(Some seen.Authorization.Code, field "code")
        Assert.Equal(Some "https://umbral.test/connect/microsoft/callback", field "redirect_uri")

        // The document lists `client_secret_basic` among its methods, so that is
        // what the exchange uses; the secret is the one the operator stored.
        Assert.Equal(Some FakeMicrosoft.ClientId, request.BasicClientId)
        Assert.Equal(Some FakeMicrosoft.ClientSecret, request.BasicClientSecret)

        let challenge = (System.Web.HttpUtility.ParseQueryString seen.AuthorizeUri.Query)["code_challenge"]
        let verifier = (field "code_verifier").Value
        Assert.Equal(challenge, Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes verifier)))

        // The key set is fetched where Microsoft keeps it, once.
        Assert.Equal(1, world.Fake.KeyFetches)
        Assert.Equal(1, world.Fake.Stub.CountOf "/token")
        do! assertNothingSensitiveLogged world
    }

[<Fact>]
let ``an expired client secret is refused by the token endpoint and logged as rejected`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        world.Fake.Stub.TokenResponse <-
            Some(HttpStatusCode.BadRequest, """{"error":"invalid_client","error_description":"AADSTS7000222: expired"}""")

        do! assertRefusedWith world "sign-in-failed" "rejected: invalid_client"
    }

// ---------------------------------------------------------------------------
// Code review of task m5ft: the key that verified the signature, the shared
// validator on this path, the claims that must not verify or fill in an email
// ---------------------------------------------------------------------------

// Microsoft.IdentityModel verifies a signature with the first key of the set that
// has the token's `kid`; a later key with the same `kid` never verifies anything.
// The rule judges the key the library used, so the order of the list cannot make
// a different key's issuer count.

[<Fact>]
let ``a kid listed twice judges the key that verified, not another with the same kid`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        // The key that signs (first with this kid) names the personal tenant; a
        // second key with the same kid names the template, which would fit a work
        // tenant. The token says work: the key that verified is the one judged.
        world.Fake.Stub.KeyIssuers["key-1"] <- Ms.issuerFor FakeMicrosoft.PersonalTenant
        world.Fake.PublishDuplicateKid("key-1", Some Ms.issuerTemplate) |> ignore

        do! assertRefusedWith world "sign-in-failed" (invalidDetail "key_issuer")
    }

[<Fact>]
let ``a kid listed twice does not make a good key refused because another with the same kid names a mismatch`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        // The key that signs (first) names the template; a second key with the same
        // kid names the personal tenant. Only the first verifies, and it is the one judged.
        world.Fake.PublishDuplicateKid("key-1", Some(Ms.issuerFor FakeMicrosoft.PersonalTenant)) |> ignore

        let! outcome = signIn (newBrowser world) world
        signedIn outcome |> ignore
    }

[<Fact>]
let ``a token signed by a second key with a kid already listed is refused as a bad signature`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        let second = world.Fake.PublishDuplicateKid("key-1", Some Ms.issuerTemplate)
        world.Fake.Asserts(FakeMicrosoft.WorkTenantA, FakeMicrosoft.Oid, email = FakeMicrosoft.Email, key = second)

        do! assertRefusedWith world "sign-in-failed" (invalidDetail "signature")
    }

[<Fact>]
let ``a one-organization key set carries that tenant's own issuer and its token is accepted`` () =
    task {
        let! world = start (OneOrganization(Guid.Parse FakeMicrosoft.WorkTenantA))
        use _ = world.App
        use _ = world.Fake

        world.Fake.Stub.KeyIssuers["key-1"] <- Ms.issuerFor FakeMicrosoft.WorkTenantA

        let! outcome = signIn (newBrowser world) world
        signedIn outcome |> ignore
    }

[<Fact>]
let ``a one-organization key set whose key was issued to another tenant is refused`` () =
    task {
        let! world = start (OneOrganization(Guid.Parse FakeMicrosoft.WorkTenantA))
        use _ = world.App
        use _ = world.Fake

        world.Fake.Stub.KeyIssuers["key-1"] <- Ms.issuerFor FakeMicrosoft.WorkTenantB

        do! assertRefusedWith world "sign-in-failed" (invalidDetail "key_issuer")
    }

[<Fact>]
let ``an unsigned token (alg none) is refused on the Microsoft path`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        world.Fake.Stub.IdToken <- { world.Fake.Stub.IdToken with Unsigned = true }

        // Not `assertRefusedWith`: an unsigned token has an empty signature, which
        // its search of the log for the signature would find everywhere.
        let browser = newBrowser world
        let! seen = visit browser world
        let! back = callback browser seen.Authorization
        assertRefused browser back "sign-in-failed"

        let! logged = lastFailure world
        Assert.Equal(invalidDetail "algorithm", logged)
        do! assertNobodyMade world
    }

[<Fact>]
let ``a token signed with a shared secret (HS256) is refused on the Microsoft path`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        let shared = SymmetricSecurityKey(Encoding.UTF8.GetBytes(world.ClientSecret.PadRight(48, 'x')))

        world.Fake.Stub.IdToken <-
            { world.Fake.Stub.IdToken with
                Algorithm = SecurityAlgorithms.HmacSha256
                Key = Some(shared :> SecurityKey) }

        do! assertRefusedWith world "sign-in-failed" (invalidDetail "algorithm")
    }

[<Fact>]
let ``an expired token is refused on the Microsoft path`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        let now = world.App.Clock.GetUtcNow()
        world.Fake.Stub.IdToken <- { world.Fake.Stub.IdToken with ExpiresAt = Some(now - TimeSpan.FromMinutes 3.0) }

        do! assertRefusedWith world "sign-in-failed" (invalidDetail "expired")
    }

[<Fact>]
let ``a token that is not valid yet is refused on the Microsoft path`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        // Microsoft's clock is ten minutes ahead of Umbral's.
        world.Fake.Stub.Now <- fun () -> world.App.Clock.GetUtcNow() + TimeSpan.FromMinutes 10.0

        do! assertRefusedWith world "sign-in-failed" (invalidDetail "not_yet_valid")
    }

[<Fact>]
let ``the standard email_verified claim without xms_edov leaves the email unverified`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        world.Fake.Asserts(
            FakeMicrosoft.WorkTenantA,
            FakeMicrosoft.Oid,
            email = FakeMicrosoft.Email,
            claims = [ "email_verified", (true :> obj) ]
        )

        let! outcome = signIn (newBrowser world) world
        signedIn outcome |> ignore

        let! stored = userOf world (subjectOf FakeMicrosoft.WorkTenantA)
        Assert.Equal($"{FakeMicrosoft.Name}|{FakeMicrosoft.Email}|0", stored)
    }

[<Fact>]
let ``preferred_username and upn are not an email: with no email claim the stored email is empty`` () =
    task {
        let! world = start AnyOrganization
        use _ = world.App
        use _ = world.Fake

        world.Fake.Asserts(
            FakeMicrosoft.WorkTenantA,
            FakeMicrosoft.Oid,
            xmsEdov = (true :> obj),
            claims = [ "preferred_username", ("maria@contoso.example" :> obj); "upn", ("maria@contoso.example" :> obj) ]
        )

        let! outcome = signIn (newBrowser world) world
        signedIn outcome |> ignore

        let! stored = userOf world (subjectOf FakeMicrosoft.WorkTenantA)
        Assert.Equal($"{FakeMicrosoft.Name}||0", stored)
    }
