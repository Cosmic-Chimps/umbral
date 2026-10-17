/// Shared components of the admin UI and the end-user picker. Styles live in
/// Assets/umbral.input.css (the `.um-*` classes) and Assets/tailwind.config.js;
/// these functions only put markup and accessibility attributes in place.
/// Every function takes the text it shows as a parameter — no view, here or
/// above it, holds a literal (Views/Texts.fs is the only home for those).
///
/// Components here carry no `data-*` test hooks: the hook table in
/// contracts/admin-and-config.md (data-screen, data-app, data-connector,
/// data-connector-state, data-user, data-user-state, data-linked-provider,
/// data-secret-once, data-notice, data-origin-mismatch) names screen- and
/// row-specific identity that only the page assembling a component knows.
/// Pages add those to what a component returns with `.data(...)`, the same
/// way the reference project does. `data-callback-url` is the one exception
/// worth calling out: it belongs on whichever element `copyField` renders for
/// the Connectors screen, added the same way by that screen.
module Umbral.Views.Components

open Oxpecker.ViewEngine
open Umbral.Views.Theme

/// An inline SVG, sized and aligned by `.um-icon` and coloured by the text
/// colour around it (`currentColor`). `raw` is safe here: the only input is a
/// closed union whose path data is constants in Theme.fs, never user text.
let icon (i: Icon) =
    raw $"""<svg class="um-icon" viewBox="0 -960 960 960" fill="currentColor" aria-hidden="true" focusable="false"><path d="{i.Path}"/></svg>"""

/// The Gnomon (an inline SVG) next to the wordmark. The wordmark is set in
/// lowercase and the display face by `um-wordmark` (CSS); the text stays
/// "Umbral" for titles and screen readers.
let private brandClass =
    "inline-flex items-center gap-2.5 rounded text-headline-md text-on-surface"

