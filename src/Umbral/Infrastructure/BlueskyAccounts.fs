/// What the Bluesky connector keeps on a link (specs/015-bluesky-connector,
/// data-model "Storage, schema version 12"): the host of the authorization
/// server the person last signed in through, `linked_accounts.provider_server`.
///
/// Only reads and writes. The callback calls `recordServer` before it finishes
/// the sign-in (for a link that exists) and after it (for one it just created);
/// `recheckServer` judges "Only these servers" again when a remembered sign-in is
/// reused, and `clearHandle` removes a handle that is no longer verified.
module Umbral.Infrastructure.BlueskyAccounts

open System.Threading.Tasks

[<Literal>]
let private RecordServer =
    "UPDATE linked_accounts SET provider_server = @host
      WHERE provider_kind = 'bluesky' AND provider_subject = @did"

[<Literal>]
let private ClearHandle =
    "UPDATE users SET preferred_username = NULL
      WHERE id = (SELECT user_id FROM linked_accounts
                   WHERE provider_kind = 'bluesky' AND provider_subject = @did)"

[<Literal>]
let private LinkServers =
    "SELECT provider_server FROM linked_accounts
      WHERE user_id = @user AND provider_kind = 'bluesky' AND provider_server IS NOT NULL"

/// Remembers the authorization server a Bluesky link just signed in through.
/// Only the `bluesky` row of this DID is written.
let recordServer (config: Database.DbConfig) (did: string) (host: string) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- RecordServer
        command.Parameters.AddWithValue("@host", host) |> ignore
        command.Parameters.AddWithValue("@did", did) |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

/// Clears the `preferred_username` of the user this DID signs in as: after a
/// sign-in that verified no handle, an old one must not stay (`UserStore` keeps
/// a stored value when it is given none, and Bluesky handles move).
let clearHandle (config: Database.DbConfig) (did: string) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- ClearHandle
        command.Parameters.AddWithValue("@did", did) |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

/// Whether a remembered sign-in through the Bluesky connector must be refused
/// because "Only these servers" no longer lists the person's authorization
/// server. The same reasoning as `FediverseServers.recheckServer`: tightening a
/// rule reaches the people already signed in. `Some host` is the refused server;
/// `None` admits. It fails closed: under "Only these servers" a user whose
/// Bluesky links have no server recorded is refused (`Some ""`, as the Fediverse
/// one does when it reads no host), because no link can predate the column.
let recheckServer (config: Database.DbConfig) (connectorSlug: string option) (userId: string) : Task<string option> =
    task {
        match connectorSlug with
        | None -> return None
        | Some slug ->
            let! connector = ConnectorStore.findBySlug config slug

            match connector with
            | Some c when c.Kind = Umbral.Domains.Connectors.Bluesky && c.FediverseAccess.OnlyListed ->
                use! connection = Database.openConnection config
                use command = connection.CreateCommand()
                command.CommandText <- LinkServers
                command.Parameters.AddWithValue("@user", userId) |> ignore
                use! reader = command.ExecuteReaderAsync()
                let hosts = ResizeArray<string>()

                while reader.Read() do
                    hosts.Add(reader.GetString 0)

                if hosts.Count = 0 then
                    return Some ""
                elif hosts |> Seq.exists (Umbral.Domains.Fediverse.allows c.FediverseAccess) then
                    return None
                else
                    return Some hosts[0]
            | _ -> return None
    }
