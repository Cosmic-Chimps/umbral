/// The Apps screen (T072-T074).
///
/// An App is an OIDC client, stored entirely by OpenIddict — Umbral owns no
/// row for it. The screen's one job is to keep an operator from building a
/// half-working client by hand: permissions come from an app-type choice, a
/// redirect URI is checked before OpenIddict ever sees it, and the generated
/// secret is shown exactly once because OpenIddict hashes it on write and
/// cannot give it back.
module Umbral.Handlers.Admin.Apps

open System
open Microsoft.AspNetCore.DataProtection
open System.Collections.Concurrent
open Microsoft.AspNetCore.Http
open OpenIddict.Abstractions
open type OpenIddict.Abstractions.OpenIddictConstants
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Domains
open Umbral.Domains.Branding
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Views

// ---------------------------------------------------------------------------
// Strings this screen needs that are not in Views/Texts.fs.
//
// Texts.fs is owned by someone else in this change; these are placeholders so
// the gap is obvious and greppable (reported back, per the brief).
// ---------------------------------------------------------------------------

[<Literal>]
let private AppSecretNotIssued =
    "This is a public client. It has no client secret — only a confidential (\"Web application\") app gets one."

[<Literal>]
let private AppNotFoundNotice = "That app doesn't exist, or has already been deleted."

[<Literal>]
let private AppTypeInvalidNotice = "Choose an app type from the list, then save again."

// ---------------------------------------------------------------------------
// The shown-once secret.
//
// The contract requires the secret to appear immediately after creation, and
// rule 3 requires every POST to end in a redirect rather than a rendered
// response. Both are true at once by stashing the plaintext in memory under a
// single-use token, redirecting with that token in the query string, and
// having the GET of the app's own page consume (remove) it on the very read
// that shows it — a refresh of the same URL then shows nothing, which is what
// "not on any later page load" means in practice.
// ---------------------------------------------------------------------------

/// Not private: AppSecret.fs stashes a regenerated secret through this same
/// single-use store, so a rotation is shown exactly once the same way a
/// creation is.
type RevealedSecret = { ClientId: string; Secret: string }

/// Carried in a Data-Protected, single-use cookie rather than a token in the
/// query string.
///
/// A URL is the wrong place for a capability that reveals a credential: query
/// strings land in access logs, browser history and `Referer` headers sent to
/// whatever the operator clicks next. The cookie never leaves the browser that
/// created the app, is encrypted at rest and in transit, is deleted on the read
/// that shows it, and expires on its own if the operator closes the tab —
/// which also means no server-side table of plaintext secrets waiting for a
/// page load that may never come.
module SecretReveal =
    [<Literal>]
    let private CookieName = "umbral_app_secret"

    [<Literal>]
    let private Purpose = "Umbral.AppSecretReveal.v1"

    /// Long enough to survive the redirect, short enough that a secret is not
    /// sitting in a cookie jar while the operator goes to lunch.
    let private Lifetime = TimeSpan.FromMinutes 5.0

    let private protector (ctx: HttpContext) =
        ctx.GetService<IDataProtectionProvider>().CreateProtector Purpose

    let stash (ctx: HttpContext) (clientId: string) (secret: string) =
        let payload = (protector ctx).Protect $"{clientId}\u001f{secret}"

        let options = CookieOptions()
        options.HttpOnly <- true
        options.IsEssential <- true
        options.Path <- "/admin"
        options.SameSite <- SameSiteMode.Strict
        options.Secure <- ctx.Request.IsHttps
        options.MaxAge <- Lifetime

        ctx.Response.Cookies.Append(CookieName, payload, options)

    let private expire (ctx: HttpContext) =
        let options = CookieOptions()
        options.Path <- "/admin"
        options.SameSite <- SameSiteMode.Strict
        options.Secure <- ctx.Request.IsHttps
        ctx.Response.Cookies.Delete(CookieName, options)

    /// The secret the cookie holds, if it is about `clientId`, the app whose pane
    /// is being drawn. A cookie about another app is left alone: it is that app's
    /// to show, and it expires by itself. One that does not decode is deleted,
    /// since nothing will ever show it.
    let consumeFor (ctx: HttpContext) (clientId: string) : RevealedSecret option =
        let payload =
            match ctx.Request.Cookies.TryGetValue CookieName with
            | true, value ->
                match Option.ofObj value with
                | Some v when v.Length > 0 -> Some v
                | _ -> None
            | _ -> None

        match payload with
        | None -> None
        | Some payload ->

        let decoded =
            try
                match ((protector ctx).Unprotect payload).Split '\u001f' with
                | [| id; secret |] -> Some { ClientId = id; Secret = secret }
                | _ -> None
            with _ ->
                None

        match decoded with
        | Some revealed when revealed.ClientId = clientId ->
            // Deleted on the read that shows it, so a refresh shows nothing.
            expire ctx
            Some revealed
        | Some _ -> None
        | None ->
            expire ctx
            None

let listPath = "/admin/apps"

/// An app's own address: where every POST about it redirects, and what a list
/// entry links to. The client id is generated, but it is escaped all the same.
let detailPath (clientId: string) = listPath + "/" + Uri.EscapeDataString clientId

// ---------------------------------------------------------------------------
// App type -> permissions
// ---------------------------------------------------------------------------

type private AppType =
    | Web
    | SpaOrNative

    member this.Value =
        match this with
        | Web -> "web"
        | SpaOrNative -> "spa"

    member this.Label(t: Texts.Texts) =
        match this with
        | Web -> t.AppTypeWebOption
        | SpaOrNative -> t.AppTypeSpaOption

    member this.Description(t: Texts.Texts) =
        match this with
        | Web -> t.AppTypeWebDescription
        | SpaOrNative -> t.AppTypeSpaDescription

    static member Parse(value: string) =
        match value with
        | "web" -> Some Web
        | "spa" -> Some SpaOrNative
        | _ -> None

let private allAppTypes = [ Web; SpaOrNative ]

