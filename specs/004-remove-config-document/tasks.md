---
description: "Task list for removing the configuration document"
---

# Tasks: Remove the Configuration Document

**Input**: Design documents from `/specs/004-remove-config-document/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/removed-surface.md](./contracts/removed-surface.md), [quickstart.md](./quickstart.md)

**Tests**: Included. The constitution makes them mandatory ("Development Workflow & Quality Gates"). Here that means: the tests of deleted behaviour are deleted, the redirect URI tests move with their rule, and new regression tests prove that a file is ignored and the export is gone.

**Line numbers** below are from `5f834b7`. T002 refreshes them after feature 003 is merged; trust the symbol names over the numbers.

## T001/T002 outcome (2026-10-10)

- 003 was built **with** its file keys (`c1fedbe`, "the configuration document carries shared and connectors"), not without them as Clarifications Q4 planned. It was merged into this branch as `13d9262`. Its keys (`shared` on connectors, `connectors` on apps, the FR-018 check) live only in `ConfigDocument.fs`, `ConfigFile.fs`, `ConfigExport.fs` and `ConfigFileTests.fs`, so T011 and T017 remove them with the rest.
- The spec cleanup for 002 and 003 (task `2026-10-10-drop-the-configuration-file-requirements-from-specs-002-and-tnch`) is in progress in another session. This feature does not edit `specs/002-*` or `specs/003-*`, to avoid conflicting with it.
- New since `5f834b7`: `ExtraScopes = []` also in `tests/Umbral.Tests/AppChoiceRuleTests.fs`, `AppConnectorChoiceTests.fs` and `SignInFlowTests.fs` (T025), and twice in `src/Umbral/Handlers/Admin/Connectors.fs` (T024). `Database.fs` is now schema v2 with an upgrade step. `tests/Umbral.Tests/DatabaseTests.fs` holds a literal **v1** schema with `extra_scopes`: keep it, because it stands for an old database. A v1 or v2 database keeps the unread column (research R4). Line numbers elsewhere moved; follow the symbol names.
- FR-008 is met by 003 (`Connectors.fs` edits the display name): T027 verifies it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel: different files, no dependency on an incomplete task
- **[Story]**: The user story this serves (US1 to US4 in [spec.md](./spec.md))

## One change set

All four stories ship together. US1 breaks constitution Principle V until US4
amends it (plan, Complexity Tracking), so no story is merged on its own. The
stories still order the work, and each ends with the build and the whole suite
green.

## Compile order

F# compiles in the order of `<Compile Include>` items. Every task that adds or
deletes a `.fs` file also edits `src/Umbral/Umbral.fsproj` or
`tests/Umbral.Tests/Umbral.Tests.fsproj`. A deleted file still listed there is
a build error. A new test file not listed there is never compiled, so its tests
never run and nothing reports it.

---

## Phase 1: Setup

**Purpose**: start from the right base and know the real extent of the work.

- [X] T001 Confirm the prerequisites, and stop if either is missing. (1) Feature 003 is merged into the branch this work starts from. (2) The task `2026-10-10-drop-the-configuration-file-requirements-from-specs-002-and-tnch` is done: `specs/003-per-app-connectors/` has no FR-016 to FR-018 or SC-005, and 003 added no keys to `src/Umbral/Domains/ConfigDocument.fs`. If 003 did add keys, list them in research.md R1 before continuing.
- [X] T002 Run the quickstart V0 grep from the repo root (see [quickstart.md](./quickstart.md#v0-inventory-is-empty-before-and-after)). Compare its output with research.md R1 and record any new file or symbol in R1, so later tasks cover it.
- [X] T003 Run `dotnet build` and `dotnet test` from the repo root and record the passing test count in this file under "Baseline" at the end. Later phases compare against it: the count must drop only by the deleted tests and rise by the added ones.

---

## Phase 2: Foundational (blocks every story)

**Purpose**: move the one rule the Apps screen still needs out of the file's module, and keep the test project's YAML reader, before anything is deleted.

- [X] T004 Create `src/Umbral/Domains/RedirectUris.fs`, module `Umbral.Domains.RedirectUris`, holding `acceptable (value: string) : Result<Uri, string>`. Move the body and comments of `acceptableRedirectUri` from `src/Umbral/Domains/ConfigDocument.fs` unchanged (research R2). In `ConfigDocument.fs`, make `acceptableRedirectUri` call `RedirectUris.acceptable` so the file keeps compiling until T011 deletes it. Add `<Compile Include="Domains/RedirectUris.fs" />` to `src/Umbral/Umbral.fsproj`, before `Domains/ConfigDocument.fs`.
  - Deviation: the doc comment of `acceptable` is reworded to drop "the configuration file"; the body and the code comments are unchanged. `ConfigDocument.acceptableRedirectUri` delegated to it until T011.
- [X] T005 In `src/Umbral/Handlers/Admin/Apps.fs` (`parseStrictUri`, near line 176), call `Umbral.Domains.RedirectUris.acceptable`. Reword the comment above it: it now says the screen and the rule share one module, and drops "the configuration file". Depends on T004.
- [X] T006 [P] Create `tests/Umbral.Tests/RedirectUriTests.fs` and move the two theories from `tests/Umbral.Tests/ConfigFileTests.fs` (near lines 626-648) into it unchanged, except that they call `RedirectUris.acceptable`. Delete them from `ConfigFileTests.fs`. Add `<Compile Include="RedirectUriTests.fs" />` to `tests/Umbral.Tests/Umbral.Tests.fsproj`. Depends on T004.
- [X] T007 [P] Add `<PackageReference Include="YamlDotNet" Version="16.*" />` to `tests/Umbral.Tests/Umbral.Tests.fsproj`, the same version range as `src/Umbral/Umbral.fsproj` line 124. `MetaTests.fs` uses `YamlDotNet.RepresentationModel` and today gets it only through the app (research R3).
- [X] T008 Run `dotnet build` and `dotnet test`: green, with the same test count as T003.
  - Deviation: build only; the full test run was left to T028, as the parent agent asked.

**Checkpoint**: the redirect URI rule no longer depends on the configuration document.

---

## Phase 3: User Story 1 - The admin area is the one place configuration changes (Priority: P1) 🎯

**Goal**: Umbral never reads, applies or exports a configuration document, and connectors request only their kind's scopes (FR-001, FR-002, FR-004 to FR-009).

**Independent Test**: start an instance whose data directory holds an `umbral.yaml`, with `UMBRAL:CONFIG_FILE` set. It starts, applies nothing, and leaves the file untouched. A connector disabled in the admin area stays disabled across restarts. `/admin/config` answers 404.

### Tests for User Story 1 (write first, and see them fail)

- [X] T009 [US1] Create `tests/Umbral.Tests/NoConfigFileTests.fs` and add `<Compile Include="NoConfigFileTests.fs" />` to `tests/Umbral.Tests/Umbral.Tests.fsproj`. Use the `Fixtures` helpers (`createAppIn`, `createAppWith`) and the sign-in helpers already in `Infrastructure/`. Four facts:
  - Deviation: fact 2 declares a valid `discord` connector (a `${VAR}` reference) and the operator adds `github` on the screen, so what the file says cannot be what the assertions read. Before the removal facts 1, 3 and 4 failed and fact 2 passed, as planned.
  1. **A file and the setting are ignored.** Write `umbral.yaml` into a temp data directory. It declares a `github` connector with the literal `client_secret: oops-a-literal`, which the old version refused at startup, and an `operator.credential_hash`. Start with `UMBRAL:CONFIG_FILE` pointing at it. Expect: startup succeeds, `ConnectorStore.listAll` is empty, `AdminAuth.isConfigured` is false, and the file's bytes are unchanged.
  2. **An admin change survives restarts.** With the same file declaring `enabled: true`, create a connector through the Connectors screen, disable it, then stop and start the app on the same data directory twice. Expect: the connector is disabled each time.
  3. **The export is gone.** `GET /admin/config` gives 404 for an anonymous client and for a signed-in operator.
  4. **No event names the file.** After facts 1 and 2, no `security_events` row has `event_type = 'config_applied'` or a `detail` containing `config_file`.

  Run it: facts 1, 3 and 4 fail before T010. Fact 1 fails on the literal secret.

### Implementation for User Story 1: remove the file

- [X] T010 [US1] In `src/Umbral/Program.fs`: delete `Umbral.Handlers.Admin.ConfigExport.endpoint` from `endpoints` (line 43). Delete the whole "Apply the configuration file" block in `initialize` (lines 328-376). Reword the setup-banner comment (lines 391-395) so it no longer says the file may claim the instance. `dataDirectory` and `config` in `initialize` may become unused: remove whichever are, because warnings are errors.
- [X] T011 [US1] Delete `src/Umbral/Handlers/Admin/ConfigExport.fs`, `src/Umbral/Infrastructure/ConfigFile.fs` and `src/Umbral/Domains/ConfigDocument.fs`. Remove their three `<Compile Include>` items and the `YamlDotNet` `PackageReference` from `src/Umbral/Umbral.fsproj`. Depends on T010.
- [X] T012 [P] [US1] In `src/Umbral/Infrastructure/AdminAuth.fs`, delete `isCredentialHash` (near line 90), `FileCredentialKey`, `FileCredentialResult` and `applyCredentialHash` (near lines 191-237). Keep `hashCredential`: the setup and credential screens and `reset-credential` use it. Any helper that only `isCredentialHash` used goes too.
- [X] T013 [P] [US1] In `src/Umbral/Infrastructure/SecurityEvents.fs`, delete the `ConfigApplied` case, its `"config_applied"` string and its entry in `allEventTypes`, plus the `// Configuration` comment above the case. In `tests/Umbral.Tests/SecurityEventsTests.fs` (line 98), record any other event type in place of `ConfigApplied`. The test is about the injected clock, not the type.
- [X] T014 [P] [US1] In `src/Umbral/Infrastructure/DataDirectory.fs`, delete the `ConfigFile` member and its comment (lines 36-37).
- [X] T015 [P] [US1] In `src/Umbral/Views/Texts.fs`, delete `ConfigExportTitle`, `ConfigExportLead`, `ConfigExportDownloadButton` and `ConfigExportSecretNote` from the record (lines 158-162) and from the value (lines 336-339).
- [X] T016 [P] [US1] In `src/Umbral/Infrastructure/AppClients.fs` (line 4), reword the module comment so the Apps screen is its only caller.
- [X] T017 [US1] Delete `tests/Umbral.Tests/ConfigFileTests.fs` and its `<Compile Include>` in `tests/Umbral.Tests/Umbral.Tests.fsproj`. The redirect URI theories already moved in T006.
- [X] T018 [US1] In `tests/Umbral.Tests/CredentialTests.fs`, delete the "From the configuration file (FR-017a)" block (from line 231, including `open Umbral.Domains.ConfigDocument`, `hashOf` and every fact through `a hash tuned too low to be a real one is refused`). Leave the `hash-credential` helper and facts (from about line 405) for T030. If `hashOf` is still used by them, keep it.
- [X] T019 [US1] In `tests/Umbral.Tests/SecurityEventCoverageTests.fs` (research R5), delete `configYaml`, `withConfigFileAnd`, `withConfigFile` and the three facts that test applying the file (from line 134). Rewrite the fake-connector fact (near line 297) and the journey from line 342, which also applies `apps:` near line 374, to create their connector and app the way the other tests do: through the Connectors and Apps screens, or the store-writing helpers in `Infrastructure/Flows.fs` and `Infrastructure/Journeys.fs`. Keep every assertion about the events those actions record. Where the journey expected the connector's events to be `Actor.System` from the file, they now come from the operator's action. Update the expectation and say why in a comment.
  - Deviation: the journey is one fact, not two. It now creates `discord` through the Connectors screen, and replaces the file's `AppUpdated` with the Apps screen's connector choice. No fact asserts an actor for those events.
