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
