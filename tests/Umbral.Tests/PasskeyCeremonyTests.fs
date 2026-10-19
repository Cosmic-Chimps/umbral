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
