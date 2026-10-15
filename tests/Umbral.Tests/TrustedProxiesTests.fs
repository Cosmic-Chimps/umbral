/// UMBRAL__TRUSTED_PROXIES: whose X-Forwarded-* headers are believed.
///
/// Three layers. The parser is a pure function. The options it produces are
/// driven through ASP.NET Core's own middleware, which is where the question
/// "does a dual-stack `::ffff:a.b.c.d` connection match an IPv4 entry" has its
/// answer. And the whole app is run with the setting, to prove the rate limit
/// can no longer be dodged by a sender that is not the proxy.
module Umbral.Tests.TrustedProxiesTests

open System
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.HttpOverrides
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.Extensions.Options
open Xunit
open Umbral.Infrastructure
open Umbral.Infrastructure.TrustedProxies
open Umbral.Tests.Infrastructure

// ---------------------------------------------------------------------------
// The parser
// ---------------------------------------------------------------------------

let private entries raw =
    match parse raw with
    | Ok(Only e) -> e
    | Ok TrustAll -> failwith "expected a list, got trust-all"
    | Error e -> failwith $"expected success, got: {e}"

let private address (text: string) = Address(IPAddress.Parse text)
let private network (text: string) = Network(System.Net.IPNetwork.Parse text)

[<Fact>]
let ``unset, empty and blank all mean trust any sender`` () =
    for raw in ([ null; ""; "   " ]: (string | null) list) do
        Assert.Equal(Ok TrustAll, parse raw)

[<Fact>]
let ``an IPv4 address is accepted`` () =
    Assert.Equal<Entry list>([ address "172.26.64.1" ], entries "172.26.64.1")

[<Fact>]
let ``an IPv6 address is accepted`` () =
    Assert.Equal<Entry list>([ address "fd00::1" ], entries "fd00::1")

[<Fact>]
let ``IPv4 and IPv6 networks are accepted`` () =
    Assert.Equal<Entry list>([ network "10.0.0.0/8"; network "fd00::/8" ], entries "10.0.0.0/8,fd00::/8")

[<Fact>]
let ``whitespace around entries and empty entries are ignored`` () =
    Assert.Equal<Entry list>(
        [ address "172.26.64.1"; network "10.0.0.0/8"; network "fd00::/8" ],
        entries "  172.26.64.1 ,\t10.0.0.0/8 , fd00::/8 ,, "
    )

[<Fact>]
let ``an IPv4-mapped entry is read as the IPv4 address it stands for`` () =
    Assert.Equal<Entry list>([ address "172.26.64.1" ], entries "::ffff:172.26.64.1")
    Assert.Equal<Entry list>([ network "10.0.0.0/8" ], entries "::ffff:10.0.0.0/104")

[<Theory>]
[<InlineData("300.1.1.1")>]
[<InlineData("10.0.0.0/33")>]
[<InlineData("abc")>]
[<InlineData("10.1")>] // legacy shorthand for 10.0.0.1: nobody means that
[<InlineData("1")>]
[<InlineData("10.0.0.0/")>]
[<InlineData("10.0.0.0/x")>]
[<InlineData("10.0.0.0/-1")>]
[<InlineData("fd00::/129")>]
[<InlineData("10.0.0.5/8")>] // bits set beyond the prefix
let ``an invalid entry is refused, naming it and the next step`` (bad: string) =
    match parse $"172.26.64.1, {bad}, 10.0.0.0/8" with
    | Ok _ -> failwith $"expected {bad} to be refused"
    | Error message ->
        Assert.Contains($"\"{bad}\"", message)
        Assert.Contains(EnvironmentVariable, message)
        // The next action: what to write, or how to leave the setting out.
        Assert.Contains("comma-separated", message)
        Assert.Contains("Leaving the setting out", message)

[<Fact>]
let ``a network with host bits set says which network was meant`` () =
    match parse "10.0.0.5/8" with
    | Error message -> Assert.Contains("\"10.0.0.0/8\"", message)
    | Ok _ -> failwith "expected an error"

[<Fact>]
let ``only separators is refused rather than read as trust-all`` () =
    // Someone who typed a value meant to restrict. Silently trusting everyone
    // would be the opposite of what they asked for.
    match parse " , ," with
    | Error message -> Assert.Contains(EnvironmentVariable, message)
    | Ok _ -> failwith "expected an error"

[<Fact>]
let ``either spelling of the setting is read`` () =
    let read (key: string) : string | null =
        if key = EnvironmentVariable then "10.0.0.1" else null

    Assert.Equal(Ok(Only [ address "10.0.0.1" ]), fromConfiguration read)
    Assert.Equal(Ok TrustAll, fromConfiguration (fun _ -> null))

// ---------------------------------------------------------------------------
// What the options do, through ASP.NET Core's own middleware
// ---------------------------------------------------------------------------

