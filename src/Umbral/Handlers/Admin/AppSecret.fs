/// Regenerating an app's client secret.
///
/// There is no "show it again": OpenIddict stores only a PBKDF2 hash, so the
/// only recovery from a lost secret is a new one. The new value goes through
/// the same single-use reveal as creation (Apps.fs) and the old one stops
/// authenticating the app immediately — the confirmation on the Apps screen
/// says so before this ever runs.
module Umbral.Handlers.Admin.AppSecret

open OpenIddict.Abstractions
open Oxpecker
open Umbral.Infrastructure
open Umbral.Handlers.Admin

let handler (clientId: string) : EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<System.TimeProvider>()
            let! _ = WebForms.readValidated ctx

            let applications = ctx.GetService<IOpenIddictApplicationManager>()
            let! application = applications.FindByClientIdAsync clientId

            match application with
            | null -> return! WebForms.redirectWithNotice ctx "/admin/apps" "app-not-found"
            | application ->

            let! clientType = applications.GetClientTypeAsync application

            if clientType = OpenIddictConstants.ClientTypes.Public then
                // A public client was never issued a secret — nothing to
                // rotate. The screen does not offer this button for one, but
                // the route is reachable directly, so refuse rather than mint
                // a secret OpenIddict would then ignore.
                return! WebForms.redirectWithNotice ctx "/admin/apps" "app-no-secret"
            else

            let newSecret = Umbral.Domains.Ids.newClientSecret ()
            let! _ = applications.UpdateAsync(application, newSecret)

            let remoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string

            do!
                SecurityEvents.record
                    config
                    clock
                    { SecurityEvents.event
                          SecurityEvents.AppSecretRotated
                          SecurityEvents.Success
                          SecurityEvents.Actor.Operator with
                        ClientId = Some clientId
                        RemoteIp = remoteIp }

            // Same single-use reveal mechanism as creation: stashed in memory,
            // consumed by the very next GET /admin/apps, through the cookie.
            Apps.SecretReveal.stash ctx clientId newSecret

            return!
                WebForms.redirectWithNotice ctx "/admin/apps" "app-secret-regenerated"
        })

let endpoint = POST [ routef "/admin/apps/{%s}/secret" handler ]
