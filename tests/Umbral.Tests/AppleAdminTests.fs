/// The Apple connector's pane, its Save and its Enable check on the Connectors
/// screen (specs/013 User Story 1).
///
/// Everything goes through the real screen and the real secret store, with the
/// fake Apple behind the provider calls. A refusal must save nothing, mark the
/// field it is about, keep every typed value but the key, and never put what was
/// pasted back in the page; a saved key must be in the store and nowhere else.
module Umbral.Tests.AppleAdminTests

open System
open System.Net
open System.Security.Cryptography
open System.Text.RegularExpressions
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.Flows
open Umbral.Tests.Infrastructure.StubOidcProvider

let private texts () = Umbral.Views.Texts.current ()

/// `HtmlDecode`, which the nullness checker sees as possibly null.
let private decode (text: string) =
    WebUtility.HtmlDecode text |> Option.ofObj |> Option.defaultValue ""

let private path = "/admin/connectors/apple"

type private World =
    { App: Fixtures.TestApp
      Operator: Browser
      Fake: FakeApple.FakeApple }

let private start () =
    task {
        let fake = FakeApple.create ()
        let! app = Fixtures.createAppWithProviderHandler fake.Handler fakeSettings ignore
        FakeApple.attach app fake
        let operator = Browser app.Client
        do! claimOperator app operator

        return
            { App = app
              Operator = operator
              Fake = fake }
    }

/// What the pane posts, with the usual four values and the key Apple knows.
let private fields (world: World) =
    [ "displayName", "Apple"
      "servicesId", FakeApple.ServicesId
      "teamId", FakeApple.TeamId
      "keyId", FakeApple.KeyId
      "privateKey", world.Fake.PrivateKeyPem
      "shared", "true"
      "allowedDomains", "" ]

/// The fields with some of them replaced.
let private with' (changes: (string * string) list) (original: (string * string) list) =
    original |> List.map (fun (name, value) -> name, (changes |> List.tryFind (fun (n, _) -> n = name) |> Option.map snd |> Option.defaultValue value))

let private save (world: World) (posted: (string * string) list) =
    world.Operator.PostForm(path, path, posted)

let private body (response: System.Net.Http.HttpResponseMessage) =
    task {
        let! text = response.Content.ReadAsStringAsync()
        return decode text
    }

let private newPem () =
    ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem()

/// The base64 lines of a PEM: what must never appear on a page or in a log.
let private bodyLines (pem: string) =
    pem.Split('\n') |> Array.filter (fun line -> line.Length > 0 && not (line.StartsWith "-----")) |> Array.toList

let private stored (world: World) = ConnectorStore.findBySlug world.App.DbConfig "apple"

let private storedKey (world: World) =
    task {
        let! connector = stored world
        let connector = connector.Value

        match Secrets.SecretRef.Decode connector.ClientSecretRef with
        | Ok reference -> return! Fixtures.readSecret world.App reference
        | Error _ -> return None
    }

let private count (world: World) (statement: string) = scalar world.App statement []

/// The opening tag of an input or textarea by its id.
let private controlOf (html: string) (name: string) =
    let m = Regex.Match(html, "<(?:input|textarea)\\b[^>]*\\bid=\"" + name + "\"[^>]*>")
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
    let tag = controlOf html name
    let m = Regex.Match(tag, "\\bvalue=\"([^\"]*)\"")
    if m.Success then decode m.Groups[1].Value else ""

let private events (world: World) (eventType: string) =
    task {
        let! n = count world $"SELECT count(*) FROM security_events WHERE event_type = '{eventType}'"
        return int n
    }

