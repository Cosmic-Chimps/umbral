/// Whole-product flows for the operator's passkeys (spec 011): adding one,
/// confirming with one, signing in with one, all through the real routes with
/// a software authenticator in place of the browser's.
module Umbral.Tests.Infrastructure.OperatorFlows

open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Xunit
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.Flows
open Umbral.Tests.Infrastructure.PasskeyFlows

let operatorAuthenticator () =
    SoftwareAuthenticator.SoftwareAuthenticator("umbral.test", Fixtures.TestOrigin)

/// An operator signed in with the password, and the antiforgery token of the
/// Credential screen, which the JSON routes under it take in their header.
let signedInOperator (app: Fixtures.TestApp) : Task<Browser * string> =
    task {
        let browser = Browser(app.Client)
        do! claimOperator app browser
        let! token = browser.TokenFrom "/admin/credential"
        return browser, token
    }

/// The answer of an existing operator passkey to a fresh confirm ceremony made
/// for `action` (`add`, `switch-off`, `switch-on`, `remove:<id>`): the ceremony
/// id and the credential as the browser would send it.
let confirmAnswer
    (browser: Browser)
    (token: string)
    (action: string)
    (authenticator: SoftwareAuthenticator.SoftwareAuthenticator)
    (credentialId: byte array)
    : Task<string * JsonElement> =
    task {
        let! response, started = postJson browser "/admin/credential/confirm/options" token {| action = action |}
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        let credential = authenticator.Assert(started.GetProperty "options", credentialId)
        return strOf started "ceremony", credential
    }

/// `confirmAnswer` for a form post, where the credential is a JSON string.
let confirmCeremony
    (browser: Browser)
    (token: string)
    (action: string)
    (authenticator: SoftwareAuthenticator.SoftwareAuthenticator)
    (credentialId: byte array)
    : Task<string * string> =
    task {
        let! ceremony, credential = confirmAnswer browser token action authenticator credentialId
        return ceremony, credential.GetRawText()
    }

/// Registration options already obtained: the authenticator's answer, posted.
let finishAdd
    (browser: Browser)
    (token: string)
    (authenticator: SoftwareAuthenticator.SoftwareAuthenticator)
    (started: JsonElement)
    : Task<HttpResponseMessage * JsonElement> =
    task {
        let credential = authenticator.Register(started.GetProperty "options")
        return! postJson browser "/admin/credential/passkeys" token {| ceremony = strOf started "ceremony"; credential = credential |}
    }

/// Adds a passkey with the password as the re-check. Returns the answer of
/// the completing request and the new credential's id.
let addOperatorPasskey
    (browser: Browser)
    (token: string)
    (authenticator: SoftwareAuthenticator.SoftwareAuthenticator)
    (name: string)
    : Task<HttpResponseMessage * JsonElement * byte array> =
    task {
        let! options, started =
            postJson browser "/admin/credential/passkeys/options" token {| name = name; current = OperatorCredential |}

        Assert.Equal(HttpStatusCode.OK, options.StatusCode)
        let! response, body = finishAdd browser token authenticator started
        return response, body, (List.last authenticator.Credentials).Id
    }

/// Adds a passkey with a passkey as the re-check: `confirming` is the
/// credential of `confirmer` that answers the confirm ceremony.
let addOperatorPasskeyConfirmedBy
    (browser: Browser)
    (token: string)
    (authenticator: SoftwareAuthenticator.SoftwareAuthenticator)
    (name: string)
    (confirmer: SoftwareAuthenticator.SoftwareAuthenticator)
    (confirming: byte array)
    : Task<HttpResponseMessage * JsonElement * byte array> =
    task {
        let! ceremony, credential = confirmAnswer browser token "add" confirmer confirming

        let! options, started =
            postJson
                browser
                "/admin/credential/passkeys/options"
                token
                {| name = name; ceremony = ceremony; credential = credential |}

        Assert.Equal(HttpStatusCode.OK, options.StatusCode)
        let! response, body = finishAdd browser token authenticator started
        return response, body, (List.last authenticator.Credentials).Id
    }

/// A fresh browser on the sign-in page with the ceremony it started.
let startOperatorSignIn (app: Fixtures.TestApp) : Task<Browser * string * JsonElement> =
    task {
        let browser = Browser(app.Client)
        let! token = browser.TokenFrom "/admin/signin"
        let! response, started = postJson browser "/admin/signin/passkey/options" token {||}
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        return browser, token, started
    }

/// Signs in from a fresh browser with a credential the authenticator holds.
/// Returns the browser (to look for a session) with the answer.
let signInWithOperatorPasskey
    (app: Fixtures.TestApp)
    (authenticator: SoftwareAuthenticator.SoftwareAuthenticator)
    (credentialId: byte array)
    (overrides: SoftwareAuthenticator.AssertOverrides)
    : Task<Browser * HttpResponseMessage * JsonElement> =
    task {
        let! browser, token, started = startOperatorSignIn app
        let credential = authenticator.Assert(started.GetProperty "options", credentialId, overrides)

        let! response, body =
            postJson browser "/admin/signin/passkey" token {| ceremony = strOf started "ceremony"; credential = credential |}

        return browser, response, body
    }

/// (outcome, detail) of the events of one type, oldest first.
let outcomes (app: Fixtures.TestApp) (eventType: string) : Task<(string * string option) list> =
    task {
        use! connection = Umbral.Infrastructure.Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT outcome, detail FROM security_events WHERE event_type = @t ORDER BY id"
        command.Parameters.AddWithValue("@t", eventType) |> ignore
        use! reader = command.ExecuteReaderAsync()
        let rows = System.Collections.Generic.List<_>()

        while reader.Read() do
            rows.Add((reader.GetString 0, (if reader.IsDBNull 1 then None else Some(reader.GetString 1))))

        return List.ofSeq rows
    }
