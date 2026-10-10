# Implementation Plan: Generic OpenID Connect Connector

**Branch**: `002-generic-oidc-connector` | **Date**: 2026-10-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-generic-oidc-connector/spec.md`

## Summary

Add a fourth connector kind, `oidc`, that an operator configures with an
issuer URL, a chosen short name, a client ID and a client secret. Everything
else comes from the provider's discovery document, read through
Microsoft.IdentityModel's `ConfigurationManager` and cached per issuer. The
`id_token` is signature-validated against the provider's published keys —
unlike Google's adapter — and a person is keyed by (issuer, `sub`), so two
providers never merge and a re-added provider reconnects its people.

It slots into spec 001's design rather than around it: one more
`IProviderAdapter`, one async `Resolve` step before the pure authorize-URL
builder, one nullable column, and the first real use of the schema version row
that 001 created and never read.

## Built on spec 003 (amendment, 2026-10-10)

Spec 003 (per-app connectors) is complete and was merged into this branch
before implementation (`6484e86`), because spec 004 records the owner's order
"003 first". 003 already provides several connectors per kind, an
operator-chosen immutable slug (`Ids.isValidSlug`, plus "not another kind's
name"), `POST /admin/connectors` to add a connector, an editable display name,
a per-connector `is_shared` flag, per-app connector choices, and schema
version 2. This plan therefore **extends** those rather than adding parallel
ones:

- **Slug**: 003's rule, unchanged (spec FR-003 amended). No separate validator.
- **Create**: 003's `create` handler in `Handlers/Admin/Connectors.fs` gains the
  `oidc` kind and an `issuer` field. There is no `OidcConnectorCreate.fs`. For
  `oidc`, a validation or discovery error **re-renders the add form with the
  operator's input** (spec FR-005) instead of 003's redirect-with-notice.
- **Screen**: an `oidc` connector is a card like any other (with its issuer
  read-only); the kind has no "empty card" — the add form offers
  "OpenID Connect provider" as a kind, with the issuer field and the FR-009
  warning.
- **Shared and per-app choice**: apply to `oidc` connectors as to every kind
  (default shared); no extra work beyond tests.
- **Schema**: the next version is **3**. 003's inline 1→2 upgrade becomes the
  first of an ordered list of steps; this feature adds step 3 (`issuer_url`)
  and the downgrade guard (research R8).

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`), unchanged

**Primary Dependencies**: as spec 001, plus
`Microsoft.IdentityModel.Protocols.OpenIdConnect` 8.19.2 (same family and
version as the `Microsoft.IdentityModel.*` assemblies OpenIddict already
brings in). Uses `ConfigurationManager<OpenIdConnectConfiguration>`,
`HttpDocumentRetriever` and `JsonWebTokenHandler`.

**Storage**: SQLite, unchanged engine. `connectors.issuer_url TEXT NULL` plus a
unique partial index; `schema_version` 1 → 2 with an in-place upgrade (research R8).

**Testing**: xunit v3 on Microsoft.Testing.Platform, TestServer, as spec 001.
A stub identity provider (RSA key, JWKS, discovery, token and userinfo
endpoints) built in the tests and served through a new provider-HTTP seam
(research R9). Test JWTs are minted with Microsoft.IdentityModel in tests only.

**Target Platform**: unchanged (`linux/amd64`, `linux/arm64` containers)

**Project Type**: web service with server-rendered admin UI, unchanged

**Performance Goals**: one discovery fetch per issuer per refresh interval;
none on the hot path once warm. Not a throughput concern.

**Constraints**: startup never contacts a provider (FR-021); idle RSS under
150 MB still holds (one small cache per configured issuer); no new runtime
dependency outside the container.

**Scale/Scope**: a handful of `oidc` connectors per instance. ~4 new source
files, ~8 touched; one admin section; one doc page.

## Constitution Check

*GATE: evaluated before Phase 0 and again after Phase 1 design.*

