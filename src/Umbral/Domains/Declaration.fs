/// The declaration (spec 019): the apps and the passkey connector an operator
/// writes in the environment, read and checked as a value.
///
/// Pure on purpose: no configuration, no database, no OpenIddict. `parse` takes
/// the text of `UMBRAL__DECLARATION` and the child keys of
/// `UMBRAL:APP_SECRETS`, and returns the declaration or every problem found.
/// Checking all of it before anything is written is D6: a half-applied
/// declaration is worse than a start that says why it did not happen.
///
/// No problem line ever holds a secret value, and `AppSecret` cannot print one.
module Umbral.Domains.Declaration

open System
open System.Collections.Generic
open System.Text.Json

/// The two app types of the admin area: confidential (a secret) and public (PKCE only).
type AppType =
    | Web
    | Spa

/// A declared secret. `ToString` never shows the value, so `%A`, a log line or
/// a test failure message cannot leak it; `Value` is the only way to read it.
[<Sealed>]
type AppSecret private (value: string) =
    member _.Value = value
    override _.ToString() = "AppSecret(***)"

    // Value equality, so two declarations read from the same input are equal.
    override _.Equals(other: obj) =
        match other with
        | :? AppSecret as s -> String.Equals(value, s.Value, StringComparison.Ordinal)
        | _ -> false

    override _.GetHashCode() = StringComparer.Ordinal.GetHashCode value

    static member Create(value: string) = AppSecret value

/// One declared app. `Key` is the client ID.
type DeclaredApp =
    {
        Key: string
        /// Trimmed; the key when the declaration gives no name.
        Name: string
        Type: AppType
        /// The stored form, in declared order.
        RedirectUris: string list
        PostLogoutRedirectUris: string list
        Connectors: Connectors.AppChoice
        /// `Some` for a web app, `None` for a spa app.
        Secret: AppSecret option
    }

/// The declared passkey connector: it exists, is enabled and shared.
type DeclaredPasskeys = { SignUp: Connectors.PasskeyEnrolment }

type Declaration =
    {
        /// `None`: the passkey connector is not declared.
        Passkeys: DeclaredPasskeys option
        Apps: DeclaredApp list
    }

/// The only version this Umbral reads.
let supportedVersion = 1

let private maxNameLength = 100

let private secretVariable (key: string) = $"UMBRAL__APP_SECRETS__{key}"

let private minSecretLength = 32

/// A short, safe rendering of a JSON value for a problem line (never a secret:
/// the declaration holds none).
let private describe (element: JsonElement) =
    let raw = element.GetRawText()
    if raw.Length > 40 then raw.Substring(0, 40) + "..." else raw

/// Read the properties of an object into a map, reporting an unknown property
/// and a property written twice. `label` starts every line.
let private readObject
    (problems: ResizeArray<string>)
    (label: string)
    (known: string list)
    (element: JsonElement)
    : Dictionary<string, JsonElement> =
    let props = Dictionary<string, JsonElement>(StringComparer.Ordinal)

    for p in element.EnumerateObject() do
        if not (List.contains p.Name known) then
            problems.Add $"{label}: unknown property \"{p.Name}\". Remove it."
        elif props.ContainsKey p.Name then
            problems.Add $"{label}: the property \"{p.Name}\" is written twice. Keep one."
        else
            props[p.Name] <- p.Value

    props

/// A string property, or a problem when it is not a string.
let private stringProp (problems: ResizeArray<string>) (label: string) (name: string) (element: JsonElement) =
    if element.ValueKind = JsonValueKind.String then
        Some(element.GetString() |> Option.ofObj |> Option.defaultValue "")
    else
        problems.Add $"{label}: \"{name}\" must be a string, not {describe element}."
        None

