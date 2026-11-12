# Implementation Plan: Microsoft accounts (personal, work and school)

**Spec**: [microsoft-connector.spec.md](microsoft-connector.spec.md) · **Research**: [research.md](research.md) · **Data model**: [data-model.md](data-model.md) · **Contract**: [contracts/routes-and-screens.md](contracts/routes-and-screens.md) · **Step 1**: `.alta/reviews/2026-11-11-microsoft/notes.md`

## Summary

A built-in connector kind `microsoft`, one per instance, shaped like Apple on the
admin side (its own pane and save route) and like the OIDC connector on the
protocol side (discovery, code exchange, `id_token` validated by
Microsoft.IdentityModel). What is Microsoft's own: the four audiences and the
discovery issuer each must name; the expected `iss` built from the token's
canonical `tid`; the signing key's `issuer`; the tenant rule; the `tid#oid`
identity; `email_verified` only from `xms_edov`; and the re-check of the tenant
at authorize.

## Technical Context

F# on .NET 10, Oxpecker, SQLite, xUnit with the in-process test server and stub
upstreams behind `ProviderAdapters.PrimaryHandler` (as `FakeApple`). No new
package: `Microsoft.IdentityModel.*` 8.19.2 is already there.

## Constitution Check

| Gate | Assessment |
|---|---|
| I. Three concepts | PASS. A Connector kind. A tenant is a setting of it, not a noun. |
| II. One artifact | PASS. One column (schema 13), no package, nothing at startup: discovery is lazy, as for every OIDC provider. |
| III. Standards | PASS. Signature, audience, lifetime, algorithm and the exact `iss` comparison are Microsoft.IdentityModel's. Umbral builds the one expected issuer string and compares the key's `issuer` and the tenant set: string equality, as `Oidc.fs` does for `nonce` and `azp`. |
| IV. Secure by default | PASS. Exact issuer per audience at discovery, the issuer–tenant–key chain Microsoft documents, PKCE, `nonce`, `state`, no unverified email, every refusal logged, secret in the store. |
| VI. Documentation | PASS. A walkthrough, the OIDC page's advice, the navigation lines, README, sidebar, changelog file, backup page's schema version. |
| VII. Default answer is no | PASS. Sign-in only; "who may sign in" decides who can be a user. No groups, roles or Graph. |

## Structure

| File | Change |
| --- | --- |
| `Domains/Connectors.fs` | `Microsoft` kind; `MicrosoftAudience` type; `Connector.MicrosoftAudience`; endpoints; ordering |
| `Domains/Microsoft.fs` (new) | the pure rules of the data model |
| `Infrastructure/Database.fs` | schema 13 |
| `Infrastructure/ConnectorStore.fs` | the column; `saveMicrosoft` (like `saveApple`) |
| `Infrastructure/ProviderAdapters.fs` | `AccountRefused` |
| `Infrastructure/OidcDiscovery.fs` | `CheckAt(documentBase, expectedIssuer)` and `RefreshAt`, `Check`/`Refresh` becoming the case where both are the issuer |
| `Infrastructure/Providers/Oidc.fs` | `validateIdToken` takes the issuer to expect apart from the one to refresh, and optional extra checks; the OIDC and Apple calls unchanged in effect |
| `Infrastructure/Providers/Microsoft.fs` (new) | `MicrosoftAdapter` |
| `Infrastructure/MicrosoftAccounts.fs` (new) | `recheckTenant` |
| `Handlers/Connect/Authorize.fs`, `Token.fs` | the re-check beside the Bluesky one |
| `Handlers/Connect/Picker.fs` | `AccountRefused` → `account-not-allowed` |
| `Handlers/Admin/MicrosoftConnector.fs` (new) | parse, draft, fields of the pane |
| `Handlers/Admin/Connectors.fs`, `ConnectorToggle.fs` | list entry, pane, save route, Enable check, notices |
| `Views/Texts.fs` | the contract's words |
| `Program.fs`, `Umbral.fsproj` | registration, routes, compile order |
| tests | `MicrosoftDomainTests`, `FakeMicrosoft`, `MicrosoftSignInTests`, `MicrosoftAdminTests`, `MicrosoftRecheckTests`, `DatabaseTests`, discovery tests |
| docs | `docs/connectors/microsoft.md`, `oidc.md`, nav lines, README, `site/sidebar.mjs`, `changelog.d/016-microsoft-connector.md`, backup page |

## Validation order inside the adapter

1. Discovery for the stored audience (cached), issuer exact.
2. Code exchange; `id_token` required.
3. Parse the token (no trust); `alg` asymmetric; `tid` canonical GUID, else `tenant`.
4. Keys: known `kid` or one throttled `RefreshAt`.
5. Library validation with `ValidIssuers = [ issuerFor tid ]`, audience, keys, lifetime.
6. Key issuer: `issuer` of the JWK with the token's `kid`, `{tenantid}` → `tid`, equal to `iss`; else `key_issuer`.
7. `nonce`, `azp`.
8. `oid` canonical GUID, else `subject`.
9. `audience.Admits tid`, else `AccountRefused`.
10. Profile: subject `tid#oid`, email, `email_verified` per FR-009, name.
