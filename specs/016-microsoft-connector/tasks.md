# Tasks: Microsoft accounts (personal, work and school)

**Input**: [plan.md](plan.md), [microsoft-connector.spec.md](microsoft-connector.spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/routes-and-screens.md](contracts/routes-and-screens.md).

Paths are under `src/Umbral/` unless they start with `tests/`, `docs/`, `site/` or `changelog.d/`.

## Phase 1: Foundation

- [x] **T001** The `Microsoft` kind, `MicrosoftAudience` and `Connector.MicrosoftAudience` in `Domains/Connectors.fs`, matched everywhere the compiler asks (the pane a placeholder for now); `Domains/Microsoft.fs` with the data model's rules; `AccountRefused` in `ProviderAdapters.fs` and `Picker.failureCode`. Tests: `tests/Umbral.Tests/MicrosoftDomainTests.fs` (every rule of the data model's table, `Admits` for each audience against the personal tenant and two others, canonical-`tid` refusals: upper case, braces, no dashes, trailing space).
- [x] **T002** Schema 13 (`connectors.microsoft_audience`), `ConnectorStore` read and write, `saveMicrosoft`. Tests in `DatabaseTests.fs` (12 upgrades to 13; round trip of each audience; NULL loads as None).

## Phase 2: Protocol

- [ ] **T003** `OidcDiscovery.CheckAt` / `RefreshAt`; `Oidc.validateIdToken` split as the plan says. Existing OIDC and Apple tests unchanged and green; new discovery tests: a template-issuer document accepted at `CheckAt` with the template, refused when the expected issuer differs.
- [ ] **T004** `Providers/Microsoft.fs`, registered in `Program.fs`. `tests/Umbral.Tests/Infrastructure/FakeMicrosoft.fs` (on `StubOidcProvider`: discovery per segment with R3's issuers, a key set with `issuer` per key, a token endpoint issuing `id_token`s with chosen `tid`, `iss`, `oid`, `email`, `xms_edov`, key). `MicrosoftSignInTests.fs`: SC-001's list, plus the unknown-`kid` refresh, a missing `oid`, the `nonce`, and the security-log words.

## Phase 3: Re-check and screens

- [ ] **T005** `MicrosoftAccounts.recheckTenant`, called in `Authorize.fs` and `Token.fs` beside the Bluesky re-check. Tests: narrowing "both" to "personal" refuses a work-account session at the next authorize and keeps a personal one; widening admits the same user again.
- [ ] **T006** The pane: `Handlers/Admin/MicrosoftConnector.fs`, `Connectors.fs` (list entry after Apple, pane fields, save route, notices), `ConnectorToggle.fs` (Enable check), `Texts.fs`. Tests in `MicrosoftAdminTests.fs`: each refusal of the contract (422, values kept, secret not drawn back), save of each audience, the secret kept when blank, unreachable at Save → disabled with the notice, Enable check, list position, not in the add form.

## Phase 4: Docs, captures, reviews

- [x] **T007** Docs (FR-015).
- [ ] **T008** Captures for the design review (contract's list) in `.alta/reviews/2026-11-11-microsoft/`.
- [ ] **T009** Full suite green; design, documentation and code reviews; one re-check each.
