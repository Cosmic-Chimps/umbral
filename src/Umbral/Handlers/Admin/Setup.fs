/// First run: claiming the instance.
///
/// FR-016. Nothing administrative works until this is done, and Umbral ships
/// no default credential for anyone to guess at.
///
/// It also requires the one-time token printed to the container log. FR-016
/// says the operator must establish access before any admin screen works, but
/// not *who* gets to be the operator — and an open setup page on a fresh
/// instance is claimable by whoever reaches it first. Reading `docker logs` is
/// the first thing an operator does anyway.
module Umbral.Handlers.Admin.Setup

open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Infrastructure
open Umbral.Infrastructure.SecurityEvents
open Umbral.Views

let private view (ctx: HttpContext) (t: Texts.Texts) (error: string option) =
    div(class' = "flex min-h-[70vh] items-center justify-center") {
        div(class' = "um-card w-full max-w-md space-y-5").data("screen", "setup") {
            Components.pageHeading t.SetupTitle (Some t.SetupLead)

            match error with
            | Some message -> Components.alert Components.Failure message :> HtmlElement
            | None -> Fragment() :> HtmlElement

            form(method = "post", action = "/admin/setup", class' = "space-y-4") {
                ctx.GetAntiforgeryInput()

                Components.field t.SetupTokenLabel "token" "" (Some t.SetupTokenHint) None
                Components.passwordField t.CredentialLabel "credential" (Some t.CredentialHint) None
                Components.passwordField t.CredentialConfirmLabel "confirm" None None

                button(type' = "submit", class' = Components.btn Components.Primary + " w-full justify-center") {
                    Components.icon Theme.Shield
                    t.SetupButton
                }
            }
        }
    }
    |> Layout.page ctx t t.SetupTitle

let private alreadyDone (ctx: HttpContext) =
    ctx.Response.Redirect "/admin/signin"
    System.Threading.Tasks.Task.CompletedTask

let page: EndpointHandler =
    fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let! configured = AdminAuth.isConfigured config

            if configured then
                return! alreadyDone ctx
            else
                let error = ctx.TryGetQueryValue "msg" |> Option.map (Texts.current ()).Notice
                return! ctx.WriteHtmlView(view ctx (Texts.current ()) error)
        }

let submit: EndpointHandler =
    fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<System.TimeProvider>()
            let! configured = AdminAuth.isConfigured config

            if configured then
                // Guarded again on the POST, not only on the GET: the form is
                // reachable by anyone who kept a stale page open, and setup
                // must never be a way to take over a configured instance.
                return! alreadyDone ctx
            else

            let! form = WebForms.readValidated ctx
            let token = WebForms.value form "token"
            let credential = WebForms.value form "credential"
            let confirm = WebForms.value form "confirm"

            let remoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string

            let fail detail notice =
                task {
                    do!
                        SecurityEvents.record
                            config
                            clock
                            { event OperatorSignInFailed Failure Actor.Operator with
                                RemoteIp = remoteIp
                                Detail = Some detail }

                    return! WebForms.redirectWithNotice ctx "/admin/setup" notice
                }

            if not ((ctx.GetService<AdminAuth.SetupToken>()).Matches token) then
                return! fail "setup_token_mismatch" "setup-token-invalid"
            elif credential <> confirm then
                // Checked before length, so mistyping twice does not also
                // complain about something that was not the problem.
                return! WebForms.redirectWithNotice ctx "/admin/setup" "setup-mismatch"
            else
                match! AdminAuth.setInitialCredential config clock credential with
                | AdminAuth.TooShort _ -> return! WebForms.redirectWithNotice ctx "/admin/setup" "setup-too-short"
                | AdminAuth.AlreadyConfigured -> return! alreadyDone ctx
                | AdminAuth.Set ->
                    do!
                        SecurityEvents.record
                            config
                            clock
                            { event OperatorCredentialChanged Success Actor.Operator with
                                RemoteIp = remoteIp
                                Detail = Some "initial_setup" }

                    // Straight in, rather than bouncing through a sign-in form
                    // they just proved they can pass.
                    do! AdminAuth.signIn ctx
                    return! WebForms.redirect ctx "/admin/connectors"
        }

let endpoint =
    [ GET [ route "/admin/setup" page ]
      POST [ route "/admin/setup" submit |> configureEndpoint _.RequireRateLimiting(RateLimits.AdminAuth) ] ]
