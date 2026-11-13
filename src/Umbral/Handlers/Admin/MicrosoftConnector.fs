/// What is Microsoft's own on the Connectors screen (specs/016-microsoft-connector,
/// contracts/routes-and-screens.md).
///
/// The pane of the Microsoft connector is the pane of every other provider (the
/// setup section, the pills, Enable and Delete), so `Connectors.providerPane`
/// draws it and switches only the fields inside the settings form, as it does for
/// Apple. This module holds those fields, the form a refused save draws again, and
/// the reading of what was posted: the application (client) ID, the client secret,
/// who may sign in (four cards, none preselected) and the tenant ID that goes with
/// one of them.
///
/// The secret is read from the form once and never put in a draft, a page, a log
/// or a message.
module Umbral.Handlers.Admin.MicrosoftConnector

open Microsoft.AspNetCore.Http
open Oxpecker.ViewEngine
open Umbral.Domains
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Views

/// A refused form, to draw again: every typed value except the secret, and the
/// message of each field that was refused, by the field's name.
type Draft =
    { DisplayName: string
      ClientId: string
      /// The card that was chosen as posted, None when none was (or the value was
      /// none of the four).
      Audience: string option
      /// The tenant ID as typed, kept when another card is chosen.
      TenantId: string
      Shared: bool
      /// The allowed domains as typed.
      Domains: string
      Problems: Map<string, string> }

/// What a form with nothing wrong in it says.
type Parsed =
    { DisplayName: string
      /// The client ID in its canonical spelling (lower case).
      ClientId: string
      Audience: MicrosoftAudience
      Shared: bool
      Domains: string list
      /// The secret that was typed; None keeps the stored one.
      Secret: string option }

/// The fields a refusal can name, which are also the names of the form's controls.
[<Literal>]
let DisplayNameField = "displayName"

[<Literal>]
let ClientIdField = "clientId"

[<Literal>]
let ClientSecretField = "clientSecret"

[<Literal>]
let AudienceField = "audience"

[<Literal>]
let TenantIdField = "tenantId"

[<Literal>]
let DomainsField = "allowedDomains"

/// The four values of the cards, in the contract's order.
let private cards (t: Texts.Texts) =
    [ "consumers", t.MicrosoftConsumersTitle, t.MicrosoftConsumersDesc
      "tenant", t.MicrosoftTenantTitle, t.MicrosoftTenantDesc
      "organizations", t.MicrosoftOrganizationsTitle, t.MicrosoftOrganizationsDesc
      "common", t.MicrosoftCommonTitle, t.MicrosoftCommonDesc ]

let private knownValues = set [ "consumers"; "tenant"; "organizations"; "common" ]

/// The form value of a stored audience: the card it is drawn as.
let private cardOf (audience: MicrosoftAudience) =
    match audience with
    | PersonalAccounts -> "consumers"
    | OneOrganization _ -> "tenant"
    | AnyOrganization -> "organizations"
    | PersonalAndAnyOrganization -> "common"

/// Read the form, collecting every problem rather than stopping at the first.
/// `hasSecret` says whether a secret is already stored, in which case an empty box
/// keeps it. `parseDomains` is the screen's own reading of the allowed domains, so
/// the message is the one the other panes give.
let parse
    (t: Texts.Texts)
    (parseDomains: string -> Result<string list, string>)
    (hasSecret: bool)
    (form: IFormCollection)
    : Result<Parsed, Draft> =
    let typed name = WebForms.value form name
    let displayName = typed DisplayNameField
    let clientId = typed ClientIdField
    let tenantId = typed TenantIdField
    let domains = typed DomainsField
    let secret = WebForms.optional form ClientSecretField

    let chosen =
        WebForms.optional form AudienceField |> Option.filter knownValues.Contains

    let clientParsed = Microsoft.parseClientId clientId
    let domainsParsed = parseDomains domains

    // Ok None only for a card that needs no tenant; Error carries the refusal.
    let tenantResult: Result<System.Guid option, string> =
        match chosen with
        | Some "tenant" ->
            if tenantId = "" then
                Error t.MicrosoftTenantIdMissing
            else
                match Microsoft.parseTenantId tenantId with
                | None -> Error t.MicrosoftTenantIdInvalid
                | Some g when g = Microsoft.personalTenant -> Error t.MicrosoftTenantIdPersonal
                | Some g -> Ok(Some g)
        | _ -> Ok None

    let secretResult =
        match secret with
        | None when not hasSecret -> Error t.MicrosoftSecretMissing
        | _ -> Ok secret

    let problems =
        [ if displayName = "" then
              DisplayNameField, t.Notice "connector-display-name-required"
          if clientParsed.IsNone then
              ClientIdField, t.MicrosoftClientIdInvalid
          match secretResult with
          | Error message -> ClientSecretField, message
          | Ok _ -> ()
          if chosen.IsNone then
              AudienceField, t.MicrosoftAudienceMissing
          match tenantResult with
          | Error message -> TenantIdField, message
          | Ok _ -> ()
          match domainsParsed with
          | Error message -> DomainsField, message
          | Ok _ -> () ]

    let audience =
        match chosen, tenantResult with
        | Some "consumers", _ -> Some PersonalAccounts
        | Some "organizations", _ -> Some AnyOrganization
        | Some "common", _ -> Some PersonalAndAnyOrganization
        | Some "tenant", Ok(Some g) -> Some(OneOrganization g)
        | _ -> None

    match clientParsed, audience, secretResult, domainsParsed with
    | Some client, Some audience, Ok secret, Ok list when problems.IsEmpty ->
        Ok
            { DisplayName = displayName
              ClientId = client.ToString "D"
              Audience = audience
              Shared = WebForms.checkbox form "shared"
              Domains = list
              Secret = secret }
    | _ ->
        Error
            { DisplayName = displayName
              ClientId = clientId
              Audience = chosen
              TenantId = tenantId
              Shared = WebForms.checkbox form "shared"
              Domains = domains
              Problems = Map.ofList problems }

