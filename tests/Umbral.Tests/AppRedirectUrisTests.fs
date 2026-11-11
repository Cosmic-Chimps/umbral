/// Spec 012: an app's redirect URIs and post-logout redirect URIs are lists an
/// operator edits, through the real routes and the real OpenIddict store.
///
/// Asserts on notice codes in redirects, `data-uri` on the pane's rows and the
/// security log, never on the wording of any text. The sign-in tests are the
/// ones that matter most: they show that an edit changes only the list it was
/// meant to, that the app keeps its secret and its client id, and that an
/// address that was removed stops working at the authorize endpoint.
module Umbral.Tests.AppRedirectUrisTests

open System
open System.Net
open System.Text.RegularExpressions
open Xunit
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

/// Two real addresses an app may have besides `Flows.RedirectUri`.
let private second = "https://grafana.example.com/second"
let private third = "http://localhost:3000/callback?a=1;b=2"

/// An instance with the operator signed in and one registered web app, which
/// has `Flows.RedirectUri`, no post-logout URI and the secret revealed once.
let private setup () =
    task {
        let! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app
        let! clientId, secret = registerApp operator "Photos"
        return app, operator, clientId, secret
    }

let private pageOf (clientId: string) = $"/admin/apps/{clientId}"

let private post (operator: Browser) (clientId: string) (route: string) (uri: string) =
    operator.PostForm(pageOf clientId, $"/admin/apps/{clientId}/{route}", [ "uri", uri ])

let private addRedirect operator clientId uri = post operator clientId "redirect-uris" uri
let private removeRedirect operator clientId uri = post operator clientId "redirect-uris/remove" uri
let private addPostLogout operator clientId uri = post operator clientId "post-logout-uris" uri
let private removePostLogout operator clientId uri = post operator clientId "post-logout-uris/remove" uri

/// The `data-uri` of every row of the redirect list and of the post-logout
/// list on an app's page, in the order drawn, decoded from HTML.
let private listsOn (html: string) =
    let marker (name: string) = html.IndexOf $"data-{name}"
    let redirectAt = marker "redirect-uris"
    let postLogoutAt = marker "post-logout-uris"
    Assert.True(redirectAt >= 0 && postLogoutAt > redirectAt, "the pane shows both lists")

    let rows (part: string) =
        [ for m in Regex.Matches(part, "data-uri=\"([^\"]*)\"") -> string (WebUtility.HtmlDecode m.Groups[1].Value) ]

    rows (html.Substring(redirectAt, postLogoutAt - redirectAt)), rows (html.Substring postLogoutAt)

let private listsOf (operator: Browser) (clientId: string) =
    task {
        let! html = operator.GetHtml(pageOf clientId)
        return listsOn html
    }

let private redirectsOf operator clientId =
    task {
        let! redirects, _ = listsOf operator clientId
        return redirects
    }

let private postLogoutsOf operator clientId =
    task {
        let! _, postLogouts = listsOf operator clientId
        return postLogouts
    }

/// The `app_updated` events of an app, oldest first: actor and detail.
let private updates (app: Fixtures.TestApp) (clientId: string) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()

        command.CommandText <-
            "SELECT actor, coalesce(detail, '') FROM security_events
             WHERE event_type = 'app_updated' AND client_id = @c ORDER BY id"

        command.Parameters.AddWithValue("@c", clientId) |> ignore
        use! reader = command.ExecuteReaderAsync()
        let rows = ResizeArray()

        let mutable go = true

        while go do
            let! has = reader.ReadAsync()

            if has then
                rows.Add(reader.GetString 0, reader.GetString 1)
            else
                go <- false

        return List.ofSeq rows
    }

let private details app clientId =
    task {
        let! rows = updates app clientId
        return rows |> List.map snd
    }

/// A redirect to the app's own page with this notice.
let private assertNotice (clientId: string) (notice: string) (response: Net.Http.HttpResponseMessage) =
    Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
    Assert.Equal($"/admin/apps/{clientId}?msg={notice}", location response)

