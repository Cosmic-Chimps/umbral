module Umbral.Tests.KeyPersistenceTests

open System
open System.IO
open System.Security.Cryptography.X509Certificates
open Xunit
open Umbral.Infrastructure.Keys

let private tempDir () =
    let path = Path.Combine(Path.GetTempPath(), "umbral-keys-" + Guid.NewGuid().ToString "N")
    { new Object() with
        member _.ToString() = path },
    path

[<Fact>]
let ``a second load returns the same certificate`` () =
    let _, dir = tempDir ()

    try
        use first = loadOrCreate dir Signing
        use second = loadOrCreate dir Signing

        // The whole point of this file: a restart must not invalidate every
        // token Umbral has issued. Same thumbprint means the same key.
        Assert.Equal(first.Thumbprint, second.Thumbprint)
    finally
        if Directory.Exists dir then Directory.Delete(dir, true)

[<Fact>]
let ``signing and encryption certificates are distinct`` () =
    let _, dir = tempDir ()

    try
        let signing, encryption = loadOrCreateAll dir
        use signing = signing
        use encryption = encryption
        Assert.NotEqual<string>(signing.Thumbprint, encryption.Thumbprint)
    finally
        if Directory.Exists dir then Directory.Delete(dir, true)

[<Fact>]
let ``both certificates land in the data directory`` () =
    let _, dir = tempDir ()

    try
        let signing, encryption = loadOrCreateAll dir
        (signing :> IDisposable).Dispose()
        (encryption :> IDisposable).Dispose()

        // Backup is `cp` of one directory, per Principle II. If a key escapes
        // this directory, that promise is broken.
        Assert.True(File.Exists(Path.Combine(dir, "signing.pfx")))
        Assert.True(File.Exists(Path.Combine(dir, "encryption.pfx")))
    finally
        if Directory.Exists dir then Directory.Delete(dir, true)

[<Fact>]
let ``the certificate carries a usable private key`` () =
    let _, dir = tempDir ()

    try
        use certificate = loadOrCreate dir Signing
        Assert.True(certificate.HasPrivateKey)

        // GetRSAPrivateKey is an extension method; F# does not pick it up from
        // the type, so call it on the static class that defines it.
        match RSACertificateExtensions.GetRSAPrivateKey certificate with
        | null -> failwith "the signing certificate has no RSA private key"
        | rsa ->
            use rsa = rsa
            Assert.Equal(4096, rsa.KeySize)
    finally
        if Directory.Exists dir then Directory.Delete(dir, true)

[<Fact>]
let ``the private key is reloadable after a simulated restart`` () =
    let _, dir = tempDir ()

    try
        let thumbprint =
            use certificate = loadOrCreate dir Signing
            certificate.Thumbprint

        // Nothing in memory survives; only the file does. This is the container
        // replacement case that AddDevelopmentSigningCertificate fails.
        use reloaded = loadOrCreate dir Signing
        Assert.Equal(thumbprint, reloaded.Thumbprint)
        Assert.True(reloaded.HasPrivateKey)
    finally
        if Directory.Exists dir then Directory.Delete(dir, true)

[<Fact>]
let ``the certificate is valid now and for years`` () =
    let _, dir = tempDir ()

    try
        use certificate = loadOrCreate dir Signing
        let now = DateTimeOffset.UtcNow
        Assert.True(certificate.NotBefore.ToUniversalTime() <= now.UtcDateTime)
        // An appliance on a shelf must not expire quietly.
        Assert.True(certificate.NotAfter.ToUniversalTime() > now.AddYears(4).UtcDateTime)
    finally
        if Directory.Exists dir then Directory.Delete(dir, true)

[<Fact>]
let ``the private key file is not world readable`` () =
    if OperatingSystem.IsWindows() then () else

    let _, dir = tempDir ()

    try
        use _ = loadOrCreate dir Signing
        let mode = File.GetUnixFileMode(Path.Combine(dir, "signing.pfx"))
        Assert.Equal(enum<UnixFileMode> 0, mode &&& UnixFileMode.GroupRead)
        Assert.Equal(enum<UnixFileMode> 0, mode &&& UnixFileMode.OtherRead)
    finally
        if Directory.Exists dir then Directory.Delete(dir, true)
