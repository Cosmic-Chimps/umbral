/// Reading, applying and exporting the configuration file.
///
/// Everything here is the half that touches the world: YAML, the environment,
/// the database. The rules about what a valid document looks like live in
/// `Domains.ConfigDocument`.
module Umbral.Infrastructure.ConfigFile

open System
open System.Collections.Generic
open System.IO
open System.Threading.Tasks
open OpenIddict.Abstractions
open type OpenIddict.Abstractions.OpenIddictConstants
open YamlDotNet.Serialization
open YamlDotNet.Serialization.NamingConventions
open Umbral.Domains
open Umbral.Domains.ConfigDocument

// ---------------------------------------------------------------------------
// Parsing
//
// A mutable DTO shaped like the YAML, converted into the immutable document
// immediately. YamlDotNet needs settable properties and a parameterless
// constructor, neither of which belongs in the domain.
//
// NOT `type private`. F# compiles a private type as non-public, and a
// reflection-based serializer cannot construct one — YamlDotNet then returns a
// default instance with every field unset, which parses as "version 0, no
// connectors, no apps" rather than as an error. The same mistake cost an
// afternoon in OAuthRoundTrip; it fails silently both times.
// ---------------------------------------------------------------------------

[<AllowNullLiteral>]
type ConnectorDto() =
    member val Slug: string = "" with get, set
    member val Kind: string = "" with get, set
    member val DisplayName: string = "" with get, set
    member val ClientId: string = "" with get, set
    member val ClientSecret: string = "" with get, set
    member val ExtraScopes: List<string> | null = null with get, set
    member val Enabled: bool = true with get, set

[<AllowNullLiteral>]
type AppDto() =
    member val ClientId: string = "" with get, set
    member val Name: string = "" with get, set
    member val ClientSecret: string = "" with get, set
    member val RedirectUris: List<string> | null = null with get, set
    member val PostLogoutRedirectUris: List<string> | null = null with get, set

[<AllowNullLiteral>]
type DocumentDto() =
    member val Version: int = 0 with get, set
    member val Connectors: List<ConnectorDto> | null = null with get, set
    member val Apps: List<AppDto> | null = null with get, set

