/// The rule for what may be registered as a redirect URI.
///
/// Pure, so the rule can be read and tested without the Apps screen or
/// OpenIddict in the way. The screen calls it before OpenIddict sees the value,
/// so a rejection names the offending value rather than surfacing as an opaque
/// mismatch at the authorize step.
module Umbral.Domains.RedirectUris

open System

/// Why a string may not be used as a redirect URI. A value with no words, so a
/// screen can say it in its own language and the reason can travel as a code.
type Problem =
    | Empty
    | Wildcard
    | NotAbsolute
    | NoHost
    | Fragment

/// The code of a problem: stable text for a cookie or a test, never shown.
let problemCode (problem: Problem) : string =
    match problem with
    | Empty -> "empty"
    | Wildcard -> "wildcard"
    | NotAbsolute -> "not-absolute"
    | NoHost -> "no-host"
    | Fragment -> "fragment"

/// The problem a code names, or `None` for anything else.
let problemOfCode (code: string) : Problem option =
    match code with
    | "empty" -> Some Empty
    | "wildcard" -> Some Wildcard
    | "not-absolute" -> Some NotAbsolute
    | "no-host" -> Some NoHost
    | "fragment" -> Some Fragment
    | _ -> None

/// Whether a string may be used as a redirect URI, and the problem if not.
///
/// One rule in one place. It used to be written twice, and the second copy was
/// missing the file-scheme case below — which is how a rule written twice
/// always ends.
let check (value: string) : Result<Uri, Problem> =
    // The wildcard check comes FIRST. A URL with a `*` in the host does not
    // parse as a Uri at all, so leaving it to the parser produces "is not an
    // absolute URL" — true, but it names the wrong cause for an operator who
    // deliberately wrote a wildcard and needs to be told Umbral matches exactly.
    if String.IsNullOrWhiteSpace value then
        Error Empty
    elif value.Contains '*' then
        Error Wildcard
    else
        match Uri.TryCreate(value, UriKind.Absolute) with
        | false, _ -> Error NotAbsolute
        | true, uri ->
            match Option.ofObj uri with
            | None -> Error NotAbsolute

            // On Unix a leading slash parses as an absolute FILE uri, so
            // "/callback" would otherwise pass as an absolute URL and be stored
            // as a redirect target no provider could ever send anyone to.
            | Some uri when uri.Scheme = Uri.UriSchemeFile -> Error NotAbsolute

            | Some uri when
                (uri.Scheme = Uri.UriSchemeHttp || uri.Scheme = Uri.UriSchemeHttps)
                && String.IsNullOrEmpty uri.Host
                ->
                Error NoHost

            | Some uri when uri.Fragment <> "" -> Error Fragment

            | Some uri -> Ok uri

/// `check` with its problem in words, naming the value.
let acceptable (value: string) : Result<Uri, string> =
    match check value with
    | Ok uri -> Ok uri
    | Error Empty -> Error "is empty."
    | Error Wildcard -> Error $"\"{value}\" contains a wildcard. Redirect URIs are matched exactly."
    | Error NotAbsolute ->
        match Uri.TryCreate(value, UriKind.Absolute) with
        // A file uri is what a leading slash parses as: it has its own advice.
        | true, uri when (match Option.ofObj uri with
                          | Some u -> u.Scheme = Uri.UriSchemeFile
                          | None -> false) ->
            Error
                $"\"{value}\" is not an absolute URL. Include the scheme and host, \
                  for example https://app.example.com/callback."
        | _ -> Error $"\"{value}\" is not an absolute URL."
    | Error NoHost -> Error $"\"{value}\" has no host."
    | Error Fragment -> Error $"\"{value}\" contains a fragment, which a provider will never send back."

