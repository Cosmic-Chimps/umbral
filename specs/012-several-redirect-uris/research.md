# Research: Several redirect URIs per app

## R1. Storage: no schema change

OpenIddict (7.7.1, EF Core store) keeps an application's `RedirectUris` and
`PostLogoutRedirectUris` as JSON arrays on its own table, and
`AppClients.buildDescriptor` already takes lists. An app made today holds a
one-item list. Editing is `PopulateAsync(descriptor, application)`, a change to
the descriptor's sets, and `UpdateAsync(application, descriptor)`.

`UpdateAsync(application, descriptor)` re-hashes the secret only when the
descriptor's secret differs from the stored one; a descriptor populated from
the application carries the stored hash, so the secret is untouched. A test
exchanges a code with the original secret after an edit to pin this.

Decision: no schema step, no upgrade note. Umbral's `SchemaVersion` stays 9.

## R2. The limit: 10 per list

Ten covers localhost, a few staging hosts, production and a native scheme. A
larger list is a sign the app should be several apps, and the pane must stay
readable at 360px. The same number as the operator passkeys limit (spec 011),
so there is one "ten" to remember. The number lives in one place
(`RedirectUris.maxPerApp`), and the docs and the notice name it.

## R3. Loopback on any port stays out

RFC 8252 §7.3 asks the server to accept any port for a loopback IP redirect, so
a desktop app can listen on a free port. That is a new matching rule, not a
second exact URI, and Principle IV says "matched exactly". It needs its own
decision (only `http`, only `127.0.0.1` and `[::1]`, never `localhost` per
§8.3, exact path), its own tests at authorize and end-session, and possibly a
constitution note. A follow-up task carries it.

Found later the same day by that task (`spgx`, branch `alta/crisp-lark-n2hx`):
OpenIddict 7.7.1's own `ValidateRedirectUriAsync` and
`ValidatePostLogoutRedirectUriAsync` already ignore the port of a loopback
address for an app of type `native` (every "Single-page or native" app here),
and accept `localhost` and `https` too. So "matched exactly" was already not
entirely true for those apps before this spec. This spec changes nothing there.
`spgx` replaces both checks with Umbral's own rule. Until it merges, the claims
in this spec about exact matching hold for Web applications, and for native
apps except the loopback port.

## R4. "Already registered"

OpenIddict's descriptor holds a `HashSet<Uri>`, where `Uri` equality ignores
the case of the scheme and host. Adding `https://App.example.com/cb` next to
`https://app.example.com/cb` would be silently merged by the set. To never say
"added" when nothing was, a candidate equal (`Uri.Equals`) to a registered one
is refused as already registered. Authorize keeps comparing strings exactly
(OpenIddict's ordinal comparison), so the registered spelling is the one apps
must send.

## R5. Where the rule lives

`Domains/RedirectUris.fs` stays the one place: `acceptable` is unchanged, and
two pure functions decide an add and a removal on a list of registered strings
(`add` and `remove`, with closed refusal cases). The handler only reads,
writes and logs. The pure functions are unit-tested without a server.

## R6. Security log detail

`app_updated` already exists (`Detail = connector_choice` for the sign-in
methods). The new details follow the `;`/`=` form of `storeDetail`:
`redirect_uri_added;uri=<value>`, with the URI escaped by
`Uri.EscapeDataString` so a `;` or `=` in a query string cannot add a field.
A redirect URI is not a secret. Only operators read the log.
