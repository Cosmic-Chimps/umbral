/// Where the allowed email domains that apply are read from (specs/006).
///
/// A connector's own list lives on its row (`ConnectorStore`); the instance list
/// is one row of `settings`. Every caller that needs "what applies to this
/// connector" asks here, so the callback, the authorize and token endpoints, the
/// Google hint and the admin screens cannot read the two lists differently.
/// Read inside the request that needs it, never cached: a list saved at 15:00 is
/// in effect at 15:00:01.
module Umbral.Infrastructure.EmailDomainRules

open System
open System.Threading.Tasks
open Umbral.Domains
open Umbral.Domains.Connectors
open Umbral.Domains.EmailDomains

/// The `settings` key of the instance list. No row and an empty value both mean
/// there is no instance list.
[<Literal>]
let SettingKey = "allowed_email_domains"

/// The instance list, empty when there is none.
let instanceList (config: Database.DbConfig) : Task<string list> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT value FROM settings WHERE key = @key"
        command.Parameters.AddWithValue("@key", SettingKey) |> ignore
        let! value = command.ExecuteScalarAsync()

        return
            match value with
            | :? string as stored -> deserialise stored
            | _ -> []
    }

/// Replaces the instance list. An empty list removes the row, so "no instance
/// list" has one spelling in the database.
let setInstanceList (config: Database.DbConfig) (clock: TimeProvider) (list: string list) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        match list with
        | [] ->
            command.CommandText <- "DELETE FROM settings WHERE key = @key"
            command.Parameters.AddWithValue("@key", SettingKey) |> ignore
        | _ ->
            command.CommandText <-
                "INSERT INTO settings (key, value, updated_utc) VALUES (@key, @value, @now)
                 ON CONFLICT (key) DO UPDATE SET value = @value, updated_utc = @now"

            command.Parameters.AddWithValue("@key", SettingKey) |> ignore
            command.Parameters.AddWithValue("@value", serialise list) |> ignore
            command.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore

        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

/// What applies to a connector: its own list, else the instance list, else none.
/// The instance list is not read when the connector has its own.
///
/// The passkey kind is never limited by a list, not even the instance's: a
/// passkey carries no email, so there is no domain to judge, and the instance
/// list is not read for it at all. Who may sign in through it is decided by its
/// enrolment mode (specs/008 FR-010). The Fediverse kind is the same: an account
/// there has no email (specs/010 research R11).
let ruleFor (config: Database.DbConfig) (connector: Connector) : Task<Rule> =
    task {
        match connector.Kind, connector.AllowedEmailDomains with
        | Passkey, _
        | Fediverse, _ -> return NoRule
        | _, [] ->
            let! instance = instanceList config
            return applying [] instance
        | _, own -> return applying own []
    }

/// What applies when there is no connector to ask: the instance list alone. For a
/// refresh token whose connector was deleted since it was issued.
let instanceRule (config: Database.DbConfig) : Task<Rule> =
    task {
        let! instance = instanceList config
        return applying [] instance
    }

/// Whether any list is set anywhere: the instance's or any connector's. What
/// decides whether something that cannot say which connector it came from (a
/// refresh token issued before this feature) may still be honoured.
let anyListSet (config: Database.DbConfig) : Task<bool> =
    task {
        let! instance = instanceList config

        if not (List.isEmpty instance) then
            return true
        else
            use! connection = Database.openConnection config
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT count(*) FROM connectors WHERE allowed_email_domains <> ''"
            let! count = command.ExecuteScalarAsync()
            return Convert.ToInt32 count > 0
    }

/// Judges something that remembers a sign-in, by what that sign-in left behind:
/// the slug of the connector it went through and the domain of its verified
/// email. A session, an authorization code and a refresh token are all judged
/// here, so none of them can read the rule differently.
///
/// - a connector that exists: its rule (its own list, else the instance's);
/// - a connector that has since been deleted: the instance list alone;
/// - no connector at all (issued before this feature): refused exactly while
///   some list is set, since nothing says which one would have applied.
let recheck (config: Database.DbConfig) (connectorSlug: string option) (emailDomain: string option) : Task<Decision> =
    task {
        match connectorSlug with
        | Some slug ->
            let! connector = ConnectorStore.findBySlug config slug

            let! rule =
                match connector with
                | Some found -> ruleFor config found
                | None -> instanceRule config

            return checkDomain rule emailDomain
        | None ->
            let! any = anyListSet config
            return if any then Refused(Instance, NoEmail) else Admitted
    }