let private authorizeWith (clientId: string) (redirectUri: string) =
    "/connect/authorize?client_id=" + Uri.EscapeDataString clientId
    + "&response_type=code&redirect_uri=" + Uri.EscapeDataString redirectUri
    + "&scope=openid&code_challenge=" + Challenge
    + "&code_challenge_method=S256&state=s"

/// One whole sign-in through `redirectUri` and the code exchange with `secret`.
let private signInAndExchange (app: Fixtures.TestApp) (clientId: string) (secret: string) (redirectUri: string) =
    task {
        let person = Browser app.Client

        match! signInViaFakeTo redirectUri person clientId "openid" "fake-user-1" "sam@example.com" with
        | Error where -> return failwith $"the sign-in through {redirectUri} was refused: {where}"
        | Ok code -> return! exchangeCodeFor redirectUri app clientId secret code
    }

let private assertSignsIn (app: Fixtures.TestApp) clientId secret redirectUri =
    task {
        let! response = signInAndExchange app clientId secret redirectUri
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        let! body = response.Content.ReadAsStringAsync()
        Assert.Contains("access_token", body)
    }

// ---------------------------------------------------------------------------
// 1. Adding
// ---------------------------------------------------------------------------

[<Fact>]
let ``adding a redirect URI lists it after the first and logs one event`` () =
    task {
        let! app, operator, clientId, _ = setup ()
        use _ = app

        let! before = updates app clientId
        let! added = addRedirect operator clientId second
        assertNotice clientId "app-redirect-uri-added" added

        let! redirects = redirectsOf operator clientId
        Assert.Equal<string list>([ RedirectUri; second ], redirects)

        let! after = updates app clientId
        Assert.Equal(before.Length + 1, after.Length)
        Assert.Equal(("operator", "redirect_uri_added;uri=" + Uri.EscapeDataString second), List.last after)
    }

[<Fact>]
let ``an address with a semicolon and an equals sign is logged escaped and kept whole`` () =
    task {
        let! app, operator, clientId, _ = setup ()
        use _ = app

        let! added = addRedirect operator clientId third
        assertNotice clientId "app-redirect-uri-added" added

        let! redirects = redirectsOf operator clientId
        Assert.Equal<string list>([ RedirectUri; third ], redirects)

        let! logged = details app clientId
        let last = List.last logged
        Assert.Equal("redirect_uri_added;uri=" + Uri.EscapeDataString third, last)
        // One detail, one uri: the address's own `;` and `=` did not become keys.
        Assert.Equal(2, last.Split(';').Length)
    }

// ---------------------------------------------------------------------------
// 2. Refusals
// ---------------------------------------------------------------------------

/// A refusal changes nothing and logs nothing.
let private assertUnchanged (app: Fixtures.TestApp) operator clientId (redirects: string list) (eventsBefore: int) =
    task {
        let! now = redirectsOf operator clientId
        Assert.Equal<string list>(redirects, now)
        let! events = updates app clientId
        Assert.Equal(eventsBefore, events.Length)
    }

[<Fact>]
let ``an invalid address is refused and the list is unchanged`` () =
    task {
        let! app, operator, clientId, _ = setup ()
        use _ = app
        let! before = updates app clientId

        let! refused = addRedirect operator clientId "https://*.example.com/cb"
        assertNotice clientId "app-redirect-uri-invalid" refused

        let! refusedPost = addPostLogout operator clientId "/not-absolute"
        assertNotice clientId "app-redirect-uri-invalid" refusedPost

        do! assertUnchanged app operator clientId [ RedirectUri ] before.Length
        let! postLogouts = postLogoutsOf operator clientId
        Assert.Empty postLogouts
    }

// An invalid add keeps what was typed: it comes back, through the single-use
// cookie, in the field of the list it was typed in.

/// The add field of a list on an app's page: its HTML-decoded `value` (None
/// when it has no value attribute) and whether it is marked `aria-invalid`.
/// Found by its id and read attribute by attribute, so attribute order does
/// not matter.
let private addField (html: string) (prefix: string) (clientId: string) =
    let tag =
        Regex.Match(html, "<input[^>]*id=\"" + Regex.Escape($"{prefix}-{clientId}") + "\"[^>]*>")

    Assert.True(tag.Success, $"the pane has the {prefix} field")

    let attribute (name: string) =
        let m = Regex.Match(tag.Value, "\\s" + name + "=\"([^\"]*)\"")
        if m.Success then Some(string (WebUtility.HtmlDecode m.Groups[1].Value)) else None

    attribute "value", attribute "aria-invalid" = Some "true"

