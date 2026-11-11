/// Sign in with Apple through the real pipeline, against a fake Apple (specs/013
/// User Stories 2 and 3).
///
/// Every test starts at `/connect/apple/start`, plays Apple's page with
/// `FakeApple`, and posts the form to `POST /connect/apple/callback` as the
/// browser would. Only the network is replaced: the adapter, the discovery cache,
/// the correlation cookie, the client secret Umbral signs and the user store are
/// the product's own. The fake judges the client secret as Apple does, so a
/// sign-in that completes also proves that the secret was good.
///
/// What is held to account most closely is `email_verified` (task h762: account
/// linking rests on it) and the refusals: a forged, replayed or mismatched answer
/// ends at the sign-in page with no session, no user and a reason in the log, and
/// no secret of any kind is in the log.
module Umbral.Tests.AppleSignInTests

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading.Tasks
open Microsoft.IdentityModel.Tokens
open Xunit
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.StubOidcProvider
open Umbral.Tests.Infrastructure.Journeys

// ---------------------------------------------------------------------------
// The world of a test
// ---------------------------------------------------------------------------

/// An app with the fake Apple behind its provider calls, the Apple connector and
/// the fake connector seeded, and one registered app to sign in to.
type private World =
    { App: Fixtures.TestApp
      Fake: FakeApple.FakeApple
      ClientId: string
      ClientSecret: string
      /// Every authorization code the fake's page handed out, for the search of
      /// the log.
      Codes: List<string> }

/// `keyFor` says which key the connector stores: usually the one Apple knows.
let private startWithKey (keyFor: FakeApple.FakeApple -> string) =
    task {
        let fake = FakeApple.create ()
        let! app = Fixtures.createAppWithProviderHandler fake.Handler fakeSettings ignore
        FakeApple.attach app fake

        let operator = Browser app.Client
        let! prepared = prepare app operator
        do! FakeApple.seedConnectorWith app (keyFor fake)

        // Apple's usual person; a test that wants another identity says so.
        fake.AssertsVerifiedEmail("apple-sub-1", "maria@example.com")

        return
            { App = app
              Fake = fake
              ClientId = prepared.ClientId
              ClientSecret = prepared.ClientSecret
              Codes = List<string>() }
    }

let private start () =
    startWithKey (fun fake -> fake.PrivateKeyPem)

let private location (response: HttpResponseMessage) = response.Headers.Location |> string

let private newBrowser (world: World) = Browser world.App.Client

/// What the browser has seen of Apple's side of a sign-in so far.
type private Visit =
    { /// Where Umbral sent the browser.
      AuthorizeUri: Uri
      /// What Apple's page would post back.
      Fields: (string * string) list }

/// From the app's authorization request, through the sign-in page's Apple
/// button, to Apple's page and the form it would post. Nothing is posted.
let private visit (browser: Browser) (world: World) (user: string option) =
    task {
        let! toPicker = browser.Get(authorizeUrl world.ClientId "openid email profile")
        let! toApple = browser.Get((location toPicker).Replace("/signin", "/connect/apple/start"))

        let authorizeUri = Uri(location toApple)
        let fields = world.Fake.Approve(authorizeUri, user)

        for name, value in fields do
            if name = "code" then
                world.Codes.Add value

        return
            { AuthorizeUri = authorizeUri
              Fields = fields }
    }

/// The authorization code the app is given, or where the browser landed
/// instead, from the answer to Apple's POST.
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

