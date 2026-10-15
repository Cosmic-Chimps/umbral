/// Rate limits on the places a guess can be made (FR-038).
///
/// ASP.NET Core's built-in limiter, attached per endpoint, so a route that is
/// not listed here is not limited and nothing about the limiter can be
/// "forgotten" into a protocol endpoint by accident. The limits are constants,
/// not settings: nobody asked for a knob (Principle VII), and a knob is a way to
/// switch off the thing this exists for.
///
/// It counts REQUESTS to these endpoints, not only failed ones. For the sign-in
/// and setup forms that is the same thing — a page that is not being submitted
/// is not being counted, because only the POST is limited — and it is the only
/// way to refuse an attempt before running the password hash on it.
module Umbral.Infrastructure.RateLimits

open System
open System.Net
open System.Net.Sockets
open System.Threading.RateLimiting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.RateLimiting

/// `POST /admin/signin` and `POST /admin/setup`: the operator credential and the
/// setup token. Strict, because the operator is one person who types a
/// credential a few times at most, and because what it guards is everything.
/// 10 attempts per 10 minutes is about 1,400 a day from one source against a
/// 12-character minimum credential and a random setup token.
[<Literal>]
let AdminAuth = "admin-auth"

/// `GET /connect/{slug}/start` and `GET /connect/{slug}/callback`: an end user
/// signing in through a provider. `start` is limited because it mints the
/// round-trip cookie that a valid callback needs; `callback` because it is the
/// request that makes Umbral call the provider with the connector's secret.
/// Looser than the admin limit, because many people can share one address (an
/// office, a school, carrier-grade NAT) and one login is two requests: 30 a
/// minute is fifteen logins a minute from one address.
///
/// The OIDC endpoints OpenIddict serves (`/connect/authorize`, `/connect/token`
/// and the rest) are deliberately NOT here. A client application calls them from
/// its own server, so every login of every user of that app arrives from one
/// address, and a limit would fail logins for exactly the healthy case. Their
/// secrets are already random 256-bit values (Ids.fs), which is not guessable.
[<Literal>]
let SignIn = "sign-in"

let private adminWindow = TimeSpan.FromMinutes 10.0
let private signInWindow = TimeSpan.FromMinutes 1.0

/// The partition key for one client address.
///
/// IPv6 is counted per /64, not per address. One client normally controls at
/// least a /64 (2^64 addresses), so a limit per full address could be walked
/// around by rotating the low half of the address on every attempt. The key is
/// the network, written as `2001:db8:1::/64`. A scope id (`%eth0`) names a local
/// interface, not a client, so it is dropped.
///
/// An IPv4-mapped IPv6 address (`::ffff:203.0.113.10`, which a dual-stack
/// socket can report) is the same client as `203.0.113.10`, so it is folded to
/// the IPv4 form. Otherwise a client would have two budgets, one per spelling.
let partitionKey (address: IPAddress) : string =
    if address.IsIPv4MappedToIPv6 then
        string (address.MapToIPv4())
    elif address.AddressFamily = AddressFamily.InterNetworkV6 then
        let bytes = address.GetAddressBytes()
        Array.fill bytes 8 8 0uy
        $"{IPAddress bytes}/64"
    else
        string address

/// The source of a request, as resolved after `UseForwardedHeaders`.
///
/// When there is no address at all (a TestServer, or a unix socket) everything
/// shares one partition named "unknown". Limited together is the safe way to
/// fail: the alternative, an exemption, would mean a request that hides its
/// origin is the one request that is never limited.
let source (ctx: HttpContext) : string =
    match ctx.Connection.RemoteIpAddress with
    | null -> "unknown"
    | address -> partitionKey address

let private fixedWindow (permits: int) (window: TimeSpan) (key: string) =
    RateLimitPartition.GetFixedWindowLimiter(
        key,
        fun _ ->
            FixedWindowRateLimiterOptions(
                PermitLimit = permits,
                Window = window,
                // Refuse rather than hold the request: a queue is a place for
                // an attacker's requests to wait, using our memory.
                QueueLimit = 0,
                AutoReplenishment = true
            )
    )

/// Registers both policies. Partitioned by `source`, so one client being
/// limited does not affect any other.
let configure (options: RateLimiterOptions) =
    options.AddPolicy(AdminAuth, fun ctx -> fixedWindow 10 adminWindow (source ctx)) |> ignore
    options.AddPolicy(SignIn, fun ctx -> fixedWindow 30 signInWindow (source ctx)) |> ignore

/// How long a rejected caller should wait, in whole seconds. The limiter
/// reports the time to the end of the window; the longest window is the
/// fallback, so a caller is never told to come back too early.
let retryAfterSeconds (context: OnRejectedContext) : int =
    match context.Lease.TryGetMetadata MetadataName.RetryAfter with
    | true, retry -> max 1 (int (Math.Ceiling retry.TotalSeconds))
    | false, _ -> int adminWindow.TotalSeconds
