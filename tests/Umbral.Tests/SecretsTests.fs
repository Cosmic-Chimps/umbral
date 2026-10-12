module Umbral.Tests.SecretsTests

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.DataProtection
open Xunit
open Umbral.Infrastructure.Secrets

let private store () : ISecretStore =
    LocalSecretStore(DataProtectionProvider.Create "Umbral.Tests") :> ISecretStore

let private name = SecretName.derive "umbral" "discord" (DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero))

let private none = CancellationToken.None

/// The value inside an `Ok`, failing the test with the failure's cause otherwise.
let private ok (result: Result<'a, StoreFailure>) =
    match result with
    | Ok value -> value
    | Error failure -> failwith $"expected Ok, got {failure.Cause}"

[<Fact>]
let ``a stored secret comes back`` () =
    task {
        let s = store ()
        let! put = s.Put(name, "s3cr3t-value", none)
        let! recovered = s.Get(ok put, none)
        Assert.Equal(Ok "s3cr3t-value", recovered)
    }

[<Fact>]
let ``the locator is not the plaintext`` () =
    task {
        let s = store ()
        let! put = s.Put(name, "s3cr3t-value", none)
        let reference = ok put
        // Whatever ends up in the row, reading the row must not reveal the
        // secret. This is the whole point of the column.
        Assert.DoesNotContain("s3cr3t-value", reference.Locator)
        Assert.Equal("local", reference.Store)
    }

[<Fact>]
let ``a reference round-trips through its encoded form`` () =
    task {
        let s = store ()
        let! put = s.Put(SecretName.derive "umbral" "github" DateTimeOffset.UnixEpoch, "another-secret", none)
        let reference = ok put

        // The row holds one column, so encode/decode must be lossless even
        // though the locator itself contains no shortage of punctuation.
        match SecretRef.Decode reference.Encoded with
        | Error e -> failwith e
        | Ok decoded ->
            Assert.Equal(reference.Store, decoded.Store)
            Assert.Equal(reference.Locator, decoded.Locator)
            let! recovered = s.Get(decoded, none)
            Assert.Equal(Ok "another-secret", recovered)
    }

[<Fact>]
let ``a reference with no store prefix is rejected`` () =
    match SecretRef.Decode "no-prefix-here" with
    | Ok r -> failwith $"expected a rejection, got store '{r.Store}'"
    | Error _ -> ()

[<Theory>]
[<InlineData("aws", "arn:aws:secretsmanager:eu-west-1:123456789012:secret:umbral-github-k3f9x2qa-AbCdEf")>]
[<InlineData("azure", "https://umbral-kv.vault.azure.net/secrets/umbral-github-k3f9x2qa")>]
[<InlineData("gcp", "projects/my-project/secrets/umbral-github-k3f9x2qa")>]
let ``a reference to an ARN or a URI keeps every colon after the first`` (store: string) (locator: string) =
    // The reference splits at the FIRST colon only, because a locator is
    // allowed colons of its own and nothing outside the store may reinterpret it.
    match SecretRef.Decode $"{store}:{locator}" with
    | Error e -> failwith e
    | Ok decoded ->
        Assert.Equal(store, decoded.Store)
        Assert.Equal(locator, decoded.Locator)

[<Fact>]
let ``a reference belonging to another store is not read`` () =
    task {
        let s = store ()
        let! recovered = s.Get({ Store = "aws"; Locator = "arn:aws:secretsmanager:..." }, none)
        // A store must never try to interpret a locator it did not write.
        // That rule is what keeps a second backend from needing a schema change.
        Assert.Equal(Error NotConfigured, recovered)
    }

[<Fact>]
let ``an undecryptable secret reports not found rather than throwing`` () =
    task {
        let first = store ()
        let! put = first.Put(name, "s3cr3t-value", none)

        // A different key ring: a fresh container whose /data/keys was lost or
        // never mounted. The operator must get a message, not a stack trace in
        // front of an end user mid-login.
        let other = LocalSecretStore(DataProtectionProvider.Create "Umbral.Tests.Other") :> ISecretStore
        let! recovered = other.Get(ok put, none)
        Assert.Equal(Error NotFound, recovered)
    }

[<Fact>]
let ``the local store has nothing to delete or probe`` () =
    task {
        let s = store ()
        let! put = s.Put(name, "s3cr3t-value", none)
        let! deleted = s.Delete(ok put, none)
        let! probed = s.Probe none
        Assert.Equal(Ok(), deleted)
        Assert.Equal(Ok(), probed)
    }

[<Fact>]
let ``a secret name is stable for the same input`` () =
    let at = DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero)
    Assert.Equal(SecretName.derive "umbral" "github" at, SecretName.derive "umbral" "github" at)

