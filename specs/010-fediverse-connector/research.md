# Research: The Fediverse connector

Decisions for spec 010, made 2026-10-24 from three code readings (provider
seam, connect handlers, admin frame) and checks against live servers and their
source. Each entry: Decision, Rationale, Alternatives considered.

## R1. Which servers are supported, and how Umbral knows

**Decision.** A server is supported when either:

1. its OAuth server metadata (`/.well-known/oauth-authorization-server`)
   lists `S256` in `code_challenge_methods_supported` and names an
   `app_registration_endpoint`, or
2. it publishes no such metadata and its NodeInfo (`/.well-known/nodeinfo`,
   then the 2.0 or 2.1 document it links) names the software `gotosocial`.

Everything else is unsupported: Mastodon before 4.3, Akkoma, Pleroma, Misskey,
Sharkey and any unknown software. Every endpoint Umbral then uses must be
`https` on the server's own host (`http` and loopback only under the
development setting, R6).

**Rationale.** The task makes PKCE a hard requirement. What the servers do:

- Mastodon 4.3+ publishes metadata with `code_challenge_methods_supported:
  ["S256"]` and `app_registration_endpoint` (checked on mastodon.social).
- GoToSocial 0.22 has no metadata (404) but enforces S256 PKCE
  (`internal/oauth/server.go`, `AllowedCodeChallengeMethods: S256`). Its
  NodeInfo says `gotosocial`. Its endpoints are Mastodon's: `/api/v1/apps`,
  `/oauth/authorize`, `/oauth/token`.
- Akkoma (and Pleroma, which shares the code) has no metadata, and its OAuth
  controller, authorization and token modules never read `code_challenge`.
  It cannot satisfy PKCE, so it is refused. This narrows the task's "Pleroma/
  Akkoma" list; `umbral-designer` approved the copy that says so (2026-10-24).
- Misskey publishes no app registration endpoint (MiAuth, or IndieAuth-style
  client ids), so rule 1 refuses it even if it publishes metadata.

**Alternatives.** Send PKCE to every server and hope: a server that ignores
`code_challenge` would accept the code without checking it, and Umbral could
not tell. Version-sniffing Mastodon through `/api/v2/instance`: metadata is the
documented signal and covers forks that backport it.

## R2. Finding the server from what the person typed

**Decision.** Normalise the input (R5). A server (`mastodon.social`,
`https://mastodon.social`) is used as is. For a handle (`@ana@example.com` or
`ana@example.com`) Umbral asks the handle's domain with WebFinger
(`/.well-known/webfinger?resource=acct:ana@example.com`), takes the host of the
`self` link (`application/activity+json`) and uses that as the server. If
WebFinger fails or has no `self` link, the handle's domain is the server. The
host WebFinger names is normalised and checked again exactly like typed input.

**Rationale.** Mastodon lets an account's handle live on one domain and its
server on another (`LOCAL_DOMAIN` versus `WEB_DOMAIN`). One bounded request
covers it; the fallback keeps the common case working when WebFinger is
blocked.

**Alternatives.** Ask the person for the server only: worse for people who
know their handle and not their server.

## R3. The server list (operator control)

