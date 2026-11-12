/// Any OpenID Connect provider, configured by its issuer URL alone.
///
/// Where Google's adapter trusts the `id_token` because it arrives straight from
/// Google's token endpoint over TLS (and says so at length), this one cannot: the
/// provider is whatever the operator typed, and a misrouted or proxied token
/// endpoint must end in a refusal, not in a trusted identity. So the `id_token`
/// is validated in full against the provider's published keys, by
/// Microsoft.IdentityModel and not by code of ours (constitution III) — Umbral
/// only compares what is string equality: `nonce`, `azp` and the userinfo `sub`.
///
/// What the provider says about itself — endpoints, keys, supported client
/// authentication — comes from `OidcDiscovery`, never from the connector row.
module Umbral.Infrastructure.Providers.Oidc

open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.IdentityModel.JsonWebTokens
open Microsoft.IdentityModel.Protocols.OpenIdConnect
open Microsoft.IdentityModel.Tokens
open Umbral.Domains.Connectors
open Umbral.Infrastructure.OidcDiscovery
open Umbral.Infrastructure.ProviderAdapters

[<Literal>]
let private MaxResponseBytes = 1_000_000

let private requestTimeout = TimeSpan.FromSeconds 10.0

/// How far a provider's clock may differ from ours before a fresh token reads as
/// expired or not yet valid (research R4). The reason is recorded, so an operator
/// whose provider runs fast sees "expired" and knows where to look.
let private clockSkew = TimeSpan.FromMinutes 2.0

/// A string claim, absent when it is missing, not a string or empty. The
/// Microsoft adapter reads its claims with it too.
let optString (element: JsonElement) (name: string) : string option =
    match element.TryGetProperty name with
    | true, prop when prop.ValueKind = JsonValueKind.String ->
        Option.ofObj (prop.GetString()) |> Option.filter (fun s -> s <> "")
    | _ -> None

/// Only the JSON literal `true` counts (FR-018, research R6). The string
/// `"true"`, which some providers send, is NOT a verified address: linking
/// accounts on it would let one person claim another's email.
let isJsonTrue (element: JsonElement) (name: string) =
    match element.TryGetProperty name with
    | true, prop -> prop.ValueKind = JsonValueKind.True
    | _ -> false

/// A provider is untrusted input: reading its body without a cap turns a 200
/// OK into a memory-exhaustion vector.
let private readBounded (content: HttpContent) : Task<string> =
    task {
        use! stream = content.ReadAsStreamAsync()
        use buffer = new MemoryStream()
        let chunk = Array.zeroCreate<byte> 8192
        let mutable readCount = 1

        while readCount > 0 do
            let! n = stream.ReadAsync(chunk, 0, chunk.Length)
            readCount <- n

            if n > 0 then
                if buffer.Length + int64 n > int64 MaxResponseBytes then
                    failwith "response exceeded the size cap"

                buffer.Write(chunk, 0, n)

        return Encoding.UTF8.GetString(buffer.ToArray())
    }

/// Discovery's verdict as a connector error. The Apple adapter uses it too.
let mapDiscovery (result: DiscoveryResult) : Result<OpenIdConnectConfiguration, ConnectorError> =
    match result with
    | Discovered configuration -> Ok configuration
    | Unreachable reason -> Error(ProviderUnreachable reason)
    | Wrong reason -> Error(MalformedResponse reason)

let private listed (values: Collections.Generic.ICollection<string>) =
    values |> Seq.filter (fun v -> not (String.IsNullOrEmpty v)) |> List.ofSeq

/// PKCE is sent unless the document lists methods and `S256` is not among them
/// (research R5). An absent list says nothing, and a conforming authorization
/// server ignores a parameter it does not know (RFC 6749 §3.1), so sending the
/// challenge is the safe reading of silence. The Apple adapter uses it too.
let offersPkce (configuration: OpenIdConnectConfiguration) =
    match listed configuration.CodeChallengeMethodsSupported with
    | [] -> true
    | methods -> List.contains "S256" methods

