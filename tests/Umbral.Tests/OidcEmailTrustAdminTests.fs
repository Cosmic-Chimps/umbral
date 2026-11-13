/// The trust setting on the add form (specs/017 T003): the group of two cards,
/// required for an OpenID Connect provider and for no other kind, kept across a
/// refusal, refused with an own list of allowed domains when trust is off, and
/// stored with the connector and in its `connector_created` event. Words are
/// those of contracts/screens.md.
module Umbral.Tests.OidcEmailTrustAdminTests

open System
open System.Net
open System.Text.RegularExpressions
open System.Threading.Tasks
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.StubOidcProvider

let private issuer = "https://auth.example.test/application/o/umbral"
let private providerSecret = "the-provider-client-secret-7c1e9a"

let private fields (trust: string option) (domains: string) =
    [ "kind", "oidc"
      "slug", "work-sso"
      "displayName", "Work SSO"
      "issuer", issuer
      "clientId", "umbral-client"
      "clientSecret", providerSecret
      "shared", "true" ]
    @ (match trust with
       | Some value -> [ "emailTrust", value ]
       | None -> [])
    @ (if domains = "" then [] else [ "allowedDomains", domains ])

let private start () =
    task {
        let stub = create issuer
        let! app = Fixtures.createAppWithProviderHandler stub fakeSettings ignore
        stub.Now <- fun () -> app.Clock.GetUtcNow()

        let operator = Browser app.Client
        do! claimInstance app operator
        return app, operator
    }

/// The `value`s of the trust radios that are checked, in the page.
let private checkedChoices (html: string) =
    [ for m in Regex.Matches(html, "<input[^>]*name=\"emailTrust\"[^>]*>") do
          let tag = m.Value

          if tag.Contains "checked" then
              yield Regex.Match(tag, "value=\"([^\"]*)\"").Groups[1].Value ]

let private radioCount (html: string) =
    Regex.Matches(html, "<input[^>]*name=\"emailTrust\"[^>]*>").Count

let private refusedWith (operator: Browser) (app: Fixtures.TestApp) (fields: (string * string) list) =
    task {
        let! response = operator.PostForm("/admin/connectors/new", "/admin/connectors", fields)
        let! html = response.Content.ReadAsStringAsync()
        Assert.Equal(enum<HttpStatusCode> 422, response.StatusCode)
        Assert.Contains("data-add-connector", html)

        let! stored = ConnectorStore.findBySlug app.DbConfig "work-sso"
        Assert.True(stored.IsNone, "a connector was stored despite the refusal")

        // Every typed value but the secret comes back.
        Assert.Contains("value=\"work-sso\"", html)
        Assert.Contains("value=\"Work SSO\"", html)
        Assert.Contains($"value=\"{issuer}\"", html)
        Assert.Contains("value=\"umbral-client\"", html)
        Assert.DoesNotContain(providerSecret, html)
        return html
    }

let private createdDetail (app: Fixtures.TestApp) =
    Flows.scalar app "SELECT ifnull(detail, '') FROM security_events WHERE event_type = 'connector_created' AND connector_slug = 'work-sso'" []

[<Fact>]
let ``the add form draws the trust group with neither card checked on a first visit`` () =
    task {
        let! app, operator = start ()
        use _ = app

        let! html = operator.GetHtml "/admin/connectors/new"
        Assert.Contains("data-email-trust", html)
        Assert.Contains("Verified email addresses from this provider", html)
        Assert.Contains("Trusted", html)
        Assert.Contains("Not trusted", html)
        Assert.Equal(2, radioCount html)
        Assert.Empty(checkedChoices html)
        Assert.DoesNotContain("data-email-trust-error", html)
        Assert.DoesNotContain("data-email-trust-warning", html)

        // Before the allowed domains field.
        Assert.True(html.IndexOf "data-email-trust" < html.IndexOf "name=\"allowedDomains\"")
    }

[<Fact>]
let ``no choice is refused with the message under the group and every typed value kept`` () =
    task {
        let! app, operator = start ()
        use _ = app

        let! html = refusedWith operator app (fields None "acme.com")
        Assert.Contains("data-add-error=\"emailTrust\"", html)
        Assert.Contains("data-email-trust-error", html)
        Assert.Contains("Choose whether to trust this provider", html)
        Assert.Contains("value=\"acme.com\"", html)
        Assert.Empty(checkedChoices html)
    }

