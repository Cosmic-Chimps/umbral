module Umbral.Tests.SecretsTests

open System.Threading.Tasks
open Microsoft.AspNetCore.DataProtection
open Xunit
open Umbral.Infrastructure.Secrets

let private store () : ISecretStore =
    LocalSecretStore(DataProtectionProvider.Create "Umbral.Tests") :> ISecretStore

[<Fact>]
let ``a stored secret comes back`` () =
    task {
        let s = store ()
        let! reference = s.Put("connector/discord/client_secret", "s3cr3t-value")
        let! recovered = s.Get reference
        Assert.Equal(Some "s3cr3t-value", recovered)
    }

[<Fact>]
let ``the locator is not the plaintext`` () =
    task {
        let s = store ()
        let! reference = s.Put("connector/discord/client_secret", "s3cr3t-value")
        // Whatever ends up in the row, reading the row must not reveal the
        // secret. This is the whole point of the column.
        Assert.DoesNotContain("s3cr3t-value", reference.Locator)
        Assert.Equal("local", reference.Store)
    }

[<Fact>]
let ``a reference round-trips through its encoded form`` () =
    task {
        let s = store ()
        let! reference = s.Put("connector/github/client_secret", "another-secret")

        // The row holds one column, so encode/decode must be lossless even
        // though the locator itself contains no shortage of punctuation.
        match SecretRef.Decode reference.Encoded with
        | Error e -> failwith e
        | Ok decoded ->
            Assert.Equal(reference.Store, decoded.Store)
            Assert.Equal(reference.Locator, decoded.Locator)
            let! recovered = s.Get decoded
            Assert.Equal(Some "another-secret", recovered)
    }

[<Fact>]
let ``a reference with no store prefix is rejected`` () =
    match SecretRef.Decode "no-prefix-here" with
    | Ok r -> failwith $"expected a rejection, got store '{r.Store}'"
    | Error _ -> ()

[<Fact>]
let ``a reference belonging to another store is not read`` () =
    task {
        let s = store ()
        let! recovered = s.Get { Store = "aws"; Locator = "arn:aws:secretsmanager:..." }
        // A store must never try to interpret a locator it did not write.
        // That rule is what keeps a second backend from needing a schema change.
        Assert.Equal(None, recovered)
    }

[<Fact>]
let ``an undecryptable secret reports absence rather than throwing`` () =
    task {
        let first = store ()
        let! reference = first.Put("connector/discord/client_secret", "s3cr3t-value")

        // A different key ring: a fresh container whose /data/keys was lost or
        // never mounted. The operator must get a message, not a stack trace in
        // front of an end user mid-login.
        let other = LocalSecretStore(DataProtectionProvider.Create "Umbral.Tests.Other") :> ISecretStore
        let! recovered = other.Get reference
        Assert.Equal(None, recovered)
    }

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
