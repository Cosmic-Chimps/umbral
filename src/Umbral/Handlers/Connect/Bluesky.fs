/// The Bluesky sign-in (specs/015-bluesky-connector): the handle step, the
/// callback with the authority check of research R6, and the client metadata
/// document.
///
/// The step turns what the person typed into a DID, a PDS and an authorization
/// server, checks the server against the connector's list, pushes the
/// authorization request with a key made for this attempt, and sends the browser
/// there. The key, the DID, the token endpoint and the server's last DPoP nonce
/// travel in the encrypted correlation cookie (`Extra`), never in memory or on
/// disk. The callback trusts nothing the query says but the code: the server is
/// the one the cookie names, and the account is only accepted once that
/// server is shown to be the one the DID itself points to.
///
/// Nothing here logs a code, a token, the key or a nonce.
module Umbral.Handlers.Connect.Bluesky

open System
open System.Net.Http
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading.Tasks
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
module Rules = Umbral.Domains.Bluesky
module Client = Umbral.Infrastructure.Providers.Bluesky

let private card (t: Texts.Texts) (ctx: HttpContext) (returnUrl: string) (otherWays: bool) (typed: string) (error: string option) =
    div(class' = "flex min-h-[60vh] items-center justify-center") {
        div(class' = "um-card w-full max-w-sm space-y-5").data("screen", "bluesky-handle") {
            Components.pageHeading t.BlueskyStepTitle None

            form(method = "post", action = "/connect/bluesky/start", class' = "space-y-4") {
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
                            .attr("placeholder", t.BlueskyHandlePlaceholder)
                            .attr("autofocus", "autofocus")
                        |> ignore)
                    t.BlueskyHandleLabel
                    "handle"
                    typed
                    (Some t.BlueskyHandleHint)
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

/// The connector this step belongs to, of the kind Bluesky, enabled or not.
let private findAny (config: Database.DbConfig) =
    task {
        match! ConnectorStore.findBySlug config "bluesky" with
        | Some connector when connector.Kind = Bluesky -> return Some connector
        | _ -> return None
    }

/// The connector, or nothing when it is absent, of another kind or off: the step
/// then does not exist.
let private findConnector (config: Database.DbConfig) =
    task {
        match! findAny config with
        | Some connector when connector.IsEnabled -> return Some connector
        | _ -> return None
    }

/// The client ID of this instance's origin, or the reason it can't work.
let private clientIdOf (ctx: HttpContext) : Result<Rules.ClientId, string> =
    let origin = ctx.GetService<PublicOrigin.PublicOrigin>()
    Rules.clientIdFor (ctx.GetService<Client.Options>()).AllowDev origin.Uri

let private clientIdText (id: Rules.ClientId) =
    match id with
    | Rules.Metadata text
    | Rules.Loopback text -> text

let private record' (ctx: HttpContext) (config: Database.DbConfig) kind outcome (detail: string) =
    record
        config
        (ctx.GetService<TimeProvider>())
        { event kind outcome Actor.User with
            ConnectorSlug = Some "bluesky"
            RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string
            Detail = Some detail }

/// An origin that can't work (FR-004): the step does not start; the person gets
/// the sign-in error page and the operator the reason.
let private originUnsuitable (ctx: HttpContext) (config: Database.DbConfig) (returnUrl: string) =
    task {
        do! record' ctx config SignInFailed Failure "bluesky:origin_unsuitable"
        return Picker.redirectWithFailure ctx (Some returnUrl) "sign-in-failed"
    }

/// One function draws the step, so the first visit and every refusal look alike.
/// Chrome is chosen as the Fediverse step chooses it.
let private render (ctx: HttpContext) (returnUrl: string) (typed: string) (error: string option) =
    task {
        let config = ctx.GetService<Database.DbConfig>()
        let t = Texts.current ()
        let! app = Picker.findApp ctx returnUrl

        let! branding =
            match app with
            | Some app -> BrandingStore.forApp config app.ClientId
            | None -> Task.FromResult None

        let! all = ConnectorStore.listAll config

        let! choice =
            match app with
            | Some app -> AppConnectors.choiceFor config app.ClientId
            | None -> Task.FromResult AllShared

        let otherWays = (offeredTo choice all).Length > 1
        let content = card t ctx returnUrl otherWays typed error

        match branding with
        | Some branding ->
            return!
                ctx.WriteHtmlView(content |> Layout.signInPage ctx t t.BlueskyStepTitle { Accent = branding.Accent })
        | None -> return! ctx.WriteHtmlView(content |> Layout.page ctx t t.BlueskyStepTitle)
    }

let private getHandler: EndpointHandler =
    fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()

            match! findConnector config with
            | None -> return! Layout.notFound ctx
            | Some _ ->
                let returnUrl = Picker.sanitizeReturnUrl (ctx.TryGetQueryValue "returnUrl" |> Option.toObj)

                match clientIdOf ctx with
                | Error _ -> return! originUnsuitable ctx config returnUrl
                | Ok _ -> return! render ctx returnUrl "" None
        }

// ---------------------------------------------------------------------------
// The step
// ---------------------------------------------------------------------------

/// Why the step refused: what the person sees, the word of the log, and the host
/// the log names when one is known.
type private Refusal =
    { Message: string
      Reason: string
      Host: string option }

let private refusal message reason host =
    { Message = message
      Reason = reason
      Host = host }

/// The contract's table: each client error becomes one message and one word.
/// `subject` is what the person typed, normalised; `fallbackHost` is the host to
/// name when the error does not know which one failed.
let private refusalOf (t: Texts.Texts) (subject: string) (fallbackHost: string option) (error: Client.ClientError) =
    match error with
    | Client.NonPublic -> refusal t.BlueskyNotAHandle "not_a_handle" None
    | Client.NotFound -> refusal (t.BlueskyNotFound subject) "handle_not_found" None
    | Client.Unreachable host ->
        let host = if host = "" then fallbackHost else Some host
        refusal (t.BlueskyUnreachable(host |> Option.defaultValue subject)) "server_unreachable" host
    | Client.Unsupported _ -> refusal (t.BlueskyUnsupported subject) "server_unsupported" fallbackHost

let private hostOf (origin: string) =
    match Uri.TryCreate(origin, UriKind.Absolute) with
    | true, uri -> Option.ofObj uri |> Option.map _.Host
    | _ -> None

/// The server's host as the connector's list spells it, from the issuer.
let private issuerHost (allowDev: bool) (issuer: string) : string option =
    match Uri.TryCreate(issuer, UriKind.Absolute) with
    | true, uri ->
        match Option.ofObj uri |> Option.map _.Authority |> Option.map (Umbral.Domains.Fediverse.normaliseServer allowDev) with
        | Some(Ok host) -> Some host
        | _ -> None
    | _ -> None

let private andThen (f: 'a -> Task<Result<'b, 'e>>) (task': Task<Result<'a, 'e>>) : Task<Result<'b, 'e>> =
    task {
        match! task' with
        | Ok value -> return! f value
        | Error error -> return Error error
    }

let private mapError (f: 'e -> 'f) (task': Task<Result<'a, 'e>>) : Task<Result<'a, 'f>> =
    task {
        match! task' with
        | Ok value -> return Ok value
        | Error error -> return Error(f error)
    }

/// What a start leaves in the correlation's `Extra`.
let private extraOf (did: string) (handle: string) (tokenEndpoint: string) (jwk: string) (nonce: string) : string =
    let json = JsonObject()
    json["did"] <- JsonValue.Create did
    json["handle"] <- JsonValue.Create handle
    json["tokenEndpoint"] <- JsonValue.Create tokenEndpoint
    json["jwk"] <- JsonValue.Create jwk
    json["nonce"] <- JsonValue.Create nonce
    json.ToJsonString()

/// The step, as a chain of results: the address to send the browser to, or the
/// refusal.
let private begin'
    (ctx: HttpContext)
    (t: Texts.Texts)
    (connector: Connector)
    (clientId: string)
    (returnUrl: string)
    (input: Rules.Input)
    : Task<Result<Uri, Refusal>> =
    let options = ctx.GetService<Client.Options>()
    let allowDev = options.AllowDev
    let http = ctx.GetService<IHttpClientFactory>().CreateClient Client.HttpClientName
    let txt = ctx.GetService<Client.TxtResolver>()

    let subject =
        match input with
        | Rules.Handle handle -> handle
        | Rules.Did did -> did

    let fail = refusalOf t subject

    task {
        // 1. The DID: a handle is resolved, a DID is used as it is.
        let! resolved =
            match input with
            | Rules.Did did -> Task.FromResult(Ok did)
            | Rules.Handle handle ->
                Client.resolveHandle http txt allowDev handle |> mapError (fail (Some handle))

        // 2. Its document; for a handle, the document must name it back.
        let! documented =
            resolved
            |> Task.FromResult
            |> andThen (fun did ->
                Client.resolveDid http options did
                |> mapError (fail (hostOf $"https://{subject}"))
                |> andThen (fun (doc: Rules.DidDocument) ->
                    match input with
                    | Rules.Handle handle when not (Rules.claimsHandle doc handle) ->
                        Task.FromResult(Error(refusal (t.BlueskyNotFound handle) "handle_not_found" None))
                    | _ -> Task.FromResult(Ok(did, doc))))

        // 3. The authorization server of its PDS.
        let! discovered =
            documented
            |> Task.FromResult
            |> andThen (fun (did, doc: Rules.DidDocument) ->
                Client.discover http allowDev doc.Pds
                |> mapError (fail (hostOf doc.Pds))
                |> andThen (fun meta -> Task.FromResult(Ok(did, doc, meta))))

        // 4. The list, before the server is sent anything it has not already
        //    published itself.
        let! allowed =
            discovered
            |> Task.FromResult
            |> andThen (fun (did, doc, meta: Rules.ServerMetadata) ->
                match issuerHost allowDev meta.Issuer with
                | None -> Task.FromResult(Error(refusal (t.BlueskyUnsupported subject) "server_unsupported" None))
                | Some host when not (Umbral.Domains.Fediverse.allows connector.FediverseAccess host) ->
                    Task.FromResult(Error(refusal (t.BlueskyNotAllowed host) "server_not_allowed" (Some host)))
                | Some host -> Task.FromResult(Ok(did, doc, meta, host)))

        // 5. The pushed authorization request, with a key made for this attempt.
        return!
            allowed
            |> Task.FromResult
            |> andThen (fun (did, doc: Rules.DidDocument, meta: Rules.ServerMetadata, host) ->
                task {
                    let origin = ctx.GetService<PublicOrigin.PublicOrigin>()
                    let redirect = (PublicOrigin.callbackUri origin "bluesky").ToString()
                    let state = Ids.newStateValue ()
                    let verifier = Ids.newCodeVerifier ()
                    let jwk = Client.newKey ()

                    match!
                        Client.pushAuthorization
                            http
                            meta
                            clientId
                            redirect
                            state
                            (Ids.codeChallengeS256 verifier)
                            subject
                            jwk
                    with
                    | Error error -> return Error(refusalOf t subject (Some host) error)
                    | Ok pushed ->
                        let handle =
                            match input with
                            | Rules.Handle handle -> handle
                            | Rules.Did _ -> ""

                        let extra = extraOf did handle meta.Token jwk (Option.defaultValue "" pushed.Nonce)

                        OAuthRoundTrip.startWith
                            (ctx.GetService<IDataProtectionProvider>())
                            (ctx.GetService<TimeProvider>())
                            ctx
                            OAuthRoundTrip.Redirect
                            "bluesky"
                            meta.Issuer
                            returnUrl
                            extra
                            state
                            verifier
                        |> ignore

                        return Ok(Client.authorizeUrl meta clientId pushed.RequestUri)
                })
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
                let typed = WebForms.value form "handle"
                let returnUrl = Picker.sanitizeReturnUrl (WebForms.value form "returnUrl")
                let allowDev = (ctx.GetService<Client.Options>()).AllowDev

                let refuse (r: Refusal) =
                    task {
                        let server =
                            match r.Host with
                            | Some host -> $";server={host}"
                            | None -> ""

                        do! record' ctx config SignInFailed Failure $"bluesky:{r.Reason}{server}"
                        ctx.Response.StatusCode <- 422
                        return! render ctx returnUrl typed (Some r.Message)
                    }

                match clientIdOf ctx with
                | Error _ -> return! originUnsuitable ctx config returnUrl
                | Ok clientId ->

                match Rules.parseInput allowDev typed with
                | None -> return! refuse (refusal t.BlueskyNotAHandle "not_a_handle" None)
                | Some input ->
                    match! begin' ctx t connector (clientIdText clientId) returnUrl input with
                    | Error r -> return! refuse r
                    | Ok url -> ctx.Response.Redirect(url.ToString())
        }

// ---------------------------------------------------------------------------
// The callback (research R6)
// ---------------------------------------------------------------------------

/// What the start left in the cookie.
type private Stored =
    { Did: string
      Handle: string
      TokenEndpoint: string
      Jwk: string
      Nonce: string }

let private storedOf (extra: string) : Stored option =
    try
        use doc = JsonDocument.Parse extra
        let root = doc.RootElement

        let field (name: string) =
            match root.TryGetProperty name with
            | true, value when value.ValueKind = JsonValueKind.String -> Option.ofObj (value.GetString()) |> Option.defaultValue ""
            | _ -> ""

        let stored =
            { Did = field "did"
              Handle = field "handle"
              TokenEndpoint = field "tokenEndpoint"
              Jwk = field "jwk"
              Nonce = field "nonce" }

        if (Rules.parseDid stored.Did).IsSome && stored.TokenEndpoint <> "" && stored.Jwk <> "" then
            Some stored
        else
            None
    with _ ->
        None

let private callbackHandler: EndpointHandler =
    fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let protectors = ctx.GetService<IDataProtectionProvider>()
            let options = ctx.GetService<Client.Options>()
            let allowDev = options.AllowDev

            // Read before `complete` consumes the cookie, so its own failures can
            // still return to the app's page.
            let early = OAuthRoundTrip.peek protectors clock ctx OAuthRoundTrip.Redirect
            let earlyReturnUrl = early |> Option.map _.ReturnUrl

            let earlyServer =
                match early |> Option.bind (fun c -> hostOf c.Server) with
                | Some host -> $";server={host}"
                | None -> ""

            match OAuthRoundTrip.complete protectors clock ctx OAuthRoundTrip.Redirect "bluesky" (OAuthRoundTrip.answerOfQuery ctx) with
            | Error error ->
                do! record' ctx config SignInFailed Failure $"bluesky:exchange_failed;{error.Detail}{earlyServer}"

                match error with
                | OAuthRoundTrip.ProviderReturnedError "access_denied" ->
                    return Picker.redirectWithFailure ctx earlyReturnUrl "access-denied"
                | _ -> return Picker.redirectWithFailure ctx earlyReturnUrl "sign-in-failed"

            | Ok correlation ->

            let issuer = correlation.Server
            let host = issuerHost allowDev issuer |> Option.defaultValue ""
            let serverTag = if host = "" then "" else $";server={host}"

            let failWith (word: string) (code: string) =
                task {
                    do! record' ctx config SignInFailed Failure $"bluesky:{word}{serverTag}"
                    return Picker.redirectWithFailure ctx (Some correlation.ReturnUrl) code
                }

            let fail (word: string) = failWith word "sign-in-failed"

            // 2. `iss` is the server that answered; a missing one fails too.
            let iss =
                match ctx.Request.Query.TryGetValue "iss" with
                | true, values when values.Count > 0 -> Option.ofObj (values[0]) |> Option.defaultValue ""
                | _ -> ""

            if iss = "" || iss <> issuer then
                return! fail "iss_mismatch"
            else

            // 3. The connector, and the list again: it may have changed since the
            //    round trip began.
            match! findConnector config with
            | None -> return! fail "exchange_failed"
            | Some connector ->

            if host = "" then
                return! fail "exchange_failed"
            elif not (Umbral.Domains.Fediverse.allows connector.FediverseAccess host) then
                return! failWith "server_not_allowed" "account-not-allowed"
            else

            // 4. What the start stored.
            match storedOf correlation.Extra, Rules.clientIdFor allowDev (ctx.GetService<PublicOrigin.PublicOrigin>()).Uri with
            | None, _
            | _, Error _ -> return! fail "exchange_failed"
            | Some stored, Ok clientId ->

            match ctx.Request.Query.TryGetValue "code" with
            | true, values when values.Count > 0 && not (String.IsNullOrEmpty values[0]) ->
                let code = string values[0]
                let http = ctx.GetService<IHttpClientFactory>().CreateClient Client.HttpClientName
                let origin = ctx.GetService<PublicOrigin.PublicOrigin>()
                let redirect = (PublicOrigin.callbackUri origin "bluesky").ToString()

                // 5. The code for tokens, with the stored key.
                match!
                    Client.exchange
                        http
                        stored.TokenEndpoint
                        (clientIdText clientId)
                        redirect
                        code
                        correlation.CodeVerifier
                        stored.Jwk
                        (if stored.Nonce = "" then None else Some stored.Nonce)
                with
                | Error _ -> return! fail "exchange_failed"
                | Ok answer ->

                // 6. The token answer names the account the sign-in began with.
                match Rules.checkTokenAnswer stored.Did answer with
                | Error word -> return! fail word
                | Ok did ->

                // 7. The authority check: from nothing cached, the DID's own
                //    server must be the one that answered. Any failure on the way
                //    is a refusal, never a pass.
                let! fresh =
                    task {
                        match! Client.resolveDid http options did with
                        | Error _ -> return None
                        | Ok doc ->
                            match! Client.discover http allowDev doc.Pds with
                            | Ok meta when meta.Issuer = issuer -> return Some doc
                            | _ -> return None
                    }

                match fresh with
                | None -> return! fail "not_authoritative"
                | Some doc ->

                // 8. The handle shown is one the document still names.
                let handle =
                    if stored.Handle <> "" && Rules.claimsHandle doc stored.Handle then
                        Some stored.Handle
                    else
                        Rules.handleOf doc

                // 9. The profile, from the PDS, without authentication.
                let! name, picture = Client.profile http doc.Pds did

                let displayName =
                    name |> Option.orElse handle |> Option.orElse (Some did)

                let profile: ProviderAdapters.ProviderProfile =
                    { Subject = did
                      Email = None
                      EmailVerified = false
                      DisplayName = displayName
                      AvatarUrl = picture
                      Username = handle
                      ProfileUrl = Some $"https://bsky.app/profile/{did}" }

                do! Callback.finishSignIn ctx connector profile correlation.ReturnUrl false
                do! BlueskyAccounts.recordServer config did host

            | _ -> return! fail "exchange_failed"
        }

// ---------------------------------------------------------------------------
// The client metadata document (FR-005)
// ---------------------------------------------------------------------------

let private metadataHandler: EndpointHandler =
    fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()

            match! findAny config with
            | None -> return! Layout.notFound ctx
            | Some _ ->
                match clientIdOf ctx with
                | Ok(Rules.Metadata url) ->
                    let origin = ctx.GetService<PublicOrigin.PublicOrigin>()
                    ctx.Response.StatusCode <- 200
                    ctx.Response.ContentType <- "application/json; charset=utf-8"
                    ctx.Response.Headers["Cache-Control"] <- "public, max-age=300"
                    do! ctx.Response.WriteAsync(Rules.clientMetadataJson origin.Uri url)
                | _ -> return! Layout.notFound ctx
        }

let endpoint =
    [ GET [ route "/connect/bluesky/callback" callbackHandler |> configureEndpoint _.RequireRateLimiting(RateLimits.SignIn) ]
      GET [ route "/connect/bluesky/start" getHandler |> configureEndpoint _.RequireRateLimiting(RateLimits.SignIn) ]
      POST [ route "/connect/bluesky/start" postHandler |> configureEndpoint _.RequireRateLimiting(RateLimits.SignIn) ]
      GET [ route "/connect/bluesky/client-metadata.json" metadataHandler ] ]
