/// An external secret store through the product (specs/005 User Story 1): the
/// real handlers, registry and callback over an in-memory store registered as
/// `aws`, with `UMBRAL:SECRET_STORE=aws` so the settings parse as they would in
/// a deployment. The identity provider is the stub behind the real adapter.
///
/// Asserts on notice codes, events and the store's own call log, never on the
/// wording of a text, except where the text must name the secret (FR-010).
module Umbral.Tests.ExternalSecretConnectorTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Infrastructure.Secrets
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.InMemorySecretStore
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.StubOidcProvider

let private awsSettings =
    [ "UMBRAL:SECRET_STORE", "aws"
      "AWS_REGION", "eu-west-1" ]

let private issuer = "https://auth.example.test/application/o/umbral"
let private plaintext = "plaintext-client-secret-8d41c7"

/// Runs `afterPut` once the inner store has written, so a test can make a create
/// lose its race for the slug in the window between the secret and the row.
type private RacingStore(inner: ISecretStore, afterPut: unit -> Task<unit>) =
    interface ISecretStore with
        member _.Id = inner.Id

        member _.Put(name, value, ct) =
            task {
                let! result = inner.Put(name, value, ct)
                do! afterPut ()
                return result
            }

        member _.Get(reference, ct) = inner.Get(reference, ct)
        member _.Delete(reference, ct) = inner.Delete(reference, ct)
        member _.Probe ct = inner.Probe ct

/// The app with `store` as `aws` and active, the stub behind provider calls, an
/// operator signed in, and the fake connector so that deleting another one is
/// never the "last door".
let private start (store: ISecretStore) (stub: StubOidcProvider) (settings: (string * string) list) =
    task {
        let! app =
            Fixtures.createAppWithProviderHandler
                (stub :> HttpMessageHandler)
                (fakeSettings @ settings)
                (Fixtures.withSecretStores [ store ] "aws")

        stub.Now <- fun () -> app.Clock.GetUtcNow()

        let operator = Browser app.Client
        do! claimInstance app operator
        do! addFakeConnector app "fake-connector-secret-value"
        return app, operator
    }

let private events (app: Fixtures.TestApp) (kind: string) =
    Flows.scalar
        app
        "SELECT coalesce(group_concat(actor || '|' || outcome || '|' || coalesce(connector_slug, '') || '|' || coalesce(detail, ''), ' ; '), '')
         FROM security_events WHERE event_type = @t"
        [ "@t", kind ]

let private oidcFields (slug: string) =
    [ "kind", "oidc"
      "slug", slug
      "displayName", "Home"
      "issuer", issuer
      "clientId", "umbral-client"
      "clientSecret", plaintext
      "shared", "true" ]

let private githubFields (slug: string) =
    [ "kind", "github"
      "slug", slug
      "displayName", "GitHub " + slug
      "clientId", "gh-client"
      "clientSecret", plaintext ]

let private location (response: HttpResponseMessage) = response.Headers.Location |> string

/// The names a store holds for a slug, as `umbral-<slug>-<8>` strings.
let private namesFor (store: InMemorySecretStore) (slug: string) =
    store.Locators
    |> List.choose (fun locator ->
        let m = Regex.Match(locator, $"^mem/(umbral-{Regex.Escape slug}-[a-z2-7]{{8}})$")
        if m.Success then Some m.Groups[1].Value else None)

let private referenceOf (app: Fixtures.TestApp) (slug: string) =
    task {
        let! stored = ConnectorStore.findBySlug app.DbConfig slug
        return stored.Value.ClientSecretRef
    }

/// SC-002: the plaintext, and any `local:` reference, are in no file of the
/// data directory (database, its journal and shared memory, key ring).
let private scanDataDirectory (app: Fixtures.TestApp) (needles: string list) =
    for file in Directory.EnumerateFiles(app.DataDirectory, "*", SearchOption.AllDirectories) do
        use stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ||| FileShare.Delete)
        use memory = new MemoryStream()
        stream.CopyTo memory
        // Latin-1 maps every byte to one char, so an ASCII needle is found as is.
        let content = Encoding.Latin1.GetString(memory.ToArray())

        for needle in needles do
            Assert.False(content.Contains needle, $"'{needle}' found in {Path.GetFileName file}")

let private signInThroughProvider (browser: Browser) (stub: StubOidcProvider) (clientId: string) (slug: string) =
    task {
        let! toPicker = browser.Get(authorizeUrl clientId "openid email profile")
        let! toProvider = browser.Get((toPicker.Headers.Location |> string).Replace("/signin", $"/connect/{slug}/start"))
        let authorization = stub.Authorize(Uri(toProvider.Headers.Location |> string))

        let! back =
            browser.Get(
                $"/connect/{slug}/callback?code={Uri.EscapeDataString authorization.Code}&state={Uri.EscapeDataString authorization.State}"
            )

        return back.Headers.Location |> string
    }

let private reachedApp (landed: string) = landed.Contains "/connect/authorize"

// ---------------------------------------------------------------------------
// Creating
// ---------------------------------------------------------------------------