/// One whole sign-in through Apple, ending with Umbral's code for the app or
/// where the person landed instead.
let private signIn (browser: Browser) (world: World) (user: string option) =
    task {
        let! seen = visit browser world user
        let! back = FakeApple.post browser seen.Fields
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

/// The user Umbral made for an Apple subject: name, email and the verified flag.
let private userOf (world: World) (subject: string) =
    scalar
        world
        "SELECT u.display_name || '|' || coalesce(u.email, '') || '|' || u.email_verified FROM users u \
         JOIN linked_accounts l ON l.user_id = u.id WHERE l.provider_kind = 'apple' AND l.provider_subject = @s"
        [ "@s", subject ]

let private lastFailure (world: World) =
    scalar
        world
        "SELECT detail FROM security_events WHERE event_type = 'sign_in_failed' ORDER BY id DESC LIMIT 1"
        []

let private eventCount (world: World) (eventType: string) =
    count world $"SELECT count(*) FROM security_events WHERE event_type = '{eventType}'"

/// The sign-in page with the generic failure, and no session: what every
/// refusal looks like to the person.
let private assertRefused (browser: Browser) (back: HttpResponseMessage) (code: string) =
    Assert.Equal(HttpStatusCode.Redirect, back.StatusCode)
    let landed = location back
    Assert.Contains("/signin", landed)
    Assert.Contains($"msg={code}", landed)
    Assert.False(browser.Cookies.ContainsKey Umbral.Infrastructure.Sessions.CookieName, "a session was started")

// ---------------------------------------------------------------------------
// Secrets stay out of the log (SC-004)
// ---------------------------------------------------------------------------

/// The body of a PEM: what is between its lines, as one run of base64.
let private pemBody (pem: string) =
    pem.Split('\n')
    |> Array.filter (fun line -> line.Length > 0 && not (line.StartsWith "-----"))
    |> String.concat ""

/// Every column of every row of `security_events`, as one text.
let private dumpEvents (world: World) =
    scalar
        world
        "SELECT coalesce(group_concat(occurred_utc || '|' || event_type || '|' || outcome || '|' || actor || '|' \
         || coalesce(user_id, '') || '|' || coalesce(connector_slug, '') || '|' || coalesce(client_id, '') || '|' \
         || coalesce(remote_ip, '') || '|' || coalesce(detail, ''), char(10)), '') FROM security_events"
        []

/// No row of the log holds the key (whole or in part), a client secret the fake
/// received, a code Apple's page issued, or an `id_token` (whole or its
/// signature). `extraKeys` are further PEMs a test stored.
let private assertNoSecretsLogged (world: World) (extraKeys: string list) =
    task {
        let! log = dumpEvents world

        let secrets =
            [ for pem in world.Fake.PrivateKeyPem :: extraKeys do
                  let body = pemBody pem
                  yield body
                  yield body.Substring(0, 40)

              yield! world.Fake.ClientSecrets
              // The signature of a client secret is its last segment.
              for secret in world.Fake.ClientSecrets do
                  yield secret.Split('.')[2]

              yield! world.Codes

              match world.Fake.Stub.LastIdToken with
              | Some idToken ->
                  yield idToken
                  yield idToken.Split('.')[2]
              | None -> () ]

        for secret in secrets do
            Assert.False(log.Contains secret, "a secret is in the security log")
    }

// ---------------------------------------------------------------------------
// The request to Apple
// ---------------------------------------------------------------------------

[<Fact>]
let ``the start redirects to Apple with the form post request and keeps its cookie for a cross-site post`` () =
    task {
        let! world = start ()
        use _ = world.App
        let browser = newBrowser world

        let! toPicker = browser.Get(authorizeUrl world.ClientId "openid email profile")
        let! toApple = browser.Get((location toPicker).Replace("/signin", "/connect/apple/start"))

        Assert.Equal(HttpStatusCode.Redirect, toApple.StatusCode)
        let target = Uri(location toApple)
        Assert.Equal("https://appleid.apple.com/auth/authorize", target.GetLeftPart UriPartial.Path)

        let query = System.Web.HttpUtility.ParseQueryString target.Query
        Assert.Equal("code", query["response_type"])
        Assert.Equal("form_post", query["response_mode"])
        Assert.Equal("name email", query["scope"])
        Assert.Equal(FakeApple.ServicesId, query["client_id"])
        Assert.Equal("https://umbral.test/connect/apple/callback", query["redirect_uri"])
        Assert.False(String.IsNullOrEmpty query["state"], "no state")
        Assert.False(String.IsNullOrEmpty query["nonce"], "no nonce")
        Assert.False(String.IsNullOrEmpty query["code_challenge"], "no PKCE challenge")
        Assert.Equal("S256", query["code_challenge_method"])

        let cookies =
            match toApple.Headers.TryGetValues "Set-Cookie" with
            | true, values -> values |> Option.ofObj |> Option.map List.ofSeq |> Option.defaultValue []
            | _ -> []

        let post = cookies |> List.tryFind (fun c -> c.StartsWith "umbral_connect_post=")
        Assert.True(post.IsSome, "no umbral_connect_post cookie")

        let attributes = post.Value.ToLowerInvariant()
        Assert.Contains("samesite=none", attributes)
        Assert.Contains("secure", attributes)
        Assert.Contains("httponly", attributes)

        // The cookie of the redirect mode is not minted for a provider that answers by form post.
        Assert.False(cookies |> List.exists (fun c -> c.StartsWith "umbral_connect="), "the redirect-mode cookie was set")
    }

// ---------------------------------------------------------------------------
// The happy path
// ---------------------------------------------------------------------------

[<Fact>]
let ``a registered app's authorization code flow through Apple hands the app the email, the verification and the name`` () =
    task {
        let! world = start ()
        use _ = world.App

        let! outcome = signIn (newBrowser world) world (Some FakeApple.mariaUser)
        let code = signedIn outcome

        let! exchanged = exchangeCode world.App.Client world.ClientId world.ClientSecret code
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode)
        let! tokens = json exchanged

        let payload = segmentOf 1 (strOf tokens "id_token")
        Assert.Equal("apple", strOf payload "idp")
        Assert.Equal("maria@example.com", strOf payload "email")
        Assert.Equal(JsonValueKind.True, payload.GetProperty("email_verified").ValueKind)
        Assert.Equal("Maria Ruiz", strOf payload "name")

        let! info = userInfo world.App.Client (strOf tokens "access_token")
        Assert.Equal(HttpStatusCode.OK, info.StatusCode)
        let! claims = json info
        Assert.Equal("maria@example.com", strOf claims "email")
        Assert.Equal(JsonValueKind.True, claims.GetProperty("email_verified").ValueKind)
        Assert.Equal("Maria Ruiz", strOf claims "name")

        do! assertNoSecretsLogged world []
    }

