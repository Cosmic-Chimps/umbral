---
description: "Task list for 003: federated login for every app, with optional per-app connectors"
---

# Tasks: Federated Login for Every App, with Optional Per-App Connectors

**Input**: [plan.md](./plan.md), [per-app-connectors.spec.md](./per-app-connectors.spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts)

**Tests**: included. The constitution requires a real-endpoint test for every OIDC behaviour and a test with no real provider credentials for every connector. Tests in each phase are written first and fail until the implementation lands. Connector tests use the `fake` kind.

**Format**: `- [ ] **T###** [P?] [US#] Description · file`. `[P]` means independent of the rest of its wave.

**Routes**: the new routes go into the existing `endpoint` lists of `Admin/Apps.fs` and `Admin/Connectors.fs`, which `Program.fs` already includes with `yield!`. `Program.fs` does not change.

---

## Phase 1: Setup

Files: `tests/Umbral.Tests/Umbral.Tests.fsproj`, the four new test files as empty modules.

- [x] **T001** Create empty test modules `AppChoiceRuleTests.fs`, `SignInAppNameTests.fs`, `AppConnectorChoiceTests.fs`, `MultipleConnectorsTests.fs` and register them in the test project before `SmokeTests.fs` · `tests/Umbral.Tests/Umbral.Tests.fsproj`

---

## Phase 2: Foundational

Files: `src/Umbral/Domains/Connectors.fs`, `src/Umbral/Infrastructure/Database.fs`, `src/Umbral/Views/Texts.fs`, `src/Umbral/Infrastructure/ConnectorStore.fs`, `src/Umbral/Infrastructure/AppConnectors.fs` (new), `src/Umbral/Umbral.fsproj`, `src/Umbral/Infrastructure/Sessions.fs`, `src/Umbral/Handlers/Connect/Callback.fs`. Tests: `AppChoiceRuleTests.fs`, `DatabaseTests.fs`, `SignInFlowTests.fs`.

No story starts until this phase is done.

### Tests

**Wave 1 — independent (different files):**

- [x] **T002** [P] Unit tests for `offeredTo` (AllShared offers enabled shared only; Only offers enabled members, shared or not; an unknown slug in a set widens nothing) and `strandedApps` (names apps left with nothing, keeps input order) · `tests/Umbral.Tests/AppChoiceRuleTests.fs`
- [x] **T003** [P] Upgrade test: a version 1 database with connectors comes up as version 2, every connector shared, no choice rows. A fresh database is version 2 with the column. A second start changes nothing · `tests/Umbral.Tests/DatabaseTests.fs`
- [x] **T004** [P] Sign-in flow: the token's `idp` is the connector's kind; the session carries the `connector` claim with the slug; a session cookie with only an `idp` claim (pre-upgrade) is still honoured; a failed callback redirects to `/signin` with the original `returnUrl` · `tests/Umbral.Tests/SignInFlowTests.fs`

### Implementation

**Wave 1 — independent (different files):**

- [x] **T005** [P] Add `IsShared` to `Connector`, the `AppChoice` union, `offeredTo` and `strandedApps`, as in contracts/signin-and-session.md · `src/Umbral/Domains/Connectors.fs`
- [x] **T006** [P] Schema v2: `is_shared` in `CREATE TABLE connectors`, the two choice tables with their cascades and index, the v1 → v2 `ALTER TABLE` inside the schema transaction, `SchemaVersion = 2` (R2) · `src/Umbral/Infrastructure/Database.fs`
- [x] **T007** [P] Every new text and notice code from both contracts: `PickerTitleForApp`, `PickerNoConnectorsForApp`, the naming advice, the add-connector form, the connector choice, the stranded-apps warnings and the nine notice codes. Each operator message says what to do next · `src/Umbral/Views/Texts.fs`

**⟶ Wait for Wave 1 to finish, then:**

**Wave 2 — independent (different files):**

- [x] **T008** [P] Read and write `is_shared`. Add `create` with an operator-chosen slug and `update` for display name, credentials and shared. `save` no longer infers the kind from the slug · `src/Umbral/Infrastructure/ConnectorStore.fs`
- [x] **T009** [P] New module: `choiceFor clientId`, `allChoices`, `setChoice clientId AppChoice` (one transaction: marker row and set, or delete for AllShared), `deleteFor clientId`. Register it after `ConnectorStore.fs` · `src/Umbral/Infrastructure/AppConnectors.fs`, `src/Umbral/Umbral.fsproj`
- [x] **T010** [P] `Sessions.signIn ctx userId kind slug` writes `idp` = kind and `connector` = slug. Add `currentConnector` with the pre-upgrade fallback to `idp`. `Callback` signs in with both, and `Callback.fail` keeps the round trip's `returnUrl` (R3, R4) · `src/Umbral/Infrastructure/Sessions.fs`, `src/Umbral/Handlers/Connect/Callback.fs`

