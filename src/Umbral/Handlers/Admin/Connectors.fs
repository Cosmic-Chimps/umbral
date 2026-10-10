/// The Connectors screen.
///
/// The five-minute promise lives here. An operator arrives knowing nothing,
/// and has to leave with Discord working — so the screen's job is to hand them
/// the exact callback URL rather than let them guess at one, and to warn about
/// the two things that otherwise fail an hour later with an error naming
/// neither cause.
module Umbral.Handlers.Admin.Connectors

open System
open Microsoft.AspNetCore.Http
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Views

// ---------------------------------------------------------------------------
// The origin-mismatch banner (T070)
// ---------------------------------------------------------------------------

/// What the browser thinks it reached, for comparison only. This value is
/// NEVER used to build a redirect URI — it comes from headers a client can
/// forge. It exists so that the commonest silent misconfiguration announces
/// itself on screen instead of surfacing as `redirect_uri_mismatch` later.
let private apparentOrigin (ctx: HttpContext) =
    let host = ctx.Request.Host.Value
    if String.IsNullOrEmpty host then None else Some $"{ctx.Request.Scheme}://{host}"

let originBanner (ctx: HttpContext) (t: Texts.Texts) (origin: PublicOrigin.PublicOrigin) =
    match apparentOrigin ctx with
    | Some apparent when not (String.Equals(apparent, origin.Text, StringComparison.OrdinalIgnoreCase)) ->
        (div(class' = "um-alert um-alert-warning").attr("role", "alert").data("origin-mismatch", "") {
            Components.icon Theme.Warning
            span() { t.OriginMismatch origin.Text apparent }
        })
        :> HtmlElement
    | _ -> Fragment() :> HtmlElement

// ---------------------------------------------------------------------------
// One connector card
// ---------------------------------------------------------------------------

type private Row =
    { Kind: ProviderKind
      Existing: Connector option }

    member this.Slug =
        match this.Existing with
        | Some c -> c.Slug
        | None -> this.Kind.Value

let private card
    (ctx: HttpContext)
    (t: Texts.Texts)
    (origin: PublicOrigin.PublicOrigin)
    (enabledCount: int)
    (row: Row)
    =
    let slug = row.Slug
    let callbackUrl = (PublicOrigin.callbackUri origin slug).ToString()
    let isConfigured = row.Existing.IsSome
    let isEnabled = row.Existing |> Option.map (fun c -> c.IsEnabled) |> Option.defaultValue false

    // FR-023: the one enabled connector is the one whose removal closes the
    // door. The screen knows that when it draws the buttons, so it can ask for
    // the stronger confirmation and set the flag the handlers wait for —
    // without it the warning redirect would have no way to be answered.
    let isLastEnabled = isEnabled && enabledCount = 1
    let confirmed (path: string) = if isLastEnabled then path + "?confirm=yes" else path

    article(class' = "um-card space-y-4")
        .data("connector", slug)
        .data("connector-state", (if isEnabled then "enabled" else "disabled")) {

        div(class' = "flex items-center justify-between gap-3") {
            h2(class' = "text-headline-md text-on-surface") { row.Kind.DisplayName }

            if isConfigured then
                Components.stateBadge isEnabled t.Enabled t.Disabled
            else
                Components.pill Components.MutedTone t.NotConfigured
        }

        // THE field. Without it the operator invents a URL, the provider
        // rejects the callback, and the error names neither the URL nor Umbral.
        Components.copyField
            t.Copy
            t.Copied
            t.CallbackUrlLabel
            $"callback-{slug}"
            callbackUrl
            (fun input -> input.data("callback-url", "") |> ignore)

        // Shown at the moment the provider is chosen, not buried in a document.
        // Google refuses an IP address or a non-https host outright, which is
        // the natural homelab address — unfixable in code, so it must be said
        // before the operator spends ten minutes in Google's console.
        match setupCaveat row.Kind with
        | Some caveat ->
            (div(class' = "um-alert um-alert-warning").attr("role", "note").data("setup-caveat", slug) {
                Components.icon Theme.Info
                span() { caveat }
            })
            :> HtmlElement
        | None -> Fragment() :> HtmlElement

        form(method = "post", action = $"/admin/connectors/{slug}", class' = "space-y-4") {
            ctx.GetAntiforgeryInput()

            Components.field
                t.ClientIdLabel
                "clientId"
                (row.Existing |> Option.map (fun c -> c.ClientId) |> Option.defaultValue "")
                (Some (t.ProviderConsoleHint row.Kind.DisplayName))
                None

            match row.Existing with
            | Some existing ->
                // Never Edit: for a saved secret there is nothing to edit from,
                // because the screen cannot read it back.
                (Components.maskedSecretField
                    t.ClientSecretLabel
                    (Secrets.masked existing.ClientSecretHint)
                    (Fragment() :> HtmlElement))
                :> HtmlElement
            | None -> Fragment() :> HtmlElement

            Components.passwordField
                (if isConfigured then t.ReplaceSecretLabel else t.ClientSecretLabel)
                "clientSecret"
                (Some (if isConfigured then t.ReplaceSecretHint else t.ClientSecretHint))
                None

            div(class' = "flex flex-wrap items-center gap-2") {
                button(type' = "submit", class' = Components.btn Components.Primary) {
                    Components.icon Theme.CheckCircle
                    t.SaveConnector
                }

                if isConfigured then
                    // htmx intercepts this; it is a real form either way (D11).
                    (form(method = "post", action = confirmed $"/admin/connectors/{slug}/toggle", class' = "inline") {
                        ctx.GetAntiforgeryInput()

                        button(type' = "submit", class' = Components.btnSm Components.Ghost)
                            .attr("hx-post", confirmed $"/admin/connectors/{slug}/toggle")
                            .attr("hx-target", "closest article")
                            .attr("hx-swap", "outerHTML") {
                            Components.icon Theme.Power
                            if isEnabled then t.Disable else t.Enable
                        }
                    })
                    |> (if isLastEnabled then
                            Components.confirmSubmit t.ConnectorDisableLastWarning
                        else
                            id)
                    :> HtmlElement
                else
                    Fragment() :> HtmlElement

                if isConfigured then
                    (form(method = "post", action = confirmed $"/admin/connectors/{slug}/delete", class' = "inline") {
                        ctx.GetAntiforgeryInput()

                        button(type' = "submit", class' = Components.btnSm Components.Danger) {
                            Components.icon Theme.DeleteForever
                            t.ConnectorDeleteButton
                        }
                    })
                    |> Components.confirmSubmit (
                        if isLastEnabled then
                            t.ConnectorDeleteLastWarning
                        else
                            t.ConnectorDeleteConfirm row.Kind.DisplayName
                    )
                    :> HtmlElement
                else
                    Fragment() :> HtmlElement
            }
        }
    }

// ---------------------------------------------------------------------------
// Screen
// ---------------------------------------------------------------------------

let private view ctx (t: Texts.Texts) origin (rows: Row list) (enabledCount: int) (notice: string option) =
    div(class' = "space-y-6").data("screen", "connectors") {
        Components.pageHeading t.ConnectorsTitle (Some t.ConnectorsLead)
        originBanner ctx t origin

        match notice with
        | Some code ->
            (div().data("notice", code) { Components.alert Components.Success (t.Notice code) })
            :> HtmlElement
        | None -> Fragment() :> HtmlElement

        for row in rows do
            card ctx t origin enabledCount row
    }
    |> Layout.operatorPage ctx t t.ConnectorsTitle

let page: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let t = Texts.current ()
            let! existing = ConnectorStore.listAll (ctx.GetService<Database.DbConfig>())

            // Every supported provider gets a card whether or not it is
            // configured, so "add a connector" is not a thing to go looking for.
            // Ordered Discord, GitHub, Google: the two easy wins first, since
            // Google is the one that can refuse a homelab address outright.
            let rows =
                allKinds
                |> List.map (fun kind ->
                    { Kind = kind
                      Existing = existing |> List.tryFind (fun c -> c.Kind = kind) })

            // Counted over every stored connector, not over the cards: a fake
            // one has no card but still keeps the door open, and the handlers
            // count it the same way.
            let enabledCount = existing |> List.filter (fun c -> c.IsEnabled) |> List.length

            let notice = ctx.TryGetQueryValue "msg"
            return! ctx.WriteHtmlView(view ctx t (ctx.GetService<PublicOrigin.PublicOrigin>()) rows enabledCount notice)
        })

