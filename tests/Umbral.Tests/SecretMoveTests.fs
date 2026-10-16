/// The `move-secrets` command (specs/005 User Story 3), driven in process: the
/// real `SecretMove.run` over a real app's database, with an in-memory store
/// registered as `aws` so that "the store fails on its Nth call" is a line of
/// test, not a network.
///
/// Moving out of a store needs a registry whose active store is `local`, and the
/// app's own registry (active `aws`) cannot be changed after it is built, so
/// `registryActive` builds a second one over the same stores: the same in-memory
/// store, the same key ring, only the target differs. That is also what the
/// command sees when an operator changes `UMBRAL__SECRET_STORE` and runs it.
module Umbral.Tests.SecretMoveTests

open System
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.DataProtection
open Microsoft.Extensions.DependencyInjection
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Infrastructure.Secrets
open Umbral.Infrastructure.SecretStores.Registry
open Umbral.Infrastructure.SecretStores.Settings
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.InMemorySecretStore
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.StubOidcProvider

let private awsSettings =
    [ "UMBRAL:SECRET_STORE", "aws"
      "AWS_REGION", "eu-west-1" ]

let private issuer = "https://auth.example.test/application/o/umbral"

/// Slug order is the order of the report lines.
let private slugs = [ "alpha"; "bravo"; "charlie" ]

let private plaintextOf (slug: string) = $"plaintext-for-{slug}-3f9a1c"

/// Runs `afterPut` once the inner store has written, so a test can change a row
/// in the window between the copy and the switch.
type private RacingStore(inner: ISecretStore, afterPut: SecretName -> Task<unit>) =
    interface ISecretStore with
        member _.Id = inner.Id

        member _.Put(name, value, ct) =
            task {
                let! result = inner.Put(name, value, ct)
                do! afterPut name
                return result
            }

        member _.Get(reference, ct) = inner.Get(reference, ct)
        member _.Delete(reference, ct) = inner.Delete(reference, ct)
        member _.Probe ct = inner.Probe ct

let private localStore (app: Fixtures.TestApp) =
    LocalSecretStore(app.Services.GetRequiredService<IDataProtectionProvider>()) :> ISecretStore

/// A registry over the same stores whose active one is `activeId`.
let private registryActive (app: Fixtures.TestApp) (store: ISecretStore) (activeId: string) =
    SecretStores(localStore app, activeId, Map [ store.Id, lazy (Ok store) ])

let private settingsOf (app: Fixtures.TestApp) =
    app.Services.GetRequiredService<SecretStoreSettings>()

/// An app with `store` as `aws` and active, which is where a move out of `local` goes.
let private startWith (store: ISecretStore) =
    Fixtures.createAppWith awsSettings (Fixtures.withSecretStores [ store ] "aws")

/// A connector whose secret is in the local store, written the way the product
/// writes one.
let private seedLocal (app: Fixtures.TestApp) (kind: ProviderKind) (slug: string) (issuerUrl: string option) =
    task {
        let now = app.Clock.GetUtcNow()
        let name = SecretName.derive "umbral" slug now
        let plaintext = plaintextOf slug

        match! (localStore app).Put(name, plaintext, CancellationToken.None) with
        | Error failure -> return failwith $"seeding failed: {failure.Cause}"
        | Ok reference ->
            let! _ =
                ConnectorStore.create
                    app.DbConfig
                    now
                    { Slug = slug
                      Kind = kind
                      DisplayName = slug
                      ClientId = "client-" + slug
                      ClientSecretRef = ""
                      ClientSecretHint = ""
                      IssuerUrl = issuerUrl
                      AllowedEmailDomains = []
                      IsEnabled = true
                      IsShared = true
                      CreatedUtc = now
                      UpdatedUtc = now }
                    (reference.Encoded, hint plaintext)

            ()
    }

let private seedAll (app: Fixtures.TestApp) =
    task {
        for slug in slugs do
            do! seedLocal app GitHub slug None
    }

/// Runs the command and gives back the exit code and every line it wrote.
let private move (app: Fixtures.TestApp) (stores: SecretStores) (dryRun: bool) =
    task {
        let lines = ResizeArray<string>()

        let! code =
            SecretMove.run stores (settingsOf app) app.DbConfig app.Clock dryRun (fun line -> lines.Add line)

        return code, List.ofSeq lines
    }

