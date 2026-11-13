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
open Umbral.Infrastructure.SecretStores.Registry
open Umbral.Handlers.Admin.ConnectorPaths

/// What turning on an Apple connector found out (specs/013 FR-008).
type private AppleEnable =
    | AppleUsable
    /// The key cannot sign, or the row has no Team ID and Key ID to sign with.
    | AppleKeyUnusable
    | AppleCannotBeReached of reason: string
    /// The key could not be read from its secret store: the store and the cause word.
    | AppleStoreFailed of reason: string

/// Read the connector's key from its secret store, the way the callback reads it
/// at the moment of use, and run the Apple check with it: sign first, then ask
/// Apple for its discovery document and keys. Neither the key nor what the store
/// said reaches the answer: the reason carries the store's id and a cause word.
let private checkApple (ctx: Microsoft.AspNetCore.Http.HttpContext) (connector: Connector) =
    task {
        let config = ctx.GetService<Database.DbConfig>()
        let clock = ctx.GetService<System.TimeProvider>()
        let secrets = ctx.GetService<SecretStores>()
        let remoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string

        match connector.AppleKey with
        | None -> return AppleKeyUnusable
        | Some keyIds ->

        let store, reading =
            match Secrets.SecretRef.Decode connector.ClientSecretRef with
            | Ok reference ->
                reference.Store,
                secrets.Read(
                    reference,
                    (fun cause ->
                        SecurityEvents.recordStoreStale config clock connector.Slug remoteIp reference.Store cause),
                    ctx.RequestAborted
                )
            | Error _ -> "unknown", System.Threading.Tasks.Task.FromResult(Error Secrets.NotFound)

        match! reading with
        | Error cause ->
            do!
                SecurityEvents.recordStoreFailure
                    config
                    clock
                    SecurityEvents.Actor.Operator
                    connector.Slug
                    remoteIp
                    store
                    "get"
                    cause

            return AppleStoreFailed $"{store}: {cause.Cause}"
        | Ok pem ->
            match!
                Providers.Apple.check
                    (ctx.GetService<OidcDiscovery.OidcDiscovery>())
                    clock
                    keyIds
                    connector.ClientId
                    pem
            with
            | Providers.Apple.Ready -> return AppleUsable
            | Providers.Apple.KeyUnusable -> return AppleKeyUnusable
            | Providers.Apple.AppleUnreachable reason -> return AppleCannotBeReached reason
    }

let handler (slug: string) : EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<System.TimeProvider>()
            let! _ = WebForms.readValidated ctx
            let! existing = ConnectorStore.findBySlug config slug

            match existing with
            | None -> return! WebForms.redirectWithNotice ctx listPath "connector-unknown"
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
                    WebForms.NoticeReason.redirect ctx (detailPath slug) "connector-strands-apps" "slug" slug "disable"
            elif turningOff && remaining = 0 && not confirmed then
                return! WebForms.redirectWithNotice ctx (detailPath slug) "connector-last-enabled"
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

            // The same, for Apple, whose key is read from its store: switching on
            // proves the key signs and Apple answers; switching off asks nothing.
            let! apple =
                match connector.Kind with
                | Apple when not turningOff ->
                    task {
                        let! outcome = checkApple ctx connector
                        return Some outcome
                    }
                | _ -> System.Threading.Tasks.Task.FromResult None

            // Microsoft is asked for the discovery document of the stored audience
            // before it is switched on; a row with no readable audience cannot work.
            // Switching off asks nothing.
            let! microsoft =
                match connector.Kind with
                | Microsoft when not turningOff ->
                    match connector.MicrosoftAudience with
                    | None ->
                        System.Threading.Tasks.Task.FromResult(
                            Some(OidcDiscovery.Wrong "no account type is chosen for this connector")
                        )
                    | Some audience ->
                        task {
                            let! result =
                                ctx
                                    .GetService<OidcDiscovery.OidcDiscovery>()
                                    .CheckAt(
                                        Umbral.Domains.Microsoft.documentBase audience,
                                        Umbral.Domains.Microsoft.discoveryIssuer audience
                                    )

                            return Some result
                        }
                | _ -> System.Threading.Tasks.Task.FromResult None

            let refuseApple (notice: string) (reason: string) =
                WebForms.NoticeReason.redirect ctx (detailPath slug) notice "slug" slug (WebForms.truncate 300 reason)

            // Bluesky is switched on only at an address it can work at: `https`, no
            // port (or the development loopback). Switching off asks nothing.
            let blueskyBlocked =
                connector.Kind = Bluesky
                && not turningOff
                && (let allowDev = (ctx.GetService<Providers.Bluesky.Options>()).AllowDev

                    match Umbral.Domains.Bluesky.clientIdFor allowDev (ctx.GetService<PublicOrigin.PublicOrigin>()).Uri with
                    | Error _ -> true
                    | Ok _ -> false)

            if blueskyBlocked then
                return! WebForms.redirectWithNotice ctx (detailPath slug) "connector-enable-bluesky-origin"
            else

            match apple with
            | Some AppleKeyUnusable -> return! WebForms.redirectWithNotice ctx (detailPath slug) "connector-enable-apple-key"
            | Some(AppleCannotBeReached reason) -> return! refuseApple "connector-enable-apple-unreachable" reason
            | Some(AppleStoreFailed reason) -> return! refuseApple "connector-enable-apple-store" reason
            | Some AppleUsable
            | None ->

            match microsoft with
            | Some(OidcDiscovery.Unreachable reason) -> return! refuseApple "connector-enable-microsoft-unreachable" reason
            | Some(OidcDiscovery.Wrong reason) -> return! refuseApple "connector-enable-microsoft-wrong" reason
            | Some(OidcDiscovery.Discovered _)
            | None ->

            match discovery with
            | Some(OidcDiscovery.Unreachable reason) ->
                return!
                    WebForms.NoticeReason.redirect
                        ctx
                        (detailPath slug)
                        "connector-enable-oidc-unreachable"
                        "slug"
                        slug
                        (WebForms.truncate 300 reason)
            | Some(OidcDiscovery.Wrong reason) ->
                return!
                    WebForms.NoticeReason.redirect
                        ctx
                        (detailPath slug)
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
                    (detailPath slug)
                    (if turningOff then "connector-disabled" else "connector-enabled")
        })

let endpoint = POST [ routef "/admin/connectors/{%s}/toggle" (slugOfSegment >> handler) ]
