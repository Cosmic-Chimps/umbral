/// The document shell. `page` is called once per full-page response;
/// `messagePage` wraps it for 404/500/error pages (contracts/admin-and-config.md).
/// Every htmx-marked route (connector toggle, connector save, user toggle)
/// returns the fragment alone when `HX-Request` is present and the same
/// element piped through `page` otherwise (research.md D11) — that branch
/// lives in the handler, not here.
module Umbral.Views.Layout

open Microsoft.AspNetCore.Http
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Views.Texts
open Umbral.Views.Theme

/// Top navigation: the operator's screens (Principle I) and sign-out. Only
/// `operatorPage` renders it, and only handlers already behind `requireOperator`
/// call that. Setup, sign-in, the end-user picker and every message page use
/// `page`, which shows the brand alone: an end user mid-login must not see
/// links into `/admin` or a sign-out button.
let private navLinks (ctx: HttpContext) (t: Texts) =
    nav(class' = "flex items-center gap-1 sm:gap-2") {
        a(href = "/admin/apps", class' = Components.btnSm Components.Ghost) { t.NavApps }
        a(href = "/admin/connectors", class' = Components.btnSm Components.Ghost) { t.NavConnectors }
        a(href = "/admin/users", class' = Components.btnSm Components.Ghost) { t.NavUsers }
        a(href = "/admin/credential", class' = Components.btnSm Components.Ghost) { t.NavCredential }

        form(method = "post", action = "/admin/signout") {
            ctx.GetAntiforgeryInput()
            button(type' = "submit", class' = Components.btnSm Components.Ghost) { t.NavSignOut }
        }
    }

/// The Content-Security-Policy of every page Umbral renders: scripts, styles,
/// fonts and frames only from this origin (nothing inline, nothing from a CDN),
/// images also from `data:` for the favicon, no plugins, no `<base>`.
///
/// It is set by `page`, not by a site-wide middleware, because OpenIddict's
/// `response_mode=form_post` answer is an HTML page with an inline auto-submit
/// script: a global policy would leave every app that uses form_post on a blank
/// page. Every page Umbral renders itself goes through `page`, so it is
/// covered. `form-action` and `frame-ancestors` are deliberately not set: they
/// are out of scope here, and `frame-ancestors` would interfere with the
/// silent-renew iframes of single-page-app clients.
let contentSecurityPolicy =
    "default-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'none'"

/// What a branded sign-in page takes from an app's branding (spec 006). Only
/// the accent colour reaches the page shell: the logo is content in the card,
/// placed by the picker, not part of the chrome.
type SignInBranding = { Accent: Umbral.Domains.Branding.Accent option }

/// Which chrome a page wears. Each is a different answer to "who is looking":
/// anyone (Umbral's brand and footer), the signed-in operator (the same plus the
/// navigation), or a person signing in to an app that has branding (the app's
/// colour, no Umbral wordmark, and the one mention of Umbral the page keeps).
type private Chrome =
    | Plain
    | Operator
    | SignIn of SignInBranding

/// Base HTML layout on the design system (src/Umbral/Assets/). The stylesheet
/// (compiled by build-css.sh), htmx and Umbral's own script are served by
/// Umbral itself, so a page loads nothing from a third party and runs no inline
/// script (see `contentSecurityPolicy`). `stylesheets` are further same-origin
/// sheets linked after `umbral.css`, so a rule in them wins ties with it: the
/// accent sheets of spec 006.
let private shell
    (chrome: Chrome)
    (stylesheets: string list)
    (ctx: HttpContext)
    (t: Texts)
    (titleText: string)
    (content: HtmlElement)
    =
    ctx.Response.Headers.ContentSecurityPolicy <- contentSecurityPolicy

    let showNav = (chrome = Operator)

    // The person's theme choice (cookie `umbral_theme`), written on <html> so the
    // stylesheet themes the page before the first paint: no script, no flash
    // (spec 007, contracts/theme.md). Under System both themes are offered to the
    // browser (`color-scheme`, one `theme-color` per scheme); a chosen theme
    // names only its own.
    let theme = ThemeChoice.ofRequest ctx

    let colorScheme =
        match theme with
        | System -> "dark light"
        | Light -> "light"
        | Dark -> "dark"

    let themeColors: HtmlElement list =
        match theme with
        | System ->
            [ meta(name = "theme-color", content = "#0b0b14").attr("media", "(prefers-color-scheme: dark)")
              meta(name = "theme-color", content = "#f4f1ea").attr("media", "(prefers-color-scheme: light)") ]
        | Light -> [ meta(name = "theme-color", content = "#f4f1ea") ]
        | Dark -> [ meta(name = "theme-color", content = "#0b0b14") ]

    // `data-accent` goes on the body, so the accent sheet's `[data-accent]` rule
    // reaches everything on the page. Only a branded sign-in page sets it; the
    // admin pages never do, which is what keeps the operator's own interface in
    // Umbral's colour whatever the apps look like (FR-005).
    let body' =
        body(class' = "min-h-screen bg-surface text-on-surface flex flex-col")

    match chrome with
    | SignIn { Accent = Some accent } -> body'.data("accent", Umbral.Domains.Branding.Accent.hex accent) |> ignore
    | _ -> ()

    let html' = html(lang = "en", class' = "bg-surface")
    html'.data("theme", ThemeChoice.attr theme) |> ignore

    html' {
        head() {
            meta(charset = "UTF-8")
            meta(name = "viewport", content = "width=device-width, initial-scale=1.0")
            meta(name = "color-scheme", content = colorScheme)

            for themeColor in themeColors do
                themeColor

            title() { $"{titleText} | {t.SiteName}" }
            link(rel = "icon", href = faviconHref).attr("type", "image/svg+xml")
            link(rel = "stylesheet", href = Assets.css.Href)

            for href in stylesheets do
                link(rel = "stylesheet", href = href)

            // htmx 4.x: Oxpecker.Htmx 4.0.* emits attributes for this major version only.
            script(src = Assets.htmx.Href)
            script(src = Assets.script.Href)
        }

        body' {
            a(href = "#main", class' = "sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-[60] um-btn um-btn-primary") {
                t.SkipToContent
            }

            // A branded sign-in page has no header: the wordmark is Umbral's
            // mark, and the app's page is the app's (FR-006).
            match chrome with
            | SignIn _ -> Fragment() :> HtmlElement
            | _ ->
                (header(class' = "sticky top-0 z-50 border-b border-outline-variant/40 bg-surface/90 backdrop-blur-md") {
                    nav(class' = "mx-auto flex h-16 max-w-page items-center justify-between gap-4 px-4 md:px-8") {
                        Components.brand showNav t.SiteName

                        if showNav then
                            navLinks ctx t
                    }
                })
                :> HtmlElement

            main (id = "main", class' = "mx-auto w-full max-w-page flex-1 px-4 py-8 md:px-8 md:py-12") { content }

            // The mark's colours are the theme's `--mark-*` and the text is a
            // fixed token; no accent rule sets either, so the mark stays
            // Umbral's whatever the app chose.
            match chrome with
            | SignIn _ ->
                (footer(class' = "border-t border-outline-variant/40").data("secured-by", "") {
                    div(class' = "mx-auto flex max-w-page items-center gap-3 px-4 py-6 text-label-md font-normal text-on-surface-variant md:px-8") {
                        span(class' = "block h-5 w-5") { raw Mark.small }
                        p() { t.SecuredByUmbral }
                    }
                })
                :> HtmlElement
            | _ ->
                (footer(class' = "border-t border-outline-variant/40") {
                    div(class' = "mx-auto flex max-w-page items-center gap-3 px-4 py-6 text-label-md font-normal text-on-surface-variant md:px-8") {
                        span(class' = "block h-5 w-5") { raw Mark.small }
                        p() { t.Footer }
                    }
                })
                :> HtmlElement
        }
    }

/// A page for anyone: the brand only, no operator navigation.
let page (ctx: HttpContext) (t: Texts) (titleText: string) (content: HtmlElement) =
    shell Plain [] ctx t titleText content

/// The sign-in page of an app that has branding: its accent colour on the page,
/// no Umbral header, and "Secured by Umbral" for a footer. Everything else about
/// the page, including the Content-Security-Policy, is `page`'s.
let signInPage (ctx: HttpContext) (t: Texts) (titleText: string) (branding: SignInBranding) (content: HtmlElement) =
    let stylesheets =
        match branding.Accent with
        | Some accent -> [ $"/branding/accent/{Umbral.Domains.Branding.Accent.hex accent}.css" ]
        | None -> []

    shell (SignIn branding) stylesheets ctx t titleText content

/// A page for a signed-in operator: the brand plus the operator navigation.
/// Call it only from handlers behind `AdminAuth.requireOperator`. `stylesheets`
/// are extra same-origin sheets, which the Apps screen uses to preview each
/// app's accent inside its card.
let operatorPage (ctx: HttpContext) (t: Texts) (titleText: string) (stylesheets: string list) (content: HtmlElement) =
    shell Operator stylesheets ctx t titleText content

/// Where a message page sends the person reading it, which depends on who that is.
///
/// Under `/admin` it is the operator, and `/admin` itself routes them to setup,
/// sign-in or their landing screen. Everywhere else it is an end user in the
/// middle of an app's sign-in (a rate limit on `/connect/...`, a protocol error,
/// an expired form), and the operator sign-in is the wrong place for them —
/// they cannot use it and should not learn it exists. There is no Umbral page
/// to send them to either: `/signin` without a pending authorize request has
/// nowhere to return to afterwards. What they can act on is the app they came
/// from, so the page says so in words instead of offering a link.
let private backLink (ctx: HttpContext) (t: Texts) : HtmlElement =
    if ctx.Request.Path.StartsWithSegments(PathString "/admin") then
        a(href = "/admin", class' = Components.btn Components.Primary) {
            Components.icon Home
            t.BackHome
        }
    else
        p(class' = "text-body-md text-on-surface-variant").data("back-to-app", "") { t.BackToApp }

/// Short message page whose body is composed by the caller, for the cases that
/// need more than a sentence — a protocol error showing the detail a developer
/// integrating an app actually needs.
let messagePageWith (ctx: HttpContext) (t: Texts) (heading: string) (body: HtmlElement) =
    div(class' = "flex min-h-[50vh] items-center justify-center") {
        div(class' = "um-card w-full max-w-md text-center space-y-4") {
            span(class' = "mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-surface-container-high text-headline-lg text-primary") {
                Components.icon ExploreOff
            }

            h1(class' = "text-headline-lg text-on-surface") { heading }
            body
            backLink ctx t
        }
    }
    |> page ctx t heading

/// Short message page (404, 500, rate limit, expired form)
let messagePage (ctx: HttpContext) (t: Texts) (heading: string) (body: string) =
    p(class' = "text-body-md text-on-surface-variant") { body }
    |> messagePageWith ctx t heading

/// The themed 404. Every "there is nothing here" answer goes through this, so a
/// route that is absent, a connector that is disabled and a fake provider that is
/// not allowed are all the same page: nothing to tell them apart by probing.
let notFound (ctx: HttpContext) =
    let t = Texts.current ()
    ctx.SetStatusCode 404
    ctx.WriteHtmlView(messagePage ctx t t.NotFoundTitle t.NotFoundBody)
