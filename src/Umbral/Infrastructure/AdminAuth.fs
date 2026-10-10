/// The single operator credential, and the session it grants.
///
/// FR-017. There is one credential and no operator accounts: no usernames, no
/// roles, no delegation. It is independent of every connector on purpose — a
/// clean install has no connectors to sign in through, and a provider whose
/// credentials have expired must never lock the operator out of the one screen
/// that can fix them.
module Umbral.Infrastructure.AdminAuth

open System
open System.Security.Claims
open System.Security.Cryptography
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Http
open Oxpecker

[<Literal>]
let Scheme = "Umbral.Operator"

[<Literal>]
let CookieName = "umbral_operator"

[<Literal>]
let private CredentialKey = "operator_credential"

[<Literal>]
let private CredentialSetKey = "operator_credential_set_utc"

/// A random value the admin cookie carries and the database holds. Replacing
/// the credential changes the database's copy, which is what makes every
/// session minted before it stop being accepted — a cookie is a bearer
/// credential that nothing else would ever revoke before its twelve hours are up.
[<Literal>]
let private SessionStampKey = "operator_session_stamp"

/// OWASP's current PBKDF2-HMAC-SHA256 figure. It costs roughly a second on a
/// Raspberry Pi 4, which is paid once per admin sign-in and never on a user's
/// login path — the only place this is verified is the admin form.
[<Literal>]
let private Iterations = 600_000

[<Literal>]
let private SaltBytes = 16

[<Literal>]
let private HashBytes = 32

/// "pbkdf2-sha256$<iterations>$<salt>$<hash>", all base64. The iteration count
/// is stored rather than assumed so raising it later does not invalidate every
/// existing credential: an old value still verifies against its own cost.
let hashCredential (plaintext: string) =
    let salt = RandomNumberGenerator.GetBytes SaltBytes

    let hash =
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes plaintext,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashBytes
        )

    $"pbkdf2-sha256${Iterations}${Convert.ToBase64String salt}${Convert.ToBase64String hash}"

let verifyCredential (stored: string) (candidate: string) =
    match stored.Split '$' with
    | [| "pbkdf2-sha256"; iterations; salt; expected |] ->
        match Int32.TryParse iterations with
        | true, iterations when iterations > 0 ->
            try
                let actual =
                    Rfc2898DeriveBytes.Pbkdf2(
                        Encoding.UTF8.GetBytes candidate,
                        Convert.FromBase64String salt,
                        iterations,
                        HashAlgorithmName.SHA256,
                        HashBytes
                    )

                // Constant time: the attacker controls `candidate` and can retry.
                CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String expected)
            with :? FormatException ->
                false
        | _ -> false
    | _ -> false

/// Whether `value` is a verification value in the format `hashCredential`
/// writes, and strong enough to be one: the file takes a hash rather than a
/// credential, and a mistyped plaintext must be refused rather than stored as
/// something no one can ever sign in with.
let isCredentialHash (value: string) =
    match value.Split '$' with
    | [| "pbkdf2-sha256"; iterations; salt; hash |] ->
        match Int32.TryParse iterations with
        | true, iterations when iterations >= 100_000 ->
            try
                Convert.FromBase64String(salt).Length >= SaltBytes
                && Convert.FromBase64String(hash).Length = HashBytes
            with :? FormatException ->
                false
        | _ -> false
    | _ -> false

/// How long a credential must be. Short enough not to be theatre, long enough
/// that 600k PBKDF2 iterations are actually protecting something.
[<Literal>]
let MinimumLength = 12

// ---------------------------------------------------------------------------
// Storage
// ---------------------------------------------------------------------------

let private readSetting (config: Database.DbConfig) (key: string) : Task<string option> =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT value FROM settings WHERE key = @key"
        command.Parameters.AddWithValue("@key", key) |> ignore
        let! value = command.ExecuteScalarAsync()
        return if isNull value then None else Some(string value)
    }

let private writeSetting (config: Database.DbConfig) (clock: TimeProvider) (key: string) (value: string) =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()

        command.CommandText <-
            "INSERT INTO settings (key, value, updated_utc) VALUES (@key, @value, @now)
             ON CONFLICT (key) DO UPDATE SET value = @value, updated_utc = @now"

        command.Parameters.AddWithValue("@key", key) |> ignore
        command.Parameters.AddWithValue("@value", value) |> ignore
        command.Parameters.AddWithValue("@now", clock.GetUtcNow().ToString "o") |> ignore
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

