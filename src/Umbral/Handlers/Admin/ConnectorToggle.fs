/// Enabling and disabling a connector, without a restart (FR-010).
///
/// One of the three interactions where htmx earns its place (D11): flipping a
/// switch should not reload the page. It is a real form underneath, so it works
/// identically with htmx absent — which is what lets one test drive both paths.
module Umbral.Handlers.Admin.ConnectorToggle

open Oxpecker
open Umbral.Infrastructure

let handler (slug: string) : EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<System.TimeProvider>()
            let! _ = WebForms.readValidated ctx
            let! existing = ConnectorStore.findBySlug config slug

            match existing with
            | None -> return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-unknown"
            | Some connector ->

            let turningOff = connector.IsEnabled

            // FR-023: warn before the action that leaves nobody able to sign in
            // anywhere. Refusing outright would be worse — an operator may well
            // mean to close the door — so it takes a confirm flag, which the
            // button sets once the warning has been shown.
            let! remaining = ConnectorStore.enabledCountExcluding config slug
            let confirmed = ctx.TryGetQueryValue "confirm" = Some "yes"

            if turningOff && remaining = 0 && not confirmed then
                return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-last-enabled"
            else

            do! ConnectorStore.setEnabled config clock slug (not connector.IsEnabled)

            do!
                SecurityEvents.record
                    config
                    clock
                    { SecurityEvents.event
                          (if turningOff then
                               SecurityEvents.ConnectorDisabled
                           else
                               SecurityEvents.ConnectorEnabled)
                          SecurityEvents.Success
                          SecurityEvents.Actor.Operator with
                        ConnectorSlug = Some slug
                        RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string }

            return!
                WebForms.redirectWithNotice
                    ctx
                    "/admin/connectors"
                    (if turningOff then "connector-disabled" else "connector-enabled")
        })

let endpoint = POST [ routef "/admin/connectors/{%s}/toggle" handler ]
