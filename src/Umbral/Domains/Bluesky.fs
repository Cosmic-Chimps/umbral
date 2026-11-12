/// The pure rules of the Bluesky connector (specs/015-bluesky-connector).
///
/// Pure, like `Fediverse`, and for the same reason: these rules decide which
/// handles, DIDs, servers and answers Umbral will believe, so every caller (the
/// account step, the resolver, the callback) shares one total function. Nothing
/// here does I/O and no exception escapes: a parse that fails is `None` or
/// `Error`. The guarded HTTP client is the second line, this is the first.
///
/// `allowDev` is the development setting (the Development environment with the
/// insecure-http flag). It admits the `.test` top-level label, `http` origins and
/// the loopback client ID, so a local fake server works.
///
/// The server allowlist is not here: the handlers call
/// `Umbral.Domains.Fediverse.parseServers` and `allows` directly.
module Umbral.Domains.Bluesky

open System
open System.Text
open System.Text.Json
open System.Text.Json.Nodes

// ---------------------------------------------------------------------------
// Handles (research R3)
// ---------------------------------------------------------------------------

let private isLabelChar (c: char) =
    (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c = '-'

let private labelOk (label: string) =
    label.Length >= 1
    && label.Length <= 63
    && label[0] <> '-'
    && label[label.Length - 1] <> '-'
    && Seq.forall isLabelChar label

/// Lower-case ASCII host-name syntax, as handles use: at least two labels, 253
/// characters at most, and the last label does not start with a digit. Used as
/// it is: callers lower-case first.
let private hostSyntax (text: string) =
    if text.Length = 0 || text.Length > 253 then
        false
    else
        let labels = text.Split '.'

        labels.Length >= 2
        && Array.forall labelOk labels
        && not (Char.IsAsciiDigit (labels[labels.Length - 1]).[0])

let private reservedTlds =
    set [ "alt"; "arpa"; "example"; "internal"; "invalid"; "local"; "localhost"; "onion" ]

let private tldOf (host: string) = host.Substring(host.LastIndexOf '.' + 1)

/// The one spelling of a handle: trimmed, one leading `@` and one trailing `.`
/// dropped, lower-cased. None when it is not a handle: wrong syntax, not ASCII,
/// a reserved top-level label (`.test` too, unless `allowDev`).
let normaliseHandle (allowDev: bool) (text: string | null) : string option =
    match text with
    | null -> None
    | value ->
        let trimmed = value.Trim()
        let noAt = if trimmed.StartsWith '@' then trimmed.Substring 1 else trimmed
        let noDot = if noAt.EndsWith '.' then noAt.Substring(0, noAt.Length - 1) else noAt

        // ASCII is checked before lower-casing: some non-ASCII letters (the
        // Kelvin sign) lower-case into ASCII ones.
        if not (Seq.forall Char.IsAscii noDot) then
            None
        else
            let handle = noDot.ToLowerInvariant()

            if not (hostSyntax handle) then None
            else
                let tld = tldOf handle

                if reservedTlds.Contains tld || (tld = "test" && not allowDev) then
                    None
                else
                    Some handle

/// Whether the text is a handle, in any spelling `normaliseHandle` accepts.
let isHandle (allowDev: bool) (text: string | null) : bool = (normaliseHandle allowDev text).IsSome

// ---------------------------------------------------------------------------
// DIDs (research R4)
// ---------------------------------------------------------------------------

let private plcPrefix = "did:plc:"
let private webPrefix = "did:web:"

let private isBase32 (c: char) = (c >= 'a' && c <= 'z') || (c >= '2' && c <= '7')

/// The DID itself when it is a `did:plc:` (24 characters of `[a-z2-7]`) or a
/// `did:web:` (a lower-case dotted host name: no port, no path, no `%`). A DID
/// is case-sensitive and is never lower-cased here. Every other method is None.
let parseDid (text: string | null) : string option =
    match text with
    | null -> None
    | did ->
        if did.StartsWith(plcPrefix, StringComparison.Ordinal) then
            let id = did.Substring plcPrefix.Length
            if id.Length = 24 && Seq.forall isBase32 id then Some did else None
        elif did.StartsWith(webPrefix, StringComparison.Ordinal) then
            if hostSyntax (did.Substring webPrefix.Length) then Some did else None
        else
            None

/// The host of a `did:web`, else None.
let didWebHost (text: string | null) : string option =
    match parseDid text with
    | Some did when did.StartsWith(webPrefix, StringComparison.Ordinal) -> Some(did.Substring webPrefix.Length)
    | _ -> None

// ---------------------------------------------------------------------------
// What the person typed
// ---------------------------------------------------------------------------

type Input =
    /// A handle, in its one normalised spelling.
    | Handle of string
    | Did of string

/// A handle, a DID, or None. Empty text is None.
let parseInput (allowDev: bool) (text: string | null) : Input option =
    match text with
    | null -> None
    | value ->
        let trimmed = value.Trim()
        let noAt = if trimmed.StartsWith '@' then trimmed.Substring 1 else trimmed

        if noAt.StartsWith("did:", StringComparison.Ordinal) then
            parseDid noAt |> Option.map Did
        else
            normaliseHandle allowDev noAt |> Option.map Handle

// ---------------------------------------------------------------------------
// DNS (research R3)
// ---------------------------------------------------------------------------

/// The DID of the `_atproto.<handle>` TXT records: the one value that starts
/// `did=`, after trimming and the quotes a DNS library may leave. None when
/// there are none, several, or the one is not a DID.
let atprotoDidOfTxt (values: string array) : string option =
    let didValues =
        values
        |> Array.choose (fun value ->
            // A null entry is as empty as an empty one.
            let unquoted =
                if String.IsNullOrEmpty value then "" else value.Trim().Trim('"').Trim()

            if unquoted.StartsWith("did=", StringComparison.Ordinal) then
                Some(unquoted.Substring 4)
            else
                None)

    match didValues with
    | [| one |] -> parseDid one
    | _ -> None

// ---------------------------------------------------------------------------
// JSON helpers
// ---------------------------------------------------------------------------

let private tryParseJson (json: string | null) : JsonDocument option =
    match json with
    | null -> None
    | text ->
        try
            Some(JsonDocument.Parse text)
        with _ ->
            None

let private tryProp (element: JsonElement) (name: string) : JsonElement option =
    if element.ValueKind <> JsonValueKind.Object then
        None
    else
        match element.TryGetProperty name with
        | true, value -> Some value
        | _ -> None

let private stringProp (element: JsonElement) (name: string) : string option =
    match tryProp element name with
    | Some value when value.ValueKind = JsonValueKind.String -> Option.ofObj (value.GetString())
    | _ -> None

let private boolProp (element: JsonElement) (name: string) : bool =
    match tryProp element name with
    | Some value -> value.ValueKind = JsonValueKind.True
    | None -> false

/// The strings of an array property; anything else in it, or no array, is ignored.
let private stringList (element: JsonElement) (name: string) : string list =
    match tryProp element name with
    | Some array when array.ValueKind = JsonValueKind.Array ->
        [ for item in array.EnumerateArray() do
              if item.ValueKind = JsonValueKind.String then
                  match item.GetString() with
                  | null -> ()
                  | text -> yield text ]
    | _ -> []

/// An `https` origin (`http` with `allowDev`): scheme, host and, only under
/// `allowDev`, a port other than the scheme's own, with no path other than `/`,
/// no query, no fragment, no userinfo. Returned in its one spelling, without a
/// trailing slash. Like the Fediverse connector's `normaliseServer`, production
/// connects to the default port only.
let private originOf (allowDev: bool) (text: string) : string option =
    let schemeOk =
        text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || (allowDev && text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))

    if not schemeOk || text.Trim() <> text || text.Contains '?' || text.Contains '#' then
        None
    else
        match Uri.TryCreate(text, UriKind.Absolute) with
        | true, NonNull uri when
            uri.UserInfo = ""
            && uri.Host <> ""
            && uri.AbsolutePath = "/"
            && uri.Query = ""
            && uri.Fragment = ""
            && (allowDev || uri.IsDefaultPort)
            ->
            Some(uri.GetLeftPart UriPartial.Authority)
        | _ -> None

