module Umbral.Tests.PasskeyCeremonyTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open System.Text.RegularExpressions
open Xunit
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.Flows
open Umbral.Tests.Infrastructure.PasskeyFlows

let private authenticator () = SoftwareAuthenticator.SoftwareAuthenticator("umbral.test", Fixtures.TestOrigin)

/// A claimed instance with the passkey connector (invitations) and an app.
let private prepare (app: Fixtures.TestApp) =
    task {
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! enablePasskeyConnector operator "invitations"
        let! clientId, secret = registerApp operator "Grafana"
        return operator, clientId, secret
    }

let private unusableWith (app: Fixtures.TestApp) (secret: string) (reason: string) =
    task {
        let visitor = Browser(app.Client)
        let! html, _ = openInvitation visitor secret
        Assert.Contains("data-screen=\"invitation-unusable\"", html)
        Assert.Contains("This invitation link can&#39;t be used", html.Replace("can't", "can&#39;t"))
        let! failed = events app "sign_in_failed"
        let last = List.last failed
        let _, _, detail = last
        Assert.Equal(Some $"passkey_refused;reason={reason};stage=invitation", detail)
    }

[<Fact>]
let ``enabling the connector shows an enabled shared card and records the event`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! enablePasskeyConnector operator "invitations"
        let! html = operator.GetHtml "/admin/connectors"
        Assert.Contains("data-connector=\"passkey\"", html)
        Assert.Contains("data-connector-state=\"enabled\"", html)
        Assert.Contains("data-connector-shared=\"shared\"", html)
        let! created = events app "connector_created"
        Assert.Equal<string option list>([ Some "enrolment=invitations" ], created |> List.map (fun (_, _, d) -> d))
    }

[<Fact>]
let ``an invitation link is shown once and listed as open`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! enablePasskeyConnector operator "invitations"
        let! _ = inviteNewPerson operator "Ana"
        let! again = operator.GetHtml "/admin/connectors"
        Assert.DoesNotContain("data-invitation-link", again)
        Assert.Equal(1, Regex.Matches(again, "<li[^>]*data-invitation=").Count)
        let! created = events app "passkey_invitation_created"
        Assert.Equal<string option list>([ Some "for=new" ], created |> List.map (fun (_, _, d) -> d))
    }

[<Fact>]
let ``the invitation page asks for a name and a nickname`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! enablePasskeyConnector operator "invitations"
        let! link = inviteNewPerson operator "Ana"
        let! html, _ = openInvitation (Browser(app.Client)) (secretOf link)
        Assert.Contains("data-screen=\"invitation\"", html)
        Assert.Contains("name=\"displayName\"", html)
        Assert.Contains("name=\"nickname\"", html)
    }

[<Fact>]
let ``registering through an invitation creates the person, the passkey and a session`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, clientId, _ = prepare app
        let! link = inviteNewPerson operator "Ana"
        let person = Browser(app.Client)
        let! response, body = registerThroughInvitation person (authenticator ()) (secretOf link) (Some "Ana") "Phone"
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        Assert.Equal("/account/passkeys?msg=passkey-added", strOf body "redirect")

        let! userId = scalar app "SELECT id FROM users WHERE display_name = 'Ana' AND email IS NULL" []
        Assert.NotEqual<string>("", userId)
        let! count = scalar app "SELECT count(*) FROM passkeys WHERE user_id = @u AND nickname = 'Phone'" [ "@u", userId ]
        Assert.Equal("1", count)
        let! used = scalar app "SELECT count(*) FROM passkey_invitations WHERE used_utc IS NOT NULL" []
        Assert.Equal("1", used)

        // The session cookie is on the browser: authorize goes straight to the app.
        let! authorized = person.Get(authorizeUrl clientId "openid")
        Assert.Contains("code=", location authorized)

        let! createdUser = events app "user_created"
        Assert.Contains((Some userId, Some "passkey", None), createdUser |> List.map (fun (u, c, _) -> u, c, None))
        let! registered = events app "passkey_registered"
        Assert.Equal<(string option * string option * string option) list>([ Some userId, Some "passkey", Some "via=invitation" ], registered)
    }

