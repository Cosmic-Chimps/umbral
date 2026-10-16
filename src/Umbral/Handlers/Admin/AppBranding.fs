/// The operator's branding forms on the Apps screen: set or clear an app's
/// accent colour, upload, replace or remove its logo (spec 006, research R3).
///
/// Every route goes through `requireOperator` and `WebForms.readValidated`, looks
/// the app up by client id, changes nothing when it refuses, and ends in a
/// redirect back to that app's card (`/admin/apps?msg=…&app=…#app-…`), so the
/// operator lands where they were. A refused form is a notice, not a security
/// event: only a change is logged (research R9).
module Umbral.Handlers.Admin.AppBranding

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open OpenIddict.Abstractions
open Oxpecker
open Umbral.Domains
open Umbral.Domains.Branding
open Umbral.Infrastructure

/// Back to the card of `clientId`, with the notice. The client id is an
/// OpenIddict id and the app was found by it, but it travels in a URL, so it is
/// escaped like any value.
let private redirectToCard (ctx: HttpContext) (notice: string) (clientId: string) =
    let id = Uri.EscapeDataString clientId
    ctx.Response.Redirect $"/admin/apps?msg={Uri.EscapeDataString notice}&app={id}#app-{id}"
    System.Threading.Tasks.Task.CompletedTask

/// A branding change in the security log: an app was updated, by the operator,
/// and `detail` says which part.
let private recordChange (ctx: HttpContext) (clientId: string) (detail: string) =
    let config = ctx.GetService<Database.DbConfig>()
    let clock = ctx.GetService<TimeProvider>()

    SecurityEvents.record
        config
        clock
        { SecurityEvents.event SecurityEvents.AppUpdated SecurityEvents.Success SecurityEvents.Actor.Operator with
            ClientId = Some clientId
            RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string
            Detail = Some detail }

/// Set an app's accent colour. The form field is `#rrggbb`, either case; it is
/// stored lowercase without the `#`. A colour that is hard to see on the card is
/// saved all the same and the notice says so (FR-017): it is the operator's
/// choice, and Umbral keeps the button text readable whatever they pick.
///
/// An empty field is refused for now. Clearing the colour is a later story.
let setAccent (clientId: string) : EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! form = WebForms.readValidated ctx

            let applications = ctx.GetService<IOpenIddictApplicationManager>()
            let! application = applications.FindByClientIdAsync clientId

            match application with
            | null -> return! WebForms.redirectWithNotice ctx "/admin/apps" "app-not-found"
            | _ ->

            match Accent.parse (WebForms.value form "accent") with
            | Error NotHexColour -> return! redirectToCard ctx "app-accent-invalid" clientId
            | Ok accent ->

            do! BrandingStore.setAccent config (clock.GetUtcNow()) clientId (Some accent)
            do! recordChange ctx clientId "branding_accent_set"

            let notice =
                if Accent.lowContrastOnCard accent then
                    "app-accent-low-contrast"
                else
                    "app-accent-saved"

            return! redirectToCard ctx notice clientId
        })

// ---------------------------------------------------------------------------
// The logo
// ---------------------------------------------------------------------------

/// The request-size limit of the logo upload, and the most a form body may buffer
/// for it: 3 MB. The stored logo is at most 1.25 MB (`PngLogo.maxBytes`), and the
/// admin script sends a PNG of a few hundred KB; this is room for a no-script
/// upload that is over the limit, which the server then refuses with a notice
/// rather than a bare 413 (research R3).
[<Literal>]
let private RequestLimitBytes = 3145728L

/// Where a refusal that carries a measured value goes: the value in the
/// single-use cookie (never the query), the notice and the app in the redirect.
let private refuseWith (ctx: HttpContext) (clientId: string) (notice: string) (values: (string * string) list) =
    WebForms.NoticeReason.stash ctx notice "app" clientId (WebForms.NoticeReason.encodePairs values)
    redirectToCard ctx notice clientId

