/// Turning a provider profile into an Umbral user.
///
/// The decision itself lives in `Domains.Linking` as a pure function; this is
/// only the part that reads and writes. Keeping them apart is deliberate: the
/// rule that decides whether two sign-ins are the same person is the one place
/// a bug hands over someone's account, and it should be reviewable without a
/// database in the way.
module Umbral.Infrastructure.UserStore

open System
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Umbral.Domains
open Umbral.Domains.Connectors
open Umbral.Infrastructure.ProviderAdapters

type Resolution =
    { User: Users.User
      /// What happened, for the security event. The operator should be able to
      /// tell a new account from a linked one after the fact.
      Decision: Linking.Decision
      Reason: Linking.Reason }

let private findLinkedUserId (connection: SqliteConnection) (kind: ProviderKind) (subject: string) =
    task {
        use command = connection.CreateCommand()

        command.CommandText <-
            "SELECT user_id FROM linked_accounts WHERE provider_kind = @kind AND provider_subject = @subject"

        command.Parameters.AddWithValue("@kind", kind.Value) |> ignore
        command.Parameters.AddWithValue("@subject", subject) |> ignore
        let! value = command.ExecuteScalarAsync()
        return if isNull value then None else Some(string value)
    }

/// Users whose email matches AND is verified.
///
/// The `email_verified = 1` clause is repeated here even though the supporting
/// index is already partial. The linking rule refuses unverified candidates
/// too, so linking on an unverified address would take two independent
/// mistakes — in a query and in a pure function reviewed separately.
let private findVerifiedEmailMatches (connection: SqliteConnection) (email: string) =
    task {
        use command = connection.CreateCommand()

        command.CommandText <-
            "SELECT id, email_verified FROM users WHERE email = @email COLLATE NOCASE AND email_verified = 1"

        command.Parameters.AddWithValue("@email", email) |> ignore

        use! reader = command.ExecuteReaderAsync()
        let results = ResizeArray()
        let mutable go = true

        while go do
            let! hasRow = reader.ReadAsync()

            if hasRow then
                let candidate: Linking.Candidate =
                    { UserId = reader.GetString 0
                      EmailVerified = reader.GetInt32 1 = 1 }

                results.Add candidate
            else
                go <- false

        return List.ofSeq results
    }

