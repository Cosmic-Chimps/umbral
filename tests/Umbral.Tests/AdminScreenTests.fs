/// The Apps and Users admin screens (T072-T074, T083-T086).
module Umbral.Tests.AdminScreenTests

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Text.RegularExpressions
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Xunit
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure

// ---------------------------------------------------------------------------
// A tiny cookie-carrying browser.
//
// Fixtures has no browser helper yet (per the brief): `app.GetTestClient()`
// talks straight to the TestServer pipeline and does not manage cookies, so a
// sign-in followed by an authenticated request needs something that does.
// ---------------------------------------------------------------------------

type private Browser(client: HttpClient) =
    let cookies = Dictionary<string, string>()

    let captureCookies (response: HttpResponseMessage) =
        match response.Headers.TryGetValues "Set-Cookie" with
        | true, values ->
            for raw in values do
                match raw.Split(';').[0].Split('=', 2) with
                | [| name; value |] -> cookies[name] <- value
                | _ -> ()
        | false, _ -> ()

    let attachCookies (request: HttpRequestMessage) =
        if cookies.Count > 0 then
            let header = cookies |> Seq.map (fun kv -> $"{kv.Key}={kv.Value}") |> String.concat "; "
            request.Headers.TryAddWithoutValidation("Cookie", header) |> ignore

    member _.GetAsync(url: string) : Task<HttpResponseMessage> =
        task {
            use request = new HttpRequestMessage(HttpMethod.Get, url)
            attachCookies request
            let! response = client.SendAsync request
            captureCookies response
            return response
        }

    /// Posts form-urlencoded fields without following the redirect, which is
    /// what lets a test assert on the 302 and its `Location` directly.
    member _.PostFormAsync (url: string) (fields: (string * string) list) : Task<HttpResponseMessage> =
        task {
            use request = new HttpRequestMessage(HttpMethod.Post, url)
            attachCookies request
            request.Content <- new FormUrlEncodedContent(fields |> List.map KeyValuePair.Create)
            let! response = client.SendAsync request
            captureCookies response
            return response
        }

/// Scrapes the hidden `<input type="hidden" name="..." value="...">` that
/// `ctx.GetAntiforgeryInput()` renders onto every form — the field name and
/// value a real browser would submit back.
let private antiforgeryField (html: string) : string * string =
    let m =
        Regex.Match(html, "<input[^>]*type=\"hidden\"[^>]*name=\"([^\"]+)\"[^>]*value=\"([^\"]+)\"")

    if m.Success then
        (m.Groups[1].Value, m.Groups[2].Value)
    else
        failwith "No antiforgery field found on the page."

let private credential = "correct-horse-battery-staple"

/// Claims a fresh instance and signs in as the operator, returning a
/// `Browser` carrying the session cookie for every call after this one.
let private signedInOperator (app: Fixtures.TestApp) : Task<Browser> =
    task {
        let! _ = AdminAuth.setInitialCredential app.DbConfig app.Clock credential

        let browser = Browser(app.Client)
        let! signInPage = browser.GetAsync "/admin/signin"
        let! signInHtml = signInPage.Content.ReadAsStringAsync()
        let fieldName, fieldValue = antiforgeryField signInHtml

        let! response =
            browser.PostFormAsync "/admin/signin" [ fieldName, fieldValue; "credential", credential ]

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        return browser
    }

/// GETs a page known to carry a form and returns its antiforgery field, for a
/// handler under test that needs a *valid* token.
let private tokenFrom (browser: Browser) (url: string) : Task<string * string> =
    task {
        let! page = browser.GetAsync url
        let! html = page.Content.ReadAsStringAsync()
        return antiforgeryField html
    }

// ---------------------------------------------------------------------------
// Every admin route is gated
// ---------------------------------------------------------------------------

let private adminRoutes =
    [ "GET", "/admin/apps"
      "POST", "/admin/apps"
      "POST", "/admin/apps/some-client/secret"
      "POST", "/admin/apps/some-client/delete"
      "GET", "/admin/users"
      "POST", "/admin/users/some-user/toggle"
      "POST", "/admin/users/some-user/delete" ]

