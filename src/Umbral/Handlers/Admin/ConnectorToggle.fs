/// Enabling and disabling a connector, without a restart (FR-010).
///
/// One of the three interactions where htmx earns its place (D11): flipping a
/// switch should not reload the page. It is a real form underneath, so it works
/// identically with htmx absent — which is what lets one test drive both paths.
module Umbral.Handlers.Admin.ConnectorToggle

open OpenIddict.Abstractions
open Oxpecker
open Umbral.Domains.Connectors
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

            // FR-010: the sharper warning, which names the apps that would be
            // left with no way to sign in. It wins over the last-door one when
            // both apply, because it says who is affected.
            let! connectors = ConnectorStore.listAll config
            let! apps = AppConnectors.appsWithChoices config (ctx.GetService<IOpenIddictApplicationManager>())

            let after =
                connectors
                |> List.map (fun c -> if c.Slug = slug then { c with IsEnabled = false } else c)

            let stranded = if turningOff then AppConnectors.strandedByChange apps connectors after else []

            if not stranded.IsEmpty && not confirmed then
                return!
                    WebForms.redirectWithNoticeAbout
                        ctx
                        "/admin/connectors"
                        "connector-strands-apps"
                        [ "slug", slug ]
            elif turningOff && remaining = 0 && not confirmed then
                return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-last-enabled"
            else

            // Clarification Q2: an OpenID Connect provider is asked about itself
            // before it is switched on, because it may have been saved while
            // unreachable (FR-005a) or have changed since. Switching it off never
            // asks: closing the door must work when the provider is down.
            let! discovery =
                match connector.Kind, connector.IssuerUrl with
                | Oidc, Some issuer when not turningOff ->
                    task {
                        let! result = ctx.GetService<OidcDiscovery.OidcDiscovery>().Check issuer
                        return Some result
                    }
                | _ -> System.Threading.Tasks.Task.FromResult None

            match discovery with
            | Some(OidcDiscovery.Unreachable reason) ->
                return!
                    WebForms.NoticeReason.redirect
                        ctx
                        "/admin/connectors"
                        "connector-enable-oidc-unreachable"
                        "slug"
                        slug
                        (WebForms.truncate 300 reason)
            | Some(OidcDiscovery.Wrong reason) ->
                return!
                    WebForms.NoticeReason.redirect
                        ctx
                        "/admin/connectors"
                        "connector-enable-oidc-wrong"
                        "slug"
                        slug
                        (WebForms.truncate 300 reason)
            | Some(OidcDiscovery.Discovered _)
            | None ->

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
