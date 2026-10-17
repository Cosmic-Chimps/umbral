/// Serves an app's logo and the accent stylesheet its sign-in page links
/// (spec 006, research R4 and R7).
///
/// No authentication, no antiforgery, no rate limit, like Handlers/Assets.fs:
/// the sign-in page needs them before anyone is signed in, and nothing they
/// serve is secret. What they must not do is say anything about which apps are
/// branded: a path that is not exactly the shape of an address is a plain 404,
/// and the accent sheet is a pure function of its path, so it is served for any
/// colour whether or not an app uses it.
module Umbral.Handlers.Branding

open System.Text
open System.Text.RegularExpressions
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Oxpecker
open Umbral.Domains.Branding
open Umbral.Infrastructure

/// A logo's address: the lowercase hex SHA-256 of its bytes. Anchored, and
/// checked before any lookup, so the database never sees another shape.
let private sha256Path = Regex("^[0-9a-f]{64}$", RegexOptions.Compiled)

/// An accent's address: six lowercase hex digits, the form `Accent` stores.
let private accentPath = Regex("^[0-9a-f]{6}$", RegexOptions.Compiled)

/// The part of a route value before `suffix`, or `None` when it does not end
/// in it. The route captures the whole segment, `{hash}.png`, because the
/// extension is part of what makes a path an address and a path that lacks it
/// is not one.
let private withoutSuffix (suffix: string) (segment: string) =
    if segment.EndsWith(suffix, System.StringComparison.Ordinal) then
        Some(segment.Substring(0, segment.Length - suffix.Length))
    else
        None

let private notFound: EndpointHandler =
    fun ctx ->
        ctx.SetStatusCode StatusCodes.Status404NotFound
        Task.CompletedTask

/// The rule's format version, in the ETag after the colour: bump it if the
/// body's shape changes, so a browser holding the old body revalidates. 2 is
/// spec 007's: the rule also sets the `primary-fill` properties.
[<Literal>]
let private AccentFormatVersion = 2

/// The one rule an accent sheet holds (contracts/public-endpoints.md). It
/// redefines `--color-primary` and `--color-on-primary` as well as the two
/// channel variables: `:root` defines them as `rgb(var(--color-primary-rgb))`,
/// but a custom property's `var()` is resolved on the element that declares it
/// and the result inherited, so without the redefinition every direct use of
/// `var(--color-primary)` (focus outline, caret, radio) would stay Umbral blue
/// under `[data-accent]`.
///
/// Spec 007 added the four `primary-fill` properties, which the primary button
/// and the pressed option of the theme control read. In Umbral's own themes they
/// differ from `primary` (gold fill in dark, near-black in light); under an
/// accent they are the accent too, in both themes, with the same text colour
/// that is legible on it (FR-016 holds per accent, not per background). The
/// rule never sets a `--mark-*` property: the Gnomon stays Umbral's (FR-005).
///
/// Scoped to an attribute, not `:root`, so the Apps screen can link the same
/// sheet to preview a button without recolouring its own interface (FR-005).
let private accentRule (accent: Accent) =
    let r, g, b = Accent.rgb accent

    let onAccent =
        match Accent.onAccent accent with
        | Black -> "0 0 0"
        | White -> "255 255 255"

    $"[data-accent=\"{Accent.hex accent}\"]{{--color-primary-rgb:{r} {g} {b};--color-on-primary-rgb:{onAccent};--color-primary:rgb({r} {g} {b});--color-on-primary:rgb({onAccent});--color-primary-fill-rgb:{r} {g} {b};--color-on-primary-fill-rgb:{onAccent};--color-primary-fill:rgb({r} {g} {b});--color-on-primary-fill:rgb({onAccent})}}"

/// Headers every answer from here carries, a 304 included, as Assets.fs does.
let private immutable (ctx: HttpContext) (etag: string) =
    ctx.Response.Headers.CacheControl <- "public, max-age=31536000, immutable"
    ctx.Response.Headers.ETag <- etag
    ctx.Response.Headers.XContentTypeOptions <- "nosniff"

let private accentSheet (segment: string) : EndpointHandler =
    fun ctx ->
        let parsed =
            withoutSuffix ".css" segment
            |> Option.filter accentPath.IsMatch
            |> Option.bind (fun hex -> Accent.parse ("#" + hex) |> Result.toOption)

        match parsed with
        | None -> notFound ctx
        | Some accent ->
            let etag = $"\"{Accent.hex accent}-{AccentFormatVersion}\""
            immutable ctx etag

            if Umbral.Handlers.Assets.matches etag ctx then
                ctx.SetStatusCode StatusCodes.Status304NotModified
                Task.CompletedTask
            else
                let body = Encoding.UTF8.GetBytes(accentRule accent)
                ctx.SetContentType "text/css; charset=utf-8"
                ctx.Response.ContentLength <- int64 body.Length
                ctx.Response.Body.WriteAsync(body, 0, body.Length)

let private logoImage (segment: string) : EndpointHandler =
    fun ctx ->
        task {
            let sha = withoutSuffix ".png" segment |> Option.filter sha256Path.IsMatch

            // Looked up before the ETag is compared: a logo that was replaced
            // or removed is gone, and a browser that cached it must be told so
            // (FR-020), not that it is unchanged.
            let! bytes =
                task {
                    match sha with
                    | None -> return None
                    | Some sha ->
                        let config = ctx.GetService<Database.DbConfig>()
                        let! found = BrandingStore.logoBytes config sha
                        return found |> Option.map (fun bytes -> sha, bytes)
                }

            match bytes with
            | None -> do! notFound ctx
            | Some(sha, bytes) ->
                let etag = $"\"{sha}\""
                immutable ctx etag
                let headers = ctx.Response.Headers
                // The bytes are one the operator uploaded and Umbral rewrote,
                // so they are not trusted to be harmless if opened on their
                // own: nothing may run, and nothing outside this origin may
                // embed them.
                headers.ContentSecurityPolicy <- "default-src 'none'; sandbox"
                headers["Cross-Origin-Resource-Policy"] <- "same-origin"

                if Umbral.Handlers.Assets.matches etag ctx then
                    ctx.SetStatusCode StatusCodes.Status304NotModified
                else
                    ctx.SetContentType "image/png"
                    ctx.Response.ContentLength <- int64 bytes.Length
                    do! ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length)
        }
        :> Task

let endpoint =
    GET [ routef "/branding/accent/{%s}" accentSheet
          routef "/branding/logo/{%s}" logoImage ]