let private redirectField html clientId = addField html "redirect-uri-add" clientId
let private postLogoutField html clientId = addField html "post-logout-uri-add" clientId

/// The refused address and what the page is after it, for a list.
let private refusedThenPage (operator: Browser) clientId (add: Browser -> string -> string -> _) (typed: string) =
    task {
        let! refused = add operator clientId typed
        assertNotice clientId "app-redirect-uri-invalid" refused
        let! html = operator.GetHtml(location refused)
        return refused, html
    }

[<Fact>]
let ``a refused redirect URI comes back in its own field and not in the other`` () =
    task {
        let! app, operator, clientId, _ = setup ()
        use _ = app
        let typed = "https://*.example.com/cb"

        let! _, html = refusedThenPage operator clientId addRedirect typed
        Assert.Equal((Some typed, true), redirectField html clientId)
        Assert.Equal((None, false), postLogoutField html clientId)
    }

[<Fact>]
let ``a refused post-logout URI comes back in its own field and not in the other`` () =
    task {
        let! app, operator, clientId, _ = setup ()
        use _ = app
        let typed = "/not-absolute"

        let! _, html = refusedThenPage operator clientId addPostLogout typed
        Assert.Equal((Some typed, true), postLogoutField html clientId)
        Assert.Equal((None, false), redirectField html clientId)
    }

[<Fact>]
let ``the refused address is not in the Location or readable in a Set-Cookie`` () =
    task {
        let! app, operator, clientId, _ = setup ()
        use _ = app
        let typed = "https://*.example.com/secret-path"

        let! refused = addRedirect operator clientId typed
        assertNotice clientId "app-redirect-uri-invalid" refused
        Assert.DoesNotContain("secret-path", location refused)

        let cookies =
            match refused.Headers.TryGetValues "Set-Cookie" with
            | true, values -> List.ofSeq (Option.defaultValue Seq.empty (Option.ofObj values))
            | _ -> []

        Assert.NotEmpty cookies

        for cookie in cookies do
            Assert.DoesNotContain("secret-path", cookie)
            Assert.DoesNotContain("example.com", cookie)
    }

[<Fact>]
let ``a second visit to the page shows the field empty`` () =
    task {
        let! app, operator, clientId, _ = setup ()
        use _ = app
        let! refused, first = refusedThenPage operator clientId addRedirect "https://*.example.com/cb"
        Assert.Equal((Some "https://*.example.com/cb", true), redirectField first clientId)

        // The cookie was read once: a refresh of the same address has nothing left.
        let! again = operator.GetHtml(location refused)
        Assert.Equal((None, false), redirectField again clientId)
        Assert.Equal((None, false), postLogoutField again clientId)
    }

[<Fact>]
let ``an address with separators and quotes comes back whole and escaped`` () =
    task {
        let! app, operator, clientId, _ = setup ()
        use _ = app
        let typed = "https://*.example.com/cb?a=1;b=\"x\" <y>'z"

        let! _, html = refusedThenPage operator clientId addRedirect typed
        Assert.Equal((Some typed, true), redirectField html clientId)

        let raw = Regex.Match(html, "<input[^>]*id=\"redirect-uri-add-" + Regex.Escape clientId + "\"[^>]*>").Value
        Assert.DoesNotContain("\"x\"", raw)
        Assert.DoesNotContain("<y>", raw)
    }

[<Fact>]
let ``an address over the bound is refused and not kept`` () =
    task {
        let! app, operator, clientId, _ = setup ()
        use _ = app
        let typed = "https://*.example.com/" + String('a', 2100)

        let! _, html = refusedThenPage operator clientId addRedirect typed
        Assert.Equal((None, false), redirectField html clientId)
        Assert.Equal((None, false), postLogoutField html clientId)
    }