/// A refused save: 422, nothing stored, the message under the field and the
/// field marked, the other typed values drawn again, and none of what was
/// pasted in the page.
let private assertRefused (world: World) (posted: (string * string) list) (field: string) (message: string) =
    task {
        let! response = save world posted
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode)
        let! html = body response

        let! rows = count world "SELECT count(*) FROM connectors WHERE slug = 'apple'"
        Assert.Equal("0", rows)
        let! created = events world "connector_created"
        Assert.Equal(0, created)

        Assert.Equal(Some message, errorOf html field)
        Assert.Contains("aria-invalid=\"true\"", controlOf html field)

        for name, value in posted do
            if List.contains name [ "displayName"; "servicesId"; "teamId"; "keyId" ] then
                Assert.Equal(value, valueOf html name)

        // The key is never drawn back: not as text of the box, not in an attribute.
        let pasted = posted |> List.find (fun (n, _) -> n = "privateKey") |> snd

        for line in bodyLines pasted do
            Assert.DoesNotContain(line, html)

        if pasted.Length > 0 then
            Assert.DoesNotContain(pasted.Trim(), html)

        // The textarea is empty.
        Assert.Matches("<textarea[^>]*id=\"privateKey\"[^>]*></textarea>", html)
    }

// ---------------------------------------------------------------------------
// The pane and the list
// ---------------------------------------------------------------------------

[<Fact>]
let ``not set up the list has an Apple entry and the pane shows the return URL, the caveat and five fields`` () =
    task {
        let! world = start ()
        use _ = world.App
        let! list = world.Operator.GetHtml "/admin/connectors"

        let entries = [ for m in Regex.Matches(list, "data-connector-entry=\"([^\"]*)\"") -> m.Groups[1].Value ]
        Assert.Contains("apple", entries)
        Assert.True(List.findIndex ((=) "apple") entries > List.findIndex ((=) "google") entries)

        let! html = world.Operator.GetHtml path
        let decoded = decode html
        Assert.Contains("data-connector-kind=\"apple\"", html)
        Assert.Contains("https://umbral.test/connect/apple/callback", html)
        Assert.Contains("data-setup-caveat=\"apple\"", html)
        Assert.Contains("Apple accepts only an https address on a real domain name", decoded)

        // The set-up note is Apple's own and says nothing of what Apple's screen
        // shows (design review): the generic advice names the provider's consent
        // screen, which nobody has confirmed for a Services ID.
        Assert.Contains("data-naming-advice", html)
        Assert.Contains("Give the Services ID a description that names this Umbral instance", decoded)
        Assert.DoesNotContain("consent screen", decoded)
        Assert.Contains("a paid Apple Developer Program membership", decoded)

        let t = texts ()

        for label in [ t.AppleDisplayNameLabel; t.AppleServicesIdLabel; t.AppleTeamIdLabel; t.AppleKeyIdLabel; t.ApplePrivateKeyLabel ] do
            Assert.Matches("<label[^>]*>" + Regex.Escape label + "</label>", decoded)

        for name in [ "displayName"; "servicesId"; "teamId"; "keyId"; "privateKey" ] do
            controlOf html name |> ignore

        Assert.Contains("<textarea", html)
        Assert.Contains("rows=\"6\"", controlOf html "privateKey")
        Assert.Contains("um-textarea", controlOf html "privateKey")
        Assert.Contains("spellcheck=\"false\"", controlOf html "privateKey")
        Assert.Contains("autocomplete=\"off\"", controlOf html "privateKey")
        Assert.Contains("autocapitalize=\"off\"", controlOf html "privateKey")
        Assert.Contains(t.AppleDisplayNameHint, decoded)

        // Nothing saved, nothing to switch off or delete.
        Assert.DoesNotContain("data-connector-actions", html)

        // The add form does not offer the kind.
        let! add = world.Operator.GetHtml "/admin/connectors/new"
        Assert.Contains("value=\"google\"", add)
        Assert.DoesNotContain("value=\"apple\"", add)
    }

// ---------------------------------------------------------------------------
// A save
// ---------------------------------------------------------------------------

