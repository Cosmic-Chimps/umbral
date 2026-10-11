/// What Umbral knows about each upstream provider, compiled in.
///
/// Endpoints, scopes and claim mapping are properties of the *kind*, not rows
/// in the connectors table. authentik and Ory make them editable because they
/// support a hundred providers; Umbral supports three, and an editable token
/// endpoint is surface area that only produces support tickets. The `Oidc` kind
/// is the additive way this was opened up: it carries one issuer URL and reads
/// everything else from the provider's own discovery document, so the endpoints
/// are still never typed in by hand.
module Umbral.Domains.Connectors

open System

type ProviderKind =
    | Discord
    | GitHub
    | Google
    /// Development and tests only. Hard-gated: Umbral refuses to start if a
    /// connector of this kind exists outside Development with the flag unset.
    /// A fake identity provider that ships enabled is a total authentication
    /// bypass.
    | Fake
    /// Any provider that publishes an OpenID Connect discovery document,
    /// configured by its issuer URL alone (specs/002-generic-oidc-connector).
    /// Its endpoints are not compiled in: `ProviderAdapters.IProviderAdapter.Resolve`
    /// reads them from the provider.
    | Oidc

    member this.Value =
        match this with
        | Discord -> "discord"
        | GitHub -> "github"
        | Google -> "google"
        | Fake -> "fake"
        | Oidc -> "oidc"

    member this.DisplayName =
        match this with
        | Discord -> "Discord"
        | GitHub -> "GitHub"
        | Google -> "Google"
        | Fake -> "Fake (development)"
        | Oidc -> "OpenID Connect"

/// The three providers with a card of their own. Deliberately not `Oidc`: an
/// OpenID Connect provider has no "first connector" to configure in place, it
/// is always added by issuer.
let allKinds = [ Discord; GitHub; Google ]

let parseKind (value: string) =
    match value with
    | "discord" -> Some Discord
    | "github" -> Some GitHub
    | "google" -> Some Google
    | "fake" -> Some Fake
    | "oidc" -> Some Oidc
    | _ -> None

/// Where the provider's OAuth endpoints live, and what we must ask it for.
type ProviderEndpoints =
    { AuthorizationUrl: string
      TokenUrl: string
      /// Empty for Fake, which never leaves the process.
      UserInfoUrl: string
      /// The minimum needed for a stable id, an email, a display name and an
      /// avatar. These are all the scopes Umbral requests.
      Scopes: string list
      /// Discord does not document PKCE support; two third-party sources say
      /// S256-only. Shipped on, with this flag so a provider that rejects the
      /// challenge is a one-line change rather than a redesign (T002).
      SupportsPkce: bool
      /// Google returns an id_token and requires a nonce with it. The others
      /// are plain OAuth 2.0 and have no id_token at all.
      UsesNonce: bool }

let endpointsFor (kind: ProviderKind) =
    match kind with
    | Discord ->
        { AuthorizationUrl = "https://discord.com/oauth2/authorize"
          TokenUrl = "https://discord.com/api/oauth2/token"
          UserInfoUrl = "https://discord.com/api/v10/users/@me"
          Scopes = [ "identify"; "email" ]
          SupportsPkce = true
          UsesNonce = false }

    | GitHub ->
        { AuthorizationUrl = "https://github.com/login/oauth/authorize"
          TokenUrl = "https://github.com/login/oauth/access_token"
          UserInfoUrl = "https://api.github.com/user"
          // read:user for the private user response, user:email for the
          // addresses — /user.email is null whenever the user keeps it private.
          Scopes = [ "read:user"; "user:email" ]
          SupportsPkce = true
          UsesNonce = false }

    | Google ->
        { AuthorizationUrl = "https://accounts.google.com/o/oauth2/v2/auth"
          TokenUrl = "https://oauth2.googleapis.com/token"
          UserInfoUrl = "https://openidconnect.googleapis.com/v1/userinfo"
          // `openid` is mandatory: without it Google returns no id_token.
          Scopes = [ "openid"; "email"; "profile" ]
          SupportsPkce = true
          UsesNonce = true }

    | Fake ->
        { AuthorizationUrl = "/dev/fake-provider/authorize"
          TokenUrl = ""
          UserInfoUrl = ""
          Scopes = []
          SupportsPkce = false
          UsesNonce = false }

    | Oidc ->
        // Only what holds for every OpenID Connect provider. The URLs are empty
        // because they are the provider's to say: `Resolve` fills them from its
        // discovery document, and no authorize URL is ever built from this
        // static entry (specs/002 research R3).
        { AuthorizationUrl = ""
          TokenUrl = ""
          UserInfoUrl = ""
          Scopes = [ "openid"; "email"; "profile" ]
          SupportsPkce = true
          UsesNonce = true }

