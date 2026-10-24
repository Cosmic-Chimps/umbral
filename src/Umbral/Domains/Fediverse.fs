/// The pure rules of the Fediverse connector (specs/010-fediverse-connector).
///
/// Pure, like `EmailDomains`, and for the same reason: these rules decide whose
/// server Umbral will talk to and which servers may sign in, so every caller
/// (the account step, the callback, the admin screens, the configuration file)
/// shares one total function and none can disagree with another. Nothing here
/// does I/O; the guarded HTTP client is the second line, this is the first.
///
/// A server is always its one normalised spelling: ASCII, lower case, no scheme,
/// no path, no default port. The allowlist, the stored registration and the
/// subject all use it, so `Mastodon.Social` and `mastodon.social` are the same
/// server and the same people. Only the development setting (`allowDev`: the
/// Development environment with the insecure-http flag) admits `localhost`, IP
/// literals, other ports and `http`, so a local fake server works.
module Umbral.Domains.Fediverse

open System
open System.Net
open System.Net.Sockets
open System.Text.RegularExpressions
open Umbral.Domains

// ---------------------------------------------------------------------------
// Server addresses (research R5)
// ---------------------------------------------------------------------------

/// Why one entry was refused. The entry is the text as typed, trimmed.
type ServerEntryError =
    /// `*` anywhere in it. Its own case because it is the likely mistake and
    /// the way out is different: list each server.
    | Wildcard of entry: string
    /// Not a server: a path, a bad port, an IP address, a single label.
    | NotAServer of entry: string

let private isLiteralHost (name: string) =
    name = "localhost"
    || (let bare = name.TrimStart('[').TrimEnd(']')
        fst (IPAddress.TryParse bare))

/// The host part of a normalised server, without its port.
let private hostOnly (host: string) =
    if host.StartsWith '[' then
        host.Substring(0, host.IndexOf ']' + 1)
    else
        match host.IndexOf ':' with
        | -1 -> host
        | at -> host.Substring(0, at)

let private stripPrefix (prefix: string) (text: string) =
    if text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) then
        Some(text.Substring prefix.Length)
    else
        None

/// Splits `host`, `host:port`, `[v6]`, `[v6]:port` and a bare `v6` into the
/// host and the port text. None when the shape is wrong.
let private splitPort (text: string) : (string * string option) option =
    if text.StartsWith '[' then
        match text.IndexOf ']' with
        | -1 -> None
        | close ->
            let rest = text.Substring(close + 1)
            let inside = text.Substring(1, close - 1)

            if rest = "" then Some(inside, None)
            elif rest.StartsWith ':' then Some(inside, Some(rest.Substring 1))
            else None
    else
        match text.Split ':' with
        | [| host |] -> Some(host, None)
        | [| host; port |] -> Some(host, Some port)
        // More than one colon: a bare IPv6 literal, which has no port.
        | _ -> Some(text, None)

/// The one spelling of a server Umbral stores, compares and puts in a subject.
/// The result is the host, plus `:port` under `allowDev` for a port that is not
/// 443. `EmailDomains.normaliseDomain` does the domain rules and the IDN
/// conversion, so a server address and an email domain follow one rule.
let normaliseServer (allowDev: bool) (text: string | null) : Result<string, ServerEntryError> =
    let entry =
        match text with
        | null -> ""
        | value -> value.Trim()

    if entry.Contains '*' then
        Error(Wildcard entry)
    else
        let refused = Error(NotAServer entry)
        let afterAt = if entry.StartsWith '@' then entry.Substring 1 else entry

        let afterScheme =
            match stripPrefix "https://" afterAt with
            | Some rest -> Some rest
            | None ->
                match stripPrefix "http://" afterAt with
                | Some rest when allowDev -> Some rest
                | Some _ -> None
                | None -> Some afterAt

        match afterScheme with
        | None -> refused
        | Some rest ->
            let rest = if rest.EndsWith '/' then rest.Substring(0, rest.Length - 1) else rest

            let badChar =
                rest |> Seq.exists (fun c -> c = '/' || c = '?' || c = '#' || c = '@' || Char.IsWhiteSpace c)

            if rest = "" || badChar then
                refused
            else
                match splitPort rest with
                | None -> refused
                | Some(rawHost, rawPort) ->
                    let port =
                        match rawPort with
                        | None -> Ok None
                        | Some p ->
                            match Int32.TryParse(p, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture) with
                            | true, number when number >= 1 && number <= 65535 ->
                                if number = 443 then Ok None
                                elif allowDev then Ok(Some number)
                                else Error()
                            | _ -> Error()

                    match port with
                    | Error() -> refused
                    | Ok port ->
                        let name = rawHost.ToLowerInvariant()
                        let name = if name.EndsWith '.' then name.Substring(0, name.Length - 1) else name
                        let suffix = port |> Option.map (fun p -> $":{p}") |> Option.defaultValue ""

                        if isLiteralHost name then
                            if allowDev then
                                let shown = if name.Contains ':' then $"[{name.Trim('[', ']')}]" else name
                                Ok(shown + suffix)
                            else
                                refused
                        else
                            match EmailDomains.normaliseDomain name with
                            | Some host -> Ok(host + suffix)
                            | None -> refused

