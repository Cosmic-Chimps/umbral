/// The verifier against a software authenticator, with no HTTP
/// (specs/008-passkey-connector, T013): each refusal the library can give is
/// made by one override and must come out as its closed `Reason`.
module Umbral.Tests.WebAuthnVerifierTests

open System
open System.Text.Json
open Xunit
open Umbral.Domains
open Umbral.Domains.Passkeys

module Store = Umbral.Infrastructure.Passkeys
open Umbral.Infrastructure
open Umbral.Infrastructure.WebAuthn
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.SoftwareAuthenticator

let private OriginText = "https://umbral.test"
let private RpId = "umbral.test"

let private origin =
    match PublicOrigin.parse OriginText with
    | Ok origin -> origin
    | Error error -> failwith (PublicOrigin.describe error)

let private seedUser (app: Fixtures.TestApp) (id: string) =
    Users.insertForTesting
        app.DbConfig
        app.Clock
        { Id = id
          DisplayName = $"User {id}"
          Email = None
          EmailVerified = false
          AvatarUrl = None
          IsEnabled = true
          FirstSeenUtc = app.Clock.GetUtcNow()
          LastSeenUtc = app.Clock.GetUtcNow() }

let private element (json: string) = JsonDocument.Parse(json).RootElement

let private stored (userId: string) (registered: Registered) (signCount: uint32) : Store.Passkey =
    { Id = Store.newId ()
      UserId = userId
      CredentialId = registered.CredentialId
      PublicKey = registered.PublicKey
      SignCount = signCount
      Nickname = "test"
      AaGuid = None
      Transports = registered.Transports
      BackupEligible = Some registered.BackupEligible
      CreatedUtc = DateTimeOffset.UnixEpoch
      LastUsedUtc = None }

/// A user, a verifier and an authenticator, and a registration made with
/// `overrides`.
let private register (overrides: RegisterOverrides) =
    task {
        use! app = Fixtures.createApp ()
        do! seedUser app "u1"
        let verifier = Verifier(origin, app.DbConfig)
        let authenticator = SoftwareAuthenticator(RpId, OriginText)
        let options = verifier.RegistrationOptions("u1", "User", "User u1", [])
        let json = options.ToJson()
        let credential = authenticator.Register(element json, overrides)
        return! verifier.VerifyRegistration(json, credential)
    }

let private expectRegistrationRefused (reason: Reason) (overrides: RegisterOverrides) =
    task {
        let! result = register overrides
        Assert.Equal(Error reason, result |> Result.map ignore)
    }

/// Registers cleanly, then signs in with `overrides` against a stored count.
let private signIn (storedCount: uint32) (make: byte array -> AssertOverrides) =
    task {
        use! app = Fixtures.createApp ()
        do! seedUser app "u1"
        do! seedUser app "u2"
        let verifier = Verifier(origin, app.DbConfig)
        let authenticator = SoftwareAuthenticator(RpId, OriginText)
        let options = verifier.RegistrationOptions("u1", "User", "User u1", [])
        let json = options.ToJson()
        let! registered = verifier.VerifyRegistration(json, authenticator.Register(element json))
        let registered = Result.defaultWith (fun reason -> failwith $"{reason}") registered
        // A second credential of another user, whose key can sign instead.
        let otherOptions = verifier.RegistrationOptions("u2", "Other", "User u2", [])
        let otherJson = otherOptions.ToJson()
        authenticator.Register(element otherJson) |> ignore
        let other = authenticator.Credentials |> List.last

        let passkey = stored "u1" registered storedCount
        let assertion = verifier.AssertionOptions()
        let assertionJson = assertion.ToJson()

        let response =
            authenticator.Assert(
                element assertionJson,
                registered.CredentialId,
                make other.Id
            )

        return! verifier.VerifyAssertion(assertionJson, response, passkey)
    }