[<Theory>]
[<InlineData("maybe")>]
[<InlineData("ON")>]
[<InlineData("true")>]
let ``a value other than on or off is refused as no choice`` (value: string) =
    task {
        let! app, operator = start ()
        use _ = app

        let! html = refusedWith operator app (fields (Some value) "")
        Assert.Contains("data-add-error=\"emailTrust\"", html)
        Assert.Contains("Choose whether to trust this provider", html)
        Assert.Empty(checkedChoices html)
    }

[<Fact>]
let ``off with an own list of domains is refused under the domains field and the choice is kept`` () =
    task {
        let! app, operator = start ()
        use _ = app

        let! html = refusedWith operator app (fields (Some "off") "acme.com")
        Assert.Contains("data-add-error=\"allowedDomains\"", html)
        Assert.Contains("Allowed email domains need verified addresses", html)
        Assert.Contains("Trust them, or clear this list.", html)
        Assert.DoesNotContain("data-email-trust-error", html)
        Assert.Contains("value=\"acme.com\"", html)
        Assert.Equal<string list>([ "off" ], checkedChoices html)
    }

[<Fact>]
let ``a refusal about another field keeps the chosen card`` () =
    task {
        let! app, operator = start ()
        use _ = app

        // An invalid slug: refused before the trust is looked at.
        let broken = fields (Some "on") "" |> List.map (fun (n, v) -> if n = "slug" then n, "Bad Slug!" else n, v)
        let! response = operator.PostForm("/admin/connectors/new", "/admin/connectors", broken)
        let! html = response.Content.ReadAsStringAsync()

        Assert.Equal(enum<HttpStatusCode> 422, response.StatusCode)
        Assert.Contains("data-add-error=\"slug\"", html)
        Assert.Equal<string list>([ "on" ], checkedChoices html)
        Assert.DoesNotContain(providerSecret, html)
    }

[<Theory>]
[<InlineData("on", true)>]
[<InlineData("off", false)>]
let ``each choice is stored with the connector and named in the connector_created event`` (value: string, expected: bool) =
    task {
        let! app, operator = start ()
        use _ = app

        let! response = operator.PostForm("/admin/connectors/new", "/admin/connectors", fields (Some value) "")
        Assert.Contains("connector-added-oidc", response.Headers.Location |> string)

        let! stored = ConnectorStore.findBySlug app.DbConfig "work-sso"
        Assert.Equal(Some expected, stored.Value.EmailTrust)
        Assert.Equal(expected, trustsProviderEmail stored.Value)

        let! detail = createdDetail app
        Assert.Contains($"email_trust={value}", detail)
        Assert.StartsWith("store=", detail)
    }

[<Fact>]
let ``trust on keeps an own list of domains`` () =
    task {
        let! app, operator = start ()
        use _ = app

        let! response = operator.PostForm("/admin/connectors/new", "/admin/connectors", fields (Some "on") "acme.com")
        Assert.Contains("connector-added-oidc", response.Headers.Location |> string)

        let! stored = ConnectorStore.findBySlug app.DbConfig "work-sso"
        Assert.Equal<string list>([ "acme.com" ], stored.Value.AllowedEmailDomains)
        Assert.Equal(Some true, stored.Value.EmailTrust)
    }

[<Fact>]
let ``a connector of another kind is added without the group's value and stores none`` () =
    task {
        let! app, operator = start ()
        use _ = app

        let! response =
            operator.PostForm(
                "/admin/connectors/new",
                "/admin/connectors",
                [ "kind", "github"
                  "slug", "gh"
                  "displayName", "GitHub"
                  "clientId", "gh-client"
                  "clientSecret", "gh-secret" ]
            )

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.DoesNotContain("emailTrust", response.Headers.Location |> string)

        let! stored = ConnectorStore.findBySlug app.DbConfig "gh"
        Assert.Equal(GitHub, stored.Value.Kind)
        Assert.Equal(None, stored.Value.EmailTrust)

        let! detail =
            Flows.scalar app "SELECT ifnull(detail, '') FROM security_events WHERE event_type = 'connector_created' AND connector_slug = 'gh'" []

        Assert.DoesNotContain("email_trust", detail)
    }