[<Fact>]
let ``an address just under the bound comes back, in a cookie a browser keeps`` () =
    task {
        let! app, operator, clientId, _ = setup ()
        use _ = app
        // `list=redirect-uris;uri=` and the escaped prefix take 57 characters:
        // the reason is 1,997, just under `MaxLength`.
        let typed = "https://*.example.com/" + String('a', 1940)
        let reason = Umbral.Handlers.Admin.Apps.KeptAddress.encode "redirect-uris" typed
        Assert.True(reason.IsSome && reason.Value.Length > 1990)

        let! refused = addRedirect operator clientId typed

        // Browsers drop a cookie whose name and value pass 4,096 bytes, silently.
        let noticeCookie =
            match refused.Headers.TryGetValues "Set-Cookie" with
            | true, values ->
                Option.ofObj values
                |> Option.bind (Seq.tryFind (fun c -> c.StartsWith "umbral_notice_reason="))
            | _ -> None

        Assert.True(noticeCookie.IsSome, "the refusal writes the notice cookie")
        let nameAndValue = noticeCookie.Value.Split(';').[0]
        Assert.True(nameAndValue.Length < 4096, $"the cookie fits a browser ({nameAndValue.Length})")

        let! html = operator.GetHtml(location refused)
        Assert.Equal((Some typed, true), redirectField html clientId)
    }

[<Fact>]
let ``a kept address is for its own app only`` () =
    task {
        let! app, operator, clientId, _ = setup ()
        use _ = app
        let! otherId, _ = registerApp operator "Other"

        let! _ = addRedirect operator clientId "https://*.example.com/cb"

        // Another app's page, asked for the same notice, ignores the cookie.
        let! html = operator.GetHtml($"{pageOf otherId}?msg=app-redirect-uri-invalid")
        Assert.Equal((None, false), redirectField html otherId)
        Assert.Equal((None, false), postLogoutField html otherId)
    }

[<Fact>]
let ``KeptAddress round-trips what it carries`` () =
    for typed in [ "https://*.example.com/cb?a=1;b=2"; "100%/é/日本"; "a=b;c=d"; "%41%zz" ] do
        let reason = Umbral.Handlers.Admin.Apps.KeptAddress.encode "redirect-uris" typed
        Assert.True(reason.IsSome, typed)
        Assert.Equal(Some typed, Umbral.Handlers.Admin.Apps.KeptAddress.decode "redirect-uris" reason.Value)