/// `client_secret_basic` is the default of the specification when the document
/// does not say; `client_secret_post` only when basic is excluded (research R5).
/// Discovery has already refused a document that offers neither.
let private usesBasicAuthentication (configuration: OpenIdConnectConfiguration) =
    match listed configuration.TokenEndpointAuthMethodsSupported with
    | [] -> true
    | methods -> List.contains "client_secret_basic" methods

/// What the token endpoint answered with. The Apple adapter uses it too.
type TokenResponse =
    { AccessToken: string
      IdToken: string option }

/// RFC 6749 §2.3.1: the id and secret are form-encoded before they are joined
/// and base64'd, so a secret with a `:` or `%` in it still reaches the provider
/// intact.
let private basicHeader (clientId: string) (clientSecret: string) =
    let credentials = $"{Uri.EscapeDataString clientId}:{Uri.EscapeDataString clientSecret}"
    AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes credentials))

/// The token request, with the client authentication the provider's discovery
/// document asks for. The Apple adapter uses it too, with a signed client secret
/// in place of the stored one.
let exchangeCode
    (http: HttpClient)
    (configuration: OpenIdConnectConfiguration)
    (request: ExchangeRequest)
    : Task<Result<TokenResponse, ConnectorError>> =
    task {
        use cts = new CancellationTokenSource(requestTimeout)
        use httpRequest = new HttpRequestMessage(HttpMethod.Post, configuration.TokenEndpoint)

        let basic = usesBasicAuthentication configuration

        let fields =
            [ "grant_type", "authorization_code"
              "code", request.Code
              "redirect_uri", request.RedirectUri.ToString()
              // Sent exactly when the authorize URL carried the challenge: both
              // sides read the same document, so they agree.
              if offersPkce configuration then
                  "code_verifier", request.CodeVerifier
              if not basic then
                  "client_id", request.Connector.ClientId
                  "client_secret", request.ClientSecret ]

        httpRequest.Content <-
            new FormUrlEncodedContent(fields |> List.map (fun (k, v) -> Collections.Generic.KeyValuePair(k, v)))

        if basic then
            httpRequest.Headers.Authorization <- basicHeader request.Connector.ClientId request.ClientSecret

        // Redirects are never followed: the named client's handler sets
        // AllowAutoRedirect = false, so an authorization code is not walked
        // anywhere a token endpoint chose to send it.
        use! response = http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token)
        let! body = readBounded response.Content

        let parsed =
            try
                Some(JsonDocument.Parse body)
            with :? JsonException ->
                None

        match parsed with
        // An error page where the token response should be: a provider that is
        // down (5xx) or refusing us (4xx), not one that sent a bad identity.
        | None when not response.IsSuccessStatusCode ->
            if int response.StatusCode >= 500 then
                return Error(ProviderUnreachable $"the token endpoint answered with status {int response.StatusCode}")
            else
                return Error(ProviderRejected $"token request failed with status {int response.StatusCode}")
        | None -> return Error(MalformedResponse "token response was not JSON")
        | Some doc ->
            use doc = doc
            let root = doc.RootElement

            if root.ValueKind <> JsonValueKind.Object then
                return Error(MalformedResponse "token response was not a JSON object")
            else

            match optString root "error" with
            | Some error -> return Error(ProviderRejected error)
            | None ->
                if not response.IsSuccessStatusCode then
                    return Error(ProviderRejected $"token request failed with status {int response.StatusCode}")
                else
                    match optString root "access_token" with
                    | None -> return Error(MalformedResponse "token response had no access_token")
                    | Some accessToken -> return Ok { AccessToken = accessToken; IdToken = optString root "id_token" }
    }

/// What the validated `id_token` says. Read from its payload after the signature
/// and the standard claims have been checked, as the JSON it arrived as, so that
/// `email_verified` keeps its type.
type private Identity =
    { Subject: string
      Email: string option
      EmailVerified: bool
      Name: string option
      Picture: string option }

let private identityOf (payload: JsonElement) (subject: string) : Identity =
    { Subject = subject
      Email = optString payload "email"
      EmailVerified = isJsonTrue payload "email_verified"
      Name = optString payload "name" |> Option.orElse (optString payload "preferred_username")
      Picture = optString payload "picture" }

