/// The passkey sign-in page (specs/008-passkey-connector, contracts/ceremonies.md
/// "The passkey sign-in page").
///
/// The picker's button for the passkey connector leads here instead of to a
/// provider. The page only draws the controls; `/assets/passkeys.js` runs the
/// ceremony against the JSON endpoints, reading the antiforgery token from the
/// hidden input rendered here.
module Umbral.Handlers.Passkeys.SignIn

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Domains.Connectors
open Umbral.Handlers.Connect
open Umbral.Infrastructure
open Umbral.Views

/// What the page says when the script cannot run. A control that does nothing
/// without script is worse than a line that says so.
let javascriptNote (t: Texts.Texts) : HtmlElement =
    (noscript().data("javascript-required", "") {
        p(class' = "text-body-md text-on-surface-variant") { t.PasskeyJavascriptRequired }
    })

/// The failure the script asked the server to name with `?msg=`, if it is one
/// of the known codes. Never any other text from the address.
let noticeAlert (ctx: HttpContext) (t: Texts.Texts) : HtmlElement =
    match ctx.TryGetQueryValue "msg" |> Option.bind Texts.knownNotice with
    | Some code -> Components.alert Components.Failure (t.Notice code) :> HtmlElement
    | None -> Fragment() :> HtmlElement

/// The optional nickname field of every registration form.
let nicknameField (t: Texts.Texts) : HtmlElement =
    Components.fieldWith
        "text"
        []
        (fun i -> i.attr("maxlength", "64") |> ignore)
        t.PasskeyNicknameLabel
        "nickname"
        ""
        None
        None

let private signUpForm (t: Texts.Texts) : HtmlElement =
    form(method = "post", class' = "space-y-4").data("passkey-signup", "") {
        Components.fieldWith
            "text"
            []
            (fun i -> i.attr("required", "required").attr("maxlength", "64") |> ignore)
            t.PasskeyDisplayNameLabel
            "displayName"
            ""
            None
            None

        nicknameField t

        button(type' = "submit", class' = Components.btn Components.Secondary + " w-full justify-center") {
            t.PasskeyCreateAccountButton
        }
    }

let private card (ctx: HttpContext) (t: Texts.Texts) (enrolment: PasskeyEnrolment) =
    div(class' = "flex min-h-[60vh] items-center justify-center") {
        div(class' = "um-card w-full max-w-sm space-y-5").data("screen", "passkey-signin") {
            Components.pageHeading t.SignInWithPasskey (Some t.PasskeySignInLead)
            noticeAlert ctx t
            ctx.GetAntiforgeryInput()

            button(type' = "button", class' = Components.btn Components.Primary + " w-full justify-center")
                .data("passkey-signin", "") {
                Components.icon Theme.Link
                t.PasskeyContinue
            }

            match enrolment with
            | OpenSignUp -> signUpForm t
            | InvitationsOnly ->
                (p(class' = "text-body-md text-on-surface-variant").data("invitations-only", "") {
                    t.PasskeyInvitationsOnlyLine
                })
                :> HtmlElement

            javascriptNote t
        }
    }

let handler: EndpointHandler =
    fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let t = Texts.current ()

            match! ConnectorStore.findBySlug config "passkey" with
            | Some connector when connector.Kind = Passkey && connector.IsEnabled ->
                let returnUrl = Picker.sanitizeReturnUrl (ctx.TryGetQueryValue "returnUrl" |> Option.toObj)

                // The app's chrome comes from the same `returnUrl` the picker
                // reads and from nothing else, so a crafted link cannot dress
                // one app's page as another's.
                let! app = Picker.findApp ctx returnUrl

                let! branding =
                    match app with
                    | Some app -> BrandingStore.forApp config app.ClientId
                    | None -> System.Threading.Tasks.Task.FromResult None

                let content = card ctx t connector.PasskeyEnrolment
                let scripts = [ Assets.passkeys ]

                match branding with
                | Some branding ->
                    return!
                        ctx.WriteHtmlView(
                            content
                            |> Layout.signInPageWithScripts scripts ctx t t.SignInWithPasskey { Accent = branding.Accent }
                        )
                | None -> return! ctx.WriteHtmlView(content |> Layout.pageWithScripts scripts ctx t t.SignInWithPasskey)
            | _ -> return! Layout.notFound ctx
        }

let endpoint =
    [ GET [ route "/connect/passkey/start" handler |> configureEndpoint _.RequireRateLimiting(RateLimits.SignIn) ] ]