[<Fact>]
let ``the OIDC add form writes the secret to the store, signs in with it, and leaves no plaintext on disk`` () =
    task {
        let store = InMemorySecretStore "aws"
        let stub = create issuer
        let! app, operator = start store stub awsSettings
        use _ = app

        let! added = operator.PostForm("/admin/connectors", "/admin/connectors", oidcFields "home")
        Assert.Contains("connector-added-oidc", location added)

        let names = namesFor store "home"
        Assert.Equal(1, names.Length)
        Assert.Equal<string list>([ plaintext ], store.VersionsOf $"mem/{names.Head}")

        let! reference = referenceOf app "home"
        Assert.StartsWith("aws:", reference)

        let! clientId, _ = createClientApp operator
        let landed = signInThroughProvider (Browser app.Client) stub clientId "home"
        let! landed = landed
        Assert.True(reachedApp landed, landed)
        Assert.Equal(Some plaintext, stub.LastTokenRequest.Value.BasicClientSecret)

        scanDataDirectory app [ plaintext; "local:" ]
    }

[<Fact>]
let ``the other two create paths write to the store under umbral-slug-discriminator`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app, operator = start store (create issuer) awsSettings
        use _ = app

        let! added = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "work-github")
        Assert.Contains("connector-added", location added)

        // The first save on the empty card of a kind.
        let! saved =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors/github",
                [ "clientId", "gh-client"
                  "clientSecret", plaintext ] )

        Assert.Contains("connector-saved", location saved)

        for slug in [ "work-github"; "github" ] do
            let names = namesFor store slug
            Assert.Equal(1, names.Length)
            Assert.Equal<string list>([ plaintext ], store.VersionsOf $"mem/{names.Head}")
            let! reference = referenceOf app slug
            Assert.StartsWith("aws:", reference)

        scanDataDirectory app [ plaintext; "local:" ]
    }

[<Fact>]
let ``with no setting at all the reference is local`` () =
    task {
        let stub = create issuer
        let! app = Fixtures.createAppWithProviderHandler (stub :> HttpMessageHandler) fakeSettings ignore
        use _ = app
        let operator = Browser app.Client
        do! claimInstance app operator

        let! added = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "github-one")
        Assert.Contains("connector-added", location added)

        let! reference = referenceOf app "github-one"
        Assert.StartsWith("local:", reference)
    }

[<Fact>]
let ``a store that refuses the write saves nothing on any create path`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app, operator = start store (create issuer) awsSettings
        use _ = app
        store.FailEveryCall AccessDenied

        // OIDC: the form is drawn again, not a 500.
        let! oidc = operator.PostForm("/admin/connectors", "/admin/connectors", oidcFields "home")
        Assert.Equal(HttpStatusCode.UnprocessableEntity, oidc.StatusCode)
        let! body = oidc.Content.ReadAsStringAsync()
        Assert.Contains("AWS Secrets Manager", body)
        Assert.DoesNotContain(plaintext, body)

        let! second = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "work-github")
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode)
        Assert.Contains("connector-secret-store-failed", location second)

        let! third =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors/github",
                [ "clientId", "gh-client"; "clientSecret", plaintext ])

        Assert.Equal(HttpStatusCode.Redirect, third.StatusCode)
        Assert.Contains("connector-secret-store-failed", location third)

        // The screen the redirect lands on names the store and what to check.
        let! screen = operator.GetHtml(location third)
        Assert.Contains("data-notice=\"connector-secret-store-failed\"", screen)
        Assert.Contains("AWS Secrets Manager", screen)
        Assert.DoesNotContain(plaintext, screen)

        for slug in [ "home"; "work-github"; "github" ] do
            let! stored = ConnectorStore.findBySlug app.DbConfig slug
            Assert.True(stored.IsNone, $"{slug} must not be saved")

        let! created = events app "connector_created"
        Assert.Equal("", created)

        let! failed = events app "secret_store_failed"
        let lines = failed.Split " ; "
        Assert.Equal(3, lines.Length)

        for line in lines do
            Assert.Contains("operator|failure|", line)
            Assert.EndsWith("store=aws;op=put;cause=access_denied", line)

        Assert.DoesNotContain(plaintext, failed)
    }

[<Fact>]
let ``a create that loses the race for the slug deletes the secret it wrote`` () =
    task {
        let inner = InMemorySecretStore "aws"
        let appRef = ref Unchecked.defaultof<Fixtures.TestApp>
        // Armed once the app is up: seeding writes secrets through this store too.
        let armed = ref false

        // Between the secret and the row, someone else takes the slug.
        let rival () =
            task {
                if armed.Value then
                    let app = appRef.Value
                    let now = app.Clock.GetUtcNow()

                    let! _ =
                        ConnectorStore.create
                            app.DbConfig
                            now
                            { Slug = "work-github"
                              Kind = GitHub
                              DisplayName = "Rival"
                              ClientId = "rival"
                              ClientSecretRef = ""
                              ClientSecretHint = ""
                              IssuerUrl = None
                              AllowedEmailDomains = []
                              IsEnabled = true
                              IsShared = true
                              CreatedUtc = now
                              UpdatedUtc = now }
                            ("aws:rival", "ival")

                    ()
            }

        let! app, operator = start (RacingStore(inner, rival)) (create issuer) awsSettings
        use _ = app
        appRef.Value <- app
        armed.Value <- true

        let! lost = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "work-github")
        Assert.Contains("connector-slug-taken", location lost)

        Assert.Empty(namesFor inner "work-github")
        let wrote = inner.Calls |> List.filter (fun c -> c.Op = "put" && c.Target.Value.StartsWith "umbral-work-github-")
        let deleted = inner.Calls |> List.filter (fun c -> c.Op = "delete" && c.Target.Value.StartsWith "mem/umbral-work-github-")
        Assert.Equal(1, wrote.Length)
        Assert.Equal(1, deleted.Length)
    }