/// One address list: every entry checked and stored by the admin area's own
/// rule (`RedirectUris.add`), so a duplicate and the cap mean the same here.
let private readUris
    (problems: ResizeArray<string>)
    (label: string)
    (name: string)
    (required: bool)
    (found: JsonElement option)
    : string list =
    match found with
    | None ->
        if required then
            problems.Add $"{label}: \"{name}\" is missing. List at least one address."

        []
    | Some element when element.ValueKind <> JsonValueKind.Array ->
        problems.Add $"{label}: \"{name}\" must be a list of addresses, not {describe element}."
        []
    | Some element ->
        let items = element.EnumerateArray() |> Seq.toList

        if required && items.IsEmpty then
            problems.Add $"{label}: \"{name}\" is empty. List at least one address."

        if items.Length > RedirectUris.maxPerApp then
            problems.Add
                $"{label}: \"{name}\" has {items.Length} addresses; at most {RedirectUris.maxPerApp} are allowed."

        let mutable stored: string list = []

        for i, item in items |> List.truncate RedirectUris.maxPerApp |> List.indexed do
            if item.ValueKind <> JsonValueKind.String then
                problems.Add $"{label}: {name}[{i}] must be a string, not {describe item}."
            else
                let text = (item.GetString() |> Option.ofObj |> Option.defaultValue "").Trim()

                match RedirectUris.add stored text with
                | Ok next -> stored <- next
                | Error(RedirectUris.Invalid _) ->
                    match RedirectUris.acceptable text with
                    | Error reason -> problems.Add $"{label}: {name}[{i}] {reason}"
                    | Ok _ -> problems.Add $"{label}: {name}[{i}] \"{text}\" is not acceptable."
                | Error RedirectUris.AlreadyRegistered ->
                    problems.Add
                        $"{label}: {name}[{i}] \"{text}\" is already listed, perhaps written differently. List each address once."
                | Error RedirectUris.Full ->
                    problems.Add
                        $"{label}: {name}[{i}] \"{text}\" does not fit; at most {RedirectUris.maxPerApp} are allowed."

        stored

let private readConnectors
    (problems: ResizeArray<string>)
    (label: string)
    (found: JsonElement option)
    : Connectors.AppChoice =
    match found with
    | None -> Connectors.AllShared
    | Some element when element.ValueKind = JsonValueKind.String ->
        match element.GetString() with
        | "all-shared" -> Connectors.AllShared
        | other ->
            problems.Add
                $"{label}: \"connectors\" is \"{other}\". Write \"all-shared\" or a list of connector keys."

            Connectors.AllShared
    | Some element when element.ValueKind = JsonValueKind.Array ->
        let mutable slugs = Set.empty

        for i, item in element.EnumerateArray() |> Seq.indexed do
            if item.ValueKind <> JsonValueKind.String then
                problems.Add $"{label}: connectors[{i}] must be a string, not {describe item}."
            else
                let slug = item.GetString() |> Option.ofObj |> Option.defaultValue ""

                if Ids.isValidSlug slug then
                    slugs <- Set.add slug slugs
                else
                    problems.Add
                        $"{label}: connectors[{i}] \"{slug}\" is not a connector key (lowercase letters, digits and hyphens, at most 32)."

        Connectors.Only slugs
    | Some element ->
        problems.Add
            $"{label}: \"connectors\" must be \"all-shared\" or a list of connector keys, not {describe element}."

        Connectors.AllShared

/// A key is a connector slug: it becomes the client ID.
let private validKey (key: string) = Ids.isValidSlug key

/// The text of the secret variable(s) naming `key`: the key itself and, for a
/// key with hyphens, the form with `_` in their place (N1). Lowercase.
let private secretForms (key: string) =
    [ key; key.Replace('-', '_') ] |> List.distinct

/// Check the secret of one app against the secrets given. Adds problems and
/// returns the secret to keep for a web app.
let private checkSecret
    (problems: ResizeArray<string>)
    (label: string)
    (key: string)
    (appType: AppType option)
    (secrets: (string * string) list)
    : AppSecret option =
    let forms = secretForms key

    let found =
        secrets
        |> List.filter (fun (k, _) -> List.contains (k.ToLowerInvariant()) forms)

    match appType, found with
    | None, _ -> None
    | Some Spa, [] -> None
    | Some Spa, (k, _) :: _ ->
        problems.Add
            $"{label}: {secretVariable k} is set, but a spa app is public and holds no secret. Remove the variable."

        None
    | Some Web, [] ->
        problems.Add
            $"{label}: {secretVariable (key.Replace('-', '_'))} is not set. A web app needs a secret of at least {minSecretLength} characters."

        None
    | Some Web, [ (k, v) ] ->
        if v.Length < minSecretLength then
            problems.Add $"{label}: {secretVariable k} is shorter than {minSecretLength} characters."
            None
        else
            Some(AppSecret.Create v)
    | Some Web, many ->
        let names = many |> List.map (fun (k, _) -> secretVariable k) |> String.concat " and "
        problems.Add $"{label}: {names} are both set. Keep one of them."
        None

