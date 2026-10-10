/// Every user-facing string of the admin UI and the end-user connector picker.
/// English only (D10/D11 in research.md) — kept as one record anyway, because
/// that is what makes SC-010 ("every message names the next action")
/// auditable by reading a single file. No other file may hold a literal
/// shown to an operator or an end user.
module Umbral.Views.Texts

type Texts =
    { SiteName: string
      Footer: string
      SkipToContent: string

      NavApps: string
      NavConnectors: string
      NavUsers: string
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
      SetupTooShort: string
      SetupMismatch: string
      SetupAlreadyDone: string

      // Operator sign-in (/admin/signin)
      SignInTitle: string
      SignInLead: string
      SignInButton: string
      SignInIncorrect: string
      SignInRateLimited: string

      // Replacing the operator credential once signed in (FR-017a)
      CredentialReplaceTitle: string
      CredentialReplaceLead: string
      CurrentCredentialLabel: string
      CredentialReplaced: string
      CredentialCurrentIncorrect: string

      // End-user connector picker (/signin) — not an admin screen
      PickerTitle: string
      PickerLead: string
      SignInWith: string -> string
      PickerNoConnectors: string
      PickerSignInFailed: string
      PickerAccessDenied: string
      PickerProviderUnreachable: string

      // Screen 1 — Apps
      AppsTitle: string
      AppsLead: string
      AppsNeedConnectorTitle: string
      AppsNeedConnectorBody: string
      AppsEmptyTitle: string
      AppsEmptyBody: string
      AppRegisterButton: string
      AppNameLabel: string
      AppNameRequired: string
      AppRedirectUriLabel: string
      AppRedirectUriHint: string
      AppRedirectUriInvalid: string
      AppPostLogoutRedirectUriLabel: string
      AppTypeLabel: string
      AppTypeHint: string
      AppCreateButton: string
      AppClientIdLabel: string
      AppIssuerUrlLabel: string
      AppSecretShownTitle: string
      AppSecretShownBody: string
      AppSecretLabel: string
      AppSecretOnceWarning: string
      AppBackToApps: string
      AppRegenerateButton: string
      AppRegenerateConfirm: string -> string
      AppRegenerateNotice: string
      AppDeleteButton: string
      AppDeleteConfirm: string -> string
      AppDeletedNotice: string

      // Screen 2 — Connectors
      ConnectorsTitle: string
      ConnectorsLead: string
      ConnectorClientIdLabel: string
      ConnectorClientSecretLabel: string
      ConnectorSecretMasked: string -> string
      ConnectorSecretReplaceButton: string
      ConnectorSecretNeverSetHint: string
      ConnectorSaveButton: string
      ConnectorCallbackUrlLabel: string
      ConnectorCallbackUrlHint: string
      ConnectorEnableButton: string
      ConnectorDisableButton: string
      ConnectorEnabledBadge: string
      ConnectorDisabledBadge: string
      ConnectorDeleteButton: string
      ConnectorDeleteConfirm: string -> string
      ConnectorDisableConfirm: string -> string
      ConnectorDisableLastWarning: string
      ConnectorSavedNotice: string
      ConnectorDeletedNotice: string
      ConnectorCredentialsRejected: string -> string
      GoogleLimitationNotice: string

      // The origin-mismatch banner (D4), naming both origins
      OriginMismatchBanner: string -> string -> string

      // Screen 3 — Users
      UsersTitle: string
      UsersLead: string
      UsersEmptyTitle: string
      UsersEmptyBody: string
      UserSearchLabel: string
      UserSearchPlaceholder: string
      UserDisplayNameLabel: string
      UserEmailLabel: string
      UserProvidersLabel: string
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
      UserDeletedNotice: string

      // Config export (GET /admin/config)
      ConfigExportTitle: string
      ConfigExportLead: string
      ConfigExportDownloadButton: string
      ConfigExportSecretNote: string

      // Generic chrome and errors (FR-037: every message names the next action)
      CopyButton: string
      CopiedNotice: string
      AntiforgeryExpiredTitle: string
      AntiforgeryExpiredBody: string
      NotFoundTitle: string
      NotFoundBody: string
      ErrorTitle: string
      ErrorBody: string
      RateLimitedTitle: string
      RateLimitedBody: string
      BackToSignIn: string
      BackHome: string

      /// Flash messages addressed by a short code (`data-notice="{code}"`).
      /// Codes not recognised fall back to the code itself, which is a bug
      /// in the caller, not something an operator should ever see.
      ProtocolErrorTitle: string
      ProtocolErrorBody: string
      Notice: string -> string }