/// What each way a logo can be wrong says, and what it carries.
let private refusal (ctx: HttpContext) (clientId: string) (uploadedBytes: int64) (error: LogoError) =
    match error with
    | Empty -> redirectToCard ctx "app-logo-missing" clientId
    | NotPng -> redirectToCard ctx "app-logo-not-png" clientId
    | Corrupt
    | UnsupportedChunk _ -> redirectToCard ctx "app-logo-corrupt" clientId
    | Interlaced -> redirectToCard ctx "app-logo-interlaced" clientId
    | TooSmall(width, height) ->
        refuseWith ctx clientId "app-logo-too-small" [ "width", string width; "height", string height ]
    | TooBig(width, height) ->
        refuseWith ctx clientId "app-logo-too-big" [ "width", string width; "height", string height ]
    // `validate` saw the bytes it was given, which are capped; the size the
    // operator sent is the file's own length.
    | TooLarge _ -> refuseWith ctx clientId "app-logo-too-large" [ "bytes", string uploadedBytes ]

/// At most `limit` bytes of a file, so a file of any size costs this much
/// memory and no more.
let private readAtMost (limit: int) (file: IFormFile) =
    task {
        use stream = file.OpenReadStream()
        let buffer = Array.zeroCreate<byte> limit
        let mutable total = 0
        let mutable reading = true

        while reading && total < limit do
            let! read = stream.ReadAsync(buffer, total, limit - total)
            if read = 0 then reading <- false else total <- total + read

        return buffer[0 .. total - 1]
    }

/// Upload, or replace, an app's logo. Only a PNG that `PngLogo.validate` accepts
/// is stored, and what is stored is the file it wrote, never the upload.
let setLogo (clientId: string) : EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()

            let! formRead =
                task {
                    try
                        // The antiforgery middleware is switched off for this
                        // route (see `endpoint`), so this is where the body is
                        // read and the token checked, and where a body that is
                        // too large can be caught.
                        let! form = WebForms.readValidated ctx
                        return Ok form
                    with
                    // Over the size limit: Kestrel's `BadHttpRequestException`
                    // (413) once the body passes the request limit, or the form
                    // reader's `InvalidDataException` once a file passes the
                    // multipart limit. Antiforgery validation reads the body to
                    // find the token and wraps whatever that throws in an
                    // `AntiforgeryValidationException`, keeping it as the inner
                    // exception, so that is where each is found. Either is the
                    // operator sending too much, and the answer is the notice
                    // and not an error page. A token that is wrong is an
                    // `AntiforgeryValidationException` with no such inner
                    // exception, and goes on to the 400.
                    | :? BadHttpRequestException
                    | :? InvalidDataException -> return Error()
                    | :? Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException as e when
                        (e.InnerException :? BadHttpRequestException || e.InnerException :? InvalidDataException) ->
                        return Error()
                }

            match formRead with
            | Error() -> return! redirectToCard ctx "app-logo-too-large" clientId
            | Ok form ->

            let applications = ctx.GetService<IOpenIddictApplicationManager>()
            let! application = applications.FindByClientIdAsync clientId

            match application with
            | null -> return! WebForms.redirectWithNotice ctx "/admin/apps" "app-not-found"
            | _ ->

            match form.Files.GetFile "logo" with
            | null -> return! redirectToCard ctx "app-logo-missing" clientId
            | file when file.Length = 0L -> return! redirectToCard ctx "app-logo-missing" clientId
            | file ->

            // One byte over the limit, so a file over it is seen to be over
            // without reading any more of it.
            let! bytes = readAtMost (PngLogo.maxBytes + 1) file

            match PngLogo.validate bytes with
            | Error error -> return! refusal ctx clientId file.Length error
            | Ok logo ->

            do! BrandingStore.setLogo config (clock.GetUtcNow()) clientId logo
            do! recordChange ctx clientId "branding_logo_set"
            return! redirectToCard ctx "app-logo-saved" clientId
        })

let endpoint =
    [ POST [ routef "/admin/apps/{%s}/branding/accent" setAccent ]
      POST [
          routef "/admin/apps/{%s}/branding/logo" setLogo
          |> configureEndpoint (fun builder ->
              // `DisableAntiforgery` stops the middleware from reading this body
              // before the handler can catch what the read throws: a body over
              // the limit would otherwise leave the middleware as a 400 expired
              // form (the multipart limit) or a bare 413 (Kestrel's), neither
              // of which says what is wrong. The handler's `readValidated`
              // does the same check, and CsrfTests holds this route to it
              // like every other.
              builder.DisableAntiforgery().WithMetadata(
                  RequestSizeLimitAttribute RequestLimitBytes,
                  RequestFormLimitsAttribute(
                      MemoryBufferThreshold = int RequestLimitBytes,
                      MultipartBodyLengthLimit = RequestLimitBytes
                  )
              ))
      ] ]
