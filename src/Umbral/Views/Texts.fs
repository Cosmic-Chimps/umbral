/// Every user-facing string of the admin UI and the end-user connector picker.
/// English only (D10/D11 in research.md) — kept as one record anyway, because
/// that is what makes SC-010 ("every message names the next action")
/// auditable by reading a single file. No other file may hold a literal
/// shown to an operator or an end user.
module Umbral.Views.Texts

module SecretStoreSettings = Umbral.Infrastructure.SecretStores.Settings

open Umbral.Infrastructure.Secrets
open Umbral.Domains

type Texts =
    { SiteName: string
      Footer: string
      SkipToContent: string

      NavApps: string
      NavConnectors: string
      NavUsers: string
      NavCredential: string
      NavSignOut: string

      // First run (FR-016, FR-017): a single operator credential, no username.
      SetupTitle: string
      SetupLead: string
      CredentialLabel: string
      CredentialConfirmLabel: string
      CredentialHint: string
      SetupButton: string
      SetupTokenLabel: string
      SetupTokenHint: string

      // Operator sign-in (/admin/signin)
      SignInTitle: string
      SignInLead: string
      SignInButton: string

      // Replacing the operator credential once signed in (FR-017a)
      CredentialReplaceTitle: string
      CredentialReplaceLead: string
      CredentialNewLabel: string
      CredentialReplaceButton: string
      CurrentCredentialLabel: string
      CredentialReplaced: string

      // End-user connector picker (/signin) — not an admin screen
      PickerTitle: string
      PickerLead: string
      SignInWith: string -> string
      PickerNoConnectors: string
      /// The title once the page serves a registered app (FR-001).
      PickerTitleForApp: string -> string
      /// Used instead of `PickerNoConnectors` when an app is named and offers
      /// nothing: it says whose sign-in is affected and who can fix it.
      PickerNoConnectorsForApp: string -> string
      /// The footer of a branded sign-in page (spec 006, FR-006). It replaces
      /// `Footer` and the header wordmark there, and is the page's one
      /// mention of Umbral, so it cannot be removed by branding.
      SecuredByUmbral: string

      // Screen 1 — Apps
      AppsTitle: string
      AppsLead: string
      AppsNeedConnectorTitle: string
      AppsNeedConnectorBody: string
      AppsEmptyTitle: string
      AppsEmptyBody: string
      AppNameLabel: string
      AppRedirectUriLabel: string
      AppRedirectUriHint: string
      AppPostLogoutRedirectUriLabel: string
      AppTypeLabel: string
      AppTypeHint: string
      AppTypeWebDescription: string
      AppTypeSpaDescription: string
      AppCreateButton: string
      AppClientIdLabel: string
      AppIssuerUrlLabel: string
      AppSecretLabel: string
      AppSecretOnceWarning: string
      AppRegenerateButton: string
      AppRegenerateConfirm: string -> string
      AppDeleteButton: string
      AppDeleteConfirm: string -> string

      // Which connectors an app offers (FR-005, FR-009, FR-010)
      AppConnectorsLabel: string
      AppConnectorChoiceHint: string
      AppConnectorChoiceAllSharedTitle: string
      AppConnectorChoiceAllSharedDescription: string
      AppConnectorChoiceChosenTitle: string
      AppConnectorChoiceChosenDescription: string
      AppConnectorChoiceNoneToChoose: string
      AppConnectorsAllSharedSummary: string
      AppConnectorsChosenSummary: string
      AppConnectorsChosenNone: string
      AppConnectorsSaveButton: string
      // The branding section of an app card (spec 006)
      BrandingHeading: string
      AccentLabel: string
      AccentHint: string
      AccentSave: string
      /// Names the preview button under the logo: it shows what the app's
      /// sign-in button looks like in the saved colour.
      BrandingSampleLabel: string
      /// What the preview button names after "Sign in with": no real connector,
      /// since the preview is of the colour and not of one provider.
      BrandingSampleProvider: string
      NoLogo: string
      // The logo form (spec 006). The `Logo*` texts after the hint go to the
      // crop script as data attributes, so the script holds none of its own;
      // `{limit}`, `{actual}`, `{width}` and `{height}` are filled in there.
      LogoLabel: string
      LogoHint: string
      LogoSave: string
      LogoUseWhole: string
      LogoCropLabel: string
      LogoCropHandleLabel: string
      LogoErrorFormat: string
      LogoErrorFileSize: string
      LogoErrorDimensions: string
      LogoErrorNotImage: string
      /// The refusals that carry a measured value, said with it: the image's
      /// size in pixels, or the file's size in bytes. The fixed text of the same
      /// code (`Notice`) is what is shown when the value is not there.
      AppLogoTooSmall: int -> int -> string
      AppLogoTooBig: int -> int -> string
      AppLogoTooLarge: int64 -> string
      /// The confirmation before a logo is removed; the argument is the app's name.
      AppLogoRemoveConfirm: string -> string
      /// The button that removes the logo.
      AppLogoRemoveButton: string
      /// Heading over the list of apps a change would leave with no way to
      /// sign in; each app follows with `data-stranded-app` (FR-010).
      StrandedAppsTitle: string
      /// The confirming button on a stranded-apps warning, one per action.
      ConnectorDisableStrandsConfirm: string
      ConnectorNotSharedStrandsConfirm: string
      ConnectorDeleteStrandsConfirm: string
      AppConnectorsStrandConfirm: string

      // Screen 2 — Connectors
      ConnectorsTitle: string
      ConnectorsLead: string
      AppTypeWebOption: string
      AppTypeSpaOption: string
      NotConfigured: string
      Enabled: string
      Disabled: string
      Enable: string
      Disable: string
      Copy: string
      Copied: string
      CallbackUrlLabel: string
      ClientIdLabel: string
      ClientSecretLabel: string
      ClientSecretHint: string
      ReplaceSecretLabel: string
      ReplaceSecretHint: string
      ProviderConsoleHint: string -> string
      SaveConnector: string
      OriginMismatch: string -> string -> string
      ConnectorDeleteButton: string
      ConnectorDeleteConfirm: string -> string
      /// Name, then the issuer: an OpenID Connect provider's people come back only
      /// when it is added again under the same issuer (FR-017).
      ConnectorDeleteConfirmOidc: string -> string -> string
      ConnectorDisableLastWarning: string
      ConnectorDeleteLastWarning: string

      // Several connectors of one kind, and which apps see them (FR-003,
      // FR-012, FR-013, FR-014)
      /// The advice to name the provider's application after this Umbral
      /// instance, and why. Takes the provider's name.
      ConnectorNamingAdvice: string -> string
      ConnectorDisplayNameLabel: string
      ConnectorDisplayNameHint: string
      ConnectorSharedLabel: string
      ConnectorSharedHint: string
      ConnectorSharedBadge: string
      ConnectorNotSharedBadge: string
      /// Shown on a connector that is not shared and that no app has chosen.
      ConnectorOfferedNowhere: string
      ConnectorAddTitle: string
      ConnectorAddLead: string
      ConnectorAddKindLabel: string
      ConnectorAddSlugLabel: string
      ConnectorAddSlugHint: string
      ConnectorAddButton: string
      // OpenID Connect providers (specs/002): the add form's kind and issuer, the
      // email-trust warning (FR-009), the immutability sentence (FR-004) and every
      // way the form is refused, each naming what to do next (spec 001 SC-010).
      ConnectorAddOidcOption: string
      ConnectorIssuerLabel: string
      ConnectorIssuerHint: string
      ConnectorIssuerFixedNote: string
      /// Extra scopes of an OpenID Connect provider (specs/002 T025).
      ConnectorEmailTrustWarning: string
      ConnectorOidcClientIdHint: string
      ConnectorOidcSecretAgainHint: string
      /// What the operator calls the provider where a built-in kind's name would
      /// read wrongly ("OpenID Connect's developer console").
      ConnectorOidcProviderName: string
      ConnectorAddOidcDisplayNameRequired: string
      ConnectorAddOidcSlugInvalid: string
      ConnectorAddOidcSlugTaken: string
      ConnectorAddOidcClientIdRequired: string
      ConnectorAddOidcSecretRequired: string
      /// The display name of the connector that already holds the issuer.
      ConnectorAddOidcIssuerTaken: string -> string
      /// Issuer, then the reason it could not be reached: said after the provider
      /// was saved disabled for that reason (FR-005a).
      ConnectorAddOidcUnreachable: string -> string -> string
      /// Issuer, then the reason: said when enabling a provider that cannot be
      /// reached now (Clarification Q2).
      ConnectorEnableOidcUnreachable: string -> string -> string
      /// The reason a discovery check refused the provider outright (it
      /// describes itself wrongly), when the operator tried to enable it.
      ConnectorEnableOidcWrong: string -> string

      // The origin-mismatch banner (D4), naming both origins

      // Screen 3 — Users
      UsersTitle: string
      UsersLead: string
      UsersEmptyTitle: string
      UsersEmptyBody: string
      UserSearchLabel: string
      UserSearchPlaceholder: string
      UserFirstSignInLabel: string
      UserLastSignInLabel: string
      UserNeverSignedIn: string
      UserEnabledBadge: string
      UserDisabledBadge: string
      UserEnableButton: string
      UserDisableButton: string
      UserDisableConfirm: string -> string
      UserDeleteButton: string
      UserDeleteConfirm: string -> string

      // Home page (GET /): static, the same for everyone, nothing about this instance
      HomeTitle: string
      HomeHeadline: string
      HomeLead: string
      HomeSigningInHeading: string
      HomeSigningInBody: string
      HomeBuildingHeading: string
      HomeBuildingBody: string
      HomeDiscoveryLink: string
      HomeRunningHeading: string
      HomeRunningBody: string
      HomeAdminButton: string

      // Generic chrome and errors (FR-037: every message names the next action)
      AntiforgeryExpiredTitle: string
      AntiforgeryExpiredBody: string
      NotFoundTitle: string
      NotFoundBody: string
      ErrorTitle: string
      ErrorBody: string
      RateLimitedTitle: string
      RateLimitedBody: int -> string
      BackHome: string
      BackToApp: string

      /// Flash messages addressed by a short code (`data-notice="{code}"`).
      /// The code arrives in a URL anyone can write, so it is never shown:
      /// screens drop a code `knownNotice` does not recognise, and a code that
      /// gets here anyway gets a fixed text.
      ProtocolErrorTitle: string
      ProtocolErrorBody: string
      Notice: string -> string

      // The external secret stores (spec 005), named for the operator. The
      // argument is a store id as a `SecretRef` carries it; an id this build
      // does not know is shown as it is, which is a fixed word of ours (it
      // came from a stored reference, never from a URL).
      SecretStoreName: string -> string
      /// A write to the store was refused: the store id and the failure. Shown
      /// inline by the OIDC add form and, through the single-use notice cookie,
      /// by the redirecting paths.
      ConnectorSecretStoreFailed: string -> StoreFailure -> string
      /// A delete left the secret in its manager: the store's display name and
      /// the name the operator has to remove it under there.
      ConnectorSecretLeftBehind: string -> string -> string
      /// A replace moved the secret to the active store but could not remove the
      /// old copy: the old store's display name and the name to remove there.
      ConnectorSecretReplacedLeftBehind: string -> string -> string

      // The `move-secrets` command (spec 005, FR-016 to FR-019): one plain line
      // per connector, then a summary. Store arguments are store ids as a
      // `SecretRef` carries them (`local`, `aws`), because the lines are read by
      // an operator at a shell and matched on their first word by tests. No
      // line ever carries a value, a locator or a credential.
      MoveMoved: string -> string -> string -> string
      MoveWouldMove: string -> string -> string -> string
      MoveAlreadyIn: string -> string -> string
      /// The row changed while the move was copying, so the switch was refused
      /// and nothing was overwritten (slug).
      MoveChangedMeanwhile: string -> string
      /// The connector's lease was held, by an edit in the admin screen or by a
      /// move that is still running, so it was left alone (slug).
      MoveBusy: string -> string
      /// slug, from, to, then why in plain words.
      MoveFailed: string -> string -> string -> string -> string
      /// slug, from, to, the failure of the delete (in the `from` store), and
      /// the name the leftover has under there.
      MoveLeftBehind: string -> string -> string -> StoreFailure -> string -> string
      /// Why a call to a store failed: the store's id and the failure.
      MoveStoreFailure: string -> StoreFailure -> string
      /// The target did not give back what was written (target store id).
      MoveMismatch: string -> string
      /// The stored reference could not even be read (slug has no store prefix).
      MoveReferenceUnreadable: string
      /// dry run, connectors, moved (or would move), already in place, changed
      /// meanwhile, failed.
      MoveSummary: bool -> int -> int -> int -> int -> int -> string

      // Startup refusals about the external secret store (spec 005, FR-005):
      // one line per problem, each saying which variable and what to set.
      SecretStoreProblem: SecretStoreSettings.SettingProblem -> string

      // Where a connector's secret is kept, and why a store is failing (spec
      // 005, User Story 4). All take store ids as a `SecretRef` carries them
      // and say what to do next; none can carry a value, a locator or a
      // credential.
      /// The card pill: "Kept in <store name>" (store id).
      SecretStoreLabel: string -> string
      /// The card warning for a secret kept in a store this instance cannot
      /// build (store id).
      SecretUnreadable: string -> string
      /// The Connectors page notice when the active store failed its startup
      /// probe or its latest save (store id, failure).
      SecretStoreProblemNotice: string -> StoreFailure -> string
      /// The warning logged at startup for the same failure (store id,
      /// failure).
      SecretStoreProbeWarning: string -> StoreFailure -> string }

