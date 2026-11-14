/// Applying the declaration (spec 019): the apps and the passkey connector an
/// operator wrote in the environment become real, once, at start.
///
/// `Domains/Declaration.fs` reads and checks the text. This module checks it
/// against the database (a client ID an operator made by hand, a passkey
/// connector set up with other settings), then writes it in one transaction
/// with its security events, or writes nothing.
///
/// One transaction across OpenIddict and Umbral's own tables (R2): the apply
/// takes one DI scope, begins the transaction on that scope's `UmbralContext`,
/// writes Umbral's rows on the context's own connection with the transaction's
/// `DbTransaction`, and takes the application manager from the SAME scope, whose
/// EF store saves through that context and so inside the transaction.
///
/// Any failure while applying stops the start (C1). OpenIddict's application
/// manager keeps an in-memory cache, and an app created or updated in a
/// transaction that rolled back could stay in it with the wrong redirect URIs;
/// the process going away is what drops the cache with the transaction. So
/// nothing here catches an exception, and the caller never continues past one.
///
/// Nothing written here is a secret, and no event detail holds a secret or an
/// address: the details are closed words and field names.
module Umbral.Infrastructure.Declarations

open System
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Storage
open Microsoft.Extensions.DependencyInjection
open OpenIddict.Abstractions
open Umbral.Domains
open Umbral.Domains.Connectors
open Umbral.Domains.Declaration

/// The connector a declaration can name, and the slug of the connector the
/// Passkey pane creates.
[<Literal>]
let PasskeySlug = "passkey"

/// What `declared_items` records: an app (by client ID) or a connector (by slug).
type ItemKind =
    | AppItem
    | ConnectorItem

    member this.Value =
        match this with
        | AppItem -> "app"
        | ConnectorItem -> "connector"

let private readItems (connection: SqliteConnection) : Task<Map<ItemKind * string, bool>> =
    task {
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT kind, id, is_declared FROM declared_items"
        use! reader = command.ExecuteReaderAsync()
        let mutable items = Map.empty
        let mutable go = true

        while go do
            let! hasRow = reader.ReadAsync()

            if hasRow then
                let kind = if reader.GetString 0 = "app" then AppItem else ConnectorItem
                items <- Map.add (kind, reader.GetString 1) (reader.GetInt32 2 = 1) items
            else
                go <- false

        return items
    }

/// Whether the declaration owns this item now (a row with `is_declared = 1`).
let isDeclared (config: Database.DbConfig) (kind: ItemKind) (id: string) : Task<bool> =
    task {
        use! connection = Database.openConnection config
        let! items = readItems connection
        return items |> Map.tryFind (kind, id) |> Option.defaultValue false
    }

/// The client IDs of the apps the declaration owns now, in one read for a screen.
let declaredClientIds (config: Database.DbConfig) : Task<Set<string>> =
    task {
        use! connection = Database.openConnection config
        let! items = readItems connection

        return
            items
            |> Map.toSeq
            |> Seq.choose (fun ((kind, id), declared) -> if kind = AppItem && declared then Some id else None)
            |> Set.ofSeq
    }

/// Whether the declaration owns the passkey connector now.
let isPasskeyDeclared (config: Database.DbConfig) : Task<bool> =
    isDeclared config ConnectorItem PasskeySlug

/// Drops the history row of an item that is deleted from the admin area. Only a
/// row that is no longer declared (D3): a declared item's routes refuse before
/// this, and the row would only come back at the next start. Without it a later
/// item with the same client ID or slug would start out looking as if it had
/// been declared once.
let forget (config: Database.DbConfig) (kind: ItemKind) (id: string) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "DELETE FROM declared_items WHERE kind = @kind AND id = @id AND is_declared = 0"
        command.Parameters.AddWithValue("@kind", kind.Value) |> ignore
        command.Parameters.AddWithValue("@id", id) |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

/// How the passkey connector came out.
type PasskeyOutcome =
    | PasskeyCreated
    | PasskeyUpdated
    | PasskeyAdopted
    | PasskeyUnchanged
    | PasskeyNoLongerDeclared
    /// Not declared, and not declared before: nothing to say.
    | PasskeyAbsent

