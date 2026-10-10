/// The upstream round trip as a state machine, with no provider involved.
///
/// Hand-rolling the OAuth client means Umbral owns `state`, PKCE and `nonce`
/// correctness (research.md D1). The cost of that is capped by this file: the
/// machinery is written once, shared by all three providers, and every way it
/// can be attacked is exercised here rather than three times over HTTP.
module Umbral.Tests.OAuthRoundTripTests

open System
open Microsoft.AspNetCore.DataProtection
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives
open Microsoft.Extensions.Time.Testing
open Xunit
open Umbral.Infrastructure

let private protectors = DataProtectionProvider.Create "Umbral.Tests"

/// A context carrying forward whatever cookies the previous one set, which is
/// what a browser does and what every one of these cases depends on.
let private contextWith (cookies: (string * string) list) (query: (string * string) list) =
    let ctx = DefaultHttpContext()
    ctx.Request.Scheme <- "https"
    ctx.Request.Host <- HostString "umbral.test"

    if not cookies.IsEmpty then
        let header = cookies |> List.map (fun (k, v) -> $"{k}={v}") |> String.concat "; "
        ctx.Request.Headers.Cookie <- StringValues header

    ctx.Request.QueryString <-
        QueryString(
            "?"
            + (query |> List.map (fun (k, v) -> $"{k}={Uri.EscapeDataString v}") |> String.concat "&")
        )

    ctx

/// The cookies a response set, in the shape a request header wants.
let private cookiesFrom (ctx: HttpContext) =
    ctx.Response.Headers.SetCookie
    |> Seq.choose Option.ofObj
    |> Seq.map (fun value -> value.Split(';').[0])
    |> Seq.choose (fun pair ->
        match pair.Split('=', 2) with
        | [| name; v |] when v <> "" -> Some(name, v)
        | _ -> None)
    |> List.ofSeq

let private clock () = FakeTimeProvider DateTimeOffset.UtcNow

/// Start a round trip and return the cookies plus the minted correlation.
let private start (clock: TimeProvider) slug =
    let ctx = contextWith [] []
    let started = OAuthRoundTrip.start protectors clock ctx slug "/connect/authorize?x=1"
    cookiesFrom ctx, started.Correlation

let private complete (clock: TimeProvider) cookies slug query =
    OAuthRoundTrip.complete protectors clock (contextWith cookies query) slug

// ---------------------------------------------------------------------------

[<Fact>]
let ``a well-formed callback completes`` () =
    let clock = clock ()
    let cookies, correlation = start clock "discord"

    match complete clock cookies "discord" [ "code", "abc"; "state", correlation.State ] with
    | Ok recovered ->
        Assert.Equal(correlation.CodeVerifier, recovered.CodeVerifier)
        Assert.Equal("/connect/authorize?x=1", recovered.ReturnUrl)
    | Error e -> failwith $"expected success, got {e.Detail}"

[<Fact>]
let ``no correlation cookie is refused`` () =
    let clock = clock ()
    let _, correlation = start clock "discord"

    // Someone arriving cold: a replayed link, or poking at the route.
    match complete clock [] "discord" [ "code", "abc"; "state", correlation.State ] with
    | Error OAuthRoundTrip.NoCorrelation -> ()
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``a missing state is refused`` () =
    let clock = clock ()
    let cookies, _ = start clock "discord"

    match complete clock cookies "discord" [ "code", "abc" ] with
    | Error OAuthRoundTrip.StateMissing -> ()
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``a mismatched state is refused`` () =
    let clock = clock ()
    let cookies, _ = start clock "discord"

    // The CSRF case: an attacker's authorization response presented to a
    // victim's session.
    match complete clock cookies "discord" [ "code", "abc"; "state", "not-the-state" ] with
    | Error OAuthRoundTrip.StateMismatch -> ()
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``a REPLAYED callback is refused, because the cookie is gone`` () =
    let clock = clock ()
    let cookies, correlation = start clock "discord"
    let query = [ "code", "abc"; "state", correlation.State ]

    // First use succeeds...
    match complete clock cookies "discord" query with
    | Ok _ -> ()
    | Error e -> failwith $"the first use should succeed, got {e.Detail}"

    // ...and the second is refused even though the client re-sent the cookie.
    // Deleting it only stops a cooperating browser; the correlation is
    // stateless, so the server remembers which states it has redeemed.
    let ctx = contextWith cookies query
    let result = OAuthRoundTrip.complete protectors clock ctx "discord"

    match result with
    | Error OAuthRoundTrip.Replayed -> ()
    | other -> failwith $"a replayed callback must be refused as a replay, got {other}"

    // And the response deletes the cookie whatever the outcome.
    let setCookies = ctx.Response.Headers.SetCookie |> Seq.choose Option.ofObj |> List.ofSeq
    Assert.True(setCookies |> List.exists (fun v -> v.Contains "umbral_connect"))