let private referenceOf (app: Fixtures.TestApp) (slug: string) =
    task {
        let! stored = ConnectorStore.findBySlug app.DbConfig slug
        return stored.Value.ClientSecretRef
    }

/// Every connector reads back its own plaintext through the app's registry,
/// whatever store its reference names: the promise that no interruption
/// leaves a connector without a readable secret.
let private assertAllReadable (app: Fixtures.TestApp) =
    task {
        for slug in slugs do
            let! encoded = referenceOf app slug
            let reference = (SecretRef.Decode encoded) |> Result.defaultWith failwith
            let! read = Fixtures.readSecret app reference
            Assert.True((read = Some(plaintextOf slug)), $"{slug} is not readable through {reference.Store}")
    }

let private assertAllIn (app: Fixtures.TestApp) (storeId: string) =
    task {
        for slug in slugs do
            let! encoded = referenceOf app slug
            Assert.StartsWith(storeId + ":", encoded)
    }

let private movedEvents (app: Fixtures.TestApp) (slug: string) =
    Flows.scalar
        app
        "SELECT count(*) FROM security_events WHERE event_type = 'connector_secret_moved' AND connector_slug = @s"
        [ "@s", slug ]

let private firstWord (line: string) =
    line.Split(' ', StringSplitOptions.RemoveEmptyEntries) |> Array.tryHead |> Option.defaultValue ""

let private reportLines (lines: string list) =
    lines |> List.filter (fun l -> l <> "" && not (l.Contains " connector"))

// ---------------------------------------------------------------------------
// The two directions, with a sign-in after each
// ---------------------------------------------------------------------------

