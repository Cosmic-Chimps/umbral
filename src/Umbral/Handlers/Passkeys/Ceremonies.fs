/// The four JSON endpoints of a passkey ceremony (specs/008-passkey-connector,
/// contracts/ceremonies.md): the options for a sign-in or a registration, and
/// the completion of each.
///
/// Umbral decides only what is Umbral's: whether the connector is on, who the
/// person is, whether an invitation is open. Whether the browser's answer is
/// genuine is `WebAuthn.Verifier`'s. Every refusal goes through `refuse`, which
/// writes the one `sign_in_failed` event with a closed reason word and answers
/// `{ "error": <notice code> }`; the library's own message, the challenge, the
/// credential id and the invitation secret never reach the log or the event.
module Umbral.Handlers.Passkeys.Ceremonies

open System
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Oxpecker
open Umbral.Domains
open Umbral.Domains.Connectors
open Umbral.Domains.Passkeys
open Umbral.Handlers.Connect
open Umbral.Infrastructure
open Umbral.Infrastructure.SecurityEvents

module Rules = Umbral.Domains.Passkeys
module Store = Umbral.Infrastructure.Passkeys

/// The connector's own slug: there is at most one passkey connector.
[<Literal>]
let private Slug = "passkey"

/// More than any real `PublicKeyCredential.toJSON()` is.
[<Literal>]
let private BodyLimit = 65536

[<Literal>]
let private NoticeSignIn = "passkey-sign-in-failed"

[<Literal>]
let private NoticeRegistration = "passkey-registration-failed"

[<Literal>]
let private NoticeInvitation = "passkey-invitation-unusable"

let private remoteIpOf (ctx: HttpContext) =
    ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string

let private writeJson (ctx: HttpContext) (status: int) (body: JsonNode) : Task =
    ctx.Response.StatusCode <- status
    ctx.Response.ContentType <- "application/json; charset=utf-8"
    ctx.Response.WriteAsync(body.ToJsonString())

/// `JsonValue.Create` and `JsonNode.Parse` are typed nullable, so the fields are
/// too; `JsonObject` takes a null as the JSON `null`.
let private writeObject (ctx: HttpContext) (status: int) (fields: (string * (JsonNode | null)) list) : Task =
    let node = JsonObject()

    for name, value in fields do
        node[name] <- value

    writeJson ctx status node

let private text (value: string) : JsonNode | null = JsonValue.Create value

let private badRequest (ctx: HttpContext) : Task =
    writeObject ctx 400 [ "error", text "bad-request" ]

/// A refusal: the event first, then the answer. `userId` is the user when the
/// ceremony had got far enough to know one. The detail is the closed words
/// only.
let private refuse
    (ctx: HttpContext)
    (stage: Stage)
    (userId: string option)
    (reason: Reason)
    (notice: string)
    : Task =
    task {
        do!
            record
                (ctx.GetService<Database.DbConfig>())
                (ctx.GetService<TimeProvider>())
                { event SignInFailed Failure Actor.User with
                    UserId = userId
                    ConnectorSlug = Some Slug
                    RemoteIp = remoteIpOf ctx
                    Detail = Some(Rules.refusalDetail reason stage) }

        do! writeObject ctx 400 [ "error", text notice ]
    }

/// The reason an invitation that is not open is refused with.
let invitationReason (state: PasskeyInvitations.State) : Reason =
    match state with
    | PasskeyInvitations.Open -> InvitationUnknown
    | PasskeyInvitations.Used -> InvitationUsed
    | PasskeyInvitations.Revoked -> InvitationRevoked
    | PasskeyInvitations.Expired -> InvitationExpired
    | PasskeyInvitations.UserGone -> InvitationUserGone

/// The antiforgery check first, before anything is read, then the body as a JSON
/// object; anything else is `bad-request`.
let private withBody (ctx: HttpContext) (inner: JsonElement -> Task) : Task =
    task {
        do! WebForms.validateAntiforgery ctx

        match! WebForms.readJsonDocument ctx BodyLimit with
        | None -> do! badRequest ctx
        | Some document ->
            use document = document

            if document.RootElement.ValueKind = JsonValueKind.Object then
                do! inner document.RootElement
            else
                do! badRequest ctx
    }

let private tryProperty (element: JsonElement) (name: string) : JsonElement option =
    match element.ValueKind with
    | JsonValueKind.Object ->
        match element.TryGetProperty name with
        | true, value -> Some value
        | _ -> None
    | _ -> None