/// The display name of a store, by the id a `SecretRef` carries.
let private secretStoreNameEn (storeId: string) : string =
    match storeId with
    | "local" -> "this instance (encrypted)"
    | "aws" -> "AWS Secrets Manager"
    | "gcp" -> "Google Cloud Secret Manager"
    | "azure" -> "Azure Key Vault"
    | "bellabaxter" -> "BellaBaxter"
    | other -> other

/// What a failure says about the store, in a clause that follows its name.
let private causeEn (failure: StoreFailure) : string =
    match failure with
    | Unreachable -> "could not be reached"
    | AccessDenied -> "refused Umbral's credentials or permissions"
    | NotConfigured -> "is not set up on this instance"
    | Rejected _ -> "rejected the request"
    | NotFound -> "failed"

/// What to look at when a store cannot be reached, which differs by store.
let private reachHintEn (storeId: string) : string =
    match storeId with
    | "aws" -> "check the network and AWS_REGION"
    | "gcp" -> "check the network and GOOGLE_CLOUD_PROJECT"
    | "azure" -> "check the network and UMBRAL__AZURE_KEY_VAULT_URI"
    | "bellabaxter" -> "check the network and the BellaBaxter URL"
    | _ -> "check the network"

/// What to check when a store failed in a way with no remedy of its own, by
/// cause: the sentence the startup warning, the page notice and the refusal of
/// a save share.
let private whatToCheckEn (storeId: string) (failure: StoreFailure) : string =
    match failure with
    | AccessDenied ->
        "Check that the credentials Umbral was given are valid and that they may read and write secrets, following the permissions in docs/secret-stores/."
    | NotConfigured ->
        "Check the settings and credentials for it in the environment, following docs/secret-stores/."
    | Unreachable -> $"Check the network between this host and it, and that Umbral's credentials for it are valid ({reachHintEn storeId}), following docs/secret-stores/."
    | _ -> "Check its settings and the permissions Umbral was given, following docs/secret-stores/."

