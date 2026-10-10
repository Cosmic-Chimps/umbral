/// The end-session endpoint.
///
/// Ends Umbral's own session. It deliberately does NOT reach into the upstream
/// provider: signing out of Umbral must not sign someone out of Discord, which
/// would be a surprising thing for a login bridge to do to the rest of
/// someone's browser.
module Umbral.Handlers.Connect.EndSession

open Microsoft.AspNetCore
open Microsoft.AspNetCore.Authentication
open OpenIddict.Server.AspNetCore
open Oxpecker
open Umbral.Infrastructure

let handler: EndpointHandler =
    fun ctx ->
        task {
            let request = ctx.GetOpenIddictServerRequest()

            if isNull (box request) then
                failwith
                    "The OpenID Connect request could not be retrieved. The Oxpecker route and \
                     OpenIddictSetup's endpoint URI disagree."

            do! Sessions.signOut ctx

            // SignOut on OpenIddict's scheme is what validates and honours
            // post_logout_redirect_uri against what the app registered. Doing
            // the redirect by hand would skip that check and turn this endpoint
            // into an open redirect.
            return!
                ctx.SignOutAsync(
                    OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    AuthenticationProperties(RedirectUri = "/")
                )
        }

let endpoint =
    [ GET [ route "/connect/logout" handler ]
      POST [ route "/connect/logout" handler ] ]
