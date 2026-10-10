/// The structured record of who did what, required by FR-035.
///
/// Umbral has no support team and no audit product, so this is the only place
/// an operator can look when something is wrong. That makes two properties
/// matter more than completeness:
///
///   1. The shape is closed. `EventType` is a union, not a string, so a typo
///      cannot invent an event nobody queries for, and `allEventTypes` lets a
///      test assert every one is actually emitted somewhere (T092).
///   2. Nothing secret reaches it. The API takes named fields rather than a
///      free-text message precisely so that `detail` never becomes the place
///      someone interpolates a token "just this once".
module Umbral.Infrastructure.SecurityEvents

open System
open System.Threading.Tasks
open Microsoft.Data.Sqlite

type EventType =
    // Sign-in
    | SignInSucceeded
    | SignInFailed
    | UserCreated
    | UserLinked
    // User lifecycle
    | UserEnabled
    | UserDisabled
    | UserDeleted
    // Operator
    | OperatorSignedIn
    | OperatorSignInFailed
    | OperatorCredentialChanged
    // Connectors
    | ConnectorCreated
    | ConnectorUpdated
    | ConnectorEnabled
    | ConnectorDisabled
    | ConnectorDeleted
    // Apps
    | AppCreated
    | AppUpdated
    | AppSecretRotated
    | AppDeleted
    // Protocol
    | AuthorizationCodeReplayed
    // Configuration
    | ConfigApplied

    /// The stored form. Explicit rather than derived from the case name: these
    /// values end up in a database an operator may query by hand, and renaming
    /// a case should not silently orphan the rows written before it.
    member this.Value =
        match this with
        | SignInSucceeded -> "sign_in_succeeded"
        | SignInFailed -> "sign_in_failed"
        | UserCreated -> "user_created"
        | UserLinked -> "user_linked"
        | UserEnabled -> "user_enabled"
        | UserDisabled -> "user_disabled"
        | UserDeleted -> "user_deleted"
        | OperatorSignedIn -> "operator_signed_in"
        | OperatorSignInFailed -> "operator_sign_in_failed"
        | OperatorCredentialChanged -> "operator_credential_changed"
        | ConnectorCreated -> "connector_created"
        | ConnectorUpdated -> "connector_updated"
        | ConnectorEnabled -> "connector_enabled"
        | ConnectorDisabled -> "connector_disabled"
        | ConnectorDeleted -> "connector_deleted"
        | AppCreated -> "app_created"
        | AppUpdated -> "app_updated"
        | AppSecretRotated -> "app_secret_rotated"
        | AppDeleted -> "app_deleted"
        | AuthorizationCodeReplayed -> "authorization_code_replayed"
        | ConfigApplied -> "config_applied"

/// Every event type, for the test that asserts each one is emitted somewhere in
/// the product (T092). A type nobody writes is either a missing call site or a
/// case that should be deleted; both are worth finding.
let allEventTypes =
    [ SignInSucceeded
      SignInFailed
      UserCreated
      UserLinked
      UserEnabled
      UserDisabled
      UserDeleted
      OperatorSignedIn
      OperatorSignInFailed
      OperatorCredentialChanged
      ConnectorCreated
      ConnectorUpdated
      ConnectorEnabled
      ConnectorDisabled
      ConnectorDeleted
      AppCreated
      AppUpdated
      AppSecretRotated
      AppDeleted
      AuthorizationCodeReplayed
      ConfigApplied ]

type Outcome =
    | Success
    | Failure

    member this.Value =
        match this with
        | Success -> "success"
        | Failure -> "failure"

/// Who caused it. `User` is an end user signing in; `Operator` is the one
/// person with the admin credential; `System` is Umbral itself — a startup
/// config apply, a pruning job.
type Actor =
    | Operator
    | User
    | System

    member this.Value =
        match this with
        | Operator -> "operator"
        | User -> "user"
        | System -> "system"

/// One event.
///
/// `Detail` is short, structured and **never** a secret, a token, an
/// authorization code or a `state` value. The useful content is a reason code
/// — "redirect_uri_mismatch", "provider_rejected_credentials" — not a dump.
type SecurityEvent =
    { EventType: EventType
      Outcome: Outcome
      Actor: Actor
      UserId: string option
      ConnectorSlug: string option
      ClientId: string option
      RemoteIp: string option
      Detail: string option }

/// A minimal event of a given type. Build on it with record update syntax, so
/// adding a field at a call site stays visible in a diff.
let event (eventType: EventType) (outcome: Outcome) (actor: Actor) =
    { EventType = eventType
      Outcome = outcome
      Actor = actor
      UserId = None
      ConnectorSlug = None
      ClientId = None
      RemoteIp = None
      Detail = None }

/// `box` widens to `obj | null` under F# nullness checking; an upcast of a
/// value already known to be non-null does not.
let private asParameter (value: string option) : obj =
    match value with
    | Some v -> v :> obj
    | None -> DBNull.Value :> obj

/// Write within a caller's transaction, so that an event and the change it
/// describes commit together or not at all. A disabled user whose event went
/// missing is worse than no log.
let recordIn
    (connection: SqliteConnection)
    (transaction: SqliteTransaction | null)
    (clock: TimeProvider)
    (e: SecurityEvent)
    : Task<unit> =
    task {
        use command = connection.CreateCommand()
        command.Transaction <- transaction

        command.CommandText <-
            "INSERT INTO security_events
                 (occurred_utc, event_type, outcome, actor, user_id, connector_slug, client_id, remote_ip, detail)
             VALUES (@occurred, @type, @outcome, @actor, @user, @connector, @client, @ip, @detail)"

        command.Parameters.AddWithValue("@occurred", clock.GetUtcNow().ToString "o") |> ignore
        command.Parameters.AddWithValue("@type", e.EventType.Value) |> ignore
        command.Parameters.AddWithValue("@outcome", e.Outcome.Value) |> ignore
        command.Parameters.AddWithValue("@actor", e.Actor.Value) |> ignore
        command.Parameters.AddWithValue("@user", asParameter e.UserId) |> ignore
        command.Parameters.AddWithValue("@connector", asParameter e.ConnectorSlug) |> ignore
        command.Parameters.AddWithValue("@client", asParameter e.ClientId) |> ignore
        command.Parameters.AddWithValue("@ip", asParameter e.RemoteIp) |> ignore
        command.Parameters.AddWithValue("@detail", asParameter e.Detail) |> ignore

        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

/// Write on its own connection, for the call sites with no transaction of their
/// own — a failed sign-in, a rejected admin credential.
let record (config: Database.DbConfig) (clock: TimeProvider) (e: SecurityEvent) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        do! recordIn connection null clock e
    }