// ---------------------------------------------------------------------------
// The pane (T004a)
// ---------------------------------------------------------------------------

let private pane = "/admin/connectors/work-sso"

/// An app with `work-sso` added through the form with the given choice.
let private startWith (trust: string) =
    task {
        let! app, operator = start ()
        let! response = operator.PostForm("/admin/connectors/new", "/admin/connectors", fields (Some trust) "")
        Assert.Contains("connector-added-oidc", response.Headers.Location |> string)
        return app, operator
    }

/// What the pane posts: every field, the secret empty (kept).
let private paneFields (trust: string option) (domains: string) =
    [ "displayName", "Work SSO"; "clientId", "umbral-client"; "shared", "true"; "allowedDomains", domains ]
    @ (match trust with
       | Some value -> [ "emailTrust", value ]
       | None -> [])

let private savePane (operator: Browser) (posted: (string * string) list) =
    operator.PostForm("/admin/connectors", pane, posted)

/// The save the confirming form posts to.
let private savePaneConfirmed (operator: Browser) (posted: (string * string) list) =
    operator.PostForm("/admin/connectors", pane + "?confirm=trust-off", posted)

let private updatedDetails (app: Fixtures.TestApp) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()

        command.CommandText <-
            "SELECT ifnull(detail, '') FROM security_events WHERE event_type = 'connector_updated' AND connector_slug = 'work-sso' ORDER BY id"

        use! reader = command.ExecuteReaderAsync()
        let rows = ResizeArray()

        while reader.Read() do
            rows.Add(reader.GetString 0)

        return List.ofSeq rows
    }

let private sql (app: Fixtures.TestApp) (statement: string) =
    task {
        let! _ = Flows.scalar app statement []
        return ()
    }

