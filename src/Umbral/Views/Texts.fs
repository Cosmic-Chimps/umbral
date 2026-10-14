/// Every user-facing string of the admin UI and the end-user connector picker.
/// English only (D10/D11 in research.md) — kept as one record anyway, because
/// that is what makes SC-010 ("every message names the next action")
/// auditable by reading a single file. No other file may hold a literal
/// shown to an operator or an end user.
module Umbral.Views.Texts

module SecretStoreSettings = Umbral.Infrastructure.SecretStores.Settings

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
      /// A write to the store was refused: the store's display name and the
      /// cause word (`StoreFailure.Cause`). Shown inline by the OIDC add form
      /// and, through the single-use notice cookie, by the redirecting paths.
      ConnectorSecretStoreFailed: string -> string -> string
      /// A delete left the secret in its manager: the store's display name and
      /// the name the operator has to remove it under there.
      ConnectorSecretLeftBehind: string -> string -> string

      // Startup refusals about the external secret store (spec 005, FR-005):
      // one line per problem, each saying which variable and what to set.
      SecretStoreProblem: SecretStoreSettings.SettingProblem -> string }

/// The display name of a store, by the id a `SecretRef` carries.
let private secretStoreNameEn (storeId: string) : string =
    match storeId with
    | "local" -> "this instance (encrypted)"
    | "aws" -> "AWS Secrets Manager"
    | "gcp" -> "Google Cloud Secret Manager"
    | "azure" -> "Azure Key Vault"
    | "bellabaxter" -> "BellaBaxter"
    | other -> other

/// What a `StoreFailure.Cause` word means to someone who has to fix it.
let private causeEn (cause: string) : string =
    match cause with
    | "unreachable" -> "could not be reached"
    | "access_denied" -> "refused Umbral's credentials or permissions"
    | "not_configured" -> "is not set up on this instance"
    | "rejected" -> "rejected the request"
    | _ -> "failed"

let private connectorSecretStoreFailedEn (store: string) (cause: string) : string =
    $"{store} {causeEn cause}, so the client secret was not saved and nothing was changed. Check that Umbral's credentials for it are valid, that they may write secrets, and that it is reachable from this host, following the walkthrough in docs/secret-stores/. Then enter the secret again."

let private connectorSecretLeftBehindEn (store: string) (name: string) : string =
    $"The connector was deleted, but its client secret {name} could not be removed from {store}. Delete it there by hand, or anyone with access to that store can still read it."

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
      SecretStoreProblem = secretStoreProblemEn }

/// The code itself when it is one a screen has a message for, so that a screen
/// reading `?msg=` draws nothing, not even `data-notice`, for any other text.
/// Codes are the same in every language, so the English table decides.
let knownNotice (code: string) : string option =
    noticeEn code |> Option.map (fun _ -> code)

/// Umbral is English-only (research.md D10); every handler calls this the
/// same way the multi-language reference project calls its own equivalent,
/// so a future language is additive rather than a rewrite.
let current () : Texts = en
