/// Recovery: an invitation the operator creates for an existing user, from the
/// Users screen, that lets that person add a passkey to their account.
module Umbral.Handlers.Admin.UserPasskeys

open System
open Oxpecker
open Umbral.Infrastructure
open Umbral.Views

let handler (id: string) : EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! _ = WebForms.readValidated ctx

            match! Umbral.Infrastructure.Users.findById config id with
            | None -> return! WebForms.redirectWithNotice ctx "/admin/users" "user-not-found"
            | Some _ ->
                match! ConnectorStore.findBySlug config "passkey" with
                | None -> return! WebForms.redirectWithNotice ctx "/admin/users" "passkey-not-enabled"
                | Some _ ->
                    let! _, secret = PasskeyInvitations.create config clock "passkey" (Some id) ""
                    let origin = ctx.GetService<PublicOrigin.PublicOrigin>()

                    PasskeyConnector.InviteReveal.stash
                        ctx
                        $"user:{id}"
                        (Umbral.Domains.Passkeys.invitationLink origin.Text secret)

                    do!
                        SecurityEvents.record
                            config
                            clock
                            { SecurityEvents.event
                                  SecurityEvents.PasskeyInvitationCreated
                                  SecurityEvents.Success
                                  SecurityEvents.Actor.Operator with
                                ConnectorSlug = Some "passkey"
                                UserId = Some id
                                Detail = Some "for=user"
                                RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string }

                    return! WebForms.redirectWithNoticeAbout ctx "/admin/users" "passkey-user-invited" [ "user", id ]
        })

let removeHandler (id: string) (pid: string) : EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! _ = WebForms.readValidated ctx

            match! Passkeys.findById config pid with
            | Some pk when pk.UserId = id ->
                let! _ = Passkeys.remove config pid

                do!
                    SecurityEvents.record
                        config
                        clock
                        { SecurityEvents.event
                              SecurityEvents.PasskeyRemoved
                              SecurityEvents.Success
                              SecurityEvents.Actor.Operator with
                            ConnectorSlug = Some "passkey"
                            UserId = Some id
                            Detail = Some "by=operator"
                            RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string }

                return! WebForms.redirectWithNotice ctx "/admin/users" "passkey-removed"
            | _ -> return! WebForms.redirectWithNotice ctx "/admin/users" "passkey-not-found"
        })

let endpoint =
    [ POST
          [ routef "/admin/users/{%s}/invite" handler
            routef "/admin/users/{%s}/passkeys/{%s}/remove" removeHandler ] ]
