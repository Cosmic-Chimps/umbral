# Tasks: Declare apps and the passkey connector at start

**Input**: [plan.md](plan.md), [declared-apps.spec.md](declared-apps.spec.md), [data-model.md](data-model.md), [contracts/](contracts/).

Paths are under `src/Umbral/` unless they start with `tests/`, `docs/`, `samples/`, `src/CosmicChimps...` or `changelog.d/`.

D1 to D11 cleared by the code reviewer with C1 and C2 (a962660); `contracts/screens.md` answered by the designer. The build may start.

## Phase 1: Reading and checking

- [x] **T001** `Domains/Declaration.fs`: types and `parse` (JSON shape, version, unknown properties, keys, names, types, URI lists with `RedirectUris`, duplicates and cap, connectors value, secrets present/absent/length, the `_` form of a key and both forms refused (N1), stray secret variables), problem lines as in the contract. Tests: one per refusal line; a valid document parses; order of problems stable.

## Phase 2: Storage and apply

- [x] **T002** `declared_items`, step 16, `SchemaVersion = 16`; `...In` store variants; `Infrastructure/Declarations.fs` (`conflicts`, `apply` in one EF transaction, un-declare); the step in `Program.initialize` with the refusal and the start line. Tests: `DatabaseTests` 15 → 16; R2 rollback (a failure after the app write leaves no app and no event); create; second start writes nothing (no event, no row change); each field change is one `app_updated` with its `fields=`; secret change rotates (`app_secret_rotated declared`), old secret refused at the token endpoint, new one accepted; web → spa and spa → web; un-declare (named no more; variable unset); re-declare; app conflict and passkey conflict stop the start and write nothing; C1: an induced failure mid-apply leaves no app and no event in the database and the start raises; C2: a volume set up by the old sample steps (passkey connector enabled, shared, open sign-up, made in the admin area) starts with the new declaration, records `connector_updated declared;adopted`, the start line says adopted, and the sample signs in; passkey created, enabled, shared, enrolment, changed enrolment; unknown connector slug warns and saves; no event detail or log line holds a secret (capture the log); a sign-in through a declared app with the passkey connector end to end.

## Phase 3: Admin area

- [x] **T003** The Declared mark and owned fields per the designer's contract; refusals in `Apps.setConnectors`, `AppRedirectUris`, `AppSecret`, `AppDelete`, `PasskeyConnector.save`, `ConnectorToggle`, `ConnectorDelete` (`app-declared`, `connector-declared`); branding and invitations still work; `AppDelete` of an un-declared app removes its history row. Tests: each refused route, posted to directly, changes nothing; branding saves; mark drawn only while declared.

## Phase 4: Aspire package and sample

- [x] **T004** Package API per `contracts/package.md`, with tests (generated secret 48 characters (N2), secret variable in the `_` form (N1), environment of Umbral and of the project, JSON shape, secret parameter generated/persisted/secret and referenced, publish-mode refusal, key rule and duplicates, no variables without `AddApp`/`WithPasskeys`, manifest has no secret value).
- [x] **T005** `samples/aspire`: declare the sample and passkeys; steps reduced; README. Walk: fresh volume → passkey sign-up and sign-in in the sample with no admin click; second run → no new event (read `security_events`), no duplicate; change the redirect path in the AppHost → one `app_updated`; grep the Umbral log, the AppHost console and the manifest for the secret value.

## Phase 5: Docs, constitution, reviews

- [x] **T006** Constitution 3.2.0 (Principle V paragraph, sync report); docs (FR-013, including: secrets are plain environment variables; claim before you publish with open sign-up; PRODUCT.md's "one place" sentence); changelog fragment with the Upgrade note (schema 16; a declaration is off unless given).
- [x] **T007** Captures of the Declared marking in `.alta/reviews/2026-11-14-declared-apps/` with `notes.md`.
- [ ] **T008** Full suite green; code, design and documentation reviews; one re-check each.
