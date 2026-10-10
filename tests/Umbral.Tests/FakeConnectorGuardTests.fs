module Umbral.Tests.FakeConnectorGuardTests

open System
open System.IO
open System.Net
open System.Threading.Tasks
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure

/// Put a fake connector straight into the database, as a configuration file or
/// a careless operator could.
let private seedFakeConnector (config: Database.DbConfig) (clock: TimeProvider) =
    ConnectorStore.save
        config
        clock
        { Slug = "fake"
          Kind = ProviderKind.Fake
          DisplayName = "Fake"
          ClientId = "fake-client"
          ClientSecretRef = "local:unused"
          ClientSecretHint = "none"
          ExtraScopes = []
          IsEnabled = true
          IsShared = true
          CreatedUtc = clock.GetUtcNow()
          UpdatedUtc = clock.GetUtcNow() }
        None

[<Fact>]
let ``an instance REFUSES to start when a fake connector exists without the gate`` () =
    task {
        let dataDirectory =
            Path.Combine(Path.GetTempPath(), "umbral-fakeguard-" + Guid.NewGuid().ToString "N")

        let settings = [ "UMBRAL:DATA_DIRECTORY", dataDirectory ]

        try
            // Start once with the gate open to get a fake connector into the
            // database, exactly as a development instance would have.
            let! permitted =
                Fixtures.createAppWith
                    (settings
                     @ [ "ASPNETCORE_ENVIRONMENT", "Development"
                         "UMBRAL:ALLOW_FAKE_CONNECTOR", "true" ])
                    ignore

            do! seedFakeConnector permitted.DbConfig TimeProvider.System
            do! (permitted :> IAsyncDisposable).DisposeAsync()

            // Now the same data directory, with the gate shut — a copied
            // volume, a promoted container, a forgotten environment variable.
            // Continuing would serve a live authentication bypass, so the only
            // safe answer is to refuse.
            let! error =
                Assert.ThrowsAnyAsync<exn>(fun () ->
                    task {
                        let! app = Fixtures.createAppWith settings ignore
                        do! (app :> IAsyncDisposable).DisposeAsync()
                    }
                    :> Task)

            Assert.Contains("fake", error.Message, StringComparison.OrdinalIgnoreCase)
            // The message has to say what to do, not merely that something is wrong.
            Assert.Contains("UMBRAL__ALLOW_FAKE_CONNECTOR", error.Message)
        finally
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
            if Directory.Exists dataDirectory then Directory.Delete(dataDirectory, true)
    }

[<Fact>]
let ``the fake provider page does not exist unless the gate is open`` () =
    task {
        // Default fixture: no ALLOW_FAKE_CONNECTOR. A 404 rather than a
        // redirect or an explanation — on an instance where this is not
        // allowed, the route should not appear to exist.
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/dev/fake-provider/authorize"
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)
    }

[<Fact>]
let ``both conditions are required, not either`` () =
    task {
        // The environment alone is not enough...
        use! developmentOnly =
            Fixtures.createAppWith [ "ASPNETCORE_ENVIRONMENT", "Development" ] ignore

        let! a = developmentOnly.Client.GetAsync "/dev/fake-provider/authorize"
        Assert.Equal(HttpStatusCode.NotFound, a.StatusCode)

        // ...and neither is the flag. Production with the flag set is the
        // dangerous combination, and it is the one most likely to happen by
        // copying an environment file.
        use! flagOnly =
            Fixtures.createAppWith
                [ "ASPNETCORE_ENVIRONMENT", "Production"
                  "UMBRAL:ALLOW_FAKE_CONNECTOR", "true" ]
                ignore

        let! b = flagOnly.Client.GetAsync "/dev/fake-provider/authorize"
        Assert.Equal(HttpStatusCode.NotFound, b.StatusCode)
    }

[<Fact>]
let ``an unset environment is treated as not-development`` () =
    task {
        // The fail-open trap: "not Production" would read as allowed whenever
        // ASPNETCORE_ENVIRONMENT is absent, which is common in a container.
        use! app = Fixtures.createAppWith [ "UMBRAL:ALLOW_FAKE_CONNECTOR", "true" ] ignore
        let! response = app.Client.GetAsync "/dev/fake-provider/authorize"
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)
    }
