/// The switch that turns the operator's password sign-in off and on (spec 011,
/// user story 2), through the real routes: the passkey ceremony it takes, the
/// sessions it ends, and what a password can no longer do while it is off.
module Umbral.Tests.OperatorPasswordSwitchTests

open System.Net
open System.Threading.Tasks
open Xunit
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.Flows
open Umbral.Tests.Infrastructure.PasskeyFlows
open Umbral.Tests.Infrastructure.OperatorFlows

let private switchPath = "/admin/credential/password-signin"

/// The switch form as the card posts it, with whatever re-check fields the
/// test brings.
let private postSwitch (browser: Browser) (state: string) (fields: (string * string) list) =
    browser.PostForm("/admin/credential", switchPath, ("state", state) :: fields)

let private passkeyCheck (ceremony: string, credential: string) =
    [ "ceremony", ceremony; "credential", credential ]

let private withOnePasskey (app: Fixtures.TestApp) =
    task {
        let! operator, token = signedInOperator app
        let authenticator = operatorAuthenticator ()
        let! response, _, credentialId = addOperatorPasskey operator token authenticator "Laptop"
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        return operator, token, authenticator, credentialId
    }

let private isSignedIn (browser: Browser) =
    task {
        let! response = browser.Get "/admin/users"
        return response.StatusCode = HttpStatusCode.OK
    }

let private switchEvents (app: Fixtures.TestApp) = outcomes app "operator_password_signin_changed"

/// A second browser signed in with the password, the way another device would be.
let private secondBrowser (app: Fixtures.TestApp) =
    task {
        let browser = Browser(app.Client)
        let! _ = browser.PostForm("/admin/signin", "/admin/signin", [ "credential", OperatorCredential ])
        let! signedIn = isSignedIn browser
        Assert.True signedIn
        return browser
    }

[<Fact>]
let ``turning it off with no passkey is refused with the next action`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, _ = signedInOperator app

        let! response = postSwitch operator "off" []
        Assert.Contains("operator-password-off-needs-passkey", location response)

        let! on = OperatorPasskeys.passwordSignInEnabled app.DbConfig
        Assert.True on
        let! events = switchEvents app
        Assert.Equal<(string * string option) list>([ "failure", Some "no_passkey" ], events)
    }

[<Fact>]
let ``turning it off with no re-check draws the confirm block and changes nothing`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, _, _, _ = withOnePasskey app

        let! response = postSwitch operator "off" []
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode)
        let! html = response.Content.ReadAsStringAsync()
        Assert.Contains("data-confirm=\"switch-off\"", html)

        let! on = OperatorPasskeys.passwordSignInEnabled app.DbConfig
        Assert.True on
        let! events = switchEvents app
        Assert.Empty events
    }

[<Fact>]
let ``a password is not a re-check for turning it off`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, _, _, _ = withOnePasskey app

        let! response = postSwitch operator "off" [ "current", OperatorCredential ]
        Assert.Contains("credential-current-incorrect", location response)

        let! on = OperatorPasskeys.passwordSignInEnabled app.DbConfig
        Assert.True on
        let! events = switchEvents app
        Assert.Equal<(string * string option) list>([ "failure", Some "recheck_password_mismatch" ], events)
    }

[<Fact>]
let ``turning it off with a passkey ends the other sessions and keeps this one`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, token, authenticator, credentialId = withOnePasskey app
        let! other = secondBrowser app

        let! answer = confirmCeremony operator token "switch-off" authenticator credentialId
        let! response = postSwitch operator "off" (passkeyCheck answer)
        Assert.Contains("operator-password-off", location response)

        let! on = OperatorPasskeys.passwordSignInEnabled app.DbConfig
        Assert.False on

        let! otherStill = isSignedIn other
        Assert.False otherStill
        let! thisStill = isSignedIn operator
        Assert.True thisStill

        let! events = switchEvents app
        Assert.Equal<(string * string option) list>([ "success", Some "off" ], events)
    }

[<Fact>]
let ``while it is off a password does nothing and no screen asks for one`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, token, authenticator, credentialId = withOnePasskey app
        let! answer = confirmCeremony operator token "switch-off" authenticator credentialId
        let! _ = postSwitch operator "off" (passkeyCheck answer)

        // The right password signs nobody in.
        let stranger = Browser(app.Client)
        let! signIn = stranger.PostForm("/admin/signin", "/admin/signin", [ "credential", OperatorCredential ])
        Assert.Contains("signin-incorrect", location signIn)
        let! opened = isSignedIn stranger
        Assert.False opened

        // Nor does it re-check an added passkey.
        let! token = operator.TokenFrom "/admin/credential"

        let! options, body =
            postJson operator "/admin/credential/passkeys/options" token {| name = "Phone"; current = OperatorCredential |}

        Assert.Equal(HttpStatusCode.BadRequest, options.StatusCode)
        Assert.Equal("credential-current-incorrect", strOf body "error")

        // Nor replace the stored password.
        let replacement = "a-brand-new-credential"

        let! replaced =
            operator.PostForm(
                "/admin/credential",
                "/admin/credential",
                [ "current", OperatorCredential
                  "credential", replacement
                  "confirm", replacement ]
            )

        Assert.Contains("credential-current-incorrect", location replaced)

        // And neither screen draws a password field.
        let! credentialPage = operator.GetHtml "/admin/credential"
        Assert.DoesNotContain("type=\"password\"", credentialPage)
        let! signInPage = Browser(app.Client).GetHtml "/admin/signin"
        Assert.DoesNotContain("type=\"password\"", signInPage)

        // The stored password is the old one: it works again once the switch is on.
        let! answer = confirmCeremony operator token "switch-on" authenticator credentialId
        let! back = postSwitch operator "on" (passkeyCheck answer)
        Assert.Contains("operator-password-on", location back)

        let! oldWorks = AdminAuth.verify app.DbConfig OperatorCredential
        Assert.True oldWorks
        let! newWorks = AdminAuth.verify app.DbConfig replacement
        Assert.False newWorks
    }

[<Fact>]
let ``turning it on with a passkey lets the old password sign in again`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, token, authenticator, credentialId = withOnePasskey app
        let! answer = confirmCeremony operator token "switch-off" authenticator credentialId
        let! _ = postSwitch operator "off" (passkeyCheck answer)

        let! answer = confirmCeremony operator token "switch-on" authenticator credentialId
        let! response = postSwitch operator "on" (passkeyCheck answer)
        Assert.Contains("operator-password-on", location response)

        let! on = OperatorPasskeys.passwordSignInEnabled app.DbConfig
        Assert.True on

        let! signedIn = secondBrowser app
        let! ok = isSignedIn signedIn
        Assert.True ok

        let! events = switchEvents app
        Assert.Equal<(string * string option) list>([ "success", Some "off"; "success", Some "on" ], events)
    }

[<Fact>]
let ``a switch to the state it is already in changes nothing and writes nothing`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, _, _, _ = withOnePasskey app

        let! on = postSwitch operator "on" []
        Assert.Equal("/admin/credential", location on)

        let! junk = postSwitch operator "sideways" []
        Assert.Equal("/admin/credential", location junk)

        let! events = switchEvents app
        Assert.Empty events
    }
