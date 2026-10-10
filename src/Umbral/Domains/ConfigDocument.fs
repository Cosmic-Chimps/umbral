/// The declarative description of an instance: every App and every Connector.
///
/// Pure — parsing a string into this shape, and judging whether the shape is
/// usable. Reading files, resolving environment variables and writing to the
/// database all live in `Infrastructure.ConfigFile`, so the rules can be read
/// and tested without any of that in the way.
module Umbral.Domains.ConfigDocument

open System

[<Literal>]
let SupportedVersion = 1

type ConnectorEntry =
    { Slug: string
      Kind: string
      DisplayName: string option
      ClientId: string
      /// As written in the file: always a `${VAR}` reference, never a literal.
      ClientSecret: string
      ExtraScopes: string list
      Enabled: bool }

type AppEntry =
    { ClientId: string
      Name: string
      ClientSecret: string option
      RedirectUris: string list
      PostLogoutRedirectUris: string list }

type Document =
    { Version: int
      Connectors: ConnectorEntry list
      Apps: AppEntry list }

// ---------------------------------------------------------------------------
// Validation
//
// Whole-document, and every message names the offending entry by its position
// and its identity (FR-030). An operator editing YAML at 1am needs to be told
// which of forty entries is wrong, not that "the configuration is invalid".
// ---------------------------------------------------------------------------

type Problem =
    { /// e.g. "connectors[2].slug" — a path into the document, so it can be
      /// found by eye in the file.
      Where: string
      What: string }

let private problem where what = { Where = where; What = what }

/// `${VAR}` and nothing else.
///
/// This is the rule that keeps a secret out of the operator's git repository,
/// and it is an ERROR rather than a warning on purpose: the audience keeps
/// their homelab in version control, and a warning is a thing you scroll past.
let private secretReference = Text.RegularExpressions.Regex(@"^\$\{([A-Za-z_][A-Za-z0-9_]*)\}$")

let referencedVariable (value: string) =
    let m = secretReference.Match value
    if m.Success then Some m.Groups[1].Value else None

let private validateSecret where (value: string) =
    if String.IsNullOrWhiteSpace value then
        [ problem where "is empty. Give a reference such as ${UMBRAL_DISCORD_SECRET}." ]
    elif (referencedVariable value).IsNone then
        [ problem
              where
              "is a literal value. Secrets must be written as a reference such as ${UMBRAL_DISCORD_SECRET}, \
               so that this file can be kept in version control." ]
    else
        []

/// Whether a string may be used as a redirect URI, and why not if not.
///
/// Exported because the admin form needs exactly the same answer as the
/// configuration file. The rule used to be written twice, and the second copy
/// was missing the file-scheme case below — which is how a rule written twice
/// always ends.
let acceptableRedirectUri (value: string) : Result<Uri, string> =
    // The wildcard check comes FIRST. A URL with a `*` in the host does not
    // parse as a Uri at all, so leaving it to the parser produces "is not an
    // absolute URL" — true, but it names the wrong cause for an operator who
    // deliberately wrote a wildcard and needs to be told Umbral matches exactly.
    if String.IsNullOrWhiteSpace value then
        Error "is empty."
    elif value.Contains '*' then
        Error $"\"{value}\" contains a wildcard. Redirect URIs are matched exactly."
    else
        match Uri.TryCreate(value, UriKind.Absolute) with
        | false, _ -> Error $"\"{value}\" is not an absolute URL."
        | true, uri ->
            match Option.ofObj uri with
            | None -> Error $"\"{value}\" is not an absolute URL."

            // On Unix a leading slash parses as an absolute FILE uri, so
            // "/callback" would otherwise pass as an absolute URL and be stored
            // as a redirect target no provider could ever send anyone to.
            | Some uri when uri.Scheme = Uri.UriSchemeFile ->
                Error
                    $"\"{value}\" is not an absolute URL. Include the scheme and host, \
                      for example https://app.example.com/callback."

            | Some uri when
                (uri.Scheme = Uri.UriSchemeHttp || uri.Scheme = Uri.UriSchemeHttps)
                && String.IsNullOrEmpty uri.Host
                ->
                Error $"\"{value}\" has no host."

            | Some uri when uri.Fragment <> "" ->
                Error $"\"{value}\" contains a fragment, which a provider will never send back."

            | Some uri -> Ok uri

let private validateUri where (value: string) =
    match acceptableRedirectUri value with
    | Ok _ -> []
    | Error what -> [ problem where what ]

let private validateConnector (index: int) (entry: ConnectorEntry) =
    let at field = $"connectors[{index}].{field}"

    [ if not (Ids.isValidSlug entry.Slug) then
          problem
              (at "slug")
              $"\"{entry.Slug}\" is not a valid slug. Use lower-case letters, digits and hyphens, up to 32 \
                characters. It appears in the callback URL registered with the provider."

      if (Connectors.parseKind entry.Kind).IsNone then
          problem
              (at "kind")
              $"\"{entry.Kind}\" is not a provider Umbral supports. Use discord, github or google."

      if String.IsNullOrWhiteSpace entry.ClientId then
          problem (at "client_id") "is empty. Copy it from the provider's developer console."

      yield! validateSecret (at "client_secret") entry.ClientSecret ]

let private validateApp (index: int) (entry: AppEntry) =
    let at field = $"apps[{index}].{field}"

    [ if String.IsNullOrWhiteSpace entry.ClientId then
          problem (at "client_id") "is empty."

      if String.IsNullOrWhiteSpace entry.Name then
          problem (at "name") "is empty. Give the app a name so it can be told apart in the admin area."

      if entry.RedirectUris.IsEmpty then
          problem
              (at "redirect_uris")
              "is empty. An app needs at least one redirect URI, matched exactly when it signs someone in."

      for i, uri in List.indexed entry.RedirectUris do
          yield! validateUri (at $"redirect_uris[{i}]") uri

      for i, uri in List.indexed entry.PostLogoutRedirectUris do
          yield! validateUri (at $"post_logout_redirect_uris[{i}]") uri

      match entry.ClientSecret with
      | Some secret -> yield! validateSecret (at "client_secret") secret
      | None -> () ]

let private duplicates (selector: 'a -> string) (items: 'a list) =
    items
    |> List.map selector
    |> List.countBy id
    |> List.filter (fun (_, count) -> count > 1)
    |> List.map fst

/// Every problem in the document, not just the first. An operator fixing a file
/// by hand should get the whole list, not discover the next one on each restart.
let validate (document: Document) : Problem list =
    [ if document.Version <> SupportedVersion then
          problem
              "version"
              $"is {document.Version}. This build understands version {SupportedVersion}."

      for slug in duplicates (fun (c: ConnectorEntry) -> c.Slug) document.Connectors do
          problem "connectors" $"\"{slug}\" appears more than once. A slug identifies a connector."

      for clientId in duplicates (fun (a: AppEntry) -> a.ClientId) document.Apps do
          problem "apps" $"\"{clientId}\" appears more than once. A client id identifies an app."

      for i, connector in List.indexed document.Connectors do
          yield! validateConnector i connector

      for i, app in List.indexed document.Apps do
          yield! validateApp i app ]

/// One message an operator can act on, listing every problem found.
let describe (problems: Problem list) =
    problems
    |> List.map (fun p -> $"  {p.Where} {p.What}")
    |> String.concat Environment.NewLine
    |> fun body ->
        $"The configuration file cannot be applied:{Environment.NewLine}{body}{Environment.NewLine}\
          Nothing has been changed."
