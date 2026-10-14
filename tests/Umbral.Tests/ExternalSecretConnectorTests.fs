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
        Assert.Equal("operator|success|work-github|", removed)

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
