/// The one setting Umbral refuses to start without.
///
/// Every URL Umbral publishes or compares is derived from here and never from
/// the incoming request: the OIDC issuer, the connector redirect URIs, the
/// callback URL shown in the admin UI.
///
/// This is forced, not chosen. The `redirect_uri` sent to a provider's token
/// endpoint must byte-match the one sent to its authorize endpoint, which must
/// byte-match what the operator registered in the provider's console. Derived
/// from `Request.Scheme` and `Host`, that value is `http://umbral:8080` behind a
/// reverse proxy while the registered one is `https://auth.example.com`, and
/// every login then fails at the token exchange with an opaque
/// `redirect_uri_mismatch` that names neither the proxy nor the setting.
///
/// Trusting `X-Forwarded-*` to paper over it trades a reliability bug for a
/// security one, since those headers are forgeable by anything that can reach
/// the process.
module Umbral.Infrastructure.PublicOrigin

open System

/// What an operator sets in a container or a shell.
[<Literal>]
let EnvironmentVariable = "UMBRAL__PUBLIC_ORIGIN"

/// The same setting seen through `IConfiguration`. .NET maps `__` in an
/// environment variable to its section separator, so `UMBRAL__PUBLIC_ORIGIN`
/// and `UMBRAL:PUBLIC_ORIGIN` are one value reached two ways — and reading the
/// configuration rather than the environment directly is what lets
/// `dotnet user-secrets` supply it during development without a second code
/// path or a second name to document.
[<Literal>]
let ConfigurationKey = "UMBRAL:PUBLIC_ORIGIN"

/// A validated, normalised public origin: scheme, host and optional port, with
/// no path, query or fragment, and no trailing slash.
type PublicOrigin =
    private
        { Value: Uri }

    member this.Uri = this.Value
    /// e.g. "https://auth.example.com" — no trailing slash, ever, because this
    /// is concatenated with paths and a stray slash produces a mismatch that is
    /// invisible when you read it.
    member this.Text = this.Value.GetLeftPart(UriPartial.Authority)
    override this.ToString() = this.Text

type OriginError =
    | NotSet
    | NotAbsolute of value: string
    | UnsupportedScheme of scheme: string
    | InsecureSchemeOnRemoteHost of host: string
    | HasPath of path: string
    | HasQueryOrFragment

/// What to tell the operator. Principle VI: say what to do next, not only what
/// is wrong. These are read by someone at 1am with a Pi and no patience.
let describe (error: OriginError) =
    let example =
        $"Set %s{EnvironmentVariable} to the address people reach Umbral at, for example https://auth.example.com"

    match error with
    | NotSet -> $"%s{EnvironmentVariable} is not set. %s{example}."
    | NotAbsolute value ->
        $"%s{EnvironmentVariable} is \"%s{value}\", which is not an absolute URL. %s{example}."
    | UnsupportedScheme scheme ->
        $"%s{EnvironmentVariable} uses the scheme \"%s{scheme}\". Only http and https are supported. %s{example}."
    | InsecureSchemeOnRemoteHost host ->
        $"%s{EnvironmentVariable} uses http with the host \"%s{host}\". Plain http is accepted only for localhost. "
        + "Put Umbral behind a reverse proxy that terminates TLS and set the origin to its https address."
    | HasPath path ->
        $"%s{EnvironmentVariable} contains the path \"%s{path}\". Give only the scheme, host and port, for example https://auth.example.com. "
        + "Umbral does not support running under a sub-path."
    | HasQueryOrFragment ->
        $"%s{EnvironmentVariable} contains a query string or a fragment. Give only the scheme, host and port, for example https://auth.example.com."

let private isLoopback (host: string) =
    match host.ToLowerInvariant() with
    | "localhost"
    | "127.0.0.1"
    | "::1"
    | "[::1]" -> true
    | _ -> false

/// Parse and normalise. Normalisation matters as much as validation here: a
/// trailing slash or an uppercase host produces a mismatch nobody can see by
/// reading the two values side by side.
let parse (raw: string) : Result<PublicOrigin, OriginError> =
    if String.IsNullOrWhiteSpace raw then
        Error NotSet
    else

    let trimmed = raw.Trim()

    // Uri.TryCreate's out parameter is `Uri | null` even on success, so narrow
    // it through Option.ofObj rather than trusting the boolean.
    let parsed =
        match Uri.TryCreate(trimmed, UriKind.Absolute) with
        | true, uri -> Option.ofObj uri
        | false, _ -> None

    match parsed with
    | None -> Error(NotAbsolute trimmed)
    | Some uri ->

    let scheme = uri.Scheme.ToLowerInvariant()

    if scheme <> "http" && scheme <> "https" then
        Error(UnsupportedScheme scheme)
    elif scheme = "http" && not (isLoopback uri.Host) then
        // Google refuses a non-https redirect URI outright for any host but
        // localhost, so this would fail later anyway — better here, by name.
        Error(InsecureSchemeOnRemoteHost uri.Host)
    elif not (String.IsNullOrEmpty uri.Query) || not (String.IsNullOrEmpty uri.Fragment) then
        Error HasQueryOrFragment
    elif uri.AbsolutePath <> "/" && uri.AbsolutePath <> "" then
        Error(HasPath uri.AbsolutePath)
    else

    let builder = UriBuilder(scheme, uri.Host.ToLowerInvariant())
    // Keep a non-default port, drop a default one: https://x:443 and https://x
    // are the same origin, and only one of them matches what a provider stores.
    if not uri.IsDefaultPort then
        builder.Port <- uri.Port
    else
        builder.Port <- -1

    Ok { Value = builder.Uri }

/// Read through a lookup, or fail with a message naming the variable.
///
/// The lookup is a parameter rather than a direct `Environment.GetEnvironmentVariable`
/// so that `Program.fs` can hand over `IConfiguration` — which already layers
/// environment variables over user secrets in Development — and so that a test
/// can hand over a dictionary without touching process state.
let fromEnvironment (read: string -> string | null) : Result<PublicOrigin, OriginError> =
    match read EnvironmentVariable with
    | null -> Error NotSet
    | value -> parse value

/// Read from configuration, trying the section form first and the raw
/// environment-variable name second. Both are documented, both must work, and
/// an operator should never have to know which one this build happens to read.
let fromConfiguration (read: string -> string | null) : Result<PublicOrigin, OriginError> =
    match read ConfigurationKey with
    | null -> fromEnvironment read
    | value -> parse value

/// Compose a path onto the origin. The single place that concatenation happens,
/// so a trailing-slash bug can exist in exactly one spot.
let resolve (origin: PublicOrigin) (path: string) =
    let trimmed = path.TrimStart('/')
    Uri(origin.Uri, trimmed)

/// The redirect URI a connector must register in its provider's console.
/// Shown read-only in the admin UI with a copy button, because an operator who
/// types this by hand gets an error that names nothing.
let callbackUri (origin: PublicOrigin) (slug: string) =
    resolve origin $"connect/{slug}/callback"
