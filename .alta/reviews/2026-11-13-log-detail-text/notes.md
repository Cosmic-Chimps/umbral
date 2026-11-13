# Task d3tl: free text in security-log details

The rule: a `detail` is `;`-separated fields of `name=value`. Text Umbral did not write
itself (an exception message, a provider's answer) must not be able to add a field.

## Details that carry free text (fixed here)

| Where | Detail | Free text from |
|---|---|---|
| `Infrastructure/ProviderAdapters.fs` `ConnectorError.Detail` | `declined: …`, `rejected: …`, `unreachable: …`, `malformed_response: …` | `HttpRequestException.Message` (names the host), a provider's `error` field (Discord, GitHub, Google), other exception messages, OIDC discovery reasons through `Providers/Oidc.fs`. Reaches the log from `Connect/Start.fs` (resolve failed), `Connect/Callback.fs` (exchange failed). |
| `Handlers/Admin/Connectors.fs` OIDC save | `store=…;saved_disabled: <reason>` | OIDC discovery reason (an exception message) |
| `Handlers/Admin/Connectors.fs` Apple save | `store=…;saved_disabled: <reason>` | Apple reachability reason |
| `Handlers/Admin/Connectors.fs` Microsoft save | `store=…;audience=…;saved_disabled: <reason>` | OIDC discovery reason |

All four go through `SecurityEvents.safeText`. `ConnectorError.Detail` is the one place
for every adapter, so the provider text is cleaned there and not at each call site.

## Details checked and left alone (already safe)

- `AccountRefused` → `refused: tenant_not_allowed;tenant=<guid>`: Umbral writes the
  structure on purpose, the tenant is the canonical GUID (`Providers/Microsoft.fs`).
- `RoundTripError.Detail` `provider_error:<word>`: through `cleanErrorWord` (`OAuthRoundTrip.fs`).
- `refusalDetail` (email domains): closed words and `safeDomain`.
- `storeDetail`, `connector_secret_unreadable;store=…;cause=…`: store id and closed cause word.
- `server=<host>` (Fediverse, Bluesky, Authorize, Token): hosts through
  `Fediverse.normaliseServer` / `Uri.Host`, or read back from stored subjects.
- `tenant=<guid>` (Authorize, Token): parsed GUIDs.
- `audience=…`: `Microsoft.stored`, a closed word or a GUID.
- `uri=…` (`AppRedirectUris.fs`): `Uri.EscapeDataString`.
- `from=…;to=…` (`SecretMove.fs`), `count=…`, `enrolment=…`, `via=…`, `by=…`, `for=…`,
  `apple-logo=…`, and every fixed word (Setup, SignIn, Credential, OperatorPasskeys,
  OperatorPasswordSignIn, AppBranding, Passkeys, ConnectorToggle, Program offline reset).
- `ConnectorToggle.fs`: its enable/disable event has no detail; its Apple/discovery
  reasons go to the notice cookie only, not to the log.