/// Runs one request through `ForwardedHeadersMiddleware` built from the options
/// `configure` makes, and returns the client address and scheme the rest of the
/// pipeline would see.
let private resolve (setting: string | null) (connection: string) (forwardedFor: string) =
    let trust =
        match parse setting with
        | Ok t -> t
        | Error e -> failwith e

    let options = ForwardedHeadersOptions()
    configure trust options

    let mutable seen: IPAddress | null = null
    let mutable scheme = ""

    let next =
        RequestDelegate(fun ctx ->
            seen <- ctx.Connection.RemoteIpAddress
            scheme <- ctx.Request.Scheme
            Task.CompletedTask)

    let middleware =
        ForwardedHeadersMiddleware(next, NullLoggerFactory.Instance, Options.Create options)

    let ctx = DefaultHttpContext()
    ctx.Connection.RemoteIpAddress <- IPAddress.Parse connection
    ctx.Request.Scheme <- "http"
    ctx.Request.Headers["X-Forwarded-For"] <- forwardedFor
    ctx.Request.Headers["X-Forwarded-Proto"] <- "https"
    middleware.Invoke(ctx).GetAwaiter().GetResult()
    string seen, scheme

[<Fact>]
let ``unset: forwarded headers are believed from any sender`` () =
    Assert.Equal(("198.51.100.7", "https"), resolve null "203.0.113.50" "198.51.100.7")
    Assert.Equal(("198.51.100.7", "https"), resolve "" "::ffff:203.0.113.50" "198.51.100.7")

[<Fact>]
let ``set: headers from an address that is not listed are ignored, scheme included`` () =
    // The real connection address stays, and so does the scheme: a stranger
    // cannot claim https either.
    Assert.Equal(("203.0.113.50", "http"), resolve "172.26.64.1" "203.0.113.50" "198.51.100.7")
    Assert.Equal(("203.0.113.50", "http"), resolve "10.0.0.0/8" "203.0.113.50" "198.51.100.7")

[<Fact>]
let ``set: headers from a listed address or network are believed`` () =
    Assert.Equal(("198.51.100.7", "https"), resolve "172.26.64.1" "172.26.64.1" "198.51.100.7")
    Assert.Equal(("198.51.100.7", "https"), resolve "10.0.0.0/8" "10.9.8.7" "198.51.100.7")
    Assert.Equal(("198.51.100.7", "https"), resolve "fd00::/8" "fd00::5" "198.51.100.7")
    Assert.Equal(("198.51.100.7", "https"), resolve "fd00::5" "fd00::5" "198.51.100.7")

[<Fact>]
let ``set: the network edge is respected`` () =
    Assert.Equal(("10.0.1.1", "http"), resolve "10.0.0.0/24" "10.0.1.1" "198.51.100.7")

[<Fact>]
let ``set: a dual-stack connection address, ::ffff:a.b.c.d, still matches an IPv4 entry`` () =
    // ASP.NET Core maps an IPv4-mapped connection address back to IPv4 before it
    // compares it with KnownProxies and KnownIPNetworks. These pin that down, for
    // a bare address and for a network, so a framework change that stops doing it
    // fails here rather than silently turning every sender into an untrusted one.
    Assert.Equal(("198.51.100.7", "https"), resolve "172.26.64.1" "::ffff:172.26.64.1" "198.51.100.7")
    Assert.Equal(("198.51.100.7", "https"), resolve "10.0.0.0/8" "::ffff:10.9.8.7" "198.51.100.7")

[<Fact>]
let ``set: a dual-stack address that is not listed is still ignored`` () =
    // The address is left as the socket reported it.
    Assert.Equal(("::ffff:203.0.113.50", "http"), resolve "172.26.64.1" "::ffff:203.0.113.50" "198.51.100.7")

[<Fact>]
let ``set: an entry written in mapped form matches the plain IPv4 connection`` () =
    Assert.Equal(("198.51.100.7", "https"), resolve "::ffff:172.26.64.1" "172.26.64.1" "198.51.100.7")
    Assert.Equal(("198.51.100.7", "https"), resolve "::ffff:172.26.64.1" "::ffff:172.26.64.1" "198.51.100.7")

[<Fact>]
let ``set: loopback is not trusted unless it is listed`` () =
    // ASP.NET's defaults trust loopback. Clearing them is part of the contract:
    // a process on the same host is not the proxy unless the operator says so.
    Assert.Equal(("127.0.0.1", "http"), resolve "172.26.64.1" "127.0.0.1" "198.51.100.7")
    Assert.Equal(("::1", "http"), resolve "172.26.64.1" "::1" "198.51.100.7")

// ---------------------------------------------------------------------------
// The whole app: the rate limit can no longer be dodged
// ---------------------------------------------------------------------------

[<Literal>]
let private ConnectionHeader = "X-Test-Connection"