- [X] T020 [P] [US1] In `tests/Umbral.Tests/SubcommandTests.fs` (line 37), drop `"UMBRAL__CONFIG_FILE"` from the variables the test clears.
- [X] T021 [P] [US1] In `tests/Umbral.Tests/MetaTests.fs`, reword the `isNotALiteral` comment (line 117) so it no longer cites `ConfigFile.fs`; `${NAME}` is compose's and a workflow's form. Replace the `umbral.yaml` `InlineData` case (line 265) with one for another YAML file the scan covers, such as a compose file using a list-shaped key path, so YAML scanning keeps a positive case. Confirm `isSettingsFile` still matches `*.yml` and `*.yaml`.
  - Deviation: the replacement case is a workflow file with a list-shaped path (`jobs.build.steps.env.API_TOKEN`).

### Implementation for User Story 1: remove extra scopes (FR-007)

- [X] T022 [US1] In `src/Umbral/Domains/Connectors.fs`, delete the `ExtraScopes` field (line 128). Make `ScopeString` (line 135) the kind's `Endpoints.Scopes`, distinct and space-joined, as before without the extra list. Reword the comment at line 53, which ends "Anything beyond this is the operator's `extra_scopes`.", to say these are all the scopes Umbral requests.
- [X] T023 [US1] In `src/Umbral/Infrastructure/ConnectorStore.fs`, remove `extra_scopes` from the column list (line 17), the reader (lines 37-40: shift later ordinal indexes down by one), the insert column and value lists (lines 100-101), the update (line 107) and the `@scopes` parameter (line 117). In `src/Umbral/Infrastructure/Database.fs`, remove `extra_scopes TEXT NULL,` from `CREATE TABLE connectors` (line 89). No upgrade step (research R4, data-model.md). Depends on T022.
- [X] T024 [P] [US1] Remove `ExtraScopes = []` from the record literal in `src/Umbral/Handlers/Admin/Connectors.fs` (line 274). Depends on T022.
- [X] T025 [P] [US1] Remove `ExtraScopes = []` from the connector record literals in `tests/Umbral.Tests/FakeConnectorGuardTests.fs` (line 24), `ProviderAdapterTests.fs` (82), `ConnectorRuntimeTests.fs` (111, 290), `OidcProtocolTests.fs` (131), `ConnectorDeleteTests.fs` (37), `FirstLoginTests.fs` (137), `Infrastructure/Flows.fs` (130) and `Infrastructure/Journeys.fs` (153). In `FakeConnectorGuardTests.fs` line 12, reword the comment "as a configuration file or…" to drop the file. Depends on T022.
- [X] T026 [US1] Confirm that the authorize URLs asserted in `tests/Umbral.Tests/ProviderAdapterTests.fs` (near lines 125-151) still pass unchanged: they are the kind-only scope strings that FR-007 requires.