/// What an operator must be told when they pick this provider, beyond the
/// generic instructions. Empty for the ones with nothing surprising.
let setupCaveat (kind: ProviderKind) =
    match kind with
    | Google ->
        // Not fixable in code, and the single most likely thing to waste a
        // homelabber's evening: the natural LAN address cannot be registered
        // with Google at all.
        Some
            "Google does not accept a redirect URI that is an IP address, or any http address other \
             than localhost. A LAN address like http://192.168.1.50:8080 cannot be registered. Use a \
             domain name with HTTPS in front of Umbral, or forward a port so the address is \
             http://localhost."
    | Discord
    | GitHub
    | Fake
    | Oidc -> None

/// A configured connector, as stored.
type Connector =
    { /// Immutable: it is in the callback URL the operator registered with the
      /// provider. Renaming the display name must not break every login.
      Slug: string
      Kind: ProviderKind
      DisplayName: string
      ClientId: string
      /// "<store>:<locator>" — opaque outside the ISecretStore that wrote it.
      ClientSecretRef: string
      ClientSecretHint: string
      /// Only for `Oidc`: the normalised issuer URL (`normaliseIssuer`). Part
      /// of every person's identity there (`oidcSubject`), so it never changes
      /// once saved. None for every other kind.
      IssuerUrl: string option
      IsEnabled: bool
      /// A shared connector is offered to every app set to "all shared
      /// connectors". One that is not shared is offered only to the apps whose
      /// chosen set names it (FR-014).
      IsShared: bool
      CreatedUtc: DateTimeOffset
      UpdatedUtc: DateTimeOffset }

    member this.Endpoints = endpointsFor this.Kind

    member this.ScopeString =
        this.Endpoints.Scopes |> List.distinct |> String.concat " "

/// An app's connector choice (FR-005). No row in `app_connector_choices` means
/// AllShared, which is also the default for a new app.
type AppChoice =
    | AllShared
    /// A chosen set of connector slugs. Empty is a valid set: the app then
    /// offers nothing, and stays limited rather than widening (FR-011).
    | Only of Set<string>

/// The enabled connectors an app offers. AllShared: every enabled connector
/// that is shared (FR-006, FR-014). Only: every enabled connector whose slug is
/// in the set, shared or not (FR-007). A slug in the set that no connector has
/// offers nothing and widens nothing.
///
/// The only place this rule is written. The sign-in page, the authorize
/// endpoint's session check (FR-008), the admin warnings (FR-010) and the Apps
/// screen all call it, so they cannot disagree.
let offeredTo (choice: AppChoice) (connectors: Connector list) : Connector list =
    connectors
    |> List.filter (fun connector ->
        connector.IsEnabled
        && match choice with
           | AllShared -> connector.IsShared
           | Only slugs -> Set.contains connector.Slug slugs)

/// The apps left with nothing offered, given the connectors as they would be
/// after a change. Input is each app's client id, name and choice; output is
/// (client id, name) in the input order. Every FR-010 warning names these.
let strandedApps (apps: (string * string * AppChoice) list) (connectors: Connector list) : (string * string) list =
    apps
    |> List.filter (fun (_, _, choice) -> List.isEmpty (offeredTo choice connectors))
    |> List.map (fun (clientId, name, _) -> clientId, name)

// ---------------------------------------------------------------------------
// OpenID Connect issuers (specs/002-generic-oidc-connector, research R2 and R7).
// Pure, and shared by the admin form and the configuration file, so the two can
// never accept different addresses.
// ---------------------------------------------------------------------------