/// An absolute `https` URL (`http` with `allowDev`), 2048 characters at most,
/// with no userinfo and no fragment. Returned as it was given.
let private endpointOf (allowDev: bool) (text: string) : string option =
    let schemeOk =
        text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || (allowDev && text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))

    if not schemeOk || text.Length > 2048 then
        None
    else
        match Uri.TryCreate(text, UriKind.Absolute) with
        | true, NonNull uri when uri.UserInfo = "" && uri.Host <> "" && uri.Fragment = "" && not (text.Contains '#') -> Some text
        | _ -> None

// ---------------------------------------------------------------------------
// The DID document (research R4)
// ---------------------------------------------------------------------------

type DidDocument =
    { Id: string
      AlsoKnownAs: string list
      /// The PDS origin, normalised, without a trailing slash.
      Pds: string }

let private pdsOf (allowDev: bool) (did: string) (root: JsonElement) : string option =
    match tryProp root "service" with
    | Some services when services.ValueKind = JsonValueKind.Array ->
        services.EnumerateArray()
        |> Seq.tryFind (fun service ->
            let idOk =
                match stringProp service "id" with
                | Some id -> id = "#atproto_pds" || id = did + "#atproto_pds"
                | None -> false

            idOk && stringProp service "type" = Some "AtprotoPersonalDataServer")
        |> Option.bind (fun service -> stringProp service "serviceEndpoint")
        |> Option.bind (originOf allowDev)
    | _ -> None

