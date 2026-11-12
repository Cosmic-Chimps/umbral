/// The pane of the built-in Bluesky connector on the Connectors screen
/// (specs/015), built like the Fediverse pane (design/contracts/bluesky-connector-ui.md,
/// section 4).
///
/// The pane is the set-up facts (what the connector is, the client metadata
/// address, that no email comes back), the settings (shared, which servers may
/// sign in, the servers list) and the action row. Where the instance's address
/// cannot work (`Domains.Bluesky.clientIdFor` says no), the pane says so and, with
/// no connector yet, offers nothing to set. The save is written elsewhere; this
/// file is a view.
module Umbral.Handlers.Admin.BlueskyConnector

open Microsoft.AspNetCore.Http
open Oxpecker
open Oxpecker.ViewEngine
open Umbral.Domains
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Views
open Umbral.Handlers.Admin.ConnectorPaths

module Rules = Umbral.Domains.Fediverse

[<Literal>]
let private Slug = "bluesky"

/// One scope as a radio card, drawn like the Passkey pane's enrolment modes.
let private scopeRadio (checkedValue: string) (value: string) (title: string) (description: string) =
    label(class' = "um-choice") {
        let radio = input(type' = "radio", name = "scope", value = value, class' = "um-choice-radio")

        if checkedValue = value then
            radio.attr("checked", "checked") |> ignore

        radio

        span(class' = "um-choice-body") {
            span(class' = "um-choice-title") { title }
            span(class' = "um-choice-desc") { description }
        }
    }

/// The Bluesky pane. `existing` None is the unconfigured kind. `draft` is the
/// servers text as typed and the refusal message, after a refused save.
/// `allowDev` is the development setting of the Bluesky options.
let pane
    (ctx: HttpContext)
    (t: Texts.Texts)
    (origin: PublicOrigin.PublicOrigin)
    (enabledCount: int)
    (chosenSlugs: Set<string>)
    (existing: Connector option)
    (draft: (string * string) option)
    (view: Components.FrameView)
    (notice: HtmlElement option)
    (allowDev: bool)
    : HtmlElement =
    let clientId = Bluesky.clientIdFor allowDev origin.Uri
    let isConfigured = existing.IsSome
    let isEnabled = existing |> Option.exists (fun c -> c.IsEnabled)
    let isShared = existing |> Option.map (fun c -> c.IsShared) |> Option.defaultValue true
    let onlyListed = existing |> Option.exists (fun c -> c.FediverseAccess.OnlyListed)
    let servers = existing |> Option.map (fun c -> c.FediverseAccess.Servers) |> Option.defaultValue []
    let scope = if onlyListed then "listed" else "any"

    // A refused save draws the form again as the operator left it: the radio and
    // the shared box they chose, not the stored ones (the servers text comes in
    // `draft`). Read only then, from the form being answered.
    let submitted (name: string) =
        if draft.IsSome && ctx.Request.HasFormContentType then
            match ctx.Request.Form.TryGetValue name with
            | true, values when values.Count > 0 -> Some(string values[0])
            | _ -> None
        else
            None

    let formScope = if draft.IsSome then submitted "scope" |> Option.defaultValue scope else scope
    let formShared = if draft.IsSome then submitted "shared" = Some "true" else isShared
    let offeredNowhere = isConfigured && not isShared && not (Set.contains Slug chosenSlugs)
    let isLastEnabled = isEnabled && enabledCount = 1
    let confirmed (path: string) = if isLastEnabled then path + "?confirm=yes" else path

    let serversValue =
        match draft with
        | Some(text, _) -> text
        | None -> Rules.displayServers servers

    let serversError = draft |> Option.map snd
    let blocked = match clientId with Error _ -> true | Ok _ -> false
    // With no connector yet, a blocked address leaves nothing to set; with one,
    // its sections stay, so it can still be switched off or deleted.
    let paneOnlyAlert = blocked && not isConfigured
    let showEmptyNote = isConfigured && onlyListed && List.isEmpty servers && draft.IsNone

    let element =
        Components.framePane
            view
            "connector-heading"
            (listPath, t.NavConnectors)
            [ Components.paneHeading
                  "connector-heading"
                  ProviderKind.Bluesky.DisplayName
                  [ if isConfigured then
                        Components.pill
                            Components.MutedTone
                            (if isShared then t.ConnectorSharedBadge else t.ConnectorNotSharedBadge)

                        Components.stateBadge isEnabled t.Enabled t.Disabled
                    else
                        Components.pill Components.MutedTone t.NotConfigured ]

              if isConfigured then
                  (p(class' = "um-hint um-mono mt-1") { Slug }) :> HtmlElement

              if paneOnlyAlert then
                  (p(class' = "mt-6 text-body-md text-on-surface-variant") { t.BlueskyCardLead }) :> HtmlElement

              (if notice.IsSome || offeredNowhere || blocked then
                   (div(class' = "mt-6 space-y-3") {
                       match notice with
                       | Some block -> block
                       | None -> Fragment() :> HtmlElement

                       if blocked then
                           (div(class' = "um-alert um-alert-warning").attr("role", "note").data("origin-blocked", "") {
                               Components.icon Theme.Warning
                               span() { t.BlueskyOriginBlocked origin.Text }
                           })
                           :> HtmlElement

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

              if not paneOnlyAlert then
                  Components.paneSection
                      t.ConnectorSetupTitle
                      (div(class' = "space-y-5") {
                          p(class' = "text-body-md text-on-surface-variant") { t.BlueskyCardLead }

                          match clientId with
                          | Ok(Bluesky.Metadata url) ->
                              // The whole address, wrapping, with Copy under it, as the
                              // home page's issuer URL (design review nit 1): a field
                              // cut the ".json" off at 1280.
                              (div(class' = "space-y-1").data("client-metadata-url", url) {
                                  Components.copyText
                                      t.Copy
                                      t.Copied
                                      t.BlueskyCopyMetadata
                                      t.BlueskyMetadataLabel
                                      $"metadata-{Slug}"
                                      url

                                  p(class' = "um-hint") { t.BlueskyMetadataHint }
                              })
                              :> HtmlElement
                          | Ok(Bluesky.Loopback _) ->
                              (div(class' = "um-alert um-alert-info").attr("role", "note").data("dev-loopback-note", "") {
                                  Components.icon Theme.Info
                                  span() { t.BlueskyDevNote }
                              })
                              :> HtmlElement
                          | Error _ -> Fragment() :> HtmlElement

                          (div(class' = "um-alert um-alert-info").attr("role", "note").data("no-email-note", "") {
                              Components.icon Theme.Info
                              span() { t.BlueskyNoEmailNote }
                          })
                          :> HtmlElement
                      })

                  Components.paneSection
                      t.ConnectorSettingsTitle
                      (form(method = "post", action = "/admin/connectors/bluesky", class' = "space-y-4") {
                          ctx.GetAntiforgeryInput()

                          div(class' = "space-y-1") {
                              label(class' = "um-check-row") {
                                  let box = input(type' = "checkbox", name = "shared", value = "true", class' = "um-check")

                                  if formShared then
                                      box.attr("checked", "checked") |> ignore

                                  box
                                  span() { t.ConnectorSharedLabel }
                              }

                              p(class' = "um-hint") { t.ConnectorSharedHint }
                          }

                          fieldset(class' = "um-choice-group").data("instance-scope", "") {
                              div(class' = "grid gap-3 md:grid-cols-2") {
                                  scopeRadio formScope "any" t.FediverseScopeAnyLabel t.BlueskyScopeAnyHint
                                  scopeRadio formScope "listed" t.FediverseScopeListedLabel t.BlueskyScopeListedHint
                              }
                          }

                          Components.fieldWith
                              "text"
                              []
                              (fun i -> i.data("instance-list", "") |> ignore)
                              t.BlueskyServersLabel
                              "servers"
                              serversValue
                              (Some t.BlueskyServersHint)
                              serversError

                          if showEmptyNote then
                              (div(class' = "um-alert um-alert-warning").attr("role", "note").data("servers-empty", "") {
                                  Components.icon Theme.Warning
                                  span() { t.BlueskyServersEmptyNote }
                              })
                              :> HtmlElement

                          div(class' = "flex flex-wrap items-center gap-2") {
                              button(type' = "submit", class' = Components.btn Components.Primary) {
                                  Components.icon Theme.CheckCircle
                                  if isConfigured then t.SaveConnector else t.BlueskyEnableButton
                              }
                          }
                      })

                  if isConfigured then
                      (Components.paneActions
                          [ (form(method = "post", action = confirmed "/admin/connectors/bluesky/toggle", class' = "inline") {
                                ctx.GetAntiforgeryInput()

                                button(type' = "submit", class' = Components.btnSm Components.Ghost)
                                    .attr("hx-post", confirmed "/admin/connectors/bluesky/toggle")
                                    .attr("hx-target", "[data-frame]")
                                    .attr("hx-select", "[data-frame]")
                                    .attr("hx-swap", "outerHTML") {
                                    Components.icon Theme.Power
                                    if isEnabled then t.Disable else t.Enable
                                }
                            })
                            |> (if isLastEnabled then Components.confirmSubmit t.ConnectorDisableLastWarning else id)
                            :> HtmlElement

                            (form(method = "post", action = confirmed "/admin/connectors/bluesky/delete", class' = "inline") {
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
                                    t.ConnectorDeleteConfirm ProviderKind.Bluesky.DisplayName
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
