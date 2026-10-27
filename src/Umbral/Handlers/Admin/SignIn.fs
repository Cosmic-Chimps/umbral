/// Operator sign-in and sign-out.
module Umbral.Handlers.Admin.SignIn

open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Infrastructure
open Umbral.Infrastructure.SecurityEvents
open Umbral.Views

/// Everything the script needs is rendered hidden until it runs, so a browser
/// without it sees today's password form and nothing that does nothing.
let private scripted (element: #HtmlTag) =
    element.attr("hidden", "").data("scripted", "")

let private view (ctx: HttpContext) (t: Texts.Texts) (passwordOn: bool) (error: string option) =
    let passkeyButton =
        // Primary when it is the only way in, one press away otherwise.
        let kind = if passwordOn then Components.Secondary else Components.Primary

        button(type' = "button", class' = Components.btn kind + " w-full justify-center")
            .data("operator-passkey-signin", (if passwordOn then "operator-passkey-signin-failed" else "operator-passkey-signin-failed-passkey-only"))
            .attr("disabled", "")
        |> scripted
        |> fun b ->
            b {
                Components.icon Theme.Key
                t.SignInWithPasskey
            }

    div(class' = "flex min-h-[70vh] items-center justify-center") {
        div(class' = "um-card w-full max-w-sm space-y-5").data("screen", "operator-signin") {
            Components.pageHeading t.SignInTitle (Some(if passwordOn then t.SignInLeadBoth else t.SignInLeadPasskeyOnly))

            match error with
            | Some message -> Components.alert Components.Failure message :> HtmlElement
            | None -> Fragment() :> HtmlElement

            // The script reads the token from here; with the password form
            // gone there is no form to carry one.
            ctx.GetAntiforgeryInput()

            if passwordOn then
                form(method = "post", action = "/admin/signin", class' = "space-y-4") {
                    ctx.GetAntiforgeryInput()
                    Components.passwordField t.SignInPasswordLabel "credential" None None

                    button(type' = "submit", class' = Components.btn Components.Primary + " w-full justify-center") {
                        Components.icon Theme.Lock
                        t.SignInButton
                    }
                }

                div(class' = "um-or") { t.PickerOr } |> scripted

            passkeyButton

            noscript().data("javascript-required", "") {
                p(class' = "text-body-md text-on-surface-variant") {
                    if passwordOn then t.OperatorPasskeyNeedsJavaScript else t.OperatorPasskeyNeedsJavaScriptOnly
                }
            }
        }
    }
    |> Layout.pageWithScripts [ Assets.passkeys ] ctx t t.SignInTitle

let page: EndpointHandler =
    fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let! configured = AdminAuth.isConfigured config

            if not configured then
                // Nothing to sign in against yet.
                ctx.Response.Redirect "/admin/setup"
                return ()
            else
                let! passwordOn = OperatorPasskeys.passwordSignInEnabled config
                let error = ctx.TryGetQueryValue "msg" |> Option.bind Texts.knownNotice |> Option.map (Texts.current ()).Notice
                return! ctx.WriteHtmlView(view ctx (Texts.current ()) passwordOn error)
        }

let submit: EndpointHandler =
    fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<System.TimeProvider>()
            let! form = WebForms.readValidated ctx
            let credential = WebForms.value form "credential"
            let remoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string

            // Read before the check: a switch-off that lands while the password
            // is being hashed moves the stamp, and a cookie signed with the
            // earlier value is then refused at the next request.
            let! stamp = AdminAuth.sessionStamp config

            // While password sign-in is off `verify` is false for every
            // value, so the right password gets the wrong-password answer
            // (FR-008).
            match! AdminAuth.verify config credential with
            | true ->
                do!
                    SecurityEvents.record
                        config
                        clock
                        { event OperatorSignedIn Success Actor.Operator with RemoteIp = remoteIp }

                do! AdminAuth.signInWithStamp ctx stamp
                return! WebForms.redirect ctx "/admin/connectors"

            | false ->
                do!
                    SecurityEvents.record
                        config
                        clock
                        { event OperatorSignInFailed Failure Actor.Operator with
                            RemoteIp = remoteIp
                            Detail = Some "credential_mismatch" }

                // One message whether the instance is unconfigured or the
                // credential is wrong: there is nothing useful to learn here.
                return! WebForms.redirectWithNotice ctx "/admin/signin" "signin-incorrect"
        }

let endpoint =
    [ GET [ route "/admin/signin" page ]
      POST [ route "/admin/signin" submit |> configureEndpoint _.RequireRateLimiting(RateLimits.AdminAuth) ] ]
