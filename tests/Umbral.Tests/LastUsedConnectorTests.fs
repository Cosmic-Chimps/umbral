/// "Last used" on the sign-in page (specs/009-last-used-connector): the
/// `umbral_last_connector` cookie and the picker that reads it.
///
/// Two halves. The cookie half asks WHEN the browser is told which connector it
/// last signed in with: after every sign-in that worked, and after nothing else
/// (a redirect to a provider, a declined or failed callback, a refusal, the
/// operator's own sign-in, adding a passkey, signing out). It also reads the
/// raw `Set-Cookie` header, because a hint that is readable by a script or sent
/// over a cross-site request, or that carries anything about the person, would
/// be a leak on a page anyone can open.
///
/// The picker half asks what the page does with it: put that connector first
/// with a badge when there is a choice to make, and otherwise be exactly the
/// page it always was, whatever the cookie says. The cookie is attacker-supplied
/// by nature (any site can try to set one, and a person can edit theirs), so the
/// value is only ever compared and never drawn.
module Umbral.Tests.LastUsedConnectorTests

open System
open System.Net
open System.Net.Http
open System.Text.RegularExpressions
open Xunit
open Umbral.Domains
open Umbral.Domains.Connectors
open Umbral.Handlers.Connect
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows
open Umbral.Tests.Infrastructure.PasskeyFlows
open Umbral.Views

let private CookieName = BrowserCookies.LastConnectorCookie

let private authenticator () =
    SoftwareAuthenticator.SoftwareAuthenticator("umbral.test", Fixtures.TestOrigin)

// ---------------------------------------------------------------------------
// Set-up and reading the wire
// ---------------------------------------------------------------------------

/// The raw `Set-Cookie` headers of the cookie under test, exactly as sent.
let private lastConnectorHeaders (response: HttpResponseMessage) =
    match response.Headers.TryGetValues "Set-Cookie" with
    | true, values ->
        values
        |> Option.ofObj
        |> Option.defaultValue Seq.empty
        |> Seq.filter (fun v -> v.StartsWith $"{CookieName}=")
        |> List.ofSeq
    | _ -> []

/// The cookie's value on the wire.
let private valueOf (header: string) =
    header.Split(';').[0].Substring(CookieName.Length + 1)

/// A claimed instance with the fake connector, the passkey connector (with the
/// given enrolment word) and one app. Returns the operator, whose browser holds
/// an admin session and so is never the browser of an end user.
let private setup (app: Fixtures.TestApp) (enrolment: string) =
    task {
        let operator = Browser(app.Client)
        do! claimOperator app operator
        do! seedFakeConnector app
        do! enablePasskeyConnector operator enrolment
        let! clientId, secret = registerApp operator "Grafana"
        return operator, clientId, secret
    }

/// The first two steps of a sign-in through the fake provider: the start
/// redirect, and the `state` the provider will hand back.
let private startFake (browser: Browser) (clientId: string) =
    task {
        let! toPicker = browser.Get(Journeys.authorizeUrl clientId "openid")
        let! start = browser.Get((location toPicker).Replace("/signin", "/connect/fake/start"))
        let providerUrl = location start
        let decoded = Uri.UnescapeDataString providerUrl
        let redirect = Regex.Match(decoded, "redirect_uri=([^&]+)").Groups[1].Value
        let state = Regex.Match(decoded, "state=([^&]+)").Groups[1].Value
        return start, providerUrl, redirect, state
    }

/// A whole sign-in through the fake provider, up to and including the callback,
/// without following the callback's redirect. Returns the start redirect and the
/// callback's response.
let private attemptFake (browser: Browser) (clientId: string) (subject: string) (email: string) =
    task {
        let! start, providerUrl, redirect, state = startFake browser clientId

        let! approved =
            browser.PostForm(
                providerUrl,
                "/dev/fake-provider/authorize",
                [ "redirect_uri", redirect
                  "state", state
                  "subject", subject
                  "email", email
                  "displayName", "Test User"
                  "emailVerified", "on" ]
            )

        let! callback = browser.Get(location approved)
        return start, callback
    }

