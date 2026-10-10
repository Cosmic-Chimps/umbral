# Quickstart: validating the OpenID Connect connector

**Feature**: `002-generic-oidc-connector`

## Prerequisites

- Spec 001's prerequisites (.NET 10 SDK; Docker for the container scenarios).
- For scenarios 1 and 4: a real identity provider. The quickest is Pocket ID or
  Authentik in Docker on the same machine, behind the compose file's Caddy so it
  is https. Creating its application is excluded from the timing.
- Everything else runs offline against a stub provider in the test suite.

## Running the suite

```bash
dotnet test Umbral.slnx
dotnet test Umbral.slnx --filter-class "*OidcAdapterTests*"
dotnet test Umbral.slnx --filter-class "*OidcConnectorTests*"
```

The stub provider is built in the tests: an RSA key, its JWKS, a discovery
document and a token endpoint, served through the provider-HTTP seam
(research R9). No network.

---

## Scenario 1 — First sign-in through your own provider *(SC-001, User Story 1)*

Time it from opening the Connectors screen to the app landing signed in; stop
the clock while in the provider's console.

1. In the provider, create an OAuth2/OpenID application: **confidential**,
   redirect URI left for later. Note its **issuer**, client ID and secret.
2. Umbral → Connectors → **Add another connector**: Provider "OpenID Connect
   provider", Identifier `home`, Button label `Home`, the Issuer URL, client
   ID, client secret. Save.
3. Copy the callback URL shown (`<origin>/connect/home/callback`) into the
   provider's redirect URI field.
4. Start a sign-in at a registered app; choose **Sign in with Home**.

**Expect**: the app receives Umbral's tokens with `idp = oidc` (the kind, per spec 003); the Users
screen shows one user whose linked account reads "Home". A second sign-in: same
`sub`, still one user.

**Expect on a typo**: an issuer with a missing path segment is refused at save
with the "describes itself as …" message; nothing is saved.

## Scenario 2 — Two providers, one subject *(SC-005, User Story 2)*

Automated (`OidcConnectorTests`): two stub providers with different issuers
both return `sub = "1"`.

**Expect**: two users. Delete connector A, re-add it with the same issuer under
a different short name, sign in again → the original user's `sub`.

## Scenario 3 — Forged and mismatched identities *(SC-003)*

Automated (`OidcAdapterTests`): for each row of the "Accepted identity" table in
[contracts/oidc-connector.md](./contracts/oidc-connector.md) — wrong issuer,
wrong audience, expired, signed by another key, `alg: none`, HS256, nonce
mismatch, userinfo subject mismatch — plus a discovery document naming a
different issuer.

**Expect**: every case refused, no session cookie set, a `sign_in_failed` event
with the listed reason and no token in `detail`.

## Scenario 4 — Unreachable provider *(SC-004, SC-006, Clarification Q2)*

1. Stop the identity provider.
2. Restart Umbral → it starts; other connectors sign in normally.
3. Add a new `oidc` connector pointing at the stopped provider → saved
   **disabled** with the "could not reach" warning; it is not on the sign-in page.
4. Try to enable it → refused with the same reason.
5. Start the provider; enable → succeeds; sign-in works.

## Scenario 5 — Configuration file round trip *(SC-007, User Story 3)*

1. Write an `umbral.yaml` with one `kind: oidc` entry and set its secret
   variable; start a clean instance → the connector exists; restart → nothing
   changes (no `connector_updated` event).
2. Change the entry's `issuer` → startup refused, naming the entry.
3. Export from `/admin/config` → the entry round-trips into a second clean
   instance.

## Scenario 6 — Upgrading a 001 database *(research R8)*

Automated: a data directory created by the 001 schema (version 1, a GitHub
connector, a linked user) starts under the new build → `schema_version = 2`,
rows intact, an `oidc` connector can be added. A database stamped `3` refuses to
start, naming both versions.
