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

/// Start a round trip in `mode` and return the cookies plus the minted correlation.
let private startIn (mode: OAuthRoundTrip.ResponseMode) (clock: TimeProvider) slug server =
    let ctx = contextWith [] []
    let started = OAuthRoundTrip.start protectors clock ctx mode slug server "/connect/authorize?x=1"
    cookiesFrom ctx, started.Correlation

let private startOn (clock: TimeProvider) slug server = startIn OAuthRoundTrip.Redirect clock slug server

let private start (clock: TimeProvider) slug = startOn clock slug ""

/// Complete a round trip that was answered by a redirect: the values are in the
/// query string.
let private complete (clock: TimeProvider) cookies slug query =
    let ctx = contextWith cookies query
    OAuthRoundTrip.complete protectors clock ctx OAuthRoundTrip.Redirect slug (OAuthRoundTrip.answerOfQuery ctx)

let private answer (state: string option) (error: string option) : OAuthRoundTrip.Answer =
    { OAuthRoundTrip.Answer.State = state
      OAuthRoundTrip.Answer.Error = error }

/// Complete a round trip that was answered by a form post: the values come from
/// the body, which the caller has already read into an `Answer`.
let private completePost (clock: TimeProvider) cookies slug (answer: OAuthRoundTrip.Answer) =
    OAuthRoundTrip.complete protectors clock (contextWith cookies []) OAuthRoundTrip.FormPost slug answer

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
    let result =
        OAuthRoundTrip.complete protectors clock ctx OAuthRoundTrip.Redirect "discord" (OAuthRoundTrip.answerOfQuery ctx)

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

    match OAuthRoundTrip.complete other clock ctx OAuthRoundTrip.Redirect "discord" (OAuthRoundTrip.answerOfQuery ctx) with
    | Error OAuthRoundTrip.Corrupt -> ()
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``the correlation cookie is HttpOnly, Lax and scoped to connect`` () =
    let clock = clock ()
    let ctx = contextWith [] []
    OAuthRoundTrip.start protectors clock ctx OAuthRoundTrip.Redirect "discord" "" "/" |> ignore

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

// ---------------------------------------------------------------------------
// The server a fediverse round trip is bound to (specs/010 research R9)
// ---------------------------------------------------------------------------

[<Fact>]
let ``a correlation started with a server returns it from complete`` () =
    let clock = clock ()
    let cookies, correlation = startOn clock "fediverse" "mastodon.social"
    Assert.Equal("mastodon.social", correlation.Server)

    match complete clock cookies "fediverse" [ "code", "abc"; "state", correlation.State ] with
    | Ok recovered -> Assert.Equal("mastodon.social", recovered.Server)
    | Error e -> failwith $"expected success, got {e.Detail}"