**Checkpoint**: the full existing suite passes unchanged, plus T002 to T004. Sign-in behaves exactly as before for every app.

---

## Phase 3: User Story 1 - Everyone can tell where they are signing in (P1)

Files: `src/Umbral/Handlers/Connect/Picker.fs`. Tests: `SignInAppNameTests.fs`.

The naming advice on the Connectors screen (FR-003) is built in Phase 5, which owns that screen. The docs are in Phase 6.

### Tests

- [x] **T011** [US1] Sign-ins started from app A and from app B show `data-signin-app` A and B. `/signin` alone, an unknown `client_id`, and a `returnUrl` that is not `/connect/authorize` show no `data-signin-app`. A `client_id` query value on `/signin` itself is ignored. After a failed callback the page still names the app · `tests/Umbral.Tests/SignInAppNameTests.fs`

### Implementation

- [x] **T012** [US1] Read `client_id` from the sanitized `returnUrl` when its path is `/connect/authorize`. Look the app up through `IOpenIddictApplicationManager` and load its choice with `AppConnectors.choiceFor`. Render `PickerTitleForApp` with `data-signin-app`, the buttons from `offeredTo`, and `PickerNoConnectorsForApp` when an app offers nothing. With no app, keep the general title and offer every enabled shared connector (FR-001, FR-002, FR-006, FR-007) · `src/Umbral/Handlers/Connect/Picker.fs`

**Checkpoint**: every sign-in started from an app names that app (SC-001).

---

## Phase 4: User Story 2 - Limit an app to some connectors (P2)

Files: `src/Umbral/Handlers/Connect/Authorize.fs`, `src/Umbral/Handlers/Admin/Apps.fs`, `src/Umbral/Handlers/Admin/AppDelete.fs`, `src/Umbral/Handlers/Admin/ConnectorToggle.fs`, `src/Umbral/Handlers/Admin/ConnectorDelete.fs`, `src/Umbral/Domains/ConfigDocument.fs`, `src/Umbral/Infrastructure/ConfigFile.fs`, `src/Umbral/Handlers/Admin/ConfigExport.fs`. Tests: `AppConnectorChoiceTests.fs`, `ConnectorDeleteTests.fs`, `ConfigFileTests.fs`.

### Tests

**Wave 1 — independent (different files):**

- [x] **T013** [P] [US2] An app limited to one connector offers only it on `/signin` while another app offers all shared ones. A session through a connector the app does not offer leads to the picker, or `login_required` with `prompt=none`, and is not signed out. A session through an offered connector is honoured. The Apps screen saves both choices, shows `data-app-choice` and `data-app-connector`, and warns with `data-stranded-app` before an empty or all-disabled set. Disabling the only connector of a limited app warns, naming it. Deleting an app removes its choice rows · `tests/Umbral.Tests/AppConnectorChoiceTests.fs`
- [x] **T014** [P] [US2] Deleting a connector removes it from every chosen set and leaves those apps limited (FR-011). Deleting a limited app's only connector warns with `connector-delete-strands-apps` and needs `confirm=yes` · `tests/Umbral.Tests/ConnectorDeleteTests.fs`
- [x] **T015** [P] [US2] `shared` and `connectors` parse with their defaults. `connectors: []` is distinct from absent. An unknown slug or a repeated slug in an app's list is refused with its path (FR-018). Applying twice writes no second event. Export then apply on a fresh instance reproduces both settings (FR-017) · `tests/Umbral.Tests/ConfigFileTests.fs`

### Implementation

**Wave 1 — independent (different files):**

- [x] **T016** [P] [US2] FR-008: in the session branch, load the app's choice and honour the session only if `Sessions.currentConnector` is in `offeredTo`. Otherwise redirect to `/signin?returnUrl=…`, or `forbid` with `login_required` for `prompt=none`. Never sign out · `src/Umbral/Handlers/Connect/Authorize.fs`
- [x] **T017** [P] [US2] Connector choice as radio cards on create (`connectorChoice`, `connectors`). Add `POST /admin/apps/{clientId}/connectors` to `endpoint`. Show the choice on each app row. Run the `strandedApps` warning with `confirm=yes`. Base the "add a connector first" gate on `offeredTo AllShared` (FR-005, FR-009, FR-010) · `src/Umbral/Handlers/Admin/Apps.fs`
- [x] **T018** [P] [US2] Delete the app's choice rows with `AppConnectors.deleteFor` · `src/Umbral/Handlers/Admin/AppDelete.fs`
- [x] **T019** [P] [US2] Before disabling, compute `strandedApps`. Warn with `connector-strands-apps` and the slug unless `confirm=yes`. Keep `connector-last-enabled` for when no app is stranded (FR-010) · `src/Umbral/Handlers/Admin/ConnectorToggle.fs`
- [x] **T020** [P] [US2] Same guard for delete with `connector-delete-strands-apps`. Update the header comment: deleting also removes the connector from every chosen set (FR-010, FR-011) · `src/Umbral/Handlers/Admin/ConnectorDelete.fs`
- [x] **T021** [P] [US2] `ConnectorEntry.Shared`, `AppEntry.Connectors: string list option`, and the FR-018 and repeated-slug checks in `validate` (R7) · `src/Umbral/Domains/ConfigDocument.fs`

