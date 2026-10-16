/// The operator's branding forms on the Apps screen: set or clear an app's
/// accent colour, upload, replace or remove its logo (spec 006, research R3).
///
/// Every route goes through `requireOperator` and `WebForms.readValidated`, looks
/// the app up by client id, changes nothing when it refuses, and ends in a
/// redirect back to that app's card (`/admin/apps?msg=…&app=…#app-…`), so the
/// operator lands where they were. A refused form is a notice, not a security
/// event: only a change is logged (research R9).
module Umbral.Handlers.Admin.AppBranding

open System
open Microsoft.AspNetCore.Http
open OpenIddict.Abstractions
open Oxpecker
open Umbral.Domains.Branding
open Umbral.Infrastructure

/// Back to the card of `clientId`, with the notice. The client id is an
/// OpenIddict id and the app was found by it, but it travels in a URL, so it is
/// escaped like any value.
let private redirectToCard (ctx: HttpContext) (notice: string) (clientId: string) =
    let id = Uri.EscapeDataString clientId
    ctx.Response.Redirect $"/admin/apps?msg={Uri.EscapeDataString notice}&app={id}#app-{id}"
    System.Threading.Tasks.Task.CompletedTask

/// A branding change in the security log: an app was updated, by the operator,
/// and `detail` says which part.
let private recordChange (ctx: HttpContext) (clientId: string) (detail: string) =
    let config = ctx.GetService<Database.DbConfig>()
    let clock = ctx.GetService<TimeProvider>()

    SecurityEvents.record
        config
        clock
        { SecurityEvents.event SecurityEvents.AppUpdated SecurityEvents.Success SecurityEvents.Actor.Operator with
            ClientId = Some clientId
            RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string
            Detail = Some detail }

/// Set an app's accent colour. The form field is `#rrggbb`, either case; it is
/// stored lowercase without the `#`. A colour that is hard to see on the card is
/// saved all the same and the notice says so (FR-017): it is the operator's
/// choice, and Umbral keeps the button text readable whatever they pick.
///
/// An empty field is refused for now. Clearing the colour is a later story.
let setAccent (clientId: string) : EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! form = WebForms.readValidated ctx

            let applications = ctx.GetService<IOpenIddictApplicationManager>()
            let! application = applications.FindByClientIdAsync clientId

            match application with
            | null -> return! WebForms.redirectWithNotice ctx "/admin/apps" "app-not-found"
            | _ ->

            match Accent.parse (WebForms.value form "accent") with
            | Error NotHexColour -> return! redirectToCard ctx "app-accent-invalid" clientId
            | Ok accent ->

            do! BrandingStore.setAccent config (clock.GetUtcNow()) clientId (Some accent)
            do! recordChange ctx clientId "branding_accent_set"

            let notice =
                if Accent.lowContrastOnCard accent then
                    "app-accent-low-contrast"
                else
                    "app-accent-saved"

            return! redirectToCard ctx notice clientId
        })

let endpoint =
    [ POST [ routef "/admin/apps/{%s}/branding/accent" setAccent ] ]
