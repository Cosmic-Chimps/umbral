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
/// path on this server is followed (`isLocalPath`), anything else goes home. It
/// is deliberately not `Picker.sanitizeReturnUrl`, which has its own contract.
module Umbral.Handlers.ThemeChoice

open System
open Microsoft.AspNetCore.Http
open Oxpecker
open Umbral.Infrastructure
open Umbral.Views

/// A year: long enough that the choice is not asked again, short enough that a
/// forgotten browser does not hold a preference forever.
let private Lifetime = TimeSpan.FromDays 365.0

/// A path on this server: it starts with `/`, the next character is not `/` or
/// `\` (browsers read both as the start of a host, `//evil.example` and
/// `/\evil.example`), and every character is printable ASCII: no control
/// character or white space, which is how a header is split, and nothing the
/// server would refuse to write into `Location` (the control sends the path
/// encoded, so a real page never needs more).
let isLocalPath (candidate: string) : bool =
    not (String.IsNullOrEmpty candidate)
    && candidate[0] = '/'
    && (candidate.Length = 1 || (candidate[1] <> '/' && candidate[1] <> '\\'))
    && candidate |> Seq.forall (fun c -> c > ' ' && c < '\u007f')

/// The cookie's options. Path `/` so every page sees it, `Lax` so it is sent
/// when someone follows a link here, `HttpOnly` because no script of ours reads
/// it, `Secure` whenever the request was HTTPS.
let private cookieOptions (ctx: HttpContext) (lifetime: TimeSpan option) =
    let options = CookieOptions()
    options.Path <- "/"
    options.SameSite <- SameSiteMode.Lax
    options.HttpOnly <- true
    options.IsEssential <- true
    options.Secure <- ctx.Request.IsHttps
    lifetime |> Option.iter (fun l -> options.MaxAge <- l)
    options

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
            | Some Theme.Light -> ctx.Response.Cookies.Append(Theme.ThemeCookie, "light", cookieOptions ctx (Some Lifetime))
            | Some Theme.Dark -> ctx.Response.Cookies.Append(Theme.ThemeCookie, "dark", cookieOptions ctx (Some Lifetime))
            | Some Theme.System -> ctx.Response.Cookies.Delete(Theme.ThemeCookie, cookieOptions ctx None)
            | None -> ()

            let returnUrl = posted "returnUrl"
            let target = if isLocalPath returnUrl then returnUrl else "/"

            ctx.Response.StatusCode <- StatusCodes.Status303SeeOther
            ctx.Response.Headers.Location <- target
        }

let endpoint = POST [ route "/theme" handler ]