/// A DID document. `malformed` when it is not a JSON object, `id_mismatch` when
/// its `id` is not the DID asked for, `no_pds` when it names no usable PDS.
let parseDidDocument (allowDev: bool) (expectedDid: string) (json: string | null) : Result<DidDocument, string> =
    match tryParseJson json with
    | None -> Error "malformed"
    | Some doc ->
        use doc = doc
        let root = doc.RootElement

        if root.ValueKind <> JsonValueKind.Object then
            Error "malformed"
        elif stringProp root "id" <> Some expectedDid then
            Error "id_mismatch"
        else
            match pdsOf allowDev expectedDid root with
            | None -> Error "no_pds"
            | Some pds ->
                Ok
                    { Id = expectedDid
                      AlsoKnownAs = stringList root "alsoKnownAs"
                      Pds = pds }

/// Whether the document holds `at://<handle>`, compared without regard to case.
let claimsHandle (doc: DidDocument) (handle: string) : bool =
    let wanted = "at://" + handle
    doc.AlsoKnownAs |> List.exists (fun entry -> String.Equals(entry, wanted, StringComparison.OrdinalIgnoreCase))

/// The first `at://` entry that is a valid handle, normalised. The development
/// `.test` label is accepted here: this only names who signed in, and the
/// handle that was typed has already been judged with the real setting.
let handleOf (doc: DidDocument) : string option =
    doc.AlsoKnownAs
    |> List.tryPick (fun entry ->
        if entry.StartsWith("at://", StringComparison.OrdinalIgnoreCase) then
            normaliseHandle true (entry.Substring 5)
        else
            None)

// ---------------------------------------------------------------------------
// From PDS to authorization server (research R5)
// ---------------------------------------------------------------------------

/// The one authorization server origin of a protected resource document: exactly
/// one entry, an origin.
let parseProtectedResource (allowDev: bool) (json: string | null) : string option =
    match tryParseJson json with
    | None -> None
    | Some doc ->
        use doc = doc

        match tryProp doc.RootElement "authorization_servers" with
        | Some servers when servers.ValueKind = JsonValueKind.Array && servers.GetArrayLength() = 1 ->
            let only = servers[0]

            if only.ValueKind = JsonValueKind.String then
                Option.ofObj (only.GetString()) |> Option.bind (originOf allowDev)
            else
                None
        | _ -> None

type ServerMetadata =
    { Issuer: string
      Par: string
      Authorize: string
      Token: string }

