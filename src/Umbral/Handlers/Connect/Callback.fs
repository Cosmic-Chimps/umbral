/// Coming back from a provider.
///
/// The security of this handler is almost entirely in `OAuthRoundTrip.complete`,
/// which validates the correlation and deletes the cookie before anything else
/// can happen. What is left here is: exchange, resolve, sign in, replay.
module Umbral.Handlers.Connect.Callback

open System
open Microsoft.AspNetCore.DataProtection
open Microsoft.AspNetCore.Builder
open Oxpecker
open Umbral.Infrastructure
open Umbral.Infrastructure.ProviderAdapters
open Umbral.Infrastructure.SecurityEvents

/// Everything an end user sees when a sign-in fails is one of these codes,
/// turned into text by the picker. FR-037: never the provider's own words, and
/// never anything about how Umbral is configured.
let private failureCode (error: ConnectorError) =
    match error with
    | ProviderDeclined _ -> "access-denied"
    | ProviderRejected _ -> "sign-in-failed"
    | ProviderUnreachable _ -> "provider-unreachable"
    | MalformedResponse _ -> "sign-in-failed"

let private fail (ctx: Microsoft.AspNetCore.Http.HttpContext) (code: string) =
    ctx.Response.Redirect $"/signin?msg={Uri.EscapeDataString code}"

let handler (slug: string) : EndpointHandler =
    fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let remoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string

            let record outcome eventType detail =
                record
                    config
                    clock
                    { event eventType outcome Actor.User with
                        ConnectorSlug = Some slug
                        RemoteIp = remoteIp
                        Detail = detail }

            // Validates state, checks the slug and expiry, and DELETES the
            // correlation cookie before any of it — so a replayed callback
            // finds nothing to work with.
            let completion =
                OAuthRoundTrip.complete (ctx.GetService<IDataProtectionProvider>()) clock ctx slug

            match completion with
            | Error error ->
                do! record Failure SignInFailed (Some error.Detail)

                match error with
                | OAuthRoundTrip.ProviderReturnedError "access_denied" -> return fail ctx "access-denied"
                | _ -> return fail ctx "sign-in-failed"

            | Ok correlation ->

            let! connector = ConnectorStore.findBySlug config slug

            match connector with
            | None
            | Some { IsEnabled = false } ->
                // Disabled between the redirect out and the return. Rare, and
                // the honest answer is that this route is no longer available.
                do! record Failure SignInFailed (Some "connector_unavailable")
                return fail ctx "sign-in-failed"

            | Some connector ->

            let code =
                match ctx.Request.Query.TryGetValue "code" with
                | true, values when values.Count > 0 -> Option.ofObj values[0]
                | _ -> None

            match code with
            | None ->
                do! record Failure SignInFailed (Some "no_authorization_code")
                return fail ctx "sign-in-failed"

            | Some code ->

            // Unprotected at the moment of use and never held anywhere else.
            let secrets = ctx.GetService<Secrets.ISecretStore>()

            let! secret =
                match Secrets.SecretRef.Decode connector.ClientSecretRef with
                | Ok reference -> secrets.Get reference
                | Error _ -> System.Threading.Tasks.Task.FromResult None

            match secret with
            | None ->
                // Almost always a lost or unmounted /data/keys: the ciphertext
                // is there and the key ring that reads it is not.
                do! record Failure SignInFailed (Some "connector_secret_unreadable")
                return fail ctx "sign-in-failed"

            | Some secret ->

            let registry = ctx.GetService<Registry>()

            match registry.For connector.Kind with
            | None ->
                do! record Failure SignInFailed (Some "no_adapter_for_kind")
                return fail ctx "sign-in-failed"

            | Some adapter ->

            let origin = ctx.GetService<PublicOrigin.PublicOrigin>()

            let! exchange =
                adapter.ExchangeAndFetch
                    { Connector = connector
                      ClientSecret = secret
                      // The same value sent to the authorize endpoint, derived
                      // the same way. A provider compares them byte for byte.
                      RedirectUri = PublicOrigin.callbackUri origin connector.Slug
                      Code = code
                      CodeVerifier = correlation.CodeVerifier
                      Nonce = correlation.Nonce }

            match exchange with
            | Error error ->
                // The operator gets the cause; the end user gets a code.
                do! record Failure SignInFailed (Some error.Detail)
                return fail ctx (failureCode error)

            | Ok profile ->

            let! resolution = UserStore.resolve config clock connector.Kind profile

            do!
                record
                    Success
                    (match resolution.Decision with
                     | Umbral.Domains.Linking.CreateNew -> UserCreated
                     | Umbral.Domains.Linking.LinkToExisting _ -> UserLinked
                     | Umbral.Domains.Linking.ExistingLink _ -> SignInSucceeded)
                    None

            if not resolution.User.IsEnabled then
                // FR-022. Checked here as well as at the authorize endpoint, so
                // a disabled user cannot even establish the session.
                do!
                    record
                        Failure
                        SignInFailed
                        (Some "user_disabled")

                return fail ctx "account-disabled"
            else

            do! Sessions.signIn ctx resolution.User.Id connector.Slug

            do!
                SecurityEvents.record
                    config
                    clock
                    { event SignInSucceeded Success Actor.User with
                        UserId = Some resolution.User.Id
                        ConnectorSlug = Some slug
                        RemoteIp = remoteIp }

            // Back to the authorization request this all started for, replayed
            // byte for byte so OpenIddict sees exactly what the app sent.
            ctx.Response.Redirect correlation.ReturnUrl
            return ()
        }

let endpoint =
    GET [ routef "/connect/{%s}/callback" handler |> configureEndpoint _.RequireRateLimiting(RateLimits.SignIn) ]
