# Tasks: Declare apps and the passkey connector at start

**Input**: [plan.md](plan.md), [declared-apps.spec.md](declared-apps.spec.md), [data-model.md](data-model.md), [contracts/](contracts/).

Paths are under `src/Umbral/` unless they start with `tests/`, `docs/`, `samples/`, `src/CosmicChimps...` or `changelog.d/`.

The build starts only once the code reviewer has cleared D1 to D11 and the designer has answered `contracts/screens.md`.

## Phase 1: Reading and checking

- [ ] **T001** `Domains/Declaration.fs`: types and `parse` (JSON shape, version, unknown properties, keys, names, types, URI lists with `RedirectUris`, duplicates and cap, connectors value, secrets present/absent/length, stray secret variables), problem lines as in the contract. Tests: one per refusal line; a valid document parses; order of problems stable.

## Phase 2: Storage and apply

- [ ] **T002** `declared_items`, step 16, `SchemaVersion = 16`; `...In` store variants; `Infrastructure/Declarations.fs` (`conflicts`, `apply` in one EF transaction, un-declare); the step in `Program.initialize` with the refusal and the start line. Tests: `DatabaseTests` 15 → 16; R2 rollback (a failure after the app write leaves no app and no event); create; second start writes nothing (no event, no row change); each field change is one `app_updated` with its `fields=`; secret change rotates (`app_secret_rotated declared`), old secret refused at the token endpoint, new one accepted; web → spa and spa → web; un-declare (named no more; variable unset); re-declare; app conflict and passkey conflict stop the start and write nothing; passkey created, enabled, shared, enrolment, changed enrolment; unknown connector slug warns and saves; no event detail or log line holds a secret (capture the log); a sign-in through a declared app with the passkey connector end to end.

## Phase 3: Admin area

- [ ] **T003** The Declared mark and owned fields per the designer's contract; refusals in `Apps.setConnectors`, `AppRedirectUris`, `AppSecret`, `AppDelete`, `PasskeyConnector.save`, `ConnectorToggle`, `ConnectorDelete` (`app-declared`, `connector-declared`); branding and invitations still work; `AppDelete` of an un-declared app removes its history row. Tests: each refused route changes nothing; branding saves; mark drawn only while declared.

## Phase 4: Aspire package and sample

- [ ] **T004** Package API per `contracts/package.md`, with tests (environment of Umbral and of the project, JSON shape, secret parameter generated/persisted/secret and referenced, publish-mode refusal, key rule and duplicates, no variables without `AddApp`/`WithPasskeys`, manifest has no secret value).
- [ ] **T005** `samples/aspire`: declare the sample and passkeys; steps reduced; README. Walk: fresh volume → passkey sign-up and sign-in in the sample with no admin click; second run → no new event (read `security_events`), no duplicate; change the redirect path in the AppHost → one `app_updated`; grep the Umbral log, the AppHost console and the manifest for the secret value.

## Phase 5: Docs, constitution, reviews

- [ ] **T006** Constitution 3.2.0 (Principle V paragraph, sync report); docs (FR-013); changelog fragment with the Upgrade note (schema 16; a declaration is off unless given).
- [ ] **T007** Captures of the Declared marking in `.alta/reviews/2026-11-14-declared-apps/` with `notes.md`.
- [ ] **T008** Full suite green; code, design and documentation reviews; one re-check each.