// ---------------------------------------------------------------------------
// Replacing and deleting
// ---------------------------------------------------------------------------

[<Fact>]
let ``replacing the secret adds a version under the same name and the next sign-in sends it`` () =
    task {
        let store = InMemorySecretStore "aws"
        let stub = create issuer
        let! app, operator = start store stub awsSettings
        use _ = app

        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", oidcFields "home")
        let! before = referenceOf app "home"
        let replacement = "replacement-secret-93ab20"

        let! saved =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors/home",
                [ "displayName", "Home"
                  "clientId", "umbral-client"
                  "clientSecret", replacement
                  "shared", "true" ])

        Assert.Contains("connector-saved", location saved)

        let! after = referenceOf app "home"
        Assert.Equal(before, after)

        let names = namesFor store "home"
        Assert.Equal(1, names.Length)
        Assert.Equal<string list>([ plaintext; replacement ], store.VersionsOf $"mem/{names.Head}")

        let! clientId, _ = createClientApp operator
        let! landed = signInThroughProvider (Browser app.Client) stub clientId "home"
        Assert.True(reachedApp landed, landed)
        Assert.Equal(Some replacement, stub.LastTokenRequest.Value.BasicClientSecret)

        scanDataDirectory app [ plaintext; replacement; "local:" ]
    }

[<Fact>]
let ``replacing a secret that lives in the local store moves it to the active store and removes the old copy`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app, operator = start store (create issuer) awsSettings
        use _ = app

        // A connector written before the instance had a store: a local reference.
        let secrets = app.Services.GetRequiredService<Umbral.Infrastructure.SecretStores.Registry.SecretStores>()
        let local = secrets.For "local" |> Result.defaultWith (fun _ -> failwith "no local")
        let now = app.Clock.GetUtcNow()
        let! put = local.Put(SecretName.derive "umbral" "github" now, plaintext, CancellationToken.None)
        let old = match put with Ok r -> r | Error _ -> failwith "local put"

        let! _ =
            ConnectorStore.create
                app.DbConfig
                now
                { Slug = "github"
                  Kind = GitHub
                  DisplayName = "GitHub"
                  ClientId = "gh-client"
                  ClientSecretRef = ""
                  ClientSecretHint = ""
                  IssuerUrl = None
                  AllowedEmailDomains = []
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = now
                  UpdatedUtc = now }
                (old.Encoded, "hint")

        let! saved =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors/github",
                [ "displayName", "GitHub"; "clientId", "gh-client"; "clientSecret", plaintext ])

        Assert.Contains("connector-saved", location saved)

        let! after = referenceOf app "github"
        Assert.StartsWith("aws:", after)
        // The name derives from the row's own created_utc.
        let expected = (SecretName.derive "umbral" "github" now).Cloud
        Assert.Equal<string list>([ expected ], namesFor store "github")
    }

[<Fact>]
let ``deleting a connector deletes its secret from the store`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app, operator = start store (create issuer) awsSettings
        use _ = app
        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "work-github")
        let name = (namesFor store "work-github").Head

        let! deleted = operator.PostForm("/admin/connectors", "/admin/connectors/work-github/delete", [])
        Assert.Contains("connector-deleted", location deleted)

        Assert.Empty(namesFor store "work-github")
        Assert.Contains({ Op = "delete"; Target = Some $"mem/{name}" }, store.Calls)
    }

