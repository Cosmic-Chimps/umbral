/// What the code review of spec 011 found and this answers (B1, B2, N2, N3):
/// a confirm counts for the action it was made for and no other, a passkey does
/// not confirm its own removal, and a sign-in that a removal or a switch-off
/// overtakes leaves a cookie the next request refuses.
module Umbral.Tests.OperatorConfirmBindingTests

open System.Net
open System.Threading.Tasks
open Xunit
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.Flows
open Umbral.Tests.Infrastructure.PasskeyFlows
open Umbral.Tests.Infrastructure.OperatorFlows

let private wrongAction = "passkey_refused;reason=confirm_not_for_this_action;stage=assertion"

let private withTwoPasskeys (app: Fixtures.TestApp) =
    task {
        let! operator, token = signedInOperator app
        let laptop = operatorAuthenticator ()
        let phone = operatorAuthenticator ()
        let! _, _, laptopId = addOperatorPasskey operator token laptop "Laptop"
        let! _, _, phoneId = addOperatorPasskey operator token phone "Phone"
        return operator, token, laptop, laptopId, phone, phoneId
    }

let private idOf (app: Fixtures.TestApp) (name: string) =
    scalar app "SELECT id FROM operator_passkeys WHERE name = @n" [ "@n", name ]

let private passkeyCheck (ceremony: string, credential: string) =
    [ "ceremony", ceremony; "credential", credential ]

let private switchEvents (app: Fixtures.TestApp) = outcomes app "operator_password_signin_changed"

let private postSwitch (browser: Browser) (state: string) (fields: (string * string) list) =
    browser.PostForm("/admin/credential", "/admin/credential/password-signin", ("state", state) :: fields)

let private postRemove (browser: Browser) (id: string) (fields: (string * string) list) =
    browser.PostForm("/admin/credential", $"/admin/credential/passkeys/{id}/remove", fields)

let private notConfirmed (response: System.Net.Http.HttpResponseMessage) =
    Assert.Contains("operator-passkey-not-confirmed", location response)

// ---------------------------------------------------------------------------
// B2: a confirm is for one action
// ---------------------------------------------------------------------------

[<Fact>]
let ``a confirm made for another action does not add a passkey, and is burned`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, token, laptop, laptopId, _, _ = withTwoPasskeys app
        let! ceremony, credential = confirmAnswer operator token "switch-off" laptop laptopId

        let! options, body =
            postJson operator "/admin/credential/passkeys/options" token {| name = "Tablet"; ceremony = ceremony; credential = credential |}

        Assert.Equal(HttpStatusCode.BadRequest, options.StatusCode)
        Assert.Equal("operator-passkey-not-confirmed", strOf body "error")
        let! events = outcomes app "operator_passkey_added"
        Assert.Equal(("failure", Some wrongAction), List.last events)

        // Burned: the same answer no longer works for the action it was made for.
        let! again = postSwitch operator "off" [ "ceremony", ceremony; "credential", credential.GetRawText() ]
        notConfirmed again
        let! on = OperatorPasskeys.passwordSignInEnabled app.DbConfig
        Assert.True on
    }

[<Fact>]
let ``a confirm made for another action does not switch password sign-in off, and is burned`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, token, laptop, laptopId, _, _ = withTwoPasskeys app
        let! phone = idOf app "Phone"
        let! answer = confirmCeremony operator token $"remove:{phone}" laptop laptopId

        let! response = postSwitch operator "off" (passkeyCheck answer)
        notConfirmed response
        let! on = OperatorPasskeys.passwordSignInEnabled app.DbConfig
        Assert.True on
        let! events = switchEvents app
        Assert.Equal<(string * string option) list>([ "failure", Some wrongAction ], events)

        // Burned: the same answer does not remove the passkey either.
        let! removed = postRemove operator phone (passkeyCheck answer)
        notConfirmed removed
        let! held = scalar app "SELECT count(*) FROM operator_passkeys" []
        Assert.Equal("2", held)
    }

[<Fact>]
let ``a confirm made for another action does not switch password sign-in on`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, token, laptop, laptopId, _, _ = withTwoPasskeys app
        let! off = confirmCeremony operator token "switch-off" laptop laptopId
        let! _ = postSwitch operator "off" (passkeyCheck off)
        let! on = OperatorPasskeys.passwordSignInEnabled app.DbConfig
        Assert.False on

        let! answer = confirmCeremony operator token "switch-off" laptop laptopId
        let! response = postSwitch operator "on" (passkeyCheck answer)
        notConfirmed response
        let! stillOff = OperatorPasskeys.passwordSignInEnabled app.DbConfig
        Assert.False stillOff
    }

[<Fact>]
let ``a confirm made for another action does not remove a passkey, and is burned`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, token, laptop, laptopId, _, _ = withTwoPasskeys app
        let! phone = idOf app "Phone"
        let! ceremony, credential = confirmCeremony operator token "add" laptop laptopId

        let! response = postRemove operator phone (passkeyCheck (ceremony, credential))
        notConfirmed response
        let! held = scalar app "SELECT count(*) FROM operator_passkeys" []
        Assert.Equal("2", held)
        let! events = outcomes app "operator_passkey_removed"
        Assert.Equal(("failure", Some wrongAction), List.last events)

        // Burned: the answer does not start an add for the action it was made for.
        let json = System.Text.Json.JsonDocument.Parse(credential).RootElement.Clone()

        let! options, _ =
            postJson operator "/admin/credential/passkeys/options" token {| name = "Tablet"; ceremony = ceremony; credential = json |}

        Assert.Equal(HttpStatusCode.BadRequest, options.StatusCode)
    }