let en: Texts =
    { SiteName = "Umbral"
      Footer = "Umbral — self-hosted social login"
      SkipToContent = "Skip to content"

      NavApps = "Apps"
      NavConnectors = "Connectors"
      NavUsers = "Users"
      NavSignOut = "Sign out"

      SetupTitle = "Set up Umbral"
      SetupLead = "Choose the operator credential. It's the only way to reach this admin area, so keep it somewhere safe — there's no recovery screen if you lose it."
      CredentialLabel = "Operator credential"
      CredentialConfirmLabel = "Confirm credential"
      CredentialHint = "At least 12 characters. This is not tied to any connector, so a broken Discord or Google app can never lock you out."
      SetupButton = "Set credential and continue"
      SetupTokenLabel = "Setup token"
      SetupTokenHint =
        "Printed in the container log when Umbral started. Run `docker logs umbral` and copy the token shown there."
      SetupTooShort = "Use at least 12 characters, then submit the form again."
      SetupMismatch = "The two values don't match. Type the same credential in both fields and try again."
      SetupAlreadyDone = "Setup is already complete. Sign in instead."

      SignInTitle = "Operator sign-in"
      SignInLead = "Sign in with the operator credential to manage Apps, Connectors and Users."
      SignInButton = "Sign in"
      SignInIncorrect = "That credential wasn't right. Check it and try again."
      SignInRateLimited = "Too many failed attempts. Wait a few minutes before trying again."

      CredentialReplaceTitle = "Replace the operator credential"
      CredentialReplaceLead = "Enter your current credential, then the new one. Every other admin session is signed out immediately."
      CurrentCredentialLabel = "Current credential"
      CredentialReplaced = "Operator credential replaced. Use it next time you sign in."
      CredentialCurrentIncorrect = "That's not the current credential. Check it and try again."

      PickerTitle = "Sign in"
      PickerLead = "Choose how you'd like to sign in."
      SignInWith = fun provider -> $"Sign in with {provider}"
      PickerNoConnectors = "No sign-in option is available right now. Contact the operator of this service."
      PickerSignInFailed = "Sign-in was cancelled or didn't complete. Try again, or choose a different option below."
      PickerAccessDenied = "Your account is disabled. Contact the operator if you believe this is a mistake."
      PickerProviderUnreachable = "That provider isn't responding right now. Try again in a moment, or choose a different option below."

      AppsTitle = "Apps"
      AppsLead = "Apps that delegate sign-in to Umbral. Each gets its own client ID and secret."
      AppsNeedConnectorTitle = "Add a connector first"
      AppsNeedConnectorBody = "An app needs at least one enabled connector so people have a way to sign in. Go to Connectors and set one up."
      AppsEmptyTitle = "No apps yet"
      AppsEmptyBody = "Register your first app to get an issuer URL, a client ID and a client secret."
      AppRegisterButton = "Register app"
      AppNameLabel = "Name"
      AppNameRequired = "Enter a name for this app, then save again."
      AppRedirectUriLabel = "Redirect URI"
      AppRedirectUriHint = "The exact, absolute callback URL the app expects — no wildcards."
      AppRedirectUriInvalid = "Enter an absolute URL with no wildcards, for example https://app.example.com/callback, then save again."
      AppPostLogoutRedirectUriLabel = "Post-logout redirect URI (optional)"
      AppTypeLabel = "App type"
      AppTypeHint = "Sets the app's permissions. There's no separate checkbox list to get wrong."
      AppCreateButton = "Create app"
      AppClientIdLabel = "Client ID"
      AppIssuerUrlLabel = "Issuer URL"
      AppSecretShownTitle = "App created"
      AppSecretShownBody = "Copy the client ID and secret into the app's configuration now."
      AppSecretLabel = "Client secret"
      AppSecretOnceWarning = "This secret is shown only once and Umbral keeps no way to show it again. If you lose it, come back here and regenerate it."
      AppBackToApps = "Back to Apps"
      AppRegenerateButton = "Regenerate secret"
      AppRegenerateConfirm = fun name -> $"Regenerate the secret for \"{name}\"? The app will stop working with its old secret until you update its configuration with the new one."
      AppRegenerateNotice = "New secret generated. Update the app's configuration before its old secret is used again."
      AppDeleteButton = "Delete app"
      AppDeleteConfirm = fun name -> $"Delete \"{name}\"? Every token already issued to it stops working immediately, and this can't be undone."
      AppDeletedNotice = "App deleted. Its tokens no longer work."

      ConnectorsTitle = "Connectors"
      ConnectorsLead = "Upstream providers people can sign in with. Enable the ones you've registered an OAuth app with."
      ConnectorClientIdLabel = "Client ID"
      ConnectorClientSecretLabel = "Client secret"
      ConnectorSecretMasked = fun lastFour -> $"Saved, ending in {lastFour}"
      ConnectorSecretReplaceButton = "Replace secret"
      ConnectorSecretNeverSetHint = "No secret saved yet."
      ConnectorSaveButton = "Save"
      ConnectorCallbackUrlLabel = "Callback URL"
      ConnectorCallbackUrlHint = "Paste this into the provider's console as the redirect URI. Umbral computes it — never type it by hand."
      ConnectorEnableButton = "Enable"
      ConnectorDisableButton = "Disable"
      ConnectorEnabledBadge = "Enabled"
      ConnectorDisabledBadge = "Disabled"
      ConnectorDeleteButton = "Delete"
      ConnectorDeleteConfirm = fun name -> $"Delete the {name} connector? Anyone who signs in only with {name} will no longer be able to, and this can't be undone."
      ConnectorDisableConfirm = fun name -> $"Disable {name}? People won't be able to sign in with it until it's enabled again."
      ConnectorDisableLastWarning = "This is the only enabled connector. Disabling it means nobody can sign in anywhere until you enable one again."
      ConnectorSavedNotice = "Connector saved. The new credentials are used on the next sign-in — no restart needed."
      ConnectorDeletedNotice = "Connector deleted."
      ConnectorCredentialsRejected = fun provider -> $"{provider} rejected these credentials. Double-check the client ID and secret you pasted, then save again."
      GoogleLimitationNotice = "Google refuses a redirect URI that is a raw IP address or that isn't https (except localhost) — so a typical \"http://192.168.x.x\" homelab address can't be registered with Google at all. Either put Umbral behind a domain name with HTTPS, or skip Google and use Discord or GitHub instead."

      OriginMismatchBanner =
        fun configured apparent ->
            $"This request arrived as {apparent}, but Umbral is configured with the public origin {configured}. Fix UMBRAL__PUBLIC_ORIGIN or the proxy in front of Umbral so the two agree — a mismatch here is why redirect URIs fail."

      UsersTitle = "Users"
      UsersLead = "People who have signed in at least once, across every app."
      UsersEmptyTitle = "No users yet"
      UsersEmptyBody = "Users appear here automatically the first time someone signs in."
      UserSearchLabel = "Search users"
      UserSearchPlaceholder = "Search by name or email"
      UserDisplayNameLabel = "Name"
      UserEmailLabel = "Email"
      UserProvidersLabel = "Signed in with"
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
      UserDeletedNotice = "User deleted."

      ConfigExportTitle = "Export configuration"
      ConfigExportLead = "The current apps and connectors as one YAML file. Secrets are written as environment-variable references, never in clear text."
      ConfigExportDownloadButton = "Download umbral.yaml"
      ConfigExportSecretNote = "App secrets can't be exported — OpenIddict only stores a hash of them. An app imported from this file gets a fresh secret; update its configuration with the new one."

      CopyButton = "Copy"
      CopiedNotice = "Copied"
      AntiforgeryExpiredTitle = "This form expired"
      AntiforgeryExpiredBody = "Reload the page and try again."
      NotFoundTitle = "Page not found"
      NotFoundBody = "There's nothing at this address. Check the link, or go back to sign-in."
      ErrorTitle = "Something went wrong"
      ErrorBody = "Umbral couldn't complete that request. Try again, and contact the operator if it keeps happening."
      RateLimitedTitle = "Too many attempts"
      RateLimitedBody = "Wait a few minutes, then try again."
      BackToSignIn = "Back to sign-in"
      BackHome = "Back to sign-in"

      ProtocolErrorTitle = "That sign-in request was rejected"
      ProtocolErrorBody =
        "The application sent a request Umbral could not accept. This is almost always a "
        + "configuration mismatch between the app and Umbral: check that its redirect URI is "
        + "registered here exactly as the app sends it, and that it uses PKCE with S256."
      Notice =
        fun code ->
            match code with
            | "setup-token-invalid" ->
                "That setup token doesn't match. Run `docker logs umbral` and copy the token printed when the container started."
            | "setup-mismatch" -> "The two values don't match. Type the same credential in both fields and try again."
            | "setup-too-short" -> "Use at least 12 characters, then submit the form again."
            | "signin-incorrect" -> "That credential is not correct. Try again."
            | "sign-in-failed" -> "Sign-in didn't complete. Try again, or choose a different option."
            | "access-denied" -> "You cancelled the sign-in. Choose an option below to try again."
            | "provider-unreachable" ->
                "That provider could not be reached. Try again in a moment, or choose a different option."
            | "account-disabled" -> "This account has been disabled. Contact whoever runs this Umbral."
            | "app-created" -> "App registered."
            | "app-secret-regenerated" -> "New secret generated."
            | "app-deleted" -> "App deleted."
            | "connector-saved" -> "Connector saved."
            | "connector-enabled" -> "Connector enabled."
            | "connector-disabled" -> "Connector disabled."
            | "connector-deleted" -> "Connector deleted."
            | "user-enabled" -> "User enabled."
            | "user-disabled" -> "User disabled."
            | "user-deleted" -> "User deleted."
            | "credential-replaced" -> "Operator credential replaced."
            | other -> other }

/// Umbral is English-only (research.md D10); every handler calls this the
/// same way the multi-language reference project calls its own equivalent,
/// so a future language is additive rather than a rewrite.
let current () : Texts = en
