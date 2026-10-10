/// The authorization endpoint — the one handler Umbral writes by hand.
///
/// By the time it runs, OpenIddict has already validated `client_id`,
/// `redirect_uri`, `scope`, `response_type` and PKCE. What remains is Umbral's
/// decision: is there a person here, are they still allowed in, and what do we
/// say about them.
module Umbral.Handlers.Connect.Authorize

open System
open System.Collections.Generic
open System.Security.Claims
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Extensions
open System.Threading.Tasks
open Microsoft.IdentityModel.Tokens
open OpenIddict.Abstractions
open OpenIddict.Server.AspNetCore
open type OpenIddict.Abstractions.OpenIddictConstants
open Oxpecker
open Umbral.Infrastructure

// ---------------------------------------------------------------------------
// Claim destinations
//
// THE line that fails silently. A claim with no destination is dropped from
// both tokens — no error, no warning, HTTP 200, a valid signature. The T001
// spike ran the flow with and without this call: with it the id_token carries
// email, name, picture and idp; without it only `sub` survives. The only way to
// notice from outside is to decode the token, which is why TokenClaimsTests
// asserts the claim content of a real issued token rather than how the identity
// was built.
// ---------------------------------------------------------------------------

let destinationsFor (claim: Claim) : string seq =
    let principal = claim.Subject

    let hasScope (scope: string) =
        match principal with
        | null -> false
        | p -> p.HasScope scope

    match claim.Type with
    // `sub` needs no destination — OpenIddict always emits it — but being
    // explicit costs nothing and stops a future reader wondering.
    | t when t = Claims.Subject ->
        seq {
            Destinations.AccessToken
            Destinations.IdentityToken
        }

    | t when t = Claims.Name || t = Claims.PreferredUsername || t = Claims.Picture ->
        seq {
            Destinations.AccessToken
            if hasScope Scopes.Profile then Destinations.IdentityToken
        }

    | t when t = Claims.Email || t = Claims.EmailVerified ->
        seq {
            Destinations.AccessToken
            if hasScope Scopes.Email then Destinations.IdentityToken
        }

    // An app asking "which provider was this" is not asking for a profile, so
    // this rides along unconditionally.
    | t when t = OpenIddictSetup.IdpClaim ->
        seq {
            Destinations.AccessToken
            Destinations.IdentityToken
        }

    | _ -> Seq.singleton Destinations.AccessToken

// ---------------------------------------------------------------------------
// Building the identity
// ---------------------------------------------------------------------------

/// Writes what Umbral says about a person onto an identity. Shared with the
/// token endpoint, which re-applies it on refresh so a name or email changed at
/// the provider since sign-in shows up in the next tokens instead of the ones
/// frozen at authorization time.
///
/// Removes before it sets, so a claim the person no longer has (an avatar, a
/// verified email) leaves the identity rather than lingering from the last
/// issuance. On a fresh identity the removals are no-ops.
let setProfileClaims (identity: ClaimsIdentity) (user: Users.User) (idp: string) =
    identity.SetClaim(Claims.Subject, user.Id) |> ignore
    identity.SetClaim(Claims.Name, user.DisplayName) |> ignore
    identity.SetClaim(OpenIddictSetup.IdpClaim, idp) |> ignore

    identity.RemoveClaims Claims.Email |> ignore
    identity.RemoveClaims Claims.EmailVerified |> ignore
    identity.RemoveClaims Claims.Picture |> ignore

    match user.Email with
    | Some email ->
        identity.SetClaim(Claims.Email, email) |> ignore
        // The BOOLEAN overload. SetClaim has 15+ overloads and passing "true"
        // picks the string one, emitting "email_verified": "true" — which an
        // app doing `if (claims.email_verified)` reads as truthy even for
        // "false". The spike hit exactly this.
        identity.SetClaim(Claims.EmailVerified, Nullable user.EmailVerified) |> ignore
    | None -> ()

    match user.AvatarUrl with
    | Some url -> identity.SetClaim(Claims.Picture, url) |> ignore
    | None -> ()

let private buildIdentity (user: Users.User) (idp: string) (scopes: string seq) =
    // A ClaimsIdentity with a null authentication type is NOT authenticated and
    // SignInAsync rejects it. This constant is the thing that makes it valid.
    let identity =
        ClaimsIdentity(
            authenticationType = TokenValidationParameters.DefaultAuthenticationType,
            nameType = Claims.Name,
            roleType = Claims.Role
        )

    setProfileClaims identity user idp
    identity.SetScopes scopes |> ignore
    identity

/// OpenIddict turns this into the protocol's own error response — a redirect at
/// the authorization endpoint, JSON at the token endpoint — so neither handler
/// ever builds one by hand.
let forbid (ctx: HttpContext) (error: string) (description: string) =
    let properties =
        AuthenticationProperties(
            Dictionary<string, string | null>(
                dict
                    [ OpenIddictServerAspNetCoreConstants.Properties.Error, (error: string | null)
                      OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription, description ]
            )
        )

    // OpenIddict turns a Forbid on its own scheme into a correct error
    // redirect, using the right response_mode. Never hand-roll that redirect.
    ctx.ForbidAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, properties)

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

