/// Deleting an app. The confirmation shown on the Apps screen names what
/// breaks — every token already issued to it stops working immediately — so
/// this handler itself only has to act once that confirmation has happened.
module Umbral.Handlers.Admin.AppDelete

open OpenIddict.Abstractions
open Oxpecker
open Umbral.Infrastructure

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

            // Deleting the OpenIddict application cascades to its
            // authorizations and tokens on OpenIddict's own tables, which is
            // what makes "every token stops working immediately" true.
            do! applications.DeleteAsync application

            // No foreign key points into OpenIddict's tables, so the app's
            // connector choice is removed here (FR-005). A later app never
            // inherits it: client ids are generated and never reused.
            do! AppConnectors.deleteFor config clientId

            // The same for its branding: the row, and the logo bytes in it, go
            // with the app, so the logo's address returns 404 from this moment.
            // Not logged apart from the deletion itself.
            do! BrandingStore.deleteFor config clientId

            let remoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string

            do!
                SecurityEvents.record
                    config
                    clock
                    { SecurityEvents.event SecurityEvents.AppDeleted SecurityEvents.Success SecurityEvents.Actor.Operator with
                        ClientId = Some clientId
                        RemoteIp = remoteIp }

            return! WebForms.redirectWithNotice ctx "/admin/apps" "app-deleted"
        })

let endpoint = POST [ routef "/admin/apps/{%s}/delete" handler ]