let private tryString (element: JsonElement) (name: string) : string option =
    match tryProperty element name with
    | Some value when value.ValueKind = JsonValueKind.String -> Option.ofObj (value.GetString())
    | _ -> None

let private fromBase64Url (text: string) : byte array =
    let padded = text.Replace('-', '+').Replace('_', '/')
    Convert.FromBase64String(padded + String('=', (4 - padded.Length % 4) % 4))

/// Base64url to bytes, None for anything that is not.
let private tryBytes (text: string) : byte array option =
    try
        Some(fromBase64Url text)
    with :? FormatException ->
        None

/// The passkey connector when it exists and is on; the only one there is.
let private enabledConnector (config: Database.DbConfig) : Task<Connector option> =
    task {
        let! found = ConnectorStore.findBySlug config Slug

        return
            found
            |> Option.filter (fun connector -> connector.Kind = Passkey && connector.IsEnabled)
    }

// ---------------------------------------------------------------------------
// Sign-in
// ---------------------------------------------------------------------------

let assertionOptions: EndpointHandler =
    fun ctx ->
        withBody ctx (fun body ->
            task {
                let config = ctx.GetService<Database.DbConfig>()
                let clock = ctx.GetService<TimeProvider>()

                match! enabledConnector config with
                | None -> do! refuse ctx Assertion None Reason.ConnectorDisabled NoticeSignIn
                | Some _ ->
                    let options = ctx.GetService<WebAuthn.Verifier>().AssertionOptions()
                    let ceremonyId = Store.newCeremonyId ()

                    do!
                        Store.startCeremony
                            config
                            clock
                            { Id = ceremonyId
                              Purpose = Store.Signin
                              OptionsJson = options.ToJson()
                              UserId = None
                              InvitationId = None
                              DisplayName = None
                              Nickname = None
                              ReturnUrl = Some(Picker.sanitizeReturnUrl (tryString body "returnUrl" |> Option.toObj))
                              CreatedUtc = clock.GetUtcNow()
                              ExpiresUtc = clock.GetUtcNow() }

                    do!
                        writeObject
                            ctx
                            200
                            [ "ceremony", text ceremonyId
                              "options", JsonNode.Parse(options.ToJson()) ]
            })

/// Consumes the ceremony the body names, so a reused id fails before anything
/// else is looked at. Unknown, used, expired and of another purpose are one answer.
let private takeCeremony (ctx: HttpContext) (body: JsonElement) (purposes: Store.Purpose list) =
    task {
        match tryString body "ceremony" with
        | None -> return None
        | Some id ->
            let! found = Store.consumeCeremony (ctx.GetService<Database.DbConfig>()) (ctx.GetService<TimeProvider>()) id
            return found |> Option.filter (fun ceremony -> List.contains ceremony.Purpose purposes)
    }

let assertion: EndpointHandler =
    fun ctx ->
        withBody ctx (fun body ->
            task {
                let config = ctx.GetService<Database.DbConfig>()
                let clock = ctx.GetService<TimeProvider>()
                let refuse = refuse ctx Assertion

                match! takeCeremony ctx body [ Store.Signin ] with
                | None -> do! refuse None CeremonyUnknownOrExpired NoticeSignIn
                | Some ceremony ->

                match! enabledConnector config with
                | None -> do! refuse None Reason.ConnectorDisabled NoticeSignIn
                | Some _ ->

                let credential = tryProperty body "credential"

                let handle =
                    credential
                    |> Option.bind (fun c -> tryProperty c "response")
                    |> Option.bind (fun r -> tryString r "userHandle")
                    |> Option.bind tryBytes
                    |> Option.map Encoding.UTF8.GetString

                let! user =
                    match handle with
                    | Some id -> Users.findById config id
                    | None -> Task.FromResult None

                match credential, user with
                | None, _ -> do! refuse None MalformedResponse NoticeSignIn
                | _, None -> do! refuse None UserNotFound NoticeSignIn
                | Some credential, Some user ->

                let! stored =
                    match tryString credential "rawId" |> Option.orElse (tryString credential "id") |> Option.bind tryBytes with
                    | Some credentialId -> Store.findByCredentialId config credentialId
                    | None -> Task.FromResult None

                match stored with
                | None -> do! refuse (Some user.Id) UnknownCredential NoticeSignIn
                | Some stored ->

                match! ctx.GetService<WebAuthn.Verifier>().VerifyAssertion(ceremony.OptionsJson, credential, stored) with
                | Error reason -> do! refuse (Some user.Id) reason NoticeSignIn
                | Ok newCount ->

                if not user.IsEnabled then
                    do! refuse (Some user.Id) Reason.UserDisabled "account-disabled"
                else

                do! Store.recordUse config clock stored.Id newCount
                do! Sessions.signIn ctx user.Id Slug Slug None

                do!
                    record
                        config
                        clock
                        { event SignInSucceeded Success Actor.User with
                            UserId = Some user.Id
                            ConnectorSlug = Some Slug
                            RemoteIp = remoteIpOf ctx }

                do! writeObject ctx 200 [ "redirect", text (defaultArg ceremony.ReturnUrl "/") ]
            })

