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

/// Top navigation: the three screens (Principle I) and sign-out. Shown on
/// every page `Layout.page` renders; `/admin/*` routes that require it are
/// already behind `requireOperator` by the time a handler calls this, and the
/// unauthenticated screens (setup, sign-in, the end-user picker) render their
/// own narrow content and simply don't link into it.
let private nav (ctx: HttpContext) (t: Texts) =
    Oxpecker.ViewEngine.nav(class' = "flex items-center gap-1 sm:gap-2") {
        a(href = "/admin/apps", class' = Components.btnSm Components.Ghost) { t.NavApps }
        a(href = "/admin/connectors", class' = Components.btnSm Components.Ghost) { t.NavConnectors }
        a(href = "/admin/users", class' = Components.btnSm Components.Ghost) { t.NavUsers }

        form(method = "post", action = "/admin/signout") {
            ctx.GetAntiforgeryInput()
            button(type' = "submit", class' = Components.btnSm Components.Ghost) { t.NavSignOut }
        }
    }

/// Base HTML layout on the design system (Views/Theme.fs). No frontend build
/// step: Tailwind, fonts and htmx all come from a CDN, configured at request
/// time from F# data.
let page (ctx: HttpContext) (t: Texts) (titleText: string) (content: HtmlElement) =
    html(lang = "en", class' = "bg-surface") {
        head() {
            meta(charset = "UTF-8")
            meta(name = "viewport", content = "width=device-width, initial-scale=1.0")
            meta(name = "color-scheme", content = "dark")
            meta(name = "theme-color", content = "#0d1117")
            title() { $"{titleText} | {t.SiteName}" }
            link(rel = "icon", href = faviconHref).attr("type", "image/svg+xml")
            link(rel = "preconnect", href = "https://fonts.googleapis.com")
            link(rel = "preconnect", href = "https://fonts.gstatic.com").attr("crossorigin", "")

            for sheet in fontStylesheets do
                link(rel = "stylesheet", href = sheet)

            style() { raw tokensCss }
            script(src = "https://cdn.tailwindcss.com")
            script() { raw tailwindConfig }
            style().attr("type", "text/tailwindcss") { raw componentCss }
            // htmx 4.x: Oxpecker.Htmx 4.0.* emits attributes for this major version only.
            script(src = "https://cdn.jsdelivr.net/npm/htmx.org@4.0.0", crossorigin = "anonymous")
            script() { raw Components.copyScript }
        }

        body(class' = "min-h-screen bg-surface text-on-surface flex flex-col") {
            a(href = "#main", class' = "sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-[60] um-btn um-btn-primary") {
                t.SkipToContent
            }

            header(class' = "sticky top-0 z-50 border-b border-outline-variant/40 bg-surface/90 backdrop-blur-md") {
                Oxpecker.ViewEngine.nav(class' = "mx-auto flex h-16 max-w-page items-center justify-between gap-4 px-4 md:px-8") {
                    Components.brand t.SiteName
                    nav ctx t
                }
            }

            main (id = "main", class' = "mx-auto w-full max-w-page flex-1 px-4 py-8 md:px-8 md:py-12") { content }

            footer (class' = "border-t border-outline-variant/40") {
                div(class' = "mx-auto flex max-w-page items-center gap-3 px-4 py-6 text-label-md font-normal text-on-surface-variant md:px-8") {
                    span(class' = "block h-5 w-5 opacity-80") { raw markSvg }
                    p() { t.Footer }
                }
            }
        }
    }

/// Short message page (404, 500, rate limit, expired form)
let messagePage (ctx: HttpContext) (t: Texts) (heading: string) (body: string) =
    div(class' = "flex min-h-[50vh] items-center justify-center") {
        div(class' = "um-card w-full max-w-md text-center space-y-4") {
            span(class' = "mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-surface-container-high text-headline-lg text-primary") {
                Components.icon ExploreOff
            }

            h1(class' = "text-headline-lg text-on-surface") { heading }
            p(class' = "text-body-md text-on-surface-variant") { body }

            a(href = "/admin", class' = Components.btn Components.Primary) {
                Components.icon Home
                t.BackHome
            }
        }
    }
    |> page ctx t heading
