/// Beginning the hop to a provider.
///
/// The connector is read from SQLite inside this request. That is the entire
/// payoff of hand-rolling the OAuth client (research.md D1): a credential
/// replaced in the admin form a second ago is already in use here, with no
/// options cache to invalidate and no restart.
module Umbral.Handlers.Connect.Start

open Microsoft.AspNetCore.DataProtection
open Microsoft.AspNetCore.Builder
open Oxpecker
open Umbral.Infrastructure
open Umbral.Views

let handler (slug: string) : EndpointHandler =
    fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let! connector = ConnectorStore.findBySlug config slug

            match connector with
            | None
            | Some { IsEnabled = false } ->
                // Disabled is indistinguishable from absent on purpose: a
                // turned-off connector should not be discoverable by probing.
                return! Layout.notFound ctx

            | Some connector ->
                let registry = ctx.GetService<ProviderAdapters.Registry>()

                match registry.For connector.Kind with
                | None ->
                    return! Layout.notFound ctx

                | Some adapter ->
                    let returnUrl =
                        Picker.sanitizeReturnUrl (ctx.TryGetQueryValue "returnUrl" |> Option.toObj)

                    let started =
                        OAuthRoundTrip.start
                            (ctx.GetService<IDataProtectionProvider>())
                            (ctx.GetService<System.TimeProvider>())
                            ctx
                            connector.Slug
                            returnUrl

                    // From the configured public origin, never from this
                    // request: it must byte-match what the operator registered
                    // in the provider's console, and behind a proxy the request
                    // says something else entirely.
                    let origin = ctx.GetService<PublicOrigin.PublicOrigin>()
                    let redirectUri = PublicOrigin.callbackUri origin connector.Slug

                    let url =
                        adapter.BuildAuthorizeUrl
                            { Connector = connector
                              RedirectUri = redirectUri
                              State = started.Correlation.State
                              CodeChallenge = started.CodeChallenge
                              Nonce = started.Correlation.Nonce }

                    ctx.Response.Redirect(url.ToString())
                    return ()
        }

let endpoint =
    GET [ routef "/connect/{%s}/start" handler |> configureEndpoint _.RequireRateLimiting(RateLimits.SignIn) ]