[<Fact>]
let ``the token request Apple receives carries the code, the verifier and a client secret it accepted`` () =
    task {
        let! world = start ()
        use _ = world.App
        let browser = newBrowser world

        let! seen = visit browser world None
        let! back = FakeApple.post browser seen.Fields
        let! outcome = codeFrom browser back
        signedIn outcome |> ignore

        let request = world.Fake.Stub.LastTokenRequest.Value
        let field name = request.Fields |> Map.tryFind name

        Assert.Equal(Some "authorization_code", field "grant_type")
        Assert.Equal(seen.Fields |> List.tryFind (fun (n, _) -> n = "code") |> Option.map snd, field "code")
        Assert.Equal(Some "https://umbral.test/connect/apple/callback", field "redirect_uri")
        Assert.Equal(Some FakeApple.ServicesId, field "client_id")
        // `client_secret_post`, as Apple's document says: no Authorization header.
        Assert.Equal(None, request.BasicClientId)

        // The verifier hashes to the challenge Umbral sent to Apple.
        let challenge = (System.Web.HttpUtility.ParseQueryString seen.AuthorizeUri.Query)["code_challenge"]

        let verifier = (field "code_verifier").Value
        Assert.Equal(challenge, Base64UrlEncoder.Encode(SHA256.HashData(Text.Encoding.ASCII.GetBytes verifier)))

        // The fake judged this secret and let the sign-in through; decoded, it
        // is exactly what FR-009 says.
        let secret = (field "client_secret").Value
        Assert.Contains(secret, world.Fake.ClientSecrets)

        let header = segmentOf 0 secret
        Assert.Equal("ES256", strOf header "alg")
        Assert.Equal(FakeApple.KeyId, strOf header "kid")

        let payload = segmentOf 1 secret

        let names = payload.EnumerateObject() |> Seq.map (fun p -> p.Name) |> Seq.sort |> List.ofSeq
        Assert.Equal<string list>([ "aud"; "exp"; "iat"; "iss"; "sub" ], names)
        Assert.Equal(FakeApple.TeamId, strOf payload "iss")
        Assert.Equal(FakeApple.ServicesId, strOf payload "sub")
        Assert.Equal(FakeApple.Issuer, strOf payload "aud")
        Assert.Equal(300L, payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64())
    }

