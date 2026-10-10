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

/// Replace the credential. For an operator who is already signed in, and for
/// the offline reset path (FR-017b), which is why it does not refuse when one
/// already exists.
let replaceCredential (config: Database.DbConfig) (clock: TimeProvider) (plaintext: string) : Task<SetResult> =
    task {
        if isNull (box plaintext) || plaintext.Length < MinimumLength then
            return TooShort MinimumLength
        else
            do! writeSetting config clock CredentialKey (hashCredential plaintext)
            do! writeSetting config clock CredentialSetKey (clock.GetUtcNow().ToString "o")
            return Set
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

let configureCookie (options: CookieAuthenticationOptions) =
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

[<Literal>]
let private OperatorClaim = "umbral:operator"

let signIn (ctx: HttpContext) : Task =
    let identity = ClaimsIdentity(Scheme)
    identity.AddClaim(Claim(ClaimTypes.Name, "operator"))
    identity.AddClaim(Claim(OperatorClaim, "true"))
    ctx.SignInAsync(Scheme, ClaimsPrincipal identity)

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
