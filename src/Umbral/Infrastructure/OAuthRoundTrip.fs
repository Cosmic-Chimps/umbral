/// The security of every upstream sign-in, written once.
///
/// Hand-rolling the OAuth client (research.md D1) means Umbral owns `state`,
/// PKCE and `nonce` correctness. The cost of that is capped structurally: this
/// file is the only place any of it happens, it is shared by all three
/// providers, and it is tested as a state machine with no provider involved.
/// There is deliberately no per-provider security code to get subtly wrong
/// three times.
module Umbral.Infrastructure.OAuthRoundTrip

open System
open Microsoft.AspNetCore.DataProtection
open Microsoft.AspNetCore.Http
open Umbral.Domains

[<Literal>]
let private CookiePath = "/connect"

/// Ten minutes. Matches GitHub's authorization-code lifetime, so a correlation
/// never outlives the code it is protecting.
let private Lifetime = TimeSpan.FromMinutes 10.0

/// How the provider sends its answer back to the callback.
type ResponseMode =
    /// A redirect: the answer is in the query string of a GET. Every provider but Apple.
    | Redirect
    /// `response_mode=form_post`: the answer is the body of a cross-site POST (Apple).
    | FormPost

/// The two values of the provider's answer this file judges, read by the
/// caller from wherever the answer came: the query string or the form.
type Answer =
    { State: string option
      Error: string option }

/// The answer of a redirect, read from the query string: the first value of
/// each, and a null as nothing.
let answerOfQuery (ctx: HttpContext) : Answer =
    let first (name: string) =
        match ctx.Request.Query.TryGetValue name with
        | true, value when value.Count > 0 -> Option.ofObj (value[0])
        | _ -> None

    { State = first "state"
      Error = first "error" }

// ---------------------------------------------------------------------------
// The cookie of each mode
//
// A `SameSite=Lax` cookie is not sent by a browser on a cross-site POST, and
// Apple answers with exactly that: a form posted from appleid.apple.com. So the
// form-post mode has a cookie of its own that is `SameSite=None`, which a
// browser sends on a cross-site POST, and `Secure`, which a browser requires of
// a `None` cookie and drops it without (Apple accepts only https return URLs
// anyway). That is what ASP.NET Core's own OpenID Connect handler does for the
// same reason.
//
// `None` is not used for the other providers because a browser does not send it
// over plain http, which the laptop trial runs on. Their cookie stays `Lax`,
// exactly as it was.
//
// The two modes differ in cookie name AND in protector purpose, so a
// correlation minted in one mode cannot be redeemed in the other by
// construction: the other mode does not look at that cookie, and could not
// decrypt it if handed the value. No flag inside the payload is trusted for it.
// ---------------------------------------------------------------------------

type private CookieSettings =
    { CookieName: string
      Purpose: string
      SameSite: SameSiteMode
      Secure: bool }

let private settingsFor (ctx: HttpContext) (mode: ResponseMode) =
    match mode with
    | Redirect ->
        { CookieName = "umbral_connect"
          Purpose = "Umbral.Connect.v1"
          SameSite = SameSiteMode.Lax
          Secure = ctx.Request.IsHttps }
    | FormPost ->
        { CookieName = "umbral_connect_post"
          Purpose = "Umbral.Connect.FormPost.v1"
          SameSite = SameSiteMode.None
          Secure = true }

/// What is stashed between the redirect out and the callback back.
type Correlation =
    { State: string
      CodeVerifier: string
      Nonce: string
      /// Checked against the slug in the callback path: a response that comes
      /// back on a different connector's route is not this correlation's.
      Slug: string
      /// The authorization request to replay once the user is signed in.
      ReturnUrl: string
      IssuedAtUtc: DateTimeOffset
      /// The normalised fediverse server this round trip is bound to, empty for
      /// every other kind. The callback exchanges the code there and nowhere
      /// else (specs/010 research R9).
      Server: string }

type RoundTripError =
    | NoCorrelation
    | StateMissing
    | StateMismatch
    | Expired
    /// A correlation that has already been redeemed once.
    | Replayed
    | SlugMismatch of expected: string * actual: string
    | ProviderReturnedError of error: string
    | Corrupt

    member this.Detail =
        match this with
        | NoCorrelation -> "no_correlation_cookie"
        | StateMissing -> "state_missing"
        | StateMismatch -> "state_mismatch"
        | Expired -> "correlation_expired"
        | Replayed -> "correlation_replayed"
        | SlugMismatch _ -> "slug_mismatch"
        | ProviderReturnedError error -> $"provider_error:{error}"
        | Corrupt -> "correlation_corrupt"