/// The failures that have a remedy of their own, by their closed code. `None`
/// for every other failure, whose `Rejected` reason, if any, stays unprinted.
let private specificFailureEn (storeId: string) (failure: StoreFailure) : string option =
    let name = secretStoreNameEn storeId

    match failure with
    | Rejected "soft_deleted" ->
        Some $"{name} holds a deleted secret under this connector's name. Restore it (or purge it) in the console, then save again."
    | Rejected "provider_ambiguous" ->
        Some
            $"{name} has more than one secret provider in this environment, or none, so Umbral cannot tell which one keeps its secrets. Set UMBRAL__BELLABAXTER_PROVIDER to the name of the provider to use, then restart Umbral."
    | Rejected "location_not_found" ->
        Some
            $"{name} has no project, environment or provider by the configured name. Check BELLA_BAXTER_PROJECT, BELLA_BAXTER_ENV and UMBRAL__BELLABAXTER_PROVIDER."
    | NotFound ->
        Some
            $"{name} does not have the secret or location Umbral asked for. Check its settings (region, project, vault) and the permissions Umbral was given, following docs/secret-stores/."
    | _ -> None

/// What is wrong with a store and what to do about it, from the store's id and
/// the failure. The one place a failure becomes words: the startup warning, the
/// page notice and the refusal of a save all build on it, so a failure with a
/// remedy of its own (a secret deleted in the console, an ambiguous BellaBaxter
/// provider) says it in all three. A `Rejected` reason is printed only through
/// the closed codes `specificFailureEn` knows; any other reason gets the
/// generic text, because it is an exception type name or a status code.
let storeFailureText (storeId: string) (failure: StoreFailure) : string =
    match specificFailureEn storeId failure with
    | Some text -> text
    | None -> $"{secretStoreNameEn storeId} {causeEn failure}. {whatToCheckEn storeId failure}"

let private connectorSecretStoreFailedEn (storeId: string) (failure: StoreFailure) : string =
    $"{storeFailureText storeId failure} The client secret was not saved and nothing was changed; enter it again once that is fixed."

let private connectorSecretLeftBehindEn (store: string) (name: string) : string =
    $"The connector was deleted, but its client secret {name} could not be removed from {store}. Delete it there by hand, or anyone with access to that store can still read it."

let private connectorSecretReplacedLeftBehindEn (store: string) (name: string) : string =
    $"The connector was saved, but its previous secret could not be removed from {store}. Remove {name} there by hand, or anyone with access to that store can still read it."

let private secretStoreLabelEn (storeId: string) : string =
    $"Kept in {secretStoreNameEn storeId}"

let private secretUnreadableEn (storeId: string) : string =
    $"This connector's secret is kept in {secretStoreNameEn storeId}, which this instance cannot read, so signing in with it will fail. Enter the secret again on this card to keep it in the active store, or set up {secretStoreNameEn storeId} on this instance again as described in docs/secret-stores/."

/// A report line: the word the line is matched on, padded so that the slugs
/// line up in a terminal, then what it says.
let private moveLine (word: string) (slug: string) (rest: string) = $"{word.PadRight 9} {slug.PadRight 10} {rest}"

