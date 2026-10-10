# Research: Generic OpenID Connect Connector

**Feature**: `002-generic-oidc-connector` | **Date**: 2026-10-10 | **Plan**: [plan.md](./plan.md)

Spec 001's research ([D1](../001-umbral-mvp/research.md), D4, D7, D12, D14)
still holds; this file records only what is new. Each decision lists what was
chosen, why, and what was rejected.

---

## R1 — One new connector kind, configured by issuer alone

**Decision**: add `Oidc` to `ProviderKind` (stored as `"oidc"`). A connector of
that kind carries an **issuer URL**; everything else Umbral needs — endpoints,
signing keys, supported features — is read from
`{issuer}/.well-known/openid-configuration`. No endpoint, claim name or
algorithm is operator-editable.

**Rationale**: spec 001's data model deliberately left endpoint URLs out of the
`connectors` table ("editable endpoints are surface area that only produces
support tickets") and named `Oidc of issuerUrl` as the additive way to open it
up. Discovery is mandatory in every product the spec lists (Authentik,
Keycloak, Pocket ID, Authelia, Zitadel, Kanidm, GitLab, Entra), so one URL is
enough, and an operator copies it from a field their provider labels "Issuer".

**Alternatives considered**:

| Alternative | Why rejected |
| --- | --- |
| Editable authorize/token/userinfo URLs (generic OAuth 2.0) | Covers non-OIDC providers, but every field is a misconfiguration waiting to happen, and without an `id_token` there is no standard identity to validate. That is the authentik/Ory surface the constitution exists to avoid. |
| One compiled-in kind per product (Authentik, Keycloak, …) | Eight kinds that differ only in a URL, and the list never ends. |
| `OpenIddict.Client` with dynamic registrations | Rejected in spec 001 D1 for the same reasons, which still hold: dynamic registration is unsupported by its maintainer, and it couples the social half to the token-issuing half. |

---

## R2 — Discovery and signing keys through Microsoft.IdentityModel

**Decision**: use `Microsoft.IdentityModel.Protocols.OpenIdConnect` 8.19.2 —
same family and version as the `Microsoft.IdentityModel.*` 8.19.2 assemblies
OpenIddict already brings in (`dotnet list package --include-transitive`) —
specifically `ConfigurationManager<OpenIdConnectConfiguration>` with an
`HttpDocumentRetriever` built over Umbral's named provider `HttpClient`
(redirects off, 10 s timeout, bounded body as the other adapters).

- **One manager per issuer**, held in a singleton `OidcDiscovery` cache keyed by
  the normalised issuer. It refreshes on its own schedule (12 h automatic, 5 min
  minimum between forced refreshes), so key rotation needs no operator action
  (FR-013). On a signature failure with an unknown `kid`, the adapter calls
  `RequestRefresh()` once and retries validation.
- **`RequireHttps = true`**, except for an issuer whose host is `localhost`,
  `127.0.0.1` or `[::1]` when the instance itself runs with the Development +
  `UMBRAL__INSECURE_ALLOW_HTTP` escape hatch (Clarification Q1).
- **Lazy at sign-in, eager at save** (FR-005, FR-021). Startup never fetches. The
  admin save calls the same cache, so a successful save also warms it.

**Checks applied to every fetched document** (failures map to FR-005's
messages): `issuer` equals the configured issuer after trailing-slash
normalisation (mix-up defence; OIDC Discovery §4.3); `authorization_endpoint`,
`token_endpoint` and `jwks_uri` present and https (same localhost exception);
`response_types_supported`, when present, contains `code`;
`id_token_signing_alg_values_supported`, when present, contains at least one
asymmetric algorithm (R4); `token_endpoint_auth_methods_supported`, when
present, contains `client_secret_basic` or `client_secret_post` (R5).

**Unreachable vs. wrong** (Clarification Q2): a network failure, timeout,
non-2xx, or a body that is not a JSON object is *unreachable* → save disabled
with a warning. A reachable document that fails a check is *wrong* → refuse.

**Alternatives considered**: hand-parsing the discovery JSON and the JWKS with
`System.Text.Json` (no new package, but key rotation, `kid` matching and
refresh throttling become ours to get right — exactly what Principle III says
not to write); fetching discovery once at save and storing the endpoints in the
row (no runtime fetch, but key rotation would need a re-save, and stored
endpoints drift silently from the provider's truth).

---

## R3 — The adapter seam: resolve endpoints before building the URL

**Decision**: `IProviderAdapter` gains one async member,
`Resolve: Connector -> Task<Result<ProviderEndpoints, ConnectorError>>`, and
`AuthorizeRequest` gains an `Endpoints: ProviderEndpoints` field filled from it.
`commonAuthorizeParameters` reads `request.Endpoints` instead of
`request.Connector.Endpoints`.

- Built-in adapters implement `Resolve` as `Task.FromResult(Ok (endpointsFor kind))`
  — no behaviour change; their golden URL tests change only in how the request
  is constructed.
- `BuildAuthorizeUrl` stays pure: everything it needs is in the request.
- `Start.fs` calls `Resolve` before `BuildAuthorizeUrl`; an `Error` becomes the
  usual end-user failure notice plus a `sign_in_failed` event with the cause.
- `ExchangeAndFetch` is unchanged in signature; the OIDC adapter reads the
  (cached) configuration itself for the token endpoint, JWKS and userinfo.
- `ProviderKind.Oidc` gets a static `endpointsFor` entry holding only what does
  not depend on the provider — scopes `openid email profile`, `UsesNonce = true`,
  `SupportsPkce = true` — with empty URLs that `Resolve` replaces. A test
  asserts no code path builds an authorize URL from the static entry.

**Alternatives considered**: making `BuildAuthorizeUrl` async (loses the purity
the golden tests rely on, and changes four adapters' contracts for one); a
separate resolver service outside the adapter (two places that know about
providers instead of one).

---

## R4 — Identity tokens: signature validated, shared-secret signing refused

**Decision**: validate the `id_token` with `JsonWebTokenHandler.ValidateTokenAsync`
and these parameters: `ValidIssuer` = configured issuer (both slash forms
accepted, R2), `ValidAudience` = the connector's client ID,
`IssuerSigningKeys` = the discovery JWKS, `ValidAlgorithms` = the asymmetric
algorithms (`RS256/384/512`, `PS256/384/512`, `ES256/384/512`) — so `none` and
`HS*` are refused (FR-012) — `RequireExpirationTime = true`, clock skew 2 min,
lifetime evaluated against Umbral's `TimeProvider` so tests can drive expiry.
Then, in Umbral code: `nonce` equals the round-trip nonce; when `aud` has more
than one value, `azp` equals the client ID.

**Rationale**: Google's adapter skips signature validation because the token
comes straight from Google's token endpoint over TLS (OIDC Core §3.1.3.7 allows
that). For an arbitrary, possibly self-hosted provider the same argument holds
in principle, but validating costs nothing once the JWKS is cached, and it
turns a misrouted or proxied token endpoint into a refusal instead of a trusted
identity. The library does the cryptography; Umbral writes no JWT code
(Principle III).

**HS256 (client-secret-signed) tokens**: legal in OIDC, refused here. Every
product the spec names signs with published asymmetric keys by default; the
walkthrough tells an operator where to switch if theirs does not. Revisit only
if a real provider requires it.

---

## R5 — Token request: client authentication and PKCE

**Decision**:

- **Client authentication**: `client_secret_basic` when
  `token_endpoint_auth_methods_supported` is absent (its spec default) or lists
  it; else `client_secret_post` when listed; otherwise refuse at save with the
  capability named. The client secret is always required (Clarification Q3).
- **PKCE**: `S256` always, unless `code_challenge_methods_supported` is present
  and lacks `S256`, in which case PKCE is omitted (state and nonce still
  apply). An unknown parameter is ignored by a conforming authorization server
  (RFC 6749 §3.1), so sending PKCE to a provider that does not advertise it is
  safe.
- **Scopes**: `openid email profile` plus the connector's `extra_scopes`.
- Bounded response read and 10 s timeout, as the other adapters.

---

## R6 — Profile: id_token first, userinfo to fill gaps

**Decision**: `sub`, `email`, `email_verified`, `name` (falling back to
`preferred_username`), `picture` from the validated `id_token`. When email or
name is missing and the document advertises `userinfo_endpoint`, call it with
the access token; its `sub` must equal the `id_token`'s or the sign-in is
refused (FR-014, OIDC Core §5.3.2). `email_verified` counts only when it is the
JSON literal `true` — a string `"true"` is unverified (FR-018).

---

## R7 — Identity key: issuer and subject, independent of the slug

**Decision**: `linked_accounts` keeps `PRIMARY KEY (provider_kind, provider_subject)`.
For `oidc`, `provider_kind = "oidc"` and
`provider_subject = "<normalised issuer>#<sub>"`.

**Rationale**: OIDC Core §2 makes (`iss`, `sub`) the identity; a `sub` alone is
only unique within one issuer, and many products issue small integers or UUIDs
that can repeat across instances (FR-016). Keying on the **issuer**, not the
slug, means deleting a connector and re-adding it — under any slug — finds the
same people (FR-017). `#` cannot occur in an issuer (OIDC Discovery §3:
no fragment), so the split is unambiguous. The normalised issuer drops one
trailing slash, so `https://a/` and `https://a` are one identity.

**Alternatives considered**: `provider_kind = "oidc:<issuer>"` (the kind column
stops being a closed set, which the Users screen and every query that switches
on it would have to learn); a new `provider_issuer` column in the PK (a table
rebuild in SQLite for no gain over the encoding).

The Users screen shows such a linked account as the connector's display name
when a connector with that issuer exists, else as the issuer's host.

---

## R8 — Schema change on a deployed database

**Decision**: `Database.SchemaVersion` goes from 1 to 2. `initializeSchema`
reads `settings.schema_version` and, inside the same transaction, applies each
missing step in order. Step 2:

```sql
ALTER TABLE connectors ADD COLUMN issuer_url TEXT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_connectors_issuer ON connectors (issuer_url) WHERE issuer_url IS NOT NULL;
```

then sets `schema_version = 2`. A fresh database gets the column in its
`CREATE TABLE` and is stamped 2 directly. A database stamped **higher** than
the build knows refuses to start, naming both versions and saying to run the
newer image or restore the backup taken before the upgrade.

**Rationale**: spec 001 created the version row precisely for this, but never
needed an upgrade path; this is the first. Its doc comment ("so a downgrade
fails loudly") describes a check that nothing performs today — no code reads
`schema_version` — so this step adds the read as well as the upgrade. `ADD COLUMN` with a NULL default is
an O(1) metadata change in SQLite and safe on every 001 instance. The unique
index means two connectors cannot claim one issuer: they would share every
person (R7) under two names, which is confusion with no use.

**Test**: a database file created by the 001 schema (version 1, one connector,
one linked account) is upgraded in place, keeps its rows, and accepts an `oidc`
connector afterwards.

---

## R9 — A seam for stubbing provider HTTP in end-to-end tests

**Decision**: `Program.configureServices` registers the provider client's
primary handler through a `ProviderAdapters.PrimaryHandler` factory added with
`TryAddSingleton`; the test fixture registers a stub first. Production keeps
`HttpClientHandler(AllowAutoRedirect = false)`.

**Rationale**: spec 001's end-to-end tests sign in through the Fake connector,
so no seam existed for a real adapter inside the hosted app. The OIDC user
stories need one (Story 1 independent test). Per the fixture's rule, the seam
goes in the product, not a mock.

---

## R10 — Saving, enabling and the admin screen

**Decision**:

- The Connectors screen keeps its three built-in cards and adds an **OpenID
  Connect providers** section: one card per `oidc` connector (same actions as a
  built-in card: replace secret, enable/disable, delete), and an **Add** form
  (display name, short name, issuer URL, client ID, client secret, extra
  scopes) carrying the FR-009 email-trust warning.
- **Save** runs R2's checks. *Wrong* → re-render with input kept and the reason.
  *Unreachable* → save with `is_enabled = 0` and a warning notice
  (Clarification Q2). *OK* → save enabled.
- **Enable** (existing toggle route) runs the same check first for an `oidc`
  connector and refuses with the reason if it fails or is unreachable.
  Disabling never checks.
- Short name and issuer are shown read-only after creation with the sentence
  from FR-004.
- Short-name rule: `^[a-z0-9](?:[a-z0-9-]{0,30}[a-z0-9])?$` with length ≥ 2,
  not `discord|github|google|fake|oidc`, unique (PK).

---

## R11 — Configuration file

**Decision**: a connector entry gains `issuer:`, required when `kind: oidc` and
an error otherwise. Whole-document validation adds: slug rule (R10), issuer an
absolute https URL with no query or fragment (localhost exception as R2), no
two entries with the same issuer. Apply never fetches discovery (FR-021); a
file-declared provider that is wrong at runtime fails sign-ins with the usual
notice and a `sign_in_failed` event naming the cause. Export writes `issuer:`
for `oidc` entries.

---

## Constitution Check answers (for plan.md)

| Gate | Answer |
| --- | --- |
| Concept count | No new noun: an OpenID Connect provider is a Connector. |
| Single artifact | No new runtime dependency outside the container; one NuGet package from a family already in the image. The identity provider is the operator's, reached at sign-in time only, and its absence never blocks startup. |
| Protocol safety | Signature, key and discovery handling are Microsoft.IdentityModel's; Umbral compares `nonce` and `azp`, which are string equality, not cryptography. |
| Secure default | https only; signature validated; asymmetric algorithms only; issuer mix-up check; PKCE whenever supported; secret required. |
| Config parity | Every field is in the YAML file and the export. |
| Documentation | `docs/connectors/oidc.md` and README changes are tasks. |
| Scope | Not on the exclusion list: social/federated **login** through one more provider. No claim mapping, no authorization rules, no SAML/LDAP. |