[<Fact>]
let ``KeptAddress carries nothing for another list, an empty address, too much or garbage`` () =
    let open' = Umbral.Handlers.Admin.Apps.KeptAddress.encode
    let decode = Umbral.Handlers.Admin.Apps.KeptAddress.decode
    let max = Umbral.Handlers.Admin.Apps.KeptAddress.MaxLength

    let reason = (open' "redirect-uris" "https://*.example.com/cb").Value
    Assert.Equal(None, decode "post-logout-uris" reason)
    Assert.Equal(None, open' "redirect-uris" "")
    Assert.Equal(None, open' "redirect-uris" (String('a', max + 1)))
    // Short enough as typed, too long once percent-encoded.
    Assert.Equal(None, open' "redirect-uris" (String('/', max - 100)))
    Assert.Equal(None, decode "redirect-uris" "")
    Assert.Equal(None, decode "redirect-uris" "garbage")
    Assert.Equal(None, decode "redirect-uris" "list=redirect-uris")
    Assert.Equal(None, decode "redirect-uris" "list=redirect-uris;uri=")
    Assert.Equal(None, decode "redirect-uris" "uri=x")

[<Theory>]
[<InlineData("https://grafana.example.com/callback")>]
// Only the host's case differs: the same address to a provider.
[<InlineData("https://GRAFANA.example.com/callback")>]
let ``a duplicate is refused and the list is unchanged`` (candidate: string) =
    task {
        let! app, operator, clientId, _ = setup ()
        use _ = app
        let! before = updates app clientId

        let! refused = addRedirect operator clientId candidate
        assertNotice clientId "app-redirect-uri-duplicate" refused

        do! assertUnchanged app operator clientId [ RedirectUri ] before.Length
    }

[<Fact>]
let ``the eleventh address is refused, and a full list shows no add form`` () =
    task {
        let! app, operator, clientId, _ = setup ()
        use _ = app

        let extras = [ for i in 2 .. 10 -> $"https://app{i}.example.com/cb" ]

        for uri in extras do
            let! added = addRedirect operator clientId uri
            assertNotice clientId "app-redirect-uri-added" added

        let expected = RedirectUri :: extras
        let! before = updates app clientId

        let! refused = addRedirect operator clientId "https://app11.example.com/cb"
        assertNotice clientId "app-redirect-uri-limit" refused
        do! assertUnchanged app operator clientId expected before.Length

        // The full list has no form to add to it; the other list still has.
        let! html = operator.GetHtml(pageOf clientId)
        Assert.DoesNotContain($"action=\"/admin/apps/{clientId}/redirect-uris\"", html)
        Assert.Contains($"action=\"/admin/apps/{clientId}/post-logout-uris\"", html)
    }

[<Fact>]
let ``the only redirect URI cannot be removed`` () =
    task {
        let! app, operator, clientId, _ = setup ()
        use _ = app
        let! before = updates app clientId

        let! refused = removeRedirect operator clientId RedirectUri
        assertNotice clientId "app-redirect-uri-last" refused

        do! assertUnchanged app operator clientId [ RedirectUri ] before.Length

        // And the pane offers no Remove for it.
        let! html = operator.GetHtml(pageOf clientId)
        Assert.DoesNotContain($"action=\"/admin/apps/{clientId}/redirect-uris/remove\"", html)
    }

[<Fact>]
let ``removing an address the list does not hold is refused`` () =
    task {
        let! app, operator, clientId, _ = setup ()
        use _ = app
        let! before = updates app clientId

        let! refused = removeRedirect operator clientId second
        assertNotice clientId "app-redirect-uri-not-registered" refused

        let! refusedPost = removePostLogout operator clientId second
        assertNotice clientId "app-redirect-uri-not-registered" refusedPost

        do! assertUnchanged app operator clientId [ RedirectUri ] before.Length
    }

[<Fact>]
let ``a post without the antiforgery token is refused and changes nothing`` () =
    task {
        let! app, operator, clientId, _ = setup ()
        use _ = app
        let! before = updates app clientId

        for route in [ "redirect-uris"; "redirect-uris/remove"; "post-logout-uris"; "post-logout-uris/remove" ] do
            let! response = operator.PostWithoutToken($"/admin/apps/{clientId}/{route}", [ "uri", second ])
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)

        do! assertUnchanged app operator clientId [ RedirectUri ] before.Length
    }

[<Fact>]
let ``an unknown client id goes back to the list with app-not-found`` () =
    task {
        let! app, operator, _, _ = setup ()
        use _ = app

        for route in [ "redirect-uris"; "redirect-uris/remove"; "post-logout-uris"; "post-logout-uris/remove" ] do
            let! response = operator.PostForm("/admin/apps", $"/admin/apps/no-such-app/{route}", [ "uri", second ])
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
            Assert.Equal("/admin/apps?msg=app-not-found", location response)
    }

// ---------------------------------------------------------------------------
// 3. Sign-in through each address, with the secret the app was born with
// ---------------------------------------------------------------------------

[<Fact>]
let ``an app signs in through either of its two addresses with its original secret`` () =
    task {
        let! app, operator, clientId, secret = setup ()
        use _ = app

        let! added = addRedirect operator clientId second
        assertNotice clientId "app-redirect-uri-added" added

        // Edited before the exchange: the secret surviving the edit is part of the claim.
        do! assertSignsIn app clientId secret RedirectUri
        do! assertSignsIn app clientId secret second
    }

// ---------------------------------------------------------------------------
// 4. A removed address is refused at authorize
// ---------------------------------------------------------------------------

[<Fact>]
let ``a removed redirect URI is refused at authorize and the remaining one still signs in`` () =
    task {
        let! app, operator, clientId, secret = setup ()
        use _ = app

        let! _ = addRedirect operator clientId second
        do! assertSignsIn app clientId secret second

        let! removed = removeRedirect operator clientId second
        assertNotice clientId "app-redirect-uri-removed" removed

        let! logged = details app clientId
        Assert.Equal("redirect_uri_removed;uri=" + Uri.EscapeDataString second, List.last logged)

        let person = Browser app.Client
        let! refused = person.Get(authorizeWith clientId second)

        // The assertion that matters: the browser is not sent to an address the
        // app no longer has, and no code is issued for it.
        let where = refused.Headers.Location |> Option.ofObj |> Option.map string |> Option.defaultValue ""
        Assert.False(where.StartsWith second, $"redirected to the removed address: {where}")
        Assert.DoesNotContain("code=", where)
        // Umbral's own error page, as for any unregistered redirect_uri.
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode)

        let! redirects = redirectsOf operator clientId
        Assert.Equal<string list>([ RedirectUri ], redirects)
        do! assertSignsIn app clientId secret RedirectUri
    }

// ---------------------------------------------------------------------------
// 5. The post-logout list
// ---------------------------------------------------------------------------

let private signOut (app: Fixtures.TestApp) (clientId: string) (postLogout: string) =
    let person = Browser app.Client

    person.Get(
        "/connect/logout?client_id=" + Uri.EscapeDataString clientId
        + "&post_logout_redirect_uri=" + Uri.EscapeDataString postLogout
    )

[<Fact>]
let ``post-logout addresses are added, used at sign-out and removed`` () =
    task {
        let! app, operator, clientId, _ = setup ()
        use _ = app

        let after1 = "https://grafana.example.com/signed-out"
        let after2 = "https://grafana.example.com/bye"

        let! added1 = addPostLogout operator clientId after1
        assertNotice clientId "app-post-logout-uri-added" added1
        let! added2 = addPostLogout operator clientId after2
        assertNotice clientId "app-post-logout-uri-added" added2

        let! postLogouts = postLogoutsOf operator clientId
        Assert.Equal<string list>([ after1; after2 ], postLogouts)

        let! logged = details app clientId
        let lastTwo = logged |> List.rev |> List.take 2 |> List.rev

        Assert.Equal<string list>(
            [ "post_logout_redirect_uri_added;uri=" + Uri.EscapeDataString after1
              "post_logout_redirect_uri_added;uri=" + Uri.EscapeDataString after2 ],
            lastTwo
        )

        // Signing out sends the person to the address the app asked for.
        for uri in [ after1; after2 ] do
            let! response = signOut app clientId uri
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
            Assert.StartsWith(uri, location response)

        // Remove one: it is no longer somewhere Umbral will send anyone.
        let! removed = removePostLogout operator clientId after1
        assertNotice clientId "app-post-logout-uri-removed" removed
        let! logged = details app clientId
        Assert.Equal("post_logout_redirect_uri_removed;uri=" + Uri.EscapeDataString after1, List.last logged)

        let! gone = signOut app clientId after1
        let where = gone.Headers.Location |> Option.ofObj |> Option.map string |> Option.defaultValue ""
        // Umbral's home, not the address the app asked for.
        Assert.Equal("/", where)

        let! still = signOut app clientId after2
        Assert.StartsWith(after2, location still)

        // The last one may go: sign-out then has nowhere of the app's to go.
        let! removedLast = removePostLogout operator clientId after2
        assertNotice clientId "app-post-logout-uri-removed" removedLast
        let! emptied = postLogoutsOf operator clientId
        Assert.Empty emptied
        // The redirect list is untouched by all of it.
        let! redirects = redirectsOf operator clientId
        Assert.Equal<string list>([ RedirectUri ], redirects)
    }

// ---------------------------------------------------------------------------
// 6. An app that has been there since its creation
// ---------------------------------------------------------------------------

[<Fact>]
let ``the original address keeps working after one is added and after one is removed`` () =
    task {
        let! app, operator, clientId, secret = setup ()
        use _ = app

        do! assertSignsIn app clientId secret RedirectUri

        let! _ = addRedirect operator clientId second
        do! assertSignsIn app clientId secret RedirectUri

        let! _ = removeRedirect operator clientId second
        do! assertSignsIn app clientId secret RedirectUri
    }
