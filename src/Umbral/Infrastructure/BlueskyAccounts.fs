/// What the Bluesky connector keeps on a link (specs/015-bluesky-connector,
/// data-model "Storage, schema version 12"): the host of the authorization
/// server the person last signed in through, `linked_accounts.provider_server`.
///
/// Only reads and writes. The callback calls `recordServer` right after the
/// link exists; `recheckServer` judges "Only these servers" again when a
/// remembered sign-in is reused.
module Umbral.Infrastructure.BlueskyAccounts

open System.Threading.Tasks

[<Literal>]
let private RecordServer =
    "UPDATE linked_accounts SET provider_server = @host
      WHERE provider_kind = 'bluesky' AND provider_subject = @did"

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

/// Whether a remembered sign-in through the Bluesky connector must be refused
/// because "Only these servers" no longer lists the person's authorization
/// server. The same reasoning as `FediverseServers.recheckServer`: tightening a
/// rule reaches the people already signed in. `Some host` is the refused server;
/// `None` admits, also for a link that has no server recorded.
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
                    return None
                elif hosts |> Seq.exists (Umbral.Domains.Fediverse.allows c.FediverseAccess) then
                    return None
                else
                    return Some hosts[0]
            | _ -> return None
    }
