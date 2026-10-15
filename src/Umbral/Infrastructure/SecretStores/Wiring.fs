/// Builds the registry from what the deployment configured (specs/005 research R2).
///
/// Two kinds of factory go in. The ACTIVE store's comes from the parsed
/// settings. A READER's comes from the vendor's own variables, for a store the
/// instance no longer writes to: a connector whose secret stayed in AWS after
/// the operator switched to the local store must still sign in (FR-011), so a
/// store is registered for reading whenever its credentials are visibly in the
/// environment, and `For` answers `NotConfigured` for one whose credentials are
/// gone.
///
/// A reader needs nothing the reference does not already say: the ARN carries
/// its region, the GCP and Azure locators their project and vault, and a
/// Bella locator its own project, environment and provider. What a reader's
/// options still demand (a region, a project, a vault) is filled from the
/// configuration when it is there and left empty or a placeholder otherwise;
/// the stores only use those values to WRITE, and a reader never does.
///
/// Every factory is a `lazy`, and each store's construction sits in its own
/// non-inlined function, so building this registry touches no vendor
/// assembly: a local-only instance loads none (SC-004, SC-006). `CanRead` builds
/// the store it is asked about, which makes no network call and loads that one
/// vendor's assembly, and only when a connector's card names the store.
module Umbral.Infrastructure.SecretStores.Wiring

open System
open System.Runtime.CompilerServices
open Microsoft.Extensions.Configuration
open Umbral.Infrastructure.Secrets
open Umbral.Infrastructure.SecretStores.Registry
open Umbral.Infrastructure.SecretStores.Settings

/// Where a reader of the Azure store would write, which it never does. A
/// reserved name (RFC 2606), so a mistaken `Put` goes nowhere.
let private placeholderVault = Uri "https://reader.invalid/"

let private present (config: IConfiguration) (keys: string list) =
    keys |> List.exists (fun key -> not (String.IsNullOrWhiteSpace config[key]))

let private text (config: IConfiguration) (key: string) =
    match config[key] with
    | null -> ""
    | value -> value.Trim()

let private bellaOptions (location: BellaLocation) (prefix: string) : BellaBaxterStore.BellaStoreOptions =
    { BaseUrl = location.BaseUrl
      Auth =
        match location.Auth with
        | Settings.ApiKey key -> BellaBaxterStore.ApiKey key
        | Settings.AccessToken token -> BellaBaxterStore.AccessToken token
      Project = location.Project
      Environment = location.Environment
      Provider = location.Provider
      PrivateKeyPem = location.PrivateKeyPem
      Prefix = prefix
      OuterHandler = None }

// One function per store, none inlined: the vendor type is first touched when
// the function RUNS, which only a `lazy` does.

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private awsStore (region: string) (prefix: string) : ISecretStore =
    AwsStore.create
        { Region = region
          Prefix = prefix
          ServiceUrl = None
          HttpMessageHandler = None
          Credentials = None }

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private gcpStore (projectId: string) (prefix: string) : ISecretStore =
    GcpStore.create
        { ProjectId = projectId
          Prefix = prefix
          Credential = None
          HttpMessageHandler = None
          BaseUri = None }

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private azureStore (vault: Uri) (prefix: string) : ISecretStore =
    AzureStore.create
        { VaultUri = vault
          Prefix = prefix
          Credential = None
          Transport = None }

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private bellaStore (location: BellaLocation) (prefix: string) : ISecretStore =
    BellaBaxterStore.create (bellaOptions location prefix)

/// A construction that throws (a credential file that cannot be parsed, say)
/// is a store that is not set up here, not a request that fails with a 500.
let private factory (build: unit -> Result<ISecretStore, StoreFailure>) =
    lazy
        (try
            build ()
         with _ ->
             Error NotConfigured)

/// The store `settings` makes active.
let private active (settings: SecretStoreSettings) : Lazy<Result<ISecretStore, StoreFailure>> option =
    let prefix = settings.Prefix

    match settings.Store with
    | Settings.StoreLocation.Local -> None
    | Settings.StoreLocation.Aws region -> Some(factory (fun () -> Ok(awsStore region prefix)))
    | Settings.StoreLocation.Gcp project -> Some(factory (fun () -> Ok(gcpStore project prefix)))
    | Settings.StoreLocation.Azure vault -> Some(factory (fun () -> Ok(azureStore vault prefix)))
    | Settings.StoreLocation.BellaBaxter location -> Some(factory (fun () -> Ok(bellaStore location prefix)))

/// The variables whose presence says an instance has credentials for a store
/// (or a place to look for them), per store id.
let readerVariables =
    [ AwsStore.StoreId, [ "AWS_REGION"; "AWS_ACCESS_KEY_ID"; "AWS_PROFILE"; "AWS_WEB_IDENTITY_TOKEN_FILE" ]
      GcpStore.StoreId, [ "GOOGLE_APPLICATION_CREDENTIALS"; "GOOGLE_CLOUD_PROJECT" ]
      AzureStore.StoreId, [ "AZURE_CLIENT_ID"; "AZURE_TENANT_ID"; AzureVaultUriKey ]
      BellaBaxterStore.StoreId, [ "BELLA_BAXTER_API_KEY"; "BELLA_BAXTER_ACCESS_TOKEN" ] ]

/// A reader for a store that is not the active one.
let private reader (config: IConfiguration) (prefix: string) (storeId: string) : Lazy<Result<ISecretStore, StoreFailure>> =
    factory (fun () ->
        match storeId with
        | "aws" -> Ok(awsStore (text config AwsRegionKey) prefix)
        | "gcp" -> Ok(gcpStore (text config GcpProjectKey) prefix)
        | "azure" ->
            // `TryCreate`'s out value is `Uri | null` even on success.
            let vault =
                match Uri.TryCreate(text config AzureVaultUriKey, UriKind.Absolute) with
                | true, uri ->
                    match Option.ofObj uri with
                    | Some uri when uri.Scheme = Uri.UriSchemeHttps -> uri
                    | _ -> placeholderVault
                | _ -> placeholderVault

            Ok(azureStore vault prefix)
        | _ ->
            // The one reader that cannot be built without a credential of its
            // own: Bella has no ambient chain.
            match bellaReaderLocation config with
            | Some location -> Ok(bellaStore location prefix)
            | None -> Error NotConfigured)

/// The registry for this deployment.
let build (config: IConfiguration) (settings: SecretStoreSettings) (clock: TimeProvider) (local: ISecretStore) : SecretStores =
    let activeId = settings.StoreId

    let readers =
        readerVariables
        |> List.filter (fun (id, keys) -> id <> activeId && present config keys)
        |> List.map (fun (id, _) -> id, reader config settings.Prefix id)

    let others =
        match active settings with
        | Some built -> (activeId, built) :: readers
        | None -> readers

    SecretStores(local, activeId, Map.ofList others, callBound, clock, settings.CacheLifetime, settings.GracePeriod)
