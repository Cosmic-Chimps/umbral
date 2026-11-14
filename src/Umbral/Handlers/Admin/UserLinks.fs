/// Removing one linked provider account from a user (specs/018-remove-a-link,
/// FR-002 to FR-005).
///
/// The row's button posts here without `confirm`: nothing changes, and the Users
/// page is drawn again at 422 with that row's confirm block open. The block's
/// button posts again with `confirm=remove`, which is the removal.
module Umbral.Handlers.Admin.UserLinks

open System
open System.Threading.Tasks
open Oxpecker
open Umbral.Domains
open Umbral.Infrastructure

/// FR-005. The connector the screen labels the link with: for OpenID Connect
/// the one holding the subject's issuer, for another kind the only connector of
/// that kind; None when there is none or more than one. A slug, never a subject.
let private connectorSlugFor (config: Database.DbConfig) (kind: string) (subject: string) : Task<string option> =
    task {
        let! connectors = ConnectorStore.listAll config

        match Connectors.parseKind kind with
        | Some Connectors.Oidc ->
            match Connectors.parseOidcSubject subject with
            | Some(issuer, _) ->
                return
                    connectors
                    |> List.tryFind (fun c -> c.IssuerUrl = Some issuer)
                    |> Option.map (fun c -> c.Slug)
            | None -> return None
        | Some parsed ->
            match connectors |> List.filter (fun c -> c.Kind = parsed) with
            | [ only ] -> return Some only.Slug
            | _ -> return None
        | None -> return None
    }

let handler (id: string) (fingerprint: string) : EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! form = WebForms.readValidated ctx
            let confirmed = form.["confirm"].ToString() = "remove"
            let term = form.["search"].ToString()

            match! Umbral.Infrastructure.Users.findById config id with
            | None -> return! WebForms.redirectWithNotice ctx "/admin/users" "user-not-found"
            | Some _ ->
                if not confirmed then
                    let! links = UserStore.linkedAccountDetails config id

                    let held =
                        links |> List.exists (fun l -> Linking.fingerprint l.Kind l.Subject = fingerprint)

                    if held then
                        return! Users.render ctx term None None (Some(id, fingerprint)) 422
                    else
                        return! WebForms.redirectWithNotice ctx "/admin/users" "user-link-not-found"
                else
                    match! UserStore.removeLink config clock id fingerprint with
                    | UserStore.LinkRemoved(kind, subject, lowered, passkeys) ->
                        let! slug = connectorSlugFor config kind subject
                        let detail = $"kind={kind};passkeys={passkeys}" + (if lowered then ";lowered=1" else "")

                        do!
                            SecurityEvents.record
                                config
                                clock
                                { SecurityEvents.event
                                      SecurityEvents.UserUnlinked
                                      SecurityEvents.Success
                                      SecurityEvents.Actor.Operator with
                                    ConnectorSlug = slug
                                    UserId = Some id
                                    Detail = Some detail
                                    RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string }

                        return! WebForms.redirectWithNotice ctx "/admin/users" "user-link-removed"
                    | UserStore.LinkLastWayIn ->
                        return! WebForms.redirectWithNoticeAbout ctx "/admin/users" "user-link-last" [ "user", id ]
                    | UserStore.LinkNotFound -> return! WebForms.redirectWithNotice ctx "/admin/users" "user-link-not-found"
        })

let endpoint = POST [ routef "/admin/users/{%s}/links/{%s}/remove" handler ]
