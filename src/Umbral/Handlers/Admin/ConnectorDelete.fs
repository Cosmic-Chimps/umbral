/// Deleting a connector (T077).
///
/// What happens to the people who signed in through it: nothing. `linked_accounts`
/// has a foreign key to `users` and NONE to `connectors` — a link is keyed on the
/// provider kind and the provider's own subject (data-model.md), so deleting the
/// connector removes the way in and leaves every account, and every link, as it
/// was. Add a Discord connector again and the same people sign in to the same
/// `sub`, which is what keeps the apps that already know them working. Nothing
/// is revoked either: their sessions and tokens belong to the user, not to the
/// connector, so only NEW sign-ins through it stop.
///
/// What it does do to apps: the connector leaves every app's chosen set (the
/// `app_connectors` rows cascade), and an app limited to a chosen set stays
/// limited even when the set empties, rather than widening to every shared
/// connector (FR-010, FR-011). The Connectors screen warns, naming the apps it
/// would leave with no way to sign in, before this runs.
///
/// The confirmation on the Connectors screen says exactly that; this handler
/// carries it out. It also erases the stored client secret, since a deleted
/// connector's credential has no reason to stay on disk.
module Umbral.Handlers.Admin.ConnectorDelete

open OpenIddict.Abstractions
open Oxpecker
open Umbral.Infrastructure
open Umbral.Infrastructure.SecretStores.Registry

module SecretStoreSettings = Umbral.Infrastructure.SecretStores.Settings

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

            // FR-023, the same shape as the toggle: warn before the action that
            // leaves nobody able to sign in anywhere, and let a flag set by the
            // screen's own confirmation carry the operator through. Only an
            // ENABLED connector counts — deleting a disabled one changes
            // nothing about who can sign in.
            let! remaining = ConnectorStore.enabledCountExcluding config slug
            let confirmed = ctx.TryGetQueryValue "confirm" = Some "yes"

            // FR-010: the warning that names the apps this would leave with no
            // way to sign in. It wins over the last-door one when both apply.
            let! connectors = ConnectorStore.listAll config
            let! apps = AppConnectors.appsWithChoices config (ctx.GetService<IOpenIddictApplicationManager>())
            let after = connectors |> List.filter (fun c -> c.Slug <> slug)
            let stranded = AppConnectors.strandedByChange apps connectors after

            if not stranded.IsEmpty && not confirmed then
                return!
                    WebForms.redirectWithNoticeAbout
                        ctx
                        "/admin/connectors"
                        "connector-delete-strands-apps"
                        [ "slug", slug ]
            elif connector.IsEnabled && remaining = 0 && not confirmed then
                return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-delete-last-enabled"
            else

            do! ConnectorStore.delete config slug

            let remoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string

            // The event is recorded with the row, before the store is asked: the
            // connector is gone whatever the store says, and the record of it
            // must not depend on a manager that may be down (FR-010).
            do!
                SecurityEvents.record
                    config
                    clock
                    { SecurityEvents.event SecurityEvents.ConnectorDeleted SecurityEvents.Success SecurityEvents.Actor.Operator with
                        ConnectorSlug = Some slug
                        RemoteIp = remoteIp }

            // After the row, not before: a failure in between leaves an
            // orphaned secret, where the other order would leave a connector
            // that exists and can no longer authenticate.
            let secrets = ctx.GetService<SecretStores>()

            let! leftBehind =
                match Secrets.SecretRef.Decode connector.ClientSecretRef with
                | Error _ -> System.Threading.Tasks.Task.FromResult None
                | Ok reference ->
                    task {
                        let! outcome =
                            match secrets.For reference.Store with
                            | Ok store -> store.Delete(reference, ctx.RequestAborted)
                            | Error failure -> System.Threading.Tasks.Task.FromResult(Error failure)

                        secrets.Evict reference

                        match outcome with
                        | Ok() -> return None
                        | Error failure -> return Some(reference.Store, failure)
                    }

            match leftBehind with
            | None -> return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-deleted"
            | Some(store, failure) ->
                do! SecurityEvents.recordStoreFailure config clock SecurityEvents.Actor.Operator slug remoteIp store "delete" failure

                // The row is gone, so the secret's name is derived from what the
                // row held and carried to the screen in the single-use notice
                // cookie: never from the URL, which anyone can write. It is the
                // name this instance gave the secret; an operator who changed
                // the prefix since sees the current one.
                let name =
                    (Secrets.SecretName.derive
                        (ctx.GetService<SecretStoreSettings.SecretStoreSettings>().Prefix)
                        slug
                        connector.CreatedUtc)
                        .In store

                return!
                    WebForms.NoticeReason.redirect
                        ctx
                        "/admin/connectors"
                        "connector-secret-left-behind"
                        slug
                        $"{store}|{name}"
        })

let endpoint = POST [ routef "/admin/connectors/{%s}/delete" handler ]