/// The authorization server's metadata. Errors, each one word:
/// `malformed`, `issuer_mismatch`, then the FR-010 conditions: `par_required`,
/// `pkce_s256`, `dpop_es256`, `iss_parameter`, `scope_atproto`,
/// `client_metadata`, `auth_method_none`; last `endpoint_invalid`.
let parseServerMetadata (allowDev: bool) (origin: string) (json: string | null) : Result<ServerMetadata, string> =
    match tryParseJson json with
    | None -> Error "malformed"
    | Some doc ->
        use doc = doc
        let root = doc.RootElement

        if root.ValueKind <> JsonValueKind.Object then
            Error "malformed"
        else
            let par = stringProp root "pushed_authorization_request_endpoint"
            let has name value = List.contains value (stringList root name)

            let failed =
                if stringProp root "issuer" <> Some origin then Some "issuer_mismatch"
                elif not (boolProp root "require_pushed_authorization_requests") || par.IsNone then Some "par_required"
                elif not (has "code_challenge_methods_supported" "S256") then Some "pkce_s256"
                elif not (has "dpop_signing_alg_values_supported" "ES256") then Some "dpop_es256"
                elif not (boolProp root "authorization_response_iss_parameter_supported") then Some "iss_parameter"
                elif not (has "scopes_supported" "atproto") then Some "scope_atproto"
                elif not (boolProp root "client_id_metadata_document_supported") then Some "client_metadata"
                elif not (has "token_endpoint_auth_methods_supported" "none") then Some "auth_method_none"
                else None

            match failed with
            | Some word -> Error word
            | None ->
                // PAR, authorize and token must be the issuer's own: same scheme,
                // host and port. Uri.GetLeftPart drops a default port and lowers
                // the host, so the comparison is ordinal on both spellings.
                let sameOrigin (url: string) =
                    match Uri.TryCreate(url, UriKind.Absolute) with
                    | true, NonNull uri ->
                        String.Equals(uri.GetLeftPart UriPartial.Authority, origin, StringComparison.OrdinalIgnoreCase)
                    | _ -> false

                let endpoint name =
                    stringProp root name
                    |> Option.bind (endpointOf allowDev)
                    |> Option.filter sameOrigin

                match endpoint "pushed_authorization_request_endpoint", endpoint "authorization_endpoint", endpoint "token_endpoint" with
                | Some par, Some authorize, Some token ->
                    Ok
                        { Issuer = origin
                          Par = par
                          Authorize = authorize
                          Token = token }
                | _ -> Error "endpoint_invalid"

// ---------------------------------------------------------------------------
// The token answer (research R6, steps 5 and 6)
// ---------------------------------------------------------------------------

type TokenAnswer =
    { Sub: string
      Scope: string
      TokenType: string }

/// `sub`, `scope` and `token_type` of the token endpoint's answer; a missing or
/// non-string field is "". None when the answer is not a JSON object.
let parseTokenAnswer (json: string | null) : TokenAnswer option =
    match tryParseJson json with
    | None -> None
    | Some doc ->
        use doc = doc
        let root = doc.RootElement

        if root.ValueKind <> JsonValueKind.Object then
            None
        else
            let field name = stringProp root name |> Option.defaultValue ""

            Some
                { Sub = field "sub"
                  Scope = field "scope"
                  TokenType = field "token_type" }

/// Ok with the DID, or `token_invalid` (the type is not DPoP, the scope lacks
/// `atproto`, `sub` is not a DID), or `did_mismatch` (`sub` is not the DID the
/// sign-in began with). In that order.
let checkTokenAnswer (expectedDid: string) (answer: TokenAnswer) : Result<string, string> =
    let scopes = answer.Scope.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)

    if not (String.Equals(answer.TokenType, "DPoP", StringComparison.OrdinalIgnoreCase)) then
        Error "token_invalid"
    elif not (Array.contains "atproto" scopes) then
        Error "token_invalid"
    elif (parseDid answer.Sub).IsNone then
        Error "token_invalid"
    elif answer.Sub <> expectedDid then
        Error "did_mismatch"
    else
        Ok answer.Sub

// ---------------------------------------------------------------------------
// The profile record (research R10)
// ---------------------------------------------------------------------------

let private maxNameLength = 200

/// Control (Cc), format (Cf), line separator (Zl) and paragraph separator (Zp)
/// characters never belong in a display name: the same rule as Apple's `nameOf`
/// (a copy: that one is private to the provider), walked as code points so a
/// surrogate pair stays whole. U+2028 and U+2029 break lines in an app that
/// renders the name.
let private isDroppedInName (rune: Rune) =
    Rune.IsControl rune
    || (match Rune.GetUnicodeCategory rune with
        | Globalization.UnicodeCategory.Format
        | Globalization.UnicodeCategory.LineSeparator
        | Globalization.UnicodeCategory.ParagraphSeparator -> true
        | _ -> false)

