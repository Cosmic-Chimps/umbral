/// Where a secret lives, and how it gets there and back.
///
/// Umbral holds three kinds of secret and treats them differently, because the
/// question that matters is whether Umbral needs the plaintext back:
///
///   upstream connector secret  reversible — Umbral must send it to Discord
///   app (OIDC client) secret   hashed by OpenIddict, never recoverable
///   operator credential        hashed, only ever compared
///
/// Only the first kind passes through this file.
///
/// The `ISecretStore` indirection is here on day one rather than later. The
/// default — encrypt with Data Protection, keep the ciphertext in SQLite — is
/// right for the audience: one container, `cp` to back up, nothing to reach
/// over the network during a login. But an operator with Vault, Key Vault or
/// BellaBaxter will want Umbral to put the secret there and keep only a
/// reference, and Keycloak is the cautionary tale: it shipped
/// readable-in-database and bolted a vault indirection on years later, which is
/// why that feature is awkward to this day. Adding the seam now costs a few
/// lines; adding it after deployments exist costs a migration for each of them.
///
/// The external stores themselves (AWS, GCP, Azure, BellaBaxter) live under
/// Infrastructure/SecretStores/, and `SecretStores.Registry` decides which of them
/// a given reference is read from. See specs/005-external-secret-stores/ for why
/// the seam below reports failures instead of throwing them.
module Umbral.Infrastructure.Secrets

open System
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.DataProtection

/// Where one secret actually lives. `Store` names the backend that created it;
/// `Locator` means whatever that backend needs to find it again — for the local
/// store the ciphertext itself, for an external one an ARN, a URI or an id.
///
/// Nothing outside the owning store may interpret `Locator`. That rule is what
/// lets a second backend be added without touching the schema.
type SecretRef =
    { Store: string
      Locator: string }

    /// The single column form, so the two halves cannot drift apart in a row.
    member this.Encoded = $"{this.Store}:{this.Locator}"

    static member Decode(encoded: string) =
        match encoded.IndexOf ':' with
        | -1 -> Error $"malformed secret reference (no store prefix): {encoded.Length} characters"
        | i ->
            Ok
                { Store = encoded.Substring(0, i)
                  Locator = encoded.Substring(i + 1) }

/// Why a store could not do what it was asked. Only `Unreachable` means "try
/// again later is reasonable", which is the one case where the cache may keep
/// serving a value it already holds; every other case says the answer will not
/// change by waiting, so a stale value must not hide it.
///
/// `Rejected` carries a reason, and the reason is the place a vendor message
/// would be tempting to paste. It is not: an adapter puts in what the operator
/// can act on (an exception type name, a state such as "soft-deleted"), never a
/// vendor message that might echo a name, a locator or a value.
type StoreFailure =
    | Unreachable
    | AccessDenied
    | NotFound
    | NotConfigured
    | Rejected of reason: string

    /// The word recorded in a security event's `Detail` (`cause=...`). Closed and
    /// fixed on purpose: the reason of `Rejected` stays out of the event.
    member this.Cause =
        match this with
        | Unreachable -> "unreachable"
        | AccessDenied -> "access_denied"
        | NotFound -> "not_found"
        | NotConfigured -> "not_configured"
        | Rejected _ -> "rejected"

/// The logical name of a connector's secret, before any store renders it.
///
/// It is derived, not random, so that a retried write (or a move that is run
/// twice) hits the same name and adds a version instead of leaving a second
/// copy behind. The discriminator is what lets a connector that is deleted and
/// created again under the same slug get a different name: AWS keeps a deleted
/// name through its recovery window and Azure through soft-delete, and writing
/// to a name in that state fails (specs/005 research R5).
type SecretName =
    { Prefix: string
      Slug: string
      Discriminator: string }

    /// `{prefix}-{slug}-{disc}`, for the stores whose names are `[A-Za-z0-9-]`
    /// (AWS, GCP, Azure). At most 32 + 1 + 32 + 1 + 8 = 74 characters.
    member this.Cloud = $"{this.Prefix}-{this.Slug}-{this.Discriminator}"

    /// `UMBRAL_{PREFIX}_{SLUG}_{DISC}`, for BellaBaxter keys, which must start
    /// with a letter and use `[A-Za-z0-9_]`. Slugs never contain `_`, so replacing
    /// `-` keeps the mapping one-to-one. The `UMBRAL_` lead keeps these keys
    /// recognisable in an environment other apps may also load.
    member this.Bella =
        $"UMBRAL_{this.Prefix}_{this.Slug}_{this.Discriminator}".ToUpperInvariant().Replace('-', '_')

