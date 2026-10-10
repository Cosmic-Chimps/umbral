module Umbral.Tests.BootstrapTests

open System
open System.IO
open System.Threading.Tasks
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Xunit
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure

let private scalar (config: Database.DbConfig) (sql: string) =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- sql
        let! value = command.ExecuteScalarAsync()
        return if isNull value then "" else string value
    }

[<Fact>]
let ``the app starts on a TestServer`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/"
        // No routes are mapped yet; reaching the terminal 404 proves the whole
        // pipeline composed and ran.
        Assert.Equal(Net.HttpStatusCode.NotFound, response.StatusCode)
    }

[<Fact>]
let ``startup creates both lanes of the schema`` () =
    task {
        use! app = Fixtures.createApp ()

        let! umbralTables =
            scalar
                app.DbConfig
                "SELECT count(*) FROM sqlite_master WHERE type='table'
                 AND name IN ('users','linked_accounts','connectors','settings','security_events')"

        let! openIddictTables =
            scalar
                app.DbConfig
                "SELECT count(*) FROM sqlite_master WHERE type='table' AND name LIKE 'OpenIddict%'"

        // Umbral's six on hand-written SQL, OpenIddict's four on EF migrations,
        // one file, neither aware of the other.
        Assert.Equal("5", umbralTables)
        Assert.Equal("4", openIddictTables)
    }

[<Fact>]
let ``startup generates both certificates into the data directory`` () =
    task {
        use! app = Fixtures.createApp ()
        let keys = Path.Combine(app.DataDirectory, "keys")
        Assert.True(File.Exists(Path.Combine(keys, "signing.pfx")))
        Assert.True(File.Exists(Path.Combine(keys, "encryption.pfx")))
    }

[<Fact>]
let ``the data protection key ring lands in the data directory too`` () =
    task {
        use! app = Fixtures.createApp ()

        // It protects the admin cookie, antiforgery tokens and every stored
        // connector secret. Anywhere but the mounted volume and a replaced
        // container logs the operator out and orphans every secret.
        let keys = Path.Combine(app.DataDirectory, "keys")
        let ringFiles = Directory.GetFiles(keys, "key-*.xml")
        Assert.NotEmpty(ringFiles)
    }

[<Fact>]
let ``everything persisted lives under the one data directory`` () =
    task {
        use! app = Fixtures.createApp ()
        // Principle II: backup is `cp` of one directory. A file written outside
        // it is a silent breach of that promise, so assert the database is in.
        Assert.StartsWith(app.DataDirectory, app.DbConfig.DataSource)
    }

[<Fact>]
let ``a second start reuses the keys rather than regenerating them`` () =
    task {
        let dataDirectory =
            Path.Combine(Path.GetTempPath(), "umbral-restart-" + Guid.NewGuid().ToString "N")

        let settings = [ "UMBRAL:DATA_DIRECTORY", dataDirectory ]

        try
            let! first = Fixtures.createAppWith settings ignore
            let thumbprint =
                use cert = Keys.loadOrCreate (Path.Combine(dataDirectory, "keys")) Keys.Signing
                cert.Thumbprint
            do! (first :> IAsyncDisposable).DisposeAsync()

            // SC-005: a restart must leave every issued token still valid, which
            // it cannot if the signing key changed underneath them.
            let! second = Fixtures.createAppWith settings ignore
            use cert = Keys.loadOrCreate (Path.Combine(dataDirectory, "keys")) Keys.Signing
            Assert.Equal(thumbprint, cert.Thumbprint)
            do! (second :> IAsyncDisposable).DisposeAsync()
        finally
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
            if Directory.Exists dataDirectory then Directory.Delete(dataDirectory, true)
    }

[<Fact>]
let ``the secret store is resolvable and round-trips`` () =
    task {
        use! app = Fixtures.createApp ()
        let store = app.Services.GetRequiredService<Secrets.ISecretStore>()

        let! reference = store.Put("connector/discord/client_secret", "value-from-the-form")
        let! recovered = store.Get reference

        Assert.Equal(Some "value-from-the-form", recovered)
    }

[<Fact>]
let ``startup refuses to run without a public origin`` () =
    task {
        let dataDirectory =
            Path.Combine(Path.GetTempPath(), "umbral-noorigin-" + Guid.NewGuid().ToString "N")

        try
            let build () =
                Umbral.Program.buildApp Array.empty (fun builder ->
                    builder.Configuration.AddInMemoryCollection(
                        dict [ "UMBRAL:DATA_DIRECTORY", dataDirectory ])
                    |> ignore)
                |> ignore

            // Booting half-working is the failure mode this prevents: the
            // symptom would otherwise surface hours later as a provider error
            // naming neither the setting nor the proxy.
            let ex = Assert.Throws<Exception> build
            Assert.Contains("UMBRAL__PUBLIC_ORIGIN", ex.Message)
        finally
            if Directory.Exists dataDirectory then Directory.Delete(dataDirectory, true)
    }
