/// T097 — SC-007: an idle instance holds under 150 MB resident.
///
/// Constitution II makes this a promise to Raspberry Pi owners, and it is the
/// kind of promise that erodes one innocent dependency at a time: nothing
/// fails, the process just gets a little bigger until a 1 GB board starts
/// swapping. So it is measured, by a test, against the real thing.
///
/// "The real thing" matters. The other suites host the app inside the test
/// process on a TestServer, where the number would be the test runner's — xUnit,
/// the TestServer and every other test's garbage. This starts Umbral as its OWN
/// process, exactly as a container does: the built `Umbral.dll` that sits next
/// to this assembly, `DOTNET_gcServer=0`, a fresh data directory and a free
/// port. It waits until the app answers, makes a few requests so the pipeline,
/// the views and the database have all been touched, lets it sit idle for a few
/// seconds, and reads its resident set.
///
/// What is read, and where the budget is enforced:
///
///   Linux    `VmRSS` from `/proc/<pid>/status` — the figure `docker stats`
///            and the OOM killer care about. THIS is where SC-007 is asserted,
///            because Linux is where the promise is made: a Pi, or a container.
///   others   `Process.WorkingSet64` (macOS: `resident_size`). Measured and
///            printed, but held to a loose sanity ceiling only. macOS RSS is not
///            a stable number — the same build lands on ~115 MB or ~165 MB from
///            one start to the next, as the OS compresses or keeps pages — so
///            a 150 MB assertion there would fail on a developer's laptop for
///            reasons that say nothing about a Raspberry Pi. CI on Linux runs
///            the strict one.
///
/// It runs twice (specs/005 SC-006). The second time the instance has an
/// external secret store, `aws`, because that is the configuration that pulls a
/// vendor SDK, its HTTP stack and its credential chain into the process: the
/// promise to a Pi owner has to hold with the SDK loaded, not only without it.
/// There is no AWS in a test, so the SDK is pointed at a stub on a local port
/// that refuses everything the way a manager would an unknown secret.
module Umbral.Tests.IdleMemoryTests

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Runtime.InteropServices
open System.Text
open System.Threading
open System.Threading.Tasks
open Xunit

/// The SC-007 ceiling, "150 MB" read the way a container limit (`--memory=150m`)
/// and every memory tool reads it: mebibytes.
[<Literal>]
let private CeilingBytes = 157_286_400L

/// Off Linux: only a runaway fails. See the module comment.
[<Literal>]
let private SanityCeilingBytes = 262_144_000L

/// Long enough for startup work (migrations, key loading, JIT) to settle and
/// for the GC to have had its chance, short enough not to slow the suite.
let private idleFor = TimeSpan.FromSeconds 5.0

/// A port nobody is listening on. Released before the child binds it, so there
/// is a window in which another process could take it; the child would then
/// fail to start and the test would say so, rather than measuring the wrong app.
let private freePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

/// The resident set of `pid`, in bytes, from the best source the platform has.
let private residentBytes (child: Process) =
    if OperatingSystem.IsLinux() then
        // "VmRSS:   146092 kB" — kibibytes, despite the spelling.
        File.ReadAllLines $"/proc/{child.Id}/status"
        |> Array.find (fun line -> line.StartsWith "VmRSS:")
        |> fun line -> int64 (line.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)[1]) * 1024L
    else
        child.Refresh()
        child.WorkingSet64

/// The `dotnet` that is running this test, not whichever one is first on the
/// PATH: the app targets the same runtime the tests do.
let private dotnetHost () =
    let name = if RuntimeInformation.IsOSPlatform OSPlatform.Windows then "dotnet.exe" else "dotnet"
    // .../dotnet/shared/Microsoft.NETCore.App/<version>/ -> .../dotnet/
    let runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory().TrimEnd(Path.DirectorySeparatorChar)
    let root = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", ".."))
    let candidate = Path.Combine(root, name)
    if File.Exists candidate then candidate else name