[<Fact>]
let ``two sign-ins send two different client secrets`` () =
    task {
        let! world = start ()
        use _ = world.App

        let! first = signIn (newBrowser world) world None
        signedIn first |> ignore

        world.App.Clock.Advance(TimeSpan.FromSeconds 30.0)

        let! second = signIn (newBrowser world) world None
        signedIn second |> ignore

        let secrets = world.Fake.ClientSecrets
        Assert.Equal(2, secrets.Length)
        Assert.NotEqual<string>(secrets[0], secrets[1])
        Assert.NotEqual(
            (segmentOf 1 secrets[0]).GetProperty("iat").GetInt64(),
            (segmentOf 1 secrets[1]).GetProperty("iat").GetInt64()
        )
    }

// ---------------------------------------------------------------------------
// The name
// ---------------------------------------------------------------------------

[<Fact>]
let ``the first sign-in with a user field stores the name and a later one without it keeps it`` () =
    task {
        let! world = start ()
        use _ = world.App

        let! first = signIn (newBrowser world) world (Some FakeApple.mariaUser)
        signedIn first |> ignore

        let! afterFirst = userOf world "apple-sub-1"
        Assert.Equal("Maria Ruiz|maria@example.com|1", afterFirst)

        let! second = signIn (newBrowser world) world None
        signedIn second |> ignore

        let! afterSecond = userOf world "apple-sub-1"
        Assert.Equal("Maria Ruiz|maria@example.com|1", afterSecond)

        let! users = count world "SELECT count(*) FROM users"
        Assert.Equal(1, users)
    }

[<Fact>]
let ``a first sign-in with no user field makes the user Apple user and a later one with a name sets it`` () =
    task {
        let! world = start ()
        use _ = world.App

        let! first = signIn (newBrowser world) world None
        signedIn first |> ignore

        let! afterFirst = userOf world "apple-sub-1"
        Assert.Equal("Apple user|maria@example.com|1", afterFirst)

        let! second = signIn (newBrowser world) world (Some FakeApple.mariaUser)
        signedIn second |> ignore

        let! afterSecond = userOf world "apple-sub-1"
        Assert.Equal("Maria Ruiz|maria@example.com|1", afterSecond)
    }

[<Fact>]
let ``an email inside the user field is never read, the address is the id_token's`` () =
    task {
        let! world = start ()
        use _ = world.App

        let forged =
            """{"name":{"firstName":"Maria","lastName":"Ruiz"},"email":"someone-else@example.org"}"""

        let! outcome = signIn (newBrowser world) world (Some forged)
        signedIn outcome |> ignore

        let! stored = userOf world "apple-sub-1"
        Assert.Equal("Maria Ruiz|maria@example.com|1", stored)

        let! others = count world "SELECT count(*) FROM users WHERE email = 'someone-else@example.org'"
        Assert.Equal(0, others)
    }

[<Fact>]
let ``a user field that is not JSON does not fail the sign-in`` () =
    task {
        let! world = start ()
        use _ = world.App

        let! outcome = signIn (newBrowser world) world (Some "this is { not json")
        signedIn outcome |> ignore

        let! stored = userOf world "apple-sub-1"
        Assert.Equal("Apple user|maria@example.com|1", stored)
    }

// ---------------------------------------------------------------------------
// The email flags (task h762)
// ---------------------------------------------------------------------------

/// `email_verified` as the JSON value each label names; None is the claim absent.
let private verifiedClaim (label: string) : obj option =
    match label with
    | "boolean-true" -> Some((true :> obj))
    | "string-true" -> Some(("true" :> obj))
    | "boolean-false" -> Some((false :> obj))
    | "string-false" -> Some(("false" :> obj))
    | "string-TRUE" -> Some(("TRUE" :> obj))
    | "number-1" -> Some((1 :> obj))
    | "absent" -> None
    | other -> failwith $"no such claim: {other}"

[<Theory>]
[<InlineData("boolean-true")>]
[<InlineData("string-true")>]
let ``email_verified as the boolean true or the string true stores the email verified`` (label: string) =
    task {
        let! world = start ()
        use _ = world.App

        world.Fake.Asserts("apple-sub-1", Some "maria@example.com", verifiedClaim label, None)

        let! outcome = signIn (newBrowser world) world None
        signedIn outcome |> ignore

        let! stored = userOf world "apple-sub-1"
        Assert.Equal("Apple user|maria@example.com|1", stored)
    }

