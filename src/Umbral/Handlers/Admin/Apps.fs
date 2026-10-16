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
// having the GET consume (remove) it on the very read that shows it — a
// refresh of the same URL then shows nothing, which is what "not on any later
// page load" means in practice.
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

    let consume (ctx: HttpContext) : RevealedSecret option =
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

        // Deleted before it is decoded, so a refresh shows nothing whether or
        // not the decode succeeds.
        let options = CookieOptions()
        options.Path <- "/admin"
        options.SameSite <- SameSiteMode.Strict
        options.Secure <- ctx.Request.IsHttps
        ctx.Response.Cookies.Delete(CookieName, options)

        try
            match ((protector ctx).Unprotect payload).Split '\u001f' with
            | [| clientId; secret |] -> Some { ClientId = clientId; Secret = secret }
            | _ -> None
        with _ ->
            None

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
      Branding: AppBranding option }

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
/// `idPrefix` keeps the ids unique when the form appears once per app.
let private connectorChoices
    (t: Texts.Texts)
    (idPrefix: string)
    (connectors: Connector list)
    (selected: AppChoice)
    =
    let members =
        match selected with
        | Only slugs -> slugs
        | AllShared -> Set.empty

    fieldset(class' = "um-choice-group").attr("aria-describedby", $"{idPrefix}-hint") {
        legend(class' = "um-label") { t.AppConnectorsLabel }

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
            div(class' = "space-y-1").data("connector-options", "") {
                for connector in connectors do
                    label(class' = "flex items-center gap-2 text-body-sm text-on-surface") {
                        let box =
                            input(type' = "checkbox", name = "connectors", value = connector.Slug)

                        if Set.contains connector.Slug members then
                            box.attr("checked", "checked") |> ignore

                        box

                        span() { $"{connector.DisplayName} " }
                        span(class' = "um-mono") { connector.Slug }
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

/// The notices of the branding forms are shown inside the card they are about,
/// not at the top of the page: the redirect lands on `#app-{clientId}`, and a
/// message above the fold would be somewhere the operator is no longer looking.
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
/// already meets its limits (research R1). The hint says so. The crop container
/// is empty and so is the error line; the script fills the one and shows the
/// other.
let private logoForm (ctx: HttpContext) (t: Texts.Texts) (row: Row) =
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

            input(type' = "file", id = fieldId, name = "logo", class' = "block w-full text-body-md text-on-surface-variant")
                .attr("accept", "image/png,image/jpeg,image/webp")
                .attr("aria-describedby", hintId)
                .data("logo-input", "")

            p(id = hintId, class' = "um-hint") { t.LogoHint }
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
        }
    }

/// The branding section of an app card: the logo and its upload form, a preview
/// of the sign-in button in the saved colour, and the accent form.
///
/// The preview sits inside `data-accent` when there is an accent, which is what
/// lets the accent sheet (linked in the page's head, one per distinct colour)
/// recolour it without touching the rest of the page (FR-005, FR-019). Without
/// an accent it is Umbral's own colour, which is what that app's page uses.
///
/// `hidden` on the native colour picker is removed by the script when it runs:
/// a picker that does nothing is worse than none. It has no `name`, because a
/// colour input cannot be empty and so could never clear the field.
let private brandingSection (ctx: HttpContext) (t: Texts.Texts) (notice: (string * string) option) (row: Row) =
    let fieldId = $"accent-{row.ClientId}"
    let hintId = $"{fieldId}-hint"
    let accent = row.Branding |> Option.bind (fun b -> b.Accent)
    let logo = row.Branding |> Option.bind (fun b -> b.Logo)

    let sample =
        button(type' = "button", class' = Components.btn Components.Primary)
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

    div(class' = "space-y-3 border-t border-outline-variant/40 pt-4").data("branding", row.ClientId) {
        h3(class' = "um-label") { t.BrandingHeading }

        match notice with
        | Some(code, text) ->
            (div().data("notice", code) { Components.alert (brandingAlertKind code) text }) :> HtmlElement
        | None -> Fragment() :> HtmlElement

        div(class' = "flex flex-wrap items-center gap-4") {
            // The logo at the size people see it on the sign-in page.
            div(class' = "flex min-h-11 items-center").data("branding-logo", "") {
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
                | None -> (p(class' = "text-body-sm text-on-surface-variant") { t.NoLogo }) :> HtmlElement
            }

            div(class' = "space-y-1.5") {
                p(class' = "um-hint mt-0") { t.BrandingSampleLabel }

                match accent with
                | Some accent -> (div().data("accent", Accent.hex accent) { sample }) :> HtmlElement
                | None -> (div() { sample }) :> HtmlElement
            }
        }

        logoForm ctx t row

        // Only an app with a logo has one to remove. It is a form of its own
        // (a form cannot sit inside the upload form), and asks first like
        // every destructive form on this screen.
        match logo with
        | Some _ ->
            (form(method = "post", action = $"/admin/apps/{row.ClientId}/branding/logo/delete", class' = "inline") {
                ctx.GetAntiforgeryInput()

                button(type' = "submit", class' = Components.btnSm Components.Danger) {
                    Components.icon Theme.DeleteForever
                    t.AppLogoRemoveButton
                }
            })
            |> Components.confirmSubmit (t.AppLogoRemoveConfirm row.DisplayName)
            :> HtmlElement
        | None -> Fragment() :> HtmlElement

        form(method = "post", action = $"/admin/apps/{row.ClientId}/branding/accent", class' = "space-y-2")
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

let private appRow
    (ctx: HttpContext)
    (t: Texts.Texts)
    (origin: PublicOrigin.PublicOrigin)
    (connectors: Connector list)
    (cardNotice: (string * string * string) option)
    (row: Row)
    =
    let isPublic = row.ClientType = ClientTypes.Public

    // The notice, if it is about this card.
    let noticeHere =
        match cardNotice with
        | Some(clientId, code, text) when clientId = row.ClientId -> Some(code, text)
        | _ -> None

    // `id` is where a redirect from the branding forms lands, so the operator
    // returns to the card they were on.
    article(class' = "um-card space-y-4", id = $"app-{row.ClientId}")
        .data("app", row.ClientId)
        .data("app-choice", choiceValue row.Choice) {
        div(class' = "flex items-center justify-between gap-3") {
            h2(class' = "text-headline-md text-on-surface") { row.DisplayName }
            Components.pill Components.MutedTone (if isPublic then SpaOrNative.Label t else Web.Label t)
        }

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

        div(class' = "space-y-1 text-body-sm text-on-surface-variant") {
            for uri in row.RedirectUris do
                p(class' = "um-mono") { uri }
        }

        // FR-009: which connectors this app offers, readable at a glance, with
        // a small form to change it.
        div(class' = "space-y-2") {
            match row.Choice with
            | AllShared -> p(class' = "text-body-sm text-on-surface-variant") { t.AppConnectorsAllSharedSummary }
            | Only slugs when slugs.IsEmpty ->
                p(class' = "text-body-sm text-on-surface-variant") { t.AppConnectorsChosenNone }
            | Only slugs ->
                div(class' = "flex flex-wrap items-center gap-2") {
                    span(class' = "text-body-sm text-on-surface-variant") { t.AppConnectorsChosenSummary }

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
                connectorChoices t $"connectors-{row.ClientId}" connectors row.Choice

                button(type' = "submit", class' = Components.btnSm Components.Secondary) {
                    t.AppConnectorsSaveButton
                }
            }
        }

        brandingSection ctx t noticeHere row

        div(class' = "flex flex-wrap items-center gap-2") {
            if not isPublic then
                (form(method = "post", action = $"/admin/apps/{row.ClientId}/secret", class' = "inline") {
                    ctx.GetAntiforgeryInput()

                    button(type' = "submit", class' = Components.btnSm Components.Secondary) {
                        Components.icon Theme.Key
                        t.AppRegenerateButton
                    }
                })
                |> Components.confirmSubmit (t.AppRegenerateConfirm row.DisplayName)
                :> HtmlElement
            else
                Fragment() :> HtmlElement

            (form(method = "post", action = $"/admin/apps/{row.ClientId}/delete", class' = "inline") {
                ctx.GetAntiforgeryInput()

                button(type' = "submit", class' = Components.btnSm Components.Danger) {
                    Components.icon Theme.DeleteForever
                    t.AppDeleteButton
                }
            })
            |> Components.confirmSubmit (t.AppDeleteConfirm row.DisplayName)
        }
    }

// ---------------------------------------------------------------------------
// The shown-once secret card
// ---------------------------------------------------------------------------

let private secretRevealCard (t: Texts.Texts) (revealed: RevealedSecret) =
    div(class' = "um-card space-y-4 border-2 border-primary").data("secret-once", "") {
        Components.alert Components.Warning t.AppSecretOnceWarning

        Components.copyField t.Copy t.Copied t.AppClientIdLabel "revealed-client-id" revealed.ClientId ignore

        Components.copyField t.Copy t.Copied t.AppSecretLabel "revealed-secret" revealed.Secret ignore
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

let private createForm (ctx: HttpContext) (t: Texts.Texts) (connectors: Connector list) =
    form(method = "post", action = "/admin/apps", class' = "um-card space-y-4") {
        ctx.GetAntiforgeryInput()

        Components.field t.AppNameLabel "name" "" None None
        Components.field t.AppRedirectUriLabel "redirectUri" "" (Some t.AppRedirectUriHint) None
        Components.field t.AppPostLogoutRedirectUriLabel "postLogoutRedirectUri" "" None None

        appTypeChoices t Web
        connectorChoices t "connectors-new" connectors AllShared

        button(type' = "submit", class' = Components.btn Components.Primary) {
            Components.icon Theme.Add
            t.AppCreateButton
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

let private view
    (ctx: HttpContext)
    (t: Texts.Texts)
    (origin: PublicOrigin.PublicOrigin)
    (rows: Row list)
    (connectors: Connector list)
    (hasEnabledConnector: bool)
    (revealed: RevealedSecret option)
    (notice: string option)
    (cardNotice: (string * string * string) option)
    (strand: StrandWarning option)
    =
    // One sheet per distinct accent in use, however many apps share it: each
    // card's preview sits under `data-accent` and takes its colour from these.
    let accentSheets =
        rows
        |> List.choose (fun row -> row.Branding |> Option.bind (fun b -> b.Accent))
        |> List.map Accent.hex
        |> List.distinct
        |> List.map (fun hex -> $"/branding/accent/{hex}.css")

    div(class' = "space-y-6").data("screen", "apps") {
        Components.pageHeading t.AppsTitle (Some t.AppsLead)

        match notice, strand with
        | Some code, Some warning ->
            (div().data("notice", code) {
                Components.alert Components.Warning (t.Notice code)
                strandWarning ctx t warning
            })
            :> HtmlElement
        | Some code, None ->
            (div().data("notice", code) { Components.alert Components.Success (t.Notice code) }) :> HtmlElement
        | None, _ -> Fragment() :> HtmlElement

        match revealed with
        | Some r -> secretRevealCard t r
        | None -> Fragment() :> HtmlElement

        if not hasEnabledConnector then
            Components.emptyState
                Theme.Warning
                t.AppsNeedConnectorTitle
                t.AppsNeedConnectorBody
                (Some(a (href = "/admin/connectors", class' = Components.btn Components.Primary) { t.NavConnectors }))
        else
            createForm ctx t connectors

        if rows.IsEmpty then
            Components.emptyState Theme.ExploreOff t.AppsEmptyTitle t.AppsEmptyBody None
        else
            for row in rows do
                appRow ctx t origin connectors cardNotice row
    }
    |> Layout.operatorPage ctx t t.AppsTitle accentSheets

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
              PostLogoutRedirectUris = postLogoutUris |> List.ofSeq }
    }

/// Every slug in a chosen set names a stored connector. `setChoice` would hit
/// a foreign key on an unknown one, so it is refused with a notice first.
let private knownSlugs (choice: AppChoice) (connectors: Connector list) =
    match choice with
    | AllShared -> true
    | Only slugs -> slugs |> Set.forall (fun slug -> connectors |> List.exists (fun c -> c.Slug = slug))

let page: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let t = Texts.current ()
            let config = ctx.GetService<Database.DbConfig>()
            let origin = ctx.GetService<PublicOrigin.PublicOrigin>()
            let applications = ctx.GetService<IOpenIddictApplicationManager>()

            let! apps = toListAsync (applications.ListAsync())
            let! choices = AppConnectors.allChoices config
            // One read for every card, and never the logo bytes.
            let! branding = BrandingStore.allForApps config
            let rows = ResizeArray()

            for app in apps do
                let! row = toRow applications choices branding app
                rows.Add row

            let! connectors = ConnectorStore.listAll config

            // An app made now starts on "all shared connectors" (FR-005), so
            // the create form is useful only when that offers something.
            let hasEnabledConnector = not (List.isEmpty (offeredTo AllShared connectors))

            let notice = ctx.TryGetQueryValue "msg" |> Option.bind Texts.knownNotice
            let revealed = SecretReveal.consume ctx

            // A branding notice about an app that is here is drawn in its card;
            // one about nothing here (the app was deleted meanwhile) stays at the top.
            let cardNotice =
                match notice, ctx.TryGetQueryValue "app" with
                | Some code, Some clientId when isBrandingNotice code && rows |> Seq.exists (fun r -> r.ClientId = clientId) ->
                    Some(clientId, code, brandingNoticeText ctx t clientId code)
                | _ -> None

            let notice = if cardNotice.IsSome then None else notice

            // `app-connectors-strand` carries the app and the choice that was
            // attempted; the app's name is looked up here, never in the URL.
            let strand =
                match notice, ctx.TryGetQueryValue "app" with
                | Some "app-connectors-strand", Some clientId ->
                    rows
                    |> Seq.tryFind (fun r -> r.ClientId = clientId)
                    |> Option.map (fun r ->
                        let slugs =
                            (ctx.TryGetQueryValue "connectors" |> Option.defaultValue "").Split(',', StringSplitOptions.RemoveEmptyEntries)

                        { ClientId = clientId
                          Name = r.DisplayName
                          Choice = choiceFrom (ctx.TryGetQueryValue "choice" |> Option.defaultValue "") slugs })
                | _ -> None

            return!
                ctx.WriteHtmlView(
                    view ctx t origin (List.ofSeq rows) connectors hasEnabledConnector revealed notice cardNotice strand
                )
        })

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
                WebForms.redirectWithNotice ctx "/admin/apps" notice

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

            return! WebForms.redirectWithNotice ctx "/admin/apps" "app-created"
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
            | null -> return! WebForms.redirectWithNotice ctx "/admin/apps" "app-not-found"
            | application ->

            let choice = choiceFromForm form
            let! connectors = ConnectorStore.listAll config

            if not (knownSlugs choice connectors) then
                return! WebForms.redirectWithNotice ctx "/admin/apps" "app-connector-unknown"
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
                        "/admin/apps"
                        "app-connectors-strand"
                        [ "app", clientId
                          "choice", choiceValue choice
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

            return! WebForms.redirectWithNotice ctx "/admin/apps" "app-connectors-saved"
        })

let endpoint =
    [ GET [ route "/admin/apps" page ]
      POST [ route "/admin/apps" create ]
      POST [ routef "/admin/apps/{%s}/connectors" setConnectors ] ]
