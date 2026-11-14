/// Spec 019, FR-009: the admin area marks what the declaration owns, shows its
/// owned fields as values, and refuses on the server every route that would
/// change one. The routes are posted to directly, with a real antiforgery token,
/// because a refusal that only hid a button would not be one.
///
/// Asserts on notice codes, `data-*` markers, routes in the HTML and the stored
/// state, never on the wording of a text (the one exception is the marker word,
/// which is read from `Texts`, not typed here).
module Umbral.Tests.DeclaredAdminTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text.RegularExpressions
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Microsoft.Extensions.DependencyInjection
open OpenIddict.Abstractions
open Xunit
open Umbral.Domains
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Views
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

let private webSecret = "SECRETWEB-0123456789-abcdefghijklmnop"

/// A declaration of two apps (`web`, confidential; `spa`, public) and the
/// passkey connector with the given sign-up mode.
let private declaration (signUp: string) =
    $"""{{ "version": 1, "passkeys": {{ "signUp": "{signUp}" }}, "apps": [
    {{ "key": "web", "name": "Web app", "type": "web", "redirectUris": ["https://web.example.com/cb"], "postLogoutRedirectUris": ["https://web.example.com/out"], "connectors": "all-shared" }},
    {{ "key": "spa", "name": "Spa app", "type": "spa", "redirectUris": ["https://spa.example.com/cb"], "postLogoutRedirectUris": [], "connectors": "all-shared" }} ] }}"""

let private declaredSettings (signUp: string) =
    fakeSettings
    @ [ "UMBRAL:DECLARATION", declaration signUp
        "UMBRAL:APP_SECRETS:web", webSecret ]

let private withDirectory (body: string -> Task<unit>) =
    task {
        let directory = Fixtures.freshDataDirectory ()
        Directory.CreateDirectory directory |> ignore

        try
            do! body directory
        finally
            SqliteConnection.ClearAllPools()

            try
                if Directory.Exists directory then
                    Directory.Delete(directory, true)
            with _ ->
                ()
    }

/// One run of the instance on `directory`, gone again before the next.
let private runOn (directory: string) (settings: (string * string) list) (body: Fixtures.TestApp -> Task<unit>) =
    task {
        use! app = Fixtures.createAppIn directory settings ignore
        do! body app
    }

/// The first start of an instance: the operator claims it.
let private claimed (app: Fixtures.TestApp) =
    task {
        let operator = Browser app.Client
        do! claimOperator app operator
        return operator
    }

/// A later start of an instance already claimed: the operator signs in.
let private signedIn (app: Fixtures.TestApp) =
    task {
        let operator = Browser app.Client

        let! response =
            operator.PostForm("/admin/signin", "/admin/signin", [ "credential", OperatorCredential ])

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        return operator
    }

let private scalar (app: Fixtures.TestApp) (statement: string) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- statement
        let! value = command.ExecuteScalarAsync()
        return if isNull value then "" else string value
    }

let private eventCount (app: Fixtures.TestApp) =
    task {
        let! n = scalar app "SELECT count(*) FROM security_events"
        return int n
    }

let private historyRow (app: Fixtures.TestApp) (kind: string) (id: string) =
    task {
        let! n = scalar app $"SELECT count(*) FROM declared_items WHERE kind = '{kind}' AND id = '{id}'"
        return int n
    }

let private t = Texts.current ()

let private pageOf (clientId: string) = $"/admin/apps/{clientId}"

/// The HTML of the list entry of an app.
let private entryOf (html: string) (clientId: string) =
    let m = Regex.Match(html, $"<a\\b[^>]*data-app-entry=\"{Regex.Escape clientId}\"[^>]*>.*?</a>", RegexOptions.Singleline)
    Assert.True(m.Success, $"the list has an entry for {clientId}")
    m.Value

