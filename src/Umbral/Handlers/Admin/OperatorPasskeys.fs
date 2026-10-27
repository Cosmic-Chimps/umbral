/// The operator's passkey ceremonies (specs/011-operator-passkeys,
/// contracts/routes.md): signing in to the admin area, confirming a change on
/// the Credential screen, and adding a passkey to the operator credential.
///
/// The verifier and the ceremony store are the connector's, reused. Operator
/// ceremonies have their own purposes, and every handler takes only its own, so
/// no user flow can complete one and an operator ceremony can complete no user
/// flow. Whether an answer is genuine is `WebAuthn.Verifier`'s; this file only
/// decides whose passkey it was. Nothing here names a credential id, a key or a
/// challenge in a log, a URL or a page: a refusal writes one event with the
/// closed reason words and answers `{ "error": <notice code> }`.
module Umbral.Handlers.Admin.OperatorPasskeys

open System
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Oxpecker
open Umbral.Domains.Passkeys
open Umbral.Handlers.Passkeys.Ceremonies
open Umbral.Infrastructure
open Umbral.Infrastructure.SecurityEvents
open Umbral.Views

module Rules = Umbral.Domains.Passkeys
module Store = Umbral.Infrastructure.Passkeys

[<Literal>]
let private NoticeSignIn = "operator-passkey-signin-failed"

[<Literal>]
let private NoticeSignInPasskeyOnly = "operator-passkey-signin-failed-passkey-only"

[<Literal>]
let private NoticeRegistration = "operator-passkey-registration-failed"

[<Literal>]
let private NoticeNotConfirmed = "operator-passkey-not-confirmed"

[<Literal>]
let private NoticeLimit = "operator-passkey-limit"

[<Literal>]
let private NoticePasswordIncorrect = "credential-current-incorrect"

// ---------------------------------------------------------------------------
// Verifying an operator's answer
// ---------------------------------------------------------------------------

/// Whether a passkey answer is the operator's: the ceremony is consumed first,
/// so a reused id fails before anything else is looked at, and it counts only
/// when its purpose is one the caller named. A confirm ceremony also carries the
/// action it was made for (in its `Nickname`, which no confirm otherwise uses),
/// and counts only for that one (`action`); the ceremony is burned either way.
/// The credential must be one the operator holds and the answer must carry the
/// operator's own user handle. Answers with the id of the passkey that signed.
let verifyOperatorAnswer
    (ctx: HttpContext)
    (purposes: Store.Purpose list)
    (action: string option)
    (ceremonyId: string)
    (credential: JsonElement)
    : Task<Result<string, Reason>> =
    task {
        let config = ctx.GetService<Database.DbConfig>()
        let clock = ctx.GetService<TimeProvider>()

        let! found = Store.consumeCeremony config clock ceremonyId

        match found |> Option.filter (fun ceremony -> List.contains ceremony.Purpose purposes) with
        | None -> return Error CeremonyUnknownOrExpired
        | Some ceremony when action.IsSome && ceremony.Nickname <> action -> return Error ConfirmNotForThisAction
        | Some ceremony ->

        let credentialId =
            tryString credential "rawId" |> Option.orElse (tryString credential "id") |> Option.bind tryBytes

        match credentialId with
        | None -> return Error MalformedResponse
        | Some credentialId ->

        match! OperatorPasskeys.findByCredentialId config credentialId with
        | None -> return Error UnknownCredential
        | Some stored ->

        let! handle = OperatorPasskeys.userHandle config clock

        match! ctx.GetService<WebAuthn.Verifier>().VerifyAssertion(
                ceremony.OptionsJson,
                credential,
                stored.PublicKey,
                stored.SignCount,
                handle
            ) with
        | Error reason -> return Error reason
        | Ok newCount ->
            // No row changed: the passkey was removed since it was read, so
            // this answer is not the operator's any more.
            match! OperatorPasskeys.recordUse config clock stored.Id newCount with
            | 0 -> return Error UnknownCredential
            | _ -> return Ok stored.Id
    }

// ---------------------------------------------------------------------------
// The re-check
// ---------------------------------------------------------------------------

/// What a Credential-screen action found when it looked for proof.
type Recheck =
    /// The right password, or a verified operator passkey answer.
    | Confirmed
    /// Nothing was sent: the caller shows its confirm block. Not a failure, so
    /// no event.
    | Missing
    /// Something was sent and it was wrong: the closed detail for the event and
    /// the notice code for the page.
    | Failed of detail: string * notice: string

