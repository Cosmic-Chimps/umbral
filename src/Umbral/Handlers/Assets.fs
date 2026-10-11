/// Serves the stylesheet and scripts every page loads (Views/Assets.fs).
///
/// No authentication, no antiforgery, no rate limit: the sign-in and setup pages
/// need them before anyone is signed in, and they are the same public bytes for
/// everyone.
///
/// Immutable for a year is safe because a page links to `Asset.Href`, whose
/// `?v=` is a hash of the content: a new image means a new URL. The ETag still
/// answers a revalidation (a hard reload) with a 304.
module Umbral.Handlers.Assets

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Oxpecker
open Umbral.Views

/// Whether `If-None-Match` names this ETag (or is `*`). The header may list
/// several, and a weak validator (`W/"..."`, which a proxy may add) counts.
let private matches (etag: string) (ctx: HttpContext) =
    ctx.Request.Headers.IfNoneMatch
    |> Seq.collect (fun header -> (string header).Split ',')
    |> Seq.map (fun candidate -> candidate.Trim())
    |> Seq.exists (fun candidate -> candidate = "*" || candidate = etag || candidate = "W/" + etag)

let private serve (asset: Assets.Asset) : EndpointHandler =
    fun ctx ->
        let headers = ctx.Response.Headers
        headers.CacheControl <- "public, max-age=31536000, immutable"
        headers.ETag <- asset.ETag
        headers.XContentTypeOptions <- "nosniff"

        if matches asset.ETag ctx then
            ctx.SetStatusCode StatusCodes.Status304NotModified
            Task.CompletedTask
        else
            ctx.SetContentType asset.ContentType
            ctx.Response.ContentLength <- int64 asset.Bytes.Length
            ctx.Response.Body.WriteAsync(asset.Bytes, 0, asset.Bytes.Length)

let endpoint = GET [ for asset in Assets.all -> route asset.Path (serve asset) ]