/// RFC 8252 §7.3: whether `requested` is `registered` with a port added, for a
/// registration on a loopback IP address.
///
/// A native app cannot know in advance which port it will be able to listen on,
/// so it registers `http://127.0.0.1/callback` and asks for
/// `http://127.0.0.1:53124/callback`. Only the port may differ.
///
/// The rule is narrower than the one OpenIddict ships, on purpose, and it is
/// plain text comparison with no `Uri` normalisation, so what was registered is
/// what is matched:
///
///  - The host is the literal `127.0.0.1` or `[::1]`, written as `http://`.
///    `localhost` is excluded (RFC 8252 §8.3): a name resolves through a
///    resolver or a hosts file the app does not control, where the IP literal
///    cannot be redirected. `https` is excluded because a loopback listener has
///    no certificate worth the name.
///  - A registration that already names a port never qualifies: it asked for
///    that port, and only that port.
///  - The path and query are the registered text, character for character.
///  - The port is 1 to 5 digits, no leading zero, 1 to 65535.
///
/// An identical `requested` is false here: exact matching is the caller's first
/// check, and this rule is for what it does not cover.
let loopbackOnAnyPort (registered: string) (requested: string) : bool =
    let validPort (digits: string) =
        digits.Length >= 1
        && digits.Length <= 5
        && digits[0] <> '0'
        && Seq.forall (fun c -> c >= '0' && c <= '9') digits
        && int digits <= 65535

    let matches (prefix: string) =
        registered.StartsWith(prefix, StringComparison.Ordinal)
        && (let remainder = registered.Substring prefix.Length

            (remainder = "" || remainder[0] = '/' || remainder[0] = '?')
            && requested.StartsWith(prefix + ":", StringComparison.Ordinal)
            && requested.Length > prefix.Length + 1 + remainder.Length
            && requested.EndsWith(remainder, StringComparison.Ordinal)
            && validPort (requested.Substring(prefix.Length + 1, requested.Length - prefix.Length - 1 - remainder.Length)))

    matches "http://127.0.0.1" || matches "http://[::1]"

/// How many addresses one list (redirect URIs, or post-logout redirect URIs)
/// may hold. A bound keeps the pane readable and the app's stored descriptor
/// small; ten is more than any real app has asked for.
let maxPerApp = 10

/// Why a candidate was not added to a list.
type AddRefusal =
    /// `check` refused it; carries the problem, so the screen can name it.
    | Invalid of Problem
    | AlreadyRegistered
    | Full

/// The list with `candidate` appended, or the reason it cannot be.
///
/// Checks run Invalid, then AlreadyRegistered, then Full: a duplicate is
/// reported as a duplicate even when the list is full, because that is the
/// answer that helps. A registered string counts as a duplicate when it parses
/// to the same Uri (so a host written in another case is caught), or when it is
/// the same text — the latter covers entries stored before this rule existed
/// that no longer parse the same way.
let add (registered: string list) (candidate: string) : Result<string list, AddRefusal> =
    let trimmed = candidate.Trim()

    match check trimmed with
    | Error problem -> Error(Invalid problem)
    | Ok uri ->
        // A port-less loopback address accepts any port, the same one with a
        // port only that port, so the two are different registrations even
        // where `Uri` equality (port 80 is http's default) calls them the same.
        let same (existing: string) =
            existing = trimmed
            || (match Uri.TryCreate(existing, UriKind.Absolute) with
                | true, other ->
                    not (isNull other)
                    && Uri.Equals(other, uri)
                    && not (loopbackOnAnyPort existing trimmed || loopbackOnAnyPort trimmed existing)
                | _ -> false)

        if List.exists same registered then Error AlreadyRegistered
        elif registered.Length >= maxPerApp then Error Full
        else Ok(registered @ [ uri.OriginalString ])

/// Why a value was not removed from a list.
type RemoveRefusal =
    | NotRegistered
    /// Only for the redirect list: an app with no redirect URI cannot sign anyone in.
    | LastOne

/// The list without `value`.
///
/// The match is on the stored string, ordinal, because that is what the pane
/// posts back; a normalised comparison could remove a different entry than
/// the one the operator clicked. `keepOne` is the redirect list's rule that it
/// may never become empty; the post-logout list passes false.
let remove (registered: string list) (value: string) (keepOne: bool) : Result<string list, RemoveRefusal> =
    if not (List.contains value registered) then Error NotRegistered
    elif keepOne && registered.Length = 1 then Error LastOne
    else Ok(registered |> List.filter (fun r -> r <> value))