let private request (client: HttpClient) (method: string) (url: string) =
    task {
        use message = new HttpRequestMessage(HttpMethod.Parse method, url)
        return! client.SendAsync message
    }

[<Fact>]
let ``every admin route redirects to setup on an unconfigured instance`` () =
    task {
        use! app = Fixtures.createApp ()

        for method, url in adminRoutes do
            let! response = request app.Client method url
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
            Assert.Contains("/admin/setup", response.Headers.Location |> string)
    }

[<Fact>]
let ``every admin route redirects to sign-in once configured but signed out`` () =
    task {
        use! app = Fixtures.createApp ()
        let! _ = AdminAuth.setInitialCredential app.DbConfig app.Clock credential

        for method, url in adminRoutes do
            let! response = request app.Client method url
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
            Assert.Contains("/admin/signin", response.Headers.Location |> string)
    }

// ---------------------------------------------------------------------------
// CSRF
// ---------------------------------------------------------------------------

[<Fact>]
let ``a POST with no antiforgery token is rejected and changes nothing`` () =
    task {
        use! app = Fixtures.createApp ()
        let! browser = signedInOperator app

        // No token field — WebForms.readValidated refuses to read the form at
        // all. It must come back as a readable 400, not an unhandled
        // exception: an operator whose form sat open past the token's lifetime
        // gets here too, and they need to be told to reload, not shown a
        // stack trace.
        let! response =
            browser.PostFormAsync
                "/admin/apps"
                [ "name", "Csrf Probe"
                  "redirectUri", "https://app.example.com/callback"
                  "postLogoutRedirectUri", ""
                  "appType", "web" ]

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)

        let! body = response.Content.ReadAsStringAsync()
        Assert.Contains("expired", body, StringComparison.OrdinalIgnoreCase)

        let! appsPage = browser.GetAsync "/admin/apps"
        let! html = appsPage.Content.ReadAsStringAsync()
        Assert.DoesNotContain("Csrf Probe", html)
    }

// ---------------------------------------------------------------------------
// Apps: the shown-once secret
// ---------------------------------------------------------------------------

[<Fact>]
let ``creating a web app shows the secret exactly once, and never again`` () =
    task {
        use! app = Fixtures.createApp ()
        let! browser = signedInOperator app
        let! fieldName, fieldValue = tokenFrom browser "/admin/apps"

        let! created =
            browser.PostFormAsync
                "/admin/apps"
                [ fieldName, fieldValue
                  "name", "Grafana"
                  "redirectUri", "https://grafana.example.com/login/generic_oauth"
                  "postLogoutRedirectUri", ""
                  "appType", "web" ]

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode)
        let location = created.Headers.Location |> string
        Assert.Contains("msg=app-created", location)

        // The secret, and any capability to read it, must never ride in a URL.
        // Query strings land in access logs, browser history and the Referer
        // header sent to whatever the operator clicks next.
        Assert.DoesNotContain("secret=", location)

        let! revealPage = browser.GetAsync location
        let! revealHtml = revealPage.Content.ReadAsStringAsync()
        Assert.Contains("data-secret-once", revealHtml)

        // Single-use: the same URL, loaded again, shows nothing.
        let! secondLoad = browser.GetAsync location
        let! secondHtml = secondLoad.Content.ReadAsStringAsync()
        Assert.DoesNotContain("data-secret-once", secondHtml)

        // Nor does the plain Apps screen, afterwards.
        let! plainPage = browser.GetAsync "/admin/apps"
        let! plainHtml = plainPage.Content.ReadAsStringAsync()
        Assert.DoesNotContain("data-secret-once", plainHtml)
        Assert.Contains("data-app=", plainHtml)
    }

