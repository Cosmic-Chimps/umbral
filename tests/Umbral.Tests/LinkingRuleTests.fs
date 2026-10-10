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