/// What an app is, as far as these routes could change it: both lists, the
/// choice, whether the app exists, whether the declared secret still works.
let private stateOf (app: Fixtures.TestApp) (clientId: string) =
    task {
        use scope = app.Services.CreateScope()
        let manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>()

        match! manager.FindByClientIdAsync clientId with
        | null -> return "gone"
        | application ->
            let! redirects = manager.GetRedirectUrisAsync application
            let! posts = manager.GetPostLogoutRedirectUrisAsync application
            let! choices = AppConnectors.allChoices app.DbConfig
            let choice = choices |> Map.tryFind clientId |> Option.map string |> Option.defaultValue "all-shared"
            let! name = manager.GetDisplayNameAsync application
            let! secretOk = manager.ValidateClientSecretAsync(application, webSecret)
            return $"{name}|{String.Join(',', redirects)}|{String.Join(',', posts)}|{choice}|{secretOk}"
    }

let private passkeyState (app: Fixtures.TestApp) =
    task {
        match! ConnectorStore.findBySlug app.DbConfig "passkey" with
        | None -> return "gone"
        | Some c -> return $"{c.IsEnabled}|{c.IsShared}|{c.PasskeyEnrolment.Value}"
    }

let private assertRefused (response: HttpResponseMessage) (path: string) (notice: string) =
    Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
    Assert.Equal($"{path}?msg={notice}", location response)

// ---------------------------------------------------------------------------
// What the screens show
// ---------------------------------------------------------------------------

[<Fact>]
let ``a declared app is marked in the list and its pane shows owned fields as values`` () =
    withDirectory (fun directory ->
        runOn directory (declaredSettings "invitations") (fun app ->
            task {
                let! operator = claimed app
                let! plainId, _ = registerApp operator "Photos"

                // The list: the second line says it, no pill; the ordinary entry is as before.
                let! list = operator.GetHtml "/admin/apps"
                let declaredEntry = entryOf list "web"
                Assert.Contains($"{t.AppTypeWebOption} · {t.DeclaredBadge}", WebUtility.HtmlDecode declaredEntry)
                Assert.DoesNotContain("um-pill", declaredEntry)
                Assert.Contains($"{t.AppTypeSpaOption} · {t.DeclaredBadge}", WebUtility.HtmlDecode(entryOf list "spa"))
                Assert.DoesNotContain(t.DeclaredBadge, entryOf list plainId)

                // The pane.
                let! html = operator.GetHtml(pageOf "web")
                Assert.Contains("um-pill-muted", html)
                Assert.Contains($">{t.DeclaredBadge}<", html)
                Assert.Contains("data-declared-note", html)
                Assert.Contains("role=\"note\"", html)
                Assert.Contains("um-alert-info", html)
                Assert.Contains("<span class=\"um-mono\">UMBRAL__DECLARATION</span>", html)
                Assert.Contains($"href=\"{Texts.DeclaredAppsUrl}\"", html)
                Assert.Contains("#declared-apps", html)

                // Connection keeps its copy buttons and shows both lists as values.
                Assert.Contains("id=\"client-id-web\"", html)
                Assert.Contains("data-uri=\"https://web.example.com/cb\"", html)
                Assert.Contains("data-uri=\"https://web.example.com/out\"", html)

                // No form that changes an owned field, no closing row, no secret reveal.
                for route in [ "redirect-uris"; "post-logout-uris"; "connectors"; "secret"; "delete" ] do
                    Assert.DoesNotContain($"/admin/apps/web/{route}", html)

                Assert.DoesNotContain("data-app-actions", html)
                Assert.DoesNotContain("data-secret-once", html)

                // Branding is still the operator's.
                Assert.Contains("/admin/apps/web/branding/accent", html)

                // The ordinary app beside it is drawn as before: forms, closing row, no mark.
                let! plain = operator.GetHtml(pageOf plainId)
                Assert.DoesNotContain("data-declared-note", plain)
                Assert.DoesNotContain($">{t.DeclaredBadge}<", plain)
                Assert.Contains($"/admin/apps/{plainId}/redirect-uris", plain)
                Assert.Contains($"/admin/apps/{plainId}/connectors", plain)
                Assert.Contains($"/admin/apps/{plainId}/delete", plain)
                Assert.Contains("data-app-actions", plain)
            }))