[<RequireQualifiedAccess>]
module SecretName =

    let private base32Alphabet = "abcdefghijklmnopqrstuvwxyz234567"

    /// The first 8 characters of the RFC 4648 base32 of the first five bytes: 5
    /// bytes are exactly 40 bits, which is exactly 8 characters, so there is no
    /// padding and nothing to trim. Eight characters also keep the name clear of
    /// AWS's reading of "hyphen plus six characters" as an ARN suffix.
    let private discriminator (bytes: byte[]) =
        let bits = Array.fold (fun (acc: uint64) b -> (acc <<< 8) ||| uint64 b) 0UL bytes[0..4]

        String(Array.init 8 (fun i -> base32Alphabet[int ((bits >>> (35 - 5 * i)) &&& 31UL)]))

    /// The name for a connector, from what never changes about it: its slug and
    /// the instant it was created. `createdUtc` is written as the round-trip
    /// text of its UTC value, which is exactly what `connectors.created_utc`
    /// holds, so a name derived at create time and one derived later from the
    /// row are the same.
    let derive (prefix: string) (slug: string) (createdUtc: DateTimeOffset) : SecretName =
        let text = createdUtc.ToUniversalTime().ToString "o"
        let hash = SHA256.HashData(Encoding.UTF8.GetBytes $"{slug}|{text}")

        { Prefix = prefix
          Slug = slug
          Discriminator = discriminator hash }

/// A store for connector secrets Umbral must be able to read back.
///
/// Nothing here throws for a failure of the store itself: each member reports
/// it as a `StoreFailure`, because the callers must tell an outage from a
/// missing secret from a revoked grant (the cache, the notices and the security
/// events all depend on it). Only a programming error, such as a reference with
/// another store's id, may throw.
///
/// Every member honours its token. The caller links it to a 10 second bound so
/// that a hung manager cannot hold a sign-in open.
type ISecretStore =
    abstract Id: string
    /// Writes `plaintext` under `name`. A name that already exists gets a new
    /// version and the same reference back, so repeating a call is safe.
    abstract Put: name: SecretName * plaintext: string * cancellationToken: CancellationToken -> Task<Result<SecretRef, StoreFailure>>
    abstract Get: reference: SecretRef * cancellationToken: CancellationToken -> Task<Result<string, StoreFailure>>
    /// Something already gone is `Ok`, so a retried delete never fails.
    abstract Delete: reference: SecretRef * cancellationToken: CancellationToken -> Task<Result<unit, StoreFailure>>
    /// Whether the store is reachable and Umbral is allowed to use it.
    abstract Probe: cancellationToken: CancellationToken -> Task<Result<unit, StoreFailure>>

[<Literal>]
let LocalStoreId = "local"

[<Literal>]
let private ProtectorPurpose = "Umbral.ConnectorSecrets.v1"

/// The default: encrypted with ASP.NET Core Data Protection, ciphertext kept in
/// the SQLite row.
///
/// The key ring must be persisted to the data directory alongside the database
/// (see Keys.fs). If it is lost, every secret here becomes undecryptable and the
/// operator must re-enter them — which is why the README has to say that backing
/// up `/data` is not optional.
type LocalSecretStore(provider: IDataProtectionProvider) =
    let protector = provider.CreateProtector ProtectorPurpose

    interface ISecretStore with
        member _.Id = LocalStoreId

        member _.Put(_name, plaintext, _cancellationToken) =
            // The ciphertext IS the locator: there is nowhere else to put it,
            // and that keeps the local store free of a second lookup table.
            // The name is for a manager's console; there is none here.
            Task.FromResult(
                Ok
                    { Store = LocalStoreId
                      Locator = protector.Protect plaintext }
            )

        member _.Get(reference, _cancellationToken) =
            if reference.Store <> LocalStoreId then
                // Not this store's to read: the registry routes a reference to
                // the store that wrote it, so reaching here is a caller's
                // mistake, and an end user mid-login still gets a message
                // rather than a stack trace.
                Task.FromResult(Error NotConfigured)
            else
                try
                    Task.FromResult(Ok(protector.Unprotect reference.Locator))
                with :? Xml.XmlException | :? FormatException | :? CryptographicException ->
                    // Almost always the key ring: a fresh container with a lost
                    // or unmounted /data/keys. Reporting NotFound lets the
                    // caller say something useful instead of throwing a stack
                    // trace at an end user mid-login.
                    Task.FromResult(Error NotFound)

        member _.Delete(_reference, _cancellationToken) =
            // Nothing to do: the ciphertext goes when its row goes. An external
            // store would issue a real delete here.
            Task.FromResult(Ok())

        member _.Probe _cancellationToken =
            // Nothing to reach: the key ring is a local file Data Protection
            // already needed to start.
            Task.FromResult(Ok())

/// The last four characters of a secret, for the masked display. Never more:
/// the point of the Connectors screen is that a saved secret cannot be read
/// back off it, only replaced.
let hint (plaintext: string) =
    if String.IsNullOrEmpty plaintext then ""
    elif plaintext.Length <= 4 then String('•', plaintext.Length)
    else plaintext.Substring(plaintext.Length - 4)

/// What the admin UI shows in place of a saved secret.
let masked (hintText: string) =
    if String.IsNullOrEmpty hintText then "••••••••" else $"••••••••{hintText}"

/// A secret that cannot be printed by accident.
///
/// `ToString` is the hole every redaction effort falls through: structured
/// logging, string interpolation and exception messages all reach for it. This
/// type has no other way to get at the value than `Reveal`, which is greppable.
[<Struct>]
type Protected =
    private
        { Inner: string }

    override _.ToString() = "[redacted]"
    member this.Reveal = this.Inner
    static member Of(value: string) = { Inner = value }
