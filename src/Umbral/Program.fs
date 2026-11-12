module Umbral.Program

open System
open Microsoft.AspNetCore.Builder
open System.Net.Http
open System.Threading.Tasks
// GetOpenIddictServerRequest lives in this namespace.
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Antiforgery
open Microsoft.Extensions.Logging
open Umbral.Views
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.DataProtection
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.RateLimiting
open Microsoft.AspNetCore.HttpOverrides
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
open OpenIddict.Abstractions
open Oxpecker
open Umbral.Infrastructure
open Umbral.Infrastructure.SecretStores.Registry
open Umbral.Migrations

module SecretStoreSettings = Umbral.Infrastructure.SecretStores.Settings

/// The name of the CORS policy for the endpoints a browser-only app calls with
/// `fetch` (see `configureApp` for which ones).
[<Literal>]
let BrowserCorsPolicy = "oidc-browser"

// ---------------------------------------------------------------------------
// Endpoints
//
// One vertical slice per endpoint, see Handlers/. Every slice exposes `endpoint`
// (or a list of them, consumed with `yield!`) and it MUST appear here: a handler
// missing from this list compiles, serves nothing, and reports no error anywhere.
// MetaTests.fs turns that into a failing test naming the module; `dotnet build`
// alone stays silent.
// ---------------------------------------------------------------------------

let endpoints: Endpoint list =
    [ Umbral.Handlers.Health.endpoint
      Umbral.Handlers.Assets.endpoint
      Umbral.Handlers.Branding.endpoint
      Umbral.Handlers.ThemeChoice.endpoint
      Umbral.Handlers.Home.endpoint
      Umbral.Handlers.Admin.Landing.endpoint
      yield! Umbral.Handlers.Admin.Setup.endpoint
      yield! Umbral.Handlers.Admin.SignIn.endpoint
      Umbral.Handlers.Admin.SignOut.endpoint
      yield! Umbral.Handlers.Admin.Credential.endpoint
      yield! Umbral.Handlers.Admin.PasskeyConnector.endpoint
      yield! Umbral.Handlers.Admin.Connectors.endpoint
      Umbral.Handlers.Admin.ConnectorToggle.endpoint
      Umbral.Handlers.Admin.ConnectorDelete.endpoint
      yield! Umbral.Handlers.Dev.FakeProvider.endpoint
      yield! Umbral.Handlers.Admin.Apps.endpoint
      Umbral.Handlers.Admin.AppSecret.endpoint
      Umbral.Handlers.Admin.AppRedirectUris.endpoint
      Umbral.Handlers.Admin.AppDelete.endpoint
      yield! Umbral.Handlers.Admin.AppBranding.endpoint
      Umbral.Handlers.Admin.Users.endpoint
      Umbral.Handlers.Admin.UserToggle.endpoint
      Umbral.Handlers.Admin.UserDelete.endpoint
      yield! Umbral.Handlers.Admin.UserPasskeys.endpoint
      Umbral.Handlers.Connect.Picker.endpoint
      Umbral.Handlers.Connect.Start.endpoint
      yield! Umbral.Handlers.Connect.Callback.endpoint
      yield! Umbral.Handlers.Connect.Fediverse.endpoint
      yield! Umbral.Handlers.Connect.Authorize.endpoint
      Umbral.Handlers.Connect.Token.endpoint
      yield! Umbral.Handlers.Connect.UserInfo.endpoint
      yield! Umbral.Handlers.Connect.EndSession.endpoint
      Umbral.Handlers.Connect.Error.endpoint
      yield! Umbral.Handlers.Passkeys.SignIn.endpoint
      yield! Umbral.Handlers.Passkeys.Invitation.endpoint
      yield! Umbral.Handlers.Passkeys.Account.endpoint
      yield! Umbral.Handlers.Passkeys.Ceremonies.endpoint
      yield! Umbral.Handlers.Admin.OperatorPasskeys.endpoint
      yield! Umbral.Handlers.Admin.OperatorPasswordSignIn.endpoint
      yield! Umbral.Handlers.Admin.OperatorPasskeyRemove.endpoint ]

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

    // Every problem at once, one line each, so a deployment is fixed in one
    // pass. This is the shared path of `buildApp`, so `reset-credential` and
    // the other offline commands refuse a bad store setting exactly as the
    // server does.
    let secretStore =
        match SecretStoreSettings.parse config with
        | Ok settings -> settings
        | Error problems ->
            let describe = (Texts.current ()).SecretStoreProblem
            failwith (problems |> List.map describe |> String.concat " ")

    // Optional: unset keeps today's behaviour, so only a bad value can fail here.
    let trustedProxies =
        match TrustedProxies.fromConfiguration read with
        | Ok t -> t
        | Error message -> failwith message

    origin, dataDirectory, secretStore, trustedProxies

