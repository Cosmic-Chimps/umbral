/// Hosting the real application on a TestServer.
///
/// Every integration test goes through `Program.buildApp`, so no production
/// code knows it is under test: the customisation only points the app at a temp
/// data directory and injects a clock. If a test needs a seam that does not
/// exist, the right fix is a seam in the product, not a mock here.
module Umbral.Tests.Infrastructure.Fixtures

open System
open System.IO
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
open Microsoft.Extensions.Time.Testing
open Umbral.Infrastructure
open Umbral.Infrastructure.SecretStores.Registry

[<Literal>]
let TestOrigin = "https://umbral.test"

/// An app, its client, and the temp directory both live in. Disposing removes
/// the directory, so a run leaves nothing behind — unless the directory was
/// handed in (`ownsDirectory = false`), as a restart test does so that the
/// second instance can start on what the first left.
type TestApp(app: WebApplication, client: HttpClient, dataDirectory: string, clock: FakeTimeProvider, ownsDirectory: bool) =
    member _.App = app
    member _.Client = client
    member _.DataDirectory = dataDirectory
    member _.Clock = clock
    member _.Services = app.Services

    member _.DbConfig =
        app.Services.GetRequiredService<Umbral.Infrastructure.Database.DbConfig>()

    interface IAsyncDisposable with
        member _.DisposeAsync() =
            task {
                client.Dispose()
                do! app.DisposeAsync()

                // Pooling holds the SQLite file open; without this the delete
                // fails and temp directories accumulate across a run.
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

                if ownsDirectory then
                    try
                        if Directory.Exists dataDirectory then
                            Directory.Delete(dataDirectory, true)
                    with _ ->
                        ()
            }
            |> ValueTask

let private start
    (dataDirectory: string)
    (ownsDirectory: bool)
    (settings: (string * string) list)
    (customize: WebApplicationBuilder -> unit)
    : Task<TestApp> =
    task {
        Directory.CreateDirectory dataDirectory |> ignore

        // Starts at the real now, not a hardcoded date. It is still a fake, so
        // a test can advance it to drive an expiry — but a clock that claims to
        // be months before the certificates generated alongside it makes
        // OpenIddict reject them as not-yet-valid, and the error it raises
        // ("at least one of the registered certificates must be valid") names
        // neither the clock nor the test.
        let clock = FakeTimeProvider(DateTimeOffset.UtcNow)

        let app =
            Umbral.Program.buildApp Array.empty (fun builder ->
                builder.WebHost.UseTestServer() |> ignore

                builder.Configuration.AddInMemoryCollection(
                    dict (
                        [ "UMBRAL:PUBLIC_ORIGIN", TestOrigin
                          "UMBRAL:DATA_DIRECTORY", dataDirectory ]
                        @ settings
                    )
                )
                |> ignore

                // Registered before configureServices runs, so its
                // TryAddSingleton for the system clock does not win.
                builder.Services.TryAddSingleton<TimeProvider>(clock :> TimeProvider)

                customize builder)

        do! Umbral.Program.initialize app
        do! app.StartAsync()

        let client = app.GetTestClient()

        // TestServer takes the request scheme from the URI, and OpenIddict
        // refuses plain http by default. Pointing the client at the configured
        // public origin makes the test speak https, as a deployment does,
        // rather than needing the insecure-http escape hatch switched on.
        client.BaseAddress <- Uri TestOrigin

        return new TestApp(app, client, dataDirectory, clock, ownsDirectory)
    }

let private newTempDirectory () =
    Path.Combine(Path.GetTempPath(), "umbral-test-" + Guid.NewGuid().ToString "N")

/// Start the real app against a fresh data directory.
///
/// No Testcontainers: Umbral's database is a file, so isolation is a temp
/// directory rather than a container. That keeps the suite fast and removes
/// Docker from the list of things a contributor needs.
let createAppWith (settings: (string * string) list) (customize: WebApplicationBuilder -> unit) : Task<TestApp> =
    start (newTempDirectory ()) true settings customize

/// Start the real app on a directory that already exists, and leave it behind on
/// dispose. For restarts (SC-005): dispose the first instance, start another
/// here, and whatever survived is what a real restart would have kept. The
/// caller owns the directory and deletes it when the test ends.
let createAppIn (dataDirectory: string) (settings: (string * string) list) (customize: WebApplicationBuilder -> unit) =
    start dataDirectory false settings customize

/// A fresh temp directory path for `createAppIn`.
let freshDataDirectory () = newTempDirectory ()

let createApp () = createAppWith [] ignore

/// Hands the provider client a handler the factory must not dispose: the
/// factory rebuilds its handler chain every couple of minutes and disposes the
/// old one, and the test still owns the stub.
type private Borrowed(inner: HttpMessageHandler) =
    inherit HttpMessageHandler()

    let invoker = new HttpMessageInvoker(inner, false)

    override _.SendAsync(request, cancellationToken) = invoker.SendAsync(request, cancellationToken)