/// Resolve a profile to a user, creating or linking as the rule directs.
///
/// One transaction: the user row, its linked account and the timestamps commit
/// together. A half-written identity is worse than a failed sign-in, because
/// the next attempt would see a user with no way to reach them.
let resolve
    (config: Database.DbConfig)
    (clock: TimeProvider)
    (kind: ProviderKind)
    (profile: ProviderProfile)
    : Task<Resolution> =
    task {
        use! connection = Database.openConnection config
        use transaction = connection.BeginTransaction()

        let! existingLink = findLinkedUserId connection kind profile.Subject

        let incoming: Linking.IncomingIdentity =
            { ProviderKind = kind.Value
              ProviderSubject = profile.Subject
              Email = profile.Email
              EmailVerified = profile.EmailVerified }

        // Only looked up when it could matter: an unverified or absent email
        // can never produce a link, so there is nothing to ask the database.
        let! candidates =
            match profile.Email with
            | Some email when profile.EmailVerified && existingLink.IsNone ->
                findVerifiedEmailMatches connection email
            | _ -> Task.FromResult []

        let outcome = Linking.decide existingLink incoming candidates
        let now = clock.GetUtcNow()
        let nowText = now.ToString "o"

        let optional (v: string option) : obj =
            match v with
            | Some s -> s :> obj
            | None -> DBNull.Value :> obj

        // The users row's flag speaks for its address, so it needs one: a
        // provider that says "verified" with no address (or a blank one) has
        // verified nothing Umbral stores. The linked account keeps the raw
        // assertion below.
        let verifiesAnAddress =
            profile.EmailVerified
            && (match profile.Email with
                | Some e -> not (String.IsNullOrWhiteSpace e)
                | None -> false)

        let userId =
            match outcome.Decision with
            | Linking.ExistingLink id
            | Linking.LinkToExisting id -> id
            | Linking.CreateNew -> Ids.newSubject ()

        match outcome.Decision with
        | Linking.CreateNew ->
            use insert = connection.CreateCommand()
            insert.Transaction <- transaction

            insert.CommandText <-
                "INSERT INTO users (id, display_name, email, email_verified, avatar_url, is_enabled,
                                    first_seen_utc, last_seen_utc, preferred_username, profile_url)
                 VALUES (@id, @name, @email, @verified, @avatar, 1, @now, @now, @username, @profile)"

            insert.Parameters.AddWithValue("@id", userId) |> ignore

            insert.Parameters.AddWithValue(
                "@name",
                profile.DisplayName |> Option.defaultValue (kind.DisplayName + " user")
            )
            |> ignore

            insert.Parameters.AddWithValue("@email", optional profile.Email) |> ignore
            insert.Parameters.AddWithValue("@verified", (if verifiesAnAddress then 1 else 0)) |> ignore
            insert.Parameters.AddWithValue("@avatar", optional profile.AvatarUrl) |> ignore
            insert.Parameters.AddWithValue("@username", optional profile.Username) |> ignore
            insert.Parameters.AddWithValue("@profile", optional profile.ProfileUrl) |> ignore
            insert.Parameters.AddWithValue("@now", nowText) |> ignore
            let! _ = insert.ExecuteNonQueryAsync()
            ()

        | Linking.ExistingLink _
        | Linking.LinkToExisting _ ->
            use update = connection.CreateCommand()
            update.Transaction <- transaction

            // Refresh the display fields, but never the email's verified flag
            // downwards on an existing user: a provider that stops asserting
            // verification must not silently unpick an earlier link.
            //
            // The address is the first one seen, so the flag goes up only when
            // the provider verifies THAT address (or none is stored yet and this
            // one fills it in). Raising it for whatever address the provider
            // verifies today would mark a never-proved first address as
            // verified, and the linking query would then join that address's
            // real owner into this user (task h762). The comparison is the
            // linking query's own, COLLATE NOCASE. In an UPDATE, `email` on the
            // right-hand side is the stored value, before this statement.
            update.CommandText <-
                "UPDATE users
                 SET display_name  = COALESCE(@name, display_name),
                     avatar_url    = COALESCE(@avatar, avatar_url),
                     preferred_username = COALESCE(@username, preferred_username),
                     profile_url   = COALESCE(@profile, profile_url),
                     email         = COALESCE(email, @email),
                     email_verified= CASE WHEN @verified = 1 AND (email IS NULL OR email = @email COLLATE NOCASE)
                                          THEN 1 ELSE email_verified END,
                     last_seen_utc = @now
                 WHERE id = @id"

            update.Parameters.AddWithValue("@name", optional profile.DisplayName) |> ignore
            update.Parameters.AddWithValue("@avatar", optional profile.AvatarUrl) |> ignore
            update.Parameters.AddWithValue("@username", optional profile.Username) |> ignore
            update.Parameters.AddWithValue("@profile", optional profile.ProfileUrl) |> ignore
            update.Parameters.AddWithValue("@email", optional profile.Email) |> ignore
            update.Parameters.AddWithValue("@verified", (if verifiesAnAddress then 1 else 0)) |> ignore
            update.Parameters.AddWithValue("@now", nowText) |> ignore
            update.Parameters.AddWithValue("@id", userId) |> ignore
            let! _ = update.ExecuteNonQueryAsync()
            ()

        // Upsert the link. The primary key on (kind, subject) is what stops two
        // simultaneous first sign-ins from creating two users for one provider
        // account — the loser of the race updates instead of inserting.
        use link = connection.CreateCommand()
        link.Transaction <- transaction

        link.CommandText <-
            "INSERT INTO linked_accounts
                 (provider_kind, provider_subject, user_id, asserted_email, asserted_email_verified,
                  linked_utc, last_used_utc)
             VALUES (@kind, @subject, @userId, @email, @verified, @now, @now)
             ON CONFLICT (provider_kind, provider_subject) DO UPDATE SET
                 asserted_email          = @email,
                 asserted_email_verified = @verified,
                 last_used_utc           = @now"

        link.Parameters.AddWithValue("@kind", kind.Value) |> ignore
        link.Parameters.AddWithValue("@subject", profile.Subject) |> ignore
        link.Parameters.AddWithValue("@userId", userId) |> ignore
        link.Parameters.AddWithValue("@email", optional profile.Email) |> ignore
        link.Parameters.AddWithValue("@verified", (if profile.EmailVerified then 1 else 0)) |> ignore
        link.Parameters.AddWithValue("@now", nowText) |> ignore
        let! _ = link.ExecuteNonQueryAsync()

        do! transaction.CommitAsync()

        match! Users.findById config userId with
        | Some user ->
            return
                { User = user
                  Decision = outcome.Decision
                  Reason = outcome.Reason }
        | None -> return failwith "The user row vanished between its own commit and read."
    }

