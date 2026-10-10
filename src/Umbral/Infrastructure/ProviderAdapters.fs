/// The seam between Umbral and the three providers.
///
/// Everything provider-specific lives behind this interface, and everything
/// security-relevant lives *outside* it in OAuthRoundTrip.fs — state, PKCE and
/// nonce are written once and shared, so there is no per-provider security
/// code to get subtly wrong three times.
///
/// It is also the swap point. If OpenIddict ever ships first-class dynamic
/// client registrations, moving to its client stack is one implementation of
/// this interface rather than a rewrite (research.md D1).
module Umbral.Infrastructure.ProviderAdapters

open System
open System.Threading.Tasks
open Umbral.Domains.Connectors

/// What every provider is normalised to. The only shape downstream code sees.
type ProviderProfile =
    { /// The provider's IMMUTABLE identifier: Discord's snowflake (a string in
      /// JSON), GitHub's numeric id, Google's `sub`. Never a username: GitHub's
      /// `login` is mutable and reusable, so keying on it would make a renamed
      /// account a new user and a squatted handle an existing one.
      Subject: string
      Email: string option
      /// Only ever true when the provider itself said so. The linking rule
      /// refuses to join accounts on an unverified address, and this is the
      /// field that decision rests on.
      EmailVerified: bool
      DisplayName: string option
      /// Fully composed, never a raw hash — Discord returns an avatar hash that
      /// has to be assembled into a CDN URL.
      AvatarUrl: string option }

type ConnectorError =
    /// The person pressed cancel. Not an error worth alarming anyone about.
    | ProviderDeclined of reason: string
    /// Bad credentials, a revoked app, a rejected redirect URI. The operator
    /// needs to see this; the end user must not.
    | ProviderRejected of error: string
    | ProviderUnreachable of detail: string
    | MalformedResponse of detail: string

    /// What the operator sees in the security log.
    member this.Detail =
        match this with
        | ProviderDeclined reason -> $"declined: {reason}"
        | ProviderRejected error -> $"rejected: {error}"
        | ProviderUnreachable detail -> $"unreachable: {detail}"
        | MalformedResponse detail -> $"malformed_response: {detail}"

/// Everything a provider needs to build its authorize URL. Passed in rather
/// than read, so BuildAuthorizeUrl stays pure and golden-testable.
type AuthorizeRequest =
    { Connector: Connector
      RedirectUri: Uri
      State: string
      CodeChallenge: string
      Nonce: string }

type ExchangeRequest =
    { Connector: Connector
      ClientSecret: string
      RedirectUri: Uri
      Code: string
      CodeVerifier: string
      Nonce: string }

type IProviderAdapter =
    abstract Kind: ProviderKind

    /// Pure. A golden test can assert the exact URL — scope string,
    /// code_challenge_method, redirect_uri — which is what catches the typo
    /// that otherwise costs an afternoon of provider-side errors.
    abstract BuildAuthorizeUrl: AuthorizeRequest -> Uri

    /// The only method that touches the network: exchange the code, fetch the
    /// profile, normalise it.
    abstract ExchangeAndFetch: ExchangeRequest -> Task<Result<ProviderProfile, ConnectorError>>

/// Query-string building, shared so every adapter escapes identically.
let buildUrl (baseUrl: string) (parameters: (string * string) list) =
    let query =
        parameters
        |> List.filter (fun (_, value) -> not (String.IsNullOrEmpty value))
        |> List.map (fun (key, value) -> $"{Uri.EscapeDataString key}={Uri.EscapeDataString value}")
        |> String.concat "&"

    Uri(if String.IsNullOrEmpty query then baseUrl else $"{baseUrl}?{query}")

/// The authorize parameters every provider shares. An adapter adds its own.
let commonAuthorizeParameters (request: AuthorizeRequest) =
    let endpoints = request.Connector.Endpoints

    [ "client_id", request.Connector.ClientId
      "redirect_uri", request.RedirectUri.ToString()
      "response_type", "code"
      "scope", request.Connector.ScopeString
      "state", request.State
      if endpoints.SupportsPkce then
          "code_challenge", request.CodeChallenge
          "code_challenge_method", "S256"
      if endpoints.UsesNonce then
          "nonce", request.Nonce ]

/// Registry over the concrete adapters, so a handler asks for a kind rather
/// than knowing the implementations.
type Registry(adapters: IProviderAdapter seq) =
    let byKind = adapters |> Seq.map (fun a -> a.Kind, a) |> Map.ofSeq

    member _.For(kind: ProviderKind) = Map.tryFind kind byKind