/// The sign-in of `subject` through the fake provider, which must be admitted.
let private signInFake (browser: Browser) (clientId: string) (subject: string) (email: string) =
    task {
        let! _, callback = attemptFake browser clientId subject email
        Assert.Contains("/connect/authorize", location callback)
        return callback
    }

/// Assert that the response asks the browser to keep this slug, in the form the
/// contract gives, and nothing else about the person.
let private assertRemembers (slug: string) (userId: string option) (response: HttpResponseMessage) =
    let header = lastConnectorHeaders response |> List.exactlyOne
    Assert.Equal(slug, valueOf header)

    let lower = header.ToLowerInvariant()
    Assert.Contains("httponly", lower)
    Assert.Contains("samesite=lax", lower)
    Assert.Contains("path=/", lower)
    Assert.Contains("max-age=31536000", lower)
    // The test server answers on https, so the cookie is Secure.
    Assert.Contains("secure", lower)

    Assert.DoesNotContain("@", header)

    match userId with
    | Some id -> Assert.DoesNotContain(id, header)
    | None -> ()

/// A visitor on the passkey sign-in page: an unsigned browser, and its token.
let private visit (app: Fixtures.TestApp) (clientId: string) =
    task {
        let visitor = Browser(app.Client)
        let! returnUrl, _, token = startPasskeySignIn visitor clientId "openid"
        return visitor, returnUrl, token
    }

/// Ana: enrolled through an invitation, with the authenticator that holds her
/// passkey.
let private enrol (app: Fixtures.TestApp) (operator: Browser) =
    task {
        let! link = inviteNewPerson operator "Ana"
        let ana = authenticator ()
        let! r, _ = registerThroughInvitation (Browser(app.Client)) ana (secretOf link) (Some "Ana") "Phone"
        Assert.Equal(HttpStatusCode.OK, r.StatusCode)
        let! userId = scalar app "SELECT id FROM users WHERE display_name = 'Ana'" []
        return ana, userId
    }

// ---------------------------------------------------------------------------
// T007. The cookie: written after a sign-in that worked
// ---------------------------------------------------------------------------

[<Fact>]
let ``a provider sign-in remembers the connector's slug and nothing about the person`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, _ = setup app "invitations"
        let person = Browser(app.Client)
        let! callback = signInFake person clientId "fake-user-1" "sam@example.com"
        let! userId = userIdOf app "fake-user-1"

        assertRemembers "fake" (Some userId) callback
        Assert.Equal("fake", person.Cookies[CookieName])
        // The value is the slug and only that: not the email, the name or the
        // provider's subject either.
        let value = lastConnectorHeaders callback |> List.exactlyOne |> valueOf
        Assert.DoesNotContain("sam", value)
        Assert.DoesNotContain("fake-user-1", value)
    }

[<Fact>]
let ``a passkey sign-in remembers the passkey connector`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, clientId, _ = setup app "invitations"
        let! ana, userId = enrol app operator
        let! visitor, returnUrl, token = visit app clientId

        let! response, _ = signInWithPasskey visitor ana (List.head ana.Credentials).Id returnUrl token
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        assertRemembers "passkey" (Some userId) response
    }

[<Fact>]
let ``an open passkey sign-up remembers the passkey connector`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, _ = setup app "open"
        let! visitor, returnUrl, token = visit app clientId

        let! _, started =
            postJson visitor "/passkey/registration/options" token {| mode = "signup"; displayName = "Noor"; nickname = ""; returnUrl = returnUrl |}

        let credential = (authenticator ()).Register(started.GetProperty "options")

        let! response, _ =
            postJson visitor "/passkey/registration" token {| ceremony = Journeys.strOf started "ceremony"; credential = credential |}

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        let! userId = scalar app "SELECT id FROM users WHERE display_name = 'Noor'" []
        assertRemembers "passkey" (Some userId) response
    }

