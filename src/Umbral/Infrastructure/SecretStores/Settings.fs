/// The external store's settings: parsed once at startup, never holding a credential (specs/005 research R1).
///
/// The same shape as `PublicOrigin` and `DataDirectory`: a pure `parse` that
/// returns every problem at once, so an operator fixes a deployment in one
/// pass instead of one restart per mistake. The words that tell them what to
/// set live in `Texts.fs` (`SecretStoreProblem`), which compiles after this
/// file and so is the one that reads these types, not the other way round.
///
/// Everything is read from `IConfiguration`, never from the process
/// environment directly. Environment variables are in it under their plain
/// names (`AWS_REGION`) and Umbral's own under the section form
/// (`UMBRAL:SECRET_STORE` for `UMBRAL__SECRET_STORE`), which is also what lets
/// a test hand over a dictionary without touching process state.
///
/// What is deliberately not here: the credentials of AWS, Google and Azure.
/// Their SDKs walk their own chains (environment, profile, workload identity,
/// the host's ambient identity) and Umbral reading any of that would be a second,
/// worse copy of it. Only BellaBaxter has no such chain, so its key, token and
/// private key are read here and held as `Secrets.Protected`, which has no
/// `ToString` that tells and no way to the value but a greppable `Reveal`.
module Umbral.Infrastructure.SecretStores.Settings

open System
open System.Globalization
open System.Text.RegularExpressions
open Microsoft.Extensions.Configuration
open Umbral.Infrastructure

// Umbral's own keys: the configuration form of an `UMBRAL__X` variable.
[<Literal>]
let StoreKey = "UMBRAL:SECRET_STORE"

[<Literal>]
let PrefixKey = "UMBRAL:SECRET_STORE_PREFIX"

[<Literal>]
let CacheSecondsKey = "UMBRAL:SECRET_CACHE_SECONDS"

[<Literal>]
let GraceSecondsKey = "UMBRAL:SECRET_GRACE_SECONDS"

[<Literal>]
let AzureVaultUriKey = "UMBRAL:AZURE_KEY_VAULT_URI"

[<Literal>]
let BellaProviderKey = "UMBRAL:BELLABAXTER_PROVIDER"

// The vendors' own variables, read under their plain names.
[<Literal>]
let AwsRegionKey = "AWS_REGION"

[<Literal>]
let GcpProjectKey = "GOOGLE_CLOUD_PROJECT"

[<Literal>]
let DefaultBellaUrl = "https://api.bella-baxter.io"

[<Literal>]
let DefaultPrefix = "umbral"

let DefaultCacheLifetime = TimeSpan.FromSeconds 300.
let DefaultGracePeriod = TimeSpan.FromSeconds 3600.

/// How Umbral signs in to BellaBaxter. Defined here rather than reused from the
/// Bella store module because this file compiles first; the registry maps one
/// onto the other.
type BellaAuth =
    | ApiKey of Secrets.Protected
    | AccessToken of Secrets.Protected

type BellaLocation =
    { BaseUrl: string
      Auth: BellaAuth
      Project: string option
      Environment: string option
      Provider: string option
      PrivateKeyPem: Secrets.Protected option }

/// Which store takes new secrets, with the one location it needs. A union, not
/// five optional fields, so a store whose location is missing cannot be built.
type StoreLocation =
    | Local
    | Aws of region: string
    | Gcp of projectId: string
    | Azure of vaultUri: Uri
    | BellaBaxter of BellaLocation

type SecretStoreSettings =
    { Store: StoreLocation
      Prefix: string
      CacheLifetime: TimeSpan
      GracePeriod: TimeSpan }

    /// The identifier a `SecretRef` carries and the registry looks a store up by.
    member this.StoreId =
        match this.Store with
        | Local -> "local"
        | Aws _ -> "aws"
        | Gcp _ -> "gcp"
        | Azure _ -> "azure"
        | BellaBaxter _ -> "bellabaxter"

