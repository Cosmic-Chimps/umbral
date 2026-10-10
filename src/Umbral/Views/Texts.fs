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
      ConnectorDisableLastWarning: string
      ConnectorDeleteLastWarning: string

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

      // Config export (GET /admin/config)

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
      ConnectorDeleteLastWarning = "This is the only enabled connector. Deleting it means nobody can sign in anywhere until you add one again, and the saved secret is erased. Enable another first unless you mean to turn sign-in off."

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
      Notice =
        fun code ->
            match code with
            | "setup-token-invalid" ->
                "That setup token doesn't match. Run `docker logs umbral` and copy the token printed when the container started."
            | "setup-mismatch" -> "The two values don't match. Type the same credential in both fields and try again."
            | "setup-too-short" -> "Use at least 12 characters, then submit the form again."
            | "signin-incorrect" ->
                "That credential isn't correct. Check it and try again, or run `docker exec -it umbral /app/Umbral reset-credential` on the host if you've lost it."
            | "sign-in-failed" -> "Sign-in didn't complete. Try again, or choose a different option."
            | "access-denied" -> "You cancelled the sign-in. Choose an option below to try again."
            | "provider-unreachable" ->
                "That provider could not be reached. Try again in a moment, or choose a different option."
            | "account-disabled" -> "This account has been disabled. Contact whoever runs this Umbral."
            | "app-created" -> "App registered."
            | "app-name-required" -> "Give the app a name, then save again."
            | "app-redirect-uri-invalid" ->
                "Enter an absolute URL with no wildcards, for example https://app.example.com/callback, then save again."
            | "app-type-invalid" -> "Choose an app type, then save again."
            | "app-not-found" -> "That app no longer exists. Choose one from the list below, or register it again."
            | "app-no-secret" ->
                "Single-page and native apps have no secret to replace — they use PKCE instead. If sign-in fails for this app, check that it sends a PKCE challenge."
            | "user-not-found" -> "That user no longer exists. Choose one from the list below."
            | "app-secret-regenerated" -> "New secret generated."
            | "app-deleted" -> "App deleted."
            | "connector-saved" -> "Connector saved."
            | "connector-unknown" -> "That connector isn't one Umbral supports. Choose one of the connectors listed on this screen."
            | "connector-client-id-required" -> "Enter the client ID from the provider's console, then save again."
            | "connector-secret-required" -> "Enter the client secret from the provider's console, then save again."
            | "connector-last-enabled" ->
                "This is the only enabled connector. Turning it off means nobody can sign in to any app. Press it again to confirm."

            | "connector-delete-last-enabled" ->
                "This is the only enabled connector. Nothing was deleted. Deleting it means nobody can sign in to any app — use Delete and confirm to go ahead."

            | "connector-enabled" -> "Connector enabled."
            | "connector-disabled" -> "Connector disabled."
            | "connector-deleted" -> "Connector deleted."
            | "user-enabled" -> "User enabled."
            | "user-disabled" -> "User disabled."
            | "user-deleted" -> "User deleted."
            | "credential-replaced" -> "Operator credential replaced."
            | "credential-current-incorrect" -> "That's not the current credential. Check it and try again."
            | other -> other }

/// Umbral is English-only (research.md D10); every handler calls this the
/// same way the multi-language reference project calls its own equivalent,
/// so a future language is additive rather than a rewrite.
let current () : Texts = en