/// A display name with no control, format or line-separating characters, trimmed and cut to 200
/// characters (never inside a surrogate pair). Empty is None.
let cleanName (text: string option) : string option =
    match text with
    | None -> None
    | Some value ->
        let builder = StringBuilder()

        for rune in value.EnumerateRunes() do
            if not (isDroppedInName rune) then
                builder.Append(rune.ToString()) |> ignore

        let joined = builder.ToString().Trim()

        let cut =
            if joined.Length <= maxNameLength then
                joined
            else
                let length =
                    if Char.IsHighSurrogate joined[maxNameLength - 1] then
                        maxNameLength - 1
                    else
                        maxNameLength

                joined.Substring(0, length).TrimEnd()

        if cut = "" then None else Some cut

/// `value.displayName` of a `getRecord` answer, cleaned.
let displayName (json: string | null) : string option =
    match tryParseJson json with
    | None -> None
    | Some doc ->
        use doc = doc

        tryProp doc.RootElement "value"
        |> Option.bind (fun value -> stringProp value "displayName")
        |> cleanName

/// `value.avatar.ref.$link` of a `getRecord` answer: a CID of `[a-z0-9]`, 1 to
/// 128 characters.
let avatarCid (json: string | null) : string option =
    match tryParseJson json with
    | None -> None
    | Some doc ->
        use doc = doc

        tryProp doc.RootElement "value"
        |> Option.bind (fun value -> tryProp value "avatar")
        |> Option.bind (fun avatar -> tryProp avatar "ref")
        |> Option.bind (fun ref -> stringProp ref "$link")
        |> Option.filter (fun cid ->
            cid.Length >= 1
            && cid.Length <= 128
            && Seq.forall (fun c -> (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) cid)

// ---------------------------------------------------------------------------
// The client ID and its document (research R8)
// ---------------------------------------------------------------------------

type ClientId =
    /// An `https` address that serves the metadata document.
    | Metadata of string
    /// AT Protocol's localhost client: the authorization server derives the
    /// metadata from the address, so no document is fetched.
    | Loopback of string

let private callbackPath = "/connect/bluesky/callback"

let private originText (origin: Uri) = origin.GetLeftPart UriPartial.Authority

/// The client ID for the instance's public origin. `https` with the default
/// port gives the metadata address. With `allowDev`, an `http` origin whose host
/// is itself `127.0.0.1` or `[::1]` gives the localhost client. Anything else is
/// `origin_unsuitable`.
let clientIdFor (allowDev: bool) (origin: Uri) : Result<ClientId, string> =
    if origin.Scheme = Uri.UriSchemeHttps && origin.IsDefaultPort then
        Ok(Metadata(originText origin + "/connect/bluesky/client-metadata.json"))
    elif allowDev
         && origin.Scheme = Uri.UriSchemeHttp
         && (origin.Host = "127.0.0.1" || origin.Host = "[::1]") then
        let redirect = originText origin + callbackPath

        Ok(Loopback("http://localhost?redirect_uri=" + Uri.EscapeDataString redirect + "&scope=atproto"))
    else
        Error "origin_unsuitable"

/// The client metadata document for the origin and the client ID.
let clientMetadata (origin: Uri) (clientId: string) : JsonObject =
    let text = originText origin

    let strings (items: string list) =
        let array = JsonArray()

        for item in items do
            array.Add(JsonValue.Create item)

        array

    let document = JsonObject()
    document["client_id"] <- JsonValue.Create clientId
    document["client_name"] <- JsonValue.Create "Umbral"
    document["client_uri"] <- JsonValue.Create text
    document["application_type"] <- JsonValue.Create "web"
    document["grant_types"] <- strings [ "authorization_code" ]
    document["response_types"] <- strings [ "code" ]
    document["redirect_uris"] <- strings [ text + callbackPath ]
    document["scope"] <- JsonValue.Create "atproto"
    document["token_endpoint_auth_method"] <- JsonValue.Create "none"
    document["dpop_bound_access_tokens"] <- JsonValue.Create true
    document

/// `clientMetadata`, serialised.
let clientMetadataJson (origin: Uri) (clientId: string) : string =
    (clientMetadata origin clientId).ToJsonString()