/// One thing wrong with the deployment. Values that are not secret are kept so
/// the message can quote what the operator typed; a BellaBaxter key or token
/// never is, so none of these can carry one into a log.
type SettingProblem =
    | UnknownStore of value: string
    | MalformedPrefix of value: string
    | MalformedCacheSeconds of value: string
    | MalformedGraceSeconds of value: string
    | AzureVaultUriMissing
    | AzureVaultUriInvalid of value: string
    | AwsRegionMissing
    | GcpProjectMissing
    | BellaCredentialMissing
    | BellaApiKeyMalformed
    | BellaTokenWithoutProject
    | BellaTokenWithoutEnvironment
    | BellaUrlRejected of value: string

let private prefixPattern = Regex("^[a-z0-9]([a-z0-9-]{0,30}[a-z0-9])?$", RegexOptions.CultureInvariant)

/// The shape `HmacSigningHandler` itself demands: `bax-{keyId}-{signingSecret}`,
/// split on the first two hyphens (the secret may hold more). Checking it here
/// turns the client's exception at the first request into a refused start.
let private apiKeyPattern = Regex(@"^bax-[^-\s]+-\S+$", RegexOptions.CultureInvariant)

let private read (config: IConfiguration) (key: string) : string option =
    match config[key] with
    | null -> None
    | value when String.IsNullOrWhiteSpace value -> None
    | value -> Some(value.Trim())

/// An integer ≥ 0 of seconds; `None` for anything else, including overflow.
let private seconds (value: string) : TimeSpan option =
    match Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture) with
    | true, n when n >= 0 -> Some(TimeSpan.FromSeconds(float n))
    | _ -> None

