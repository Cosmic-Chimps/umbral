/// The person's own passkeys page (specs/008-passkey-connector,
/// contracts/ceremonies.md "The passkeys page"): the ways they can sign in,
/// each passkey with a remove button, and the form that adds one. Always the
/// plain chrome: the page belongs to the instance, not to an app.
module Umbral.Handlers.Passkeys.Account

open System
open System.Globalization
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Http
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Domains.Passkeys
open Umbral.Infrastructure
open Umbral.Infrastructure.SecurityEvents
open Umbral.Views

let private signInRedirect (ctx: HttpContext) : Task =
    ctx.Response.Redirect "/signin?returnUrl=%2Faccount%2Fpasskeys"
    Task.CompletedTask

/// The signed-in, enabled user, or None after sending the person to sign in.
/// A session whose user is gone or disabled is ended.
let private currentUser (ctx: HttpContext) : Task<Users.User option> =
    task {
        let config = ctx.GetService<Database.DbConfig>()
        let! session = ctx.AuthenticateAsync Sessions.Scheme

        let subject =
            (if session.Succeeded then Option.ofObj session.Principal else None)
            |> Option.bind Sessions.currentSubject

        match subject with
        | None ->
            do! signInRedirect ctx
            return None
        | Some id ->
            match! Users.findById config id with
            | Some user when user.IsEnabled -> return Some user
            | _ ->
                do! Sessions.signOut ctx
                do! signInRedirect ctx
                return None
    }

let private date (value: DateTimeOffset) =
    value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

let private passkeyCard (ctx: HttpContext) (t: Texts.Texts) (passkey: Passkeys.Passkey) =
    article(class' = "um-card space-y-2").data("passkey", passkey.Id) {
        h2(class' = "text-headline-md text-on-surface") { passkey.Nickname }

        p(class' = "text-body-md text-on-surface-variant") {
            t.PasskeyAddedLabel + " " + date passkey.CreatedUtc
        }

        p(class' = "text-body-md text-on-surface-variant") {
            t.PasskeyLastUsedLabel
            + " "
            + (match passkey.LastUsedUtc with
               | Some used -> date used
               | None -> t.PasskeyNeverUsed)
        }

        (form(method = "post", action = $"/account/passkeys/{Uri.EscapeDataString passkey.Id}/remove") {
            ctx.GetAntiforgeryInput()

            button(type' = "submit", class' = Components.btnSm Components.Secondary) { t.PasskeyRemoveButton }
        })
        |> Components.confirmSubmit t.PasskeyRemoveConfirm
    }

let private view
    (ctx: HttpContext)
    (t: Texts.Texts)
    (waysIn: string)
    (passkeys: Passkeys.Passkey list)
    (connectorEnabled: bool)
    =
    div(class' = "mx-auto w-full max-w-2xl space-y-5").data("screen", "passkeys") {
        Components.pageHeading t.PasskeysTitle (Some t.PasskeysLead)
        SignIn.noticeAlert ctx t

        p(class' = "text-body-md text-on-surface").data("ways-in", "") { t.PasskeysWaysIn waysIn }

        for passkey in passkeys do
            passkeyCard ctx t passkey

        if not connectorEnabled then
            p(class' = "text-body-md text-on-surface-variant").data("passkeys-not-enabled", "") {
                t.PasskeysNotEnabled
            }
        elif passkeys.Length >= MaxPerUser then
            p(class' = "text-body-md text-on-surface-variant").data("passkeys-limit", "") { t.PasskeysLimitReached }
        else
            form(method = "post", class' = "space-y-4").data("passkey-add", "") {
                ctx.GetAntiforgeryInput()
                SignIn.nicknameField t

                button(type' = "submit", class' = Components.btn Components.Primary) { t.PasskeyAddButton }
            }

        SignIn.javascriptNote t
    }

let page: EndpointHandler =
    fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let t = Texts.current ()

            match! currentUser ctx with
            | None -> return ()
            | Some user ->
                let! passkeys = Passkeys.listForUser config user.Id
                let! kinds = UserStore.linkedKinds config user.Id
                let! connector = ConnectorStore.findBySlug config "passkey"

                let names =
                    kinds
                    |> List.choose (fun kind ->
                        Umbral.Domains.Connectors.parseKind kind |> Option.map _.DisplayName)
                    |> List.distinct

                let names =
                    if passkeys.IsEmpty then
                        names
                    else
                        names @ [ Umbral.Domains.Connectors.Passkey.DisplayName ]

                let enabled = connector |> Option.exists _.IsEnabled

                return!
                    ctx.WriteHtmlView(
                        view ctx t (String.Join(", ", names)) passkeys enabled
                        |> Layout.pageWithScripts [ Assets.passkeys ] ctx t t.PasskeysTitle
                    )
        }

let remove (id: string) : EndpointHandler =
    fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let! _ = WebForms.readValidated ctx

            match! currentUser ctx with
            | None -> return ()
            | Some user ->
                let! outcome = Passkeys.removeOwnIfNotLast config user.Id id

                let notice =
                    match outcome with
                    | Passkeys.Removed -> "passkey-removed"
                    | Passkeys.LastWayIn -> "passkey-last-cannot-remove"
                    | Passkeys.NotFound -> "passkey-not-found"

                if outcome = Passkeys.Removed then
                    do!
                        record
                            config
                            (ctx.GetService<TimeProvider>())
                            { event PasskeyRemoved Success Actor.User with
                                UserId = Some user.Id
                                ConnectorSlug = Some "passkey"
                                RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string
                                Detail = Some "by=user" }

                return! WebForms.redirectWithNotice ctx "/account/passkeys" notice
        }

let endpoint =
    [ GET [ route "/account/passkeys" page ]
      POST [ routef "/account/passkeys/{%s}/remove" remove ] ]
