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
open System.Text.Json
open Microsoft.AspNetCore.DataProtection
open Microsoft.AspNetCore.Http
open Umbral.Domains

[<Literal>]
let private ProtectorPurpose = "Umbral.Connect.v1"

[<Literal>]
let private CookieName = "umbral_connect"

[<Literal>]
let private CookiePath = "/connect"

/// Ten minutes. Matches GitHub's authorization-code lifetime, so a correlation
/// never outlives the code it is protecting.
let private Lifetime = TimeSpan.FromMinutes 10.0

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
      IssuedAtUtc: DateTimeOffset }

type RoundTripError =
    | NoCorrelation
    | StateMissing
    | StateMismatch
    | Expired
    | SlugMismatch of expected: string * actual: string
    | ProviderReturnedError of error: string
    | Corrupt

    member this.Detail =
        match this with
        | NoCorrelation -> "no_correlation_cookie"
        | StateMissing -> "state_missing"
        | StateMismatch -> "state_mismatch"
        | Expired -> "correlation_expired"
        | SlugMismatch _ -> "slug_mismatch"
        | ProviderReturnedError error -> $"provider_error:{error}"
        | Corrupt -> "correlation_corrupt"

// ---------------------------------------------------------------------------
// Serialisation
// ---------------------------------------------------------------------------

[<CLIMutable>]
type private Stored =
    { s: string
      v: string
      n: string
      g: string
      r: string
      t: string }

let private toStored (c: Correlation) =
    { s = c.State
      v = c.CodeVerifier
      n = c.Nonce
      g = c.Slug
      r = c.ReturnUrl
      t = c.IssuedAtUtc.ToString "o" }

let private fromStored (s: Stored) =
    { State = s.s
      CodeVerifier = s.v
      Nonce = s.n
      Slug = s.g
      ReturnUrl = s.r
      IssuedAtUtc = DateTimeOffset.Parse(s.t, Globalization.CultureInfo.InvariantCulture) }

// ---------------------------------------------------------------------------
// Begin
// ---------------------------------------------------------------------------

type Started =
    { Correlation: Correlation
      CodeChallenge: string }

/// Mint the correlation and set the cookie. Returns what the adapter needs to
/// build its authorize URL.
let start
    (protectorProvider: IDataProtectionProvider)
    (clock: TimeProvider)
    (ctx: HttpContext)
    (slug: string)
    (returnUrl: string)
    : Started =
    let verifier = Ids.newCodeVerifier ()

    let correlation =
        { State = Ids.newStateValue ()
          CodeVerifier = verifier
          Nonce = Ids.newNonce ()
          Slug = slug
          ReturnUrl = returnUrl
          IssuedAtUtc = clock.GetUtcNow() }

    let protector = protectorProvider.CreateProtector ProtectorPurpose
    let payload = protector.Protect(JsonSerializer.Serialize(toStored correlation))

    let options = CookieOptions()
    options.HttpOnly <- true
    options.IsEssential <- true
    options.Path <- CookiePath
    // Lax, not Strict: the browser returns here on the provider's top-level
    // redirect, and a Strict cookie is withheld on exactly that navigation, so
    // every login would fail. Not None either — some browsers drop it over
    // plain http, which breaks local development.
    options.SameSite <- SameSiteMode.Lax
    options.Secure <- ctx.Request.IsHttps
    options.MaxAge <- Lifetime

    ctx.Response.Cookies.Append(CookieName, payload, options)

    { Correlation = correlation
      CodeChallenge = Ids.codeChallengeS256 verifier }

// ---------------------------------------------------------------------------
// Complete
// ---------------------------------------------------------------------------

let private deleteCookie (ctx: HttpContext) =
    let options = CookieOptions()
    options.Path <- CookiePath
    options.Secure <- ctx.Request.IsHttps
    options.SameSite <- SameSiteMode.Lax
    ctx.Response.Cookies.Delete(CookieName, options)

/// Validate the callback and surrender the correlation.
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
    (slug: string)
    : Result<Correlation, RoundTripError> =

    let cookie =
        match ctx.Request.Cookies.TryGetValue CookieName with
        // A `when` guard does not narrow nullness for the compiler, so the
        // null case is matched rather than tested.
        | true, value ->
            match Option.ofObj value with
            | Some v when v.Length > 0 -> Some v
            | _ -> None
        | _ -> None

    // Unconditionally, and before any comparison: one callback, one chance.
    deleteCookie ctx

    match cookie with
    | None -> Error NoCorrelation
    | Some payload ->

    let correlation =
        try
            let protector = protectorProvider.CreateProtector ProtectorPurpose
            let json = protector.Unprotect payload

            // Deserialize is typed nullable: malformed JSON yields null rather
            // than throwing for some shapes, and a null correlation must be
            // treated as tampering, not dereferenced.
            JsonSerializer.Deserialize<Stored> json
            |> Option.ofObj
            |> Option.map fromStored
        with _ ->
            // A tampered or undecryptable cookie, or a key ring that changed.
            // Indistinguishable from an attack, and treated as one.
            None

    match correlation with
    | None -> Error Corrupt
    | Some correlation ->

    // The provider's own refusal comes back as a query parameter with no code.
    // Checked after the cookie is gone so a cancelled attempt cannot be reused.
    let providerError =
        match ctx.Request.Query.TryGetValue "error" with
        | true, value when value.Count > 0 -> Option.ofObj (value[0])
        | _ -> None

    match providerError with
    | Some error -> Error(ProviderReturnedError error)
    | None ->

    let returnedState =
        match ctx.Request.Query.TryGetValue "state" with
        | true, value when value.Count > 0 -> Option.ofObj (value[0])
        | _ -> None

    match returnedState with
    | None -> Error StateMissing
    | Some returnedState ->

    // Constant time: `state` is attacker-influenced and retryable, and ordinary
    // string equality leaks the position of the first difference through timing.
    if not (Ids.constantTimeEquals correlation.State returnedState) then
        Error StateMismatch
    elif correlation.Slug <> slug then
        // The response arrived on a different connector's callback route than
        // the one it was minted for. Distinct routes per provider exist so the
        // routing layer catches an IdP mix-up before our own lookup has to.
        Error(SlugMismatch(correlation.Slug, slug))
    elif clock.GetUtcNow() - correlation.IssuedAtUtc > Lifetime then
        // Checked even though the cookie carries Max-Age: a client controls its
        // own cookie jar, and the server's clock is the one that counts.
        Error Expired
    else
        Ok correlation