[<Fact>]
let ``three local connectors move to aws and back to local, readable after each, source copies deleted`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app = startWith store
        use _ = app
        do! seedAll app

        let toAws = app.Services.GetRequiredService<SecretStores>()
        let! code, lines = move app toAws false

        Assert.Equal(0, code)
        Assert.Equal<string list>([ "moved"; "moved"; "moved" ], reportLines lines |> List.map firstWord)
        Assert.Contains("3 connectors: 3 moved, 0 already in place, 0 failed.", lines)
        do! assertAllIn app "aws"
        do! assertAllReadable app
        Assert.Equal(3, store.Locators.Length)

        // The copies in the target hold the plaintext of their connector.
        for slug in slugs do
            let! encoded = referenceOf app slug
            let reference = (SecretRef.Decode encoded) |> Result.defaultWith failwith
            Assert.Equal<string list>([ plaintextOf slug ], store.VersionsOf reference.Locator)

        let toLocal = registryActive app store "local"
        let! code, lines = move app toLocal false

        Assert.Equal(0, code)
        Assert.Equal<string list>([ "moved"; "moved"; "moved" ], reportLines lines |> List.map firstWord)
        do! assertAllIn app "local"
        do! assertAllReadable app
        // Source copies deleted.
        Assert.Empty store.Locators

        for slug in slugs do
            let! count = movedEvents app slug
            Assert.Equal("2", count)
    }

[<Fact>]
let ``a connector signs in after being moved to aws and again after being moved back`` () =
    task {
        let store = InMemorySecretStore "aws"
        let stub = create issuer

        let! app =
            Fixtures.createAppWithProviderHandler
                (stub :> HttpMessageHandler)
                (fakeSettings @ awsSettings)
                (Fixtures.withSecretStores [ store ] "aws")

        use _ = app
        stub.Now <- fun () -> app.Clock.GetUtcNow()

        let operator = Browser app.Client
        do! claimInstance app operator
        do! addFakeConnector app "fake-connector-secret-value"
        do! seedLocal app Oidc "home" (Some issuer)
        let! clientId, _ = createClientApp operator

        let signIn () =
            task {
                let browser = Browser app.Client
                let! toPicker = browser.Get(authorizeUrl clientId "openid email profile")

                let! toProvider =
                    browser.Get((toPicker.Headers.Location |> string).Replace("/signin", "/connect/home/start"))

                let authorization = stub.Authorize(Uri(toProvider.Headers.Location |> string))

                let! back =
                    browser.Get(
                        $"/connect/home/callback?code={Uri.EscapeDataString authorization.Code}&state={Uri.EscapeDataString authorization.State}"
                    )

                Assert.Contains("/connect/authorize", back.Headers.Location |> string)
                Assert.Equal(Some(plaintextOf "home"), stub.LastTokenRequest.Value.BasicClientSecret)
            }

        do! signIn ()

        let! code, _ = move app (app.Services.GetRequiredService<SecretStores>()) false
        Assert.Equal(0, code)
        let! encoded = referenceOf app "home"
        Assert.StartsWith("aws:", encoded)
        do! signIn ()

        let! code, _ = move app (registryActive app store "local") false
        Assert.Equal(0, code)
        let! encoded = referenceOf app "home"
        Assert.StartsWith("local:", encoded)
        do! signIn ()
    }

// ---------------------------------------------------------------------------
// Interrupted at any call
// ---------------------------------------------------------------------------

[<Fact>]
let ``a store failing on any one call while moving to aws, then a clean re-run, leaves everything readable once`` () =
    task {
        // How many calls a clean move makes, measured rather than assumed.
        let measured = InMemorySecretStore "aws"
        let! reference = startWith measured
        let total =
            task {
                use app = reference
                do! seedAll app
                // The startup probe is the store's first call; a move's own
                // calls are counted from after it.
                let before = measured.CallCount
                let! _ = move app (app.Services.GetRequiredService<SecretStores>()) false
                return measured.CallCount - before
            }

        let! total = total
        Assert.True(total >= 6, $"a clean move of three connectors made only {total} calls")

        for n in 1..total do
            let store = InMemorySecretStore "aws"
            let! app = startWith store
            use _ = app
            do! seedAll app
            let stores = app.Services.GetRequiredService<SecretStores>()

            store.FailCall(store.CallCount + n, Unreachable)
            let! code, _ = move app stores false

            Assert.True((code = 1), $"call {n}: a failing store must make the exit code 1, got {code}")
            // Nothing is ever left pointing at a secret that is not there.
            do! assertAllReadable app

            let! code, _ = move app stores false
            Assert.True((code = 0), $"call {n}: the clean re-run must succeed, got {code}")
            do! assertAllIn app "aws"
            do! assertAllReadable app
            // A retried write hits the same name: one secret per connector.
            Assert.True((store.Locators.Length = 3), $"call {n}: expected 3 secrets in the target, found {store.Locators.Length}")
    }

[<Fact>]
let ``a store failing on any one call while moving back to local, then a clean re-run, leaves everything readable`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! first = startWith store
        let total =
            task {
                use app = first
                do! seedAll app
                let! _ = move app (app.Services.GetRequiredService<SecretStores>()) false
                let before = store.CallCount
                let! _ = move app (registryActive app store "local") false
                return store.CallCount - before
            }

        let! total = total
        Assert.True(total >= 6, $"a clean move of three connectors made only {total} calls")

        for n in 1..total do
            let store = InMemorySecretStore "aws"
            let! app = startWith store
            use _ = app
            do! seedAll app
            let! _ = move app (app.Services.GetRequiredService<SecretStores>()) false
            let toLocal = registryActive app store "local"

            store.FailCall(store.CallCount + n, Unreachable)
            let! code, _ = move app toLocal false

            // A failed read stops that connector (1); a failed delete only
            // leaves a copy behind (0). Either way nothing is unreadable.
            Assert.True((code = 0 || code = 1), $"call {n}: exit code {code}")
            do! assertAllReadable app

            let! code, _ = move app toLocal false
            Assert.True((code = 0), $"call {n}: the clean re-run must succeed, got {code}")
            do! assertAllIn app "local"
            do! assertAllReadable app
    }

// ---------------------------------------------------------------------------
// The report and the exit code
// ---------------------------------------------------------------------------

[<Fact>]
let ``a row changed between the copy and the switch is skipped and left as it is`` () =
    task {
        let inner = InMemorySecretStore "aws"
        let appRef = ref Unchecked.defaultof<Fixtures.TestApp>
        let armed = ref false

        // While bravo is being copied, the operator replaces its secret.
        let rival (name: SecretName) =
            task {
                if armed.Value && name.Slug = "bravo" then
                    let app = appRef.Value

                    let! _ =
                        ConnectorStore.update
                            app.DbConfig
                            app.Clock
                            "bravo"
                            "bravo"
                            "client-bravo"
                            true
                            None
                            (Some("local:replaced-meanwhile", "ably"))

                    ()
            }

        let! app = startWith (RacingStore(inner, rival))
        use _ = app
        appRef.Value <- app
        do! seedAll app
        armed.Value <- true

        let! code, lines = move app (app.Services.GetRequiredService<SecretStores>()) false

        Assert.Equal(0, code)
        Assert.Equal<string list>([ "moved"; "skipped"; "moved" ], reportLines lines |> List.map firstWord)

        let! bravo = referenceOf app "bravo"
        Assert.Equal("local:replaced-meanwhile", bravo)

        let! moved = movedEvents app "bravo"
        Assert.Equal("0", moved)
        let! alpha = movedEvents app "alpha"
        let! charlie = movedEvents app "charlie"
        Assert.Equal("1", alpha)
        Assert.Equal("1", charlie)
    }

