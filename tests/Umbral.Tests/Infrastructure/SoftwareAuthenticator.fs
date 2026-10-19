/// A browser and an authenticator in software (specs/008-passkey-connector,
/// T012): it builds the `PublicKeyCredential.toJSON()` answers a real device
/// would give, so the verifier and the endpoints are tested against the real
/// library with no network and no hardware. Overrides make each refusal.
module Umbral.Tests.Infrastructure.SoftwareAuthenticator

open System
open System.Formats.Cbor
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Nodes

let toBase64Url (bytes: byte array) : string =
    Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')

let fromBase64Url (text: string) : byte array =
    let padded = text.Replace('-', '+').Replace('_', '/')

    let padded =
        match padded.Length % 4 with
        | 2 -> padded + "=="
        | 3 -> padded + "="
        | _ -> padded

    Convert.FromBase64String padded

type RegisterOverrides =
    { Origin: string option
      RpId: string option
      Challenge: string option
      Flags: byte option
      Counter: uint32 option
      Rk: bool option
      CredentialId: byte array option
      Type: string option }

    static member None =
        { Origin = None
          RpId = None
          Challenge = None
          Flags = None
          Counter = None
          Rk = None
          CredentialId = None
          Type = None }

type AssertOverrides =
    { Origin: string option
      RpId: string option
      Challenge: string option
      Flags: byte option
      Counter: uint32 option
      UserHandle: byte array option
      CredentialId: byte array option
      SignWith: byte array option
      Type: string option }

    static member None =
        { Origin = None
          RpId = None
          Challenge = None
          Flags = None
          Counter = None
          UserHandle = None
          CredentialId = None
          SignWith = None
          Type = None }

/// What the authenticator remembers of one registration.
type Credential =
    { Id: byte array
      Key: ECDsa
      UserHandle: byte array
      mutable Counter: uint32 }