[<Fact>]
let ``a used, expired, revoked or unknown invitation is refused with its reason`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, _ = prepare app

        let! used = inviteNewPerson operator "used"
        let! r, _ = registerThroughInvitation (Browser(app.Client)) (authenticator ()) (secretOf used) (Some "Ana") "Phone"
        Assert.Equal(HttpStatusCode.OK, r.StatusCode)
        do! unusableWith app (secretOf used) "invitation_used"

        do! unusableWith app "no-such-secret" "invitation_unknown"

        let! revoked = inviteNewPerson operator "revoked"
        let! html = operator.GetHtml "/admin/connectors"
        let id = Regex.Match(html, "data-invitation=\"([^\"]+)\"").Groups[1].Value
        let! revoke = operator.PostForm("/admin/connectors", $"/admin/connectors/passkey/invitations/{id}/revoke", [])
        Assert.Equal(HttpStatusCode.Redirect, revoke.StatusCode)
        do! unusableWith app (secretOf revoked) "invitation_revoked"

        let! expiring = inviteNewPerson operator "expiring"
        app.Clock.Advance(TimeSpan.FromDays 8.0)
        do! unusableWith app (secretOf expiring) "invitation_expired"
    }

/// A person with one passkey, and the authenticator that holds it.
let private enrolled (app: Fixtures.TestApp) (operator: Browser) =
    task {
        let! link = inviteNewPerson operator "Ana"
        let authenticator = authenticator ()
        let! r, _ = registerThroughInvitation (Browser(app.Client)) authenticator (secretOf link) (Some "Ana") "Phone"
        Assert.Equal(HttpStatusCode.OK, r.StatusCode)
        let! userId = scalar app "SELECT id FROM users WHERE display_name = 'Ana'" []
        return authenticator, userId
    }

[<Fact>]
let ``signing in with a passkey yields tokens without an email, and again the same sub`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, clientId, secret = prepare app
        let! authenticator, userId = enrolled app operator
        let credentialId = (List.head authenticator.Credentials).Id

        let browser = Browser(app.Client)
        let! returnUrl, page, token = startPasskeySignIn browser clientId "openid profile email"
        Assert.Contains("data-screen=\"passkey-signin\"", page)
        Assert.Contains("data-passkey-signin", page)
        Assert.Contains("data-invitations-only", page)
        Assert.DoesNotContain("data-passkey-signup", page)
        Assert.Contains("<noscript data-javascript-required", page)

        let! response, body = signInWithPasskey browser authenticator credentialId returnUrl token
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        Assert.Equal(returnUrl, strOf body "redirect")

        let! code = codeFromRedirect browser (strOf body "redirect")
        let! exchanged = exchangeCode app clientId secret code
        let! tokens = json exchanged
        let idToken = segmentOf 1 (strOf tokens "id_token")
        Assert.Equal(userId, strOf idToken "sub")
        Assert.Equal("Ana", strOf idToken "name")
        Assert.Equal("passkey", strOf idToken "idp")
        Assert.False(idToken.TryGetProperty("email", ref Unchecked.defaultof<JsonElement>))
        Assert.False(idToken.TryGetProperty("email_verified", ref Unchecked.defaultof<JsonElement>))

        let message = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo")
        message.Headers.Add("Authorization", "Bearer " + strOf tokens "access_token")
        let! info = app.Client.SendAsync message
        let! infoBody = json info
        Assert.False(infoBody.TryGetProperty("email", ref Unchecked.defaultof<JsonElement>))

        let! succeeded = events app "sign_in_succeeded"
        Assert.Equal<(string option * string option * string option) list>([ Some userId, Some "passkey", None ], succeeded)
        let! count = scalar app "SELECT sign_count FROM passkeys WHERE user_id = @u" [ "@u", userId ]
        Assert.Equal("1", count)
        let! last = scalar app "SELECT count(*) FROM passkeys WHERE user_id = @u AND last_used_utc IS NOT NULL" [ "@u", userId ]
        Assert.Equal("1", last)

        // Again, same credential, same person.
        let again = Browser(app.Client)
        let! returnUrl2, _, token2 = startPasskeySignIn again clientId "openid"
        let! response2, body2 = signInWithPasskey again authenticator credentialId returnUrl2 token2
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode)
        let! code2 = codeFromRedirect again (strOf body2 "redirect")
        let! exchanged2 = exchangeCode app clientId secret code2
        let! tokens2 = json exchanged2
        Assert.Equal(userId, strOf (segmentOf 1 (strOf tokens2 "id_token")) "sub")
    }

