/// The Microsoft connector's pane, its Save and its Enable check on the
/// Connectors screen (specs/016-microsoft-connector, T006).
///
/// Everything goes through the real screen and the real secret store, with the
/// fake Microsoft behind the provider calls. A refusal must save nothing, mark the
/// field it is about, keep every typed value but the secret, and never put the
/// secret back in the page; a saved secret must be in the store and nowhere else.
module Umbral.Tests.MicrosoftAdminTests

open System
open System.Net
open System.Text.RegularExpressions
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.Flows
open Umbral.Tests.Infrastructure.StubOidcProvider

module Ms = Umbral.Domains.Microsoft

let private texts () = Umbral.Views.Texts.current ()

/// `HtmlDecode`, which the nullness checker sees as possibly null.
let private decode (text: string) =
    WebUtility.HtmlDecode text |> Option.ofObj |> Option.defaultValue ""

let private path = "/admin/connectors/microsoft"

type private World =
    { App: Fixtures.TestApp
      Operator: Browser
      Fake: FakeMicrosoft.FakeMicrosoft }

/// An app with the fake Microsoft for `audience` behind its provider calls and the
/// operator signed in. The fake answers only below the audience's own segment, so
/// a save for another audience finds Microsoft unreachable.
let private startFor (audience: MicrosoftAudience) =
    task {
        let fake = FakeMicrosoft.create audience
        let! app = Fixtures.createAppWithProviderHandler fake.Handler fakeSettings ignore
        FakeMicrosoft.attach app fake
        let operator = Browser app.Client
        do! claimOperator app operator

        return
            { App = app
              Operator = operator
              Fake = fake }
    }

let private start () = startFor PersonalAndAnyOrganization

let private tenantA = Guid.Parse FakeMicrosoft.WorkTenantA

/// What the pane posts: the usual values, the "both" card.
let private fields =
    [ "displayName", "Microsoft"
      "clientId", FakeMicrosoft.ClientId
      "clientSecret", FakeMicrosoft.ClientSecret
      "audience", "common"
      "tenantId", ""
      "shared", "true"
      "allowedDomains", "" ]

/// The fields with some of them replaced.
let private with' (changes: (string * string) list) (original: (string * string) list) =
    original
    |> List.map (fun (name, value) ->
        name, (changes |> List.tryFind (fun (n, _) -> n = name) |> Option.map snd |> Option.defaultValue value))

let private save (world: World) (posted: (string * string) list) =
    world.Operator.PostForm(path, path, posted)

let private body (response: System.Net.Http.HttpResponseMessage) =
    task {
        let! text = response.Content.ReadAsStringAsync()
        return decode text
    }

let private stored (world: World) = ConnectorStore.findBySlug world.App.DbConfig "microsoft"

let private storedSecret (world: World) =
    task {
        let! connector = stored world
        let connector = connector.Value

        match Secrets.SecretRef.Decode connector.ClientSecretRef with
        | Ok reference -> return! Fixtures.readSecret world.App reference
        | Error _ -> return None
    }

let private count (world: World) (statement: string) = scalar world.App statement []

/// The opening tag of an input by its id.
let private controlOf (html: string) (name: string) =
    let m = Regex.Match(html, "<input\\b[^>]*\\bid=\"" + name + "\"[^>]*>")
    Assert.True(m.Success, $"no control {name}")
    m.Value

/// The text of the error under a field, or None when it has none.
let private errorOf (html: string) (name: string) =
    let m = Regex.Match(html, "<p[^>]*\\bid=\"" + name + "-error\"[^>]*>(.*?)</p>", RegexOptions.Singleline)

    if m.Success then
        Some((decode (Regex.Replace(m.Value, "<[^>]+>", ""))).Trim())
    else
        None

let private valueOf (html: string) (name: string) =
    let m = Regex.Match(controlOf html name, "\\bvalue=\"([^\"]*)\"")
    if m.Success then decode m.Groups[1].Value else ""

/// The four radio inputs of the audience group, in the order of the page.
let private radios (html: string) =
    [ for m in Regex.Matches(html, "<input\\b[^>]*\\bname=\"audience\"[^>]*>") -> m.Value ]

let private radioValue (radio: string) =
    Regex.Match(radio, "\\bvalue=\"([^\"]*)\"").Groups[1].Value

let private checkedRadios (html: string) =
    radios html |> List.filter (fun r -> r.Contains "checked") |> List.map radioValue

let private events (world: World) (eventType: string) =
    task {
        let! n = count world $"SELECT count(*) FROM security_events WHERE event_type = '{eventType}'"
        return int n
    }

/// A refused save: 422, nothing stored, each message under its field and the field
/// marked, the typed values drawn again, and the secret not in the page.
let private assertRefused (world: World) (posted: (string * string) list) (refusals: (string * string) list) =
    task {
        let! response = save world posted
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode)
        let! html = body response

        let! rows = count world "SELECT count(*) FROM connectors WHERE slug = 'microsoft'"
        Assert.Equal("0", rows)
        let! created = events world "connector_created"
        Assert.Equal(0, created)

        for field, message in refusals do
            Assert.Equal(Some message, errorOf html field)

            if field = "audience" then
                for radio in radios html do
                    Assert.Contains("aria-invalid=\"true\"", radio)
            else
                Assert.Contains("aria-invalid=\"true\"", controlOf html field)

        // Typed values kept (the secret never is).
        for name in [ "displayName"; "clientId"; "tenantId" ] do
            Assert.Equal(posted |> List.find (fun (n, _) -> n = name) |> snd, valueOf html name)

        let chosen = posted |> List.find (fun (n, _) -> n = "audience") |> snd

        Assert.Equal<string list>((if chosen = "" then [] else [ chosen ]), checkedRadios html)

        let secret = posted |> List.find (fun (n, _) -> n = "clientSecret") |> snd

        if secret <> "" then
            Assert.DoesNotContain(secret, html)

        // The box is drawn empty: no value at all.
        Assert.Matches("<input[^>]*id=\"clientSecret\"[^>]*value=\"\"", html)
    }

