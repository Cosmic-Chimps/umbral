/// The page an invitation link opens (specs/008-passkey-connector,
/// contracts/ceremonies.md "The invitation page").
///
/// Opening a link only reads: nothing is spent until the registration
/// completes. The secret is deliberately not written into the page, because the
/// script takes it from the address (`/invite/<secret>`). An unusable link is
/// still a 200 page that says what to do next, with the cause kept in the log.
module Umbral.Handlers.Passkeys.Invitation

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Domains.Passkeys
open Umbral.Infrastructure
open Umbral.Infrastructure.SecurityEvents
open Umbral.Views

let private page (ctx: HttpContext) (t: Texts.Texts) (title: string) (content: HtmlElement) =
    content |> Layout.pageWithScripts [ Assets.passkeys ] ctx t title

let private shell (screen: string) (inner: HtmlElement) =
    div(class' = "flex min-h-[60vh] items-center justify-center") {
        div(class' = "um-card w-full max-w-sm space-y-5").data("screen", screen) { inner }
    }

let private unusable (ctx: HttpContext) (t: Texts.Texts) =
    div(class' = "space-y-5") {
        Components.pageHeading t.PasskeyInviteUnusableTitle (Some t.PasskeyInviteUnusableBody)
    }
    |> shell "invitation-unusable"
    |> page ctx t t.PasskeyInviteUnusableTitle

let private usable (ctx: HttpContext) (t: Texts.Texts) (forUser: string option) =
    let title, lead =
        match forUser with
        | None -> t.PasskeyInviteNewTitle, Some t.PasskeyInviteNewLead
        | Some _ -> t.PasskeyInviteExistingTitle, None

    div(class' = "space-y-5") {
        Components.pageHeading title lead
        SignIn.noticeAlert ctx t

        match forUser with
        | Some name ->
            (p(class' = "text-body-md text-on-surface").data("invitation-account", "") {
                t.PasskeyInviteExistingAccount name
            })
            :> HtmlElement
        | None -> Fragment() :> HtmlElement

        form(method = "post", class' = "space-y-4").data("passkey-invitation", "") {
            ctx.GetAntiforgeryInput()

            match forUser with
            | None ->
                Components.fieldWith
                    "text"
                    []
                    (fun i -> i.attr("required", "required").attr("maxlength", "64") |> ignore)
                    t.PasskeyDisplayNameLabel
                    "displayName"
                    ""
                    None
                    None
            | Some _ -> Fragment() :> HtmlElement

            SignIn.nicknameField t

            button(type' = "submit", class' = Components.btn Components.Primary + " w-full justify-center") {
                t.PasskeyInviteCreateButton
            }
        }

        SignIn.javascriptNote t
    }
    |> shell "invitation"
    |> page ctx t title

let handler (secret: string) : EndpointHandler =
    fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let t = Texts.current ()

            let! found = PasskeyInvitations.findBySecret config clock secret

            // The secret is in this page's address. The page links nowhere
            // else, but a browser would still put the address in `Referer` on
            // any request the page made; this says never to.
            ctx.Response.Headers.Append("Referrer-Policy", "no-referrer")

            match found with
            | Some(invitation, PasskeyInvitations.Open) ->
                let forUser =
                    invitation.UserId
                    |> Option.map (fun _ -> defaultArg invitation.UserDisplayName "")

                return! ctx.WriteHtmlView(usable ctx t forUser)
            | other ->
                // The same page for every cause; the cause is for the operator.
                let reason =
                    match other with
                    | Some(_, state) -> Ceremonies.invitationReason state
                    | None -> InvitationUnknown

                do!
                    record
                        config
                        clock
                        { event SignInFailed Failure Actor.User with
                            UserId = other |> Option.bind (fun (invitation, _) -> invitation.UserId)
                            ConnectorSlug = Some "passkey"
                            RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string
                            Detail = Some(Umbral.Domains.Passkeys.refusalDetail reason Stage.Invitation) }

                return! ctx.WriteHtmlView(unusable ctx t)
        }

let endpoint =
    [ GET [ routef "/invite/{%s}" handler |> configureEndpoint _.RequireRateLimiting(RateLimits.SignIn) ] ]
