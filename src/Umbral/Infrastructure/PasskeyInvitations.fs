/// Invitation links (specs/008-passkey-connector, data-model.md, research R9).
///
/// What is stored is the SHA-256 of the secret in the link, never the secret:
/// the link is shown to the operator once and cannot be recovered from the
/// database. Opening a link only reads; the one write that spends it is
/// `consumeIn`, run in the transaction that inserts the passkey it produced, and
/// it is conditional, so of two simultaneous redemptions exactly one succeeds.
module Umbral.Infrastructure.PasskeyInvitations

open System
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Umbral.Domains

module Rules = Umbral.Domains.Passkeys

/// An invitation as stored. `UserId` None is an invitation for a new person;
/// Some is one that adds a passkey to that existing user.
type Invitation =
    { Id: string
      ConnectorSlug: string
      UserId: string option
      /// The user's display name, for the operator's list (None for a new
      /// person, or when the user is gone).
      UserDisplayName: string option
      /// The operator's own words, 0 to 64 characters; people never see it.
      Note: string
      CreatedUtc: DateTimeOffset
      ExpiresUtc: DateTimeOffset
      UsedUtc: DateTimeOffset option
      RevokedUtc: DateTimeOffset option }

/// What a link can be used for right now.
type State =
    | Open
    | Used
    | Revoked
    | Expired
    /// The invitation is for an existing user who is disabled or no longer there.
    | UserGone

let private parseTime (text: string) =
    DateTimeOffset.Parse(text, Globalization.CultureInfo.InvariantCulture)

/// The invitation, and whether its user exists and is enabled (None for an
/// invitation for a new person, where the question does not arise).
[<Literal>]
let private Columns =
    "SELECT i.id, i.connector_slug, i.user_id, u.display_name, i.note, i.created_utc, i.expires_utc,
            i.used_utc, i.revoked_utc, u.is_enabled
     FROM passkey_invitations i LEFT JOIN users u ON u.id = i.user_id"

let private read (reader: SqliteDataReader) : Invitation * bool option =
    let time (ordinal: int) =
        if reader.IsDBNull ordinal then None else Some(parseTime (reader.GetString ordinal))

    let userId = if reader.IsDBNull 2 then None else Some(reader.GetString 2)

    // A user-bound row whose user is missing or disabled; a row for a new
    // person has no user to ask about.
    let userUsable =
        match userId with
        | None -> None
        | Some _ -> Some(not (reader.IsDBNull 9) && reader.GetInt32 9 = 1)

    { Id = reader.GetString 0
      ConnectorSlug = reader.GetString 1
      UserId = userId
      UserDisplayName = if reader.IsDBNull 3 then None else Some(reader.GetString 3)
      Note = reader.GetString 4
      CreatedUtc = parseTime (reader.GetString 5)
      ExpiresUtc = parseTime (reader.GetString 6)
      UsedUtc = time 7
      RevokedUtc = time 8 },
    userUsable

/// The state of an invitation at `now`. Used and revoked are final and outrank
/// the rest, then time, then the user.
let private stateOf (now: DateTimeOffset) (invitation: Invitation, userUsable: bool option) : State =
    if invitation.UsedUtc.IsSome then Used
    elif invitation.RevokedUtc.IsSome then Revoked
    elif invitation.ExpiresUtc <= now then Expired
    elif userUsable = Some false then UserGone
    else Open

let private query (config: Database.DbConfig) (where: string) (parameters: (string * obj) list) =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- Columns + where

        for name, value in parameters do
            command.Parameters.AddWithValue(name, value) |> ignore

        use! reader = command.ExecuteReaderAsync()
        let results = ResizeArray()
        let mutable go = true

        while go do
            let! hasRow = reader.ReadAsync()
            if hasRow then results.Add(read reader) else go <- false

        return List.ofSeq results
    }

/// How long Umbral keeps a used, revoked or expired invitation before `prune`
/// removes it: long enough for the operator to see what happened to it.
let private keepFinished = TimeSpan.FromDays 30.

/// Removes the invitations that ended more than 30 days ago (used, revoked, or
/// expired). Called by `create`, so a table nobody invites into stays as it is
/// and a busy one cleans itself; no job is needed.
let prune (config: Database.DbConfig) (clock: TimeProvider) : Task<int> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "DELETE FROM passkey_invitations
             WHERE (used_utc IS NOT NULL AND used_utc < @cutoff)
                OR (revoked_utc IS NOT NULL AND revoked_utc < @cutoff)
                OR expires_utc < @cutoff"

        command.Parameters.AddWithValue("@cutoff", (clock.GetUtcNow() - keepFinished).ToString "o") |> ignore
        return! command.ExecuteNonQueryAsync()
    }