let private moveStoreFailureEn (storeId: string) (failure: StoreFailure) : string =
    let name = secretStoreNameEn storeId

    match failure with
    | Unreachable -> $"could not reach {name} ({reachHintEn storeId})"
    | AccessDenied -> $"{name} refused Umbral's credentials or permissions (check that they may read and write secrets)"
    | NotFound -> $"the secret was not found in {name} (re-enter it on the Connectors screen)"
    | NotConfigured -> $"{name} is not set up on this instance (keep its credentials available while moving out of it)"
    | Rejected "soft_deleted" ->
        $"{name} holds a deleted secret under this connector's name (restore or purge it in the console, then run move-secrets again)"
    | Rejected "provider_ambiguous" ->
        $"{name} has more than one secret provider in this environment, or none (set UMBRAL__BELLABAXTER_PROVIDER)"
    | Rejected "location_not_found" ->
        $"{name} has no project, environment or provider by the configured name (check BELLA_BAXTER_PROJECT, BELLA_BAXTER_ENV and UMBRAL__BELLABAXTER_PROVIDER)"
    | Rejected _ -> $"{name} rejected the request (check its permissions and settings)"

let private secretStoreProblemNoticeEn (storeId: string) (failure: StoreFailure) : string =
    $"{storeFailureText storeId failure} Secrets kept in {secretStoreNameEn storeId} cannot be read or saved right now. This notice clears after a secret is saved to it successfully."

let private secretStoreProbeWarningEn (storeId: string) (failure: StoreFailure) : string =
    $"Secret store check at startup: {storeFailureText storeId failure} Umbral started anyway, but secrets kept in {secretStoreNameEn storeId} cannot be read or saved until this is fixed."

let private moveSummaryEn (dryRun: bool) (total: int) (moved: int) (already: int) (changed: int) (failed: int) : string =
    let noun = if total = 1 then "connector" else "connectors"
    let movedWords = if dryRun then "would move" else "moved"
    let changedPart = if changed > 0 then $", {changed} changed meanwhile (run it again)" else ""
    $"{total} {noun}: {moved} {movedWords}, {already} already in place{changedPart}, {failed} failed."

/// The stored-logo limit as people read it: 1.25 MB, from the byte count.
let private logoLimitMegabytes = float PngLogo.maxBytes / 1048576.0

/// A file size in megabytes with one decimal, for a notice.
let private megabytes (bytes: int64) = (float bytes / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)

/// The notice texts, by code; `None` for a code no screen sends.
let private noticeEn (code: string) : string option =
    match code with
    | "setup-token-invalid" ->
        Some "That setup token doesn't match. Run `docker logs umbral` and copy the token printed when the container started."
    | "setup-mismatch" -> Some "The two values don't match. Type the same credential in both fields and try again."
    | "setup-too-short" -> Some "Use at least 12 characters, then submit the form again."
    | "signin-incorrect" ->
        Some "That credential isn't correct. Check it and try again, or run `docker exec -it umbral /app/Umbral reset-credential` on the host if you've lost it."
    | "sign-in-failed" -> Some "Sign-in didn't complete. Try again, or choose a different option."
    | "access-denied" -> Some "You cancelled the sign-in. Choose an option below to try again."
    | "provider-unreachable" ->
        Some "That provider could not be reached. Try again in a moment, or choose a different option."
    | "account-disabled" -> Some "This account has been disabled. Contact whoever runs this Umbral."
    | "app-created" -> Some "App registered."
    | "app-name-required" -> Some "Give the app a name, then save again."
    | "app-redirect-uri-invalid" ->
        Some "Enter an absolute URL with no wildcards, for example https://app.example.com/callback, then save again."
    | "app-type-invalid" -> Some "Choose an app type, then save again."
    | "app-not-found" -> Some "That app no longer exists. Choose one from the list below, or register it again."
    | "app-no-secret" ->
        Some "Single-page and native apps have no secret to replace — they use PKCE instead. If sign-in fails for this app, check that it sends a PKCE challenge."
    | "user-not-found" -> Some "That user no longer exists. Choose one from the list below."
    | "app-secret-regenerated" -> Some "New secret generated."
    | "app-deleted" -> Some "App deleted."
    | "connector-saved" -> Some "Connector saved."
    | "connector-unknown" -> Some "That connector isn't one Umbral supports. Choose one of the connectors listed on this screen."
    | "connector-client-id-required" -> Some "Enter the client ID from the provider's console, then save again."
    | "connector-secret-required" -> Some "Enter the client secret from the provider's console, then save again."
    // The fallbacks for a notice that arrives without its server-side detail
    // (a refresh, a link): the same advice, without the store's name.
    | "connector-secret-store-failed" ->
        Some "The client secret could not be saved to its secret store, so nothing was saved. Check that Umbral's credentials for the store are valid, that they may write secrets, and that the store is reachable from this host, following the walkthrough in docs/secret-stores/. Then enter the secret again."
    | "connector-secret-left-behind" ->
        Some "The connector was deleted, but its client secret could not be removed from its secret store. Remove it there by hand: it is named after the connector, under the prefix umbral by default."
    | "connector-secret-replaced-left-behind" ->
        Some "The connector was saved, but its previous secret could not be removed from its old secret store. Remove it there by hand: it is named after the connector, under the prefix umbral by default."
    | "connector-secret-busy" ->
        Some "A secret move is working on this connector. Try again in a minute."
    | "connector-last-enabled" ->
        Some "This is the only enabled connector. Turning it off means nobody can sign in to any app. Press it again to confirm."

    | "connector-delete-last-enabled" ->
        Some "This is the only enabled connector. Nothing was deleted. Deleting it means nobody can sign in to any app — use Delete and confirm to go ahead."

    | "connector-added-oidc" ->
        Some "OpenID Connect provider added and enabled. Copy its callback URL from its card into your provider's admin area, then people can sign in with it."
    | "connector-added" ->
        Some "Connector added. Copy its callback URL into the provider's console, then enable it to offer it."
    | "connector-added-oidc-unreachable" ->
        Some "The provider was saved but is disabled, because Umbral could not reach it. Enable it once its address opens in a browser."
    | "connector-enable-oidc-unreachable" ->
        Some "Not enabled: Umbral could not reach the provider. Try again once its address opens in a browser."
    | "connector-enable-oidc-wrong" ->
        Some "Not enabled: the provider describes itself in a way Umbral cannot use. Check the issuer and the provider's signing settings, then try again."
    | "connector-slug-invalid" ->
        Some "The identifier can use only lower-case letters, digits and hyphens, up to 32 characters. Change it and add the connector again."
    | "connector-slug-taken" ->
        Some "Another connector already has that identifier. Choose a different one and add the connector again."
    | "connector-display-name-required" -> Some "Enter the label for the sign-in button, then save again."
    | "connector-strands-apps" ->
        Some "Nothing was changed. The apps listed would be left with no way to sign in. Give them another connector first, or confirm to go ahead."
    | "connector-delete-strands-apps" ->
        Some "Nothing was deleted. The apps listed would be left with no way to sign in. Give them another connector first, or confirm to delete it anyway."
    | "app-connectors-strand" ->
        Some "Nothing was saved. With that choice the app would have no enabled connector to sign in with. Choose at least one enabled connector, or confirm to save it anyway."
    | "app-connector-unknown" ->
        Some "One of the ticked connectors no longer exists. Reload the page, tick from the list shown, and save again."
    | "app-connectors-saved" -> Some "Sign-in methods saved. The app's next sign-in page uses them."
    | "app-accent-saved" -> Some "Accent colour saved. The app's sign-in page uses it from the next visit."
    | "app-accent-low-contrast" ->
        Some "Saved. This colour is hard to see on Umbral's dark background, so outlines and highlights in it will be faint. Buttons stay readable."
    | "app-accent-invalid" ->
        Some "Enter the colour as # followed by six hexadecimal digits, for example #f26b1d."
    | "app-logo-saved" -> Some "Logo saved. The app's sign-in page shows it from the next visit."
    | "app-logo-missing" -> Some "Choose an image file first, then save the logo."
    | "app-logo-not-png" ->
        Some "Umbral stores logos as PNG. Turn on JavaScript on this page to crop, scale and convert a PNG, JPEG or WebP image, or upload a PNG of at most 512 × 512 pixels."
    | "app-logo-corrupt" -> Some "This PNG is damaged. Export it again from your image editor."
    | "app-logo-interlaced" -> Some "Save the PNG without interlacing, or turn on JavaScript to convert it."
    | "app-logo-too-small" ->
        Some $"The image is too small. Use one of at least {PngLogo.minSide} × {PngLogo.minSide} pixels."
    | "app-logo-too-big" ->
        Some $"The image is too big. Turn on JavaScript to scale it down, or export it at {PngLogo.maxSide} pixels or less on each side."
    | "app-logo-too-large" ->
        Some $"The file is too large. A logo is stored at {logoLimitMegabytes} MB or less. Turn on JavaScript to shrink it, or export a smaller PNG."
    | "app-logo-removed" -> Some "Logo removed. The app's sign-in page shows no logo from the next visit."
    | "app-accent-cleared" -> Some "Accent colour cleared. The app's sign-in page uses Umbral's colour from the next visit."
    | "connector-enabled" -> Some "Connector enabled."
    | "connector-disabled" -> Some "Connector disabled."
    | "connector-deleted" -> Some "Connector deleted."
    | "user-enabled" -> Some "User enabled."
    | "user-disabled" -> Some "User disabled."
    | "user-deleted" -> Some "User deleted."
    | "credential-replaced" -> Some "Operator credential replaced."
    | "credential-current-incorrect" -> Some "That's not the current credential. Check it and try again."
    | _ -> None

