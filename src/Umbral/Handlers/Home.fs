/// `GET /`: the front page, for anyone who types the bare address.
///
/// Static on purpose. It is the same page for every visitor and says nothing a
/// stranger could use to learn about this instance: no connectors, no apps, no
/// user count, not even whether the instance has been claimed yet (the rule the
/// themed 404 follows too: nothing to learn by probing). The one instance-specific
/// value is the issuer, the configured public origin, and that is already public
/// through the discovery document the page links to.
///
/// It tells each of the three kinds of visitor where to go next: an end user
/// back to the app they came from, an app developer to the issuer and the
/// discovery document, an operator to `/admin` (which itself routes to setup,
/// sign-in or Connectors). Only `/` is mapped; every other unknown path keeps
/// the themed 404.
module Umbral.Handlers.Home

open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Infrastructure
open Umbral.Views
open Umbral.Views.Theme

let private view (t: Texts.Texts) (origin: PublicOrigin.PublicOrigin) =
    div(class' = "space-y-10") {
        section(class' = "flex flex-col items-center gap-4 py-6 text-center") {
            span(class' = "block h-16 w-16") { raw Mark.full }
            h1(class' = "text-headline-lg text-on-surface") { t.HomeHeadline }
            p(class' = "max-w-prose text-body-md text-on-surface-variant") { t.HomeLead }
        }

        div(class' = "grid gap-4 md:grid-cols-3") {
            section(class' = "um-card space-y-3") {
                span(class' = "flex h-10 w-10 items-center justify-center rounded-full bg-surface-container-high text-headline-md text-primary") {
                    Components.icon Lock
                }

                h2(class' = "text-headline-md text-on-surface") { t.HomeSigningInHeading }
                p(class' = "text-body-md text-on-surface-variant") { t.HomeSigningInBody }
            }

            section(class' = "um-card space-y-3") {
                span(class' = "flex h-10 w-10 items-center justify-center rounded-full bg-surface-container-high text-headline-md text-primary") {
                    Components.icon Key
                }

                h2(class' = "text-headline-md text-on-surface") { t.HomeBuildingHeading }
                p(class' = "text-body-md text-on-surface-variant") { t.HomeBuildingBody }
                Components.copyField t.Copy t.Copied t.AppIssuerUrlLabel "home-issuer" origin.Text ignore

                a(href = "/.well-known/openid-configuration", class' = Components.btn Components.Secondary) {
                    Components.icon Link
                    t.HomeDiscoveryLink
                }
            }

            section(class' = "um-card space-y-3") {
                span(class' = "flex h-10 w-10 items-center justify-center rounded-full bg-surface-container-high text-headline-md text-primary") {
                    Components.icon Shield
                }

                h2(class' = "text-headline-md text-on-surface") { t.HomeRunningHeading }
                p(class' = "text-body-md text-on-surface-variant") { t.HomeRunningBody }

                a(href = "/admin", class' = Components.btn Components.Primary) {
                    Components.icon Home
                    t.HomeAdminButton
                }
            }
        }
    }

let handler: EndpointHandler =
    fun ctx ->
        let t = Texts.current ()
        let origin = ctx.GetService<PublicOrigin.PublicOrigin>()
        ctx.WriteHtmlView(Layout.page ctx t t.HomeTitle (view t origin))

let endpoint = GET [ route "/" handler ]
