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
