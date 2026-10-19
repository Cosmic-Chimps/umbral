/// `Domains/Passkeys.fs`: the pure rules of the passkey connector (spec 008
/// data-model.md, research R6 and R9). No database, no HTTP.
module Umbral.Tests.PasskeyDomainTests

open System
open System.Text.RegularExpressions
open Microsoft.Extensions.Time.Testing
open Xunit
open Umbral.Domains
open Umbral.Domains.Connectors
open Umbral.Domains.Passkeys

[<Fact>]
let ``the enrolment mode is parsed from its two words and nothing else`` () =
    Assert.Equal(Some InvitationsOnly, parseEnrolment "invitations")
    Assert.Equal(Some OpenSignUp, parseEnrolment "open")
    Assert.Equal(None, parseEnrolment "")
    Assert.Equal(None, parseEnrolment "Open")
    Assert.Equal(None, parseEnrolment "anyone")

[<Theory>]
[<InlineData(0)>]
[<InlineData(1)>]
let ``an enrolment mode round trips through its stored word`` (which: int) =
    let mode = if which = 0 then InvitationsOnly else OpenSignUp
    Assert.Equal(Some mode, parseEnrolment mode.Value)
    Assert.Equal((if which = 0 then "invitations" else "open"), mode.Value)

[<Fact>]
let ``the passkey kind is a connector kind of its own`` () =
    Assert.Equal("passkey", Passkey.Value)
    Assert.Equal("Passkey", Passkey.DisplayName)
    Assert.Equal(Some Passkey, parseKind "passkey")
    Assert.DoesNotContain(Passkey, allKinds)
    Assert.Equal(None, setupCaveat Passkey)
    Assert.Equal("", (endpointsFor Passkey).AuthorizationUrl)

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData("\t \n")>]
let ``a display name of nothing is refused, naming the limits`` (name: string) =
    match validateDisplayName name with
    | Error message ->
        Assert.Contains("1", message)
        Assert.Contains("64", message)
    | Ok value -> failwith $"'{value}' was accepted"

[<Fact>]
let ``a display name of 65 characters is refused and one of 64 is kept`` () =
    Assert.True((validateDisplayName (String('a', 65))).IsError)
    Assert.Equal(Ok(String('a', 64)), validateDisplayName (String('a', 64)))
    Assert.Equal(Ok "a", validateDisplayName "a")

[<Fact>]
let ``a display name is trimmed before it is measured`` () =
    Assert.Equal(Ok "Ada Lovelace", validateDisplayName "  Ada Lovelace \n")
    // 64 characters inside 70 of white space is still 64.
    Assert.Equal(Ok(String('b', 64)), validateDisplayName ("   " + String('b', 64) + "   "))
    Assert.True((validateDisplayName null).IsError)

[<Fact>]
let ``a nickname may be left empty and may be 64 characters but not 65`` () =
    Assert.Equal(Ok None, validateNickname "")
    Assert.Equal(Ok None, validateNickname "   ")
    Assert.Equal(Ok None, validateNickname null)
    Assert.Equal(Ok(Some(String('n', 64))), validateNickname (String('n', 64)))
    Assert.Equal(Ok(Some "Phone"), validateNickname " Phone ")

    match validateNickname (String('n', 65)) with
    | Error message -> Assert.Contains("64", message)
    | Ok _ -> failwith "65 characters were accepted"

[<Fact>]
let ``the default nickname names the day the passkey was added`` () =
    let clock = FakeTimeProvider(DateTimeOffset(2026, 10, 19, 13, 5, 0, TimeSpan.Zero))
    let nickname = defaultNickname clock
    Assert.Contains("2026-10-19", nickname)
    Assert.True(nickname.Length <= 64)
    Assert.Equal(Ok(Some nickname), validateNickname nickname)

[<Fact>]
let ``an invitation secret is 43 base64url characters and never repeats`` () =
    let secrets = List.init 200 (fun _ -> newInvitationSecret ())

    for secret in secrets do
        Assert.Equal(43, secret.Length)
        Assert.Matches("^[A-Za-z0-9_-]{43}$", secret)

    Assert.Equal(200, (Set.ofList secrets).Count)

