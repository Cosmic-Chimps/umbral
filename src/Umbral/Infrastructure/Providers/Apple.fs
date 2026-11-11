/// Sign in with Apple.
///
/// Apple is an OpenID Connect provider with one fixed issuer, so almost all of
/// this adapter is the OIDC adapter's code, reused from `Oidc.fs`: discovery and
/// its key cache, the token request, and the validation of the `id_token` by
/// Microsoft.IdentityModel. What is Apple's own is four things:
///
/// - the client secret is not a string the operator keeps but a short-lived ES256
///   JWT that Umbral signs with the operator's `.p8` key for every token request;
/// - the answer comes back as a cross-site form POST (`response_mode=form_post`),
///   which the round trip handles and hands over as `PostedUser`;
/// - the person's name is in that form's `user` field, once, and nowhere in the
///   token;
/// - `email_verified` and `is_private_email` are "a string or Boolean value".
///
/// The key, the signed client secret, the code and the tokens never go into an
/// error value, a log or an exception message from here (FR-010).
module Umbral.Infrastructure.Providers.Apple

open System
open System.Collections.Generic
open System.Net.Http
open System.Security.Cryptography
open System.Text.Json
open System.Threading.Tasks
open Microsoft.IdentityModel.JsonWebTokens
open Microsoft.IdentityModel.Tokens
open Umbral.Domains.Connectors
open Umbral.Infrastructure.OidcDiscovery
open Umbral.Infrastructure.ProviderAdapters

// ---------------------------------------------------------------------------
// The key and the client secret
// ---------------------------------------------------------------------------

/// Import a PEM into an elliptic-curve key, or None when it is not one on the
/// P-256 curve with its private half. The caller owns the key and disposes it.
/// No exception escapes and none of its text is used: what was pasted may be a
/// private key, and an exception's message is free to quote it.
let private importKey (pem: string) : ECDsa option =
    let ecdsa = ECDsa.Create()

    try
        try
            ecdsa.ImportFromPem pem

            if ecdsa.KeySize <> 256 then
                ecdsa.Dispose()
                None
            else
                // A public key imports too; asking for the private parameters
                // fails for it, and that is the refusal we want.
                ecdsa.ExportParameters true |> ignore
                Some ecdsa
        with _ ->
            ecdsa.Dispose()
            None
    with _ ->
        None

/// The `.p8` key as the operator pasted it: trimmed, with `\r\n` and `\r` turned
/// into `\n`, and only when it imports as an elliptic-curve private key on the
/// P-256 curve. Anything else (nothing, text that is not PEM, an RSA key, another
/// curve, a public key, an encrypted key) is None.
///
/// The refusal says nothing of what was pasted, because what was pasted may be a
/// private key: no part of the input and no text of an exception leaves here.
let normaliseKey (input: string | null) : string option =
    match Option.ofObj input with
    | None -> None
    | Some text ->
        let normalised = text.Trim().Replace("\r\n", "\n").Replace("\r", "\n")

        if normalised = "" then
            None
        else
            match importKey normalised with
            | Some key ->
                key.Dispose()
                Some normalised
            | None -> None

/// How long a client secret is good for. Apple allows six months, which is why
/// other tools ask the operator to generate a token and renew it twice a year
/// (research R4). Umbral holds the key, so it signs one per token request and
/// the token is useless a few minutes later.
let clientSecretLifetime = TimeSpan.FromMinutes 5.0

/// The client secret of FR-009: an ES256 JWT with header `kid` the Key ID and
/// claims `iss` the Team ID, `sub` the Services ID, `aud` Apple's issuer, `iat`
/// now and `exp` five minutes later, and no other claim. None when the key cannot
/// be imported or cannot sign.
///
/// Signed by Microsoft.IdentityModel and not by code of ours (constitution III).
/// The library adds `nbf` by default; with that turned off and `iat` and `exp`
/// given by hand the payload has exactly the documented claims.
let clientSecret (time: TimeProvider) (keyIds: AppleKeyIds) (servicesId: string) (pem: string) : string option =
    match importKey pem with
    | None -> None
    | Some ecdsa ->
        use ecdsa = ecdsa

        try
            let now = time.GetUtcNow().UtcDateTime

            let key = ECDsaSecurityKey(ecdsa)
            key.KeyId <- keyIds.KeyId

            // The key lives for this one signature. The library keeps signature
            // providers in a cache keyed by key id by default, and a cached
            // provider would hold this key after it is disposed and fail the next
            // call with `ObjectDisposedException`: this key gets a factory that
            // caches nothing.
            key.CryptoProviderFactory <- CryptoProviderFactory(CacheSignatureProviders = false)

            let claims = Dictionary<string, objnull>()
            claims["sub"] <- box servicesId

            let descriptor =
                SecurityTokenDescriptor(
                    Issuer = keyIds.TeamId,
                    Audience = appleIssuer,
                    Claims = claims,
                    IssuedAt = Nullable now,
                    Expires = Nullable(now + clientSecretLifetime),
                    SigningCredentials = SigningCredentials(key, SecurityAlgorithms.EcdsaSha256)
                )

            let handler = JsonWebTokenHandler(SetDefaultTimesOnTokenCreation = false)
            Some(handler.CreateToken descriptor)
        with _ ->
            // Nothing of the exception: it may describe the key.
            None

