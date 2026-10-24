/// The Passkey pane of the Connectors screen and its three routes (specs/008).
///
/// The passkey kind has no client id, no secret and no callback URL, so it has
/// a pane and a save of its own rather than bending the generic ones: the pane
/// chooses who may enrol, and the operator invites people from it.
module Umbral.Handlers.Admin.PasskeyConnector

open System
open Microsoft.AspNetCore.DataProtection
open Microsoft.AspNetCore.Http
open OpenIddict.Abstractions
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Domains
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Views
open Umbral.Handlers.Admin.ConnectorPaths

[<Literal>]
let private Slug = "passkey"

/// The invitation link, shown once. A Data-Protected, single-use cookie rather
/// than a query string, for the reason `Apps.SecretReveal` gives: a link that
/// signs someone up must not land in access logs, history or `Referer`.
/// `about` says what the link is for (`connector`; a later task adds `user:<id>`).
module InviteReveal =
    [<Literal>]
    let private CookieName = "umbral_passkey_invite"

    [<Literal>]
    let private Purpose = "Umbral.PasskeyInviteReveal.v1"

    let private Lifetime = TimeSpan.FromMinutes 5.0

    let private protector (ctx: HttpContext) =
        ctx.GetService<IDataProtectionProvider>().CreateProtector Purpose

    let stash (ctx: HttpContext) (about: string) (link: string) =
        let options = CookieOptions()
        options.HttpOnly <- true
        options.IsEssential <- true
        options.Path <- "/admin"
        options.SameSite <- SameSiteMode.Strict
        options.Secure <- ctx.Request.IsHttps
        options.MaxAge <- Lifetime
        ctx.Response.Cookies.Append(CookieName, (protector ctx).Protect $"{about}\u001f{link}", options)

    /// The link, if the cookie is there and is about `about`. Deleted before it
    /// is decoded, so a refresh shows nothing whether or not the decode works.
    let consume (ctx: HttpContext) (about: string) : string option =
        match ctx.Request.Cookies.TryGetValue CookieName with
        | true, value when not (String.IsNullOrEmpty value) ->
            let options = CookieOptions()
            options.Path <- "/admin"
            options.SameSite <- SameSiteMode.Strict
            options.Secure <- ctx.Request.IsHttps
            ctx.Response.Cookies.Delete(CookieName, options)

            try
                match ((protector ctx).Unprotect(nonNull value)).Split '\u001f' with
                | [| a; link |] when String.Equals(a, about) -> Some link
                | _ -> None
            with _ ->
                None
        | _ -> None

let private formatTime (value: DateTimeOffset) =
    value.ToString("yyyy-MM-dd HH:mm 'UTC'", Globalization.CultureInfo.InvariantCulture)

