/// The account step of the Fediverse connector (specs/010): the person says
/// where their account is, Umbral finds and checks the server, registers itself
/// there on first use, and starts the round trip bound to that server.
module Umbral.Handlers.Connect.Fediverse

open System
open System.Net
open System.Net.Http
open System.Threading
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.DataProtection
open Microsoft.AspNetCore.Http
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Domains
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Infrastructure.SecurityEvents
open Umbral.Views
module Rules = Umbral.Domains.Fediverse
module Client = Umbral.Infrastructure.Providers.Fediverse

/// How long a registration goes without being asked whether the server still
/// knows it (research R8).
let private recheckAfter = TimeSpan.FromHours 24.0

let private card (t: Texts.Texts) (ctx: HttpContext) (returnUrl: string) (otherWays: bool) (typed: string) (error: string option) =
    div(class' = "flex min-h-[60vh] items-center justify-center") {
        div(class' = "um-card w-full max-w-sm space-y-5").data("screen", "fediverse-account") {
            Components.pageHeading t.FediverseStepTitle None

            form(method = "post", action = "/connect/fediverse/start", class' = "space-y-4") {
                ctx.GetAntiforgeryInput()
                input(type' = "hidden", name = "returnUrl", value = returnUrl)

                Components.fieldWith
                    "text"
                    []
                    (fun i ->
                        i
                            .attr("autocomplete", "username")
                            .attr("autocapitalize", "none")
                            .attr("spellcheck", "false")
                            .attr("inputmode", "email")
                            .attr("placeholder", t.FediverseAccountPlaceholder)
                            .attr("autofocus", "autofocus")
                        |> ignore)
                    t.FediverseAccountLabel
                    "account"
                    typed
                    (Some t.FediverseAccountHint)
                    error

                button(type' = "submit", class' = Components.btn Components.Primary + " w-full justify-center") {
                    t.FediverseContinue
                }
            }

            if otherWays then
                a(href = $"/signin?returnUrl={Uri.EscapeDataString returnUrl}", class' = Components.btn Components.Ghost) {
                    Components.icon Theme.ChevronLeft
                    t.FediverseOtherWays
                }
        }
    }

/// The connector this step belongs to, or nothing when it is absent, of
/// another kind, or off: the step then does not exist.
let private findConnector (config: Database.DbConfig) =
    task {
        match! ConnectorStore.findBySlug config "fediverse" with
        | Some connector when connector.Kind = Fediverse && connector.IsEnabled -> return Some connector
        | _ -> return None
    }

/// One function draws the step, so the first visit and every refusal look alike.
/// Chrome is chosen as the passkey page chooses it: branded only from the app
/// the `returnUrl` names, never from anything else on the request.
let private render (ctx: HttpContext) (returnUrl: string) (typed: string) (error: string option) =
    task {
        let config = ctx.GetService<Database.DbConfig>()
        let t = Texts.current ()
        let! app = Picker.findApp ctx returnUrl

        let! branding =
            match app with
            | Some app -> BrandingStore.forApp config app.ClientId
            | None -> System.Threading.Tasks.Task.FromResult None

        // Same rule as the picker: the connectors this app offers.
        let! all = ConnectorStore.listAll config

        let! choice =
            match app with
            | Some app -> AppConnectors.choiceFor config app.ClientId
            | None -> System.Threading.Tasks.Task.FromResult AllShared

        let otherWays = (offeredTo choice all).Length > 1
        let content = card t ctx returnUrl otherWays typed error

        match branding with
        | Some branding ->
            return!
                ctx.WriteHtmlView(
                    content |> Layout.signInPage ctx t t.FediverseStepTitle { Accent = branding.Accent }
                )
        | None -> return! ctx.WriteHtmlView(content |> Layout.page ctx t t.FediverseStepTitle)
    }

let private getHandler: EndpointHandler =
    fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()

            match! findConnector config with
            | None -> return! Layout.notFound ctx
            | Some _ ->
                let returnUrl = Picker.sanitizeReturnUrl (ctx.TryGetQueryValue "returnUrl" |> Option.toObj)
                return! render ctx returnUrl "" None
        }

