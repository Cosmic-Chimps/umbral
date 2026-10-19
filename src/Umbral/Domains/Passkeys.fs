/// The rules of the passkey connector that need no database and no library
/// (specs/008-passkey-connector, data-model.md, research R6 and R9).
///
/// Pure, like `Linking` and `EmailDomains`: the limits, the name rules, the
/// invitation secret and its hash, and the closed words that say why a
/// ceremony was refused. The cryptography of a ceremony is not here and is not
/// Umbral's at all: `Infrastructure/WebAuthn.fs` hands it to fido2-net-lib and
/// maps what comes back to `Reason`.
module Umbral.Domains.Passkeys

open System
open System.Security.Cryptography
open System.Text
open Umbral.Domains.Connectors

/// The enrolment mode as the form and the row spell it, `None` for anything
/// else so a hand-written value is refused rather than guessed at.
let parseEnrolment (value: string) : PasskeyEnrolment option =
    match value with
    | "invitations" -> Some InvitationsOnly
    | "open" -> Some OpenSignUp
    | _ -> None

/// How long an invitation link works: 7 × 24 hours from creation (FR-013).
let InvitationLifetime = TimeSpan.FromDays 7.

/// How long a started ceremony may be completed. The most the spec allows, and
/// the library's own default timeout.
let CeremonyLifetime = TimeSpan.FromMinutes 5.

/// The most passkeys one user holds (FR-018).
let MaxPerUser = 10

/// The longest display name or nickname, in characters.
let private MaxNameLength = 64

/// The name an app shows for a person who has no provider to take it from.
/// Trimmed first: a name of white space is no name. The `Error` says the limits
/// in words for a log or a test; what a person reads is `Texts`.
let validateDisplayName (value: string | null) : Result<string, string> =
    let name =
        match value with
        | null -> ""
        | text -> text.Trim()

    if name.Length >= 1 && name.Length <= MaxNameLength then
        Ok name
    else
        Error $"A display name is 1 to {MaxNameLength} characters, not counting white space around it."

/// A passkey's own name, to tell it from the person's others. Optional: `Ok None`
/// asks the caller for `defaultNickname`.
let validateNickname (value: string | null) : Result<string option, string> =
    let name =
        match value with
        | null -> ""
        | text -> text.Trim()

    if name.Length = 0 then Ok None
    elif name.Length <= MaxNameLength then Ok(Some name)
    else Error $"A nickname is at most {MaxNameLength} characters."

/// What a passkey is called when its owner gave no name: a fixed phrase with
/// the day it was added, which is what tells two of them apart later.
let defaultNickname (clock: TimeProvider) : string =
    let day = clock.GetUtcNow().ToString("yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture)
    $"Passkey added {day}"

/// The secret in an invitation link: 256 bits, base64url, which is 43
/// characters. High entropy is what lets the stored hash go unsalted (R9).
let newInvitationSecret () : string = Ids.randomToken 32

/// What Umbral stores of a secret: SHA-256 as lowercase hex. The link is
/// shown once and never recoverable from the database.
let hashInvitationSecret (secret: string) : string =
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes secret)).ToLowerInvariant()

/// The address the operator sends: the public origin (its text, no trailing
/// slash), `/invite/` and the secret.
let invitationLink (origin: string) (secret: string) : string = $"{origin}/invite/{secret}"

/// Where a refusal happened, the third field of the detail.
type Stage =
    | Registration
    | Assertion
    | Invitation

    member this.Value =
        match this with
        | Registration -> "registration"
        | Assertion -> "assertion"
        | Invitation -> "invitation"

/// Why a ceremony was refused: a closed set, so the security log stays
/// greppable and no value can reach it (research R6). The first twelve are the
/// verifier's (`WebAuthn.mapException`, from the library's error codes), the
/// rest are Umbral's own checks. The library's exception message is never kept:
/// it names the offending origin.
type Reason =
    | OriginMismatch
    | RpIdMismatch
    | ChallengeMismatch
    | UserVerificationMissing
    | UserPresenceMissing
    | InvalidSignature
    | SignCountRegressed
    | CredentialNotUnique
    | UserHandleMismatch
    | TypeMismatch
    | MalformedResponse
    /// Anything else the library refused.
    | VerificationFailed
    | CeremonyUnknownOrExpired
    | UnknownCredential
    | UserNotFound
    | UserDisabled
    | ConnectorDisabled
    | PasskeyNotDiscoverable
    | PasskeyLimitReached
    | DisplayNameInvalid
    | NicknameInvalid
    | EnrolmentClosed
    | InvitationUnknown
    | InvitationUsed
    | InvitationExpired
    | InvitationRevoked
    | InvitationUserGone
    | JavascriptRequired

    /// The stored word. Explicit, like `SecurityEvents.EventType.Value`: rows
    /// an operator queries by hand must not be orphaned by renaming a case.
    member this.Word =
        match this with
        | OriginMismatch -> "origin_mismatch"
        | RpIdMismatch -> "rpid_mismatch"
        | ChallengeMismatch -> "challenge_mismatch"
        | UserVerificationMissing -> "user_verification_missing"
        | UserPresenceMissing -> "user_presence_missing"
        | InvalidSignature -> "invalid_signature"
        | SignCountRegressed -> "sign_count_regressed"
        | CredentialNotUnique -> "credential_not_unique"
        | UserHandleMismatch -> "user_handle_mismatch"
        | TypeMismatch -> "type_mismatch"
        | MalformedResponse -> "malformed_response"
        | VerificationFailed -> "verification_failed"
        | CeremonyUnknownOrExpired -> "ceremony_unknown_or_expired"
        | UnknownCredential -> "unknown_credential"
        | UserNotFound -> "user_not_found"
        | UserDisabled -> "user_disabled"
        | ConnectorDisabled -> "connector_disabled"
        | PasskeyNotDiscoverable -> "passkey_not_discoverable"
        | PasskeyLimitReached -> "passkey_limit_reached"
        | DisplayNameInvalid -> "display_name_invalid"
        | NicknameInvalid -> "nickname_invalid"
        | EnrolmentClosed -> "enrolment_closed"
        | InvitationUnknown -> "invitation_unknown"
        | InvitationUsed -> "invitation_used"
        | InvitationExpired -> "invitation_expired"
        | InvitationRevoked -> "invitation_revoked"
        | InvitationUserGone -> "invitation_user_gone"
        | JavascriptRequired -> "javascript_required"

/// The `Detail` of a `sign_in_failed` event for a refused ceremony:
/// `passkey_refused;reason=…;stage=…`. Closed words only: no challenge,
/// credential id, key, invitation secret or library message can get in.
let refusalDetail (reason: Reason) (stage: Stage) : string =
    $"passkey_refused;reason={reason.Word};stage={stage.Value}"
