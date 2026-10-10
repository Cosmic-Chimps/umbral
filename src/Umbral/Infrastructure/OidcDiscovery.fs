/// What an OpenID Connect provider says about itself, fetched once and kept.
///
/// An `oidc` connector stores an issuer URL and nothing else about the provider:
/// its endpoints and signing keys are read from `{issuer}/.well-known/openid-configuration`
/// and the `jwks_uri` it names (specs/002 research R1, R2). That is Microsoft.IdentityModel's
/// `ConfigurationManager`, one per issuer, so refresh scheduling, key rotation
/// and the throttle on forced refreshes are the library's and not ours to get
/// subtly wrong (constitution III).
///
/// Lazy by design: nothing here runs at startup (FR-021). The first caller to
/// ask — the admin save, the enable toggle, a sign-in — pays for the fetch, and
/// a successful save therefore also warms the cache for the first sign-in.
module Umbral.Infrastructure.OidcDiscovery

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Microsoft.IdentityModel.Protocols
open Microsoft.IdentityModel.Protocols.OpenIdConnect
open Umbral.Domains.Connectors

/// The three ways a discovery check can end, which drive different outcomes
/// (Clarification Q2): an unreachable provider is saved disabled, a wrong one
/// is refused.
///
/// The success case is `Discovered` and not `Ok` so that a file opening this
/// module does not have `Ok` mean `DiscoveryResult.Ok` where it wants `Result.Ok`.
type DiscoveryResult =
    /// Fetched (or cached) and every check passed.
    | Discovered of OpenIdConnectConfiguration
    /// Nothing usable came back: a network failure, a timeout, a non-2xx
    /// answer, or a body that is not a JSON object. Says nothing about whether
    /// the provider is configured correctly. The reason names the cause and
    /// never contains a credential — discovery carries none.
    | Unreachable of reason: string
    /// A document came back and it is not one Umbral can sign people in with.
    /// The reason names the field and says what to change.
    | Wrong of reason: string

/// The signature algorithms an `id_token` may use: asymmetric only (research R4).
/// `none` and the `HS*` family are absent on purpose — a token anyone holding
/// the client secret can forge is not proof of who signed in (FR-012). Shared
/// with the adapter, whose `ValidAlgorithms` is this list.
let asymmetricAlgorithms =
    [ "RS256"; "RS384"; "RS512"; "PS256"; "PS384"; "PS512"; "ES256"; "ES384"; "ES512" ]

/// Ten seconds, as every other call to a provider.
let private defaultTimeout = TimeSpan.FromSeconds 10.0

/// The most a discovery document or a key set may weigh. A provider is untrusted
/// input, and `HttpDocumentRetriever` buffers whatever it is sent: without a cap a
/// 200 OK with an endless body is a memory-exhaustion vector. The adapters cap
/// their own reads at the same size (`readBounded`); this is the same rule for the
/// one place the library does the reading.
[<Literal>]
let MaxDocumentBytes = 1_000_000L

/// The library's own minimum between forced refreshes.
let private minimumRefreshInterval = TimeSpan.FromMinutes 5.0

let private discoveryPath = "/.well-known/openid-configuration"

/// One slash off the end, so `https://a/b/` and `https://a/b` compare equal:
/// providers differ on whether their `issuer` field carries one (research R2).
let private withoutTrailingSlash (value: string) =
    if value.EndsWith '/' then value.Substring(0, value.Length - 1) else value

/// https, or http for a loopback host on an instance that allows it. The same
/// rule `normaliseIssuer` applies to the issuer itself, applied to every
/// endpoint the document names.
let private isAcceptableUrl (allowLocalhostHttp: bool) (value: string) =
    match Uri.TryCreate(value, UriKind.Absolute) with
    | true, uri ->
        match Option.ofObj uri with
        | Some uri when uri.Scheme = Uri.UriSchemeHttps -> true
        | Some uri when uri.Scheme = Uri.UriSchemeHttp -> allowLocalhostHttp && isLoopbackHost uri.Host
        | _ -> false
    | _ -> false