let handler: EndpointHandler =
    fun ctx ->
        task {
            // The RESPONSE is checked first, and that ordering is the whole
            // point.
            //
            // When OpenIddict rejects an authorization request — an unknown
            // client, a missing PKCE challenge, an unregistered redirect_uri —
            // it sets a response AND leaves the parsed request in place, then
            // passes the request through. Looking at the request first and only
            // consulting the response when it is null means a rejected request
            // sails on into the sign-in picker: Umbral would show a login page
            // for a client it has never heard of, and only fail later, if at
            // all.
            // The RESPONSE is checked first, and that ordering is the point.
            //
            // When OpenIddict rejects an authorization request — an unknown
            // client, a missing PKCE challenge, an unregistered redirect_uri —
            // it sets a response AND leaves the parsed request in place, then
            // passes it through. Looking at the request first, and consulting
            // the response only when the request is null, lets a rejected
            // request sail on into the sign-in picker: Umbral would show a
            // login page for a client it has never heard of.
            if not (isNull (box (ctx.GetOpenIddictServerResponse()))) then
                // OpenIddict has already decided, and its error names the
                // protocol fact an integrating developer needs.
                return! Error.handler ctx
            else

            match ctx.GetOpenIddictServerRequest() with
            | null ->
                // Neither a request nor a response: the Oxpecker route and
                // Set*EndpointUris genuinely disagree. A wiring bug.
                return
                    failwith
                        "The OpenID Connect request could not be retrieved and OpenIddict produced no \
                         response. The Oxpecker route and OpenIddictSetup's endpoint URI disagree."
            | request ->

            let! session = ctx.AuthenticateAsync Sessions.Scheme

            let signedIn =
                if session.Succeeded then Option.ofObj session.Principal else None

            match signedIn with
            | Some principal when not (request.HasPromptValue PromptValues.Login) ->
                let subject = Sessions.currentSubject principal
                let idp = Sessions.currentIdp principal |> Option.defaultValue "unknown"

                let! user =
                    match subject with
                    | Some id -> Users.findById (ctx.GetService<Database.DbConfig>()) id
                    | None -> Task.FromResult None

                match user with
                | None ->
                    // The session names a user who no longer exists — deleted
                    // while signed in. Drop the cookie so the next attempt
                    // starts clean.
                    do! Sessions.signOut ctx
                    return! forbid ctx Errors.LoginRequired "The signed-in user no longer exists."

                | Some u when not u.IsEnabled ->
                    // FR-022. Checked against the database on every
                    // authorization, not against the cookie, so disabling takes
                    // effect at the next sign-in rather than at cookie expiry.
                    do! Sessions.signOut ctx

                    // Refused here as well as at the callback, so it is recorded
                    // here as well: this is the sign-in that was turned away.
                    do!
                        SecurityEvents.record
                            (ctx.GetService<Database.DbConfig>())
                            (ctx.GetService<TimeProvider>())
                            { SecurityEvents.event SecurityEvents.SignInFailed SecurityEvents.Failure SecurityEvents.Actor.User with
                                UserId = Some u.Id
                                ClientId = Option.ofObj request.ClientId
                                RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string
                                Detail = Some "user_disabled" }

                    return! forbid ctx Errors.AccessDenied "This account has been disabled."

                | Some u ->
                    let applications = ctx.GetService<IOpenIddictApplicationManager>()
                    let authorizations = ctx.GetService<IOpenIddictAuthorizationManager>()

                    let! application =
                        match request.ClientId with
                        | null -> Task.FromResult(null: obj | null)
                        | clientId -> applications.FindByClientIdAsync(clientId).AsTask()

                    match application with
                    | null ->
                        return! forbid ctx Errors.InvalidClient "The client application is not registered."

                    | application ->
                        let identity = buildIdentity u idp (request.GetScopes())
                        let! rawApplicationId = applications.GetIdAsync application

                        // GetIdAsync is typed nullable even though a resolved
                        // application always has one; an authorization with no
                        // client would be unrevokable, so refuse rather than
                        // paper over it.
                        let applicationId =
                            match rawApplicationId with
                            | null -> failwith "The registered application has no identifier."
                            | id -> id

                        // A permanent authorization is what makes refresh tokens
                        // and per-user revocation possible: revoking it is how
                        // FR-022 stops tokens already issued.
                        let! authorization =
                            authorizations.CreateAsync(
                                identity = identity,
                                subject = u.Id,
                                client = applicationId,
                                ``type`` = AuthorizationTypes.Permanent,
                                scopes = identity.GetScopes()
                            )

                        let! authorizationId = authorizations.GetIdAsync authorization
                        identity.SetAuthorizationId authorizationId |> ignore

                        // See the comment on destinationsFor. Without this the
                        // tokens come out carrying nothing but `sub`, and
                        // nothing anywhere says so.
                        identity.SetDestinations(Func<Claim, IEnumerable<string>>(destinationsFor))
                        |> ignore

                        return!
                            ctx.SignInAsync(
                                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                                ClaimsPrincipal identity
                            )

            | _ ->
                if request.HasPromptValue PromptValues.None then
                    // prompt=none means "show no UI". With no session, saying so
                    // is the only correct answer.
                    return! forbid ctx Errors.LoginRequired "No user is signed in."
                else
                    // Send them to the connector picker carrying the whole
                    // authorization request, so it replays byte for byte once
                    // they are back.
                    let returnUrl = ctx.Request.GetEncodedPathAndQuery()
                    ctx.Response.Redirect($"/signin?returnUrl={Uri.EscapeDataString returnUrl}")
                    return ()
        }

let endpoint =
    [ GET [ route "/connect/authorize" handler ]
      POST [ route "/connect/authorize" handler ] ]
