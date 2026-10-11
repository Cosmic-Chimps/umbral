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
      /// Only for `kind: oidc` (required there, an error anywhere else): the
      /// address the provider calls its issuer. As written in the file until
      /// `normaliseIssuers` has run, the spelling Umbral stores after it.
      Issuer: string option
      DisplayName: string option
      ClientId: string
      /// As written in the file: always a `${VAR}` reference, never a literal.
      ClientSecret: string
      ExtraScopes: string list
      Enabled: bool
      /// Offered to every app that has not chosen its own set (FR-016). Absent
      /// from the file means true.
      Shared: bool }

type AppEntry =
    { ClientId: string
      Name: string
      ClientSecret: string option
      RedirectUris: string list
      PostLogoutRedirectUris: string list
      /// The connectors this app offers (FR-016). `None` is "all shared
      /// connectors", which is what leaving the key out means; `Some []` is a
      /// chosen set with nothing in it, and is NOT the same thing.
      Connectors: string list option }

/// The operator credential, as a verification value (FR-017a).
type OperatorEntry =
    { /// As written in the file: a `${VAR}` reference to a hash made by
      /// `Umbral hash-credential`. Never the credential, and — because a hash
      /// is still something to brute-force — never a literal either.
      CredentialHash: string }

type Document =
    { Version: int
      Operator: OperatorEntry option
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

let private validateOperator (entry: OperatorEntry) =
    let where = "operator.credential_hash"

    if String.IsNullOrWhiteSpace entry.CredentialHash then
        [ problem
              where
              "is empty. Give a reference such as ${UMBRAL_OPERATOR_HASH}, holding the output of \
               `Umbral hash-credential`." ]
    elif (referencedVariable entry.CredentialHash).IsNone then
        // Said plainly, because the likeliest cause is the credential itself
        // pasted into the file, and the message must not echo the value.
        [ problem
              where
              "is a literal value. The file never holds the credential: run `Umbral hash-credential`, \
               put its output in an environment variable and write a reference such as \
               ${UMBRAL_OPERATOR_HASH} here, so that this file can be kept in version control." ]
    else
        []

/// `allowLocalhostHttp` is whether this instance runs with the Development
/// plain-http escape hatch, the one thing that lets a loopback issuer be http:
/// the same flag the admin form's `normaliseIssuer` is given, so the file and
/// the form accept the same addresses.
let private validateConnector (allowLocalhostHttp: bool) (index: int) (entry: ConnectorEntry) =
    let at field = $"connectors[{index}].{field}"
    let kind = Connectors.parseKind entry.Kind

    [ if not (Ids.isValidSlug entry.Slug) then
          problem
              (at "slug")
              $"\"{entry.Slug}\" is not a valid slug. Use lower-case letters, digits and hyphens, up to 32 \
                characters. It appears in the callback URL registered with the provider."
      else
          // A kind's own name is where its card posts, so a connector of another
          // kind under it would be edited as the wrong provider (the admin form
          // refuses it for the same reason). `oidc` is a kind's name too.
          match Connectors.parseKind entry.Slug, kind with
          | Some named, Some own when named <> own ->
              problem
                  (at "slug")
                  $"\"{entry.Slug}\" is the name of a built-in provider, so it can only be the slug of a connector \
                    of that kind. Choose another slug, like home or club-keycloak."
          | _ -> ()

      match kind with
      | None ->
          problem
              (at "kind")
              $"\"{entry.Kind}\" is not a provider Umbral supports. Use discord, github, google or oidc."
      | Some _ -> ()

      match kind, entry.Issuer with
      | Some Connectors.Oidc, None ->
          problem
              (at "issuer")
              "is missing. A kind: oidc entry needs the issuer your provider shows, like \
               https://auth.example.com/application/o/umbral."
      | Some Connectors.Oidc, Some issuer ->
          match Connectors.normaliseIssuer allowLocalhostHttp issuer with
          | Ok _ -> ()
          | Error error -> problem (at "issuer") error.Message
      | Some _, Some _ ->
          problem
              (at "issuer")
              $"is only for kind: oidc, and this entry is kind: {entry.Kind}. Remove the issuer from it."
      | _ -> ()

      match kind with
      | Some kind ->
          match Connectors.checkExtraScopes kind entry.ExtraScopes with
          | Ok _ -> ()
          | Error error -> problem (at "extra_scopes") error.Message
      | None -> ()

      if String.IsNullOrWhiteSpace entry.ClientId then
          problem (at "client_id") "is empty. Copy it from the provider's developer console."

      yield! validateSecret (at "client_secret") entry.ClientSecret ]

let private duplicates (selector: 'a -> string) (items: 'a list) =
    items
    |> List.map selector
    |> List.countBy id
    |> List.filter (fun (_, count) -> count > 1)
    |> List.map fst

/// `defined` is every connector slug the same document declares: a chosen set
/// is judged against the document alone, never the database, so whether a file
/// is valid does not depend on the instance it is applied to (FR-018).
let private validateApp (defined: Set<string>) (index: int) (entry: AppEntry) =
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
      | None -> ()

      match entry.Connectors with
      | None -> ()
      | Some slugs ->
          for i, slug in List.indexed slugs do
              if not (defined.Contains slug) then
                  problem
                      (at $"connectors[{i}]")
                      $"\"{slug}\" is not a connector defined in this file. Add it under connectors, or remove it from this app."

          for slug in duplicates id slugs do
              problem (at "connectors") $"\"{slug}\" appears more than once." ]