let private passwordMismatch = Failed("recheck_password_mismatch", NoticePasswordIncorrect)

let private notConfirmed (reason: Reason) =
    Failed(Rules.refusalDetail reason Assertion, NoticeNotConfirmed)

/// The confirm actions: what a confirm ceremony is made for, and what each
/// action accepts. A removal's action names the passkey, and that passkey may
/// not confirm its own removal ("another passkey").
let ConfirmAdd = "add"
let ConfirmSwitchOff = "switch-off"
let ConfirmSwitchOn = "switch-on"
let confirmRemove (id: string) = $"remove:{id}"

let private removalPrefix = "remove:"

/// Whether `action` is one a confirm can be made for.
let isConfirmAction (action: string) =
    action = ConfirmAdd
    || action = ConfirmSwitchOff
    || action = ConfirmSwitchOn
    || (action.StartsWith removalPrefix && action.Length > removalPrefix.Length && action.Length <= 80)

/// A passkey answer wins over a password when both came. `allowPassword` is
/// false for the actions that take a passkey only (turning password sign-in
/// off): a password there is wrong without being looked at. `action` is what
/// this request does; a confirm made for another one is refused.
let recheck
    (ctx: HttpContext)
    (current: string option)
    (answer: (string * JsonElement) option)
    (allowPassword: bool)
    (action: string)
    : Task<Recheck> =
    task {
        match answer, current with
        | Some(ceremonyId, credential), _ ->
            match! verifyOperatorAnswer ctx [ Store.OperatorConfirm ] (Some action) ceremonyId credential with
            | Ok signedBy when action = confirmRemove signedBy -> return notConfirmed ConfirmNotForThisAction
            | Ok _ -> return Confirmed
            | Error reason -> return notConfirmed reason
        | None, Some password when password <> "" ->
            if not allowPassword then
                return passwordMismatch
            else
                // `verify` is false while password sign-in is off, whatever
                // the password is.
                match! AdminAuth.verify (ctx.GetService<Database.DbConfig>()) password with
                | true -> return Confirmed
                | false -> return passwordMismatch
        | None, _ -> return Missing
    }

/// The re-check of a form post: `current`, `ceremony` and `credential` (the
/// browser's answer as a JSON string, since a form field holds text). A ceremony
/// without a credential, or the reverse, is a malformed answer, not a missing one.
let recheckForm (ctx: HttpContext) (form: IFormCollection) (allowPassword: bool) (action: string) : Task<Recheck> =
    task {
        let current = WebForms.optional form "current"

        match WebForms.optional form "ceremony", WebForms.optional form "credential" with
        | None, None -> return! recheck ctx current None allowPassword action
        | Some ceremonyId, Some credentialText ->
            let parsed =
                try
                    Some(JsonDocument.Parse credentialText)
                with :? JsonException ->
                    None

            match parsed with
            | None -> return notConfirmed MalformedResponse
            | Some document ->
                use document = document
                return! recheck ctx current (Some(ceremonyId, document.RootElement)) allowPassword action
        | _ -> return notConfirmed MalformedResponse
    }

/// The re-check of a JSON body, which only the add takes: `current` and `ceremony`
/// strings, `credential` an object. The password is allowed; `verify` refuses it
/// while it is off.
let recheckJson (ctx: HttpContext) (body: JsonElement) : Task<Recheck> =
    let current = tryString body "current" |> Option.filter (fun value -> value <> "")

    match tryString body "ceremony", tryProperty body "credential" with
    | None, None -> recheck ctx current None true ConfirmAdd
    | Some ceremonyId, Some credential -> recheck ctx current (Some(ceremonyId, credential)) true ConfirmAdd
    | _ -> Task.FromResult(notConfirmed MalformedResponse)

// ---------------------------------------------------------------------------
// Events and refusals
// ---------------------------------------------------------------------------

let private recordEvent
    (ctx: HttpContext)
    (eventType: EventType)
    (outcome: Outcome)
    (detail: string option)
    : Task =
    record
        (ctx.GetService<Database.DbConfig>())
        (ctx.GetService<TimeProvider>())
        { event eventType outcome Actor.Operator with
            RemoteIp = remoteIpOf ctx
            Detail = detail }