// ---------------------------------------------------------------------------
// Composition
// ---------------------------------------------------------------------------

let configureServices (config: IConfiguration) (services: IServiceCollection) =
    let origin, dataDirectory, secretStore, trustedProxies = requireSettings config
    let dbConfig: Database.DbConfig = { DataSource = dataDirectory.DatabaseFile }

    services.AddSingleton<PublicOrigin.PublicOrigin> origin |> ignore
    services.AddSingleton<DataDirectory.DataDirectory> dataDirectory |> ignore
    // Parsed once here; the registry wiring reads it when it builds the stores.
    services.AddSingleton<SecretStoreSettings.SecretStoreSettings> secretStore |> ignore
    services.AddSingleton<Database.DbConfig> dbConfig |> ignore
    services.AddSingleton<TrustedProxies.Trust> trustedProxies |> ignore
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

    // TryAdd, so that a test can register a registry of its own (an in-memory
    // store under any id) before this runs, as it does for TimeProvider above.
    services.TryAddSingleton<SecretStores>(fun sp ->
        let local = Secrets.LocalSecretStore(sp.GetRequiredService<IDataProtectionProvider>())
        Umbral.Infrastructure.SecretStores.Wiring.build
            config
            (sp.GetRequiredService<SecretStoreSettings.SecretStoreSettings>())
            (sp.GetRequiredService<TimeProvider>())
            local)

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

    // Behind a reverse proxy the client IP and scheme arrive in headers. With
    // UMBRAL__TRUSTED_PROXIES unset the lists are cleared and the headers are
    // believed from any sender: the proxy is a different container with an
    // address nobody can know in advance, and that is acceptable only while the
    // proxy is the one thing that can reach Umbral's port. Where other
    // containers can reach it too (apps on one shared bridge), any of them could
    // send its own X-Forwarded-For on every request and dodge the rate limits,
    // so the operator names the proxy there and the headers from every other
    // connection address are ignored. Either way these feed rate limiting,
    // logging and OpenIddict's scheme detection, never a connector redirect
    // URI, which comes from the configured origin alone. See TrustedProxies.fs.
    services.Configure<ForwardedHeadersOptions>(fun (options: ForwardedHeadersOptions) ->
        TrustedProxies.configure trustedProxies options)
    |> ignore

    // The end user's session with Umbral. The operator's admin cookie is a
    // separate scheme (T042) so a bug in one cannot grant the other.
    services
        .AddAuthentication(Sessions.Scheme)
        .AddCookie(Sessions.Scheme, Action<CookieAuthenticationOptions> Sessions.configure)
        .AddCookie(AdminAuth.Scheme, Action<CookieAuthenticationOptions> AdminAuth.configureCookie)
    |> ignore

    // One per process. A restart mints a new token and prints it again, which
    // is correct: the old one should not outlive the process that announced it.
    services.AddSingleton<AdminAuth.SetupToken>() |> ignore

    let signing, encryption = Keys.loadOrCreateAll dataDirectory.KeysDirectory

    // Plain http is refused unless the environment is EXPLICITLY Development
    // and the operator explicitly asked for it. Two conditions, because this is
    // the setting most likely to be switched on "just for now" on a LAN and
    // left there.
    //
    // Phrased as "is Development" rather than "is not Production" on purpose:
    // ASPNETCORE_ENVIRONMENT is frequently unset, and the negative form would
    // then allow insecure http by default — fail-open, in the one place that
    // must not.
    let allowInsecureHttp =
        config["ASPNETCORE_ENVIRONMENT"] = "Development"
        && config["UMBRAL:INSECURE_ALLOW_HTTP"] = "true"

    OpenIddictSetup.addOpenIddict
        { Issuer = origin.Uri
          SigningCertificate = signing
          EncryptionCertificate = encryption
          AllowInsecureHttp = allowInsecureHttp }
        services

    // One named client for every upstream provider call.
    //
    // AllowAutoRedirect = false is a requirement, not a tidy default: a token
    // endpoint that answered with a redirect could otherwise walk an
    // authorization code — a bearer credential — to somewhere we never meant to
    // send it. The adapters cannot set this themselves; it belongs to whoever
    // builds the handler, which is `ProviderAdapters.defaultPrimaryHandler`.
    //
    // Reached through a registered factory so a test can register its own first
    // and put a stub identity provider behind the real adapters; `TryAdd`
    // leaves that registration alone and production gets the default.
    services.TryAddSingleton<ProviderAdapters.PrimaryHandler>(
        ProviderAdapters.PrimaryHandler(fun () -> ProviderAdapters.defaultPrimaryHandler ()))

    services
        .AddHttpClient(ProviderAdapters.HttpClientName)
        .ConfigurePrimaryHttpMessageHandler(fun (sp: IServiceProvider) ->
            sp.GetRequiredService<ProviderAdapters.PrimaryHandler>().Invoke())
    |> ignore

    // The Fediverse connector's own client (spec 010, research R6). Not the one
    // above: that one reaches an operator's own OpenID Connect server, which may
    // sit on a LAN, while this one talks to servers a stranger typed. Its default
    // handler refuses non-public addresses at connect time and follows no
    // redirects; the development allowance is the `allowInsecureHttp` above, so a
    // local fake server works.
    //
    // A factory again, so a test registers its own first and puts a stub server
    // behind the real client; `TryAdd` leaves that registration alone.
    services.AddSingleton<Providers.Fediverse.Options>(({ AllowDev = allowInsecureHttp }: Providers.Fediverse.Options))
    |> ignore

    services.TryAddSingleton<Providers.Fediverse.HostResolver>(Providers.Fediverse.systemResolver)
    services.TryAddSingleton<Providers.Fediverse.PrimaryHandler>(
        Providers.Fediverse.PrimaryHandler(fun () -> Providers.Fediverse.guardedHandler allowInsecureHttp))

    services
        .AddHttpClient(Providers.Fediverse.HttpClientName)
        .ConfigurePrimaryHttpMessageHandler(fun (sp: IServiceProvider) ->
            sp.GetRequiredService<Providers.Fediverse.PrimaryHandler>().Invoke())
    |> ignore

    // The Bluesky connector's client (spec 015). The same guarded handler and the
    // same seam as the Fediverse client, under a client name of its own, so a test
    // stubs both. The PLC directory is where `did:plc` documents are read; an
    // override must be https unless the development setting is on, like every
    // address Umbral is told to trust.
    let plcDirectory =
        let text =
            match Option.ofObj config["UMBRAL:BLUESKY_PLC_DIRECTORY"] |> Option.map _.Trim() with
            | Some configured when configured <> "" -> configured.TrimEnd '/'
            | _ -> "https://plc.directory"

        let allowed =
            match Uri.TryCreate(text, UriKind.Absolute) with
            | true, uri ->
                match Option.ofObj uri with
                | Some u -> u.Scheme = Uri.UriSchemeHttps || (allowInsecureHttp && u.Scheme = Uri.UriSchemeHttp)
                | None -> false
            | _ -> false

        match allowed with
        | true -> text
        | false ->
            failwith
                $"UMBRAL__BLUESKY_PLC_DIRECTORY must be an https address, for example https://plc.directory (http only in Development with UMBRAL__INSECURE_ALLOW_HTTP=true), but it is '{text}'."

    services.AddSingleton<Providers.Bluesky.Options>(
        ({ AllowDev = allowInsecureHttp
           PlcDirectory = plcDirectory }: Providers.Bluesky.Options)
    )
    |> ignore

    services.TryAddSingleton<Providers.Bluesky.TxtResolver>(Providers.Bluesky.dnsResolver)

    services
        .AddHttpClient(Providers.Bluesky.HttpClientName)
        .ConfigurePrimaryHttpMessageHandler(fun (sp: IServiceProvider) ->
            sp.GetRequiredService<Providers.Fediverse.PrimaryHandler>().Invoke())
    |> ignore

    // Development only, and refused at startup if a fake connector exists
    // without both gate conditions — see `initialize`.
    services.AddSingleton<Providers.Fake.FakeGate>() |> ignore

    services.AddSingleton<Providers.Fake.FakeAdapter>(fun sp ->
        Providers.Fake.FakeAdapter(
            sp.GetRequiredService<IDataProtectionProvider>(),
            sp.GetRequiredService<Providers.Fake.FakeGate>()))
    |> ignore

    services.AddSingleton<ProviderAdapters.IProviderAdapter>(fun sp ->
        sp.GetRequiredService<Providers.Fake.FakeAdapter>() :> ProviderAdapters.IProviderAdapter)
    |> ignore

    services.AddSingleton<ProviderAdapters.IProviderAdapter>(fun sp ->
        Providers.Discord.DiscordAdapter(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient ProviderAdapters.HttpClientName)
        :> ProviderAdapters.IProviderAdapter)
    |> ignore

    services.AddSingleton<ProviderAdapters.IProviderAdapter>(fun sp ->
        Providers.GitHub.GitHubAdapter(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient ProviderAdapters.HttpClientName)
        :> ProviderAdapters.IProviderAdapter)
    |> ignore

    services.AddSingleton<ProviderAdapters.IProviderAdapter>(fun sp ->
        Providers.Google.GoogleAdapter(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient ProviderAdapters.HttpClientName)
        :> ProviderAdapters.IProviderAdapter)
    |> ignore

    // One discovery cache for every `oidc` connector (research R2): a manager
    // per issuer, built over the same named client, so a provider's discovery
    // and keys are fetched once per refresh and not once per sign-in. The same
    // Development-only flag that admits plain http for OpenIddict admits it for
    // a loopback issuer, and nothing else does. The client is the cache's own — it
    // sets a response size cap on it — so it is not shared with an adapter.
    services.AddSingleton<OidcDiscovery.OidcDiscovery>(fun sp ->
        OidcDiscovery.OidcDiscovery(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient ProviderAdapters.HttpClientName,
            allowInsecureHttp,
            TimeSpan.FromSeconds 10.0,
            sp.GetRequiredService<TimeProvider>()))
    |> ignore

    // The adapter for them: its own client from the factory like the others, and
    // Umbral's clock so token lifetimes are judged on the same time as everything
    // else (and a test can move it).
    services.AddSingleton<ProviderAdapters.IProviderAdapter>(fun sp ->
        Providers.Oidc.OidcAdapter(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient ProviderAdapters.HttpClientName,
            sp.GetRequiredService<OidcDiscovery.OidcDiscovery>(),
            sp.GetRequiredService<TimeProvider>())
        :> ProviderAdapters.IProviderAdapter)
    |> ignore

    // Sign in with Apple reuses the OpenID Connect discovery cache and the
    // OpenID Connect adapter's token exchange and validation.
    services.AddSingleton<ProviderAdapters.IProviderAdapter>(fun sp ->
        Providers.Apple.AppleAdapter(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient ProviderAdapters.HttpClientName,
            sp.GetRequiredService<OidcDiscovery.OidcDiscovery>(),
            sp.GetRequiredService<TimeProvider>())
        :> ProviderAdapters.IProviderAdapter)
    |> ignore

    services.AddSingleton<ProviderAdapters.Registry>(fun sp ->
        ProviderAdapters.Registry(sp.GetServices<ProviderAdapters.IProviderAdapter>()))
    |> ignore

    // Limits are attached to endpoints one by one (RateLimits.fs), so adding
    // the service limits nothing until a route asks for a policy.
    services.AddRateLimiter(fun options ->
        options.RejectionStatusCode <- StatusCodes.Status429TooManyRequests
        RateLimits.configure options

        options.OnRejected <-
            fun context ct ->
                let ctx = context.HttpContext
                let seconds = RateLimits.retryAfterSeconds context
                ctx.Response.Headers.RetryAfter <- string seconds
                ctx.GetLogger().LogWarning("Rate limit reached on {Path}", ctx.Request.Path)
                let t = Texts.current ()
                // Whole minutes, rounded up: "about 1 minute" for 40 seconds
                // is truer than "0 minutes".
                let minutes = int (Math.Ceiling(float seconds / 60.0))

                ctx.WriteHtmlView(Layout.messagePage ctx t t.RateLimitedTitle (t.RateLimitedBody minutes))
                |> ValueTask)
    |> ignore

    // Any origin, on the few endpoints a browser-only app calls (the path list
    // is in configureApp). Why `*` is safe even on the token endpoint: PKCE
    // (S256, required) and the exact redirect URI already bind the exchange to
    // the app, so the response is readable only by whoever already holds both
    // the code and the verifier; and a secret cannot leak through CORS, which
    // gates reading a response, not sending a request. Auth0 does the same.
    // Per-client origins (Keycloak's "Web origins") would mean reading
    // `client_id` from the form before OpenIddict runs, for no gain. No
    // credentials and no `Vary: Origin`, because the answer never depends on
    // the origin. WWW-Authenticate is exposed so an app can read userinfo's
    // `invalid_token`.
    services.AddCors(fun options ->
        options.AddPolicy(
            BrowserCorsPolicy,
            fun policy ->
                policy
                    .AllowAnyOrigin()
                    .WithMethods("GET", "POST")
                    .AllowAnyHeader()
                    .WithExposedHeaders("WWW-Authenticate")
                    .SetPreflightMaxAge(TimeSpan.FromHours 1.0)
                |> ignore))
    |> ignore

    // A passkey page's script posts JSON, so the token comes in a header; forms
    // keep using the hidden field.
    services.AddAntiforgery(fun options -> options.HeaderName <- "RequestVerificationToken") |> ignore

    // One relying-party verifier for the process; the library behind it is built
    // on first use (WebAuthn.fs), so an instance with no passkey connector never
    // loads it.
    services.AddSingleton<WebAuthn.Verifier>(fun sp ->
        WebAuthn.Verifier(sp.GetRequiredService<PublicOrigin.PublicOrigin>()))
    |> ignore

    services.AddRouting().AddOxpecker() |> ignore

    // SC-008: no secret reaches a log, at ANY level an operator might switch on
    // while debugging. T093 drives a whole journey at Trace and found three
    // sources, each capped here with a rule on the exact category. A category
    // rule outranks both the minimum level and any `Logging__LogLevel__*`
    // setting, so "turn verbose logging on" cannot turn a leak on with it.
    services.AddLogging(fun logging -> LogRedaction.capCategories logging) |> ignore