### Display name (FR-008)

- [X] T027 [US1] Verify, with no code change, that feature 003 made a connector's display name editable on the Connectors screen (`src/Umbral/Handlers/Admin/Connectors.fs`) and that a test covers it. If it did not, stop and record the gap here. FR-008 says 003 provides it (spec Clarifications, Q4).
  - Deviation: verified, no change: `MultipleConnectorsTests.fs` ("saving a card edits the stored connector by its row and keeps its secret") renames a connector's display name through the Connectors screen and refuses a blank one.

- [X] T028 [US1] Run `dotnet build` and `dotnet test`: green, with T009's four facts passing. The test count is the T003 baseline, minus the deleted file tests, plus T009's facts.

**Checkpoint**: nothing in `src/` reads, applies or exports a configuration document. Connectors request only their kind's scopes.

---

## Phase 4: User Story 2 - Claim a new instance without the file (Priority: P2)

**Goal**: `hash-credential` is gone, and `reset-credential` is the documented unattended claim (FR-003, FR-010).

**Independent Test**: start a clean instance, run `reset-credential`, and sign in with the credential it prints. `SubcommandTests` already covers that path ("reset-credential on an unclaimed instance claims it…").

- [ ] T029 [US2] In `src/Umbral/Program.fs`, delete `hashCredential` and its doc comment (lines 518-566) and the `elif args |> Array.contains "hash-credential"` branch in `main` (lines 573-574). Reword the comment above `main` if it lists the subcommands.
- [ ] T030 [US2] In `tests/Umbral.Tests/CredentialTests.fs`, delete the `hash-credential` process helper (near line 405) and both facts, `hash-credential prints a hash that verifies…` and `hash-credential refuses a credential that is too short` (from line 427). Remove `hashOf` and any `open` left unused.
- [ ] T031 [P] [US2] In `README.md`, add a short "Claim an instance from a script" subsection, beside "Forgotten operator credential" or inside the first-run steps. It says: start the container, then run `docker exec umbral /app/Umbral reset-credential` (compose: `docker compose exec umbral /app/Umbral reset-credential`) once. It claims an unclaimed instance and prints a credential once, for the script to store. The setup screen then no longer asks for a token. Every other step stays as it is.
- [ ] T032 [US2] Run `dotnet build` and `dotnet test`: green. Both `reset-credential` facts in `SubcommandTests.fs` pass unchanged.

