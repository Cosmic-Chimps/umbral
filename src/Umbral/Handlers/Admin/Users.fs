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
      LastSeenUtc = DateTimeOffset.Parse(reader.GetString 7, Globalization.CultureInfo.InvariantCulture)
      // The admin list does not show them, so it does not select them.
      PreferredUsername = None
      ProfileUrl = None }

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

/// One linked provider account on the card (specs/018): its label, the opaque
/// fingerprint that names it in a form (never the subject), and its dates.
type LinkRow =
    { Kind: string
      Fingerprint: string
      Label: string
      LinkedUtc: DateTimeOffset
      LastUsedUtc: DateTimeOffset }

/// `LinkedProviders` is each linked account's kind, for the `data-linked-provider`
/// hook, and the label the screen shows for it (`linkedProviders`). `Links` is
/// the same accounts with their dates, for the list that removes one;
/// `ConfirmLink` is the fingerprint of the link whose confirm block is open, and
/// `Search` the term the page was drawn for, which the forms and Cancel keep.
type Row =
    { User: Umbral.Infrastructure.Users.User
      LinkedProviders: (string * string) list
      Links: LinkRow list
      Passkeys: Umbral.Infrastructure.Passkeys.Passkey list
      CanInvite: bool
      RevealedLink: string option
      ConfirmLink: string option
      Search: string }

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

/// A user's linked accounts with their dates and fingerprints (specs/018).
let private linkRows (config: Database.DbConfig) (userId: string) : Task<LinkRow list> =
    task {
        let! accounts = UserStore.linkedAccountDetails config userId
        let! connectors = ConnectorStore.listAll config

        return
            accounts
            |> List.map (fun a ->
                { Kind = a.Kind
                  Fingerprint = Umbral.Domains.Linking.fingerprint a.Kind a.Subject
                  Label = linkedLabel connectors a.Kind a.Subject
                  LinkedUtc = a.LinkedUtc
                  LastUsedUtc = a.LastUsedUtc })
    }

/// One user's row as the screen draws it. The user toggle's fragment and the
/// page both come through here, so they cannot disagree.
let loadRow
    (config: Database.DbConfig)
    (user: Umbral.Infrastructure.Users.User)
    (canInvite: bool)
    (revealed: string option)
    (confirmLink: string option)
    (term: string)
    : Task<Row> =
    task {
        let! linked = linkedProviders config user.Id
        let! links = linkRows config user.Id
        let! passkeys = Passkeys.listForUser config user.Id

        return
            { User = user
              LinkedProviders = linked
              Links = links
              Passkeys = passkeys
              CanInvite = canInvite
              RevealedLink = revealed
              ConfirmLink = confirmLink
              Search = term }
    }

/// Both timestamps are always populated — a row only exists because someone
/// signed in — so there is no "never" case to render here, unlike the
/// optional hint in Texts.UserNeverSignedIn.
let private formatSignIn (value: DateTimeOffset) =
    value.ToString("yyyy-MM-dd HH:mm 'UTC'", Globalization.CultureInfo.InvariantCulture)