/// The declared app at `index`, with every problem found in it. Returns `None`
/// when the element is not an object. `key` of the element, when valid, is
/// returned for the caller to track uniqueness and secrets.
let private readApp
    (problems: ResizeArray<string>)
    (seenKeys: Dictionary<string, int>)
    (claimedKeys: HashSet<string>)
    (secrets: (string * string) list)
    (index: int)
    (element: JsonElement)
    : DeclaredApp option =
    let baseLabel = $"apps[{index}]"

    if element.ValueKind <> JsonValueKind.Object then
        problems.Add $"{baseLabel}: must be an object, not {describe element}."
        None
    else
        // A valid key names the app in every line, the unknown-property ones too.
        let peekedLabel =
            match element.TryGetProperty "key" with
            | true, k when k.ValueKind = JsonValueKind.String && validKey (k.GetString() |> Option.ofObj |> Option.defaultValue "") ->
                $"{baseLabel} (key \"{k.GetString()}\")"
            | _ -> baseLabel

        let props =
            readObject
                problems
                peekedLabel
                [ "key"; "name"; "type"; "redirectUris"; "postLogoutRedirectUris"; "connectors" ]
                element

        let get name =
            match props.TryGetValue name with
            | true, v -> Some v
            | _ -> None

        // The key first: it names every later line.
        let key =
            match get "key" with
            | None ->
                problems.Add $"{baseLabel}: \"key\" is missing. It becomes the client ID."
                None
            | Some k when k.ValueKind <> JsonValueKind.String ->
                problems.Add $"{baseLabel}: \"key\" must be a string, not {describe k}."
                None
            | Some k ->
                let text = k.GetString() |> Option.ofObj |> Option.defaultValue ""

                if validKey text then
                    Some text
                else
                    problems.Add
                        $"{baseLabel}: \"key\" \"{text}\" is not valid. Use lowercase letters, digits and hyphens, at most 32."

                    None

        let label =
            match key with
            | Some k -> $"{baseLabel} (key \"{k}\")"
            | None -> baseLabel

        let duplicate =
            match key with
            | Some k when seenKeys.ContainsKey k ->
                problems.Add
                    $"{label}: the key \"{k}\" is already used by apps[{seenKeys[k]}]. Keys must be unique."

                true
            | Some k ->
                seenKeys[k] <- index
                false
            | None -> false

        let name =
            match get "name" with
            | None -> key
            | Some n ->
                match stringProp problems label "name" n with
                | None -> key
                | Some text ->
                    let trimmed = text.Trim()

                    if trimmed = "" then
                        problems.Add $"{label}: \"name\" is empty. Leave it out to use the key."
                        key
                    elif trimmed.Length > maxNameLength then
                        problems.Add $"{label}: \"name\" is longer than {maxNameLength} characters."
                        key
                    else
                        Some trimmed

        let appType =
            match get "type" with
            | None ->
                problems.Add $"{label}: \"type\" is missing. Write \"web\" or \"spa\"."
                None
            | Some t ->
                match stringProp problems label "type" t with
                | Some "web" -> Some Web
                | Some "spa" -> Some Spa
                | Some other ->
                    problems.Add $"{label}: \"type\" is \"{other}\". Write \"web\" or \"spa\"."
                    None
                | None -> None

        let redirectUris = readUris problems label "redirectUris" true (get "redirectUris")

        let postLogoutRedirectUris =
            readUris problems label "postLogoutRedirectUris" false (get "postLogoutRedirectUris")

        let connectors = readConnectors problems label (get "connectors")

        // A secret variable naming this app is claimed by it, whatever else is
        // wrong with the app, so it is not reported again as a stray.
        match key with
        | Some k when not duplicate ->
            for form in secretForms k do
                claimedKeys.Add form |> ignore
        | _ -> ()

        let secret =
            match key with
            | Some k when not duplicate -> checkSecret problems label k appType secrets
            | _ -> None

        match key, name, appType with
        | Some k, Some n, Some t when not duplicate ->
            Some
                {
                    Key = k
                    Name = n
                    Type = t
                    RedirectUris = redirectUris
                    PostLogoutRedirectUris = postLogoutRedirectUris
                    Connectors = connectors
                    Secret = secret
                }
        | _ -> None

