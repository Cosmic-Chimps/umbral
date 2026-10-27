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

let private startProcessWith (environment: (string * string) list) (dataDirectory: string) (port: int) (args: string list) =
    let info = ProcessStartInfo("dotnet")
    info.ArgumentList.Add umbralDll

    for a in args do
        info.ArgumentList.Add a

    info.RedirectStandardOutput <- true
    info.RedirectStandardError <- true
    // A clean slate: nothing the developer's shell exports may change what runs.
    for name in [ "ASPNETCORE_ENVIRONMENT"; "DOTNET_ENVIRONMENT" ] do
        info.Environment.Remove name |> ignore

    info.Environment["UMBRAL__PUBLIC_ORIGIN"] <- "https://umbral.test"
    info.Environment["UMBRAL__DATA_DIRECTORY"] <- dataDirectory
    info.Environment["ASPNETCORE_HTTP_PORTS"] <- string port

    for name, value in environment do
        info.Environment[name] <- value

    Process.Start info |> nonNull

let private startProcess = startProcessWith []

let private runWith (environment: (string * string) list) (dataDirectory: string) (port: int) (args: string list) =
    task {
        use proc = startProcessWith environment dataDirectory port args
        let out = proc.StandardOutput.ReadToEndAsync()
        let err = proc.StandardError.ReadToEndAsync()

        if not (proc.WaitForExit(TimeSpan.FromSeconds 90.0)) then
            proc.Kill true
            failwith (sprintf "`Umbral %s` did not exit within 90 seconds" (String.concat " " args))

        let! stdout = out
        let! stderr = err
        return proc.ExitCode, stdout, stderr
    }

let private run = runWith []

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
    let m = Regex.Match(stdout, @"New password\s+(\S+)")
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

let private scalarOf (dir: string) (statement: string) =
    task {
        use! connection = Database.openConnection (dbConfig dir)
        use command = connection.CreateCommand()
        command.CommandText <- statement
        let! value = command.ExecuteScalarAsync()
        return if isNull value then "" else string value
    }

let private stamp (dir: string) =
    scalarOf dir "SELECT value FROM settings WHERE key = 'operator_session_stamp'"

let private eventDetails (dir: string) (eventType: string) =
    scalarOf
        dir
        $"SELECT COALESCE(GROUP_CONCAT(actor || ':' || outcome || ':' || COALESCE(detail, ''), ' '), '') FROM security_events WHERE event_type = '{eventType}'"

/// A claimed instance with two operator passkeys and password sign-in off.
let private seedSwitchedOff (dir: string) =
    task {
        let! code, _, _ = run dir (freePort ()) [ "reset-credential" ]
        Assert.Equal(0, code)
        let config = dbConfig dir
        let clock = TimeProvider.System

        for index, name in List.indexed [ "Laptop"; "Phone" ] do
            let! outcome =
                OperatorPasskeys.add
                    config
                    clock
                    { Id = OperatorPasskeys.newId ()
                      CredentialId = [| byte index; 1uy; 2uy |]
                      PublicKey = [| 9uy; byte index |]
                      SignCount = 0u
                      Name = name
                      Transports = []
                      CreatedUtc = clock.GetUtcNow()
                      LastUsedUtc = None }

            Assert.Equal(OperatorPasskeys.Added, outcome)

        let! off = OperatorPasskeys.turnPasswordSignInOff config clock
        Assert.Equal(OperatorPasskeys.SwitchedOff, off)
    }