**Checkpoint**: the operator credential can be set only from the setup screen, the credential screen or `reset-credential`.

---

## Phase 5: User Story 3 - Move or rebuild an instance from its data directory (Priority: P2)

**Goal**: copying the data directory is the documented and tested way to move an instance (SC-003). It replaces 001 SC-006.

**Independent Test**: copy an instance's data directory and start a second instance on the copy with the same public origin. It serves the same sign-ins, app secrets and sessions.

- [ ] T033 [US3] Add a fact to `tests/Umbral.Tests/RestartTests.fs`: "an instance started from a copy of the data directory serves the same apps, secrets and sessions". Sign a person in to an app with a confidential client, using the helpers the existing restart fact uses. Stop the app. Copy the data directory recursively to a second temp directory and start a new app on the copy with `Fixtures.createAppIn` and the same public origin. Expect: the app's existing client secret is accepted at the token endpoint, the person's Umbral session cookie is still valid, the identity token validates against the copy's JWKS, and the connector list is the same.
- [ ] T034 [P] [US3] In `README.md`, rework the opening of the backup and restore section so it also says that copying the data directory is how to move Umbral to another machine or rebuild it. The same public origin is needed: tokens and the redirect addresses registered with providers depend on it. Keep every command as it is.
- [ ] T035 [US3] Run `dotnet test`: green, including T033.

