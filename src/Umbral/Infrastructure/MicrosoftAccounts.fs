/// What the Microsoft connector re-checks on a remembered sign-in
/// (specs/016-microsoft-connector, FR-011 and research R3, last paragraph).
///
/// Only reads. The audience is chosen by the operator and the tenant of a person
/// is part of the link (`provider_subject` is `<tid>#<oid>`), so narrowing the
/// audience can leave signed-in people outside it; `recheckTenant` is how the
/// narrowing reaches them, the same way `FediverseServers.recheckServer` and
/// `BlueskyAccounts.recheckServer` do for their server lists.
module Umbral.Infrastructure.MicrosoftAccounts

open System.Threading.Tasks

[<Literal>]
let private LinkSubjects =
    "SELECT provider_subject FROM linked_accounts
      WHERE user_id = @user AND provider_kind = 'microsoft'"

/// Whether a remembered sign-in through a Microsoft connector must be refused
/// because the connector's audience no longer admits the tenant of the person
/// (specs/016 FR-011). `Some tid` is the refused tenant (canonical string);
/// `None` admits. A person is admitted when any one of their Microsoft links is
/// in the audience. It fails closed: a Microsoft connector whose audience cannot
/// be read is refused (`Some ""`, or the first tenant read), and so is a user
/// with no readable Microsoft link (`Some ""`). No slug, or a connector of
/// another kind, admits: the rule belongs to the Microsoft kind only. The
/// callers refuse that case first (`ConnectorStore.recheckKind`, task s1ug), so
/// a session or token made through Microsoft never reaches another kind's rules.
let recheckTenant (config: Database.DbConfig) (connectorSlug: string option) (userId: string) : Task<string option> =
    task {
        match connectorSlug with
        | None -> return None
        | Some slug ->
            let! connector = ConnectorStore.findBySlug config slug

            match connector with
            | Some c when c.Kind = Umbral.Domains.Connectors.Microsoft ->
                use! connection = Database.openConnection config
                use command = connection.CreateCommand()
                command.CommandText <- LinkSubjects
                command.Parameters.AddWithValue("@user", userId) |> ignore
                use! reader = command.ExecuteReaderAsync()
                let tenants = ResizeArray<System.Guid>()

                while reader.Read() do
                    match Umbral.Domains.Microsoft.parseMicrosoftSubject (reader.GetString 0) with
                    | Some(tid, _) -> tenants.Add tid
                    | None -> ()

                let first =
                    if tenants.Count = 0 then "" else tenants[0].ToString("D")

                match c.MicrosoftAudience with
                | None -> return Some first
                | Some audience ->
                    if tenants |> Seq.exists (Umbral.Domains.Microsoft.admits audience) then
                        return None
                    else
                        return Some first
            | _ -> return None
    }