/// Why an issuer URL was refused. The text is what the operator reads, in the
/// words of the contract's Add-form table: it names the rule and the way out.
type IssuerError =
    | IssuerEmpty
    /// Not an address at all, or a relative one.
    | IssuerNotAbsolute
    /// Anything but https — or http for a host that is not a loopback address
    /// on an instance that allows it.
    | IssuerNotHttps
    | IssuerHasQueryOrFragment

    member this.Message =
        match this with
        | IssuerEmpty ->
            "Enter the issuer URL your provider shows, like https://auth.example.com/application/o/umbral."
        | IssuerNotAbsolute ->
            "The issuer must be a full address starting with https://, like https://auth.example.com/application/o/umbral."
        | IssuerNotHttps ->
            "The issuer must start with https://. Put the provider behind https — the walkthrough shows how with the reverse proxy that already fronts Umbral."
        | IssuerHasQueryOrFragment ->
            "The issuer is an address without a ? or # part. Copy it exactly as your provider shows it, including any path."

/// Hosts that may be reached over plain http when the instance itself runs with
/// the Development escape hatch (research R2, Clarification Q1). `Uri.Host`
/// already lower-cases and brackets an IPv6 literal.
let isLoopbackHost (host: string) =
    host = "localhost" || host = "127.0.0.1" || host = "[::1]"

/// The one spelling of an issuer Umbral stores and compares: an absolute https
/// URL with the host lower-cased, no query, no fragment and one trailing slash
/// dropped, so `https://Auth.example.com/o/` and `https://auth.example.com/o`
/// are the same provider and the same people (research R7).
///
/// `allowLocalhostHttp` admits `http://` for a loopback host only. The caller
/// passes whether this instance runs with the Development insecure-http flag.
///
/// `System.Uri` is not trusted to say "not absolute": on Unix it reads a bare
/// `/path` as a `file://` URL rather than failing, so the scheme is checked
/// before anything else is believed.
let normaliseIssuer (allowLocalhostHttp: bool) (input: string | null) : Result<string, IssuerError> =
    let text =
        match input with
        | null -> ""
        | value -> value.Trim()

    if text = "" then
        Error IssuerEmpty
    elif text.Contains '?' || text.Contains '#' then
        Error IssuerHasQueryOrFragment
    else
        let parsed =
            match Uri.TryCreate(text, UriKind.Absolute) with
            | true, uri -> Option.ofObj uri
            | _ -> None

        match parsed with
        | Some uri when uri.IsAbsoluteUri && (uri.Scheme = Uri.UriSchemeHttp || uri.Scheme = Uri.UriSchemeHttps) ->
            let allowed =
                uri.Scheme = Uri.UriSchemeHttps
                || (allowLocalhostHttp && isLoopbackHost uri.Host)

            if not allowed then
                Error IssuerNotHttps
            else
                // One slash only: `/a//` keeps its meaning as the path `/a/`.
                let path = uri.AbsolutePath
                let path = if path.EndsWith '/' then path.Substring(0, path.Length - 1) else path
                Ok(uri.GetLeftPart UriPartial.Authority + path)

        | Some uri when uri.IsAbsoluteUri && uri.Scheme <> Uri.UriSchemeFile -> Error IssuerNotHttps
        | _ -> Error IssuerNotAbsolute

/// What `linked_accounts.provider_subject` holds for an `oidc` sign-in:
/// `<issuer>#<sub>`. A `sub` is unique only within its issuer and many products
/// issue small integers that repeat across instances, so the issuer is part of
/// the identity (OIDC Core §2); and it is the issuer, not the connector's slug,
/// so deleting a connector and adding it back finds the same people (research R7).
/// `issuer` is already normalised.
let oidcSubject (issuer: string) (sub: string) = $"{issuer}#{sub}"

/// The inverse of `oidcSubject`. An issuer cannot contain `#` (OIDC Discovery
/// §3), so the first one separates the two; a `sub` may contain more.
let parseOidcSubject (subject: string) : (string * string) option =
    match subject.IndexOf '#' with
    | index when index > 0 && index < subject.Length - 1 -> Some(subject.Substring(0, index), subject.Substring(index + 1))
    | _ -> None
