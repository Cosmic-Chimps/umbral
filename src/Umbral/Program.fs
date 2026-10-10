module Umbral.Program

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.DataProtection
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.HttpOverrides
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
open Oxpecker
open Umbral.Infrastructure
open Umbral.Migrations

// ---------------------------------------------------------------------------
// Endpoints
//
// One vertical slice per endpoint, see Handlers/. Every slice exposes `endpoint`
// (or a list of them, consumed with `yield!`) and it MUST appear here: a handler
// missing from this list compiles, serves nothing, and reports no error anywhere.
// T102 turns that into a build failure.
// ---------------------------------------------------------------------------

let endpoints: Endpoint list = []

// ---------------------------------------------------------------------------
// Startup settings
//
// Read through IConfiguration rather than the environment directly, so that
// `dotnet user-secrets` supplies them in Development and environment variables
// supply them everywhere else, with no second code path.
// ---------------------------------------------------------------------------

/// Fail at startup with a message naming the setting, rather than booting into
/// a half-working state whose symptom appears hours later as a provider error
/// that mentions neither the setting nor the proxy.
let private requireSettings (config: IConfiguration) =
    let read (key: string) : string | null = config[key]

    let origin =
        match PublicOrigin.fromConfiguration read with
        | Ok o -> o
        | Error e -> failwith (PublicOrigin.describe e)

    let dataDirectory =
        match DataDirectory.fromConfiguration read with
        | Ok d -> d
        | Error message -> failwith message

    origin, dataDirectory

// ---------------------------------------------------------------------------
// Composition
// ---------------------------------------------------------------------------

let configureServices (config: IConfiguration) (services: IServiceCollection) =
    let origin, dataDirectory = requireSettings config
    let dbConfig: Database.DbConfig = { DataSource = dataDirectory.DatabaseFile }

    services.AddSingleton<PublicOrigin.PublicOrigin> origin |> ignore
    services.AddSingleton<DataDirectory.DataDirectory> dataDirectory |> ignore
    services.AddSingleton<Database.DbConfig> dbConfig |> ignore
    services.TryAddSingleton<TimeProvider> TimeProvider.System

    // The key ring protects the admin cookie, antiforgery tokens and every
    // stored connector secret. Its default location is ephemeral inside a
    // container, so a rebuilt image would log the operator out and make every
    // saved secret undecryptable. SetApplicationName pins the isolation key to
    // a name rather than a content-root path, which changes between builds.
    services
        .AddDataProtection()
        .PersistKeysToFileSystem(IO.DirectoryInfo dataDirectory.KeysDirectory)
        .SetApplicationName "Umbral"
    |> ignore

    services.AddSingleton<Secrets.ISecretStore>(fun sp ->
        Secrets.LocalSecretStore(sp.GetRequiredService<IDataProtectionProvider>()) :> Secrets.ISecretStore)
    |> ignore

    // EF Core exists for OpenIddict's four tables and nothing else; Umbral's own
    // six are raw SQLite in Database.fs.
    services.AddDbContext<UmbralContext>(fun options ->
        // The migrations live in the C# Umbral.Migrations project, because EF
        // Core's design-time tooling scaffolds C# only. Referenced by name:
        // the web project does not depend on it at compile time.
        options.UseSqlite(Database.connectionString dbConfig, (fun b -> b.MigrationsAssembly "Umbral.Migrations" |> ignore))
        |> ignore
        options.UseOpenIddict() |> ignore)
    |> ignore

    // Behind a reverse proxy the client IP and scheme arrive in headers. Only
    // loopback proxies are trusted by default, and the proxy is a different
    // container with a different address, so the lists are cleared. That is
    // acceptable only because Umbral is not meant to be directly exposed — and
    // note these feed logging and OpenIddict's scheme detection, never a
    // connector redirect URI, which comes from the configured origin alone.
    services.Configure<ForwardedHeadersOptions>(fun (options: ForwardedHeadersOptions) ->
        options.ForwardedHeaders <-
            ForwardedHeaders.XForwardedFor ||| ForwardedHeaders.XForwardedProto ||| ForwardedHeaders.XForwardedHost

        // KnownNetworks is deprecated in .NET 10; KnownIPNetworks replaces it.
        options.KnownIPNetworks.Clear()
        options.KnownProxies.Clear())
    |> ignore

    // No scheme is registered yet: the operator cookie arrives with AdminAuth
    // (T042) and OpenIddict's server and validation handlers with T030. The
    // services are here now so that UseAuthentication can sit in its correct
    // place in the pipeline from the start — OpenIddict's server integration
    // runs inside that middleware, so discovering the ordering later would mean
    // re-testing everything built on top of it.
    services.AddAuthentication() |> ignore

    services.AddAntiforgery() |> ignore
    services.AddRouting().AddOxpecker() |> ignore

/// Middleware order is load-bearing. Each line is here for a reason:
///   UseForwardedHeaders  first, so scheme and client IP are right before anything reads them
///   UseAuthentication    OpenIddict's server integration runs INSIDE this as an
///                        IAuthenticationRequestHandler, so it must precede UseOxpecker
///   UseAntiforgery       must sit between routing and the endpoints
let configureApp (app: WebApplication) =
    app
        .UseForwardedHeaders()
        .UseRouting()
        .UseAuthentication()
        // No UseAuthorization. Umbral has no authorization model by design
        // (Principle VII): admin access is the `requireOperator` handler
        // combinator, and an app decides what its own users may do. Adding the
        // middleware would mean adding policies, which is the thing we refuse.
        .UseAntiforgery()
        .UseOxpecker(endpoints)
    |> ignore

    app.Run(fun ctx ->
        ctx.SetStatusCode 404
        ctx.WriteText "Not found")

/// Everything that must happen before the first request: the schema Umbral owns,
/// OpenIddict's migrations, and the key material.
///
/// Safe to run at startup because a single container is a single writer — there
/// is no multi-instance race to lose.
let initialize (app: WebApplication) =
    task {
        let dataDirectory = app.Services.GetRequiredService<DataDirectory.DataDirectory>()
        let dbConfig = app.Services.GetRequiredService<Database.DbConfig>()

        do! Database.initializeSchema dbConfig

        use scope = app.Services.CreateScope()
        let context = scope.ServiceProvider.GetRequiredService<UmbralContext>()
        do! context.Database.MigrateAsync()

        // Generate on first run so that a restart, or a replaced container,
        // leaves every issued token still valid (SC-005).
        let signing, encryption = Keys.loadOrCreateAll dataDirectory.KeysDirectory
        (signing :> IDisposable).Dispose()
        (encryption :> IDisposable).Dispose()
    }

/// The web app, composable so integration tests host the real thing on a TestServer.
///
/// This seam is why `customize` exists: a test points the app at a temp SQLite file,
/// stubs the provider HttpMessageHandler and injects a fake TimeProvider, and no
/// production code knows it is under test.
let buildApp (args: string array) (customize: WebApplicationBuilder -> unit) : WebApplication =
    let builder = WebApplication.CreateBuilder(args)
    customize builder
    configureServices builder.Configuration builder.Services
    let app = builder.Build()
    configureApp app
    app

[<EntryPoint>]
let main args =
    let app = buildApp args ignore
    (initialize app).GetAwaiter().GetResult()
    app.Run()
    0
