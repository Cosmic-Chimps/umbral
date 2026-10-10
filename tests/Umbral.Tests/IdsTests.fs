module Umbral.Tests.IdsTests

open System
open System.Security.Cryptography
open System.Text
open Xunit
open Umbral.Domains.Ids

[<Fact>]
let ``a subject is base64url with no padding`` () =
    let subject = newSubject ()
    // It goes into URLs and into a `sub` claim, so no '+', '/' or '='.
    Assert.DoesNotContain("=", subject)
    Assert.DoesNotContain("+", subject)
    Assert.DoesNotContain("/", subject)

[<Fact>]
let ``subjects do not repeat`` () =
    let generated = List.init 1000 (fun _ -> newSubject ()) |> Set.ofList
    Assert.Equal(1000, generated.Count)

[<Fact>]
let ``a code verifier is within the length RFC 7636 allows`` () =
    let verifier = newCodeVerifier ()
    Assert.InRange(verifier.Length, 43, 128)

[<Fact>]
let ``the code challenge is the base64url sha256 of the verifier`` () =
    let verifier = newCodeVerifier ()

    let expected =
        verifier
        |> Encoding.ASCII.GetBytes
        |> SHA256.HashData
        |> Convert.ToBase64String
        |> fun s -> s.TrimEnd('=').Replace('+', '-').Replace('/', '_')

    Assert.Equal(expected, codeChallengeS256 verifier)

[<Fact>]
let ``the code challenge matches the RFC 7636 appendix B vector`` () =
    // The one published test vector. If this breaks, every upstream token
    // exchange breaks, and the provider's error will not say why.
    Assert.Equal(
        "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
        codeChallengeS256 "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"
    )

[<Theory>]
[<InlineData("discord", true)>]
[<InlineData("github", true)>]
[<InlineData("my-discord-2", true)>]
[<InlineData("a", true)>]
[<InlineData("", false)>]
[<InlineData("My Discord", false)>]
[<InlineData("Discord", false)>]
[<InlineData("dis_cord", false)>]
[<InlineData("dis/cord", false)>]
[<InlineData("../etc/passwd", false)>]
let ``slug validation`` (candidate: string) (expected: bool) =
    // A slug lands in a URL path, so path traversal and spaces must not pass.
    Assert.Equal(expected, isValidSlug candidate)

[<Fact>]
let ``a slug longer than 32 characters is refused`` () =
    Assert.False(isValidSlug (String('a', 33)))
    Assert.True(isValidSlug (String('a', 32)))

[<Fact>]
let ``constant time comparison agrees with equality`` () =
    Assert.True(constantTimeEquals "abc" "abc")
    Assert.False(constantTimeEquals "abc" "abd")
    Assert.False(constantTimeEquals "abc" "ab")

[<Fact>]
let ``constant time comparison treats null as no match`` () =
    // A missing `state` query parameter must never compare equal to a stored one.
    Assert.False(constantTimeEquals null "abc")
    Assert.False(constantTimeEquals "abc" null)
    Assert.False(constantTimeEquals null null)