[<Fact>]
let ``a save with a usable key and Apple reachable stores the row enabled and the key in the store`` () =
    task {
        let! world = start ()
        use _ = world.App
        let posted = fields world
        let! response = save world posted
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("msg=connector-saved", location response)
        Assert.DoesNotContain("unreachable", location response)

        let! connector = stored world
        let connector = connector.Value
        Assert.Equal(ProviderKind.Apple, connector.Kind)
        Assert.True connector.IsEnabled
        Assert.True connector.IsShared
        Assert.Equal(FakeApple.ServicesId, connector.ClientId)
        Assert.Equal(Some { TeamId = FakeApple.TeamId; KeyId = FakeApple.KeyId }, connector.AppleKey)
        Assert.Equal("", connector.ClientSecretHint)
        Assert.Equal(None, connector.IssuerUrl)

        // The store holds the normalised PEM: the one Apple's key exports.
        let! key = storedKey world
        Assert.Equal(Some(world.Fake.PrivateKeyPem.Trim().Replace("\r\n", "\n")), key)

        // The pane now shows the key as dots, and a box to replace it.
        let! html = world.Operator.GetHtml(location response)
        let decoded = decode html
        let t = texts ()
        Assert.Contains(Secrets.masked "", decoded)
        Assert.Contains(t.ApplePrivateKeySavedLabel, decoded)
        Assert.Matches("<label[^>]*>" + Regex.Escape t.AppleReplaceKeyLabel + "</label>", decoded)
        Assert.Contains(t.AppleReplaceKeyHint, decoded)
        Assert.Contains("data-connector-state=\"enabled\"", html)
        Assert.Equal(FakeApple.TeamId, valueOf html "teamId")
        Assert.Equal(FakeApple.KeyId, valueOf html "keyId")
        Assert.Matches("<textarea[^>]*id=\"privateKey\"[^>]*></textarea>", html)

        // No line of the key is in the page, and none is in a security event.
        for line in bodyLines world.Fake.PrivateKeyPem do
            Assert.DoesNotContain(line, html)
            let! hits = scalar world.App "SELECT count(*) FROM security_events WHERE detail LIKE @p" [ "@p", $"%%{line}%%" ]
            Assert.Equal("0", hits)

        let! created = scalar world.App "SELECT count(*) FROM security_events WHERE event_type = 'connector_created' AND connector_slug = 'apple'" []
        Assert.Equal("1", created)
    }

// ---------------------------------------------------------------------------
// Each refusal
// ---------------------------------------------------------------------------

[<Fact>]
let ``a Team ID of nine characters is refused under its field`` () =
    task {
        let! world = start ()
        use _ = world.App
        do! assertRefused world (fields world |> with' [ "teamId", "TEAM12345" ]) "teamId" (texts ()).AppleTeamIdInvalid
    }

[<Fact>]
let ``a Key ID with a dash is refused under its field`` () =
    task {
        let! world = start ()
        use _ = world.App
        do! assertRefused world (fields world |> with' [ "keyId", "KEY-234567" ]) "keyId" (texts ()).AppleKeyIdInvalid
    }

[<Fact>]
let ``an empty Services ID is refused under its field`` () =
    task {
        let! world = start ()
        use _ = world.App
        do! assertRefused world (fields world |> with' [ "servicesId", "" ]) "servicesId" (texts ()).AppleServicesIdInvalid
    }

[<Fact>]
let ``a Services ID with a space is refused under its field`` () =
    task {
        let! world = start ()
        use _ = world.App
        do! assertRefused world (fields world |> with' [ "servicesId", "com.example sign in" ]) "servicesId" (texts ()).AppleServicesIdInvalid
    }

[<Fact>]
let ``no key on a new connector is refused under the key box`` () =
    task {
        let! world = start ()
        use _ = world.App
        do! assertRefused world (fields world |> with' [ "privateKey", "" ]) "privateKey" (texts ()).ApplePrivateKeyMissing
    }

[<Fact>]
let ``a key that is an RSA key is refused and not drawn back`` () =
    task {
        let! world = start ()
        use _ = world.App
        let rsa = RSA.Create(2048).ExportPkcs8PrivateKeyPem()
        do! assertRefused world (fields world |> with' [ "privateKey", rsa ]) "privateKey" (texts ()).ApplePrivateKeyUnusable
    }

[<Fact>]
let ``a key that is text is refused and not drawn back`` () =
    task {
        let! world = start ()
        use _ = world.App
        do! assertRefused world (fields world |> with' [ "privateKey", "this is not a key, it is a sentence" ]) "privateKey" (texts ()).ApplePrivateKeyUnusable
    }

[<Fact>]
let ``two fields wrong at once show both messages`` () =
    task {
        let! world = start ()
        use _ = world.App
        let! response = save world (fields world |> with' [ "teamId", "short"; "keyId", "KEY-234567"; "domains", "" ])
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode)
        let! html = body response
        let t = texts ()
        Assert.Equal(Some t.AppleTeamIdInvalid, errorOf html "teamId")
        Assert.Equal(Some t.AppleKeyIdInvalid, errorOf html "keyId")
        Assert.Contains("aria-invalid=\"true\"", controlOf html "teamId")
        Assert.Contains("aria-invalid=\"true\"", controlOf html "keyId")
        // The fields that were fine carry no error.
        Assert.Equal(None, errorOf html "servicesId")
        Assert.Equal(None, errorOf html "privateKey")
        Assert.Equal("short", valueOf html "teamId")
    }

[<Fact>]
let ``a refused allowed domain is refused under its own field with the rest kept`` () =
    task {
        let! world = start ()
        use _ = world.App
        let! response = save world (fields world |> with' [ "allowedDomains", "*.example.org"; "displayName", "My Apple" ])
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode)
        let! html = body response
        Assert.Equal(Some((texts ()).AllowedDomainsWildcard "*.example.org"), errorOf html "allowedDomains")
        Assert.Equal("*.example.org", valueOf html "allowedDomains")
        Assert.Equal("My Apple", valueOf html "displayName")
        let! rows = count world "SELECT count(*) FROM connectors WHERE slug = 'apple'"
        Assert.Equal("0", rows)
    }

// ---------------------------------------------------------------------------
// Apple unreachable
// ---------------------------------------------------------------------------

[<Fact>]
let ``Apple unreachable at save stores the connector disabled, says why, and Enable works once Apple answers`` () =
    task {
        let! world = start ()
        use _ = world.App
        world.Fake.Stub.Mode <- Down

        let! response = save world (fields world)
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("msg=connector-saved-apple-unreachable", location response)

        let! connector = stored world
        Assert.False connector.Value.IsEnabled
        // The key is saved all the same: the operator does not paste it twice.
        let! key = storedKey world
        Assert.True key.IsSome

        let! html = world.Operator.GetHtml(location response)
        let decoded = decode html
        Assert.Contains("data-notice=\"connector-saved-apple-unreachable\"", html)
        Assert.Contains("Saved, but not enabled: Umbral could not reach Apple (", decoded)
        Assert.Contains("Press Enable to try again.", decoded)
        Assert.Contains("data-connector-state=\"disabled\"", html)

        world.Fake.Stub.Mode <- Up
        let! enabled = world.Operator.PostForm(path, path + "/toggle", [])
        Assert.Contains("msg=connector-enabled", location enabled)
        let! after = stored world
        Assert.True after.Value.IsEnabled
    }

// ---------------------------------------------------------------------------
// Replace and keep
// ---------------------------------------------------------------------------

[<Fact>]
let ``a new key replaces the stored one, keeps no hint and leaves the connector enabled`` () =
    task {
        let! world = start ()
        use _ = world.App
        let! _ = save world (fields world)

        let replacement = newPem ()

        let! response = save world (fields world |> with' [ "privateKey", replacement; "keyId", "NEWKEY1234" ])
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("msg=connector-saved", location response)

        let! connector = stored world
        Assert.Equal("", connector.Value.ClientSecretHint)
        Assert.True connector.Value.IsEnabled
        Assert.Equal(Some "NEWKEY1234", connector.Value.AppleKey |> Option.map (fun k -> k.KeyId))

        let! key = storedKey world
        Assert.Equal(Some(replacement.Trim()), key)

        let! updated = events world "connector_updated"
        Assert.Equal(1, updated)

        let! html = world.Operator.GetHtml(location response)

        for line in bodyLines replacement do
            Assert.DoesNotContain(line, html)
    }

[<Fact>]
let ``an empty key box keeps the stored key and stores the other new values`` () =
    task {
        let! world = start ()
        use _ = world.App
        let! _ = save world (fields world)
        let! before = storedKey world

        let! response = save world (fields world |> with' [ "privateKey", ""; "displayName", "Apple for the club"; "keyId", "OTHERKEY12" ])
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("msg=connector-saved", location response)

        let! connector = stored world
        Assert.Equal("Apple for the club", connector.Value.DisplayName)
        Assert.Equal(Some "OTHERKEY12", connector.Value.AppleKey |> Option.map (fun k -> k.KeyId))
        Assert.True connector.Value.IsEnabled

        let! after = storedKey world
        Assert.Equal(before, after)
        Assert.True before.IsSome
    }

[<Fact>]
let ``replacing the key of a disabled connector leaves it disabled`` () =
    task {
        let! world = start ()
        use _ = world.App
        let! _ = save world (fields world)
        do! ConnectorStore.setEnabled world.App.DbConfig world.App.Clock "apple" false

        let! response = save world (fields world |> with' [ "privateKey", newPem () ])
        Assert.Contains("msg=connector-saved", location response)

        let! connector = stored world
        Assert.False connector.Value.IsEnabled
    }

[<Fact>]
let ``replacing the key while Apple is unreachable leaves an enabled connector enabled and asks Apple nothing`` () =
    task {
        let! world = start ()
        use _ = world.App
        let! _ = save world (fields world)
        world.Fake.Stub.Mode <- Down
        let before = world.Fake.Stub.Requests.Length

        let replacement = newPem ()
        let! response = save world (fields world |> with' [ "privateKey", replacement ])
        Assert.Contains("msg=connector-saved", location response)

        let! connector = stored world
        Assert.True connector.Value.IsEnabled
        let! key = storedKey world
        Assert.Equal(Some(replacement.Trim()), key)
        Assert.Equal(before, world.Fake.Stub.Requests.Length)
    }

[<Fact>]
let ``a refused replacement changes nothing`` () =
    task {
        let! world = start ()
        use _ = world.App
        let! _ = save world (fields world)
        let! before = storedKey world

        let! response = save world (fields world |> with' [ "privateKey", "not a key"; "displayName", "Changed" ])
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode)
        let! html = body response
        Assert.Equal(Some((texts ()).ApplePrivateKeyUnusable), errorOf html "privateKey")
        // The typed name is kept on the page, the stored one is untouched.
        Assert.Equal("Changed", valueOf html "displayName")
        let! connector = stored world
        Assert.Equal("Apple", connector.Value.DisplayName)
        let! after = storedKey world
        Assert.Equal(before, after)
    }

// ---------------------------------------------------------------------------
// Enable and Disable
// ---------------------------------------------------------------------------

[<Fact>]
let ``Enable with a stored secret that is not a key stays disabled and says the key cannot sign`` () =
    task {
        let! world = start ()
        use _ = world.App
        do! FakeApple.seedConnectorWith world.App "this is not a key"
        do! ConnectorStore.setEnabled world.App.DbConfig world.App.Clock "apple" false

        let! response = world.Operator.PostForm("/admin/connectors", path + "/toggle", [])
        Assert.Contains("msg=connector-enable-apple-key", location response)

        let! connector = stored world
        Assert.False connector.Value.IsEnabled

        let! html = world.Operator.GetHtml(location response)
        Assert.Contains("data-notice=\"connector-enable-apple-key\"", html)
        Assert.Contains((texts ()).Notice "connector-enable-apple-key", decode html)
        Assert.Equal(0, world.Fake.Stub.Requests.Length)
    }

[<Fact>]
let ``Enable with Apple unreachable stays disabled and says so, and Disable then asks Apple nothing`` () =
    task {
        let! world = start ()
        use _ = world.App
        do! FakeApple.seedConnector world.App world.Fake
        do! ConnectorStore.setEnabled world.App.DbConfig world.App.Clock "apple" false
        world.Fake.Stub.Mode <- Down

        let! response = world.Operator.PostForm("/admin/connectors", path + "/toggle", [])
        Assert.Contains("msg=connector-enable-apple-unreachable", location response)

        let! connector = stored world
        Assert.False connector.Value.IsEnabled

        let! html = world.Operator.GetHtml(location response)
        let decoded = decode html
        Assert.Contains("data-notice=\"connector-enable-apple-unreachable\"", html)
        Assert.Contains("Not enabled: Umbral could not reach Apple (", decoded)

        // Enabled again by hand, Apple still down: switching off works and asks nothing.
        do! ConnectorStore.setEnabled world.App.DbConfig world.App.Clock "apple" true
        let requests = world.Fake.Stub.Requests.Length

        let! off = world.Operator.PostForm("/admin/connectors", path + "/toggle?confirm=yes", [])
        Assert.Contains("msg=connector-disabled", location off)
        let! after = stored world
        Assert.False after.Value.IsEnabled
        Assert.Equal(requests, world.Fake.Stub.Requests.Length)
    }

[<Fact>]
let ``Enable with the key readable and Apple reachable enables`` () =
    task {
        let! world = start ()
        use _ = world.App
        do! FakeApple.seedConnector world.App world.Fake
        do! ConnectorStore.setEnabled world.App.DbConfig world.App.Clock "apple" false

        let! response = world.Operator.PostForm("/admin/connectors", path + "/toggle", [])
        Assert.Contains("msg=connector-enabled", location response)
        let! connector = stored world
        Assert.True connector.Value.IsEnabled
    }

// ---------------------------------------------------------------------------
// One Apple connector
// ---------------------------------------------------------------------------

[<Fact>]
let ``the add form does not create an Apple connector, and nothing else can take the slug`` () =
    task {
        let! world = start ()
        use _ = world.App
        let post kind =
            world.Operator.PostForm(
                "/admin/connectors/new",
                "/admin/connectors",
                [ "kind", kind
                  "slug", "apple"
                  "displayName", "Another Apple"
                  "clientId", "client"
                  "clientSecret", "secret"
                  "issuer", "https://id.example.org" ]
            )

        let! crafted = post "apple"
        Assert.Contains("connector-unknown", location crafted)

        let! discord = post "discord"
        Assert.Contains("connector-slug-taken", location discord)

        let! oidc = post "oidc"
        Assert.Equal(HttpStatusCode.UnprocessableEntity, oidc.StatusCode)

        let! rows = count world "SELECT count(*) FROM connectors WHERE slug = 'apple'"
        Assert.Equal("0", rows)
    }

[<Fact>]
let ``a legacy connector of another kind holding the slug keeps its entry and gets no second Apple one`` () =
    task {
        let! world = start ()
        use _ = world.App
        let! reference = Fixtures.putSecret world.App "apple" "legacy-secret"

        do!
            ConnectorStore.save
                world.App.DbConfig
                world.App.Clock
                { Slug = "apple"
                  Kind = ProviderKind.Fake
                  DisplayName = "Legacy"
                  ClientId = "legacy"
                  ClientSecretRef = reference.Encoded
                  ClientSecretHint = "cret"
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

        let! list = world.Operator.GetHtml "/admin/connectors"
        let entries = [ for m in Regex.Matches(list, "data-connector-entry=\"apple\"") -> m.Value ]
        Assert.Equal(1, entries.Length)

        // Saving the Apple form must not turn the other kind's row into Apple's.
        let! response = save world (fields world)
        Assert.Contains("connector-unknown", location response)
        let! kind = count world "SELECT kind FROM connectors WHERE slug = 'apple'"
        Assert.Equal("fake", kind)
    }

[<Fact>]
let ``a post without an antiforgery token changes nothing`` () =
    task {
        let! world = start ()
        use _ = world.App
        let! response = world.Operator.PostWithoutToken(path, fields world)
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        let! rows = count world "SELECT count(*) FROM connectors WHERE slug = 'apple'"
        Assert.Equal("0", rows)
    }

// ---------------------------------------------------------------------------
// What the screen stores is what the adapter reads
// ---------------------------------------------------------------------------

[<Fact>]
let ``a sign-in through the connector the admin screen created works end to end`` () =
    task {
        let! world = start ()
        use _ = world.App
        let! saved = save world (fields world)
        Assert.Contains("msg=connector-saved", location saved)

        world.Fake.AssertsVerifiedEmail("apple-sub-admin", "maria@example.com")
        // The Apps screen is driven with the helper's own browser, signed in as
        // the operator by the same cookies.
        let operator = Journeys.Browser world.App.Client

        for cookie in world.Operator.Cookies do
            operator.Cookies[cookie.Key] <- cookie.Value

        let! clientId, _ = createClientApp operator

        let person = Journeys.Browser world.App.Client
        let! toPicker = person.Get(authorizeUrl clientId "openid email profile")
        let! toApple = person.Get((location toPicker).Replace("/signin", "/connect/apple/start"))
        let authorizeUri = Uri(location toApple)
        let posted = world.Fake.Approve(authorizeUri, Some FakeApple.mariaUser)
        let! back = FakeApple.post person posted
        Assert.Contains("/connect/authorize", location back)

        // The fake judged the client secret Umbral signed with the stored key.
        Assert.Equal(1, world.Fake.TokenRequests)
        let! users = scalar world.App "SELECT count(*) FROM users" []
        Assert.Equal("1", users)
    }

// ---------------------------------------------------------------------------
// Unsharing, with an app that would be stranded
// ---------------------------------------------------------------------------

[<Fact>]
let ``unsharing the only shared connector warns, and the warning's own button saves it unshared with everything else kept`` () =
    task {
        let! world = start ()
        use _ = world.App

        // The Apple connector, with a list of its own, is the only shared enabled
        // connector; an app that offers every shared connector depends on it.
        let! saved = save world (fields world |> with' [ "allowedDomains", "example.com" ])
        Assert.Contains("msg=connector-saved", location saved)
        let! keyBefore = storedKey world

        let operator = Journeys.Browser world.App.Client

        for cookie in world.Operator.Cookies do
            operator.Cookies[cookie.Key] <- cookie.Value

        let! clientId, _ = createClientApp operator
        for cookie in operator.Cookies do
            world.Operator.Cookies[cookie.Key] <- cookie.Value

        // The pane's form with the shared box unticked: nothing changes, and the
        // screen says which app would be left with no way to sign in.
        let unticked = fields world |> List.filter (fun (name, _) -> name <> "shared" && name <> "privateKey")
        let! warned = save world unticked
        Assert.Contains("connector-strands-apps", location warned)
        let! unchanged = stored world
        Assert.True unchanged.Value.IsShared

        let! screen = world.Operator.GetHtml(location warned)
        Assert.Contains($"data-stranded-app=\"{clientId}\"", screen)
        Assert.Contains("data-notice=\"connector-strands-apps\"", screen)

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
        Assert.Equal<string list>(List.sort [ "__RequestVerificationToken"; "displayName"; "clientId"; "servicesId"; "teamId"; "keyId"; "allowedDomains" ], names)
        Assert.DoesNotContain("privateKey", names)

        let! confirmed = world.Operator.PostWithoutToken(action, hidden)
        Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode)
        Assert.Contains("msg=connector-saved", location confirmed)

        // Unshared, and nothing else moved.
        let! after = stored world
        let after = after.Value
        Assert.False after.IsShared
        Assert.True after.IsEnabled
        Assert.Equal("Apple", after.DisplayName)
        Assert.Equal(FakeApple.ServicesId, after.ClientId)
        Assert.Equal(Some { TeamId = FakeApple.TeamId; KeyId = FakeApple.KeyId }, after.AppleKey)
        Assert.Equal<string list>([ "example.com" ], after.AllowedEmailDomains)
        let! keyAfter = storedKey world
        Assert.True keyBefore.IsSome
        Assert.Equal(keyBefore, keyAfter)
    }