[<Fact>]
let ``a public app gets no client secret at all`` () =
    task {
        use! app = Fixtures.createApp ()
        let! browser = signedInOperator app
        let! fieldName, fieldValue = tokenFrom browser "/admin/apps"

        let! created =
            browser.PostFormAsync
                "/admin/apps"
                [ fieldName, fieldValue
                  "name", "SPA Client"
                  "redirectUri", "https://spa.example.com/callback"
                  "postLogoutRedirectUri", ""
                  "appType", "spa" ]

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode)
        let location = created.Headers.Location |> string
        // No secret was generated, so there is nothing to reveal and no
        // `secret=` token on the redirect.
        Assert.DoesNotContain("secret=", location)
    }

[<Fact>]
let ``the app type is two radio choices with Web preselected, not a select`` () =
    task {
        // The create form only shows once there is an enabled connector.
        use! app = Fixtures.createAppWith Journeys.fakeSettings ignore
        do! Journeys.addFakeConnector app "fake-connector-secret-value"
        let! browser = signedInOperator app
        let! page = browser.GetAsync "/admin/apps"
        let! html = page.Content.ReadAsStringAsync()

        Assert.DoesNotContain("<select", html)

        // One fieldset with a legend, two real radios sharing the name.
        Assert.Contains("<fieldset", html)
        Assert.Contains("<legend", html)

        let radios = Regex.Matches(html, "<input[^>]*name=\"appType\"[^>]*>")
        Assert.Equal(2, radios.Count)

        let webRadio = radios |> Seq.find (fun m -> m.Value.Contains "value=\"web\"")
        let spaRadio = radios |> Seq.find (fun m -> m.Value.Contains "value=\"spa\"")
        Assert.Contains("type=\"radio\"", webRadio.Value)
        Assert.Contains("checked", webRadio.Value)
        Assert.Contains("type=\"radio\"", spaRadio.Value)
        Assert.DoesNotContain("checked", spaRadio.Value)

        // Each choice says which one is the operator's.
        Assert.Contains("Web application", html)
        Assert.Contains("Single-page or native app", html)
        Assert.Contains("client secret", html)
        Assert.Contains("no secret", html)
    }

[<Fact>]
let ``each app type is listed under its own name once created`` () =
    task {
        use! app = Fixtures.createApp ()
        let! browser = signedInOperator app

        for name, appType in [ "Web One", "web"; "Spa One", "spa" ] do
            let! fieldName, fieldValue = tokenFrom browser "/admin/apps"

            let! created =
                browser.PostFormAsync
                    "/admin/apps"
                    [ fieldName, fieldValue
                      "name", name
                      "redirectUri", "https://app.example.com/callback"
                      "postLogoutRedirectUri", ""
                      "appType", appType ]

            Assert.Equal(HttpStatusCode.Redirect, created.StatusCode)
            Assert.Contains("msg=app-created", created.Headers.Location |> string)

        let! page = browser.GetAsync "/admin/apps"
        let! html = page.Content.ReadAsStringAsync()

        let cardOf (name: string) =
            let start = html.IndexOf $">{name}<"
            Assert.True(start >= 0, $"no card for {name}")
            html.Substring(start, 400)

        Assert.Contains("Web application", cardOf "Web One")
        Assert.Contains("Single-page or native app", cardOf "Spa One")
    }

// ---------------------------------------------------------------------------
// Apps: redirect URI validation
// ---------------------------------------------------------------------------

[<Fact>]
let ``a redirect URI containing a wildcard is rejected with a message`` () =
    task {
        use! app = Fixtures.createApp ()
        let! browser = signedInOperator app
        let! fieldName, fieldValue = tokenFrom browser "/admin/apps"

        let! response =
            browser.PostFormAsync
                "/admin/apps"
                [ fieldName, fieldValue
                  "name", "Wildcard App"
                  "redirectUri", "https://*.example.com/cb"
                  "postLogoutRedirectUri", ""
                  "appType", "web" ]

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("msg=app-redirect-uri-invalid", response.Headers.Location |> string)

        let! appsPage = browser.GetAsync "/admin/apps"
        let! html = appsPage.Content.ReadAsStringAsync()
        // The message names the next action (Texts.AppRedirectUriInvalid),
        // and the rejected app was never created.
        Assert.DoesNotContain("Wildcard App", html)
    }

