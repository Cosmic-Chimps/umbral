/// The OAuth client Umbral registered by itself on each fediverse server
/// (specs/010-fediverse-connector, research R7, data-model "Server registration").
///
/// Only reads and writes. The secret is always protected with the local Data
/// Protection key ring in the data directory, whatever store connector secrets
/// use, because Umbral creates one per server and an operator never sees it, so
/// there is nothing for an external store to be a reference to.
module Umbral.Infrastructure.FediverseServers

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.DataProtection
open Microsoft.Data.Sqlite

/// One registration, keyed by the normalised host.
type Registration =
    { Host: string
      ClientId: string
      ClientSecret: Secrets.Protected
      /// As the server's metadata says.
      AuthorizeUrl: string
      /// As the server's metadata says.
      TokenUrl: string
      /// The callback address this registration was made with; when it is no
      /// longer the instance's current one, the registration is treated as forgotten.
      RedirectUri: string
      RegisteredUtc: DateTimeOffset
      /// Last time the registration was proved to still work.
      CheckedUtc: DateTimeOffset option
      /// Last successful sign-in through this server.
      LastUsedUtc: DateTimeOffset option }

[<Literal>]
let private Purpose = "Umbral.FediverseServers.v1"

[<Literal>]
let private Select =
    "SELECT host, client_id, client_secret, authorize_url, token_url, redirect_uri,
            registered_utc, checked_utc, last_used_utc FROM fediverse_servers WHERE host = @host"

[<Literal>]
let private InsertIfAbsent =
    "INSERT INTO fediverse_servers
        (host, client_id, client_secret, authorize_url, token_url, redirect_uri, registered_utc, checked_utc, last_used_utc)
     VALUES (@host, @clientId, @secret, @authorizeUrl, @tokenUrl, @redirectUri, @registered, @checked, @lastUsed)
     ON CONFLICT(host) DO NOTHING"

[<Literal>]
let private Replace =
    "INSERT INTO fediverse_servers
        (host, client_id, client_secret, authorize_url, token_url, redirect_uri, registered_utc, checked_utc, last_used_utc)
     VALUES (@host, @clientId, @secret, @authorizeUrl, @tokenUrl, @redirectUri, @registered, @checked, @lastUsed)
     ON CONFLICT(host) DO UPDATE SET
        client_id = excluded.client_id,
        client_secret = excluded.client_secret,
        authorize_url = excluded.authorize_url,
        token_url = excluded.token_url,
        redirect_uri = excluded.redirect_uri,
        registered_utc = excluded.registered_utc,
        checked_utc = excluded.checked_utc,
        last_used_utc = excluded.last_used_utc"

let private parseTime (text: string) =
    DateTimeOffset.Parse(text, Globalization.CultureInfo.InvariantCulture)

let private timeOrNull (value: DateTimeOffset option) : obj =
    match value with
    | Some time -> time.ToUniversalTime().ToString "o" :> obj
    | None -> DBNull.Value :> obj

let private bind (protection: IDataProtectionProvider) (command: SqliteCommand) (registration: Registration) =
    let protector = protection.CreateProtector Purpose
    command.Parameters.AddWithValue("@host", registration.Host) |> ignore
    command.Parameters.AddWithValue("@clientId", registration.ClientId) |> ignore
    command.Parameters.AddWithValue("@secret", protector.Protect registration.ClientSecret.Reveal) |> ignore
    command.Parameters.AddWithValue("@authorizeUrl", registration.AuthorizeUrl) |> ignore
    command.Parameters.AddWithValue("@tokenUrl", registration.TokenUrl) |> ignore
    command.Parameters.AddWithValue("@redirectUri", registration.RedirectUri) |> ignore
    command.Parameters.AddWithValue("@registered", registration.RegisteredUtc.ToUniversalTime().ToString "o") |> ignore
    command.Parameters.AddWithValue("@checked", timeOrNull registration.CheckedUtc) |> ignore
    command.Parameters.AddWithValue("@lastUsed", timeOrNull registration.LastUsedUtc) |> ignore

let private readRow (protection: IDataProtectionProvider) (reader: SqliteDataReader) : Registration option =
    let protector = protection.CreateProtector Purpose

    try
        Some
            { Host = reader.GetString 0
              ClientId = reader.GetString 1
              ClientSecret = Secrets.Protected.Of(protector.Unprotect(reader.GetString 2))
              AuthorizeUrl = reader.GetString 3
              TokenUrl = reader.GetString 4
              RedirectUri = reader.GetString 5
              RegisteredUtc = parseTime (reader.GetString 6)
              CheckedUtc = if reader.IsDBNull 7 then None else Some(parseTime (reader.GetString 7))
              LastUsedUtc = if reader.IsDBNull 8 then None else Some(parseTime (reader.GetString 8)) }
    with :? System.Security.Cryptography.CryptographicException ->
        // A lost key ring: the ciphertext is there and nothing can read it. It
        // reads as no registration, so the caller registers again and replaces the row.
        None

