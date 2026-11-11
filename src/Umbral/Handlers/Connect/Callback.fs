/// Coming back from a provider.
///
/// The security of this handler is almost entirely in `OAuthRoundTrip.complete`,
/// which validates the correlation and deletes the cookie before anything else
/// can happen. What is left here is: exchange, resolve, sign in, replay.
///
/// The callback has two routes on one path, because providers answer in two
/// ways. `GET` is the answer of a redirect (every provider but Apple): the
/// values are in the query string and the correlation cookie is `SameSite=Lax`.
/// `POST` is the answer of `response_mode=form_post` (Sign in with Apple): the
/// values are the body of a cross-site POST, which a browser sends the
/// correlation cookie on only if it is `SameSite=None`, so the round trip keeps
/// a second cookie for it (specs/013 research R2). Both routes then run the
/// same code: only where the values are read from differs.
module Umbral.Handlers.Connect.Callback

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Features
open Microsoft.AspNetCore.DataProtection
open Microsoft.AspNetCore.Builder
open Oxpecker
open Umbral.Domains
open Umbral.Infrastructure
open Umbral.Infrastructure.ProviderAdapters
open Umbral.Infrastructure.SecretStores.Registry
open Umbral.Infrastructure.SecurityEvents

let private fail = Picker.redirectWithFailure

/// Everything after a provider has said who the person is: the allowed email
/// domains, resolving the user, the session, and the redirect back to the
/// authorization request. `connector` is the one the person signed in through.
///
/// `applyEmailRule` is false for a provider with no email at all (the fediverse,
/// specs/010 FR-018), where there is nothing for a domain rule to judge.
let finishSignIn
    (ctx: HttpContext)
    (connector: Connectors.Connector)
    (profile: ProviderProfile)
    (returnUrl: string)
    (applyEmailRule: bool)
    : Task<unit> =
    task {
        let config = ctx.GetService<Database.DbConfig>()
        let clock = ctx.GetService<TimeProvider>()
        let remoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string

        let recordFor userId outcome eventType detail =
            record
                config
                clock
                { event eventType outcome Actor.User with
                    UserId = userId
                    ConnectorSlug = Some connector.Slug
                    RemoteIp = remoteIp
                    Detail = detail }

        let record = recordFor None

        let fail ctx code = fail ctx (Some returnUrl) code

        // The allowed email domains (specs/006), judged on what the provider
        // has just said about THIS sign-in and before anything is written: a
        // person who is refused leaves no user, link or update behind.
        let! verdict =
            task {
                if applyEmailRule then
                    let! rule = EmailDomainRules.ruleFor config connector
                    return EmailDomains.check rule profile.Email profile.EmailVerified
                else
                    return EmailDomains.Admitted
            }

        match verdict with
        | EmailDomains.Refused(level, reason) ->
            // The event names the reason, the level and the normalised
            // domain; never the address, and no user (there is none yet).
            do!
                record
                    Failure
                    SignInFailed
                    (Some(refusalDetail reason level Callback (EmailDomains.domainOf profile.Email)))

            return fail ctx "account-not-allowed"

        | EmailDomains.Admitted ->

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

        // The session remembers the domain it was admitted on, but only when
        // the email was verified: that is what a later re-check may rely on.
        let admittedDomain =
            if profile.EmailVerified then EmailDomains.domainOf profile.Email else None

        do! Sessions.signIn ctx resolution.User.Id connector.Kind.Value connector.Slug admittedDomain
        do! recordFor userId Success SignInSucceeded None

        // Back to the authorization request this all started for, replayed
        // byte for byte so OpenIddict sees exactly what the app sent.
        ctx.Response.Redirect returnUrl
        return ()
    }

