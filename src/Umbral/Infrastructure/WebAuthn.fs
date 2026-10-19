/// The one place Umbral calls the WebAuthn library (specs/008-passkey-connector,
/// plan.md "The verifier", research R2 and R6).
///
/// Umbral writes no attestation or signature code: fido2-net-lib parses every
/// registration and verifies every sign-in, and this module only builds the
/// options it is asked to, hands the browser's answer back to it, and turns what
/// comes out into a closed `Reason` word. Keeping that in one file is what keeps
/// "as little auth as possible" true: nothing else in Umbral names the library.
///
/// The relying party is the configured public origin and never the request
/// (FR-024): its host is the RP id, its text the only origin accepted. The
/// library's exception message is dropped on the floor, because it names the
/// offending origin (R6).
module Umbral.Infrastructure.WebAuthn

open System
open System.Collections.Generic
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Fido2NetLib
open Fido2NetLib.Exceptions
open Fido2NetLib.Objects
open Umbral.Domains.Passkeys

/// What a verified registration gives Umbral to store.
type Registered =
    { CredentialId: byte array
      PublicKey: byte array
      SignCount: uint32
      AaGuid: Guid
      /// The library's transport names (`internal`, `usb`, ...).
      Transports: string list
      BackupEligible: bool
      /// The `users.id` the options named: the handle the authenticator stored.
      UserId: string
      /// What the browser said about `credProps.rk`: None when it said nothing.
      Discoverable: bool option }

/// `Fido2NetLib.None` is a class (the "none" attestation format), and with that
/// namespace open it shadows the option case `None` in expressions.
let private absent<'T> : 'T option = Option<'T>.None

let private transportName (transport: AuthenticatorTransport) =
    match transport with
    | AuthenticatorTransport.Usb -> "usb"
    | AuthenticatorTransport.Nfc -> "nfc"
    | AuthenticatorTransport.Ble -> "ble"
    | AuthenticatorTransport.SmartCard -> "smart-card"
    | AuthenticatorTransport.Hybrid -> "hybrid"
    | AuthenticatorTransport.Internal -> "internal"
    | _ -> "unknown"

let private parseTransport (name: string) : AuthenticatorTransport option =
    match name with
    | "usb" -> Some AuthenticatorTransport.Usb
    | "nfc" -> Some AuthenticatorTransport.Nfc
    | "ble" -> Some AuthenticatorTransport.Ble
    | "smart-card" -> Some AuthenticatorTransport.SmartCard
    | "hybrid" -> Some AuthenticatorTransport.Hybrid
    | "internal" -> Some AuthenticatorTransport.Internal
    | _ -> absent

/// Why the library refused, from its error code (R6). The exception's message is
/// never read. Two refusals share a code the library reuses for several checks,
/// so the one Umbral can tell apart itself is passed in: `handleRejected` is true
/// when Umbral's own user-handle callback said no.
let mapException (handleRejected: bool) (error: Fido2VerificationException) : Reason =
    match error.Code with
    | Fido2ErrorCode.InvalidRpidHash -> RpIdMismatch
    | Fido2ErrorCode.InvalidSignature -> InvalidSignature
    | Fido2ErrorCode.InvalidSignCount -> SignCountRegressed
    | Fido2ErrorCode.UserVerificationRequirementNotMet -> UserVerificationMissing
    | Fido2ErrorCode.UserPresentFlagNotSet -> UserPresenceMissing
    | Fido2ErrorCode.MissingAuthenticatorResponseChallenge
    | Fido2ErrorCode.InvalidAuthenticatorResponseChallenge -> ChallengeMismatch
    | Fido2ErrorCode.MissingAuthenticatorResponseOrigin
    | Fido2ErrorCode.InvalidAuthenticatorResponseOrigin
    | Fido2ErrorCode.InvalidAuthenticatorResponseTopOrigin
    | Fido2ErrorCode.CrossOriginRequestNotAllowed -> OriginMismatch
    | Fido2ErrorCode.NonUniqueCredentialId -> CredentialNotUnique
    | Fido2ErrorCode.InvalidAssertionResponse
    | Fido2ErrorCode.InvalidAttestationResponse -> if handleRejected then UserHandleMismatch else TypeMismatch
    | Fido2ErrorCode.MalformedAuthenticatorResponse
    | Fido2ErrorCode.MalformedAttestationObject
    | Fido2ErrorCode.InvalidAttestationObject
    | Fido2ErrorCode.MissingAttestationObject
    | Fido2ErrorCode.MissingAuthenticatorData
    | Fido2ErrorCode.InvalidAuthenticatorData -> MalformedResponse
    | _ -> VerificationFailed

let private utf8 (text: string) = Encoding.UTF8.GetBytes text