[<Fact>]
let ``a dry run makes no call to any store, changes nothing and says would move`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app = startWith store
        use _ = app
        // The startup probe has already asked the store once.
        let probed = store.CallCount
        do! seedAll app
        let before = [ for slug in slugs -> referenceOf app slug |> fun t -> t.Result ]

        let! code, lines = move app (app.Services.GetRequiredService<SecretStores>()) true

        Assert.Equal(0, code)
        Assert.Equal<string list>([ "would"; "would"; "would" ], reportLines lines |> List.map firstWord)
        Assert.Contains(lines, fun l -> l.StartsWith "would move  alpha  local → aws")
        Assert.Contains("3 connectors: 3 would move, 0 already in place, 0 failed.", lines)
        Assert.Equal(probed, store.CallCount)
        Assert.Equal<string list>(before, [ for slug in slugs -> referenceOf app slug |> fun t -> t.Result ])

        for slug in slugs do
            let! count = movedEvents app slug
            Assert.Equal("0", count)
    }

[<Fact>]
let ``a second run prints only skipped lines and returns 0`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app = startWith store
        use _ = app
        do! seedAll app
        let stores = app.Services.GetRequiredService<SecretStores>()
        let! _ = move app stores false

        let calls = store.CallCount
        let! code, lines = move app stores false

        Assert.Equal(0, code)
        Assert.Equal<string list>([ "skipped"; "skipped"; "skipped" ], reportLines lines |> List.map firstWord)
        Assert.Contains("3 connectors: 0 moved, 3 already in place, 0 failed.", lines)
        Assert.Equal(calls, store.CallCount)

        for slug in slugs do
            let! count = movedEvents app slug
            Assert.Equal("1", count)
    }

[<Fact>]
let ``one failure returns 1, reports it without a locator or value, and the connector keeps its old copy`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app = startWith store
        use _ = app
        do! seedAll app
        let! before = referenceOf app "bravo"

        // The move's calls 1 and 2 are alpha's put and read-back; 3 is bravo's
        // put. Counted from after the startup probe.
        store.FailCall(store.CallCount + 3, AccessDenied)
        let! code, lines = move app (app.Services.GetRequiredService<SecretStores>()) false

        Assert.Equal(1, code)
        Assert.Equal<string list>([ "moved"; "failed"; "moved" ], reportLines lines |> List.map firstWord)

        let failed = lines |> List.find (fun l -> l.StartsWith "failed")
        Assert.Contains("bravo", failed)
        Assert.Contains("local → aws", failed)
        Assert.Contains("1 failed", lines |> List.last)

        let everything = String.concat "\n" lines

        for slug in slugs do
            Assert.DoesNotContain(plaintextOf slug, everything)

        Assert.DoesNotContain("mem/", everything)
        Assert.DoesNotContain("local:CfDJ", everything)

        let! after = referenceOf app "bravo"
        Assert.Equal(before, after)

        let! events =
            Flows.scalar
                app
                "SELECT actor || '|' || outcome || '|' || connector_slug || '|' || detail FROM security_events WHERE event_type = 'secret_store_failed'"
                []

        Assert.Equal("system|failure|bravo|store=aws;op=put;cause=access_denied", events)
    }

[<Fact>]
let ``a source delete that fails prints a warning, returns 0, and the connector is moved`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app = startWith store
        use _ = app
        do! seedAll app
        let! _ = move app (app.Services.GetRequiredService<SecretStores>()) false

        // Moving back: alpha's calls are get (1), delete (2).
        let toLocal = registryActive app store "local"
        store.FailCall(store.CallCount + 2, Unreachable)
        let! code, lines = move app toLocal false

        Assert.Equal(0, code)
        Assert.Equal<string list>([ "warning"; "moved"; "moved" ], reportLines lines |> List.map firstWord)

        let warning = lines |> List.find (fun l -> l.StartsWith "warning")
        // It names what to remove and where, in the manager's rendering.
        Assert.Contains("umbral-alpha-", warning)
        Assert.DoesNotContain(plaintextOf "alpha", warning)

        do! assertAllIn app "local"
        do! assertAllReadable app
        // The leftover is the one copy that stayed behind.
        Assert.Equal(1, store.Locators.Length)

        // There, and back: the failed delete came after the switch.
        let! count = movedEvents app "alpha"
        Assert.Equal("2", count)
    }

