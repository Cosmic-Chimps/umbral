/// The end user's "sign in with…" page.
///
/// The only screen an end user ever sees on Umbral, and usually for under a
/// second. It exists because the authorize handler needs somewhere to send
/// someone who has no session yet.
module Umbral.Handlers.Connect.Picker

open System
open OpenIddict.Abstractions
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Domains.Branding
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Views

/// Browsers drop tabs and line breaks anywhere in a URL and trim spaces and
/// control characters, so `/\t/host` is `//host`; a raw line break would also
/// split the `Location` header.
let private hasInvisible (text: string) =
    text |> Seq.exists (fun c -> Char.IsControl c || Char.IsWhiteSpace c)

/// A path a browser resolves on this instance and nowhere else. Browsers read a
/// `\` in the path of an http(s) URL as a `/`, so `/\host` is `//host`.
let private isLocalPath (path: string) =
    path.StartsWith '/' && not (path.StartsWith "//") && not (path.Contains '\\') && not (hasInvisible path)

/// Where to go once a provider has vouched for them. Only ever a path on this
/// instance: an absolute URL here would make the picker an open redirect, and
/// the value is attacker-supplied by construction. The path part must also stay
/// local once percent-decoded (`/%5Chost`, `/%2F/host`): a browser keeps those
/// on this instance, but one layer that decodes them once more would not. A `\`
/// in the query is just a character, so only the path is held to that rule.
let sanitizeReturnUrl (candidate: string | null) =
    match Option.ofObj candidate with
    | Some url when not (hasInvisible url) ->
        let path =
            match url.IndexOfAny [| '?'; '#' |] with
            | -1 -> url
            | at -> url.Substring(0, at)

        if isLocalPath path && isLocalPath (Uri.UnescapeDataString path) then url else "/"
    | _ -> "/"

/// Everything an end user sees when a sign-in fails is one of these codes,
/// turned into text below. FR-037: never the provider's own words, and never
/// anything about how Umbral is configured.
let failureCode (error: ProviderAdapters.ConnectorError) =
    match error with
    | ProviderAdapters.ProviderDeclined _ -> "access-denied"
    | ProviderAdapters.ProviderRejected _ -> "sign-in-failed"
    | ProviderAdapters.ProviderUnreachable _ -> "provider-unreachable"
    | ProviderAdapters.MalformedResponse _ -> "sign-in-failed"

/// Back to the sign-in page with a failure code. With a `returnUrl` it goes
/// back to the page of the app the person started from, so the page still names
/// that app and offers its connectors (FR-001, research.md R4); without one
/// there is no round trip to return to, and the bare page is all there is.
/// Shared by the two handlers that can fail a sign-in: the start of the round
/// trip and its callback.
let redirectWithFailure (ctx: Microsoft.AspNetCore.Http.HttpContext) (returnUrl: string option) (code: string) =
    match returnUrl with
    | Some returnUrl ->
        ctx.Response.Redirect $"/signin?returnUrl={Uri.EscapeDataString returnUrl}&msg={Uri.EscapeDataString code}"
    | None -> ctx.Response.Redirect $"/signin?msg={Uri.EscapeDataString code}"

/// The app a sign-in belongs to, learned from the authorize request it will be
/// sent back to and from nowhere else (FR-001, FR-002). A `client_id` on
/// `/signin` itself is deliberately never read: it could disagree with
/// `returnUrl` and let a crafted link say "Sign in to A" while signing in to B.
let private clientIdOf (returnUrl: string) =
    match returnUrl.IndexOf '?' with
    | -1 -> None
    | at ->
        let path = returnUrl.Substring(0, at)

        if path = "/connect/authorize" then
            let query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(returnUrl.Substring(at))

            match query.TryGetValue "client_id" with
            | true, values when values.Count > 0 && not (String.IsNullOrEmpty values[0]) -> Some(string values[0])
            | _ -> None
        else
            None

/// A registered app: its client id and the name people know it by.
type private NamedApp = { ClientId: string; Name: string }

