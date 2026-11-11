/// What is Apple's own on the Connectors screen (specs/013-apple-connector).
///
/// The pane of the Apple connector is the pane of every other provider (the
/// setup section, the pills, Enable and Delete), so `Connectors.providerPane`
/// draws it and switches only the fields inside the settings form. This module
/// holds those fields, the form a refused save draws again, and the reading of
/// what was posted: Services ID, Team ID, Key ID and the `.p8` key in place of
/// the client ID and client secret of the others.
///
/// The key is a secret. It is read from the form once, parsed, and never put in
/// a draft, a page, a log or a message: a refusal says only that the key is not
/// usable, not what was pasted.
module Umbral.Handlers.Admin.AppleConnector

open Microsoft.AspNetCore.Http
open Oxpecker.ViewEngine
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Infrastructure.Providers
open Umbral.Views

/// A refused form, to draw again: every typed value except the key, and the
/// message of each field that was refused, by the field's name.
type Draft =
    { DisplayName: string
      ServicesId: string
      TeamId: string
      KeyId: string
      Shared: bool
      /// The allowed domains as typed.
      Domains: string
      Problems: Map<string, string> }

/// What a form with nothing wrong in it says.
type Parsed =
    { DisplayName: string
      ServicesId: string
      Ids: AppleKeyIds
      Shared: bool
      Domains: string list
      /// The normalised PEM of a key that was pasted; None keeps the stored one.
      Key: string option }

/// The fields a refusal can name, which are also the names of the form's controls.
[<Literal>]
let DisplayNameField = "displayName"

[<Literal>]
let ServicesIdField = "servicesId"

[<Literal>]
let TeamIdField = "teamId"

[<Literal>]
let KeyIdField = "keyId"

[<Literal>]
let PrivateKeyField = "privateKey"

[<Literal>]
let DomainsField = "allowedDomains"

/// Read the form, collecting every problem rather than stopping at the first:
/// an operator copying four values from Apple's site gets them all corrected in
/// one round. `hasKey` says whether a key is already stored, in which case an
/// empty box keeps it. `parseDomains` is the screen's own reading of the allowed
/// domains, so the message is the one the other panes give.
let parse
    (t: Texts.Texts)
    (parseDomains: string -> Result<string list, string>)
    (hasKey: bool)
    (form: IFormCollection)
    : Result<Parsed, Draft> =
    let typed name = WebForms.value form name
    let displayName = typed DisplayNameField
    let servicesId = typed ServicesIdField
    let teamId = typed TeamIdField
    let keyId = typed KeyIdField
    let domains = typed DomainsField
    let pasted = WebForms.optional form PrivateKeyField

    let servicesParsed = parseServicesId servicesId
    let teamParsed = parseAppleId teamId
    let keyIdParsed = parseAppleId keyId
    let domainsParsed = parseDomains domains

    // None when no key was pasted and one is stored (keep it); Error when the
    // box is empty on a connector with no key, or its content is not a key.
    let keyResult: Result<string option, string> =
        match pasted with
        | None -> if hasKey then Ok None else Error t.ApplePrivateKeyMissing
        | Some text ->
            match Apple.normaliseKey text with
            | Some pem -> Ok(Some pem)
            | None -> Error t.ApplePrivateKeyUnusable

    let problems =
        [ if displayName = "" then
              DisplayNameField, t.AppleDisplayNameRequired
          if servicesParsed.IsNone then
              ServicesIdField, t.AppleServicesIdInvalid
          if teamParsed.IsNone then
              TeamIdField, t.AppleTeamIdInvalid
          if keyIdParsed.IsNone then
              KeyIdField, t.AppleKeyIdInvalid
          match keyResult with
          | Error message -> PrivateKeyField, message
          | Ok _ -> ()
          match domainsParsed with
          | Error message -> DomainsField, message
          | Ok _ -> () ]

    match servicesParsed, teamParsed, keyIdParsed, keyResult, domainsParsed with
    | Some servicesId', Some teamId', Some keyId', Ok key, Ok list when problems.IsEmpty ->
        Ok
            { DisplayName = displayName
              ServicesId = servicesId'
              Ids = { TeamId = teamId'; KeyId = keyId' }
              Shared = WebForms.checkbox form "shared"
              Domains = list
              Key = key }
    | _ ->
        Error
            { DisplayName = displayName
              ServicesId = servicesId
              TeamId = teamId
              KeyId = keyId
              Shared = WebForms.checkbox form "shared"
              Domains = domains
              Problems = Map.ofList problems }

