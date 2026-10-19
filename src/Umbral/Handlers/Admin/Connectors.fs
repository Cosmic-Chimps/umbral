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
open Umbral.Infrastructure.SecretStores.Registry
open Umbral.Views

module SecretStoreSettings = Umbral.Infrastructure.SecretStores.Settings

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

/// FR-005, Story 4: the active store failed its startup probe, and nothing
/// since has written to it successfully. Names the store, the cause and what to
/// check, all from `Texts`; nothing is read from the request.
let private secretStoreBanner (ctx: HttpContext) (t: Texts.Texts) =
    let secrets = ctx.GetService<SecretStores>()

    match secrets.Status.Current with
    | Failed(failure, _) ->
        (div(class' = "um-alert um-alert-warning").attr("role", "alert").data("secret-store-problem", "") {
            Components.icon Theme.Warning
            span() { t.SecretStoreProblemNotice secrets.Active.Id failure }
        })
        :> HtmlElement
    | Healthy -> Fragment() :> HtmlElement

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

/// A list the operator typed that was refused, and what to tell them. The card
/// it belongs to is drawn with the text as typed and the reason under the field.
type private DomainsDraft = { Typed: string; Problem: string }

/// What the screen knows about allowed email domains beyond the connectors
/// themselves: the instance list, a refused entry at either level, and the
/// connectors a removal warning names (slug and name).
type private Domains =
    { Instance: string list
      InstanceDraft: DomainsDraft option
      CardDraft: (string * DomainsDraft) option
      Removal: (string * string) list option }

/// The two refusals of a list typed by hand, in the operator's words, naming the
/// entry. The entry is theirs: the views draw it as text, never as markup.
let private parseDomains (t: Texts.Texts) (typed: string) : Result<string list, string> =
    match EmailDomains.parse typed with
    | Ok list -> Ok list
    | Error(EmailDomains.Wildcard entry) -> Error(t.AllowedDomainsWildcard entry)
    | Error(EmailDomains.NotADomain entry) -> Error(t.AllowedDomainsNotADomain entry)

/// The `allowedDomains` field of a connector (add form and card): the help names
/// the instance list, which is what an empty field falls back to.
let private connectorDomainsField (t: Texts.Texts) (instance: string list) (typed: string) (problem: string option) =
    let help =
        t.ConnectorDomainsHelp(
            if instance.IsEmpty then t.AllowedDomainsNone else EmailDomains.display instance
        )

    Components.fieldWith
        "text"
        []
        (fun i -> i.attr("placeholder", t.AllowedDomainsPlaceholder) |> ignore)
        t.AllowedDomainsLabel
        "allowedDomains"
        typed
        (Some help)
        problem