let private buildDescriptor
    (clientId: string)
    (name: string)
    (appType: AppType)
    (redirectUri: Uri)
    (postLogoutRedirectUri: Uri option)
    (secret: string option)
    =
    AppClients.buildDescriptor
        clientId
        name
        (appType = Web)
        [ redirectUri ]
        (Option.toList postLogoutRedirectUri)
        secret

// ---------------------------------------------------------------------------
// Redirect URI validation (T072)
//
// "Exact, absolute, and must reject wildcards" — checked here, before
// OpenIddict ever sees the value, so the rejection names the offending value
// rather than surfacing as an opaque mismatch at the authorize step.
// ---------------------------------------------------------------------------

/// The screen and the rule share one module. The screen used to keep its own
/// copy, and that copy lacked a case: on Unix "/callback" parses as an absolute
/// file:// uri and sailed through. One rule, one place.
let private parseStrictUri (candidate: string) : Uri option =
    match Umbral.Domains.RedirectUris.acceptable candidate with
    | Ok uri -> Some uri
    | Error _ -> None

// ---------------------------------------------------------------------------
// One app row
// ---------------------------------------------------------------------------

type private Row =
    { ClientId: string
      DisplayName: string
      ClientType: string
      RedirectUris: string list
      PostLogoutRedirectUris: string list
      Choice: AppChoice
      Branding: AppBranding option
      /// The names of the enabled connectors this app offers that have no
      /// domain rule, when it also offers some that do (FR-020). Empty when
      /// there is no such mix.
      Unrestricted: string list }

/// FR-020: an app that offers a connector with a domain rule and one without is
/// limited only as far as its people choose the limited one, so the open
/// connectors are named. `instance` is the instance list, read once by the
/// caller. An open passkey connector lets anyone in like a connector with no
/// domain rule; an invitations-only one admits only invited people, like a rule
/// (spec 008 FR-010). Only what `offeredTo` offers counts: a connector that is off, or not
/// offered to this app, lets nobody in here.
let private unrestrictedFor (instance: string list) (connectors: Connector list) (choice: AppChoice) : string list =
    let offered = offeredTo choice connectors

    let hasRule (connector: Connector) =
        if connector.Kind = Passkey then
            connector.PasskeyEnrolment = InvitationsOnly
        else
        match EmailDomains.applying connector.AllowedEmailDomains instance with
        | EmailDomains.NoRule -> false
        | EmailDomains.Rule _ -> true

    let open', limited = offered |> List.partition (hasRule >> not)

    if open'.IsEmpty || limited.IsEmpty then [] else open' |> List.map (fun c -> c.DisplayName)

// ---------------------------------------------------------------------------
// The connector choice (FR-005, FR-009)
// ---------------------------------------------------------------------------

/// The choice as the form names it: `all-shared` or `chosen`.
let private choiceValue (choice: AppChoice) =
    match choice with
    | AllShared -> "all-shared"
    | Only _ -> "chosen"

/// The choice a posted form or a warning redirect describes. Absent, blank or
/// unrecognised means `all-shared`; `connectors` is read only for `chosen`
/// (contracts/admin-and-config.md). Zero values with `chosen` is a chosen set
/// with nothing in it.
let private choiceFrom (kind: string) (slugs: string seq) : AppChoice =
    match kind with
    | "chosen" ->
        slugs
        |> Seq.map (fun slug -> slug.Trim())
        |> Seq.filter (fun slug -> slug <> "")
        |> Set.ofSeq
        |> Only
    | _ -> AllShared

let private choiceFromForm (form: IFormCollection) : AppChoice =
    let slugs =
        match form.TryGetValue "connectors" with
        | true, values -> values |> Seq.choose Option.ofObj |> List.ofSeq
        | _ -> []

    choiceFrom (WebForms.value form "connectorChoice") slugs