// ---------------------------------------------------------------------------
// What the answer says
// ---------------------------------------------------------------------------

/// Whether a claim is true, as Apple documents it: `email_verified` and
/// `is_private_email` are "a string or Boolean value … either a string ("true" or
/// "false") or a Boolean (true or false)". That is why this adapter, and only
/// this one, accepts the string; the generic OpenID Connect adapter keeps
/// refusing it (specs/002 FR-018).
///
/// True only for the JSON literal `true` and the JSON string exactly `"true"`,
/// case-sensitive and untrimmed. Every other value, type or absence is false.
/// Account linking rests on this claim (task h762), so it is small.
let claimIsTrue (payload: JsonElement) (name: string) : bool =
    if payload.ValueKind <> JsonValueKind.Object then
        false
    else
        match payload.TryGetProperty name with
        | true, value ->
            match value.ValueKind with
            | JsonValueKind.True -> true
            | JsonValueKind.String -> value.GetString() = "true"
            | _ -> false
        | _ -> false

let private maxPostedUserLength = 8192
let private maxNameLength = 200

let private nameParts (user: JsonElement) : string list =
    if user.ValueKind <> JsonValueKind.Object then
        []
    else
        match user.TryGetProperty "name" with
        | true, name when name.ValueKind = JsonValueKind.Object ->
            [ "firstName"; "lastName" ]
            |> List.choose (fun field ->
                match name.TryGetProperty field with
                | true, part when part.ValueKind = JsonValueKind.String ->
                    let text = part.GetString() |> Option.ofObj |> Option.defaultValue ""
                    let cleaned = String(text.ToCharArray() |> Array.filter (fun c -> not (Char.IsControl c)))
                    Some(cleaned.Trim())
                | _ -> None)
            |> List.filter (fun part -> part <> "")
        | _ -> []

/// The display name from the form post's `user` field, JSON like
/// `{"name":{"firstName":"Maria","lastName":"Ruiz"},"email":"..."}`. Only
/// `name.firstName` and `name.lastName` are read; control characters are dropped,
/// the two are joined with one space and the result is cut to 200 characters.
///
/// Nothing signs this field, so it gives a display name and nothing else
/// (research R3 point 4): its `email` is never read. None when nothing usable is
/// there, and None leaves a stored name as it is (research R7).
let nameOf (postedUser: string option) : string option =
    match postedUser with
    | None -> None
    | Some text when text.Length > maxPostedUserLength -> None
    | Some text ->
        let parsed =
            try
                Some(JsonDocument.Parse text)
            with :? JsonException ->
                None

        match parsed with
        | None -> None
        | Some doc ->
            use doc = doc

            match nameParts doc.RootElement with
            | [] -> None
            | parts ->
                let joined = String.concat " " parts

                let cut =
                    if joined.Length <= maxNameLength then
                        joined
                    else
                        // Not in the middle of a surrogate pair.
                        let length = if Char.IsHighSurrogate joined[maxNameLength - 1] then maxNameLength - 1 else maxNameLength
                        joined.Substring(0, length).TrimEnd()

                if cut = "" then None else Some cut

// ---------------------------------------------------------------------------
// The admin check
// ---------------------------------------------------------------------------

type CheckResult =
    /// The key signs and Apple's discovery document and keys were fetched.
    | Ready
    /// The key cannot be imported or cannot sign.
    | KeyUnusable
    /// Apple's discovery document or keys could not be fetched or are not usable; the reason is for the operator.
    | AppleUnreachable of reason: string

/// What the admin screen runs before it enables the connector: sign first, which
/// is local and so says "the key is wrong" without a network call, then ask Apple
/// for its discovery document and keys. The signed secret goes nowhere: Apple
/// cannot be asked whether it accepts it without a real code.
let check
    (discovery: OidcDiscovery)
    (time: TimeProvider)
    (keyIds: AppleKeyIds)
    (servicesId: string)
    (pem: string)
    : Task<CheckResult> =
    task {
        match clientSecret time keyIds servicesId pem with
        | None -> return KeyUnusable
        | Some _ ->
            match! discovery.Check appleIssuer with
            | Discovered _ -> return Ready
            | Unreachable reason
            | Wrong reason -> return AppleUnreachable reason
    }