/// The checks of research R2 on a document that did arrive. The first failure
/// wins, and each names its field and the way out.
let private validate (allowLocalhostHttp: bool) (issuer: string) (document: OpenIdConnectConfiguration) =
    let listed (values: ICollection<string>) = values |> Seq.filter (fun v -> not (String.IsNullOrEmpty v)) |> List.ofSeq

    let endpointProblem (field: string) (value: string) (required: bool) =
        match String.IsNullOrEmpty value, required with
        | true, true -> Some $"The provider's discovery document has no {field}, which Umbral needs to sign people in."
        | true, false -> None
        | false, _ when isAcceptableUrl allowLocalhostHttp value -> None
        | false, _ ->
            Some $"The provider's {field} is {value}, which is not an https address. Umbral will not send anyone's sign-in over plain http."

    let responseTypes = listed document.ResponseTypesSupported
    let algorithms = listed document.IdTokenSigningAlgValuesSupported
    let authMethods = listed document.TokenEndpointAuthMethodsSupported

    [ // Mix-up defence: a document that names a different issuer than the one
      // typed is some other provider's, or a proxy's (OIDC Discovery §4.3).
      if isNull document.Issuer || withoutTrailingSlash document.Issuer <> withoutTrailingSlash issuer then
          let actual = if String.IsNullOrEmpty document.Issuer then "nothing" else document.Issuer

          Some $"That address describes itself as {actual}, not {issuer}. Use exactly the issuer your provider shows, including any path."
      else
          None

      endpointProblem "authorization_endpoint" document.AuthorizationEndpoint true
      endpointProblem "token_endpoint" document.TokenEndpoint true
      endpointProblem "jwks_uri" document.JwksUri true
      // Optional, but if it is there it receives an access token.
      endpointProblem "userinfo_endpoint" document.UserInfoEndpoint false

      if not responseTypes.IsEmpty && not (List.contains "code" responseTypes) then
          Some "The provider does not offer the authorization code flow (its response_types_supported has no \"code\"), which is the only one Umbral uses."
      else
          None

      if not algorithms.IsEmpty && not (algorithms |> List.exists (fun a -> List.contains a asymmetricAlgorithms)) then
          Some "This provider only signs with a shared secret. Switch its signing key to RS256 or ES256 — see the walkthrough."
      else
          None

      if not authMethods.IsEmpty
         && not (List.contains "client_secret_basic" authMethods || List.contains "client_secret_post" authMethods) then
          Some "The provider does not accept client_secret_basic or client_secret_post at its token endpoint, and Umbral needs one of them to sign in as a confidential client."
      else
          None ]
    |> List.tryPick id

/// The cause of a failed fetch, in a phrase an operator can act on. The library
/// wraps whatever went wrong in an `InvalidOperationException`, so the cause is
/// the innermost exception.
let private classify (timeout: TimeSpan) (error: exn) : DiscoveryResult =
    let rec chain (e: exn) =
        [ yield e

          match e.InnerException with
          | null -> ()
          | inner -> yield! chain inner ]

    let causes = chain error

    let firstLine (text: string) =
        let line = text.Split('\n').[0].Trim()
        if line.Length > 160 then line.Substring(0, 160) + "…" else line

    // The jwks_uri is fetched through the same retriever, with https required
    // (RequireHttps below). IDX20108 is that refusal: it is a document that
    // names a wrong address, not a provider that is down.
    if causes |> List.exists (fun e -> e.Message.Contains "IDX20108") then
        Wrong "The provider's jwks_uri is not an https address. Umbral will not fetch signing keys over plain http."
    // The cap on what one fetch may buffer (`MaxDocumentBytes`) was hit. Not a
    // document the provider sent badly but one that is not a document: unreachable,
    // as a page where JSON should be is. The framework words this as a plain
    // `HttpRequestException`, so its limit error is how it is recognised.
    elif causes
         |> List.exists (fun e ->
             match e with
             | :? HttpRequestException as http ->
                 http.HttpRequestError = HttpRequestError.ConfigurationLimitExceeded
                 || http.Message.Contains "maximum buffer size"
             | _ -> false) then
        Unreachable $"its answer was larger than {MaxDocumentBytes / 1_000_000L} MB, so it is not a discovery document or a key set"
    elif causes |> List.exists (fun e -> e :? OperationCanceledException) then
        Unreachable $"no answer within {timeout.TotalSeconds} seconds"
    // IDX20807 is the library's "the answer was not a success". It hides the
    // status itself from exception messages (it may be PII), so all that can be
    // said is that there was one.
    elif causes |> List.exists (fun e -> e.Message.Contains "IDX20807") then
        Unreachable "it answered with an error instead of a discovery document"
    else
        // The innermost transport error says why: refused, no such host, TLS.
        match causes |> List.rev |> List.tryPick (function | :? HttpRequestException as e -> Some e | _ -> None) with
        | Some e -> Unreachable $"the connection failed ({firstLine e.Message})"
        | None when causes |> List.exists (fun e -> e :? System.IO.IOException) -> Unreachable "the connection failed"
        | None -> Unreachable "the answer was not an OpenID Connect discovery document"

