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
    { Where: string; Variable: string }

/// Replace every `${VAR}` with its value, or report every one that is missing.
///
/// Reported together rather than one per restart: an operator who forgot three
/// variables should learn that once.
let resolveSecrets (read: string -> string | null) (document: Document) =
    let missing = ResizeArray<ResolveError>()

    let resolve where (reference: string) =
        match referencedVariable reference with
        | None -> reference
        | Some name ->
            match Option.ofObj (read name) with
            | Some value when value.Length > 0 -> value
            | _ ->
                missing.Add { Where = where; Variable = name }
                ""

    let resolved =
        { document with
            Connectors =
                document.Connectors
                |> List.mapi (fun i c ->
                    { c with ClientSecret = resolve $"connectors[{i}].client_secret" c.ClientSecret })
            Apps =
                document.Apps
                |> List.mapi (fun i a ->
                    { a with
                        ClientSecret = a.ClientSecret |> Option.map (resolve $"apps[{i}].client_secret") }) }

    if missing.Count = 0 then Ok resolved else Error(List.ofSeq missing)

let describeMissing (missing: ResolveError list) =
    let body =
        missing
        |> List.map (fun m -> $"  {m.Where} references {m.Variable}, which is not set.")
        |> String.concat Environment.NewLine

    $"The configuration file cannot be applied:{Environment.NewLine}{body}{Environment.NewLine}\
      Set those environment variables, or remove the entries that need them. Nothing has been changed."

// ---------------------------------------------------------------------------
// Applying
// ---------------------------------------------------------------------------

/// Apply a resolved document. Idempotent: `slug` and `client_id` are the
/// identity, so applying the same file twice changes nothing and creates no
/// duplicates (FR-027).
///
/// Connectors only. Apps go through OpenIddict's manager, which the caller
/// supplies, because this module has no business knowing about it.
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

// ---------------------------------------------------------------------------
// Exporting
// ---------------------------------------------------------------------------

let private variableFor (slug: string) =
    "UMBRAL_" + slug.ToUpperInvariant().Replace('-', '_') + "_SECRET"

/// Emit the same format `parse` accepts, so export and import round-trip
/// (FR-029).
///
/// One asymmetry, and it is forced rather than chosen: an app's secret is
/// PBKDF2-hashed by OpenIddict on write, so there is nothing to export. An
/// imported app is created with a fresh secret, and the file says so where
/// someone will read it rather than discover it.
let export (connectors: Connectors.Connector list) (apps: (string * string * string list * string list) list) =
    let lines = ResizeArray<string>()

    lines.Add "# Umbral configuration."
    lines.Add "#"
    lines.Add "# Secrets are references to environment variables, never literals: this file"
    lines.Add "# is meant to live in version control."
    lines.Add "#"
    lines.Add "# App secrets are NOT exported. OpenIddict hashes them on write, so there is"
    lines.Add "# nothing to read back. An app created from this file gets a fresh secret,"
    lines.Add "# which must then be copied into the app itself."
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

        for clientId, name, redirectUris, postLogoutUris in apps do
            lines.Add("  - client_id: \"" + clientId + "\"")
            lines.Add $"    name: {name}"
            lines.Add "    redirect_uris:"

            for uri in redirectUris do
                lines.Add $"      - {uri}"

            if not postLogoutUris.IsEmpty then
                lines.Add "    post_logout_redirect_uris:"

                for uri in postLogoutUris do
                    lines.Add $"      - {uri}"

    String.concat Environment.NewLine lines + Environment.NewLine

/// Where the file lives, when the operator supplies one.
let locate (configured: string | null) (dataDirectory: DataDirectory.DataDirectory) =
    match Option.ofObj configured with
    | Some path when path.Trim().Length > 0 -> path.Trim()
    | _ -> dataDirectory.ConfigFile

let tryRead (path: string) =
    if File.Exists path then Some(File.ReadAllText path) else None