let private listOf (items: List<'a> | null) =
    match Option.ofObj items with
    | Some xs -> List.ofSeq xs
    | None -> []

let private optional (value: string | null) =
    match Option.ofObj value with
    | Some v when v.Trim().Length > 0 -> Some(v.Trim())
    | _ -> None

let private deserializer =
    DeserializerBuilder()
        // snake_case in the file, PascalCase on the DTO.
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build()

type ParseError =
    | Malformed of detail: string
    | Invalid of problems: Problem list

    member this.Describe =
        match this with
        | Malformed detail ->
            $"The configuration file is not valid YAML: {detail}. Nothing has been changed."
        | Invalid problems -> describe problems

let parse (yaml: string) : Result<Document, ParseError> =
    // Matching on null narrows for the nullness checker where Option.ofObj
    // does not: AllowNullLiteral makes the type admit null, and the option's
    // payload is then still seen as nullable.
    let dto =
        try
            match deserializer.Deserialize<DocumentDto> yaml with
            | null -> Error(Malformed "it is empty")
            | dto -> Ok dto
        with ex ->
            Error(Malformed ex.Message)

    match dto with
    | Error e -> Error e
    | Ok dto ->

    let document =
        { Version = dto.Version
          Connectors =
            listOf dto.Connectors
            |> List.map (fun c ->
                { Slug = c.Slug
                  Kind = c.Kind
                  DisplayName = optional c.DisplayName
                  ClientId = c.ClientId
                  ClientSecret = c.ClientSecret
                  ExtraScopes = listOf c.ExtraScopes
                  Enabled = c.Enabled })
          Apps =
            listOf dto.Apps
            |> List.map (fun a ->
                { ClientId = a.ClientId
                  Name = a.Name
                  ClientSecret = optional a.ClientSecret
                  RedirectUris = listOf a.RedirectUris
                  PostLogoutRedirectUris = listOf a.PostLogoutRedirectUris }) }

    // Whole-document, before anything is written. A file that half-applies
    // leaves an instance in a state the operator did not ask for and cannot
    // read off the file in front of them (FR-030).
    match validate document with
    | [] -> Ok document
    | problems -> Error(Invalid problems)

// ---------------------------------------------------------------------------
// Resolving secrets
// ---------------------------------------------------------------------------

type ResolveError =
    { Where: string
      Variable: string
      /// Finishes "references VAR, which ..." — a missing variable and a
      /// too-short one are both reported by name, so both are one type.
      Reason: string }

/// The shortest app secret the file may set. The Apps screen generates 256
/// bits; a file lets a person choose, and a client secret is the one thing
/// standing between the internet and a token exchange, so a four-letter word
/// is refused rather than hashed.
[<Literal>]
let MinimumAppSecretLength = 16

/// Replace every `${VAR}` with its value, or report every one that is missing.
///
/// Reported together rather than one per restart: an operator who forgot three
/// variables should learn that once.
let resolveSecrets (read: string -> string | null) (document: Document) =
    let missing = ResizeArray<ResolveError>()

    let resolve minimumLength where (reference: string) =
        match referencedVariable reference with
        | None -> reference
        | Some name ->
            match Option.ofObj (read name) with
            | Some value when value.Length >= max 1 minimumLength -> value
            | Some value when value.Length > 0 ->
                missing.Add
                    { Where = where
                      Variable = name
                      Reason = $"is shorter than the {minimumLength} characters an app secret needs" }

                ""
            | _ ->
                missing.Add { Where = where; Variable = name; Reason = "is not set" }
                ""

    let resolved =
        { document with
            Connectors =
                document.Connectors
                |> List.mapi (fun i c ->
                    { c with ClientSecret = resolve 1 $"connectors[{i}].client_secret" c.ClientSecret })
            Apps =
                document.Apps
                |> List.mapi (fun i a ->
                    { a with
                        ClientSecret =
                            a.ClientSecret
                            |> Option.map (resolve MinimumAppSecretLength $"apps[{i}].client_secret") }) }

    if missing.Count = 0 then Ok resolved else Error(List.ofSeq missing)

let describeMissing (missing: ResolveError list) =
    let body =
        missing
        |> List.map (fun m -> $"  {m.Where} references {m.Variable}, which {m.Reason}.")
        |> String.concat Environment.NewLine

    $"The configuration file cannot be applied:{Environment.NewLine}{body}{Environment.NewLine}\
      Fix those environment variables, or remove the entries that need them. Nothing has been changed."

// ---------------------------------------------------------------------------
// Applying
// ---------------------------------------------------------------------------

/// Apply a resolved document. Idempotent: `slug` and `client_id` are the
/// identity, so applying the same file twice changes nothing and creates no
/// duplicates (FR-027).
///
/// Connectors only. The apps are `applyApps`, which needs OpenIddict's manager
/// and so takes it from the caller.
let applyConnectors
    (config: Database.DbConfig)
    (clock: TimeProvider)
    (secrets: Secrets.ISecretStore)
    (document: Document)
    : Task<int> =
    task {
        let mutable applied = 0

        for entry in document.Connectors do
            match Connectors.parseKind entry.Kind with
            | None -> ()
            | Some kind ->
                let! existing = ConnectorStore.findBySlug config entry.Slug
                let! reference = secrets.Put($"connector/{entry.Slug}/client_secret", entry.ClientSecret)

                let connector: Connectors.Connector =
                    { Slug = entry.Slug
                      Kind = kind
                      DisplayName = entry.DisplayName |> Option.defaultValue kind.DisplayName
                      ClientId = entry.ClientId
                      ClientSecretRef = reference.Encoded
                      ClientSecretHint = Secrets.hint entry.ClientSecret
                      ExtraScopes = entry.ExtraScopes
                      IsEnabled = entry.Enabled
                      CreatedUtc = existing |> Option.map (fun c -> c.CreatedUtc) |> Option.defaultValue (clock.GetUtcNow())
                      UpdatedUtc = clock.GetUtcNow() }

                do! ConnectorStore.save config clock connector (Some(reference.Encoded, Secrets.hint entry.ClientSecret))
                applied <- applied + 1

                // FR-035: a connector the file creates or changes is a change an
                // operator may need to trace, actor System because nobody was
                // signed in to make it. Only a REAL change is recorded — a
                // restart re-applies the same file and would otherwise write a
                // "connector updated" row per connector every time the process
                // starts, burying the one that matters.
                match existing with
                | None ->
                    do!
                        SecurityEvents.record
                            config
                            clock
                            { SecurityEvents.event
                                  SecurityEvents.ConnectorCreated
                                  SecurityEvents.Success
                                  SecurityEvents.Actor.System with
                                ConnectorSlug = Some entry.Slug
                                Detail = Some "source=config_file" }

                | Some before ->
                    // Compared by decrypting the stored secret: the stored
                    // reference differs on every write even for an identical
                    // secret, and the hint is only four characters.
                    let! secretChanged =
                        task {
                            match Secrets.SecretRef.Decode before.ClientSecretRef with
                            | Ok previous ->
                                let! plaintext = secrets.Get previous
                                return plaintext <> Some entry.ClientSecret
                            | Error _ -> return true
                        }

                    let changed =
                        [ if before.ClientId <> connector.ClientId then "client_id"
                          if before.DisplayName <> connector.DisplayName then "display_name"
                          if before.ExtraScopes <> connector.ExtraScopes then "extra_scopes"
                          if before.IsEnabled <> connector.IsEnabled then "enabled"
                          if secretChanged then "secret" ]

                    if not changed.IsEmpty then
                        do!
                            SecurityEvents.record
                                config
                                clock
                                { SecurityEvents.event
                                      SecurityEvents.ConnectorUpdated
                                      SecurityEvents.Success
                                      SecurityEvents.Actor.System with
                                    ConnectorSlug = Some entry.Slug
                                    Detail = Some("source=config_file;changed=" + String.concat "," changed) }

        return applied
    }

/// Apply the `apps:` list. Idempotent on `client_id`, like the connectors.
///
/// What the file says about a secret. An app's secret is hashed by OpenIddict
/// on write, so the file cannot be where it is READ from — but it can be where
/// it is SET: `client_secret: ${UMBRAL_GRAFANA_SECRET}` is a reference to a
/// value the operator already holds (it is what they paste into Grafana), and
/// applying the file makes Umbral store the hash of that value. The alternative
/// — minting a fresh secret on import — would show it nowhere at all, because
/// nobody is at a screen when a container starts. An entry with a reference
/// is a confidential app; one without is a public client (a single-page or
/// native app) and is never given a secret.
///
/// Re-applying compares rather than rewrites: the stored hash is checked
/// against the value the variable holds NOW, so an unchanged file changes
/// nothing and a changed variable takes effect on the next start.
///
/// Apps in the database that the file does not list are left alone, as
/// connectors are: the file describes what must exist, not everything that may.
let applyApps
    (config: Database.DbConfig)
    (clock: TimeProvider)
    (applications: IOpenIddictApplicationManager)
    (document: Document)
    : Task<int> =
    task {
        let mutable applied = 0

        // The same rule the Apps screen uses. `validate` has already run, so a
        // refusal here means this was called on a document that skipped it.
        let uris where (values: string list) =
            values
            |> List.map (fun value ->
                match acceptableRedirectUri value with
                | Ok uri -> uri
                | Error why -> failwith $"{where} {why}")

        let record eventType (entry: AppEntry) (changed: string list) =
            SecurityEvents.record
                config
                clock
                { SecurityEvents.event eventType SecurityEvents.Success SecurityEvents.Actor.System with
                    ClientId = Some entry.ClientId
                    Detail = Some("source=config_file" + (if changed.IsEmpty then "" else ";changed=" + String.concat "," changed)) }

        for index, entry in List.indexed document.Apps do
            let desired =
                AppClients.buildDescriptor
                    entry.ClientId
                    entry.Name
                    entry.ClientSecret.IsSome
                    (uris $"apps[{index}].redirect_uris" entry.RedirectUris)
                    (uris $"apps[{index}].post_logout_redirect_uris" entry.PostLogoutRedirectUris)
                    entry.ClientSecret

            match! applications.FindByClientIdAsync entry.ClientId with
            | null ->
                let! _ = applications.CreateAsync desired
                applied <- applied + 1
                do! record SecurityEvents.AppCreated entry []

            | existing ->
                let current = OpenIddictApplicationDescriptor()
                do! applications.PopulateAsync(current, existing)

                let uriSet (set: ISet<Uri>) = set |> Seq.map _.OriginalString |> Set.ofSeq

                let changed =
                    [ if current.DisplayName <> desired.DisplayName then "name"
                      if current.ClientType <> desired.ClientType then "client_type"
                      if uriSet current.RedirectUris <> uriSet desired.RedirectUris then "redirect_uris"
                      if uriSet current.PostLogoutRedirectUris <> uriSet desired.PostLogoutRedirectUris then
                          "post_logout_redirect_uris"
                      if Set.ofSeq current.Permissions <> Set.ofSeq desired.Permissions
                         || Set.ofSeq current.Requirements <> Set.ofSeq desired.Requirements then
                          "permissions" ]

                // Verified against the hash rather than compared: the stored
                // value is a salted PBKDF2 hash, so there is nothing to
                // compare, and verifying is the one thing it can do.
                let! secretChanged =
                    task {
                        match entry.ClientSecret with
                        | None -> return false
                        | Some secret when current.ClientType <> ClientTypes.Confidential -> return true
                        | Some secret ->
                            let! matches = applications.ValidateClientSecretAsync(existing, secret)
                            return not matches
                    }

                if not changed.IsEmpty || secretChanged then
                    // An unchanged secret is handed back as the stored hash:
                    // the manager hashes a descriptor's secret only when it
                    // differs from what is stored, so this leaves it alone
                    // instead of hashing a hash.
                    if not secretChanged then
                        desired.ClientSecret <- current.ClientSecret

                    do! applications.UpdateAsync(existing, desired)
                    applied <- applied + 1

                    if not changed.IsEmpty then
                        do! record SecurityEvents.AppUpdated entry changed

                    // Its own event: "who rotated the secret" is a question an
                    // operator asks of the log by itself, and the value never
                    // appears in it.
                    if secretChanged then
                        do! record SecurityEvents.AppSecretRotated entry []

        return applied
    }

// ---------------------------------------------------------------------------
// Exporting
// ---------------------------------------------------------------------------

/// `UMBRAL_<NAME>_SECRET`, for a connector slug or an app's client id. A client
/// id may hold characters an environment variable cannot ('.', '-'), so
/// anything outside letters and digits becomes an underscore.
let private variableFor (name: string) =
    "UMBRAL_" + Text.RegularExpressions.Regex.Replace(name.ToUpperInvariant(), "[^A-Z0-9]", "_") + "_SECRET"

/// An app as the export sees it. `Confidential` is what decides whether the
/// entry carries a `client_secret` reference: a public client never has one.
type ExportedApp =
    { ClientId: string
      Name: string
      Confidential: bool
      RedirectUris: string list
      PostLogoutRedirectUris: string list }

/// Emit the same format `parse` accepts, so export and import round-trip
/// (FR-029).
///
/// One asymmetry, and it is forced rather than chosen: an app's secret is
/// PBKDF2-hashed by OpenIddict on write, so there is nothing to export. What
/// the file carries instead is a reference to where the operator keeps it, and
/// the file says so where someone will read it rather than discover it.
let export (connectors: Connectors.Connector list) (apps: ExportedApp list) =
    let lines = ResizeArray<string>()

    lines.Add "# Umbral configuration."
    lines.Add "#"
    lines.Add "# Secrets are references to environment variables, never literals: this file"
    lines.Add "# is meant to live in version control."
    lines.Add "#"
    lines.Add "# App secrets are NOT exported. OpenIddict hashes them on write, so there is"
    lines.Add "# nothing to read back. Each web app below references a variable: set it to"
    lines.Add "# the secret that app already uses (at least 16 characters) and Umbral will"
    lines.Add "# store its hash when this file is applied. An app without a client_secret is"
    lines.Add "# a public client (single-page or native) and has none."
    lines.Add ""
    lines.Add $"version: {SupportedVersion}"
    lines.Add ""

    if connectors.IsEmpty then
        lines.Add "connectors: []"
    else
        lines.Add "connectors:"

        let quote (value: string) = "\"" + value + "\""

        for c in connectors do
            let enabled = if c.IsEnabled then "true" else "false"
            lines.Add $"  - slug: {c.Slug}"
            lines.Add $"    kind: {c.Kind.Value}"
            lines.Add $"    display_name: {c.DisplayName}"
            lines.Add("    client_id: " + quote c.ClientId)
            lines.Add $"    client_secret: ${{{variableFor c.Slug}}}"
            lines.Add $"    enabled: {enabled}"

            if not c.ExtraScopes.IsEmpty then
                let scopes = String.concat ", " c.ExtraScopes
                lines.Add $"    extra_scopes: [{scopes}]"

    lines.Add ""

    if apps.IsEmpty then
        lines.Add "apps: []"
    else
        lines.Add "apps:"

        for app in apps do
            lines.Add("  - client_id: \"" + app.ClientId + "\"")
            lines.Add $"    name: {app.Name}"

            if app.Confidential then
                lines.Add $"    client_secret: ${{{variableFor app.ClientId}}}"

            lines.Add "    redirect_uris:"

            for uri in app.RedirectUris do
                lines.Add $"      - {uri}"

            if not app.PostLogoutRedirectUris.IsEmpty then
                lines.Add "    post_logout_redirect_uris:"

                for uri in app.PostLogoutRedirectUris do
                    lines.Add $"      - {uri}"

    String.concat Environment.NewLine lines + Environment.NewLine

/// Where the file lives, when the operator supplies one.
let locate (configured: string | null) (dataDirectory: DataDirectory.DataDirectory) =
    match Option.ofObj configured with
    | Some path when path.Trim().Length > 0 -> path.Trim()
    | _ -> dataDirectory.ConfigFile

let tryRead (path: string) =
    if File.Exists path then Some(File.ReadAllText path) else None