/// One `ConfigurationManager` per issuer, over the named provider client
/// (redirects off, see `ProviderAdapters.HttpClientName`).
///
/// `allowLocalhostHttp` is whether this instance runs with the Development
/// plain-http escape hatch (Clarification Q1): the only way a loopback issuer
/// is accepted, and then only for loopback.
///
/// `timeout` bounds one fetch, discovery document and keys together; production
/// uses the ten seconds of the other provider calls. `time` is Umbral's clock, so
/// a test that drives the clock drives the refresh throttle too.
///
/// `client` becomes this cache's own: its `MaxResponseContentBufferSize` is set to
/// `MaxDocumentBytes` here, so it must be an instance made for this purpose, not
/// one shared with the adapters (the factory hands out a new `HttpClient` on every
/// `CreateClient`, so the setting stays with this one).
type OidcDiscovery(client: HttpClient, allowLocalhostHttp: bool, timeout: TimeSpan, time: TimeProvider) =
    do client.MaxResponseContentBufferSize <- MaxDocumentBytes

    let managers = ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>>()

    /// When each issuer was last refreshed on demand, for the throttle.
    let refreshedAt = ConcurrentDictionary<string, DateTimeOffset>()

    let create (issuer: string) =
        let host = Uri(issuer).Host

        // https is required of the library as well as checked above: it is the
        // one that fetches the jwks_uri, and it refuses a plain-http one
        // before any request leaves. Loopback is the single exception, and
        // only where the instance allows it.
        let retriever = HttpDocumentRetriever(client, RequireHttps = not (allowLocalhostHttp && isLoopbackHost host))

        ConfigurationManager<OpenIdConnectConfiguration>(
            issuer + discoveryPath,
            OpenIdConnectConfigurationRetriever(),
            retriever
        )

    new(client: HttpClient, allowLocalhostHttp: bool) =
        OidcDiscovery(client, allowLocalhostHttp, defaultTimeout, TimeProvider.System)

    /// The one spelling of an issuer this instance stores and compares: the
    /// rule of `normaliseIssuer` with this instance's plain-http setting, so the
    /// admin form and the cache cannot disagree about which addresses are allowed.
    member _.Normalise(issuer: string) : Result<string, IssuerError> = normaliseIssuer allowLocalhostHttp issuer

    /// Ask the provider about itself, or the cache when it already has. Used by
    /// the admin save, the enable toggle, sign-in and the token exchange.
    ///
    /// `issuer` is what the operator typed or the row stores; it is normalised
    /// here, so a caller cannot ask about a spelling the rest of the system
    /// would not recognise.
    ///
    /// Only a success stays cached. A failure — unreachable or wrong — drops the
    /// manager, so the operator who fixes the provider and tries again is
    /// answered by the provider and not by the last answer it gave. (The
    /// library itself would cache a wrong document for twelve hours, and
    /// remember a failed first fetch for a second.)
    member _.Check(issuer: string) : Task<DiscoveryResult> =
        task {
            match normaliseIssuer allowLocalhostHttp issuer with
            | Error error -> return Wrong error.Message
            | Ok issuer ->

            let manager = managers.GetOrAdd(issuer, create)
            use deadline = new CancellationTokenSource(timeout)

            let! outcome =
                task {
                    try
                        let! document = manager.GetConfigurationAsync deadline.Token

                        match validate allowLocalhostHttp issuer document with
                        | None -> return Discovered document
                        | Some problem -> return Wrong problem
                    with error ->
                        return classify timeout error
                }

            match outcome with
            | Discovered _ -> ()
            | Unreachable _
            | Wrong _ -> managers.TryRemove(KeyValuePair(issuer, manager)) |> ignore

            return outcome
        }

    /// Ask the provider again, because a token named a `kid` the cache has not
    /// seen: the provider rotated its keys (FR-013). Returns what `Check` would,
    /// after a fetch rather than before it.
    ///
    /// Not the library's `RequestRefresh`: past the first fetch the library
    /// refreshes in the background and hands back the old keys to the caller
    /// that asked, which is exactly the caller that needs the new ones. Dropping
    /// the manager makes the next `Check` a first fetch, and a first fetch waits.
    ///
    /// Throttled to one forced refresh per issuer per five minutes, the library's
    /// own interval, so a stream of tokens with invented `kid`s cannot turn
    /// Umbral into a way to hammer the provider; inside the window this is just
    /// `Check`, and the caller's retry fails as it should.
    member this.Refresh(issuer: string) : Task<DiscoveryResult> =
        match normaliseIssuer allowLocalhostHttp issuer with
        | Error error -> Task.FromResult(Wrong error.Message)
        | Ok issuer ->
            let now = time.GetUtcNow()

            let due =
                match refreshedAt.TryGetValue issuer with
                | true, last -> now - last >= minimumRefreshInterval
                | _ -> true

            if due then
                refreshedAt[issuer] <- now
                managers.TryRemove issuer |> ignore

            this.Check issuer