/// Whether first-run setup still has to happen. Until this is true, no
/// administrative screen is reachable (FR-016) — and Umbral ships with no
/// default or well-known credential, so there is nothing to guess at.
let isConfigured (config: Database.DbConfig) : Task<bool> =
    task {
        let! stored = readSetting config CredentialKey
        return stored.IsSome
    }

type SetResult =
    | Set
    | TooShort of minimum: int
    | AlreadyConfigured

/// First-run setup. Refuses once a credential exists, so the setup screen
/// cannot be used to take an instance over later.
let setInitialCredential (config: Database.DbConfig) (clock: TimeProvider) (plaintext: string) : Task<SetResult> =
    task {
        let! configured = isConfigured config

        if configured then
            return AlreadyConfigured
        elif isNull (box plaintext) || plaintext.Length < MinimumLength then
            return TooShort MinimumLength
        else
            do! writeSetting config clock CredentialKey (hashCredential plaintext)
            do! writeSetting config clock CredentialSetKey (clock.GetUtcNow().ToString "o")
            return Set
    }

/// Replace the credential. For an operator who is already signed in (FR-017a),
/// and for the offline reset path (FR-017b), which is why it does not refuse
/// when one already exists.
///
/// Every session signed in before this is ended, because the reason to replace
/// a credential is usually that someone else may hold it, and a session they
/// opened with it must not outlive it. The caller who is replacing it signs in
/// again to keep its own (`signIn` reads the new stamp).
let replaceCredential (config: Database.DbConfig) (clock: TimeProvider) (plaintext: string) : Task<SetResult> =
    task {
        if isNull (box plaintext) || plaintext.Length < MinimumLength then
            return TooShort MinimumLength
        else
            do! writeSetting config clock CredentialKey (hashCredential plaintext)
            do! writeSetting config clock CredentialSetKey (clock.GetUtcNow().ToString "o")
            do! writeSetting config clock SessionStampKey (Umbral.Domains.Ids.newSubject ())
            return Set
    }

/// The hash the configuration file last applied, so that a restart which finds
/// the same value in the file changes nothing — and so a credential the
/// operator replaced from the admin screen since is not put back by it.
[<Literal>]
let private FileCredentialKey = "operator_credential_file_hash"

type FileCredentialResult =
    /// An unclaimed instance took the file's credential: no setup screen needed.
    | Claimed
    /// The file carries a different value from the last one it applied.
    | Replaced
    | Unchanged

/// Apply the operator credential a configuration file carries (FR-017a), as a
/// hash made by `Umbral hash-credential` — the file never holds the credential.
///
/// "The file wins on every start" would be wrong here: it would quietly undo
/// every replacement made from the admin screen at the next restart. So the file
/// wins when its value CHANGES, which is the only time the operator edited it.
let applyCredentialHash (config: Database.DbConfig) (clock: TimeProvider) (hash: string) : Task<FileCredentialResult> =
    task {
        let! configured = isConfigured config
        let! lastApplied = readSetting config FileCredentialKey
        let! stored = readSetting config CredentialKey

        if lastApplied = Some hash then
            return Unchanged
        else
            do! writeSetting config clock FileCredentialKey hash

            if stored = Some hash then
                // Already the credential, by whichever door: nothing to change
                // and no one to sign out.
                return Unchanged
            else
                do! writeSetting config clock CredentialKey hash
                do! writeSetting config clock CredentialSetKey (clock.GetUtcNow().ToString "o")

                if configured then
                    do! writeSetting config clock SessionStampKey (Umbral.Domains.Ids.newSubject ())
                    return Replaced
                else
                    return Claimed
    }

let verify (config: Database.DbConfig) (candidate: string) : Task<bool> =
    task {
        match! readSetting config CredentialKey with
        | None -> return false
        | Some stored -> return verifyCredential stored candidate
    }

// ---------------------------------------------------------------------------
// Session
// ---------------------------------------------------------------------------

[<Literal>]
let private OperatorClaim = "umbral:operator"

[<Literal>]
let private StampClaim = "umbral:stamp"

