/// GitHub as an OAuth 2.0 client — not OIDC, so there is no `id_token` and the
/// user-info call is never optional.
module Umbral.Infrastructure.Providers.GitHub

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
let private UserInfoUrl = "https://api.github.com/user"

[<Literal>]
let private EmailsUrl = "https://api.github.com/user/emails"

/// The REST API rejects a request with no `User-Agent` outright.
[<Literal>]
let private UserAgent = "Umbral-OIDC-Provider"

[<Literal>]
let private ApiVersion = "2022-11-21"

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

/// The token endpoint returns form-encoded by default; `Accept: application/json`
/// is what makes it return JSON. A bad code comes back as HTTP 200 with an
/// `error` field in the body — the status line lies, the body does not.
let private exchangeCode (http: HttpClient) (request: ExchangeRequest) : Task<Result<string, ConnectorError>> =
    task {
        use cts = new CancellationTokenSource(requestTimeout)
        use httpRequest = new HttpRequestMessage(HttpMethod.Post, request.Connector.Endpoints.TokenUrl)
        httpRequest.Headers.Accept.Add(MediaTypeWithQualityHeaderValue "application/json")
        httpRequest.Headers.UserAgent.ParseAdd UserAgent

        httpRequest.Content <-
            formContent
                [ "client_id", request.Connector.ClientId
                  "client_secret", request.ClientSecret
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
            | Some token when token.Length > 0 -> return Ok token
            | _ -> return Error(MalformedResponse "token response had no access_token")
    }

let private getJson (http: HttpClient) (url: string) (accessToken: string) : Task<Result<string, ConnectorError>> =
    task {
        use cts = new CancellationTokenSource(requestTimeout)
        use httpRequest = new HttpRequestMessage(HttpMethod.Get, url)
        httpRequest.Headers.Authorization <- AuthenticationHeaderValue("Bearer", accessToken)
        httpRequest.Headers.Accept.Add(MediaTypeWithQualityHeaderValue "application/vnd.github+json")
        httpRequest.Headers.UserAgent.ParseAdd UserAgent
        httpRequest.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", ApiVersion) |> ignore

        use! response = http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token)
        let! body = readBounded response.Content

        if response.IsSuccessStatusCode then
            return Ok body
        else
            return Error(ProviderRejected $"request to {url} failed with status {int response.StatusCode}")
    }

/// `/user/emails` is an array; the address to use is the one that is both
/// primary and verified. Anything else (a verified-but-not-primary address, a
/// primary-but-unverified one) is not it.
let private primaryVerifiedEmail (emailsBody: string) : string option =
    use doc = JsonDocument.Parse emailsBody

    doc.RootElement.EnumerateArray()
    |> Seq.tryPick (fun entry ->
        match optBool entry "primary", optBool entry "verified", optString entry "email" with
        | Some true, Some true, Some email -> Some email
        | _ -> None)

type private UserSummary =
    { Subject: string
      Login: string option
      Name: string option
      AvatarUrl: string option
      /// Present only when the user has chosen to make an email public. GitHub
      /// does not expose it there unless it is also verified, so it is treated
      /// as verified without a second round trip.
      InlineEmail: string option }

let private parseUser (userBody: string) : Result<UserSummary, ConnectorError> =
    use doc = JsonDocument.Parse userBody
    let root = doc.RootElement

    match root.TryGetProperty "id" with
    | true, idProp when idProp.ValueKind = JsonValueKind.Number ->
        Ok
            { Subject = (idProp.GetInt64()).ToString(Globalization.CultureInfo.InvariantCulture)
              Login = optString root "login"
              Name = optString root "name"
              AvatarUrl = optString root "avatar_url"
              InlineEmail = optString root "email" }
    | _ -> Error(MalformedResponse "user response had no numeric 'id'")

let private fetchProfile (http: HttpClient) (accessToken: string) : Task<Result<ProviderProfile, ConnectorError>> =
    task {
        match! getJson http UserInfoUrl accessToken with
        | Error e -> return Error e
        | Ok userBody ->
            match parseUser userBody with
            | Error e -> return Error e
            | Ok summary ->
                // ALWAYS ask /user/emails, even when /user carried an address.
                //
                // An inline email is GitHub's public profile address, and
                // GitHub does not document it as necessarily verified. The
                // linking rule's entire safety rests on EmailVerified meaning
                // "the provider asserted this", never "we inferred it" — and a
                // rule phrased "never infer verification" stays true under
                // review in a way that "infer it in this one case, because of
                // how GitHub's settings UI behaves" does not.
                //
                // The cost is one request per sign-in, on a product doing
                // single-digit logins a minute. It also means the address we
                // link on is always the canonical primary one.
                let! emailsResult = getJson http EmailsUrl accessToken

                let email =
                    match emailsResult with
                    | Ok emailsBody -> primaryVerifiedEmail emailsBody
                    | Error _ ->
                        // GitHub already authenticated this person; failing the
                        // whole sign-in because the address list was unreadable
                        // (a denied user:email scope, a transient 5xx) is worse
                        // than signing them in with no email. No email means no
                        // linking, which is the safe direction.
                        None

                return
                    Ok
                        { Subject = summary.Subject
                          Email = email
                          EmailVerified = email.IsSome
                          // `login` is mutable and reusable — a display hint
                          // only, never the identity.
                          DisplayName = summary.Name |> Option.orElse summary.Login
                          AvatarUrl = summary.AvatarUrl }
    }

type GitHubAdapter(httpClient: HttpClient) =
    interface IProviderAdapter with
        member _.Kind = GitHub

        member _.BuildAuthorizeUrl(request: AuthorizeRequest) =
            buildUrl request.Connector.Endpoints.AuthorizationUrl (commonAuthorizeParameters request)

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