[<Fact>]
let ``created, updated and deleted events carry the store that holds the secret`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app, operator = start store (create issuer) awsSettings
        use _ = app

        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "work-github")
        let! created = events app "connector_created"
        Assert.Equal("operator|success|work-github|store=aws", created)

        // Saving without a new secret changes no secret: no store to name.
        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors/work-github", [ "clientId", "gh-client"; "displayName", "Renamed" ])
        let! renamed = events app "connector_updated"
        Assert.Equal("operator|success|work-github|", renamed)

        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors/work-github", [ "clientId", "gh-client"; "clientSecret", "another-secret-value" ])
        let! updated = events app "connector_updated"
        Assert.Equal("operator|success|work-github| ; operator|success|work-github|store=aws", updated)

        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors/work-github/delete", [])
        let! deleted = events app "connector_deleted"
        Assert.Equal("operator|success|work-github|store=aws", deleted)
    }

[<Fact>]
let ``a replace is refused while a move holds the connector's lease, and a delete too, saving nothing`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app, operator = start store (create issuer) awsSettings
        use _ = app
        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "work-github")
        let name = (namesFor store "work-github").Head
        let! before = referenceOf app "work-github"
        let callsBefore = store.CallCount

        let! taken = ConnectorStore.tryLease app.DbConfig app.Clock "work-github" "a-move" ConnectorStore.leaseLength
        Assert.True taken

        let! replaced =
            operator.PostForm("/admin/connectors", "/admin/connectors/work-github", [ "clientId", "gh-client"; "clientSecret", "replacement-secret-value" ])

        Assert.Contains("connector-secret-busy", location replaced)
        let! screen = operator.GetHtml(location replaced)
        Assert.Contains("data-notice=\"connector-secret-busy\"", screen)
        Assert.Contains("A secret move is working on this connector. Try again in a minute.", screen)

        let! deleted = operator.PostForm("/admin/connectors", "/admin/connectors/work-github/delete", [])
        Assert.Contains("connector-secret-busy", location deleted)

        // Nothing was written, replaced or deleted, anywhere.
        Assert.Equal(callsBefore, store.CallCount)
        Assert.Equal<string list>([ plaintext ], store.VersionsOf $"mem/{name}")
        let! after = referenceOf app "work-github"
        Assert.Equal(before, after)

        // Released: the same replace goes through.
        do! ConnectorStore.releaseLease app.DbConfig "work-github" "a-move"

        let! again =
            operator.PostForm("/admin/connectors", "/admin/connectors/work-github", [ "clientId", "gh-client"; "clientSecret", "replacement-secret-value" ])

        Assert.Contains("connector-saved", location again)
        Assert.Equal<string list>([ plaintext; "replacement-secret-value" ], store.VersionsOf $"mem/{name}")

        // And the lease is not left behind by a finished save.
        let! free = ConnectorStore.tryLease app.DbConfig app.Clock "work-github" "next" ConnectorStore.leaseLength
        Assert.True free
    }

[<Fact>]
let ``a lease that expired does not block the admin screen`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app, operator = start store (create issuer) awsSettings
        use _ = app
        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "work-github")

        let! _ = ConnectorStore.tryLease app.DbConfig app.Clock "work-github" "a-dead-move" ConnectorStore.leaseLength
        app.Clock.Advance(TimeSpan.FromMinutes 3.)

        let! replaced =
            operator.PostForm("/admin/connectors", "/admin/connectors/work-github", [ "clientId", "gh-client"; "clientSecret", "replacement-secret-value" ])

        Assert.Contains("connector-saved", location replaced)
    }

[<Fact>]
let ``a replace that keeps the secret in the same store never deletes the old copy, even if its locator reads differently`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app, operator = start store (create issuer) awsSettings
        use _ = app
        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "work-github")

        // The row names the same secret under another spelling of its locator
        // (the way GCP names a project by id or by number).
        let! stored = ConnectorStore.findBySlug app.DbConfig "work-github"
        let respelled = { Store = "aws"; Locator = "mem/respelled-locator" }
        do! ConnectorStore.save app.DbConfig app.Clock stored.Value (Some(respelled.Encoded, "hint"))

        let callsBefore = store.CallCount

        let! saved =
            operator.PostForm("/admin/connectors", "/admin/connectors/work-github", [ "clientId", "gh-client"; "clientSecret", "replacement-secret-value" ])

        Assert.Contains("connector-saved", location saved)
        let calls = store.Calls |> List.skip callsBefore
        Assert.Equal<string list>([ "put" ], calls |> List.map (fun c -> c.Op))
    }

[<Fact>]
let ``a replace that moves the secret between stores and cannot remove the old copy says the connector was saved`` () =
    task {
        let aws = InMemorySecretStore "aws"
        let gcp = InMemorySecretStore "gcp"

        let! app =
            Fixtures.createAppWithProviderHandler
                (create issuer :> HttpMessageHandler)
                (fakeSettings @ awsSettings)
                (Fixtures.withSecretStores [ aws :> ISecretStore; gcp ] "aws")

        use _ = app
        let operator = Browser app.Client
        do! claimInstance app operator
        do! addFakeConnector app "fake-connector-secret-value"

        let now = app.Clock.GetUtcNow()
        let old = { Store = "gcp"; Locator = "mem/old-copy" }

        let! _ =
            ConnectorStore.create
                app.DbConfig
                now
                { Slug = "github"
                  Kind = ProviderKind.GitHub
                  DisplayName = "GitHub"
                  ClientId = "gh-client"
                  ClientSecretRef = ""
                  ClientSecretHint = ""
                  IssuerUrl = None
                  AllowedEmailDomains = []
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = now
                  UpdatedUtc = now }
                (old.Encoded, "hint")

        gcp.FailEveryCall Unreachable

        let! saved =
            operator.PostForm("/admin/connectors", "/admin/connectors/github", [ "displayName", "GitHub"; "clientId", "gh-client"; "clientSecret", plaintext ])

        Assert.Contains("connector-secret-replaced-left-behind", location saved)
        let! screen = operator.GetHtml(location saved)
        Assert.Contains("data-notice=\"connector-secret-replaced-left-behind\"", screen)
        Assert.Contains("The connector was saved, but its previous secret could not be removed from Google Cloud Secret Manager", screen)
        Assert.DoesNotContain("was deleted", screen)

        let! after = referenceOf app "github"
        Assert.StartsWith("aws:", after)
    }