[<Fact>]
let ``a declared public app shows its sign-in methods and has no closing row`` () =
    withDirectory (fun directory ->
        runOn directory (declaredSettings "invitations") (fun app ->
            task {
                let! operator = claimed app
                let! html = operator.GetHtml(pageOf "spa")
                Assert.Contains("data-declared-note", html)
                Assert.Contains("data-uri=\"https://spa.example.com/cb\"", html)
                Assert.DoesNotContain("data-app-actions", html)
                Assert.DoesNotContain("/admin/apps/spa/connectors", html)
                Assert.DoesNotContain("name=\"connectorChoice\"", html)
            }))

[<Fact>]
let ``the declared passkey pane marks it, shows label and value lines and has no closing row`` () =
    withDirectory (fun directory ->
        runOn directory (declaredSettings "invitations") (fun app ->
            task {
                let! operator = claimed app

                let! list = operator.GetHtml "/admin/connectors"
                let entry = Regex.Match(list, "<a\\b[^>]*data-connector-entry=\"passkey\"[^>]*>.*?</a>", RegexOptions.Singleline).Value
                Assert.Contains($"passkey</span> · {t.DeclaredBadge}", WebUtility.HtmlDecode entry)
                // The entry keeps its state badge, the one marker it allows.
                Assert.Contains("um-pill-success", entry)

                let! html = operator.GetHtml "/admin/connectors/passkey"
                Assert.Contains($">{t.DeclaredBadge}<", html)
                Assert.Contains("data-declared-note", html)
                Assert.Contains("role=\"note\"", html)
                Assert.Contains("#declared-apps", html)

                // Settings as values: Shared, and who can sign up.
                Assert.Contains("data-declared-settings", html)
                Assert.Equal(2, Regex.Matches(html, "data-value-line").Count)
                Assert.Contains(t.ConnectorSharedLabel, html)
                Assert.Contains(t.DeclaredYes, html)
                Assert.Contains(t.DeclaredWhoCanSignUpLabel, html)
                Assert.Contains(t.PasskeyEnrolmentInvitationsLabel, html)

                // No Save, no toggle, no delete; the Invitations section stays.
                Assert.DoesNotContain("action=\"/admin/connectors/passkey\"", html)
                Assert.DoesNotContain("/admin/connectors/passkey/toggle", html)
                Assert.DoesNotContain("/admin/connectors/passkey/delete", html)
                Assert.DoesNotContain("data-connector-actions", html)
                Assert.Contains("/admin/connectors/passkey/invitations", html)
            }))

[<Fact>]
let ``a declared open sign-up passkey connector says anyone can sign up`` () =
    withDirectory (fun directory ->
        runOn directory (declaredSettings "open") (fun app ->
            task {
                let! operator = claimed app
                let! html = operator.GetHtml "/admin/connectors/passkey"
                Assert.Contains(t.PasskeyEnrolmentOpenLabel, html)
                Assert.DoesNotContain(t.PasskeyEnrolmentInvitationsLabel, html)
            }))

// ---------------------------------------------------------------------------
// The refusals
// ---------------------------------------------------------------------------