[<Fact>]
let ``a registration through an invitation remembers the passkey connector`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, _ = setup app "invitations"
        let! link = inviteNewPerson operator "Ana"
        let person = Browser(app.Client)
        let! response, _ = registerThroughInvitation person (authenticator ()) (secretOf link) (Some "Ana") "Phone"

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        let! userId = scalar app "SELECT id FROM users WHERE display_name = 'Ana'" []
        assertRemembers "passkey" (Some userId) response
        Assert.Equal("passkey", person.Cookies[CookieName])
    }

[<Fact>]
let ``the connector used last replaces the one before it`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, clientId, _ = setup app "invitations"
        let! ana, _ = enrol app operator

        let person = Browser(app.Client)
        let! _ = signInFake person clientId "fake-user-1" "sam@example.com"
        Assert.Equal("fake", person.Cookies[CookieName])

        // The same browser signs out of Umbral's session (the cookie is a
        // browser's, not a session's) and comes back through a passkey.
        person.Cookies.Remove Sessions.CookieName |> ignore
        let! returnUrl, _, token = startPasskeySignIn person clientId "openid"
        let! response, _ = signInWithPasskey person ana (List.head ana.Credentials).Id returnUrl token

        assertRemembers "passkey" None response
        Assert.Equal("passkey", person.Cookies[CookieName])
    }

// ---------------------------------------------------------------------------
// T007. The cookie: written by nothing else
// ---------------------------------------------------------------------------

[<Fact>]
let ``starting a round trip remembers nothing`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, _ = setup app "invitations"
        let person = Browser(app.Client)

        let! start, _, _, _ = startFake person clientId
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode)
        Assert.Empty(lastConnectorHeaders start)

        let! passkeyStart = person.Get "/connect/passkey/start?returnUrl=%2F"
        Assert.Equal(HttpStatusCode.OK, passkeyStart.StatusCode)
        Assert.Empty(lastConnectorHeaders passkeyStart)

        Assert.False(person.Cookies.ContainsKey CookieName)
    }

[<Fact>]
let ``a declined or failed callback remembers nothing`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, _ = setup app "invitations"

        let callbacks (state: string) =
            [ "the provider declined", $"error=access_denied&state={state}"
              "the provider's code is not one it issued", $"code=not-a-code&state={state}"
              "the state is not the one minted", "code=whatever&state=not-the-state" ]

        for index in 0..2 do
            let person = Browser(app.Client)
            let! _, _, _, state = startFake person clientId
            let label, query = List.item index (callbacks state)

            let! callback = person.Get $"/connect/fake/callback?{query}"
            Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode)
            Assert.DoesNotContain("/connect/authorize", location callback)
            Assert.True((lastConnectorHeaders callback).IsEmpty, label)
            Assert.False(person.Cookies.ContainsKey CookieName, label)
            Assert.False(person.Cookies.ContainsKey Sessions.CookieName, label)
    }

[<Fact>]
let ``a sign-in refused by the allowed email domains remembers nothing`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, _ = setup app "invitations"
        let! stored = ConnectorStore.findBySlug app.DbConfig "fake"
        do! ConnectorStore.save app.DbConfig app.Clock { stored.Value with AllowedEmailDomains = [ "example.org" ] } None

        let person = Browser(app.Client)
        let! _, callback = attemptFake person clientId "fake-user-1" "sam@elsewhere.example"

        Assert.DoesNotContain("/connect/authorize", location callback)
        Assert.Empty(lastConnectorHeaders callback)
        Assert.False(person.Cookies.ContainsKey CookieName)
        Assert.False(person.Cookies.ContainsKey Sessions.CookieName)
    }

[<Fact>]
let ``a disabled person's sign-in remembers nothing, through a provider or a passkey`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, clientId, _ = setup app "invitations"

        // Sam, signed in once, then disabled.
        let! _ = signInFake (Browser(app.Client)) clientId "fake-user-1" "sam@example.com"
        let! samId = userIdOf app "fake-user-1"
        let! samOff = operator.PostForm("/admin/users", $"/admin/users/{samId}/toggle", [])
        Assert.Equal(HttpStatusCode.Redirect, samOff.StatusCode)

        let sam = Browser(app.Client)
        let! _, callback = attemptFake sam clientId "fake-user-1" "sam@example.com"
        Assert.DoesNotContain("/connect/authorize", location callback)
        Assert.Empty(lastConnectorHeaders callback)
        Assert.False(sam.Cookies.ContainsKey CookieName)

        // Ana, enrolled, then disabled.
        let! ana, anaId = enrol app operator
        let! anaOff = operator.PostForm("/admin/users", $"/admin/users/{anaId}/toggle", [])
        Assert.Equal(HttpStatusCode.Redirect, anaOff.StatusCode)

        let! visitor, returnUrl, token = visit app clientId
        let! response, _ = signInWithPasskey visitor ana (List.head ana.Credentials).Id returnUrl token
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode)
        Assert.Empty(lastConnectorHeaders response)
        Assert.False(visitor.Cookies.ContainsKey CookieName)
    }

