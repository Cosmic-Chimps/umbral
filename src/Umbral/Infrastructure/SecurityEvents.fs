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
    // Secret stores (specs/005-external-secret-stores)
    | SecretStoreFailed
    | ConnectorSecretMoved
    // Apps
    | AppCreated
    | AppUpdated
    | AppSecretRotated
    | AppDeleted
    // Allowed email domains (specs/006-allowed-email-domains)
    | AllowedDomainsChanged
    // Protocol
    | AuthorizationCodeReplayed

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
        | SecretStoreFailed -> "secret_store_failed"
        | ConnectorSecretMoved -> "connector_secret_moved"
        | AppCreated -> "app_created"
        | AppUpdated -> "app_updated"
        | AppSecretRotated -> "app_secret_rotated"
        | AppDeleted -> "app_deleted"
        | AllowedDomainsChanged -> "allowed_domains_changed"
        | AuthorizationCodeReplayed -> "authorization_code_replayed"

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
      SecretStoreFailed
      ConnectorSecretMoved
      AppCreated
      AppUpdated
      AppSecretRotated
      AppDeleted
      // TODO(006 T030): add AllowedDomainsChanged here once the admin screens
      // emit it (T020, T023) and the coverage journey drives it. Declared above
      // so those tasks can use it; listed here it would fail the coverage test.
      AuthorizationCodeReplayed ]

/// The `Detail` of a `secret_store_failed` event: which store, which operation
/// (`put`, `get`, `delete` or `probe`), and the closed cause word, plus
/// `stale=true` when the failure was a refresh whose old value was served from
/// the grace period. Built from fixed words only, so no value, locator or
/// credential can reach the event table through it.
let storeDetail (store: string) (op: string) (cause: Secrets.StoreFailure) (stale: bool) =
    let core = $"store={store};op={op};cause={cause.Cause}"
    if stale then core + ";stale=true" else core

/// Where a domain refusal happened: the provider's return, an existing session
/// at the authorize endpoint, or a code or refresh token at the token endpoint.
type RefusalStage =
    | Callback
    | Authorize
    | Token

    member this.Value =
        match this with
        | Callback -> "callback"
        | Authorize -> "authorize"
        | Token -> "token"

/// A domain is only written into an event when it is already in the normalised
/// form `EmailDomains` produces: lower-case letters, digits, dots and hyphens.
/// The detail is `;`- and `=`-separated, so anything else (a provider's odd
/// address reaching here through `domainOf`) is written as `-` instead of
/// being allowed to add fields of its own.
let private safeDomain (domain: string option) =
    match domain with
    | Some name when
        name.Length > 0
        && name.Length <= 253
        && name |> Seq.forall (fun c -> (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c = '.' || c = '-')
        ->
        name
    | _ -> "-"

/// The `Detail` of a `sign_in_failed` event for a refused email domain:
/// `email_domain_refused;reason=…;rule=…;stage=…;domain=…`. Built from closed
/// words and the normalised domain only, never from the address: the full
/// address does not reach the event table (specs/006 research R10).
let refusalDetail
    (reason: Umbral.Domains.EmailDomains.Refusal)
    (level: Umbral.Domains.EmailDomains.Level)
    (stage: RefusalStage)
    (domain: string option)
    =
    let reasonWord =
        match reason with
        | Umbral.Domains.EmailDomains.NoEmail -> "no_email"
        | Umbral.Domains.EmailDomains.EmailNotVerified -> "email_not_verified"
        | Umbral.Domains.EmailDomains.DomainNotAllowed _ -> "domain_not_allowed"

    let levelWord =
        match level with
        | Umbral.Domains.EmailDomains.Connector -> "connector"
        | Umbral.Domains.EmailDomains.Instance -> "instance"

    $"email_domain_refused;reason={reasonWord};rule={levelWord};stage={stage.Value};domain={safeDomain domain}"

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

/// A call to a secret store failed (`secret_store_failed`). The one place the
/// three callers that record it (the admin paths, the delete and the sign-in
/// callback) build the event, so its `Detail` is always `storeDetail` and never
/// something a caller assembled from a value or a locator.
let recordStoreFailure
    (config: Database.DbConfig)
    (clock: TimeProvider)
    (actor: Actor)
    (connectorSlug: string)
    (remoteIp: string option)
    (store: string)
    (op: string)
    (cause: Secrets.StoreFailure)
    : Task<unit> =
    record
        config
        clock
        { event SecretStoreFailed Failure actor with
            ConnectorSlug = Some connectorSlug
            RemoteIp = remoteIp
            Detail = Some(storeDetail store op cause false) }

/// The startup probe of the active store failed (`secret_store_failed`,
/// `op=probe`). Actor System and no connector: nobody asked for it and it is
/// about the store, not any one connector.
let recordStoreProbeFailed
    (config: Database.DbConfig)
    (clock: TimeProvider)
    (store: string)
    (cause: Secrets.StoreFailure)
    : Task<unit> =
    record
        config
        clock
        { event SecretStoreFailed Failure System with
            Detail = Some(storeDetail store "probe" cause false) }

/// A sign-in was served the cached secret because the refresh could not reach
/// the store (`secret_store_failed` with `stale=true`, FR-014). Actor User: the
/// person signing in is the one whose request it happened on.
let recordStoreStale
    (config: Database.DbConfig)
    (clock: TimeProvider)
    (connectorSlug: string)
    (remoteIp: string option)
    (store: string)
    (cause: Secrets.StoreFailure)
    : Task<unit> =
    record
        config
        clock
        { event SecretStoreFailed Failure User with
            ConnectorSlug = Some connectorSlug
            RemoteIp = remoteIp
            Detail = Some(storeDetail store "get" cause true) }