/// Read and check the declaration.
///
/// `json` is the value of `UMBRAL__DECLARATION`. `secrets` are the child keys
/// and values of the configuration section `UMBRAL:APP_SECRETS`, the key as
/// written and in any case. Whether a named connector exists, and whether a
/// client ID already belongs to an app made in the admin area, are not known
/// here: the caller checks those against the database.
///
/// Every problem is returned, in document order, then the stray secret
/// variables by name. No line holds a secret value.
let parse (json: string) (secrets: (string * string) list) : Result<Declaration, string list> =
    let problems = ResizeArray<string>()

    let document =
        if String.IsNullOrWhiteSpace json then
            problems.Add "declaration: it is empty. Write a JSON document with \"version\": 1."
            None
        else
            try
                Some(JsonDocument.Parse json)
            with :? JsonException as e ->
                problems.Add $"declaration: it is not valid JSON ({e.Message})"
                None

    match document with
    | None -> Error(List.ofSeq problems)
    | Some doc ->
        use doc = doc
        let root = doc.RootElement

        if root.ValueKind <> JsonValueKind.Object then
            Error [ "declaration: it must be a JSON object, with \"version\": 1." ]
        else
            let props = readObject problems "declaration" [ "version"; "passkeys"; "apps" ] root

            // version
            match props.TryGetValue "version" with
            | false, _ ->
                problems.Add $"declaration: \"version\" is missing; this Umbral reads declaration version {supportedVersion}."
            | true, v ->
                let ok =
                    v.ValueKind = JsonValueKind.Number
                    && (match v.TryGetInt32() with
                        | true, n -> n = supportedVersion
                        | _ -> false)

                if not ok then
                    problems.Add
                        $"declaration: \"version\" is {describe v}; this Umbral reads declaration version {supportedVersion}."

            // passkeys
            let passkeys =
                match props.TryGetValue "passkeys" with
                | false, _ -> None
                | true, p when p.ValueKind <> JsonValueKind.Object ->
                    problems.Add $"passkeys: must be an object, not {describe p}."
                    None
                | true, p ->
                    let inner = readObject problems "passkeys" [ "signUp" ] p

                    match inner.TryGetValue "signUp" with
                    | false, _ -> Some { SignUp = Connectors.InvitationsOnly }
                    | true, s ->
                        match s.ValueKind, (if s.ValueKind = JsonValueKind.String then s.GetString() else null) with
                        | JsonValueKind.String, "invitations" -> Some { SignUp = Connectors.InvitationsOnly }
                        | JsonValueKind.String, "open" -> Some { SignUp = Connectors.OpenSignUp }
                        | _ ->
                            problems.Add
                                $"passkeys: \"signUp\" is {describe s}. Write \"invitations\" or \"open\"."

                            None

            // apps
            let seenKeys = Dictionary<string, int>(StringComparer.Ordinal)
            let claimed = HashSet<string>(StringComparer.Ordinal)

            let apps =
                match props.TryGetValue "apps" with
                | false, _ -> []
                | true, a when a.ValueKind <> JsonValueKind.Array ->
                    problems.Add $"declaration: \"apps\" must be a list, not {describe a}."
                    []
                | true, a ->
                    a.EnumerateArray()
                    |> Seq.toList
                    |> List.indexed
                    |> List.choose (fun (i, element) -> readApp problems seenKeys claimed secrets i element)

            // Secrets naming no declared app: a leftover for a removed or renamed app.
            secrets
            |> List.filter (fun (k, _) -> not (claimed.Contains(k.ToLowerInvariant())))
            |> List.sortBy (fun (k, _) -> k.ToLowerInvariant())
            |> List.iter (fun (k, _) ->
                problems.Add
                    $"{secretVariable k}: no declared web app has the key \"{k}\". Remove the variable.")

            if problems.Count > 0 then
                Error(List.ofSeq problems)
            else
                Ok { Passkeys = passkeys; Apps = apps }