[<Fact>]
let ``reset-credential switches password sign-in on, keeps the passkeys and ends every session`` () =
    withDataDirectory (fun dir ->
        task {
            do! seedSwitchedOff dir
            let! before = stamp dir

            let! code, stdout, stderr = run dir (freePort ()) [ "reset-credential" ]
            Assert.True((code = 0), $"exit {code}\n{stdout}\n{stderr}")

            Assert.Contains("The operator password has been replaced.", stdout)
            Assert.Contains("Password sign-in is on.", stdout)
            Assert.Contains("2 operator passkeys are kept. Add --forget-passkeys to remove them.", stdout)

            let password = printedCredential stdout
            let! on = OperatorPasskeys.passwordSignInEnabled (dbConfig dir)
            Assert.True on
            Assert.True(AdminAuth.verify (dbConfig dir) password |> fun t -> t.Result)
            let! kept = OperatorPasskeys.count (dbConfig dir)
            Assert.Equal(2, kept)

            // A session cookie carries the stamp, so a moved stamp rejects it.
            let! after = stamp dir
            Assert.NotEqual<string>(before, after)

            let! switched = eventDetails dir "operator_password_signin_changed"
            Assert.Equal("system:success:offline_reset", switched)
            let! removed = eventDetails dir "operator_passkey_removed"
            Assert.Equal("", removed)
        })

[<Fact>]
let ``reset-credential with --forget-passkeys removes every operator passkey`` () =
    withDataDirectory (fun dir ->
        task {
            do! seedSwitchedOff dir

            let! code, stdout, stderr = run dir (freePort ()) [ "reset-credential"; "--forget-passkeys" ]
            Assert.True((code = 0), $"exit {code}\n{stdout}\n{stderr}")

            Assert.Contains("Password sign-in is on.", stdout)
            Assert.Contains("2 operator passkeys were removed.", stdout)
            Assert.DoesNotContain("are kept", stdout)

            let! left = OperatorPasskeys.count (dbConfig dir)
            Assert.Equal(0, left)
            let! on = OperatorPasskeys.passwordSignInEnabled (dbConfig dir)
            Assert.True on

            let! removed = eventDetails dir "operator_passkey_removed"
            Assert.Equal("system:success:offline_reset count=2", removed)
        })

[<Fact>]
let ``reset-credential says nothing of passkeys when there are none, and claiming prints no switch lines`` () =
    withDataDirectory (fun dir ->
        task {
            let! _, claimed, _ = run dir (freePort ()) [ "reset-credential" ]
            Assert.Contains("had no operator yet", claimed)
            Assert.DoesNotContain("Password sign-in", claimed)
            Assert.DoesNotContain("passkey", claimed)

            let! _, replaced, _ = run dir (freePort ()) [ "reset-credential"; "--forget-passkeys" ]
            Assert.Contains("Password sign-in is on.", replaced)
            Assert.DoesNotContain("passkey", replaced)
            let! removed = eventDetails dir "operator_passkey_removed"
            Assert.Equal("", removed)
            let! switched = eventDetails dir "operator_password_signin_changed"
            Assert.Equal("", switched)
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

[<Fact>]
let ``move-secrets --dry-run with only environment variables exits 0 and prints the summary`` () =
    withDataDirectory (fun dir ->
        task {
            let! code, stdout, stderr = run dir (freePort ()) [ "move-secrets"; "--dry-run" ]

            Assert.True((code = 0), $"exit {code}\n{stdout}\n{stderr}")
            Assert.Contains("0 connectors: 0 would move, 0 already in place, 0 failed.", stdout)
            // Like reset-credential: no web host, no first-run banner.
            Assert.DoesNotContain("not configured yet", stdout)
        })

[<Fact>]
let ``move-secrets with an unknown store exits non-zero with the settings message before any connector line`` () =
    withDataDirectory (fun dir ->
        task {
            let! code, stdout, stderr =
                runWith [ "UMBRAL__SECRET_STORE", "nonsense" ] dir (freePort ()) [ "move-secrets"; "--dry-run" ]

            Assert.NotEqual(0, code)
            Assert.Contains("UMBRAL__SECRET_STORE", stderr)
            Assert.Contains("nonsense", stderr)
            Assert.DoesNotContain("connectors:", stdout)
            Assert.DoesNotContain("skipped", stdout)
            Assert.DoesNotContain("would move", stdout)
        })