// ---------------------------------------------------------------------------
// The pane and the list
// ---------------------------------------------------------------------------

[<Fact>]
let ``not set up the list has a Microsoft entry after Apple, and the add form does not offer it`` () =
    task {
        let! world = start ()
        use _ = world.App
        let! list = world.Operator.GetHtml "/admin/connectors"

        let entries = [ for m in Regex.Matches(list, "data-connector-entry=\"([^\"]*)\"") -> m.Groups[1].Value ]
        Assert.Contains("microsoft", entries)
        Assert.Equal(List.findIndex ((=) "apple") entries + 1, List.findIndex ((=) "microsoft") entries)

        // "Not set up", as the other built-in kinds.
        let entry = Regex.Match(list, "data-connector-entry=\"microsoft\".*?</a>", RegexOptions.Singleline).Value
        Assert.Contains((texts ()).NotConfigured, decode entry)

        let! add = world.Operator.GetHtml "/admin/connectors/new"
        Assert.Contains("value=\"google\"", add)
        Assert.DoesNotContain("value=\"microsoft\"", add)
    }

[<Fact>]
let ``not set up the pane shows the return URL, the caveat, the advice, four cards with none chosen and the tenant field`` () =
    task {
        let! world = start ()
        use _ = world.App
        let! html = world.Operator.GetHtml path
        let decoded = decode html
        let t = texts ()

        Assert.Contains("data-connector-kind=\"microsoft\"", html)
        Assert.Contains("https://umbral.test/connect/microsoft/callback", html)
        Assert.Contains("data-setup-caveat=\"microsoft\"", html)
        Assert.Contains("A Microsoft client secret expires: after 6 months by default, 24 months at most.", decoded)
        Assert.Contains("data-naming-advice", html)
        Assert.Contains(t.MicrosoftNamingAdvice, decoded)

        for label in
            [ t.MicrosoftDisplayNameLabel
              t.MicrosoftClientIdLabel
              t.ClientSecretLabel
              t.MicrosoftTenantIdLabel ] do
            Assert.Matches("<label[^>]*>" + Regex.Escape label + "</label>", decoded)

        Assert.Contains(t.MicrosoftDisplayNameHint, decoded)
        Assert.Contains(t.MicrosoftClientIdHint, decoded)
        Assert.Contains(t.MicrosoftClientSecretHint, decoded)
        Assert.Contains(t.MicrosoftTenantIdHint, decoded)
        Assert.Equal("Microsoft", valueOf html "displayName")
        Assert.Contains("um-mono", controlOf html "clientId")
        Assert.Contains("um-mono", controlOf html "tenantId")
        Assert.DoesNotContain("placeholder", controlOf html "tenantId")
        Assert.Contains("type=\"password\"", controlOf html "clientSecret")

        // The four cards, in the contract's order, stacked, none chosen.
        Assert.Equal<string list>([ "consumers"; "tenant"; "organizations"; "common" ], radios html |> List.map radioValue)
        Assert.Empty(checkedRadios html)
        Assert.Contains("um-choice-group", html)
        Assert.Equal(4, Regex.Matches(html, "class=\"um-choice\"").Count)
        Assert.Contains(t.MicrosoftAudienceLabel, decoded)

        for title, desc in
            [ t.MicrosoftConsumersTitle, t.MicrosoftConsumersDesc
              t.MicrosoftTenantTitle, t.MicrosoftTenantDesc
              t.MicrosoftOrganizationsTitle, t.MicrosoftOrganizationsDesc
              t.MicrosoftCommonTitle, t.MicrosoftCommonDesc ] do
            Assert.Contains(title, decoded)
            Assert.Contains(desc, decoded)

        // The group is one column: no column count on the grid that holds the cards.
        let group = Regex.Match(html, "<fieldset class=\"um-choice-group\".*?</fieldset>", RegexOptions.Singleline).Value
        Assert.DoesNotContain("grid-cols", group)

        // The tenant field is after the group, always in the page.
        Assert.True(html.IndexOf "id=\"tenantId\"" > html.LastIndexOf "name=\"audience\"")

        // Nothing saved, nothing to switch off or delete.
        Assert.DoesNotContain("data-connector-actions", html)
    }

// ---------------------------------------------------------------------------
// Refusals
// ---------------------------------------------------------------------------