/// The body of both routes. `mode` is the one the request arrived in, `answer`
/// and `code` what the provider sent (read by the route, from the query string
/// or the form) and `postedUser` the unsigned `user` field of a form post.
let private callback
    (mode: OAuthRoundTrip.ResponseMode)
    (answer: OAuthRoundTrip.Answer)
    (code: string option)
    (postedUser: string option)
    (slug: string)
    : EndpointHandler =
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
            let earlyReturnUrl = OAuthRoundTrip.returnUrlOf protectors clock ctx mode

            let completion = OAuthRoundTrip.complete protectors clock ctx mode slug answer

            match completion with
            | Error error ->
                do! record Failure SignInFailed (Some error.Detail)

                match error with
                // `user_cancelled_authorize` is Apple's word for Cancel.
                | OAuthRoundTrip.ProviderReturnedError("access_denied" | "user_cancelled_authorize") ->
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

            // The second lock on the two modes. Each has a cookie of its own,
            // so a correlation of one cannot be redeemed in the other; this
            // also refuses an answer that came the other way than its kind
            // uses, should that ever not hold.
            if connector.Endpoints.AnswersByFormPost <> (mode = OAuthRoundTrip.FormPost) then
                do! record Failure SignInFailed (Some "response_mode_mismatch")
                return fail ctx "sign-in-failed"
            else

            match code with
            | None ->
                do! record Failure SignInFailed (Some "no_authorization_code")
                return fail ctx "sign-in-failed"

            | Some code ->

            // Unprotected at the moment of use and never held anywhere else.
            let secrets = ctx.GetService<SecretStores>()

            let! secret =
                match Secrets.SecretRef.Decode connector.ClientSecretRef with
                | Ok reference ->
                    // A value served from the grace period is recorded here, where
                    // the connector and the caller's address are known.
                    secrets.Read(
                        reference,
                        (fun cause ->
                            recordStoreStale config clock slug remoteIp reference.Store cause),
                        ctx.RequestAborted
                    )
                | Error _ -> System.Threading.Tasks.Task.FromResult(Error Secrets.NotFound)

            match secret with
            | Error cause ->
                // With the local store, almost always a lost or unmounted
                // /data/keys: the ciphertext is there and the key ring that
                // reads it is not. With an external one, the cause word says
                // whether to look at the network, the grant or the name. Both
                // events carry only the store id and that word (FR-015); the
                // person sees the same page as for any failed sign-in.
                let store =
                    match Secrets.SecretRef.Decode connector.ClientSecretRef with
                    | Ok reference -> reference.Store
                    | Error _ -> "unknown"

                do! recordStoreFailure config clock Actor.User slug remoteIp store "get" cause

                do!
                    record
                        Failure
                        SignInFailed
                        (Some $"connector_secret_unreadable;store={store};cause={cause.Cause}")

                return fail ctx "sign-in-failed"

            | Ok secret ->

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
                      Nonce = correlation.Nonce
                      PostedUser = postedUser }

            match exchange with
            | Error error ->
                // The operator gets the cause; the end user gets a code.
                do! record Failure SignInFailed (Some error.Detail)
                return fail ctx (Picker.failureCode error)

            | Ok profile ->

            do! finishSignIn ctx connector profile correlation.ReturnUrl true
            return ()
        }

/// A redirect's answer: everything is in the query string.
let handler (slug: string) : EndpointHandler =
    fun ctx ->
        let code =
            match ctx.Request.Query.TryGetValue "code" with
            | true, values when values.Count > 0 -> Option.ofObj values[0]
            | _ -> None

        callback OAuthRoundTrip.Redirect (OAuthRoundTrip.answerOfQuery ctx) code None slug ctx

/// The most a form post may weigh. A real answer is an `id_token`, a code and a
/// state: a few kilobytes.
[<Literal>]
let private MaxFormBytes = 65536

