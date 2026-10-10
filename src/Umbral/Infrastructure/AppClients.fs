/// How an App is shaped as an OpenIddict application.
///
/// An App is an OIDC client stored entirely by OpenIddict. Two doors create
/// one — the Apps screen and the `apps:` list of the configuration file — and
/// both must produce the same client, or "the file is equivalent to the admin
/// UI" (Principle V) stops being true in the one place a login would notice:
/// an app whose authorization works and whose token exchange fails. So the
/// permission set, the PKCE requirement and the client type are written once,
/// here.
module Umbral.Infrastructure.AppClients

open System
open OpenIddict.Abstractions
open type OpenIddict.Abstractions.OpenIddictConstants

/// The one permission set (T072). The brief names a single set — derived from
/// the app-type choice rather than exposed as checkboxes — and applies it
/// identically whether the client is confidential or public; the only things
/// that vary by type are ClientType, ApplicationType and whether a secret is
/// issued at all.
let permissions =
    [ Permissions.Endpoints.Authorization
      Permissions.Endpoints.Token
      Permissions.Endpoints.EndSession
      Permissions.Endpoints.Revocation
      Permissions.GrantTypes.AuthorizationCode
      Permissions.GrantTypes.RefreshToken
      Permissions.ResponseTypes.Code
      Permissions.Scopes.Email
      Permissions.Scopes.Profile ]

/// The descriptor for an App. `confidential` is the Web / SPA-or-native choice:
/// a confidential client holds a secret (`secret` is its plaintext, which
/// OpenIddict hashes on write), a public one never does.
let buildDescriptor
    (clientId: string)
    (name: string)
    (confidential: bool)
    (redirectUris: Uri list)
    (postLogoutRedirectUris: Uri list)
    (secret: string option)
    =
    let descriptor = OpenIddictApplicationDescriptor()
    descriptor.ClientId <- clientId
    descriptor.ClientSecret <- Option.toObj secret
    descriptor.DisplayName <- name

    descriptor.ApplicationType <- if confidential then ApplicationTypes.Web else ApplicationTypes.Native
    descriptor.ClientType <- if confidential then ClientTypes.Confidential else ClientTypes.Public

    // Implicit: an app integrating against Umbral has no UI of its own to show
    // a consent screen, and Umbral has no concept of per-scope consent anyway.
    descriptor.ConsentType <- ConsentTypes.Implicit

    for uri in redirectUris do
        descriptor.RedirectUris.Add uri |> ignore

    for uri in postLogoutRedirectUris do
        descriptor.PostLogoutRedirectUris.Add uri |> ignore

    for permission in permissions do
        descriptor.Permissions.Add permission |> ignore

    descriptor.Requirements.Add Requirements.Features.ProofKeyForCodeExchange
    |> ignore

    descriptor
