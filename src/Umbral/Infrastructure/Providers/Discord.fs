/// Discord as an OAuth 2.0 client.
///
/// Two things make a hand-rolled Discord integration easy to get subtly wrong:
/// the token endpoint rejects a JSON body outright, and the avatar is a hash
/// that must be assembled into a CDN URL rather than a URL on its own.
module Umbral.Infrastructure.Providers.Discord

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
let private UserInfoUrl = "https://discord.com/api/v10/users/@me"

[<Literal>]
let private MaxResponseBytes = 1_000_000

let private requestTimeout = TimeSpan.FromSeconds 10.0

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

/// The token endpoint accepts `application/x-www-form-urlencoded` only — a
/// JSON body is rejected outright, which is the commonest hand-rolled-Discord
/// mistake.
let private exchangeCode (http: HttpClient) (request: ExchangeRequest) : Task<Result<string, ConnectorError>> =
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
        | Some error ->
            // Discord returns a non-2xx status alongside the body on a bad
            // code, but the body is what carries the reason worth recording.
            return Error(ProviderRejected error)
        | None ->
            match optString root "access_token" with
            | Some token when token.Length > 0 -> return Ok token
            | _ -> return Error(MalformedResponse "token response had no access_token")
    }

/// `index = (id >> 22) % 6` for a migrated account (`discriminator = "0"`),
/// `index = discriminator % 5` for a legacy one. The snowflake must be parsed
/// as UInt64 for the shift; the subject itself stays a string everywhere else.
let private embedAvatarIndex (id: string) (discriminator: string) : int =
    if discriminator = "0" then
        match UInt64.TryParse id with
        | true, snowflake -> int ((snowflake >>> 22) % 6UL)
        | false, _ -> 0
    else
        match Int32.TryParse discriminator with
        | true, d -> ((d % 5) + 5) % 5
        | false, _ -> 0

let private avatarUrl (id: string) (discriminator: string) (avatar: string option) : string =
    match avatar with
    | Some hash ->
        // A hash beginning "a_" is animated; Discord serves those as .gif.
        let ext = if hash.StartsWith("a_", StringComparison.Ordinal) then "gif" else "png"
        $"https://cdn.discordapp.com/avatars/{id}/{hash}.{ext}"
    | None ->
        let index = embedAvatarIndex id discriminator
        $"https://cdn.discordapp.com/embed/avatars/{index}.png"

let private fetchProfile (http: HttpClient) (accessToken: string) : Task<Result<ProviderProfile, ConnectorError>> =
    task {
        use cts = new CancellationTokenSource(requestTimeout)
        use httpRequest = new HttpRequestMessage(HttpMethod.Get, UserInfoUrl)
        httpRequest.Headers.Authorization <- AuthenticationHeaderValue("Bearer", accessToken)

        use! response = http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token)
        let! body = readBounded response.Content

        if not response.IsSuccessStatusCode then
            return Error(ProviderRejected $"user info request failed with status {int response.StatusCode}")
        else
            use doc = JsonDocument.Parse body
            let root = doc.RootElement

            match optString root "id" with
            | None -> return Error(MalformedResponse "user response had no 'id'")
            | Some subject ->
                // "0" is the documented default for an account that has not
                // migrated; treating a missing field the same way is safe.
                let discriminator = optString root "discriminator" |> Option.defaultValue "0"
                let username = optString root "username"
                // The real display name on a migrated account lives here, not
                // in `username`.
                let globalName = optString root "global_name"
                let avatar = optString root "avatar"
                // Nullable even with the `email` scope granted.
                let email = optString root "email"
                let verified = optBool root "verified" |> Option.defaultValue false

                return
                    Ok
                        { Subject = subject
                          Email = email
                          EmailVerified = email.IsSome && verified
                          DisplayName = globalName |> Option.orElse username
                          AvatarUrl = Some(avatarUrl subject discriminator avatar)
                          Username = None
                          ProfileUrl = None }
    }

type DiscordAdapter(httpClient: HttpClient) =
    interface IProviderAdapter with
        member _.Kind = Discord

        member _.Resolve connector =
            Task.FromResult(Ok(endpointsFor connector.Kind))

        member _.BuildAuthorizeUrl(request: AuthorizeRequest) =
            buildUrl request.Endpoints.AuthorizationUrl (commonAuthorizeParameters request)

        member _.ExchangeAndFetch(request: ExchangeRequest) =
            task {
                try
                    match! exchangeCode httpClient request with
                    | Error e -> return Error e
                    | Ok accessToken -> return! fetchProfile httpClient accessToken
                with
                | :? OperationCanceledException -> return Error(ProviderUnreachable "timed out")
                | :? HttpRequestException as ex -> return Error(ProviderUnreachable ex.Message)
                | ex -> return Error(MalformedResponse ex.Message)
            }