/// The event first, then the answer.
let private refuseWith
    (ctx: HttpContext)
    (eventType: EventType)
    (detail: string)
    (notice: string)
    : Task =
    task {
        do! recordEvent ctx eventType Failure (Some detail)
        do! writeObject ctx 400 [ "error", text notice ]
    }

let private refuseSignIn (ctx: HttpContext) (reason: Reason) : Task =
    task {
        // The notice depends on whether a password is on offer on the page.
        let! passwordOn = OperatorPasskeys.passwordSignInEnabled (ctx.GetService<Database.DbConfig>())
        let notice = if passwordOn then NoticeSignIn else NoticeSignInPasskeyOnly
        do! refuseWith ctx OperatorSignInFailed (Rules.refusalDetail reason Assertion) notice
    }

let private refuseRegistration (ctx: HttpContext) (reason: Reason) : Task =
    refuseWith ctx OperatorPasskeyAdded (Rules.refusalDetail reason Registration) NoticeRegistration

/// Starts a ceremony of one purpose from the options the library made and
/// answers `{ ceremony, options }`.
let private startAndAnswer
    (ctx: HttpContext)
    (purpose: Store.Purpose)
    (optionsJson: string)
    (nickname: string option)
    : Task =
    task {
        let config = ctx.GetService<Database.DbConfig>()
        let clock = ctx.GetService<TimeProvider>()
        let ceremonyId = Store.newCeremonyId ()

        do!
            Store.startCeremony
                config
                clock
                { Id = ceremonyId
                  Purpose = purpose
                  OptionsJson = optionsJson
                  UserId = None
                  InvitationId = None
                  DisplayName = None
                  Nickname = nickname
                  ReturnUrl = None
                  CreatedUtc = clock.GetUtcNow()
                  ExpiresUtc = clock.GetUtcNow() }

        do!
            writeObject
                ctx
                200
                [ "ceremony", text ceremonyId
                  "options", JsonNode.Parse optionsJson ]
    }

// ---------------------------------------------------------------------------
// Sign-in
// ---------------------------------------------------------------------------

let signInOptions: EndpointHandler =
    fun ctx ->
        withBody ctx (fun _ ->
            task {
                let! configured = AdminAuth.isConfigured (ctx.GetService<Database.DbConfig>())

                if not configured then
                    do! badRequest ctx
                else
                    let options = ctx.GetService<WebAuthn.Verifier>().AssertionOptions()
                    do! startAndAnswer ctx Store.OperatorSignin (options.ToJson()) None
            })

let signIn: EndpointHandler =
    fun ctx ->
        withBody ctx (fun body ->
            task {
                let config = ctx.GetService<Database.DbConfig>()
                let! configured = AdminAuth.isConfigured config

                if not configured then
                    // Like the options route: before first-run setup there is
                    // nothing to sign in to, whatever a restored database holds.
                    do! badRequest ctx
                else
                    // Read before the answer is checked: a removal or a switch-off
                    // that lands meanwhile moves the stamp, and a cookie signed
                    // with the earlier value is refused at its next request.
                    let! stamp = AdminAuth.sessionStamp config

                    match tryString body "ceremony", tryProperty body "credential" with
                    | Some ceremonyId, Some credential ->
                        match! verifyOperatorAnswer ctx [ Store.OperatorSignin ] None ceremonyId credential with
                        | Error reason -> do! refuseSignIn ctx reason
                        | Ok _ ->
                            do! recordEvent ctx OperatorSignedIn Success (Some "passkey")
                            do! AdminAuth.signInWithStamp ctx stamp
                            do! writeObject ctx 200 [ "redirect", text "/admin/connectors" ]
                    | _ -> do! refuseSignIn ctx MalformedResponse
            })

// ---------------------------------------------------------------------------
// The Credential screen
// ---------------------------------------------------------------------------

let confirmOptions: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        withBody ctx (fun body ->
            task {
                // The action the confirm is for rides with the ceremony, so an
                // answer made for one change cannot authorise another (FR-004).
                match tryString body "action" |> Option.filter isConfirmAction with
                | None -> do! writeObject ctx 400 [ "error", text NoticeNotConfirmed ]
                | Some action ->
                    let options = ctx.GetService<WebAuthn.Verifier>().AssertionOptions()
                    do! startAndAnswer ctx Store.OperatorConfirm (options.ToJson()) (Some action)
            }))

