/// The Apps screen (T072-T074).
///
/// An App is an OIDC client, stored entirely by OpenIddict — Umbral owns no
/// row for it. The screen's one job is to keep an operator from building a
/// half-working client by hand: permissions come from an app-type choice, a
/// redirect URI is checked before OpenIddict ever sees it, and the generated
/// secret is shown exactly once because OpenIddict hashes it on write and
/// cannot give it back.
module Umbral.Handlers.Admin.Apps

open System
open Microsoft.AspNetCore.DataProtection
open System.Collections.Concurrent
open Microsoft.AspNetCore.Http
open OpenIddict.Abstractions
open type OpenIddict.Abstractions.OpenIddictConstants
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Infrastructure
open Umbral.Views

// ---------------------------------------------------------------------------
// Strings this screen needs that are not in Views/Texts.fs.
//
// Texts.fs is owned by someone else in this change; these are placeholders so
// the gap is obvious and greppable (reported back, per the brief).
// ---------------------------------------------------------------------------

[<Literal>]
let private AppSecretNotIssued =
    "This is a public client. It has no client secret — only a confidential (\"Web application\") app gets one."

[<Literal>]
let private AppNotFoundNotice = "That app doesn't exist, or has already been deleted."

[<Literal>]
let private AppTypeInvalidNotice = "Choose an app type from the list, then save again."

// ---------------------------------------------------------------------------
// The shown-once secret.
//
// The contract requires the secret to appear immediately after creation, and
// rule 3 requires every POST to end in a redirect rather than a rendered
// response. Both are true at once by stashing the plaintext in memory under a
// single-use token, redirecting with that token in the query string, and
// having the GET consume (remove) it on the very read that shows it — a
// refresh of the same URL then shows nothing, which is what "not on any later
// page load" means in practice.
// ---------------------------------------------------------------------------

/// Not private: AppSecret.fs stashes a regenerated secret through this same
/// single-use store, so a rotation is shown exactly once the same way a
/// creation is.
type RevealedSecret = { ClientId: string; Secret: string }

/// Carried in a Data-Protected, single-use cookie rather than a token in the
/// query string.
///
/// A URL is the wrong place for a capability that reveals a credential: query
/// strings land in access logs, browser history and `Referer` headers sent to
/// whatever the operator clicks next. The cookie never leaves the browser that
/// created the app, is encrypted at rest and in transit, is deleted on the read
/// that shows it, and expires on its own if the operator closes the tab —
/// which also means no server-side table of plaintext secrets waiting for a
/// page load that may never come.
module SecretReveal =
    [<Literal>]
    let private CookieName = "umbral_app_secret"

    [<Literal>]
    let private Purpose = "Umbral.AppSecretReveal.v1"

    /// Long enough to survive the redirect, short enough that a secret is not
    /// sitting in a cookie jar while the operator goes to lunch.
    let private Lifetime = TimeSpan.FromMinutes 5.0

    let private protector (ctx: HttpContext) =
        ctx.GetService<IDataProtectionProvider>().CreateProtector Purpose

    let stash (ctx: HttpContext) (clientId: string) (secret: string) =
        let payload = (protector ctx).Protect $"{clientId}\u001f{secret}"

        let options = CookieOptions()
        options.HttpOnly <- true
        options.IsEssential <- true
        options.Path <- "/admin"
        options.SameSite <- SameSiteMode.Strict
        options.Secure <- ctx.Request.IsHttps
        options.MaxAge <- Lifetime

        ctx.Response.Cookies.Append(CookieName, payload, options)

    let consume (ctx: HttpContext) : RevealedSecret option =
        let payload =
            match ctx.Request.Cookies.TryGetValue CookieName with
            | true, value ->
                match Option.ofObj value with
                | Some v when v.Length > 0 -> Some v
                | _ -> None
            | _ -> None

        match payload with
        | None -> None
        | Some payload ->

        // Deleted before it is decoded, so a refresh shows nothing whether or
        // not the decode succeeds.
        let options = CookieOptions()
        options.Path <- "/admin"
        options.SameSite <- SameSiteMode.Strict
        options.Secure <- ctx.Request.IsHttps
        ctx.Response.Cookies.Delete(CookieName, options)

        try
            match ((protector ctx).Unprotect payload).Split '\u001f' with
            | [| clientId; secret |] -> Some { ClientId = clientId; Secret = secret }
            | _ -> None
        with _ ->
            None

