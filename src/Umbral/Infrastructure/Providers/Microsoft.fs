/// Sign in with Microsoft: personal accounts, and work or school accounts of one
/// organization, of any, or both (specs/016-microsoft-connector).
///
/// Microsoft is an OpenID Connect provider, so most of this adapter is the OIDC
/// adapter's code, reused from `Oidc.fs`: discovery and its key cache, the token
/// request, and the validation of the `id_token` (signature, audience, lifetime,
/// algorithm, `nonce`, `azp`) by Microsoft.IdentityModel. What is Microsoft's own
/// is the shape of its issuers and the identity it gives, in five things:
///
/// - the shared endpoints (`consumers`, `organizations`, `common`) publish a
///   discovery document whose `issuer` is not the address it is read from, and for
///   two of them is a template, `https://login.microsoftonline.com/{tenantid}/v2.0`
///   (research R1). The document is therefore read at the audience's address and
///   its `issuer` must be exactly the one the audience names
///   (`Microsoft.discoveryIssuer`), through `OidcDiscovery.CheckAt`: the mix-up
///   defence is kept, not loosened;
/// - the `iss` of a token is the template with the token's own `tid`. The adapter
///   reads `tid` from the payload before validating, only to build the one string
///   it hands the library as the valid issuer, and accepts it only as a GUID in its
///   canonical spelling, so nothing but a GUID can reach that string (research R2,
///   after Microsoft's "Validate the issuer");
/// - the signing key's own `issuer` property, which Microsoft publishes on every
///   key in its key sets, must equal that `iss` once `{tenantid}` is replaced by the
///   `tid`: a key is only good for the tenants it was issued for (research R2,
///   after Microsoft's "Validate the signing key issuer");
/// - the person is `tid#oid`, not the token's `sub`, which is different for every
///   client ID (research R4), and a tenant the operator's choice does not admit is
///   refused as `AccountRefused`, not as a malformed answer (research R3);
/// - an `email` is never verified on Microsoft's word alone: it counts as verified
///   only when the token also carries the optional claim `xms_edov` as the JSON
///   literal `true` (research R5). The userinfo endpoint is never called.
///
/// Why these comparisons are ours and not `Microsoft.IdentityModel.Validators`: see
/// research R2. What is left for Umbral is string equality; the cryptography and
/// the claim checks stay the library's.
///
/// A token, a claim value, the code and the secret never go into an error value, a
/// log or an exception message from here: the reasons are the contract's words only.
module Umbral.Infrastructure.Providers.Microsoft

open System
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.IdentityModel.JsonWebTokens
open Microsoft.IdentityModel.Protocols.OpenIdConnect
open Microsoft.IdentityModel.Tokens
open Umbral.Domains.Connectors
open Umbral.Domains.Microsoft
open Umbral.Infrastructure.OidcDiscovery
open Umbral.Infrastructure.ProviderAdapters

let private noAudience = MalformedResponse "the connector has no audience"

let private invalid (reason: string) = Error(MalformedResponse $"id_token_invalid: {reason}")

/// A claim that is a JSON string holding a canonical GUID, in the one spelling
/// `tid` and `oid` have for Umbral (`Microsoft.canonicalTid`).
let private guidClaim (payload: JsonElement) (name: string) : Guid option =
    match payload.TryGetProperty name with
    | true, prop when prop.ValueKind = JsonValueKind.String -> canonicalTid (prop.GetString())
    | _ -> None

/// The `issuer` a published key carries, as a string. Unknown members of a key are
/// kept by the library in `AdditionalData`, as the value it read.
let private keyIssuer (key: JsonWebKey) : string option =
    match key.AdditionalData.TryGetValue "issuer" with
    | true, (:? string as value) -> Some value
    | true, (:? JsonElement as element) when element.ValueKind = JsonValueKind.String -> Option.ofObj (element.GetString())
    | _ -> None