let private present (value: 'T | null) : 'T =
    match value with
    | null -> failwith "unexpected null"
    | v -> v

let private sha256 (text: string) = SHA256.HashData(Encoding.UTF8.GetBytes text)

let private padTo32 (bytes: byte array) =
    if bytes.Length >= 32 then bytes else Array.append (Array.zeroCreate (32 - bytes.Length)) bytes

let private bigEndian32 (value: uint32) =
    [| byte (value >>> 24); byte (value >>> 16); byte (value >>> 8); byte value |]

let private cose (key: ECDsa) : byte array =
    let q = key.ExportParameters(false).Q
    let writer = CborWriter()
    writer.WriteStartMap 5
    writer.WriteInt32 1
    writer.WriteInt32 2
    writer.WriteInt32 3
    writer.WriteInt32 -7
    writer.WriteInt32 -1
    writer.WriteInt32 1
    writer.WriteInt32 -2
    writer.WriteByteString(padTo32 (present q.X))
    writer.WriteInt32 -3
    writer.WriteByteString(padTo32 (present q.Y))
    writer.WriteEndMap()
    writer.Encode()

let private clientData (kind: string) (challenge: string) (origin: string) : byte array =
    let node = JsonObject()
    node["type"] <- JsonValue.Create kind
    node["challenge"] <- JsonValue.Create challenge
    node["origin"] <- JsonValue.Create origin
    Encoding.UTF8.GetBytes(node.ToJsonString())

let private parse (node: JsonNode) : JsonElement =
    JsonDocument.Parse(node.ToJsonString()).RootElement

type SoftwareAuthenticator(rpId: string, origin: string) =
    let credentials = Collections.Generic.List<Credential>()

    member _.Credentials: Credential list = List.ofSeq credentials

    member _.Register(options: JsonElement, ?overrides: RegisterOverrides) : JsonElement =
        let o = defaultArg overrides RegisterOverrides.None
        let challenge = o.Challenge |> Option.defaultValue (present (options.GetProperty("challenge").GetString()))
        let userId = fromBase64Url (present (options.GetProperty("user").GetProperty("id").GetString()))
        let key = ECDsa.Create ECCurve.NamedCurves.nistP256
        let id = Array.zeroCreate<byte> 16
        RandomNumberGenerator.Fill id
        credentials.Add { Id = id; Key = key; UserHandle = userId; Counter = o.Counter |> Option.defaultValue 0u }

        let shownId = o.CredentialId |> Option.defaultValue id
        let flags = o.Flags |> Option.defaultValue 0x45uy
        let idLength = [| byte (shownId.Length >>> 8); byte shownId.Length |]

        let authData =
            Array.concat
                [ sha256 (o.RpId |> Option.defaultValue rpId)
                  [| flags |]
                  bigEndian32 (o.Counter |> Option.defaultValue 0u)
                  Array.zeroCreate 16
                  idLength
                  shownId
                  cose key ]

        let attestation = CborWriter()
        attestation.WriteStartMap 3
        attestation.WriteTextString "fmt"
        attestation.WriteTextString "none"
        attestation.WriteTextString "attStmt"
        attestation.WriteStartMap 0
        attestation.WriteEndMap()
        attestation.WriteTextString "authData"
        attestation.WriteByteString authData
        attestation.WriteEndMap()

        let data =
            clientData
                (o.Type |> Option.defaultValue "webauthn.create")
                challenge
                (o.Origin |> Option.defaultValue origin)

        let response = JsonObject()
        response["attestationObject"] <- JsonValue.Create(toBase64Url (attestation.Encode()))
        response["clientDataJSON"] <- JsonValue.Create(toBase64Url data)
        response["transports"] <- JsonArray(JsonValue.Create "internal")

        let credProps = JsonObject()
        credProps["rk"] <- JsonValue.Create(o.Rk |> Option.defaultValue true)
        let extensions = JsonObject()
        extensions["credProps"] <- credProps

        let result = JsonObject()
        result["id"] <- JsonValue.Create(toBase64Url shownId)
        result["rawId"] <- JsonValue.Create(toBase64Url shownId)
        result["type"] <- JsonValue.Create "public-key"
        result["authenticatorAttachment"] <- JsonValue.Create "platform"
        result["clientExtensionResults"] <- extensions
        result["response"] <- response
        parse result

    member _.Assert(options: JsonElement, credentialId: byte array, ?overrides: AssertOverrides) : JsonElement =
        let o = defaultArg overrides AssertOverrides.None
        let challenge = o.Challenge |> Option.defaultValue (present (options.GetProperty("challenge").GetString()))
        let find (id: byte array) = credentials |> Seq.find (fun c -> c.Id = id)
        let credential = find credentialId
        let signer = o.SignWith |> Option.map find |> Option.defaultValue credential

        let counter =
            match o.Counter with
            | Some value -> value
            | None ->
                credential.Counter <- credential.Counter + 1u
                credential.Counter

        let authData =
            Array.concat
                [ sha256 (o.RpId |> Option.defaultValue rpId)
                  [| o.Flags |> Option.defaultValue 0x05uy |]
                  bigEndian32 counter ]

        let data =
            clientData (o.Type |> Option.defaultValue "webauthn.get") challenge (o.Origin |> Option.defaultValue origin)

        let signature =
            signer.Key.SignData(
                Array.append authData (SHA256.HashData data),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence
            )

        let shownId = o.CredentialId |> Option.defaultValue credential.Id
        let response = JsonObject()
        response["authenticatorData"] <- JsonValue.Create(toBase64Url authData)
        response["clientDataJSON"] <- JsonValue.Create(toBase64Url data)
        response["signature"] <- JsonValue.Create(toBase64Url signature)
        response["userHandle"] <- JsonValue.Create(toBase64Url (o.UserHandle |> Option.defaultValue credential.UserHandle))

        let result = JsonObject()
        result["id"] <- JsonValue.Create(toBase64Url shownId)
        result["rawId"] <- JsonValue.Create(toBase64Url shownId)
        result["type"] <- JsonValue.Create "public-key"
        result["authenticatorAttachment"] <- JsonValue.Create "platform"
        result["clientExtensionResults"] <- JsonObject()
        result["response"] <- response
        parse result
