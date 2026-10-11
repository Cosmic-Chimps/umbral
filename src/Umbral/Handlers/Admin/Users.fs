/// The Users screen (T083).
///
/// Read the contract's "The Users screen stays minimal" section before adding
/// anything here. It shows a display name, the providers a person has linked,
/// their first and last sign-in, and whether they are enabled — and nothing
/// Umbral does not actually hold: no IP addresses, no network segment, no
/// `@alias`, no "enrolled" count, no token introspection stream.
module Umbral.Handlers.Admin.Users

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Data.Sqlite
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Infrastructure
open Umbral.Views

// ---------------------------------------------------------------------------
// Listing and searching.
//
// Not in Infrastructure/Users.fs: that file (and UserStore.fs) are outside
// this change's file list — the brief names only findById, setEnabled and
// linkedKinds as what this screen reads from there — so the one query this
// screen needs lives beside the screen instead of adding an edit to a file
// this change is not scoped to touch.
// ---------------------------------------------------------------------------

let private readOptionalString (reader: SqliteDataReader) (ordinal: int) =
    if reader.IsDBNull ordinal then None else Some(reader.GetString ordinal)

let private readUser (reader: SqliteDataReader) : Umbral.Infrastructure.Users.User =
    { Id = reader.GetString 0
      DisplayName = reader.GetString 1
      Email = readOptionalString reader 2
      EmailVerified = reader.GetInt32 3 = 1
      AvatarUrl = readOptionalString reader 4
      IsEnabled = reader.GetInt32 5 = 1
      FirstSeenUtc = DateTimeOffset.Parse(reader.GetString 6, Globalization.CultureInfo.InvariantCulture)
      LastSeenUtc = DateTimeOffset.Parse(reader.GetString 7, Globalization.CultureInfo.InvariantCulture) }

/// Users matching a display-name-or-email search, most recent sign-in first.
let private search
    (config: Database.DbConfig)
    (term: string)
    : Task<Umbral.Infrastructure.Users.User list>
    =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "SELECT id, display_name, email, email_verified, avatar_url, is_enabled, first_seen_utc, last_seen_utc
             FROM users
             WHERE @term = ''
                OR display_name LIKE '%' || @term || '%' COLLATE NOCASE
                OR (email IS NOT NULL AND email LIKE '%' || @term || '%' COLLATE NOCASE)
             ORDER BY last_seen_utc DESC"

        command.Parameters.AddWithValue("@term", term) |> ignore

        use! reader = command.ExecuteReaderAsync()
        let results = ResizeArray()
        let mutable go = true

        while go do
            let! hasRow = reader.ReadAsync()
            if hasRow then results.Add(readUser reader) else go <- false

        return List.ofSeq results
    }

// ---------------------------------------------------------------------------
// One user row, shared with UserToggle.fs's htmx fragment response (D11: this
// is one of the three in-place interactions).
// ---------------------------------------------------------------------------

/// `LinkedProviders` is each linked account's kind, for the `data-linked-provider`
/// hook, and the label the screen shows for it (`linkedProviders`).
type Row =
    { User: Umbral.Infrastructure.Users.User
      LinkedProviders: (string * string) list }

let private providerLabel (kind: string) =
    Umbral.Domains.Connectors.parseKind kind
    |> Option.map (fun k -> k.DisplayName)
    |> Option.defaultValue kind

/// What to call a linked account on screen. A built-in kind is its own name. An
/// `oidc` account is the display name of the connector that holds its issuer —
/// "Home" and "Work" are two accounts, not two "OpenID Connect" — or, when that
/// connector has been deleted, the issuer's host, so the operator can still tell
/// where the person came from (data-model "Display").
let linkedLabel (connectors: Umbral.Domains.Connectors.Connector list) (kind: string) (subject: string) =
    match Umbral.Domains.Connectors.parseKind kind with
    | Some Umbral.Domains.Connectors.Oidc ->
        match Umbral.Domains.Connectors.parseOidcSubject subject with
        | Some(issuer, _) ->
            match connectors |> List.tryFind (fun c -> c.IssuerUrl = Some issuer) with
            | Some connector -> connector.DisplayName
            | None ->
                if Uri.IsWellFormedUriString(issuer, UriKind.Absolute) then
                    Uri(issuer).Host
                else
                    issuer
        | None -> providerLabel kind
    | _ -> providerLabel kind

/// A user's linked accounts as the screen's row wants them. Used by this screen
/// and by the user toggle's fragment, so both draw the same labels.
let linkedProviders (config: Database.DbConfig) (userId: string) : Task<(string * string) list> =
    task {
        let! accounts = UserStore.linkedAccounts config userId
        let! connectors = ConnectorStore.listAll config

        return accounts |> List.map (fun (kind, subject) -> kind, linkedLabel connectors kind subject)
    }