// ---------------------------------------------------------------------------
// The lease on a connector's secret (research R8)
// ---------------------------------------------------------------------------

/// Runs `beforePut` before the inner store writes, so a test can act between
/// the move's read of the old value and its write of the copy, or hold an
/// operator's write open.
type private HookedStore(inner: ISecretStore, beforePut: (SecretName -> Task<unit>) ref) =
    interface ISecretStore with
        member _.Id = inner.Id

        member _.Put(name, value, ct) =
            task {
                do! beforePut.Value name
                return! inner.Put(name, value, ct)
            }

        member _.Get(reference, ct) = inner.Get(reference, ct)
        member _.Delete(reference, ct) = inner.Delete(reference, ct)
        member _.Probe ct = inner.Probe ct

let private replaceForm (secret: string) =
    [ "clientId", "client-alpha"
      "clientSecret", secret
      "displayName", "alpha"
      "shared", "true" ]

let private replaceLocation (response: HttpResponseMessage) = response.Headers.Location |> string

let private latestOf (store: InMemorySecretStore) (reference: string) =
    let decoded = SecretRef.Decode reference |> Result.defaultWith failwith
    store.VersionsOf decoded.Locator |> List.tryLast

[<Fact>]
let ``an operator's replace during a move is refused, and once the move is done the new secret is the manager's latest version`` () =
    task {
        let inner = InMemorySecretStore "aws"
        let beforePut = ref (fun (_: SecretName) -> Task.FromResult())
        let! app = startWith (HookedStore(inner, beforePut))
        use _ = app
        do! seedLocal app GitHub "alpha" None
        let operator = Browser app.Client
        do! claimInstance app operator

        // The move has read the old value; now, before it writes, the operator
        // replaces the secret. Without the lease this saved, and the move's
        // write of the OLD value then landed on the same name afterwards.
        let refused = ref ""

        beforePut.Value <-
            fun _ ->
                task {
                    let! response = operator.PostForm("/admin/connectors", "/admin/connectors/alpha", replaceForm "operator-new-secret")
                    refused.Value <- replaceLocation response
                }

        let! code, lines = move app (app.Services.GetRequiredService<SecretStores>()) false
        Assert.Equal(0, code)
        Assert.Equal("moved", firstWord (reportLines lines |> List.head))
        Assert.Contains("connector-secret-busy", refused.Value)

        // Nothing of the refused replace was written; the connector's secret is
        // the one the move copied.
        beforePut.Value <- fun _ -> Task.FromResult()
        let! reference = referenceOf app "alpha"
        Assert.Equal(Some(plaintextOf "alpha"), latestOf inner reference)

        // The operator saves again, and now it is the latest version.
        let! saved = operator.PostForm("/admin/connectors", "/admin/connectors/alpha", replaceForm "operator-new-secret")
        Assert.Contains("connector-saved", replaceLocation saved)
        let! after = referenceOf app "alpha"
        Assert.Equal(reference, after)
        Assert.Equal(Some "operator-new-secret", latestOf inner after)
    }

