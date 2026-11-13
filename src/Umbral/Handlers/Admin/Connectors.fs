/// The Connectors screen.
///
/// The five-minute promise lives here. An operator arrives knowing nothing,
/// and has to leave with Discord working — so the screen's job is to hand them
/// the exact callback URL rather than let them guess at one, and to warn about
/// the two things that otherwise fail an hour later with an error naming
/// neither cause.
///
/// A list and a detail pane, on the frame the Apps screen uses. One entry per
/// stored connector, so a second Discord application is a second entry (FR-012),
/// plus a muted entry for each kind with none, whose pane creates the first
/// connector of its kind with the kind's name as its slug: the first-run path has
/// no "add" step. The add form (`/new`) is for the second and later ones, where
/// the operator picks the slug. The list ends, behind a hairline, with the entry
/// for the allowed email domains of the whole instance. Every connector's pane
/// says to name the provider's application after the Umbral instance (FR-003).
module Umbral.Handlers.Admin.Connectors

open System
open Microsoft.AspNetCore.Http
open OpenIddict.Abstractions
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Domains
open Umbral.Domains.Connectors
open Umbral.Handlers.Admin.ConnectorPaths
open Umbral.Infrastructure
open Umbral.Infrastructure.SecretStores.Registry
open Umbral.Views

module FediverseRules = Umbral.Domains.Fediverse

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
// One connector's pane
// ---------------------------------------------------------------------------