/// What a start applied, for the start line.
type Applied =
    { AppsCreated: int
      AppsUpdated: int
      AppsUnchanged: int
      AppsNoLongerDeclared: int
      Passkey: PasskeyOutcome
      /// A connector key an app names that no connector has yet. Not a refusal.
      Warnings: string list }

/// The one Information line of a start (contracts/declaration.md): counts only,
/// and what happened to the passkey connector, never a name or an address.
let describe (applied: Applied) : string =
    let apps = if applied.AppsCreated = 1 then "app" else "apps"

    let passkey =
        match applied.Passkey with
        | PasskeyCreated -> "; passkey connector created"
        | PasskeyUpdated -> "; passkey connector updated"
        | PasskeyAdopted -> "; passkey connector adopted"
        | PasskeyUnchanged -> "; passkey connector unchanged"
        | PasskeyNoLongerDeclared -> "; passkey connector no longer declared"
        | PasskeyAbsent -> ""

    $"Declaration applied: {applied.AppsCreated} {apps} created, {applied.AppsUpdated} updated, {applied.AppsUnchanged} unchanged, {applied.AppsNoLongerDeclared} no longer declared{passkey}."

/// Whether a start with this outcome has a line worth writing when there was no
/// declaration: only when something was handed back to the admin area.
let handedBack (applied: Applied) =
    applied.AppsNoLongerDeclared > 0 || applied.Passkey = PasskeyNoLongerDeclared

// ---------------------------------------------------------------------------
// Writes on a given connection and transaction
// ---------------------------------------------------------------------------