[<Fact>]
let ``the passkey routes are 404 without an enabled connector`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser(app.Client)
        do! claimOperator app operator
        let visitor = Browser(app.Client)

        let! none = visitor.Get "/connect/passkey/start?returnUrl=%2F"
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode)

        do! enablePasskeyConnector operator "invitations"
        // A second enabled connector, so turning this one off is not the "last door" case.
        do! seedFakeConnector app
        let! toggled = operator.PostForm("/admin/connectors", "/admin/connectors/passkey/toggle", [])
        Assert.Equal(HttpStatusCode.Redirect, toggled.StatusCode)
        let! disabled = visitor.Get "/connect/passkey/start?returnUrl=%2F"
        Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode)

    }

/// The generic callback answers every slug the same way before it looks the
/// connector up: no correlation cookie is a failed sign-in, sent back to the
/// sign-in page. A passkey has no provider round trip, so this route can never
/// complete for it: with a correlation it would still find no adapter for the
/// kind. What matters is that it signs nobody in.
[<Fact>]
let ``the passkey callback route cannot sign anyone in`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, _, _ = prepare app
        let visitor = Browser(app.Client)
        let! callback = visitor.Get "/connect/passkey/callback?code=anything&state=anything"
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode)
        Assert.StartsWith("/signin?", location callback)
        Assert.Contains("msg=sign-in-failed", location callback)
        Assert.False(visitor.Cookies.ContainsKey "umbral_session", "a passkey callback established a session")
    }

[<Fact>]
let ``the sign-in page carries the app's accent only when it has one`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, clientId, _ = prepare app

        let bodyTag (html: string) = Regex.Match(html, "<body[^>]*>").Value

        let! _, unbranded, _ = startPasskeySignIn (Browser(app.Client)) clientId "openid"
        Assert.DoesNotContain("data-accent", bodyTag unbranded)

        let! saved = operator.PostForm("/admin/apps", $"/admin/apps/{clientId}/branding/accent", [ "accent", "#f26b1d" ])
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode)
        let! _, branded, _ = startPasskeySignIn (Browser(app.Client)) clientId "openid"
        Assert.Contains("data-accent", bodyTag branded)
    }

// ---------------------------------------------------------------------------
// Refusals (User Story 2): every refusal is a 400 with a fixed notice, one
// `sign_in_failed` event with a closed reason word, no session and no new
// person or passkey.
// ---------------------------------------------------------------------------

type private Counts = { Users: string; Passkeys: string; Failed: string; AllEvents: string }

let private countsOf (app: Fixtures.TestApp) =
    task {
        let! users = scalar app "SELECT count(*) FROM users" []
        let! passkeys = scalar app "SELECT count(*) FROM passkeys" []
        let! failed = scalar app "SELECT count(*) FROM security_events WHERE event_type = 'sign_in_failed'" []
        let! all = scalar app "SELECT count(*) FROM security_events" []
        return { Users = users; Passkeys = passkeys; Failed = failed; AllEvents = all }
    }

let private assertRefused
    (app: Fixtures.TestApp)
    (before: Counts)
    (visitor: Browser)
    (response: HttpResponseMessage, body: JsonElement)
    (notice: string)
    (reason: string)
    (stage: string)
    =
    task {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        Assert.Equal(notice, strOf body "error")
        let! after = countsOf app
        Assert.Equal(before.Users, after.Users)
        Assert.Equal(before.Passkeys, after.Passkeys)
        Assert.Equal(string (int before.Failed + 1), after.Failed)
        let! failed = events app "sign_in_failed"
        let _, _, detail = List.last failed
        Assert.Equal(Some $"passkey_refused;reason={reason};stage={stage}", detail)
        Assert.False(visitor.Cookies.ContainsKey "umbral_session", "no session was started")
    }

/// A visitor on the sign-in page, and its antiforgery token.
let private visit (app: Fixtures.TestApp) =
    task {
        let visitor = Browser(app.Client)
        let! token = visitor.TokenFrom "/connect/passkey/start?returnUrl=%2F"
        return visitor, token
    }

let private assertionOptions (visitor: Browser) (token: string) =
    task {
        let! _, started = postJson visitor "/passkey/assertion/options" token {| returnUrl = "/" |}
        return started
    }

