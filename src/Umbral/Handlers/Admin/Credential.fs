/// Replacing the operator credential while signed in.
///
/// FR-017a. The first-run screen sets it, `reset-credential` replaces it from
/// the host for someone who has lost it, and this is the third door: the
/// operator who still has it and wants a different one — after sharing a screen,
/// after typing it somewhere they should not have, or simply because the
/// generated one from a reset is not the one they want to keep.
///
/// It asks for the CURRENT credential even though the person is signed in. A
/// session cookie is the weaker thing here (it can be left open on a shared
/// machine, or lifted from a browser), and replacing the credential is the
/// action that would turn that into permanent access.
module Umbral.Handlers.Admin.Credential

open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Infrastructure
open Umbral.Infrastructure.SecurityEvents
open Umbral.Views

let private view (ctx: HttpContext) (t: Texts.Texts) (notice: string option) =
    div(class' = "mx-auto w-full max-w-md space-y-5").data("screen", "credential") {
        Components.pageHeading t.CredentialReplaceTitle (Some t.CredentialReplaceLead)

        match notice with
        | Some "credential-replaced" ->
            (div().data("notice", "credential-replaced") { Components.alert Components.Success t.CredentialReplaced })
            :> HtmlElement
        | Some code -> (div().data("notice", code) { Components.alert Components.Failure (t.Notice code) }) :> HtmlElement
        | None -> Fragment() :> HtmlElement

        form(method = "post", action = "/admin/credential", class' = "um-card space-y-4") {
            ctx.GetAntiforgeryInput()

            Components.currentPasswordField t.CurrentCredentialLabel "current" None None
            Components.passwordField t.CredentialNewLabel "credential" (Some t.CredentialHint) None
            Components.passwordField t.CredentialConfirmLabel "confirm" None None

            button(type' = "submit", class' = Components.btn Components.Primary) {
                Components.icon Theme.Shield
                t.CredentialReplaceButton
            }
        }
    }
    |> Layout.page ctx t t.CredentialReplaceTitle

let page: EndpointHandler =
    AdminAuth.requireOperator (fun ctx -> ctx.WriteHtmlView(view ctx (Texts.current ()) (ctx.TryGetQueryValue "msg" |> Option.bind Texts.knownNotice)))

let submit: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<System.TimeProvider>()
            let! form = WebForms.readValidated ctx
            let current = WebForms.value form "current"
            let credential = WebForms.value form "credential"
            let confirm = WebForms.value form "confirm"
            let remoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string

            match! AdminAuth.verify config current with
            | false ->
                // A wrong guess at the credential is a wrong guess whoever
                // makes it, and a signed-in session is exactly what someone
                // who walked up to an open browser has.
                do!
                    SecurityEvents.record
                        config
                        clock
                        { event OperatorSignInFailed Failure Actor.Operator with
                            RemoteIp = remoteIp
                            Detail = Some "credential_change_mismatch" }

                return! WebForms.redirectWithNotice ctx "/admin/credential" "credential-current-incorrect"

            | true ->

            if credential <> confirm then
                return! WebForms.redirectWithNotice ctx "/admin/credential" "setup-mismatch"
            else
                match! AdminAuth.replaceCredential config clock credential with
                | AdminAuth.Set ->
                    do!
                        SecurityEvents.record
                            config
                            clock
                            { event OperatorCredentialChanged Success Actor.Operator with
                                RemoteIp = remoteIp
                                Detail = Some "replaced_signed_in" }

                    // Every session was just ended, this one included. The
                    // person who changed it has not left, so they get a new
                    // one — the only session that survives.
                    do! AdminAuth.signIn ctx
                    return! WebForms.redirectWithNotice ctx "/admin/credential" "credential-replaced"

                | AdminAuth.TooShort _
                | AdminAuth.AlreadyConfigured -> return! WebForms.redirectWithNotice ctx "/admin/credential" "setup-too-short"
        })

let endpoint =
    [ GET [ route "/admin/credential" page ]
      POST [ route "/admin/credential" submit |> configureEndpoint _.RequireRateLimiting(RateLimits.AdminAuth) ] ]