/// One place where an exception becomes a page.
///
/// Without this, a rejected form leaves `AntiforgeryValidationException`
/// unhandled: a stack trace in Development and a blank 500 in production,
/// neither of which tells the person what to do. FR-037 asks for the next
/// action, and an expired form has a very clear one — reload and try again.
let private errorHandler (ctx: HttpContext) (next: RequestDelegate) =
    task {
        try
            return! next.Invoke ctx
        with
        | :? AntiforgeryValidationException as ex ->
            // Expected, not exceptional: a form left open past its token's
            // lifetime, or a cross-site post. Nothing was changed either way.
            ctx.GetLogger().LogWarning(ex, "Antiforgery validation failed on {Path}", ctx.Request.Path)
            let t = Texts.current ()
            ctx.SetStatusCode 400
            return! ctx.WriteHtmlView(Layout.messagePage ctx t t.AntiforgeryExpiredTitle t.AntiforgeryExpiredBody)

        | ex ->
            ctx.GetLogger().LogError(ex, "Unhandled error on {Path}", ctx.Request.Path)
            let t = Texts.current ()
            ctx.SetStatusCode 500
            // The message says what to do and names nothing about how this
            // instance is configured.
            return! ctx.WriteHtmlView(Layout.messagePage ctx t t.ErrorTitle t.ErrorBody)
    }
    :> Task