// ---------------------------------------------------------------------------
// Serialisation
//
// Seven strings joined by a unit separator, not JSON.
//
// The first version used System.Text.Json over a `type private Stored`. F#
// compiles a private type as non-public, which the serializer cannot construct,
// so every correlation came back as a decode failure — indistinguishable from
// tampering, and reported as such. Joining seven values we control needs no
// serializer at all.
//
// None of these values can contain the separator: they are all base64url
// (state, verifier, nonce), a validated slug, a local path, or a host (a host
// cannot contain the unit separator, so the server field is safe too).
// ---------------------------------------------------------------------------

[<Literal>]
let private Separator = '\u001f'

let private encode (c: Correlation) =
    String.Join(Separator, [| c.State; c.CodeVerifier; c.Nonce; c.Slug; c.ReturnUrl; c.IssuedAtUtc.ToString "o"; c.Server |])

let private decode (payload: string) =
    let build (state: string) (verifier: string) (nonce: string) (slug: string) (returnUrl: string) (issuedAt: string) (server: string) =
        match DateTimeOffset.TryParse(issuedAt, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind) with
        | true, issuedAt ->
            Some
                { State = state
                  CodeVerifier = verifier
                  Nonce = nonce
                  Slug = slug
                  ReturnUrl = returnUrl
                  IssuedAtUtc = issuedAt
                  Server = server }
        | _ -> None

    match payload.Split Separator with
    | [| state; verifier; nonce; slug; returnUrl; issuedAt; server |] ->
        build state verifier nonce slug returnUrl issuedAt server
    // Six fields: a cookie minted just before an upgrade, which has no server.
    | [| state; verifier; nonce; slug; returnUrl; issuedAt |] -> build state verifier nonce slug returnUrl issuedAt ""
    | _ -> None

// ---------------------------------------------------------------------------
// Begin
// ---------------------------------------------------------------------------

type Started =
    { Correlation: Correlation
      CodeChallenge: string }

/// Mint the correlation and set the cookie of `mode`. Returns what the adapter
/// needs to build its authorize URL.
let start
    (protectorProvider: IDataProtectionProvider)
    (clock: TimeProvider)
    (ctx: HttpContext)
    (mode: ResponseMode)
    (slug: string)
    (server: string)
    (returnUrl: string)
    : Started =
    let verifier = Ids.newCodeVerifier ()

    let correlation =
        { State = Ids.newStateValue ()
          CodeVerifier = verifier
          Nonce = Ids.newNonce ()
          Slug = slug
          ReturnUrl = returnUrl
          IssuedAtUtc = clock.GetUtcNow()
          Server = server }

    let settings = settingsFor ctx mode
    let protector = protectorProvider.CreateProtector settings.Purpose
    let payload = protector.Protect(encode correlation)

    let options = CookieOptions()
    options.HttpOnly <- true
    options.IsEssential <- true
    options.Path <- CookiePath
    // Lax, not Strict, for a redirect: the browser returns here on the
    // provider's top-level redirect, and a Strict cookie is withheld on exactly
    // that navigation, so every login would fail. The form-post mode needs
    // `None`. Why each, and why not `None` everywhere: see the section above.
    options.SameSite <- settings.SameSite
    options.Secure <- settings.Secure
    options.MaxAge <- Lifetime

    ctx.Response.Cookies.Append(settings.CookieName, payload, options)

    { Correlation = correlation
      CodeChallenge = Ids.codeChallengeS256 verifier }

// ---------------------------------------------------------------------------
// Single use, enforced by the server
//
// Deleting the cookie makes a replay fail for a COOPERATING browser — a
// refresh of the callback URL, which is the common case. It does nothing about
// a client that simply keeps the cookie and sends it again, because the
// correlation is stateless: everything needed to accept it travels in the
// cookie itself.
//
// So the states that have been used are remembered here. One entry per sign-in
// attempt, expiring with the correlation it belongs to, in a product that is
// one container by design — the bound is "sign-ins in the last ten minutes",
// which for this audience is single digits.
//
// Without this, "single use" was a claim in a comment rather than a property.
// ---------------------------------------------------------------------------

let private consumed = Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset>()

let private rememberConsumed (now: DateTimeOffset) (state: string) =
    // Prune while inserting: no timer, no background service, and the table
    // cannot outgrow the window.
    for entry in consumed do
        if now - entry.Value > Lifetime then
            consumed.TryRemove entry.Key |> ignore

    consumed.TryAdd(state, now)

let private alreadyConsumed (now: DateTimeOffset) (state: string) =
    match consumed.TryGetValue state with
    | true, usedAt -> now - usedAt <= Lifetime
    | _ -> false

// ---------------------------------------------------------------------------
// Complete
// ---------------------------------------------------------------------------

/// With the attributes the cookie was set with: a browser removes a cookie only
/// when the deleting one has the same name, path and, for `SameSite=None`, the
/// same `Secure`.
let private deleteCookie (ctx: HttpContext) (settings: CookieSettings) =
    let options = CookieOptions()
    options.Path <- CookiePath
    options.Secure <- settings.Secure
    options.SameSite <- settings.SameSite
    ctx.Response.Cookies.Delete(settings.CookieName, options)

