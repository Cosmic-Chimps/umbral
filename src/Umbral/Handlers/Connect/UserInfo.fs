/// The user-info endpoint.
///
/// Hand-written rather than left to OpenIddict's built-in handler, and that is
/// a requirement rather than a preference: the T001 spike established that the
/// built-in maps a fixed standard claim set only, drops the profile claims, and
/// has no concept of a custom claim like `idp`. FR-005 says this endpoint must
/// return the same identity as the id_token, which the default cannot.
module Umbral.Handlers.Connect.UserInfo

open System.Collections.Generic
open Microsoft.AspNetCore.Authentication
open OpenIddict.Abstractions
open OpenIddict.Validation.AspNetCore
open type OpenIddict.Abstractions.OpenIddictConstants
open Oxpecker
open Umbral.Infrastructure

let handler: EndpointHandler =
    fun ctx ->
        task {
            let! result = ctx.AuthenticateAsync OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme

            match (if result.Succeeded then Option.ofObj result.Principal else None) with
            | None ->
                ctx.SetStatusCode 401
                return! ctx.WriteJson {| error = Errors.InvalidToken |}

            | Some principal ->

            let subject = principal.GetClaim Claims.Subject

            // Read the user fresh rather than echoing the token's claims: a
            // display name or avatar changed since issuance should be reflected,
            // and a user disabled since issuance must not be served at all.
            let! user =
                match subject with
                | null -> System.Threading.Tasks.Task.FromResult None
                | id -> Users.findById (ctx.GetService<Database.DbConfig>()) id

            match user with
            | None | Some { IsEnabled = false } ->
                ctx.SetStatusCode 401
                return! ctx.WriteJson {| error = Errors.InvalidToken |}

            | Some u ->

            // A sign-in method removed since the token was issued ends the token
            // (specs/018). The connector and the credential reference ride in the
            // access token (reference token, never readable by an app). A token
            // with no connector claim was issued before this version: it is
            // refused only when the user is recorded in `legacy_sessions_ended`,
            // since a full re-check would refuse every such token as Unrecorded.
            let config = ctx.GetService<Database.DbConfig>()
            let connectorSlug = Option.ofObj (principal.GetClaim OpenIddictSetup.ConnectorClaim)

            let! refusal =
                match connectorSlug with
                | Some _ ->
                    EndUserSession.recheck
                        config
                        u.Id
                        connectorSlug
                        (Option.ofObj (principal.GetClaim OpenIddictSetup.IdpClaim))
                        (Option.ofObj (principal.GetClaim OpenIddictSetup.CredentialClaim))
                        SecurityEvents.UserInfo
                | None ->
                    task {
                        let! ended = UserStore.legacySessionsEnded config u.Id

                        return
                            if ended then
                                Some(SecurityEvents.credentialRemovedDetail SecurityEvents.UserInfo)
                            else
                                None
                    }

            match refusal with
            | Some detail ->
                do!
                    SecurityEvents.record
                        config
                        (ctx.GetService<System.TimeProvider>())
                        { SecurityEvents.event
                              SecurityEvents.SignInFailed
                              SecurityEvents.Failure
                              SecurityEvents.Actor.User with
                            UserId = Some u.Id
                            ClientId = Option.ofObj (principal.GetClaim Claims.ClientId)
                            ConnectorSlug = connectorSlug
                            RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string
                            Detail = Some detail }

                ctx.SetStatusCode 401
                return! ctx.WriteJson {| error = Errors.InvalidToken |}

            | None ->
                let claims = Dictionary<string, obj>()
                claims[Claims.Subject] <- u.Id

                // Scope-gated exactly as the id_token's destinations are, so the
                // two cannot disagree about what an app is allowed to see.
                if principal.HasScope Scopes.Profile then
                    claims[Claims.Name] <- u.DisplayName

                    match u.AvatarUrl with
                    | Some url -> claims[Claims.Picture] <- url
                    | None -> ()

                    match u.PreferredUsername with
                    | Some username -> claims[Claims.PreferredUsername] <- username
                    | None -> ()

                    match u.ProfileUrl with
                    | Some url -> claims[Claims.Profile] <- url
                    | None -> ()

                if principal.HasScope Scopes.Email then
                    match u.Email with
                    | Some email ->
                        claims[Claims.Email] <- email
                        // A real JSON boolean, matching the id_token claim.
                        claims[Claims.EmailVerified] <- u.EmailVerified
                    | None -> ()

                match principal.GetClaim OpenIddictSetup.IdpClaim with
                | null -> ()
                | idp -> claims[OpenIddictSetup.IdpClaim] <- idp

                return! ctx.WriteJson claims
        }

let endpoint =
    [ GET [ route "/connect/userinfo" handler ]
      POST [ route "/connect/userinfo" handler ] ]