/// What `SecretStoreSettings.parse` found wrong, in the words of someone at 1am with a Pi:
/// the variable by its `__` name and what to set it to. None of them quotes a
/// BellaBaxter key, token or private key; the problems never carry one.
let private secretStoreProblemEn (problem: SecretStoreSettings.SettingProblem) : string =
    match problem with
    | SecretStoreSettings.UnknownStore value ->
        $"UMBRAL__SECRET_STORE is \"{value}\", which is not a store Umbral knows. Set it to one of local, aws, gcp, azure or bellabaxter, or remove it to keep secrets in the data directory."
    | SecretStoreSettings.MalformedPrefix value ->
        $"UMBRAL__SECRET_STORE_PREFIX is \"{value}\". Use 1 to 32 lowercase letters, digits or hyphens, not starting or ending with a hyphen, for example umbral. Remove it to use umbral."
    | SecretStoreSettings.MalformedCacheSeconds value ->
        $"UMBRAL__SECRET_CACHE_SECONDS is \"{value}\", which is not a whole number of seconds of 0 or more. Set it to a number such as 300, or 0 to turn the cache off."
    | SecretStoreSettings.MalformedGraceSeconds value ->
        $"UMBRAL__SECRET_GRACE_SECONDS is \"{value}\", which is not a whole number of seconds of 0 or more. Set it to a number such as 3600, or 0 to refuse a secret the manager cannot confirm."
    | SecretStoreSettings.AzureVaultUriMissing ->
        "UMBRAL__SECRET_STORE is azure but UMBRAL__AZURE_KEY_VAULT_URI is not set. Set it to your vault's URI, such as https://umbral-kv.vault.azure.net/."
    | SecretStoreSettings.AzureVaultUriInvalid value ->
        $"UMBRAL__AZURE_KEY_VAULT_URI is \"{value}\". Give your vault's address as an https URL with no path, such as https://umbral-kv.vault.azure.net/."
    | SecretStoreSettings.AwsRegionMissing ->
        "UMBRAL__SECRET_STORE is aws but AWS_REGION is not set. Set it to the region new secrets are created in, such as eu-west-1."
    | SecretStoreSettings.GcpProjectMissing ->
        "UMBRAL__SECRET_STORE is gcp but GOOGLE_CLOUD_PROJECT is not set. Set it to the ID of the Google Cloud project new secrets are created in."
    | SecretStoreSettings.BellaCredentialMissing ->
        "UMBRAL__SECRET_STORE is bellabaxter but neither BELLA_BAXTER_API_KEY nor BELLA_BAXTER_ACCESS_TOKEN is set. Set BELLA_BAXTER_API_KEY to an API key from your Bella Baxter project."
    | SecretStoreSettings.BellaApiKeyMalformed ->
        "BELLA_BAXTER_API_KEY does not look like a Bella Baxter API key, which has the form bax-{id}-{secret}. Copy the whole key again from Bella Baxter, with nothing before or after it."
    | SecretStoreSettings.BellaTokenWithoutProject ->
        "BELLA_BAXTER_ACCESS_TOKEN is set but BELLA_BAXTER_PROJECT is not. A token does not name a project, so set BELLA_BAXTER_PROJECT to the project's slug."
    | SecretStoreSettings.BellaTokenWithoutEnvironment ->
        "BELLA_BAXTER_ACCESS_TOKEN is set but BELLA_BAXTER_ENV is not. A token does not name an environment, so set BELLA_BAXTER_ENV to the environment's slug."
    | SecretStoreSettings.BellaUrlRejected value ->
        $"BELLA_BAXTER_URL is \"{value}\". Set it to the https address of your Bella Baxter server, such as https://api.bella-baxter.io. Plain http is accepted only for localhost."