/// The relying party's verifier: one `Fido2` built on first use, so an instance
/// without a passkey connector never loads the library or libsodium (R2).
type Verifier(origin: PublicOrigin.PublicOrigin, config: Database.DbConfig) =
    let fido2 =
        lazy
            (let settings =
                Fido2Configuration(
                    RPID = origin.Uri.Host,
                    RPName = "Umbral",
                    Origins = HashSet<string>([ origin.Text ]),
                    ChallengeSize = 32,
                    Timeout = 300000u
                )

             Fido2(settings, null))

    let descriptors (existing: Passkeys.Passkey list) : PublicKeyCredentialDescriptor list =
        existing
        |> List.map (fun passkey ->
            let transports = passkey.Transports |> List.choose parseTransport |> Array.ofList
            PublicKeyCredentialDescriptor(PublicKeyCredentialType.PublicKey, passkey.CredentialId, transports))

    /// The options for a registration: a discoverable credential with user
    /// verification, no attestation, the credential-properties extension (so the
    /// server can see `rk`), and the user's existing credentials excluded so the
    /// same authenticator is not enrolled twice. The user handle is the opaque
    /// `users.id` (R10); `name` is what the authenticator shows (the email when
    /// the user has one, else the display name).
    member _.RegistrationOptions
        (userId: string, name: string, displayName: string, existing: Passkeys.Passkey list)
        : CredentialCreateOptions =
        let selection =
            AuthenticatorSelection(
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required
            )

        let request =
            RequestNewCredentialParams(
                User = Fido2User(Id = utf8 userId, Name = name, DisplayName = displayName),
                ExcludeCredentials = descriptors existing,
                AuthenticatorSelection = selection,
                AttestationPreference = AttestationConveyancePreference.None,
                Extensions = AuthenticationExtensionsClientInputs(CredProps = true)
            )

        fido2.Value.RequestNewCredential request

    /// The options for a sign-in: user verification required and no
    /// `allowCredentials`, because nobody has said who they are; the
    /// authenticator offers the passkeys it holds and answers with a user handle.
    member _.AssertionOptions() : AssertionOptions =
        let request =
            GetAssertionOptionsParams(
                AllowedCredentials = List<PublicKeyCredentialDescriptor>(),
                UserVerification = UserVerificationRequirement.Required
            )

        fido2.Value.GetAssertionOptions request

    /// Verifies a registration against the options stored when it began.
    /// `credential` is the browser's `PublicKeyCredential.toJSON()`.
    member _.VerifyRegistration(optionsJson: string, credential: JsonElement) : Task<Result<Registered, Reason>> =
        task {
            let parsed =
                try
                    let options = CredentialCreateOptions.FromJson optionsJson

                    match JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(credential) with
                    | null -> Error MalformedResponse
                    | response -> Ok(options, response)
                with
                | :? JsonException -> Error MalformedResponse

            match parsed with
            | Error reason -> return Error reason
            | Ok(options, response) ->
                try
                    let request =
                        MakeNewCredentialParams(
                            AttestationResponse = response,
                            OriginalOptions = options,
                            IsCredentialIdUniqueToUserCallback =
                                IsCredentialIdUniqueToUserAsyncDelegate(fun parameters _ ->
                                    task {
                                        let! exists = Passkeys.credentialIdExists config parameters.CredentialId
                                        return not exists
                                    })
                        )

                    let! registered = fido2.Value.MakeNewCredentialAsync(request)

                    // What the browser said about `credProps.rk`; nothing at all is
                    // not a refusal (some authenticators and browsers do not say).
                    let discoverable =
                        if isNull (box response.ClientExtensionResults) then
                            absent
                        else
                            // `credProps` itself is optional: a browser that was not
                            // asked, or cannot say, sends none.
                            match response.ClientExtensionResults.CredProps with
                            | null -> absent
                            | props -> Some props.Rk

                    if discoverable = Some false then
                        return Error PasskeyNotDiscoverable
                    else
                        return
                            Ok
                                { CredentialId = registered.Id
                                  PublicKey = registered.PublicKey
                                  SignCount = registered.SignCount
                                  AaGuid = registered.AaGuid
                                  Transports =
                                    (if isNull registered.Transports then [] else List.ofArray registered.Transports)
                                    |> List.map transportName
                                  BackupEligible = registered.IsBackupEligible
                                  UserId = Encoding.UTF8.GetString registered.User.Id
                                  Discoverable = discoverable }
                with
                | :? Fido2VerificationException as error -> return Error(mapException false error)
                | :? OperationCanceledException -> return raise (OperationCanceledException())
                | _ -> return Error VerificationFailed
        }

    /// Verifies a sign-in against the options stored when it began and the
    /// passkey the credential id named. Gives the authenticator's new signature
    /// counter for `Passkeys.recordUse`.
    member _.VerifyAssertion
        (optionsJson: string, credential: JsonElement, stored: Passkeys.Passkey)
        : Task<Result<uint32, Reason>> =
        task {
            let parsed =
                try
                    let options = AssertionOptions.FromJson optionsJson

                    match JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(credential) with
                    | null -> Error MalformedResponse
                    | response -> Ok(options, response)
                with
                | :? JsonException -> Error MalformedResponse

            match parsed with
            | Error reason -> return Error reason
            | Ok(options, response) ->
                let mutable handleRejected = false

                try
                    let owner =
                        IsUserHandleOwnerOfCredentialIdAsync(fun parameters _ ->
                            task {
                                let ours = utf8 stored.UserId

                                let owns =
                                    not (isNull (box parameters.UserHandle))
                                    && CryptographicOperations.FixedTimeEquals(
                                        ReadOnlySpan<byte>(ours),
                                        ReadOnlySpan<byte>(parameters.UserHandle)
                                    )

                                if not owns then handleRejected <- true
                                return owns
                            })

                    let request =
                        MakeAssertionParams(
                            AssertionResponse = response,
                            OriginalOptions = options,
                            StoredPublicKey = stored.PublicKey,
                            StoredSignatureCounter = stored.SignCount,
                            IsUserHandleOwnerOfCredentialIdCallback = owner
                        )

                    let! verified = fido2.Value.MakeAssertionAsync(request)
                    return Ok verified.SignCount
                with
                | :? Fido2VerificationException as error -> return Error(mapException handleRejected error)
                | :? OperationCanceledException -> return raise (OperationCanceledException())
                | _ -> return Error VerificationFailed
        }