/// Whether the request's body is `application/x-www-form-urlencoded`: the media
/// type alone, case-insensitive, whatever parameters (`charset`) follow it. It is
/// what Apple sends. `HasFormContentType` would also accept `multipart/form-data`,
/// which Apple never sends and which has no business here.
let private isUrlEncodedForm (ctx: HttpContext) =
    match Option.ofObj ctx.Request.ContentType with
    | None -> false
    | Some contentType ->
        let mediaType =
            match contentType.IndexOf ';' with
            | -1 -> contentType
            | index -> contentType.Substring(0, index)

        String.Equals(mediaType.Trim(), "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)

/// The answer of a form post. A body that is not a url-encoded form, is too
/// large or cannot be read is an answer with nothing in it, NOT a refusal here: the
/// shared body must still run, because `OAuthRoundTrip.complete` is what
/// deletes the correlation cookie before anything is judged. The person gets
/// the ordinary failure (`state_missing`, or `no_correlation_cookie`).
let private readPostedAnswer (ctx: HttpContext) : Task<OAuthRoundTrip.Answer * string option * string option> =
    task {
        let nothing: OAuthRoundTrip.Answer * string option * string option =
            ({ OAuthRoundTrip.Answer.State = None; OAuthRoundTrip.Answer.Error = None }, None, None)

        let declared = ctx.Request.ContentLength

        if declared.HasValue && declared.Value > int64 MaxFormBytes then
            // Declared too large: the body is not read at all.
            return nothing
        elif not (isUrlEncodedForm ctx) then
            return nothing
        else
            try
                // The real cap on the bytes read, also for a body that does not
                // declare its length: where the server lets the limit be set (Kestrel
                // does), it stops reading at the cap and the read throws. Together
                // with the `Content-Length` check above, that is the whole bound on
                // the body. The limits in `FormOptions` below bound the fields only:
                // the body is not buffered here, so `BufferBodyLengthLimit` would do
                // nothing and is not set.
                match ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() with
                | null -> ()
                | feature when not feature.IsReadOnly -> feature.MaxRequestBodySize <- Nullable(int64 MaxFormBytes)
                | _ -> ()

                let options =
                    FormOptions(
                        ValueCountLimit = 8,
                        KeyLengthLimit = 64,
                        ValueLengthLimit = MaxFormBytes
                    )

                let feature = FormFeature(ctx.Request, options)
                let! form = feature.ReadFormAsync ctx.RequestAborted

                let field (name: string) =
                    match form.TryGetValue name with
                    | true, values when values.Count > 0 ->
                        Option.ofObj values[0] |> Option.filter (fun v -> v.Length > 0)
                    | _ -> None

                return ({ OAuthRoundTrip.Answer.State = field "state"; OAuthRoundTrip.Answer.Error = field "error" }, field "code", field "user")
            with _ ->
                return nothing
    }

/// A form post's answer: the values are the body of the POST.
let postHandler (slug: string) : EndpointHandler =
    fun ctx ->
        task {
            let! answer, code, postedUser = readPostedAnswer ctx
            return! callback OAuthRoundTrip.FormPost answer code postedUser slug ctx
        }

// This POST takes NO antiforgery token, on purpose, and does not call
// `WebForms.readValidated`. The form is Apple's page: no page of Umbral's could
// have written a token into it. What stands in its place is the same
// construction as the token (a secret in a cookie, a matching value in the
// request): the browser must hold the form-post correlation cookie, which is
// HttpOnly and encrypted, and the posted `state` must equal the one inside it,
// a value that left Umbral once, in the redirect to Apple, in this browser. A
// page on another site can make the browser send the cookie but can neither
// read it nor know `state`. `state` is compared before the provider's `error`
// is believed, so a forged POST that carries `error=user_cancelled_authorize`
// and no right `state` is `state_missing` or `state_mismatch`, never a
// "you cancelled" that did not happen, and whatever it puts in `error` is
// reduced to a plain word before it reaches the log. All a forged POST can do
// is spend one sign-in attempt, because the cookie is deleted before anything
// is compared. Nothing is read from the session, and no user, link or session is written on a
// refused path (specs/013 research R3).
//
// `DisableAntiforgery` is what makes the route work at all: `UseAntiforgery`
// otherwise records a missing token on the request, and reading a form that
// has such a verdict throws, so every Apple answer would be a server error.
let endpoint =
    [ GET [ routef "/connect/{%s}/callback" handler |> configureEndpoint _.RequireRateLimiting(RateLimits.SignIn) ]
      POST [ routef "/connect/{%s}/callback" postHandler
             |> configureEndpoint (fun builder ->
                 builder.RequireRateLimiting(RateLimits.SignIn) |> ignore
                 builder.DisableAntiforgery() |> ignore
                 builder) ] ]