[<Fact>]
let ``a secret name does not depend on the offset the time is written in`` () =
    let utc = DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero)
    let local = utc.ToOffset(TimeSpan.FromHours 5.)
    Assert.Equal(SecretName.derive "umbral" "github" utc, SecretName.derive "umbral" "github" local)

[<Fact>]
let ``a connector created again gets a different name`` () =
    let first = SecretName.derive "umbral" "github" (DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero))
    let second = SecretName.derive "umbral" "github" (DateTimeOffset(2026, 10, 12, 9, 30, 1, TimeSpan.Zero))
    Assert.NotEqual<string>(first.Discriminator, second.Discriminator)
    Assert.NotEqual<string>(first.Cloud, second.Cloud)

[<Fact>]
let ``the discriminator is eight characters of lower-case base32`` () =
    let d = (SecretName.derive "umbral" "github" DateTimeOffset.UnixEpoch).Discriminator
    Assert.Matches("^[a-z2-7]{8}$", d)

[<Fact>]
let ``the discriminator is the base32 of the hash of the slug and the time`` () =
    // Pinned, so that a refactor cannot silently rename every secret in every
    // manager: a name that changed would be a secret that can no longer be found.
    let at = DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero)
    let hash = System.Security.Cryptography.SHA256.HashData(Text.Encoding.UTF8.GetBytes "github|2026-10-12T09:30:00.0000000+00:00")
    let alphabet = "abcdefghijklmnopqrstuvwxyz234567"
    let bits = hash[0..4] |> Array.fold (fun (acc: uint64) b -> (acc <<< 8) ||| uint64 b) 0UL
    let expected = String(Array.init 8 (fun i -> alphabet[int ((bits >>> (35 - 5 * i)) &&& 31UL)]))
    Assert.Equal(expected, (SecretName.derive "umbral" "github" at).Discriminator)

[<Fact>]
let ``the renderings are within the length and alphabet each store accepts`` () =
    // The longest a name can be: a 32-character prefix and slug.
    let prefix = String('p', 32)
    let slug = String('s', 32)
    let n = SecretName.derive prefix slug DateTimeOffset.UnixEpoch

    Assert.True(n.Cloud.Length <= 74, $"cloud rendering is {n.Cloud.Length} characters")
    // The Bella key carries the `UMBRAL_` lead (7 characters) on top of the 74.
    // tasks.md T005 says 74 for both; the lead is research R5's own rule, so the
    // bound here is the sum.
    Assert.True(n.Bella.Length <= 74 + 7, $"bella rendering is {n.Bella.Length} characters")

[<Theory>]
[<InlineData("umbral", "github")>]
[<InlineData("my-org-prod", "a-b-c")>]
[<InlineData("u", "0")>]
let ``a rendering uses only what its stores allow`` (prefix: string) (slug: string) =
    let n = SecretName.derive prefix slug DateTimeOffset.UnixEpoch

    // AWS, GCP and Azure: letters, digits and hyphens, nothing else.
    Assert.Matches("^[a-z0-9-]+$", n.Cloud)
    Assert.Equal($"{prefix}-{slug}-{n.Discriminator}", n.Cloud)

    // BellaBaxter: starts with a letter, then upper-case letters, digits and underscores.
    Assert.Matches("^[A-Z][A-Z0-9_]*$", n.Bella)
    Assert.StartsWith("UMBRAL_", n.Bella)
    Assert.Equal($"UMBRAL_{prefix}_{slug}_{n.Discriminator}".ToUpperInvariant().Replace('-', '_'), n.Bella)

[<Theory>]
[<InlineData("abcdefgh", "efgh")>]
[<InlineData("abcd", "••••")>]
[<InlineData("ab", "••")>]
[<InlineData("", "")>]
let ``the hint reveals at most four characters`` (plaintext: string) (expected: string) =
    // A short secret must not be substantially revealed by its own hint.
    Assert.Equal(expected, hint plaintext)

[<Fact>]
let ``the masked form never contains the secret`` () =
    let secret = "super-secret-value"
    let shown = masked (hint secret)
    Assert.DoesNotContain(secret, shown)
    Assert.Contains("••••", shown)

[<Fact>]
let ``a protected value does not print itself`` () =
    let p = Protected.Of "s3cr3t-value"

    // ToString is the hole every redaction effort falls through: structured
    // logging, interpolation and exception messages all reach for it.
    Assert.Equal("[redacted]", p.ToString())
    Assert.Equal("[redacted]", $"{p}")
    Assert.Equal("[redacted]", sprintf "%O" p)
    Assert.Equal("s3cr3t-value", p.Reveal)
