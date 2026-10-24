/// All of Umbral's OpenIddict configuration, in one file on purpose.
///
/// F# meets a fluent builder badly: every call returns the builder, F# lambdas
/// must return unit, and the result is a wall of `|> ignore`. Quarantining it
/// here keeps that noise out of the handlers, which are the code anyone will
/// actually read.
///
/// The T001 spike proved this configuration end to end in C# before any of it
/// was written in F#, which is why the comments below can state what the
/// behaviour *is* rather than what the documentation claims.
module Umbral.Infrastructure.OpenIddictSetup

open System
open System.Threading.Tasks
// GetHttpRequest, like GetOpenIddictServerRequest, lives in this namespace.
open Microsoft.AspNetCore
open Microsoft.Extensions.DependencyInjection
open Quartz
open OpenIddict.Abstractions
open OpenIddict.Server
open OpenIddict.Server.AspNetCore
open type OpenIddict.Abstractions.OpenIddictConstants

[<Literal>]
let AuthorizationEndpoint = "connect/authorize"

[<Literal>]
let TokenEndpoint = "connect/token"

[<Literal>]
let UserInfoEndpoint = "connect/userinfo"

[<Literal>]
let EndSessionEndpoint = "connect/logout"

[<Literal>]
let RevocationEndpoint = "connect/revoke"

[<Literal>]
let IntrospectionEndpoint = "connect/introspect"

/// Umbral's one non-standard claim: which connector authenticated this
/// sign-in. Additive and safe for a client to ignore, and the only honest way
/// for an app to show "signed in with Discord".
[<Literal>]
let IdpClaim = "idp"

/// The two private claims that let the token endpoint judge a code or a refresh
/// token by the rule again (specs/006 R7): the slug of the connector the sign-in
/// went through, and the domain of the verified email that was checked. Given
/// NO destination, so they live only in the encrypted code and refresh token and
/// never in an access token, an id token or the userinfo response.
[<Literal>]
let ConnectorClaim = "umbral_connector"

[<Literal>]
let EmailDomainClaim = "umbral_email_domain"

// ---------------------------------------------------------------------------
// T105 — refusing `plain` PKCE
//
// OpenIddict 7.7.1 advertises ["plain", "S256"] and genuinely accepts `plain`
// end to end; the T001 spike confirmed both by running the flow. There is no
// builder flag that restricts the method — RequireProofKeyForCodeExchange() is
// the only PKCE-related method on the server builder.
//
// This matters rather than being pedantry. `plain` sends the verifier AS the
// challenge, so anyone who can read the authorization request learns the
// verifier and PKCE protects nothing. Advertising a method we do not want used
// is the quiet default Principle IV exists to forbid.
// ---------------------------------------------------------------------------