[<Fact>]
let ``an expired correlation is refused even if the cookie survived`` () =
    let clock = FakeTimeProvider DateTimeOffset.UtcNow
    let cookies, correlation = start clock "discord"

    // A client controls its own cookie jar, so Max-Age is a request, not a
    // guarantee. The server's clock is the one that counts.
    clock.Advance(TimeSpan.FromMinutes 11.0)

    match complete clock cookies "discord" [ "code", "abc"; "state", correlation.State ] with
    | Error OAuthRoundTrip.Expired -> ()
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``a correlation still inside its window is accepted`` () =
    let clock = FakeTimeProvider DateTimeOffset.UtcNow
    let cookies, correlation = start clock "discord"
    clock.Advance(TimeSpan.FromMinutes 9.0)

    match complete clock cookies "discord" [ "code", "abc"; "state", correlation.State ] with
    | Ok _ -> ()
    | Error e -> failwith $"unexpected: {e.Detail}"

[<Fact>]
let ``a callback on another connector's route is refused`` () =
    let clock = clock ()
    let cookies, correlation = start clock "discord"

    // The IdP mix-up case. Distinct routes per provider exist so the routing
    // layer catches this before our own lookup has to, and this is the check
    // that makes the separation mean something.
    match complete clock cookies "github" [ "code", "abc"; "state", correlation.State ] with
    | Error(OAuthRoundTrip.SlugMismatch(expected, actual)) ->
        Assert.Equal("discord", expected)
        Assert.Equal("github", actual)
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``a provider's own error is reported, and checked after the cookie is gone`` () =
    let clock = clock ()
    let cookies, correlation = start clock "discord"

    match complete clock cookies "discord" [ "error", "access_denied"; "state", correlation.State ] with
    | Error(OAuthRoundTrip.ProviderReturnedError "access_denied") -> ()
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``a tampered cookie is refused`` () =
    let clock = clock ()
    let cookies, correlation = start clock "discord"

    let tampered =
        cookies |> List.map (fun (name, value) -> name, value.Substring(0, value.Length - 4) + "AAAA")

    // Indistinguishable from an attack, and treated as one.
    match complete clock tampered "discord" [ "code", "abc"; "state", correlation.State ] with
    | Error OAuthRoundTrip.Corrupt -> ()
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``a cookie from a different key ring is refused`` () =
    let clock = clock ()
    let cookies, correlation = start clock "discord"

    // A replaced container whose /data/keys was not mounted. The operator sees
    // a failed sign-in rather than a stack trace, and nothing is trusted.
    let other = DataProtectionProvider.Create "Umbral.Tests.Other"
    let ctx = contextWith cookies [ "code", "abc"; "state", correlation.State ]

    match OAuthRoundTrip.complete other clock ctx "discord" with
    | Error OAuthRoundTrip.Corrupt -> ()
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``the correlation cookie is HttpOnly, Lax and scoped to connect`` () =
    let clock = clock ()
    let ctx = contextWith [] []
    OAuthRoundTrip.start protectors clock ctx "discord" "/" |> ignore

    let header =
        ctx.Response.Headers.SetCookie
        |> Seq.choose Option.ofObj
        |> Seq.find (fun v -> v.StartsWith "umbral_connect")

    Assert.Contains("httponly", header.ToLowerInvariant())
    Assert.Contains("path=/connect", header.ToLowerInvariant())

    // Lax, not Strict: the browser arrives here on the provider's top-level
    // redirect back, and Strict is withheld on exactly that navigation — every
    // login would fail. Not None either, which some browsers drop over http.
    Assert.Contains("samesite=lax", header.ToLowerInvariant())

[<Fact>]
let ``two round trips never share a state or a verifier`` () =
    let clock = clock ()
    let states = List.init 50 (fun _ -> snd (start clock "discord"))

    Assert.Equal(50, states |> List.map (fun c -> c.State) |> List.distinct |> List.length)
    Assert.Equal(50, states |> List.map (fun c -> c.CodeVerifier) |> List.distinct |> List.length)
    Assert.Equal(50, states |> List.map (fun c -> c.Nonce) |> List.distinct |> List.length)
