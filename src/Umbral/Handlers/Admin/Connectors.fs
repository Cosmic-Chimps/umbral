/// The Connectors screen.
///
/// The five-minute promise lives here. An operator arrives knowing nothing,
/// and has to leave with Discord working — so the screen's job is to hand them
/// the exact callback URL rather than let them guess at one, and to warn about
/// the two things that otherwise fail an hour later with an error naming
/// neither cause.
///
/// One card per stored connector, so a second Discord application is a second
/// card (FR-012), plus today's empty card for each kind with none, which creates
/// the first connector of its kind with the kind's name as its slug: the
/// first-run path has no "add" step. The add form is for the second and later
/// ones, where the operator picks the slug. Every card says to name the provider's
/// application after the Umbral instance (FR-003).
module Umbral.Handlers.Admin.Connectors

open System
open Microsoft.AspNetCore.Http
open OpenIddict.Abstractions
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Domains
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

/// The shared setting as a tick box with its hint. A ticked box posts
/// `shared=true`; an unticked one posts nothing, and on a full form that means
/// not shared (FR-014).
let private sharedBox (t: Texts.Texts) (isShared: bool) =
    div(class' = "space-y-1") {
        label(class' = "flex items-center gap-2 text-body-md text-on-surface") {
            let box = input(type' = "checkbox", name = "shared", value = "true")

            if isShared then
                box.attr("checked", "checked") |> ignore

            box
            span() { t.ConnectorSharedLabel }
        }

        p(class' = "um-hint") { t.ConnectorSharedHint }
    }

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
    (chosenSlugs: Set<string>)
    (row: Row)
    =
    let slug = row.Slug
    let callbackUrl = (PublicOrigin.callbackUri origin slug).ToString()
    let isConfigured = row.Existing.IsSome

    // What the kind is called where its own name would read wrongly: an OpenID
    // Connect provider has no name of its own, the operator's label stands in.
    let providerName =
        match row.Kind, row.Existing with
        | Oidc, Some existing -> existing.DisplayName
        | Oidc, None -> t.ConnectorOidcProviderName
        | kind, _ -> kind.DisplayName
    let isEnabled = row.Existing |> Option.map (fun c -> c.IsEnabled) |> Option.defaultValue false
    // An empty card is offered as shared, like the add form (FR-014).
    let isShared = row.Existing |> Option.map (fun c -> c.IsShared) |> Option.defaultValue true

    // FR-014: not shared and in no app's chosen set means nobody sees it, which
    // is a puzzle to meet at sign-in rather than here.
    let offeredNowhere = isConfigured && not isShared && not (Set.contains slug chosenSlugs)

    // FR-023: the one enabled connector is the one whose removal closes the
    // door. The screen knows that when it draws the buttons, so it can ask for
    // the stronger confirmation and set the flag the handlers wait for —
    // without it the warning redirect would have no way to be answered.
    let isLastEnabled = isEnabled && enabledCount = 1
    let confirmed (path: string) = if isLastEnabled then path + "?confirm=yes" else path

    let element =
        article(class' = "um-card space-y-4")
            .data("connector", slug)
            .data("connector-kind", row.Kind.Value)
            .data("connector-shared", (if isShared then "shared" else "not-shared"))
            .data("connector-state", (if isEnabled then "enabled" else "disabled"))

    if offeredNowhere then
        element.data("offered-nowhere", "") |> ignore

    element {

        div(class' = "flex items-center justify-between gap-3") {
            div() {
                h2(class' = "text-headline-md text-on-surface") {
                    match row.Existing with
                    | Some c -> c.DisplayName
                    | None -> row.Kind.DisplayName
                }

                if isConfigured then
                    p(class' = "um-hint um-mono") { slug }
            }

            div(class' = "flex flex-wrap items-center gap-2") {
                if isConfigured then
                    Components.pill
                        Components.MutedTone
                        (if isShared then t.ConnectorSharedBadge else t.ConnectorNotSharedBadge)

                if isConfigured then
                    Components.stateBadge isEnabled t.Enabled t.Disabled
                else
                    Components.pill Components.MutedTone t.NotConfigured
            }
        }

        if offeredNowhere then
            (div(class' = "um-alert um-alert-warning").attr("role", "note") {
                Components.icon Theme.Warning
                span() { t.ConnectorOfferedNowhere }
            })
            :> HtmlElement

        // FR-003: said on every card, because the first time an operator
        // learns what the name matters for should not be a person asking
        // "what is this app?" on a consent screen.
        (div(class' = "um-alert um-alert-info").attr("role", "note").data("naming-advice", "") {
            Components.icon Theme.Info
            span() { t.ConnectorNamingAdvice providerName }
        })
        :> HtmlElement

        // THE field. Without it the operator invents a URL, the provider
        // rejects the callback, and the error names neither the URL nor Umbral.
        Components.copyField
            t.Copy
            t.Copied
            t.CallbackUrlLabel
            $"callback-{slug}"
            callbackUrl
            (fun input -> input.data("callback-url", "") |> ignore)

        // The issuer is part of everyone's identity (FR-016), so it is shown and
        // never edited: delete and add again is the only way to change it.
        match row.Existing |> Option.bind (fun c -> c.IssuerUrl) with
        | Some issuer ->
            Components.copyField
                t.Copy
                t.Copied
                t.ConnectorIssuerLabel
                $"issuer-{slug}"
                issuer
                (fun input -> input.data("issuer", "") |> ignore)

            (p(class' = "um-hint").data("issuer-fixed", "") { t.ConnectorIssuerFixedNote }) :> HtmlElement
        | None -> Fragment() :> HtmlElement

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

            // FR-013: the slug and the kind are not fields. Neither ever
            // changes, so the form has nothing to edit them with.
            Components.fieldWith
                "text"
                []
                (fun i -> i.attr("required", "required") |> ignore)
                t.ConnectorDisplayNameLabel
                "displayName"
                (row.Existing |> Option.map (fun c -> c.DisplayName) |> Option.defaultValue row.Kind.DisplayName)
                (Some t.ConnectorDisplayNameHint)
                None

            Components.field
                t.ClientIdLabel
                "clientId"
                (row.Existing |> Option.map (fun c -> c.ClientId) |> Option.defaultValue "")
                (Some(
                    match row.Kind with
                    | Oidc -> t.ConnectorOidcClientIdHint
                    | kind -> t.ProviderConsoleHint kind.DisplayName
                ))
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

            sharedBox t isShared

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
                            match row.Kind, row.Existing with
                            | Oidc, Some({ IssuerUrl = Some issuer } as existing) ->
                                t.ConnectorDeleteConfirmOidc existing.DisplayName issuer
                            | Oidc, Some existing -> t.ConnectorDeleteConfirm existing.DisplayName
                            | kind, _ -> t.ConnectorDeleteConfirm kind.DisplayName
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

/// What a warning is about: the action the apps would be stranded by.
type private StrandAction =
    | Disabling
    | Deleting
    /// Saving a card that turns a shared connector to not shared (FR-014).
    | Unsharing

/// The apps a disable, a delete or an unshare would leave with no way to sign
/// in, named, with the button that goes ahead anyway (FR-010). Recomputed from
/// the slug the redirect carried, so the redirect never has to carry app names.
type private StrandWarning =
    { Slug: string
      Action: StrandAction
      /// What the stored connector holds, which the unshare confirmation posts
      /// back: the warning said nothing was changed, so what was typed on the
      /// card is not carried through it.
      Stored: Connector option
      Apps: (string * string) list }

let private strandWarning (ctx: HttpContext) (t: Texts.Texts) (warning: StrandWarning) =
    div(class' = "space-y-2") {
        p() { t.StrandedAppsTitle }

        ul() {
            for clientId, name in warning.Apps do
                li().data("stranded-app", clientId) { name }
        }

        match warning.Action, warning.Stored with
        | Unsharing, Some stored ->
            // The card's own save, with the stored values and the box
            // unticked: the card form cannot be re-posted from here.
            form(method = "post", action = $"/admin/connectors/{warning.Slug}?confirm=yes", class' = "inline") {
                ctx.GetAntiforgeryInput()
                input(type' = "hidden", name = "displayName", value = stored.DisplayName)
                input(type' = "hidden", name = "clientId", value = stored.ClientId)

                button(type' = "submit", class' = Components.btnSm Components.Danger) {
                    t.ConnectorNotSharedStrandsConfirm
                }
            }
        | Unsharing, None -> ()
        | action, _ ->
            let path = if action = Deleting then "delete" else "toggle"

            form(method = "post", action = $"/admin/connectors/{warning.Slug}/{path}?confirm=yes", class' = "inline") {
                ctx.GetAntiforgeryInput()

                button(type' = "submit", class' = Components.btnSm Components.Danger) {
                    if action = Deleting then t.ConnectorDeleteStrandsConfirm else t.ConnectorDisableStrandsConfirm
                }
            }
    }

/// What the add form shows: empty on a first visit, the operator's own input
/// after a refusal. For an OpenID Connect provider a refused form is drawn again
/// with everything kept and the reason under the field it is about (spec FR-005);
/// the secret is the one thing never drawn again.
type private AddFormState =
    { Kind: string
      Slug: string
      DisplayName: string
      Issuer: string
      ClientId: string
      Shared: bool
      /// The field the refusal is about (`slug`, `displayName`, `issuer`,
      /// `clientId`, `clientSecret`) and what to tell the operator.
      Problem: (string * string) option }

let private emptyAddForm =
    { Kind = Discord.Value
      Slug = ""
      DisplayName = ""
      Issuer = ""
      ClientId = ""
      Shared = true
      Problem = None }

/// The add form (FR-012): a connector of a kind with a slug the operator
/// chooses. Ticked as shared by default, like the first connector of a kind
/// (FR-014). The kind is a real select: with five options and a label there is
/// nothing to explain that a radio card would add. It is one plain form, so it
/// works with no script: the issuer field is always there and says it is for
/// OpenID Connect only.
let private addForm (ctx: HttpContext) (t: Texts.Texts) (kinds: ProviderKind list) (state: AddFormState) =
    let problemAt field =
        match state.Problem with
        | Some(at, message) when at = field -> Some message
        | _ -> None

    let form' =
        form(method = "post", action = "/admin/connectors", class' = "space-y-4").data("add-connector", "")

    match state.Problem with
    | Some(field, _) -> form'.data("add-error", field) |> ignore
    | None -> ()

    section(class' = "um-card space-y-4").data("add-connector-section", "") {
        h2(class' = "text-headline-md text-on-surface") { t.ConnectorAddTitle }
        p(class' = "max-w-prose text-body-md text-on-surface-variant") { t.ConnectorAddLead }

        form' {
            ctx.GetAntiforgeryInput()

            div() {
                label(for' = "add-kind", class' = "um-label") { t.ConnectorAddKindLabel }

                select(id = "add-kind", name = "kind", class' = "um-input") {
                    for kind in kinds do
                        let entry =
                            option(value = kind.Value) {
                                match kind with
                                | Oidc -> t.ConnectorAddOidcOption
                                | kind -> kind.DisplayName
                            }

                        if kind.Value = state.Kind then
                            entry.attr("selected", "selected") |> ignore

                        entry
                }
            }

            Components.fieldWith
                "text"
                []
                (fun i -> i.attr("required", "required").attr("maxlength", "32").attr("pattern", "[a-z0-9\\-]+") |> ignore)
                t.ConnectorAddSlugLabel
                "slug"
                state.Slug
                (Some t.ConnectorAddSlugHint)
                (problemAt "slug")

            Components.fieldWith
                "text"
                []
                (fun i -> i.attr("required", "required").attr("maxlength", "64") |> ignore)
                t.ConnectorDisplayNameLabel
                "displayName"
                state.DisplayName
                (Some t.ConnectorDisplayNameHint)
                (problemAt "displayName")

            Components.field
                t.ConnectorIssuerLabel
                "issuer"
                state.Issuer
                (Some t.ConnectorIssuerHint)
                (problemAt "issuer")

            Components.field t.ClientIdLabel "clientId" state.ClientId None (problemAt "clientId")

            Components.passwordField
                t.ClientSecretLabel
                "clientSecret"
                (Some(if state.Problem.IsSome then t.ConnectorOidcSecretAgainHint else t.ClientSecretHint))
                (problemAt "clientSecret")

            sharedBox t state.Shared

            // FR-009, before the operator submits and not after: what they are
            // agreeing to by adding a provider.
            (div(class' = "um-alert um-alert-warning").attr("role", "note").data("email-trust-warning", "") {
                Components.icon Theme.Warning
                span() { t.ConnectorEmailTrustWarning }
            })
            :> HtmlElement

            button(type' = "submit", class' = Components.btn Components.Primary) {
                Components.icon Theme.CheckCircle
                t.ConnectorAddButton
            }
        }
    }

let private view
    ctx
    (t: Texts.Texts)
    origin
    (rows: Row list)
    (enabledCount: int)
    (chosenSlugs: Set<string>)
    (addableKinds: ProviderKind list)
    (add: AddFormState)
    (notice: string option)
    (strand: StrandWarning option)
    (explained: string option)
    =
    div(class' = "space-y-6").data("screen", "connectors") {
        Components.pageHeading t.ConnectorsTitle (Some t.ConnectorsLead)
        originBanner ctx t origin

        match notice, strand with
        // A notice that carries what the provider said (`explained`) is a
        // warning, not a success: the provider was saved disabled, or would not
        // be enabled.
        | Some code, None when explained.IsSome ->
            (div().data("notice", code) { Components.alert Components.Warning explained.Value })
            :> HtmlElement
        | Some code, Some warning ->
            (div().data("notice", code) {
                Components.alert Components.Warning (t.Notice code)
                strandWarning ctx t warning
            })
            :> HtmlElement
        | Some code, None ->
            (div().data("notice", code) { Components.alert Components.Success (t.Notice code) })
            :> HtmlElement
        | None, _ -> Fragment() :> HtmlElement

        for row in rows do
            card ctx t origin enabledCount chosenSlugs row

        addForm ctx t addableKinds add
    }
    |> Layout.page ctx t t.ConnectorsTitle

/// The kinds the add form offers: the three real providers, OpenID Connect (which
/// has no empty card, it is only ever added here), and the fake one
/// only where the fake connector is allowed to exist (see `FakeGate`). A fake
/// connector stored elsewhere would stop the next start.
let private addableKinds (ctx: HttpContext) =
    if (ctx.GetService<Providers.Fake.FakeGate>()).IsAllowed then
        allKinds @ [ Oidc; Fake ]
    else
        allKinds @ [ Oidc ]

/// Every stored connector gets a card, then an empty card for each kind with
/// none. Stored ones in the order Discord, GitHub, Google (the two easy wins
/// first, since Google is the one that can refuse a homelab address outright),
/// then by slug; a stored fake one last.
let private rowsFor (existing: Connector list) =
    let kindOrder kind =
        allKinds |> List.tryFindIndex ((=) kind) |> Option.defaultValue allKinds.Length

    let stored =
        existing
        |> List.sortBy (fun c -> kindOrder c.Kind, c.Slug)
        |> List.map (fun c -> { Kind = c.Kind; Existing = Some c })

    let empty =
        allKinds
        |> List.filter (fun kind -> not (existing |> List.exists (fun c -> c.Kind = kind)))
        |> List.map (fun kind -> { Kind = kind; Existing = None })

    stored @ empty |> List.sortBy (fun row -> kindOrder row.Kind, row.Slug)

/// The screen, with the add form as given. `page` draws it empty; a refused add
/// draws it again with the operator's input.
let private render (ctx: HttpContext) (add: AddFormState) : System.Threading.Tasks.Task =
        task {
            let t = Texts.current ()
            let config = ctx.GetService<Database.DbConfig>()
            let! existing = ConnectorStore.listAll config
            let! choices = AppConnectors.allChoices config

            // Counted over every stored connector, not over the cards, exactly
            // as the handlers count it.
            let enabledCount = existing |> List.filter (fun c -> c.IsEnabled) |> List.length

            // Every slug some app's chosen set names, for `data-offered-nowhere`.
            let chosenSlugs =
                choices
                |> Map.toSeq
                |> Seq.collect (fun (_, choice) ->
                    match choice with
                    | Only slugs -> Set.toSeq slugs
                    | AllShared -> Seq.empty)
                |> Set.ofSeq

            let notice = ctx.TryGetQueryValue "msg"

            // The strand warnings carry the slug they are about. The apps are
            // recomputed here, with the connector as the action would leave it.
            let! strand =
                match notice, ctx.TryGetQueryValue "slug" with
                | Some("connector-strands-apps" | "connector-delete-strands-apps" as code), Some slug ->
                    task {
                        let! apps = AppConnectors.appsWithChoices config (ctx.GetService<IOpenIddictApplicationManager>())

                        let action =
                            if code = "connector-delete-strands-apps" then Deleting
                            elif ctx.TryGetQueryValue "change" = Some "unshare" then Unsharing
                            else Disabling

                        let after =
                            match action with
                            | Deleting -> existing |> List.filter (fun c -> c.Slug <> slug)
                            | Unsharing ->
                                existing |> List.map (fun c -> if c.Slug = slug then { c with IsShared = false } else c)
                            | Disabling ->
                                existing |> List.map (fun c -> if c.Slug = slug then { c with IsEnabled = false } else c)

                        return
                            Some
                                { Slug = slug
                                  Action = action
                                  Stored = existing |> List.tryFind (fun c -> c.Slug = slug)
                                  Apps = AppConnectors.strandedByChange apps existing after }
                    }
                | _ -> System.Threading.Tasks.Task.FromResult None

            // The OpenID Connect notices that say what the provider answered. The
            // issuer is read from the stored connector, the reason from the
            // redirect: it is only ever drawn as escaped text.
            let explained =
                match notice, ctx.TryGetQueryValue "slug" |> Option.bind (fun slug -> existing |> List.tryFind (fun c -> c.Slug = slug)) with
                | Some code, Some { IssuerUrl = Some issuer } ->
                    let reason = ctx.TryGetQueryValue "reason" |> Option.defaultValue ""

                    match code with
                    | "connector-added-oidc-unreachable" -> Some(t.ConnectorAddOidcUnreachable issuer reason)
                    | "connector-enable-oidc-unreachable" -> Some(t.ConnectorEnableOidcUnreachable issuer reason)
                    | "connector-enable-oidc-wrong" -> Some(t.ConnectorEnableOidcWrong reason)
                    | _ -> None
                | _ -> None

            return!
                ctx.WriteHtmlView(
                    view
                        ctx
                        t
                        (ctx.GetService<PublicOrigin.PublicOrigin>())
                        (rowsFor existing)
                        enabledCount
                        chosenSlugs
                        (addableKinds ctx)
                        add
                        notice
                        strand
                        explained
                )
        }

let page: EndpointHandler =
    AdminAuth.requireOperator (fun ctx -> render ctx emptyAddForm)

let private recordEventWith
    (ctx: HttpContext)
    (kind: SecurityEvents.EventType)
    (slug: string)
    (detail: string option)
    =
    SecurityEvents.record
        (ctx.GetService<Database.DbConfig>())
        (ctx.GetService<TimeProvider>())
        { SecurityEvents.event kind SecurityEvents.Success SecurityEvents.Actor.Operator with
            ConnectorSlug = Some slug
            Detail = detail
            RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string }

let private recordEvent (ctx: HttpContext) (kind: SecurityEvents.EventType) (slug: string) =
    recordEventWith ctx kind slug None

/// Store a secret under the connector's own slug, returning what the store
/// keeps: the encoded reference and the hint the screen shows.
let private putSecret (ctx: HttpContext) (slug: string) (plaintext: string) =
    task {
        let! reference = ctx.GetService<Secrets.ISecretStore>().Put($"connector/{slug}/client_secret", plaintext)
        return reference.Encoded, Secrets.hint plaintext
    }

/// Whether `kind` may be created from the first-connector path, an empty card
/// posting to its own name. The fake kind is refused where the fake connector is
/// not allowed.
///
/// `Oidc` is refused here for good: it has no empty card, because a connector of
/// that kind is nothing without an issuer and the issuer is typed on the add form
/// (`createOidc`). Posting to `/admin/connectors/oidc` must not create an OpenID
/// Connect connector with no issuer.
let private mayCreate (ctx: HttpContext) (kind: ProviderKind) =
    match kind with
    | Fake -> (ctx.GetService<Providers.Fake.FakeGate>()).IsAllowed
    | Oidc -> false
    | Discord
    | GitHub
    | Google -> true

/// What a discovery check means for saving a new OpenID Connect provider
/// (research R2, Clarification Q2). One place, so saving it disabled when the
/// provider is unreachable (FR-005a) is a change to this match and nothing else.
type private SaveDecision =
    /// Every check passed: save it enabled.
    | SaveEnabled
    /// The provider could not be reached (FR-005a): save it disabled, and say
    /// why. Enabling it later runs the check again.
    | SaveDisabled of reason: string
    /// Refuse, keep the input, and tell the operator why.
    | Refuse of message: string

let private decide (result: OidcDiscovery.DiscoveryResult) =
    match result with
    | OidcDiscovery.Discovered _ -> SaveEnabled
    | OidcDiscovery.Wrong reason -> Refuse reason
    | OidcDiscovery.Unreachable reason -> SaveDisabled reason

/// Add an OpenID Connect provider (specs/002 T023). The checks run in the order
/// of the form's fields, so the first thing wrong is the first thing the operator
/// sees; the provider is asked about itself last, and nothing is written —
/// not even the secret — until it has answered.
///
/// Unlike every other kind, a refusal draws the form again with the input kept
/// (spec FR-005) and not a redirect with a notice: five fields typed from a
/// provider's admin area are not something to ask for twice.
let private createOidc (ctx: HttpContext) (form: Microsoft.AspNetCore.Http.IFormCollection) : System.Threading.Tasks.Task =
    task {
        let t = Texts.current ()
        let config = ctx.GetService<Database.DbConfig>()
        let clock = ctx.GetService<TimeProvider>()
        let discovery = ctx.GetService<OidcDiscovery.OidcDiscovery>()

        let state =
            { Kind = Oidc.Value
              Slug = WebForms.value form "slug"
              DisplayName = WebForms.value form "displayName"
              Issuer = WebForms.value form "issuer"
              ClientId = WebForms.value form "clientId"
              Shared = WebForms.checkbox form "shared"
              Problem = None }

        let refuse (field: string) (message: string) =
            ctx.SetStatusCode 422
            render ctx { state with Problem = Some(field, message) }

        let! slugTaken = ConnectorStore.findBySlug config state.Slug

        // A kind's own name is where its empty card posts, so a connector of
        // another kind under it would be edited as the wrong provider; `oidc`
        // itself is a kind's name too.
        let reserved = (parseKind state.Slug).IsSome

        if not (Ids.isValidSlug state.Slug) then
            return! refuse "slug" t.ConnectorAddOidcSlugInvalid
        elif slugTaken.IsSome || reserved then
            return! refuse "slug" t.ConnectorAddOidcSlugTaken
        elif state.DisplayName = "" || state.DisplayName.Length > 64 then
            return! refuse "displayName" t.ConnectorAddOidcDisplayNameRequired
        else

        match discovery.Normalise state.Issuer with
        | Error error -> return! refuse "issuer" error.Message
        | Ok issuer ->

        let newSecret = WebForms.optional form "clientSecret"

        if state.ClientId = "" then
            return! refuse "clientId" t.ConnectorAddOidcClientIdRequired
        elif newSecret.IsNone then
            return! refuse "clientSecret" t.ConnectorAddOidcSecretRequired
        else

        let! holder = ConnectorStore.findByIssuer config issuer

        match holder with
        | Some other -> return! refuse "issuer" (t.ConnectorAddOidcIssuerTaken other.DisplayName)
        | None ->

        let! checkedProvider = discovery.Check issuer

        match decide checkedProvider with
        | Refuse message -> return! refuse "issuer" message
        | decision ->

        let enabled, unreachable =
            match decision with
            | SaveDisabled reason -> false, Some reason
            | _ -> true, None

        // Only now does the secret reach the store: the slug is free, the issuer
        // is free and the provider answered.
        let! secret = putSecret ctx state.Slug newSecret.Value

        let! added =
            ConnectorStore.create
                config
                clock
                { Slug = state.Slug
                  Kind = Oidc
                  DisplayName = state.DisplayName
                  ClientId = state.ClientId
                  ClientSecretRef = ""
                  ClientSecretHint = ""
                  ExtraScopes = []
                  IssuerUrl = Some issuer
                  IsEnabled = enabled
                  IsShared = state.Shared
                  CreatedUtc = clock.GetUtcNow()
                  UpdatedUtc = clock.GetUtcNow() }
                secret

        match added with
        | ConnectorStore.Created ->
            // The reason is what the provider's address did, never a credential;
            // it is cut short because it also travels in the redirect.
            let reasonShort = unreachable |> Option.map (WebForms.truncate 300)

            do!
                recordEventWith
                    ctx
                    SecurityEvents.ConnectorCreated
                    state.Slug
                    (reasonShort |> Option.map (fun reason -> $"saved_disabled: {reason}"))

            match reasonShort with
            | None -> return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-added-oidc"
            | Some reason ->
                return!
                    WebForms.redirectWithNoticeAbout
                        ctx
                        "/admin/connectors"
                        "connector-added-oidc-unreachable"
                        [ "slug", state.Slug; "reason", reason ]
        // Lost a race between the checks above and the insert.
        | ConnectorStore.SlugTaken -> return! refuse "slug" t.ConnectorAddOidcSlugTaken
        | ConnectorStore.IssuerTaken -> return! refuse "issuer" (t.ConnectorAddOidcIssuerTaken issuer)
    }

/// Add a connector of a kind under a slug the operator chose (FR-012).
let create: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! form = WebForms.readValidated ctx

            let slug = WebForms.value form "slug"
            let displayName = WebForms.value form "displayName"
            let clientId = WebForms.value form "clientId"
            let newSecret = WebForms.optional form "clientSecret"
            let! taken = ConnectorStore.findBySlug config slug

            match parseKind (WebForms.value form "kind") with
            | None -> return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-unknown"
            | Some Oidc -> return! createOidc ctx form
            | Some kind when not (mayCreate ctx kind) ->
                return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-unknown"
            | Some kind ->

            // A kind's own name is where its empty card posts, so a connector
            // of another kind under it would be edited as the wrong provider.
            let reservedForOtherKind =
                match parseKind slug with
                | Some named -> named <> kind
                | None -> false

            if not (Ids.isValidSlug slug) then
                return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-slug-invalid"
            elif taken.IsSome || reservedForOtherKind then
                return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-slug-taken"
            elif displayName = "" then
                return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-display-name-required"
            elif clientId = "" then
                return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-client-id-required"
            else

            match newSecret with
            | None -> return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-secret-required"
            | Some plaintext ->

            // The slug was just checked free, so writing the secret under it
            // cannot replace another connector's.
            let! secret = putSecret ctx slug plaintext

            let! added =
                ConnectorStore.create
                    config
                    clock
                    { Slug = slug
                      Kind = kind
                      DisplayName = displayName
                      ClientId = clientId
                      ClientSecretRef = ""
                      ClientSecretHint = ""
                      ExtraScopes = []
                      IssuerUrl = None
                      // A connector arrives enabled, as the first of a kind does.
                      IsEnabled = true
                      IsShared = WebForms.checkbox form "shared"
                      CreatedUtc = clock.GetUtcNow()
                      UpdatedUtc = clock.GetUtcNow() }
                    secret

            if added <> ConnectorStore.Created then
                return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-slug-taken"
            else

            do! recordEvent ctx SecurityEvents.ConnectorCreated slug
            return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-added"
        })

/// Save a card. The kind comes from the stored row, never from the slug
/// (FR-012); a slug with no row is the first connector of the kind it names,
/// which is the empty card's path.
let save (slug: string) : EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! form = WebForms.readValidated ctx

            let clientId = WebForms.value form "clientId"
            let newSecret = WebForms.optional form "clientSecret"
            let! existing = ConnectorStore.findBySlug config slug

            // A card posts its display name and its shared box every time. A
            // post without a display name field is an older client of this
            // endpoint (a script, a saved form): it leaves both as they are
            // rather than reading an absent box as "not shared".
            let fullForm = form.ContainsKey "displayName"
            let displayName = WebForms.value form "displayName"
            let isShared = WebForms.checkbox form "shared"
            let confirmed = ctx.TryGetQueryValue "confirm" = Some "yes"

            let kind =
                match existing with
                | Some c -> Some c.Kind
                | None -> parseKind slug |> Option.filter (mayCreate ctx)

            match kind with
            | None -> return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-unknown"
            | Some _ when clientId = "" ->
                return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-client-id-required"
            | Some _ when fullForm && displayName = "" ->
                return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-display-name-required"
            | Some kind ->

            match existing, newSecret with
            // A new connector needs a secret; an existing one does not, because
            // the screen cannot show the saved value back for retyping.
            | None, None -> return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-secret-required"

            | None, Some plaintext ->
                let! secret = putSecret ctx slug plaintext

                let! added =
                    ConnectorStore.create
                        config
                        clock
                        { Slug = slug
                          Kind = kind
                          DisplayName = (if fullForm then displayName else kind.DisplayName)
                          ClientId = clientId
                          ClientSecretRef = ""
                          ClientSecretHint = ""
                          ExtraScopes = []
                          IssuerUrl = None
                          // A connector arrives enabled: the operator came here to
                          // turn it on, and a saved-but-off connector is a puzzle.
                          IsEnabled = true
                          IsShared = (not fullForm) || isShared
                          CreatedUtc = clock.GetUtcNow()
                          UpdatedUtc = clock.GetUtcNow() }
                        secret

                if added <> ConnectorStore.Created then
                    return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-slug-taken"
                else
                    do! recordEvent ctx SecurityEvents.ConnectorCreated slug
                    return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-saved"

            | Some stored, _ ->
                let newName = if fullForm then displayName else stored.DisplayName
                let newShared = if fullForm then isShared else stored.IsShared

                // FR-010: turning sharing off can leave an all-shared app with
                // nothing to offer. Nothing changes until the operator confirms.
                let! stranded =
                    task {
                        if stored.IsShared && not newShared && not confirmed then
                            let! connectors = ConnectorStore.listAll config
                            let! apps = AppConnectors.appsWithChoices config (ctx.GetService<IOpenIddictApplicationManager>())

                            let after =
                                connectors
                                |> List.map (fun c -> if c.Slug = slug then { c with IsShared = false } else c)

                            return AppConnectors.strandedByChange apps connectors after
                        else
                            return []
                    }

                if not stranded.IsEmpty then
                    return!
                        WebForms.redirectWithNoticeAbout
                            ctx
                            "/admin/connectors"
                            "connector-strands-apps"
                            [ "slug", slug; "change", "unshare" ]
                else

                let! secretUpdate =
                    match newSecret with
                    | Some plaintext ->
                        task {
                            let! secret = putSecret ctx slug plaintext
                            return Some secret
                        }
                    | None -> System.Threading.Tasks.Task.FromResult None

                let! updated = ConnectorStore.update config clock slug newName clientId newShared secretUpdate

                if not updated then
                    return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-unknown"
                else
                    do! recordEvent ctx SecurityEvents.ConnectorUpdated slug
                    return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-saved"
        })

let endpoint =
    [ GET [ route "/admin/connectors" page ]
      POST [ route "/admin/connectors" create ]
      POST [ routef "/admin/connectors/{%s}" save ] ]
