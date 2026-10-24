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
            insert.Parameters.AddWithValue("@verified", (if profile.EmailVerified then 1 else 0)) |> ignore
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
            update.CommandText <-
                "UPDATE users
                 SET display_name  = COALESCE(@name, display_name),
                     avatar_url    = COALESCE(@avatar, avatar_url),
                     preferred_username = COALESCE(@username, preferred_username),
                     profile_url   = COALESCE(@profile, profile_url),
                     email         = COALESCE(email, @email),
                     email_verified= CASE WHEN @verified = 1 THEN 1 ELSE email_verified END,
                     last_seen_utc = @now
                 WHERE id = @id"

            update.Parameters.AddWithValue("@name", optional profile.DisplayName) |> ignore
            update.Parameters.AddWithValue("@avatar", optional profile.AvatarUrl) |> ignore
            update.Parameters.AddWithValue("@username", optional profile.Username) |> ignore
            update.Parameters.AddWithValue("@profile", optional profile.ProfileUrl) |> ignore
            update.Parameters.AddWithValue("@email", optional profile.Email) |> ignore
            update.Parameters.AddWithValue("@verified", (if profile.EmailVerified then 1 else 0)) |> ignore
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