[<Theory>]
[<InlineData("boolean-false")>]
[<InlineData("string-false")>]
[<InlineData("string-TRUE")>]
[<InlineData("number-1")>]
[<InlineData("absent")>]
let ``email_verified as anything else signs in and stores the email not verified`` (label: string) =
    task {
        let! world = start ()
        use _ = world.App

        world.Fake.Asserts("apple-sub-1", Some "maria@example.com", verifiedClaim label, None)

        let! outcome = signIn (newBrowser world) world None
        signedIn outcome |> ignore

        let! stored = userOf world "apple-sub-1"
        Assert.Equal("Apple user|maria@example.com|0", stored)
    }

/// A person who already exists with a verified address, made through the fake
/// connector, which is the way the other linking tests make one.
let private existingAna (world: World) =
    task {
        let! _ = signInThroughFake (newBrowser world) world.ClientId "openid email profile" "fake-ana" "ana@example.com"
        let! users = count world "SELECT count(*) FROM users WHERE email = 'ana@example.com' AND email_verified = 1"
        Assert.Equal(1, users)
    }

[<Fact>]
let ``an Apple sign-in with the same verified email and email_verified true joins the existing user`` () =
    task {
        let! world = start ()
        use _ = world.App
        do! existingAna world

        world.Fake.AssertsVerifiedEmail("apple-ana", "ana@example.com")

        let! outcome = signIn (newBrowser world) world None
        signedIn outcome |> ignore

        let! users = count world "SELECT count(*) FROM users"
        Assert.Equal(1, users)

        let! linked = eventCount world "user_linked"
        Assert.Equal(1, linked)

        let! accounts = count world "SELECT count(*) FROM linked_accounts WHERE provider_kind = 'apple'"
        Assert.Equal(1, accounts)
    }

[<Theory>]
[<InlineData("string-TRUE")>]
[<InlineData("boolean-false")>]
let ``an Apple sign-in whose email_verified is not true is never joined to the existing user`` (label: string) =
    task {
        let! world = start ()
        use _ = world.App
        do! existingAna world

        world.Fake.Asserts("apple-ana", Some "ana@example.com", verifiedClaim label, None)

        let! outcome = signIn (newBrowser world) world None
        signedIn outcome |> ignore

        let! users = count world "SELECT count(*) FROM users"
        Assert.Equal(2, users)

        let! linked = eventCount world "user_linked"
        Assert.Equal(0, linked)
    }

[<Fact>]
let ``a relay address signs the person in and is stored as their verified email`` () =
    task {
        let! world = start ()
        use _ = world.App

        let relay = "x7f3k@privaterelay.appleid.com"
        world.Fake.Asserts("apple-sub-1", Some relay, Some(("true" :> obj)), Some(("true" :> obj)))

        let! outcome = signIn (newBrowser world) world None
        signedIn outcome |> ignore

        let! stored = userOf world "apple-sub-1"
        Assert.Equal($"Apple user|{relay}|1", stored)
    }

[<Fact>]
let ``an id_token with no email claim signs the person in with no email`` () =
    task {
        let! world = start ()
        use _ = world.App

        world.Fake.Asserts("apple-sub-1", None, None, None)

        let! outcome = signIn (newBrowser world) world None
        signedIn outcome |> ignore

        let! stored = userOf world "apple-sub-1"
        Assert.Equal("Apple user||0", stored)
    }

[<Fact>]
let ``the fake mints a boolean, a string and a number as their own JSON types`` () =
    let fake = FakeApple.create ()

    let spec =
        { defaultIdToken with
            Claims = [ "a", (true :> obj); "b", ("true" :> obj); "c", (1 :> obj) ] }

    let token = fake.Stub.MintIdToken(spec, FakeApple.ServicesId, "nonce")
    let payload = segmentOf 1 token

    Assert.Equal(JsonValueKind.True, payload.GetProperty("a").ValueKind)
    Assert.Equal(JsonValueKind.String, payload.GetProperty("b").ValueKind)
    Assert.Equal(JsonValueKind.Number, payload.GetProperty("c").ValueKind)

// ---------------------------------------------------------------------------
// Refusals (User Story 3)
// ---------------------------------------------------------------------------

