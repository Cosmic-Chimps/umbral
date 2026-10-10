/// The app's own subcommands, run as real processes.
///
/// `main` decides what a command line means before anything is built, and that
/// decision (no second web host, no first-run banner) cannot be observed from a
/// TestServer. So these start `Umbral.dll` the way a container does, against a
/// temp data directory, and read what it prints.
module Umbral.Tests.SubcommandTests

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Text.RegularExpressions
open System.Threading.Tasks
open Xunit
open Umbral.Infrastructure

let private nonNull (value: 'a | null) : 'a =
    match value with
    | null -> failwith "expected a value"
    | v -> v

let private umbralDll = Path.Combine(AppContext.BaseDirectory, "Umbral.dll")

let private startProcess (dataDirectory: string) (port: int) (args: string list) =
    let info = ProcessStartInfo("dotnet")
    info.ArgumentList.Add umbralDll

    for a in args do
        info.ArgumentList.Add a

    info.RedirectStandardOutput <- true
    info.RedirectStandardError <- true
    // A clean slate: nothing the developer's shell exports may change what runs.
    for name in [ "ASPNETCORE_ENVIRONMENT"; "DOTNET_ENVIRONMENT"; "UMBRAL__CONFIG_FILE" ] do
        info.Environment.Remove name |> ignore

    info.Environment["UMBRAL__PUBLIC_ORIGIN"] <- "https://umbral.test"
    info.Environment["UMBRAL__DATA_DIRECTORY"] <- dataDirectory
    info.Environment["ASPNETCORE_HTTP_PORTS"] <- string port
    Process.Start info |> nonNull

let private run (dataDirectory: string) (port: int) (args: string list) =
    task {
        use proc = startProcess dataDirectory port args
        let out = proc.StandardOutput.ReadToEndAsync()
        let err = proc.StandardError.ReadToEndAsync()

        if not (proc.WaitForExit(TimeSpan.FromSeconds 90.0)) then
            proc.Kill true
            failwith (sprintf "`Umbral %s` did not exit within 90 seconds" (String.concat " " args))

        let! stdout = out
        let! stderr = err
        return proc.ExitCode, stdout, stderr
    }

let private freePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

let private withDataDirectory (body: string -> Task) =
    task {
        let dir = Path.Combine(Path.GetTempPath(), "umbral-sub-" + Guid.NewGuid().ToString "N")
        Directory.CreateDirectory dir |> ignore

        try
            do! body dir
        finally
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            try
                Directory.Delete(dir, true)
            with _ ->
                ()
    }

let private dbConfig (dir: string) : Database.DbConfig =
    { DataSource = Path.Combine(dir, "umbral.db") }

let private printedCredential (stdout: string) =
    let m = Regex.Match(stdout, @"New credential\s+(\S+)")
    Assert.True(m.Success, $"no credential in the output:\n{stdout}")
    m.Groups[1].Value

[<Fact>]
let ``reset-credential on an unclaimed instance claims it, says so, and prints no setup banner`` () =
    withDataDirectory (fun dir ->
        task {
            let! code, stdout, stderr = run dir (freePort ()) [ "reset-credential" ]

            Assert.True((code = 0), $"exit {code}\n{stdout}\n{stderr}")
            Assert.Contains("had no operator yet", stdout)
            // The first-run banner and its token have no business above a
            // credential that has just made the token irrelevant.
            Assert.DoesNotContain("not configured yet", stdout)
            Assert.DoesNotContain("Token", stdout)
            Assert.DoesNotContain("/admin/setup", stdout)

            let credential = printedCredential stdout
            Assert.True(AdminAuth.isConfigured (dbConfig dir) |> fun t -> t.Result)
            Assert.True(AdminAuth.verify (dbConfig dir) credential |> fun t -> t.Result)
        })

[<Fact>]
let ``reset-credential on a claimed instance replaces the credential and prints no setup banner`` () =
    withDataDirectory (fun dir ->
        task {
            let! _, firstOut, _ = run dir (freePort ()) [ "reset-credential" ]
            let first = printedCredential firstOut

            let! code, stdout, stderr = run dir (freePort ()) [ "reset-credential" ]

            Assert.True((code = 0), $"exit {code}\n{stdout}\n{stderr}")
            Assert.Contains("has been replaced", stdout)
            Assert.DoesNotContain("not configured yet", stdout)
            Assert.DoesNotContain("had no operator yet", stdout)

            let second = printedCredential stdout
            Assert.NotEqual<string>(first, second)
            Assert.False(AdminAuth.verify (dbConfig dir) first |> fun t -> t.Result)
            Assert.True(AdminAuth.verify (dbConfig dir) second |> fun t -> t.Result)
        })

[<Fact>]
let ``healthcheck exits 1 when nothing answers, and builds nothing`` () =
    withDataDirectory (fun dir ->
        task {
            let! code, stdout, _ = run dir (freePort ()) [ "healthcheck" ]

            Assert.Equal(1, code)
            Assert.DoesNotContain("not configured yet", stdout)
            // It never composed the app, so it never touched the data directory.
            Assert.Empty(Directory.GetFileSystemEntries dir)
        })

[<Fact>]
let ``healthcheck exits 0 against a running instance`` () =
    withDataDirectory (fun dir ->
        task {
            let port = freePort ()
            use server = startProcess dir port []

            try
                // Wait for the listener, not for a fixed time.
                use http = new HttpClient()
                let mutable up = false
                let deadline = DateTime.UtcNow.AddSeconds 60.0

                while not up && DateTime.UtcNow < deadline do
                    try
                        let! response = http.GetAsync $"http://localhost:{port}/healthz"
                        up <- response.IsSuccessStatusCode
                    with _ ->
                        do! Task.Delay 250

                Assert.True(up, "the server did not come up")

                let! code, _, _ = run dir port [ "healthcheck" ]
                Assert.Equal(0, code)
            finally
                server.Kill true
                server.WaitForExit()
        })
