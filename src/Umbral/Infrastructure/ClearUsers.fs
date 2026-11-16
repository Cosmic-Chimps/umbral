/// Deletes every end user and everything they hold, and keeps the instance's
/// setup. Behind the `clear-users` command, for two callers: the nightly reset
/// of a demo instance, and an operator who wants to start over without
/// redoing the connectors and apps.
///
/// One transaction, so the clearing and its `users_cleared` event commit
/// together or not at all: a failure leaves every user as it was, and a
/// cleared instance never lacks its record.
///
/// Gone: users, their linked accounts, passkeys and passkey invitations, the
/// passkey ceremonies in progress, the records of removed links and ended
/// legacy sessions, and OpenIddict's tokens and authorizations (so every
/// session and refresh token stops working). The security log goes only when
/// asked.
///
/// Kept: the operator credential and operator passkeys, connectors,
/// `fediverse_servers`, apps with their branding and connector choices,
/// `declared_items`, settings and the signing, encryption and data-protection
/// keys (files, not rows).
///
/// Every table is deleted from explicitly, children first, rather than relying
/// on ON DELETE CASCADE: the command says what it removes, and a table with no
/// foreign key (`removed_links`, `passkey_ceremonies`) would otherwise stay.
module Umbral.Infrastructure.ClearUsers

open System
open System.Threading.Tasks
open Microsoft.Data.Sqlite

/// What `clear` removed. `Tokens` counts OpenIddict's token rows.
type Cleared =
    { Users: int
      LinkedAccounts: int
      Passkeys: int
      Tokens: int
      SecurityLogCleared: bool }

/// How many users there are, for the run that was not confirmed. Changes nothing.
let count (config: Database.DbConfig) : Task<int> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT COUNT(*) FROM users"
        let! value = command.ExecuteScalarAsync()
        return Convert.ToInt32 value
    }

/// The counts the output reports, read inside the transaction just before the
/// deletes, so they are exactly what goes.
[<Literal>]
let private CountHeld =
    "SELECT (SELECT COUNT(*) FROM users), (SELECT COUNT(*) FROM linked_accounts), (SELECT COUNT(*) FROM passkeys)"

/// A data directory that never started has no OpenIddict tables: the live
/// process applies their migrations at its start, and this command does not.
[<Literal>]
let private OpenIddictPresent =
    "SELECT COUNT(*) FROM sqlite_master
     WHERE type = 'table' AND name IN ('OpenIddictTokens', 'OpenIddictAuthorizations')"

[<Literal>]
let private CountTokens = "SELECT COUNT(*) FROM OpenIddictTokens"

/// Every token and authorization: all of them belong to end users (the
/// operator signs in with a cookie of its own, not through OpenIddict).
/// Tokens first, since they point at their authorization.
[<Literal>]
let private DeleteOpenIddict =
    "DELETE FROM OpenIddictTokens;
     DELETE FROM OpenIddictAuthorizations;"

/// Children first, then the users. Every passkey invitation and ceremony goes,
/// including those for a person who has no user yet: each was made for a person.
[<Literal>]
let private DeleteUsers =
    "DELETE FROM passkey_ceremonies;
     DELETE FROM passkey_invitations;
     DELETE FROM passkeys;
     DELETE FROM linked_accounts;
     DELETE FROM legacy_sessions_ended;
     DELETE FROM removed_links;
     DELETE FROM users;"

[<Literal>]
let private DeleteSecurityLog = "DELETE FROM security_events"

let private commandIn (connection: SqliteConnection) (transaction: SqliteTransaction) =
    let command = connection.CreateCommand()
    command.Transaction <- transaction
    command

/// Delete every user and what they hold; also the security log when
/// `clearSecurityLog`. Records one `users_cleared` event naming only the
/// number of users (no id, name or subject). Any exception rolls everything
/// back and propagates.
let clear (config: Database.DbConfig) (clock: TimeProvider) (clearSecurityLog: bool) : Task<Cleared> =
    task {
        use! connection = Database.openConnection config
        // `BeginTransaction()` is BEGIN IMMEDIATE in Microsoft.Data.Sqlite
        // (only `deferred = true` makes it BEGIN): the write lock is taken
        // here, waiting up to busy_timeout for the live process, so no write
        // of its own can fail halfway. Disposed without a commit, the
        // transaction rolls back.
        use transaction = connection.BeginTransaction()

        use counts = commandIn connection transaction
        counts.CommandText <- CountHeld
        let! users, linkedAccounts, passkeys =
            task {
                use! reader = counts.ExecuteReaderAsync()
                let! _ = reader.ReadAsync()
                return reader.GetInt32 0, reader.GetInt32 1, reader.GetInt32 2
            }

        use present = commandIn connection transaction
        present.CommandText <- OpenIddictPresent
        let! openIddictTables = present.ExecuteScalarAsync()

        let! tokens =
            task {
                if Convert.ToInt32 openIddictTables = 2 then
                    use count = commandIn connection transaction
                    count.CommandText <- CountTokens
                    let! value = count.ExecuteScalarAsync()
                    use delete = commandIn connection transaction
                    delete.CommandText <- DeleteOpenIddict
                    let! _ = delete.ExecuteNonQueryAsync()
                    return Convert.ToInt32 value
                else
                    return 0
            }

        use deleteUsers = commandIn connection transaction
        deleteUsers.CommandText <- DeleteUsers
        let! _ = deleteUsers.ExecuteNonQueryAsync()

        if clearSecurityLog then
            use deleteLog = commandIn connection transaction
            deleteLog.CommandText <- DeleteSecurityLog
            let! _ = deleteLog.ExecuteNonQueryAsync()
            ()

        let detail =
            if clearSecurityLog then
                $"users={users};security_log=cleared"
            else
                $"users={users}"

        do!
            SecurityEvents.recordIn
                connection
                transaction
                clock
                { SecurityEvents.event SecurityEvents.UsersCleared SecurityEvents.Success SecurityEvents.Actor.System with
                    Detail = Some detail }

        do! transaction.CommitAsync()

        return
            { Users = users
              LinkedAccounts = linkedAccounts
              Passkeys = passkeys
              Tokens = tokens
              SecurityLogCleared = clearSecurityLog }
    }