/// The form of a well-formed save whose choice Microsoft did not confirm, drawn
/// again: every typed value but the secret, and `message` under the tenant ID when
/// "one organization" is chosen, else under the "Who may sign in" group.
/// `tenantId` and `domains` are as typed.
let refusedChoice (parsed: Parsed) (tenantId: string) (domains: string) (message: string) : Draft =
    let field =
        match parsed.Audience with
        | OneOrganization _ -> TenantIdField
        | _ -> AudienceField

    { DisplayName = parsed.DisplayName
      ClientId = parsed.ClientId
      Audience = Some(cardOf parsed.Audience)
      TenantId = tenantId
      Shared = parsed.Shared
      Domains = domains
      Problems = Map.ofList [ field, message ] }

/// One card, drawn like the Passkey pane's enrolment modes (`.um-choice`). When
/// the group was refused every radio is marked invalid and points at the message.
let private card (selected: string option) (refused: bool) (value: string) (title: string) (description: string) =
    label(class' = "um-choice") {
        let radio =
            input(type' = "radio", name = AudienceField, value = value, class' = "um-choice-radio")

        if selected = Some value then
            radio.attr("checked", "checked") |> ignore

        if refused then
            radio.attr("aria-invalid", "true").attr("aria-describedby", "audience-error") |> ignore

        radio

        span(class' = "um-choice-body") {
            span(class' = "um-choice-title") { title }
            span(class' = "um-choice-desc") { description }
        }
    }

/// The fields of the settings form that are Microsoft's, in the order of the
/// contract: display name, application (client) ID, client secret, who may sign in
/// (four stacked cards), the tenant ID. The shared box and the allowed domains
/// follow, drawn by the pane as for every kind.
///
/// Values come from the draft of a refused save when there is one, else from the
/// stored connector. The secret box is always drawn empty; a saved secret is shown
/// by its masked hint alone.
let fields (t: Texts.Texts) (existing: Connector option) (draft: Draft option) : HtmlElement =
    let problem name =
        draft |> Option.bind (fun d -> d.Problems.TryFind name)

    let displayName =
        match draft with
        | Some d -> d.DisplayName
        | None -> existing |> Option.map (fun c -> c.DisplayName) |> Option.defaultValue "Microsoft"

    let clientId =
        match draft with
        | Some d -> d.ClientId
        | None -> existing |> Option.map (fun c -> c.ClientId) |> Option.defaultValue ""

    // None preselected on a new connector; the stored card otherwise.
    let selected =
        match draft with
        | Some d -> d.Audience
        | None -> existing |> Option.bind (fun c -> c.MicrosoftAudience) |> Option.map cardOf

    let tenantId =
        match draft with
        | Some d -> d.TenantId
        | None ->
            match existing |> Option.bind (fun c -> c.MicrosoftAudience) with
            | Some(OneOrganization g) -> g.ToString "D"
            | _ -> ""

    let audienceProblem = problem AudienceField

    div(class' = "space-y-3") {
        Components.fieldWith
            "text"
            []
            (fun i -> i.attr("required", "required") |> ignore)
            t.MicrosoftDisplayNameLabel
            DisplayNameField
            displayName
            (Some t.MicrosoftDisplayNameHint)
            (problem DisplayNameField)

        Components.monoField
            t.MicrosoftClientIdLabel
            ClientIdField
            clientId
            (Some t.MicrosoftClientIdHint)
            (problem ClientIdField)

        match existing with
        | Some c ->
            (Components.maskedSecretField
                t.ClientSecretLabel
                (Secrets.masked c.ClientSecretHint)
                (Fragment() :> HtmlElement))
            :> HtmlElement

            Components.passwordField
                t.MicrosoftReplaceSecretLabel
                ClientSecretField
                (Some t.MicrosoftReplaceSecretHint)
                (problem ClientSecretField)
        | None ->
            Components.passwordField
                t.ClientSecretLabel
                ClientSecretField
                (Some t.MicrosoftClientSecretHint)
                (problem ClientSecretField)

        fieldset(class' = "um-choice-group").data("microsoft-audience", "") {
            legend(class' = "um-label") { t.MicrosoftAudienceLabel }

            // One column at every width: the grid has no columns to put cards in a row.
            div(class' = "grid gap-3") {
                for value, title, description in cards t do
                    card selected audienceProblem.IsSome value title description
            }

            match audienceProblem with
            | Some message ->
                (p(id = "audience-error", class' = "um-field-error") {
                    Components.icon Theme.ErrorMark
                    span() { message }
                })
                :> HtmlElement
            | None -> Fragment() :> HtmlElement
        }

        Components.monoField
            t.MicrosoftTenantIdLabel
            TenantIdField
            tenantId
            (Some t.MicrosoftTenantIdHint)
            (problem TenantIdField)
    }