/// Why an `id_token` was refused, as the security log records it (the contract's
/// "Reason recorded" column). Only these words: a token, a claim value or the
/// library's own message may carry the token's contents.
let private invalid (reason: string) = Error(MalformedResponse $"id_token_invalid: {reason}")

/// The library's verdict, in the contract's words.
let private reasonFor (error: exn | null) =
    match error with
    | :? SecurityTokenSignatureKeyNotFoundException
    | :? SecurityTokenInvalidSignatureException -> "signature"
    | :? SecurityTokenInvalidAlgorithmException -> "algorithm"
    | :? SecurityTokenInvalidIssuerException -> "issuer"
    | :? SecurityTokenInvalidAudienceException -> "audience"
    | :? SecurityTokenExpiredException
    | :? SecurityTokenNoExpirationException -> "expired"
    | :? SecurityTokenNotYetValidException -> "not_yet_valid"
    | _ -> "malformed"

/// The settings every `id_token` is held to (research R4).
let private validationParameters
    (time: TimeProvider)
    (clientId: string)
    (validIssuers: string list)
    (keys: SecurityKey seq)
    =
    let parameters = TokenValidationParameters()
    // Which `iss` values are the provider's is the caller's to say: the OIDC
    // connector's stored issuer with and without a trailing slash, since
    // providers differ on whether the claim has one and discovery accepted
    // either (research R2); Microsoft's, exactly one string built from the
    // token's own tenant (specs/016 research R2).
    parameters.ValidIssuers <- validIssuers
    parameters.ValidateIssuer <- true
    parameters.ValidAudience <- clientId
    parameters.ValidateAudience <- true
    parameters.IssuerSigningKeys <- keys
    parameters.ValidateIssuerSigningKey <- true
    parameters.RequireSignedTokens <- true
    // Asymmetric only: `none` and the `HS*` family are refused (FR-012).
    parameters.ValidAlgorithms <- asymmetricAlgorithms
    parameters.ValidateLifetime <- true
    parameters.RequireExpirationTime <- true
    parameters.ClockSkew <- clockSkew

    // Umbral's clock, so a test that moves it moves token lifetimes with it:
    // the library's own check reads the system time, and its `TimeProvider`
    // setting is not public. The rule is the library's — expiry and start
    // judged with the skew, the exceptions it would raise — only the clock
    // is ours.
    parameters.LifetimeValidator <-
        LifetimeValidator(fun notBefore expires _ _ ->
            let now = time.GetUtcNow().UtcDateTime

            if not expires.HasValue then
                raise (SecurityTokenNoExpirationException "the id_token has no expiry")
            elif notBefore.HasValue && notBefore.Value > now + clockSkew then
                raise (SecurityTokenNotYetValidException "the id_token is not valid yet")
            elif expires.Value < now - clockSkew then
                raise (SecurityTokenExpiredException "the id_token has expired")
            else
                true)

    parameters

/// A validated `id_token`: the payload as the JSON it arrived as, the parsed token
/// (header, `kid`, `iss`), and the configuration whose keys judged it, which is
/// the fresher one when the keys had to be fetched again.
type IdTokenValidated =
    { Payload: JsonElement
      Token: JsonWebToken
      Configuration: OpenIdConnectConfiguration }