/// Middleware order is load-bearing. Each line is here for a reason:
///   UseForwardedHeaders  first, so scheme and client IP are right before anything reads them
///   UseCors              after routing, before authentication: OpenIddict answers
///                        discovery, keys and revocation inside UseAuthentication, so
///                        the CORS middleware must answer a preflight (204) before
///                        OpenIddict sees it and answers 400; before the rate limiter,
///                        so a 429 carries the header and a browser app can read it
///   UseRateLimiter       after routing, because the limits are endpoint metadata and
///                        there is nothing to read until a route has been chosen; before
///                        authentication and antiforgery, so a refused request costs no
///                        cookie decryption and no password hash
///   UseAuthentication    OpenIddict's server integration runs INSIDE this as an
///                        IAuthenticationRequestHandler, so it must precede UseOxpecker
///   UseAntiforgery       must sit between routing and the endpoints
let configureApp (app: WebApplication) =
    app
        .UseForwardedHeaders()
        // Nothing under /admin may be kept by a browser or a proxy: the page that
        // shows a new client secret or a passkey invitation link would come back
        // from the cache on Back. Set when the response starts, so no handler
        // can leave a different value behind.
        .Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
            if ctx.Request.Path.StartsWithSegments "/admin" then
                ctx.Response.OnStarting(fun () ->
                    ctx.Response.Headers.CacheControl <- "no-store"
                    Task.CompletedTask)

            next.Invoke ctx)
        // Layout.referrerPolicy on every response, unless the handler set its
        // own or it is OpenIddict's form_post answer (see there). Set when the
        // response starts, when both are known.
        .Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
            ctx.Response.OnStarting(fun () ->
                let formPost =
                    match ctx.GetOpenIddictServerRequest() with
                    | null -> false
                    | request -> request.ResponseMode = OpenIddictConstants.ResponseModes.FormPost

                if not formPost && not (ctx.Response.Headers.ContainsKey "Referrer-Policy") then
                    ctx.Response.Headers["Referrer-Policy"] <- Layout.referrerPolicy

                Task.CompletedTask)

            next.Invoke ctx)
        .UseRouting()
        // CORS for exactly five paths and nothing else: the ones a browser-only
        // app fetches (discovery, keys, token, userinfo, revocation). The
        // others (/connect/authorize, /connect/logout, the admin and passkey
        // pages) are navigated to, never fetched cross-origin. It is a path
        // match and not endpoint metadata because discovery, keys and
        // revocation are not Oxpecker routes: OpenIddict answers them inside
        // UseAuthentication. `Request.Path` excludes the PathBase, like the
        // endpoint constants, which are relative.
        .UseWhen(
            (fun ctx ->
                let path = ctx.Request.Path

                [ OpenIddictSetup.DiscoveryEndpoint
                  OpenIddictSetup.JsonWebKeySetEndpoint
                  OpenIddictSetup.TokenEndpoint
                  OpenIddictSetup.UserInfoEndpoint
                  OpenIddictSetup.RevocationEndpoint ]
                |> List.exists (fun endpoint -> path = PathString("/" + endpoint))),
            fun branch -> branch.UseCors BrowserCorsPolicy |> ignore
        )
        .UseRateLimiter()
        .UseAuthentication()
        // No UseAuthorization. Umbral has no authorization model by design
        // (Principle VII): admin access is the `requireOperator` handler
        // combinator, and an app decides what its own users may do. Adding the
        // middleware would mean adding policies, which is the thing we refuse.
        .UseAntiforgery()
        .Use(errorHandler)
        .UseOxpecker(endpoints)
    |> ignore

    app.Run(fun ctx -> Layout.notFound ctx)

