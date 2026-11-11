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
