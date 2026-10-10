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
/// See .alta/tasks/ for the deferred spec covering the external stores.
module Umbral.Infrastructure.Secrets

open System
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

/// A secret Umbral must be able to read back. The logical `name` is stable and
/// descriptive (`connector/discord/client_secret`) so that an external store
/// holds something a human can recognise in its own console, rather than a
/// surrogate key only Umbral understands.
type ISecretStore =
    abstract Id: string
    abstract Put: name: string * plaintext: string -> Task<SecretRef>
    abstract Get: reference: SecretRef -> Task<string option>
    abstract Delete: reference: SecretRef -> Task<unit>

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

        member _.Put(_name, plaintext) =
            // The ciphertext IS the locator: there is nowhere else to put it,
            // and that keeps the local store free of a second lookup table.
            Task.FromResult
                { Store = LocalStoreId
                  Locator = protector.Protect plaintext }

        member _.Get reference =
            if reference.Store <> LocalStoreId then
                Task.FromResult None
            else
                try
                    Task.FromResult(Some(protector.Unprotect reference.Locator))
                with :? Xml.XmlException | :? FormatException | :? Security.Cryptography.CryptographicException ->
                    // Almost always the key ring: a fresh container with a lost
                    // or unmounted /data/keys. Returning None lets the caller
                    // say something useful instead of throwing a stack trace at
                    // an end user mid-login.
                    Task.FromResult None

        member _.Delete _reference =
            // Nothing to do: the ciphertext goes when its row goes. An external
            // store would issue a real delete here.
            Task.FromResult()

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