/// The kind and the provider's subject of each account a user has linked, oldest
/// first. The subject is not shown as it is: for an `oidc` account it carries the
/// issuer, which the Users screen turns into the connector's name
/// (`Domains.Connectors.parseOidcSubject`).
let linkedAccounts (config: Database.DbConfig) (userId: string) : Task<(string * string) list> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "SELECT provider_kind, provider_subject FROM linked_accounts WHERE user_id = @id ORDER BY linked_utc"

        command.Parameters.AddWithValue("@id", userId) |> ignore

        use! reader = command.ExecuteReaderAsync()
        let results = ResizeArray()
        let mutable go = true

        while go do
            let! hasRow = reader.ReadAsync()

            if hasRow then
                results.Add((reader.GetString 0, reader.GetString 1))
            else
                go <- false

        return List.ofSeq results
    }

/// The providers a user has linked, for the Users screen. Nothing more than the
/// kinds — the screen shows which providers, never a token or a provider id.
let linkedKinds (config: Database.DbConfig) (userId: string) : Task<string list> =
    task {
        let! accounts = linkedAccounts config userId
        return accounts |> List.map fst
    }

// ---------------------------------------------------------------------------
// Removing one linked provider account (specs/018-remove-a-link)
// ---------------------------------------------------------------------------

/// One of a user's linked provider accounts with its dates, for the Users
/// screen. The subject is the stored one (for `oidc`, `issuer#sub`); the screen
/// never prints it and names the account by `Linking.fingerprint`.
type LinkedAccount =
    { Kind: string
      Subject: string
      LinkedUtc: DateTimeOffset
      LastUsedUtc: DateTimeOffset }

/// The user's links with their dates, oldest first (the screen's list).
let linkedAccountDetails (config: Database.DbConfig) (userId: string) : Task<LinkedAccount list> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "SELECT provider_kind, provider_subject, linked_utc, last_used_utc
             FROM linked_accounts WHERE user_id = @id ORDER BY linked_utc, provider_kind, provider_subject"

        command.Parameters.AddWithValue("@id", userId) |> ignore

        use! reader = command.ExecuteReaderAsync()
        let results = ResizeArray()
        let mutable go = true

        while go do
            let! hasRow = reader.ReadAsync()

            if hasRow then
                results.Add
                    { Kind = reader.GetString 0
                      Subject = reader.GetString 1
                      LinkedUtc = DateTimeOffset.Parse(reader.GetString 2, Globalization.CultureInfo.InvariantCulture)
                      LastUsedUtc = DateTimeOffset.Parse(reader.GetString 3, Globalization.CultureInfo.InvariantCulture) }
            else
                go <- false

        return List.ofSeq results
    }

type RemoveLinkOutcome =
    /// The link is gone and recorded in `removed_links`, with the passkeys added
    /// through it (`passkeys`, transitively). `lowered` is whether the user's
    /// `email_verified` fell because only this link backed it.
    | LinkRemoved of kind: string * subject: string * lowered: bool * passkeys: int
    /// It would leave no way in (no other link, no passkey): nothing changed.
    | LinkLastWayIn
    /// The user has no link with that fingerprint: nothing changed.
    | LinkNotFound

[<Literal>]
let private DeletePasskeysAddedViaLink =
    "WITH RECURSIVE gone(id) AS (
         SELECT id FROM passkeys WHERE user_id = @userId AND added_via = @linkRef
         UNION
         SELECT p.id FROM passkeys p JOIN gone g ON p.added_via = 'passkey:' || g.id
          WHERE p.user_id = @userId)
     DELETE FROM passkeys WHERE id IN (SELECT id FROM gone)"

[<Literal>]
let private DeleteLinkUnlessLast =
    "DELETE FROM linked_accounts
     WHERE provider_kind = @kind AND provider_subject = @subject AND user_id = @userId
       AND ((SELECT count(*) FROM linked_accounts WHERE user_id = @userId) > 1
            OR EXISTS (SELECT 1 FROM passkeys WHERE user_id = @userId))"

[<Literal>]
let private ReadUserLinks =
    "SELECT provider_kind, provider_subject FROM linked_accounts WHERE user_id = @userId"