[<Fact>]
let ``every route that would change a declared app is refused and changes nothing`` () =
    withDirectory (fun directory ->
        runOn directory (declaredSettings "invitations") (fun app ->
            task {
                let! operator = claimed app
                let! baseline = stateOf app "web"
                let! eventsBefore = eventCount app
                let page = pageOf "web"

                let posts =
                    [ "choice", "/admin/apps/web/connectors", [ "connectorChoice", "chosen" ]
                      "choice confirmed", "/admin/apps/web/connectors?confirm=yes", [ "connectorChoice", "chosen" ]
                      "redirect add", "/admin/apps/web/redirect-uris", [ "uri", "https://evil.example.com/cb" ]
                      "redirect remove", "/admin/apps/web/redirect-uris/remove", [ "uri", "https://web.example.com/cb" ]
                      "post-logout add", "/admin/apps/web/post-logout-uris", [ "uri", "https://evil.example.com/out" ]
                      "post-logout remove", "/admin/apps/web/post-logout-uris/remove", [ "uri", "https://web.example.com/out" ]
                      "secret", "/admin/apps/web/secret", []
                      "delete", "/admin/apps/web/delete", [] ]

                for name, action, fields in posts do
                    let! response = operator.PostForm("/admin/apps", action, fields)
                    assertRefused response page "app-declared"
                    let! now = stateOf app "web"
                    Assert.True((baseline = now), $"{name} left the app as it was")

                let! eventsAfter = eventCount app
                Assert.Equal(eventsBefore, eventsAfter)

                // The refusal is a warning in the pane.
                let! html = operator.GetHtml $"{page}?msg=app-declared"
                Assert.Contains("data-notice=\"app-declared\"", html)
                Assert.Contains("um-alert-warning", html)
                Assert.Contains("data-declared-note", html)
            }))

[<Fact>]
let ``every route that would change the declared passkey connector is refused and changes nothing`` () =
    withDirectory (fun directory ->
        runOn directory (declaredSettings "invitations") (fun app ->
            task {
                let! operator = claimed app
                let! baseline = passkeyState app
                let! eventsBefore = eventCount app
                let page = "/admin/connectors/passkey"

                let posts =
                    [ "save", "/admin/connectors/passkey", [ "enrolment", "open"; "shared", "" ]
                      "save confirmed", "/admin/connectors/passkey?confirm=yes", [ "enrolment", "open" ]
                      "toggle", "/admin/connectors/passkey/toggle", []
                      "toggle confirmed", "/admin/connectors/passkey/toggle?confirm=yes", []
                      "delete", "/admin/connectors/passkey/delete", []
                      "delete confirmed", "/admin/connectors/passkey/delete?confirm=yes", [] ]

                for name, action, fields in posts do
                    let! response = operator.PostForm(page, action, fields)
                    assertRefused response page "connector-declared"
                    let! now = passkeyState app
                    Assert.True((baseline = now), $"{name} left the connector as it was")

                let! eventsAfter = eventCount app
                Assert.Equal(eventsBefore, eventsAfter)

                let! html = operator.GetHtml $"{page}?msg=connector-declared"
                Assert.Contains("data-notice=\"connector-declared\"", html)
                Assert.Contains("um-alert-warning", html)
            }))

[<Fact>]
let ``branding still saves on a declared app`` () =
    withDirectory (fun directory ->
        runOn directory (declaredSettings "invitations") (fun app ->
            task {
                let! operator = claimed app

                let! response =
                    operator.PostForm("/admin/apps", "/admin/apps/web/branding/accent", [ "accent", "#D9480F" ])

                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
                Assert.Equal("/admin/apps/web?msg=app-accent-saved", location response)
                let! stored = scalar app "SELECT accent_color FROM app_branding WHERE client_id = 'web'"
                Assert.Equal("d9480f", stored)
            }))

[<Fact>]
let ``an invitation can still be made on the declared passkey connector`` () =
    withDirectory (fun directory ->
        runOn directory (declaredSettings "invitations") (fun app ->
            task {
                let! operator = claimed app

                let! response =
                    operator.PostForm(
                        "/admin/connectors/passkey",
                        "/admin/connectors/passkey/invitations",
                        [ "note", "for Sam" ]
                    )

                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
                Assert.Equal("/admin/connectors/passkey?msg=passkey-invitation-created", location response)
                let! html = operator.GetHtml "/admin/connectors/passkey"
                Assert.Contains("data-invitation=", html)
            }))

// ---------------------------------------------------------------------------
// When the declaration lets go
// ---------------------------------------------------------------------------