let private markDeclared
    (connection: SqliteConnection)
    (transaction: SqliteTransaction)
    (clock: TimeProvider)
    (kind: ItemKind)
    (id: string)
    : Task<unit> =
    task {
        use command = connection.CreateCommand()
        command.Transaction <- transaction

        command.CommandText <-
            "INSERT INTO declared_items (kind, id, is_declared, first_declared_utc, changed_utc)
             VALUES (@kind, @id, 1, @now, @now)
             ON CONFLICT (kind, id) DO UPDATE SET is_declared = 1, changed_utc = @now"

        command.Parameters.AddWithValue("@kind", kind.Value) |> ignore
        command.Parameters.AddWithValue("@id", id) |> ignore
        command.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

let private markUndeclared
    (connection: SqliteConnection)
    (transaction: SqliteTransaction)
    (clock: TimeProvider)
    (kind: ItemKind)
    (id: string)
    : Task<unit> =
    task {
        use command = connection.CreateCommand()
        command.Transaction <- transaction

        command.CommandText <-
            "UPDATE declared_items SET is_declared = 0, changed_utc = @now
             WHERE kind = @kind AND id = @id AND is_declared = 1"

        command.Parameters.AddWithValue("@kind", kind.Value) |> ignore
        command.Parameters.AddWithValue("@id", id) |> ignore
        command.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

let private recordEvent
    (connection: SqliteConnection)
    (transaction: SqliteTransaction)
    (clock: TimeProvider)
    (eventType: SecurityEvents.EventType)
    (clientId: string option)
    (connectorSlug: string option)
    (detail: string)
    : Task<unit> =
    SecurityEvents.recordIn
        connection
        transaction
        clock
        { SecurityEvents.event eventType SecurityEvents.Success SecurityEvents.Actor.System with
            ClientId = clientId
            ConnectorSlug = connectorSlug
            Detail = Some detail }

// ---------------------------------------------------------------------------
// The plan: everything read, nothing written
// ---------------------------------------------------------------------------

/// What an existing declared app needs.
type private AppChange =
    { App: DeclaredApp
      Application: obj
      /// The fields that differ, in the order of the event detail.
      Fields: string list
      /// The stored secret no longer validates against the declared one.
      Rotate: bool
      /// The declaration owned it before, let go, and names it again.
      Redeclared: bool }

type private AppStep =
    | CreateApp of DeclaredApp
    | ChangeApp of AppChange
    | KeepApp

type private PasskeyStep =
    | CreatePasskey of DeclaredPasskeys
    | ChangePasskey of DeclaredPasskeys * Connector * redeclared: bool
    | AdoptPasskey of DeclaredPasskeys
    | KeepPasskey
    | UndeclarePasskey
    | NoPasskey

let private enrolmentWords (enrolment: PasskeyEnrolment) =
    match enrolment with
    | InvitationsOnly -> "invitations only"
    | OpenSignUp -> "anyone can sign up"

/// How the stored passkey connector differs from the declared one, in words
/// for the refusal line.
let private passkeyDifferences (declared: DeclaredPasskeys) (stored: Connector) =
    [ if not stored.IsEnabled then
          "disabled"
      if not stored.IsShared then
          "not shared"
      if stored.PasskeyEnrolment <> declared.SignUp then
          $"who can sign up: {enrolmentWords stored.PasskeyEnrolment}" ]

let private matches (declared: DeclaredPasskeys) (stored: Connector) =
    stored.IsEnabled && stored.IsShared && stored.PasskeyEnrolment = declared.SignUp

let private label (index: int) (key: string) = $"apps[{index}] (key \"{key}\")"

/// Read what exists and decide, per item, what a start must do. Writes nothing.
/// Returns the refusals (all of them) or the steps and the warnings.
let private plan
    (config: Database.DbConfig)
    (applications: IOpenIddictApplicationManager)
    (declaration: Declaration option)
    : Task<Result<AppStep list * PasskeyStep * string list * string list, string list>> =
    task {
        use! connection = Database.openConnection config
        let! items = readItems connection
        let! connectors = ConnectorStore.listAll config

        let refusals = ResizeArray<string>()
        let warnings = ResizeArray<string>()

        // The passkey connector first: an app's connector list may name it.
        let passkey =
            match declaration |> Option.bind _.Passkeys with
            | None ->
                if items |> Map.tryFind (ConnectorItem, PasskeySlug) = Some true then
                    UndeclarePasskey
                else
                    NoPasskey
            | Some declared ->
                match connectors |> List.tryFind (fun c -> c.Slug = PasskeySlug) with
                | None -> CreatePasskey declared
                | Some stored when stored.Kind <> Passkey ->
                    refusals.Add
                        $"passkeys: the connector key \"{PasskeySlug}\" belongs to a {stored.Kind.Value} connector. Remove \"passkeys\" from the declaration; that connector stays as it is."

                    NoPasskey
                | Some stored ->
                    match items |> Map.tryFind (ConnectorItem, PasskeySlug) with
                    | Some true -> if matches declared stored then KeepPasskey else ChangePasskey(declared, stored, false)
                    | Some false -> ChangePasskey(declared, stored, true)
                    // Never declared. Exactly what the declaration says: adopting
                    // changes nothing and widens nothing (C2). Anything else may be
                    // in use with other settings, and is refused, never merged.
                    | None ->
                        if matches declared stored then
                            AdoptPasskey declared
                        else
                            let differences = passkeyDifferences declared stored |> String.concat ", "

                            refusals.Add
                                $"passkeys: the passkey connector was set up in the admin area with other settings ({differences}). Remove \"passkeys\" from the declaration; the connector stays as it is."

                            NoPasskey

        let known =
            connectors
            |> List.map _.Slug
            |> Set.ofList
            |> fun slugs -> if declaration |> Option.bind _.Passkeys |> Option.isSome then Set.add PasskeySlug slugs else slugs

        let declaredApps = declaration |> Option.map _.Apps |> Option.defaultValue []
        let steps = ResizeArray<AppStep>()

        for index, declared in List.indexed declaredApps do
            let text = label index declared.Key

            // `app_connectors` has a foreign key to `connectors`: a key no
            // connector has cannot be stored. It is accepted with a warning and
            // left out until the connector exists and a later start applies it;
            // the app offers nothing through it meanwhile, as the spec says. The
            // comparison and the writes both use what can be stored, so a start
            // that changes nothing still writes nothing.
            let app =
                match declared.Connectors with
                | Only slugs ->
                    for slug in slugs do
                        if not (Set.contains slug known) then
                            warnings.Add
                                $"{text}: connectors names \"{slug}\", and no connector has that key yet. The app offers nothing through it until the connector exists and Umbral starts again."

                    { declared with Connectors = Only(Set.intersect slugs known) }
                | AllShared -> declared

            let! found = applications.FindByClientIdAsync app.Key

            match found with
            | null -> steps.Add(CreateApp app)
            | application ->
                match items |> Map.tryFind (AppItem, app.Key) with
                | None ->
                    refusals.Add
                        $"{text}: the client ID \"{app.Key}\" belongs to an app created in the admin area. Give the declared app another key, or delete that app on the Apps screen."

                    steps.Add KeepApp
                | Some owned ->
                    let! storedName = applications.GetDisplayNameAsync application
                    let! storedType = applications.GetClientTypeAsync application
                    let! redirect = applications.GetRedirectUrisAsync application
                    let! postLogout = applications.GetPostLogoutRedirectUrisAsync application
                    let! storedChoice = AppConnectors.choiceFor config app.Key

                    let storedConfidential = storedType = OpenIddictConstants.ClientTypes.Confidential
                    let declaredConfidential = (app.Type = Web)

                    // Sets, not lists: OAuth matches exactly and has no order, and a
                    // set cannot make every start look like a change.
                    let sameSet (stored: seq<string>) (declared: string list) =
                        Set.ofSeq stored = Set.ofList declared

                    let fields =
                        [ if Option.ofObj storedName <> Some app.Name then
                              "name"
                          if storedConfidential <> declaredConfidential then
                              "type"
                          if not (sameSet redirect app.RedirectUris) then
                              "redirect_uris"
                          if not (sameSet postLogout app.PostLogoutRedirectUris) then
                              "post_logout_redirect_uris"
                          if storedChoice <> app.Connectors then
                              "connectors" ]

                    // A type change sets or drops the secret with it. Otherwise a web
                    // app's stored secret is checked against the one given.
                    let! rotate =
                        task {
                            match app.Secret with
                            | Some secret when storedConfidential && declaredConfidential ->
                                let! valid = applications.ValidateClientSecretAsync(application, secret.Value)
                                return not valid
                            | _ -> return false
                        }

                    let redeclared = not owned

                    if fields.IsEmpty && not rotate && not redeclared then
                        steps.Add KeepApp
                    else
                        steps.Add(
                            ChangeApp
                                { App = app
                                  Application = application
                                  Fields = fields
                                  Rotate = rotate
                                  Redeclared = redeclared }
                        )

        if refusals.Count > 0 then
            return Error(List.ofSeq refusals)
        else
            let named = declaredApps |> List.map _.Key |> Set.ofList

            let leaving =
                items
                |> Map.toList
                |> List.choose (fun ((kind, id), declared) ->
                    if kind = AppItem && declared && not (Set.contains id named) then Some id else None)

            return Ok(List.ofSeq steps, passkey, leaving, List.ofSeq warnings)
    }

// ---------------------------------------------------------------------------
// The apply
// ---------------------------------------------------------------------------

let private uris (list: string list) = list |> List.map (fun s -> Uri(s, UriKind.Absolute))

/// Apply the declaration, or un-declare everything still declared when there is
/// none, in one transaction with its events; or write nothing.
///
/// `Error` holds every refusal (conflicts with the admin area's own items), and
/// nothing was written. Any exception propagates, with the transaction rolled
/// back, and the caller must stop the start (C1).
let apply (services: IServiceProvider) (declaration: Declaration option) : Task<Result<Applied, string list>> =
    task {
        let config = services.GetRequiredService<Database.DbConfig>()
        let clock = services.GetRequiredService<TimeProvider>()

        // One scope: the context that holds the transaction, and the manager whose
        // store saves through it.
        use scope = services.CreateScope()
        let context = scope.ServiceProvider.GetRequiredService<Umbral.Migrations.UmbralContext>()
        let applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>()

        let! planned = plan config applications declaration

        match planned with
        | Error refusals -> return Error refusals
        | Ok(steps, passkey, leaving, warnings) ->

        let count predicate = steps |> List.filter predicate |> List.length

        let applied =
            { AppsCreated = count (function CreateApp _ -> true | _ -> false)
              AppsUpdated = count (function ChangeApp _ -> true | _ -> false)
              AppsUnchanged = count (function KeepApp -> true | _ -> false)
              AppsNoLongerDeclared = leaving.Length
              Passkey =
                match passkey with
                | CreatePasskey _ -> PasskeyCreated
                | ChangePasskey _ -> PasskeyUpdated
                | AdoptPasskey _ -> PasskeyAdopted
                | KeepPasskey -> PasskeyUnchanged
                | UndeclarePasskey -> PasskeyNoLongerDeclared
                | NoPasskey -> PasskeyAbsent
              Warnings = warnings }

        let nothingToWrite =
            applied.AppsCreated = 0
            && applied.AppsUpdated = 0
            && leaving.IsEmpty
            && (match passkey with
                | KeepPasskey
                | NoPasskey -> true
                | _ -> false)

        // A start that changes nothing touches nothing: no transaction, no row.
        if nothingToWrite then
            return Ok applied
        else

        use! transaction = context.Database.BeginTransactionAsync()
        let connection = context.Database.GetDbConnection() :?> SqliteConnection
        let dbTransaction = transaction.GetDbTransaction() :?> SqliteTransaction
        let now = clock.GetUtcNow()
        let record = recordEvent connection dbTransaction clock

        // 1. The passkey connector, so an app's list naming it finds it.
        match passkey with
        | CreatePasskey declared ->
            let connector =
                { Slug = PasskeySlug
                  Kind = Passkey
                  DisplayName = "Passkey"
                  ClientId = ""
                  ClientSecretRef = ""
                  ClientSecretHint = ""
                  IssuerUrl = None
                  AllowedEmailDomains = []
                  PasskeyEnrolment = declared.SignUp
                  FediverseAccess = FediverseAccess.Any
                  AppleKey = None
                  MicrosoftAudience = None
                  EmailTrust = None
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = now
                  UpdatedUtc = now }

            let! result = ConnectorStore.createIn connection dbTransaction now connector ("", "")

            match result with
            | ConnectorStore.Created -> ()
            | _ -> failwith $"The passkey connector could not be created: the key \"{PasskeySlug}\" is taken."

            do! markDeclared connection dbTransaction clock ConnectorItem PasskeySlug

            do!
                record
                    SecurityEvents.ConnectorCreated
                    None
                    (Some PasskeySlug)
                    $"declared;enrolment={declared.SignUp.Value}"

        | ChangePasskey(declared, stored, redeclared) ->
            let enrolmentChanged = stored.PasskeyEnrolment <> declared.SignUp
            let sharedChanged = not stored.IsShared

            if enrolmentChanged then
                let! _ = ConnectorStore.setEnrolmentIn connection dbTransaction clock PasskeySlug declared.SignUp
                ()

            if sharedChanged then
                do! ConnectorStore.setSharedIn connection dbTransaction clock PasskeySlug true

            if enrolmentChanged || sharedChanged || redeclared then
                let shared = if sharedChanged then ";shared" else ""
                let again = if redeclared then ";redeclared" else ""

                do!
                    record
                        SecurityEvents.ConnectorUpdated
                        None
                        (Some PasskeySlug)
                        $"declared;enrolment={declared.SignUp.Value}{shared}{again}"

            if not stored.IsEnabled then
                do! ConnectorStore.setEnabledIn connection dbTransaction clock PasskeySlug true
                do! record SecurityEvents.ConnectorEnabled None (Some PasskeySlug) "declared"

            do! markDeclared connection dbTransaction clock ConnectorItem PasskeySlug

        | AdoptPasskey _ ->
            do! markDeclared connection dbTransaction clock ConnectorItem PasskeySlug
            do! record SecurityEvents.ConnectorUpdated None (Some PasskeySlug) "declared;adopted"

        | KeepPasskey
        | UndeclarePasskey
        | NoPasskey -> ()

        // 2. Each app in the declaration's order.
        for step in steps do
            match step with
            | KeepApp -> ()

            | CreateApp app ->
                let secret = app.Secret |> Option.map _.Value

                let descriptor =
                    AppClients.buildDescriptor
                        app.Key
                        app.Name
                        (app.Type = Web)
                        (uris app.RedirectUris)
                        (uris app.PostLogoutRedirectUris)
                        secret

                let! _ = applications.CreateAsync descriptor

                // No row means "all shared connectors", so the default writes none.
                match app.Connectors with
                | Only _ -> do! AppConnectors.setChoiceIn connection dbTransaction app.Key app.Connectors
                | AllShared -> ()

                do! markDeclared connection dbTransaction clock AppItem app.Key
                do! record SecurityEvents.AppCreated (Some app.Key) None "declared"

            | ChangeApp change ->
                let app = change.App
                let has field = List.contains field change.Fields

                if has "name" || has "type" || has "redirect_uris" || has "post_logout_redirect_uris" || change.Rotate then
                    // Populated from the stored application, so the stored secret
                    // hash travels in the descriptor and is not hashed again; only
                    // a secret that is set here (a type change to web, a rotation)
                    // differs from it and is hashed.
                    let descriptor = OpenIddictApplicationDescriptor()
                    do! applications.PopulateAsync(descriptor, change.Application)

                    if has "name" then
                        descriptor.DisplayName <- app.Name

                    if has "type" then
                        let confidential = (app.Type = Web)
                        descriptor.ApplicationType <- if confidential then OpenIddictConstants.ApplicationTypes.Web else OpenIddictConstants.ApplicationTypes.Native
                        descriptor.ClientType <- if confidential then OpenIddictConstants.ClientTypes.Confidential else OpenIddictConstants.ClientTypes.Public
                        descriptor.ClientSecret <- app.Secret |> Option.map _.Value |> Option.toObj

                    if has "redirect_uris" then
                        descriptor.RedirectUris.Clear()

                        for uri in uris app.RedirectUris do
                            descriptor.RedirectUris.Add uri |> ignore

                    if has "post_logout_redirect_uris" then
                        descriptor.PostLogoutRedirectUris.Clear()

                        for uri in uris app.PostLogoutRedirectUris do
                            descriptor.PostLogoutRedirectUris.Add uri |> ignore

                    if change.Rotate then
                        descriptor.ClientSecret <- app.Secret |> Option.map _.Value |> Option.toObj

                    do! applications.UpdateAsync(change.Application, descriptor)

                if has "connectors" then
                    do! AppConnectors.setChoiceIn connection dbTransaction app.Key app.Connectors

                do! markDeclared connection dbTransaction clock AppItem app.Key

                if not change.Fields.IsEmpty || change.Redeclared then
                    let changed = String.concat "," change.Fields
                    let fields = if change.Fields.IsEmpty then "" else ";fields=" + changed

                    let again = if change.Redeclared then ";redeclared" else ""
                    do! record SecurityEvents.AppUpdated (Some app.Key) None $"declared{fields}{again}"

                if change.Rotate then
                    do! record SecurityEvents.AppSecretRotated (Some app.Key) None "declared"

        // 3. What left the declaration goes back to the admin area, unchanged.
        for clientId in leaving do
            do! markUndeclared connection dbTransaction clock AppItem clientId
            do! record SecurityEvents.AppUpdated (Some clientId) None "undeclared"

        match passkey with
        | UndeclarePasskey ->
            do! markUndeclared connection dbTransaction clock ConnectorItem PasskeySlug
            do! record SecurityEvents.ConnectorUpdated None (Some PasskeySlug) "undeclared"
        | _ -> ()

        do! transaction.CommitAsync()
        return Ok applied
    }