let en: Texts =
    { SiteName = "Umbral"
      Footer = "Umbral — self-hosted social login"
      SkipToContent = "Skip to content"

      NavApps = "Apps"
      NavConnectors = "Connectors"
      NavUsers = "Users"
      NavCredential = "Credential"
      NavSignOut = "Sign out"

      SetupTitle = "Set up Umbral"
      SetupLead = "Choose the operator credential. It's the only way to reach this admin area, so keep it somewhere safe. There's no recovery screen: if you lose it, run `docker exec -it umbral /app/Umbral reset-credential` on the host."
      CredentialLabel = "Operator credential"
      CredentialConfirmLabel = "Confirm credential"
      CredentialHint = "At least 12 characters. This is not tied to any connector, so a broken Discord or Google app can never lock you out."
      SetupButton = "Set credential and continue"
      SetupTokenLabel = "Setup token"
      SetupTokenHint =
        "Printed in the container log when Umbral started. Run `docker logs umbral` and copy the token shown there."

      SignInTitle = "Operator sign-in"
      SignInLead = "Sign in with the operator credential to manage Apps, Connectors and Users."
      SignInButton = "Sign in"

      CredentialReplaceTitle = "Replace the operator credential"
      CredentialReplaceLead = "Enter your current credential, then the new one. Every other admin session is signed out immediately."
      CredentialNewLabel = "New credential"
      CredentialReplaceButton = "Replace credential"
      CurrentCredentialLabel = "Current credential"
      CredentialReplaced = "Operator credential replaced. Use it next time you sign in."

      PickerTitle = "Sign in"
      PickerLead = "Choose how you'd like to sign in."
      SignInWith = fun provider -> $"Sign in with {provider}"
      SecuredByUmbral = "Secured by Umbral"
      PickerNoConnectors = "No sign-in option is available right now. Contact the operator of this service."
      PickerTitleForApp = fun app -> $"Sign in to {app}"
      PickerNoConnectorsForApp = fun app -> $"{app} has no sign-in method available right now. Contact whoever runs {app}, who can fix this."

      AppsTitle = "Apps"
      AppsLead = "Apps that delegate sign-in to Umbral. Each gets its own client ID and secret."
      AppsNeedConnectorTitle = "Add a connector first"
      AppsNeedConnectorBody = "An app needs at least one enabled connector so people have a way to sign in. Go to Connectors and set one up."
      AppsEmptyTitle = "No apps yet"
      AppsEmptyBody = "Register your first app to get an issuer URL, a client ID and a client secret."
      AppNameLabel = "Name"
      AppRedirectUriLabel = "Redirect URI"
      AppRedirectUriHint = "The exact, absolute callback URL the app expects — no wildcards."
      AppPostLogoutRedirectUriLabel = "Post-logout redirect URI (optional)"
      AppTypeLabel = "App type"
      AppTypeHint = "Not sure? Ask where the sign-in code runs: on a server you control, pick Web application; on the person's own device, pick Single-page or native app."
      AppTypeWebDescription = "Your app has a back end that can keep a secret: ASP.NET, Node/Express, Django, Rails, PHP. Sign-in happens on the server. You get a client ID and a client secret, and sign-in is also protected with PKCE."
      AppTypeSpaDescription = "Your app runs entirely on the person's device, where anything shipped can be read: a React, Vue or Angular app with no back end, a mobile or desktop app, a command-line tool. You get a client ID only, no secret; the app proves itself with PKCE."
      AppCreateButton = "Create app"
      AppClientIdLabel = "Client ID"
      AppIssuerUrlLabel = "Issuer URL"
      AppSecretLabel = "Client secret"
      AppSecretOnceWarning = "This secret is shown only once and Umbral keeps no way to show it again. If you lose it, come back here and regenerate it."
      AppRegenerateButton = "Regenerate secret"
      AppRegenerateConfirm = fun name -> $"Regenerate the secret for \"{name}\"? The app will stop working with its old secret until you update its configuration with the new one."
      AppDeleteButton = "Delete app"
      AppDeleteConfirm = fun name -> $"Delete \"{name}\"? Every token already issued to it stops working immediately, and this can't be undone."

      AppConnectorsLabel = "Sign-in methods"
      AppConnectorChoiceHint = "Which connectors this app offers on its sign-in page. This only decides how people sign in. It never decides who may."
      AppConnectorChoiceAllSharedTitle = "All shared connectors"
      AppConnectorChoiceAllSharedDescription = "The app offers every enabled connector marked shared, including ones you add later. This is the right choice for most apps."
      AppConnectorChoiceChosenTitle = "Only the connectors I choose"
      AppConnectorChoiceChosenDescription = "The app offers just the ticked connectors, shared or not. Use it when this app has its own provider application, for example so the consent screen shows its own name."
      AppConnectorChoiceNoneToChoose = "No connectors yet. Add one on the Connectors screen, then come back and choose."
      AppConnectorsAllSharedSummary = "All shared connectors"
      AppConnectorsChosenSummary = "Chosen connectors"
      AppConnectorsChosenNone = "None chosen. This app offers no sign-in method until you choose one."
      AppConnectorsSaveButton = "Save sign-in methods"
      BrandingHeading = "Sign-in page branding"
      AccentLabel = "Accent colour"
      AccentHint = "# and six hexadecimal digits, for example #f26b1d. Leave empty to use Umbral's colour."
      AccentSave = "Save colour"
      BrandingSampleLabel = "Preview of the sign-in button"
      BrandingSampleProvider = "your provider"
      NoLogo = "No logo"
      LogoLabel = "Logo"
      LogoHint =
        $"PNG, JPEG or WebP. Umbral stores a PNG of at most {PngLogo.maxSide} × {PngLogo.maxSide} pixels. Without JavaScript, upload a PNG of {PngLogo.minSide}–{PngLogo.maxSide} pixels a side, up to {logoLimitMegabytes} MB."
      LogoSave = "Save logo"
      LogoUseWhole = "Use whole image"
      LogoCropLabel =
        "Crop area. Drag it to move, drag a corner to resize, or use the arrow keys; hold Shift with an arrow key to resize."
      LogoCropHandleLabel = "Resize the crop area"
      LogoErrorFormat = "Umbral accepts PNG, JPEG and WebP images. Choose a file in one of those formats."
      LogoErrorFileSize = "That file is {actual} MB. Choose an image of at most {limit} MB."
      LogoErrorDimensions = "That image is {width} × {height} pixels. Choose one of at most {limit} pixels on each side."
      LogoErrorNotImage = "That file could not be read as an image. Choose a PNG, JPEG or WebP file."
      AppLogoTooSmall =
        fun width height ->
            $"The image is {width} × {height} pixels. Use one of at least {PngLogo.minSide} × {PngLogo.minSide} pixels."
      AppLogoTooBig =
        fun width height ->
            $"The image is {width} × {height} pixels, over the {PngLogo.maxSide} × {PngLogo.maxSide} limit. Turn on JavaScript to scale it down, or export it at {PngLogo.maxSide} pixels or less on each side."
      AppLogoRemoveConfirm =
        fun name -> $"Remove the logo of {name}? Its sign-in page shows no logo from the next visit. You can upload one again at any time."
      AppLogoRemoveButton = "Remove logo"
      AppLogoTooLarge =
        fun bytes ->
            $"The file is {megabytes bytes} MB. A logo is stored at {logoLimitMegabytes} MB or less. Turn on JavaScript to shrink it, or export a smaller PNG."
      StrandedAppsTitle = "These apps would be left with no way to sign in:"
      ConnectorDisableStrandsConfirm = "Disable anyway"
      ConnectorNotSharedStrandsConfirm = "Stop sharing anyway"
      ConnectorDeleteStrandsConfirm = "Delete anyway"
      AppConnectorsStrandConfirm = "Save anyway"

      ConnectorsTitle = "Connectors"
      AppTypeWebOption = "Web application"
      AppTypeSpaOption = "Single-page or native app"
      NotConfigured = "Not set up"
      Enabled = "Enabled"
      Disabled = "Disabled"
      Enable = "Enable"
      Disable = "Disable"
      Copy = "Copy"
      Copied = "Copied"
      CallbackUrlLabel = "Redirect URI to register with the provider"
      ClientIdLabel = "Client ID"
      ClientSecretLabel = "Client secret"
      ClientSecretHint = "Stored encrypted. It can't be shown again once saved — only replaced."
      ReplaceSecretLabel = "Replace client secret"
      ReplaceSecretHint = "Leave blank to keep the saved secret."
      ProviderConsoleHint =
        fun provider -> $"From the OAuth application you created in {provider}'s developer console."
      SaveConnector = "Save connector"
      OriginMismatch =
        fun configured actual ->
          $"Umbral is configured with the public address {configured}, but you reached it at {actual}. "
          + "Social login will fail until they match: set UMBRAL__PUBLIC_ORIGIN to the address people actually use, then restart Umbral."
      ConnectorsLead = "Upstream providers people can sign in with. Enable the ones you've registered an OAuth app with."
      ConnectorDeleteButton = "Delete"
      ConnectorDeleteConfirm = fun name -> $"Delete the {name} connector? Nobody can sign in with {name} until you add it again, and the saved secret is erased. Their accounts are kept, and adding {name} back returns them to the same account."
      ConnectorDisableLastWarning = "This is the only enabled connector. Disabling it means nobody can sign in anywhere until you enable one again, so enable another first unless you mean to turn sign-in off."
      ConnectorDeleteConfirmOidc =
        fun name issuer ->
            $"Delete the {name} connector? Nobody can sign in with {name} until you add it again, and the saved secret is erased. Their accounts are kept: add it again with the same issuer, {issuer}, and people reconnect to the same account — under a different issuer they would start fresh."
      ConnectorDeleteLastWarning = "This is the only enabled connector. Deleting it means nobody can sign in anywhere until you add one again, and the saved secret is erased. Enable another first unless you mean to turn sign-in off."
      ConnectorNamingAdvice =
        fun provider ->
            $"Name the application you create in {provider}'s developer console after this Umbral instance (for example \"Umbral — home\"). People see that name on {provider}'s consent screen, and an instance name they recognise is one they trust. A name that belongs to another app makes it look as if they are signing in to that app."
      ConnectorDisplayNameLabel = "Button label"
      ConnectorDisplayNameHint = "Shown on the sign-in button as \"Sign in with …\". When two connectors of one kind exist, make the labels different so people can tell them apart."
      ConnectorSharedLabel = "Shared with every app"
      ConnectorSharedHint = "Shared connectors are offered to every app set to \"all shared connectors\". Untick it to offer this one only to the apps that choose it."
      ConnectorSharedBadge = "Shared"
      ConnectorNotSharedBadge = "Not shared"
      ConnectorOfferedNowhere = "Not shared, and no app has chosen it, so nobody sees it yet. Choose it on an app's Sign-in methods, or tick Shared."
      ConnectorAddTitle = "Add another connector"
      ConnectorAddLead = "A second Discord, GitHub or Google application, for example one with its own name for a single app, or an OpenID Connect provider such as Authentik, Keycloak or Pocket ID. The first connector of Discord, GitHub and Google needs no form here: use its card below."
      ConnectorAddKindLabel = "Provider"
      ConnectorAddSlugLabel = "Identifier"
      ConnectorAddSlugHint = "Lower-case letters, digits and hyphens, up to 32. It becomes part of the callback URL you register with the provider, so it can't be changed once saved."
      ConnectorAddButton = "Add connector"
      ConnectorAddOidcOption = "OpenID Connect provider"
      ConnectorIssuerLabel = "Issuer URL"
      ConnectorIssuerHint =
        "Only for an OpenID Connect provider (Authentik, Keycloak, Pocket ID, Authelia…): the address it calls its issuer, "
        + "like https://auth.example.com/application/o/umbral. Umbral reads everything else from it. Leave it empty for Discord, GitHub and Google."
      ConnectorIssuerFixedNote =
        "The identifier and the issuer can't be changed, because people's accounts are tied to the issuer and the callback URL is registered with the provider. "
        + "To change either, delete this provider and add it again: the same issuer brings everyone back to their account."
      ConnectorEmailTrustWarning =
        "Umbral trusts an email address that an OpenID Connect provider marks as verified, and uses it to recognise the same person across providers. "
        + "Add only a provider you run or trust. One that lets people claim email addresses they don't control would let them sign in as someone else."
      ConnectorOidcClientIdHint = "From the client you created for Umbral in your provider's admin area."
      ConnectorOidcSecretAgainHint = "Type the client secret again: Umbral never sends a secret back to your browser."
      ConnectorOidcProviderName = "your identity provider"
      ConnectorAddOidcDisplayNameRequired =
        "Give the provider a name people will recognise on the sign-in page, up to 64 characters."
      ConnectorAddOidcSlugInvalid =
        "Use up to 32 lowercase letters, digits or hyphens, like home or club-keycloak, for the identifier."
      ConnectorAddOidcSlugTaken =
        "Another connector already has that identifier, or it is the name of a built-in provider. Choose a different one, like home or club-keycloak."
      ConnectorAddOidcClientIdRequired = "Enter the client ID from your provider's admin area, then add the provider again."
      ConnectorAddOidcSecretRequired =
        "Umbral signs in as a confidential client, so it needs the client secret your provider generated. Paste it, then add the provider again."
      ConnectorAddOidcIssuerTaken =
        fun name ->
            $"{name} already uses this issuer, and two connectors for one provider would share every person under two names. Use that one, or delete it first."
      ConnectorAddOidcUnreachable =
        fun issuer reason ->
            $"Umbral could not reach {issuer}: {reason}. The provider was saved but is disabled — enable it once {issuer}/.well-known/openid-configuration opens in a browser."
      ConnectorEnableOidcUnreachable =
        fun issuer reason ->
            $"Umbral could not reach {issuer}: {reason}. It stays disabled — try again once {issuer}/.well-known/openid-configuration opens in a browser."
      ConnectorEnableOidcWrong = fun reason -> $"Not enabled. {reason}"

      UsersTitle = "Users"
      UsersLead = "People who have signed in at least once, across every app."
      UsersEmptyTitle = "No users yet"
      UsersEmptyBody = "Users appear here automatically the first time someone signs in."
      UserSearchLabel = "Search users"
      UserSearchPlaceholder = "Search by name or email"
      UserFirstSignInLabel = "First sign-in"
      UserLastSignInLabel = "Last sign-in"
      UserNeverSignedIn = "Never"
      UserEnabledBadge = "Enabled"
      UserDisabledBadge = "Disabled"
      UserEnableButton = "Enable"
      UserDisableButton = "Disable"
      UserDisableConfirm = fun name -> $"Disable {name}? They won't be able to sign in again, and their existing sessions and tokens stop working immediately."
      UserDeleteButton = "Delete"
      UserDeleteConfirm = fun name -> $"Delete {name}? This removes their account and every linked provider account permanently, and can't be undone."

      HomeTitle = "Welcome"
      HomeHeadline = "Umbral"
      HomeLead =
        "A self-hosted sign-in server that lets your apps offer social login (Discord, GitHub, Google, "
        + "any OpenID Connect provider) through one OpenID Connect issuer."
      HomeSigningInHeading = "Signing in?"
      HomeSigningInBody =
        "You don't start here. Open the app you want to use and choose to sign in; it brings you to Umbral's sign-in screen."
      HomeBuildingHeading = "Building an app?"
      HomeBuildingBody = "Point your OpenID Connect client at this issuer. Its settings are published at the discovery address."
      HomeDiscoveryLink = "OpenID Connect discovery document"
      HomeRunningHeading = "Running this instance?"
      HomeRunningBody = "Set up providers, register apps and manage people from the admin area."
      HomeAdminButton = "Open the admin area"

      AntiforgeryExpiredTitle = "This form expired"
      AntiforgeryExpiredBody = "Reload the page and try again."
      NotFoundTitle = "Page not found"
      NotFoundBody = "There's nothing at this address. Check the link and try again."
      ErrorTitle = "Something went wrong"
      ErrorBody = "Umbral couldn't complete that request. Try again, and contact the operator if it keeps happening."
      RateLimitedTitle = "Too many attempts"
      RateLimitedBody =
        fun minutes ->
            let unit = if minutes = 1 then "minute" else "minutes"
            $"Wait about {minutes} {unit}, then try again. Nothing was lost; the limit lifts by itself."
      BackHome = "Back to sign-in"
      BackToApp = "Go back to the app you were signing in to, and start again from there."

      ProtocolErrorTitle = "That sign-in request was rejected"
      ProtocolErrorBody =
        "The application sent a request Umbral could not accept. This is almost always a "
        + "configuration mismatch between the app and Umbral: check that its redirect URI is "
        + "registered here exactly as the app sends it, and that it uses PKCE with S256. If you were "
        + "just trying to sign in, go back to the app and try again, and tell its owner if it keeps happening."
      Notice = fun code -> noticeEn code |> Option.defaultValue "Reload the page to see its current state."
      SecretStoreName = secretStoreNameEn
      ConnectorSecretStoreFailed = connectorSecretStoreFailedEn
      ConnectorSecretLeftBehind = connectorSecretLeftBehindEn
      ConnectorSecretReplacedLeftBehind = connectorSecretReplacedLeftBehindEn
      MoveMoved = fun slug from target -> moveLine "moved" slug $"{from} → {target}"
      MoveWouldMove = fun slug from target -> $"would move  {slug}  {from} → {target}"
      MoveAlreadyIn = fun slug store -> moveLine "skipped" slug $"already in {store}"
      MoveChangedMeanwhile =
        fun slug -> moveLine "skipped" slug "changed while it was being moved, so nothing was overwritten; run the command again"
      MoveBusy = fun slug -> moveLine "skipped" slug "being edited; run move-secrets again"
      MoveFailed =
        fun slug from target why ->
            moveLine "failed" slug $"{from} → {target}: {why}. It still uses its old, readable copy"
      MoveLeftBehind =
        fun slug from target cause name ->
            moveLine
                "warning"
                slug
                $"moved {from} → {target}; the old copy could not be deleted from {from} ({moveStoreFailureEn from cause}): remove {name} there by hand, or anyone with access to it can still read the secret"
      MoveStoreFailure = moveStoreFailureEn
      MoveMismatch =
        fun target -> $"{secretStoreNameEn target} did not return what was written to it"
      MoveReferenceUnreadable = "its stored secret reference is not readable (re-enter the secret on the Connectors screen)"
      MoveSummary = moveSummaryEn
      SecretStoreProblem = secretStoreProblemEn
      SecretStoreLabel = secretStoreLabelEn
      SecretUnreadable = secretUnreadableEn
      SecretStoreProblemNotice = secretStoreProblemNoticeEn
      SecretStoreProbeWarning = secretStoreProbeWarningEn }

/// The code itself when it is one a screen has a message for, so that a screen
/// reading `?msg=` draws nothing, not even `data-notice`, for any other text.
/// Codes are the same in every language, so the English table decides.
let knownNotice (code: string) : string option =
    noticeEn code |> Option.map (fun _ -> code)

/// Umbral is English-only (research.md D10); every handler calls this the
/// same way the multi-language reference project calls its own equivalent,
/// so a future language is additive rather than a rewrite.
let current () : Texts = en
