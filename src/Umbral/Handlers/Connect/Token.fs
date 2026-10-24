/// The token endpoint.
///
/// OpenIddict validates the request, authenticates the client, verifies PKCE
/// and checks the code or refresh token itself; passthrough hands what is left
/// to Umbral, which is the one question OpenIddict cannot answer: is this
/// person STILL allowed in? A refresh token outlives the moment it was issued,
/// and between then and now the user may have been disabled or deleted —
/// FR-022 says no new tokens in either case.
module Umbral.Handlers.Connect.Token

open System
open System.Security.Claims
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Http
open Microsoft.IdentityModel.Tokens
open OpenIddict.Abstractions
open OpenIddict.Server.AspNetCore
open type OpenIddict.Abstractions.OpenIddictConstants
open Oxpecker
open Umbral.Domains
open Umbral.Infrastructure

let handler: EndpointHandler =
    fun ctx ->
        task {
            match ctx.GetOpenIddictServerRequest() with
            | null ->
                // Neither a request nor a response: the Oxpecker route and
                // SetTokenEndpointUris disagree. A wiring bug, said as one.
                return
                    failwith
                        "The OpenID Connect token request could not be retrieved. The Oxpecker route and OpenIddictSetup's endpoint URI disagree."
            | request ->

            if not (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType()) then
                // AllowAuthorizationCodeFlow / AllowRefreshTokenFlow already
                // refuse every other grant before passthrough, so reaching this
                // means a flow was enabled without a handler for it. Refuse it
                // rather than mint a token for a grant Umbral never designed.
                return! Authorize.forbid ctx Errors.UnsupportedGrantType "The specified grant type is not supported."
            else

            // The code or refresh token, validated: signature, expiry, status,
            // client binding. Authenticating through the OpenIddict scheme is
            // how a passthrough handler reads the result of all that.
            let! result = ctx.AuthenticateAsync OpenIddictServerAspNetCoreDefaults.AuthenticationScheme

            match result.Principal with
            | null ->
                // OpenIddict normally answers a rejected code or token itself,
                // before passthrough runs, so this is a belt-and-braces refusal
                // rather than a path a request takes. A replayed code is
                // recorded where it is rejected: OpenIddictSetup's
                // RecordAuthorizationCodeReplay.
                return! Authorize.forbid ctx Errors.InvalidGrant "The token is no longer valid."

            | principal ->

            let subject = Option.ofObj (principal.GetClaim Claims.Subject)

            let! user =
                match subject with
                | Some id -> Users.findById (ctx.GetService<Database.DbConfig>()) id
                | None -> System.Threading.Tasks.Task.FromResult None

            match user with
            | None ->
                // Deleted since the token was issued. UserDelete revokes the
                // authorizations too, so OpenIddict usually refuses first; this
                // is the check that holds when it does not.
                return! Authorize.forbid ctx Errors.InvalidGrant "The user no longer exists."

            | Some u when not u.IsEnabled ->
                // FR-022, re-checked against the database on every issuance
                // rather than trusted from the token.
                return! Authorize.forbid ctx Errors.InvalidGrant "This account has been disabled."

            | Some u ->

            // The allowed email domains (specs/006), judged again on both grants:
            // a refresh token outlives the rule it was issued under, and a code
            // can wait a few minutes for redemption. The connector and the domain
            // come from the principal's private claims, which no app can read.
            let config = ctx.GetService<Database.DbConfig>()
            let connectorSlug = Option.ofObj (principal.GetClaim OpenIddictSetup.ConnectorClaim)
            let emailDomain = Option.ofObj (principal.GetClaim OpenIddictSetup.EmailDomainClaim)

            let! decision = EmailDomainRules.recheck config connectorSlug emailDomain

            match decision with
            | EmailDomains.Refused(level, reason) ->
                do!
                    SecurityEvents.record
                        config
                        (ctx.GetService<TimeProvider>())
                        { SecurityEvents.event SecurityEvents.SignInFailed SecurityEvents.Failure SecurityEvents.Actor.User with
                            UserId = Some u.Id
                            ClientId = Option.ofObj request.ClientId
                            ConnectorSlug = connectorSlug
                            RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string
                            Detail = Some(SecurityEvents.refusalDetail reason level SecurityEvents.Token emailDomain) }

                return! Authorize.forbid ctx Errors.InvalidGrant "This account can no longer be used to sign in."

            | EmailDomains.Admitted ->

            // Code review B4: the Fediverse server list, judged at the same place.
            let! refusedServer = FediverseServers.recheckServer config connectorSlug u.Id

            match refusedServer with
            | Some host ->
                do!
                    SecurityEvents.record
                        config
                        (ctx.GetService<TimeProvider>())
                        { SecurityEvents.event SecurityEvents.SignInFailed SecurityEvents.Failure SecurityEvents.Actor.User with
                            UserId = Some u.Id
                            ClientId = Option.ofObj request.ClientId
                            ConnectorSlug = connectorSlug
                            RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string
                            Detail = Some $"fediverse:server_not_allowed;server={host}" }

                return! Authorize.forbid ctx Errors.InvalidGrant "This account can no longer be used to sign in."

            | None ->

            // Rebuilt from the principal's own claims, not from scratch. The
            // private `oi_*` claims — scopes, authorization id, token lineage —
            // live there, and without them the new tokens lose their
            // authorization and revocation stops reaching them.
            let identity =
                ClaimsIdentity(
                    principal.Claims,
                    TokenValidationParameters.DefaultAuthenticationType,
                    Claims.Name,
                    Claims.Role
                )

            // Current profile over the stale one, so a refresh reflects who the
            // person is now.
            Authorize.setProfileClaims identity u (principal.GetClaim OpenIddictSetup.IdpClaim |> Option.ofObj |> Option.defaultValue "unknown")

            // Destinations do not survive the round trip through a token. They
            // are claim PROPERTIES, not part of the token, so without this the
            // refreshed tokens carry nothing but `sub` — HTTP 200, a valid
            // signature, no error anywhere.
            identity.SetDestinations(System.Func<Claim, System.Collections.Generic.IEnumerable<string>>(Authorize.destinationsFor))
            |> ignore

            return!
                ctx.SignInAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, ClaimsPrincipal identity)
        }

let endpoint = POST [ route "/connect/token" handler ]