[<Fact>]
let ``a confirm made for removing one passkey does not remove another`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, token, laptop, laptopId, _, _ = withTwoPasskeys app
        let! _ = addOperatorPasskey operator token (operatorAuthenticator ()) "Tablet"
        let! phone = idOf app "Phone"
        let! tablet = idOf app "Tablet"

        let! answer = confirmCeremony operator token $"remove:{phone}" laptop laptopId
        let! response = postRemove operator tablet (passkeyCheck answer)
        notConfirmed response
        let! held = scalar app "SELECT count(*) FROM operator_passkeys" []
        Assert.Equal("3", held)
    }

[<Fact>]
let ``a confirm for an action the server does not know is not started`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, token = signedInOperator app

        for action in [ "format-disk"; "remove:"; ""; "REMOVE:x" ] do
            let! response, body = postJson operator "/admin/credential/confirm/options" token {| action = action |}
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
            Assert.Equal("operator-passkey-not-confirmed", strOf body "error")

        let! missing, _ = postJson operator "/admin/credential/confirm/options" token {||}
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode)
    }

// ---------------------------------------------------------------------------
// N3: another passkey, not the one being removed
// ---------------------------------------------------------------------------

[<Fact>]
let ``a passkey cannot confirm its own removal`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, token, laptop, laptopId, phone, phoneId = withTwoPasskeys app
        let! laptopRow = idOf app "Laptop"

        let! own = confirmCeremony operator token $"remove:{laptopRow}" laptop laptopId
        let! refused = postRemove operator laptopRow (passkeyCheck own)
        notConfirmed refused
        let! events = outcomes app "operator_passkey_removed"
        Assert.Equal(("failure", Some wrongAction), List.last events)
        let! held = scalar app "SELECT count(*) FROM operator_passkeys" []
        Assert.Equal("2", held)

        let! other = confirmCeremony operator token $"remove:{laptopRow}" phone phoneId
        let! removed = postRemove operator laptopRow (passkeyCheck other)
        Assert.Contains("operator-passkey-removed", location removed)
    }

// ---------------------------------------------------------------------------
// N2: no passkey sign-in before setup
// ---------------------------------------------------------------------------

[<Fact>]
let ``the passkey sign-in post refuses before first-run setup`` () =
    task {
        use! app = Fixtures.createApp ()
        let browser = Browser(app.Client)
        let! token = browser.TokenFrom "/admin/setup"

        let! response, _ = postJson browser "/admin/signin/passkey" token {| ceremony = "x"; credential = {| id = "y" |} |}

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        let! events = outcomes app "operator_sign_in_failed"
        Assert.Empty events
    }

// ---------------------------------------------------------------------------
// B1: a sign-in that is overtaken leaves a stale cookie
// ---------------------------------------------------------------------------

/// The operator-signed-in event is written after the answer is verified and
/// before the cookie is made. A trigger on that insert stands in for a removal
/// or a switch-off landing in exactly that gap: it moves the session stamp.
let private overtakeSignIn (app: Fixtures.TestApp) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()

        command.CommandText <-
            "CREATE TRIGGER overtake AFTER INSERT ON security_events
             WHEN NEW.event_type = 'operator_signed_in'
             BEGIN
               INSERT OR REPLACE INTO settings (key, value, updated_utc) VALUES ('operator_session_stamp', 'moved-by-the-test', NEW.occurred_utc);
             END"

        let! _ = command.ExecuteNonQueryAsync()
        ()
    }

let private admitted (browser: Browser) =
    task {
        let! response = browser.Get "/admin/credential"
        return response.StatusCode = HttpStatusCode.OK
    }

[<Fact>]
let ``a passkey sign-in overtaken by a stamp change leaves a cookie that is refused`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, token = signedInOperator app
        let authenticator = operatorAuthenticator ()
        let! _, _, credentialId = addOperatorPasskey operator token authenticator "Laptop"
        let none = SoftwareAuthenticator.AssertOverrides.None

        // Without the overtaking change the same sign-in opens a session.
        let! fresh, response, _ = signInWithOperatorPasskey app authenticator credentialId none
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        let! open' = admitted fresh
        Assert.True open'

        do! overtakeSignIn app
        let! late, overtaken, _ = signInWithOperatorPasskey app authenticator credentialId none
        Assert.Equal(HttpStatusCode.OK, overtaken.StatusCode)
        let! stale = admitted late
        Assert.False stale
    }

[<Fact>]
let ``a password sign-in overtaken by a stamp change leaves a cookie that is refused`` () =
    task {
        use! app = Fixtures.createApp ()
        let! _ = signedInOperator app

        let signIn () =
            task {
                let browser = Browser(app.Client)
                let! response = browser.PostForm("/admin/signin", "/admin/signin", [ "credential", OperatorCredential ])
                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
                Assert.Contains("/admin/connectors", location response)
                return browser
            }

        let! fresh = signIn ()
        let! open' = admitted fresh
        Assert.True open'

        do! overtakeSignIn app
        let! late = signIn ()
        let! stale = admitted late
        Assert.False stale
    }