[<Fact>]
let ``after a start without the declaration the app and the connector are ordinary again`` () =
    withDirectory (fun directory ->
        task {
            do! runOn directory (declaredSettings "invitations") (fun app -> task { do! claimed app :> Task })

            do!
                runOn directory fakeSettings (fun app ->
                    task {
                        let! operator = signedIn app

                        // The app: no mark, no note, forms and closing row are back.
                        let! html = operator.GetHtml(pageOf "web")
                        Assert.DoesNotContain("data-declared-note", html)
                        Assert.DoesNotContain($">{t.DeclaredBadge}<", html)
                        Assert.Contains("/admin/apps/web/redirect-uris", html)
                        Assert.Contains("data-app-actions", html)

                        let! list = operator.GetHtml "/admin/apps"
                        Assert.DoesNotContain(t.DeclaredBadge, entryOf list "web")

                        // The routes work.
                        let! response =
                            operator.PostForm(
                                "/admin/apps",
                                "/admin/apps/web/redirect-uris",
                                [ "uri", "https://web.example.com/second" ]
                            )

                        Assert.Equal("/admin/apps/web?msg=app-redirect-uri-added", location response)

                        let! response = operator.PostForm("/admin/apps", "/admin/apps/web/secret", [])
                        Assert.Equal("/admin/apps/web?msg=app-secret-regenerated", location response)

                        // The connector: Save is back, and so is the toggle.
                        let! passkey = operator.GetHtml "/admin/connectors/passkey"
                        Assert.DoesNotContain("data-declared-note", passkey)
                        Assert.Contains("action=\"/admin/connectors/passkey\"", passkey)
                        Assert.Contains("data-connector-actions", passkey)

                        let! response =
                            operator.PostForm(
                                "/admin/connectors/passkey",
                                "/admin/connectors/passkey",
                                [ "enrolment", "open"; "shared", "true" ]
                            )

                        Assert.Equal("/admin/connectors/passkey?msg=passkey-connector-saved", location response)
                        let! state = passkeyState app
                        Assert.Equal("True|True|open", state)
                    })
        })

[<Fact>]
let ``deleting an app that is no longer declared removes its history row`` () =
    withDirectory (fun directory ->
        task {
            do! runOn directory (declaredSettings "invitations") (fun app -> task { do! claimed app :> Task })

            do!
                runOn directory fakeSettings (fun app ->
                    task {
                        let! operator = signedIn app
                        let! rowsBefore = historyRow app "app" "web"
                        Assert.Equal(1, rowsBefore)

                        let! response = operator.PostForm("/admin/apps", "/admin/apps/web/delete", [])
                        Assert.Equal("/admin/apps?msg=app-deleted", location response)

                        let! gone = stateOf app "web"
                        Assert.Equal("gone", gone)
                        let! rowAfter = historyRow app "app" "web"
                        Assert.Equal(0, rowAfter)

                        // The other declared app's row is untouched.
                        let! other = historyRow app "app" "spa"
                        Assert.Equal(1, other)
                    })
        })

[<Fact>]
let ``deleting the passkey connector once it is no longer declared removes its history row`` () =
    withDirectory (fun directory ->
        task {
            do! runOn directory (declaredSettings "invitations") (fun app -> task { do! claimed app :> Task })

            do!
                runOn directory fakeSettings (fun app ->
                    task {
                        let! operator = signedIn app
                        let! rowsBefore = historyRow app "connector" "passkey"
                        Assert.Equal(1, rowsBefore)

                        // Its only enabled connector: the confirmation the screen sets.
                        let! response =
                            operator.PostForm(
                                "/admin/connectors/passkey",
                                "/admin/connectors/passkey/delete?confirm=yes",
                                []
                            )

                        Assert.Equal("/admin/connectors?msg=connector-deleted", location response)
                        let! state = passkeyState app
                        Assert.Equal("gone", state)
                        let! rowAfter = historyRow app "connector" "passkey"
                        Assert.Equal(0, rowAfter)
                    })
        })