[<Fact>]
let ``the operator's sign-in remembers nothing`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser(app.Client)
        do! claimOperator app operator
        Assert.True(operator.Cookies.ContainsKey AdminAuth.CookieName)
        Assert.False(operator.Cookies.ContainsKey CookieName)

        // And signing in again, as the operator does on a later visit.
        let again = Browser(app.Client)
        let! response = again.PostForm("/admin/signin", "/admin/signin", [ "credential", OperatorCredential ])
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.True(again.Cookies.ContainsKey AdminAuth.CookieName)
        Assert.Empty(lastConnectorHeaders response)
        Assert.False(again.Cookies.ContainsKey CookieName)
    }

[<Fact>]
let ``adding a passkey while signed in leaves the remembered connector alone`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, _ = setup app "invitations"
        let sam = Browser(app.Client)
        let! _ = signInFake sam clientId "fake-user-1" "sam@example.com"
        Assert.Equal("fake", sam.Cookies[CookieName])

        let! token = sam.TokenFrom "/account/passkeys"
        let! options, started = postJson sam "/passkey/registration/options" token {| mode = "add"; nickname = "Laptop" |}
        Assert.Equal(HttpStatusCode.OK, options.StatusCode)
        Assert.Empty(lastConnectorHeaders options)

        let credential = (authenticator ()).Register(started.GetProperty "options")

        let! added, _ =
            postJson sam "/passkey/registration" token {| ceremony = Journeys.strOf started "ceremony"; credential = credential |}

        Assert.Equal(HttpStatusCode.OK, added.StatusCode)
        Assert.Empty(lastConnectorHeaders added)
        Assert.Equal("fake", sam.Cookies[CookieName])
    }

[<Fact>]
let ``signing out does not clear the remembered connector`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! _, clientId, _ = setup app "invitations"
        let person = Browser(app.Client)
        let! _ = signInFake person clientId "fake-user-1" "sam@example.com"
        Assert.True(person.Cookies.ContainsKey Sessions.CookieName)

        let! logout = person.Get "/connect/logout"
        Assert.True(int logout.StatusCode < 400, $"logout answered {logout.StatusCode}")

        // The session cookie is what the response expires; this one is not touched.
        let expired =
            match logout.Headers.TryGetValues "Set-Cookie" with
            | true, values -> values |> Option.ofObj |> Option.defaultValue Seq.empty |> Seq.exists (fun v -> v.StartsWith $"{Sessions.CookieName}=")
            | _ -> false

        Assert.True(expired, "the session cookie is expired by the sign-out")
        Assert.Empty(lastConnectorHeaders logout)
        Assert.Equal("fake", person.Cookies[CookieName])
    }

// ---------------------------------------------------------------------------
// T008. Picker.arrange
// ---------------------------------------------------------------------------

let private connector (slug: string) (kind: ProviderKind) : Connector =
    { Slug = slug
      Kind = kind
      DisplayName = slug.ToUpperInvariant()
      ClientId = slug
      ClientSecretRef = "unused"
      ClientSecretHint = ""
      IssuerUrl = None
      AllowedEmailDomains = []
      PasskeyEnrolment = InvitationsOnly
      FediverseAccess = FediverseAccess.Any
      IsEnabled = true
      IsShared = true
      CreatedUtc = DateTimeOffset.UnixEpoch
      UpdatedUtc = DateTimeOffset.UnixEpoch }

let private slugs (connectors: Connector list) = connectors |> List.map _.Slug

let private offered =
    [ connector "a" ProviderKind.Fake
      connector "b" ProviderKind.Discord
      connector "c" ProviderKind.Passkey ]