/// Two people with a verified address through this connector's issuer: `solo`
/// by it alone, `both` also by a Google link. Trust off lowers `solo` only.
let private seedPeople (app: Fixtures.TestApp) =
    task {
        for id, email in [ "solo", "solo@example.com"; "both", "both@example.com" ] do
            do!
                sql
                    app
                    $"INSERT INTO users (id, display_name, email, email_verified, first_seen_utc, last_seen_utc)
                      VALUES ('{id}', '{id}', '{email}', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')"

            do!
                sql
                    app
                    $"INSERT INTO linked_accounts (provider_kind, provider_subject, user_id, asserted_email, asserted_email_verified, linked_utc, last_used_utc)
                      VALUES ('oidc', '{issuer}#{id}', '{id}', '{email}', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')"

        do!
            sql
                app
                "INSERT INTO linked_accounts (provider_kind, provider_subject, user_id, asserted_email, asserted_email_verified, linked_utc, last_used_utc)
                 VALUES ('google', 'g-both', 'both', 'both@example.com', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')"
    }

let private flagOf (app: Fixtures.TestApp) (id: string) =
    Flows.scalar app $"SELECT email_verified FROM users WHERE id = '{id}'" []

let private expectEq (expected: string) (actual: Task<string>) =
    task {
        let! value = actual
        Assert.Equal(expected, value)
    }

let private entryTag (html: string) =
    Regex.Match(html, "<a\\b[^>]*data-connector-entry=\"work-sso\"[^>]*>").Value

[<Fact>]
let ``the pane checks on for a trusted connector, shows the hint, and the group is not on another kind's pane`` () =
    task {
        let! app, operator = startWith "on"
        use _ = app

        let! html = operator.GetHtml pane
        Assert.Contains("data-email-trust", html)
        Assert.Equal<string list>([ "on" ], checkedChoices html)
        Assert.Contains("data-email-trust-hint", html)
        Assert.Contains("Turning trust off unlinks nobody who is already linked.", html)

        // Before the allowed domains field.
        Assert.True(html.IndexOf "data-email-trust" < html.IndexOf "name=\"allowedDomains\"")

        let! _ =
            operator.PostForm(
                "/admin/connectors/new",
                "/admin/connectors",
                [ "kind", "discord"; "slug", "discord"; "displayName", "Discord"; "clientId", "d-id"; "clientSecret", "d-secret" ]
            )

        let! discord = operator.GetHtml "/admin/connectors/discord"
        Assert.DoesNotContain("data-email-trust", discord)
        Assert.Equal(0, radioCount discord)
    }

[<Fact>]
let ``the pane checks off for an untrusted connector and for a null row`` () =
    task {
        let! app, operator = startWith "off"
        use _ = app

        let! off = operator.GetHtml pane
        Assert.Equal<string list>([ "off" ], checkedChoices off)

        do! sql app "UPDATE connectors SET email_trust = 1 WHERE slug = 'work-sso'"
        let! on = operator.GetHtml pane
        Assert.Equal<string list>([ "on" ], checkedChoices on)

        do! sql app "UPDATE connectors SET email_trust = NULL WHERE slug = 'work-sso'"
        let! unset = operator.GetHtml pane
        Assert.Equal<string list>([ "off" ], checkedChoices unset)
    }

[<Fact>]
let ``saving off from on stores off, lowers who only this connector verified and logs how many`` () =
    task {
        let! app, operator = startWith "on"
        use _ = app
        do! seedPeople app

        let! response = savePaneConfirmed operator (paneFields (Some "off") "")
        Assert.Contains("connector-saved", response.Headers.Location |> string)

        let! stored = ConnectorStore.findBySlug app.DbConfig "work-sso"
        Assert.Equal(Some false, stored.Value.EmailTrust)

        do! expectEq "0" (flagOf app "solo")
        do! expectEq "1" (flagOf app "both")

        let! details = updatedDetails app
        Assert.Equal("email_trust=off;lowered=1", Assert.Single details)
    }

[<Fact>]
let ``saving on from off stores on and logs it`` () =
    task {
        let! app, operator = startWith "off"
        use _ = app

        let! response = savePane operator (paneFields (Some "on") "")
        Assert.Contains("connector-saved", response.Headers.Location |> string)

        let! stored = ConnectorStore.findBySlug app.DbConfig "work-sso"
        Assert.Equal(Some true, stored.Value.EmailTrust)

        let! details = updatedDetails app
        Assert.Equal("email_trust=on", Assert.Single details)
    }

[<Theory>]
[<InlineData("on")>]
[<InlineData("off")>]
let ``saving the same value logs no trust part`` (value: string) =
    task {
        let! app, operator = startWith value
        use _ = app

        let renamed = paneFields (Some value) "" |> List.map (fun (n, v) -> if n = "displayName" then n, "Renamed" else n, v)
        let! response = savePane operator renamed
        Assert.Contains("connector-saved", response.Headers.Location |> string)

        let! details = updatedDetails app
        let detail = Assert.Single details
        Assert.DoesNotContain("email_trust", detail)
    }

[<Fact>]
let ``a post without the field leaves the setting as it is`` () =
    task {
        let! app, operator = startWith "off"
        use _ = app
        do! sql app "UPDATE connectors SET email_trust = NULL WHERE slug = 'work-sso'"

        let renamed = paneFields None "" |> List.map (fun (n, v) -> if n = "displayName" then n, "Renamed" else n, v)
        let! response = savePane operator renamed
        Assert.Contains("connector-saved", response.Headers.Location |> string)

        let! stored = ConnectorStore.findBySlug app.DbConfig "work-sso"
        Assert.Equal("Renamed", stored.Value.DisplayName)
        // A NULL row stays NULL: nothing was posted, nothing was lowered.
        Assert.Equal(None, stored.Value.EmailTrust)
    }

[<Theory>]
[<InlineData("maybe")>]
[<InlineData("")>]
let ``an invalid value is refused under the group and nothing is saved`` (value: string) =
    task {
        let! app, operator = startWith "on"
        use _ = app

        let renamed = paneFields (Some value) "acme.com" |> List.map (fun (n, v) -> if n = "displayName" then n, "Renamed" else n, v)
        let! response = savePane operator renamed
        let! html = response.Content.ReadAsStringAsync()

        Assert.Equal(enum<HttpStatusCode> 422, response.StatusCode)
        Assert.Contains("data-email-trust-error", html)
        Assert.Contains("Choose whether to trust this provider", html)
        // What was typed comes back; neither card is checked.
        Assert.Contains("value=\"Renamed\"", html)
        Assert.Contains("value=\"acme.com\"", html)
        Assert.Empty(checkedChoices html)

        let! stored = ConnectorStore.findBySlug app.DbConfig "work-sso"
        Assert.Equal("Work SSO", stored.Value.DisplayName)
        Assert.Equal(Some true, stored.Value.EmailTrust)
        Assert.Empty stored.Value.AllowedEmailDomains
        let! details = updatedDetails app
        Assert.Empty details
    }

[<Fact>]
let ``off with an own list is refused under the domains field, the choice kept, nothing saved`` () =
    task {
        let! app, operator = startWith "on"
        use _ = app
        do! seedPeople app

        let! response = savePane operator (paneFields (Some "off") "acme.com")
        let! html = response.Content.ReadAsStringAsync()

        Assert.Equal(enum<HttpStatusCode> 422, response.StatusCode)
        Assert.Contains("Allowed email domains need verified addresses", html)
        Assert.DoesNotContain("data-email-trust-error", html)
        Assert.Contains("value=\"acme.com\"", html)
        Assert.Equal<string list>([ "off" ], checkedChoices html)

        let! stored = ConnectorStore.findBySlug app.DbConfig "work-sso"
        Assert.Equal(Some true, stored.Value.EmailTrust)
        Assert.Empty stored.Value.AllowedEmailDomains
        do! expectEq "1" (flagOf app "solo")
    }

[<Fact>]
let ``off with a stored own list and no list in the post is refused too`` () =
    task {
        let! app, operator = startWith "on"
        use _ = app
        do! sql app "UPDATE connectors SET allowed_email_domains = 'acme.com' WHERE slug = 'work-sso'"

        // A post without the list field leaves the stored list, which trust off cannot keep.
        let posted = paneFields (Some "off") "" |> List.filter (fun (n, _) -> n <> "allowedDomains")
        let! response = savePane operator posted
        let! html = response.Content.ReadAsStringAsync()

        Assert.Equal(enum<HttpStatusCode> 422, response.StatusCode)
        Assert.Contains("Allowed email domains need verified addresses", html)
        let! stored = ConnectorStore.findBySlug app.DbConfig "work-sso"
        Assert.Equal(Some true, stored.Value.EmailTrust)
    }

[<Fact>]
let ``off with the instance list set shows the alert, the nobody rule line and the attention marker`` () =
    task {
        let! app, operator = startWith "off"
        use _ = app
        do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "acme.com" ]

        let! html = operator.GetHtml pane
        Assert.Contains("data-email-trust-blocked=\"work-sso\"", html)
        Assert.Contains("Nobody can sign in through this provider. The email domains allowed for every connector", html)
        Assert.Contains("data-rule=\"nobody\"", html)
        Assert.Contains("Nobody can sign in through this provider: the email domains allowed for every connector", html)
        Assert.Contains("data-attention", entryTag html)
    }

[<Fact>]
let ``off with no list anywhere shows the untrusted rule line and no alert`` () =
    task {
        let! app, operator = startWith "off"
        use _ = app

        let! html = operator.GetHtml pane
        Assert.Contains("data-rule=\"anyone-untrusted\"", html)
        Assert.Contains("Each is a separate account, because its addresses are not trusted.", html)
        Assert.DoesNotContain("data-email-trust-blocked", html)
        Assert.DoesNotContain("data-attention", entryTag html)
    }

[<Fact>]
let ``a trusted connector with the instance list set has no alert and the usual rule line`` () =
    task {
        let! app, operator = startWith "on"
        use _ = app
        do! EmailDomainRules.setInstanceList app.DbConfig app.Clock [ "acme.com" ]

        let! html = operator.GetHtml pane
        Assert.DoesNotContain("data-email-trust-blocked", html)
        Assert.Contains("data-rule=\"instance\"", html)
        Assert.DoesNotContain("data-attention", entryTag html)
    }

/// The hidden inputs of the form with this action, but for the antiforgery token.
let private hiddenFieldsOf (html: string) (action: string) =
    let m =
        Regex.Match(
            html,
            "<form\\b[^>]*\\baction=\"" + Regex.Escape action + "\"[^>]*>(.*?)</form>",
            RegexOptions.Singleline
        )

    Assert.True(m.Success, $"no form with action {action}")

    [ for i in Regex.Matches(m.Groups[1].Value, "<input\\b[^>]*type=\"hidden\"[^>]*>") do
          let name = Regex.Match(i.Value, "name=\"([^\"]*)\"").Groups[1].Value
          let value = Regex.Match(i.Value, "value=\"([^\"]*)\"").Groups[1].Value

          if name <> "__RequestVerificationToken" then
              name, string (WebUtility.HtmlDecode value) ]

[<Fact>]
let ``the unshare confirmation of an oidc connector still saves it unshared and leaves the trust as it was`` () =
    task {
        let! app, operator = startWith "off"
        use _ = app
        let! _ = createClientApp operator

        let unshared = paneFields (Some "off") "" |> List.filter (fun (n, _) -> n <> "shared")
        let! warned = savePane operator unshared
        let back = warned.Headers.Location |> string
        Assert.Contains("connector-strands-apps", back)

        let! stillShared = ConnectorStore.findBySlug app.DbConfig "work-sso"
        Assert.True stillShared.Value.IsShared

        let! screen = operator.GetHtml back
        let action = pane + "?confirm=yes"
        Assert.Contains($"action=\"{action}\"", screen)
        let hidden = hiddenFieldsOf screen action
        Assert.DoesNotContain("emailTrust", hidden |> List.map fst)

        let! confirmed = operator.PostForm(pane, action, hidden)
        Assert.Contains("connector-saved", confirmed.Headers.Location |> string)

        let! stored = ConnectorStore.findBySlug app.DbConfig "work-sso"
        Assert.False stored.Value.IsShared
        Assert.Equal(Some false, stored.Value.EmailTrust)
    }

// ---------------------------------------------------------------------------
// The confirmation before trust is turned off (T004b)
// ---------------------------------------------------------------------------

let private typedSecret = "a-new-secret-typed-on-the-pane-91"

[<Fact>]
let ``saving off from on asks first: the count, nothing saved, the typed values carried and no secret`` () =
    task {
        let! app, operator = startWith "on"
        use _ = app
        do! seedPeople app

        let renamed =
            paneFields (Some "off") ""
            |> List.map (fun (n, v) -> if n = "displayName" then n, "Renamed" else n, v)

        let! response = savePane operator (renamed @ [ "clientSecret", typedSecret ])
        let! html = response.Content.ReadAsStringAsync()

        Assert.Equal(enum<HttpStatusCode> 422, response.StatusCode)
        Assert.Contains("data-email-trust-confirm", html)
        Assert.Contains("data-email-trust-count=\"1\"", html)
        Assert.Contains("Turning trust off makes 1 stored email address unverified: only this provider verified it.", html)
        Assert.Contains("Nobody is unlinked, and turning trust on again", html)
        Assert.Contains("Turn trust off", html)
        Assert.Contains($"action=\"{pane}?confirm=trust-off\"", html)
        Assert.Contains($"<a href=\"{pane}\"", html)

        // The pane shows what was typed, the secret never, and says to type it again.
        Assert.Contains("value=\"Renamed\"", html)
        Assert.DoesNotContain(typedSecret, html)
        Assert.Contains("Type the client secret again", html)
        // The confirmation carries no secret, and says so (code review N2).
        Assert.Contains("The new client secret was not saved: type it again after you turn trust off.", html)
        // The pane's lead is the plain one: the "Only for" opening is the add form's.
        Assert.Contains("Some providers mark an address as verified without checking it.", html)
        Assert.DoesNotContain("Only for an OpenID Connect provider. Some providers", html)
        Assert.Equal<string list>([ "off" ], checkedChoices html)

        // The confirming form carries the typed values and no secret.
        let hidden = hiddenFieldsOf html (pane + "?confirm=trust-off")
        Assert.Contains(("displayName", "Renamed"), hidden)
        Assert.Contains(("clientId", "umbral-client"), hidden)
        Assert.Contains(("shared", "on"), hidden)
        Assert.Contains(("allowedDomains", ""), hidden)
        Assert.Contains(("emailTrust", "off"), hidden)
        Assert.DoesNotContain("clientSecret", hidden |> List.map fst)

        // Nothing changed: not the setting, not the name, not a flag, not the log.
        let! stored = ConnectorStore.findBySlug app.DbConfig "work-sso"
        Assert.Equal(Some true, stored.Value.EmailTrust)
        Assert.Equal("Work SSO", stored.Value.DisplayName)
        do! expectEq "1" (flagOf app "solo")
        let! details = updatedDetails app
        Assert.Empty details
    }

[<Fact>]
let ``posting the confirming form saves off, lowers and logs how many`` () =
    task {
        let! app, operator = startWith "on"
        use _ = app
        do! seedPeople app

        let renamed =
            paneFields (Some "off") ""
            |> List.map (fun (n, v) -> if n = "displayName" then n, "Renamed" else n, v)

        let! asked = savePane operator renamed
        let! html = asked.Content.ReadAsStringAsync()
        let action = pane + "?confirm=trust-off"
        let hidden = hiddenFieldsOf html action

        let! confirmed = operator.PostForm(pane, action, hidden)
        Assert.Contains("connector-saved", confirmed.Headers.Location |> string)

        let! stored = ConnectorStore.findBySlug app.DbConfig "work-sso"
        Assert.Equal(Some false, stored.Value.EmailTrust)
        Assert.Equal("Renamed", stored.Value.DisplayName)
        do! expectEq "0" (flagOf app "solo")
        do! expectEq "1" (flagOf app "both")

        let! details = updatedDetails app
        Assert.Equal("email_trust=off;lowered=1", Assert.Single details)
    }

[<Fact>]
let ``the confirmation says so when nobody depends on the provider's addresses`` () =
    task {
        let! app, operator = startWith "on"
        use _ = app

        let! response = savePane operator (paneFields (Some "off") "")
        let! html = response.Content.ReadAsStringAsync()

        Assert.Equal(enum<HttpStatusCode> 422, response.StatusCode)
        Assert.Contains("data-email-trust-count=\"0\"", html)
        Assert.Contains("Turning trust off makes no stored email address unverified. Nobody is unlinked.", html)
        Assert.DoesNotContain("stored email address unverified: only", html)
        // No secret was typed, so nothing is said about one.
        Assert.DoesNotContain("The new client secret was not saved", html)
    }

[<Fact>]
let ``saving off on a null row asks too, with the count`` () =
    task {
        let! app, operator = startWith "on"
        use _ = app
        do! seedPeople app
        do! sql app "UPDATE connectors SET email_trust = NULL WHERE slug = 'work-sso'"

        let! response = savePane operator (paneFields (Some "off") "")
        let! html = response.Content.ReadAsStringAsync()

        Assert.Equal(enum<HttpStatusCode> 422, response.StatusCode)
        Assert.Contains("data-email-trust-count=\"1\"", html)

        let! stored = ConnectorStore.findBySlug app.DbConfig "work-sso"
        Assert.Equal(None, stored.Value.EmailTrust)
        do! expectEq "1" (flagOf app "solo")
    }

[<Fact>]
let ``saving off from an already off connector asks nothing, even with people seeded`` () =
    task {
        let! app, operator = startWith "off"
        use _ = app
        do! seedPeople app

        let! response = savePane operator (paneFields (Some "off") "")
        Assert.Contains("connector-saved", response.Headers.Location |> string)
        // Their flags were never lowered by this save: the setting did not change.
        do! expectEq "1" (flagOf app "solo")
    }

[<Fact>]
let ``saving on never asks`` () =
    task {
        let! app, operator = startWith "off"
        use _ = app
        do! seedPeople app

        let! response = savePane operator (paneFields (Some "on") "")
        Assert.Contains("connector-saved", response.Headers.Location |> string)

        let! stored = ConnectorStore.findBySlug app.DbConfig "work-sso"
        Assert.Equal(Some true, stored.Value.EmailTrust)
    }

[<Fact>]
let ``a crafted confirmation that says on just saves on`` () =
    task {
        let! app, operator = startWith "off"
        use _ = app

        let! response = savePaneConfirmed operator (paneFields (Some "on") "")
        Assert.Contains("connector-saved", response.Headers.Location |> string)

        let! stored = ConnectorStore.findBySlug app.DbConfig "work-sso"
        Assert.Equal(Some true, stored.Value.EmailTrust)

        let! details = updatedDetails app
        Assert.Equal("email_trust=on", Assert.Single details)
    }