/// The same draft with one more field refused: the key that Apple's check found
/// unusable, after the form itself had passed.
let withKeyUnusable (t: Texts.Texts) (draft: Draft) =
    { draft with Problems = draft.Problems.Add(PrivateKeyField, t.ApplePrivateKeyUnusable) }

/// A draft that carries the typed values of a form that passed, for the one
/// refusal that comes after the form is read.
let draftOf (parsed: Parsed) (typedDomains: string) : Draft =
    { DisplayName = parsed.DisplayName
      ServicesId = parsed.ServicesId
      TeamId = parsed.Ids.TeamId
      KeyId = parsed.Ids.KeyId
      Shared = parsed.Shared
      Domains = typedDomains
      Problems = Map.empty }

/// The fields of the settings form that are Apple's, in the order of the contract:
/// display name, Services ID, Team ID and Key ID side by side, then the key. The
/// shared box and the allowed domains follow, drawn by the pane as for every kind.
///
/// Values come from the draft of a refused save when there is one, else from the
/// stored connector. Whether a key is saved is the pane's to say (a connector
/// exists); the box is always drawn empty, and a saved key is shown as dots alone.
let fields (t: Texts.Texts) (existing: Connector option) (draft: Draft option) : HtmlElement =
    let problem name =
        draft |> Option.bind (fun d -> d.Problems.TryFind name)

    let stored selector =
        existing |> Option.map selector |> Option.defaultValue ""

    let displayName =
        match draft with
        | Some d -> d.DisplayName
        | None -> existing |> Option.map (fun c -> c.DisplayName) |> Option.defaultValue "Apple"

    let servicesId =
        match draft with
        | Some d -> d.ServicesId
        | None -> stored (fun c -> c.ClientId)

    let teamId =
        match draft with
        | Some d -> d.TeamId
        | None -> existing |> Option.bind (fun c -> c.AppleKey) |> Option.map (fun k -> k.TeamId) |> Option.defaultValue ""

    let keyId =
        match draft with
        | Some d -> d.KeyId
        | None -> existing |> Option.bind (fun c -> c.AppleKey) |> Option.map (fun k -> k.KeyId) |> Option.defaultValue ""

    div(class' = "space-y-3") {
        Components.fieldWith
            "text"
            []
            (fun i -> i.attr("required", "required") |> ignore)
            t.AppleDisplayNameLabel
            DisplayNameField
            displayName
            (Some t.AppleDisplayNameHint)
            (problem DisplayNameField)

        Components.fieldWith
            "text"
            []
            (fun i -> i.attr("autocomplete", "off").attr("spellcheck", "false") |> ignore)
            t.AppleServicesIdLabel
            ServicesIdField
            servicesId
            (Some t.AppleServicesIdHint)
            (problem ServicesIdField)

        div(class' = "grid gap-3 sm:grid-cols-2") {
            Components.monoField t.AppleTeamIdLabel TeamIdField teamId (Some t.AppleTeamIdHint) (problem TeamIdField)
            Components.monoField t.AppleKeyIdLabel KeyIdField keyId (Some t.AppleKeyIdHint) (problem KeyIdField)
        }

        match existing with
        | Some _ ->
            // The dots alone: no character of the key is kept anywhere.
            (Components.maskedSecretField
                t.ApplePrivateKeySavedLabel
                (Secrets.masked "")
                (Fragment() :> HtmlElement))
            :> HtmlElement

            Components.textareaField t.AppleReplaceKeyLabel PrivateKeyField 6 (Some t.AppleReplaceKeyHint) (problem PrivateKeyField)
        | None ->
            Components.textareaField t.ApplePrivateKeyLabel PrivateKeyField 6 (Some t.ApplePrivateKeyHint) (problem PrivateKeyField)
    }