/// Two entries for one provider would share every person under two names, which
/// is why the database refuses it (`ux_connectors_issuer`) and the admin form
/// too. Judged on the normalised spelling, so a trailing slash or a capital in
/// the host does not make a second provider. Reported on the later entry.
let private duplicateIssuers (allowLocalhostHttp: bool) (connectors: ConnectorEntry list) =
    connectors
    |> List.indexed
    |> List.choose (fun (index, entry) ->
        match Connectors.parseKind entry.Kind, entry.Issuer with
        | Some Connectors.Oidc, Some issuer ->
            match Connectors.normaliseIssuer allowLocalhostHttp issuer with
            | Ok normalised -> Some(index, entry.Slug, normalised)
            | Error _ -> None
        | _ -> None)
    |> List.groupBy (fun (_, _, normalised) -> normalised)
    |> List.collect (fun (normalised, entries) ->
        match entries with
        | (firstIndex, firstSlug, _) :: later ->
            later
            |> List.map (fun (index, _, _) ->
                problem
                    $"connectors[{index}].issuer"
                    $"\"{normalised}\" is already the issuer of connectors[{firstIndex}] (\"{firstSlug}\"). One provider is one \
                      connector: remove one of the two entries.")
        | [] -> [])

/// Every problem in the document, not just the first. An operator fixing a file
/// by hand should get the whole list, not discover the next one on each restart.
///
/// `allowLocalhostHttp` is the instance's Development plain-http setting (see
/// `validateConnector`); `validate` is the answer for an instance without it.
let validateWith (allowLocalhostHttp: bool) (document: Document) : Problem list =
    [ if document.Version <> SupportedVersion then
          problem
              "version"
              $"is {document.Version}. This build understands version {SupportedVersion}."

      match document.Operator with
      | Some entry -> yield! validateOperator entry
      | None -> ()

      for slug in duplicates (fun (c: ConnectorEntry) -> c.Slug) document.Connectors do
          problem "connectors" $"\"{slug}\" appears more than once. A slug identifies a connector."

      for clientId in duplicates (fun (a: AppEntry) -> a.ClientId) document.Apps do
          problem "apps" $"\"{clientId}\" appears more than once. A client id identifies an app."

      for i, connector in List.indexed document.Connectors do
          yield! validateConnector allowLocalhostHttp i connector

      yield! duplicateIssuers allowLocalhostHttp document.Connectors

      let defined = document.Connectors |> List.map _.Slug |> Set.ofList

      for i, app in List.indexed document.Apps do
          yield! validateApp defined i app ]

let validate (document: Document) : Problem list = validateWith false document

/// The document with every `oidc` issuer in the one spelling Umbral stores and
/// compares (`Connectors.normaliseIssuer`). Run after `validateWith` has passed,
/// so an issuer that cannot be normalised is left as it is rather than hidden.
let normaliseIssuers (allowLocalhostHttp: bool) (document: Document) : Document =
    let normalise (entry: ConnectorEntry) =
        match Connectors.parseKind entry.Kind, entry.Issuer with
        | Some Connectors.Oidc, Some issuer ->
            match Connectors.normaliseIssuer allowLocalhostHttp issuer with
            | Ok normalised -> { entry with Issuer = Some normalised }
            | Error _ -> entry
        | _ -> entry

    { document with Connectors = document.Connectors |> List.map normalise }

/// One message an operator can act on, listing every problem found.
let describe (problems: Problem list) =
    problems
    |> List.map (fun p -> $"  {p.Where} {p.What}")
    |> String.concat Environment.NewLine
    |> fun body ->
        $"The configuration file cannot be applied:{Environment.NewLine}{body}{Environment.NewLine}\
          Nothing has been changed."