/// What the person typed on the account step.
type Input =
    /// `@user@domain` or `user@domain`: the domain is asked where the server is
    /// (WebFinger), the user is kept as typed.
    | Handle of user: string * domain: string
    | Server of host: string

let private userPattern = Regex(@"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant)

/// A handle, a server, or None. Empty text is None.
let parseInput (allowDev: bool) (text: string | null) : Input option =
    let entry =
        match text with
        | null -> ""
        | value -> value.Trim()

    if entry = "" then
        None
    else
        let withoutAt = if entry.StartsWith '@' then entry.Substring 1 else entry

        let handle =
            match withoutAt.Split '@' with
            | [| user; domain |] when userPattern.IsMatch user && not (domain.Contains '/') ->
                match normaliseServer allowDev domain with
                | Ok host -> Some(Handle(user, host))
                | Error _ -> None
            | _ -> None

        match handle with
        | Some _ -> handle
        | None ->
            match normaliseServer allowDev entry with
            | Ok host -> Some(Server host)
            | Error _ -> None

// ---------------------------------------------------------------------------
// The server list (research R3)
// ---------------------------------------------------------------------------

/// What the operator typed, as a list of normalised servers. Commas and any
/// whitespace separate; duplicates go, keeping the first order. Empty input is
/// `Ok []`. The first entry that is not usable is the error, and nothing is kept.
let parseServers (allowDev: bool) (text: string | null) : Result<string list, ServerEntryError> =
    let entries =
        (match text with
         | null -> ""
         | value -> value)
            .Split([| ','; ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
        |> List.ofArray

    let folded =
        entries
        |> List.fold
            (fun state entry ->
                match state with
                | Error _ -> state
                | Ok hosts ->
                    match normaliseServer allowDev entry with
                    | Ok host -> Ok(host :: hosts)
                    | Error error -> Error error)
            (Ok [])

    folded |> Result.map (List.rev >> List.distinct)

/// The stored form: normalised servers joined by one space. A space never
/// appears in one, so nothing is lost.
let serialiseServers (servers: string list) : string = String.concat " " servers

/// The inverse of `serialiseServers`. Reads what is stored, without judging it.
let deserialiseServers (stored: string) : string list =
    stored.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries) |> List.ofArray

/// What the operator reads back.
let displayServers (servers: string list) : string = String.concat ", " servers

/// Whether a server may sign in: any server, or one on the list.
let allows (access: Connectors.FediverseAccess) (host: string) : bool =
    not access.OnlyListed || List.contains host access.Servers

/// The form value of the access radio.
let scopeValue (access: Connectors.FediverseAccess) : string =
    if access.OnlyListed then "listed" else "any"

/// The inverse of `scopeValue`: whether the list applies. None for anything else.
let parseScope (value: string) : bool option =
    match value with
    | "any" -> Some false
    | "listed" -> Some true
    | _ -> None

// ---------------------------------------------------------------------------
// Public addresses (research R6)
// ---------------------------------------------------------------------------

let private publicV4 (b: byte[]) : bool =
    let a, b1, c = int b[0], int b[1], int b[2]

    not (
        a = 0
        || a = 10
        || (a = 100 && (b1 &&& 0xC0) = 64)
        || a = 127
        || (a = 169 && b1 = 254)
        || (a = 172 && (b1 &&& 0xF0) = 16)
        || (a = 192 && b1 = 0 && c = 0)
        || (a = 192 && b1 = 0 && c = 2)
        || (a = 192 && b1 = 168)
        || (a = 198 && (b1 = 18 || b1 = 19))
        || (a = 198 && b1 = 51 && c = 100)
        || (a = 203 && b1 = 0 && c = 113)
        || a >= 224
    )

/// Whether a connection to this address may go out. Refuses what is not on the
/// public internet: loopback, private, link-local, CGNAT, unique-local,
/// multicast, unspecified, benchmark and documentation ranges. An IPv4-mapped
/// IPv6 address is judged as the IPv4 it wraps, and so are the embedded forms
/// (NAT64 and 6to4), or `::ffff:10.0.0.1` would walk past the IPv4 rules.
/// Mastodon's username pattern, for what a server answers (not typed input).
/// `\z`, not `$`: in .NET `$` also matches before a final newline.
let private usernamePattern =
    Regex(@"\A[A-Za-z0-9_]+([.-]+[A-Za-z0-9_]+)*\z", RegexOptions.CultureInvariant)

let isUsername (value: string) : bool =
    value.Length <= 64 && usernamePattern.IsMatch value

let isPublicAddress (address: IPAddress) : bool =
    let address = if address.IsIPv4MappedToIPv6 then address.MapToIPv4() else address
    let bytes = address.GetAddressBytes()

    if address.AddressFamily = AddressFamily.InterNetwork then
        publicV4 bytes
    else
        let b0, b1 = int bytes[0], int bytes[1]
        // ::/96 holds `::`, `::1` and the deprecated IPv4-compatible form; no
        // public server is reached through any of them.
        let ipv4Compatible = bytes[0..11] |> Array.forall ((=) 0uy)

        let nat64 =
            // 64:ff9b::/96
            b0 = 0x00 && b1 = 0x64 && bytes[2] = 0xffuy && bytes[3] = 0x9buy && bytes[4..11] |> Array.forall ((=) 0uy)

        // Local NAT64 (64:ff9b:1::/48), discard (100::/64), ORCHID
        // (2001:10::/28, 2001:20::/28) and IPv4-translated (::ffff:0:0:0/96).
        let refusedRange =
            (b0 = 0x00 && b1 = 0x64 && bytes[2] = 0xffuy && bytes[3] = 0x9buy && bytes[4] = 0x00uy && bytes[5] = 0x01uy)
            || (b0 = 0x01 && b1 = 0x00 && bytes[2..7] |> Array.forall ((=) 0uy))
            || (b0 = 0x20 && b1 = 0x01 && bytes[2] = 0uy && (int bytes[3] &&& 0xF0) = 0x10)
            || (b0 = 0x20 && b1 = 0x01 && bytes[2] = 0uy && (int bytes[3] &&& 0xF0) = 0x20)
            || (bytes[0..7] |> Array.forall ((=) 0uy) && bytes[8] = 0xffuy && bytes[9] = 0xffuy && bytes[10] = 0uy && bytes[11] = 0uy)

        if ipv4Compatible || refusedRange then false
        elif b0 = 0xfe && (b1 &&& 0xC0) = 0x80 then false
        elif b0 = 0xfe && (b1 &&& 0xC0) = 0xC0 then false
        elif (b0 &&& 0xFE) = 0xFC then false
        elif b0 = 0xFF then false
        elif b0 = 0x20 && b1 = 0x01 && bytes[2] = 0x0duy && bytes[3] = 0xb8uy then false
        // Teredo (2001::/32) tunnels to an obfuscated IPv4 address: refused
        // rather than decoded.
        elif b0 = 0x20 && b1 = 0x01 && bytes[2] = 0uy && bytes[3] = 0uy then false
        elif nat64 then publicV4 bytes[12..15]
        elif b0 = 0x20 && b1 = 0x02 then publicV4 bytes[2..5]
        else true

/// Development only, so a local fake server works: `http` for `localhost`, an IP
/// literal or a host with a port. Everything else is `https`.
let baseUrl (allowDev: bool) (host: string) : string =
    let plain = allowDev && (host.Contains ':' && not (host.StartsWith '[' && host.EndsWith ']') || isLiteralHost (hostOnly host))
    (if plain then "http://" else "https://") + host

// ---------------------------------------------------------------------------
// Which servers are supported (research R1)
// ---------------------------------------------------------------------------

/// The three addresses Umbral uses on a supported server.
type ServerEndpoints =
    { AuthorizeUrl: string
      TokenUrl: string
      RegistrationUrl: string }

/// What `/.well-known/oauth-authorization-server` said, already parsed.
type ServerMetadata =
    { CodeChallengeMethods: string list
      AuthorizationEndpoint: string option
      TokenEndpoint: string option
      RegistrationEndpoint: string option }

/// An endpoint is believed only when it is an absolute https address (http under
/// `allowDev`) on the server's own host and port, so metadata cannot send the
/// sign-in or the registration to another machine.
/// Longest endpoint URL kept. An anonymous person could otherwise grow the
/// database without limit through a hostile server (code review B2).
[<Literal>]
let private MaxEndpointLength = 2048

let private ownEndpoint (allowDev: bool) (host: string) (endpoint: string option) : string option =
    endpoint
    |> Option.bind (fun text ->
        let parsed =
            match Uri.TryCreate(text, UriKind.Absolute) with
            | true, uri -> Option.ofObj uri
            | _ -> None

        match parsed with
        | Some uri when
            text.Length <= MaxEndpointLength
            && (uri.Scheme = Uri.UriSchemeHttps || (allowDev && uri.Scheme = Uri.UriSchemeHttp))
            && uri.UserInfo = ""
            && uri.Authority.ToLowerInvariant() = host
            ->
            Some text
        | _ -> None)

/// Supported when the metadata lists S256 PKCE and names all three endpoints on
/// the server's own host. Without S256 a server could ignore the challenge and
/// Umbral could not tell, so it is refused.
let fromMetadata (allowDev: bool) (host: string) (m: ServerMetadata) : ServerEndpoints option =
    if not (List.contains "S256" m.CodeChallengeMethods) then
        None
    else
        match
            ownEndpoint allowDev host m.AuthorizationEndpoint,
            ownEndpoint allowDev host m.TokenEndpoint,
            ownEndpoint allowDev host m.RegistrationEndpoint
        with
        | Some authorize, Some token, Some registration ->
            Some
                { AuthorizeUrl = authorize
                  TokenUrl = token
                  RegistrationUrl = registration }
        | _ -> None

/// The fallback for a server with no metadata: GoToSocial, which enforces S256
/// and has Mastodon's endpoints. Akkoma and Pleroma implement no PKCE, and
/// Misskey and Sharkey have no app registration, so they stay unsupported, as
/// does any unknown software.
let fromNodeInfo (allowDev: bool) (host: string) (softwareName: string) : ServerEndpoints option =
    if String.Equals(softwareName, "gotosocial", StringComparison.OrdinalIgnoreCase) then
        let root = baseUrl allowDev host

        Some
            { AuthorizeUrl = root + "/oauth/authorize"
              TokenUrl = root + "/oauth/token"
              RegistrationUrl = root + "/api/v1/apps" }
    else
        None

// ---------------------------------------------------------------------------
// Identity (research R10)
// ---------------------------------------------------------------------------

/// The subject of a Fediverse linked account: account ids are unique per server
/// only, so the host is part of it. The account id is never a username.
let fediverseSubject (host: string) (accountId: string) : string = $"{host}#{accountId}"

/// The inverse of `fediverseSubject`: split at the first `#`, so an id that
/// itself contains one survives. None when either part is empty.
let parseFediverseSubject (subject: string) : (string * string) option =
    match subject.IndexOf '#' with
    | at when at > 0 && at < subject.Length - 1 -> Some(subject.Substring(0, at), subject.Substring(at + 1))
    | _ -> None

/// What Mastodon itself shows: `username@server`.
let preferredUsername (username: string) (host: string) : string = $"{username}@{host}"
