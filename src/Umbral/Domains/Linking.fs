/// FR-012: deciding whether a provider account belongs to someone we already
/// know.
///
/// Pure, and separated from the database on purpose. This is the one rule in
/// Umbral where a bug hands one person another person's account, so it is
/// written as a total function over values and tested branch by branch rather
/// than discovered inside a query.
module Umbral.Domains.Linking

/// FR-008 (specs/018). The opaque name of one provider account, for a form and
/// for a claim: the unpadded base64url SHA-256 of the provider kind, a line feed
/// and the provider's subject (43 characters). Pure and never parsed back: a
/// holder finds the account by computing the fingerprint of each link a user
/// has. The page and the session never carry the subject itself.
let fingerprint (kind: string) (subject: string) : string =
    let bytes = System.Text.Encoding.UTF8.GetBytes(kind + "\n" + subject)
    let hash = System.Security.Cryptography.SHA256.HashData bytes

    System.Convert
        .ToBase64String(hash)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_')

/// FR-007 (specs/018). What a session and an authorization carry: the provider
/// account it was made with, or the passkey.
type CredentialReference =
    | LinkCredential of fingerprint: string
    | PasskeyCredential of passkeyId: string

/// `link:<fingerprint>` or `passkey:<id>`.
let credentialValue (credential: CredentialReference) : string =
    match credential with
    | LinkCredential fp -> "link:" + fp
    | PasskeyCredential id -> "passkey:" + id

/// The reverse of `credentialValue`; None for anything else, an empty value too.
let parseCredential (value: string) : CredentialReference option =
    if value.StartsWith("link:", System.StringComparison.Ordinal) && value.Length > 5 then
        Some(LinkCredential(value.Substring 5))
    elif value.StartsWith("passkey:", System.StringComparison.Ordinal) && value.Length > 8 then
        Some(PasskeyCredential(value.Substring 8))
    else None

/// What a provider told us about whoever just signed in.
type IncomingIdentity =
    { ProviderKind: string
      /// The provider's immutable identifier. Never a username or an email.
      ProviderSubject: string
      Email: string option
      /// Strictly what the provider asserted. Umbral never infers this.
      EmailVerified: bool }

/// An existing user, reduced to the fields the rule may look at. Nothing else
/// is in scope — a display name or an avatar must not influence identity.
type Candidate = { UserId: string; EmailVerified: bool }

type Decision =
    /// This exact provider account is already linked. The common path.
    | ExistingLink of userId: string
    /// A verified email matched exactly one verified existing user.
    | LinkToExisting of userId: string
    /// Everything else.
    | CreateNew

type Reason =
    | AlreadyLinked
    | NoEmailAsserted
    | EmailNotVerified
    | NoVerifiedMatch
    | AmbiguousMatch of count: int
    | SingleVerifiedMatch

type Outcome = { Decision: Decision; Reason: Reason }

/// Decide.
///
/// `existingLink` is the user already bound to (kind, subject), if any.
/// `verifiedEmailMatches` is every user whose **verified** email equals the
/// incoming one — the caller must not include unverified rows, and the partial
/// index on `users(email) WHERE email_verified = 1` is what makes the safe
/// query the natural one to write.
let decide
    (existingLink: string option)
    (incoming: IncomingIdentity)
    (verifiedEmailMatches: Candidate list)
    : Outcome =

    match existingLink with
    | Some userId ->
        // Keyed on the provider's immutable id, so a GitHub user who renames
        // themselves is still the same person, and someone who claims their
        // abandoned handle is not.
        { Decision = ExistingLink userId
          Reason = AlreadyLinked }

    | None ->

    // Blank counts as absent. Providers are inconsistent about whether a
    // missing address is null or "", and treating "" as a value would let
    // every user who has one match every other.
    let asserted =
        incoming.Email
        |> Option.map (fun e -> e.Trim())
        |> Option.filter (fun e -> e.Length > 0)

    match asserted with
    | None ->
        // GitHub users can keep an email private and Discord accounts can be
        // unverified. No email is normal, not an error.
        { Decision = CreateNew; Reason = NoEmailAsserted }

    | Some _ when not incoming.EmailVerified ->
        // THE rule that must never bend. A provider that lets someone claim
        // victim@example.com without proving they can read that inbox would
        // otherwise hand over the victim's account. All three providers require
        // inbox control before setting their verified flag; anything that does
        // not assert it gets a brand new user.
        { Decision = CreateNew; Reason = EmailNotVerified }

    | Some _ ->
        match verifiedEmailMatches |> List.filter (fun c -> c.EmailVerified) with
        | [] -> { Decision = CreateNew; Reason = NoVerifiedMatch }

        | [ single ] ->
            { Decision = LinkToExisting single.UserId
              Reason = SingleVerifiedMatch }

        | several ->
            // Two users can legitimately share a verified email: both were
            // created while some provider withheld the address, and a later
            // change verified both. Guessing between them is how one person
            // gets another's account, so Umbral creates a third user and lets
            // the operator delete the strays.
            { Decision = CreateNew
              Reason = AmbiguousMatch several.Length }