[<Fact>]
let ``a six-field payload minted before the upgrade still decodes with no server`` () =
    let clock = clock ()
    let state = "the-state"

    // The old shape, protected under the same purpose the module uses.
    let payload =
        String.Join(
            '\u001f',
            [| state; "verifier"; "nonce"; "discord"; "/connect/authorize?x=1"; clock.GetUtcNow().ToString "o" |]
        )

    let protected' = protectors.CreateProtector("Umbral.Connect.v1").Protect payload

    match complete clock [ "umbral_connect", protected' ] "discord" [ "code", "abc"; "state", state ] with
    | Ok recovered ->
        Assert.Equal("", recovered.Server)
        Assert.Equal("/connect/authorize?x=1", recovered.ReturnUrl)
    | Error e -> failwith $"expected success, got {e.Detail}"

// ---------------------------------------------------------------------------
// The form-post mode (specs/013 research R2): a cookie of its own, because a
// Lax cookie is not sent on the cross-site POST Apple answers with
// ---------------------------------------------------------------------------

let private setCookies (ctx: HttpContext) =
    ctx.Response.Headers.SetCookie |> Seq.choose Option.ofObj |> List.ofSeq

let private startFormPost (clock: TimeProvider) slug = startIn OAuthRoundTrip.FormPost clock slug ""

[<Fact>]
let ``a form-post round trip sets its own cookie, SameSite None and always Secure`` () =
    let clock = clock ()
    // Plain http on purpose: the cookie is Secure regardless, because a browser
    // drops a SameSite=None cookie that is not.
    let ctx = contextWith [] []
    ctx.Request.Scheme <- "http"
    OAuthRoundTrip.start protectors clock ctx OAuthRoundTrip.FormPost "apple" "" "/" |> ignore

    let headers = setCookies ctx |> List.map _.ToLowerInvariant()
    let header = headers |> List.exactlyOne

    Assert.StartsWith("__secure-umbral_connect_post=", header)
    Assert.Contains("samesite=none", header)
    Assert.Contains("secure", header)
    Assert.Contains("httponly", header)
    Assert.Contains("path=/connect", header)
    Assert.Empty(headers |> List.filter (fun h -> h.StartsWith "umbral_connect="))

[<Fact>]
let ``the form-post cookie carries the Secure prefix and the Lax cookie does not`` () =
    let clock = clock ()

    // `__Secure-`: a browser refuses to let a plain-http response set it, so a man
    // in the middle on http cannot plant a correlation of their own. Only the
    // always-Secure cookie can have it; the Lax one is used over http in development.
    let post, _ = startFormPost clock "apple"
    let lax, _ = start clock "discord"

    Assert.Equal<string list>([ "__Secure-umbral_connect_post" ], post |> List.map fst)
    Assert.Equal<string list>([ "umbral_connect" ], lax |> List.map fst)

[<Fact>]
let ``a redirect round trip still sets only the Lax cookie, and is not forced Secure over http`` () =
    let clock = clock ()
    let ctx = contextWith [] []
    ctx.Request.Scheme <- "http"
    OAuthRoundTrip.start protectors clock ctx OAuthRoundTrip.Redirect "discord" "" "/" |> ignore

    let header = setCookies ctx |> List.exactlyOne |> _.ToLowerInvariant()

    Assert.StartsWith("umbral_connect=", header)
    Assert.Contains("samesite=lax", header)
    Assert.DoesNotContain("secure", header)
    Assert.DoesNotContain("umbral_connect_post", header)

[<Fact>]
let ``a well-formed form post completes`` () =
    let clock = clock ()
    let cookies, correlation = startFormPost clock "apple"

    match completePost clock cookies "apple" (answer (Some correlation.State) None) with
    | Ok recovered ->
        Assert.Equal(correlation.CodeVerifier, recovered.CodeVerifier)
        Assert.Equal(correlation.Nonce, recovered.Nonce)
        Assert.Equal("apple", recovered.Slug)
        Assert.Equal("/connect/authorize?x=1", recovered.ReturnUrl)
    | Error e -> failwith $"expected success, got {e.Detail}"

[<Fact>]
let ``a form post with the wrong state is refused and the response deletes the cookie`` () =
    let clock = clock ()
    let cookies, _ = startFormPost clock "apple"
    let ctx = contextWith cookies []

    match OAuthRoundTrip.complete protectors clock ctx OAuthRoundTrip.FormPost "apple" (answer (Some "not-the-state") None) with
    | Error OAuthRoundTrip.StateMismatch -> ()
    | other -> failwith $"unexpected: {other}"

    // Spent whatever the outcome, with the attributes it was set with so that a
    // browser really removes it.
    let deletion =
        setCookies ctx
        |> List.map _.ToLowerInvariant()
        |> List.find (fun h -> h.StartsWith "__secure-umbral_connect_post=")

    Assert.Contains("expires=thu, 01 jan 1970", deletion)
    Assert.Contains("path=/connect", deletion)
    Assert.Contains("samesite=none", deletion)
    Assert.Contains("secure", deletion)

[<Fact>]
let ``a form post with no state, or an empty one, is refused as missing`` () =
    let clock = clock ()
    let cookies, _ = startFormPost clock "apple"

    match completePost clock cookies "apple" (answer None None) with
    | Error OAuthRoundTrip.StateMissing -> ()
    | other -> failwith $"unexpected: {other}"

    let cookies, _ = startFormPost clock "apple"

    match completePost clock cookies "apple" (answer (Some "") None) with
    | Error OAuthRoundTrip.StateMissing -> ()
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``a form post carrying Apple's cancel is reported as the provider's error`` () =
    let clock = clock ()
    let cookies, correlation = startFormPost clock "apple"

    match completePost clock cookies "apple" (answer (Some correlation.State) (Some "user_cancelled_authorize")) with
    | Error(OAuthRoundTrip.ProviderReturnedError "user_cancelled_authorize") -> ()
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``a replayed form post is refused`` () =
    let clock = clock ()
    let cookies, correlation = startFormPost clock "apple"
    let posted = answer (Some correlation.State) None

    match completePost clock cookies "apple" posted with
    | Ok _ -> ()
    | Error e -> failwith $"the first use should succeed, got {e.Detail}"

    // The client kept the cookie and sent it again: the shared table of
    // redeemed states refuses it.
    match completePost clock cookies "apple" posted with
    | Error OAuthRoundTrip.Replayed -> ()
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``a form-post correlation expires after ten minutes`` () =
    let clock = FakeTimeProvider DateTimeOffset.UtcNow
    let cookies, correlation = startFormPost clock "apple"
    clock.Advance(TimeSpan.FromMinutes 11.0)

    match completePost clock cookies "apple" (answer (Some correlation.State) None) with
    | Error OAuthRoundTrip.Expired -> ()
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``a form post on another connector's route is refused`` () =
    let clock = clock ()
    let cookies, correlation = startFormPost clock "apple"

    match completePost clock cookies "discord" (answer (Some correlation.State) None) with
    | Error(OAuthRoundTrip.SlugMismatch("apple", "discord")) -> ()
    | other -> failwith $"unexpected: {other}"

let private renamed (name: string) (cookies: (string * string) list) =
    cookies |> List.map (fun (_, value) -> name, value)

[<Fact>]
let ``a Lax correlation presented as the form-post cookie is refused as corrupt`` () =
    let clock = clock ()
    let cookies, correlation = start clock "apple"

    // A different protector purpose: the value cannot be decrypted under the
    // other mode's, whatever it is called.
    match completePost clock (renamed "__Secure-umbral_connect_post" cookies) "apple" (answer (Some correlation.State) None) with
    | Error OAuthRoundTrip.Corrupt -> ()
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``a form-post correlation presented as the Lax cookie is refused as corrupt`` () =
    let clock = clock ()
    let cookies, correlation = startFormPost clock "apple"

    match complete clock (renamed "umbral_connect" cookies) "apple" [ "state", correlation.State ] with
    | Error OAuthRoundTrip.Corrupt -> ()
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``a request with only the Lax cookie has no form-post correlation`` () =
    let clock = clock ()
    let cookies, correlation = start clock "apple"

    match completePost clock cookies "apple" (answer (Some correlation.State) None) with
    | Error OAuthRoundTrip.NoCorrelation -> ()
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``peek and returnUrlOf read the cookie of their own mode only`` () =
    let clock = clock ()
    let postCookies, _ = startFormPost clock "apple"
    let laxCookies, _ = start clock "discord"

    let peekIn mode cookies =
        OAuthRoundTrip.returnUrlOf protectors clock (contextWith cookies []) mode

    Assert.Equal(Some "/connect/authorize?x=1", peekIn OAuthRoundTrip.FormPost postCookies)
    Assert.Equal(None, peekIn OAuthRoundTrip.Redirect postCookies)
    Assert.Equal(Some "/connect/authorize?x=1", peekIn OAuthRoundTrip.Redirect laxCookies)
    Assert.Equal(None, peekIn OAuthRoundTrip.FormPost laxCookies)

// ---------------------------------------------------------------------------
// `state` before the provider's `error`, and a cleaned error word (code review
// B1): a page on another site can post any `error` it likes and make the
// browser send the cookie along
// ---------------------------------------------------------------------------

[<Fact>]
let ``an error with no state is a missing state, not the provider's word`` () =
    for mode in [ OAuthRoundTrip.Redirect; OAuthRoundTrip.FormPost ] do
        let clock = clock ()

        let result =
            match mode with
            | OAuthRoundTrip.Redirect ->
                let cookies, _ = start clock "discord"
                complete clock cookies "discord" [ "error", "access_denied" ]
            | OAuthRoundTrip.FormPost ->
                let cookies, _ = startFormPost clock "apple"
                completePost clock cookies "apple" (answer None (Some "user_cancelled_authorize"))

        match result with
        | Error OAuthRoundTrip.StateMissing -> ()
        | other -> failwith $"{mode}: unexpected {other}"

[<Fact>]
let ``an error with a wrong state is a state mismatch, not the provider's word`` () =
    for mode in [ OAuthRoundTrip.Redirect; OAuthRoundTrip.FormPost ] do
        let clock = clock ()

        let result =
            match mode with
            | OAuthRoundTrip.Redirect ->
                let cookies, _ = start clock "discord"
                complete clock cookies "discord" [ "error", "access_denied"; "state", "not-the-state" ]
            | OAuthRoundTrip.FormPost ->
                let cookies, _ = startFormPost clock "apple"
                completePost clock cookies "apple" (answer (Some "not-the-state") (Some "user_cancelled_authorize"))

        match result with
        | Error OAuthRoundTrip.StateMismatch -> ()
        | other -> failwith $"{mode}: unexpected {other}"

/// Complete a fresh round trip in `mode` with the right `state` and this `error`.
let private withRightState (mode: OAuthRoundTrip.ResponseMode) (error: string) =
    let clock = clock ()

    match mode with
    | OAuthRoundTrip.Redirect ->
        let cookies, correlation = start clock "discord"
        complete clock cookies "discord" [ "error", error; "state", correlation.State ]
    | OAuthRoundTrip.FormPost ->
        let cookies, correlation = startFormPost clock "apple"
        completePost clock cookies "apple" (answer (Some correlation.State) (Some error))

[<Theory>]
[<InlineData("access_denied", "access_denied")>]
[<InlineData("user_cancelled_authorize", "user_cancelled_authorize")>]
[<InlineData("invalid_request", "invalid_request")>]
[<InlineData("a;b=c", "other")>]
[<InlineData("Access_Denied", "other")>]
[<InlineData("ACCESS_DENIED", "other")>]
[<InlineData("", "other")>]
[<InlineData("with space", "other")>]
[<InlineData("digits_123", "other")>]
[<InlineData("caf\u00e9", "other")>]
let ``an error with the right state is the provider's word, cleaned`` (sent: string, recorded: string) =
    for mode in [ OAuthRoundTrip.Redirect; OAuthRoundTrip.FormPost ] do
        match withRightState mode sent with
        | Error(OAuthRoundTrip.ProviderReturnedError word) ->
            Assert.Equal(recorded, word)
            Assert.Equal($"provider_error:{recorded}", (OAuthRoundTrip.ProviderReturnedError word).Detail)
        | other -> failwith $"{mode}: unexpected {other}"

[<Fact>]
let ``an error of 64 letters passes and one of 65 or 10000 is other`` () =
    for mode in [ OAuthRoundTrip.Redirect; OAuthRoundTrip.FormPost ] do
        let word (sent: string) =
            match withRightState mode sent with
            | Error(OAuthRoundTrip.ProviderReturnedError word) -> word
            | other -> failwith $"{mode}: unexpected {other}"

        Assert.Equal(String('a', 64), word (String('a', 64)))
        Assert.Equal("other", word (String('a', 65)))
        Assert.Equal("other", word (String('a', 10000)))

[<Fact>]
let ``a provider's error spends the correlation`` () =
    let clock = clock ()
    let cookies, correlation = startFormPost clock "apple"
    let cancelled = answer (Some correlation.State) (Some "user_cancelled_authorize")

    match completePost clock cookies "apple" cancelled with
    | Error(OAuthRoundTrip.ProviderReturnedError _) -> ()
    | other -> failwith $"unexpected: {other}"

    // The client kept the cookie and sent the answer again, this time with no error.
    match completePost clock cookies "apple" (answer (Some correlation.State) None) with
    | Error OAuthRoundTrip.Replayed -> ()
    | other -> failwith $"unexpected: {other}"

// ---------------------------------------------------------------------------
// Single use in one atomic step (code review N1)
// ---------------------------------------------------------------------------

[<Fact>]
let ``concurrent callbacks with the same correlation give exactly one success`` () =
    let clock = clock ()
    let cookies, correlation = startFormPost clock "apple"
    let posted = answer (Some correlation.State) None
    let attempts = 16
    let gate = new Threading.Barrier(attempts)
    let results = Collections.Concurrent.ConcurrentBag<Result<OAuthRoundTrip.Correlation, OAuthRoundTrip.RoundTripError>>()

    // A thread each, so that all of them are really waiting at the barrier
    // before any goes on. Each has a context of its own with the same cookie, as
    // separate requests do.
    let threads =
        [ for _ in 1..attempts ->
              Threading.Thread(fun () ->
                  let ctx = contextWith cookies []
                  gate.SignalAndWait()
                  results.Add(OAuthRoundTrip.complete protectors clock ctx OAuthRoundTrip.FormPost "apple" posted)) ]

    threads |> List.iter _.Start()
    threads |> List.iter _.Join()

    let all = List.ofSeq results
    Assert.Equal(attempts, all.Length)
    Assert.Equal(1, all |> List.filter Result.isOk |> List.length)

    Assert.True(
        all
        |> List.forall (fun r ->
            match r with
            | Ok _
            | Error OAuthRoundTrip.Replayed -> true
            | _ -> false),
        "every other callback is a replay"
    )