[<Fact>]
let ``the remembered connector comes out first and the others keep their order`` () =
    for slug, rest in [ "a", [ "b"; "c" ]; "b", [ "a"; "c" ]; "c", [ "a"; "b" ] ] do
        let first, others = Picker.arrange (Some slug) offered
        Assert.Equal(Some slug, first |> Option.map _.Slug)
        Assert.Equal<string list>(rest, slugs others)

[<Fact>]
let ``two offered connectors are enough to single one out`` () =
    let first, others = Picker.arrange (Some "b") (offered |> List.take 2)
    Assert.Equal(Some "b", first |> Option.map _.Slug)
    Assert.Equal<string list>([ "a" ], slugs others)

[<Fact>]
let ``with one connector there is nothing to choose and the list is unchanged`` () =
    let only = [ connector "a" ProviderKind.Fake ]
    let first, others = Picker.arrange (Some "a") only
    Assert.Equal(None, first)
    Assert.Equal<string list>([ "a" ], slugs others)

    let none, empty = Picker.arrange (Some "a") []
    Assert.Equal(None, none)
    Assert.Empty empty

[<Fact>]
let ``no cookie or one that names no offered connector leaves the list unchanged`` () =
    for last in [ None; Some ""; Some "nope"; Some "a,b"; Some "<script>"; Some " a" ] do
        let first, others = Picker.arrange last offered
        Assert.Equal(None, first)
        Assert.Equal<string list>([ "a"; "b"; "c" ], slugs others)

[<Fact>]
let ``a slug that differs only in case is not a match`` () =
    let first, others =
        Picker.arrange (Some "Fake") [ connector "fake" ProviderKind.Fake; connector "other" ProviderKind.Discord ]

    Assert.Equal(None, first)
    Assert.Equal<string list>([ "fake"; "other" ], slugs others)

// ---------------------------------------------------------------------------
// T008. The sign-in page
// ---------------------------------------------------------------------------

/// A Discord connector, written straight to the store with a real secret, so the
/// page offers a second provider next to the fake one.
let private seedDiscord (app: Fixtures.TestApp) =
    task {
        let! reference = Fixtures.putSecret app "discord" "discord-secret"

        do!
            ConnectorStore.save
                app.DbConfig
                app.Clock
                { connector "discord" ProviderKind.Discord with
                    DisplayName = "Discord"
                    ClientId = "1234"
                    ClientSecretRef = reference.Encoded
                    ClientSecretHint = Secrets.hint "discord-secret"
                    CreatedUtc = app.Clock.GetUtcNow()
                    UpdatedUtc = app.Clock.GetUtcNow() }
                None
    }

/// Fake, Discord and the passkey connector, all shared, and two apps: A, which
/// offers every shared connector, and B.
let private picker () =
    task {
        let! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, idA, _ = setup app "invitations"
        do! seedDiscord app
        let! idB, _ = registerApp operator "Recipes"
        return app, operator, idA, idB
    }

let private pickerFor (clientId: string) =
    "/signin?returnUrl=" + Uri.EscapeDataString(Journeys.authorizeUrl clientId "openid")

/// GET the sign-in page carrying exactly these cookies (a hand-made header, as
/// ThemeTests does), and return the page without its theme control: the
/// control's form carries a token that changes on every request.
let private pageWith (app: Fixtures.TestApp) (cookies: string list) (url: string) =
    task {
        use request = new HttpRequestMessage(HttpMethod.Get, url)

        if not cookies.IsEmpty then
            request.Headers.Add("Cookie", String.concat "; " cookies)

        let! response = app.Client.SendAsync request
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        let! html = response.Content.ReadAsStringAsync()
        return withoutThemeControl html
    }

let private remembered (value: string) = $"{CookieName}={value}"

/// The slugs of the page's connector buttons, in page order.
let private order (html: string) =
    [ for m in Regex.Matches(html, "<a [^>]*data-connector=\"([^\"]*)\"") -> m.Groups[1].Value ]