/// The cookie of `mode`, when the request carries a non-empty one.
let private readCookie (ctx: HttpContext) (settings: CookieSettings) =
    match ctx.Request.Cookies.TryGetValue settings.CookieName with
    // A `when` guard does not narrow nullness for the compiler, so the
    // null case is matched rather than tested.
    | true, value ->
        match Option.ofObj value with
        | Some v when v.Length > 0 -> Some v
        | _ -> None
    | _ -> None

/// Validate the callback and surrender the correlation.
///
/// `answer` is what the provider sent, read by the caller from the query string
/// of a redirect or the form of a post; `mode` says which cookie to look for.
///
/// Order matters here and is the whole point of the function:
///
///   1. read the cookie
///   2. DELETE it, before anything else can fail or succeed
///   3. only then compare
///
/// Deleting first is what makes a replayed callback find nothing. If deletion
/// happened after a successful exchange, an attacker could replay the callback
/// until something raced.
let complete
    (protectorProvider: IDataProtectionProvider)
    (clock: TimeProvider)
    (ctx: HttpContext)
    (mode: ResponseMode)
    (slug: string)
    (answer: Answer)
    : Result<Correlation, RoundTripError> =

    let settings = settingsFor ctx mode
    let cookie = readCookie ctx settings

    // Unconditionally, and before any comparison: one callback, one chance.
    deleteCookie ctx settings

    match cookie with
    | None -> Error NoCorrelation
    | Some payload ->

    let correlation =
        try
            let protector = protectorProvider.CreateProtector settings.Purpose
            decode (protector.Unprotect payload)
        with _ ->
            // A tampered or undecryptable cookie, or a key ring that changed.
            // Indistinguishable from an attack, and treated as one.
            None

    match correlation with
    | None -> Error Corrupt
    | Some correlation ->

    // The provider's own refusal comes back with no code. Checked after the
    // cookie is gone so a cancelled attempt cannot be reused.
    match answer.Error with
    | Some error -> Error(ProviderReturnedError error)
    | None ->

    // An empty state is a missing one: it cannot equal a minted value.
    match answer.State |> Option.filter (fun s -> s.Length > 0) with
    | None -> Error StateMissing
    | Some returnedState ->

    // Constant time: `state` is attacker-influenced and retryable, and ordinary
    // string equality leaks the position of the first difference through timing.
    let now = clock.GetUtcNow()

    if not (Ids.constantTimeEquals correlation.State returnedState) then
        Error StateMismatch
    elif alreadyConsumed now correlation.State then
        // A second callback carrying a correlation that has already been
        // redeemed. The cookie deletion did not stop it, so the server does.
        Error Replayed
    elif correlation.Slug <> slug then
        // The response arrived on a different connector's callback route than
        // the one it was minted for. Distinct routes per provider exist so the
        // routing layer catches an IdP mix-up before our own lookup has to.
        Error(SlugMismatch(correlation.Slug, slug))
    elif now - correlation.IssuedAtUtc > Lifetime then
        // Checked even though the cookie carries Max-Age: a client controls its
        // own cookie jar, and the server's clock is the one that counts.
        Error Expired
    else
        rememberConsumed now correlation.State |> ignore
        Ok correlation

/// The authorization request this round trip belongs to, read from the
/// correlation cookie WITHOUT consuming it. `complete` rejects most failures
/// before it hands the correlation back, but a person who cancels at the
/// provider, or whose state does not match, should still land on the sign-in
/// page of the app they started from. Only a correlation that decrypts and has
/// not expired speaks for the request: with none, there is no round trip to
/// return to and the caller falls back to the bare sign-in page.
///
/// Reads only. It does not delete the cookie and does not mark anything
/// consumed, so it cannot weaken `complete`.
let peek
    (protectorProvider: IDataProtectionProvider)
    (clock: TimeProvider)
    (ctx: HttpContext)
    (mode: ResponseMode)
    : Correlation option =
    let settings = settingsFor ctx mode

    match readCookie ctx settings with
    | None -> None
    | Some payload ->
        try
            let protector = protectorProvider.CreateProtector settings.Purpose

            match decode (protector.Unprotect payload) with
            | Some correlation when clock.GetUtcNow() - correlation.IssuedAtUtc <= Lifetime ->
                Some correlation
            | _ -> None
        with _ ->
            None

/// The authorization request of `peek`'s correlation: where a failed callback
/// returns the person.
let returnUrlOf
    (protectorProvider: IDataProtectionProvider)
    (clock: TimeProvider)
    (ctx: HttpContext)
    (mode: ResponseMode)
    : string option =
    peek protectorProvider clock ctx mode |> Option.map _.ReturnUrl