[<Fact>]
let ``a move that meets an operator's replace in flight skips the connector, and the new secret stays the latest version`` () =
    task {
        let inner = InMemorySecretStore "aws"
        let beforePut = ref (fun (_: SecretName) -> Task.FromResult())
        let! app = startWith (HookedStore(inner, beforePut))
        use _ = app
        do! seedLocal app GitHub "alpha" None
        let operator = Browser app.Client
        do! claimInstance app operator

        // The operator's write is held open inside the store.
        let entered = TaskCompletionSource()
        let release = TaskCompletionSource()

        beforePut.Value <-
            fun _ ->
                task {
                    entered.SetResult()
                    do! release.Task
                }

        let replacing = operator.PostForm("/admin/connectors", "/admin/connectors/alpha", replaceForm "operator-new-secret")
        do! entered.Task

        beforePut.Value <- fun _ -> Task.FromResult()
        let! code, lines = move app (app.Services.GetRequiredService<SecretStores>()) false

        Assert.Equal(0, code)
        let line = reportLines lines |> List.head
        Assert.Equal("skipped", firstWord line)
        Assert.Contains("being edited; run move-secrets again", line)

        release.SetResult()
        let! saved = replacing
        Assert.Contains("connector-saved", replaceLocation saved)

        let! reference = referenceOf app "alpha"
        Assert.StartsWith("aws:", reference)
        Assert.Equal(Some "operator-new-secret", latestOf inner reference)

        // Run again: it is already where it goes, and nothing is overwritten.
        let! _, again = move app (app.Services.GetRequiredService<SecretStores>()) false
        Assert.Equal("skipped", firstWord (reportLines again |> List.head))
        Assert.Equal(Some "operator-new-secret", latestOf inner reference)
    }

[<Fact>]
let ``a move skips a connector whose lease is held, and moves it once the lease is released`` () =
    task {
        let store = InMemorySecretStore "aws"
        let! app = startWith store
        use _ = app
        do! seedAll app

        let! taken = ConnectorStore.tryLease app.DbConfig app.Clock "bravo" "someone-editing" ConnectorStore.leaseLength
        Assert.True taken

        let stores = app.Services.GetRequiredService<SecretStores>()
        let! code, lines = move app stores false

        Assert.Equal(0, code)
        Assert.Equal<string list>([ "moved"; "skipped"; "moved" ], reportLines lines |> List.map firstWord)
        Assert.Contains("being edited; run move-secrets again", lines |> List.find (fun l -> l.Contains "bravo"))
        let! bravo = referenceOf app "bravo"
        Assert.StartsWith("local:", bravo)

        do! ConnectorStore.releaseLease app.DbConfig "bravo" "someone-editing"
        let! code, lines = move app stores false
        Assert.Equal(0, code)
        Assert.Equal<string list>([ "skipped"; "moved"; "skipped" ], reportLines lines |> List.map firstWord)
        do! assertAllIn app "aws"
    }

[<Fact>]
let ``a lease is taken by one holder, released only by that holder, and taken over once it has expired`` () =
    task {
        let! app = startWith (InMemorySecretStore "aws")
        use _ = app
        let take holder = ConnectorStore.tryLease app.DbConfig app.Clock "alpha" holder ConnectorStore.leaseLength

        let! first = take "one"
        let! second = take "two"
        Assert.True first
        Assert.False second

        // Somebody else's release does nothing.
        do! ConnectorStore.releaseLease app.DbConfig "alpha" "two"
        let! stillHeld = take "two"
        Assert.False stillHeld

        // Past its length the lease is the next taker's.
        app.Clock.Advance(TimeSpan.FromMinutes 2. + TimeSpan.FromSeconds 1.)
        let! takenOver = take "two"
        Assert.True takenOver

        // The first holder's late release must not remove the new holder's lease.
        do! ConnectorStore.releaseLease app.DbConfig "alpha" "one"
        let! stillTwos = take "three"
        Assert.False stillTwos

        do! ConnectorStore.releaseLease app.DbConfig "alpha" "two"
        let! free = take "three"
        Assert.True free
    }

[<Fact>]
let ``a move deletes the copy it wrote when the connector was deleted while it was copying`` () =
    task {
        let inner = InMemorySecretStore "aws"
        let appRef = ref Unchecked.defaultof<Fixtures.TestApp>

        // The row is deleted straight from the table, as a delete that did not
        // honour the lease would have.
        let rival (name: SecretName) =
            task {
                if name.Slug = "alpha" then
                    let! _ = ConnectorStore.delete appRef.Value.DbConfig "alpha"
                    ()
            }

        let! app = startWith (RacingStore(inner, rival))
        use _ = app
        appRef.Value <- app
        do! seedLocal app GitHub "alpha" None

        let! code, lines = move app (app.Services.GetRequiredService<SecretStores>()) false

        Assert.Equal(0, code)
        Assert.Equal("skipped", firstWord (reportLines lines |> List.head))
        // The copy did not outlive the connector.
        Assert.Empty inner.Locators
        let! moved = movedEvents app "alpha"
        Assert.Equal("0", moved)
    }
