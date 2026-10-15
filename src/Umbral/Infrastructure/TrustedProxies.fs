/// Whose `X-Forwarded-*` headers Umbral believes.
///
/// Behind a reverse proxy the client address, scheme and host arrive in headers,
/// and Umbral reads them (UseForwardedHeaders runs first). The rate limits
/// (RateLimits.fs) are partitioned by the address that comes out of that, so
/// the headers decide who is "the same client".
///
/// ASP.NET Core ignores the headers from any sender that is not in its known
/// proxy lists, and keeps the real connection address. By default Umbral
/// clears those lists, because the proxy is a different container with an
/// address nobody can know in advance, and that is safe only while the proxy is
/// the one thing that can reach Umbral's port. On a host where other containers
/// can reach the port directly (apps sharing one bridge network), any of them
/// could send a fresh `X-Forwarded-For` on every request and never be limited.
///
/// This setting is the way to close that: name the proxy, and the headers from
/// anything else are ignored. It is optional and unset means "trust any sender",
/// so an existing docker or compose install keeps working without a change.
module Umbral.Infrastructure.TrustedProxies

open System
open System.Net
open System.Net.Sockets
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.HttpOverrides

[<Literal>]
let EnvironmentVariable = "UMBRAL__TRUSTED_PROXIES"

/// The same setting seen through `IConfiguration` (`__` is the section
/// separator), for the reason given in PublicOrigin.fs.
[<Literal>]
let ConfigurationKey = "UMBRAL:TRUSTED_PROXIES"

/// One trusted sender: a single address, or a network.
type Entry =
    | Address of IPAddress
    | Network of System.Net.IPNetwork

type Trust =
    /// Unset or empty: forwarded headers are believed from any sender.
    | TrustAll
    /// Only these senders. The entries keep the order the operator wrote them.
    | Only of Entry list

let private fail (entry: string) (problem: string) =
    Error(
        $"%s{EnvironmentVariable} contains \"%s{entry}\", which %s{problem}. "
        + $"Fix or remove that entry. The setting takes a comma-separated list of IP addresses and networks, "
        + "for example \"172.26.64.1, 10.0.0.0/8, fd00::/8\". Leaving the setting out entirely "
        + "makes Umbral believe forwarded headers from any sender."
    )

/// `IPAddress.TryParse` also accepts shorthand such as "1" or "10.1", which
/// means 0.0.0.1 and 10.0.0.1. Nobody who writes "10.1" means that, so require
/// the full four-part form for IPv4 and let anything with a colon be IPv6.
let private tryAddress (text: string) : IPAddress option =
    let shapeOk = text.Contains ':' || (text.Split '.').Length = 4

    match shapeOk, IPAddress.TryParse text with
    | true, (true, address) ->
        match address with
        | null -> None
        | a -> Some a
    | _ -> None

/// An IPv4-mapped address (`::ffff:10.0.0.1`) is written as the IPv4 address it
/// stands for, so an entry means the same thing whichever way it is spelled.
let private normalize (address: IPAddress) =
    if address.IsIPv4MappedToIPv6 then address.MapToIPv4() else address