**Checkpoint**: what export and apply promised, a copied data directory delivers, with app secrets included.

---

## Phase 6: User Story 4 - Documentation and governance say one thing (Priority: P3)

**Goal**: no document describes the file. The constitution no longer requires it (FR-011 to FR-014, SC-004, SC-005).

**Independent Test**: the quickstart V0 grep finds only the allowed hits.

- [ ] T036 [US4] Amend `.specify/memory/constitution.md` to **2.0.0** (research R7): (1) replace Principle V's body with a heading "### V. (Removed in 2.0.0)" and one paragraph. The paragraph says the YAML file and parity rule were dropped on 2026-10-10 by spec 004, that the admin area is the one place configuration changes and the data directory is the whole state, and why: each feature had to extend the file, the file undid admin changes on restart, and an export could never carry app secrets. Do **not** renumber VI and VII. (2) Delete the "Config parity" row from the Constitution Check table. (3) In the principles list near the top, mark V as removed. (4) Write a new sync impact report at the top: version change `1.0.0 → 2.0.0`, the rationale (MAJOR: a principle removed), what was removed, and templates checked (none names the YAML file). (5) Set **Last Amended** to the date of the change.
- [ ] T037 [P] [US4] In `docs/vision.md` (line 44), mark the "1 YAML for config-as-code" row as dropped (status ❌ or "dropped") with a one-line reason pointing at spec 004.
- [ ] T038 [US4] In `README.md`, remove: the "Configuration as code" table-of-contents entry (line 34); the whole "Configuration as code" section with its subsection "The operator credential in the file" (lines 447-545); the `umbral.yaml` row of the data-directory table (line 390); the `UMBRAL__CONFIG_FILE` row of the settings reference (line 572); and "A broken `umbral.yaml` also stops startup and names the line." in troubleshooting (line 584). Check that no remaining link points at `#configuration-as-code`.
- [ ] T039 [P] [US4] In `docs/connectors/discord.md`, `docs/connectors/github.md` and `docs/connectors/google.md`, delete the "The same thing in the configuration file" subsection with its YAML block and the README link after it. Also delete the sentence about adding scopes with `extra_scopes` (Discord near line 94, GitHub near lines 88-89). Keep the statement of which scopes the provider needs.
- [ ] T040 [P] [US4] In `specs/001-umbral-mvp/spec.md`, add a one-line note, "**Superseded by [004](../004-remove-config-document/spec.md).**", directly under FR-017a (scoped to "the configuration-file part"), FR-027, FR-028, FR-029, FR-030, SC-006, SC-008 ("the configuration export" part), the "Configuration document" entity and the User Story 5 heading. Change no requirement text (research R8).
- [ ] T041 [P] [US4] In `specs/001-umbral-mvp/quickstart.md` (Scenario 5) and `specs/001-umbral-mvp/data-model.md` (the `extra_scopes` row, `config_applied` in the event list, and the "The configuration document" section near line 230), add the same one-line superseded note. Change nothing else.