| Gate | Verdict | Evidence |
| --- | --- | --- |
| **Concept count** | **Pass** | An OpenID Connect provider is a Connector. The Connectors screen gains a section, not a fourth screen. |
| **Single artifact** | **Pass** | One NuGet package from a family already in the image. The identity provider is the operator's and is contacted at sign-in and save only; startup never depends on it. State stays in `/data`. |
| **Protocol safety** | **Pass** | Discovery, JWKS, key rotation and signature validation are Microsoft.IdentityModel's. Umbral compares `nonce` and `azp` (string equality). OpenIddict still issues every token Umbral hands to apps. |
| **Secure default** | **Pass** | https-only issuers (Clarification Q1); asymmetric signatures only; issuer mix-up check; PKCE whenever supported; secret required (Q3); unreachable-at-save is stored disabled, never silently enabled (Q2). |
| **Config parity** | **Pass** | Every field, including `issuer`, is in the YAML file and the export (contract). |
| **Documentation** | **Pass** | `docs/connectors/oidc.md`, README provider list, cross-links — tasks. |
| **Scope** | **Pass** | Federated *login* through one more provider. No claim mapping, authorization rules, SAML or LDAP (spec Assumptions). |

**Post-design re-check**: unchanged. Phase 1 added a nullable column and an
in-memory cache; no new noun, no new storage engine, no external service.

## What changes

### 1. Domain: a fourth kind and its rules
`Domains/Connectors.fs` gains `Oidc` (value `"oidc"`), a static
`endpointsFor Oidc` holding only provider-independent facts (scopes
`openid email profile`, `UsesNonce`, `SupportsPkce`; empty URLs), and
`Connector.IssuerUrl: string option`. New pure validators, shared by the admin
form and the config file: `validateSlug`, `normaliseIssuer` (absolute https,
no query/fragment, one trailing slash dropped, host lowercased, localhost
exception passed in as a flag). `parseKind` and `allKinds` updated; the picker
and the Connectors screen keep iterating built-in kinds and list `oidc`
connectors separately.

### 2. Discovery cache
New `Infrastructure/OidcDiscovery.fs`: a singleton holding one
`ConfigurationManager<OpenIdConnectConfiguration>` per normalised issuer, built
over the named provider `HttpClient`. `check: issuer -> Task<DiscoveryResult>`
returns `Ok config | Unreachable reason | Wrong reason` (research R2 — the split
drives Clarification Q2). Used by the admin save, the enable toggle, `Resolve`
and the token exchange.

### 3. Adapter seam and the OIDC adapter
`IProviderAdapter.Resolve` and `AuthorizeRequest.Endpoints` (research R3);
the three built-in adapters return their static endpoints. New
`Infrastructure/Providers/Oidc.fs`: authorize URL per the contract; token
request with basic/post client authentication (R5); `id_token` validated with
`JsonWebTokenHandler` (R4) using Umbral's `TimeProvider`; userinfo fallback
with subject check (R6); profile subject `<issuer>#<sub>` (R7). One `kid`-miss
refresh-and-retry. Registered in `Program.fs` with the other adapters.

### 4. Storage
`Database.fs`: `issuer_url` in the fresh schema; `SchemaVersion = 2`;
`initializeSchema` reads the version, upgrades 1 → 2 in its transaction,
refuses a higher version (R8). `ConnectorStore` reads/writes `issuer_url` and
offers `findByIssuer`. `UserStore`'s linked-account display maps `oidc` rows to
the matching connector's display name, else the issuer host.

### 5. Admin UI
`Handlers/Admin/Connectors.fs` renders `oidc` connectors as cards and offers
the kind on spec 003's add form (contract); spec 003's add handler at
`POST /admin/connectors` accepts `kind = oidc` with an `issuer` field; the
existing update route accepts `oidc` slugs without
slug/issuer fields; `ConnectorToggle.fs` runs the discovery check before
enabling an `oidc` connector. All messages live in `Views/Texts.fs` and name
the next action (spec 001 SC-010).

### 6. Configuration file
`ConfigDocument.fs` adds `issuer` with the validation in the contract;
`ConfigFile.applyConnectors` writes it and refuses an issuer change on an
existing slug; `ConfigExport.fs` emits it. No discovery at apply.

### 7. Test seam for provider HTTP
`ProviderAdapters.PrimaryHandler` registered with `TryAddSingleton`; the test
fixture can register a stub first (R9). Production behaviour unchanged.

### 8. Documentation
`docs/connectors/oidc.md` (FR-024: issuer locations for Authentik, Keycloak,
Pocket ID, Authelia; confidential client; basic/post; scopes; email-trust
warning; plain-http provider behind the reverse proxy; HS256; troubleshooting).
README provider list and links; "Other providers" lines in the three existing
walkthroughs; spec 001's data model gets a pointer to this one.

## Testing strategy