/// Validate an `id_token` against the provider's published keys, once the
/// signature, the standard claims, `nonce`, `azp` and a present `sub` have been
/// checked, and return it with the configuration that was used.
///
/// `validIssuers` are the `iss` values the library accepts: the caller's rule.
/// `refresh` is how the keys are fetched again, and it is the caller's too, since
/// where a provider's document lives is not always its issuer.
///
/// The signing keys come from `configuration`, the discovery cache's. A token
/// whose `kid` the cache has never seen means the provider rotated its keys
/// (FR-013): `refresh` is asked once — the cache throttles it to one forced
/// refresh per document per five minutes — and the token is judged against what
/// that returns. A `kid` that is still unknown then is a signature failure.
let validateIdTokenWith
    (time: TimeProvider)
    (clientId: string)
    (validIssuers: string list)
    (refresh: unit -> Task<DiscoveryResult>)
    (configuration: OpenIdConnectConfiguration)
    (idToken: string)
    (expectedNonce: string)
    : Task<Result<IdTokenValidated, ConnectorError>> =
    task {
        // Reading the header is not trusting it: it only picks the keys and
        // lets the two cheap refusals (`alg`, `kid`) happen before any
        // cryptography. The signature is still the library's to check.
        let token =
            try
                Some(JsonWebToken idToken)
            with :? ArgumentException ->
                None

        match token with
        | None -> return invalid "malformed"
        | Some token ->

        if not (List.contains token.Alg asymmetricAlgorithms) then
            return invalid "algorithm"
        else

        let! keysResult =
            task {
                let known =
                    String.IsNullOrEmpty token.Kid
                    || configuration.SigningKeys |> Seq.exists (fun key -> key.KeyId = token.Kid)

                if known then
                    return Ok configuration
                else
                    let! refreshed = refresh ()
                    return mapDiscovery refreshed
            }

        match keysResult with
        | Error error -> return Error error
        | Ok configuration ->

        let! result =
            JsonWebTokenHandler()
                .ValidateTokenAsync(idToken, validationParameters time clientId validIssuers configuration.SigningKeys)

        if not result.IsValid then
            return invalid (reasonFor result.Exception)
        else
            use doc = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes token.EncodedPayload))
            let payload = doc.RootElement

            let audiences =
                match payload.TryGetProperty "aud" with
                | true, aud when aud.ValueKind = JsonValueKind.Array ->
                    aud.EnumerateArray()
                    |> Seq.choose (fun a -> if a.ValueKind = JsonValueKind.String then Option.ofObj (a.GetString()) else None)
                    |> List.ofSeq
                | _ -> []

            if optString payload "nonce" <> Some expectedNonce then
                return invalid "nonce"
            // OIDC Core §3.1.3.7: with several audiences the token must say
            // which party it was issued to, and it must be this client.
            elif audiences.Length > 1 && optString payload "azp" <> Some clientId then
                return invalid "audience"
            else
                match optString payload "sub" with
                | None -> return invalid "subject"
                // Cloned: the document is disposed when this block ends, and the
                // caller reads the payload after it.
                | Some _ ->
                    return
                        Ok
                            { Payload = payload.Clone()
                              Token = token
                              Configuration = configuration }
    }

/// `validateIdTokenWith` for a provider whose document is at its issuer and whose
/// `iss` is that issuer with or without a trailing slash: the OIDC connector's
/// case, and Apple's. Returns the payload. The Apple adapter uses it too.
let validateIdToken
    (discovery: OidcDiscovery)
    (time: TimeProvider)
    (clientId: string)
    (issuer: string)
    (configuration: OpenIdConnectConfiguration)
    (idToken: string)
    (expectedNonce: string)
    : Task<Result<JsonElement, ConnectorError>> =
    task {
        // The issuer is stored without a trailing slash; providers differ on
        // whether the claim has one, and discovery accepted either (research R2).
        let! validated =
            validateIdTokenWith
                time
                clientId
                [ issuer; issuer + "/" ]
                (fun () -> discovery.Refresh issuer)
                configuration
                idToken
                expectedNonce

        return validated |> Result.map (fun v -> v.Payload)
    }