// ---------------------------------------------------------------------------
// App type -> permissions
// ---------------------------------------------------------------------------

type private AppType =
    | Web
    | SpaOrNative

    member this.Value =
        match this with
        | Web -> "web"
        | SpaOrNative -> "spa"

    member this.Label(t: Texts.Texts) =
        match this with
        | Web -> t.AppTypeWebOption
        | SpaOrNative -> t.AppTypeSpaOption

    member this.Description(t: Texts.Texts) =
        match this with
        | Web -> t.AppTypeWebDescription
        | SpaOrNative -> t.AppTypeSpaDescription

    static member Parse(value: string) =
        match value with
        | "web" -> Some Web
        | "spa" -> Some SpaOrNative
        | _ -> None

let private allAppTypes = [ Web; SpaOrNative ]

let private buildDescriptor
    (clientId: string)
    (name: string)
    (appType: AppType)
    (redirectUri: Uri)
    (postLogoutRedirectUri: Uri option)
    (secret: string option)
    =
    AppClients.buildDescriptor
        clientId
        name
        (appType = Web)
        [ redirectUri ]
        (Option.toList postLogoutRedirectUri)
        secret

// ---------------------------------------------------------------------------
// Redirect URI validation (T072)
//
// "Exact, absolute, and must reject wildcards" — checked here, before
// OpenIddict ever sees the value, so the rejection names the offending value
// rather than surfacing as an opaque mismatch at the authorize step.
// ---------------------------------------------------------------------------

/// The SAME rule the configuration file uses. It lived here as its own copy
/// until the copy in Domains.ConfigDocument grew a case this one lacked: on
/// Unix "/callback" parses as an absolute file:// uri and sailed through.
/// One rule, one place.
let private parseStrictUri (candidate: string) : Uri option =
    match Umbral.Domains.ConfigDocument.acceptableRedirectUri candidate with
    | Ok uri -> Some uri
    | Error _ -> None

// ---------------------------------------------------------------------------
// One app row
// ---------------------------------------------------------------------------

type private Row =
    { ClientId: string
      DisplayName: string
      ClientType: string
      RedirectUris: string list
      PostLogoutRedirectUris: string list }