[<Fact>]
let ``a post with the wrong state is refused, spends the correlation, and the right state is refused after it`` () =
    task {
        let! world = start ()
        use _ = world.App
        let browser = newBrowser world

        let! seen = visit browser world None

        let forged =
            seen.Fields |> List.map (fun (name, value) -> if name = "state" then name, "not-the-state" else name, value)

        let! refused = FakeApple.post browser forged
        assertRefused browser refused "sign-in-failed"

        let! detail = lastFailure world
        Assert.Equal("state_mismatch", detail)

        // The correlation was spent by the first answer.
        let! later = FakeApple.post browser seen.Fields
        assertRefused browser later "sign-in-failed"

        Assert.Equal(0, world.Fake.TokenRequests)
        let! users = count world "SELECT count(*) FROM users"
        Assert.Equal(0, users)
        do! assertNoSecretsLogged world []
    }

[<Fact>]
let ``a post from a browser without the correlation cookie is refused`` () =
    task {
        let! world = start ()
        use _ = world.App

        let! seen = visit (newBrowser world) world None

        // Another browser, or a forged cross-site post: valid fields, no cookie.
        let stranger = newBrowser world
        let! refused = FakeApple.post stranger seen.Fields
        assertRefused stranger refused "sign-in-failed"

        let! detail = lastFailure world
        Assert.Equal("no_correlation_cookie", detail)
        Assert.Equal(0, world.Fake.TokenRequests)
        do! assertNoSecretsLogged world []
    }

[<Fact>]
let ``an answer that was already redeemed is refused when posted again, with or without the old cookie`` () =
    task {
        let! world = start ()
        use _ = world.App
        let browser = newBrowser world

        let! seen = visit browser world (Some FakeApple.mariaUser)
        let cookie = browser.Cookies["umbral_connect_post"]

        let! first = FakeApple.post browser seen.Fields
        Assert.Contains("/connect/authorize", location first)
        Assert.Equal(1, world.Fake.TokenRequests)

        // The cookie was deleted by the first answer.
        let! again = FakeApple.post browser seen.Fields
        Assert.Contains("msg=sign-in-failed", location again)

        // And with the old cookie value put back by hand.
        browser.Cookies["umbral_connect_post"] <- cookie
        let! putBack = FakeApple.post browser seen.Fields
        Assert.Contains("msg=sign-in-failed", location putBack)

        let! detail = lastFailure world
        Assert.Equal("correlation_replayed", detail)

        Assert.Equal(1, world.Fake.TokenRequests)
        let! signedInCount = eventCount world "sign_in_succeeded"
        Assert.Equal(1, signedInCount)
        do! assertNoSecretsLogged world []
    }

[<Fact>]
let ``the answer as a GET is refused`` () =
    task {
        let! world = start ()
        use _ = world.App
        let browser = newBrowser world

        let! seen = visit browser world None
        let field name = seen.Fields |> List.find (fun (n, _) -> n = name) |> snd

        let state = Uri.EscapeDataString(field "state")
        let code = Uri.EscapeDataString(field "code")

        let! refused = browser.Get $"/connect/apple/callback?state={state}&code={code}"

        assertRefused browser refused "sign-in-failed"
        Assert.Equal(0, world.Fake.TokenRequests)

        let! users = count world "SELECT count(*) FROM users"
        Assert.Equal(0, users)
        do! assertNoSecretsLogged world []
    }

[<Fact>]
let ``cancelling at Apple lands on the sign-in page as a cancelled sign-in and stores nothing`` () =
    task {
        let! world = start ()
        use _ = world.App
        let browser = newBrowser world

        let! seen = visit browser world None
        let! back = FakeApple.post browser (world.Fake.Cancel seen.AuthorizeUri)

        assertRefused browser back "access-denied"

        let! detail = lastFailure world
        Assert.Equal("provider_error:user_cancelled_authorize", detail)
        Assert.Equal(0, world.Fake.TokenRequests)

        let! users = count world "SELECT count(*) FROM users"
        let! accounts = count world "SELECT count(*) FROM linked_accounts WHERE provider_kind = 'apple'"
        Assert.Equal(0, users)
        Assert.Equal(0, accounts)
    }

