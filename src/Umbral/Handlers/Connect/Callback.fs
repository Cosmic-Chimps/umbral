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

/// Back to the sign-in page with a failure code. With a `returnUrl` it goes
/// back to the page of the app the person started from, so the page still names
/// that app and offers its connectors (FR-001, research.md R4); without one
/// there is no round trip to return to, and the bare page is all there is.
let private fail (ctx: Microsoft.AspNetCore.Http.HttpContext) (returnUrl: string option) (code: string) =
    match returnUrl with
    | Some returnUrl ->
        ctx.Response.Redirect $"/signin?returnUrl={Uri.EscapeDataString returnUrl}&msg={Uri.EscapeDataString code}"
    | None -> ctx.Response.Redirect $"/signin?msg={Uri.EscapeDataString code}"

let handler (slug: string) : EndpointHandler =
    fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let remoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string

            // `userId` is None until the callback has worked out WHO this is;
            // every failure before that point genuinely has no user to name.
            let recordFor userId outcome eventType detail =
                record
                    config
                    clock
                    { event eventType outcome Actor.User with
                        UserId = userId
                        ConnectorSlug = Some slug
                        RemoteIp = remoteIp
                        Detail = detail }

            let record = recordFor None

            // Validates state, checks the slug and expiry, and DELETES the
            // correlation cookie before any of it — so a replayed callback
            // finds nothing to work with.
            let protectors = ctx.GetService<IDataProtectionProvider>()

            // Read before `complete` consumes the cookie, so the failures that
            // `complete` itself reports can still return to the app's page.
            let earlyReturnUrl = OAuthRoundTrip.returnUrlOf protectors clock ctx

            let completion = OAuthRoundTrip.complete protectors clock ctx slug

            match completion with
            | Error error ->
                do! record Failure SignInFailed (Some error.Detail)

                match error with
                | OAuthRoundTrip.ProviderReturnedError "access_denied" ->
                    return fail ctx earlyReturnUrl "access-denied"
                | _ -> return fail ctx earlyReturnUrl "sign-in-failed"

            | Ok correlation ->

            // Every failure from here on has a correlation, so it returns to
            // the page the person started from.
            let fail ctx code = fail ctx (Some correlation.ReturnUrl) code

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

            let userId = Some resolution.User.Id

            // A first sign-in or a new link is a change in its own right and is
            // recorded as one. An ordinary return visit is not: it is only the
            // sign-in, recorded below once it has actually succeeded — never
            // here, where a disabled user would still be logged as a success.
            match resolution.Decision with
            | Umbral.Domains.Linking.CreateNew -> do! recordFor userId Success UserCreated None
            | Umbral.Domains.Linking.LinkToExisting _ -> do! recordFor userId Success UserLinked None
            | Umbral.Domains.Linking.ExistingLink _ -> ()

            if not resolution.User.IsEnabled then
                // FR-022. Checked here as well as at the authorize endpoint, so
                // a disabled user cannot even establish the session.
                do! recordFor userId Failure SignInFailed (Some "user_disabled")

                return fail ctx "account-disabled"
            else

            do! Sessions.signIn ctx resolution.User.Id connector.Kind.Value connector.Slug
            do! recordFor userId Success SignInSucceeded None

            // Back to the authorization request this all started for, replayed
            // byte for byte so OpenIddict sees exactly what the app sent.
            ctx.Response.Redirect correlation.ReturnUrl
            return ()
        }

let endpoint =
    GET [ routef "/connect/{%s}/callback" handler |> configureEndpoint _.RequireRateLimiting(RateLimits.SignIn) ]
