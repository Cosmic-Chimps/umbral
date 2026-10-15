/// Google as an OIDC provider.
///
/// Signature verification of the `id_token` is deliberately skipped — see the
/// comment on `parseIdToken` below before "fixing" that.
module Umbral.Infrastructure.Providers.Google

open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Umbral.Domains.Connectors
open Umbral.Infrastructure.ProviderAdapters

[<Literal>]
let private UserInfoUrl = "https://openidconnect.googleapis.com/v1/userinfo"

[<Literal>]
let private MaxResponseBytes = 1_000_000

let private requestTimeout = TimeSpan.FromSeconds 10.0

/// Both spellings appear in the wild across Google's own documentation
/// versions and in real tokens. Hardcoding one is a real bug.
let private acceptedIssuers = set [ "https://accounts.google.com"; "accounts.google.com" ]

let private optString (element: JsonElement) (name: string) : string option =
    match element.TryGetProperty name with
    | true, prop when prop.ValueKind <> JsonValueKind.Null && prop.ValueKind <> JsonValueKind.Undefined ->
        Option.ofObj (prop.GetString())
    | _ -> None

let private optBool (element: JsonElement) (name: string) : bool option =
    match element.TryGetProperty name with
    | true, prop when prop.ValueKind = JsonValueKind.True || prop.ValueKind = JsonValueKind.False ->
        Some(prop.GetBoolean())
    | _ -> None

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

let private formContent (fields: (string * string) list) =
    new FormUrlEncodedContent(fields |> List.map (fun (k, v) -> Collections.Generic.KeyValuePair(k, v)))

/// RFC 7515 §2: the JWS alphabet is base64url, unpadded.
let private base64UrlDecode (segment: string) : byte[] =
    let rem = segment.Length % 4
    let padded = if rem = 0 then segment else segment + String('=', 4 - rem)
    Convert.FromBase64String(padded.Replace('-', '+').Replace('_', '/'))

type private IdTokenClaims =
    { Iss: string
      Aud: string
      Sub: string
      Exp: int64
      Nonce: string option }

/// Parses the `id_token`'s claims **without verifying its signature**.
///
/// This is intentional, not a shortcut. OIDC Core §3.1.3.7 permits relying on
/// TLS server authentication in place of signature validation when the
/// `id_token` arrives through the client's own direct call to the token
/// endpoint — exactly this back-channel POST, as opposed to the token
/// travelling through the front channel or a third party. The claims below
/// are still validated in full (`iss`, `aud`, `exp`, `nonce`, `sub`).
///
/// If this code is ever changed to accept an `id_token` from anywhere other
/// than this direct token-endpoint response — the authorize redirect, a
/// form post, a cached value — full signature verification against Google's
/// JWKS becomes mandatory. Do not drop the signature check without also
/// removing this exemption.
let private parseIdToken (idToken: string) : Result<IdTokenClaims, ConnectorError> =
    match idToken.Split '.' with
    | [| _header; payload; _signature |] ->
        try
            let json = Encoding.UTF8.GetString(base64UrlDecode payload)
            use doc = JsonDocument.Parse json
            let root = doc.RootElement

            match optString root "iss", optString root "sub" with
            | Some iss, Some sub when sub.Length > 0 ->
                let exp =
                    match root.TryGetProperty "exp" with
                    | true, prop when prop.ValueKind = JsonValueKind.Number -> prop.GetInt64()
                    | _ -> 0L

                Ok
                    { Iss = iss
                      Aud = optString root "aud" |> Option.defaultValue ""
                      Sub = sub
                      Exp = exp
                      Nonce = optString root "nonce" }
            | _ -> Error(MalformedResponse "id_token payload had no 'iss' or 'sub'")
        with
        | :? FormatException
        | :? JsonException -> Error(MalformedResponse "id_token payload was not valid base64url JSON")
    | _ -> Error(MalformedResponse "id_token was not a 3-part JWT")

let private validateClaims (connector: Connector) (expectedNonce: string) (claims: IdTokenClaims) : Result<unit, ConnectorError> =
    if not (acceptedIssuers.Contains claims.Iss) then
        Error(MalformedResponse "id_token had an unrecognised issuer")
    elif claims.Aud <> connector.ClientId then
        Error(MalformedResponse "id_token audience did not match our client id")
    elif claims.Exp <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() then
        Error(MalformedResponse "id_token had expired")
    elif claims.Nonce <> Some expectedNonce then
        Error(MalformedResponse "id_token nonce did not match the one we sent")
    else
        Ok()

type private TokenResponse =
    { AccessToken: string
      IdToken: string option }

