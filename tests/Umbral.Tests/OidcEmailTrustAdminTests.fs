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