let private appRow (ctx: HttpContext) (t: Texts.Texts) (origin: PublicOrigin.PublicOrigin) (row: Row) =
    let isPublic = row.ClientType = ClientTypes.Public

    article(class' = "um-card space-y-4").data("app", row.ClientId) {
        div(class' = "flex items-center justify-between gap-3") {
            h2(class' = "text-headline-md text-on-surface") { row.DisplayName }
            Components.pill Components.MutedTone (if isPublic then SpaOrNative.Label t else Web.Label t)
        }

        div(class' = "grid gap-3 sm:grid-cols-2") {
            Components.copyField
                t.Copy
                t.Copied
                t.AppClientIdLabel
                $"client-id-{row.ClientId}"
                row.ClientId
                ignore

            Components.copyField
                t.Copy
                t.Copied
                t.AppIssuerUrlLabel
                $"issuer-{row.ClientId}"
                origin.Text
                ignore
        }

        div(class' = "space-y-1 text-body-sm text-on-surface-variant") {
            for uri in row.RedirectUris do
                p(class' = "um-mono") { uri }
        }

        div(class' = "flex flex-wrap items-center gap-2") {
            if not isPublic then
                (form(method = "post", action = $"/admin/apps/{row.ClientId}/secret", class' = "inline") {
                    ctx.GetAntiforgeryInput()

                    button(type' = "submit", class' = Components.btnSm Components.Secondary) {
                        Components.icon Theme.Key
                        t.AppRegenerateButton
                    }
                })
                |> Components.confirmSubmit (t.AppRegenerateConfirm row.DisplayName)
                :> HtmlElement
            else
                Fragment() :> HtmlElement

            (form(method = "post", action = $"/admin/apps/{row.ClientId}/delete", class' = "inline") {
                ctx.GetAntiforgeryInput()

                button(type' = "submit", class' = Components.btnSm Components.Danger) {
                    Components.icon Theme.DeleteForever
                    t.AppDeleteButton
                }
            })
            |> Components.confirmSubmit (t.AppDeleteConfirm row.DisplayName)
        }
    }

// ---------------------------------------------------------------------------
// The shown-once secret card
// ---------------------------------------------------------------------------

let private secretRevealCard (t: Texts.Texts) (revealed: RevealedSecret) =
    div(class' = "um-card space-y-4 border-2 border-primary").data("secret-once", "") {
        Components.alert Components.Warning t.AppSecretOnceWarning

        Components.copyField t.Copy t.Copied t.AppClientIdLabel "revealed-client-id" revealed.ClientId ignore

        Components.copyField t.Copy t.Copied t.AppSecretLabel "revealed-secret" revealed.Secret ignore
    }

// ---------------------------------------------------------------------------
// Screen
// ---------------------------------------------------------------------------

/// The app type as two radio cards rather than a `<select>`: the operator has
/// to decide between them and a drop-down hides the sentence that says which
/// one is theirs. Real radio inputs in a fieldset keep it a native, keyboard
/// and screen-reader friendly group; the card styling is CSS only (`.um-choice`).
let private appTypeChoices (t: Texts.Texts) (selected: AppType) =
    fieldset(class' = "um-choice-group").attr("aria-describedby", "appType-hint") {
        legend(class' = "um-label") { t.AppTypeLabel }

        div(class' = "grid gap-3 md:grid-cols-2") {
            for appType in allAppTypes do
                label(class' = "um-choice") {
                    let radio =
                        input(type' = "radio", name = "appType", value = appType.Value, class' = "um-choice-radio")

                    // `checked` is reserved in F#, so the boolean attribute is set by name.
                    if appType = selected then
                        radio.attr("checked", "checked") |> ignore

                    radio

                    span(class' = "um-choice-body") {
                        span(class' = "um-choice-title") { appType.Label t }
                        span(class' = "um-choice-desc") { appType.Description t }
                    }
                }
        }

        p(id = "appType-hint", class' = "um-hint") { t.AppTypeHint }
    }

let private createForm (ctx: HttpContext) (t: Texts.Texts) =
    form(method = "post", action = "/admin/apps", class' = "um-card space-y-4") {
        ctx.GetAntiforgeryInput()

        Components.field t.AppNameLabel "name" "" None None
        Components.field t.AppRedirectUriLabel "redirectUri" "" (Some t.AppRedirectUriHint) None
        Components.field t.AppPostLogoutRedirectUriLabel "postLogoutRedirectUri" "" None None

        appTypeChoices t Web

        button(type' = "submit", class' = Components.btn Components.Primary) {
            Components.icon Theme.Add
            t.AppCreateButton
        }
    }

let private view
    (ctx: HttpContext)
    (t: Texts.Texts)
    (origin: PublicOrigin.PublicOrigin)
    (rows: Row list)
    (hasEnabledConnector: bool)
    (revealed: RevealedSecret option)
    (notice: string option)
    =
    div(class' = "space-y-6").data("screen", "apps") {
        Components.pageHeading t.AppsTitle (Some t.AppsLead)

        match notice with
        | Some code -> (div().data("notice", code) { Components.alert Components.Success (t.Notice code) }) :> HtmlElement
        | None -> Fragment() :> HtmlElement

        match revealed with
        | Some r -> secretRevealCard t r
        | None -> Fragment() :> HtmlElement

        if not hasEnabledConnector then
            Components.emptyState
                Theme.Warning
                t.AppsNeedConnectorTitle
                t.AppsNeedConnectorBody
                (Some(a (href = "/admin/connectors", class' = Components.btn Components.Primary) { t.NavConnectors }))
        else
            createForm ctx t

        if rows.IsEmpty then
            Components.emptyState Theme.ExploreOff t.AppsEmptyTitle t.AppsEmptyBody None
        else
            for row in rows do
                appRow ctx t origin row
    }
    |> Layout.operatorPage ctx t t.AppsTitle

/// F#'s `task { for x in ... }` binds `seq<'T>`, not `IAsyncEnumerable<'T>`, so
/// OpenIddict's manager enumerables (backed by EF Core queries) are drained by
/// hand instead.
let private toListAsync (source: Collections.Generic.IAsyncEnumerable<'a>) =
    task {
        let enumerator = source.GetAsyncEnumerator()
        let results = ResizeArray<'a>()
        let mutable go = true

        while go do
            let! hasNext = enumerator.MoveNextAsync()
            if hasNext then results.Add enumerator.Current else go <- false

        do! enumerator.DisposeAsync()
        return List.ofSeq results
    }

let private toRow (applications: IOpenIddictApplicationManager) (app: obj) =
    task {
        let! clientId = applications.GetClientIdAsync app
        let! displayName = applications.GetDisplayNameAsync app
        let! clientType = applications.GetClientTypeAsync app
        let! redirectUris = applications.GetRedirectUrisAsync app
        let! postLogoutUris = applications.GetPostLogoutRedirectUrisAsync app

        return
            { ClientId = clientId |> Option.ofObj |> Option.defaultValue ""
              DisplayName = displayName |> Option.ofObj |> Option.defaultValue ""
              ClientType = clientType |> Option.ofObj |> Option.defaultValue ClientTypes.Confidential
              RedirectUris = redirectUris |> List.ofSeq
              PostLogoutRedirectUris = postLogoutUris |> List.ofSeq }
    }

let page: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let t = Texts.current ()
            let config = ctx.GetService<Database.DbConfig>()
            let origin = ctx.GetService<PublicOrigin.PublicOrigin>()
            let applications = ctx.GetService<IOpenIddictApplicationManager>()

            let! apps = toListAsync (applications.ListAsync())
            let rows = ResizeArray()

            for app in apps do
                let! row = toRow applications app
                rows.Add row

            let! connectors = ConnectorStore.listAll config
            let hasEnabledConnector = connectors |> List.exists (fun c -> c.IsEnabled)

            let notice = ctx.TryGetQueryValue "msg"
            let revealed = SecretReveal.consume ctx

            return! ctx.WriteHtmlView(view ctx t origin (List.ofSeq rows) hasEnabledConnector revealed notice)
        })

let create: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! form = WebForms.readValidated ctx

            let name = WebForms.value form "name"
            let redirectUriRaw = WebForms.value form "redirectUri"
            let postLogoutRaw = WebForms.optional form "postLogoutRedirectUri"
            let appTypeRaw = WebForms.value form "appType"

            let remoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string

            let fail notice =
                WebForms.redirectWithNotice ctx "/admin/apps" notice

            if name = "" then
                return! fail "app-name-required"
            else

            match parseStrictUri redirectUriRaw with
            | None -> return! fail "app-redirect-uri-invalid"
            | Some redirectUri ->

            let postLogoutResult =
                match postLogoutRaw with
                | None -> Ok None
                | Some raw ->
                    match parseStrictUri raw with
                    | Some uri -> Ok(Some uri)
                    | None -> Error()

            match postLogoutResult with
            | Error() -> return! fail "app-redirect-uri-invalid"
            | Ok postLogoutUri ->

            match AppType.Parse appTypeRaw with
            | None -> return! fail "app-type-invalid"
            | Some appType ->

            let clientId = Umbral.Domains.Ids.newClientId ()

            let secret =
                match appType with
                | Web -> Some(Umbral.Domains.Ids.newClientSecret ())
                | SpaOrNative -> None

            let descriptor = buildDescriptor clientId name appType redirectUri postLogoutUri secret
            let applications = ctx.GetService<IOpenIddictApplicationManager>()
            let! _ = applications.CreateAsync descriptor

            do!
                SecurityEvents.record
                    config
                    clock
                    { SecurityEvents.event SecurityEvents.AppCreated SecurityEvents.Success SecurityEvents.Actor.Operator with
                        ClientId = Some clientId
                        RemoteIp = remoteIp }

            match secret with
            | Some s -> SecretReveal.stash ctx clientId s
            | None -> ()

            return! WebForms.redirectWithNotice ctx "/admin/apps" "app-created"
        })

let endpoint =
    [ GET [ route "/admin/apps" page ]
      POST [ route "/admin/apps" create ] ]