/// Creates an invitation on the connector `slug`, for a new person (`userId`
/// None) or for an existing user, and returns it with its secret. The secret is
/// returned here and nowhere else: only its hash is written.
let create
    (config: Database.DbConfig)
    (clock: TimeProvider)
    (slug: string)
    (userId: string option)
    (note: string)
    : Task<Invitation * string> =
    task {
        let! _ = prune config clock

        let secret = Rules.newInvitationSecret ()
        let now = clock.GetUtcNow()
        let id = Ids.randomToken 16
        let expires = now + Rules.InvitationLifetime

        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "INSERT INTO passkey_invitations (id, secret_hash, connector_slug, user_id, note, created_utc, expires_utc)
             VALUES (@id, @hash, @slug, @userId, @note, @now, @expires)"

        command.Parameters.AddWithValue("@id", id) |> ignore
        command.Parameters.AddWithValue("@hash", Rules.hashInvitationSecret secret) |> ignore
        command.Parameters.AddWithValue("@slug", slug) |> ignore

        command.Parameters.AddWithValue(
            "@userId",
            (match userId with
             | Some user -> user :> obj
             | None -> DBNull.Value :> obj)
        )
        |> ignore

        command.Parameters.AddWithValue("@note", note) |> ignore
        command.Parameters.AddWithValue("@now", now.ToString "o") |> ignore
        command.Parameters.AddWithValue("@expires", expires.ToString "o") |> ignore
        let! _ = command.ExecuteNonQueryAsync()

        let invitation =
            { Id = id
              ConnectorSlug = slug
              UserId = userId
              UserDisplayName = None
              Note = note
              CreatedUtc = now
              ExpiresUtc = expires
              UsedUtc = None
              RevokedUtc = None }

        return invitation, secret
    }

/// The longest secret worth looking up: a real one is 43 characters, and nothing
/// longer is hashed on behalf of a stranger.
[<Literal>]
let private LongestSecret = 128

/// What a link in a browser's address bar names, and what state it is in. A
/// read: nothing is spent. None for a secret no invitation has (or one too long
/// to be anyone's).
let findBySecret
    (config: Database.DbConfig)
    (clock: TimeProvider)
    (secret: string)
    : Task<(Invitation * State) option> =
    task {
        if String.IsNullOrEmpty secret || secret.Length > LongestSecret then
            return None
        else
            let! rows =
                query config " WHERE i.secret_hash = @hash" [ "@hash", Rules.hashInvitationSecret secret :> obj ]

            let now = clock.GetUtcNow()

            return
                rows
                |> List.tryHead
                |> Option.map (fun row -> fst row, stateOf now row)
    }

/// The open invitations of a connector, newest first: what the operator's card
/// lists. Used, revoked, expired and user-gone ones are not shown.
let listOpen (config: Database.DbConfig) (clock: TimeProvider) (slug: string) : Task<Invitation list> =
    task {
        let! rows =
            query
                config
                " WHERE i.connector_slug = @slug AND i.used_utc IS NULL AND i.revoked_utc IS NULL
                    ORDER BY i.created_utc DESC, i.id"
                [ "@slug", slug :> obj ]

        let now = clock.GetUtcNow()

        return
            rows
            |> List.filter (fun row -> stateOf now row = Open)
            |> List.map fst
    }

/// Withdraws an invitation that is still open. False when it was not (unknown,
/// used, revoked or expired), and nothing changes.
let revoke (config: Database.DbConfig) (clock: TimeProvider) (id: string) : Task<bool> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "UPDATE passkey_invitations SET revoked_utc = @now
             WHERE id = @id AND used_utc IS NULL AND revoked_utc IS NULL AND expires_utc > @now"

        command.Parameters.AddWithValue("@id", id) |> ignore
        command.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore
        let! changed = command.ExecuteNonQueryAsync()
        return changed > 0
    }

/// Spends an invitation inside the caller's transaction, the one that inserts the
/// passkey it produced. True when this call spent it; false when it was already
/// used, revoked or expired, in which case the caller rolls back and refuses.
/// One conditional statement is what makes "exactly one of two simultaneous
/// redemptions succeeds" true.
let consumeIn
    (connection: SqliteConnection)
    (transaction: SqliteTransaction)
    (clock: TimeProvider)
    (id: string)
    : Task<bool> =
    task {
        use command = connection.CreateCommand()
        command.Transaction <- transaction

        command.CommandText <-
            "UPDATE passkey_invitations SET used_utc = @now
             WHERE id = @id AND used_utc IS NULL AND revoked_utc IS NULL AND expires_utc > @now"

        command.Parameters.AddWithValue("@id", id) |> ignore
        command.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore
        let! changed = command.ExecuteNonQueryAsync()
        return changed > 0
    }