type RejectPlainCodeChallengeMethod() =
    interface IOpenIddictServerHandler<OpenIddictServerEvents.ValidateAuthorizationRequestContext> with
        member _.HandleAsync(context) =
            let method' = context.Request.CodeChallengeMethod

            if not (String.IsNullOrEmpty method') && method' <> CodeChallengeMethods.Sha256 then
                context.Reject(
                    error = Errors.InvalidRequest,
                    description =
                        $"The code challenge method '{method'}' is not supported. Use S256.",
                    uri = null
                )

            ValueTask.CompletedTask

    static member Descriptor =
        OpenIddictServerHandlerDescriptor
            .CreateBuilder<OpenIddictServerEvents.ValidateAuthorizationRequestContext>()
            .UseSingletonHandler<RejectPlainCodeChallengeMethod>()
            // After OpenIddict's own PKCE validation, so a request with no
            // challenge at all is already rejected by the time we see it and
            // this handler only ever judges a method that was supplied.
            .SetOrder(OpenIddictServerHandlers.Protection.ValidateTokenEntry.Descriptor.Order + 1_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build()

/// Rejecting `plain` is only half the job: OpenIddict still advertises it in
/// the discovery document, and a client library reading
/// `code_challenge_methods_supported` would reasonably pick it and then fail.
/// Telling clients the truth about what this server accepts is the other half.
type AdvertiseOnlyS256() =
    interface IOpenIddictServerHandler<OpenIddictServerEvents.HandleConfigurationRequestContext> with
        member _.HandleAsync(context) =
            context.CodeChallengeMethods.Remove CodeChallengeMethods.Plain |> ignore
            ValueTask.CompletedTask

    static member Descriptor =
        OpenIddictServerHandlerDescriptor
            .CreateBuilder<OpenIddictServerEvents.HandleConfigurationRequestContext>()
            .UseSingletonHandler<AdvertiseOnlyS256>()
            .SetOrder(OpenIddictServerHandlers.Discovery.AttachCodeChallengeMethods.Descriptor.Order + 1_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build()

// ---------------------------------------------------------------------------
// FR-031, research.md D4 — the discovery document follows the configured origin
//
// `issuer` comes from `SetIssuer`, but OpenIddict resolves every endpoint URL
// in the document against the base URI of the request being answered, which is
// built from its scheme and Host. Behind a proxy that forwards a different
// port, or none, a client reading discovery was told to send people to
// `https://localhost:18443/connect/authorize` or `http://umbral:8080/...`
// while `issuer` said `https://auth.example.com`. Every client library takes
// its endpoints from this document, so the origin was configured and still not
// the one in use.
//
// Fixed on the way OUT, not at the request. Pinning the transaction's base
// URI instead would also change how OpenIddict matches incoming requests to
// endpoints (it compares against that same base), and behind a proxy that
// would stop `/connect/token` from matching at all. Rewriting only what the
// document says leaves matching alone, and the admin origin-mismatch banner
// keeps seeing the request as it really arrived.
// ---------------------------------------------------------------------------

type PublishIssuerOrigin() =
    interface IOpenIddictServerHandler<OpenIddictServerEvents.HandleConfigurationRequestContext> with
        member _.HandleAsync(context) =
            match context.Issuer with
            | null -> ()
            | issuer ->
                let rebase (endpoint: Uri | null) : Uri | null =
                    match endpoint with
                    | null -> null
                    | e -> Uri(issuer, e.PathAndQuery)

                context.AuthorizationEndpoint <- rebase context.AuthorizationEndpoint
                context.TokenEndpoint <- rebase context.TokenEndpoint
                context.UserInfoEndpoint <- rebase context.UserInfoEndpoint
                context.EndSessionEndpoint <- rebase context.EndSessionEndpoint
                context.RevocationEndpoint <- rebase context.RevocationEndpoint
                context.IntrospectionEndpoint <- rebase context.IntrospectionEndpoint
                context.JsonWebKeySetEndpoint <- rebase context.JsonWebKeySetEndpoint
                // Not offered by Umbral today (no device flow, no PAR, no
                // mTLS); rebased anyway so that switching one on cannot
                // quietly reintroduce the request's host.
                context.DeviceAuthorizationEndpoint <- rebase context.DeviceAuthorizationEndpoint
                context.PushedAuthorizationEndpoint <- rebase context.PushedAuthorizationEndpoint
                context.MtlsDeviceAuthorizationEndpointAlias <- rebase context.MtlsDeviceAuthorizationEndpointAlias
                context.MtlsIntrospectionEndpointAlias <- rebase context.MtlsIntrospectionEndpointAlias
                context.MtlsPushedAuthorizationEndpointAlias <- rebase context.MtlsPushedAuthorizationEndpointAlias
                context.MtlsRevocationEndpointAlias <- rebase context.MtlsRevocationEndpointAlias
                context.MtlsTokenEndpointAlias <- rebase context.MtlsTokenEndpointAlias
                context.MtlsUserInfoEndpointAlias <- rebase context.MtlsUserInfoEndpointAlias

            ValueTask.CompletedTask

    static member Descriptor =
        OpenIddictServerHandlerDescriptor
            .CreateBuilder<OpenIddictServerEvents.HandleConfigurationRequestContext>()
            .UseSingletonHandler<PublishIssuerOrigin>()
            // After every Attach* handler, so nothing built later can put a
            // request-derived URL back.
            .SetOrder(OpenIddictServerHandlers.Discovery.AttachAdditionalMetadata.Descriptor.Order + 1_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build()

// ---------------------------------------------------------------------------
// FR-008 — recording an authorization-code replay
//
// A code is single use, and OpenIddict enforces that: the second presentation
// is rejected as ID2010 ("already been redeemed") and the whole authorization
// is revoked, which is the right outcome. What it does not do is tell the
// operator. A replayed code is either an interception in progress or a client
// that retries when it should not, and both are worth being able to find.
//
// The rejection happens inside OpenIddict's own pipeline, BEFORE passthrough
// would hand the request to Token.fs, so no handler of ours ever sees it as a
// request. The first place Umbral can observe it is the token response being
// applied, where the error and its documentation link are already settled.
// Matched on the link (".../errors/ID2010") rather than the sentence: the link
// names the exact rejection and is not prose someone will reword. The
// protocol test that replays a code fails if this ever stops matching.
// ---------------------------------------------------------------------------

[<Literal>]
let private RedeemedCodeErrorId = "ID2010"

type RecordAuthorizationCodeReplay() =
    interface IOpenIddictServerHandler<OpenIddictServerEvents.ApplyTokenResponseContext> with
        member _.HandleAsync(context) =
            let isReplay =
                not (isNull context.Request)
                && context.Request.IsAuthorizationCodeGrantType()
                && (match context.Response.ErrorUri with
                    | null -> false
                    | uri -> uri.EndsWith RedeemedCodeErrorId)

            if not isReplay then
                ValueTask.CompletedTask
            else
                match context.Transaction.GetHttpRequest() with
                | null -> ValueTask.CompletedTask
                | request ->
                    let services = request.HttpContext.RequestServices

                    ValueTask(
                        SecurityEvents.record
                            (services.GetRequiredService<Database.DbConfig>())
                            (services.GetRequiredService<TimeProvider>())
                            { SecurityEvents.event SecurityEvents.AuthorizationCodeReplayed SecurityEvents.Failure SecurityEvents.Actor.User with
                                ClientId = Option.ofObj context.Request.ClientId
                                RemoteIp =
                                    request.HttpContext.Connection.RemoteIpAddress
                                    |> Option.ofObj
                                    |> Option.map string }
                    )

    static member Descriptor =
        OpenIddictServerHandlerDescriptor
            .CreateBuilder<OpenIddictServerEvents.ApplyTokenResponseContext>()
            .UseSingletonHandler<RecordAuthorizationCodeReplay>()
            // Before OpenIddict writes the response (ProcessJsonResponse is at
            // 500_000): once the body is out the request is handled and later
            // handlers never run.
            .SetOrder(100_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build()

// ---------------------------------------------------------------------------
// Registration
// ---------------------------------------------------------------------------

type Options =
    { Issuer: Uri
      SigningCertificate: Security.Cryptography.X509Certificates.X509Certificate2
      EncryptionCertificate: Security.Cryptography.X509Certificates.X509Certificate2
      /// Development only, and gated on the environment by the caller.
      AllowInsecureHttp: bool }

let addOpenIddict (options: Options) (services: IServiceCollection) =
    services
        .AddOpenIddict()
        .AddCore(fun core ->
            core.UseEntityFrameworkCore().UseDbContext<Umbral.Migrations.UmbralContext>()
            |> ignore

            // Reference tokens (below) write a row per issuance, so the token
            // table grows faster than it would otherwise. Pruning is not
            // optional on a device with an SD card.
            core.UseQuartz() |> ignore)

        .AddServer(fun server ->
            // Relative, with no leading slash: they resolve against PathBase.
            // The Oxpecker route is the absolute path. When these two disagree
            // GetOpenIddictServerRequest() returns null, and that null is the
            // signature of this specific mistake.
            server
                .SetAuthorizationEndpointUris(AuthorizationEndpoint)
                .SetTokenEndpointUris(TokenEndpoint)
                .SetUserInfoEndpointUris(UserInfoEndpoint)
                .SetEndSessionEndpointUris(EndSessionEndpoint)
                .SetRevocationEndpointUris(RevocationEndpoint)
                .SetIntrospectionEndpointUris(IntrospectionEndpoint)
            |> ignore

            // The only flow Umbral offers (FR-002). No implicit, no password.
            server.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange().AllowRefreshTokenFlow()
            |> ignore

            server.AddEventHandler RejectPlainCodeChallengeMethod.Descriptor |> ignore
            server.AddEventHandler AdvertiseOnlyS256.Descriptor |> ignore
            server.AddEventHandler PublishIssuerOrigin.Descriptor |> ignore
            server.AddEventHandler RecordAuthorizationCodeReplay.Descriptor |> ignore

            // Opaque reference tokens rather than self-contained JWTs. This is
            // what makes FR-022 true as written: revoking an authorization stops
            // the token at the next use instead of at its expiry, which the
            // spike confirmed returns 401 with no propagation delay. It also
            // removes OpenIddict's default access-token encryption, and with it
            // the five-segment JWE that confuses everyone who tries to decode
            // an access token.
            server.UseReferenceAccessTokens().UseReferenceRefreshTokens() |> ignore

            // Never inferred from the request. Behind a reverse proxy the
            // request says http://umbral:8080 while every client is checking
            // `iss` against the public address.
            server.SetIssuer options.Issuer |> ignore

            server.RegisterScopes [| Scopes.OpenId; Scopes.Email; Scopes.Profile; Scopes.OfflineAccess |]
            |> ignore

            server.RegisterClaims
                [| Claims.Subject
                   Claims.Email
                   Claims.EmailVerified
                   Claims.Name
                   Claims.PreferredUsername
                   Claims.Profile
                   Claims.Picture
                   IdpClaim |]
            |> ignore

            // OpenIddict 7.0 turned both on by default. Pure OIDC clients send
            // neither parameter, so nothing breaks today — but the first client
            // that does would fail opaquely, and Umbral has no resource servers
            // to validate against.
            server.DisableAudienceValidation().DisableResourceValidation() |> ignore

            server.AddSigningCertificate(options.SigningCertificate) |> ignore
            server.AddEncryptionCertificate(options.EncryptionCertificate) |> ignore

            let aspNet = server.UseAspNetCore()

            // Passthrough means OpenIddict validates the request, stashes it on
            // the HttpContext, and lets it flow on to Oxpecker instead of
            // answering itself. It is framework-agnostic: MVC's SignIn and
            // Forbid are thin wrappers over HttpContext.SignInAsync and
            // ForbidAsync, which an Oxpecker handler calls directly.
            //
            // The token endpoint IS in this list, since T091. OpenIddict still
            // does the heavy lifting there — PKCE verification, client
            // authentication, code and refresh-token validation, rolling
            // refresh rotation — but the decision to issue is Umbral's, because
            // FR-022 needs `is_enabled` re-checked on every refresh and a
            // handler that does not run cannot check it. The cost is that the
            // handler now owns rebuilding the identity and re-applying claim
            // destinations (Handlers/Connect/Token.fs).
            aspNet
                .EnableAuthorizationEndpointPassthrough()
                .EnableTokenEndpointPassthrough()
                .EnableUserInfoEndpointPassthrough()
                .EnableEndSessionEndpointPassthrough()
            |> ignore

            // Error passthrough is NOT inert. With it on and no error handler
            // mapped, every request OpenIddict would reject cleanly becomes an
            // InvalidOperationException and a 500 — the spike hit exactly that.
            // It is enabled here only because Handlers/Connect/Error.fs exists.
            //
            // It is also mutually exclusive with EnableStatusCodePagesIntegration
            // — OpenIddict refuses to start with both ("the error pass-through
            // mode cannot be used when the status code pages integration is
            // enabled"). Passthrough wins: FR-037 wants Umbral's own page
            // carrying the detail a developer integrating an app actually needs.
            aspNet.EnableErrorPassthrough() |> ignore

            if options.AllowInsecureHttp then
                aspNet.DisableTransportSecurityRequirement() |> ignore)

        .AddValidation(fun validation ->
            validation.UseLocalServer() |> ignore
            validation.UseAspNetCore() |> ignore

            // Checks the authorization entry on every token use, which is the
            // other half of making revocation immediate.
            validation.EnableAuthorizationEntryValidation() |> ignore)
    |> ignore

    // Quartz 4 renamed the configurator to IQuartzBuilder and its methods now
    // return the builder, so the lambda needs an explicit ignore.
    services.AddQuartz(fun (q: IQuartzBuilder) ->
        q.UseSimpleTypeLoader() |> ignore
        q.UseInMemoryStore() |> ignore)
    |> ignore

    services.AddQuartzHostedService(fun (q: QuartzHostedServiceOptions) -> q.WaitForJobsToComplete <- true)
    |> ignore