/// What the id_token's fault is, by label, as a change to the stub's token spec
/// (the fake's clock is the app's, so "expired" is relative to it).
let private spoilIdToken (world: World) (label: string) =
    let stub = world.Fake.Stub

    match label with
    | "signature" -> stub.IdToken <- { stub.IdToken with Key = Some(stub.Attacker :> SecurityKey) }
    | "audience" -> stub.IdToken <- { stub.IdToken with Audience = Some [ "com.example.somebody.else" ] }
    | "issuer" -> stub.IdToken <- { stub.IdToken with Issuer = Some "https://evil.example.test" }
    | "nonce" -> stub.IdToken <- { stub.IdToken with Nonce = Replace "not-the-nonce-that-was-sent" }
    | "expired" -> stub.IdToken <- { stub.IdToken with ExpiresAt = Some(world.App.Clock.GetUtcNow().AddHours -1.0) }
    | other -> failwith $"no such fault: {other}"

[<Theory>]
[<InlineData("signature")>]
[<InlineData("audience")>]
[<InlineData("issuer")>]
[<InlineData("nonce")>]
[<InlineData("expired")>]
let ``an id_token with a fault is refused and the log names the fault`` (label: string) =
    task {
        let! world = start ()
        use _ = world.App
        let browser = newBrowser world

        spoilIdToken world label

        let! seen = visit browser world (Some FakeApple.mariaUser)
        let! back = FakeApple.post browser seen.Fields
        assertRefused browser back "sign-in-failed"

        let! detail = lastFailure world
        Assert.Contains($"id_token_invalid: {label}", detail)

        let! users = count world "SELECT count(*) FROM users"
        Assert.Equal(0, users)
        do! assertNoSecretsLogged world []
    }

[<Fact>]
let ``an answer from the token endpoint with no id_token is refused`` () =
    task {
        let! world = start ()
        use _ = world.App
        let browser = newBrowser world

        world.Fake.Stub.OmitIdToken <- true

        let! seen = visit browser world None
        let! back = FakeApple.post browser seen.Fields
        assertRefused browser back "sign-in-failed"

        let! detail = lastFailure world
        Assert.Contains("id_token_missing", detail)

        let! users = count world "SELECT count(*) FROM users"
        Assert.Equal(0, users)
        do! assertNoSecretsLogged world []
    }

[<Fact>]
let ``a stored key that is not the one Apple knows is answered invalid_client and refused`` () =
    task {
        let wrong = (ECDsa.Create ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem()

        let! world = startWithKey (fun _ -> wrong)
        use _ = world.App
        let browser = newBrowser world

        let! seen = visit browser world None
        let! back = FakeApple.post browser seen.Fields
        assertRefused browser back "sign-in-failed"

        let! detail = lastFailure world
        Assert.Equal("rejected: invalid_client", detail)

        // Apple was asked, and said no: one request, with a secret.
        Assert.Equal(1, world.Fake.TokenRequests)
        Assert.Equal(1, world.Fake.ClientSecrets.Length)

        let! users = count world "SELECT count(*) FROM users"
        Assert.Equal(0, users)

        // The key that was stored is as secret as the one Apple knows.
        do! assertNoSecretsLogged world [ wrong ]
    }

[<Fact>]
let ``a stored secret that is not a key is refused without asking Apple`` () =
    task {
        let! world = startWithKey (fun _ -> "not a key")
        use _ = world.App
        let browser = newBrowser world

        let! seen = visit browser world None
        let! back = FakeApple.post browser seen.Fields
        assertRefused browser back "sign-in-failed"

        let! detail = lastFailure world
        Assert.Contains("apple_key_unusable", detail)
        Assert.Equal(0, world.Fake.TokenRequests)

        let! users = count world "SELECT count(*) FROM users"
        Assert.Equal(0, users)
        do! assertNoSecretsLogged world []
    }

[<Fact>]
let ``a connector disabled between the start and the post is refused`` () =
    task {
        let! world = start ()
        use _ = world.App
        let browser = newBrowser world

        let! seen = visit browser world None

        let! _ = scalar world "UPDATE connectors SET is_enabled = 0 WHERE slug = 'apple'" []

        let! back = FakeApple.post browser seen.Fields
        assertRefused browser back "sign-in-failed"

        let! detail = lastFailure world
        Assert.Equal("connector_unavailable", detail)
        Assert.Equal(0, world.Fake.TokenRequests)
        do! assertNoSecretsLogged world []
    }
