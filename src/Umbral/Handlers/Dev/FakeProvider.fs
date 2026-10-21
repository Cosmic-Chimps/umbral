/// The fake provider's own "authorize" page.
///
/// Stands in for Discord's consent screen. Everything either side of it is the
/// real path, so a test that drives this drives the correlation cookie, the
/// `state` comparison, the linking rule and real token issuance.
///
/// 404s unless the gate allows it. Not a redirect and not an explanatory error:
/// on an instance where this is not allowed, the route should not appear to
/// exist at all.
module Umbral.Handlers.Dev.FakeProvider

open System
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Infrastructure.Providers.Fake
open Umbral.Views

let private notHere (ctx: Microsoft.AspNetCore.Http.HttpContext) = Layout.notFound ctx

let page: EndpointHandler =
    fun ctx ->
        if not (ctx.GetService<FakeGate>()).IsAllowed then
            notHere ctx
        else

        let t = Texts.current ()
        let redirectUri = ctx.TryGetQueryValue "redirect_uri" |> Option.defaultValue ""
        let state = ctx.TryGetQueryValue "state" |> Option.defaultValue ""

        let body =
            div(class' = "flex min-h-[60vh] items-center justify-center") {
                div(class' = "um-card w-full max-w-sm space-y-5").data("screen", "fake-provider") {
                    Components.pageHeading "Fake provider" (Some "Development only. This signs you in as whoever you type.")

                    form(method = "post", action = AuthorizePath, class' = "space-y-4") {
                        ctx.GetAntiforgeryInput()
                        input(type' = "hidden", name = "redirect_uri", value = redirectUri)
                        input(type' = "hidden", name = "state", value = state)

                        Components.field "Subject" "subject" "fake-user-1" None None
                        Components.field "Email" "email" "test@example.com" None None
                        Components.field "Display name" "displayName" "Test User" None None

                        label(class' = "um-check-row") {
                            input(type' = "checkbox", name = "emailVerified", value = "on", class' = "um-check")
                                .attr("checked", "checked")

                            span() { "Email verified" }
                        }

                        button(type' = "submit", class' = Components.btn Components.Primary + " w-full justify-center")
                            .data("fake-approve", "") {
                            "Sign in"
                        }
                    }
                }
            }

        ctx.WriteHtmlView(Layout.page ctx t "Fake provider" body)

let submit: EndpointHandler =
    fun ctx ->
        task {
            let gate = ctx.GetService<FakeGate>()

            if not gate.IsAllowed then
                return! notHere ctx
            else

            let! form = Umbral.Infrastructure.WebForms.readValidated ctx
            let value = Umbral.Infrastructure.WebForms.value form

            let adapter = ctx.GetService<FakeAdapter>()

            let code =
                adapter.IssueCode(
                    value "subject",
                    value "email",
                    Umbral.Infrastructure.WebForms.checkbox form "emailVerified",
                    value "displayName"
                )

            // Back to Umbral's real callback, exactly as a provider would:
            // `code` and `state` on the query string, nothing else.
            let redirectUri = value "redirect_uri"
            let state = value "state"

            let separator = if redirectUri.Contains '?' then "&" else "?"

            ctx.Response.Redirect
                $"{redirectUri}{separator}code={Uri.EscapeDataString code}&state={Uri.EscapeDataString state}"

            return ()
        }

let endpoint =
    [ GET [ route AuthorizePath page ]
      POST [ route AuthorizePath submit ] ]