/// Asks the active external store once whether it can be used (specs/005
/// research R11): reachable, and Umbral allowed in. Never stops the start: a
/// manager that is down must not take the login server down with it, and
/// secrets already in the cache or the local store keep working. What it finds
/// is logged with what to check, recorded as a `secret_store_failed` event, and
/// kept in the registry's status for the Connectors screen.
///
/// Each adapter's probe reads a name that does not exist and maps the 404 that
/// answers to success, so a `NotFound` that reaches here means something else
/// was not found (a project, a vault) and is a failure like any other. The
/// registry bounds the call at 10 seconds. `/healthz` does not look
/// at this: a restart cannot repair a manager.
let private probeSecretStore (app: WebApplication) (dbConfig: Database.DbConfig) =
    task {
        let secrets = app.Services.GetRequiredService<SecretStores>()
        let active = secrets.Active

        if active.Id <> Secrets.LocalStoreId then
            try
                match! active.Probe Threading.CancellationToken.None with
                | Ok () -> ()
                | Error failure ->
                    let clock = app.Services.GetRequiredService<TimeProvider>()
                    secrets.Status.Fail(failure, clock.GetUtcNow())

                    app.Services
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Umbral.SecretStores")
                        .LogWarning(Texts.current().SecretStoreProbeWarning active.Id failure)

                    do! SecurityEvents.recordStoreProbeFailed dbConfig clock active.Id failure
            with ex ->
                // A probe that throws is a bug in an adapter, not a reason to
                // refuse to start. The type name is logged, never the message,
                // which a vendor SDK may fill with a name or a locator.
                app.Services
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Umbral.SecretStores")
                    .LogWarning("The startup check of the secret store failed with {Exception}.", ex.GetType().Name)
    }

