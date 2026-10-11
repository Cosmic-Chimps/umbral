/// Operator sign-in and sign-out.
module Umbral.Handlers.Admin.SignIn

open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Infrastructure
open Umbral.Infrastructure.SecurityEvents
open Umbral.Views

let private view (ctx: HttpContext) (t: Texts.Texts) (error: string option) =
    div(class' = "flex min-h-[70vh] items-center justify-center") {
        div(class' = "um-card w-full max-w-sm space-y-5").data("screen", "operator-signin") {
            Components.pageHeading t.SignInTitle (Some t.SignInLead)

            match error with
            | Some message -> Components.alert Components.Failure message :> HtmlElement
            | None -> Fragment() :> HtmlElement

            form(method = "post", action = "/admin/signin", class' = "space-y-4") {
                ctx.GetAntiforgeryInput()
                Components.passwordField t.CredentialLabel "credential" None None

                button(type' = "submit", class' = Components.btn Components.Primary + " w-full justify-center") {
                    Components.icon Theme.Lock
                    t.SignInButton
                }
            }
        }
    }
    |> Layout.page ctx t t.SignInTitle

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
                let error = ctx.TryGetQueryValue "msg" |> Option.bind Texts.knownNotice |> Option.map (Texts.current ()).Notice
                return! ctx.WriteHtmlView(view ctx (Texts.current ()) error)
        }

let submit: EndpointHandler =
    fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<System.TimeProvider>()
            let! form = WebForms.readValidated ctx
            let credential = WebForms.value form "credential"
            let remoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string

            match! AdminAuth.verify config credential with
            | true ->
                do!
                    SecurityEvents.record
                        config
                        clock
                        { event OperatorSignedIn Success Actor.Operator with RemoteIp = remoteIp }

                do! AdminAuth.signIn ctx
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