let private parseEntry (entry: string) : Result<Entry, string> =
    match entry.IndexOf '/' with
    | -1 ->
        match tryAddress entry with
        | Some address -> Ok(Address(normalize address))
        | None -> fail entry "is not an IP address or a network"
    | slash ->
        let addressText = entry.Substring(0, slash)
        let prefixText = entry.Substring(slash + 1)

        match tryAddress addressText, Int32.TryParse prefixText with
        | None, _ -> fail entry "is not an IP address or a network"
        | Some _, (false, _) -> fail entry "has a prefix length that is not a number"
        | Some address, (true, prefix) ->
            // `::ffff:10.0.0.0/104` is the IPv4 network 10.0.0.0/8: the prefix
            // counts the 96 bits of "::ffff:" too. A shorter prefix reaches
            // beyond IPv4 and stays an IPv6 network.
            let address, prefix =
                if address.IsIPv4MappedToIPv6 && prefix >= 96 then
                    address.MapToIPv4(), prefix - 96
                else
                    address, prefix

            let maxPrefix = if address.AddressFamily = AddressFamily.InterNetwork then 32 else 128

            if prefix < 0 || prefix > maxPrefix then
                fail entry $"has a prefix length outside 0 to %d{maxPrefix}"
            else
                // IPNetwork refuses an address with bits set beyond the prefix
                // (10.0.0.5/8). Say which network was probably meant.
                let bytes = address.GetAddressBytes()

                for i in 0 .. bytes.Length - 1 do
                    let keep = max 0 (min 8 (prefix - i * 8))
                    bytes[i] <- bytes[i] &&& byte (0xFF00 >>> keep)

                let network = IPAddress(bytes: byte array)

                if network.Equals address then
                    Ok(Network(System.Net.IPNetwork(address, prefix)))
                else
                    fail entry $"has address bits set beyond its prefix length; did you mean \"%O{network}/%d{prefix}\""

/// Parse the setting. Pure: no I/O, the same text gives the same answer.
let parse (raw: string | null) : Result<Trust, string> =
    match raw with
    | null -> Ok TrustAll
    | text when String.IsNullOrWhiteSpace text -> Ok TrustAll
    | text ->
        let entries =
            text.Split(',', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)

        if entries.Length = 0 then
            Error(
                $"%s{EnvironmentVariable} is \"%s{text}\", which holds no address. "
                + "List the proxy's IP addresses or networks, for example \"172.26.64.1, 10.0.0.0/8\", "
                + "or leave the setting out to believe forwarded headers from any sender."
            )
        else
            // The first bad entry wins, so the message names one thing to fix.
            let folder (acc: Result<Entry list, string>) (entry: string) =
                match acc with
                | Error _ -> acc
                | Ok soFar -> parseEntry entry |> Result.map (fun e -> e :: soFar)

            entries |> Array.fold folder (Ok []) |> Result.map (List.rev >> Only)

/// Read through a configuration lookup, accepting either spelling of the
/// setting, the same way PublicOrigin and DataDirectory do.
let fromConfiguration (read: string -> string | null) =
    match read ConfigurationKey with
    | null -> parse (read EnvironmentVariable)
    | value -> parse value

/// One line for the startup log, saying what the operator chose.
let describe (trust: Trust) =
    match trust with
    | TrustAll -> "Forwarded headers (X-Forwarded-*) are believed from any sender. Set UMBRAL__TRUSTED_PROXIES to limit them to your proxy."
    | Only entries ->
        let show =
            function
            | Address a -> string a
            | Network n -> string n

        "Forwarded headers (X-Forwarded-*) are believed only from: " + String.Join(", ", entries |> List.map show)

/// Applies the choice to ASP.NET Core's forwarded-headers options.
///
/// Both lists are cleared first, because the defaults trust loopback and Umbral
/// is not reached from loopback by its proxy. A single address goes to
/// `KnownProxies`, a network to `KnownIPNetworks`.
///
/// A dual-stack socket reports an IPv4 client as `::ffff:a.b.c.d`. The
/// middleware's check maps such an address back to IPv4 before comparing it with
/// both lists, so entries are written as plain IPv4 (see `normalize`) and match
/// either spelling of the connection address; TrustedProxiesTests proves it.
let configure (trust: Trust) (options: ForwardedHeadersOptions) =
    options.ForwardedHeaders <-
        ForwardedHeaders.XForwardedFor ||| ForwardedHeaders.XForwardedProto ||| ForwardedHeaders.XForwardedHost

    // KnownNetworks is deprecated in .NET 10; KnownIPNetworks replaces it.
    options.KnownIPNetworks.Clear()
    options.KnownProxies.Clear()

    match trust with
    | TrustAll -> ()
    | Only entries ->
        for entry in entries do
            match entry with
            | Address a -> options.KnownProxies.Add a
            | Network n -> options.KnownIPNetworks.Add n