/// The connector choice as two radio cards, like the app type, with a tick box
/// per connector under the second. Every stored connector is listed, shared or
/// not, enabled or not: a chosen set may name a connector that is off today.
/// `idPrefix` keeps the ids unique should two forms ever share a page.
let private connectorChoices
    (t: Texts.Texts)
    (idPrefix: string)
    (showLegend: bool)
    (connectors: Connector list)
    (selected: AppChoice)
    =
    let members =
        match selected with
        | Only slugs -> slugs
        | AllShared -> Set.empty

    fieldset(class' = "um-choice-group").attr("aria-describedby", $"{idPrefix}-hint") {
        // In a pane the section title already says it; the legend stays for
        // screen readers.
        legend(class' = (if showLegend then "um-label" else "sr-only")) { t.AppConnectorsLabel }

        div(class' = "grid gap-3 md:grid-cols-2") {
            for value, title, description in
                [ "all-shared", t.AppConnectorChoiceAllSharedTitle, t.AppConnectorChoiceAllSharedDescription
                  "chosen", t.AppConnectorChoiceChosenTitle, t.AppConnectorChoiceChosenDescription ] do
                label(class' = "um-choice") {
                    let radio =
                        input(type' = "radio", name = "connectorChoice", value = value, class' = "um-choice-radio")

                    if value = choiceValue selected then
                        radio.attr("checked", "checked") |> ignore

                    radio

                    span(class' = "um-choice-body") {
                        span(class' = "um-choice-title") { title }
                        span(class' = "um-choice-desc") { description }
                    }
                }
        }

        if connectors.IsEmpty then
            p(class' = "um-hint") { t.AppConnectorChoiceNoneToChoose }
        else
            div(class' = "mt-4 space-y-2").data("connector-options", "") {
                for connector in connectors do
                    label(class' = "um-check-row") {
                        let box =
                            input(type' = "checkbox", name = "connectors", value = connector.Slug, class' = "um-check")

                        if Set.contains connector.Slug members then
                            box.attr("checked", "checked") |> ignore

                        box

                        span() { connector.DisplayName }
                        // The identifier, quieter: often it repeats the name.
                        span(class' = "um-mono text-label-md text-on-surface-variant") { connector.Slug }
                    }
            }

        p(id = $"{idPrefix}-hint", class' = "um-hint") { t.AppConnectorChoiceHint }
    }

// ---------------------------------------------------------------------------
// Branding (spec 006)
// ---------------------------------------------------------------------------

/// The accent as the form field holds it: `#hex`, or empty when there is none.
let private accentText (branding: AppBranding option) =
    match branding |> Option.bind (fun b -> b.Accent) with
    | Some accent -> "#" + Accent.hex accent
    | None -> ""

/// The notices of the branding forms are about the app whose pane they land in,
/// so they are drawn there, under its heading, like every notice about one app.
let private isBrandingNotice (code: string) =
    code.StartsWith "app-logo-" || code.StartsWith "app-accent-"

/// How a branding notice is drawn: a change that was refused is an error, a
/// change that went through but deserves a word is a warning, and the rest is
/// good news.
let private brandingAlertKind (code: string) =
    match code with
    | "app-accent-low-contrast" -> Components.Warning
    | "app-logo-saved"
    | "app-logo-removed"
    | "app-accent-saved"
    | "app-accent-cleared" -> Components.Success
    | _ -> Components.Failure

/// How any other notice on this screen is drawn: a create or a change that was
/// refused is a warning, so it does not read as good news above the form it is
/// about; the rest went through.
let private noticeKind (code: string) =
    match code with
    | "app-name-required"
    | "app-redirect-uri-invalid"
    | "app-type-invalid"
    | "app-connector-unknown"
    | "app-not-found"
    | "app-no-secret" -> Components.Warning
    | _ -> Components.Success

/// What a branding notice says. A refusal that measured something says it, from
/// the single-use cookie the refusal wrote for this app; a link cannot make the
/// screen name a size, and with no cookie the notice's fixed text is shown.
let private brandingNoticeText (ctx: HttpContext) (t: Texts.Texts) (clientId: string) (code: string) =
    let values () =
        WebForms.NoticeReason.consume ctx code "app" clientId
        |> Option.map WebForms.NoticeReason.parsePairs
        |> Option.defaultValue Map.empty

    let number (values: Map<string, string>) (name: string) =
        values |> Map.tryFind name |> Option.bind (fun v -> match Int64.TryParse v with | true, n -> Some n | _ -> None)

    let sized =
        match code with
        | "app-logo-too-small"
        | "app-logo-too-big" ->
            let v = values ()

            match number v "width", number v "height" with
            | Some width, Some height ->
                Some((if code = "app-logo-too-small" then t.AppLogoTooSmall else t.AppLogoTooBig) (int width) (int height))
            | _ -> None
        | "app-logo-too-large" -> number (values ()) "bytes" |> Option.map t.AppLogoTooLarge
        | _ -> None

    sized |> Option.defaultValue (t.Notice code)

/// The form that uploads a logo. The file input is `accept`-filtered to the
/// three formats the script can convert, and its form is multipart. Everything
/// the script needs to know arrives as `data-*`: its limits (which are the
/// server's numbers) and its words, so it holds no text of its own.
///
/// Without the script, the file goes as it is and the server accepts a PNG that
/// already meets its limits (research R1). A `noscript` hint says so, and only
/// people without the script see it. The crop container is empty and so is the
/// error line; the script fills the one and shows the other.
///
/// An app with a logo gets its Remove button in the button row. The button
/// belongs to the delete form, which sits outside this one, by its `form`
/// attribute.
let private logoForm (ctx: HttpContext) (t: Texts.Texts) (row: Row) (hasLogo: bool) =
    let fieldId = $"logo-{row.ClientId}"
    let hintId = $"{fieldId}-hint"

    form(method = "post", action = $"/admin/apps/{row.ClientId}/branding/logo", class' = "space-y-3")
        .attr("enctype", "multipart/form-data")
        .data("logo-form", "")
        .data("max-file-bytes", string PngLogo.maxSourceBytes)
        .data("max-source-side", string PngLogo.maxSourceSide)
        .data("store-side", string PngLogo.maxSide)
        .data("min-side", string PngLogo.minSide)
        .data("error-format", t.LogoErrorFormat)
        .data("error-file-size", t.LogoErrorFileSize)
        .data("error-dimensions", t.LogoErrorDimensions)
        .data("error-not-image", t.LogoErrorNotImage)
        .data("label-crop", t.LogoCropLabel)
        .data("label-handle", t.LogoCropHandleLabel) {
        ctx.GetAntiforgeryInput()

        div() {
            label(for' = fieldId, class' = "um-label") { t.LogoLabel }

            input(type' = "file", id = fieldId, name = "logo", class' = "um-file block w-full text-body-md text-on-surface-variant")
                .attr("accept", "image/png,image/jpeg,image/webp")
                .attr("aria-describedby", hintId)
                .data("logo-input", "")

            // The `noscript` sits inside the hint, so the input's description
            // names one id that always exists and gains the second sentence
            // only where it is rendered.
            p(id = hintId, class' = "um-hint") {
                t.LogoHint
                noscript() { " " + t.LogoHintNoScript }
            }
        }

        div(class' = "space-y-2").data("logo-crop", "") {}

        p(class' = "um-field-error").attr("role", "alert").attr("hidden", "hidden").data("logo-error", "") {}

        div(class' = "flex flex-wrap items-center gap-2") {
            button(type' = "submit", class' = Components.btnSm Components.Secondary) { t.LogoSave }

            button(type' = "button", class' = Components.btnSm Components.Ghost)
                .attr("hidden", "hidden")
                .data("logo-whole", "") {
                t.LogoUseWhole
            }

            if hasLogo then
                (button(type' = "submit", class' = Components.btnSm Components.Ghost + " text-error hover:text-error")
                    .attr("form", $"logo-delete-{row.ClientId}")
                    .data("logo-remove", "") {
                    Components.icon Theme.DeleteForever
                    t.AppLogoRemoveButton
                })
                :> HtmlElement
            else
                Fragment() :> HtmlElement
        }
    }

/// The branding section of an app's pane: the logo and its upload form, a preview
/// of the sign-in button in the saved colour, and the accent form.
///
/// The preview is a panel that composes the logo above the sample button, as
/// the sign-in page does.
///
/// The preview sits inside `data-accent` when there is an accent, which is what
/// lets the accent sheet (linked in the pane, the app's own)
/// recolour it without touching the rest of the page (FR-005, FR-019). Without
/// an accent it is Umbral's own colour, which is what that app's page uses.
///
/// `hidden` on the native colour picker is removed by the script when it runs:
/// a picker that does nothing is worse than none. It has no `name`, because a
/// colour input cannot be empty and so could never clear the field.
let private brandingSection (ctx: HttpContext) (t: Texts.Texts) (row: Row) =
    let fieldId = $"accent-{row.ClientId}"
    let hintId = $"{fieldId}-hint"
    let accent = row.Branding |> Option.bind (fun b -> b.Accent)
    let logo = row.Branding |> Option.bind (fun b -> b.Logo)

    let sample =
        button(type' = "button", class' = Components.btn Components.Primary + " w-full justify-center")
            .attr("disabled", "disabled")
            .data("branding-sample", "") {
            Components.icon Theme.Link
            t.SignInWith t.BrandingSampleProvider
        }

    let picker =
        input(type' = "color", class' = "h-11 w-11 cursor-pointer rounded border border-outline-variant bg-surface-container p-1")
            .attr("hidden", "hidden")
            .attr("aria-label", t.AccentLabel)
            .data("accent-picker", "")

    div(class' = "um-pane-section").data("branding", row.ClientId) {
        h3(class' = "um-section-title") { t.BrandingHeading }

        div(class' = "space-y-5") {
            div() {
                p(class' = "um-hint mt-0 mb-1.5") { t.BrandingSampleLabel }

                div(class' = "w-full max-w-sm space-y-4 rounded-lg bg-surface-container p-4") {
                    // The logo at the size people see it on the sign-in page.
                    div(class' = "flex min-h-11 items-center justify-center").data("branding-logo", "") {
                        match logo with
                        | Some logo ->
                            (img(
                                src = $"/branding/logo/{logo.Sha256}.png",
                                alt = row.DisplayName,
                                width = logo.Width,
                                height = logo.Height,
                                class' = Components.signInLogoClass
                            ))
                            :> HtmlElement
                        | None -> (p(class' = "text-body-md text-on-surface-variant") { t.NoLogo }) :> HtmlElement
                    }

                    match accent with
                    | Some accent -> (div().data("accent", Accent.hex accent) { sample }) :> HtmlElement
                    | None -> (div() { sample }) :> HtmlElement
                }
            }

            logoForm ctx t row logo.IsSome

            // Only an app with a logo has one to remove. Its button sits in the
            // upload form's button row, tied to this form by the `form` attribute
            // (a form cannot nest in another). The submit event still fires here,
            // so the script's data-confirm listener asks first, like every
            // destructive form on this screen. `hidden` keeps the empty form out of
            // the section's spacing. Removing a logo can be undone by uploading it
            // again, so its button is a ghost in the error colour; the filled
            // danger button stays for Delete app.
            match logo with
            | Some _ ->
                (form(id = $"logo-delete-{row.ClientId}", method = "post", action = $"/admin/apps/{row.ClientId}/branding/logo/delete")
                    .attr("hidden", "hidden") {
                    ctx.GetAntiforgeryInput()
                })
                |> Components.confirmSubmit (t.AppLogoRemoveConfirm row.DisplayName)
                :> HtmlElement
            | None -> Fragment() :> HtmlElement

            form(method = "post", action = $"/admin/apps/{row.ClientId}/branding/accent", class' = "space-y-3")
                .data("accent-form", "") {
                ctx.GetAntiforgeryInput()

                div() {
                    label(for' = fieldId, class' = "um-label") { t.AccentLabel }

                    div(class' = "flex items-center gap-2") {
                        input(type' = "text", id = fieldId, name = "accent", value = accentText row.Branding, class' = "um-input um-mono max-w-xs")
                            .attr("aria-describedby", hintId)
                            .attr("autocomplete", "off")
                            .attr("spellcheck", "false")

                        picker
                    }

                    p(id = hintId, class' = "um-hint") { t.AccentHint }
                }

                button(type' = "submit", class' = Components.btnSm Components.Secondary) { t.AccentSave }
            }
        }
    }

// ---------------------------------------------------------------------------
// Screen
// ---------------------------------------------------------------------------

/// The app type as two radio cards rather than a `<select>`: the operator has
/// to decide between them and a drop-down hides the sentence that says which
/// one is theirs. Real radio inputs in a fieldset keep it a native, keyboard
/// and screen-reader friendly group; the card styling is CSS only (`.um-choice`).
let private appTypeChoices (t: Texts.Texts) (selected: AppType) =
    fieldset(class' = "um-choice-group").attr("aria-describedby", "appType-hint") {
        legend(class' = "um-label") { t.AppTypeLabel }

        div(class' = "grid gap-3 md:grid-cols-2") {
            for appType in allAppTypes do
                label(class' = "um-choice") {
                    let radio =
                        input(type' = "radio", name = "appType", value = appType.Value, class' = "um-choice-radio")

                    // `checked` is reserved in F#, so the boolean attribute is set by name.
                    if appType = selected then
                        radio.attr("checked", "checked") |> ignore

                    radio

                    span(class' = "um-choice-body") {
                        span(class' = "um-choice-title") { appType.Label t }
                        span(class' = "um-choice-desc") { appType.Description t }
                    }
                }
        }

        p(id = "appType-hint", class' = "um-hint") { t.AppTypeHint }
    }

/// The create form, the pane of `/admin/apps/new` and of an Apps screen with no
/// app yet. `cancel` is what the Cancel button is.
let private createForm (ctx: HttpContext) (t: Texts.Texts) (connectors: Connector list) (formClass: string) (cancel: HtmlElement) =
    form(method = "post", action = "/admin/apps", class' = formClass) {
        ctx.GetAntiforgeryInput()

        Components.field t.AppNameLabel "name" "" None None
        Components.field t.AppRedirectUriLabel "redirectUri" "" (Some t.AppRedirectUriHint) None
        Components.field t.AppPostLogoutRedirectUriLabel "postLogoutRedirectUri" "" None None

        appTypeChoices t Web
        connectorChoices t "connectors-new" true connectors AllShared

        div(class' = "flex flex-wrap items-center gap-3") {
            button(type' = "submit", class' = Components.btn Components.Primary) {
                Components.icon Theme.Add
                t.AppCreateButton
            }

            cancel
        }
    }

/// An attempted change of an app's choice that would leave it with nothing
/// offered (FR-010). The redirect carries the choice it was attempting, so the
/// confirming button can post exactly that.
type private StrandWarning =
    { ClientId: string
      Name: string
      Choice: AppChoice }

let private strandWarning (ctx: HttpContext) (t: Texts.Texts) (warning: StrandWarning) =
    let slugs =
        match warning.Choice with
        | Only slugs -> Set.toList slugs
        | AllShared -> []

    div(class' = "space-y-2") {
        p() { t.StrandedAppsTitle }

        ul() {
            li().data("stranded-app", warning.ClientId) { warning.Name }
        }

        form(method = "post", action = $"/admin/apps/{warning.ClientId}/connectors?confirm=yes", class' = "inline") {
            ctx.GetAntiforgeryInput()
            input(type' = "hidden", name = "connectorChoice", value = choiceValue warning.Choice)

            for slug in slugs do
                input(type' = "hidden", name = "connectors", value = slug)

            button(type' = "submit", class' = Components.btnSm Components.Danger) { t.AppConnectorsStrandConfirm }
        }
    }

/// A notice about the app whose pane this is, drawn under its heading.
let private itemNotice (ctx: HttpContext) (t: Texts.Texts) (row: Row) (code: string) : HtmlElement =
    match code with
    // The same text as the pane's warning, from the names the app has now; a
    // link that outlived the mix says the fixed text.
    | "app-connectors-unrestricted" ->
        (div().data("notice", code) {
            Components.alert
                Components.Warning
                (if row.Unrestricted.IsEmpty then
                     t.Notice code
                 else
                     // Saved either way: the warning says so first.
                     let saved = t.Notice "app-connectors-saved"
                     $"{saved} {t.AppsUnrestrictedWarning row.Unrestricted}")
        })
        :> HtmlElement
    // The choice that was attempted travels in the query; the app is this one.
    | "app-connectors-strand" ->
        let slugs =
            (ctx.TryGetQueryValue "connectors" |> Option.defaultValue "").Split(',', StringSplitOptions.RemoveEmptyEntries)

        let warning =
            { ClientId = row.ClientId
              Name = row.DisplayName
              Choice = choiceFrom (ctx.TryGetQueryValue "choice" |> Option.defaultValue "") slugs }

        (div().data("notice", code) {
            Components.alert Components.Warning (t.Notice code)
            strandWarning ctx t warning
        })
        :> HtmlElement
    | code when isBrandingNotice code ->
        (div().data("notice", code) {
            Components.alert (brandingAlertKind code) (brandingNoticeText ctx t row.ClientId code)
        })
        :> HtmlElement
    | code -> (div().data("notice", code) { Components.alert (noticeKind code) (t.Notice code) }) :> HtmlElement

/// The shown-once secret. Not a card: it sits in a pane, and a pane has none.
/// No client ID either: the Connection section right under it shows that.
let private secretReveal (t: Texts.Texts) (revealed: RevealedSecret) =
    div(class' = "space-y-4").data("secret-once", "") {
        Components.alert Components.Warning t.AppSecretOnceWarning

        Components.copyField t.Copy t.Copied t.AppSecretLabel "revealed-secret" revealed.Secret ignore
    }

/// One app's pane (the contract's order): heading, the notices about it, the
/// connection details, the sign-in methods, the branding, and the closing row.
let private appPane
    (ctx: HttpContext)
    (t: Texts.Texts)
    (origin: PublicOrigin.PublicOrigin)
    (connectors: Connector list)
    (view: Components.FrameView)
    (notice: string option)
    (revealed: RevealedSecret option)
    (row: Row)
    =
    let isPublic = row.ClientType = ClientTypes.Public

    let accent = row.Branding |> Option.bind (fun b -> b.Accent)

    (Components.framePane
        view
        "app-heading"
        (listPath, t.NavApps)
        [ // The selected app's accent sheet travels with the pane, so a swap brings
          // it, and no other app's colour is on the page.
          match accent with
          | Some accent ->
              (link(rel = "stylesheet", href = $"/branding/accent/{Accent.hex accent}.css")) :> HtmlElement
          | None -> Fragment() :> HtmlElement

          Components.paneHeading
              "app-heading"
              row.DisplayName
              [ Components.pill Components.MutedTone (if isPublic then SpaOrNative.Label t else Web.Label t) ]

          match notice with
          | Some code -> (div(class' = "mt-6") { itemNotice ctx t row code }) :> HtmlElement
          | None -> Fragment() :> HtmlElement

          match revealed with
          | Some r -> (div(class' = "mt-6") { secretReveal t r }) :> HtmlElement
          | None -> Fragment() :> HtmlElement

          Components.paneSection
              t.AppConnectionTitle
              (div(class' = "space-y-5") {
                  div(class' = "grid gap-3 sm:grid-cols-2") {
                      Components.copyField
                          t.Copy
                          t.Copied
                          t.AppClientIdLabel
                          $"client-id-{row.ClientId}"
                          row.ClientId
                          ignore

                      Components.copyField
                          t.Copy
                          t.Copied
                          t.AppIssuerUrlLabel
                          $"issuer-{row.ClientId}"
                          origin.Text
                          ignore
                  }

                  // A redirect URI has no break points of its own; at 360px it
                  // would push the pane wider than the screen.
                  ul(class' = "space-y-1 text-body-md text-on-surface-variant") {
                      for uri in row.RedirectUris do
                          li(class' = "um-mono [overflow-wrap:anywhere]") { uri }
                  }
              })

          // FR-009: which connectors this app offers, readable at a glance, with
          // a small form to change it.
          Components.paneSection
              t.AppConnectorsLabel
              (div(class' = "space-y-5") {
                  // FR-020: never blocks, only says what the choice lets in.
                  if not row.Unrestricted.IsEmpty then
                      (div().data("warning", "unrestricted-connectors") {
                          Components.alert Components.Warning (t.AppsUnrestrictedWarning row.Unrestricted)
                      })
                      :> HtmlElement

                  match row.Choice with
                  | AllShared -> p(class' = "text-body-md text-on-surface-variant") { t.AppConnectorsAllSharedSummary }
                  | Only slugs when slugs.IsEmpty ->
                      p(class' = "text-body-md text-on-surface-variant") { t.AppConnectorsChosenNone }
                  | Only slugs ->
                      div(class' = "flex flex-wrap items-center gap-2") {
                          span(class' = "text-body-md text-on-surface-variant") { t.AppConnectorsChosenSummary }

                          for slug in slugs do
                              let label =
                                  connectors
                                  |> List.tryFind (fun c -> c.Slug = slug)
                                  |> Option.map (fun c -> c.DisplayName)
                                  |> Option.defaultValue slug

                              (span().data("app-connector", slug) { Components.pill Components.MutedTone label })
                              :> HtmlElement
                      }

                  form(method = "post", action = $"/admin/apps/{row.ClientId}/connectors", class' = "space-y-3") {
                      ctx.GetAntiforgeryInput()
                      connectorChoices t $"connectors-{row.ClientId}" false connectors row.Choice

                      button(type' = "submit", class' = Components.btnSm Components.Secondary) {
                          t.AppConnectorsSaveButton
                      }
                  }
              })

          brandingSection ctx t row

          // The app's state and lifetime actions close the pane, behind their own
          // hairline, so they do not read as part of the accent form.
          (Components.paneActions
              [ if not isPublic then
                    (form(method = "post", action = $"/admin/apps/{row.ClientId}/secret", class' = "inline") {
                        ctx.GetAntiforgeryInput()

                        button(type' = "submit", class' = Components.btnSm Components.Secondary) {
                            Components.icon Theme.Key
                            t.AppRegenerateButton
                        }
                    })
                    |> Components.confirmSubmit (t.AppRegenerateConfirm row.DisplayName)
                    :> HtmlElement

                (form(method = "post", action = $"/admin/apps/{row.ClientId}/delete", class' = "inline") {
                    ctx.GetAntiforgeryInput()

                    button(type' = "submit", class' = Components.btnSm Components.Danger) {
                        Components.icon Theme.DeleteForever
                        t.AppDeleteButton
                    }
                })
                |> Components.confirmSubmit (t.AppDeleteConfirm row.DisplayName)
                :> HtmlElement ])
              .data("app-actions", "") :> HtmlElement ])
        .data("app", row.ClientId)
        .data("app-choice", choiceValue row.Choice)

/// What the URL asked for: the list, one app, or the create form.
type private Selection =
    | ListUrl
    | Item of string
    | Create

let private needConnector (t: Texts.Texts) =
    Components.emptyState
        Theme.Warning
        t.AppsNeedConnectorTitle
        t.AppsNeedConnectorBody
        (Some(a (href = "/admin/connectors", class' = Components.btn Components.Primary) { t.NavConnectors }))

let private view
    (ctx: HttpContext)
    (t: Texts.Texts)
    (origin: PublicOrigin.PublicOrigin)
    (rows: Row list)
    (connectors: Connector list)
    (hasEnabledConnector: bool)
    (selection: Selection)
    (selected: Row option)
    (revealed: RevealedSecret option)
    (notice: string option)
    =
    // The create form is the pane for `/new`, and for an Apps screen with no app
    // to show; with no enabled connector there is nothing to create with.
    let showCreate =
        hasEnabledConnector && (selection = Create || (selection = ListUrl && rows.IsEmpty))

    let frameView =
        match selection, selected with
        | Create, _ -> Components.DetailView
        | Item _, Some _ -> Components.DetailView
        | _ -> Components.ListView

    // An app's own URL is named after the app; the list URL is the screen's.
    let pageTitle =
        match selection, selected with
        | Create, _ -> t.AppCreateTitle
        | Item _, Some row -> $"{row.DisplayName} · {t.AppsTitle}"
        | _ -> t.AppsTitle

    // On the list URL, and for an unknown app, the notice is about the page; for
    // a known app and for create, it is about what the pane shows.
    let pageNotice =
        match selection, notice with
        | Item _, _ when selected.IsSome -> None
        | Create, _ -> None
        | Item _, _ -> Some "app-not-found"
        | ListUrl, code -> code

    let createButton =
        if hasEnabledConnector then
            let button =
                a(href = "/admin/apps/new", class' = Components.btnSm Components.Primary)

            Components.frameNav button "/admin/apps/new" |> ignore

            if showCreate then
                button.attr("aria-current", "page") |> ignore

            Some(
                (button {
                    Components.icon Theme.Add
                    t.AppCreateButton
                })
                :> HtmlElement
            )
        else
            None

    let entries =
        rows
        |> List.map (fun row ->
            let entry =
                Components.frameEntry
                    (detailPath row.ClientId)
                    (not showCreate && (selected |> Option.exists (fun s -> s.ClientId = row.ClientId)))
                    row.DisplayName
                    (span() { if row.ClientType = ClientTypes.Public then SpaOrNative.Label t else Web.Label t })
                    (if row.Unrestricted.IsEmpty then
                         None
                     else
                         Some(Components.markerIcon Theme.Warning t.AppsUnrestrictedMarker))

            entry.data ("app-entry", row.ClientId) :> HtmlElement)

    let list =
        Components.frameList
            frameView
            t.AppsListLabel
            (t.AppsCount rows.Length)
            createButton
            (if rows.IsEmpty then
                 Components.emptyState Theme.ExploreOff t.AppsEmptyTitle t.AppsEmptyBody None
             else
                 Components.frameEntries entries)

    let pane =
        if showCreate then
            Components.framePane
                frameView
                "app-heading"
                (listPath, t.NavApps)
                [ Components.paneHeading "app-heading" t.AppCreateTitle []

                  (match notice with
                   | Some code ->
                       (div(class' = "mt-6").data("notice", code) { Components.alert (noticeKind code) (t.Notice code) })
                       :> HtmlElement
                   | None -> Fragment() :> HtmlElement)

                  (let cancel = a(href = listPath, class' = Components.btn Components.Secondary)
                   Components.frameNav cancel listPath |> ignore

                   div(class' = "mt-6") {
                       createForm ctx t connectors "space-y-4" (cancel { t.AppCreateCancel })
                   })
                  :> HtmlElement ]
            :> HtmlElement
        else
            match selected with
            | Some row when selection <> ListUrl || not rows.IsEmpty ->
                appPane ctx t origin connectors frameView (match selection with Item _ -> notice | _ -> None) revealed row
                :> HtmlElement
            | _ -> Fragment() :> HtmlElement

    div(class' = "space-y-6").data("screen", (if selection = Create then "app-new" else "apps")) {
        Components.pageHeading t.AppsTitle (Some t.AppsLead)

        match pageNotice with
        | Some code ->
            (div().data("notice", code) { Components.alert (noticeKind code) (t.Notice code) }) :> HtmlElement
        | None -> Fragment() :> HtmlElement

        if not hasEnabledConnector then needConnector t

        // With nothing to create with, `/new` is only the advice above; the list
        // URL keeps the frame while there are apps to look after.
        if hasEnabledConnector || (selection <> Create && not rows.IsEmpty) then
            Components.frame frameView list pane
    }
    // The tab, history and a screen reader's page announcement name what the
    // URL shows; htmx takes the title from the swapped page, so it follows the
    // selection.
    |> Layout.operatorPage ctx t pageTitle []

/// F#'s `task { for x in ... }` binds `seq<'T>`, not `IAsyncEnumerable<'T>`, so
/// OpenIddict's manager enumerables (backed by EF Core queries) are drained by
/// hand instead.
let private toListAsync (source: Collections.Generic.IAsyncEnumerable<'a>) =
    task {
        let enumerator = source.GetAsyncEnumerator()
        let results = ResizeArray<'a>()
        let mutable go = true

        while go do
            let! hasNext = enumerator.MoveNextAsync()
            if hasNext then results.Add enumerator.Current else go <- false

        do! enumerator.DisposeAsync()
        return List.ofSeq results
    }

let private toRow
    (applications: IOpenIddictApplicationManager)
    (choices: Map<string, AppChoice>)
    (branding: Map<string, AppBranding>)
    (app: obj)
    =
    task {
        let! clientId = applications.GetClientIdAsync app
        let! displayName = applications.GetDisplayNameAsync app
        let! clientType = applications.GetClientTypeAsync app
        let! redirectUris = applications.GetRedirectUrisAsync app
        let! postLogoutUris = applications.GetPostLogoutRedirectUrisAsync app

        let clientId = clientId |> Option.ofObj |> Option.defaultValue ""

        return
            { ClientId = clientId
              Choice = choices |> Map.tryFind clientId |> Option.defaultValue AllShared
              Branding = branding |> Map.tryFind clientId
              DisplayName = displayName |> Option.ofObj |> Option.defaultValue ""
              ClientType = clientType |> Option.ofObj |> Option.defaultValue ClientTypes.Confidential
              RedirectUris = redirectUris |> List.ofSeq
              PostLogoutRedirectUris = postLogoutUris |> List.ofSeq
              Unrestricted = [] }
    }

/// Every slug in a chosen set names a stored connector. `setChoice` would hit
/// a foreign key on an unknown one, so it is refused with a notice first.
let private knownSlugs (choice: AppChoice) (connectors: Connector list) =
    match choice with
    | AllShared -> true
    | Only slugs -> slugs |> Set.forall (fun slug -> connectors |> List.exists (fun c -> c.Slug = slug))

/// One body for the three GETs: the list, one app, and the create form. Every one
/// renders the whole page; htmx keeps only the frame.
let private screen (selection: Selection) : EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let t = Texts.current ()
            let config = ctx.GetService<Database.DbConfig>()
            let origin = ctx.GetService<PublicOrigin.PublicOrigin>()
            let applications = ctx.GetService<IOpenIddictApplicationManager>()

            let! apps = toListAsync (applications.ListAsync())
            let! choices = AppConnectors.allChoices config
            // One read for every app, and never the logo bytes.
            let! branding = BrandingStore.allForApps config
            let rows = ResizeArray()

            for app in apps do
                let! row = toRow applications choices branding app
                rows.Add row

            let! connectors = ConnectorStore.listAll config

            // Read once for the page, not once per connector or per app.
            let! instanceList = EmailDomainRules.instanceList config

            let rows =
                rows
                |> Seq.map (fun row -> { row with Unrestricted = unrestrictedFor instanceList connectors row.Choice })
                |> List.ofSeq
                |> List.sortBy (fun row -> row.DisplayName.ToLowerInvariant(), row.ClientId)

            // An app made now starts on "all shared connectors" (FR-005), so
            // the create form is useful only when that offers something.
            let hasEnabledConnector = not (List.isEmpty (offeredTo AllShared connectors))

            let notice = ctx.TryGetQueryValue "msg" |> Option.bind Texts.knownNotice

            // The first app is the one a list URL shows on a wide screen.
            let selected =
                match selection with
                | ListUrl -> List.tryHead rows
                | Item clientId -> rows |> List.tryFind (fun row -> row.ClientId = clientId)
                | Create -> None

            // An app that is not here is a 404 with the list around it.
            match selection, selected with
            | Item _, None -> ctx.SetStatusCode 404
            | _ -> ()

            // Only the app being shown takes its secret off the cookie.
            let revealed =
                match selected with
                | Some row when selection <> Create -> SecretReveal.consumeFor ctx row.ClientId
                | _ -> None

            return! ctx.WriteHtmlView(view ctx t origin rows connectors hasEnabledConnector selection selected revealed notice)
        })

let page: EndpointHandler = screen ListUrl

let newPage: EndpointHandler = screen Create

let detailPage (clientId: string) : EndpointHandler = screen (Item clientId)

let create: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! form = WebForms.readValidated ctx

            let name = WebForms.value form "name"
            let redirectUriRaw = WebForms.value form "redirectUri"
            let postLogoutRaw = WebForms.optional form "postLogoutRedirectUri"
            let appTypeRaw = WebForms.value form "appType"

            let remoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string

            let fail notice =
                WebForms.redirectWithNotice ctx "/admin/apps/new" notice

            if name = "" then
                return! fail "app-name-required"
            else

            match parseStrictUri redirectUriRaw with
            | None -> return! fail "app-redirect-uri-invalid"
            | Some redirectUri ->

            let postLogoutResult =
                match postLogoutRaw with
                | None -> Ok None
                | Some raw ->
                    match parseStrictUri raw with
                    | Some uri -> Ok(Some uri)
                    | None -> Error()

            match postLogoutResult with
            | Error() -> return! fail "app-redirect-uri-invalid"
            | Ok postLogoutUri ->

            match AppType.Parse appTypeRaw with
            | None -> return! fail "app-type-invalid"
            | Some appType ->

            // The connector choice is validated before the app exists, so a
            // refused slug leaves nothing half made (FR-005).
            let choice = choiceFromForm form
            let! connectors = ConnectorStore.listAll config

            if not (knownSlugs choice connectors) then
                return! fail "app-connector-unknown"
            else

            let clientId = Umbral.Domains.Ids.newClientId ()

            let secret =
                match appType with
                | Web -> Some(Umbral.Domains.Ids.newClientSecret ())
                | SpaOrNative -> None

            let descriptor = buildDescriptor clientId name appType redirectUri postLogoutUri secret
            let applications = ctx.GetService<IOpenIddictApplicationManager>()
            let! _ = applications.CreateAsync descriptor

            // No row means "all shared connectors", so the default writes none.
            match choice with
            | Only _ -> do! AppConnectors.setChoice config clientId choice
            | AllShared -> ()

            do!
                SecurityEvents.record
                    config
                    clock
                    { SecurityEvents.event SecurityEvents.AppCreated SecurityEvents.Success SecurityEvents.Actor.Operator with
                        ClientId = Some clientId
                        RemoteIp = remoteIp }

            match secret with
            | Some s -> SecretReveal.stash ctx clientId s
            | None -> ()

            return! WebForms.redirectWithNotice ctx (detailPath clientId) "app-created"
        })

/// Change an existing app's connector choice (FR-005, FR-009). Warns, naming
/// the app, before a choice that leaves it with nothing to offer (FR-010), and
/// goes ahead once the button on that warning posts `?confirm=yes`.
let setConnectors (clientId: string) : EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! form = WebForms.readValidated ctx

            let applications = ctx.GetService<IOpenIddictApplicationManager>()
            let! application = applications.FindByClientIdAsync clientId

            match application with
            | null -> return! WebForms.redirectWithNotice ctx listPath "app-not-found"
            | application ->

            let choice = choiceFromForm form
            let! connectors = ConnectorStore.listAll config

            if not (knownSlugs choice connectors) then
                return! WebForms.redirectWithNotice ctx (detailPath clientId) "app-connector-unknown"
            else

            let confirmed = ctx.TryGetQueryValue "confirm" = Some "yes"
            let! name = applications.GetDisplayNameAsync application
            let named = Option.ofObj name |> Option.defaultValue clientId

            if not confirmed && not (List.isEmpty (strandedApps [ clientId, named, choice ] connectors)) then
                let slugs =
                    match choice with
                    | Only slugs -> Set.toList slugs
                    | AllShared -> []

                return!
                    WebForms.redirectWithNoticeAbout
                        ctx
                        (detailPath clientId)
                        "app-connectors-strand"
                        [ "choice", choiceValue choice
                          "connectors", String.Join(",", slugs) ]
            else

            do! AppConnectors.setChoice config clientId choice

            do!
                SecurityEvents.record
                    config
                    clock
                    { SecurityEvents.event SecurityEvents.AppUpdated SecurityEvents.Success SecurityEvents.Actor.Operator with
                        ClientId = Some clientId
                        RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string
                        Detail = Some "connector_choice" }

            // FR-020: saved either way; a choice that mixes limited and open
            // connectors says so, as a warning, instead of the plain notice.
            let! instanceList = EmailDomainRules.instanceList config

            if List.isEmpty (unrestrictedFor instanceList connectors choice) then
                return! WebForms.redirectWithNotice ctx (detailPath clientId) "app-connectors-saved"
            else
                return! WebForms.redirectWithNotice ctx (detailPath clientId) "app-connectors-unrestricted"
        })

let endpoint =
    [ GET [ route "/admin/apps" page ]
      GET [ route "/admin/apps/new" newPage ]
      GET [ routef "/admin/apps/{%s}" detailPage ]
      POST [ route "/admin/apps" create ]
      POST [ routef "/admin/apps/{%s}/connectors" setConnectors ] ]