// ---------------------------------------------------------------------------
// Registration
// ---------------------------------------------------------------------------

/// Starts a registration for `userId`: the verifier's options, and the ceremony
/// that will complete them.
let private startRegistration
    (ctx: HttpContext)
    (purpose: Store.Purpose)
    (userId: string)
    (name: string)
    (displayName: string)
    (existing: Store.Passkey list)
    (invitationId: string option)
    (newPersonName: string option)
    (nickname: string option)
    (returnUrl: string option)
    : Task =
    task {
        let clock = ctx.GetService<TimeProvider>()
        let options = ctx.GetService<WebAuthn.Verifier>().RegistrationOptions(userId, name, displayName, existing)
        let ceremonyId = Store.newCeremonyId ()

        do!
            Store.startCeremony
                (ctx.GetService<Database.DbConfig>())
                clock
                { Id = ceremonyId
                  Purpose = purpose
                  OptionsJson = options.ToJson()
                  UserId = Some userId
                  InvitationId = invitationId
                  DisplayName = newPersonName
                  Nickname = nickname
                  ReturnUrl = returnUrl
                  CreatedUtc = clock.GetUtcNow()
                  ExpiresUtc = clock.GetUtcNow() }

        do!
            writeObject
                ctx
                200
                [ "ceremony", text ceremonyId
                  "options", JsonNode.Parse(options.ToJson()) ]
    }

/// `mode: invitation`: an open invitation, for a new person (who names
/// themselves) or an existing user (whose account the passkey joins).
let private invitationOptions (ctx: HttpContext) (body: JsonElement) (connector: Connector) : Task =
    task {
        let config = ctx.GetService<Database.DbConfig>()
        let clock = ctx.GetService<TimeProvider>()
        let refuse = refuse ctx Registration

        let! found = PasskeyInvitations.findBySecret config clock (defaultArg (tryString body "invitation") "")

        match found with
        | None -> do! refuse None InvitationUnknown NoticeInvitation
        | Some(invitation, state) when state <> PasskeyInvitations.Open || invitation.ConnectorSlug <> connector.Slug ->
            do! refuse invitation.UserId (invitationReason state) NoticeInvitation
        | Some(invitation, _) ->

        match Rules.validateNickname (tryString body "nickname" |> Option.toObj) with
        | Error _ -> do! refuse invitation.UserId NicknameInvalid NoticeRegistration
        | Ok nickname ->

        match invitation.UserId with
        | Some userId ->
            match! Users.findById config userId with
            | Some user when user.IsEnabled ->
                let! existing = Store.listForUser config userId

                if existing.Length >= Rules.MaxPerUser then
                    do! refuse (Some userId) PasskeyLimitReached NoticeRegistration
                else
                    do!
                        startRegistration
                            ctx
                            Store.Invite
                            userId
                            (defaultArg user.Email user.DisplayName)
                            user.DisplayName
                            existing
                            (Some invitation.Id)
                            None
                            nickname
                            None
            | _ -> do! refuse (Some userId) InvitationUserGone NoticeInvitation
        | None ->
            match Rules.validateDisplayName (tryString body "displayName" |> Option.toObj) with
            | Error _ -> do! refuse None DisplayNameInvalid NoticeRegistration
            | Ok name ->
                do!
                    startRegistration
                        ctx
                        Store.Invite
                        (Ids.newSubject ())
                        name
                        name
                        []
                        (Some invitation.Id)
                        (Some name)
                        nickname
                        None
    }

