/// Reading a posted form, safely.
module Umbral.Infrastructure.WebForms

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Antiforgery
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

let redirect (ctx: HttpContext) (path: string) : Task =
    ctx.Response.Redirect path
    Task.CompletedTask