/// Brings the schema up to date and, when the version 9 step ran (task h762),
/// says what it did. Every command that opens the database goes through here,
/// so the step runs once, whichever starts first. A warning when it lowered
/// anything, because those users now read as unverified to the apps.
let private ensureSchema (app: WebApplication) (dbConfig: Database.DbConfig) =
    task {
        let! upgrade = Database.upgradeSchema dbConfig

        match upgrade.EmailVerifiedLowered with
        | None -> ()
        | Some count ->
            let logger =
                app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Umbral.Startup")

            let message =
                "Schema upgrade {From} -> {To}: {Count} users had email_verified set back to false, because no linked account verifies their stored address. Apps see email_verified false for them until a provider verifies that address again. To look for accounts already joined this way, see \"A user may hold someone else's provider account\" on the Troubleshooting page of the documentation, "
                + Texts.TroubleshootingUrl
                + "."

            if count > 0 then
                logger.LogWarning(message, upgrade.FromVersion, upgrade.ToVersion, count)
            else
                logger.LogInformation(message, upgrade.FromVersion, upgrade.ToVersion, count)
    }

/// Everything that must happen before the first request: the schema Umbral owns,
/// OpenIddict's migrations, and the key material.
///
/// Safe to run at startup because a single container is a single writer — there
/// is no multi-instance race to lose.
let initialize (app: WebApplication) =
    task {
        let dbConfig = app.Services.GetRequiredService<Database.DbConfig>()

        do! ensureSchema app dbConfig
        do! probeSecretStore app dbConfig

        // One line, at Information, whichever way it is set: an operator who
        // needs to know which senders are believed should find it in the log
        // without having to remember the default. Not a warning when unset,
        // because unset is the supported setup for a single-proxy compose file.
        app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Umbral.Startup")
            .LogInformation("{TrustedProxies}", TrustedProxies.describe (app.Services.GetRequiredService<TrustedProxies.Trust>()))

        use scope = app.Services.CreateScope()
        let context = scope.ServiceProvider.GetRequiredService<UmbralContext>()
        do! context.Database.MigrateAsync()

        // A fake connector authenticates anyone as whoever they claim to be.
        // If one is configured on an instance that has not explicitly allowed
        // it, refusing to start is the only safe answer: continuing would mean
        // serving a live authentication bypass, and a warning in a log nobody
        // reads is not a mitigation.
        let! connectors = ConnectorStore.listAll dbConfig
        let gate = app.Services.GetRequiredService<Providers.Fake.FakeGate>()

        if connectors |> List.exists (fun c -> c.Kind = Umbral.Domains.Connectors.Fake) && not gate.IsAllowed then
            failwith gate.Explain

        let! configured = AdminAuth.isConfigured dbConfig

        // An unconfigured instance is claimable by whoever reaches /admin/setup
        // first, so claiming it also needs a token only someone who can read
        // the container log has. appsettings.json keeps the startup chatter
        // quiet enough that it stays near the end of the log.
        if not configured then
            let origin = app.Services.GetRequiredService<PublicOrigin.PublicOrigin>()
            let token = app.Services.GetRequiredService<AdminAuth.SetupToken>()
            Console.WriteLine(token.Banner origin.Text)
    }
