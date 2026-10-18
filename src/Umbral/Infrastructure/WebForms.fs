/// Reading a posted form, safely.
module Umbral.Infrastructure.WebForms

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Antiforgery
open Microsoft.AspNetCore.DataProtection
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection

/// Read a form after confirming its antiforgery token.
///
/// The two-step is not belt-and-braces, it is required. `UseAntiforgery()` has
/// usually validated already and recorded the verdict on the request; ASP.NET
/// then REFUSES to read a form whose token failed, throwing something that
/// reads like a parsing bug rather than a rejected token. So the verdict is
/// checked first, and the handler gets a clean exception it can turn into a 400.
///
/// Every POST calls this, including ones with no fields to read — the CSRF
/// check is the point, and the form contents are incidental.
let readValidated (ctx: HttpContext) : Task<IFormCollection> =
    task {
        match ctx.Features.Get<IAntiforgeryValidationFeature>() with
        | null ->
            // The middleware did not run for this endpoint. Validate directly
            // rather than assuming it was fine.
            do! ctx.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync ctx

        | feature when not feature.IsValid ->
            let reason =
                match feature.Error with
                | null -> "Antiforgery validation failed."
                | error -> error.Message

            raise (AntiforgeryValidationException reason)

        | _ -> ()

        return! ctx.Request.ReadFormAsync ctx.RequestAborted
    }

/// A field, trimmed, with absent and blank treated alike — a form always posts
/// every field it renders, so "" is what absence looks like on the wire.
let value (form: IFormCollection) (name: string) =
    match form.TryGetValue name with
    | true, values when values.Count > 0 ->
        match Option.ofObj values[0] with
        | Some v -> v.Trim()
        | None -> ""
    | _ -> ""

let optional (form: IFormCollection) (name: string) =
    match value form name with
    | "" -> None
    | v -> Some v

let checkbox (form: IFormCollection) (name: string) =
    match value form name with
    | "on"
    | "true"
    | "1" -> true
    | _ -> false

/// A redirect carrying a notice code, which the target screen turns into
/// localized text. POST then 302 then GET: a refresh must not repost, and the
/// message must not be assembled in the handler.
let redirectWithNotice (ctx: HttpContext) (path: string) (notice: string) : Task =
    ctx.Response.Redirect $"{path}?msg={Uri.EscapeDataString notice}"
    Task.CompletedTask

/// The same, plus what the notice is about (`slug=...`, `app=...`), so the
/// screen can recompute a warning and draw the button that confirms it. Values
/// are ids and slugs, never names or secrets.
let redirectWithNoticeAbout (ctx: HttpContext) (path: string) (notice: string) (about: (string * string) list) : Task =
    let extra =
        about
        |> List.map (fun (name, value) -> $"&{name}={Uri.EscapeDataString value}")
        |> String.concat ""

    ctx.Response.Redirect $"{path}?msg={Uri.EscapeDataString notice}{extra}"
    Task.CompletedTask