/// Is any address of this host public? Resolving first means a name that points
/// inside the network is refused before Umbral sends it a single request
/// (FR-008). `Ok false` is "resolves, but only to non-public addresses".
let private resolvesPublic (resolver: Client.HostResolver) (host: string) : Threading.Tasks.Task<Result<bool, unit>> =
    task {
        let name =
            match host.LastIndexOf ':' with
            | -1 -> host
            | at -> host.Substring(0, at)

        use cts = new CancellationTokenSource(TimeSpan.FromSeconds 5.0)

        try
            let! addresses = resolver.Invoke(name, cts.Token)
            return Ok(addresses |> Array.exists Rules.isPublicAddress)
        with _ ->
            return Error()
    }

/// Why the step refused: the text the person sees and the reason the
/// operator's log keeps.
type private Refusal = { Message: string; Reason: string }

/// The same server errors map to the same words wherever they arise.
let private refusalOf (t: Texts.Texts) (host: string) (error: Client.ServerError) =
    let message =
        match error with
        | Client.Unsupported -> t.FediverseUnsupported host
        | Client.NonPublic -> t.FediverseNotAHandle
        | _ -> t.FediverseUnreachable host

    { Message = message; Reason = error.Reason }

/// FR-008: nothing is sent to a host that the list refuses or that resolves
/// only inside the network.
let private checkHost (resolver: Client.HostResolver) (t: Texts.Texts) (allowDev: bool) (access: FediverseAccess) (host: string) =
    task {
        if access.OnlyListed && not (Rules.allows access host) then
            return Some { Message = t.FediverseNotAllowed host; Reason = "server_not_allowed" }
        elif allowDev then
            return None
        else
            match! resolvesPublic resolver host with
            | Ok true -> return None
            | Ok false -> return Some { Message = t.FediverseNotAHandle; Reason = "non_public_address" }
            | Error() -> return Some { Message = t.FediverseUnreachable host; Reason = "unreachable" }
    }

let private record' (ctx: HttpContext) (config: Database.DbConfig) kind outcome (detail: string) =
    record
        config
        (ctx.GetService<TimeProvider>())
        { event kind outcome Actor.User with
            ConnectorSlug = Some "fediverse"
            RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string
            Detail = Some detail }

/// The endpoints a stored registration keeps. The registration endpoint is not
/// kept: it is only read again when Umbral registers again.
let private endpointsOf (registration: FediverseServers.Registration) : Rules.ServerEndpoints =
    { AuthorizeUrl = registration.AuthorizeUrl
      TokenUrl = registration.TokenUrl
      RegistrationUrl = "" }

/// Discover and register, then store: `store` is insert or replace, and answers
/// the registration to use, which after a lost race is the stored one.
let private register
    (client: HttpClient)
    (allowDev: bool)
    (host: string)
    (callback: string)
    (website: string)
    (clock: TimeProvider)
    (store: FediverseServers.Registration -> Threading.Tasks.Task<FediverseServers.Registration>)
    =
    task {
        match! Client.discover client allowDev host with
        | Error error -> return Error error
        | Ok endpoints ->
            match! Client.register client endpoints callback website with
            | Error error -> return Error error
            | Ok(clientId, secret) ->
                let now = clock.GetUtcNow()

                let registration: FediverseServers.Registration =
                    { Host = host
                      ClientId = clientId
                      ClientSecret = secret
                      AuthorizeUrl = endpoints.AuthorizeUrl
                      TokenUrl = endpoints.TokenUrl
                      RedirectUri = callback
                      RegisteredUtc = now
                      CheckedUtc = Some now
                      LastUsedUtc = None }

                let! stored = store registration
                return Ok stored
    }

