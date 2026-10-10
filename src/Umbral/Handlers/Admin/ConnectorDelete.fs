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
/// The confirmation on the Connectors screen says exactly that; this handler
/// carries it out. It also erases the stored client secret, since a deleted
/// connector's credential has no reason to stay on disk.
module Umbral.Handlers.Admin.ConnectorDelete

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

            // FR-023, the same shape as the toggle: warn before the action that
            // leaves nobody able to sign in anywhere, and let a flag set by the
            // screen's own confirmation carry the operator through. Only an
            // ENABLED connector counts — deleting a disabled one changes
            // nothing about who can sign in.
            let! remaining = ConnectorStore.enabledCountExcluding config slug
            let confirmed = ctx.TryGetQueryValue "confirm" = Some "yes"

            if connector.IsEnabled && remaining = 0 && not confirmed then
                return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-delete-last-enabled"
            else

            do! ConnectorStore.delete config slug

            // After the row, not before: a failure in between leaves an
            // orphaned ciphertext (harmless, and the local store keeps it in
            // the row anyway), where the other order would leave a connector
            // that exists and can no longer authenticate.
            match Secrets.SecretRef.Decode connector.ClientSecretRef with
            | Ok reference -> do! (ctx.GetService<Secrets.ISecretStore>()).Delete reference
            | Error _ -> ()

            do!
                SecurityEvents.record
                    config
                    clock
                    { SecurityEvents.event SecurityEvents.ConnectorDeleted SecurityEvents.Success SecurityEvents.Actor.Operator with
                        ConnectorSlug = Some slug
                        RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string }

            return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-deleted"
        })

let endpoint = POST [ routef "/admin/connectors/{%s}/delete" handler ]
