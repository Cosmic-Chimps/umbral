/// Which log categories are never allowed to be verbose (SC-008).
///
/// Umbral's own code never logs a secret — `SecurityEvents` takes named fields
/// for exactly that reason, and there are only two `ILogger` call sites. The
/// risk is the frameworks underneath it, which log whole requests and
/// responses. T093 captures every sink at Trace through a full journey; these
/// are what it found.
module Umbral.Infrastructure.LogRedaction

open Microsoft.Extensions.Logging

/// Each category, and why it is capped at Warning rather than left to the
/// minimum level.
let cappedCategories =
    [ // Logs "Request starting ... {Path}{QueryString}" at Information. The
      // query string of a provider's redirect back to Umbral is
      // `?code=<provider authorization code>&state=<correlation state>`: two
      // credentials, on every sign-in, at the default level. Request timing
      // is not worth that, and `security_events` is the record of what
      // happened.
      "Microsoft.AspNetCore.Hosting.Diagnostics"

      // At Trace, prints every token it creates and every response it returns
      // — access, refresh and id tokens, authorization codes and the reference
      // identifiers — unredacted: OpenIddict documents Trace as sensitive. At
      // INFORMATION it prints each token request with `code_verifier` intact
      // (it redacts `code` and `client_secret`, not the PKCE verifier).
      "OpenIddict.Server.OpenIddictServerDispatcher"

      // The same, for the validation half (userinfo): at Trace it prints
      // the bearer token it is validating.
      "OpenIddict.Validation.OpenIddictValidationDispatcher" ]

let capCategories (logging: ILoggingBuilder) =
    for category in cappedCategories do
        logging.AddFilter(category, LogLevel.Warning) |> ignore