/// `openid` is mandatory in the connector's scopes or no `id_token` comes
/// back at all; `nonce` is sent unconditionally since doing so is free and
/// Google's own documentation is inconsistent about when it is required.
let private exchangeCode (http: HttpClient) (request: ExchangeRequest) : Task<Result<TokenResponse, ConnectorError>> =
    task {
        use cts = new CancellationTokenSource(requestTimeout)
        use httpRequest = new HttpRequestMessage(HttpMethod.Post, request.Connector.Endpoints.TokenUrl)

        httpRequest.Content <-
            formContent
                [ "client_id", request.Connector.ClientId
                  "client_secret", request.ClientSecret
                  "grant_type", "authorization_code"
                  "code", request.Code
                  "redirect_uri", request.RedirectUri.ToString()
                  "code_verifier", request.CodeVerifier ]

        use! response = http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token)
        let! body = readBounded response.Content

        use doc = JsonDocument.Parse body
        let root = doc.RootElement

        match optString root "error" with
        | Some error -> return Error(ProviderRejected error)
        | None ->
            match optString root "access_token" with
            | None -> return Error(MalformedResponse "token response had no access_token")
            | Some accessToken -> return Ok { AccessToken = accessToken; IdToken = optString root "id_token" }
    }

type private UserInfo =
    { Sub: string
      Email: string option
      EmailVerified: bool
      Name: string option
      Picture: string option }

let private fetchUserInfo (http: HttpClient) (accessToken: string) : Task<Result<UserInfo, ConnectorError>> =
    task {
        use cts = new CancellationTokenSource(requestTimeout)
        use httpRequest = new HttpRequestMessage(HttpMethod.Get, UserInfoUrl)
        httpRequest.Headers.Authorization <- AuthenticationHeaderValue("Bearer", accessToken)

        use! response = http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token)
        let! body = readBounded response.Content

        if not response.IsSuccessStatusCode then
            return Error(ProviderRejected $"userinfo request failed with status {int response.StatusCode}")
        else
            use doc = JsonDocument.Parse body
            let root = doc.RootElement

            match optString root "sub" with
            | None -> return Error(MalformedResponse "userinfo response had no 'sub'")
            | Some sub ->
                return
                    Ok
                        { Sub = sub
                          Email = optString root "email"
                          // Google's own guidance: do not use `email` as an
                          // identifier, and always check this flag.
                          EmailVerified = optBool root "email_verified" |> Option.defaultValue false
                          Name = optString root "name"
                          Picture = optString root "picture" }
    }

type GoogleAdapter(httpClient: HttpClient) =
    interface IProviderAdapter with
        member _.Kind = Google

        member _.Resolve connector =
            Task.FromResult(Ok(endpointsFor connector.Kind))

        member _.BuildAuthorizeUrl(request: AuthorizeRequest) =
            // `hd` narrows the account chooser to one Google Workspace domain. It
            // is a convenience for the person; the check that matters is the
            // callback's.
            buildUrl
                request.Endpoints.AuthorizationUrl
                (commonAuthorizeParameters request
                 @ (request.HostedDomainHint |> Option.map (fun domain -> "hd", domain) |> Option.toList))

        member _.ExchangeAndFetch(request: ExchangeRequest) =
            task {
                try
                    match! exchangeCode httpClient request with
                    | Error e -> return Error e
                    | Ok tokenResponse ->
                        match tokenResponse.IdToken with
                        | None ->
                            return
                                Error(
                                    MalformedResponse
                                        "token response had no id_token; is the 'openid' scope configured?"
                                )
                        | Some idToken ->
                            match parseIdToken idToken with
                            | Error e -> return Error e
                            | Ok claims ->
                                match validateClaims request.Connector request.Nonce claims with
                                | Error e -> return Error e
                                | Ok() ->
                                    match! fetchUserInfo httpClient tokenResponse.AccessToken with
                                    | Error e -> return Error e
                                    | Ok info ->
                                        if info.Sub <> claims.Sub then
                                            return
                                                Error(
                                                    MalformedResponse
                                                        "userinfo subject did not match the id_token subject"
                                                )
                                        else
                                            return
                                                Ok
                                                    { Subject = claims.Sub
                                                      Email = info.Email
                                                      EmailVerified = info.EmailVerified
                                                      DisplayName = info.Name
                                                      AvatarUrl = info.Picture }
                with
                | :? OperationCanceledException -> return Error(ProviderUnreachable "timed out")
                | :? HttpRequestException as ex -> return Error(ProviderUnreachable ex.Message)
                | ex -> return Error(MalformedResponse ex.Message)
            }
