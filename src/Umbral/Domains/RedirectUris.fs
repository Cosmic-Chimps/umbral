/// The rule for what may be registered as a redirect URI.
///
/// Pure, so the rule can be read and tested without the Apps screen or
/// OpenIddict in the way. The screen calls it before OpenIddict sees the value,
/// so a rejection names the offending value rather than surfacing as an opaque
/// mismatch at the authorize step.
module Umbral.Domains.RedirectUris

open System

/// Whether a string may be used as a redirect URI, and why not if not.
///
/// One rule in one place. It used to be written twice, and the second copy was
/// missing the file-scheme case below — which is how a rule written twice
/// always ends.
let acceptable (value: string) : Result<Uri, string> =
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

/// How many addresses one list (redirect URIs, or post-logout redirect URIs)
/// may hold. A bound keeps the pane readable and the app's stored descriptor
/// small; ten is more than any real app has asked for.
let maxPerApp = 10

/// Why a candidate was not added to a list.
type AddRefusal =
    /// `acceptable` refused it; carries its reason, so the screen can name it.
    | Invalid of string
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

    match acceptable trimmed with
    | Error why -> Error(Invalid why)
    | Ok uri ->
        let same (existing: string) =
            existing = trimmed
            || (match Uri.TryCreate(existing, UriKind.Absolute) with
                | true, other -> not (isNull other) && Uri.Equals(other, uri)
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