[<Literal>]
let private RecordRemovedLink =
    "INSERT INTO removed_links (provider_kind, provider_subject, removed_utc)
     VALUES (@kind, @subject, @now) ON CONFLICT DO NOTHING"

/// FR-013. Records that this user had a sign-in method removed, so their
/// sessions and tokens with no credential reference are refused from now on.
[<Literal>]
let private RecordLegacySessionsEnded =
    "INSERT INTO legacy_sessions_ended (user_id, ended_utc) VALUES (@userId, @now) ON CONFLICT DO NOTHING"

[<Literal>]
let private LegacySessionsEnded =
    "SELECT EXISTS (SELECT 1 FROM legacy_sessions_ended WHERE user_id = @userId)"

/// `Database.UnbackedEmailVerified` under a local name, so the SQL below is built
/// from a constant of this file (the shape MetaTests accepts).
[<Literal>]
let private UnbackedEmailVerified = Database.UnbackedEmailVerified

[<Literal>]
let private LowerUnbackedFlag =
    "UPDATE users SET email_verified = 0 WHERE id = @userId AND " + UnbackedEmailVerified

/// FR-013. True when a sign-in method of this user was removed, so their
/// sessions and tokens with no credential reference are refused. The user-info
/// endpoint uses it for an access token issued before the credential claim.
let legacySessionsEnded (config: Database.DbConfig) (userId: string) : Task<bool> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- LegacySessionsEnded
        command.Parameters.AddWithValue("@userId", userId) |> ignore
        let! value = command.ExecuteScalarAsync()
        return Convert.ToInt32 value = 1
    }

/// FR-003. Removes one linked provider account from a user, in one transaction:
/// the passkeys added through it go first (transitively), then the link goes only
/// if the user keeps a way in among what remains (another link or a passkey),
/// with the rule in the DELETE's own condition (as `Passkeys.removeOwnIfNotLast`)
/// so two removals cannot both succeed; a refusal rolls back the passkeys too.
/// The provider account is recorded in `removed_links`, the user in
/// `legacy_sessions_ended` (FR-013), and `email_verified` falls where no
/// remaining link backs it.
let removeLink
    (config: Database.DbConfig)
    (clock: TimeProvider)
    (userId: string)
    (fingerprint: string)
    : Task<RemoveLinkOutcome>
    =
    task {
        use! connection = Database.openConnection config
        use transaction = connection.BeginTransaction()

        use read = connection.CreateCommand()
        read.Transaction <- transaction
        read.CommandText <- ReadUserLinks
        read.Parameters.AddWithValue("@userId", userId) |> ignore

        let held = ResizeArray<string * string>()
        let! reader = read.ExecuteReaderAsync()
        let mutable go = true

        while go do
            let! hasRow = reader.ReadAsync()

            if hasRow then
                held.Add((reader.GetString 0, reader.GetString 1))
            else
                go <- false

        do! reader.DisposeAsync()

        match held |> Seq.tryFind (fun (kind, subject) -> Linking.fingerprint kind subject = fingerprint) with
        | None ->
            do! transaction.RollbackAsync()
            return LinkNotFound
        | Some(kind, subject) ->
            use passkeys = connection.CreateCommand()
            passkeys.Transaction <- transaction
            passkeys.CommandText <- DeletePasskeysAddedViaLink
            passkeys.Parameters.AddWithValue("@userId", userId) |> ignore

            passkeys.Parameters.AddWithValue("@linkRef", Linking.credentialValue (Linking.LinkCredential fingerprint))
            |> ignore

            let! passkeysGone = passkeys.ExecuteNonQueryAsync()

            use delete = connection.CreateCommand()
            delete.Transaction <- transaction
            delete.CommandText <- DeleteLinkUnlessLast
            delete.Parameters.AddWithValue("@kind", kind) |> ignore
            delete.Parameters.AddWithValue("@subject", subject) |> ignore
            delete.Parameters.AddWithValue("@userId", userId) |> ignore
            let! deleted = delete.ExecuteNonQueryAsync()

            if deleted = 0 then
                do! transaction.RollbackAsync()
                return LinkLastWayIn
            else
                use record = connection.CreateCommand()
                record.Transaction <- transaction
                record.CommandText <- RecordRemovedLink
                record.Parameters.AddWithValue("@kind", kind) |> ignore
                record.Parameters.AddWithValue("@subject", subject) |> ignore
                record.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore
                let! _ = record.ExecuteNonQueryAsync()

                use ended = connection.CreateCommand()
                ended.Transaction <- transaction
                ended.CommandText <- RecordLegacySessionsEnded
                ended.Parameters.AddWithValue("@userId", userId) |> ignore
                ended.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore
                let! _ = ended.ExecuteNonQueryAsync()

                use lower = connection.CreateCommand()
                lower.Transaction <- transaction
                lower.CommandText <- LowerUnbackedFlag
                lower.Parameters.AddWithValue("@userId", userId) |> ignore
                let! lowered = lower.ExecuteNonQueryAsync()

                do! transaction.CommitAsync()
                return LinkRemoved(kind, subject, lowered > 0, passkeysGone)
    }