/// Both timestamps are always populated — a row only exists because someone
/// signed in — so there is no "never" case to render here, unlike the
/// optional hint in Texts.UserNeverSignedIn.
let private formatSignIn (value: DateTimeOffset) =
    value.ToString("yyyy-MM-dd HH:mm 'UTC'", Globalization.CultureInfo.InvariantCulture)

let row (ctx: HttpContext) (t: Texts.Texts) (data: Row) : HtmlElement =
    let user = data.User

    article(class' = "um-card space-y-3")
        .data("user", user.Id)
        .data("user-state", (if user.IsEnabled then "enabled" else "disabled")) {

        div(class' = "flex items-center justify-between gap-3") {
            div(class' = "space-y-1") {
                h2(class' = "text-headline-sm text-on-surface") { user.DisplayName }

                match user.Email with
                | Some email -> (p(class' = "text-body-sm text-on-surface-variant") { email }) :> HtmlElement
                | None -> Fragment() :> HtmlElement
            }

            Components.stateBadge user.IsEnabled t.UserEnabledBadge t.UserDisabledBadge
        }

        div(class' = "flex flex-wrap gap-1.5") {
            for kind, label in data.LinkedProviders do
                (Components.pill Components.PrimaryTone label).data ("linked-provider", kind)
        }

        div(class' = "grid gap-1 text-body-sm text-on-surface-variant sm:grid-cols-2") {
            span() { $"{t.UserFirstSignInLabel}: {formatSignIn user.FirstSeenUtc}" }
            span() { $"{t.UserLastSignInLabel}: {formatSignIn user.LastSeenUtc}" }
        }

        div(class' = "flex flex-wrap items-center gap-2") {
            (form(
                method = "post",
                action = $"/admin/users/{user.Id}/toggle",
                class' = "inline"
            ) {
                ctx.GetAntiforgeryInput()

                button(type' = "submit", class' = Components.btnSm Components.Secondary)
                    .attr("hx-post", $"/admin/users/{user.Id}/toggle")
                    .attr("hx-target", "closest article")
                    .attr("hx-swap", "outerHTML") {
                    Components.icon Theme.Power
                    if user.IsEnabled then t.UserDisableButton else t.UserEnableButton
                }
            })
            |> (if user.IsEnabled then
                    Components.confirmSubmit (t.UserDisableConfirm user.DisplayName)
                else
                    id)

            (form(method = "post", action = $"/admin/users/{user.Id}/delete", class' = "inline") {
                ctx.GetAntiforgeryInput()

                button(type' = "submit", class' = Components.btnSm Components.Danger) {
                    Components.icon Theme.DeleteForever
                    t.UserDeleteButton
                }
            })
            |> Components.confirmSubmit (t.UserDeleteConfirm user.DisplayName)
        }
    }

// ---------------------------------------------------------------------------
// Screen
// ---------------------------------------------------------------------------

let private searchForm (t: Texts.Texts) (term: string) =
    form(method = "get", action = "/admin/users", class' = "flex items-end gap-2") {
        div(class' = "flex-1") {
            label(for' = "search", class' = "um-label") { t.UserSearchLabel }

            input(
                type' = "search",
                id = "search",
                name = "search",
                value = term,
                class' = "um-input",
                placeholder = t.UserSearchPlaceholder
            )
        }

        button(type' = "submit", class' = Components.btn Components.Secondary) {
            Components.icon Theme.Search
            t.UserSearchLabel
        }
    }

let private view (ctx: HttpContext) (t: Texts.Texts) (rows: Row list) (term: string) (notice: string option) =
    div(class' = "space-y-6").data("screen", "users") {
        Components.pageHeading t.UsersTitle (Some t.UsersLead)

        match notice with
        | Some code -> (div().data("notice", code) { Components.alert Components.Success (t.Notice code) }) :> HtmlElement
        | None -> Fragment() :> HtmlElement

        searchForm t term

        if rows.IsEmpty then
            Components.emptyState Theme.Group t.UsersEmptyTitle t.UsersEmptyBody None
        else
            for data in rows do
                row ctx t data
    }
    |> Layout.operatorPage ctx t t.UsersTitle

let page: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let t = Texts.current ()
            let config = ctx.GetService<Database.DbConfig>()
            let term = ctx.TryGetQueryValue "search" |> Option.defaultValue ""
            let! users = search config term

            let rows = ResizeArray()

            for user in users do
                let! linked = linkedProviders config user.Id
                rows.Add { User = user; LinkedProviders = linked }

            let notice = ctx.TryGetQueryValue "msg" |> Option.bind Texts.knownNotice
            return! ctx.WriteHtmlView(view ctx t (List.ofSeq rows) term notice)
        })

let endpoint = GET [ route "/admin/users" page ]