/// The whole `<a>` of one connector's button.
let private buttonOf (html: string) (slug: string) =
    Regex.Match(html, "<a [^>]*data-connector=\"" + slug + "\".*?</a>", RegexOptions.Singleline).Value

let private count (pattern: string) (html: string) = Regex.Matches(html, pattern).Count

[<Fact>]
let ``the remembered connector is first with the badge, the label and the divider, and the rest follow in order`` () =
    task {
        let! app, _, idA, _ = picker ()
        use _ = app
        let! plain = pageWith app [] (pickerFor idA)
        let usual = order plain
        Assert.Equal<string list>([ "discord"; "fake"; "passkey" ], usual |> List.sort)
        Assert.Equal(0, count "data-last-used" plain)

        for slug, label in [ "discord", "Discord"; "fake", "Fake"; "passkey", "" ] do
            let! html = pageWith app [ remembered slug ] (pickerFor idA)

            Assert.Equal<string list>(slug :: (usual |> List.filter (fun s -> s <> slug)), order html)

            let button = buttonOf html slug
            Assert.Contains("data-last-used", button)
            Assert.Contains($"<span class=\"um-last-used\">{Texts.en.PickerLastUsed}</span>", button)

            // The accessible name starts with what the button shows (label in
            // name) and says it was the last used.
            let visible =
                if slug = "passkey" then Texts.en.SignInWithPasskey else Texts.en.SignInWith label

            Assert.Contains($"aria-label=\"{visible}, last used\"", button)

            // One badge, one divider, between the first button and the second.
            Assert.Equal(1, count "data-last-used" html)
            Assert.Equal(1, count "class=\"um-last-used\"" html)
            Assert.Equal(1, count "data-or-divider" html)
            Assert.Matches($"data-connector=\"{slug}\".*?data-or-divider.*?data-connector=\"", html.Replace("\n", " "))
            Assert.Contains($">{Texts.en.PickerOr}</div>", html)

            // The others are the buttons they always were.
            for other in usual |> List.filter (fun s -> s <> slug) do
                let otherButton = buttonOf html other
                Assert.DoesNotContain("data-last-used", otherButton)
                Assert.DoesNotContain("aria-label", otherButton)
                Assert.DoesNotContain("um-last-used", otherButton)
    }

[<Fact>]
let ``a cookie that names nothing offered gives the page it always was and is never drawn`` () =
    task {
        let! app, operator, idA, _ = picker ()
        use _ = app
        let! plain = pageWith app [] (pickerFor idA)

        for value in [ "nope"; "FAKE"; "Discord"; "<script>alert(1)</script>"; "fake%20x"; "\"quoted\"" ] do
            let! html = pageWith app [ remembered value ] (pickerFor idA)
            Assert.Equal(plain, html)
            Assert.DoesNotContain("data-last-used", html)
            Assert.DoesNotContain("data-or-divider", html)
            Assert.DoesNotContain("alert(1)", html)

        // An empty value, and other cookies beside it, are no better.
        let! empty = pageWith app [ remembered "" ] (pickerFor idA)
        Assert.Equal(plain, empty)
        let! other = pageWith app [ "session_hint=discord" ] (pickerFor idA)
        Assert.Equal(plain, other)
        ignore operator
    }

[<Fact>]
let ``a connector that was disabled since is not singled out`` () =
    task {
        let! app, _, idA, _ = picker ()
        use _ = app
        let! stored = ConnectorStore.findBySlug app.DbConfig "discord"
        do! ConnectorStore.save app.DbConfig app.Clock { stored.Value with IsEnabled = false } None

        let! plain = pageWith app [] (pickerFor idA)
        Assert.DoesNotContain("data-connector=\"discord\"", plain)

        let! html = pageWith app [ remembered "discord" ] (pickerFor idA)
        Assert.Equal(plain, html)
        Assert.DoesNotContain("data-last-used", html)
    }