/// Free text that goes with a notice — what an OpenID Connect provider's
/// address did, the size of an image that was refused — carried in a
/// Data-Protected, single-use cookie rather than the query string. A screen that
/// drew a `reason=` from its URL would say whatever a link sent to the operator
/// told it to, under Umbral's name; the cookie can only have been written by
/// this server, for this notice about this slug or app.
///
/// What a notice is about is named by `about` (`slug` for a connector, `app` for
/// an app), and the cookie only answers a read that names the same thing.
module NoticeReason =
    [<Literal>]
    let private CookieName = "umbral_notice_reason"

    [<Literal>]
    let private Purpose = "Umbral.NoticeReason.v2"

    /// Long enough to survive the redirect.
    let private Lifetime = TimeSpan.FromMinutes 5.0

    let private protector (ctx: HttpContext) =
        ctx.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector Purpose

    let private cookieOptions (ctx: HttpContext) =
        let options = CookieOptions()
        options.HttpOnly <- true
        options.IsEssential <- true
        options.Path <- "/admin"
        options.SameSite <- SameSiteMode.Strict
        options.Secure <- ctx.Request.IsHttps
        options

    /// Write the reason for `notice` about `value` into the cookie. For a
    /// caller that builds its own redirect (one with a `#anchor`, say); the
    /// others use `redirect`.
    let stash (ctx: HttpContext) (notice: string) (about: string) (value: string) (reason: string) : unit =
        let payload = (protector ctx).Protect $"{notice}\u001f{about}\u001f{value}\u001f{reason}"
        let options = cookieOptions ctx
        options.MaxAge <- Lifetime
        ctx.Response.Cookies.Append(CookieName, payload, options)

    /// Redirect with the notice and what it is about in the query
    /// (`about=value`), the reason in the cookie.
    let redirect
        (ctx: HttpContext)
        (path: string)
        (notice: string)
        (about: string)
        (value: string)
        (reason: string)
        : Task =
        stash ctx notice about value reason
        redirectWithNoticeAbout ctx path notice [ about, value ]

    /// The reason stored for this notice about this slug or app, if there is
    /// one. The cookie is deleted on this read whatever it holds, so a refresh
    /// shows the notice's fixed text.
    let consume (ctx: HttpContext) (notice: string) (about: string) (value: string) : string option =
        let payload =
            match ctx.Request.Cookies.TryGetValue CookieName with
            | true, cookie -> Option.ofObj cookie |> Option.filter (fun v -> v.Length > 0)
            | _ -> None

        match payload with
        | Some payload ->
            ctx.Response.Cookies.Delete(CookieName, cookieOptions ctx)

            try
                match ((protector ctx).Unprotect payload).Split('\u001f', 4) with
                | [| n; a; v; reason |] when n = notice && a = about && v = value -> Some reason
                | _ -> None
            with _ ->
                None
        | _ -> None

    /// A reason made of named values, `width=40;height=20`. The names are ours
    /// and the values numbers or ids, so neither contains `=` or `;`.
    let encodePairs (pairs: (string * string) list) : string =
        pairs |> List.map (fun (name, value) -> $"{name}={value}") |> String.concat ";"

    /// The values of a reason written by `encodePairs`. Anything that is not a
    /// `name=value` pair is dropped.
    let parsePairs (reason: string) : Map<string, string> =
        reason.Split(';', StringSplitOptions.RemoveEmptyEntries)
        |> Array.choose (fun pair ->
            match pair.Split('=', 2) with
            | [| name; value |] -> Some(name, value)
            | _ -> None)
        |> Map.ofArray

/// Cut `text` to at most `limit` characters, for a value that travels in a
/// redirect, a cookie or a log detail and must not grow without bound.
let truncate (limit: int) (text: string) =
    if text.Length <= limit then text else text.Substring(0, limit - 1) + "…"

/// A path on this server, safe to redirect to when the value came from the
/// browser (a sign-in's `returnUrl`, the theme switch's return address): it
/// starts with `/`, the next character is not `/` or `\` (browsers read both as
/// the start of a host, `//evil.example` and `/\evil.example`), and every
/// character is printable ASCII. So no control character, which browsers strip
/// (`/\t/evil.example` would become `//evil.example`) and which splits a header,
/// no white space, and nothing the server would refuse to write into
/// `Location`. Umbral's own return addresses are percent-encoded, so a real one
/// never needs more.
let isLocalPath (candidate: string | null) : bool =
    match candidate with
    | null -> false
    | path ->
        path.Length > 0
        && path[0] = '/'
        && (path.Length = 1 || (path[1] <> '/' && path[1] <> '\\'))
        && path |> Seq.forall (fun c -> c > ' ' && c < '\u007f')

let redirect (ctx: HttpContext) (path: string) : Task =
    ctx.Response.Redirect path
    Task.CompletedTask