/// Ana, with a passkey, and Bea, with another.
let private twoPeople (app: Fixtures.TestApp) (operator: Browser) =
    task {
        let! ana, anaId = enrolled app operator
        let! link = inviteNewPerson operator "Bea"
        let bea = authenticator ()
        let! r, _ = registerThroughInvitation (Browser(app.Client)) bea (secretOf link) (Some "Bea") "Key"
        Assert.Equal(HttpStatusCode.OK, r.StatusCode)
        let! beaId = scalar app "SELECT id FROM users WHERE display_name = 'Bea'" []
        return ana, anaId, bea, beaId
    }

/// One refused sign-in attempt by Ana's authenticator with `overrides`.
let private refusedSignIn (overrides: SoftwareAuthenticator.AssertOverrides) (reason: string) =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, _ = prepare app
        let! ana, _ = enrolled app operator
        let credentialId = (List.head ana.Credentials).Id
        let! visitor, token = visit app
        let! started = assertionOptions visitor token
        let! before = countsOf app
        let credential = ana.Assert(started.GetProperty "options", credentialId, overrides)
        let! result = postJson visitor "/passkey/assertion" token {| ceremony = strOf started "ceremony"; credential = credential |}
        do! assertRefused app before visitor result "passkey-sign-in-failed" reason "assertion"
    }

[<Fact>]
let ``an assertion for another origin is refused as origin_mismatch`` () =
    refusedSignIn { SoftwareAuthenticator.AssertOverrides.None with Origin = Some "https://evil.example" } "origin_mismatch"

[<Fact>]
let ``an assertion for another RP id is refused as rpid_mismatch`` () =
    refusedSignIn { SoftwareAuthenticator.AssertOverrides.None with RpId = Some "evil.example" } "rpid_mismatch"

[<Fact>]
let ``an assertion without user verification is refused`` () =
    refusedSignIn { SoftwareAuthenticator.AssertOverrides.None with Flags = Some 0x01uy } "user_verification_missing"

[<Fact>]
let ``an assertion without user presence is refused`` () =
    refusedSignIn { SoftwareAuthenticator.AssertOverrides.None with Flags = Some 0x04uy } "user_presence_missing"

[<Fact>]
let ``an assertion for an unknown credential id is refused`` () =
    refusedSignIn
        { SoftwareAuthenticator.AssertOverrides.None with CredentialId = Some(Security.Cryptography.RandomNumberGenerator.GetBytes 16) }
        "unknown_credential"

[<Fact>]
let ``an assertion of the wrong type is refused`` () =
    refusedSignIn { SoftwareAuthenticator.AssertOverrides.None with Type = Some "webauthn.create" } "type_mismatch"

[<Fact>]
let ``an assertion without a credential is refused as malformed`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, _ = prepare app
        let! _ = enrolled app operator
        let! visitor, token = visit app
        let! started = assertionOptions visitor token
        let! before = countsOf app
        let! result = postJson visitor "/passkey/assertion" token {| ceremony = strOf started "ceremony" |}
        do! assertRefused app before visitor result "passkey-sign-in-failed" "malformed_response" "assertion"
    }

[<Fact>]
let ``an assertion carrying another person's handle is refused`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, _ = prepare app
        let! ana, _, _, beaId = twoPeople app operator
        let credentialId = (List.head ana.Credentials).Id
        let! visitor, token = visit app
        let! started = assertionOptions visitor token
        let! before = countsOf app
        let overrides = { SoftwareAuthenticator.AssertOverrides.None with UserHandle = Some(Text.Encoding.UTF8.GetBytes beaId) }
        let credential = ana.Assert(started.GetProperty "options", credentialId, overrides)
        let! result = postJson visitor "/passkey/assertion" token {| ceremony = strOf started "ceremony"; credential = credential |}
        do! assertRefused app before visitor result "passkey-sign-in-failed" "user_handle_mismatch" "assertion"
    }

[<Fact>]
let ``an assertion signed with another credential's key is refused`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, _ = prepare app
        let! ana, _, bea, _ = twoPeople app operator
        let credentialId = (List.head ana.Credentials).Id
        let! visitor, token = visit app
        let! started = assertionOptions visitor token
        let! before = countsOf app
        // Bea's authenticator answers with her key but under Ana's credential id
        // and handle: the signature cannot verify against Ana's stored key.
        let beaKey = List.head bea.Credentials
        let anaHandle = (List.head ana.Credentials).UserHandle
        let credential =
            bea.Assert(
                started.GetProperty "options",
                beaKey.Id,
                { SoftwareAuthenticator.AssertOverrides.None with
                    CredentialId = Some credentialId
                    UserHandle = Some anaHandle }
            )

        let! result = postJson visitor "/passkey/assertion" token {| ceremony = strOf started "ceremony"; credential = credential |}
        do! assertRefused app before visitor result "passkey-sign-in-failed" "invalid_signature" "assertion"
    }

