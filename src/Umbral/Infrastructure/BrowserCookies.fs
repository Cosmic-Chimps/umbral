/// The small cookies Umbral keeps in a browser besides the session: the theme
/// (specs/007-umbra-look) and the connector last used to sign in (specs/009).
/// They share one set of options so a cookie is never more or less strict than
/// the one next to it.
module Umbral.Infrastructure.BrowserCookies

open System
open Microsoft.AspNetCore.Http

/// A year: long enough that the choice is not asked again, short enough that a
/// forgotten browser does not hold a preference forever.
let Year = TimeSpan.FromDays 365.0

/// The cookie's options. Path `/` so every page sees it, `Lax` so it is sent
/// when someone follows a link here, `HttpOnly` because no script of ours reads
/// it, `Secure` whenever the request was HTTPS.
let options (ctx: HttpContext) (lifetime: TimeSpan option) : CookieOptions =
    let options = CookieOptions()
    options.Path <- "/"
    options.SameSite <- SameSiteMode.Lax
    options.HttpOnly <- true
    options.IsEssential <- true
    options.Secure <- ctx.Request.IsHttps
    lifetime |> Option.iter (fun l -> options.MaxAge <- l)
    options

/// The cookie that remembers which connector this browser last signed in with.
let LastConnectorCookie = "umbral_last_connector"

/// Remember that this browser just signed in through the connector `slug`.
///
/// The cookie holds the slug and nothing about the person: the sign-in page is
/// public, so anyone at a shared computer sees whatever it shows. It is a hint,
/// not a credential, so it is not signed: a forged value only moves a button.
let rememberConnector (ctx: HttpContext) (slug: string) : unit =
    ctx.Response.Cookies.Append(LastConnectorCookie, slug, options ctx (Some Year))

/// The slug this browser last signed in with, if the request carries one.
let lastConnector (ctx: HttpContext) : string option =
    match ctx.Request.Cookies.TryGetValue LastConnectorCookie with
    | true, value when not (String.IsNullOrEmpty value) -> Some(string value)
    | _ -> None