[<Fact>]
let ``an invitation secret hashes to 64 lowercase hex characters, the same way each time`` () =
    let secret = newInvitationSecret ()
    let hash = hashInvitationSecret secret
    Assert.Matches("^[0-9a-f]{64}$", hash)
    Assert.Equal(hash, hashInvitationSecret secret)
    Assert.NotEqual<string>(hash, hashInvitationSecret (newInvitationSecret ()))
    // The known SHA-256 of "abc", so the hash is the standard one.
    Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hashInvitationSecret "abc")

[<Fact>]
let ``an invitation link is the origin, /invite/ and the secret`` () =
    Assert.Equal("https://umbral.test/invite/abc123", invitationLink "https://umbral.test" "abc123")

[<Fact>]
let ``the limits are the specified ones`` () =
    Assert.Equal(TimeSpan.FromDays 7., InvitationLifetime)
    Assert.Equal(TimeSpan.FromMinutes 5., CeremonyLifetime)
    Assert.Equal(10, MaxPerUser)

let private everyReason =
    [ OriginMismatch
      RpIdMismatch
      ChallengeMismatch
      UserVerificationMissing
      UserPresenceMissing
      InvalidSignature
      SignCountRegressed
      CredentialNotUnique
      UserHandleMismatch
      TypeMismatch
      MalformedResponse
      VerificationFailed
      CeremonyUnknownOrExpired
      UnknownCredential
      UserNotFound
      UserDisabled
      ConnectorDisabled
      PasskeyNotDiscoverable
      PasskeyLimitReached
      DisplayNameInvalid
      NicknameInvalid
      EnrolmentClosed
      InvitationUnknown
      InvitationUsed
      InvitationExpired
      InvitationRevoked
      InvitationUserGone
      JavascriptRequired ]

[<Fact>]
let ``every reason is a lowercase word with underscores, and no two share one`` () =
    Assert.Equal(28, everyReason.Length)

    for reason in everyReason do
        Assert.Matches("^[a-z]+(_[a-z]+)*$", reason.Word)

    let words = everyReason |> List.map (fun reason -> reason.Word)
    Assert.Equal(words.Length, (List.distinct words).Length)

[<Fact>]
let ``the words are the ones research R6 names`` () =
    Assert.Equal("origin_mismatch", OriginMismatch.Word)
    Assert.Equal("rpid_mismatch", RpIdMismatch.Word)
    Assert.Equal("challenge_mismatch", ChallengeMismatch.Word)
    Assert.Equal("user_verification_missing", UserVerificationMissing.Word)
    Assert.Equal("user_presence_missing", UserPresenceMissing.Word)
    Assert.Equal("sign_count_regressed", SignCountRegressed.Word)
    Assert.Equal("ceremony_unknown_or_expired", CeremonyUnknownOrExpired.Word)
    Assert.Equal("passkey_not_discoverable", PasskeyNotDiscoverable.Word)
    Assert.Equal("invitation_user_gone", InvitationUserGone.Word)
    Assert.Equal("javascript_required", JavascriptRequired.Word)

[<Fact>]
let ``a refusal detail is built from closed words only`` () =
    Assert.Equal(
        "passkey_refused;reason=origin_mismatch;stage=assertion",
        refusalDetail OriginMismatch Assertion
    )

    Assert.Equal(
        "passkey_refused;reason=invitation_used;stage=invitation",
        refusalDetail InvitationUsed Invitation
    )

    Assert.Equal(
        "passkey_refused;reason=challenge_mismatch;stage=registration",
        refusalDetail ChallengeMismatch Registration
    )

    for reason in everyReason do
        for stage in [ Registration; Assertion; Invitation ] do
            Assert.Matches(Regex.Escape "passkey_refused;reason=" + "[a-z_]+;stage=(registration|assertion|invitation)$", refusalDetail reason stage)