[<Fact>]
let ``deleting removes exactly the reference the deleted row held`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app, operator = start store (create issuer) awsSettings
        use _ = app
        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "work-github")

        // The reference the row holds NOW, after a move or a replace changed it
        // since the page the operator was looking at was drawn.
        let! stored = ConnectorStore.findBySlug app.DbConfig "work-github"
        let current = { Store = "aws"; Locator = "mem/the-current-copy" }
        do! ConnectorStore.save app.DbConfig app.Clock stored.Value (Some(current.Encoded, "hint"))

        let! returned = ConnectorStore.delete app.DbConfig "work-github"
        Assert.Equal(Some current.Encoded, returned)

        let! absent = ConnectorStore.delete app.DbConfig "work-github"
        Assert.Equal(None, absent)

        // Through the handler the store is asked to delete that reference.
        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "second-github")
        let! second = referenceOf app "second-github"
        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors/second-github/delete", [])
        let target = (SecretRef.Decode second |> Result.defaultWith failwith).Locator
        Assert.Contains({ Op = "delete"; Target = Some target }, store.Calls)
    }

[<Fact>]
let ``a store that refuses the delete leaves the connector gone, records both events and names the secret`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app, operator = start store (create issuer) awsSettings
        use _ = app
        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "work-github")
        let name = (namesFor store "work-github").Head
        store.FailEveryCall Unreachable

        let! deleted = operator.PostForm("/admin/connectors", "/admin/connectors/work-github/delete", [])
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode)
        Assert.Contains("connector-secret-left-behind", location deleted)

        let! stored = ConnectorStore.findBySlug app.DbConfig "work-github"
        Assert.True stored.IsNone

        let! removed = events app "connector_deleted"
        Assert.Equal("operator|success|work-github|store=aws", removed)

        let! failed = events app "secret_store_failed"
        Assert.Equal("operator|failure|work-github|store=aws;op=delete;cause=unreachable", failed)

        // The name comes from the cookie the refusal wrote, not from the URL.
        let! screen = operator.GetHtml(location deleted)
        Assert.Contains("data-notice=\"connector-secret-left-behind\"", screen)
        Assert.Contains(name, screen)
        Assert.DoesNotContain("mem/", screen)
        Assert.DoesNotContain(name, location deleted)
    }