/// FR-017: how a connector is limited to one app. `oidc` says why the general
/// recipe (a second connector) is not available there.
let private limitOneAppNote (text: string) =
    (p(class' = "um-hint").data("note", "limit-one-app") { text }) :> HtmlElement

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
    (domains: Domains)
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

    // Where the secret is kept, from the reference the row carries. A row whose
    // reference cannot be read as one (an unclaimed card, a connector with no
    // secret) has no store to name.
    let secretStore =
        row.Existing
        |> Option.bind (fun c ->
            match Secrets.SecretRef.Decode c.ClientSecretRef with
            | Ok reference -> Some reference.Store
            | Error _ -> None)

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

    // FR-018: which list applies to this card, as the operator reads it. The
    // stored list decides, not what a refused save typed.
    let storedList = row.Existing |> Option.map (fun c -> c.AllowedEmailDomains) |> Option.defaultValue []

    let ruleMarker, ruleText =
        match EmailDomains.applying storedList domains.Instance with
        | EmailDomains.Rule(EmailDomains.Level.Connector, list) -> "connector", t.ConnectorRuleConnector(EmailDomains.display list)
        | EmailDomains.Rule(EmailDomains.Level.Instance, list) -> "instance", t.ConnectorRuleInstance(EmailDomains.display list)
        | EmailDomains.NoRule -> "anyone", t.ConnectorRuleAnyone

    let typedDomains, domainsProblem =
        match domains.CardDraft with
        | Some(draftSlug, draft) when draftSlug = slug -> draft.Typed, Some draft.Problem
        | _ -> EmailDomains.display storedList, None

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

                match secretStore with
                | Some store ->
                    (Components.pill Components.MutedTone (t.SecretStoreLabel store)).data("secret-store", store)
                | None -> Fragment() :> HtmlElement

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

        (p(class' = "text-body-md text-on-surface").data("rule", ruleMarker) { ruleText }) :> HtmlElement

        // FR-021: known without a network call, so an operator who lost a
        // store's credentials sees it here rather than as a failed sign-in.
        match secretStore with
        | Some store when not ((ctx.GetService<SecretStores>()).CanRead store) ->
            (div(class' = "um-alert um-alert-warning").attr("role", "note").data("secret-unreadable", slug) {
                Components.icon Theme.Warning
                span() { t.SecretUnreadable store }
            })
            :> HtmlElement
        | _ -> Fragment() :> HtmlElement

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

            connectorDomainsField t domains.Instance typedDomains domainsProblem

            limitOneAppNote (
                match row.Kind with
                | Oidc -> t.ConnectorLimitOneAppNoteOidc
                | _ -> t.ConnectorLimitOneAppNote
            )

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
      /// The list as typed, for the field and for the refusal of an entry.
      AllowedDomains: string
      /// The field the refusal is about (`slug`, `displayName`, `issuer`,
      /// `clientId`, `clientSecret`, `allowedDomains`) and what to tell the
      /// operator.
      Problem: (string * string) option }

let private emptyAddForm =
    { Kind = Discord.Value
      Slug = ""
      DisplayName = ""
      Issuer = ""
      ClientId = ""
      Shared = true
      AllowedDomains = ""
      Problem = None }


/// The add form (FR-012): a connector of a kind with a slug the operator
/// chooses. Ticked as shared by default, like the first connector of a kind
/// (FR-014). The kind is a real select: with five options and a label there is
/// nothing to explain that a radio card would add. It is one plain form, so it
/// works with no script: the issuer field is always there and says it is for
/// OpenID Connect only.
let private addForm
    (ctx: HttpContext)
    (t: Texts.Texts)
    (instance: string list)
    (kinds: ProviderKind list)
    (state: AddFormState)
    =
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

            connectorDomainsField t instance state.AllowedDomains (problemAt "allowedDomains")
            limitOneAppNote t.ConnectorLimitOneAppNote

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

/// FR-016: the instance list, at the top of the screen. It posts to its own
/// route, never under a connector's slug.
let private instanceCard (ctx: HttpContext) (t: Texts.Texts) (domains: Domains) =
    let typed, problem =
        match domains.InstanceDraft with
        | Some draft -> draft.Typed, Some draft.Problem
        | None -> EmailDomains.display domains.Instance, None

    section(class' = "um-card space-y-4").data("card", "allowed-domains") {
        h2(class' = "text-headline-md text-on-surface") { t.AllowedDomainsTitle }
        p(class' = "max-w-prose text-body-md text-on-surface-variant") { t.AllowedDomainsLead }

        form(method = "post", action = "/admin/allowed-domains", class' = "space-y-4") {
            ctx.GetAntiforgeryInput()

            Components.fieldWith
                "text"
                []
                (fun i -> i.attr("placeholder", t.AllowedDomainsPlaceholder) |> ignore)
                t.AllowedDomainsLabel
                "allowedDomains"
                typed
                None
                problem

            button(type' = "submit", class' = Components.btn Components.Primary) {
                Components.icon Theme.CheckCircle
                t.AllowedDomainsSaveButton
            }
        }
    }

/// FR-019: emptying the instance list opens every connector that has no list of
/// its own to anyone with an account. Names them, with the button that goes
/// ahead anyway: the empty list again, with the confirmation.
let private removalWarning (ctx: HttpContext) (t: Texts.Texts) (code: string) (names: (string * string) list) =
    div().data("notice", code) {
        Components.alert Components.Warning (t.Notice code)

        div(class' = "space-y-2") {
            ul() {
                for slug, name in names do
                    li().data("opened-connector", slug) { name }
            }

            form(method = "post", action = "/admin/allowed-domains?confirm=yes", class' = "inline") {
                ctx.GetAntiforgeryInput()
                input(type' = "hidden", name = "allowedDomains", value = "")

                button(type' = "submit", class' = Components.btnSm Components.Danger) { t.AllowedDomainsRemoveAnyway }
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
    (domains: Domains)
    (addableKinds: ProviderKind list)
    (add: AddFormState)
    (notice: string option)
    (strand: StrandWarning option)
    (explained: string option)
    =
    div(class' = "space-y-6").data("screen", "connectors") {
        Components.pageHeading t.ConnectorsTitle (Some t.ConnectorsLead)
        originBanner ctx t origin
        secretStoreBanner ctx t

        match notice, strand with
        | Some("allowed-domains-removal" as code), _ ->
            match domains.Removal with
            | Some names -> (removalWarning ctx t code names) :> HtmlElement
            // A link that outlived the list it was about: nothing to warn of.
            | None -> Fragment() :> HtmlElement
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

        instanceCard ctx t domains

        for row in rows do
            card ctx t origin enabledCount chosenSlugs domains row

        addForm ctx t domains.Instance addableKinds add
    }
    |> Layout.operatorPage ctx t t.ConnectorsTitle []

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

/// A refused list at either level, to draw again with what was typed.
type private Drafts =
    { Instance: DomainsDraft option
      Card: (string * DomainsDraft) option }

let private noDrafts = { Instance = None; Card = None }

/// The screen, with the add form as given. `page` draws it empty; a refused add
/// draws it again with the operator's input, and so does a refused list.
let private render (ctx: HttpContext) (add: AddFormState) (drafts: Drafts) : System.Threading.Tasks.Task =
        task {
            let t = Texts.current ()
            let config = ctx.GetService<Database.DbConfig>()
            let! existing = ConnectorStore.listAll config
            let! choices = AppConnectors.allChoices config
            let! instanceList = EmailDomainRules.instanceList config

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

            let notice = ctx.TryGetQueryValue "msg" |> Option.bind Texts.knownNotice

            // FR-019: the removal warning names the connectors the removal would
            // open, recomputed here so the redirect carries no names. A list
            // that is no longer there has nothing to remove.
            let removal =
                match notice with
                | Some "allowed-domains-removal" when not instanceList.IsEmpty ->
                    match existing |> List.filter (fun c -> c.AllowedEmailDomains.IsEmpty) with
                    | [] -> None
                    | open' -> Some(open' |> List.map (fun c -> c.Slug, c.DisplayName))
                | _ -> None

            let domains =
                { Instance = instanceList
                  InstanceDraft = drafts.Instance
                  CardDraft = drafts.Card
                  Removal = removal }

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
            // cookie the refusal wrote (never from the URL). Without both, the
            // notice's fixed text, still as a warning.
            let explained =
                match notice with
                | Some("connector-added-oidc-unreachable" | "connector-enable-oidc-unreachable" | "connector-enable-oidc-wrong" as code) ->
                    let stored =
                        ctx.TryGetQueryValue "slug"
                        |> Option.bind (fun slug -> existing |> List.tryFind (fun c -> c.Slug = slug))

                    let said =
                        match stored with
                        | Some { Slug = slug; IssuerUrl = Some issuer } ->
                            WebForms.NoticeReason.consume ctx code "slug" slug
                            |> Option.map (fun reason ->
                                match code with
                                | "connector-added-oidc-unreachable" -> t.ConnectorAddOidcUnreachable issuer reason
                                | "connector-enable-oidc-unreachable" -> t.ConnectorEnableOidcUnreachable issuer reason
                                | _ -> t.ConnectorEnableOidcWrong reason)
                        | _ -> None

                    Some(said |> Option.defaultValue (t.Notice code))
                // The store failures: which store, and the cause or the secret's
                // name, come from the cookie the refusal wrote, never the URL.
                | Some("connector-secret-store-failed" | "connector-secret-left-behind" | "connector-secret-replaced-left-behind" as code) ->
                    let said =
                        ctx.TryGetQueryValue "slug"
                        |> Option.bind (fun slug -> WebForms.NoticeReason.consume ctx code "slug" slug)
                        |> Option.bind (fun reason ->
                            match reason.Split '|' with
                            // The detail is a failure code, read back through
                            // `OfCode`, which rebuilds only the codes it knows.
                            | [| store; detail |] when code = "connector-secret-store-failed" ->
                                Secrets.StoreFailure.OfCode detail |> Option.map (t.ConnectorSecretStoreFailed store)
                            | [| store; detail |] when code = "connector-secret-left-behind" ->
                                Some(t.ConnectorSecretLeftBehind (t.SecretStoreName store) detail)
                            | [| store; detail |] ->
                                Some(t.ConnectorSecretReplacedLeftBehind (t.SecretStoreName store) detail)
                            | _ -> None)

                    Some(said |> Option.defaultValue (t.Notice code))
                // Refused, nothing saved: a warning, with a fixed text.
                | Some("connector-secret-busy" as code) -> Some(t.Notice code)
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
                        domains
                        (addableKinds ctx)
                        add
                        notice
                        strand
                        explained
                )
        }


let page: EndpointHandler =
    AdminAuth.requireOperator (fun ctx -> render ctx emptyAddForm noDrafts)

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

let private remoteIpOf (ctx: HttpContext) =
    ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string

/// FR-021: a list changed. The slug is the connector's, absent for the instance
/// list; the detail is how many domains the list now holds, never which.
let private recordDomainsChanged (ctx: HttpContext) (slug: string option) (list: string list) =
    SecurityEvents.record
        (ctx.GetService<Database.DbConfig>())
        (ctx.GetService<TimeProvider>())
        { SecurityEvents.event SecurityEvents.AllowedDomainsChanged SecurityEvents.Success SecurityEvents.Actor.Operator with
            ConnectorSlug = slug
            Detail = Some $"count={List.length list}"
            RemoteIp = remoteIpOf ctx }

/// A refused list on a card or the instance card is drawn again with what was
/// typed (the secret is the one thing never redrawn), as an unprocessable form.
let private refuseDomains (ctx: HttpContext) (drafts: Drafts) =
    ctx.SetStatusCode 422
    render ctx emptyAddForm drafts

let private prefixOf (ctx: HttpContext) =
    ctx.GetService<SecretStoreSettings.SecretStoreSettings>().Prefix

/// The store refused a write. Recorded once, here, with the store and the cause
/// word and nothing the store said (FR-010); the callers turn it into a refusal.
let private recordPutFailed (ctx: HttpContext) (slug: string) (failure: Secrets.StoreFailure) =
    SecurityEvents.recordStoreFailure
        (ctx.GetService<Database.DbConfig>())
        (ctx.GetService<TimeProvider>())
        SecurityEvents.Actor.Operator
        slug
        (remoteIpOf ctx)
        (ctx.GetService<SecretStores>()).Active.Id
        "put"
        failure

/// Write the secret of a NEW connector to the active store, under the name
/// derived from its slug and the `now` that `ConnectorStore.create` will write
/// as `created_utc`. Returns the reference (to delete again if the row loses a
/// race) and what the row keeps: the encoded reference and the hint the screen
/// shows. On failure the event is already recorded.
let private putNew (ctx: HttpContext) (slug: string) (now: DateTimeOffset) (plaintext: string) =
    task {
        let secrets = ctx.GetService<SecretStores>()
        let name = Secrets.SecretName.derive (prefixOf ctx) slug now

        match! secrets.Active.Put(name, plaintext, ctx.RequestAborted) with
        | Ok reference -> return Ok(reference, (reference.Encoded, Secrets.hint plaintext))
        | Error failure ->
            do! recordPutFailed ctx slug failure
            return Error failure
    }

/// A create that wrote its secret and then lost the race for the slug (or the
/// issuer): no connector refers to that secret, so it is removed again. Best
/// effort; a store that refuses leaves an unreferenced secret, which costs
/// nothing but a line in the manager.
let private discard (ctx: HttpContext) (reference: Secrets.SecretRef) =
    task {
        match (ctx.GetService<SecretStores>()).For reference.Store with
        | Ok store ->
            let! _ = store.Delete(reference, ctx.RequestAborted)
            ()
        | Error _ -> ()
    }

/// The refusal of the redirecting paths. The store and the cause word travel in
/// the single-use notice cookie, not in the URL, so the screen can name the
/// store without a link being able to make it say anything (`NoticeReason`).
let private refuseStoreFailed (ctx: HttpContext) (slug: string) (failure: Secrets.StoreFailure) =
    WebForms.NoticeReason.redirect
        ctx
        "/admin/connectors"
        "connector-secret-store-failed"
        "slug"
        slug
        $"{(ctx.GetService<SecretStores>()).Active.Id}|{failure.Code}"

/// Whether `kind` may be created from the first-connector path, an empty card
/// posting to its own name. The fake kind is refused where the fake connector is
/// not allowed.
///
/// `Oidc` is refused here for good: it has no empty card, because a connector of
/// that kind is nothing without an issuer and the issuer is typed on the add form
/// (`createOidc`). Posting to `/admin/connectors/oidc` must not create an OpenID
/// Connect connector with no issuer.
///
/// `Passkey` is refused for the same reason in reverse: it has no client id and
/// no secret for this path to read, and its card posts to a route of its own
/// that also chooses who may enrol (specs/008 research R8).
let private mayCreate (ctx: HttpContext) (kind: ProviderKind) =
    match kind with
    | Fake -> (ctx.GetService<Providers.Fake.FakeGate>()).IsAllowed
    | Oidc
    | Passkey -> false
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
              AllowedDomains = WebForms.value form "allowedDomains"
              Problem = None }

        let refuse (field: string) (message: string) =
            ctx.SetStatusCode 422
            render ctx { state with Problem = Some(field, message) } noDrafts

        let domainsParsed = parseDomains t state.AllowedDomains

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

        // Before the provider is asked anything: a refused list costs no call.
        match domainsParsed with
        | Error message -> return! refuse "allowedDomains" message
        | Ok domains ->

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
        // `now` is taken once: it is the `created_utc` of the row and the
        // discriminator of the secret's name.
        let now = clock.GetUtcNow()

        match! putNew ctx state.Slug now newSecret.Value with
        | Error failure ->
            // The form is drawn again with what was typed, but the secret must
            // be typed again: the screen never holds it.
            return!
                refuse
                    "clientSecret"
                    (t.ConnectorSecretStoreFailed (ctx.GetService<SecretStores>()).Active.Id failure)
        | Ok(reference, secret) ->

        let! added =
            ConnectorStore.create
                config
                now
                { Slug = state.Slug
                  Kind = Oidc
                  DisplayName = state.DisplayName
                  ClientId = state.ClientId
                  ClientSecretRef = ""
                  ClientSecretHint = ""
                  IssuerUrl = Some issuer
                  AllowedEmailDomains = domains
                  PasskeyEnrolment = InvitationsOnly
                  IsEnabled = enabled
                  IsShared = state.Shared
                  CreatedUtc = now
                  UpdatedUtc = now }
                secret

        match added with
        | ConnectorStore.Created ->
            // The reason is what the provider's address did, never a credential;
            // it is cut short because it also travels in a cookie.
            let reasonShort = unreachable |> Option.map (WebForms.truncate 300)

            do!
                recordEventWith
                    ctx
                    SecurityEvents.ConnectorCreated
                    state.Slug
                    (Some(
                        match reasonShort with
                        | Some reason -> $"store={reference.Store};saved_disabled: {reason}"
                        | None -> $"store={reference.Store}"
                    ))

            if not domains.IsEmpty then
                do! recordDomainsChanged ctx (Some state.Slug) domains

            match reasonShort with
            | None -> return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-added-oidc"
            | Some reason ->
                return!
                    WebForms.NoticeReason.redirect
                        ctx
                        "/admin/connectors"
                        "connector-added-oidc-unreachable"
                        "slug"
                        state.Slug
                        reason
        // Lost a race between the checks above and the insert: nothing refers
        // to the secret just written, so it goes again.
        | ConnectorStore.SlugTaken ->
            do! discard ctx reference
            return! refuse "slug" t.ConnectorAddOidcSlugTaken
        | ConnectorStore.IssuerTaken ->
            do! discard ctx reference
            return! refuse "issuer" (t.ConnectorAddOidcIssuerTaken issuer)
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

            // Before the secret is written: a refused list leaves nothing behind.
            let typedDomains = WebForms.value form "allowedDomains"

            match parseDomains (Texts.current ()) typedDomains with
            | Error message ->
                ctx.SetStatusCode 422

                return!
                    render
                        ctx
                        { emptyAddForm with
                            Kind = kind.Value
                            Slug = slug
                            DisplayName = displayName
                            ClientId = clientId
                            Shared = WebForms.checkbox form "shared"
                            AllowedDomains = typedDomains
                            Problem = Some("allowedDomains", message) }
                        noDrafts
            | Ok domains ->

            // The slug was just checked free, so writing the secret under it
            // cannot replace another connector's.
            let now = clock.GetUtcNow()

            match! putNew ctx slug now plaintext with
            | Error failure -> return! refuseStoreFailed ctx slug failure
            | Ok(reference, secret) ->

            let! added =
                ConnectorStore.create
                    config
                    now
                    { Slug = slug
                      Kind = kind
                      DisplayName = displayName
                      ClientId = clientId
                      ClientSecretRef = ""
                      ClientSecretHint = ""
                      IssuerUrl = None
                      AllowedEmailDomains = domains
                      PasskeyEnrolment = InvitationsOnly
                      // A connector arrives enabled, as the first of a kind does.
                      IsEnabled = true
                      IsShared = WebForms.checkbox form "shared"
                      CreatedUtc = now
                      UpdatedUtc = now }
                    secret

            if added <> ConnectorStore.Created then
                do! discard ctx reference
                return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-slug-taken"
            else

            do! recordEventWith ctx SecurityEvents.ConnectorCreated slug (Some $"store={reference.Store}")

            if not domains.IsEmpty then
                do! recordDomainsChanged ctx (Some slug) domains

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

            // A card posts `allowedDomains` every time, an empty field meaning
            // "clear it". A post without the field (the unshare confirmation)
            // leaves the list as it is. Read before anything is written: a
            // refused entry saves nothing, not even the rest of the card.
            let typedDomains = WebForms.value form "allowedDomains"

            let parsedDomains =
                if form.ContainsKey "allowedDomains" then
                    parseDomains (Texts.current ()) typedDomains |> Result.map Some
                else
                    Ok None

            match parsedDomains with
            | Error message ->
                return!
                    refuseDomains
                        ctx
                        { noDrafts with
                            Card =
                                Some(
                                    slug,
                                    { Typed = typedDomains
                                      Problem = message }
                                ) }
            | Ok domains ->

            match existing, newSecret with
            // A new connector needs a secret; an existing one does not, because
            // the screen cannot show the saved value back for retyping.
            | None, None -> return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-secret-required"

            | None, Some plaintext ->
                let now = clock.GetUtcNow()

                match! putNew ctx slug now plaintext with
                | Error failure -> return! refuseStoreFailed ctx slug failure
                | Ok(reference, secret) ->

                let! added =
                    ConnectorStore.create
                        config
                        now
                        { Slug = slug
                          Kind = kind
                          DisplayName = (if fullForm then displayName else kind.DisplayName)
                          ClientId = clientId
                          ClientSecretRef = ""
                          ClientSecretHint = ""
                          IssuerUrl = None
                          AllowedEmailDomains = defaultArg domains []
                          PasskeyEnrolment = InvitationsOnly
                          // A connector arrives enabled: the operator came here to
                          // turn it on, and a saved-but-off connector is a puzzle.
                          IsEnabled = true
                          IsShared = (not fullForm) || isShared
                          CreatedUtc = now
                          UpdatedUtc = now }
                        secret

                if added <> ConnectorStore.Created then
                    do! discard ctx reference
                    return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-slug-taken"
                else
                    do! recordEventWith ctx SecurityEvents.ConnectorCreated slug (Some $"store={reference.Store}")

                    match domains with
                    | Some(_ :: _ as list) -> do! recordDomainsChanged ctx (Some slug) list
                    | _ -> ()

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

                let secrets = ctx.GetService<SecretStores>()

                // Everything from the write of a new secret to the row that
                // records it, so that a move copying this connector cannot
                // interleave with it (the lease, ConnectorStore.tryLease).
                let apply () =
                    task {
                        // The row is read again under the lease: a move that
                        // finished since the first read may have changed which
                        // store holds the old copy.
                        match! ConnectorStore.findBySlug config slug with
                        | None -> return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-unknown"
                        | Some stored ->

                        // The name is derived again from what never changes about the
                        // connector, so a connector already in the active store gets a
                        // new version of the secret it has (the reference stays), and
                        // one in another store gets a copy in the active one (R5).
                        let! secretUpdate =
                            match newSecret with
                            | Some plaintext ->
                                task {
                                    let name = Secrets.SecretName.derive (prefixOf ctx) slug stored.CreatedUtc

                                    match! secrets.Active.Put(name, plaintext, ctx.RequestAborted) with
                                    | Ok reference -> return Ok(Some(reference, (reference.Encoded, Secrets.hint plaintext)))
                                    | Error failure ->
                                        do! recordPutFailed ctx slug failure
                                        return Error failure
                                }
                            | None -> System.Threading.Tasks.Task.FromResult(Ok None)

                        match secretUpdate with
                        | Error failure -> return! refuseStoreFailed ctx slug failure
                        | Ok written ->

                        let! updated =
                            ConnectorStore.update config clock slug newName clientId newShared domains (written |> Option.map snd)

                        match written with
                        | Some(reference, _) when not updated ->
                            // The connector vanished between the read and the update;
                            // the secret just written belongs to nothing.
                            do! discard ctx reference
                        | _ -> ()

                        if not updated then
                            return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-unknown"
                        else
                            // FR-022: a changed secret names the store that now holds it.
                            do!
                                recordEventWith
                                    ctx
                                    SecurityEvents.ConnectorUpdated
                                    slug
                                    (written |> Option.map (fun (reference, _) -> $"store={reference.Store}"))

                            // FR-021: only when the stored list actually changed,
                            // judged against the row as read under the lease.
                            match domains with
                            | Some list when list <> stored.AllowedEmailDomains -> do! recordDomainsChanged ctx (Some slug) list
                            | _ -> ()

                            // A secret moved to another store leaves its old copy
                            // behind in the old one, which is removed now that no row
                            // refers to it. The card is already saved either way. Only a
                            // different STORE means a copy to remove: within one store a
                            // new version went under the same name, and a locator that
                            // differs in spelling (a GCP project id against its number)
                            // still names that same secret, which must not be deleted.
                            let! leftBehind =
                                match written, Secrets.SecretRef.Decode stored.ClientSecretRef with
                                | Some(reference, _), Ok old when old.Store <> reference.Store ->
                                    task {
                                        secrets.Evict old
                                        secrets.Evict reference

                                        match secrets.For old.Store with
                                        | Ok oldStore ->
                                            match! oldStore.Delete(old, ctx.RequestAborted) with
                                            | Ok() -> return None
                                            | Error failure -> return Some(old.Store, failure)
                                        | Error failure -> return Some(old.Store, failure)
                                    }
                                | Some(reference, _), Ok old ->
                                    secrets.Evict old
                                    secrets.Evict reference
                                    System.Threading.Tasks.Task.FromResult None
                                | Some(reference, _), _ ->
                                    secrets.Evict reference
                                    System.Threading.Tasks.Task.FromResult None
                                | None, _ -> System.Threading.Tasks.Task.FromResult None

                            match leftBehind with
                            | None -> return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-saved"
                            | Some(store, failure) ->
                                do!
                                    SecurityEvents.recordStoreFailure
                                        config
                                        clock
                                        SecurityEvents.Actor.Operator
                                        slug
                                        (remoteIpOf ctx)
                                        store
                                        "delete"
                                        failure

                                let name = (Secrets.SecretName.derive (prefixOf ctx) slug stored.CreatedUtc).In store

                                return!
                                    WebForms.NoticeReason.redirect
                                        ctx
                                        "/admin/connectors"
                                        "connector-secret-replaced-left-behind"
                                        "slug"
                                        slug
                                        $"{store}|{name}"
                    }

                match newSecret with
                // No secret is written, so there is nothing for a move to race.
                | None -> return! apply ()
                | Some _ ->
                    match! ConnectorStore.withLease config clock slug apply with
                    | Some() -> ()
                    | None -> return! WebForms.redirectWithNotice ctx "/admin/connectors" "connector-secret-busy"
        })

/// Save the instance list (FR-016, FR-019). Emptying a list that is not empty
/// while some connector has no list of its own opens those connectors to anyone
/// with an account, so it asks first, naming them; `?confirm=yes` is the answer.
/// A post without the field changes nothing, rather than reading as "clear it".
let saveAllowedDomains: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let t = Texts.current ()
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! form = WebForms.readValidated ctx

            let typed = WebForms.value form "allowedDomains"

            if not (form.ContainsKey "allowedDomains") then
                return! WebForms.redirectWithNotice ctx "/admin/connectors" "allowed-domains-saved"
            else

            match parseDomains t typed with
            | Error message ->
                return!
                    refuseDomains
                        ctx
                        { noDrafts with
                            Instance =
                                Some
                                    { Typed = typed
                                      Problem = message } }
            | Ok list ->

            let! stored = EmailDomainRules.instanceList config
            let! connectors = ConnectorStore.listAll config
            let confirmed = ctx.TryGetQueryValue "confirm" = Some "yes"

            let opens =
                list.IsEmpty
                && not stored.IsEmpty
                && connectors |> List.exists (fun c -> c.AllowedEmailDomains.IsEmpty)

            if opens && not confirmed then
                return! WebForms.redirectWithNotice ctx "/admin/connectors" "allowed-domains-removal"
            else

            if list <> stored then
                do! EmailDomainRules.setInstanceList config clock list
                do! recordDomainsChanged ctx None list

            return! WebForms.redirectWithNotice ctx "/admin/connectors" "allowed-domains-saved"
        })

let endpoint =
    [ GET [ route "/admin/connectors" page ]
      POST [ route "/admin/connectors" create ]
      POST [ route "/admin/allowed-domains" saveAllowedDomains ]
      POST [ routef "/admin/connectors/{%s}" save ] ]