/// The wordmark. It links home only for the operator (`/admin/apps`); everyone
/// else gets it as plain text, since they have no business on `/admin`.
let brand (linked: bool) (siteName: string) : HtmlElement =
    if linked then
        a(href = "/admin/apps", class' = brandClass) {
            span(class' = "block h-7 w-7") { raw Mark.small }
            span(class' = "um-wordmark") { siteName }
        }
    else
        span(class' = brandClass) {
            span(class' = "block h-7 w-7") { raw Mark.small }
            span(class' = "um-wordmark") { siteName }
        }

type ButtonKind =
    | Primary
    | Secondary
    | Ghost
    | Danger

/// Classes for a <button> or a link styled as one
let btn (kind: ButtonKind) =
    match kind with
    | Primary -> "um-btn um-btn-primary"
    | Secondary -> "um-btn um-btn-secondary"
    | Ghost -> "um-btn um-btn-ghost"
    | Danger -> "um-btn um-btn-danger"

let btnSm kind = btn kind + " um-btn-sm"

/// An app's logo at the size the sign-in page shows it: at most 96 px tall and
/// at most the card's width, whichever is smaller, keeping its proportions. The
/// Apps screen uses the same classes for its preview, so what the operator sees
/// there is the size people will see. `w-auto` and `h-auto` let the `width` and
/// `height` attributes (which reserve the space, so nothing shifts as it loads)
/// give the proportions while the maximums decide the size.
let signInLogoClass = "mx-auto block h-auto w-auto max-h-24 max-w-full"

let card () = div(class' = "um-card")

/// Page heading with an optional lead paragraph
let pageHeading (title: string) (lead: string option) =
    div(class' = "space-y-2") {
        h1(class' = "text-headline-lg text-on-surface") { title }

        match lead with
        | Some l -> (p(class' = "max-w-prose text-body-md text-on-surface-variant") { l }) :> HtmlElement
        | None -> Fragment() :> HtmlElement
    }

/// `field` with an input type ("text", "password"…), ids of other elements
/// describing the input (put first in aria-describedby) and the input's own
/// extra attributes. `.attr` appends, so `configure` must not set type, id,
/// name, value, class, aria-describedby or aria-invalid again.
let fieldWith
    (inputType: string)
    (describedBy: string list)
    (configure: input -> unit)
    (label': string)
    (name: string)
    (value: string)
    (hint: string option)
    (error: string option)
    =
    let hintId = $"{name}-hint"
    let errorId = $"{name}-error"

    let describedByAttr =
        [ yield! describedBy
          if hint.IsSome then hintId
          if error.IsSome then errorId ]
        |> String.concat " "

    let input' = input(type' = inputType, id = name, name = name, value = value, class' = "um-input")

    if describedByAttr <> "" then
        input'.attr("aria-describedby", describedByAttr) |> ignore

    if error.IsSome then
        input'.attr("aria-invalid", "true") |> ignore

    configure input'

    div() {
        label(for' = name, class' = "um-label") { label' }
        input'

        match hint with
        | Some h -> (p(id = hintId, class' = "um-hint") { h }) :> HtmlElement
        | None -> Fragment() :> HtmlElement

        match error with
        | Some e ->
            (p(id = errorId, class' = "um-field-error") {
                icon ErrorMark
                span() { e }
            })
            :> HtmlElement
        | None -> Fragment() :> HtmlElement
    }

/// A labelled text input with an optional hint and error
let field label' name value hint error = fieldWith "text" [] ignore label' name value hint error

/// A labelled password input
let passwordField label' name hint error =
    fieldWith "password" [] (fun i -> i.attr("autocomplete", "new-password") |> ignore) label' name "" hint error

/// A labelled password input for a credential that already exists, so a
/// password manager offers the saved value rather than suggesting a new one.
let currentPasswordField label' name hint error =
    fieldWith "password" [] (fun i -> i.attr("autocomplete", "current-password") |> ignore) label' name "" hint error

/// A read-only value with a copy button: how an operator gets the callback
/// URL, a client ID, or a freshly generated secret off the screen (contract:
/// "the computed callback URL is shown read-only, with a copy button").
/// `configureInput` lets the caller add a hook such as `.data("callback-url", "")`.
let copyField (copyLabel: string) (copiedLabel: string) (label': string) (name: string) (value: string) (configureInput: input -> unit) =
    let input' = input(type' = "text", id = name, name = name, value = value, class' = "um-input um-mono flex-1").attr("readonly", "")
    configureInput input'

    div(class' = "space-y-1.5") {
        label(for' = name, class' = "um-label") { label' }

        div(class' = "flex items-center gap-2") {
            input'

            button(type' = "button", class' = btn Secondary + " shrink-0").data("copy-trigger", "") {
                icon ContentCopy
                span().data("copy-label-idle", "") { copyLabel }
                span(class' = "hidden").data("copy-label-done", "") { copiedLabel }
            }
        }
    }

/// A saved secret, masked with its last four characters, and a Replace
/// action — never Edit, because there is nothing to edit from (D6).
let maskedSecretField (label': string) (maskedText: string) (replaceButton: HtmlElement) =
    div(class' = "space-y-1.5") {
        span(class' = "um-label") { label' }

        div(class' = "flex items-center justify-between gap-3") {
            span(class' = "um-mono text-body-md text-on-surface-variant") { maskedText }
            replaceButton
        }
    }

type PillTone =
    | PrimaryTone
    | SuccessTone
    | MutedTone

let pill (tone: PillTone) (text: string) =
    let toneClass =
        match tone with
        | PrimaryTone -> "um-pill-primary"
        | SuccessTone -> "um-pill-success"
        | MutedTone -> "um-pill-muted"

    span(class' = $"um-pill {toneClass}") { text }

/// Enabled/disabled state, shared by connector and user rows. The caller adds
/// `data-connector-state` / `data-user-state` to the element this returns.
let stateBadge (isEnabled: bool) (enabledLabel: string) (disabledLabel: string) =
    if isEnabled then pill SuccessTone enabledLabel else pill MutedTone disabledLabel

type AlertKind =
    | Success
    | Failure
    | Warning
    | Notice

let alert (kind: AlertKind) (text: string) =
    let cls, role, i =
        match kind with
        | Success -> "um-alert um-alert-success", "status", CheckCircle
        | Failure -> "um-alert um-alert-error", "alert", ErrorMark
        | Warning -> "um-alert um-alert-warning", "alert", Theme.Warning
        | Notice -> "um-alert um-alert-info", "status", Info

    div(class' = cls).attr("role", role) {
        span(class' = (if kind = Failure then "text-error" else "text-primary")) { icon i }
        span() { text }
    }

/// Empty state: icon, title, body, optional call to action
let emptyState (i: Icon) (title: string) (body: string) (action: HtmlElement option) =
    div(class' = "um-card flex flex-col items-center gap-3 py-12 text-center") {
        span(class' = "flex h-12 w-12 items-center justify-center rounded-full bg-surface-container-high text-headline-md text-on-surface-variant") {
            icon i
        }

        h2(class' = "text-headline-md text-on-surface") { title }
        p(class' = "max-w-prose text-body-md text-on-surface-variant") { body }

        match action with
        | Some a -> (div(class' = "mt-2") { a }) :> HtmlElement
        | None -> Fragment() :> HtmlElement
    }

/// Marks a destructive form to be confirmed before it is sent, naming what
/// breaks (contract: "destructive actions confirm, and the confirmation names
/// what breaks"). The message travels as `data-confirm` and Assets/umbral.js
/// shows the native `confirm()` — an inline `onsubmit` would be refused by the
/// Content-Security-Policy. With JavaScript off the confirm is skipped and the
/// POST goes through as normal, same as every other form here (D11). Oxpecker
/// encodes the attribute value, so an operator-supplied name in the message
/// cannot break out of it.
let confirmSubmit (message: string) (formEl: form) =
    formEl.data("confirm", message)
