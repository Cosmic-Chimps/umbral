/// Reading the users table.
///
/// The authorize handler needs one question answered on every request — "is
/// this subject a real, enabled user?" — and asking it against the database
/// rather than trusting the session cookie is what makes FR-022 true: an
/// operator who disables someone must cut them off at the next sign-in, not at
/// the next cookie expiry.
///
/// Creating and linking users lands with the connectors in Phase 4; this is the
/// read path only.
module Umbral.Infrastructure.Users

open System
open System.Threading.Tasks
open Microsoft.Data.Sqlite

type User =
    { Id: string
      DisplayName: string
      Email: string option
      EmailVerified: bool
      AvatarUrl: string option
      PreferredUsername: string option
      ProfileUrl: string option
      IsEnabled: bool
      FirstSeenUtc: DateTimeOffset
      LastSeenUtc: DateTimeOffset }

let private readOptionalString (reader: SqliteDataReader) (ordinal: int) =
    if reader.IsDBNull ordinal then None else Some(reader.GetString ordinal)

let private readUser (reader: SqliteDataReader) =
    { Id = reader.GetString 0
      DisplayName = reader.GetString 1
      Email = readOptionalString reader 2
      EmailVerified = reader.GetInt32 3 = 1
      AvatarUrl = readOptionalString reader 4
      IsEnabled = reader.GetInt32 5 = 1
      FirstSeenUtc = DateTimeOffset.Parse(reader.GetString 6, Globalization.CultureInfo.InvariantCulture)
      LastSeenUtc = DateTimeOffset.Parse(reader.GetString 7, Globalization.CultureInfo.InvariantCulture)
      PreferredUsername = readOptionalString reader 8
      ProfileUrl = readOptionalString reader 9 }

[<Literal>]
let private SelectColumns =
    "SELECT id, display_name, email, email_verified, avatar_url, is_enabled, first_seen_utc, last_seen_utc,
            preferred_username, profile_url FROM users"

let findById (config: Database.DbConfig) (id: string) : Task<User option> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- SelectColumns + " WHERE id = @id"
        command.Parameters.AddWithValue("@id", id) |> ignore

        use! reader = command.ExecuteReaderAsync()
        let! hasRow = reader.ReadAsync()
        return if hasRow then Some(readUser reader) else None
    }

/// Used by the tests that need a subject to exist before the connectors are
/// built, and by nothing else. Phase 4's resolve-or-create replaces it as the
/// only way a user comes into being in production.
let insertForTesting (config: Database.DbConfig) (clock: TimeProvider) (user: User) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "INSERT INTO users (id, display_name, email, email_verified, avatar_url, is_enabled, first_seen_utc, last_seen_utc,
                                preferred_username, profile_url)
             VALUES (@id, @name, @email, @verified, @avatar, @enabled, @first, @last, @username, @profile)"

        let now = clock.GetUtcNow().ToString "o"
        let optional (v: string option) : obj = match v with Some s -> s :> obj | None -> DBNull.Value :> obj

        command.Parameters.AddWithValue("@id", user.Id) |> ignore
        command.Parameters.AddWithValue("@name", user.DisplayName) |> ignore
        command.Parameters.AddWithValue("@email", optional user.Email) |> ignore
        command.Parameters.AddWithValue("@verified", (if user.EmailVerified then 1 else 0)) |> ignore
        command.Parameters.AddWithValue("@avatar", optional user.AvatarUrl) |> ignore
        command.Parameters.AddWithValue("@username", optional user.PreferredUsername) |> ignore
        command.Parameters.AddWithValue("@profile", optional user.ProfileUrl) |> ignore
        command.Parameters.AddWithValue("@enabled", (if user.IsEnabled then 1 else 0)) |> ignore
        command.Parameters.AddWithValue("@first", now) |> ignore
        command.Parameters.AddWithValue("@last", now) |> ignore

        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

let setEnabled (config: Database.DbConfig) (id: string) (isEnabled: bool) : Task<unit> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "UPDATE users SET is_enabled = @enabled WHERE id = @id"
        command.Parameters.AddWithValue("@enabled", (if isEnabled then 1 else 0)) |> ignore
        command.Parameters.AddWithValue("@id", id) |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }
