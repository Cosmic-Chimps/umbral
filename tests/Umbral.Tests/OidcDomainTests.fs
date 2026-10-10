/// The pure rules of the OpenID Connect connector (specs/002): the kind, the
/// issuer's one spelling, and the identity key `<issuer>#<sub>`. No network and
/// no database; the few tests that go through the admin screen only pin what is
/// hidden or refused until the screen learns about the kind.
module Umbral.Tests.OidcDomainTests

open System
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

// ---------------------------------------------------------------------------
// The kind
// ---------------------------------------------------------------------------

[<Fact>]
let ``oidc is a kind that parses and prints as itself`` () =
    Assert.Equal(Some Oidc, parseKind "oidc")
    Assert.Equal("oidc", Oidc.Value)
    Assert.Equal("OpenID Connect", Oidc.DisplayName)

[<Fact>]
let ``the built-in kinds do not include oidc, so it never gets an empty card`` () =
    Assert.Equal<ProviderKind list>([ Discord; GitHub; Google ], allKinds)

[<Fact>]
let ``the static oidc entry holds only what every provider shares and no address`` () =
    let endpoints = endpointsFor Oidc

    // The addresses are the provider's to give; `Resolve` fills them.
    Assert.Equal("", endpoints.AuthorizationUrl)
    Assert.Equal("", endpoints.TokenUrl)
    Assert.Equal("", endpoints.UserInfoUrl)
    Assert.Equal<string list>([ "openid"; "email"; "profile" ], endpoints.Scopes)
    Assert.True endpoints.UsesNonce
    Assert.True endpoints.SupportsPkce

// ---------------------------------------------------------------------------
// Issuer normalisation
// ---------------------------------------------------------------------------

let private normalise allowLocalhost (input: string) = normaliseIssuer allowLocalhost input

/// One row of the table: what goes in, whether the instance allows plain http
/// on loopback, and what comes out.
let private accepted =
    [ // Already in its one spelling.
      "https://auth.example.com/application/o/umbral", false, "https://auth.example.com/application/o/umbral"
      // Providers differ on a trailing slash; one is dropped.
      "https://auth.example.com/application/o/umbral/", false, "https://auth.example.com/application/o/umbral"
      "https://auth.example.com/", false, "https://auth.example.com"
      "https://auth.example.com", false, "https://auth.example.com"
      // Only one: `/o//` is the path `/o/`.
      "https://auth.example.com/o//", false, "https://auth.example.com/o/"
      // Host case does not matter, path case does.
      "https://Auth.Example.COM/Realms/Home", false, "https://auth.example.com/Realms/Home"
      // The default port is the same address; any other is part of it.
      "https://auth.example.com:443/o", false, "https://auth.example.com/o"
      "https://auth.example.com:8443/o", false, "https://auth.example.com:8443/o"
      // Pasted with spaces around it.
      "  https://auth.example.com/o  ", false, "https://auth.example.com/o"
      // https on loopback needs no flag.
      "https://localhost:8443/o", false, "https://localhost:8443/o"
      // Plain http, loopback only, only where the instance allows it.
      "http://localhost:8080/realms/x", true, "http://localhost:8080/realms/x"
      "http://127.0.0.1:8080/realms/x/", true, "http://127.0.0.1:8080/realms/x"
      "http://[::1]:8080/o", true, "http://[::1]:8080/o" ]

let private refused =
    [ "", false, IssuerEmpty
      "   ", false, IssuerEmpty
      // Relative, or not an address. `System.Uri` reads a bare path as file://
      // on Unix; it must not slip through as a valid issuer.
      "/application/o/umbral", false, IssuerNotAbsolute
      "auth.example.com/application/o/umbral", false, IssuerNotAbsolute
      "not a url", false, IssuerNotAbsolute
      "file:///etc/passwd", false, IssuerNotAbsolute
      // Anything but https.
      "http://auth.example.com/o", false, IssuerNotHttps
      "http://auth.example.com/o", true, IssuerNotHttps
      "ftp://auth.example.com/o", false, IssuerNotHttps
      // Loopback over http is for instances that allow it, and only loopback.
      "http://localhost:8080/o", false, IssuerNotHttps
      "http://127.0.0.1/o", false, IssuerNotHttps
      "http://[::1]/o", false, IssuerNotHttps
      "http://localhost.evil.example/o", true, IssuerNotHttps
      "http://127.0.0.1.evil.example/o", true, IssuerNotHttps
      // An issuer is an address, not a search.
      "https://auth.example.com/o?tenant=1", false, IssuerHasQueryOrFragment
      "https://auth.example.com/o?", false, IssuerHasQueryOrFragment
      "https://auth.example.com/o#frag", false, IssuerHasQueryOrFragment
      "https://auth.example.com/o/#", false, IssuerHasQueryOrFragment ]

[<Fact>]
let ``issuers are normalised to one spelling`` () =
    for input, allowLocalhost, expected in accepted do
        Assert.True(
            (normalise allowLocalhost input = Ok expected),
            $"'{input}' (localhost http {allowLocalhost}) should normalise to '{expected}', got {normalise allowLocalhost input}"
        )

[<Fact>]
let ``issuers that are not absolute https addresses are refused with the reason`` () =
    for input, allowLocalhost, expected in refused do
        Assert.True(
            (normalise allowLocalhost input = Error expected),
            $"'{input}' (localhost http {allowLocalhost}) should be refused as {expected}, got {normalise allowLocalhost input}"
        )

