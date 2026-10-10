module Umbral.Program

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Oxpecker

// ---------------------------------------------------------------------------
// Endpoints
//
// One vertical slice per endpoint, see Handlers/. Every slice exposes `endpoint`
// (or a list of them, consumed with `yield!`) and it MUST appear here: a handler
// missing from this list compiles, serves nothing, and reports no error anywhere.
// ---------------------------------------------------------------------------

let endpoints: Endpoint list = []

// ---------------------------------------------------------------------------
// Composition
// ---------------------------------------------------------------------------

let configureServices (_config: IConfiguration) (services: IServiceCollection) =
    services.AddRouting().AddOxpecker() |> ignore

/// Middleware order is load-bearing. Each line is here for a reason:
///   UseForwardedHeaders  first, so scheme and client IP are right before anything reads them
///   UseAuthentication    OpenIddict's server integration runs INSIDE this as an
///                        IAuthenticationRequestHandler, so it must precede UseOxpecker
///   UseAntiforgery       must sit between routing and the endpoints
let configureApp (app: WebApplication) =
    app.UseForwardedHeaders().UseRouting().UseOxpecker(endpoints) |> ignore

    app.Run(fun ctx ->
        ctx.SetStatusCode 404
        ctx.WriteText "Not found")

/// The web app, composable so integration tests host the real thing on a TestServer.
///
/// This seam is why `customize` exists: a test points the app at a temp SQLite file,
/// stubs the provider HttpMessageHandler and injects a fake TimeProvider, and no
/// production code knows it is under test. Retrofitting it would mean rewriting
/// this file and every test, which is why it is here before anything else.
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
    app.Run()
    0