/// Whether the address is acceptable to the Bella client (https, or plain http
/// on this machine only). It is the client's own rule, so Umbral and the
/// client cannot disagree about it. A function of its own, and not inlined, so
/// the vendor assembly is touched only when the store really is BellaBaxter:
/// with no setting, Umbral loads no vendor code (FR-002).
[<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
let private bellaUrlAcceptable (value: string) = global.BellaBaxter.Client.BellaApiAddress.IsAcceptable value

/// The vault's address must be https and only an address: a path would be
/// silently ignored by the client and hide a misread of the Azure portal's URL.
let private azureVaultUri (value: string) : Uri option =
    // Uri.TryCreate's out parameter is `Uri | null` even on success, so narrow
    // it through Option.ofObj rather than trusting the boolean.
    let parsed =
        match Uri.TryCreate(value, UriKind.Absolute) with
        | true, uri -> Option.ofObj uri
        | false, _ -> None

    parsed
    |> Option.filter (fun uri ->
        uri.Scheme = Uri.UriSchemeHttps
        && (uri.AbsolutePath = "/" || uri.AbsolutePath = "")
        && String.IsNullOrEmpty uri.Query
        && String.IsNullOrEmpty uri.Fragment)

let private parseBella (config: IConfiguration) : Result<BellaLocation, SettingProblem list> =
    let first (primary: string) (fallback: string) =
        read config primary |> Option.orElse (read config fallback)

    let problems = ResizeArray<SettingProblem>()
    let project = first "BELLA_BAXTER_PROJECT" "BELLA_PROJECT"
    let environment = first "BELLA_BAXTER_ENV" "BELLA_ENV"
    let url = read config "BELLA_BAXTER_URL" |> Option.defaultValue DefaultBellaUrl

    if not (bellaUrlAcceptable url) then
        problems.Add(BellaUrlRejected url)

    // The token wins when both are present, as it does in the Bella
    // configuration package this follows, so the two never disagree about which
    // identity Umbral writes under.
    let auth =
        match read config "BELLA_BAXTER_ACCESS_TOKEN", read config "BELLA_BAXTER_API_KEY" with
        | Some token, _ ->
            // A token is not tied to a project the way an API key is, so the
            // client cannot resolve them; say so now rather than at first save.
            if project.IsNone then
                problems.Add BellaTokenWithoutProject

            if environment.IsNone then
                problems.Add BellaTokenWithoutEnvironment

            Some(AccessToken(Secrets.Protected.Of token))
        | None, Some key ->
            if apiKeyPattern.IsMatch key then
                Some(ApiKey(Secrets.Protected.Of key))
            else
                problems.Add BellaApiKeyMalformed
                None
        | None, None ->
            problems.Add BellaCredentialMissing
            None

    match auth with
    | Some auth when problems.Count = 0 ->
        Ok
            { BaseUrl = url
              Auth = auth
              Project = project
              Environment = environment
              Provider = read config BellaProviderKey
              PrivateKeyPem = read config "BELLA_BAXTER_PRIVATE_KEY" |> Option.map Secrets.Protected.Of }
    | _ -> Error(List.ofSeq problems)

/// How a store that only READS (a reference left in BellaBaxter after the
/// instance moved elsewhere) signs in: the same `BELLA_BAXTER_*` precedence as
/// `parseBella`, without asking for a project or an environment, because a
/// locator carries its own. `None` when there is no usable credential: such an
/// instance cannot read the reference, which the registry reports as
/// `NotConfigured` and the card says so (FR-021) instead of refusing to start.
let bellaReaderLocation (config: IConfiguration) : BellaLocation option =
    match parseBella config with
    | Ok location -> Some location
    | Error _ ->
        // `parseBella` also refuses a token without a project; a reader does
        // not need one, so only the credential and the address are rechecked.
        let url = read config "BELLA_BAXTER_URL" |> Option.defaultValue DefaultBellaUrl

        let auth =
            match read config "BELLA_BAXTER_ACCESS_TOKEN", read config "BELLA_BAXTER_API_KEY" with
            | Some token, _ -> Some(AccessToken(Secrets.Protected.Of token))
            | None, Some key when apiKeyPattern.IsMatch key -> Some(ApiKey(Secrets.Protected.Of key))
            | _ -> None

        match auth with
        | Some auth when bellaUrlAcceptable url ->
            Some
                { BaseUrl = url
                  Auth = auth
                  Project = None
                  Environment = None
                  Provider = None
                  PrivateKeyPem = read config "BELLA_BAXTER_PRIVATE_KEY" |> Option.map Secrets.Protected.Of }
        | _ -> None

/// Parse and validate every setting, returning all the problems at once.
///
/// Prefix, cache and grace are read whatever the store, so a typo in them is
/// caught on the day it is made and not on the day the store is switched.
let parse (config: IConfiguration) : Result<SecretStoreSettings, SettingProblem list> =
    let problems = ResizeArray<SettingProblem>()

    let storeId = read config StoreKey |> Option.defaultValue "local"

    let prefix =
        match read config PrefixKey with
        | None -> DefaultPrefix
        | Some value when prefixPattern.IsMatch value -> value
        | Some value ->
            problems.Add(MalformedPrefix value)
            DefaultPrefix

    let duration (key: string) (fallback: TimeSpan) (problem: string -> SettingProblem) =
        match read config key with
        | None -> fallback
        | Some value ->
            match seconds value with
            | Some span -> span
            | None ->
                problems.Add(problem value)
                fallback

    let cache = duration CacheSecondsKey DefaultCacheLifetime MalformedCacheSeconds
    let grace = duration GraceSecondsKey DefaultGracePeriod MalformedGraceSeconds

    let store =
        match storeId.ToLowerInvariant() with
        | "local" -> Some Local
        | "aws" ->
            match read config AwsRegionKey with
            | Some region -> Some(Aws region)
            | None ->
                problems.Add AwsRegionMissing
                None
        | "gcp" ->
            match read config GcpProjectKey with
            | Some project -> Some(Gcp project)
            | None ->
                problems.Add GcpProjectMissing
                None
        | "azure" ->
            match read config AzureVaultUriKey with
            | None ->
                problems.Add AzureVaultUriMissing
                None
            | Some value ->
                match azureVaultUri value with
                | Some uri -> Some(Azure uri)
                | None ->
                    problems.Add(AzureVaultUriInvalid value)
                    None
        | "bellabaxter" ->
            match parseBella config with
            | Ok location -> Some(BellaBaxter location)
            | Error found ->
                problems.AddRange found
                None
        | _ ->
            problems.Add(UnknownStore storeId)
            None

    match store with
    | Some store when problems.Count = 0 ->
        Ok
            { Store = store
              Prefix = prefix
              CacheLifetime = cache
              GracePeriod = grace }
    | _ -> Error(List.ofSeq problems)