let private enrolmentRadio (t: Texts.Texts) (checkedValue: string) (value: string) (label': string) (hint: string) =
    div(class' = "space-y-1") {
        label(class' = "flex items-center gap-2 text-body-md text-on-surface") {
            let radio = input(type' = "radio", name = "enrolment", value = value)

            if checkedValue = value then
                radio.attr("checked", "checked") |> ignore

            radio
            span() { label' }
        }

        p(class' = "um-hint") { hint }
    }

/// The Passkey pane. `existing` None is the unconfigured kind, whose form
/// creates the connector with the defaults shown. `notice` is the notice block
/// the screen builds (it is compiled after this file); it may be empty.
let pane
    (ctx: HttpContext)
    (t: Texts.Texts)
    (origin: PublicOrigin.PublicOrigin)
    (enabledCount: int)
    (chosenSlugs: Set<string>)
    (existing: Connector option)
    (invitations: PasskeyInvitations.Invitation list)
    (revealedLink: string option)
    (view: Components.FrameView)
    (notice: HtmlElement option)
    : HtmlElement =
    let isConfigured = existing.IsSome
    let isEnabled = existing |> Option.exists (fun c -> c.IsEnabled)
    let isShared = existing |> Option.map (fun c -> c.IsShared) |> Option.defaultValue true
    let enrolment = existing |> Option.map (fun c -> c.PasskeyEnrolment.Value) |> Option.defaultValue "invitations"
    let offeredNowhere = isConfigured && not isShared && not (Set.contains Slug chosenSlugs)
    let isLastEnabled = isEnabled && enabledCount = 1
    let confirmed (path: string) = if isLastEnabled then path + "?confirm=yes" else path

    let element =
        Components.framePane
            view
            "connector-heading"
            (listPath, t.NavConnectors)
            [ Components.paneHeading
                  "connector-heading"
                  ProviderKind.Passkey.DisplayName
                  [ if isConfigured then
                        Components.pill
                            Components.MutedTone
                            (if isShared then t.ConnectorSharedBadge else t.ConnectorNotSharedBadge)

                        Components.stateBadge isEnabled t.Enabled t.Disabled
                    else
                        Components.pill Components.MutedTone t.NotConfigured ]

              if isConfigured then
                  (p(class' = "um-hint um-mono mt-1") { Slug }) :> HtmlElement

              // What the screen has to say: the notice, the invitation link
              // shown once, and why nobody is offered this connector.
              (if notice.IsSome || revealedLink.IsSome || offeredNowhere then
                   (div(class' = "mt-6 space-y-3") {
                       match notice with
                       | Some block -> block
                       | None -> Fragment() :> HtmlElement

                       match revealedLink with
                       | Some link ->
                           div(class' = "space-y-1") {
                               Components.copyField
                                   t.Copy
                                   t.Copied
                                   t.PasskeyInviteButton
                                   "invitation-link"
                                   link
                                   (fun i -> i.data("invitation-link", "") |> ignore)

                               p(class' = "um-hint") { t.PasskeyInvitationLinkNote }
                           }
                           :> HtmlElement
                       | None -> Fragment() :> HtmlElement

                       if offeredNowhere then
                           (div(class' = "um-alert um-alert-warning").attr("role", "note") {
                               Components.icon Theme.Warning
                               span() { t.ConnectorOfferedNowhere }
                           })
                           :> HtmlElement
                   })
                   :> HtmlElement
               else
                   Fragment() :> HtmlElement)

              Components.paneSection
                  t.ConnectorSetupTitle
                  (div(class' = "space-y-5") {
                      p(class' = "text-body-md text-on-surface-variant") { t.PasskeyCardLead }
                      p(class' = "um-hint").data("origin-bound", "") { t.PasskeyOriginBound origin.Text }
                  })

              Components.paneSection
                  t.ConnectorSettingsTitle
                  (form(method = "post", action = "/admin/connectors/passkey", class' = "space-y-3") {
                      ctx.GetAntiforgeryInput()

                      div(class' = "space-y-1") {
                          label(class' = "um-check-row") {
                              let box = input(type' = "checkbox", name = "shared", value = "true", class' = "um-check")

                              if isShared then
                                  box.attr("checked", "checked") |> ignore

                              box
                              span() { t.ConnectorSharedLabel }
                          }

                          p(class' = "um-hint") { t.ConnectorSharedHint }
                      }

                      fieldset(class' = "space-y-2").data("enrolment", "") {
                          enrolmentRadio t enrolment "invitations" t.PasskeyEnrolmentInvitationsLabel t.PasskeyEnrolmentInvitationsHint
                          enrolmentRadio t enrolment "open" t.PasskeyEnrolmentOpenLabel t.PasskeyEnrolmentOpenHint
                      }

                      p(class' = "um-hint").data("rule", "enrolment") { t.PasskeyEnrolmentRule }

                      div(class' = "flex flex-wrap items-center gap-2") {
                          button(type' = "submit", class' = Components.btn Components.Primary) {
                              Components.icon Theme.CheckCircle
                              if isConfigured then t.SaveConnector else t.PasskeyEnableButton
                          }
                      }
                  })

              if isConfigured then
                  Components.paneSection
                      t.PasskeyInvitationsTitle
                      (div(class' = "space-y-5") {
                          form(method = "post", action = "/admin/connectors/passkey/invitations", class' = "space-y-3") {
                              ctx.GetAntiforgeryInput()

                              Components.fieldWith
                                  "text"
                                  []
                                  (fun i -> i.attr("maxlength", "64") |> ignore)
                                  t.PasskeyInviteNoteLabel
                                  "note"
                                  ""
                                  (Some t.PasskeyInviteNoteHint)
                                  None

                              button(type' = "submit", class' = Components.btnSm Components.Secondary) {
                                  t.PasskeyInviteButton
                              }
                          }

                          p(class' = "um-label") { t.PasskeyOpenInvitationsHeading }

                          if invitations.IsEmpty then
                              p(class' = "um-hint") { t.PasskeyNoOpenInvitations }
                          else
                              ul(class' = "space-y-3") {
                                  for invitation in invitations do
                                      li(class' = "space-y-1").data("invitation", invitation.Id) {
                                          p(class' = "text-body-md text-on-surface") {
                                              if invitation.Note = "" then t.PasskeyInvitationNoNote else invitation.Note
                                          }

                                          p(class' = "um-hint") {
                                              $"{t.PasskeyInvitationForLabel} "

                                              match invitation.UserId with
                                              | Some userId ->
                                                  (span().data("invitation-user", userId) {
                                                      invitation.UserDisplayName |> Option.defaultValue userId
                                                  })
                                                  :> HtmlElement
                                              | None -> (span() { t.PasskeyInvitationForNew }) :> HtmlElement
                                          }

                                          p(class' = "um-hint") {
                                              $"{t.PasskeyInvitationCreatedLabel} {formatTime invitation.CreatedUtc} · {t.PasskeyInvitationExpiresLabel} {formatTime invitation.ExpiresUtc}"
                                          }

                                          (form(
                                              method = "post",
                                              action = $"/admin/connectors/passkey/invitations/{invitation.Id}/revoke",
                                              class' = "inline"
                                          ) {
                                              ctx.GetAntiforgeryInput()

                                              // A red ghost, not a red fill: inviting again undoes it, and
                                              // the pane's one red fill is Delete (DESIGN.md).
                                              button(
                                                  type' = "submit",
                                                  class' = Components.btnSm Components.Ghost + " text-error hover:text-error"
                                              ) {
                                                  t.PasskeyInvitationRevokeButton
                                              }
                                          })
                                          |> Components.confirmSubmit t.PasskeyInvitationRevokeConfirm
                                      }
                              }
                      })

              if isConfigured then
                  (Components.paneActions
                      [ (form(method = "post", action = confirmed "/admin/connectors/passkey/toggle", class' = "inline") {
                            ctx.GetAntiforgeryInput()

                            button(type' = "submit", class' = Components.btnSm Components.Ghost)
                                .attr("hx-post", confirmed "/admin/connectors/passkey/toggle")
                                .attr("hx-target", "[data-frame]")
                                .attr("hx-select", "[data-frame]")
                                .attr("hx-swap", "outerHTML") {
                                Components.icon Theme.Power
                                if isEnabled then t.Disable else t.Enable
                            }
                        })
                        |> (if isLastEnabled then Components.confirmSubmit t.ConnectorDisableLastWarning else id)
                        :> HtmlElement

                        (form(method = "post", action = confirmed "/admin/connectors/passkey/delete", class' = "inline") {
                            ctx.GetAntiforgeryInput()

                            button(type' = "submit", class' = Components.btnSm Components.Danger) {
                                Components.icon Theme.DeleteForever
                                t.ConnectorDeleteButton
                            }
                        })
                        |> Components.confirmSubmit (
                            if isLastEnabled then
                                t.ConnectorDeleteLastWarning
                            else
                                t.ConnectorDeleteConfirm ProviderKind.Passkey.DisplayName
                        )
                        :> HtmlElement ])
                      .data("connector-actions", "")
                  :> HtmlElement ]

    element
        .data("connector", Slug)
        .data("connector-kind", Slug)
        .data("connector-shared", (if isShared then "shared" else "not-shared"))
    |> fun e ->
        if isConfigured then
            e.data("connector-state", (if isEnabled then "enabled" else "disabled")) |> ignore

        if offeredNowhere then
            e.data("offered-nowhere", "") |> ignore

        e :> HtmlElement

let private record
    (ctx: HttpContext)
    (kind: SecurityEvents.EventType)
    (userId: string option)
    (detail: string option)
    =
    SecurityEvents.record
        (ctx.GetService<Database.DbConfig>())
        (ctx.GetService<TimeProvider>())
        { SecurityEvents.event kind SecurityEvents.Success SecurityEvents.Actor.Operator with
            ConnectorSlug = Some Slug
            UserId = userId
            Detail = detail
            RemoteIp = ctx.Connection.RemoteIpAddress |> Option.ofObj |> Option.map string }

/// Create the connector, or change who may enrol and whether it is shared.
/// Unsharing warns about stranded apps as the generic save does.
let save: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! form = WebForms.readValidated ctx
            let shared = WebForms.checkbox form "shared"
            let confirmed = ctx.TryGetQueryValue "confirm" = Some "yes"

            match Passkeys.parseEnrolment (WebForms.value form "enrolment") with
            | None -> return! WebForms.redirectWithNotice ctx (detailPath Slug) "passkey-connector-invalid"
            | Some enrolment ->
                let detail = Some $"enrolment={enrolment.Value}"
                let! existing = ConnectorStore.findBySlug config Slug

                match existing with
                | None ->
                    let now = clock.GetUtcNow()

                    let! result =
                        ConnectorStore.create
                            config
                            now
                            { Slug = Slug
                              Kind = Passkey
                              DisplayName = "Passkey"
                              ClientId = ""
                              ClientSecretRef = ""
                              ClientSecretHint = ""
                              IssuerUrl = None
                              AllowedEmailDomains = []
                              PasskeyEnrolment = enrolment
                              IsEnabled = true
                              IsShared = shared
                              CreatedUtc = now
                              UpdatedUtc = now }
                            ("", "")

                    ignore result
                    do! record ctx SecurityEvents.ConnectorCreated None detail
                    return! WebForms.redirectWithNotice ctx (detailPath Slug) "passkey-connector-saved"
                | Some stored when stored.Kind <> Passkey ->
                    return! WebForms.redirectWithNotice ctx listPath "connector-unknown"
                | Some stored ->
                    let! stranded =
                        task {
                            if stored.IsShared && not shared && not confirmed then
                                let! connectors = ConnectorStore.listAll config
                                let! apps = AppConnectors.appsWithChoices config (ctx.GetService<IOpenIddictApplicationManager>())

                                let after =
                                    connectors
                                    |> List.map (fun c -> if c.Slug = Slug then { c with IsShared = false } else c)

                                return AppConnectors.strandedByChange apps connectors after
                            else
                                return []
                        }

                    if not stranded.IsEmpty then
                        return!
                            WebForms.redirectWithNoticeAbout
                                ctx
                                (detailPath Slug)
                                "connector-strands-apps"
                                [ "slug", Slug; "change", "unshare" ]
                    else
                        let! _ = ConnectorStore.update config clock Slug "Passkey" "" shared None None
                        let! _ = ConnectorStore.setEnrolment config clock Slug enrolment
                        do! record ctx SecurityEvents.ConnectorUpdated None detail
                        return! WebForms.redirectWithNotice ctx (detailPath Slug) "passkey-connector-saved"
        })

/// An invitation for a new person. The link goes to the single-use cookie; it
/// is in no log line and no event.
let invite: EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! form = WebForms.readValidated ctx
            let note = (WebForms.value form "note").Trim()

            match! ConnectorStore.findBySlug config Slug with
            | None -> return! WebForms.redirectWithNotice ctx (detailPath Slug) "passkey-not-enabled"
            | Some _ when note.Length > 64 ->
                return! WebForms.redirectWithNotice ctx (detailPath Slug) "passkey-invitation-invalid"
            | Some _ ->
                let! _, secret = PasskeyInvitations.create config clock Slug None note
                let origin = ctx.GetService<PublicOrigin.PublicOrigin>()
                InviteReveal.stash ctx "connector" (Passkeys.invitationLink origin.Text secret)
                do! record ctx SecurityEvents.PasskeyInvitationCreated None (Some "for=new")
                return! WebForms.redirectWithNotice ctx (detailPath Slug) "passkey-invitation-created"
        })

/// Revoke an open invitation; one that is not open changes nothing.
let revoke (id: string) : EndpointHandler =
    AdminAuth.requireOperator (fun ctx ->
        task {
            let config = ctx.GetService<Database.DbConfig>()
            let clock = ctx.GetService<TimeProvider>()
            let! _ = WebForms.readValidated ctx

            // Looked up first so the event can name the user it was bound to.
            let! open' = PasskeyInvitations.listOpen config clock Slug
            let userId = open' |> List.tryFind (fun i -> i.Id = id) |> Option.bind (fun i -> i.UserId)

            let! revoked = PasskeyInvitations.revoke config clock id

            if revoked then
                do! record ctx SecurityEvents.PasskeyInvitationRevoked userId None
                return! WebForms.redirectWithNotice ctx (detailPath Slug) "passkey-invitation-revoked"
            else
                return! WebForms.redirectWithNotice ctx (detailPath Slug) "passkey-invitation-not-open"
        })

let endpoint: Endpoint list =
    [ POST
          [ route "/admin/connectors/passkey" save
            route "/admin/connectors/passkey/invitations" invite
            routef "/admin/connectors/passkey/invitations/{%s}/revoke" revoke ] ]