[<Fact>]
let ``a ceremony id used twice is refused the second time`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, _ = prepare app
        let! ana, _ = enrolled app operator
        let credentialId = (List.head ana.Credentials).Id
        let! visitor, token = visit app
        let! started = assertionOptions visitor token
        let respond () = ana.Assert(started.GetProperty "options", credentialId)
        let! first, _ = postJson visitor "/passkey/assertion" token {| ceremony = strOf started "ceremony"; credential = respond () |}
        Assert.Equal(HttpStatusCode.OK, first.StatusCode)
        visitor.Cookies.Remove "umbral_session" |> ignore
        let! before = countsOf app
        let! result = postJson visitor "/passkey/assertion" token {| ceremony = strOf started "ceremony"; credential = respond () |}
        do! assertRefused app before visitor result "passkey-sign-in-failed" "ceremony_unknown_or_expired" "assertion"
    }

[<Fact>]
let ``an assertion on a ceremony older than five minutes is refused`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, _ = prepare app
        let! ana, _ = enrolled app operator
        let credentialId = (List.head ana.Credentials).Id
        let! visitor, token = visit app
        let! started = assertionOptions visitor token
        app.Clock.Advance(TimeSpan.FromMinutes 6.)
        let! before = countsOf app
        let credential = ana.Assert(started.GetProperty "options", credentialId)
        let! result = postJson visitor "/passkey/assertion" token {| ceremony = strOf started "ceremony"; credential = credential |}
        do! assertRefused app before visitor result "passkey-sign-in-failed" "ceremony_unknown_or_expired" "assertion"
    }

[<Fact>]
let ``a counter that does not advance is refused, a credential that always reports zero is not`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, _ = prepare app
        let! ana, _ = enrolled app operator
        let credentialId = (List.head ana.Credentials).Id
        let attempt (counter: uint32 option) (browser: Browser) (token: string) =
            task {
                let! started = assertionOptions browser token
                let credential =
                    ana.Assert(started.GetProperty "options", credentialId, { SoftwareAuthenticator.AssertOverrides.None with Counter = counter })
                return! postJson browser "/passkey/assertion" token {| ceremony = strOf started "ceremony"; credential = credential |}
            }

        // Counter 0, twice: accepted, as the authenticators that never count do.
        for _ in 1..2 do
            // A signed-in browser's antiforgery token no longer fits a new
            // visitor, so each attempt starts from a fresh one.
            let! visitor, token = visit app
            let! r, _ = attempt (Some 0u) visitor token
            Assert.Equal(HttpStatusCode.OK, r.StatusCode)

        let! visitor1, token1 = visit app
        let! r1, _ = attempt (Some 1u) visitor1 token1
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode)

        let! visitor, token = visit app
        let! before = countsOf app
        let! result = attempt (Some 1u) visitor token
        do! assertRefused app before visitor result "passkey-sign-in-failed" "sign_count_regressed" "assertion"
    }

[<Fact>]
let ``a disabled person is refused with account-disabled`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, _ = prepare app
        let! ana, anaId = enrolled app operator
        let credentialId = (List.head ana.Credentials).Id
        let! toggled = operator.PostForm("/admin/users", $"/admin/users/{anaId}/toggle", [])
        Assert.Equal(HttpStatusCode.Redirect, toggled.StatusCode)
        let! visitor, token = visit app
        let! started = assertionOptions visitor token
        let! before = countsOf app
        let credential = ana.Assert(started.GetProperty "options", credentialId)
        let! result = postJson visitor "/passkey/assertion" token {| ceremony = strOf started "ceremony"; credential = credential |}
        do! assertRefused app before visitor result "account-disabled" "user_disabled" "assertion"
    }

/// One registration through a fresh invitation, the response built with `overrides`.
let private refusedRegistration (overrides: SoftwareAuthenticator.RegisterOverrides) (reason: string) =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, _ = prepare app
        let! link = inviteNewPerson operator "Ana"
        let secret = secretOf link
        let visitor = Browser(app.Client)
        let! _, token = openInvitation visitor secret

        let! _, started =
            postJson visitor "/passkey/registration/options" token {| mode = "invitation"; invitation = secret; displayName = "Ana"; nickname = "Phone" |}

        let! before = countsOf app
        let credential = (authenticator ()).Register(started.GetProperty "options", overrides)
        let! result = postJson visitor "/passkey/registration" token {| ceremony = strOf started "ceremony"; credential = credential |}
        do! assertRefused app before visitor result "passkey-registration-failed" reason "registration"
    }