**⟶ Wait for T021, then:**

- [x] **T022** [US2] DTO keys `shared` (default true) and `connectors` (null kept apart from empty). Apply both, adding `shared` and `connectors` to the change lists so an unchanged file writes no event. Export `shared: false` and `connectors:` only off their defaults, including `[]`. `ConfigExport.listApps` carries each app's choice (FR-016, FR-017, FR-019) · `src/Umbral/Infrastructure/ConfigFile.fs`, `src/Umbral/Handlers/Admin/ConfigExport.fs`

**Checkpoint**: an app can be limited to some connectors from the screen and from the file, and no change can strand an app without a named warning.

---

## Phase 5: User Story 3 - One app with its own name on the consent screen (P3)

Files: `src/Umbral/Handlers/Admin/Connectors.fs`. Tests: `MultipleConnectorsTests.fs`.

### Tests

- [x] **T023** [US3] Add a second `fake` connector through `POST /admin/connectors`. An invalid slug and a taken slug are refused. Its card shows its own callback address, `data-connector-kind` and `data-connector-shared`. A not-shared connector is not offered to an all-shared app, carries `data-offered-nowhere` until an app chooses it, and is offered to the app that does. Turning a chosen connector to not shared warns when it strands an app. One fake account through both connectors is one user with one `sub`, and both tokens carry the same `idp` (FR-015). Every card carries `data-naming-advice` (FR-003). Empty cards for kinds with no connector still post to `/admin/connectors/{kind}` · `tests/Umbral.Tests/MultipleConnectorsTests.fs`

### Implementation

- [x] **T024** [US3] One card per stored connector plus an empty card per kind with none. Add the `data-add-connector` form and `POST /admin/connectors` to `endpoint`. Cards get a display name field, a shared switch, `data-connector-kind`, `data-connector-shared`, `data-offered-nowhere` and the naming advice. Card save reads the kind from the stored row, creates the first of a kind from the empty card, and runs the strand warning when shared turns off (FR-003, FR-010, FR-012, FR-013, FR-014) · `src/Umbral/Handlers/Admin/Connectors.fs`

**Checkpoint**: one app can have its own Discord application while every other app keeps the shared one, and a person who uses both is one user.

---

## Phase 6: Polish

Files: `README.md`, `docs/connectors/discord.md`, `docs/connectors/github.md`, `docs/connectors/google.md`.

**Wave 1 — independent (different files):**

- [ ] **T025** [P] README: a Connector is Umbral's identity at a provider, and there can be several per kind. Name the provider's application after the instance, and say why. Cover the connector choice under "Register an app" and "Limits of the Apps screen today", that `idp` is the kind, the two YAML keys and the fields applying overwrites, the FR-018 error, and "not shared and chosen by no app" under Troubleshooting (FR-004) · `README.md`
- [ ] **T026** [P] Each guide: the naming advice at the step that names the application, step 1 reworded now that the identifier is only the kind's name for the first connector, a "Giving one app its own name" section, and `shared` in the YAML example. Google's section says a separate Google Cloud project is needed (R8) (FR-004) · `docs/connectors/discord.md`, `docs/connectors/github.md`, `docs/connectors/google.md`

**⟶ Wait for Wave 1 to finish, then:**

- [ ] **T027** Run `dotnet build` and `dotnet test` from the repository root. Check SC-001 to SC-006 against the tests that cover them, and confirm the five-minute path still needs no add step (SC-006)

---

## Dependencies & Execution Order

- Setup → Foundational → stories → Polish.
- **Foundational**: Wave 1 tests (T002 to T004) and Wave 1 implementation (T005 to T007) are independent. Wave 2 (T008 to T010) waits for T005 to T007.
- **US1** (T011, T012) needs only Foundational.
- **US2** needs Foundational. Its picker scenarios in T013 also need T012 from US1. Wave 1 (T016 to T021) is independent. T022 waits for T021.
- **US3** needs Foundational. T023's "offered to the app that chooses it" scenario sets the choice through `AppConnectors.setChoice`, so it does not wait for US2.
- **Polish**: T025 and T026 are independent. T027 runs last.