[<Fact>]
let ``a missing issuer is the empty case`` () =
    Assert.Equal(Error IssuerEmpty, normaliseIssuer false null)

[<Fact>]
let ``normalising twice changes nothing`` () =
    // Except `/o//`, where each pass drops one more slash: the rule is one
    // trailing slash, and no provider publishes an issuer that ends in two.
    for input, allowLocalhost, _ in accepted |> List.filter (fun (input, _, _) -> not (input.EndsWith "//")) do
        match normalise allowLocalhost input with
        | Ok once -> Assert.True((normalise allowLocalhost once = Ok once), $"'{once}' normalised again changed")
        | Error _ -> ()

[<Fact>]
let ``every issuer error tells the operator what to do`` () =
    let messages =
        [ IssuerEmpty; IssuerNotAbsolute; IssuerNotHttps; IssuerHasQueryOrFragment ]
        |> List.map _.Message

    // Distinct, so the operator can tell which rule they broke.
    Assert.Equal(messages.Length, (List.distinct messages).Length)

    for message in messages do
        Assert.False(String.IsNullOrWhiteSpace message)

    // The contract's words for the one most people hit.
    Assert.Contains("must start with https://", IssuerNotHttps.Message)
    Assert.Contains("reverse proxy", IssuerNotHttps.Message)

// ---------------------------------------------------------------------------
// The identity key
// ---------------------------------------------------------------------------

[<Fact>]
let ``a subject is the issuer and the sub joined by a hash`` () =
    Assert.Equal(
        "https://auth.example.com/application/o/umbral#7f3c",
        oidcSubject "https://auth.example.com/application/o/umbral" "7f3c"
    )

[<Fact>]
let ``a subject round trips, and a sub that contains a hash survives`` () =
    for sub in [ "7f3c"; "1"; "a#b"; "#"; "x#y#z"; "user@example.com" ] do
        let subject = oidcSubject "https://auth.example.com/o" sub
        Assert.Equal(Some("https://auth.example.com/o", sub), parseOidcSubject subject)

[<Fact>]
let ``a subject without both halves does not parse`` () =
    Assert.Equal(None, parseOidcSubject "no-separator")
    Assert.Equal(None, parseOidcSubject "#only-a-sub")
    Assert.Equal(None, parseOidcSubject "https://auth.example.com/o#")
    Assert.Equal(None, parseOidcSubject "")

[<Fact>]
let ``the same sub under two issuers is two identities`` () =
    // Many products issue small integers; a sub is unique only within its issuer.
    Assert.NotEqual<string>(oidcSubject "https://one.example.com" "1", oidcSubject "https://two.example.com" "1")

[<Fact>]
let ``an issuer spelled two ways is one identity once normalised`` () =
    let subject spelling =
        match normaliseIssuer false spelling with
        | Ok issuer -> oidcSubject issuer "42"
        | Error error -> failwith error.Message

    Assert.Equal(subject "https://auth.example.com/o/", subject "https://AUTH.example.com/o")

// ---------------------------------------------------------------------------
// Through the admin screen. The add form does not offer `oidc` yet (Phase 3),
// so these pin that it cannot be created by a hand-made post either; when the
// form learns the kind (T022, T023) the first two change with it.
// ---------------------------------------------------------------------------

let private instance () =
    task {
        let! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app
        return app, operator
    }

let private fields (kind: string) (slug: string) =
    [ "kind", kind
      "slug", slug
      "displayName", "Home"
      "clientId", "client-id"
      "clientSecret", "client-secret" ]

[<Fact>]
let ``oidc cannot be the slug of a connector of another kind`` () =
    task {
        let! app, operator = instance ()
        use _ = app

        // `oidc` is a kind's name now, so the rule that keeps every kind's name
        // for its own connectors reserves it with no further code.
        let! refused = operator.PostForm("/admin/connectors", "/admin/connectors", fields "fake" "oidc")
        Assert.Contains("connector-slug-taken", location refused)

        let! stored = ConnectorStore.findBySlug app.DbConfig "oidc"
        Assert.True stored.IsNone
    }

[<Fact>]
let ``a hand-made post for an oidc connector creates nothing until the form carries an issuer`` () =
    task {
        let! app, operator = instance ()
        use _ = app

        let! refused = operator.PostForm("/admin/connectors", "/admin/connectors", fields "oidc" "home")
        Assert.Contains("connector-unknown", location refused)

        let! stored = ConnectorStore.findBySlug app.DbConfig "home"
        Assert.True stored.IsNone
    }

[<Fact>]
let ``the oidc name is not an empty card that saves an issuer-less connector`` () =
    task {
        let! app, operator = instance ()
        use _ = app

        let! refused =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors/oidc",
                [ "clientId", "client-id"; "clientSecret", "client-secret" ]
            )

        Assert.Contains("connector-unknown", location refused)

        let! stored = ConnectorStore.findBySlug app.DbConfig "oidc"
        Assert.True stored.IsNone
    }

[<Fact>]
let ``the add form does not offer oidc yet`` () =
    task {
        let! app, operator = instance ()
        use _ = app

        let! screen = operator.GetHtml "/admin/connectors"
        Assert.DoesNotContain("value=\"oidc\"", screen)
        Assert.DoesNotContain("data-connector-kind=\"oidc\"", screen)
    }