| Layer | What | Where |
| --- | --- | --- |
| Pure | slug rule, issuer normalisation, subject encoding/decoding | `OidcDomainTests.fs` |
| Adapter | authorize URL golden test; every refusal in the contract's "Accepted identity" table; basic vs post; PKCE omission; `"true"` string is unverified; key rotation via `kid` refresh | `OidcAdapterTests.fs` with the stub provider |
| Discovery | unreachable vs wrong classification; issuer mismatch; http refused; localhost exception only in insecure-dev | `OidcDiscoveryTests.fs` |
| End to end | add via form → picker → full login → tokens with `idp = slug`; two issuers same `sub` → two users; delete + re-add → same `sub`; unreachable at save → disabled; enable re-checks | `OidcConnectorTests.fs` on TestServer |
| Config | apply idempotent; issuer required/forbidden; issuer change refused; export round trip | `ConfigFileTests.fs` additions |
| Storage | 001 database upgraded in place; higher version refused | `DatabaseTests.fs` additions |
| Existing | MetaTests (new endpoint listed), CsrfTests (new POST covered automatically), SecurityEventCoverageTests, LogRedactionTests (no token in logs through an OIDC login) | unchanged files, must stay green |

## Project Structure

### Documentation (this feature)

```text
specs/002-generic-oidc-connector/
├── plan.md              # This file
├── research.md          # Phase 0: R1–R11
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   └── oidc-connector.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/Umbral/
├── Domains/Connectors.fs                     # Oidc kind, IssuerUrl, slug/issuer rules
├── Domains/ConfigDocument.fs                 # issuer field + validation
├── Infrastructure/
│   ├── Database.fs                           # issuer_url, schema v2 upgrade, version guard
│   ├── ProviderAdapters.fs                   # Resolve, AuthorizeRequest.Endpoints, PrimaryHandler seam
│   ├── OidcDiscovery.fs                      # NEW: per-issuer ConfigurationManager cache
│   ├── Providers/Oidc.fs                     # NEW: the adapter
│   ├── Providers/{Discord,GitHub,Google,Fake}.fs  # Resolve returns static endpoints
│   ├── ConnectorStore.fs                     # issuer_url, findByIssuer
│   ├── UserStore.fs                          # oidc linked-account display
│   └── ConfigFile.fs                         # apply/refuse issuer change
├── Handlers/Admin/
│   ├── Connectors.fs                         # oidc cards, add-form kind + issuer, discovery on create
│   ├── ConnectorToggle.fs                    # check before enabling oidc
│   └── ConfigExport.fs                       # emit issuer
├── Handlers/Connect/Start.fs                 # Resolve before BuildAuthorizeUrl
├── Views/Texts.fs                            # every new message
├── Program.fs                                # register adapter, cache, seam, endpoint
└── Umbral.fsproj                             # new files + package

tests/Umbral.Tests/
├── Infrastructure/StubOidcProvider.fs        # NEW: RSA key, JWKS, discovery, token, userinfo
├── OidcDomainTests.fs                        # NEW
├── OidcDiscoveryTests.fs                     # NEW
├── OidcAdapterTests.fs                       # NEW
├── OidcConnectorTests.fs                     # NEW (end to end)
├── ConfigFileTests.fs · DatabaseTests.fs · ProviderAdapterTests.fs   # additions/adjustments

docs/connectors/oidc.md                       # NEW
README.md                                     # provider list, links
```

**Structure Decision**: the existing single-project layout from spec 001
(`src/Umbral`, `tests/Umbral.Tests`, vertical slices under `Handlers/`).
New files follow the compile-order rule: domain → infrastructure → views →
handlers → `Program.fs`.

## Complexity Tracking

No constitution violations to justify.

## Risks

| Risk | Mitigation |
| --- | --- |
| A provider's discovery deviates from the spec (trailing-slash issuer, missing `token_endpoint_auth_methods_supported`) | Slash-insensitive issuer comparison; documented defaults for absent fields (R2, R5); SC-002 requires four real products before release. |
| Clock skew between Umbral and a homelab provider rejects fresh tokens | 2-minute skew; `expired` reason recorded so the operator can see it. |
| The `Resolve` change breaks a built-in adapter | Built-ins return their static endpoints; their golden URL tests and the full-login tests must stay byte-identical. |
| First schema upgrade on deployed instances | `ADD COLUMN` NULL is O(1); tested on a real 001-created database file; higher-version guard prevents a downgrade from corrupting. |
| Shared provider `HttpClient` timeouts slow the sign-in page | 10 s timeout as today; the discovery cache means one fetch per issuer per refresh, not per sign-in. |
