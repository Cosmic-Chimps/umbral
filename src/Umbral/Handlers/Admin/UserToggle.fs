/// Enabling and disabling a user (T084).
///
/// Disabling must cut the person off, not just annotate them — so this
/// revokes every OpenIddict authorization for their `sub` before returning.
/// FR-022 is what makes that non-optional: a flag alone would leave an
/// already-issued access or refresh token working until it happened to expire,
/// and Umbral's reference tokens exist specifically so revocation is checked
/// on next use rather than at expiry.
///
/// One of the three htmx interactions (D11, contract test-hook table): on an
/// `HX-Request` this returns the re-rendered row alone; otherwise it is a
/// plain POST -> redirect -> GET, so the same route works with JavaScript off.
module Umbral.Handlers.Admin.UserToggle

open Oxpecker
open Oxpecker.Htmx
open OpenIddict.Abstractions
open Umbral.Infrastructure
open Umbral.Handlers.Admin.Users

/// F#'s `task { for x in ... }` binds `seq<'T>`, not `IAsyncEnumerable<'T>`,
/// so OpenIddict's authorization enumerable is drained by hand instead.
let private revokeAll (source: System.Collections.Generic.IAsyncEnumerable<obj>) (authorizations: IOpenIddictAuthorizationManager) =
    task {
        let enumerator = source.GetAsyncEnumerator()
        let mutable go = true

        while go do
            let! hasNext = enumerator.MoveNextAsync()

            if hasNext then
                let! _ = authorizations.TryRevokeAsync enumerator.Current
                ()
            else
                go <- false

        do! enumerator.DisposeAsync()
    }

let handler (id: string) : EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let t = Umbral.Views.Texts.current ()
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<System.TimeProvider>()
            let! _ = WebForms.readValidated ctx

            match! Umbral.Infrastructure.Users.findById config id with
            | None -> return! WebForms.redirectWithNotice ctx "/admin/users" "user-not-found"
            | Some user ->

            let enabling = not user.IsEnabled
            do! Umbral.Infrastructure.Users.setEnabled config id enabling

            if not enabling then
                let authorizations = ctx.GetService<IOpenIddictAuthorizationManager>()
                do! revokeAll (authorizations.FindBySubjectAsync id) authorizations

            let remoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string

            do!
                SecurityEvents.record
                    config
                    clock
                    { SecurityEvents.event
                          (if enabling then SecurityEvents.UserEnabled else SecurityEvents.UserDisabled)
                          SecurityEvents.Success
                          SecurityEvents.Actor.Operator with
                        UserId = Some id
                        RemoteIp = remoteIp }

            let isHtmx = ctx.TryGetHeaderValue HxRequestHeader.Request |> Option.isSome

            if isHtmx then
                let! refreshed = Umbral.Infrastructure.Users.findById config id

                match refreshed with
                | None -> return! WebForms.redirectWithNotice ctx "/admin/users" "user-not-found"
                | Some refreshedUser ->
                    let! linked = linkedProviders config id

                    let! passkeys = Passkeys.listForUser config id
                    let! passkeyConnector = ConnectorStore.findBySlug config "passkey"

                    let fragment =
                        row
                            ctx
                            t
                            { User = refreshedUser
                              LinkedProviders = linked
                              Passkeys = passkeys
                              CanInvite = passkeyConnector.IsSome
                              RevealedLink = None }

                    return! ctx.WriteHtmlView fragment
            else
                return!
                    WebForms.redirectWithNotice
                        ctx
                        "/admin/users"
                        (if enabling then "user-enabled" else "user-disabled")
        })

let endpoint = POST [ routef "/admin/users/{%s}/toggle" handler ]
