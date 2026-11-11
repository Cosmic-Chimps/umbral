/// The pure rules of the Sign in with Apple connector (specs/013): the kind, its
/// static endpoints entry, and the two checks the pane runs on what the operator
/// types. No network and no database.
module Umbral.Tests.AppleDomainTests

open Xunit
open Umbral.Domains.Connectors

// ---------------------------------------------------------------------------
// The kind
// ---------------------------------------------------------------------------

[<Fact>]
let ``apple is a kind that parses and prints as itself`` () =
    Assert.Equal(Some Apple, parseKind "apple")
    Assert.Equal("apple", Apple.Value)
    Assert.Equal("Apple", Apple.DisplayName)

[<Fact>]
let ``the built-in kinds do not include apple, so it never gets a card in the add form`` () =
    Assert.DoesNotContain(Apple, allKinds)

[<Fact>]
let ``the static apple entry has no address, asks for name and email, and answers by form post`` () =
    let endpoints = endpointsFor Apple

    // The addresses come from Apple's discovery document, as for `Oidc`.
    Assert.Equal("", endpoints.AuthorizationUrl)
    Assert.Equal("", endpoints.TokenUrl)
    Assert.Equal("", endpoints.UserInfoUrl)
    Assert.Equal<string list>([ "name"; "email" ], endpoints.Scopes)
    Assert.True endpoints.SupportsPkce
    Assert.True endpoints.UsesNonce
    Assert.True endpoints.AnswersByFormPost

[<Fact>]
let ``no other kind answers by form post`` () =
    for kind in [ Discord; GitHub; Google; Fake; Oidc; Fediverse; Passkey ] do
        Assert.False((endpointsFor kind).AnswersByFormPost, $"{kind.Value} must answer by redirect")

[<Fact>]
let ``the apple issuer is Apple's`` () =
    Assert.Equal("https://appleid.apple.com", appleIssuer)

// ---------------------------------------------------------------------------
// Team ID and Key ID
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("DEF123GHIJ", "DEF123GHIJ")>]
[<InlineData(" ABC123defg ", "ABC123defg")>]
[<InlineData("0123456789", "0123456789")>]
let ``parseAppleId accepts ten ASCII letters or digits, trimmed, case kept`` (input: string, expected: string) =
    Assert.Equal(Some expected, parseAppleId input)

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData("DEF123GHI")>]
[<InlineData("DEF123GHIJK")>]
[<InlineData("DEF123-HIJ")>]
[<InlineData("DEF123 HIJ")>]
[<InlineData("DEF123GHIÉ")>]
let ``parseAppleId refuses anything else`` (input: string) =
    Assert.Equal(None, parseAppleId input)

[<Fact>]
let ``parseAppleId refuses null`` () = Assert.Equal(None, parseAppleId null)

// ---------------------------------------------------------------------------
// Services ID
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("com.example.signin", "com.example.signin")>]
[<InlineData("  com.example.signin  ", "com.example.signin")>]
let ``parseServicesId accepts a value with no whitespace, trimmed`` (input: string, expected: string) =
    Assert.Equal(Some expected, parseServicesId input)

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData("com example")>]
[<InlineData("com.example\tsignin")>]
let ``parseServicesId refuses empty and whitespace inside`` (input: string) =
    Assert.Equal(None, parseServicesId input)

[<Fact>]
let ``parseServicesId refuses null`` () = Assert.Equal(None, parseServicesId null)
