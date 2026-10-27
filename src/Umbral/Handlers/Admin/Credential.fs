/// Replacing the operator credential while signed in.
///
/// FR-017a. The first-run screen sets it, `reset-credential` replaces it from
/// the host for someone who has lost it, and this is the third door: the
/// operator who still has it and wants a different one — after sharing a screen,
/// after typing it somewhere they should not have, or simply because the
/// generated one from a reset is not the one they want to keep.
///
/// It asks for the CURRENT credential even though the person is signed in. A
/// session cookie is the weaker thing here (it can be left open on a shared
/// machine, or lifted from a browser), and replacing the credential is the
/// action that would turn that into permanent access.
module Umbral.Handlers.Admin.Credential

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Infrastructure
open Umbral.Infrastructure.SecurityEvents
open Umbral.Views

/// Which confirm block the screen draws open. A remove or a switch posted
/// without a re-check lands here as a 422 redraw, the way a strand warning
/// redraws with its confirm (design/contracts/operator-passkeys-ui.md, 2).
type Confirm =
    | RemovePasskey of id: string
    | SwitchOff
    | SwitchOn

let private day (value: System.DateTimeOffset) =
    value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)

/// What the page says where a ceremony button would be when the script cannot
/// run. Same shape as the connector's note, kept here because that module is
/// compiled later.
let private javascriptNote (t: Texts.Texts) : HtmlElement =
    (noscript().data("javascript-required", "") {
        p(class' = "text-body-md text-on-surface-variant") { t.PasskeyJavascriptRequired }
    })

/// A button only the script can make useful: hidden and disabled until
/// `passkeys.js` reveals it, so a browser without script never shows a dead
/// control, and Enter in a field cannot submit a form whose only way to finish
/// is the script (the add form would otherwise post its password nowhere useful).
let private scripted (element: button) =
    element.attr("hidden", "").attr("disabled", "").data("scripted", "")

/// The re-check field. Its id is its own because the add form, a remove
/// confirm and the Password card can be on one page and all post `current`.
let private currentField (t: Texts.Texts) (id': string) (focus: bool) : HtmlElement =
    div() {
        label(for' = id', class' = "um-label") { t.CurrentPasswordLabel }

        let field =
            input(type' = "password", id = id', name = "current", class' = "um-input")
                .attr("autocomplete", "current-password")
                .attr("required", "")

        if focus then field.attr("autofocus", "") |> ignore

        field
    }

/// The two inputs `passkeys.js` fills with a confirm ceremony's answer.
let private ceremonyInputs: HtmlElement =
    Fragment() {
        input(type' = "hidden", name = "ceremony")
        input(type' = "hidden", name = "credential")
    }

let private usePasskeyButton (t: Texts.Texts) : HtmlElement =
    // `formnovalidate`: the password field beside it is required, and the
    // point of this button is not to type it.
    button(type' = "submit", class' = Components.btnSm Components.Secondary)
        .attr("formnovalidate", "")
        .data("operator-confirm-passkey", "")
    |> scripted
    |> fun b ->
        b { t.UsePasskeyInstead }

let private cancelLink (t: Texts.Texts) : HtmlElement =
    a(href = "/admin/credential", class' = Components.btnSm Components.Ghost) { t.ConfirmCancel }

let private notice (t: Texts.Texts) (code: string) : HtmlElement =
    let kind =
        match code with
        | "operator-passkey-added"
        | "operator-passkey-removed"
        | "operator-password-off"
        | "operator-password-on"
        | "credential-replaced" -> Components.Success
        | _ -> Components.Warning

    (div().data("notice", code) { Components.alert kind (t.Notice code) }) :> HtmlElement

let private removeConfirm (ctx: HttpContext) (t: Texts.Texts) (passwordOn: bool) (others: bool) (key: OperatorPasskeys.OperatorPasskey) =
    div(class' = "space-y-3 py-2").data("passkey-row", key.Id).data ("confirm", "remove") {
        p(class' = "text-body-md text-on-surface") { t.OperatorPasskeyRemoveQuestion key.Name }

        form(method = "post", action = $"/admin/credential/passkeys/{key.Id}/remove", class' = "space-y-3")
            .data ("operator-confirm", $"remove:{key.Id}") {
            ctx.GetAntiforgeryInput()
            ceremonyInputs

            if passwordOn then
                currentField t "current-remove" true

            div(class' = "flex flex-wrap items-center gap-2") {
                let remove = button(type' = "submit", class' = Components.btnSm Components.Danger)

                if not passwordOn then
                    remove.attr("autofocus", "") |> scripted |> ignore

                remove { t.OperatorPasskeyRemoveConfirmButton }

                if passwordOn && others then
                    usePasskeyButton t

                cancelLink t
            }

            if not passwordOn then
                javascriptNote t
        }
    }

let private passkeyRow (ctx: HttpContext) (t: Texts.Texts) (key: OperatorPasskeys.OperatorPasskey) =
    let used =
        match key.LastUsedUtc with
        | Some d -> t.OperatorPasskeyLastUsedOn(day d)
        | None -> t.OperatorPasskeyNeverUsed

    div(class' = "flex items-center justify-between gap-3 py-2").data ("passkey-row", key.Id) {
        div(class' = "min-w-0") {
            p(class' = "text-body-md font-medium text-on-surface") { key.Name }
            p(class' = "text-label-md text-on-surface-variant") { $"{t.OperatorPasskeyAddedOn(day key.CreatedUtc)} · {used}" }
        }

        form(method = "post", action = $"/admin/credential/passkeys/{key.Id}/remove") {
            ctx.GetAntiforgeryInput()

            button(type' = "submit", class' = Components.btnSm Components.Ghost + " text-error hover:text-error") {
                t.OperatorPasskeyRemoveButton
            }
        }
    }

let private addForm (ctx: HttpContext) (t: Texts.Texts) (passwordOn: bool) (any: bool) =
    form(method = "post", class' = "space-y-4").data ("operator-passkey-add", "") {
        ctx.GetAntiforgeryInput()

        Components.fieldWith
            "text"
            []
            (fun i -> i.attr("maxlength", "64") |> ignore)
            t.OperatorPasskeyNameLabel
            "name"
            ""
            (Some t.OperatorPasskeyNameHint)
            None

        if passwordOn then
            currentField t "current-add" false

        div(class' = "flex flex-wrap items-center gap-2") {
            // Registration always needs the script, with or without a password.
            button(type' = "submit", class' = Components.btn Components.Primary) { t.OperatorPasskeyAddButton }
            |> scripted

            if passwordOn && any then
                usePasskeyButton t
        }

        javascriptNote t
    }

let private passkeysCard (ctx: HttpContext) (t: Texts.Texts) (passwordOn: bool) (keys: OperatorPasskeys.OperatorPasskey list) (confirm: Confirm option) =
    section(class' = "um-card space-y-4").data ("section", "passkeys") {
        div(class' = "space-y-1") {
            h2(class' = "text-headline-md text-on-surface") { t.OperatorPasskeysTitle }
            p(class' = "text-body-md text-on-surface-variant") { t.OperatorPasskeysLead }
        }

        if keys.IsEmpty then
            p(class' = "text-body-md text-on-surface-variant").data ("passkeys-empty", "") { t.OperatorPasskeysEmpty }
        else
            div() {
                for index, key in List.indexed keys do
                    let row =
                        match confirm with
                        | Some(RemovePasskey id) when id = key.Id -> removeConfirm ctx t passwordOn (keys.Length > 1) key :> HtmlElement
                        | _ -> passkeyRow ctx t key :> HtmlElement

                    // Hairlines between rows, never a card inside the card.
                    div(class' = (if index = 0 then "" else "border-t border-outline-variant/40")) { row }
            }

        if keys.Length >= OperatorPasskeys.MaxPasskeys then
            p(class' = "text-body-md text-on-surface-variant").data ("passkeys-limit", "") { t.OperatorPasskeysLimit }
        else
            addForm ctx t passwordOn (not keys.IsEmpty)
    }

let private switchCard (ctx: HttpContext) (t: Texts.Texts) (passwordOn: bool) (any: bool) (confirm: Confirm option) =
    let state = if passwordOn then "off" else "on"

    section(class' = "um-card space-y-4").data ("section", "password-signin") {
        div(class' = "flex flex-wrap items-center gap-3") {
            h2(class' = "text-headline-md text-on-surface") { t.PasswordSignInTitle }

            (Components.stateBadge passwordOn t.PasswordSignInOn t.PasswordSignInOff)
                .data ("password-signin-state", (if passwordOn then "on" else "off"))
        }

        p(class' = "text-body-md text-on-surface-variant") { if passwordOn then t.PasswordSignInOnSentence else t.PasswordSignInOffSentence }

        match confirm with
        | Some SwitchOff
        | Some SwitchOn ->
            let off = (confirm = Some SwitchOff)

            div(class' = "space-y-3").data ("confirm", (if off then "switch-off" else "switch-on")) {
                p(class' = "text-body-md text-on-surface") { if off then t.PasswordSignInOffConfirm else t.PasswordSignInOnConfirm }

                form(method = "post", action = "/admin/credential/password-signin", class' = "space-y-3")
                    .data ("operator-confirm", (if off then "switch-off" else "switch-on")) {
                    ctx.GetAntiforgeryInput()
                    input(type' = "hidden", name = "state", value = (if off then "off" else "on"))
                    ceremonyInputs

                    div(class' = "flex flex-wrap items-center gap-2") {
                        button(type' = "submit", class' = Components.btn Components.Primary).attr ("autofocus", "")
                        |> scripted
                        |> fun b -> b { t.ConfirmWithPasskey }

                        cancelLink t
                    }

                    javascriptNote t
                }
            }
            :> HtmlElement
        | _ ->
            form(method = "post", action = "/admin/credential/password-signin", class' = "space-y-2") {
                ctx.GetAntiforgeryInput()
                input(type' = "hidden", name = "state", value = state)

                let blocked = passwordOn && not any

                let toggle = button(type' = "submit", class' = Components.btnSm Components.Secondary)

                if blocked then
                    toggle.attr("disabled", "") |> ignore

                toggle { if passwordOn then t.TurnPasswordSignInOff else t.TurnPasswordSignInOn }

                if blocked then
                    p(class' = "um-hint").data ("add-passkey-first", "") { t.AddPasskeyFirstHint }
            }
            :> HtmlElement
    }

let private passwordCard (ctx: HttpContext) (t: Texts.Texts) =
    section(class' = "um-card space-y-4").data ("section", "password") {
        div(class' = "space-y-1") {
            h2(class' = "text-headline-md text-on-surface") { t.PasswordCardTitle }
            p(class' = "text-body-md text-on-surface-variant") { t.PasswordCardLead }
        }

        form(method = "post", action = "/admin/credential", class' = "space-y-4") {
            ctx.GetAntiforgeryInput()

            Components.currentPasswordField t.CurrentPasswordLabel "current" None None
            Components.passwordField t.PasswordNewLabel "credential" (Some t.CredentialHint) None
            Components.passwordField t.PasswordConfirmLabel "confirm" None None

            button(type' = "submit", class' = Components.btn Components.Primary) {
                Components.icon Theme.Shield
                t.PasswordReplaceButton
            }
        }
    }

/// Draws the screen; `confirm` expands that row or the switch card into its
/// confirm block. The remove and switch routes call it with a 422 status.
let render (ctx: HttpContext) (confirm: Confirm option) : Task =
    task {
        let config = ctx.GetService<Database.DbConfig>()
        let t = Texts.current ()
        let! keys = OperatorPasskeys.list config
        let! passwordOn = OperatorPasskeys.passwordSignInEnabled config
        let code = ctx.TryGetQueryValue "msg" |> Option.bind Texts.knownNotice

        let view =
            div(class' = "mx-auto w-full max-w-2xl space-y-5").data ("screen", "credential") {
                Components.pageHeading t.CredentialTitle (Some t.CredentialLead)

                match code with
                | Some code -> notice t code
                | None -> Fragment() :> HtmlElement

                passkeysCard ctx t passwordOn keys confirm
                switchCard ctx t passwordOn (not keys.IsEmpty) confirm

                if passwordOn then
                    passwordCard ctx t
            }
            |> Layout.operatorPageWithScripts [ Assets.passkeys ] ctx t t.CredentialTitle []

        do! ctx.WriteHtmlView view
    }
    :> Task

let page: EndpointHandler = AdminAuth.requireOperator (fun ctx -> render ctx None)

let submit: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<System.TimeProvider>()
            let! form = WebForms.readValidated ctx
            let current = WebForms.value form "current"
            let credential = WebForms.value form "credential"
            let confirm = WebForms.value form "confirm"
            let remoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string

            match! AdminAuth.verify config current with
            | false ->
                // A wrong guess at the credential is a wrong guess whoever
                // makes it, and a signed-in session is exactly what someone
                // who walked up to an open browser has.
                do!
                    SecurityEvents.record
                        config
                        clock
                        { event OperatorSignInFailed Failure Actor.Operator with
                            RemoteIp = remoteIp
                            Detail = Some "credential_change_mismatch" }

                return! WebForms.redirectWithNotice ctx "/admin/credential" "credential-current-incorrect"

            | true ->

            if credential <> confirm then
                return! WebForms.redirectWithNotice ctx "/admin/credential" "setup-mismatch"
            else
                match! AdminAuth.replaceCredential config clock credential with
                | AdminAuth.Set ->
                    do!
                        SecurityEvents.record
                            config
                            clock
                            { event OperatorCredentialChanged Success Actor.Operator with
                                RemoteIp = remoteIp
                                Detail = Some "replaced_signed_in" }

                    // Every session was just ended, this one included. The
                    // person who changed it has not left, so they get a new
                    // one — the only session that survives.
                    do! AdminAuth.signIn ctx
                    return! WebForms.redirectWithNotice ctx "/admin/credential" "credential-replaced"

                | AdminAuth.TooShort _
                | AdminAuth.AlreadyConfigured -> return! WebForms.redirectWithNotice ctx "/admin/credential" "setup-too-short"
        })

let endpoint =
    [ GET [ route "/admin/credential" page ]
      POST [ route "/admin/credential" submit |> configureEndpoint _.RequireRateLimiting(RateLimits.AdminAuth) ] ]
