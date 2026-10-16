/// Umbral's own page for a protocol error it cannot redirect.
///
/// This handler and `EnableErrorPassthrough()` must exist together. The T001
/// spike turned the switch on without writing this, and every request
/// OpenIddict would otherwise have rejected cleanly — a missing PKCE
/// challenge, an unregistered `redirect_uri` — became an
/// InvalidOperationException and an HTTP 500. The switch is not inert.
///
/// A rejected `redirect_uri` is exactly the case that must land here rather
/// than redirect: sending the user to an address the app never registered is
/// the open-redirect FR-003 exists to prevent.
module Umbral.Handlers.Connect.Error

open Microsoft.AspNetCore
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Views

let handler: EndpointHandler =
    fun ctx ->
        let t = Texts.current ()
        let response = ctx.GetOpenIddictServerResponse()

        // FR-037: say what to do next, and disclose no configuration detail.
        // OpenIddict's error_description names protocol facts ("the specified
        // redirect_uri is not valid"), which is safe to show and genuinely
        // useful to the developer integrating the app — but the operator's
        // settings, the client list and the issuer stay out of it.
        let detail =
            response
            |> Option.ofObj
            |> Option.bind (fun r -> Option.ofObj r.ErrorDescription)

        ctx.SetStatusCode 400

        let body =
            div(class' = "space-y-4") {
                p(class' = "text-body-md text-on-surface-variant") { t.ProtocolErrorBody }

                match detail with
                | Some description ->
                    (p(class' = "um-mono text-body-md text-on-surface-variant").data("protocol-error", "") {
                        description
                    })
                    :> HtmlElement
                | None -> Fragment() :> HtmlElement
            }

        ctx.WriteHtmlView(Layout.messagePageWith ctx t t.ProtocolErrorTitle body)

let endpoint = GET [ route "/connect/error" handler ]