[<Fact>]
let ``a connector created again under a deleted slug gets a different secret name`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app, operator = start store (create issuer) awsSettings
        use _ = app

        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "work-github")
        let first = (namesFor store "work-github").Head
        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors/work-github/delete", [])

        app.Clock.Advance(TimeSpan.FromSeconds 5.)

        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "work-github")
        let second = (namesFor store "work-github").Head

        Assert.NotEqual<string>(first, second)
    }

// ---------------------------------------------------------------------------
// Signing in
// ---------------------------------------------------------------------------

[<Fact>]
let ``a secret the store cannot return fails the sign-in with the store and the cause on the record`` () =
    task {
        let store = InMemorySecretStore "aws"
        let stub = create issuer
        let! app, operator = start store stub awsSettings
        use _ = app
        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", oidcFields "home")
        let! clientId, _ = createClientApp operator

        store.FailEveryCall AccessDenied

        let browser = Browser app.Client
        let! landed = signInThroughProvider browser stub clientId "home"
        Assert.False(reachedApp landed, landed)

        let! failed = events app "secret_store_failed"
        Assert.Equal("user|failure|home|store=aws;op=get;cause=access_denied", failed)

        let! signIn = events app "sign_in_failed"
        Assert.Contains("user|failure|home|connector_secret_unreadable;store=aws;cause=access_denied", signIn)
    }

// ---------------------------------------------------------------------------
// The cache and its grace period (User Story 2)
// ---------------------------------------------------------------------------

let private signedInHome (store: InMemorySecretStore) (stub: StubOidcProvider) (settings: (string * string) list) =
    task {
        let! app, operator = start store stub settings
        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", oidcFields "home")
        let! clientId, _ = createClientApp operator
        return app, operator, clientId
    }

let private gets (store: InMemorySecretStore) =
    store.Calls |> List.filter (fun call -> call.Op = "get") |> List.length

[<Fact>]
let ``with the store down a sign-in is served from the grace period, and fails once it is over`` () =
    task {
        let store = InMemorySecretStore "aws"
        let stub = create issuer
        let! app, _, clientId = signedInHome store stub awsSettings
        use _ = app

        let! first = signInThroughProvider (Browser app.Client) stub clientId "home"
        Assert.True(reachedApp first, first)
        Assert.Equal(1, gets store)

        store.FailEveryCall Unreachable
        app.Clock.Advance(TimeSpan.FromSeconds 301.)

        let! second = signInThroughProvider (Browser app.Client) stub clientId "home"
        Assert.True(reachedApp second, second)
        Assert.Equal(2, gets store)

        let! stale = events app "secret_store_failed"
        Assert.Equal("user|failure|home|store=aws;op=get;cause=unreachable;stale=true", stale)

        // 3 901 s after the fetch: lifetime 300 + grace 3 600 is over.
        app.Clock.Advance(TimeSpan.FromSeconds 3600.)

        let! third = signInThroughProvider (Browser app.Client) stub clientId "home"
        Assert.False(reachedApp third, third)

        let! signIn = events app "sign_in_failed"
        Assert.Contains("connector_secret_unreadable;store=aws;cause=unreachable", signIn)
    }

[<Fact>]
let ``replacing the secret makes the next sign-in use the new value whatever the cache holds`` () =
    task {
        let store = InMemorySecretStore "aws"
        let stub = create issuer
        let! app, operator, clientId = signedInHome store stub awsSettings
        use _ = app

        let! _ = signInThroughProvider (Browser app.Client) stub clientId "home"
        Assert.Equal(Some plaintext, stub.LastTokenRequest.Value.BasicClientSecret)

        let replacement = "replacement-secret-5be1c4"

        let! saved =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors/home",
                [ "displayName", "Home"
                  "clientId", "umbral-client"
                  "clientSecret", replacement
                  "shared", "true" ])

        Assert.Contains("connector-saved", location saved)

        // No time has passed: only the eviction can make this a fresh read.
        let! landed = signInThroughProvider (Browser app.Client) stub clientId "home"
        Assert.True(reachedApp landed, landed)
        Assert.Equal(Some replacement, stub.LastTokenRequest.Value.BasicClientSecret)
    }

[<Fact>]
let ``a new app host over the same data directory starts with an empty cache`` () =
    task {
        let store = InMemorySecretStore "aws"
        let stub = create issuer
        let directory = Fixtures.freshDataDirectory ()

        try
            let startOn () =
                task {
                    let! app =
                        Fixtures.createAppIn
                            directory
                            (fakeSettings @ awsSettings)
                            (fun builder ->
                                builder.Services.AddSingleton<Umbral.Infrastructure.ProviderAdapters.PrimaryHandler>(
                                    Umbral.Infrastructure.ProviderAdapters.PrimaryHandler(fun () -> stub :> HttpMessageHandler))
                                |> ignore

                                Fixtures.withSecretStores [ store ] "aws" builder)

                    stub.Now <- fun () -> app.Clock.GetUtcNow()
                    return app
                }

            let! first = startOn ()
            let! clientId =
                task {
                    use _ = first
                    let operator = Browser first.Client
                    do! claimInstance first operator
                    do! addFakeConnector first "fake-connector-secret-value"
                    let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", oidcFields "home")
                    let! clientId, _ = createClientApp operator
                    let! landed = signInThroughProvider (Browser first.Client) stub clientId "home"
                    Assert.True(reachedApp landed, landed)
                    Assert.Equal(1, gets store)

                    // Warm: a second sign-in does not call the store.
                    let! again = signInThroughProvider (Browser first.Client) stub clientId "home"
                    Assert.True(reachedApp again, again)
                    Assert.Equal(1, gets store)
                    return clientId
                }

            use! second = startOn ()
            let! landed = signInThroughProvider (Browser second.Client) stub clientId "home"
            Assert.True(reachedApp landed, landed)
            Assert.Equal(2, gets store)
        finally
            try
                if Directory.Exists directory then
                    Directory.Delete(directory, true)
            with _ ->
                ()
    }

// ---------------------------------------------------------------------------
// User Story 4: knowing where secrets are, and why something fails
// ---------------------------------------------------------------------------

/// The markup of one connector card, so an assertion about a card cannot be
/// satisfied by another card's marker.
let private cardOf (html: string) (slug: string) =
    let m = Regex.Match(html, $"<article[^>]*data-connector=\"{Regex.Escape slug}\"[^>]*>.*?</article>", RegexOptions.Singleline)
    Assert.True(m.Success, $"no card for '{slug}'")
    m.Value

/// The app with `store` failing (or answering) as given BEFORE it starts, so the
/// startup probe meets it, and no fake connector: seeding one would write to the
/// store and clear the very state under test. Logs are captured at warning and
/// above.
let private startProbed (store: InMemorySecretStore) =
    task {
        let logs = new LogRedactionTests.CapturingProvider()

        let! app =
            Fixtures.createAppWithProviderHandler
                (create issuer :> HttpMessageHandler)
                (fakeSettings @ awsSettings)
                (fun builder ->
                    Fixtures.withSecretStores [ store ] "aws" builder
                    builder.Logging.AddProvider logs |> ignore)

        let operator = Browser app.Client
        do! claimInstance app operator
        return app, operator, logs
    }

let private problemMarker = "data-secret-store-problem"

[<Fact>]
let ``each card says which store keeps its secret, with the Texts label`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app, operator = start store (create issuer) awsSettings
        use _ = app

        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", oidcFields "home")

        // Seeded straight into the table with a local reference: an instance
        // that switched to aws after this connector was made.
        let! stored = ConnectorStore.findBySlug app.DbConfig "fake"
        let local = LocalSecretStore(app.Services.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()) :> ISecretStore

        let! put = local.Put(SecretName.derive "umbral" "fake" (app.Clock.GetUtcNow()), "fake-connector-secret-value", CancellationToken.None)
        let reference = match put with | Ok r -> r | Error _ -> failwith "local put"
        do! ConnectorStore.save app.DbConfig app.Clock { stored.Value with ClientSecretRef = reference.Encoded } None

        let! html = operator.GetHtml "/admin/connectors"
        let texts = Umbral.Views.Texts.current ()

        let home = cardOf html "home"
        Assert.Contains("data-secret-store=\"aws\"", home)
        Assert.Contains(texts.SecretStoreLabel "aws", home)
        Assert.Contains("Kept in AWS Secrets Manager", home)
        Assert.DoesNotContain("data-secret-unreadable", home)

        let fake = cardOf html "fake"
        Assert.Contains("data-secret-store=\"local\"", fake)
        Assert.Contains("Kept in this instance (encrypted)", fake)
        Assert.DoesNotContain("data-secret-unreadable", fake)
    }