/// A stand-in for AWS Secrets Manager: every request, whatever it is, gets the
/// answer the service gives for a secret that does not exist (JSON 1.1 over HTTP
/// 400). That is what the SDK's startup probe for `umbral-probe` expects, and a
/// store that is up but holds nothing is the quiet state of a fresh instance.
/// Answering ALL requests this way means the test does not depend on whether, or
/// when, the product probes. `Requests` says how many it saw.
type private ManagerStub(port: int) =
    let listener = new HttpListener()
    let cancel = new CancellationTokenSource()
    let mutable requests = 0

    let body =
        Encoding.UTF8.GetBytes """{"__type":"ResourceNotFoundException","message":"x"}"""

    do listener.Prefixes.Add $"http://127.0.0.1:{port}/"

    member _.Requests = Volatile.Read &requests

    member this.Start() =
        listener.Start()

        Task.Run(fun () ->
            task {
                while not cancel.IsCancellationRequested do
                    try
                        let! context = listener.GetContextAsync()
                        Interlocked.Increment &requests |> ignore
                        context.Response.StatusCode <- 400
                        context.Response.ContentType <- "application/x-amz-json-1.1"
                        context.Response.ContentLength64 <- int64 body.Length
                        do! context.Response.OutputStream.WriteAsync(body, 0, body.Length)
                        context.Response.Close()
                    with _ ->
                        // Stopped, or a client that hung up: neither matters here.
                        ()
            }
            :> Task)
        |> ignore

    interface IDisposable with
        member _.Dispose() =
            cancel.Cancel()
            listener.Close()
            cancel.Dispose()

/// Starts Umbral as its own process with `extraEnvironment` on top of the
/// production one, waits until it answers and sits idle, and asserts the
/// resident set. `label` names the run in the report and the job summary.
let private assertIdleUnderCeiling (label: string) (extraEnvironment: (string * string) list) =
    task {
        let umbralDll = Path.Combine(AppContext.BaseDirectory, "Umbral.dll")

        Assert.True(
            File.Exists umbralDll,
            $"the built app is not next to the test assembly ({umbralDll}); the test project must reference src/Umbral"
        )

        let dataDirectory = Path.Combine(Path.GetTempPath(), "umbral-idle-" + Guid.NewGuid().ToString "N")
        Directory.CreateDirectory dataDirectory |> ignore
        let port = freePort ()

        let start = ProcessStartInfo(dotnetHost ())
        start.ArgumentList.Add umbralDll
        start.WorkingDirectory <- AppContext.BaseDirectory
        start.UseShellExecute <- false
        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true

        // Production, as a container runs it: no Development, no Fake connector.
        start.Environment.Remove "ASPNETCORE_ENVIRONMENT" |> ignore
        start.Environment.Remove "DOTNET_ENVIRONMENT" |> ignore
        start.Environment["ASPNETCORE_URLS"] <- $"http://127.0.0.1:{port}"
        start.Environment["UMBRAL__PUBLIC_ORIGIN"] <- "https://umbral.test"
        start.Environment["UMBRAL__DATA_DIRECTORY"] <- dataDirectory
        start.Environment["DOTNET_gcServer"] <- "0"

        for name, value in extraEnvironment do
            start.Environment[name] <- value

        // What the child said, for the failure message: a process that dies at
        // startup should fail with its own explanation, not a timeout.
        let output = ConcurrentQueue<string>()
        use child = new Process(StartInfo = start)
        child.OutputDataReceived.Add(fun e ->
            match e.Data with
            | null -> ()
            | line -> output.Enqueue line)
        child.ErrorDataReceived.Add(fun e ->
            match e.Data with
            | null -> ()
            | line -> output.Enqueue line)

        let said () = String.Join(Environment.NewLine, output |> Seq.truncate 40)

        try
            Assert.True(child.Start(), "the app process did not start")
            child.BeginOutputReadLine()
            child.BeginErrorReadLine()

            use http = new HttpClient(BaseAddress = Uri $"http://127.0.0.1:{port}")
            http.Timeout <- TimeSpan.FromSeconds 10.0

            // The app sits behind a TLS-terminating proxy in a deployment, and
            // OpenIddict refuses plain http. Saying so with the header a proxy
            // sends is the production path, and keeps the environment out of
            // Development.
            let get (path: string) =
                task {
                    let request = new HttpRequestMessage(HttpMethod.Get, path)
                    request.Headers.Add("X-Forwarded-Proto", "https")
                    return! http.SendAsync request
                }

            // Wait until it answers the one URL every client library fetches.
            let deadline = DateTime.UtcNow.AddSeconds 60.0
            let mutable ready = false

            while not ready do
                if child.HasExited then
                    failwith $"the app exited with code {child.ExitCode} before answering:{Environment.NewLine}{said ()}"

                if DateTime.UtcNow > deadline then
                    failwith $"the app did not answer within 60 s:{Environment.NewLine}{said ()}"

                try
                    use! response = get "/.well-known/openid-configuration"
                    ready <- response.IsSuccessStatusCode
                with :? HttpRequestException | :? TaskCanceledException ->
                    ()

                if not ready then
                    do! Task.Delay 250

            // Enough traffic to have touched the pipeline, the key set, the
            // views (a first-run page renders the whole layout) and SQLite.
            for path in [ "/.well-known/jwks"; "/admin/setup"; "/signin"; "/.well-known/openid-configuration" ] do
                use! _ = get path
                ()

            do! Task.Delay idleFor

            Assert.False(child.HasExited, $"the app exited while idle:{Environment.NewLine}{said ()}")
            let resident = residentBytes child
            let ceiling = if OperatingSystem.IsLinux() then CeilingBytes else SanityCeilingBytes

            // Visible in the test output, because a number that passes with
            // 3 MB to spare and one that passes with 90 MB are different news.
            let mebibytes (bytes: int64) = float bytes / 1_048_576.0
            let report = $"[SC-007] {label}: idle resident set: {mebibytes resident:F1} MiB (ceiling {mebibytes ceiling:F0} MiB, {RuntimeInformation.OSDescription})"
            Console.WriteLine report
            TestContext.Current.SendDiagnosticMessage report

            // A passing test prints nothing under `dotnet test`, so on GitHub
            // Actions the number goes to the job summary: that is how the x64
            // and arm64 figures are read off a run without opening its log.
            match Environment.GetEnvironmentVariable "GITHUB_STEP_SUMMARY" with
            | null
            | "" -> ()
            | summary ->
                let arch = RuntimeInformation.OSArchitecture
                File.AppendAllText(summary, $"- {arch}, {label}: **{mebibytes resident:F1} MiB** idle resident set, ceiling {mebibytes ceiling:F0} MiB{Environment.NewLine}")

            Assert.True(
                resident < ceiling,
                $"idle resident set ({label}) was {mebibytes resident:F1} MiB, over the {mebibytes ceiling:F0} MiB ceiling (SC-007)"
            )
        finally
            if not child.HasExited then
                child.Kill true

            child.WaitForExit 10_000 |> ignore
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            try
                Directory.Delete(dataDirectory, true)
            with _ ->
                ()
    }