[<Fact>]
let ``a registration without a discoverable credential is refused`` () =
    refusedRegistration { SoftwareAuthenticator.RegisterOverrides.None with Rk = Some false } "passkey_not_discoverable"

[<Fact>]
let ``a registration for another origin is refused`` () =
    refusedRegistration { SoftwareAuthenticator.RegisterOverrides.None with Origin = Some "https://evil.example" } "origin_mismatch"

[<Fact>]
let ``a registration of the wrong type is refused`` () =
    refusedRegistration { SoftwareAuthenticator.RegisterOverrides.None with Type = Some "webauthn.get" } "type_mismatch"

[<Fact>]
let ``a registration ceremony id used twice is refused the second time`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, _ = prepare app
        let! link = inviteNewPerson operator "Ana"
        let secret = secretOf link
        let visitor = Browser(app.Client)
        let! _, token = openInvitation visitor secret

        let! _, started =
            postJson visitor "/passkey/registration/options" token {| mode = "invitation"; invitation = secret; displayName = "Ana"; nickname = "Phone" |}

        let respond () = (authenticator ()).Register(started.GetProperty "options")
        let! first, _ = postJson visitor "/passkey/registration" token {| ceremony = strOf started "ceremony"; credential = respond () |}
        Assert.Equal(HttpStatusCode.OK, first.StatusCode)
        visitor.Cookies.Remove "umbral_session" |> ignore
        let! before = countsOf app
        let! result = postJson visitor "/passkey/registration" token {| ceremony = strOf started "ceremony"; credential = respond () |}
        do! assertRefused app before visitor result "passkey-registration-failed" "ceremony_unknown_or_expired" "registration"
    }

[<Fact>]
let ``signing up while the connector is invitations-only is refused`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _ = prepare app
        let! visitor, token = visit app
        let! before = countsOf app

        let! result =
            postJson visitor "/passkey/registration/options" token {| mode = "signup"; displayName = "Mallory"; nickname = "Phone"; returnUrl = "/" |}

        do! assertRefused app before visitor result "passkey-registration-failed" "enrolment_closed" "registration"
    }

[<Fact>]
let ``two different refusals end on the same page text, with no reason word`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _ = prepare app
        let alert (html: string) = Regex.Match(html, "role=\"alert\"[^>]*>(.*?)</", RegexOptions.Singleline).Groups[1].Value.Trim()
        let! first = Browser(app.Client).GetHtml "/connect/passkey/start?returnUrl=%2F&msg=passkey-sign-in-failed"
        let! second = Browser(app.Client).GetHtml "/connect/passkey/start?returnUrl=%2F&msg=passkey-sign-in-failed"
        Assert.NotEqual<string>("", alert first)
        Assert.Equal(alert first, alert second)

        for word in [ "origin_mismatch"; "rpid_mismatch"; "unknown_credential"; "sign_count_regressed"; "user_handle_mismatch" ] do
            Assert.DoesNotContain(word, first)
    }

[<Fact>]
let ``the 31st assertion start from one source is limited`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _ = prepare app
        let! visitor, token = visit app

        let send () =
            task {
                let message = new HttpRequestMessage(HttpMethod.Post, "/passkey/assertion/options")
                message.Content <- new StringContent("""{"returnUrl":"/"}""", Text.Encoding.UTF8, "application/json")
                message.Headers.Add("RequestVerificationToken", token)
                message.Headers.Add("X-Forwarded-For", "203.0.113.50")
                return! visitor.Send message
            }

        for _ in 1..30 do
            let! r = send ()
            Assert.NotEqual(HttpStatusCode.TooManyRequests, r.StatusCode)

        let! over = send ()
        Assert.Equal(HttpStatusCode.TooManyRequests, over.StatusCode)
        Assert.True(over.Headers.Contains "Retry-After")
    }

