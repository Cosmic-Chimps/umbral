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

[<Literal>]
let TestOrigin = "https://umbral.test"

/// An app, its client, and the temp directory both live in. Disposing removes
/// the directory, so a run leaves nothing behind.
type TestApp(app: WebApplication, client: HttpClient, dataDirectory: string, clock: FakeTimeProvider) =
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

                try
                    if Directory.Exists dataDirectory then
                        Directory.Delete(dataDirectory, true)
                with _ ->
                    ()
            }
            |> ValueTask

/// Start the real app against a fresh data directory.
///
/// No Testcontainers: Umbral's database is a file, so isolation is a temp
/// directory rather than a container. That keeps the suite fast and removes
/// Docker from the list of things a contributor needs.
let createAppWith (settings: (string * string) list) (customize: WebApplicationBuilder -> unit) : Task<TestApp> =
    task {
        let dataDirectory =
            Path.Combine(Path.GetTempPath(), "umbral-test-" + Guid.NewGuid().ToString "N")

        Directory.CreateDirectory dataDirectory |> ignore

        let clock = FakeTimeProvider(DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero))

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

        return new TestApp(app, app.GetTestClient(), dataDirectory, clock)
    }

let createApp () = createAppWith [] ignore