type OidcAdapter(httpClient: HttpClient, discovery: OidcDiscovery, time: TimeProvider) =

    /// Validate the `id_token` and read the identity from its payload.
    member private _.Validate
        (connector: Connector)
        (issuer: string)
        (configuration: OpenIdConnectConfiguration)
        (idToken: string)
        (expectedNonce: string)
        : Task<Result<Identity, ConnectorError>> =
        task {
            match! validateIdToken discovery time connector.ClientId issuer configuration idToken expectedNonce with
            | Error error -> return Error error
            | Ok payload ->
                // `validateIdToken` has checked that `sub` is a present string.
                return Ok(identityOf payload (optString payload "sub" |> Option.defaultValue ""))
        }

    /// Fill what the `id_token` left out from the userinfo endpoint, and refuse
    /// the sign-in if it describes someone else (FR-014, OIDC Core §5.3.2).
    ///
    /// Email and its verified flag travel together: a flag from one source is
    /// never attached to an address from the other. A userinfo call that fails
    /// leaves the gaps as they are — the identity was already proven by the
    /// `id_token`, and a person with no email is still a person — but one that
    /// answers about a different subject is an attack or a misconfiguration and
    /// refuses the sign-in.
    member private _.FillFromUserInfo
        (configuration: OpenIdConnectConfiguration)
        (accessToken: string)
        (identity: Identity)
        : Task<Result<Identity, ConnectorError>> =
        task {
            let missingEmail = identity.Email.IsNone
            let missingName = identity.Name.IsNone

            if String.IsNullOrEmpty configuration.UserInfoEndpoint || not (missingEmail || missingName) then
                return Ok identity
            else
                use cts = new CancellationTokenSource(requestTimeout)
                use httpRequest = new HttpRequestMessage(HttpMethod.Get, configuration.UserInfoEndpoint)
                httpRequest.Headers.Authorization <- AuthenticationHeaderValue("Bearer", accessToken)

                use! response = httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token)

                if not response.IsSuccessStatusCode then
                    return Ok identity
                else
                    let! body = readBounded response.Content

                    let parsed =
                        try
                            Some(JsonDocument.Parse body)
                        with :? JsonException ->
                            None

                    match parsed with
                    | None -> return Ok identity
                    | Some doc ->
                        use doc = doc
                        let info = doc.RootElement

                        if info.ValueKind <> JsonValueKind.Object then
                            return Ok identity
                        elif optString info "sub" <> Some identity.Subject then
                            return Error(MalformedResponse "userinfo_subject_mismatch")
                        else
                            let fromInfo = identityOf info identity.Subject

                            return
                                Ok
                                    { identity with
                                        Email = if missingEmail then fromInfo.Email else identity.Email
                                        EmailVerified =
                                            if missingEmail && fromInfo.Email.IsSome then fromInfo.EmailVerified
                                            else identity.EmailVerified
                                        Name = if missingName then fromInfo.Name else identity.Name
                                        Picture = identity.Picture |> Option.orElse fromInfo.Picture }
        }

    interface IProviderAdapter with
        member _.Kind = Oidc

        /// The authorization endpoint (and whether to send PKCE) is the
        /// provider's to say, from its cached discovery document.
        member _.Resolve connector =
            task {
                match connector.IssuerUrl with
                | None -> return Error(MalformedResponse "the connector has no issuer")
                | Some issuer ->
                    let! discovered = discovery.Check issuer

                    return
                        mapDiscovery discovered
                        |> Result.map (fun configuration ->
                            { endpointsFor Oidc with
                                AuthorizationUrl = configuration.AuthorizationEndpoint
                                TokenUrl = configuration.TokenEndpoint
                                UserInfoUrl = configuration.UserInfoEndpoint |> Option.ofObj |> Option.defaultValue ""
                                SupportsPkce = offersPkce configuration })
            }

        member _.BuildAuthorizeUrl(request: AuthorizeRequest) =
            buildUrl request.Endpoints.AuthorizationUrl (commonAuthorizeParameters request)

        member this.ExchangeAndFetch(request: ExchangeRequest) =
            task {
                try
                    match request.Connector.IssuerUrl with
                    | None -> return Error(MalformedResponse "the connector has no issuer")
                    | Some issuer ->

                    let! discovered = discovery.Check issuer

                    match mapDiscovery discovered with
                    | Error error -> return Error error
                    | Ok configuration ->

                    match! exchangeCode httpClient configuration request with
                    | Error error -> return Error error
                    | Ok tokens ->

                    match tokens.IdToken with
                    | None -> return Error(MalformedResponse "id_token_missing")
                    | Some idToken ->

                    match! this.Validate request.Connector issuer configuration idToken request.Nonce with
                    | Error error -> return Error error
                    | Ok identity ->

                    match! this.FillFromUserInfo configuration tokens.AccessToken identity with
                    | Error error -> return Error error
                    | Ok identity ->
                        return
                            Ok
                                { Subject = oidcSubject issuer identity.Subject
                                  Email = identity.Email
                                  EmailVerified = identity.EmailVerified
                                  DisplayName = identity.Name
                                  AvatarUrl = identity.Picture
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