[<Fact>]
let ``a connector the app does not offer is not singled out, while another app that offers it does`` () =
    task {
        let! app, operator, idA, idB = picker ()
        use _ = app
        let! saved =
            operator.PostForm(
                "/admin/apps",
                $"/admin/apps/{idA}/connectors",
                [ "connectorChoice", "chosen"; "connectors", "fake"; "connectors", "passkey" ]
            )

        // Saved, with a warning that an open connector sits beside a limited one.
        Assert.DoesNotContain("strand", location saved)

        let! plain = pageWith app [] (pickerFor idA)
        Assert.Equal<string list>([ "fake"; "passkey" ], order plain |> List.sort)

        let! html = pageWith app [ remembered "discord" ] (pickerFor idA)
        Assert.Equal(plain, html)
        Assert.DoesNotContain("data-last-used", html)

        // The same cookie on the app that does offer Discord.
        let! other = pageWith app [ remembered "discord" ] (pickerFor idB)
        Assert.Equal("discord", List.head (order other))
        Assert.Contains("data-last-used", other)
    }

[<Fact>]
let ``a page that offers a single connector has nothing to choose and is unchanged`` () =
    task {
        let! app, operator, idA, _ = picker ()
        use _ = app

        let! saved =
            operator.PostForm("/admin/apps", $"/admin/apps/{idA}/connectors", [ "connectorChoice", "chosen"; "connectors", "fake" ])

        Assert.Contains("app-connectors-saved", location saved)

        let! plain = pageWith app [] (pickerFor idA)
        Assert.Equal<string list>([ "fake" ], order plain)

        let! html = pageWith app [ remembered "fake" ] (pickerFor idA)
        Assert.Equal(plain, html)
        Assert.DoesNotContain("data-last-used", html)
        Assert.DoesNotContain("data-or-divider", html)
        Assert.DoesNotContain("um-last-used", html)
    }

[<Fact>]
let ``the page without an app, and its notice, follow the same rule`` () =
    task {
        let! app, _, _, _ = picker ()
        use _ = app
        let! html = pageWith app [ remembered "discord" ] "/signin?msg=access-denied"

        Assert.Equal("discord", List.head (order html))
        Assert.Contains("data-last-used", html)
        Assert.Contains("data-screen=\"signin\"", html)
    }

// ---------------------------------------------------------------------------
// T008. Branding and themes
// ---------------------------------------------------------------------------

let private htmlTag (html: string) = Regex.Match(html, "<html[^>]*>").Value

[<Fact>]
let ``on an app with an accent the badge sits inside a primary button`` () =
    task {
        let! app, _, idA, _ = picker ()
        use _ = app

        match Branding.Accent.parse "#f26b1d" with
        | Ok accent -> do! BrandingStore.setAccent app.DbConfig (app.Clock.GetUtcNow()) idA (Some accent)
        | Error e -> failwith (string e)

        let! html = pageWith app [ remembered "discord" ] (pickerFor idA)
        Assert.Contains("data-accent=\"f26b1d\"", Regex.Match(html, "<body[^>]*>").Value)

        let button = buttonOf html "discord"
        Assert.Contains("um-btn-primary", button)
        Assert.Contains("data-last-used", button)
        Assert.Contains($"<span class=\"um-last-used\">{Texts.en.PickerLastUsed}</span>", button)

        // Every button is primary on a branded page, the others included.
        for slug in order html do
            Assert.Contains("um-btn-primary", buttonOf html slug)
    }

[<Fact>]
let ``the badge renders in the light and the dark theme, branded or not`` () =
    task {
        let! app, _, idA, idB = picker ()
        use _ = app

        match Branding.Accent.parse "#1f6fa8" with
        | Ok accent -> do! BrandingStore.setAccent app.DbConfig (app.Clock.GetUtcNow()) idA (Some accent)
        | Error e -> failwith (string e)

        for theme in [ "light"; "dark"; "system" ] do
            // A has an accent, B has none.
            for clientId, kind in [ idA, "um-btn-primary"; idB, "um-btn-secondary" ] do
                let! html = pageWith app [ $"{Theme.ThemeCookie}={theme}"; remembered "discord" ] (pickerFor clientId)

                Assert.Matches($"data-theme=\"{theme}\"", htmlTag html)

                let button = buttonOf html "discord"
                Assert.Contains(kind, button)
                Assert.Contains("data-last-used", button)
                Assert.Contains("class=\"um-last-used\"", button)
                Assert.Equal(1, count "data-or-divider" html)
    }