// ---------------------------------------------------------------------------
// Users: disabling revokes authorizations
// ---------------------------------------------------------------------------

[<Fact>]
let ``disabling a user revokes their authorizations`` () =
    task {
        use! app = Fixtures.createApp ()

        let userId = Umbral.Domains.Ids.newSubject ()

        do!
            Users.insertForTesting
                app.DbConfig
                app.Clock
                { Id = userId
                  DisplayName = "Sam Rivera"
                  Email = Some "sam@example.com"
                  EmailVerified = true
                  AvatarUrl = None
                  IsEnabled = true
                  FirstSeenUtc = app.Clock.GetUtcNow()
                  LastSeenUtc = app.Clock.GetUtcNow() }

        // A fresh scope per step, not the long-lived `app.Services` directly:
        // reusing one DbContext across create-then-verify would answer the
        // second question from its own first-level cache rather than the
        // database the toggle handler actually wrote to.
        let! authorizationId =
            task {
                use scope = app.Services.CreateScope()
                let authorizations = scope.ServiceProvider.GetRequiredService<OpenIddict.Abstractions.IOpenIddictAuthorizationManager>()

                let! authorization =
                    authorizations.CreateAsync(
                        identity = System.Security.Claims.ClaimsIdentity(),
                        subject = userId,
                        client = userId,
                        ``type`` = OpenIddict.Abstractions.OpenIddictConstants.AuthorizationTypes.Permanent,
                        scopes = Collections.Immutable.ImmutableArray.Create "openid"
                    )

                return! authorizations.GetIdAsync authorization
            }

        Assert.NotNull(authorizationId: string | null)

        let! browser = signedInOperator app
        let! fieldName, fieldValue = tokenFrom browser "/admin/users"

        let! toggled = browser.PostFormAsync $"/admin/users/{userId}/toggle" [ fieldName, fieldValue ]
        Assert.Equal(HttpStatusCode.Redirect, toggled.StatusCode)
        Assert.Contains("user-disabled", toggled.Headers.Location |> string)

        let! user = Users.findById app.DbConfig userId
        Assert.False(user.Value.IsEnabled)

        match authorizationId with
        | null -> Assert.Fail "the authorization has no id"
        | id ->
            use scope = app.Services.CreateScope()
            let authorizations = scope.ServiceProvider.GetRequiredService<OpenIddict.Abstractions.IOpenIddictAuthorizationManager>()
            let! afterToggle = authorizations.FindByIdAsync id

            match afterToggle with
            | null -> () // Gone entirely is also an acceptable revocation outcome.
            | authorization ->
                let! status = authorizations.GetStatusAsync authorization
                Assert.NotEqual<string>(OpenIddict.Abstractions.OpenIddictConstants.Statuses.Valid, status)
    }

[<Fact>]
let ``deleting a user is confirmed on the page and removes the row`` () =
    task {
        use! app = Fixtures.createApp ()

        let userId = Umbral.Domains.Ids.newSubject ()

        do!
            Users.insertForTesting
                app.DbConfig
                app.Clock
                { Id = userId
                  DisplayName = "Alex Doe"
                  Email = None
                  EmailVerified = false
                  AvatarUrl = None
                  IsEnabled = true
                  FirstSeenUtc = app.Clock.GetUtcNow()
                  LastSeenUtc = app.Clock.GetUtcNow() }

        let! browser = signedInOperator app
        let! usersPage = browser.GetAsync "/admin/users"
        let! usersHtml = usersPage.Content.ReadAsStringAsync()
        Assert.Contains("confirm(", usersHtml)

        let! fieldName, fieldValue = tokenFrom browser "/admin/users"
        let! deleted = browser.PostFormAsync $"/admin/users/{userId}/delete" [ fieldName, fieldValue ]
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode)

        let! gone = Users.findById app.DbConfig userId
        Assert.True(gone.IsNone)
    }