/// The shared setting as a tick box with its hint. A ticked box posts
/// `shared=true`; an unticked one posts nothing, and on a full form that means
/// not shared (FR-014).
let private sharedBox (t: Texts.Texts) (isShared: bool) =
    div(class' = "space-y-1") {
        label(class' = "um-check-row") {
            let box = input(type' = "checkbox", name = "shared", value = "true", class' = "um-check")

            if isShared then
                box.attr("checked", "checked") |> ignore

            box
            span() { t.ConnectorSharedLabel }
        }

        p(class' = "um-hint") { t.ConnectorSharedHint }
    }

/// A list the operator typed that was refused, and what to tell them. The pane
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

/// The `allowedDomains` field of a connector (add form and pane): the help names
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

/// The Apple pane's "Sign-in button logo" section (spec 014, FR-010 to FR-015). `logo` is
/// what `ConnectorStore.appleLogo` returned: a parsed value, drawn through
/// `Components.appleLogoSlot`, which is the only way any SVG reaches this page. `problem` is
/// the refusal's message, drawn under the field; the pasted text itself is never drawn back,
/// so the textarea is always empty.
let private appleLogoSection
    (ctx: HttpContext)
    (t: Texts.Texts)
    (logo: AppleLogo.AppleLogo option)
    (problem: string option)
    : HtmlElement =
    let section =
        div(class' = "um-pane-section").data("apple-logo-section", "") {
            h3(class' = "um-section-title") { t.AppleLogoSectionTitle }

            div(class' = "space-y-5") {
                p(class' = "text-body-md text-on-surface-variant") { t.AppleLogoLead }

                match logo with
                | None ->
                    (p(class' = "text-body-md text-on-surface-variant").data("apple-logo-none", "") { t.AppleLogoNone })
                    :> HtmlElement
                | Some logo ->
                    // The sample Apple button as people see it, drawn like the branding
                    // section's. `data-branding-sample` keeps the disabled button at full
                    // opacity.
                    (div(class' = "w-full max-w-sm space-y-4 rounded-lg bg-surface-container p-4") {
                        button(type' = "button", class' = "um-btn um-btn-apple w-full justify-center")
                            .attr("disabled", "disabled")
                            .data("branding-sample", "")
                            .data("apple-logo-sample", "") {
                            Components.appleLogoSlot (Some logo)
                            t.SignInWithApple
                        }
                    })
                    :> HtmlElement

                form(method = "post", action = "/admin/connectors/apple/logo", class' = "space-y-3") {
                    ctx.GetAntiforgeryInput()

                    match logo with
                    | Some _ -> Components.textareaField t.AppleLogoReplaceLabel "appleLogo" 6 (Some t.AppleLogoReplaceHint) problem
                    | None -> Components.textareaField t.AppleLogoLabel "appleLogo" 6 (Some t.AppleLogoHint) problem

                    // Save and Remove on one row, as in the branding section. Remove belongs
                    // to the form below through its `form` attribute, so it posts that form
                    // (and its confirm) and never this one's pasted text.
                    div(class' = "flex flex-wrap items-center gap-2") {
                        button(type' = "submit", class' = Components.btnSm Components.Secondary) { t.AppleLogoSave }

                        // Reversible (the operator can paste it again), so a ghost in the
                        // error colour with a confirm, never the red fill.
                        match logo with
                        | Some _ ->
                            button(
                                type' = "submit",
                                class' = Components.btnSm Components.Ghost + " text-error hover:text-error"
                            )
                                .attr("form", "apple-logo-remove") {
                                t.AppleLogoRemove
                            }
                            :> HtmlElement
                        | None -> Fragment() :> HtmlElement
                    }
                }

                match logo with
                | Some _ ->
                    (form(id = "apple-logo-remove", method = "post", action = "/admin/connectors/apple/logo/remove") {
                        ctx.GetAntiforgeryInput()
                    })
                    |> Components.confirmSubmit t.AppleLogoRemoveConfirm
                    :> HtmlElement
                | None -> Fragment() :> HtmlElement
            }
        }

    match logo with
    | Some _ -> section.data("apple-logo-saved", "") :> HtmlElement
    | None -> section :> HtmlElement

let private providerPane
    (ctx: HttpContext)
    (t: Texts.Texts)
    (origin: PublicOrigin.PublicOrigin)
    (enabledCount: int)
    (chosenSlugs: Set<string>)
    (domains: Domains)
    (view: Components.FrameView)
    (notice: HtmlElement option)
    (appleDraft: AppleConnector.Draft option)
    (microsoftDraft: MicrosoftConnector.Draft option)
    (appleLogo: AppleLogo.AppleLogo option)
    (appleLogoProblem: string option)
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
    // reference cannot be read as one (a kind not set up, a connector with no
    // secret) has no store to name.
    let secretStore =
        row.Existing
        |> Option.bind (fun c ->
            match Secrets.SecretRef.Decode c.ClientSecretRef with
            | Ok reference -> Some reference.Store
            | Error _ -> None)

    // A kind not set up is offered as shared, like the add form (FR-014).
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

    // FR-018: which list applies to this connector, as the operator reads it. The
    // stored list decides, not what a refused save typed.
    let storedList = row.Existing |> Option.map (fun c -> c.AllowedEmailDomains) |> Option.defaultValue []

    let ruleMarker, ruleText =
        match EmailDomains.applying storedList domains.Instance with
        | EmailDomains.Rule(EmailDomains.Level.Connector, list) -> "connector", t.ConnectorRuleConnector(EmailDomains.display list)
        | EmailDomains.Rule(EmailDomains.Level.Instance, list) -> "instance", t.ConnectorRuleInstance(EmailDomains.display list)
        | EmailDomains.NoRule -> "anyone", t.ConnectorRuleAnyone

    let typedDomains, domainsProblem =
        match appleDraft, microsoftDraft, domains.CardDraft with
        // The Apple and Microsoft forms refused for any of their fields keep every
        // typed value, the domains among them.
        | Some draft, _, _ -> draft.Domains, draft.Problems.TryFind AppleConnector.DomainsField
        | None, Some draft, _ -> draft.Domains, draft.Problems.TryFind MicrosoftConnector.DomainsField
        | None, None, Some(draftSlug, draft) when draftSlug = slug -> draft.Typed, Some draft.Problem
        | None, None, _ -> EmailDomains.display storedList, None

    let unreadableStore =
        match secretStore with
        | Some store when not ((ctx.GetService<SecretStores>()).CanRead store) -> Some store
        | _ -> None

    let pillsOf () =
        [ if isConfigured then
              Components.pill
                  Components.MutedTone
                  (if isShared then t.ConnectorSharedBadge else t.ConnectorNotSharedBadge)
              :> HtmlElement

          match secretStore with
          | Some store ->
              (Components.pill Components.MutedTone (t.SecretStoreLabel store)).data("secret-store", store)
              :> HtmlElement
          | None -> ()

          if isConfigured then
              Components.stateBadge isEnabled t.Enabled t.Disabled :> HtmlElement
          else
              Components.pill Components.MutedTone t.NotConfigured :> HtmlElement ]

    let toggleForm =
        // htmx intercepts this; it is a real form either way (D11).
        (form(method = "post", action = confirmed (detailPath slug + "/toggle"), class' = "inline") {
            ctx.GetAntiforgeryInput()

            button(type' = "submit", class' = Components.btnSm Components.Ghost)
                .attr("hx-post", confirmed (detailPath slug + "/toggle"))
                .attr("hx-target", "[data-frame]")
                .attr("hx-select", "[data-frame]")
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

    let deleteForm =
        (form(method = "post", action = confirmed (detailPath slug + "/delete"), class' = "inline") {
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

    let pane =
        Components.framePane
            view
            "connector-heading"
            (listPath, t.NavConnectors)
            [ Components.paneHeading
                  "connector-heading"
                  (match row.Existing with
                   | Some c -> c.DisplayName
                   | None -> row.Kind.DisplayName)
                  (pillsOf ())

              if isConfigured then
                  (p(class' = "um-hint um-mono mt-1") { slug }) :> HtmlElement

              if notice.IsSome || offeredNowhere || unreadableStore.IsSome then
                  (div(class' = "mt-6 space-y-3") {
                      match notice with
                      | Some block -> block
                      | None -> Fragment() :> HtmlElement

                      if offeredNowhere then
                          (div(class' = "um-alert um-alert-warning").attr("role", "note") {
                              Components.icon Theme.Warning
                              span() { t.ConnectorOfferedNowhere }
                          })
                          :> HtmlElement

                      // FR-021: known without a network call, so an operator who lost a
                      // store's credentials sees it here rather than as a failed sign-in.
                      match unreadableStore with
                      | Some store ->
                          (div(class' = "um-alert um-alert-warning").attr("role", "note").data("secret-unreadable", slug) {
                              Components.icon Theme.Warning
                              span() { t.SecretUnreadable store }
                          })
                          :> HtmlElement
                      | None -> Fragment() :> HtmlElement
                  })
                  :> HtmlElement

              Components.paneSection
                  t.ConnectorSetupTitle
                  (div(class' = "space-y-5") {
                      // FR-003: said on every connector, because the first time an
                      // operator learns what the name matters for should not be a
                      // person asking "what is this app?" on a consent screen.
                      (div(class' = "um-alert um-alert-info").attr("role", "note").data("naming-advice", "") {
                          Components.icon Theme.Info
                          // Apple's note claims nothing about Apple's own screen:
                          // which name it shows for a Services ID is not confirmed.
                          span() {
                              match row.Kind with
                              | Apple -> t.AppleNamingAdvice
                              // Microsoft's own page says the registration's name is
                              // shown to app users.
                              | Microsoft -> t.MicrosoftNamingAdvice
                              | _ -> t.ConnectorNamingAdvice providerName
                          }
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

                          (p(class' = "um-hint").data("issuer-fixed", "") { t.ConnectorIssuerFixedNote })
                          :> HtmlElement
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

                      (p(class' = "text-body-md text-on-surface").data("rule", ruleMarker) { ruleText })
                      :> HtmlElement
                  })

              Components.paneSection
                  t.ConnectorSettingsTitle
                  (form(method = "post", action = detailPath slug, class' = "space-y-3") {
                      ctx.GetAntiforgeryInput()

                      // FR-013: the slug and the kind are not fields. Neither ever
                      // changes, so the form has nothing to edit them with.
                      match row.Kind with
                      // Apple's values are not a client ID and a client secret: a
                      // Services ID, a Team ID, a Key ID and a .p8 key.
                      | Apple -> AppleConnector.fields t row.Existing appleDraft
                      // Microsoft's values are a client ID, a secret, who may sign in
                      // and a tenant ID.
                      | Microsoft -> MicrosoftConnector.fields t row.Existing microsoftDraft
                      | _ ->
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
                              (Some(if isConfigured then t.ReplaceSecretHint else t.ClientSecretHint))
                              None

                      sharedBox
                          t
                          (match appleDraft, microsoftDraft with
                           | Some draft, _ -> draft.Shared
                           | None, Some draft -> draft.Shared
                           | None, None -> isShared)

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
                      }
                  })

              // The Apple button's logo, with a form of its own so that adding or
              // removing it never touches the credentials. Only for a stored Apple
              // connector: an unconfigured kind has no button to put a logo on.
              match row.Kind with
              | Apple when isConfigured -> appleLogoSection ctx t appleLogo appleLogoProblem
              | _ -> Fragment() :> HtmlElement

              // An unconfigured kind has nothing to switch off or delete.
              if isConfigured then
                  (Components.paneActions [ toggleForm; deleteForm ]).data("connector-actions", "")
                  :> HtmlElement ]

    pane
        .data("connector", slug)
        .data("connector-kind", row.Kind.Value)
        .data("connector-shared", (if isShared then "shared" else "not-shared"))
        .data("connector-state", (if isEnabled then "enabled" else "disabled"))
    |> ignore

    if offeredNowhere then
        pane.data("offered-nowhere", "") |> ignore

    pane

// ---------------------------------------------------------------------------
// Screen
// ---------------------------------------------------------------------------

/// What a warning is about: the action the apps would be stranded by.
type private StrandAction =
    | Disabling
    | Deleting
    /// Saving a connector that turns a shared connector to not shared (FR-014).
    | Unsharing

/// The apps a disable, a delete or an unshare would leave with no way to sign
/// in, named, with the button that goes ahead anyway (FR-010). Drawn only for
/// a change the operator just submitted (the single-use cookie says so) and
/// recomputed here, so the redirect never has to carry app names.
type private StrandWarning =
    { Action: StrandAction
      /// What the stored connector holds. Every form action is built from its
      /// slug, and the unshare confirmation posts its values back: the warning
      /// said nothing was changed, so what was typed on the pane is not carried
      /// through it.
      Stored: Connector
      Apps: (string * string) list }

let private strandWarning (ctx: HttpContext) (t: Texts.Texts) (warning: StrandWarning) =
    div(class' = "space-y-2") {
        p() { t.StrandedAppsTitle }

        ul() {
            for clientId, name in warning.Apps do
                li().data("stranded-app", clientId) { name }
        }

        let stored = warning.Stored

        match warning.Action with
        | Unsharing ->
            // The pane's own save, with the stored values and the box
            // unticked: the pane's form cannot be re-posted from here. Each
            // kind's save needs what it reads, or the confirmation would be
            // refused, or would wipe the value.
            form(method = "post", action = detailPath stored.Slug + "?confirm=yes", class' = "inline") {
                ctx.GetAntiforgeryInput()
                input(type' = "hidden", name = "displayName", value = stored.DisplayName)
                input(type' = "hidden", name = "clientId", value = stored.ClientId)

                match stored.Kind with
                | Passkey -> input(type' = "hidden", name = "enrolment", value = stored.PasskeyEnrolment.Value)
                | Fediverse
                | Bluesky ->
                    input(type' = "hidden", name = "scope", value = Fediverse.scopeValue stored.FediverseAccess)
                    input(type' = "hidden", name = "servers", value = String.concat "\n" stored.FediverseAccess.Servers)
                | Apple ->
                    // Apple's save reads its own four values and the list, and would
                    // read an absent list as "clear it". No `privateKey`: an absent
                    // key keeps the stored one, so the key never travels here.
                    input(type' = "hidden", name = AppleConnector.ServicesIdField, value = stored.ClientId)

                    match stored.AppleKey with
                    | Some ids ->
                        input(type' = "hidden", name = AppleConnector.TeamIdField, value = ids.TeamId)
                        input(type' = "hidden", name = AppleConnector.KeyIdField, value = ids.KeyId)
                    | None -> ()

                    input(type' = "hidden", name = AppleConnector.DomainsField, value = EmailDomains.display stored.AllowedEmailDomains)
                | Microsoft ->
                    // Microsoft's save needs the card and the list, and no secret: an
                    // absent secret keeps the stored one. A row with no readable
                    // audience posts none, and the save refuses it as it would any.
                    match stored.MicrosoftAudience with
                    | Some audience ->
                        let card, tenant =
                            match audience with
                            | PersonalAccounts -> "consumers", ""
                            | OneOrganization g -> "tenant", g.ToString "D"
                            | AnyOrganization -> "organizations", ""
                            | PersonalAndAnyOrganization -> "common", ""

                        input(type' = "hidden", name = MicrosoftConnector.AudienceField, value = card)
                        input(type' = "hidden", name = MicrosoftConnector.TenantIdField, value = tenant)
                    | None -> ()

                    input(type' = "hidden", name = MicrosoftConnector.DomainsField, value = EmailDomains.display stored.AllowedEmailDomains)
                | _ -> ()

                button(type' = "submit", class' = Components.btnSm Components.Danger) {
                    t.ConnectorNotSharedStrandsConfirm
                }
            }
        | action ->
            let path = if action = Deleting then "/delete" else "/toggle"

            form(method = "post", action = detailPath stored.Slug + path + "?confirm=yes", class' = "inline") {
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
    (view: Components.FrameView)
    (state: AddFormState)
    (notice: HtmlElement option)
    =
    let problemAt field =
        match state.Problem with
        | Some(at, message) when at = field -> Some message
        | _ -> None

    let form' =
        form(method = "post", action = listPath, class' = "space-y-4").data("add-connector", "")

    match state.Problem with
    | Some(field, _) -> form'.data("add-error", field) |> ignore
    | None -> ()

    Components.framePane
        view
        "connector-heading"
        (listPath, t.NavConnectors)
        [ Components.paneHeading "connector-heading" t.ConnectorAddTitle []

          match notice with
          | Some block -> (div(class' = "mt-6") { block }) :> HtmlElement
          | None -> Fragment() :> HtmlElement

          (div(class' = "mt-6 space-y-4").data("add-connector-section", "") {
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
          })
          :> HtmlElement ]


/// FR-016: the instance list, a pane of its own at the end of the list. It posts
/// to its own route, never under a connector's slug.
let private domainsPane
    (ctx: HttpContext)
    (t: Texts.Texts)
    (domains: Domains)
    (view: Components.FrameView)
    (notice: HtmlElement option)
    =
    let typed, problem =
        match domains.InstanceDraft with
        | Some draft -> draft.Typed, Some draft.Problem
        | None -> EmailDomains.display domains.Instance, None

    (Components.framePane
        view
        "connector-heading"
        (listPath, t.NavConnectors)
        [ Components.paneHeading "connector-heading" t.AllowedDomainsTitle []

          match notice with
          | Some block -> (div(class' = "mt-6") { block }) :> HtmlElement
          | None -> Fragment() :> HtmlElement

          (div(class' = "mt-6 space-y-5") {
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
          })
          :> HtmlElement ])
        .data("card", "allowed-domains")

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

/// What the URL asked for: the list, one connector, the add form, or the
/// instance's allowed domains.
type private Selection =
    | ListUrl
    | Item of string
    | Create
    | DomainsPane

/// The notices that refuse something, drawn as a warning; every other is a success.
let private noticeKind (code: string) =
    match code with
    | "connector-unknown"
    | "connector-not-found"
    | "connector-client-id-required"
    | "connector-secret-required"
    | "connector-slug-invalid"
    | "connector-slug-taken"
    | "connector-display-name-required"
    | "connector-last-enabled"
    | "connector-delete-last-enabled"
    | "passkey-connector-invalid"
    | "passkey-invitation-invalid"
    | "passkey-invitation-not-open"
    | "passkey-not-enabled"
    | "connector-strands-apps"
    | "connector-delete-strands-apps"
    | "connector-enable-apple-key"
    | "connector-enable-bluesky-origin"
    | "allowed-domains-removal" -> Components.Warning
    | _ -> Components.Success

/// One notice block: the removal warning, what the provider said, the apps a
/// change would strand, or the plain text of the code. Drawn at the top of the
/// page or inside the pane it is about.
let private noticeBlock
    (ctx: HttpContext)
    (t: Texts.Texts)
    (domains: Domains)
    (strand: StrandWarning option)
    (explained: string option)
    (code: string)
    : HtmlElement =
    match code, strand with
    | "allowed-domains-removal", _ ->
        match domains.Removal with
        | Some names -> (removalWarning ctx t code names) :> HtmlElement
        // No cookie (a link, a refresh, another pane): the fixed text only.
        | None -> (div().data("notice", code) { Components.alert Components.Warning (t.Notice code) }) :> HtmlElement
    // A notice that carries what the provider said (`explained`) is a
    // warning, not a success: the provider was saved disabled, or would not
    // be enabled.
    | _, None when explained.IsSome ->
        (div().data("notice", code) { Components.alert Components.Warning explained.Value })
        :> HtmlElement
    | _, Some warning ->
        (div().data("notice", code) {
            Components.alert Components.Warning (t.Notice code)
            strandWarning ctx t warning
        })
        :> HtmlElement
    | _, None ->
        (div().data("notice", code) { Components.alert (noticeKind code) (t.Notice code) })
        :> HtmlElement

let private view
    ctx
    (t: Texts.Texts)
    (origin: PublicOrigin.PublicOrigin)
    (rows: Row list)
    (enabledCount: int)
    (chosenSlugs: Set<string>)
    (domains: Domains)
    (addableKinds: ProviderKind list)
    (selection: Selection)
    (selected: Row option)
    (add: AddFormState)
    (notice: string option)
    (strand: StrandWarning option)
    (explained: string option)
    (appleDraft: AppleConnector.Draft option)
    (microsoftDraft: MicrosoftConnector.Draft option)
    (appleLogo: AppleLogo.AppleLogo option)
    (appleLogoProblem: string option)
    (invitations: PasskeyInvitations.Invitation list)
    (revealedLink: string option)
    =
    let block code = noticeBlock ctx t domains strand explained code
    let blueskyAllowDev = (ctx.GetService<Providers.Bluesky.Options>()).AllowDev

    let frameView =
        match selection, selected with
        | Create, _
        | DomainsPane, _ -> Components.DetailView
        | Item _, Some _ -> Components.DetailView
        | _ -> Components.ListView

    let pageTitle =
        match selection, selected with
        | Create, _ -> t.ConnectorAddTitle
        | DomainsPane, _ -> $"{t.AllowedDomainsTitle} · {t.ConnectorsTitle}"
        | Item _, Some row ->
            let name =
                match row.Existing with
                | Some c -> c.DisplayName
                | None -> row.Kind.DisplayName

            $"{name} · {t.ConnectorsTitle}"
        | _ -> t.ConnectorsTitle

    // On the list URL, and for a connector that is not there, the notice is about
    // the page; for a known connector, the add form and the domains, it is about
    // what the pane shows.
    let pageNotice =
        match selection with
        | Item _ when selected.IsSome -> None
        | Item _ -> Some "connector-not-found"
        | ListUrl -> notice
        | Create
        | DomainsPane -> None

    let paneNotice =
        match selection, selected with
        | Item _, Some _
        | Create, _
        | DomainsPane, _ -> notice |> Option.map block
        | _ -> None

    let storedCount = rows |> List.filter (fun row -> row.Existing.IsSome) |> List.length

    let addButton =
        let button = a(href = newPath, class' = Components.btnSm Components.Primary)
        Components.frameNav button newPath |> ignore

        if selection = Create then
            button.attr("aria-current", "page") |> ignore

        (button {
            Components.icon Theme.Add
            t.ConnectorAddButton
        })
        :> HtmlElement

    let isSelected (row: Row) =
        selected |> Option.exists (fun s -> s.Slug = row.Slug)

    let entry (row: Row) =
        let attention =
            row.Existing.IsSome
            && (let slug = row.Slug
                let isShared = row.Existing |> Option.exists (fun c -> c.IsShared)
                let offeredNowhere = not isShared && not (Set.contains slug chosenSlugs)

                let unreadable =
                    row.Existing
                    |> Option.bind (fun c ->
                        match Secrets.SecretRef.Decode c.ClientSecretRef with
                        | Ok reference -> Some reference.Store
                        | Error _ -> None)
                    |> Option.exists (fun store -> not ((ctx.GetService<SecretStores>()).CanRead store))

                // A Bluesky connector that is enabled where Bluesky cannot work
                // (the address was changed after it was enabled).
                let blueskyBlocked =
                    row.Kind = Bluesky
                    && row.Existing |> Option.exists (fun c -> c.IsEnabled)
                    && (match Bluesky.clientIdFor blueskyAllowDev origin.Uri with
                        | Error _ -> true
                        | Ok _ -> false)

                offeredNowhere || unreadable || blueskyBlocked)

        let marker =
            match row.Existing with
            | Some c ->
                if attention then
                    Components.markerIcon Theme.Warning t.ConnectorsAttentionMarker :> HtmlElement
                else
                    Components.stateBadge c.IsEnabled t.Enabled t.Disabled :> HtmlElement
                |> Some
            | None -> None

        let title =
            match row.Existing with
            | Some c -> c.DisplayName
            | None -> row.Kind.DisplayName

        let subtitle =
            match row.Existing with
            | Some c -> (span(class' = "um-mono") { c.Slug }) :> HtmlElement
            | None -> (span() { t.NotConfigured }) :> HtmlElement

        let link =
            Components.frameEntry (detailPath row.Slug) (isSelected row) title subtitle marker

        link.data ("connector-entry", row.Slug) |> ignore

        if attention then
            link.data ("attention", "") |> ignore

        if row.Existing.IsNone then
            link.data ("entry-muted", "") |> ignore

        link :> HtmlElement

    let domainsEntry =
        (Components.frameEntry
            domainsPath
            (selection = DomainsPane)
            t.AllowedDomainsTitle
            (span() { t.ConnectorsDomainsEntryLine })
            None)
            .data("domains-entry", "")
        :> HtmlElement

    let list =
        Components.frameList
            frameView
            t.ConnectorsListLabel
            (t.ConnectorsCount storedCount)
            (Some addButton)
            (div() {
                Components.frameEntries (rows |> List.map entry)

                div(class' = "my-3 border-t border-outline-variant/40").attr("role", "presentation")

                Components.frameEntries [ domainsEntry ]
            })

    let pane =
        match selection, selected with
        | Create, _ -> addForm ctx t domains.Instance addableKinds frameView add paneNotice :> HtmlElement
        | DomainsPane, _ -> domainsPane ctx t domains frameView paneNotice :> HtmlElement
        | _, Some row ->
            match row.Kind with
            | Passkey ->
                PasskeyConnector.pane
                    ctx
                    t
                    origin
                    enabledCount
                    chosenSlugs
                    row.Existing
                    invitations
                    revealedLink
                    frameView
                    paneNotice
            | Fediverse ->
                let draft =
                    domains.CardDraft
                    |> Option.bind (fun (slug, d) -> if slug = "fediverse" then Some(d.Typed, d.Problem) else None)

                FediverseConnector.pane ctx t origin enabledCount chosenSlugs row.Existing draft frameView paneNotice
            | Bluesky ->
                let draft =
                    domains.CardDraft
                    |> Option.bind (fun (slug, d) -> if slug = "bluesky" then Some(d.Typed, d.Problem) else None)

                BlueskyConnector.pane ctx t origin enabledCount chosenSlugs row.Existing draft frameView paneNotice blueskyAllowDev
            | _ ->
                providerPane ctx t origin enabledCount chosenSlugs domains frameView paneNotice appleDraft microsoftDraft appleLogo appleLogoProblem row
                :> HtmlElement
        | _, None -> Fragment() :> HtmlElement

    div(class' = "space-y-6").data("screen", "connectors") {
        Components.pageHeading t.ConnectorsTitle (Some t.ConnectorsLead)
        originBanner ctx t origin
        secretStoreBanner ctx t

        match pageNotice with
        | Some code -> block code
        | None -> Fragment() :> HtmlElement

        Components.frame frameView list pane
    }
    |> Layout.operatorPage ctx t pageTitle []

/// The kinds the add form offers: the three real providers, OpenID Connect (which
/// has no empty card, it is only ever added here), and the fake one
/// only where the fake connector is allowed to exist (see `FakeGate`). A fake
/// connector stored elsewhere would stop the next start.
let private addableKinds (ctx: HttpContext) =
    if (ctx.GetService<Providers.Fake.FakeGate>()).IsAllowed then
        allKinds @ [ Oidc; Fake ]
    else
        allKinds @ [ Oidc ]

/// Every stored connector gets an entry, then one for each kind with none. Stored
/// ones in the order Discord, GitHub, Google (the two easy wins first, since
/// Google is the one that can refuse a homelab address outright), OpenID Connect,
/// the fake one, then by slug; the kinds not set up follow, in the order of
/// `allKinds`, and Fediverse, Bluesky then Passkey close the list.
let private rowsFor (existing: Connector list) =
    let kindOrder kind =
        match kind with
        | Discord -> 0
        | GitHub -> 1
        | Google -> 2
        | Apple -> 3
        | Microsoft -> 4
        | Oidc -> 5
        | Fake -> 6
        | Fediverse -> 7
        | Bluesky -> 8
        | Passkey -> 9

    let stored =
        existing
        |> List.filter (fun c -> c.Kind <> Passkey && c.Kind <> Fediverse && c.Kind <> Bluesky)
        |> List.sortBy (fun c -> kindOrder c.Kind, c.Slug)
        |> List.map (fun c -> { Kind = c.Kind; Existing = Some c })

    // Apple follows Google among the kinds not set up. A connector of another kind
    // that an operator named `apple` (before the kind existed) already holds that
    // address, so a second, empty Apple entry would link to the wrong pane.
    let appleSlugHeld = existing |> List.exists (fun c -> c.Slug = Apple.Value)

    // The same for Microsoft, which follows Apple.
    let microsoftSlugHeld = existing |> List.exists (fun c -> c.Slug = Microsoft.Value)

    let empty =
        allKinds @ [ Apple; Microsoft ]
        |> List.filter (fun kind ->
            not (existing |> List.exists (fun c -> c.Kind = kind))
            && not (kind = Apple && appleSlugHeld)
            && not (kind = Microsoft && microsoftSlugHeld))
        |> List.map (fun kind -> { Kind = kind; Existing = None })

    // The passkey row is never in the add form: it is the stored connector of
    // that kind, or the kind not set up.
    let passkey =
        { Kind = Passkey
          Existing = existing |> List.tryFind (fun c -> c.Kind = Passkey) }

    let fediverse =
        { Kind = Fediverse
          Existing = existing |> List.tryFind (fun c -> c.Kind = Fediverse) }

    let bluesky =
        { Kind = Bluesky
          Existing = existing |> List.tryFind (fun c -> c.Kind = Bluesky) }

    stored @ empty @ [ fediverse; bluesky; passkey ]

/// A refused list at either level, to draw again with what was typed.
type private Drafts =
    { Instance: DomainsDraft option
      Card: (string * DomainsDraft) option
      /// The Apple form, refused, with every typed value but the key.
      Apple: AppleConnector.Draft option
      /// The Microsoft form, refused, with every typed value but the secret.
      Microsoft: MicrosoftConnector.Draft option
      /// The refusal of a pasted logo, said under its field. The pasted text is not kept.
      AppleLogoProblem: string option }

let private noDrafts =
    { Instance = None
      Card = None
      Apple = None
      Microsoft = None
      AppleLogoProblem = None }

/// The screen, with the add form as given. The GET routes draw it empty; a refused
/// add draws it again with the operator's input, and so does a refused list.
let private render
    (ctx: HttpContext)
    (selection: Selection)
    (add: AddFormState)
    (drafts: Drafts)
    : System.Threading.Tasks.Task =
        task {
            let t = Texts.current ()
            let config = ctx.GetService<Database.DbConfig>()
            let! existing = ConnectorStore.listAll config
            let! appleLogo = ConnectorStore.appleLogo config
            let! choices = AppConnectors.allChoices config
            let! instanceList = EmailDomainRules.instanceList config

            // Counted over every stored connector, not over the cards, exactly
            // as the handlers count it.
            let clock = ctx.GetService<TimeProvider>()
            let! invitations = PasskeyInvitations.listOpen config clock "passkey"
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
                | Some "allowed-domains-removal" when
                    selection = DomainsPane
                    && not instanceList.IsEmpty
                    && (WebForms.NoticeReason.consume ctx "allowed-domains-removal" "list" "instance").IsSome
                    ->
                    match existing |> List.filter (fun c -> c.Kind <> Passkey && c.Kind <> Fediverse && c.Kind <> Bluesky && c.AllowedEmailDomains.IsEmpty) with
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
            // Only for a change the operator just submitted: the cookie the
            // refusal wrote names the action, and only that connector's own
            // pane reads it. A link alone draws the notice's fixed text.
            let! strand =
                match notice, selection, ctx.TryGetQueryValue "slug" with
                | Some("connector-strands-apps" | "connector-delete-strands-apps" as code), Item s, Some slug when slug = s ->
                    match existing |> List.tryFind (fun c -> c.Slug = s) with
                    | None -> System.Threading.Tasks.Task.FromResult None
                    | Some stored ->
                        let action =
                            match WebForms.NoticeReason.consume ctx code "slug" s, code with
                            | Some "delete", "connector-delete-strands-apps" -> Some Deleting
                            | Some "disable", "connector-strands-apps" -> Some Disabling
                            | Some "unshare", "connector-strands-apps" -> Some Unsharing
                            | _ -> None

                        match action with
                        | None -> System.Threading.Tasks.Task.FromResult None
                        | Some action ->
                            task {
                                let! apps =
                                    AppConnectors.appsWithChoices config (ctx.GetService<IOpenIddictApplicationManager>())

                                let after =
                                    match action with
                                    | Deleting -> existing |> List.filter (fun c -> c.Slug <> s)
                                    | Unsharing ->
                                        existing |> List.map (fun c -> if c.Slug = s then { c with IsShared = false } else c)
                                    | Disabling ->
                                        existing |> List.map (fun c -> if c.Slug = s then { c with IsEnabled = false } else c)

                                match AppConnectors.strandedByChange apps existing after with
                                | [] -> return None
                                | stranded ->
                                    return
                                        Some
                                            { Action = action
                                              Stored = stored
                                              Apps = stranded }
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
                // Apple's: the reason is what Apple's address did, or the store's
                // failure; it comes from the cookie the redirect wrote.
                | Some("connector-saved-apple-unreachable" | "connector-enable-apple-unreachable" | "connector-enable-apple-store" as code) ->
                    let said =
                        ctx.TryGetQueryValue "slug"
                        |> Option.bind (fun slug -> WebForms.NoticeReason.consume ctx code "slug" slug)
                        |> Option.map (fun reason ->
                            match code with
                            | "connector-saved-apple-unreachable" -> t.AppleSavedUnreachable reason
                            | "connector-enable-apple-unreachable" -> t.AppleEnableUnreachable reason
                            | _ -> t.AppleEnableStore reason)

                    Some(said |> Option.defaultValue (t.Notice code))
                // Microsoft's: the reason is what Microsoft's address did, or why its
                // answer could not be used; it comes from the same kind of cookie.
                | Some("connector-saved-microsoft-unreachable" | "connector-enable-microsoft-unreachable" | "connector-saved-microsoft-wrong" | "connector-enable-microsoft-wrong" | "connector-saved-microsoft-unchecked" as code) ->
                    let said =
                        ctx.TryGetQueryValue "slug"
                        |> Option.bind (fun slug -> WebForms.NoticeReason.consume ctx code "slug" slug)
                        |> Option.map (fun reason ->
                            match code with
                            | "connector-saved-microsoft-unreachable" -> t.MicrosoftSavedUnreachable reason
                            | "connector-enable-microsoft-unreachable" -> t.MicrosoftEnableUnreachable reason
                            | "connector-saved-microsoft-wrong" -> t.MicrosoftSavedWrong reason
                            | "connector-saved-microsoft-unchecked" -> t.MicrosoftSavedUnchecked reason
                            | _ -> t.MicrosoftEnableWrong reason)

                    Some(said |> Option.defaultValue (t.Notice code))
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

            let rows = rowsFor existing

            // The first connector is the one a list URL shows on a wide screen.
            let selected =
                match selection with
                | ListUrl -> List.tryHead rows
                | Item slug -> rows |> List.tryFind (fun row -> row.Slug = slug)
                | Create
                | DomainsPane -> None

            // A connector that is not here is a 404 with the list around it, unless
            // a refusal already set a status.
            match selection, selected with
            | Item _, None when ctx.Response.StatusCode = 200 -> ctx.SetStatusCode 404
            | _ -> ()

            // Only the Passkey pane takes the invitation link off the cookie, so
            // another page does not use it up.
            let revealedLink =
                match selected with
                | Some { Kind = Passkey } when selection = Item "passkey" ->
                    PasskeyConnector.InviteReveal.consume ctx "connector"
                | _ -> None

            return!
                ctx.WriteHtmlView(
                    view
                        ctx
                        t
                        (ctx.GetService<PublicOrigin.PublicOrigin>())
                        rows
                        enabledCount
                        chosenSlugs
                        domains
                        (addableKinds ctx)
                        selection
                        selected
                        add
                        notice
                        strand
                        explained
                        drafts.Apple
                        drafts.Microsoft
                        appleLogo
                        drafts.AppleLogoProblem
                        invitations
                        revealedLink
                )
        }


let private screen (selection: Selection) : EndpointHandler =
    AdminAuth.requireOperator (fun ctx -> render ctx selection emptyAddForm noDrafts)

let page: EndpointHandler = screen ListUrl

let newPage: EndpointHandler = screen Create

let domainsPage: EndpointHandler = screen DomainsPane

let detailPage (slug: string) : EndpointHandler = screen (Item slug)

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

/// A refused list on a connector's pane or the domains pane is drawn again with what was
/// typed (the secret is the one thing never redrawn), as an unprocessable form.
let private refuseDomains (ctx: HttpContext) (selection: Selection) (drafts: Drafts) =
    ctx.SetStatusCode 422
    render ctx selection emptyAddForm drafts

/// Create the Fediverse connector, or change whether it is shared and which
/// servers may sign in. A refused server entry saves nothing and is drawn again.
let saveFediverse: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let t = Texts.current ()
            let allowDev = (ctx.GetService<Umbral.Infrastructure.Providers.Fediverse.Options>()).AllowDev
            let! form = WebForms.readValidated ctx
            let shared = WebForms.checkbox form "shared"
            let confirmed = ctx.TryGetQueryValue "confirm" = Some "yes"
            let typed = WebForms.value form "servers"

            match FediverseRules.parseScope (WebForms.value form "scope") with
            | None -> return! WebForms.redirectWithNotice ctx (detailPath "fediverse") "fediverse-connector-invalid"
            | Some listed ->
                match FediverseRules.parseServers allowDev typed with
                | Error refused ->
                    let message =
                        match refused with
                        | FediverseRules.NotAServer entry -> t.FediverseServerNotAddress entry
                        | FediverseRules.Wildcard entry -> t.FediverseServerWildcard entry

                    return!
                        refuseDomains
                            ctx
                            (Item "fediverse")
                            { noDrafts with Card = Some("fediverse", { Typed = typed; Problem = message }) }
                | Ok servers ->
                    let access = { OnlyListed = listed; Servers = servers }
                    let detail = Some $"onlyListed={listed}"
                    let! existing = ConnectorStore.findBySlug config "fediverse"

                    match existing with
                    | None ->
                        let now = clock.GetUtcNow()

                        let! result =
                            ConnectorStore.create
                                config
                                now
                                { Slug = "fediverse"
                                  Kind = Fediverse
                                  DisplayName = "Fediverse"
                                  ClientId = ""
                                  ClientSecretRef = ""
                                  ClientSecretHint = ""
                                  IssuerUrl = None
                                  AllowedEmailDomains = []
                                  PasskeyEnrolment = InvitationsOnly
                                  FediverseAccess = access
                                  AppleKey = None
                                  MicrosoftAudience = None
                                  IsEnabled = true
                                  IsShared = shared
                                  CreatedUtc = now
                                  UpdatedUtc = now }
                                ("", "")

                        ignore result
                        do! recordEventWith ctx SecurityEvents.ConnectorCreated "fediverse" detail
                        return! WebForms.redirectWithNotice ctx (detailPath "fediverse") "fediverse-connector-saved"
                    | Some stored when stored.Kind <> Fediverse ->
                        return! WebForms.redirectWithNotice ctx listPath "connector-unknown"
                    | Some stored ->
                        let! stranded =
                            task {
                                if stored.IsShared && not shared && not confirmed then
                                    let! connectors = ConnectorStore.listAll config
                                    let! apps = AppConnectors.appsWithChoices config (ctx.GetService<IOpenIddictApplicationManager>())

                                    let after =
                                        connectors
                                        |> List.map (fun c -> if c.Slug = "fediverse" then { c with IsShared = false } else c)

                                    return AppConnectors.strandedByChange apps connectors after
                                else
                                    return []
                            }

                        if not stranded.IsEmpty then
                            return!
                                WebForms.NoticeReason.redirect
                                    ctx
                                    (detailPath "fediverse")
                                    "connector-strands-apps"
                                    "slug"
                                    "fediverse"
                                    "unshare"
                        else
                            let! _ = ConnectorStore.update config clock "fediverse" "Fediverse" "" shared None None None
                            let! _ = ConnectorStore.setFediverseAccess config clock "fediverse" access
                            do! recordEventWith ctx SecurityEvents.ConnectorUpdated "fediverse" detail
                            return! WebForms.redirectWithNotice ctx (detailPath "fediverse") "fediverse-connector-saved"
        })

/// Create the Bluesky connector, or change whether it is shared and which
/// servers may sign in. A refused server entry saves nothing and is drawn again.
/// Where the instance's address cannot work for Bluesky (`Bluesky.clientIdFor`),
/// nothing is saved at all: a crafted post changes nothing.
let saveBluesky: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let t = Texts.current ()
            let allowDev = (ctx.GetService<Umbral.Infrastructure.Providers.Bluesky.Options>()).AllowDev
            let! form = WebForms.readValidated ctx
            let origin = ctx.GetService<PublicOrigin.PublicOrigin>()
            let shared = WebForms.checkbox form "shared"
            let confirmed = ctx.TryGetQueryValue "confirm" = Some "yes"
            let typed = WebForms.value form "servers"

            // On an origin Bluesky cannot work at, no connector may be created
            // (FR-004). An existing one keeps its Save: the list and the shared
            // flag change nothing about the origin, and Enable is refused on its
            // own route.
            let! existingAtStart = ConnectorStore.findBySlug config "bluesky"

            match Bluesky.clientIdFor allowDev origin.Uri, existingAtStart with
            | Error _, None -> return! refuseDomains ctx (Item "bluesky") noDrafts
            | _ ->

            match FediverseRules.parseScope (WebForms.value form "scope") with
            | None -> return! WebForms.redirectWithNotice ctx (detailPath "bluesky") "bluesky-connector-invalid"
            | Some listed ->
                match FediverseRules.parseServers allowDev typed with
                | Error refused ->
                    let message =
                        match refused with
                        | FediverseRules.NotAServer entry -> t.FediverseServerNotAddress entry
                        | FediverseRules.Wildcard entry -> t.FediverseServerWildcard entry

                    return!
                        refuseDomains
                            ctx
                            (Item "bluesky")
                            { noDrafts with Card = Some("bluesky", { Typed = typed; Problem = message }) }
                | Ok servers ->
                    let access = { OnlyListed = listed; Servers = servers }
                    let detail = Some $"onlyListed={listed}"
                    let! existing = ConnectorStore.findBySlug config "bluesky"

                    match existing with
                    | None ->
                        let now = clock.GetUtcNow()

                        let! result =
                            ConnectorStore.create
                                config
                                now
                                { Slug = "bluesky"
                                  Kind = Bluesky
                                  DisplayName = "Bluesky"
                                  ClientId = ""
                                  ClientSecretRef = ""
                                  ClientSecretHint = ""
                                  IssuerUrl = None
                                  AllowedEmailDomains = []
                                  PasskeyEnrolment = InvitationsOnly
                                  FediverseAccess = access
                                  AppleKey = None
                                  MicrosoftAudience = None
                                  IsEnabled = true
                                  IsShared = shared
                                  CreatedUtc = now
                                  UpdatedUtc = now }
                                ("", "")

                        ignore result
                        do! recordEventWith ctx SecurityEvents.ConnectorCreated "bluesky" detail
                        return! WebForms.redirectWithNotice ctx (detailPath "bluesky") "bluesky-connector-saved"
                    | Some stored when stored.Kind <> Bluesky ->
                        return! WebForms.redirectWithNotice ctx listPath "connector-unknown"
                    | Some stored ->
                        let! stranded =
                            task {
                                if stored.IsShared && not shared && not confirmed then
                                    let! connectors = ConnectorStore.listAll config
                                    let! apps = AppConnectors.appsWithChoices config (ctx.GetService<IOpenIddictApplicationManager>())

                                    let after =
                                        connectors
                                        |> List.map (fun c -> if c.Slug = "bluesky" then { c with IsShared = false } else c)

                                    return AppConnectors.strandedByChange apps connectors after
                                else
                                    return []
                            }

                        if not stranded.IsEmpty then
                            return!
                                WebForms.NoticeReason.redirect
                                    ctx
                                    (detailPath "bluesky")
                                    "connector-strands-apps"
                                    "slug"
                                    "bluesky"
                                    "unshare"
                        else
                            let! _ = ConnectorStore.update config clock "bluesky" "Bluesky" "" shared None None None
                            let! _ = ConnectorStore.setFediverseAccess config clock "bluesky" access
                            do! recordEventWith ctx SecurityEvents.ConnectorUpdated "bluesky" detail
                            return! WebForms.redirectWithNotice ctx (detailPath "bluesky") "bluesky-connector-saved"
        })

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
let private refuseStoreFailed (ctx: HttpContext) (path: string) (slug: string) (failure: Secrets.StoreFailure) =
    WebForms.NoticeReason.redirect
        ctx
        path
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
    // Apple has four values and a route of its own (specs/013): it is not
    // created through the generic card path. Nor is Microsoft (specs/016): it has
    // a pane and a route of its own (task T006).
    | Apple
    | Microsoft
    | Oidc
    | Fediverse
    | Bluesky
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
            render ctx Create { state with Problem = Some(field, message) } noDrafts

        let domainsParsed = parseDomains t state.AllowedDomains

        let! slugTaken = ConnectorStore.findBySlug config state.Slug

        // A kind's own name is where its empty card posts, so a connector of
        // another kind under it would be edited as the wrong provider; `oidc`
        // itself is a kind's name too.
        let reserved = (parseKind state.Slug).IsSome || reservedSlugs.Contains state.Slug

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
                  FediverseAccess = FediverseAccess.Any
                  AppleKey = None
                  MicrosoftAudience = None
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
            | None -> return! WebForms.redirectWithNotice ctx (detailPath state.Slug) "connector-added-oidc"
            | Some reason ->
                return!
                    WebForms.NoticeReason.redirect
                        ctx
                        (detailPath state.Slug)
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
            | None -> return! WebForms.redirectWithNotice ctx newPath "connector-unknown"
            | Some Oidc -> return! createOidc ctx form
            | Some kind when not (mayCreate ctx kind) ->
                return! WebForms.redirectWithNotice ctx newPath "connector-unknown"
            | Some kind ->

            // A kind's own name is where its empty card posts, so a connector
            // of another kind under it would be edited as the wrong provider.
            let reservedForOtherKind =
                match parseKind slug with
                | Some named -> named <> kind
                | None -> false

            if not (Ids.isValidSlug slug) then
                return! WebForms.redirectWithNotice ctx newPath "connector-slug-invalid"
            elif taken.IsSome || reservedForOtherKind || reservedSlugs.Contains slug then
                return! WebForms.redirectWithNotice ctx newPath "connector-slug-taken"
            elif displayName = "" then
                return! WebForms.redirectWithNotice ctx newPath "connector-display-name-required"
            elif clientId = "" then
                return! WebForms.redirectWithNotice ctx newPath "connector-client-id-required"
            else

            match newSecret with
            | None -> return! WebForms.redirectWithNotice ctx newPath "connector-secret-required"
            | Some plaintext ->

            // Before the secret is written: a refused list leaves nothing behind.
            let typedDomains = WebForms.value form "allowedDomains"

            match parseDomains (Texts.current ()) typedDomains with
            | Error message ->
                ctx.SetStatusCode 422

                return!
                    render
                        ctx
                        Create
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
            | Error failure -> return! refuseStoreFailed ctx newPath slug failure
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
                      FediverseAccess = FediverseAccess.Any
                      AppleKey = None
                      MicrosoftAudience = None
                      // A connector arrives enabled, as the first of a kind does.
                      IsEnabled = true
                      IsShared = WebForms.checkbox form "shared"
                      CreatedUtc = now
                      UpdatedUtc = now }
                    secret

            if added <> ConnectorStore.Created then
                do! discard ctx reference
                return! WebForms.redirectWithNotice ctx newPath "connector-slug-taken"
            else

            do! recordEventWith ctx SecurityEvents.ConnectorCreated slug (Some $"store={reference.Store}")

            if not domains.IsEmpty then
                do! recordDomainsChanged ctx (Some slug) domains

            return! WebForms.redirectWithNotice ctx (detailPath slug) "connector-added"
        })

/// What the Microsoft save hands to `applyChanges`: the audience to write with the
/// row, and the notice (code and reason) of a failed Microsoft check, said after the
/// save in place of the plain "saved" (the connector keeps its enabled state).
type private MicrosoftSave =
    { Audience: MicrosoftAudience
      CheckNotice: (string * string) option }

/// Write the changes of a saved connector: the card's values and, when a new
/// secret came with them, the secret itself, in the active store, under the
/// lease that keeps a move from interleaving. Shared by the generic card save
/// and the Apple save, so a replaced key travels exactly the way a replaced
/// client secret does. `hintOf` says what the row keeps of the new secret (the
/// last four characters for a client secret, nothing for a private key);
/// `appleKey` is the Team ID and Key ID to store, None for every other kind.
let private applyChanges
    (ctx: HttpContext)
    (slug: string)
    (newName: string)
    (clientId: string)
    (newShared: bool)
    (domains: string list option)
    (newSecret: string option)
    (hintOf: string -> string)
    (appleKey: AppleKeyIds option)
    (microsoft: MicrosoftSave option)
    : System.Threading.Tasks.Task =
    task {
        let config = ctx.GetService<Database.DbConfig>()
        let clock = ctx.GetService<TimeProvider>()
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
                | None -> return! WebForms.redirectWithNotice ctx listPath "connector-unknown"
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
                            | Ok reference -> return Ok(Some(reference, (reference.Encoded, hintOf plaintext)))
                            | Error failure ->
                                do! recordPutFailed ctx slug failure
                                return Error failure
                        }
                    | None -> System.Threading.Tasks.Task.FromResult(Ok None)

                match secretUpdate with
                | Error failure -> return! refuseStoreFailed ctx (detailPath slug) slug failure
                | Ok written ->

                let! updated =
                    match microsoft with
                    // Microsoft's row also writes the audience and always the list.
                    | Some { Audience = audience } ->
                        ConnectorStore.saveMicrosoft
                            config
                            clock
                            slug
                            newName
                            clientId
                            newShared
                            (defaultArg domains stored.AllowedEmailDomains)
                            (written |> Option.map snd)
                            audience
                    | None ->
                        ConnectorStore.update config clock slug newName clientId newShared domains (written |> Option.map snd) appleKey

                match written with
                | Some(reference, _) when not updated ->
                    // The connector vanished between the read and the update;
                    // the secret just written belongs to nothing.
                    do! discard ctx reference
                | _ -> ()

                if not updated then
                    return! WebForms.redirectWithNotice ctx listPath "connector-unknown"
                else
                    // FR-022: a changed secret names the store that now holds it.
                    // The audience too, when it actually changed, judged against the
                    // row as read under the lease (it says who may sign in, and is no secret).
                    let detail =
                        [ match written with
                          | Some(reference, _) -> $"store={reference.Store}"
                          | None -> ()
                          match microsoft with
                          | Some { Audience = audience } when stored.MicrosoftAudience <> Some audience ->
                              $"audience={Umbral.Domains.Microsoft.stored audience}"
                          | _ -> () ]

                    do!
                        recordEventWith
                            ctx
                            SecurityEvents.ConnectorUpdated
                            slug
                            (if detail.IsEmpty then None else Some(String.concat ";" detail))

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
                    | None ->
                        match microsoft with
                        | Some { CheckNotice = Some(code, reason) } ->
                            return! WebForms.NoticeReason.redirect ctx (detailPath slug) code "slug" slug reason
                        | _ -> return! WebForms.redirectWithNotice ctx (detailPath slug) "connector-saved"
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
                                (detailPath slug)
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
            | None -> return! WebForms.redirectWithNotice ctx (detailPath slug) "connector-secret-busy"
    }

/// Save a connector. The kind comes from the stored row, never from the slug
/// (FR-012); a slug with no row is the first connector of the kind it names,
/// which is the path of a kind not set up.
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
            | None -> return! WebForms.redirectWithNotice ctx listPath "connector-unknown"
            | Some _ when clientId = "" ->
                return! WebForms.redirectWithNotice ctx (detailPath slug) "connector-client-id-required"
            | Some _ when fullForm && displayName = "" ->
                return! WebForms.redirectWithNotice ctx (detailPath slug) "connector-display-name-required"
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
                // A refused list is drawn again on the connector's pane. A slug
                // with no row has a pane only while its kind is not set up; once
                // another connector of the kind exists, the form is stale.
                let! all = ConnectorStore.listAll config

                if existing.IsNone && all |> List.exists (fun c -> c.Kind = kind) then
                    return! WebForms.redirectWithNotice ctx listPath "connector-unknown"
                else

                return!
                    refuseDomains
                        ctx
                        (Item slug)
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
            | None, None -> return! WebForms.redirectWithNotice ctx (detailPath slug) "connector-secret-required"

            | None, Some plaintext ->
                let now = clock.GetUtcNow()

                match! putNew ctx slug now plaintext with
                | Error failure -> return! refuseStoreFailed ctx (detailPath slug) slug failure
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
                          FediverseAccess = FediverseAccess.Any
                          AppleKey = None
                          MicrosoftAudience = None
                          // A connector arrives enabled: the operator came here to
                          // turn it on, and a saved-but-off connector is a puzzle.
                          IsEnabled = true
                          IsShared = (not fullForm) || isShared
                          CreatedUtc = now
                          UpdatedUtc = now }
                        secret

                if added <> ConnectorStore.Created then
                    do! discard ctx reference
                    return! WebForms.redirectWithNotice ctx (detailPath slug) "connector-slug-taken"
                else
                    do! recordEventWith ctx SecurityEvents.ConnectorCreated slug (Some $"store={reference.Store}")

                    match domains with
                    | Some(_ :: _ as list) -> do! recordDomainsChanged ctx (Some slug) list
                    | _ -> ()

                    return! WebForms.redirectWithNotice ctx (detailPath slug) "connector-saved"

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
                        WebForms.NoticeReason.redirect ctx (detailPath slug) "connector-strands-apps" "slug" slug "unshare"
                else

                return! applyChanges ctx slug newName clientId newShared domains newSecret Secrets.hint None None
        })

/// A refused Apple form is drawn again on the connector's pane as an
/// unprocessable form: every typed value but the key, each message under its
/// field (FR-005).
let private refuseApple (ctx: HttpContext) (draft: AppleConnector.Draft) =
    ctx.SetStatusCode 422
    render ctx (Item "apple") emptyAddForm { noDrafts with Apple = Some draft }

/// Create the Apple connector, or change it (specs/013 FR-001 to FR-008). Apple's
/// values are not a client ID and a client secret, so it has a route of its own
/// ahead of the generic one; the pane is the generic pane with other fields.
///
/// Every refusal is collected before anything is written, and a refused form
/// saves nothing. A new connector is checked with its key in hand (the key signs,
/// Apple answers) before the key reaches the store: unreachable saves it disabled
/// and says why; an unusable key is a refusal. Replacing the key of a saved
/// connector runs no network check and leaves it enabled or disabled as it was,
/// because a moment in which Apple cannot be reached must not switch off a
/// connector that works (FR-006). The row keeps no characters of the key.
let saveApple: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let t = Texts.current ()
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! form = WebForms.readValidated ctx
            let slug = Apple.Value
            let confirmed = ctx.TryGetQueryValue "confirm" = Some "yes"
            let! existing = ConnectorStore.findBySlug config slug

            match existing with
            // The slug is another kind's (a connector an operator named `apple`
            // before the kind existed): this form would edit it as the wrong one.
            | Some stored when stored.Kind <> Apple -> return! WebForms.redirectWithNotice ctx listPath "connector-unknown"
            | _ ->

            match AppleConnector.parse t (parseDomains t) existing.IsSome form with
            | Error draft -> return! refuseApple ctx draft
            | Ok parsed ->

            let typedDomains = WebForms.value form AppleConnector.DomainsField

            match existing with
            | None ->
                let pem = parsed.Key |> Option.get

                let! checkedKey =
                    Providers.Apple.check
                        (ctx.GetService<OidcDiscovery.OidcDiscovery>())
                        clock
                        parsed.Ids
                        parsed.ServicesId
                        pem

                match checkedKey with
                | Providers.Apple.KeyUnusable ->
                    return! refuseApple ctx (AppleConnector.withKeyUnusable t (AppleConnector.draftOf parsed typedDomains))
                | Providers.Apple.Ready
                | Providers.Apple.AppleUnreachable _ ->

                let unreachable =
                    match checkedKey with
                    | Providers.Apple.AppleUnreachable reason -> Some(WebForms.truncate 300 reason)
                    | _ -> None

                // `now` is the `created_utc` of the row and the discriminator of the
                // secret's name, so it is taken once.
                let now = clock.GetUtcNow()

                match! putNew ctx slug now pem with
                | Error failure -> return! refuseStoreFailed ctx (detailPath slug) slug failure
                | Ok(reference, (encoded, _)) ->

                let! added =
                    ConnectorStore.create
                        config
                        now
                        { Slug = slug
                          Kind = Apple
                          DisplayName = parsed.DisplayName
                          ClientId = parsed.ServicesId
                          ClientSecretRef = ""
                          ClientSecretHint = ""
                          IssuerUrl = None
                          AllowedEmailDomains = parsed.Domains
                          PasskeyEnrolment = InvitationsOnly
                          FediverseAccess = FediverseAccess.Any
                          AppleKey = Some parsed.Ids
                          MicrosoftAudience = None
                          IsEnabled = unreachable.IsNone
                          IsShared = parsed.Shared
                          CreatedUtc = now
                          UpdatedUtc = now }
                        // No characters of a private key are kept as a hint (FR-002).
                        (encoded, "")

                if added <> ConnectorStore.Created then
                    do! discard ctx reference
                    return! WebForms.redirectWithNotice ctx (detailPath slug) "connector-slug-taken"
                else

                do!
                    recordEventWith
                        ctx
                        SecurityEvents.ConnectorCreated
                        slug
                        (Some(
                            match unreachable with
                            | Some reason -> $"store={reference.Store};saved_disabled: {reason}"
                            | None -> $"store={reference.Store}"
                        ))

                if not parsed.Domains.IsEmpty then
                    do! recordDomainsChanged ctx (Some slug) parsed.Domains

                match unreachable with
                | None -> return! WebForms.redirectWithNotice ctx (detailPath slug) "connector-saved"
                | Some reason ->
                    return!
                        WebForms.NoticeReason.redirect
                            ctx
                            (detailPath slug)
                            "connector-saved-apple-unreachable"
                            "slug"
                            slug
                            reason

            | Some stored ->
                // Turning sharing off can leave an all-shared app with nothing to
                // offer. Nothing changes until the operator confirms (FR-010).
                let! stranded =
                    task {
                        if stored.IsShared && not parsed.Shared && not confirmed then
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
                        WebForms.NoticeReason.redirect ctx (detailPath slug) "connector-strands-apps" "slug" slug "unshare"
                else

                // A new key goes through the path of a replaced client secret (the
                // lease, the write, the removal of the old copy), with no hint.
                return!
                    applyChanges
                        ctx
                        slug
                        parsed.DisplayName
                        parsed.ServicesId
                        parsed.Shared
                        (Some parsed.Domains)
                        parsed.Key
                        (fun _ -> "")
                        (Some parsed.Ids)
                        None
        })

/// A refused Microsoft form is drawn again on the connector's pane as an
/// unprocessable form: every typed value but the secret, each message under its
/// field.
let private refuseMicrosoft (ctx: HttpContext) (draft: MicrosoftConnector.Draft) =
    ctx.SetStatusCode 422
    render ctx (Item "microsoft") emptyAddForm { noDrafts with Microsoft = Some draft }

/// Create the Microsoft connector, or change it (specs/016 contract). Its values
/// are a client ID, a secret, who may sign in and a tenant ID, so it has a route of
/// its own ahead of the generic one; the pane is the generic pane with other fields.
///
/// Every refusal is collected before anything is written, and a refused form saves
/// nothing. Then Microsoft is asked for the discovery document of the chosen
/// audience (`CheckAt`). A new connector is saved enabled only when that worked;
/// unreachable or not usable saves it disabled and says why. A saved connector
/// whose choice of who may sign in changed is not saved at all when the check
/// fails (422, the reason under the choice, nothing written). One whose choice
/// did not change keeps its enabled state whatever the check says, because a
/// moment in which Microsoft cannot be reached must not switch off a connector
/// that works; an enabled one says it was not checked, a disabled one that it is
/// not enabled.
let saveMicrosoft: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let t = Texts.current ()
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! form = WebForms.readValidated ctx
            let slug = ProviderKind.Microsoft.Value
            let confirmed = ctx.TryGetQueryValue "confirm" = Some "yes"
            let! existing = ConnectorStore.findBySlug config slug

            match existing with
            // The slug is another kind's: this form would edit it as the wrong one.
            | Some stored when stored.Kind <> Microsoft -> return! WebForms.redirectWithNotice ctx listPath "connector-unknown"
            | _ ->

            match MicrosoftConnector.parse t (parseDomains t) existing.IsSome form with
            | Error draft -> return! refuseMicrosoft ctx draft
            | Ok parsed ->

            // Microsoft is asked first, before anything is written. What it said
            // becomes a notice with its reason, or nothing when it answered well.
            let! discovered =
                ctx
                    .GetService<OidcDiscovery.OidcDiscovery>()
                    .CheckAt(
                        Umbral.Domains.Microsoft.documentBase parsed.Audience,
                        Umbral.Domains.Microsoft.discoveryIssuer parsed.Audience
                    )

            // The notice of a failed check: its code for a save, and the reason as
            // the cookie carries it.
            let failed =
                match discovered with
                | OidcDiscovery.Discovered _ -> None
                | OidcDiscovery.Unreachable reason ->
                    Some("connector-saved-microsoft-unreachable", WebForms.truncate 300 reason)
                | OidcDiscovery.Wrong reason -> Some("connector-saved-microsoft-wrong", WebForms.truncate 300 reason)

            match existing with
            | None ->
                // `now` is the `created_utc` of the row and the discriminator of the
                // secret's name, so it is taken once.
                let now = clock.GetUtcNow()

                match! putNew ctx slug now (Option.get parsed.Secret) with
                | Error failure -> return! refuseStoreFailed ctx (detailPath slug) slug failure
                | Ok(reference, secret) ->

                let! added =
                    ConnectorStore.create
                        config
                        now
                        { Slug = slug
                          Kind = Microsoft
                          DisplayName = parsed.DisplayName
                          ClientId = parsed.ClientId
                          ClientSecretRef = ""
                          ClientSecretHint = ""
                          IssuerUrl = None
                          AllowedEmailDomains = parsed.Domains
                          PasskeyEnrolment = InvitationsOnly
                          FediverseAccess = FediverseAccess.Any
                          AppleKey = None
                          MicrosoftAudience = Some parsed.Audience
                          IsEnabled = failed.IsNone
                          IsShared = parsed.Shared
                          CreatedUtc = now
                          UpdatedUtc = now }
                        secret

                if added <> ConnectorStore.Created then
                    do! discard ctx reference
                    return! WebForms.redirectWithNotice ctx (detailPath slug) "connector-slug-taken"
                else

                do!
                    recordEventWith
                        ctx
                        SecurityEvents.ConnectorCreated
                        slug
                        (Some(
                            let audience = Umbral.Domains.Microsoft.stored parsed.Audience

                            match failed with
                            | Some(_, reason) -> $"store={reference.Store};audience={audience};saved_disabled: {reason}"
                            | None -> $"store={reference.Store};audience={audience}"
                        ))

                if not parsed.Domains.IsEmpty then
                    do! recordDomainsChanged ctx (Some slug) parsed.Domains

                match failed with
                | None -> return! WebForms.redirectWithNotice ctx (detailPath slug) "connector-saved"
                | Some(code, reason) -> return! WebForms.NoticeReason.redirect ctx (detailPath slug) code "slug" slug reason

            // A changed choice that Microsoft does not confirm is not saved at all
            // (spec FR-003): a mistyped tenant ID must not reach a connector that
            // people use. Nothing is written, not even the secret typed with it;
            // the form is drawn again with the reason under the choice.
            | Some stored when failed.IsSome && stored.MicrosoftAudience <> Some parsed.Audience ->
                let message =
                    match discovered with
                    | OidcDiscovery.Wrong reason -> t.MicrosoftNotConfirmed(WebForms.truncate 300 reason)
                    | OidcDiscovery.Unreachable reason -> t.MicrosoftNotAnswered(WebForms.truncate 300 reason)
                    | OidcDiscovery.Discovered _ -> ""

                return!
                    refuseMicrosoft
                        ctx
                        (MicrosoftConnector.refusedChoice
                            parsed
                            (WebForms.value form MicrosoftConnector.TenantIdField)
                            (WebForms.value form MicrosoftConnector.DomainsField)
                            message)

            | Some stored ->
                // Turning sharing off can leave an all-shared app with nothing to
                // offer. Nothing changes until the operator confirms.
                let! stranded =
                    task {
                        if stored.IsShared && not parsed.Shared && not confirmed then
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
                        WebForms.NoticeReason.redirect ctx (detailPath slug) "connector-strands-apps" "slug" slug "unshare"
                else

                // The choice did not change here. An enabled connector stays so, and
                // the notice says it was not checked; a disabled one is still not
                // enabled, so the "Saved, but not enabled" notices are true.
                let notice =
                    match failed with
                    | Some(_, reason) when stored.IsEnabled -> Some("connector-saved-microsoft-unchecked", reason)
                    | other -> other

                // A new secret goes through the path of a replaced client secret (the
                // lease, the write, the removal of the old copy).
                return!
                    applyChanges
                        ctx
                        slug
                        parsed.DisplayName
                        parsed.ClientId
                        parsed.Shared
                        (Some parsed.Domains)
                        parsed.Secret
                        Secrets.hint
                        None
                        (Some
                            { Audience = parsed.Audience
                              CheckNotice = notice })
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
                return! WebForms.redirectWithNotice ctx domainsPath "allowed-domains-saved"
            else

            match parseDomains t typed with
            | Error message ->
                return!
                    refuseDomains
                        ctx
                        DomainsPane
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
                return! WebForms.NoticeReason.redirect ctx domainsPath "allowed-domains-removal" "list" "instance" "removal"
            else

            if list <> stored then
                do! EmailDomainRules.setInstanceList config clock list
                do! recordDomainsChanged ctx None list

            return! WebForms.redirectWithNotice ctx domainsPath "allowed-domains-saved"
        })

/// Save the pasted logo of the Apple button (spec 014, FR-010 to FR-015). The text is
/// parsed to an allowlist, and only the parsed value is stored; a refusal is a 422 with its
/// message under the field and nothing stored, and the pasted text is not drawn back. A
/// blank paste with a logo saved keeps it and goes back to the pane with no notice, since
/// nothing changed ("Leave blank to keep the saved logo."); with none saved it is the
/// not-an-SVG refusal.
let saveAppleLogo: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let t = Texts.current ()
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! form = WebForms.readValidated ctx
            let slug = Apple.Value
            let! existing = ConnectorStore.findBySlug config slug

            match existing with
            | Some stored when stored.Kind = Apple ->
                let pasted = WebForms.value form "appleLogo"
                let! saved = ConnectorStore.appleLogo config

                if String.IsNullOrWhiteSpace pasted && saved.IsSome then
                    return! WebForms.redirect ctx (detailPath slug)
                else
                    match AppleLogo.parse pasted with
                    | Error error ->
                        ctx.SetStatusCode 422

                        let message =
                            match error with
                            | AppleLogo.NotSvg -> t.AppleLogoNotSvg
                            | AppleLogo.MoreThanShapes -> t.AppleLogoMoreThanShapes
                            | AppleLogo.TooLarge -> t.AppleLogoTooLarge

                        return!
                            render ctx (Item slug) emptyAddForm { noDrafts with AppleLogoProblem = Some message }
                    | Ok logo ->
                        let! _ = ConnectorStore.setAppleLogo config clock logo
                        do! recordEventWith ctx SecurityEvents.ConnectorUpdated slug (Some "apple-logo=saved")
                        return! WebForms.redirectWithNotice ctx (detailPath slug) "apple-logo-saved"
            | _ -> return! WebForms.redirectWithNotice ctx listPath "connector-unknown"
        })

/// Take the logo off the Apple button: the button shows its title alone again.
let removeAppleLogo: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! _ = WebForms.readValidated ctx
            let slug = Apple.Value
            let! existing = ConnectorStore.findBySlug config slug

            match existing with
            | Some stored when stored.Kind = Apple ->
                let! _ = ConnectorStore.clearAppleLogo config clock
                do! recordEventWith ctx SecurityEvents.ConnectorUpdated slug (Some "apple-logo=removed")
                return! WebForms.redirectWithNotice ctx (detailPath slug) "apple-logo-removed"
            | _ -> return! WebForms.redirectWithNotice ctx listPath "connector-unknown"
        })

let endpoint =
    [ GET [ route "/admin/connectors" page ]
      GET [ route "/admin/connectors/new" newPage ]
      GET [ route "/admin/connectors/domains" domainsPage ]
      GET [ routef "/admin/connectors/{%s}" (slugOfSegment >> detailPage) ]
      POST [ route "/admin/connectors" create ]
      POST [ route "/admin/allowed-domains" saveAllowedDomains ]
      POST [ route "/admin/connectors/fediverse" saveFediverse ]
      POST [ route "/admin/connectors/bluesky" saveBluesky ]
      POST [ route "/admin/connectors/apple/logo" saveAppleLogo ]
      POST [ route "/admin/connectors/apple/logo/remove" removeAppleLogo ]
      POST [ route "/admin/connectors/apple" saveApple ]
      POST [ route "/admin/connectors/microsoft" saveMicrosoft ]
      POST [ routef "/admin/connectors/{%s}" (slugOfSegment >> save) ] ]
