/// `POST /theme`: the theme switch's endpoint (specs/007-umbra-look, research R5,
/// contracts/theme.md).
///
/// The choice is one cookie on this browser and nothing on the server: no
/// account, no session, so it needs no operator, and the person on a sign-in
/// page can use it. It is still a POST that changes state, so it asks for the
/// antiforgery token like every other (a page on another site must not be able
/// to flip someone's theme), and it answers a redirect, so a refresh cannot
/// repost it.
///
/// Where it redirects to is posted by the browser, so it is checked here: only a
/// path on this server is followed (`WebForms.isLocalPath`, the rule the sign-in
/// page's `returnUrl` follows too), anything else goes home.
module Umbral.Handlers.ThemeChoice

open System
open Microsoft.AspNetCore.Http
open Oxpecker
open Umbral.Infrastructure
open Umbral.Views

let handler: EndpointHandler =
    fun ctx ->
        task {
            let! form = WebForms.readValidated ctx

            // Both fields are read as posted: `WebForms.value` trims, which is right
            // for a name and wrong here, where " light" is not a choice and
            // "/a " is not a path.
            let posted (name: string) =
                match form.TryGetValue name with
                | true, values when values.Count > 0 -> Option.ofObj values[0] |> Option.defaultValue ""
                | _ -> ""

            // An unknown choice, like a missing one, changes nothing.
            match Theme.ThemeChoice.parseChoice (posted "theme") with
            | Some Theme.Light ->
                ctx.Response.Cookies.Append(Theme.ThemeCookie, "light", BrowserCookies.options ctx (Some BrowserCookies.Year))
            | Some Theme.Dark ->
                ctx.Response.Cookies.Append(Theme.ThemeCookie, "dark", BrowserCookies.options ctx (Some BrowserCookies.Year))
            | Some Theme.System -> ctx.Response.Cookies.Delete(Theme.ThemeCookie, BrowserCookies.options ctx None)
            | None -> ()

            let returnUrl = posted "returnUrl"
            let target = if WebForms.isLocalPath returnUrl then returnUrl else "/"

            ctx.Response.StatusCode <- StatusCodes.Status303SeeOther
            ctx.Response.Headers.Location <- target
        }

let endpoint = POST [ route "/theme" handler ]
