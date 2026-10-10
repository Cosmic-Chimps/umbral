/// Token signing and encryption key material.
///
/// Why this file exists instead of `AddDevelopmentSigningCertificate()`:
/// that helper stores keys in the host user's X.509 store, which inside a
/// container lives in the writable layer. `docker compose pull && up -d` on a
/// Raspberry Pi replaces the container, the keys vanish, and every token Umbral
/// ever issued becomes undecryptable — everyone logged out of every app, every
/// refresh token failing with `invalid_grant`. That is a guaranteed outage on
/// the first update, which is why SC-005 treats key persistence as correctness
/// rather than configuration.
///
/// So: generate once into the mounted data directory, load thereafter.
module Umbral.Infrastructure.Keys

open System
open System.IO
open System.Security.Cryptography
open System.Security.Cryptography.X509Certificates

/// Five years, not two. This is an appliance that may sit on a shelf; a
/// two-year certificate is a silent outage on a machine nobody logs into.
[<Literal>]
let private ValidityYears = 5

[<Literal>]
let private KeySizeBits = 4096

type KeyPurpose =
    | Signing
    | Encryption

    member this.FileName =
        match this with
        | Signing -> "signing.pfx"
        | Encryption -> "encryption.pfx"

    member this.Subject =
        match this with
        | Signing -> "CN=Umbral Signing Certificate"
        | Encryption -> "CN=Umbral Encryption Certificate"

    member this.Usage =
        match this with
        | Signing -> X509KeyUsageFlags.DigitalSignature
        | Encryption -> X509KeyUsageFlags.KeyEncipherment

let private generate (purpose: KeyPurpose) =
    use algorithm = RSA.Create(keySizeInBits = KeySizeBits)

    let request =
        CertificateRequest(
            X500DistinguishedName(purpose.Subject),
            algorithm,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        )

    request.CertificateExtensions.Add(X509KeyUsageExtension(purpose.Usage, critical = true))

    let now = DateTimeOffset.UtcNow
    request.CreateSelfSigned(now.AddMinutes(-5.0), now.AddYears ValidityYears)

/// Load the certificate for `purpose` from `directory`, generating and storing
/// it on first run. Idempotent: a second call returns the same certificate, and
/// that is what makes a restart survivable.
let loadOrCreate (directory: string) (purpose: KeyPurpose) : X509Certificate2 =
    Directory.CreateDirectory directory |> ignore
    let path = Path.Combine(directory, purpose.FileName)

    if File.Exists path then
        // EphemeralKeySet keeps the private key out of any per-user key store on
        // load: in a container there is nowhere durable for it to go, and the
        // PFX on disk is already the durable copy.
        //
        // macOS does not support the flag at all — it throws
        // PlatformNotSupportedException and tells you to remove it. Linux, where
        // Umbral actually ships, does support it. So prefer it and fall back,
        // rather than giving up the protection everywhere to suit one dev
        // platform.
        try
            X509CertificateLoader.LoadPkcs12FromFile(
                path,
                password = null,
                keyStorageFlags = X509KeyStorageFlags.EphemeralKeySet
            )
        with :? PlatformNotSupportedException ->
            X509CertificateLoader.LoadPkcs12FromFile(
                path,
                password = null,
                keyStorageFlags = X509KeyStorageFlags.DefaultKeySet
            )
    else
        let certificate = generate purpose
        let bytes = certificate.Export(X509ContentType.Pfx, password = (null: string | null))

        // Write to a temp file and move it into place, so a crash midway cannot
        // leave a truncated PFX that fails to load on the next start.
        let temp = path + ".tmp"
        File.WriteAllBytes(temp, bytes)

        if not (OperatingSystem.IsWindows()) then
            // It holds an unencrypted private key. Nobody but the owner.
            File.SetUnixFileMode(temp, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)

        File.Move(temp, path, overwrite = true)
        certificate

/// Both certificates, in one call. Distinct from each other and from whatever
/// certificate terminates TLS — reusing the TLS certificate here means rotating
/// it invalidates every live token.
let loadOrCreateAll (directory: string) =
    loadOrCreate directory Signing, loadOrCreate directory Encryption