let save (slug: string) : EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! form = WebForms.readValidated ctx

            let clientId = WebForms.value form "clientId"
            let newSecret = WebForms.optional form "clientSecret"
            let! existing = ConnectorStore.findBySlug config slug

            match parseKind slug with
            | None -> return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-unknown"
            | Some kind when clientId = "" ->
                ignore kind
                return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-client-id-required"
            | Some kind ->

            // A new connector needs a secret; an existing one does not, because
            // the screen cannot show the saved value back for retyping.
            match existing, newSecret with
            | None, None -> return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-secret-required"
            | _ ->

            let secrets = ctx.GetService<Secrets.ISecretStore>()

            let! secretUpdate =
                match newSecret with
                | Some plaintext ->
                    task {
                        let! reference = secrets.Put($"connector/{slug}/client_secret", plaintext)
                        return Some(reference.Encoded, Secrets.hint plaintext)
                    }
                | None -> System.Threading.Tasks.Task.FromResult None

            let connector =
                match existing with
                | Some c -> { c with ClientId = clientId }
                | None ->
                    { Slug = slug
                      Kind = kind
                      DisplayName = kind.DisplayName
                      ClientId = clientId
                      ClientSecretRef = ""
                      ClientSecretHint = ""
                      ExtraScopes = []
                      // A connector arrives enabled: the operator came here to
                      // turn it on, and a saved-but-off connector is a puzzle.
                      IsEnabled = true
                      CreatedUtc = clock.GetUtcNow()
                      UpdatedUtc = clock.GetUtcNow() }

            do! ConnectorStore.save config clock connector secretUpdate

            do!
                SecurityEvents.record
                    config
                    clock
                    { SecurityEvents.event
                          (if existing.IsSome then
                               SecurityEvents.ConnectorUpdated
                           else
                               SecurityEvents.ConnectorCreated)
                          SecurityEvents.Success
                          SecurityEvents.Actor.Operator with
                        ConnectorSlug = Some slug
                        RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string }

            return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-saved"
        })

let endpoint =
    [ GET [ route "/admin/connectors" page ]
      POST [ routef "/admin/connectors/{%s}" save ] ]
