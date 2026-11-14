/// The one check of a remembered end-user session (specs/018 FR-012), for every
/// place that accepts one: the authorize endpoint, the token endpoint (through
/// `recheck`), `/account/passkeys` and the passkey ceremony of purpose `Add`
/// (through `current`). A session or token outlives the credential that made
/// it, so each use asks again: is the connector still there and of the same
/// kind (task s1ug), and does the user still hold the link or passkey the
/// session was made with (specs/018 FR-009)?
module Umbral.Infrastructure.EndUserSession

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Http
open Oxpecker

/// What `current` found, so each caller keeps its own words for the first
/// three and treats `Refused` (already signed out and recorded) as signed out.
/// The cases after `NoSession` carry the session's user id, for the caller's
/// own event.
type Verdict =
    /// No session cookie, or one that does not authenticate. Nothing was done.
    | NoSession
    /// The session names a user that no longer exists. The session was ended.
    | UserGone of userId: string
    /// The user is disabled. The session was ended.
    | Disabled of userId: string
    /// The connector re-check or the credential check refused. The session was
    /// ended and `sign_in_failed` recorded.
    | Refused of userId: string
    | Admitted of Users.User

/// The connector-kind re-check, then the credential check. None admits; Some
/// detail is the `sign_in_failed` detail of the refusal. Takes plain values so
/// the authorize endpoint (session claims) and the token endpoint (token claims)
/// share it. A missing connector slug is refused as `Unrecorded`: nothing says
/// which connector's rules would apply.
let recheck
    (config: Database.DbConfig)
    (userId: string)
    (connectorSlug: string option)
    (idp: string option)
    (credential: string option)
    (stage: SecurityEvents.RefusalStage)
    : Task<string option>
    =
    task {
        match connectorSlug with
        | None -> return Some(SecurityEvents.connectorChangedDetail Umbral.Domains.Connectors.Unrecorded stage)
        | Some slug ->
            match! ConnectorStore.recheckKind config slug idp with
            | Some change -> return Some(SecurityEvents.connectorChangedDetail change stage)
            | None ->
                let! held = UserStore.credentialStillHeld config userId connectorSlug credential

                return if held then None else Some(SecurityEvents.credentialRemovedDetail stage)
    }

/// The session's enabled user, judged by `recheck` (stage `Account`). A user
/// gone or disabled, or a refusal, signs the session out; a refusal also
/// records `sign_in_failed`. Nothing here judges the email domain or server
/// rules: they decide who signs in to an app, not who manages their passkeys.
let current (ctx: HttpContext) : Task<Verdict> =
    task {
        let config = ctx.GetService<Database.DbConfig>()
        let! session = ctx.AuthenticateAsync Sessions.Scheme

        let principal = if session.Succeeded then Option.ofObj session.Principal else None

        match principal |> Option.bind (fun p -> Sessions.currentSubject p |> Option.map (fun id -> p, id)) with
        | None -> return NoSession
        | Some(principal, id) ->
            match! Users.findById config id with
            | None ->
                do! Sessions.signOut ctx
                return UserGone id
            | Some user when not user.IsEnabled ->
                do! Sessions.signOut ctx
                return Disabled user.Id
            | Some user ->
                let connector = Sessions.currentConnector principal

                match!
                    recheck
                        config
                        user.Id
                        connector
                        (Sessions.currentIdp principal)
                        (Sessions.currentCredential principal)
                        SecurityEvents.Account
                with
                | None -> return Admitted user
                | Some detail ->
                    do! Sessions.signOut ctx

                    do!
                        SecurityEvents.record
                            config
                            (ctx.GetService<TimeProvider>())
                            { SecurityEvents.event
                                  SecurityEvents.SignInFailed
                                  SecurityEvents.Failure
                                  SecurityEvents.Actor.User with
                                UserId = Some user.Id
                                ConnectorSlug = connector
                                RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string
                                Detail = Some detail }

                    return Refused user.Id
    }