/// FR-006. Whether this provider account was removed from a user before: such a
/// sign-in is treated as unverified.
let isRemoved (config: Database.DbConfig) (kind: string) (subject: string) : Task<bool> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "SELECT EXISTS (SELECT 1 FROM removed_links WHERE provider_kind = @kind AND provider_subject = @subject)"

        command.Parameters.AddWithValue("@kind", kind) |> ignore
        command.Parameters.AddWithValue("@subject", subject) |> ignore
        let! value = command.ExecuteScalarAsync()
        return Convert.ToInt32 value = 1
    }

[<Literal>]
let private PasskeyHeld =
    "SELECT EXISTS (SELECT 1 FROM passkeys WHERE user_id = @userId AND id = @id)"

/// FR-009. Whether a remembered sign-in (a session, a code or a refresh token)
/// made through `connectorSlug` and, when it has one, the credential reference
/// (`link:<fp>` or `passkey:<id>`), may go on: true admits, false refuses.
///
/// - `link:<fp>`: true when one of the user's links has that fingerprint.
/// - `passkey:<id>`: true when the user has a passkey with that row id.
/// - A value that does not parse: false (fails closed).
/// - No reference and no connector slug: true (the connector re-checks own it).
/// - No reference (made before this version), the user recorded in
///   `legacy_sessions_ended` (a sign-in method of theirs was removed, FR-013):
///   false, for every connector kind, the passkey one included. Checked first.
/// - No reference, the user not recorded: judged by the connector. The
///   passkey kind, or a connector that no longer exists, is true. For `oidc`,
///   true when the user has an `oidc` link whose subject starts with the
///   connector's issuer and `#` (false with no issuer). Another kind: true when
///   the user has a link of that kind.
let credentialStillHeld
    (config: Database.DbConfig)
    (userId: string)
    (connectorSlug: string option)
    (credential: string option)
    : Task<bool>
    =
    task {
        // A reference is judged whenever there is one, with or without a slug:
        // the slug only matters to the fallback for sessions made before it.
        match credential |> Option.map Linking.parseCredential, connectorSlug with
            | Some None, _ -> return false
            | Some(Some(Linking.LinkCredential fingerprint)), _ ->
                let! links = linkedAccountDetails config userId

                return
                    links
                    |> List.exists (fun l -> Linking.fingerprint l.Kind l.Subject = fingerprint)
            | Some(Some(Linking.PasskeyCredential id)), _ ->
                use! connection = Database.openConnection config
                use command = connection.CreateCommand()
                command.CommandText <- PasskeyHeld
                command.Parameters.AddWithValue("@userId", userId) |> ignore
                command.Parameters.AddWithValue("@id", id) |> ignore
                let! value = command.ExecuteScalarAsync()
                return Convert.ToInt32 value = 1
            | None, None -> return true
            | None, Some slug ->
                use! connection = Database.openConnection config
                use ended = connection.CreateCommand()
                ended.CommandText <- LegacySessionsEnded
                ended.Parameters.AddWithValue("@userId", userId) |> ignore
                let! recorded = ended.ExecuteScalarAsync()

                if Convert.ToInt32 recorded = 1 then
                    return false
                else
                    let! connector = ConnectorStore.findBySlug config slug

                    match connector with
                    | None -> return true
                    | Some c when c.Kind = ProviderKind.Passkey -> return true
                    | Some c ->
                        let! links = linkedAccountDetails config userId

                        if c.Kind = ProviderKind.Oidc then
                            return
                                match c.IssuerUrl with
                                | Some issuer ->
                                    links
                                    |> List.exists (fun l ->
                                        l.Kind = ProviderKind.Oidc.Value && l.Subject.StartsWith(issuer + "#", StringComparison.Ordinal))
                                | None -> false
                        else
                            return links |> List.exists (fun l -> l.Kind = c.Kind.Value)
    }
