/// Identifiers Umbral mints for itself. Pure: no I/O, no configuration.
module Umbral.Domains.Ids

open System
open System.Security.Cryptography
open System.Text.RegularExpressions

/// Base64url without padding, per RFC 4648 §5. Used for every identifier and
/// secret we generate, because these end up in URLs and in `sub` claims.
let private toBase64Url (bytes: byte array) =
    Convert
        .ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_')

let private random (byteCount: int) =
    toBase64Url (RandomNumberGenerator.GetBytes byteCount)

/// A user's stable subject identifier — the `sub` claim.
///
/// 128 bits from a cryptographic RNG, and deliberately NOT derived from any
/// upstream provider's identifier (FR-004). Deriving it would leak the
/// provider's id to every integrating app and would break the moment a user
/// links a second provider.
let newSubject () = random 16

/// A client identifier for an App, when the operator does not supply one.
let newClientId () = random 12

/// A client secret for an App. 256 bits: it is shown once and then only ever
/// compared against a PBKDF2 hash, so there is no reason to be stingy.
let newClientSecret () = random 32

/// The `state` value and the PKCE verifier for an upstream round trip.
/// RFC 7636 requires the verifier to be 43-128 characters; 32 bytes of
/// base64url is 43.
let newStateValue () = random 32
let newCodeVerifier () = random 32
let newNonce () = random 32

/// The PKCE challenge for a verifier, S256 only. `plain` is never offered:
/// GitHub rejects it outright and there is no reason to allow it anywhere.
let codeChallengeS256 (verifier: string) =
    verifier
    |> Text.Encoding.ASCII.GetBytes
    |> SHA256.HashData
    |> toBase64Url

let private slugPattern = Regex(@"^[a-z0-9-]{1,32}$", RegexOptions.Compiled)

/// A connector slug. It appears in `/connect/{slug}/callback`, which the
/// operator has registered in the provider's console, so it is immutable in
/// practice — the display name is the mutable one. Conflating them means
/// renaming a connector silently breaks every login through it.
let isValidSlug (candidate: string) =
    not (String.IsNullOrEmpty candidate) && slugPattern.IsMatch candidate

/// Constant-time comparison for values an attacker can influence and retry —
/// the `state` parameter above all. `=` on strings short-circuits on the first
/// differing byte and leaks position through timing.
///
/// Both sides are nullable because one of them always arrives from a query
/// string or a cookie, where absent and empty are different things.
let constantTimeEquals (a: string | null) (b: string | null) =
    match a, b with
    | null, _
    | _, null -> false
    | a, b ->
        CryptographicOperations.FixedTimeEquals(
            ReadOnlySpan<byte>(Text.Encoding.UTF8.GetBytes a),
            ReadOnlySpan<byte>(Text.Encoding.UTF8.GetBytes b)
        )