let private day (value: DateTimeOffset) =
    value.ToString("yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture)

/// The passkeys that go with a link (specs/018 FR-003, data-model "For the
/// confirmation"): those added while signed in with it, then, transitively,
/// those added while signed in with one of those. The same rule the removal's
/// recursive delete applies, computed from the user's own passkeys.
let private goneWith
    (fingerprint: string)
    (passkeys: Umbral.Infrastructure.Passkeys.Passkey list)
    : Umbral.Infrastructure.Passkeys.Passkey list =
    let viaLink = Umbral.Domains.Linking.credentialValue (Umbral.Domains.Linking.LinkCredential fingerprint)

    let rec grow (gone: Set<string>) =
        let next =
            passkeys
            |> List.filter (fun pk ->
                not (gone.Contains pk.Id)
                && (match pk.AddedVia with
                    | Some via when via = viaLink -> true
                    | Some via ->
                        match Umbral.Domains.Linking.parseCredential via with
                        | Some(Umbral.Domains.Linking.PasskeyCredential id) -> gone.Contains id
                        | _ -> false
                    | None -> false))

        if next.IsEmpty then gone else grow (Set.union gone (next |> List.map (fun pk -> pk.Id) |> Set.ofList))

    let ids = grow Set.empty
    passkeys |> List.filter (fun pk -> ids.Contains pk.Id)

let private usersPath (term: string) =
    if term = "" then "/admin/users" else $"/admin/users?search={Uri.EscapeDataString term}"

/// The confirm block that takes the place of a row's button (specs/018 FR-002).
/// It is a group the browser focuses on load (`tabindex="-1"` makes the div
/// focusable, `autofocus` takes the focus, with and without script) and it is
/// named by its question.
let private linkConfirm (ctx: HttpContext) (t: Texts.Texts) (data: Row) (link: LinkRow) : HtmlElement =
    let user = data.User
    let gone = goneWith link.Fingerprint data.Passkeys
    let goneIds = gone |> List.map (fun pk -> pk.Id) |> Set.ofList

    let check =
        data.Passkeys
        |> List.filter (fun pk -> pk.AddedVia.IsNone && pk.CreatedUtc > link.LinkedUtc && not (goneIds.Contains pk.Id))

    let action = $"/admin/users/{user.Id}/links/{link.Fingerprint}/remove"
    let questionId = $"link-confirm-{link.Fingerprint}"

    let passkeyList (hook: string) (intro: string) (items: Umbral.Infrastructure.Passkeys.Passkey list) =
        div(class' = "space-y-1") {
            p(class' = "text-body-md text-on-surface-variant") { intro }

            ul(class' = "list-disc space-y-0.5 pl-5 text-body-md text-on-surface").data (hook, "") {
                for pk in items do
                    li().data ("passkey", pk.Id) { t.UserLinkPasskeyLine pk.Nickname (day pk.CreatedUtc) }
            }
        }

    div(class' = "w-full basis-full space-y-3 rounded-md border border-error py-3 px-3")
        .attr("role", "group")
        .attr("aria-labelledby", questionId)
        .attr("tabindex", "-1")
        .attr("autofocus", "")
        .data ("user-link-confirm", "") {
        p(id = questionId, class' = "text-body-md text-on-surface") { t.UserLinkRemoveQuestion link.Label user.DisplayName }

        if not gone.IsEmpty then
            passkeyList "user-link-gone-passkeys" t.UserLinkGoneIntro gone
        else
            Fragment() :> HtmlElement

        if not check.IsEmpty then
            passkeyList "user-link-check-passkeys" (t.UserLinkCheckIntro(day link.LinkedUtc)) check
        else
            Fragment() :> HtmlElement

        p(class' = "text-body-md text-on-surface") { t.UserLinkCannotUndo }

        div(class' = "flex flex-wrap items-center gap-2") {
            form(method = "post", action = action, class' = "inline") {
                ctx.GetAntiforgeryInput()
                input(type' = "hidden", name = "confirm", value = "remove")
                input(type' = "hidden", name = "search", value = data.Search)

                button(type' = "submit", class' = Components.btnSm Components.Danger) { t.UserLinkRemoveButton }
            }

            a(href = usersPath data.Search, class' = Components.btnSm Components.Ghost) { t.ConfirmCancel }
        }
    }

/// The list of linked accounts, one row each (specs/018 FR-001). Drawn only
/// when the user has two or more ways in, so a link can be removed.
let private linksList (ctx: HttpContext) (t: Texts.Texts) (data: Row) : HtmlElement =
    if data.Links.IsEmpty || data.Links.Length + data.Passkeys.Length < 2 then
        Fragment() :> HtmlElement
    else
        let user = data.User

        div(class' = "space-y-1") {
            p(class' = "um-label").data ("list-label", "links") { t.UserLinksLabel }

            ul(class' = "space-y-2 text-body-md text-on-surface-variant").data ("user-links", "") {
                for link in data.Links do
                    li(class' = "flex flex-wrap items-center gap-x-3 gap-y-2")
                        .data ("user-link", link.Fingerprint) {
                        span(class' = "text-on-surface") { link.Label }

                        span() { t.UserLinkDates (day link.LinkedUtc) (day link.LastUsedUtc) }

                        if data.ConfirmLink = Some link.Fingerprint then
                            linkConfirm ctx t data link
                        else
                            form(
                                method = "post",
                                action = $"/admin/users/{user.Id}/links/{link.Fingerprint}/remove",
                                class' = "inline"
                            ) {
                                ctx.GetAntiforgeryInput()
                                input(type' = "hidden", name = "search", value = data.Search)

                                button(
                                    type' = "submit",
                                    class' = Components.btnSm Components.Ghost + " text-error hover:text-error"
                                )
                                    .data ("user-link-remove", "") {
                                    t.UserLinkRemoveButton
                                }
                            }
                    }
            }
        }

/// The user's passkeys, one row each, under their label (specs/018 FR-001).
let private passkeysList (ctx: HttpContext) (t: Texts.Texts) (data: Row) : HtmlElement =
    let user = data.User

    if data.Passkeys.IsEmpty then
        Fragment() :> HtmlElement
    else
        div(class' = "space-y-1") {
            p(class' = "um-label").data ("list-label", "passkeys") { t.UserPasskeysLabel }

            ul(class' = "space-y-1 text-body-md text-on-surface-variant").data ("user-passkeys", "") {
                for pk in data.Passkeys do
                    li(class' = "flex flex-wrap items-center gap-2").data ("passkey", pk.Id) {
                        span(class' = "text-on-surface") { pk.Nickname }

                        span() { $"{t.PasskeyAddedLabel}: {day pk.CreatedUtc}" }

                        span() {
                            match pk.LastUsedUtc with
                            | Some d -> $"{t.PasskeyLastUsedLabel}: {day d}"
                            | None -> $"{t.PasskeyLastUsedLabel}: {t.PasskeyNeverUsed}"
                        }

                        (form(
                            method = "post",
                            action = $"/admin/users/{user.Id}/passkeys/{pk.Id}/remove",
                            class' = "inline"
                        ) {
                            ctx.GetAntiforgeryInput()

                            button(
                                type' = "submit",
                                class' = Components.btnSm Components.Ghost + " text-error hover:text-error"
                            ) {
                                t.PasskeyRemoveButton
                            }
                        })
                        |> Components.confirmSubmit (t.UserPasskeyRemoveConfirm user.DisplayName)
                    }
            }
        }

let row (ctx: HttpContext) (t: Texts.Texts) (data: Row) : HtmlElement =
    let user = data.User

    article(class' = "um-card space-y-3")
        .data("user", user.Id)
        .data("user-state", (if user.IsEnabled then "enabled" else "disabled")) {

        div(class' = "flex items-center justify-between gap-3") {
            div(class' = "space-y-1") {
                h2(class' = "text-headline-sm text-on-surface") { user.DisplayName }

                match user.Email with
                | Some email -> (p(class' = "text-body-md text-on-surface-variant") { email }) :> HtmlElement
                | None -> Fragment() :> HtmlElement
            }

            Components.stateBadge user.IsEnabled t.UserEnabledBadge t.UserDisabledBadge
        }

        div(class' = "flex flex-wrap gap-1.5") {
            for kind, label in data.LinkedProviders do
                (Components.pill Components.PrimaryTone label).data ("linked-provider", kind)
            if not data.Passkeys.IsEmpty then
                (Components.pill Components.PrimaryTone "Passkey").data ("linked-provider", "passkey")
        }

        linksList ctx t data

        passkeysList ctx t data

        // Someone who still has a device adds passkeys on /account/passkeys, so
        // on a row that lists one, the operator's invitation is for recovery.
        let recovery = not data.Passkeys.IsEmpty

        if data.CanInvite then
            let hintId = $"invite-{user.Id}-hint"

            let submit =
                button(type' = "submit", class' = Components.btnSm Components.Secondary).data ("user-invite", "") {
                    if recovery then t.UserRecoveryLinkButton else t.UserInvitePasskeyButton
                }

            if recovery then
                submit.attr ("aria-describedby", hintId) |> ignore

            (div(class' = "space-y-1") {
                form(method = "post", action = $"/admin/users/{user.Id}/invite", class' = "inline") {
                    ctx.GetAntiforgeryInput()
                    submit
                }

                if recovery then
                    (p(id = hintId, class' = "um-hint").data ("user-invite-hint", "") { t.UserRecoveryLinkHint })
                    :> HtmlElement
                else
                    Fragment() :> HtmlElement
            })
            :> HtmlElement
        else
            Fragment() :> HtmlElement

        match data.RevealedLink with
        | Some link ->
            let label' = if recovery then t.UserRecoveryLinkLabel else t.UserInvitationLinkLabel

            Components.copyField t.Copy t.Copied label' $"invite-{user.Id}" link (fun i ->
                i.data ("invitation-link", "") |> ignore)
            :> HtmlElement
        | None -> Fragment() :> HtmlElement

        div(class' = "grid gap-1 text-body-md text-on-surface-variant sm:grid-cols-2") {
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

/// A refusal is a warning, so it does not read as good news above the cards;
/// the rest went through.
let private noticeKind (code: string) =
    match code with
    | "user-link-last"
    | "user-link-not-found"
    | "user-not-found" -> Components.Warning
    | _ -> Components.Success

/// `aboutName` is the name of the user the notice is about (`user=<id>`), when
/// they are among the rows.
let private view
    (ctx: HttpContext)
    (t: Texts.Texts)
    (rows: Row list)
    (term: string)
    (notice: string option)
    (aboutName: string option)
    =
    div(class' = "space-y-6").data("screen", "users") {
        Components.pageHeading t.UsersTitle (Some t.UsersLead)

        match notice with
        | Some code ->
            let text =
                match code, aboutName with
                | "passkey-user-invited", Some name -> t.UserPasskeyInvited name
                | "user-link-last", name -> t.UserLinkLast(name |> Option.defaultValue t.UserLinkLastTheUser)
                | _ -> t.Notice code

            (div().data("notice", code) { Components.alert (noticeKind code) text }) :> HtmlElement
        | None -> Fragment() :> HtmlElement

        searchForm t term

        if rows.IsEmpty then
            Components.emptyState Theme.Group t.UsersEmptyTitle t.UsersEmptyBody None
        else
            for data in rows do
                row ctx t data
    }
    |> Layout.operatorPage ctx t t.UsersTitle []

/// Draws the Users page: `term` the search, `notice` a code to show, `about`
/// the user a notice is about, `confirm` the user and link fingerprint whose
/// confirm block is open, and the status the page answers with (the removal's
/// first post redraws it at 422). The page and that post share it.
let render
    (ctx: HttpContext)
    (term: string)
    (notice: string option)
    (about: string option)
    (confirm: (string * string) option)
    (status: int)
    : Task =
    task {
        let t = Texts.current ()
        let config = ctx.GetService<Database.DbConfig>()
        let! found = search config term

        // The user whose block is open is always on the page, whatever the term.
        let! users =
            task {
                match confirm with
                | Some(userId, _) when not (found |> List.exists (fun u -> u.Id = userId)) ->
                    match! Umbral.Infrastructure.Users.findById config userId with
                    | Some user -> return user :: found
                    | None -> return found
                | _ -> return found
            }

        let! passkeyConnector = ConnectorStore.findBySlug config "passkey"

        let invitedId =
            match notice with
            | Some "passkey-user-invited" -> about
            | _ -> None

        let rows = ResizeArray()
        let mutable aboutName = None

        for user in users do
            if about = Some user.Id then
                aboutName <- Some user.DisplayName

            let revealed =
                if invitedId = Some user.Id then
                    Umbral.Handlers.Admin.PasskeyConnector.InviteReveal.consume ctx $"user:{user.Id}"
                else
                    None

            let open' =
                match confirm with
                | Some(userId, fingerprint) when userId = user.Id -> Some fingerprint
                | _ -> None

            let! data = loadRow config user passkeyConnector.IsSome revealed open' term
            rows.Add data

        if status <> 200 then
            ctx.SetStatusCode status

        return! ctx.WriteHtmlView(view ctx t (List.ofSeq rows) term notice aboutName)
    }
    :> Task

let page: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        let term = ctx.TryGetQueryValue "search" |> Option.defaultValue ""
        let notice = ctx.TryGetQueryValue "msg" |> Option.bind Texts.knownNotice
        let about = ctx.TryGetQueryValue "user"

        render ctx term notice about None 200)

let endpoint = GET [ route "/admin/users" page ]