/// `mode: add`: a signed-in person adding a passkey to their own account.
let private addOptions (ctx: HttpContext) (body: JsonElement) : Task =
    task {
        let config = ctx.GetService<Database.DbConfig>()
        let refuse = refuse ctx Registration
        let! session = ctx.AuthenticateAsync Sessions.Scheme

        match (if session.Succeeded then Option.ofObj session.Principal else None)
              |> Option.bind Sessions.currentSubject with
        | None -> do! refuse None UserNotFound NoticeRegistration
        | Some userId ->

        match! Users.findById config userId with
        | None -> do! refuse (Some userId) UserNotFound NoticeRegistration
        | Some user when not user.IsEnabled -> do! refuse (Some userId) Reason.UserDisabled NoticeRegistration
        | Some user ->

        match Rules.validateNickname (tryString body "nickname" |> Option.toObj) with
        | Error _ -> do! refuse (Some userId) NicknameInvalid NoticeRegistration
        | Ok nickname ->

        let! existing = Store.listForUser config userId

        if existing.Length >= Rules.MaxPerUser then
            do! refuse (Some userId) PasskeyLimitReached NoticeRegistration
        else
            do!
                startRegistration
                    ctx
                    Store.Add
                    user.Id
                    (defaultArg user.Email user.DisplayName)
                    user.DisplayName
                    existing
                    None
                    None
                    nickname
                    None
    }

/// `mode: signup`: anyone, when the connector is open; they name themselves.
let private signupOptions (ctx: HttpContext) (body: JsonElement) (connector: Connector) : Task =
    task {
        let refuse = refuse ctx Registration

        match connector.PasskeyEnrolment with
        | InvitationsOnly -> do! refuse None EnrolmentClosed NoticeRegistration
        | OpenSignUp ->

        match Rules.validateDisplayName (tryString body "displayName" |> Option.toObj) with
        | Error _ -> do! refuse None DisplayNameInvalid NoticeRegistration
        | Ok name ->

        match Rules.validateNickname (tryString body "nickname" |> Option.toObj) with
        | Error _ -> do! refuse None NicknameInvalid NoticeRegistration
        | Ok nickname ->

        let returnUrl = Picker.sanitizeReturnUrl (tryString body "returnUrl" |> Option.toObj)

        do!
            startRegistration
                ctx
                Store.Signup
                (Ids.newSubject ())
                name
                name
                []
                None
                (Some name)
                nickname
                (Some returnUrl)
    }

let registrationOptions: EndpointHandler =
    fun ctx ->
        withBody ctx (fun body ->
            task {
                match! enabledConnector (ctx.GetService<Database.DbConfig>()) with
                | None -> do! refuse ctx Registration None Reason.ConnectorDisabled NoticeRegistration
                | Some connector ->

                match tryString body "mode" with
                | Some "invitation" -> do! invitationOptions ctx body connector
                | Some "signup" -> do! signupOptions ctx body connector
                | Some "add" -> do! addOptions ctx body
                | _ -> do! badRequest ctx
            })

/// The one transaction of a registration: the user (for a new person), the
/// passkey, the invitation spent, and the events. Nothing is kept unless all of
/// it is. A credential id some other registration took first, or an invitation
/// some other redemption spent first, rolls it all back.
let private saveRegistration
    (ctx: HttpContext)
    (ceremony: Store.Ceremony)
    (userId: string)
    (isNewUser: bool)
    (registered: WebAuthn.Registered)
    : Task<Result<unit, Reason>> =
    task {
        let config = ctx.GetService<Database.DbConfig>()
        let clock = ctx.GetService<TimeProvider>()

        let nickname =
            match ceremony.Nickname with
            | Some typed when typed <> "" -> typed
            | _ -> Rules.defaultNickname clock

        let passkey: Store.Passkey =
            { Id = Store.newId ()
              UserId = userId
              CredentialId = registered.CredentialId
              PublicKey = registered.PublicKey
              SignCount = registered.SignCount
              Nickname = nickname
              AaGuid = Some registered.AaGuid
              Transports = registered.Transports
              BackupEligible = Some registered.BackupEligible
              CreatedUtc = clock.GetUtcNow()
              LastUsedUtc = None }

        use! connection = Database.openConnection config
        use transaction = connection.BeginTransaction()

        let evt eventType detail =
            { event eventType Success Actor.User with
                UserId = Some userId
                ConnectorSlug = Some Slug
                RemoteIp = remoteIpOf ctx
                Detail = detail }

        try
            if isNewUser then
                do! Store.insertPasskeyUserIn connection transaction clock userId (defaultArg ceremony.DisplayName "")

            do! Store.insertIn connection transaction clock passkey

            let! spent =
                match ceremony.InvitationId with
                | Some invitationId -> PasskeyInvitations.consumeIn connection transaction clock invitationId
                | None -> Task.FromResult true

            if not spent then
                do! transaction.RollbackAsync()
                return Error InvitationUsed
            else
                if isNewUser then
                    do! recordIn connection transaction clock (evt UserCreated None)

                do!
                    recordIn
                        connection
                        transaction
                        clock
                        (evt PasskeyRegistered (Some $"via={ceremony.Purpose.Value}"))

                do! transaction.CommitAsync()
                return Ok()
        with error when Store.isCredentialIdConflict error ->
            do! transaction.RollbackAsync()
            return Error CredentialNotUnique
    }