/// The registration to use for this server, made first when there is none,
/// when the callback address changed, or when the server no longer knows the
/// client. A rejected client is re-registered once (FR-013); a failure of that
/// is final, so a server that keeps refusing cannot loop us.
let private ensureRegistration
    (ctx: HttpContext)
    (config: Database.DbConfig)
    (allowDev: bool)
    (host: string)
    (callback: string)
    : Threading.Tasks.Task<Result<FediverseServers.Registration, Client.ServerError>> =
    task {
        let client = ctx.GetService<IHttpClientFactory>().CreateClient Client.HttpClientName
        let protection = ctx.GetService<IDataProtectionProvider>()
        let clock = ctx.GetService<TimeProvider>()
        let website = (ctx.GetService<PublicOrigin.PublicOrigin>()).Uri.ToString()

        let registered (store: FediverseServers.Registration -> Threading.Tasks.Task<FediverseServers.Registration>) =
            task {
                match! register client allowDev host callback website clock store with
                | Ok registration ->
                    do! record' ctx config FediverseServerRegistered Success $"server={host}"
                    return Ok registration
                | Error error -> return Error error
            }

        let replace registration =
            task {
                do! FediverseServers.replace config protection registration
                return registration
            }

        match! FediverseServers.find config protection host with
        | None ->
            // A racing first sign-in keeps one registration (R7) and both use
            // it: the code must be issued to the client the callback will read.
            // The loser's own client is left unused on the server.
            return! registered (FediverseServers.insertIfAbsent config protection)
        | Some r when r.RedirectUri <> callback -> return! registered replace
        | Some r ->
            let stale =
                match r.CheckedUtc with
                | None -> true
                | Some at -> clock.GetUtcNow() - at > recheckAfter

            if not stale then
                return Ok r
            else
                // R8: ask the server whether it still knows the client, at most
                // once a day, instead of finding out in the middle of a sign-in.
                match! Client.checkClient client (endpointsOf r) r.ClientId r.ClientSecret with
                | Ok() ->
                    do! FediverseServers.markChecked config clock host
                    return Ok r
                | Error Client.ClientRejected -> return! registered replace
                | Error error -> return Error error
    }

let private postHandler: EndpointHandler =
    fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()

            match! findConnector config with
            | None -> return! Layout.notFound ctx
            | Some connector ->
                let! form = WebForms.readValidated ctx
                let t = Texts.current ()
                let typed = WebForms.value form "account"
                let returnUrl = Picker.sanitizeReturnUrl (WebForms.value form "returnUrl")
                let allowDev = (ctx.GetService<Client.Options>()).AllowDev
                let access = connector.FediverseAccess
                let resolver = ctx.GetService<Client.HostResolver>()

                let refuse (host: string option) (refusal: Refusal) =
                    task {
                        let server =
                            match host with
                            | Some host -> $";server={host}"
                            | None -> ""

                        do! record' ctx config SignInFailed Failure $"fediverse:{refusal.Reason}{server}"
                        ctx.Response.StatusCode <- 422
                        return! render ctx returnUrl typed (Some refusal.Message)
                    }

                match Rules.parseInput allowDev typed with
                | None -> return! refuse None { Message = t.FediverseNotAHandle; Reason = "not_a_server" }
                | Some input ->
                    let typedHost =
                        match input with
                        | Rules.Handle(_, domain) -> domain
                        | Rules.Server host -> host

                    match! checkHost resolver t allowDev access typedHost with
                    | Some refusal -> return! refuse (Some typedHost) refusal
                    | None ->

                    // A handle's domain may only be an alias: the server is where
                    // WebFinger says the account lives, and that place is held to
                    // the same list and address checks as the typed one.
                    let! server =
                        task {
                            match input with
                            | Rules.Server host -> return Ok host
                            | Rules.Handle(user, domain) ->
                                let client = ctx.GetService<IHttpClientFactory>().CreateClient Client.HttpClientName
                                let! authority = Client.lookup client allowDev user domain

                                match authority |> Option.map (Rules.normaliseServer allowDev) with
                                | Some(Ok host) when host <> domain ->
                                    match! checkHost resolver t allowDev access host with
                                    | Some refusal -> return Error(host, refusal)
                                    | None -> return Ok host
                                | _ -> return Ok domain
                        }

                    match server with
                    | Error(host, refusal) -> return! refuse (Some host) refusal
                    | Ok host ->
                        let origin = ctx.GetService<PublicOrigin.PublicOrigin>()
                        let callback = (PublicOrigin.callbackUri origin "fediverse").ToString()

                        match! ensureRegistration ctx config allowDev host callback with
                        | Error error -> return! refuse (Some host) (refusalOf t host error)
                        | Ok registration ->
                            let started =
                                OAuthRoundTrip.start
                                    (ctx.GetService<IDataProtectionProvider>())
                                    (ctx.GetService<TimeProvider>())
                                    ctx
                                    "fediverse"
                                    host
                                    returnUrl

                            let url =
                                Client.authorizeUrl
                                    (endpointsOf registration)
                                    registration.ClientId
                                    callback
                                    started.Correlation.State
                                    started.CodeChallenge

                            ctx.Response.Redirect(url.ToString())
        }