[<Fact>]
let ``an idle instance stays under 150 MB resident`` () =
    assertIdleUnderCeiling "local store" []

[<Fact>]
let ``an idle instance with an external secret store stays under 150 MB resident`` () =
    task {
        let port = freePort ()
        use stub = new ManagerStub(port)
        stub.Start()

        // Static fake credentials and no instance-metadata lookup: the SDK's
        // credential chain finds the first at once instead of walking on to a
        // link-local address that nothing here answers.
        do!
            assertIdleUnderCeiling
                "aws store"
                [ "UMBRAL__SECRET_STORE", "aws"
                  "AWS_REGION", "eu-west-1"
                  "AWS_ACCESS_KEY_ID", "AKIAIDLEMEMORYTEST"
                  "AWS_SECRET_ACCESS_KEY", "idle-memory-test-not-a-real-secret"
                  "AWS_EC2_METADATA_DISABLED", "true"
                  "AWS_ENDPOINT_URL_SECRETS_MANAGER", $"http://127.0.0.1:{port}" ]

        // How many calls the SDK made, so that a run where the store was never
        // reached (and the figure above is just the local one again) shows.
        let report = $"[SC-006] the stub manager saw {stub.Requests} request(s)"
        Console.WriteLine report
        TestContext.Current.SendDiagnosticMessage report

        match Environment.GetEnvironmentVariable "GITHUB_STEP_SUMMARY" with
        | null
        | "" -> ()
        | summary -> File.AppendAllText(summary, $"- aws store: the stub manager saw {stub.Requests} request(s){Environment.NewLine}")
    }