/// Start the real app with `handler` behind every upstream provider call.
///
/// Goes through the product's own seam (`ProviderAdapters.PrimaryHandler`, specs/002
/// research R9), registered before `configureServices` runs so its `TryAddSingleton`
/// leaves it alone. The adapters, the discovery cache and the admin screens are the
/// real ones; only the network is replaced.
let createAppWithProviderHandler
    (handler: HttpMessageHandler)
    (settings: (string * string) list)
    (customize: WebApplicationBuilder -> unit)
    : Task<TestApp> =
    createAppWith settings (fun builder ->
        builder.Services.AddSingleton<Umbral.Infrastructure.ProviderAdapters.PrimaryHandler>(
            Umbral.Infrastructure.ProviderAdapters.PrimaryHandler(fun () -> new Borrowed(handler) :> HttpMessageHandler))
        |> ignore

        customize builder)

/// Start the real app with `handler` behind every call to a Fediverse server.
///
/// The same seam as above for the Fediverse connector's own client
/// (`Fediverse.PrimaryHandler`, specs/010 research R6): the real client code, the
/// server store and the screens, with only the network replaced. The connect
/// guard lives in the handler the stub replaces, so it is not in the way here;
/// FediverseClientTests drives the real one.
let createAppWithFediverseHandlerAndResolver
    (handler: HttpMessageHandler)
    (resolver: Umbral.Infrastructure.Providers.Fediverse.HostResolver)
    (settings: (string * string) list)
    (customize: WebApplicationBuilder -> unit)
    : Task<TestApp> =
    createAppWith settings (fun builder ->
        builder.Services.AddSingleton<Umbral.Infrastructure.Providers.Fediverse.PrimaryHandler>(
            Umbral.Infrastructure.Providers.Fediverse.PrimaryHandler(fun () -> new Borrowed(handler) :> HttpMessageHandler))
        |> ignore

        builder.Services.AddSingleton<Umbral.Infrastructure.Providers.Fediverse.HostResolver>(resolver) |> ignore
        customize builder)

/// As above, with every name resolving to one public address, so the address
/// pre-check passes for the stub's made-up hosts without the development setting.
let createAppWithFediverseHandler
    (handler: HttpMessageHandler)
    (settings: (string * string) list)
    (customize: WebApplicationBuilder -> unit)
    : Task<TestApp> =
    let resolver =
        Umbral.Infrastructure.Providers.Fediverse.HostResolver(fun _ _ ->
            Task.FromResult [| System.Net.IPAddress.Parse "93.184.215.14" |])

    createAppWithFediverseHandlerAndResolver handler resolver settings customize

/// Replaces the registry with one holding `stores` (each under its own `Id`)
/// and writing new secrets to the store named `activeId`, for `customize`.
///
/// The `local` store is still the real one, built from the app's key ring, so a
/// test can start on `local` and an external store at once. `bound` is the
/// time limit on a call to a store; a test of a hung store passes a short one
/// rather than waiting out the product's 10 seconds.
///
/// It works because `customize` runs before `configureServices`, whose
/// `TryAddSingleton` then leaves this registration alone.
let withSecretStoresWithin (bound: TimeSpan) (stores: Secrets.ISecretStore list) (activeId: string) (builder: WebApplicationBuilder) =
    builder.Services.TryAddSingleton<SecretStores>(fun sp ->
        let local = Secrets.LocalSecretStore(sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>())

        let others =
            stores
            |> List.map (fun store -> store.Id, lazy (Ok store))
            |> Map.ofList

        // The app's own clock and cache settings, so a test drives expiry with
        // its `FakeTimeProvider` and sets the lifetimes the way a deployment does.
        let settings = sp.GetRequiredService<Umbral.Infrastructure.SecretStores.Settings.SecretStoreSettings>()

        SecretStores(local, activeId, others, bound, sp.GetRequiredService<TimeProvider>(), settings.CacheLifetime, settings.GracePeriod))

/// `withSecretStoresWithin` with the product's own call bound.
let withSecretStores (stores: Secrets.ISecretStore list) (activeId: string) (builder: WebApplicationBuilder) =
    withSecretStoresWithin callBound stores activeId builder

/// Writes a secret the way the product's handlers do, through the registry's
/// active store, and returns the reference to put in a connector row. For tests
/// that seed a connector straight into the database. A refusal fails the test:
/// seeding is not what is being tested.
let putSecret (app: TestApp) (slug: string) (plaintext: string) : Task<Secrets.SecretRef> =
    task {
        let secrets = app.Services.GetRequiredService<SecretStores>()
        let name = Secrets.SecretName.derive "umbral" slug (app.Clock.GetUtcNow())

        match! secrets.Active.Put(name, plaintext, Threading.CancellationToken.None) with
        | Ok reference -> return reference
        | Error failure -> return failwith $"seeding a secret for '{slug}' failed: {failure.Cause}"
    }

/// Reads a secret back the way the sign-in callback does. `None` when the store
/// could not give it, whatever the reason.
let readSecret (app: TestApp) (reference: Secrets.SecretRef) : Task<string option> =
    task {
        let secrets = app.Services.GetRequiredService<SecretStores>()

        match! secrets.Read(reference, Threading.CancellationToken.None) with
        | Ok plaintext -> return Some plaintext
        | Error _ -> return None
    }