/// A connector's button. Primary when the app chose an accent colour, so the
/// colour is what the person sees to press; secondary otherwise, as it always
/// was (a logo alone does not recolour anything).
let private button (t: Texts.Texts) (kind: Components.ButtonKind) (returnUrl: string) (connector: Connector) =
    let href =
        $"/connect/{Uri.EscapeDataString connector.Slug}/start?returnUrl={Uri.EscapeDataString returnUrl}"

    a(href = href, class' = Components.btn kind + " w-full justify-center")
        .data("connector", connector.Slug) {
        Components.icon Theme.Link
        t.SignInWith connector.DisplayName
    }

let private view
    (ctx: Microsoft.AspNetCore.Http.HttpContext)
    (t: Texts.Texts)
    (returnUrl: string)
    (app: NamedApp option)
    (branding: AppBranding option)
    (connectors: Connector list)
    (notice: string option)
    =
    let title =
        match app with
        | Some app -> t.PickerTitleForApp app.Name
        | None -> t.PickerTitle

    let buttonKind =
        match branding with
        | Some { Accent = Some _ } -> Components.Primary
        | _ -> Components.Secondary

    // No branding: the page as it always was. Branding: the same card in the
    // branded chrome (accent, no Umbral header, "Secured by Umbral").
    let inLayout (card: HtmlElement) =
        match branding with
        | None -> card |> Layout.page ctx t title
        | Some branding -> card |> Layout.signInPage ctx t title { Accent = branding.Accent }

    div(class' = "flex min-h-[60vh] items-center justify-center") {
        div(class' = "um-card w-full max-w-sm space-y-5").data("screen", "signin") {
            // The logo is first in the card, above the heading. Alt text is the
            // app's name, which the heading says too, so a screen reader hears
            // the name once in each place it is shown.
            match app, branding |> Option.bind (fun b -> b.Logo) with
            | Some app, Some logo ->
                (img(
                    src = $"/branding/logo/{logo.Sha256}.png",
                    alt = app.Name,
                    width = logo.Width,
                    height = logo.Height,
                    class' = Components.signInLogoClass
                )
                    .data("signin-logo", app.ClientId))
                :> HtmlElement
            | _ -> Fragment() :> HtmlElement

            // The marker sits on the element that names the app, so a test reads
            // WHICH app the page serves without asserting on the wording.
            match app with
            | Some app ->
                (div().data("signin-app", app.ClientId) { Components.pageHeading title (Some t.PickerLead) })
                :> HtmlElement
            | None -> Components.pageHeading title (Some t.PickerLead) :> HtmlElement

            match notice with
            | Some code -> Components.alert Components.Failure (t.Notice code) :> HtmlElement
            | None -> Fragment() :> HtmlElement

            match connectors with
            | [] ->
                // Nobody can sign in anywhere. The end user cannot fix this, so
                // the message points at the person who can rather than
                // pretending something went wrong on their side. With an app
                // named, it says which app has nothing (FR-007, FR-011).
                (p(class' = "text-body-md text-on-surface-variant").data("no-connectors", "") {
                    match app with
                    | Some app -> t.PickerNoConnectorsForApp app.Name
                    | None -> t.PickerNoConnectors
                })
                :> HtmlElement

            | connectors ->
                (div(class' = "flex flex-col gap-3") {
                    for connector in connectors do
                        button t buttonKind returnUrl connector
                })
                :> HtmlElement
        }
    }
    |> inLayout

let handler: EndpointHandler =
    fun ctx ->
        task {
            let t = Texts.current ()
            let returnUrl = sanitizeReturnUrl (ctx.TryGetQueryValue "returnUrl" |> Option.toObj)
            let notice = ctx.TryGetQueryValue "msg" |> Option.bind Texts.knownNotice

            let config = ctx.GetService<Database.DbConfig>()

            // An unknown `client_id` is "no app", never an error: the page is
            // reachable by anyone with a link and must not confirm which apps
            // exist (FR-002).
            let! app =
                task {
                    match clientIdOf returnUrl with
                    | None -> return None
                    | Some clientId ->
                        let applications = ctx.GetService<IOpenIddictApplicationManager>()
                        let! found = applications.FindByClientIdAsync clientId

                        match found with
                        | null -> return None
                        | application ->
                            let! name = applications.GetDisplayNameAsync application

                            return
                                Some
                                    { ClientId = clientId
                                      Name = (if String.IsNullOrEmpty name then clientId else string name) }
                }

            // The app's branding comes from the same `clientIdOf returnUrl` as
            // its name and its connectors, and from nothing else (FR-003): a
            // `client_id` on this page's own query never reaches it, so a
            // crafted link cannot dress one app's sign-in as another's. No app,
            // no read.
            let! branding =
                task {
                    match app with
                    | Some app -> return! BrandingStore.forApp config app.ClientId
                    | None -> return None
                }

            // `offeredTo` keeps enabled connectors only, so an option that
            // cannot work is not offered. No app means every shared one.
            let! all = ConnectorStore.listAll config

            let! choice =
                task {
                    match app with
                    | Some app -> return! AppConnectors.choiceFor config app.ClientId
                    | None -> return AllShared
                }

            return! ctx.WriteHtmlView(view ctx t returnUrl app branding (offeredTo choice all) notice)
        }

let endpoint = GET [ route "/signin" handler ]
