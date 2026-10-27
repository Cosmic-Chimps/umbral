/// Removing an operator passkey (spec 011, user story 3), through the real
/// route: the re-check it takes, the sessions it ends, and the one passkey that
/// stays while the password is off.
module Umbral.Tests.OperatorPasskeyRemoveTests

open System.Net
open Xunit
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.Flows
open Umbral.Tests.Infrastructure.PasskeyFlows
open Umbral.Tests.Infrastructure.OperatorFlows

let private removePath (id: string) = $"/admin/credential/passkeys/{id}/remove"

let private removeEvents (app: Fixtures.TestApp) = outcomes app "operator_passkey_removed"

let private count (app: Fixtures.TestApp) =
    task {
        let! n = scalar app "SELECT count(*) FROM operator_passkeys" []
        return int n
    }

let private idOf (app: Fixtures.TestApp) (name: string) =
    scalar app "SELECT id FROM operator_passkeys WHERE name = @n" [ "@n", name ]

let private isSignedIn (browser: Browser) =
    task {
        let! response = browser.Get "/admin/users"
        return response.StatusCode = HttpStatusCode.OK
    }

/// Two passkeys, "Laptop" and "Phone", each on its own authenticator.
let private withTwoPasskeys (app: Fixtures.TestApp) =
    task {
        let! operator, token = signedInOperator app
        let laptop = operatorAuthenticator ()
        let phone = operatorAuthenticator ()
        let! _, _, laptopId = addOperatorPasskey operator token laptop "Laptop"
        let! _, _, phoneId = addOperatorPasskey operator token phone "Phone"
        return operator, token, laptop, laptopId, phone, phoneId
    }

let private withOnePasskey (app: Fixtures.TestApp) =
    task {
        let! operator, token = signedInOperator app
        let laptop = operatorAuthenticator ()
        let! _, _, laptopId = addOperatorPasskey operator token laptop "Laptop"
        return operator, token, laptop, laptopId
    }

let private post (browser: Browser) (id: string) (fields: (string * string) list) =
    browser.PostForm("/admin/credential", removePath id, fields)

[<Fact>]
let ``removing with no re-check draws that row's confirm block and removes nothing`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, _, _, _, _, _ = withTwoPasskeys app
        let! phone = idOf app "Phone"

        let! response = post operator phone []
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode)
        let! html = response.Content.ReadAsStringAsync()
        Assert.Contains("data-confirm=\"remove\"", html)
        Assert.Contains($"data-passkey-row=\"{phone}\"", html)

        let! n = count app
        Assert.Equal(2, n)
        let! events = removeEvents app
        Assert.Empty events
    }

[<Fact>]
let ``removing with the password removes it, refuses its sign-in and ends the other sessions`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, _, _, _, phone, phoneCredential = withTwoPasskeys app
        let phoneId = (idOf app "Phone")

        let other = Browser(app.Client)
        let! _ = other.PostForm("/admin/signin", "/admin/signin", [ "credential", OperatorCredential ])
        let! otherIn = isSignedIn other
        Assert.True otherIn

        let! id = phoneId
        let! response = post operator id [ "current", OperatorCredential ]
        Assert.Contains("operator-passkey-removed", location response)

        let! n = count app
        Assert.Equal(1, n)

        let! browser, signIn, _ =
            signInWithOperatorPasskey app phone phoneCredential SoftwareAuthenticator.AssertOverrides.None

        Assert.Equal(HttpStatusCode.BadRequest, signIn.StatusCode)
        let! opened = isSignedIn browser
        Assert.False opened

        let! otherStill = isSignedIn other
        Assert.False otherStill
        let! thisStill = isSignedIn operator
        Assert.True thisStill

        let! events = removeEvents app
        Assert.Equal<(string * string option) list>([ "success", None ], events)
    }

[<Fact>]
let ``another passkey's confirm is a re-check for removing`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, token, laptop, laptopId, _, _ = withTwoPasskeys app
        let! phone = idOf app "Phone"

        let! ceremony, credential = confirmCeremony operator token $"remove:{phone}" laptop laptopId
        let! response = post operator phone [ "ceremony", ceremony; "credential", credential ]
        Assert.Contains("operator-passkey-removed", location response)

        let! gone = scalar app "SELECT count(*) FROM operator_passkeys WHERE name = 'Phone'" []
        Assert.Equal("0", gone)
    }

[<Fact>]
let ``a wrong password is refused and the passkey stays`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, _, _, _, _, _ = withTwoPasskeys app
        let! phone = idOf app "Phone"

        let! response = post operator phone [ "current", "not-the-password" ]
        Assert.Contains("credential-current-incorrect", location response)

        let! n = count app
        Assert.Equal(2, n)
        let! events = removeEvents app
        Assert.Equal<(string * string option) list>([ "failure", Some "recheck_password_mismatch" ], events)
    }

[<Fact>]
let ``the last passkey stays while password sign-in is off`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, token, laptop, laptopId = withOnePasskey app
        let id = idOf app "Laptop"

        let! ceremony, credential = confirmCeremony operator token "switch-off" laptop laptopId
        let! off = operator.PostForm("/admin/credential", "/admin/credential/password-signin", [ "state", "off"; "ceremony", ceremony; "credential", credential ])
        Assert.Contains("operator-password-off", location off)

        let! id = id
        let! ceremony, credential = confirmCeremony operator token $"remove:{id}" laptop laptopId
        let! response = post operator id [ "ceremony", ceremony; "credential", credential ]
        Assert.Contains("operator-passkey-last", location response)

        let! n = count app
        Assert.Equal(1, n)
        let! events = removeEvents app
        Assert.Equal<(string * string option) list>([ "failure", Some "last_passkey_password_off" ], events)
    }

[<Fact>]
let ``the last passkey goes while password sign-in is on`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, _, _, _ = withOnePasskey app
        let! id = idOf app "Laptop"

        let! response = post operator id [ "current", OperatorCredential ]
        Assert.Contains("operator-passkey-removed", location response)

        let! n = count app
        Assert.Equal(0, n)
    }

[<Fact>]
let ``an unknown id says nothing and is recorded`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator, _, _, _ = withOnePasskey app

        let! response = post operator "no-such-passkey" [ "current", OperatorCredential ]
        Assert.Equal("/admin/credential", location response)

        let! n = count app
        Assert.Equal(1, n)
        let! events = removeEvents app
        Assert.Equal<(string * string option) list>([ "failure", Some "not_found" ], events)
    }