// ---------------------------------------------------------------------------
// The adapter
// ---------------------------------------------------------------------------

/// What the validated `id_token` and the form post say, before they are shaped
/// into a profile.
type private AppleIdentity =
    { Subject: string
      Email: string option
      EmailVerified: bool
      /// Read so the claim is accounted for, and acted on nowhere: a relay address
      /// is a verified address like any other (spec Clarifications).
      IsPrivateEmail: bool
      Name: string option }

let private nonBlankString (payload: JsonElement) (name: string) : string option =
    match payload.TryGetProperty name with
    | true, value when value.ValueKind = JsonValueKind.String ->
        value.GetString() |> Option.ofObj |> Option.filter (fun s -> not (String.IsNullOrWhiteSpace s))
    | _ -> None

let private identityOf (payload: JsonElement) (postedUser: string option) : AppleIdentity option =
    match nonBlankString payload "sub" with
    | None -> None
    | Some subject ->
        let email = nonBlankString payload "email"

        Some
            { Subject = subject
              Email = email
              // Without an address there is nothing for the flag to vouch for.
              EmailVerified = email.IsSome && claimIsTrue payload "email_verified"
              IsPrivateEmail = claimIsTrue payload "is_private_email"
              Name = nameOf postedUser }

type AppleAdapter(httpClient: HttpClient, discovery: OidcDiscovery, time: TimeProvider) =

    interface IProviderAdapter with
        member _.Kind = Apple

        /// The endpoints are Apple's to say, from its cached discovery document.
        /// There is no userinfo endpoint.
        member _.Resolve _ =
            task {
                let! discovered = discovery.Check appleIssuer

                return
                    Oidc.mapDiscovery discovered
                    |> Result.map (fun configuration ->
                        { endpointsFor Apple with
                            AuthorizationUrl = configuration.AuthorizationEndpoint
                            TokenUrl = configuration.TokenEndpoint
                            SupportsPkce = Oidc.offersPkce configuration })
            }

        member _.BuildAuthorizeUrl(request: AuthorizeRequest) =
            // Apple requires `form_post` when any scope is asked for.
            buildUrl request.Endpoints.AuthorizationUrl (commonAuthorizeParameters request @ [ "response_mode", "form_post" ])

        member _.ExchangeAndFetch(request: ExchangeRequest) =
            task {
                try
                    match request.Connector.AppleKey with
                    | None -> return Error(MalformedResponse "apple_key_ids_missing")
                    | Some keyIds ->

                    match clientSecret time keyIds request.Connector.ClientId request.ClientSecret with
                    | None -> return Error(ProviderRejected "apple_key_unusable")
                    | Some signed ->

                    let! discovered = discovery.Check appleIssuer

                    match Oidc.mapDiscovery discovered with
                    | Error error -> return Error error
                    | Ok configuration ->

                    // Apple's document lists `client_secret_post` only, so the
                    // exchange sends the id and the signed secret in the form.
                    match! Oidc.exchangeCode httpClient configuration { request with ClientSecret = signed } with
                    | Error error -> return Error error
                    | Ok tokens ->

                    match tokens.IdToken with
                    | None -> return Error(MalformedResponse "id_token_missing")
                    | Some idToken ->

                    match! Oidc.validateIdToken discovery time request.Connector.ClientId appleIssuer configuration idToken request.Nonce with
                    | Error error -> return Error error
                    | Ok payload ->

                    match identityOf payload request.PostedUser with
                    | None -> return Error(MalformedResponse "id_token_invalid: subject")
                    | Some identity ->
                        return
                            Ok
                                { Subject = identity.Subject
                                  Email = identity.Email
                                  EmailVerified = identity.EmailVerified
                                  DisplayName = identity.Name
                                  AvatarUrl = None
                                  Username = None
                                  ProfileUrl = None }
                with
                | :? OperationCanceledException -> return Error(ProviderUnreachable "timed out")
                | :? HttpRequestException as ex -> return Error(ProviderUnreachable ex.Message)
                | :? JsonException -> return Error(MalformedResponse "the provider's answer was not valid JSON")
                // The size cap's own message; nothing else reaches here that is
                // safe to quote.
                | ex when ex.Message = "response exceeded the size cap" -> return Error(MalformedResponse ex.Message)
                | ex -> return Error(MalformedResponse(ex.GetType().Name))
            }
