# Implementation Plan: The Fediverse connector

**Spec**: [fediverse-connector.spec.md](./fediverse-connector.spec.md) · **Research**: [research.md](./research.md) · **Data model**: [data-model.md](./data-model.md) · **Contract**: [contracts/routes-and-screens.md](./contracts/routes-and-screens.md)

> **Scale note.** About 30 files across five areas: the domain (a new kind and
> a pure module), storage (schema version 7), a new HTTP client with an SSRF
> guard, the sign-in handlers (account step, callback, a shared tail factored
> out of `Callback.fs`), and the Connectors pane. Watch three things: the
> exhaustive matches on `ProviderKind` the compiler will flag, the email-domain
> re-checks at authorize and token (they must not refuse a fediverse session),
> and the Connector record literals in tests that gain a field.

## Summary

A built-in connector kind `fediverse` lets a person sign in with a Mastodon
4.3+ or GoToSocial account. The person types a handle or a server on an account
step page. Umbral finds the server (WebFinger for a handle), checks it is public
and allowed, checks it supports Mastodon's app registration and PKCE, registers
itself there on first use (stored per server, secret encrypted with the local
key ring), runs the authorization code flow with PKCE through the existing
correlation cookie (now carrying the server), reads the account, and signs the
person in through the same tail as every other provider. The operator enables
it from a pane on the Connectors screen and chooses Any server or Only these
servers. No new dependency: `SocketsHttpHandler.ConnectCallback` gives the
connect-time address check.

## Project Structure

```text
src/Umbral/
  Domains/Connectors.fs            ProviderKind.Fediverse; FediverseAccess on Connector
  Domains/EmailDomains.fs          label/domain check made public for reuse
  Domains/Fediverse.fs             NEW pure: parseInput, normaliseServer, parseServers,
                                   scope, isPublicAddress, support rule, subject
  Infrastructure/Database.fs       schema v7: fediverse_servers, connector scope columns,
                                   users.preferred_username/profile_url
  Infrastructure/ConnectorStore.fs read/write fediverse_scope, fediverse_servers; setFediverseAccess
  Infrastructure/FediverseServers.fs   NEW registration store (protect/unprotect secret)
  Infrastructure/Providers/Fediverse.fs NEW HTTP: lookup, discover, register, checkClient,
                                   exchange, account; guarded handler + PrimaryHandler seam
  Infrastructure/ProviderAdapters.fs  ProviderProfile.Username, ProfileUrl
  Infrastructure/OAuthRoundTrip.fs Correlation.Server (7 fields, 6 still decode)
  Infrastructure/UserStore.fs, Users.fs  preferred_username, profile_url
  Infrastructure/EmailDomainRules.fs  NoRule for Fediverse
  Infrastructure/SecurityEvents.fs FediverseServerRegistered
  Infrastructure/OpenIddictSetup.fs  claims_supported += profile
  Program.fs                       umbral-fediverse client, endpoints
  Views/Texts.fs                   catalogue strings (contract section 3)
  Handlers/Connect/Callback.fs     public finishSignIn (shared tail)
  Handlers/Connect/Fediverse.fs    NEW account step GET/POST, callback
  Handlers/Connect/Authorize.fs, UserInfo.fs  preferred_username, profile claims
  Handlers/Admin/FediverseConnector.fs  NEW pane + save
  Handlers/Admin/Connectors.fs     list order, pane dispatch, mayCreate, removal warning
  Handlers/Admin/Apps.fs           Fediverse limited iff Only these servers
  Assets/umbral.input.css, umbral.css  only if a class is added
tests/Umbral.Tests/
  Infrastructure/StubMastodon.fs   NEW fake server(s), modes
  FediverseDomainTests.fs          NEW parsing, normalising, address checks, support rule
  FediverseSignInTests.fs          NEW end-to-end through the real endpoints
  FediverseAdminTests.fs           NEW pane, save, allowlist, order
  ConnectorsMasterDetailTests.fs   list order includes fediverse
docs/connectors/fediverse.md (NEW), README.md, CHANGELOG.md
```

**Structure Decision.** Fediverse sits beside the adapters rather than behind
`IProviderAdapter` (research R4); it reuses `OAuthRoundTrip`, the callback's
sign-in tail, the admin frame and `Components` unchanged.

## Constitution Check

Checked before research and again after design; both pass.

| Gate | Assessment |
| --- | --- |
| I. Concept count | PASS. Fediverse is a Connector kind; a server registration is not shown to the operator and is not a concept. |
| II. Single artifact | PASS. One SQLite table and three columns in the data directory; no new dependency; no background work. Idle memory measured (SC-007). |
| III. Protocol safety | PASS. No token is built or verified: Umbral is an OAuth client here, as for GitHub. OpenIddict still issues everything apps see. PKCE and state come from the shared `OAuthRoundTrip`. |
| IV. Secure default | PASS. PKCE required, single-use state bound to the server, exact redirect, SSRF guard at connect time, no redirects, time and size limits, secrets encrypted, rate limits, security log. Default scope is Any server, which is the task's decision; the operator narrows it. |
| VI. Documentation | PASS. `docs/connectors/fediverse.md`, README, CHANGELOG are tasks; every operator error names the next action. |
| VII. Scope | PASS. Sign-in only; the server list decides who can be a user, nothing else. |