**Decision.** In "Only these servers" mode the typed domain (the server, or the
handle's domain) must be on the list before Umbral contacts anything, and the
server WebFinger resolves to must be on the list too. The callback checks the
server again (FR-019). An empty list refuses everyone.

**Rationale.** FR-009 says an unlisted server is refused before contact. For a
split-domain server the operator lists both names; the walkthrough says so.

**Alternatives.** Match only the resolved server: needs a WebFinger request to
a domain that may not be allowed, which breaks FR-009.

## R4. Where the fediverse code lives (no adapter)

**Decision.** Fediverse does not implement `IProviderAdapter`. New files:

- `Domains/Fediverse.fs`: pure. Input parsing, server normalisation, server
  list parsing, scope, subject, the support rule of R1.
- `Infrastructure/FediverseServers.fs`: the per-server registration store.
- `Infrastructure/Providers/Fediverse.fs`: every HTTP call to a server (lookup,
  metadata, NodeInfo, registration, client check, token exchange, account), the
  guarded handler of R6.
- `Handlers/Connect/Fediverse.fs`: the account step (GET, POST) and the
  callback.
- `Handlers/Admin/FediverseConnector.fs`: the pane and its save handler.

The tail of `Callback.fs` (from "profile in hand": email-domain rules, resolve,
user events, disabled check, session, success event, redirect) becomes one
public function both callbacks call; Fediverse passes "no email rule".

**Rationale.** The adapter contract carries one client id, one secret and one
set of endpoints per connector. A fediverse sign-in has one per server, chosen
by the person at run time. Bending the contract would touch every adapter.

**Alternatives.** Extend `AuthorizeRequest`/`ExchangeRequest` with a server:
every adapter would carry a field only one kind reads.

## R5. Normalising a server address

**Decision.** One function for operator entries and typed input: trim, drop a
leading `@` or `https://` (and `http://` under the development setting only),
drop a trailing `/`, refuse a path, query or fragment, drop `:443`, refuse any
other port, lower-case, drop one trailing `.`, convert an internationalised
name to ASCII, then require a public-looking domain (at least two labels, valid
labels, non-numeric top label). IP literals and `localhost` are refused (under
the development setting, `localhost` and IP literals with a port are allowed so
a local fake server works). The domain rules reuse `EmailDomains`' label check,
made public.

**Rationale.** One spelling for the allowlist, the registration key and the
subject, so `Mastodon.Social` and `mastodon.social` are the same server and the
same people.

## R6. Calls to a server: SSRF, redirects, time limits

**Decision.** A second named HttpClient, `umbral-fediverse`, whose primary
handler is a `SocketsHttpHandler` with `AllowAutoRedirect = false`, a 5 second
connect timeout, and a `ConnectCallback` that resolves the host, keeps only
public addresses (refusing loopback, private, link-local, CGNAT, ULA,
multicast, unspecified, and IPv4-mapped forms of those) and connects to the
first kept address. Each call has its own 10 second cancellation, and every
body is read through a size bound (64 KiB). The handler comes from a registered
delegate `Fediverse.PrimaryHandler`, so tests put a stub server behind it, as
`ProviderAdapters.PrimaryHandler` does for the other providers.

The account step also checks the host before any request (literal IP,
`localhost`, or DNS to a non-public address) so the person gets the "not a
handle or a server" message (FR-008); the connect-time check is the real guard
and also defeats DNS rebinding.

The development setting is the existing one: `ASPNETCORE_ENVIRONMENT=Development`
and `UMBRAL__INSECURE_ALLOW_HTTP=true`. With it, non-public addresses and
`http` are allowed.

**Rationale.** The registration request is the one place a stranger chooses
where Umbral sends a POST. A check at connect time is the only one DNS cannot
dodge. A separate client keeps the guard off the existing providers, where an
operator's own OpenID Connect server may sit on the LAN.

**Alternatives.** The shared `umbral-provider` client: would block LAN OIDC
servers that work today.

## R7. Storing a server registration

**Decision.** Table `fediverse_servers`, one row per normalised host: client
id, client secret protected with Data Protection (purpose
`Umbral.FediverseServers.v1`, the local key ring in the data directory),
authorization and token endpoints as discovered, registered, checked and last
used times. Always local, whatever secret store the operator configured for
connector secrets.

Two first sign-ins racing: the insert is `ON CONFLICT(host) DO NOTHING`, and
both read the row back; the losing registration is left on the server unused.

**Rationale.** Principle II and IV: state in the data directory, secrets
encrypted. These secrets are created by Umbral, one per server; putting each in
a cloud secret manager would surprise the operator.

## R8. A server that forgot Umbral (FR-013)

**Decision.** Before redirecting, when the registration was not checked in the
last 24 hours, Umbral asks the token endpoint for a client-credentials token
(scope `read:accounts`). `invalid_client` (401/400 with that error) means the
registration is gone: register again once and replace the row. Any other
failure is "did not answer". A successful check stamps `checked_utc`. At the
callback, an `invalid_client` from the code exchange deletes the row so the
next attempt registers again; the person sees the generic sign-in failure.

**Rationale.** A Mastodon server shows its own error page for an unknown
client and never redirects back, so the check has to happen before the
redirect. Once a day keeps the cost to one extra request per server per day.

## R9. Correlation bound to the server (FR-016)

**Decision.** `OAuthRoundTrip.Correlation` gains `Server` (empty for every
other kind). The cookie payload becomes seven fields; `decode` still accepts
six (a cookie minted just before an upgrade). `start` takes the server.

## R10. Identity and profile

**Decision.** `linked_accounts`: kind `fediverse`, subject `<host>#<account id>`
(`fediverseSubject`). `ProviderProfile` gains `Username` and `ProfileUrl`.
`users` gains `preferred_username` and `profile_url` (schema version 7, both
null for every other user). The display name is the account's `display_name`,
or its `username` when that is empty; the avatar is `avatar`;
`preferred_username` is `<username>@<host>`; `profile` is the account's `url`
when it is `https`. Apps get `preferred_username` and `profile` under the
`profile` scope in the id token, the access token and userinfo, set or removed
like `picture`.

**Rationale.** Account ids are unique per server only. `username@server` is
what Mastodon itself shows. For a split-domain server this shows the server
host rather than the handle's domain; the docs say so.

## R11. Rules that read email

**Decision.** `EmailDomainRules.ruleFor` answers "no rule" for Fediverse, as for
Passkey, so the callback, the authorize re-check and the token re-check never
refuse a fediverse session for its missing email. On the Apps screen a
Fediverse connector is limited exactly when its scope is "Only these servers".
The instance domains removal warning skips it.

## R12. Security log

**Decision.** A new event type `fediverse_server_registered` (a credential
change, FR-023) with `server=<host>` in the detail. Sign-in failures are
`sign_in_failed` with `fediverse:<reason>;server=<host>`, reasons
`not_a_server`, `non_public_address`, `server_not_allowed`, `unsupported`,
`unreachable`, `registration_failed`, `client_rejected`, `exchange_failed`,
`account_unreadable`. No token, code or secret.

## R13. Pages and failures

**Decision.** `GET /connect/fediverse/start` renders the account step in the
picker's chrome (plain, or the app's branding) for the same `returnUrl`; the
POST re-renders it with status 422 for every problem the person can fix, with
the typed value, `aria-invalid`, the error under the field, and `autofocus` on
the field (focus with JavaScript off). Callback failures go through the
existing `Picker.redirectWithFailure` codes, like every provider
(`access-denied`, `sign-in-failed`, `account-not-allowed` for a server dropped
from the list). "Other ways to sign in" links to `/signin?returnUrl=…` when the
app offers more than one connector. Literal routes `/connect/fediverse/start`
and `/connect/fediverse/callback` outrank the generic `{slug}` routes.

## R14. Abuse

**Decision.** The account step POST, its GET and the callback use the
`sign-in` rate limit (30 a minute per address). Registration happens only after
the server passes R1, so a stranger cannot make Umbral POST to an arbitrary
URL, only to a Mastodon-compatible server's registration endpoint, at most 30
times a minute.