let registration: EndpointHandler =
    fun ctx ->
        withBody ctx (fun body ->
            task {
                let config = ctx.GetService<Database.DbConfig>()
                let refuse = refuse ctx Registration

                match! takeCeremony ctx body [ Store.Invite; Store.Signup; Store.Add ] with
                | None -> do! refuse None CeremonyUnknownOrExpired NoticeRegistration
                | Some ceremony ->

                let userId = ceremony.UserId

                // An `add` ceremony belongs to the session that started it.
                let! session = ctx.AuthenticateAsync Sessions.Scheme

                let sessionSubject =
                    (if session.Succeeded then Option.ofObj session.Principal else None)
                    |> Option.bind Sessions.currentSubject

                match! enabledConnector config with
                | None -> do! refuse userId Reason.ConnectorDisabled NoticeRegistration
                | Some connector when ceremony.Purpose = Store.Signup && connector.PasskeyEnrolment <> OpenSignUp ->
                    do! refuse userId EnrolmentClosed NoticeRegistration
                | Some _ when ceremony.Purpose = Store.Add && (sessionSubject.IsNone || sessionSubject <> userId) ->
                    do! refuse userId UserNotFound NoticeRegistration
                | Some _ ->

                match tryProperty body "credential", userId with
                | None, _
                | _, None -> do! refuse userId MalformedResponse NoticeRegistration
                | Some credential, Some userId ->

                match! ctx.GetService<WebAuthn.Verifier>().VerifyRegistration(ceremony.OptionsJson, credential) with
                | Error reason -> do! refuse (Some userId) reason NoticeRegistration
                | Ok registered ->

                // A ceremony whose user is not in the table is a new person; one
                // whose user is there adds to that account.
                let! existing = Users.findById config userId
                let! count = Store.countForUser config userId

                match existing with
                | Some user when not user.IsEnabled -> do! refuse (Some userId) Reason.UserDisabled NoticeRegistration
                | _ when count >= Rules.MaxPerUser -> do! refuse (Some userId) PasskeyLimitReached NoticeRegistration
                | _ ->

                match! saveRegistration ctx ceremony userId existing.IsNone registered with
                | Error reason ->
                    let notice =
                        if reason = InvitationUsed then NoticeInvitation else NoticeRegistration

                    do! refuse (Some userId) reason notice
                | Ok() ->
                    // Adding to an account that is already signed in changes no
                    // session; the others start one through this connector.
                    if ceremony.Purpose <> Store.Add then
                        do! Sessions.signIn ctx userId Slug Slug None

                    let redirect =
                        match ceremony.Purpose, ceremony.ReturnUrl with
                        | Store.Signup, Some returnUrl -> returnUrl
                        | _ -> "/account/passkeys?msg=passkey-added"

                    do! writeObject ctx 200 [ "redirect", text redirect ]
            })

let endpoint =
    let limited handler =
        handler |> configureEndpoint _.RequireRateLimiting(RateLimits.SignIn)

    [ POST [ route "/passkey/assertion/options" assertionOptions |> limited ]
      POST [ route "/passkey/assertion" assertion |> limited ]
      POST [ route "/passkey/registration/options" registrationOptions |> limited ]
      POST [ route "/passkey/registration" registration |> limited ] ]