[<Fact>]
let ``a registration and a sign-in verify`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seedUser app "u1"
        let verifier = Verifier(origin, app.DbConfig)
        let authenticator = SoftwareAuthenticator(RpId, OriginText)
        let options = verifier.RegistrationOptions("u1", "User", "User u1", [])
        let json = options.ToJson()
        let credential = authenticator.Register(element json)

        // The authenticator's output is what the library deserialises.
        JsonSerializer.Deserialize<Fido2NetLib.AuthenticatorAttestationRawResponse>(credential) |> ignore

        let! result = verifier.VerifyRegistration(json, credential)

        match result with
        | Error reason -> failwith $"registration refused: {reason}"
        | Ok registered ->
            Assert.Equal<byte array>(authenticator.Credentials.Head.Id, registered.CredentialId)
            Assert.Equal("u1", registered.UserId)
            Assert.Equal(Some true, registered.Discoverable)

            let passkey = stored "u1" registered registered.SignCount
            let assertion = verifier.AssertionOptions().ToJson()
            let answer = authenticator.Assert(element assertion, registered.CredentialId)
            JsonSerializer.Deserialize<Fido2NetLib.AuthenticatorAssertionRawResponse>(answer) |> ignore
            let! verified = verifier.VerifyAssertion(assertion, answer, passkey)
            Assert.Equal(Ok 1u, verified)
    }

[<Fact>]
let ``a registration at another origin is refused`` () =
    expectRegistrationRefused OriginMismatch { RegisterOverrides.None with Origin = Some "https://evil.test" }

[<Fact>]
let ``a registration for another RP id is refused`` () =
    expectRegistrationRefused RpIdMismatch { RegisterOverrides.None with RpId = Some "evil.test" }

[<Fact>]
let ``a registration with another challenge is refused`` () =
    expectRegistrationRefused ChallengeMismatch { RegisterOverrides.None with Challenge = Some(toBase64Url (Array.zeroCreate 32)) }

[<Fact>]
let ``a registration without user verification is refused`` () =
    expectRegistrationRefused UserVerificationMissing { RegisterOverrides.None with Flags = Some 0x41uy }

[<Fact>]
let ``a registration without user presence is refused`` () =
    expectRegistrationRefused UserPresenceMissing { RegisterOverrides.None with Flags = Some 0x44uy }

[<Fact>]
let ``a registration of a non-discoverable credential is refused`` () =
    expectRegistrationRefused PasskeyNotDiscoverable { RegisterOverrides.None with Rk = Some false }

[<Fact>]
let ``a registration of the wrong type is refused`` () =
    expectRegistrationRefused TypeMismatch { RegisterOverrides.None with Type = Some "webauthn.get" }

[<Fact>]
let ``garbage is a malformed registration`` () =
    task {
        use! app = Fixtures.createApp ()
        let verifier = Verifier(origin, app.DbConfig)
        let json = verifier.RegistrationOptions("u1", "User", "User u1", []).ToJson()
        let! result = verifier.VerifyRegistration(json, element "\"garbage\"")
        Assert.Equal(Error MalformedResponse, result |> Result.map ignore)
    }

[<Fact>]
let ``garbage is a malformed sign-in`` () =
    task {
        use! app = Fixtures.createApp ()
        let verifier = Verifier(origin, app.DbConfig)
        let json = verifier.AssertionOptions().ToJson()

        let passkey: Store.Passkey =
            { Id = "x"
              UserId = "u1"
              CredentialId = [| 1uy |]
              PublicKey = [| 1uy |]
              SignCount = 0u
              Nickname = ""
              AaGuid = None
              Transports = []
              BackupEligible = None
              CreatedUtc = DateTimeOffset.UnixEpoch
              LastUsedUtc = None }

        let! result = verifier.VerifyAssertion(json, element "[1]", passkey)
        Assert.Equal(Error MalformedResponse, result)
    }

let private expectSignInRefused (reason: Reason) (stored: uint32) (make: byte array -> AssertOverrides) =
    task {
        let! result = signIn stored make
        Assert.Equal(Error reason, result)
    }

[<Fact>]
let ``a sign-in at another origin is refused`` () =
    expectSignInRefused OriginMismatch 0u (fun _ -> { AssertOverrides.None with Origin = Some "https://evil.test" })

[<Fact>]
let ``a sign-in for another RP id is refused`` () =
    expectSignInRefused RpIdMismatch 0u (fun _ -> { AssertOverrides.None with RpId = Some "evil.test" })

[<Fact>]
let ``a sign-in with another challenge is refused`` () =
    expectSignInRefused ChallengeMismatch 0u (fun _ -> { AssertOverrides.None with Challenge = Some(toBase64Url (Array.zeroCreate 32)) })

[<Fact>]
let ``a sign-in without user verification is refused`` () =
    expectSignInRefused UserVerificationMissing 0u (fun _ -> { AssertOverrides.None with Flags = Some 0x01uy })

[<Fact>]
let ``a sign-in without user presence is refused`` () =
    expectSignInRefused UserPresenceMissing 0u (fun _ -> { AssertOverrides.None with Flags = Some 0x04uy })

[<Fact>]
let ``a repeated non-zero counter is refused`` () =
    expectSignInRefused SignCountRegressed 5u (fun _ -> { AssertOverrides.None with Counter = Some 5u })

[<Fact>]
let ``a counter that stays zero is accepted`` () =
    task {
        let! result = signIn 0u (fun _ -> { AssertOverrides.None with Counter = Some 0u })
        Assert.Equal(Ok 0u, result)
    }

[<Fact>]
let ``a signature by another credential's key is refused`` () =
    task {
        let! result = signIn 0u (fun other -> { AssertOverrides.None with SignWith = Some other })
        Assert.Equal(Error InvalidSignature, result)
    }

[<Fact>]
let ``another user's handle is refused`` () =
    expectSignInRefused UserHandleMismatch 0u (fun _ -> { AssertOverrides.None with UserHandle = Some "u2"B })

[<Fact>]
let ``a sign-in of the wrong type is refused`` () =
    expectSignInRefused TypeMismatch 0u (fun _ -> { AssertOverrides.None with Type = Some "webauthn.create" })
