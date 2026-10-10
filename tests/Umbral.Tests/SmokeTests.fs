/// The app boots, and says so (FR-032).
module Umbral.Tests.SmokeTests

open System.IO
open System.Net
open Xunit
open Umbral.Tests.Infrastructure

[<Fact>]
let ``the app boots on a TestServer and /healthz returns 200`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/healthz"
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    }

[<Fact>]
let ``/healthz needs no sign-in and says nothing about the instance`` () =
    task {
        use! app = Fixtures.createApp ()
        let! body = app.Client.GetStringAsync "/healthz"

        // One word. No origin, version, data path or connector count: the
        // endpoint is open to anything that can reach the port.
        Assert.Equal("ok", body)
        Assert.DoesNotContain(Fixtures.TestOrigin, body)
        Assert.DoesNotContain(app.DataDirectory, body)
    }

[<Fact>]
let ``/healthz is not rate limited`` () =
    task {
        use! app = Fixtures.createApp ()

        // More than the tightest per-source limit (30 a minute, RateLimits.fs):
        // a probe every few seconds must never be what trips a limiter.
        for _ in 1..60 do
            let! response = app.Client.GetAsync "/healthz"
            Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    }

[<Fact>]
let ``/healthz reports 503 when the database cannot be opened`` () =
    task {
        use! app = Fixtures.createApp ()

        // Replace the database file with a directory: the process is up, but it
        // cannot serve a single login, which is exactly what a probe is for.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
        let path = Path.Combine(app.DataDirectory, "umbral.db")
        Assert.True(File.Exists path, "expected the database where the test looks for it")

        for suffix in [ ""; "-wal"; "-shm" ] do
            if File.Exists(path + suffix) then
                File.Delete(path + suffix)

        Directory.CreateDirectory path |> ignore

        let! response = app.Client.GetAsync "/healthz"
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode)

        let! body = response.Content.ReadAsStringAsync()
        Assert.DoesNotContain(app.DataDirectory, body)
    }