/// Coming back from the server. As with every provider, `OAuthRoundTrip.complete`
/// validates the correlation and consumes its cookie first. The server used from
/// here on is the one the correlation names, never anything in this request's
/// query (FR-016, FR-017): a forged callback cannot redirect the code exchange.
let private callbackHandler: EndpointHandler =
    fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let protectors = ctx.GetService<IDataProtectionProvider>()
            let allowDev = (ctx.GetService<Client.Options>()).AllowDev

            // Read before `complete` consumes the cookie, so its own failures can
            // still return to the app's page.
            let earlyReturnUrl = OAuthRoundTrip.returnUrlOf protectors clock ctx

            match OAuthRoundTrip.complete protectors clock ctx "fediverse" with
            | Error error ->
                do! record' ctx config SignInFailed Failure $"fediverse:exchange_failed;{error.Detail}"

                match error with
                | OAuthRoundTrip.ProviderReturnedError "access_denied" ->
                    return Picker.redirectWithFailure ctx earlyReturnUrl "access-denied"
                | _ -> return Picker.redirectWithFailure ctx earlyReturnUrl "sign-in-failed"

            | Ok correlation ->

            let fail code = Picker.redirectWithFailure ctx (Some correlation.ReturnUrl) code
            let server = correlation.Server

            if String.IsNullOrEmpty server then
                do! record' ctx config SignInFailed Failure "fediverse:exchange_failed;no_server"
                return fail "sign-in-failed"
            else

            match! findConnector config with
            | None ->
                do! record' ctx config SignInFailed Failure $"fediverse:exchange_failed;connector_unavailable;server={server}"
                return fail "sign-in-failed"

            | Some connector ->

            // FR-019: the list may have changed since the round trip began.
            if not (Rules.allows connector.FediverseAccess server) then
                do! record' ctx config SignInFailed Failure $"fediverse:server_not_allowed;server={server}"
                return fail "account-not-allowed"
            else

            match! FediverseServers.find config (ctx.GetService<IDataProtectionProvider>()) server with
            | None ->
                do! record' ctx config SignInFailed Failure $"fediverse:client_rejected;server={server}"
                return fail "sign-in-failed"

            | Some registration ->

            match ctx.Request.Query.TryGetValue "code" with
            | true, values when values.Count > 0 && not (String.IsNullOrEmpty values[0]) ->
                let code = string values[0]
                let client = ctx.GetService<IHttpClientFactory>().CreateClient Client.HttpClientName
                let origin = ctx.GetService<PublicOrigin.PublicOrigin>()
                let callback = (PublicOrigin.callbackUri origin "fediverse").ToString()

                match!
                    Client.exchange
                        client
                        (endpointsOf registration)
                        registration.ClientId
                        registration.ClientSecret
                        callback
                        code
                        correlation.CodeVerifier
                with
                | Error Client.ClientRejected ->
                    // R8: the server no longer knows our client. The row is
                    // dropped so the next attempt registers afresh instead of
                    // failing the same way.
                    do! FediverseServers.delete config server
                    do! record' ctx config SignInFailed Failure $"fediverse:client_rejected;server={server}"
                    return fail "sign-in-failed"

                | Error _ ->
                    do! record' ctx config SignInFailed Failure $"fediverse:exchange_failed;server={server}"
                    return fail "sign-in-failed"

                | Ok token ->

                match! Client.account client allowDev server token with
                | Error _ ->
                    do! record' ctx config SignInFailed Failure $"fediverse:account_unreadable;server={server}"
                    return fail "sign-in-failed"

                | Ok account ->
                    do! FediverseServers.markUsed config clock server
                    let profile = Client.toProfile server account
                    return! Callback.finishSignIn ctx connector profile correlation.ReturnUrl false

            | _ ->
                do! record' ctx config SignInFailed Failure $"fediverse:exchange_failed;no_authorization_code;server={server}"
                return fail "sign-in-failed"
        }

let endpoint =
    [ GET [ route "/connect/fediverse/callback" callbackHandler |> configureEndpoint _.RequireRateLimiting(RateLimits.SignIn) ]
      GET [ route "/connect/fediverse/start" getHandler |> configureEndpoint _.RequireRateLimiting(RateLimits.SignIn) ]
      POST [ route "/connect/fediverse/start" postHandler |> configureEndpoint _.RequireRateLimiting(RateLimits.SignIn) ] ]