**Checkpoint**: SC-004 and SC-005 hold.

---

## Phase 7: Polish & validation

- [ ] T042 Run the quickstart V0 grep. The only hits left are the superseded notes in `specs/001-*`, `specs/002-*` and `specs/003-*`, this feature's folder, and the constitution and vision records. `YamlDotNet` appears only in `tests/Umbral.Tests/Umbral.Tests.fsproj` and `tests/Umbral.Tests/MetaTests.fs`. Fix anything else.
- [ ] T043 Run `dotnet build` and `dotnet test` from the repo root: green. Record the final count under "Baseline" beside T003's, with the difference explained.
- [ ] T044 Build the container image and run quickstart V2 to V7 against it. In V2, check that the log never mentions the file. Record the outcome of each step in [quickstart.md](./quickstart.md) or here.
- [ ] T045 Run `tests/Umbral.Tests/IdleMemoryTests.fs` (Principle II, under 150 MB idle). Expect the same or lower than before, since YamlDotNet is no longer loaded. Record the number.
- [ ] T046 Self-review the full diff against [spec.md](./spec.md). Check each FR-001 to FR-014 and SC-001 to SC-005, and that every new comment and doc sentence reads like its neighbours. Set this feature's status in [plan.md](./plan.md) and `.spec-context.json` through the companion hooks.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (T001–T003)**: first. T001 blocks everything: without 003 merged, the line numbers and 003's own file keys are wrong.
- **Foundational (T004–T008)**: after Setup. Blocks every story: T011 deletes `ConfigDocument.fs`, which the Apps screen must no longer call.
- **US1 (T009–T028)**: after Foundational.
- **US2 (T029–T032)**: after US1. It edits `Program.fs` and `CredentialTests.fs` after US1 does, and T030 relies on T018 having removed the file block.
- **US3 (T033–T035)**: after Foundational. Independent of US1 and US2 in code; it shares only `README.md` with US2 and US4.
- **US4 (T036–T041)**: after US1, because the docs must describe the code as it is. It can run beside US2 and US3, except for `README.md` (T038 after T031 and T034, or merge with care).
- **Polish (T042–T046)**: last.

### Within User Story 1

T009 (tests, failing) → T010 → T011 → T012–T016 [P] → T017 → T018 → T019 → T020–T021 [P] → T022 → T023 → T024–T025 [P] → T026 → T027 → T028.

### Parallel opportunities

- T006 and T007, after T004.
- T012, T013, T014, T015 and T016: different files, after T011.
- T020 and T021.
- T024 and T025, after T022.
- In US4: T037, T039, T040 and T041 beside T036 and T038.
- US3's T033 beside US2.

## Parallel Example: User Story 1

```text
After T011:
  T012 AdminAuth.fs     T013 SecurityEvents.fs + SecurityEventsTests.fs
  T014 DataDirectory.fs T015 Texts.fs       T016 AppClients.fs
After T022:
  T024 Handlers/Admin/Connectors.fs         T025 the eight test files
```

## Implementation Strategy

1. **Setup and Foundational**: the move of the redirect URI rule is the only piece of the file that survives. Get it green first.
2. **US1**: the core removal. Write T009 first and watch it fail, then delete the code until it passes.
3. **US2 and US3**: small. US2 is a deletion and a README note. US3 is one test and a README paragraph.
4. **US4**: the constitution amendment and the docs. Without it the change set violates Principle V, so it is not optional.
5. **Polish**: the inventory grep, the container run, and the idle memory figure.

There is no smaller MVP than the whole change set: see "One change set" above.

## Baseline

| When | Tests passing | Note |
| --- | --- | --- |
| T003 | 423 / 423 | After merging 003 (`13d9262`); 7 min 9 s |
| T043 | | |