/// The public RSA parameters of a key as the library holds it (modulus and exponent,
/// without leading zeros), None for a key that is not RSA.
let private rsaPublicOf (key: SecurityKey) : (byte[] * byte[]) option =
    let trim (bytes: byte[]) = bytes |> Array.skipWhile ((=) 0uy)

    let ofParameters (p: System.Security.Cryptography.RSAParameters) =
        match Option.ofObj p.Modulus, Option.ofObj p.Exponent with
        | Some n, Some e -> Some(trim n, trim e)
        | _ -> None

    match key with
    | :? RsaSecurityKey as rsa ->
        match Option.ofObj rsa.Rsa with
        | Some algorithm -> ofParameters (algorithm.ExportParameters false)
        | None -> ofParameters rsa.Parameters
    | :? X509SecurityKey as x509 ->
        match Option.ofObj (System.Security.Cryptography.X509Certificates.RSACertificateExtensions.GetRSAPublicKey x509.Certificate) with
        | Some algorithm -> ofParameters (algorithm.ExportParameters false)
        | None -> None
    | :? JsonWebKey as jwk ->
        match Option.ofObj jwk.N, Option.ofObj jwk.E with
        | Some n, Some e -> Some(trim (Base64UrlEncoder.DecodeBytes n), trim (Base64UrlEncoder.DecodeBytes e))
        | _ -> None
    | _ -> None

/// The `issuer` member of the published key that verified the signature: the one
/// whose public key is the key the library used. When that cannot be told (a key
/// type it does not know), every published key with the token's `kid` must name the
/// same issuer. None when there is no such key, it has no issuer, or the keys that
/// could be it disagree.
let private issuerOfVerifyingKey
    (keySet: JsonWebKeySet)
    (kid: string)
    (signingKey: SecurityKey option)
    : string option =
    let candidates = keySet.Keys |> Seq.filter (fun key -> key.Kid = kid) |> List.ofSeq

    let used =
        match signingKey |> Option.bind rsaPublicOf with
        | Some material ->
            candidates
            |> List.filter (fun key ->
                match rsaPublicOf key with
                | Some other -> other = material
                | None -> false)
        | None -> []

    // The key itself when it was found; else all the candidates, which must agree.
    match (if used.IsEmpty then candidates else used) |> List.map keyIssuer |> List.distinct with
    | [ only ] -> only
    | _ -> None

/// The key-issuer rule: the key that verified the token's signature names an issuer
/// that is the token's `iss` once `{tenantid}` is the `tid`. No `kid`, no such key,
/// no `issuer` on it, or any difference: false.
///
/// The key judged is the one the library used (`signingKey`), not the first with
/// that `kid`: a key set may list one `kid` twice, and the answer must not depend on
/// the order of the list.
let private keyIssuerMatches
    (configuration: OpenIdConnectConfiguration)
    (token: JsonWebToken)
    (signingKey: SecurityKey option)
    (tid: Guid)
    : bool =
    match Option.ofObj configuration.JsonWebKeySet, Option.ofObj token.Kid, Option.ofObj token.Issuer with
    | Some keySet, Some kid, Some iss when kid <> "" ->
        issuerOfVerifyingKey keySet kid signingKey
        |> Option.exists (fun published ->
            String.Equals(published.Replace("{tenantid}", tid.ToString "D"), iss, StringComparison.Ordinal))
    | _ -> false

/// The `tid` of a token that has been parsed and not yet validated: a JSON string
/// holding a canonical GUID, or None. An encrypted token has no payload to read.
let private unvalidatedTid (token: JsonWebToken) : Guid option =
    try
        use document = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes token.EncodedPayload))

        if document.RootElement.ValueKind = JsonValueKind.Object then
            guidClaim document.RootElement "tid"
        else
            None
    with
    | :? JsonException
    | :? FormatException
    | :? ArgumentException -> None