[<Fact>]
let ``a client ID that is not a GUID is refused with its message`` () =
    task {
        let! world = start ()
        use _ = world.App

        do!
            assertRefused world (fields |> with' [ "clientId", "not-a-guid" ]) [ "clientId", (texts ()).MicrosoftClientIdInvalid ]
    }

[<Fact>]
let ``no secret on a new connector is refused`` () =
    task {
        let! world = start ()
        use _ = world.App

        do!
            assertRefused world (fields |> with' [ "clientSecret", "" ]) [ "clientSecret", "Enter the client secret's Value." ]
    }

[<Fact>]
let ``no card chosen is refused and none is checked in the page drawn again`` () =
    task {
        let! world = start ()
        use _ = world.App

        do!
            assertRefused
                world
                (fields |> with' [ "audience", "" ])
                [ "audience", "Choose who may sign in. It must match the account types chosen when the app was registered." ]
    }

[<Fact>]
let ``an empty display name is refused with the other panes' message`` () =
    task {
        let! world = start ()
        use _ = world.App

        do!
            assertRefused
                world
                (fields |> with' [ "displayName", "" ])
                [ "displayName", (texts ()).Notice "connector-display-name-required" ]
    }

[<Fact>]
let ``one organization with no tenant ID is refused`` () =
    task {
        let! world = start ()
        use _ = world.App

        do!
            assertRefused
                world
                (fields |> with' [ "audience", "tenant" ])
                [ "tenantId", "Enter the Directory (tenant) ID of the organization." ]
    }

[<Fact>]
let ``one organization with a tenant ID that is not a GUID is refused`` () =
    task {
        let! world = start ()
        use _ = world.App

        do!
            assertRefused
                world
                (fields |> with' [ "audience", "tenant"; "tenantId", "contoso.onmicrosoft.com" ])
                [ "tenantId", "That is not a tenant ID. It is a GUID like 00000000-0000-0000-0000-000000000000." ]
    }

[<Fact>]
let ``one organization with the tenant of personal accounts is refused`` () =
    task {
        let! world = start ()
        use _ = world.App

        do!
            assertRefused
                world
                (fields |> with' [ "audience", "tenant"; "tenantId", FakeMicrosoft.PersonalTenant ])
                [ "tenantId", "That is the tenant of personal Microsoft accounts: choose Personal Microsoft accounts instead." ]
    }

[<Fact>]
let ``a tenant ID typed with another card chosen is not checked, kept and ignored`` () =
    task {
        let! world = startFor AnyOrganization
        use _ = world.App
        let! response = save world (fields |> with' [ "audience", "organizations"; "tenantId", "not a guid" ])
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("msg=connector-saved", location response)
        let! connector = stored world
        Assert.Equal(Some AnyOrganization, connector.Value.MicrosoftAudience)
    }

[<Fact>]
let ``a refused list of domains shows the existing message under its field`` () =
    task {
        let! world = start ()
        use _ = world.App
        let! response = save world (fields |> with' [ "allowedDomains", "not a domain!" ])
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode)
        let! html = body response
        Assert.True((errorOf html "allowedDomains").IsSome)
        let! rows = count world "SELECT count(*) FROM connectors WHERE slug = 'microsoft'"
        Assert.Equal("0", rows)
    }

[<Fact>]
let ``several fields refused at once each show their own message, and the typed values stay`` () =
    task {
        let! world = start ()
        use _ = world.App
        let t = texts ()

        do!
            assertRefused
                world
                [ "displayName", "My Microsoft"
                  "clientId", "nope"
                  "clientSecret", "a-secret-that-stays-out"
                  "audience", "tenant"
                  "tenantId", "also nope"
                  "shared", "true"
                  "allowedDomains", "" ]
                [ "clientId", t.MicrosoftClientIdInvalid
                  "tenantId", t.MicrosoftTenantIdInvalid ]

        // And with no card at all, no secret and no name.
        do!
            assertRefused
                world
                [ "displayName", ""
                  "clientId", "nope"
                  "clientSecret", ""
                  "audience", ""
                  "tenantId", "kept while no card is chosen"
                  "shared", "true"
                  "allowedDomains", "" ]
                [ "displayName", t.Notice "connector-display-name-required"
                  "clientId", t.MicrosoftClientIdInvalid
                  "clientSecret", t.MicrosoftSecretMissing
                  "audience", t.MicrosoftAudienceMissing ]
    }

// ---------------------------------------------------------------------------
// A save, for each audience
// ---------------------------------------------------------------------------

let private audiences =
    [ "consumers", PersonalAccounts, ""
      "tenant", OneOrganization tenantA, FakeMicrosoft.WorkTenantA.ToUpperInvariant()
      "organizations", AnyOrganization, ""
      "common", PersonalAndAnyOrganization, "" ]

[<Fact>]
let ``a save of each audience creates an enabled connector with that audience and the secret in the store`` () =
    task {
        for card, audience, tenantTyped in audiences do
            let! world = startFor audience
            use _ = world.App

            let! response = save world (fields |> with' [ "audience", card; "tenantId", tenantTyped ])
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
            Assert.Contains("msg=connector-saved", location response)
            Assert.DoesNotContain("unreachable", location response)
            Assert.DoesNotContain("wrong", location response)

            let! connector = stored world
            let connector = connector.Value
            Assert.Equal(ProviderKind.Microsoft, connector.Kind)
            Assert.True(connector.IsEnabled, card)
            Assert.True connector.IsShared
            Assert.Equal(FakeMicrosoft.ClientId, connector.ClientId)
            Assert.Equal(Some audience, connector.MicrosoftAudience)
            Assert.Equal(None, connector.IssuerUrl)

            let! secret = storedSecret world
            Assert.Equal(Some FakeMicrosoft.ClientSecret, secret)

            // The row keeps the audience in its stored spelling: the tenant's GUID in lower case.
            let! raw = count world "SELECT microsoft_audience FROM connectors WHERE slug = 'microsoft'"
            Assert.Equal(Ms.stored audience, raw)

            // The pane now shows the secret masked, the card chosen, and the tenant.
            let! html = world.Operator.GetHtml(location response)
            let t = texts ()
            Assert.Equal<string list>([ card ], checkedRadios html)
            Assert.Contains("data-connector-state=\"enabled\"", html)
            Assert.Matches("<label[^>]*>" + Regex.Escape t.MicrosoftReplaceSecretLabel + "</label>", decode html)
            Assert.DoesNotContain(FakeMicrosoft.ClientSecret, html)
            Assert.Equal((if card = "tenant" then FakeMicrosoft.WorkTenantA else ""), valueOf html "tenantId")

            let! created =
                scalar
                    world.App
                    "SELECT count(*) FROM security_events WHERE event_type = 'connector_created' AND connector_slug = 'microsoft'"
                    []

            Assert.Equal("1", created)

            // No part of the secret is in a security event.
            let! hits =
                scalar world.App "SELECT count(*) FROM security_events WHERE detail LIKE @p" [ "@p", $"%%{FakeMicrosoft.ClientSecret}%%" ]

            Assert.Equal("0", hits)
    }

// ---------------------------------------------------------------------------
// Replace and keep
// ---------------------------------------------------------------------------

[<Fact>]
let ``a saved connector saved with the secret blank keeps the secret and changes the other values`` () =
    task {
        let! world = start ()
        use _ = world.App
        let! _ = save world fields
        let! before = stored world

        let! response =
            save
                world
                (fields
                 |> with' [ "clientSecret", ""; "displayName", "Microsoft for the club"; "clientId", FakeMicrosoft.ReplacedClientId ])

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("msg=connector-saved", location response)

        let! connector = stored world
        Assert.Equal("Microsoft for the club", connector.Value.DisplayName)
        Assert.Equal(FakeMicrosoft.ReplacedClientId, connector.Value.ClientId)
        Assert.Equal(before.Value.ClientSecretRef, connector.Value.ClientSecretRef)
        Assert.True connector.Value.IsEnabled

        let! secret = storedSecret world
        Assert.Equal(Some FakeMicrosoft.ClientSecret, secret)

        let! updated = events world "connector_updated"
        Assert.Equal(1, updated)
    }

[<Fact>]
let ``a new secret replaces the stored one and leaves the connector enabled`` () =
    task {
        let! world = start ()
        use _ = world.App
        let! _ = save world fields

        let! response = save world (fields |> with' [ "clientSecret", "the-replacement-secret-1234" ])
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("msg=connector-saved", location response)

        let! connector = stored world
        Assert.True connector.Value.IsEnabled
        Assert.Equal("1234", connector.Value.ClientSecretHint)

        let! secret = storedSecret world
        Assert.Equal(Some "the-replacement-secret-1234", secret)

        let! html = world.Operator.GetHtml(location response)
        Assert.DoesNotContain("the-replacement-secret-1234", html)
    }

[<Fact>]
let ``changing the card on a saved connector changes the audience when Microsoft confirms the new choice`` () =
    task {
        // The fake serves "consumers"; the connector was saved for one organization.
        let! world = startFor PersonalAccounts
        use _ = world.App
        do! FakeMicrosoft.seedConnector world.App (OneOrganization tenantA)

        let! response = save world (fields |> with' [ "audience", "consumers"; "clientSecret", "" ])
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("msg=connector-saved", location response)
        let! after = stored world
        Assert.Equal(Some PersonalAccounts, after.Value.MicrosoftAudience)
        Assert.True after.Value.IsEnabled
    }

// ---------------------------------------------------------------------------
// Microsoft unreachable or not usable at save
// ---------------------------------------------------------------------------

[<Fact>]
let ``Microsoft unreachable at save stores the connector disabled, says why, and Enable works once it answers`` () =
    task {
        let! world = start ()
        use _ = world.App
        world.Fake.Stub.Mode <- Down

        let! response = save world fields
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("msg=connector-saved-microsoft-unreachable", location response)

        let! connector = stored world
        Assert.False connector.Value.IsEnabled
        // The secret is saved all the same: the operator does not paste it twice.
        let! secret = storedSecret world
        Assert.Equal(Some FakeMicrosoft.ClientSecret, secret)

        let! html = world.Operator.GetHtml(location response)
        let decoded = decode html
        Assert.Contains("data-notice=\"connector-saved-microsoft-unreachable\"", html)
        Assert.Contains("Saved, but not enabled: Umbral could not reach Microsoft (", decoded)
        Assert.Contains("Press Enable to try again.", decoded)
        Assert.Contains("data-connector-state=\"disabled\"", html)

        world.Fake.Stub.Mode <- Up
        let! enabled = world.Operator.PostForm(path, path + "/toggle", [])
        Assert.Contains("msg=connector-enabled", location enabled)
        let! after = stored world
        Assert.True after.Value.IsEnabled
    }

[<Fact>]
let ``a document for another audience than the one chosen is an unreachable Microsoft at save`` () =
    task {
        // The fake serves "consumers" only; the card asks for "common".
        let! world = startFor PersonalAccounts
        use _ = world.App

        let! response = save world fields
        Assert.Contains("msg=connector-saved-microsoft-unreachable", location response)
        let! connector = stored world
        Assert.False connector.Value.IsEnabled
    }

[<Fact>]
let ``Microsoft answering with another issuer at save stores the connector disabled with the wrong notice`` () =
    task {
        let! world = startFor (OneOrganization tenantA)
        use _ = world.App
        // A tenant document that names another tenant's issuer.
        world.Fake.Stub.Discovery["issuer"] <- node (Ms.issuerFor FakeMicrosoft.WorkTenantB)

        let posted = fields |> with' [ "audience", "tenant"; "tenantId", FakeMicrosoft.WorkTenantA ]
        let! response = save world posted
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("msg=connector-saved-microsoft-wrong", location response)

        let! connector = stored world
        Assert.False connector.Value.IsEnabled
        Assert.Equal(Some(OneOrganization tenantA), connector.Value.MicrosoftAudience)

        let! html = world.Operator.GetHtml(location response)
        let decoded = decode html
        Assert.Contains("data-notice=\"connector-saved-microsoft-wrong\"", html)
        Assert.Contains("Saved, but not enabled: Microsoft's answer was not usable (", decoded)
        Assert.Contains("Check the tenant ID, then press Enable.", decoded)

        // Enable asks again and says the same, in its own words.
        let! enable = world.Operator.PostForm(path, path + "/toggle", [])
        Assert.Contains("msg=connector-enable-microsoft-wrong", location enable)
        let! after = stored world
        Assert.False after.Value.IsEnabled
        let! page = world.Operator.GetHtml(location enable)
        Assert.Contains("Not enabled: Microsoft's answer was not usable (", decode page)
        Assert.Contains("Check the tenant ID, then press Enable again.", decode page)
    }

// ---------------------------------------------------------------------------
// Saving a saved connector when Microsoft cannot confirm it (spec FR-003,
// documentation review B1)
// ---------------------------------------------------------------------------

[<Fact>]
let ``an enabled connector saved with its choice unchanged while Microsoft is unreachable stays enabled and says it was not checked`` () =
    task {
        let! world = start ()
        use _ = world.App
        do! FakeMicrosoft.seedConnector world.App PersonalAndAnyOrganization
        world.Fake.Stub.Mode <- Down

        let! response = save world (fields |> with' [ "clientSecret", ""; "displayName", "Renamed" ])
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("msg=connector-saved-microsoft-unchecked", location response)

        let! connector = stored world
        Assert.Equal("Renamed", connector.Value.DisplayName)
        Assert.True connector.Value.IsEnabled

        let! html = world.Operator.GetHtml(location response)
        let decoded = decode html
        Assert.Contains("data-notice=\"connector-saved-microsoft-unchecked\"", html)
        Assert.Contains("Saved. Umbral could not check the connector with Microsoft (", decoded)
        Assert.Contains("so it stays enabled as it was.", decoded)
        // The notice that would be false for a connector that stays enabled.
        Assert.DoesNotContain("not enabled", decoded)
        Assert.Contains("data-connector-state=\"enabled\"", html)
    }

[<Fact>]
let ``an enabled connector saved with its choice unchanged while Microsoft's answer is wrong stays enabled and says it was not checked`` () =
    task {
        let! world = start ()
        use _ = world.App
        do! FakeMicrosoft.seedConnector world.App PersonalAndAnyOrganization
        world.Fake.Stub.Discovery["issuer"] <- node (Ms.issuerFor FakeMicrosoft.WorkTenantB)

        let! response = save world (fields |> with' [ "clientSecret", ""; "displayName", "Renamed" ])
        Assert.Contains("msg=connector-saved-microsoft-unchecked", location response)

        let! connector = stored world
        Assert.True connector.Value.IsEnabled
    }

[<Fact>]
let ``a disabled connector saved with its choice unchanged while Microsoft is unreachable stays disabled and says so`` () =
    task {
        let! world = start ()
        use _ = world.App
        do! FakeMicrosoft.seedConnector world.App PersonalAndAnyOrganization
        // Written straight to the store: the toggle refuses to disable the last enabled connector.
        do! ConnectorStore.setEnabled world.App.DbConfig world.App.Clock "microsoft" false
        world.Fake.Stub.Mode <- Down

        let! response = save world (fields |> with' [ "clientSecret", ""; "displayName", "Renamed" ])
        Assert.Contains("msg=connector-saved-microsoft-unreachable", location response)

        let! connector = stored world
        Assert.Equal("Renamed", connector.Value.DisplayName)
        Assert.False connector.Value.IsEnabled

        let! html = world.Operator.GetHtml(location response)
        Assert.Contains("Saved, but not enabled: Umbral could not reach Microsoft (", decode html)
    }

/// A saved connector (personal accounts) as the tests below leave it: what the
/// store holds of it, to compare after a refusal.
type private Snapshot =
    { Audience: MicrosoftAudience option
      DisplayName: string
      ClientId: string
      SecretRef: string
      Hint: string
      IsEnabled: bool
      Secret: string option }

let private snapshot (world: World) =
    task {
        let! connector = stored world
        let c = connector.Value
        let! secret = storedSecret world

        return
            { Audience = c.MicrosoftAudience
              DisplayName = c.DisplayName
              ClientId = c.ClientId
              SecretRef = c.ClientSecretRef
              Hint = c.ClientSecretHint
              IsEnabled = c.IsEnabled
              Secret = secret }
    }

let private newSecret = "the-replacement-secret-1234"

/// A refused change of choice on a saved connector: 422, the reason under
/// `field`, every typed value kept but the secret, and nothing changed or logged.
let private assertChoiceRefused
    (world: World)
    (posted: (string * string) list)
    (field: string)
    (assertMessage: string -> unit)
    =
    task {
        let! before = snapshot world
        let! updatedBefore = events world "connector_updated"

        let! response = save world posted
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode)
        let! html = body response

        match errorOf html field with
        | Some message -> assertMessage message
        | None -> failwith $"no message under {field}"

        // The other field of the pair has none.
        let other = if field = "audience" then "tenantId" else "audience"
        Assert.Equal(None, errorOf html other)

        // Typed values kept; the secret never drawn back.
        for name in [ "displayName"; "clientId"; "tenantId" ] do
            Assert.Equal(posted |> List.find (fun (n, _) -> n = name) |> snd, valueOf html name)

        let chosen = posted |> List.find (fun (n, _) -> n = "audience") |> snd
        Assert.Equal<string list>([ chosen ], checkedRadios html)
        Assert.DoesNotContain(newSecret, html)
        Assert.Matches("<input[^>]*id=\"clientSecret\"[^>]*value=\"\"", html)

        // Nothing written: the row, the secret and the log are as they were.
        let! after = snapshot world
        Assert.Equal(before, after)
        let! updatedAfter = events world "connector_updated"
        Assert.Equal(updatedBefore, updatedAfter)
    }

[<Fact>]
let ``a changed choice that Microsoft says names no tenant is refused with the wrong message under the tenant field, and nothing is saved`` () =
    task {
        let! world = startFor (OneOrganization tenantA)
        use _ = world.App
        do! FakeMicrosoft.seedConnector world.App PersonalAccounts
        world.Fake.AnswerInvalidTenant()

        let posted =
            fields
            |> with' [ "audience", "tenant"; "tenantId", FakeMicrosoft.WorkTenantA; "displayName", "Renamed"; "clientSecret", newSecret ]

        do!
            assertChoiceRefused world posted "tenantId" (fun message ->
                Assert.Equal(
                    "Microsoft did not confirm this choice: No Microsoft tenant has this ID. Nothing was saved: check the tenant ID, then try again.",
                    message
                )
                Assert.DoesNotContain("AADSTS", message))
    }

[<Fact>]
let ``a changed choice that Microsoft does not answer is refused with the did-not-answer message under the tenant field, and nothing is saved`` () =
    task {
        let! world = startFor (OneOrganization tenantA)
        use _ = world.App
        do! FakeMicrosoft.seedConnector world.App PersonalAccounts
        world.Fake.Stub.Mode <- Down

        let posted =
            fields
            |> with' [ "audience", "tenant"; "tenantId", FakeMicrosoft.WorkTenantA; "displayName", "Renamed"; "clientSecret", newSecret ]

        do!
            assertChoiceRefused world posted "tenantId" (fun message ->
                Assert.StartsWith("Microsoft did not answer (", message)
                Assert.EndsWith("). Nothing was saved: try again in a moment.", message))
    }

[<Fact>]
let ``a changed choice to any organization that Microsoft cannot confirm is refused under the choice, not under a tenant field`` () =
    task {
        // The fake serves "consumers" only; "organizations" finds Microsoft unreachable.
        let! world = startFor PersonalAccounts
        use _ = world.App
        do! FakeMicrosoft.seedConnector world.App PersonalAccounts

        let posted =
            fields |> with' [ "audience", "organizations"; "displayName", "Renamed"; "clientSecret", newSecret ]

        do!
            assertChoiceRefused world posted "audience" (fun message ->
                Assert.StartsWith("Microsoft did not answer (", message)
                Assert.EndsWith("). Nothing was saved: try again in a moment.", message))
    }

[<Fact>]
let ``a changed choice whose document names another issuer is refused with the wrong message under the choice`` () =
    task {
        let! world = startFor PersonalAndAnyOrganization
        use _ = world.App
        do! FakeMicrosoft.seedConnector world.App PersonalAccounts
        world.Fake.Stub.Discovery["issuer"] <- node (Ms.issuerFor FakeMicrosoft.WorkTenantB)

        let posted = fields |> with' [ "audience", "common"; "clientSecret", newSecret ]

        do!
            assertChoiceRefused world posted "audience" (fun message ->
                Assert.StartsWith("Microsoft did not confirm this choice: ", message)
                Assert.EndsWith(". Nothing was saved: check that the choice matches the app's registration, then try again.", message))
    }

[<Fact>]
let ``a refused edit of a saved connector leaves the audience, secret reference, hint and secret as they were`` () =
    task {
        let! world = startFor (OneOrganization tenantA)
        use _ = world.App
        let! _ = save world (fields |> with' [ "audience", "tenant"; "tenantId", FakeMicrosoft.WorkTenantA ])
        let! before = snapshot world
        Assert.False(String.IsNullOrEmpty before.Hint)

        // A tenant ID that is not a GUID, with a new secret typed into the same form.
        let! response =
            save
                world
                (fields
                 |> with' [ "audience", "tenant"; "tenantId", "not-a-tenant"; "displayName", "Renamed"; "clientSecret", newSecret ])

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode)
        let! html = body response
        Assert.Equal(Some((texts ()).MicrosoftTenantIdInvalid), errorOf html "tenantId")
        Assert.DoesNotContain(newSecret, html)
        Assert.Matches("<input[^>]*id=\"clientSecret\"[^>]*value=\"\"", html)

        let! after = snapshot world
        Assert.Equal(before, after)
        let! updated = events world "connector_updated"
        Assert.Equal(0, updated)
    }

// ---------------------------------------------------------------------------
// The security log records who may sign in (code review N1)
// ---------------------------------------------------------------------------

let private detailOf (world: World) (eventType: string) =
    scalar
        world.App
        $"SELECT coalesce(group_concat(detail, char(10)), '') FROM security_events WHERE event_type = '{eventType}'"
        []

[<Fact>]
let ``creating the connector records the audience in its stored form`` () =
    task {
        for card, audience, tenantTyped in audiences do
            let! world = startFor audience
            use _ = world.App

            let! _ = save world (fields |> with' [ "audience", card; "tenantId", tenantTyped ])

            let! detail = detailOf world "connector_created"
            Assert.Contains($"audience={Ms.stored audience}", detail)
            // No secret, no tenant typed in another spelling.
            Assert.DoesNotContain(FakeMicrosoft.ClientSecret, detail)
    }

[<Fact>]
let ``a connector created disabled records the audience too`` () =
    task {
        let! world = start ()
        use _ = world.App
        world.Fake.Stub.Mode <- Down

        let! _ = save world fields

        let! detail = detailOf world "connector_created"
        Assert.Contains("audience=common;saved_disabled: ", detail)
    }

[<Fact>]
let ``changing the audience records the new one when it is saved`` () =
    task {
        let! world = startFor PersonalAndAnyOrganization
        use _ = world.App
        do! FakeMicrosoft.seedConnector world.App PersonalAccounts

        let! _ = save world (fields |> with' [ "audience", "common"; "clientSecret", "" ])

        let! detail = detailOf world "connector_updated"
        Assert.Equal("audience=common", detail)
    }

[<Fact>]
let ``saving with the audience unchanged records no audience`` () =
    task {
        let! world = startFor PersonalAndAnyOrganization
        use _ = world.App
        do! FakeMicrosoft.seedConnector world.App PersonalAndAnyOrganization

        let! _ = save world (fields |> with' [ "clientSecret", ""; "displayName", "Renamed" ])
        let! detail = detailOf world "connector_updated"
        Assert.DoesNotContain("audience", detail)

        // A new secret with the audience unchanged names the store, and still no audience.
        let! _ = save world (fields |> with' [ "clientSecret", newSecret ])
        let! detail = detailOf world "connector_updated"
        Assert.DoesNotContain("audience", detail)
        Assert.Contains("store=", detail)
    }

[<Fact>]
let ``changing the audience together with the secret records both`` () =
    task {
        let! world = startFor PersonalAndAnyOrganization
        use _ = world.App
        do! FakeMicrosoft.seedConnector world.App PersonalAccounts

        let! _ = save world (fields |> with' [ "audience", "common"; "clientSecret", newSecret ])

        let! detail = detailOf world "connector_updated"
        Assert.Matches("^store=[^;]+;audience=common$", detail)
    }

[<Fact>]
let ``a refused change of audience logs nothing`` () =
    task {
        let! world = startFor PersonalAccounts
        use _ = world.App
        do! FakeMicrosoft.seedConnector world.App PersonalAccounts

        // "organizations" is not served by this fake: refused, nothing saved.
        let! response = save world (fields |> with' [ "audience", "organizations"; "clientSecret", "" ])
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode)

        let! updated = events world "connector_updated"
        Assert.Equal(0, updated)
        let! created = events world "connector_created"
        Assert.Equal(0, created)
    }

// ---------------------------------------------------------------------------
// The Set-up rule line leads with the choice (contract, design review nit 2)
// ---------------------------------------------------------------------------

/// The marker and the text of the pane's Set-up rule line.
let private ruleLine (html: string) =
    let m = Regex.Match(html, "<p[^>]*data-rule=\"([^\"]*)\"[^>]*>(.*?)</p>", RegexOptions.Singleline)
    Assert.True(m.Success, "no rule line")
    m.Groups[1].Value, (decode (Regex.Replace(m.Groups[2].Value, "<[^>]+>", ""))).Trim()

[<Theory>]
[<InlineData("consumers", "personal Microsoft accounts")>]
[<InlineData("tenant", "work or school accounts from one organization")>]
[<InlineData("organizations", "any work or school account")>]
[<InlineData("common", "personal and any work or school account")>]
let ``a saved pane with no domain rule says who can sign in by choice, with any email domain`` (card: string, phrase: string) =
    task {
        let audience = audiences |> List.find (fun (c, _, _) -> c = card) |> fun (_, a, _) -> a
        let! world = startFor audience
        use _ = world.App
        do! FakeMicrosoft.seedConnector world.App audience

        let! html = world.Operator.GetHtml path
        let marker, text = ruleLine html
        Assert.Equal("anyone", marker)
        Assert.Equal($"Who can sign in: {phrase}, with any email domain.", text)
    }

[<Fact>]
let ``a saved pane with a connector list says the choice and the list, as this connector's`` () =
    task {
        let! world = startFor PersonalAndAnyOrganization
        use _ = world.App
        do! FakeMicrosoft.seedConnector world.App PersonalAndAnyOrganization
        let! saved = save world (fields |> with' [ "clientSecret", ""; "allowedDomains", "contoso.com" ])
        Assert.Contains("msg=connector-saved", location saved)

        let! html = world.Operator.GetHtml path
        let marker, text = ruleLine html
        Assert.Equal("connector", marker)
        Assert.Equal(
            "Who can sign in: personal and any work or school account, with a verified email at contoso.com (this connector's list)",
            text
        )
    }

[<Fact>]
let ``a saved pane with only an instance list says the choice and the list, as the instance's`` () =
    task {
        let! world = startFor (OneOrganization tenantA)
        use _ = world.App
        do! FakeMicrosoft.seedConnector world.App (OneOrganization tenantA)
        do! EmailDomainRules.setInstanceList world.App.DbConfig world.App.Clock [ "example.org" ]

        let! html = world.Operator.GetHtml path
        let marker, text = ruleLine html
        Assert.Equal("instance", marker)
        Assert.Equal(
            "Who can sign in: work or school accounts from one organization, with a verified email at example.org (the instance list)",
            text
        )
    }

[<Fact>]
let ``a Microsoft pane not set up keeps the existing line, and another kind's pane is unchanged`` () =
    task {
        let! world = start ()
        use _ = world.App

        let! html = world.Operator.GetHtml path
        let marker, text = ruleLine html
        Assert.Equal("anyone", marker)
        Assert.Equal("Who can sign in: anyone with an account", text)

        // Another kind, set up or not, never takes the choice into its line.
        let! github = world.Operator.GetHtml "/admin/connectors/github"
        let marker, text = ruleLine github
        Assert.Equal("anyone", marker)
        Assert.Equal("Who can sign in: anyone with an account", text)

        do! EmailDomainRules.setInstanceList world.App.DbConfig world.App.Clock [ "example.org" ]
        let! github = world.Operator.GetHtml "/admin/connectors/github"
        let marker, text = ruleLine github
        Assert.Equal("instance", marker)
        Assert.Equal("Who can sign in: example.org (the instance list)", text)
    }

// ---------------------------------------------------------------------------
// Enable and Disable
// ---------------------------------------------------------------------------

[<Fact>]
let ``Enable with Microsoft unreachable stays disabled and says so, and Disable then asks Microsoft nothing`` () =
    task {
        let! world = start ()
        use _ = world.App
        do! FakeMicrosoft.seedConnector world.App PersonalAndAnyOrganization
        do! ConnectorStore.setEnabled world.App.DbConfig world.App.Clock "microsoft" false
        world.Fake.Stub.Mode <- Down

        let! response = world.Operator.PostForm("/admin/connectors", path + "/toggle", [])
        Assert.Contains("msg=connector-enable-microsoft-unreachable", location response)

        let! connector = stored world
        Assert.False connector.Value.IsEnabled

        let! html = world.Operator.GetHtml(location response)
        let decoded = decode html
        Assert.Contains("data-notice=\"connector-enable-microsoft-unreachable\"", html)
        Assert.Contains("Not enabled: Umbral could not reach Microsoft (", decoded)
        Assert.Contains("Try again in a moment.", decoded)

        // Enabled again by hand, Microsoft still down: switching off works and asks nothing.
        do! ConnectorStore.setEnabled world.App.DbConfig world.App.Clock "microsoft" true
        let requests = world.Fake.Stub.Requests.Length

        let! off = world.Operator.PostForm("/admin/connectors", path + "/toggle?confirm=yes", [])
        Assert.Contains("msg=connector-disabled", location off)
        let! after = stored world
        Assert.False after.Value.IsEnabled
        Assert.Equal(requests, world.Fake.Stub.Requests.Length)
    }

[<Fact>]
let ``Enable with Microsoft reachable enables`` () =
    task {
        let! world = start ()
        use _ = world.App
        do! FakeMicrosoft.seedConnector world.App PersonalAndAnyOrganization
        do! ConnectorStore.setEnabled world.App.DbConfig world.App.Clock "microsoft" false

        let! response = world.Operator.PostForm("/admin/connectors", path + "/toggle", [])
        Assert.Contains("msg=connector-enabled", location response)
        let! connector = stored world
        Assert.True connector.Value.IsEnabled
    }

[<Fact>]
let ``Enable for a row with no readable audience stays disabled with the wrong notice`` () =
    task {
        let! world = start ()
        use _ = world.App
        do! FakeMicrosoft.seedConnector world.App PersonalAndAnyOrganization
        do! ConnectorStore.setEnabled world.App.DbConfig world.App.Clock "microsoft" false
        let! _ = count world "UPDATE connectors SET microsoft_audience = 'nonsense' WHERE slug = 'microsoft'"

        let! response = world.Operator.PostForm("/admin/connectors", path + "/toggle", [])
        Assert.Contains("msg=connector-enable-microsoft-wrong", location response)
        let! connector = stored world
        Assert.False connector.Value.IsEnabled
        Assert.Equal(0, world.Fake.Stub.Requests.Length)
    }

// ---------------------------------------------------------------------------
// Unsharing, and the antiforgery token
// ---------------------------------------------------------------------------

[<Fact>]
let ``a post without an antiforgery token changes nothing`` () =
    task {
        let! world = start ()
        use _ = world.App
        let! response = world.Operator.PostWithoutToken(path, fields)
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        let! rows = count world "SELECT count(*) FROM connectors WHERE slug = 'microsoft'"
        Assert.Equal("0", rows)
    }

[<Fact>]
let ``a connector named microsoft of another kind is not edited by this form`` () =
    task {
        let! world = start ()
        use _ = world.App

        do!
            ConnectorStore.save
                world.App.DbConfig
                world.App.Clock
                { Slug = "microsoft"
                  Kind = ProviderKind.Fake
                  DisplayName = "Mine"
                  ClientId = "x"
                  ClientSecretRef = ""
                  ClientSecretHint = ""
                  IssuerUrl = None
                  AllowedEmailDomains = []
                  PasskeyEnrolment = InvitationsOnly
                  FediverseAccess = FediverseAccess.Any
                  AppleKey = None
                  MicrosoftAudience = None
                  EmailTrust = None
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = world.App.Clock.GetUtcNow()
                  UpdatedUtc = world.App.Clock.GetUtcNow() }
                None

        let! response = save world fields
        Assert.Contains("msg=connector-unknown", location response)
        let! kind = count world "SELECT kind FROM connectors WHERE slug = 'microsoft'"
        Assert.Equal("fake", kind)

        // And the list does not offer a second, empty Microsoft entry.
        let! list = world.Operator.GetHtml "/admin/connectors"
        Assert.Equal(1, Regex.Matches(list, "data-connector-entry=\"microsoft\"").Count)
    }

[<Fact>]
let ``unsharing the only shared connector warns, and the warning's own button saves it unshared with everything else kept`` () =
    task {
        let! world = start ()
        use _ = world.App
        let! saved = save world (fields |> with' [ "allowedDomains", "example.com" ])
        Assert.Contains("msg=connector-saved", location saved)

        // An app that offers every shared connector depends on this one.
        let operator = Journeys.Browser world.App.Client

        for cookie in world.Operator.Cookies do
            operator.Cookies[cookie.Key] <- cookie.Value

        let! clientId, _ = createClientApp operator

        for cookie in operator.Cookies do
            world.Operator.Cookies[cookie.Key] <- cookie.Value

        let unticked = fields |> List.filter (fun (name, _) -> name <> "shared" && name <> "clientSecret")
        let! warned = save world unticked
        Assert.Contains("connector-strands-apps", location warned)
        let! unchanged = stored world
        Assert.True unchanged.Value.IsShared

        let! screen = world.Operator.GetHtml(location warned)
        Assert.Contains($"data-stranded-app=\"{clientId}\"", screen)

        // Press the warning's button: post exactly the hidden fields its form holds.
        let formMatch =
            Regex.Match(screen, "<form[^>]*action=\"([^\"]*\\?confirm=yes)\"[^>]*>(.*?)</form>", RegexOptions.Singleline)

        Assert.True(formMatch.Success, "no confirm form on the page")
        let action = decode formMatch.Groups[1].Value

        let hidden =
            [ for tag in Regex.Matches(formMatch.Groups[2].Value, "<input\\b[^>]*type=\"hidden\"[^>]*>") do
                  let attribute name =
                      let m = Regex.Match(tag.Value, "\\b" + name + "=\"([^\"]*)\"")
                      if m.Success then decode m.Groups[1].Value else ""

                  yield attribute "name", attribute "value" ]

        let names = hidden |> List.map fst |> List.sort

        Assert.Equal<string list>(
            List.sort [ "__RequestVerificationToken"; "displayName"; "clientId"; "audience"; "tenantId"; "allowedDomains" ],
            names
        )

        let! confirmed = world.Operator.PostWithoutToken(action, hidden)
        Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode)
        Assert.Contains("msg=connector-saved", location confirmed)

        // Unshared, and nothing else moved, the secret included.
        let! after = stored world
        let after = after.Value
        Assert.False after.IsShared
        Assert.True after.IsEnabled
        Assert.Equal(FakeMicrosoft.ClientId, after.ClientId)
        Assert.Equal(Some PersonalAndAnyOrganization, after.MicrosoftAudience)
        Assert.Equal<string list>([ "example.com" ], after.AllowedEmailDomains)
        let! secret = storedSecret world
        Assert.Equal(Some FakeMicrosoft.ClientSecret, secret)
    }
