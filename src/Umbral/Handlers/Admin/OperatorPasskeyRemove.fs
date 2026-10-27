/// Removing one of the operator's passkeys (specs/011-operator-passkeys, user
/// story 3).
///
/// A lost phone stays a key to the admin area until its passkey is gone, so a
/// removal ends every other admin session too. The re-check is the password
/// while it is on, or another passkey's ceremony; the rule that the last
/// passkey stays while password sign-in is off lives inside the DELETE.
module Umbral.Handlers.Admin.OperatorPasskeyRemove

open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Oxpecker
open Umbral.Handlers.Admin.OperatorPasskeys
open Umbral.Infrastructure
open Umbral.Infrastructure.SecurityEvents

let private credentialPath = "/admin/credential"

// No credential id or name in the detail, only the closed words.
let private recordRemoval (ctx: HttpContext) (outcome: Outcome) (detail: string option) : Task =
    record
        (ctx.GetService<Database.DbConfig>())
        (ctx.GetService<System.TimeProvider>())
        { event OperatorPasskeyRemoved outcome Actor.Operator with
            RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string
            Detail = detail }

/// An id that names nothing says nothing: no notice, the screen as it is.
let private refuseNotFound (ctx: HttpContext) : Task =
    task {
        do! recordRemoval ctx Failure (Some "not_found")
        do! WebForms.redirect ctx credentialPath
    }

/// The last passkey while password sign-in is off: removing it would leave
/// no way in but `reset-credential`.
let private refuseLast (ctx: HttpContext) : Task =
    task {
        do! recordRemoval ctx Failure (Some "last_passkey_password_off")
        do! WebForms.redirectWithNotice ctx credentialPath "operator-passkey-last"
    }

let handler (id: string) : EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<System.TimeProvider>()
            let! form = WebForms.readValidated ctx

            match! OperatorPasskeys.findById config id with
            | None -> return! refuseNotFound ctx
            | Some _ ->

            let! passwordOn = OperatorPasskeys.passwordSignInEnabled config
            let! held = OperatorPasskeys.count config

            // Before the re-check, so the operator is told the next action
            // instead of being asked for a ceremony whose answer would be
            // refused anyway. The DELETE's own condition still decides when
            // two requests race.
            if not passwordOn && held <= 1 then
                return! refuseLast ctx
            else

            match! recheckForm ctx form true with
            | Missing ->
                ctx.SetStatusCode 422
                return! Credential.render ctx (Some(Credential.RemovePasskey id))
            | Failed(detail, notice) ->
                do! recordRemoval ctx Failure (Some detail)
                return! WebForms.redirectWithNotice ctx credentialPath notice
            | Confirmed ->
                match! OperatorPasskeys.remove config id with
                | OperatorPasskeys.NotFound -> return! refuseNotFound ctx
                | OperatorPasskeys.LastWhilePasswordOff -> return! refuseLast ctx
                | OperatorPasskeys.Removed ->
                    do! AdminAuth.endEverySession config clock
                    do! AdminAuth.signIn ctx
                    do! recordRemoval ctx Success None
                    return! WebForms.redirectWithNotice ctx credentialPath "operator-passkey-removed"
        })

let endpoint =
    [ POST [ routef "/admin/credential/passkeys/{%s}/remove" handler |> configureEndpoint _.RequireRateLimiting(RateLimits.AdminAuth) ] ]