[<Fact>]
let ``a passkey POST without the antiforgery header is refused and changes nothing`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _ = prepare app
        let! visitor, _ = visit app
        let! before = countsOf app
        let message = new HttpRequestMessage(HttpMethod.Post, "/passkey/assertion/options")
        message.Content <- new StringContent("""{"returnUrl":"/"}""", Text.Encoding.UTF8, "application/json")
        let! response = visitor.Send message
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        let! after = countsOf app
        Assert.Equal(before, after)
    }

// ---------------------------------------------------------------------------
// Open sign-up (spec 008, User Story 4)
// ---------------------------------------------------------------------------

let private openUp (operator: Browser) =
    task {
        let! response = operator.PostForm("/admin/connectors", "/admin/connectors/passkey", [ "shared", "true"; "enrolment", "open" ])
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        return response
    }

let private signupOptions (visitor: Browser) (token: string) (displayName: string) (nickname: string) (returnUrl: string) =
    postJson visitor "/passkey/registration/options" token {| mode = "signup"; displayName = displayName; nickname = nickname; returnUrl = returnUrl |}

[<Fact>]
let ``saving the card open shows the sign-up form and records the change`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, clientId, _ = prepare app
        let! saved = openUp operator
        Assert.Contains("passkey-connector-saved", location saved)
        let! updated = events app "connector_updated"
        Assert.Contains(Some "enrolment=open", updated |> List.map (fun (_, _, d) -> d))
        let! _, page, _ = startPasskeySignIn (Browser(app.Client)) clientId "openid"
        Assert.Contains("data-passkey-signup", page)
        Assert.DoesNotContain("data-invitations-only", page)
    }

[<Fact>]
let ``anyone can sign up with a passkey when the connector is open`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, clientId, secret = prepare app
        let! _ = openUp operator
        let visitor = Browser(app.Client)
        let! returnUrl, _, token = startPasskeySignIn visitor clientId "openid profile"
        let! _, started = signupOptions visitor token "Noor" "" returnUrl
        let credential = (authenticator ()).Register(started.GetProperty "options")
        let! response, body = postJson visitor "/passkey/registration" token {| ceremony = strOf started "ceremony"; credential = credential |}
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        Assert.Equal(returnUrl, strOf body "redirect")

        let! userId = scalar app "SELECT id FROM users WHERE display_name = 'Noor' AND email IS NULL" []
        Assert.NotEqual<string>("", userId)
        let! count = scalar app "SELECT count(*) FROM passkeys WHERE user_id = @u AND nickname LIKE 'Passkey added%'" [ "@u", userId ]
        Assert.Equal("1", count)
        Assert.True(visitor.Cookies.ContainsKey "umbral_session")

        let! createdUser = events app "user_created"
        Assert.Contains(Some userId, createdUser |> List.map (fun (u, _, _) -> u))
        let! registered = events app "passkey_registered"
        Assert.Equal<(string option * string option * string option) list>([ Some userId, Some "passkey", Some "via=signup" ], registered)

        let! code = codeFromRedirect visitor returnUrl
        let! exchanged = exchangeCode app clientId secret code
        let! tokens = json exchanged
        let idToken = segmentOf 1 (strOf tokens "id_token")
        Assert.Equal("Noor", strOf idToken "name")
        Assert.Equal("passkey", strOf idToken "idp")
        Assert.False(idToken.TryGetProperty("email", ref Unchecked.defaultof<JsonElement>))
    }

[<Fact>]
let ``a sign-up whose connector was closed meanwhile is refused at completion`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, clientId, _ = prepare app
        let! _ = openUp operator
        let visitor = Browser(app.Client)
        let! returnUrl, _, token = startPasskeySignIn visitor clientId "openid"
        let! _, started = signupOptions visitor token "Noor" "" returnUrl
        do! enablePasskeyConnector operator "invitations"
        let credential = (authenticator ()).Register(started.GetProperty "options")
        let! before = countsOf app
        let! result = postJson visitor "/passkey/registration" token {| ceremony = strOf started "ceremony"; credential = credential |}
        do! assertRefused app before visitor result "passkey-registration-failed" "enrolment_closed" "registration"
    }

[<Fact>]
let ``an invalid name or nickname is refused when signing up`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, _ = prepare app
        let! _ = openUp operator
        let! visitor, token = visit app
        let long = String('x', 65)

        for name, nickname, reason in [ "", "", "display_name_invalid"; long, "", "display_name_invalid"; "Noor", long, "nickname_invalid" ] do
            let! before = countsOf app
            let! result = signupOptions visitor token name nickname "/"
            do! assertRefused app before visitor result "passkey-registration-failed" reason "registration"
    }
