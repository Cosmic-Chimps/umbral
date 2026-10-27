/// The switch that turns password sign-in for the operator off and on
/// (specs/011-operator-passkeys, user story 2).
///
/// Both directions take a passkey ceremony on the spot and nothing else: turning
/// it off must not rest on a password that is about to stop working, and while
/// it is off no password exists to take. Turning it off ends every other admin
/// session, because a session opened with the password is the very thing the
/// operator is shutting the door on. The browser that did it stays signed in.
module Umbral.Handlers.Admin.OperatorPasswordSignIn

open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Oxpecker
open Umbral.Handlers.Admin.OperatorPasskeys
open Umbral.Infrastructure
open Umbral.Infrastructure.SecurityEvents

let private credentialPath = "/admin/credential"

let private recordChange (ctx: HttpContext) (outcome: Outcome) (detail: string) : Task =
    record
        (ctx.GetService<Database.DbConfig>())
        (ctx.GetService<System.TimeProvider>())
        { event OperatorPasswordSignInChanged outcome Actor.Operator with
            RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string
            Detail = Some detail }

let private refuseNoPasskey (ctx: HttpContext) : Task =
    task {
        do! recordChange ctx Failure "no_passkey"
        do! WebForms.redirectWithNotice ctx credentialPath "operator-password-off-needs-passkey"
    }

let handler: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<System.TimeProvider>()
            let! form = WebForms.readValidated ctx
            let! passwordOn = OperatorPasskeys.passwordSignInEnabled config

            match WebForms.optional form "state" with
            | Some "off" when not passwordOn -> return! WebForms.redirect ctx credentialPath
            | Some "on" when passwordOn -> return! WebForms.redirect ctx credentialPath
            | Some(("off" | "on") as state) ->
                let turningOff = (state = "off")
                let! held = OperatorPasskeys.count config

                // Before the re-check: the button is disabled with no passkey,
                // so a post that gets here is told the next action, not asked
                // for a ceremony that nothing could answer.
                if turningOff && held = 0 then
                    return! refuseNoPasskey ctx
                else

                match! recheckForm ctx form false with
                | Missing ->
                    ctx.SetStatusCode 422
                    return! Credential.render ctx (Some(if turningOff then Credential.SwitchOff else Credential.SwitchOn))
                | Failed(detail, notice) ->
                    do! recordChange ctx Failure detail
                    return! WebForms.redirectWithNotice ctx credentialPath notice
                | Confirmed ->
                    if turningOff then
                        match! OperatorPasskeys.turnPasswordSignInOff config clock with
                        | OperatorPasskeys.NoPasskey -> return! refuseNoPasskey ctx
                        | OperatorPasskeys.SwitchedOff ->
                            do! AdminAuth.endEverySession config clock
                            do! AdminAuth.signIn ctx
                            do! recordChange ctx Success "off"
                            return! WebForms.redirectWithNotice ctx credentialPath "operator-password-off"
                    else
                        do! OperatorPasskeys.turnPasswordSignInOn config clock
                        do! recordChange ctx Success "on"
                        return! WebForms.redirectWithNotice ctx credentialPath "operator-password-on"
            | _ -> return! WebForms.redirect ctx credentialPath
        })

let endpoint =
    [ POST [ route "/admin/credential/password-signin" handler |> configureEndpoint _.RequireRateLimiting(RateLimits.AdminAuth) ] ]