[<Fact>]
let ``a card whose reference names a store this instance cannot build warns, with what to do`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app, operator = start store (create issuer) awsSettings
        use _ = app

        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", oidcFields "home")

        // Only `aws` is registered here, so `azure` is a store this instance has
        // no way to build, as after its credentials were removed.
        do!
            ConnectorStore.save
                app.DbConfig
                app.Clock
                { Slug = "github"
                  Kind = ProviderKind.GitHub
                  DisplayName = "GitHub"
                  ClientId = "gh-client"
                  ClientSecretRef = "azure:https://vault.example.net/secrets/umbral-github-abcdefgh"
                  ClientSecretHint = "••••••••abcd"
                  IssuerUrl = None
                  AllowedEmailDomains = []
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None

        let! html = operator.GetHtml "/admin/connectors"

        let github = cardOf html "github"
        Assert.Contains("data-secret-store=\"azure\"", github)
        Assert.Contains("data-secret-unreadable", github)
        Assert.Contains("Azure Key Vault", github)
        Assert.Contains("cannot read", github)
        Assert.Contains("docs/secret-stores", github)
        Assert.Contains("role=\"note\"", github)

        // The warning is the unreadable card's, not every card's.
        Assert.DoesNotContain("data-secret-unreadable", cardOf html "home")
    }

[<Fact>]
let ``a card whose store has credentials in the environment that cannot be used warns that it is unreadable`` () =
    task {
        // A Bella key is present (so a reader is registered) but does not parse:
        // the store cannot be built, so the card must not claim it can be read.
        let! app =
            Fixtures.createAppWithProviderHandler
                (create issuer :> HttpMessageHandler)
                (fakeSettings @ [ "BELLA_BAXTER_API_KEY", "not-a-bax-key" ])
                ignore

        use _ = app
        let operator = Browser app.Client
        do! claimInstance app operator

        do!
            ConnectorStore.save
                app.DbConfig
                app.Clock
                { Slug = "github"
                  Kind = ProviderKind.GitHub
                  DisplayName = "GitHub"
                  ClientId = "gh-client"
                  ClientSecretRef = "bellabaxter:UMBRAL_UMBRAL_GITHUB_ABCDEFGH"
                  ClientSecretHint = "••••••••abcd"
                  IssuerUrl = None
                  AllowedEmailDomains = []
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None

        let! html = operator.GetHtml "/admin/connectors"
        Assert.Contains("data-secret-unreadable", cardOf html "github")
    }

[<Theory>]
[<InlineData("unreachable")>]
[<InlineData("access_denied")>]
let ``a failing probe at startup starts anyway, warns, records the event and shows the page notice`` (cause: string) =
    task {
        let failure = if cause = "unreachable" then Unreachable else AccessDenied
        let store = InMemorySecretStore "aws"
        store.FailEveryCall failure

        let! app, operator, logs = startProbed store
        use _ = app

        // Started: the page is served, and /healthz does not look at the manager.
        let! health = app.Client.GetAsync "/healthz"
        Assert.Equal(HttpStatusCode.OK, health.StatusCode)

        let! html = operator.GetHtml "/admin/connectors"
        Assert.Contains(problemMarker, html)
        Assert.Contains("AWS Secrets Manager", html)

        let! recorded = events app "secret_store_failed"
        Assert.Equal($"system|failure||store=aws;op=probe;cause={cause}", recorded)

        let warnings = logs.Entries |> Array.filter (fun e -> e.Level = LogLevel.Warning)
        Assert.Contains(warnings, fun e -> e.Everything.Contains "AWS Secrets Manager" && e.Everything.Contains "docs/secret-stores")
    }

[<Fact>]
let ``a successful save to the store clears the startup notice`` () =
    task {
        let store = InMemorySecretStore "aws"
        store.FailEveryCall Unreachable

        let! app, operator, _ = startProbed store
        use _ = app

        let! before = operator.GetHtml "/admin/connectors"
        Assert.Contains(problemMarker, before)

        // Still failing: a refused save does not clear it.
        let! refused = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "work-github")
        Assert.Contains("connector-secret-store-failed", location refused)
        let! stillThere = operator.GetHtml "/admin/connectors"
        Assert.Contains(problemMarker, stillThere)

        store.Recover()
        let! saved = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "work-github")
        Assert.Contains("connector-added", location saved)

        let! after = operator.GetHtml "/admin/connectors"
        Assert.DoesNotContain(problemMarker, after)
    }

[<Fact>]
let ``a probe succeeding shows no notice and records nothing`` () =
    task {
        for failure in [ None ] do
            let store = InMemorySecretStore "aws"
            failure |> Option.iter store.FailEveryCall

            let! app, operator, logs = startProbed store
            use _ = app

            let! html = operator.GetHtml "/admin/connectors"
            Assert.DoesNotContain(problemMarker, html)

            let! recorded = events app "secret_store_failed"
            Assert.Equal("", recorded)
            Assert.DoesNotContain(logs.Entries, fun e -> e.Everything.Contains "docs/secret-stores")
            // One probe, and nothing else asked of the store.
            Assert.Equal<string list>([ "probe" ], store.Calls |> List.map (fun c -> c.Op))
    }

[<Fact>]
let ``a probe answering not found is a failure: the adapters map the probe's own 404 to success`` () =
    task {
        let store = InMemorySecretStore "aws"
        store.FailEveryCall NotFound

        let! app, operator, logs = startProbed store
        use _ = app

        let! html = operator.GetHtml "/admin/connectors"
        Assert.Contains(problemMarker, html)

        let! recorded = events app "secret_store_failed"
        Assert.Equal("system|failure||store=aws;op=probe;cause=not_found", recorded)
        Assert.Contains(logs.Entries, fun e -> e.Level = LogLevel.Warning && e.Everything.Contains "docs/secret-stores")
    }

[<Fact>]
let ``a refused save raises the page notice, and says what the refusal means`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app, operator = start store (create issuer) awsSettings
        use _ = app

        let! before = operator.GetHtml "/admin/connectors"
        Assert.DoesNotContain(problemMarker, before)

        store.FailEveryCall AccessDenied
        let! refused = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "work-github")
        Assert.Contains("connector-secret-store-failed", location refused)

        let! after = operator.GetHtml "/admin/connectors"
        Assert.Contains(problemMarker, after)
    }

