/// What Umbral knows about each upstream provider, compiled in.
///
/// Endpoints, scopes and claim mapping are properties of the *kind*, not rows
/// in the connectors table. authentik and Ory make them editable because they
/// support a hundred providers; Umbral supports three, and an editable token
/// endpoint is surface area that only produces support tickets. A generic
/// `Oidc of issuerUrl` case is the additive way to open this up later.
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

    member this.Value =
        match this with
        | Discord -> "discord"
        | GitHub -> "github"
        | Google -> "google"
        | Fake -> "fake"

    member this.DisplayName =
        match this with
        | Discord -> "Discord"
        | GitHub -> "GitHub"
        | Google -> "Google"
        | Fake -> "Fake (development)"

let allKinds = [ Discord; GitHub; Google ]

let parseKind (value: string) =
    match value with
    | "discord" -> Some Discord
    | "github" -> Some GitHub
    | "google" -> Some Google
    | "fake" -> Some Fake
    | _ -> None

/// Where the provider's OAuth endpoints live, and what we must ask it for.
type ProviderEndpoints =
    { AuthorizationUrl: string
      TokenUrl: string
      /// Empty for Fake, which never leaves the process.
      UserInfoUrl: string
      /// The minimum needed for a stable id, an email, a display name and an
      /// avatar. Anything beyond this is the operator's `extra_scopes`.
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
    | Fake -> None

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
      ExtraScopes: string list
      IsEnabled: bool
      CreatedUtc: DateTimeOffset
      UpdatedUtc: DateTimeOffset }

    member this.Endpoints = endpointsFor this.Kind

    member this.ScopeString =
        (this.Endpoints.Scopes @ this.ExtraScopes)
        |> List.distinct
        |> String.concat " "
