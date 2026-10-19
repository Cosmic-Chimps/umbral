/// Whole-product flows for the passkey connector (spec 008): the operator's
/// enable-and-invite steps and a person's ceremonies, all through the real
/// routes, with a software authenticator in place of the browser's.
module Umbral.Tests.Infrastructure.PasskeyFlows

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading.Tasks
open Xunit
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.Flows

/// POST a JSON body with the antiforgery token in its header. A body that is
/// not JSON comes back as an empty object.
let postJson (browser: Browser) (path: string) (token: string) (body: obj) : Task<HttpResponseMessage * JsonElement> =
    task {
        let message = new HttpRequestMessage(HttpMethod.Post, path)
        message.Content <- new StringContent(JsonSerializer.Serialize body, Encoding.UTF8, "application/json")
        message.Headers.Add("RequestVerificationToken", token)
        let! response = browser.Send message
        let! text = response.Content.ReadAsStringAsync()

        let parsed =
            try
                JsonDocument.Parse(text).RootElement.Clone()
            with _ ->
                JsonDocument.Parse("{}").RootElement.Clone()

        return response, parsed
    }

/// Enable the passkey connector (shared) with the given enrolment word.
let enablePasskeyConnector (browser: Browser) (enrolment: string) : Task<unit> =
    task {
        let! response = browser.PostForm("/admin/connectors", "/admin/connectors/passkey", [ "shared", "true"; "enrolment", enrolment ])
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
    }

/// Invite a new person; returns the link the card reveals once.
let inviteNewPerson (browser: Browser) (note: string) : Task<string> =
    task {
        let! response = browser.PostForm("/admin/connectors", "/admin/connectors/passkey/invitations", [ "note", note ])
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        let! html = browser.GetHtml(location response)
        let m = Regex.Match(html, "<input[^>]*data-invitation-link[^>]*>")
        Assert.True(m.Success, "the card shows the invitation link")
        let v = Regex.Match(m.Value, "value=\"([^\"]+)\"")
        return string (System.Net.WebUtility.HtmlDecode(v.Groups[1].Value))
    }

/// The secret of an invitation link: its last path segment.
let secretOf (link: string) : string =
    link.TrimEnd('/').Split('/') |> Array.last

/// Open an invitation page; returns its HTML and the antiforgery token.
let openInvitation (browser: Browser) (secret: string) : Task<string * string> =
    task {
        let! html = browser.GetHtml $"/invite/{secret}"
        let m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")
        return html, (if m.Success then m.Groups[1].Value else "")
    }

/// Register through an invitation: options, then the authenticator's answer.
let registerThroughInvitation
    (browser: Browser)
    (authenticator: SoftwareAuthenticator.SoftwareAuthenticator)
    (secret: string)
    (displayName: string option)
    (nickname: string)
    : Task<HttpResponseMessage * JsonElement> =
    task {
        let! _, token = openInvitation browser secret

        let! _, started =
            postJson
                browser
                "/passkey/registration/options"
                token
                {| mode = "invitation"
                   invitation = secret
                   displayName = (displayName |> Option.toObj)
                   nickname = nickname |}

        let credential = authenticator.Register(started.GetProperty "options")
        return! postJson browser "/passkey/registration" token {| ceremony = strOf started "ceremony"; credential = credential |}
    }

/// Authorize → picker → passkey page. Returns the returnUrl, the page and a token.
let startPasskeySignIn (browser: Browser) (clientId: string) (scope: string) : Task<string * string * string> =
    task {
        let! toPicker = browser.Get(authorizeUrl clientId scope)
        Assert.Equal(HttpStatusCode.Redirect, toPicker.StatusCode)
        let picker = location toPicker
        let! pickerHtml = browser.GetHtml picker
        Assert.Contains("data-connector=\"passkey\"", pickerHtml)
        let raw = Regex.Match(picker, "returnUrl=([^&]+)").Groups[1].Value
        let! page = browser.GetHtml $"/connect/passkey/start?returnUrl={raw}"
        let m = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")
        return Uri.UnescapeDataString raw, page, (if m.Success then m.Groups[1].Value else "")
    }

/// Sign in with a credential the authenticator holds.
let signInWithPasskey
    (browser: Browser)
    (authenticator: SoftwareAuthenticator.SoftwareAuthenticator)
    (credentialId: byte array)
    (returnUrl: string)
    (token: string)
    : Task<HttpResponseMessage * JsonElement> =
    task {
        let! _, started = postJson browser "/passkey/assertion/options" token {| returnUrl = returnUrl |}
        let credential = authenticator.Assert(started.GetProperty "options", credentialId)
        return! postJson browser "/passkey/assertion" token {| ceremony = strOf started "ceremony"; credential = credential |}
    }

/// Follow the authorize redirect to the app's redirect URI; returns the code.
let codeFromRedirect (browser: Browser) (redirect: string) : Task<string> =
    task {
        let! response = browser.Get redirect
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        return Uri.UnescapeDataString(Regex.Match(location response, "code=([^&]+)").Groups[1].Value)
    }

/// (user_id, connector_slug, detail) of the events of one type, oldest first.
let events (app: Fixtures.TestApp) (eventType: string) : Task<(string option * string option * string option) list> =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT user_id, connector_slug, detail FROM security_events WHERE event_type = @t ORDER BY id"
        command.Parameters.AddWithValue("@t", eventType) |> ignore
        use! reader = command.ExecuteReaderAsync()
        let rows = Collections.Generic.List<_>()
        let opt (i: int) = if reader.IsDBNull i then None else Some(reader.GetString i)

        while reader.Read() do
            rows.Add((opt 0, opt 1, opt 2))

        return List.ofSeq rows
    }