/// The current stamp, or "" before the credential has ever been replaced — a
/// cookie from before there was a stamp carries none, and the two match.
let private currentStamp (config: Database.DbConfig) =
    task {
        let! stamp = readSetting config SessionStampKey
        return defaultArg stamp ""
    }

/// Reject a cookie whose stamp is no longer the current one. Runs on every
/// authenticated admin request: one indexed read against a table of a few rows,
/// which is cheap next to the page it guards.
let private validateStamp (context: CookieValidatePrincipalContext) : Task =
    task {
        let config = context.HttpContext.GetService<Database.DbConfig>()
        let! current = currentStamp config

        let carried =
            match context.Principal with
            | null -> ""
            | principal ->
                match principal.FindFirst StampClaim with
                | null -> ""
                | claim -> claim.Value

        if carried <> current then
            context.RejectPrincipal()
            do! context.HttpContext.SignOutAsync Scheme
    }

let configureCookie (options: CookieAuthenticationOptions) =
    options.Events.OnValidatePrincipal <- fun context -> validateStamp context
    options.Cookie.Name <- CookieName
    options.Cookie.HttpOnly <- true
    options.Cookie.IsEssential <- true
    // Strict is correct here, unlike the end-user session: nothing legitimately
    // navigates to an admin screen from another site, and the admin cookie
    // never has to survive a provider's redirect back.
    options.Cookie.SameSite <- SameSiteMode.Strict
    options.Cookie.SecurePolicy <- CookieSecurePolicy.SameAsRequest
    options.Cookie.Path <- "/admin"
    options.SlidingExpiration <- true
    options.ExpireTimeSpan <- TimeSpan.FromHours 12.0
    options.LoginPath <- PathString "/admin/signin"
    options.AccessDeniedPath <- PathString "/admin/signin"

let signIn (ctx: HttpContext) : Task =
    task {
        let! stamp = currentStamp (ctx.GetService<Database.DbConfig>())
        let identity = ClaimsIdentity(Scheme)
        identity.AddClaim(Claim(ClaimTypes.Name, "operator"))
        identity.AddClaim(Claim(OperatorClaim, "true"))
        identity.AddClaim(Claim(StampClaim, stamp))
        do! ctx.SignInAsync(Scheme, ClaimsPrincipal identity)
    }

let signOut (ctx: HttpContext) : Task = ctx.SignOutAsync Scheme

/// Guard every administrative slice. A combinator rather than middleware or an
/// authorization policy, because Umbral has no authorization model to express
/// one in (Principle VII).
let requireOperator (inner: HttpContext -> Task) : HttpContext -> Task =
    fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let! configured = isConfigured config

            if not configured then
                // Nothing is reachable until first-run setup is done, including
                // the sign-in form: there is no credential to check against.
                ctx.Response.Redirect "/admin/setup"
            else
                let! result = ctx.AuthenticateAsync Scheme

                if result.Succeeded then
                    do! inner ctx
                else
                    ctx.Response.Redirect "/admin/signin"
        }

// ---------------------------------------------------------------------------
// The first-run setup token
//
// FR-016 says the operator must establish admin access before any admin screen
// works, but says nothing about WHO gets to be that operator. An unauthenticated
// setup page means whoever reaches a fresh instance first claims it — and people
// run `docker run -p 80:80` before they configure anything.
//
// So setup additionally requires a token printed to the container log. Reading
// the log is the first thing an operator does, it costs one paste, and it makes
// "claim this instance" require access the machine already implies.
//
// Held in memory only: a restart mints a new one and prints it again, which is
// correct — the old one should not outlive the process that announced it.
// ---------------------------------------------------------------------------

type SetupToken() =
    let value = Umbral.Domains.Ids.newClientId ()
    member _.Value = value
    member _.Matches(candidate: string) = Umbral.Domains.Ids.constantTimeEquals value candidate

    /// What the operator reads in `docker logs`. Deliberately loud: it is
    /// competing with EF Core's migration chatter for their attention.
    member this.Banner(origin: string) =
        let line = String.replicate 64 "="

        String.concat
            Environment.NewLine
            [ ""
              line
              "  Umbral is not configured yet."
              ""
              $"  Open     {origin}/admin/setup"
              $"  Token    {this.Value}"
              ""
              "  The token is shown only while this container runs and is"
              "  needed once, to claim this instance."
              line
              "" ]