[<Fact>]
let ``a secret deleted in the console gets its own text on the save refusal and the page notice`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app, operator = start store (create issuer) awsSettings
        use _ = app
        store.FailEveryCall(Rejected "soft_deleted")

        let! inline' = operator.PostForm("/admin/connectors", "/admin/connectors", oidcFields "home")
        let! body = inline'.Content.ReadAsStringAsync()
        Assert.Contains("holds a deleted secret under this connector", body)

        let! refused = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "work-github")
        let! screen = operator.GetHtml(location refused)
        Assert.Contains("holds a deleted secret under this connector", screen)
        Assert.Contains("Restore it (or purge it) in the console", screen)
        Assert.DoesNotContain("rejected the request", screen)

        // The page notice the failed save raised says the same.
        Assert.Contains(problemMarker, screen)
    }

[<Fact>]
let ``a rejection with a reason that is not a known code is never printed`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app, operator = start store (create issuer) awsSettings
        use _ = app
        store.FailEveryCall(Rejected "VendorSecretThing")

        let! refused = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "work-github")
        let! screen = operator.GetHtml(location refused)
        Assert.Contains("rejected the request", screen)
        Assert.DoesNotContain("VendorSecretThing", screen)
        Assert.DoesNotContain("VendorSecretThing", location refused)
    }

[<Fact>]
let ``an ambiguous BellaBaxter provider names the setting in the warning, the page notice and the save refusal`` () =
    task {
        let store = InMemorySecretStore "aws"
        store.FailEveryCall(Rejected "provider_ambiguous")

        let! app, operator, logs = startProbed store
        use _ = app

        let! html = operator.GetHtml "/admin/connectors"
        Assert.Contains(problemMarker, html)
        Assert.Contains("UMBRAL__BELLABAXTER_PROVIDER", html)

        let warnings = logs.Entries |> Array.filter (fun e -> e.Level = LogLevel.Warning)
        Assert.Contains(warnings, fun e -> e.Everything.Contains "UMBRAL__BELLABAXTER_PROVIDER")

        let! refused = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "work-github")
        let! screen = operator.GetHtml(location refused)
        Assert.Contains("data-notice=\"connector-secret-store-failed\"", screen)
        Assert.Contains("UMBRAL__BELLABAXTER_PROVIDER", screen)
    }

[<Fact>]
let ``with the local store nothing is probed and no notice is shown`` () =
    task {
        let store = InMemorySecretStore "aws"
        store.FailEveryCall Unreachable

        // `aws` is registered but is not the active store.
        let! app =
            Fixtures.createAppWithProviderHandler
                (create issuer :> HttpMessageHandler)
                fakeSettings
                (Fixtures.withSecretStores [ store ] "local")

        use _ = app
        let operator = Browser app.Client
        do! claimInstance app operator

        let! html = operator.GetHtml "/admin/connectors"
        Assert.DoesNotContain(problemMarker, html)
        Assert.Equal(0, store.CallCount)
    }
