module Umbral.Tests.LinkingRuleTests

open Xunit
open Umbral.Domains.Linking

let private incoming email verified =
    { ProviderKind = "github"
      ProviderSubject = "12345"
      Email = email
      EmailVerified = verified }

let private verified id = { UserId = id; EmailVerified = true }

[<Fact>]
let ``a known provider account is the user it is linked to`` () =
    let outcome = decide (Some "u1") (incoming (Some "sam@example.com") true) [ verified "u2" ]

    // Keyed on the provider's immutable id, so this wins over any email match.
    // A GitHub user who renames is still the same person.
    Assert.Equal(ExistingLink "u1", outcome.Decision)
    Assert.Equal(AlreadyLinked, outcome.Reason)

[<Fact>]
let ``no email asserted creates a new user`` () =
    // GitHub users can keep an email private; Discord accounts can be
    // unverified. Normal, not an error.
    let outcome = decide None (incoming None false) []
    Assert.Equal(CreateNew, outcome.Decision)
    Assert.Equal(NoEmailAsserted, outcome.Reason)

[<Fact>]
let ``an UNVERIFIED email never links, even to an exact match`` () =
    // The account-takeover vector. A provider that lets someone claim
    // victim@example.com without proving inbox control must not be able to
    // hand over the victim's account.
    let outcome = decide None (incoming (Some "victim@example.com") false) [ verified "victim" ]

    Assert.Equal(CreateNew, outcome.Decision)
    Assert.Equal(EmailNotVerified, outcome.Reason)

[<Fact>]
let ``a verified email matching exactly one verified user links`` () =
    let outcome = decide None (incoming (Some "sam@example.com") true) [ verified "u1" ]
    Assert.Equal(LinkToExisting "u1", outcome.Decision)
    Assert.Equal(SingleVerifiedMatch, outcome.Reason)

[<Fact>]
let ``a verified email matching nobody creates a new user`` () =
    let outcome = decide None (incoming (Some "new@example.com") true) []
    Assert.Equal(CreateNew, outcome.Decision)
    Assert.Equal(NoVerifiedMatch, outcome.Reason)

[<Fact>]
let ``a verified email matching two users creates a third rather than guessing`` () =
    // Two users can legitimately share a verified address. Picking one is how
    // somebody gets somebody else's account; the operator deletes the strays.
    let outcome = decide None (incoming (Some "shared@example.com") true) [ verified "u1"; verified "u2" ]

    Assert.Equal(CreateNew, outcome.Decision)
    Assert.Equal(AmbiguousMatch 2, outcome.Reason)

[<Fact>]
let ``an unverified candidate is not a match`` () =
    // Defence in depth: the caller is told to pass only verified rows, and the
    // rule refuses them anyway. Two independent mistakes would be needed to
    // link on an unverified address.
    let outcome =
        decide None (incoming (Some "sam@example.com") true) [ { UserId = "u1"; EmailVerified = false } ]

    Assert.Equal(CreateNew, outcome.Decision)
    Assert.Equal(NoVerifiedMatch, outcome.Reason)

[<Fact>]
let ``one verified candidate among unverified ones still links`` () =
    let outcome =
        decide
            None
            (incoming (Some "sam@example.com") true)
            [ { UserId = "u1"; EmailVerified = false }
              verified "u2"
              { UserId = "u3"; EmailVerified = false } ]

    Assert.Equal(LinkToExisting "u2", outcome.Decision)

[<Fact>]
let ``an existing link wins even when the email is now unverified`` () =
    // Losing verification at the provider must not log someone out of their
    // own account.
    let outcome = decide (Some "u1") (incoming (Some "sam@example.com") false) []
    Assert.Equal(ExistingLink "u1", outcome.Decision)

[<Fact>]
let ``an empty email string is not treated as an address`` () =
    // Some providers return "" rather than null. It must not match the other
    // users who also have "".
    let outcome = decide None (incoming (Some "") true) [ verified "u1" ]
    Assert.Equal(CreateNew, outcome.Decision)

// ---------------------------------------------------------------------------
// The fingerprint of a provider account (specs/018, FR-008)
// ---------------------------------------------------------------------------

[<Fact>]
let ``a fingerprint is stable, 43 characters of unpadded base64url`` () =
    let one = fingerprint "github" "12345"

    Assert.Equal(one, fingerprint "github" "12345")
    Assert.Equal(43, one.Length)
    Assert.Matches("^[A-Za-z0-9_-]{43}$", one)

    // SHA-256 of "github\n12345", computed once and pinned: a change here would
    // orphan every session and token that carries a fingerprint.
    let expected =
        System.Convert
            .ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes "github\n12345"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_')

    Assert.Equal(expected, one)

[<Fact>]
let ``a fingerprint differs by kind and by subject`` () =
    Assert.NotEqual<string>(fingerprint "github" "1", fingerprint "google" "1")
    Assert.NotEqual<string>(fingerprint "github" "1", fingerprint "github" "2")
    // The separator keeps the two parts apart: moving a character from the
    // subject to the kind is another account.
    Assert.NotEqual<string>(fingerprint "ab" "c", fingerprint "a" "bc")

[<Fact>]
let ``a credential reference round trips through its value`` () =
    for c in [ LinkCredential "abc-_"; PasskeyCredential "p1" ] do
        Assert.Equal(Some c, parseCredential (credentialValue c))

    Assert.Equal("link:abc", credentialValue (LinkCredential "abc"))
    Assert.Equal("passkey:p1", credentialValue (PasskeyCredential "p1"))

[<Fact>]
let ``parseCredential rejects anything else`` () =
    for bad in [ ""; "link:"; "passkey:"; "garbage"; "LINK:x"; "link"; " link:x" ] do
        Assert.Equal(None, parseCredential bad)