let addOptions: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        withBody ctx (fun body ->
            task {
                let config = ctx.GetService<Database.DbConfig>()
                let clock = ctx.GetService<TimeProvider>()
                let t = Texts.current ()

                match validateNickname (tryString body "name" |> Option.toObj) with
                | Error _ -> do! writeObject ctx 400 [ "error", text NoticeRegistration ]
                | Ok name ->

                match! recheckJson ctx body with
                | Missing -> do! refuseWith ctx OperatorPasskeyAdded "recheck_missing" NoticePasswordIncorrect
                | Failed(detail, notice) -> do! refuseWith ctx OperatorPasskeyAdded detail notice
                | Confirmed ->

                let! held = OperatorPasskeys.count config

                if held >= OperatorPasskeys.MaxPasskeys then
                    do! refuseWith ctx OperatorPasskeyAdded "limit_reached" NoticeLimit
                else
                    let! handle = OperatorPasskeys.userHandle config clock
                    let! existing = OperatorPasskeys.list config

                    let options =
                        ctx
                            .GetService<WebAuthn.Verifier>()
                            .RegistrationOptions(
                                handle,
                                t.OperatorPasskeyUserName,
                                t.OperatorPasskeyDisplayName,
                                existing |> List.map (fun p -> p.CredentialId, p.Transports)
                            )

                    do! startAndAnswer ctx Store.OperatorAdd (options.ToJson()) name
            }))

let add: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        withBody ctx (fun body ->
            task {
                let config = ctx.GetService<Database.DbConfig>()
                let clock = ctx.GetService<TimeProvider>()

                match! takeCeremony ctx body [ Store.OperatorAdd ] with
                | None -> do! refuseRegistration ctx CeremonyUnknownOrExpired
                | Some ceremony ->

                match tryProperty body "credential" with
                | None -> do! refuseRegistration ctx MalformedResponse
                | Some credential ->

                match! ctx.GetService<WebAuthn.Verifier>().VerifyRegistration(
                        ceremony.OptionsJson,
                        credential,
                        OperatorPasskeys.credentialIdExists config
                    ) with
                | Error reason -> do! refuseRegistration ctx reason
                | Ok registered ->

                let! handle = OperatorPasskeys.userHandle config clock

                // The ceremony was made from the operator's handle, so this
                // cannot differ unless the stored options were altered.
                if registered.UserId <> handle then
                    do! refuseRegistration ctx UserHandleMismatch
                else
                    let passkey: OperatorPasskeys.OperatorPasskey =
                        { Id = OperatorPasskeys.newId ()
                          CredentialId = registered.CredentialId
                          PublicKey = registered.PublicKey
                          SignCount = registered.SignCount
                          Name = ceremony.Nickname |> Option.defaultWith (fun () -> defaultNickname clock)
                          Transports = registered.Transports
                          CreatedUtc = clock.GetUtcNow()
                          LastUsedUtc = None }

                    match! OperatorPasskeys.add config clock passkey with
                    | OperatorPasskeys.LimitReached -> do! refuseWith ctx OperatorPasskeyAdded "limit_reached" NoticeLimit
                    | OperatorPasskeys.CredentialNotUnique -> do! refuseRegistration ctx CredentialNotUnique
                    | OperatorPasskeys.Added ->
                        do! recordEvent ctx OperatorPasskeyAdded Success None

                        do!
                            writeObject
                                ctx
                                200
                                [ "redirect", text "/admin/credential?msg=operator-passkey-added" ]
            }))

let endpoint =
    let limited (policy: string) handler =
        handler |> configureEndpoint _.RequireRateLimiting(policy)

    [ POST [ route "/admin/signin/passkey/options" signInOptions |> limited RateLimits.SignIn ]
      POST [ route "/admin/signin/passkey" signIn |> limited RateLimits.AdminAuth ]
      POST [ route "/admin/credential/confirm/options" confirmOptions |> limited RateLimits.SignIn ]
      POST [ route "/admin/credential/passkeys/options" addOptions |> limited RateLimits.AdminAuth ]
      // The completion of an add verifies a registration, inside an admin
      // session, against a single-use ceremony that an AdminAuth-limited
      // request started after the re-check: there is nothing to guess, so it
      // takes the ceremony limit and leaves the admin budget to the requests
      // that check a password or a passkey answer.
      POST [ route "/admin/credential/passkeys" add |> limited RateLimits.SignIn ] ]