// The certificates are NOT touched here. configureServices already created
// them and handed them to OpenIddict, and loading a second copy only to
// dispose it releases the shared key handle underneath the first — on macOS
// that surfaces from OpenIddict as "at least one of the registered
// certificates must be valid", which names neither the disposal nor the key.

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

/// FR-017b: a way back in for an operator who has forgotten the credential.
///
/// Deliberately not a web route. The authority it requires is the ability to
/// run a process against the data directory — `docker exec` into the container,
/// or shell access on the host — which is strictly more than reaching the admin
/// UI. A "forgot your credential" screen on a login server is a second front
/// door, and this product has exactly one on purpose.
///
///     docker exec -it umbral /app/Umbral reset-credential
///
/// Takes only what it needs. `initialize` is not run: it would print the
/// first-run setup banner, with a token that nothing is waiting to receive,
/// above the very credential this prints. The schema is ensured here because
/// the settings table must exist, which on an instance that never started is
/// not a given.
///
/// On an instance nobody has claimed yet this claims it. That is acceptable:
/// being able to run this process is more authority than the setup token
/// represents, and refusing would only send the operator to a page that wants a
/// token from a log they may not have kept. The output says which happened.
///
/// It also switches password sign-in back on, so a password forgotten while the
/// switch was off is recoverable from the host (FR-017's promise). Operator
/// passkeys are kept unless `--forget-passkeys` is given, which removes them
/// all, for a stolen device.
let resetCredential (app: WebApplication) (forgetPasskeys: bool) (write: string -> unit) =
    task {
        let dbConfig = app.Services.GetRequiredService<Database.DbConfig>()
        let clock = app.Services.GetRequiredService<TimeProvider>()

        do! ensureSchema app dbConfig
        let! wasConfigured = AdminAuth.isConfigured dbConfig

        // Generated rather than prompted: a reset run non-interactively through
        // `docker exec` has nowhere to read a typed value from, and a generated
        // one is stronger than what a locked-out operator would pick in a hurry.
        let replacement = Umbral.Domains.Ids.newClientSecret ()

        match! AdminAuth.replaceCredential dbConfig clock replacement with
        | AdminAuth.Set ->
            do!
                SecurityEvents.record
                    dbConfig
                    clock
                    { SecurityEvents.event
                          SecurityEvents.OperatorCredentialChanged
                          SecurityEvents.Success
                          SecurityEvents.Actor.System with
                        Detail = Some(if wasConfigured then "offline_reset" else "offline_claim") }

            // The credential is replaced first, so a failure there leaves the
            // passkeys and the switch as they were.
            let! passkeys =
                task {
                    if forgetPasskeys then
                        let! removed = OperatorPasskeys.removeAll dbConfig

                        if removed > 0 then
                            do!
                                SecurityEvents.record
                                    dbConfig
                                    clock
                                    { SecurityEvents.event
                                          SecurityEvents.OperatorPasskeyRemoved
                                          SecurityEvents.Success
                                          SecurityEvents.Actor.System with
                                        Detail = Some $"offline_reset count={removed}" }

                        return removed
                    else
                        return! OperatorPasskeys.count dbConfig
                }

            let! wasOn = OperatorPasskeys.passwordSignInEnabled dbConfig
            do! OperatorPasskeys.turnPasswordSignInOn dbConfig clock

            if not wasOn then
                do!
                    SecurityEvents.record
                        dbConfig
                        clock
                        { SecurityEvents.event
                              SecurityEvents.OperatorPasswordSignInChanged
                              SecurityEvents.Success
                              SecurityEvents.Actor.System with
                            Detail = Some "offline_reset" }

            // Moved once more, last: the replacement moved it first, and a
            // passkey sign-in that landed before the passkeys went (or the
            // switch came on) would otherwise keep its session.
            do! AdminAuth.endEverySession dbConfig clock

            let line = String.replicate 64 "="

            let headline =
                if wasConfigured then
                    [ "  The operator password has been replaced." ]
                else
                    [ "  This instance had no operator yet. It is now claimed:"
                      "  the first-run setup page will no longer ask for a token." ]

            let count = if passkeys = 1 then "1 operator passkey" else $"{passkeys} operator passkeys"
            let is' = if passkeys = 1 then "is" else "are"
            let was' = if passkeys = 1 then "was" else "were"

            let passkeyLine =
                if passkeys = 0 then []
                elif forgetPasskeys then [ $"  {count} {was'} removed." ]
                else [ $"  {count} {is'} kept. Add --forget-passkeys to remove them." ]

            // Only an instance that had an operator has a switch or passkeys to
            // speak of; a fresh claim has neither.
            let state =
                if wasConfigured then
                    [ ""; "  Password sign-in is on." ] @ passkeyLine
                else
                    []

            write (
                String.concat
                    Environment.NewLine
                    ([ ""; line ]
                     @ headline
                     @ [ ""; $"  New password     {replacement}" ]
                     @ state
                     @ [ ""
                         "  Sign in with it, then change it from the admin area."
                         "  This is the only time it is shown."
                         line
                         "" ])
            )

            return 0

        | other ->
            eprintfn $"The credential could not be replaced: {other}"
            return 1
    }

