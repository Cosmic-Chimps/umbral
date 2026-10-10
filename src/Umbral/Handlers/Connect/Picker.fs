/// The end user's "sign in with…" page.
///
/// The only screen an end user ever sees on Umbral, and usually for under a
/// second. It exists because the authorize handler needs somewhere to send
/// someone who has no session yet.
module Umbral.Handlers.Connect.Picker

open System
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Views

/// Where to go once a provider has vouched for them. Only ever a path on this
/// instance: an absolute URL here would make the picker an open redirect, and
/// the value is attacker-supplied by construction.
let sanitizeReturnUrl (candidate: string | null) =
    match Option.ofObj candidate with
    | Some url when url.StartsWith "/" && not (url.StartsWith "//") -> url
    | _ -> "/"

let private button (t: Texts.Texts) (returnUrl: string) (connector: Connector) =
    let href =
        $"/connect/{Uri.EscapeDataString connector.Slug}/start?returnUrl={Uri.EscapeDataString returnUrl}"

    a(href = href, class' = Components.btn Components.Secondary + " w-full justify-center")
        .data("connector", connector.Slug) {
        Components.icon Theme.Link
        t.SignInWith connector.DisplayName
    }

let view (ctx: Microsoft.AspNetCore.Http.HttpContext) (t: Texts.Texts) (returnUrl: string) (connectors: Connector list) (notice: string option) =
    div(class' = "flex min-h-[60vh] items-center justify-center") {
        div(class' = "um-card w-full max-w-sm space-y-5").data("screen", "signin") {
            Components.pageHeading t.PickerTitle (Some t.PickerLead)

            match notice with
            | Some code -> Components.alert Components.Failure (t.Notice code) :> HtmlElement
            | None -> Fragment() :> HtmlElement

            match connectors with
            | [] ->
                // Nobody can sign in anywhere. The end user cannot fix this, so
                // the message points at the person who can rather than
                // pretending something went wrong on their side.
                (p(class' = "text-body-md text-on-surface-variant").data("no-connectors", "") {
                    t.PickerNoConnectors
                })
                :> HtmlElement

            | connectors ->
                (div(class' = "flex flex-col gap-3") {
                    for connector in connectors do
                        button t returnUrl connector
                })
                :> HtmlElement
        }
    }
    |> Layout.page ctx t t.PickerTitle

let handler: EndpointHandler =
    fun ctx ->
        task {
            let t = Texts.current ()
            let returnUrl = sanitizeReturnUrl (ctx.TryGetQueryValue "returnUrl" |> Option.toObj)
            let notice = ctx.TryGetQueryValue "msg"

            // Enabled only. An option that cannot work should not be offered.
            let! connectors = ConnectorStore.listEnabled (ctx.GetService<Database.DbConfig>())

            return! ctx.WriteHtmlView(view ctx t returnUrl connectors notice)
        }

let endpoint = GET [ route "/signin" handler ]