/// A TestServer has no connection address, so this puts one in front of the
/// pipeline, read from a request header. It is registered as a startup filter,
/// which wraps the app's own middleware, so it runs before UseForwardedHeaders
/// exactly as Kestrel's real connection address does. Test-only; nothing in
/// production knows about it.
type private ConnectionAddressFilter() =
    interface IStartupFilter with
        member _.Configure(next: Action<IApplicationBuilder>) : Action<IApplicationBuilder> =
            Action<IApplicationBuilder>(fun app ->
                app.Use(Func<HttpContext, Func<Task>, Task>(fun ctx continuation ->
                    match ctx.Request.Headers[ConnectionHeader].ToString() with
                    | "" -> ()
                    | text -> ctx.Connection.RemoteIpAddress <- IPAddress.Parse text

                    continuation.Invoke()))
                |> ignore

                next.Invoke app)

let private startWith (trusted: string option) =
    let settings =
        match trusted with
        | Some value -> [ ConfigurationKey, value ]
        | None -> []

    Fixtures.createAppWith settings (fun builder ->
        builder.Services.AddTransient<IStartupFilter, ConnectionAddressFilter>() |> ignore)

let private post (app: Fixtures.TestApp) (connection: string) (forwardedFor: string) =
    task {
        use request = new HttpRequestMessage(HttpMethod.Post, "/admin/signin")
        request.Headers.Add(ConnectionHeader, connection)
        request.Headers.Add("X-Forwarded-For", forwardedFor)
        request.Content <- new FormUrlEncodedContent(dict [ "credential", "wrong" ])
        let! response = app.Client.SendAsync request
        return response.StatusCode
    }

/// Spends the whole admin budget from `connection`, each request claiming a
/// different client address, then makes one more request claiming yet another.
let private spendAndOneMore (app: Fixtures.TestApp) (connection: string) =
    task {
        for i in 1..10 do
            let! status = post app connection $"198.51.100.{i}"
            Assert.NotEqual(HttpStatusCode.TooManyRequests, status)

        return! post app connection "198.51.100.200"
    }

[<Fact>]
let ``unset: any sender can pick its client address (today's behaviour)`` () =
    task {
        use! app = startWith None
        let! status = spendAndOneMore app "203.0.113.50"
        Assert.NotEqual(HttpStatusCode.TooManyRequests, status)
    }

[<Fact>]
let ``set: a sender that is not the proxy shares one budget however it forges X-Forwarded-For`` () =
    task {
        use! app = startWith (Some "172.26.64.1, fd00::/8")
        let! status = spendAndOneMore app "203.0.113.50"
        Assert.Equal(HttpStatusCode.TooManyRequests, status)

        // The same real address in its dual-stack spelling is the same budget.
        let! mapped = post app "::ffff:203.0.113.50" "198.51.100.201"
        Assert.Equal(HttpStatusCode.TooManyRequests, mapped)

        // A different real sender has a budget of its own.
        let! other = post app "203.0.113.51" "198.51.100.202"
        Assert.NotEqual(HttpStatusCode.TooManyRequests, other)
    }

[<Fact>]
let ``set: the proxy's X-Forwarded-For is honoured, so clients behind it are counted apart`` () =
    task {
        use! app = startWith (Some "172.26.64.1")

        for i in 1..10 do
            let! status = post app "172.26.64.1" "198.51.100.1"
            Assert.NotEqual(HttpStatusCode.TooManyRequests, status)

        let! sameClient = post app "172.26.64.1" "198.51.100.1"
        Assert.Equal(HttpStatusCode.TooManyRequests, sameClient)

        let! otherClient = post app "172.26.64.1" "198.51.100.2"
        Assert.NotEqual(HttpStatusCode.TooManyRequests, otherClient)
    }

[<Fact>]
let ``set: a proxy reported as ::ffff:a.b.c.d is still the proxy`` () =
    task {
        use! app = startWith (Some "172.26.64.1")

        for _ in 1..10 do
            let! status = post app "::ffff:172.26.64.1" "198.51.100.1"
            Assert.NotEqual(HttpStatusCode.TooManyRequests, status)

        let! sameClient = post app "::ffff:172.26.64.1" "198.51.100.1"
        Assert.Equal(HttpStatusCode.TooManyRequests, sameClient)

        let! otherClient = post app "::ffff:172.26.64.1" "198.51.100.2"
        Assert.NotEqual(HttpStatusCode.TooManyRequests, otherClient)
    }

[<Fact>]
let ``startup refuses an invalid entry, naming it`` () =
    let build () =
        Umbral.Program.buildApp Array.empty (fun builder ->
            builder.Configuration.AddInMemoryCollection(
                dict
                    [ "UMBRAL:PUBLIC_ORIGIN", Fixtures.TestOrigin
                      "UMBRAL:DATA_DIRECTORY", Fixtures.freshDataDirectory ()
                      ConfigurationKey, "172.26.64.1, 300.1.1.1" ])
            |> ignore)
        |> ignore

    let ex = Assert.Throws<Exception> build
    Assert.Contains("\"300.1.1.1\"", ex.Message)
    Assert.Contains("UMBRAL__TRUSTED_PROXIES", ex.Message)