/// The registration for a normalised host, or None when there is none or its
/// secret can no longer be read.
let find (config: Database.DbConfig) (protection: IDataProtectionProvider) (host: string) : Task<Registration option> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- Select
        command.Parameters.AddWithValue("@host", host) |> ignore
        use! reader = command.ExecuteReaderAsync()
        let! hasRow = reader.ReadAsync()
        return if hasRow then readRow protection reader else None
    }

/// Overwrites every column: a registration made again after the server forgot
/// the app or the callback address moved.
let replace (config: Database.DbConfig) (protection: IDataProtectionProvider) (registration: Registration) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- Replace
        bind protection command registration
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

/// Stores the registration unless the host already has one, then returns what is
/// stored. Two first sign-ins racing keep one registration (research R7); the
/// losing one stays unused on the server.
let insertIfAbsent
    (config: Database.DbConfig)
    (protection: IDataProtectionProvider)
    (registration: Registration)
    : Task<Registration>
    =
    task {
        do!
            task {
                use! connection = Database.openConnection config
                use command = connection.CreateCommand()
                command.CommandText <- InsertIfAbsent
                bind protection command registration
                let! _ = command.ExecuteNonQueryAsync()
                return ()
            }

        let! stored = find config protection registration.Host

        match stored with
        | Some stored -> return stored
        | None ->
            // The row the insert left alone has a secret no key can read: keep
            // the fresh registration, or every sign-in would register again.
            do! replace config protection registration
            return registration
    }

let delete (config: Database.DbConfig) (host: string) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "DELETE FROM fediverse_servers WHERE host = @host"
        command.Parameters.AddWithValue("@host", host) |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

[<Literal>]
let private MarkChecked = "UPDATE fediverse_servers SET checked_utc = @now WHERE host = @host"

[<Literal>]
let private MarkUsed =
    "UPDATE fediverse_servers SET last_used_utc = @now, checked_utc = @now WHERE host = @host"

let private stamp (config: Database.DbConfig) (prepare: SqliteCommand -> unit) (clock: TimeProvider) (host: string) =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        prepare command
        command.Parameters.AddWithValue("@host", host) |> ignore
        command.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

/// The registration was just proved to still work.
let markChecked (config: Database.DbConfig) (clock: TimeProvider) (host: string) : Task<unit> =
    stamp config (fun command -> command.CommandText <- MarkChecked) clock host

/// A sign-in through this server succeeded. It also stamps `checked_utc`: a
/// successful exchange proves the client works.
let markUsed (config: Database.DbConfig) (clock: TimeProvider) (host: string) : Task<unit> =
    stamp config (fun command -> command.CommandText <- MarkUsed) clock host

[<Literal>]
let private FediverseSubjects =
    "SELECT provider_subject FROM linked_accounts WHERE user_id = @user AND provider_kind = 'fediverse'"

/// Whether a remembered sign-in through the Fediverse connector must be refused
/// because "Only these servers" no longer lists the person's server (code review
/// B4). The same reasoning as the email-domain recheck: tightening a rule reaches
/// the people already signed in. `Some host` is the refused server ("" when none
/// can be read); `None` admits.
let recheckServer (config: Database.DbConfig) (connectorSlug: string option) (userId: string) : Task<string option> =
    task {
        match connectorSlug with
        | None -> return None
        | Some slug ->
            let! connector = ConnectorStore.findBySlug config slug

            match connector with
            | Some c when c.Kind = Umbral.Domains.Connectors.Fediverse && c.FediverseAccess.OnlyListed ->
                use! connection = Database.openConnection config
                use command = connection.CreateCommand()
                command.CommandText <- FediverseSubjects
                command.Parameters.AddWithValue("@user", userId) |> ignore
                use! reader = command.ExecuteReaderAsync()
                let hosts = ResizeArray<string>()

                while reader.Read() do
                    match Umbral.Domains.Fediverse.parseFediverseSubject (reader.GetString 0) with
                    | Some(host, _) -> hosts.Add host
                    | None -> ()

                if hosts |> Seq.exists (Umbral.Domains.Fediverse.allows c.FediverseAccess) then
                    return None
                else
                    return Some(Seq.tryHead hosts |> Option.defaultValue "")
            | _ -> return None
    }