type MicrosoftAdapter(httpClient: HttpClient, discovery: OidcDiscovery, time: TimeProvider) =

    /// The audience's discovery document: read at its own address, naming the
    /// issuer the audience says it must.
    member private _.Discover(audience: MicrosoftAudience) =
        task {
            let! discovered = discovery.CheckAt(documentBase audience, discoveryIssuer audience)
            return Oidc.mapDiscovery discovered
        }

    interface IProviderAdapter with
        member _.Kind = Microsoft

        /// The endpoints are Microsoft's to say, from the audience's cached
        /// discovery document. The userinfo endpoint is never called, so none is
        /// kept.
        member this.Resolve connector =
            task {
                match connector.MicrosoftAudience with
                | None -> return Error noAudience
                | Some audience ->
                    let! configuration = this.Discover audience

                    return
                        configuration
                        |> Result.map (fun configuration ->
                            { endpointsFor Microsoft with
                                AuthorizationUrl = configuration.AuthorizationEndpoint
                                TokenUrl = configuration.TokenEndpoint
                                UserInfoUrl = ""
                                SupportsPkce = Oidc.offersPkce configuration })
            }

        member _.BuildAuthorizeUrl(request: AuthorizeRequest) =
            buildUrl request.Endpoints.AuthorizationUrl (commonAuthorizeParameters request)

        /// The order is the plan's "Validation order inside the adapter": nothing
        /// is believed before the step that earns it.
        member this.ExchangeAndFetch(request: ExchangeRequest) =
            task {
                try
                    match request.Connector.MicrosoftAudience with
                    | None -> return Error noAudience
                    | Some audience ->

                    match! this.Discover audience with
                    | Error error -> return Error error
                    | Ok configuration ->

                    match! Oidc.exchangeCode httpClient configuration request with
                    | Error error -> return Error error
                    | Ok tokens ->

                    match tokens.IdToken with
                    | None -> return Error(MalformedResponse "id_token_missing")
                    | Some idToken ->

                    // Parsed, not trusted: only `tid` is read, to build the one
                    // issuer the library will accept.
                    let parsed =
                        try
                            Some(JsonWebToken idToken)
                        with :? ArgumentException ->
                            None

                    match parsed with
                    | None -> return invalid "malformed"
                    | Some unvalidated ->

                    match unvalidatedTid unvalidated with
                    | None -> return invalid "tenant"
                    | Some tid ->

                    let validIssuers = [ issuerFor (tid.ToString "D") ]
                    let refresh () = discovery.RefreshAt(documentBase audience, discoveryIssuer audience)

                    match! Oidc.validateIdTokenWith time request.Connector.ClientId validIssuers refresh configuration idToken request.Nonce with
                    | Error error -> return Error error
                    | Ok validated ->

                    // The key that signed it was issued for this tenant's tokens.
                    // Judged against the configuration that did the validating,
                    // which is the refreshed one when the keys were fetched again.
                    if not (keyIssuerMatches validated.Configuration validated.Token validated.SigningKey tid) then
                        return invalid "key_issuer"
                    else

                    let payload = validated.Payload

                    match guidClaim payload "oid" with
                    | None -> return invalid "subject"
                    | Some oid ->

                    if not (admits audience tid) then
                        // The tenant is the one `tid` proved, in canonical form.
                        let canonicalTenant = tid.ToString "D"
                        return Error(AccountRefused $"tenant_not_allowed;tenant={canonicalTenant}")
                    else
                        let email = Oidc.optString payload "email"

                        return
                            Ok
                                { Subject = microsoftSubject tid oid
                                  Email = email
                                  // Without an address there is nothing for the flag to
                                  // vouch for, and only the JSON literal `true` counts.
                                  EmailVerified = email.IsSome && Oidc.isJsonTrue payload "xms_edov"
                                  DisplayName =
                                    Oidc.optString payload "name"
                                    |> Option.orElse (Oidc.optString payload "preferred_username")
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