/// The container health check: `/app/Umbral healthcheck`, exit 0 when this
/// instance's own /healthz answers 200 and 1 otherwise.
///
/// A subcommand of the app rather than curl or wget because the aspnet image
/// ships neither, and installing one only to probe localhost would grow the
/// image and its attack surface. Nothing is built here: no web host, no data
/// directory, no database — only a request to the one that is already running.
/// The port is the one the image already sets for the listener
/// (ASPNETCORE_HTTP_PORTS), so there is no second setting to keep in step.
/// Brings every connector secret into the store `UMBRAL__SECRET_STORE` names
/// (specs/005 research R8). An offline command like `reset-credential`, and for
/// the same reasons: the authority it needs is running a process against the
/// data directory, and `initialize` is skipped because its banner and probe
/// have nothing to say here. The schema is ensured because an instance that
/// never started has no connectors table to read. It may run beside the live
/// instance; the switch of each connector is guarded (see `SecretMove`).
let moveSecrets (app: WebApplication) (dryRun: bool) (write: string -> unit) =
    task {
        let dbConfig = app.Services.GetRequiredService<Database.DbConfig>()
        do! ensureSchema app dbConfig

        return!
            Umbral.Infrastructure.SecretMove.run
                (app.Services.GetRequiredService<SecretStores>())
                (app.Services.GetRequiredService<SecretStoreSettings.SecretStoreSettings>())
                dbConfig
                (app.Services.GetRequiredService<TimeProvider>())
                dryRun
                write
    }

let healthcheck () =
    let port =
        match Environment.GetEnvironmentVariable "ASPNETCORE_HTTP_PORTS" with
        | null -> "8080"
        | ports ->
            ports.Split([| ';'; ',' |], StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
            |> Array.tryHead
            |> Option.defaultValue "8080"

    try
        use client = new HttpClient(Timeout = TimeSpan.FromSeconds 5.0)
        use response = client.GetAsync($"http://localhost:{port}/healthz").GetAwaiter().GetResult()
        if response.IsSuccessStatusCode then 0 else 1
    with _ ->
        1

[<EntryPoint>]
let main args =
    // Subcommands are decided before anything is built, so that none of them
    // can start a second web host or print the first-run banner.
    if args |> Array.contains "healthcheck" then
        healthcheck ()
    else
        let app = buildApp args ignore

        if args |> Array.contains "reset-credential" then
            (resetCredential app (args |> Array.contains "--forget-passkeys") Console.WriteLine).GetAwaiter().GetResult()
        elif args |> Array.contains "move-secrets" then
            (moveSecrets app (args |> Array.contains "--dry-run") Console.WriteLine).GetAwaiter().GetResult()
        else
            (initialize app).GetAwaiter().GetResult()
            app.Run()
            0
