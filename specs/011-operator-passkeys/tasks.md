# Tasks: Operator passkeys

**Input**: [plan.md](plan.md), [operator-passkeys.spec.md](operator-passkeys.spec.md), [data-model.md](data-model.md), [contracts/routes.md](contracts/routes.md), [research.md](research.md), and `design/contracts/operator-passkeys-ui.md` (normative for screens and words).

Paths are under `src/Umbral/` unless they start with `tests/`. Tests run through the real endpoints with `tests/Umbral.Tests/Infrastructure/SoftwareAuthenticator.fs` and fail first.

**Shared registrations**: `Umbral.fsproj`, `tests/Umbral.Tests/Umbral.Tests.fsproj` and the route list in `Program.fs` gain one line per new file. Those one-line registrations are part of the task that creates the file; nothing else in them is edited outside its owner task.

## Phase 1: Setup

No setup: no dependency, no tooling.

## Phase 2: Foundational (blocks every story)

**Wave 1 — independent (different files):**

- [x] **T001** [P] Schema 8: `operator_passkeys` in `schema` and `UpgradeToVersion8`, `SchemaVersion = 8`; update the schema tests for version 8 and an upgrade from 7 · `Infrastructure/Database.fs`, `tests/Umbral.Tests/DatabaseTests.fs`
- [x] **T002** [P] Three ceremony purposes `operator-signin`, `operator-add`, `operator-confirm` (stored words, parse) · `Infrastructure/Passkeys.fs`
- [x] **T003** [P] Event types `operator_passkey_added`, `operator_passkey_removed`, `operator_password_signin_changed` · `Infrastructure/SecurityEvents.fs`
- [x] **T004** [P] Every string and notice code of `design/contracts/operator-passkeys-ui.md` section 4, registered in `knownNotice` · `Views/Texts.fs`

**⟶ Wait for Wave 1 to finish, then:**

- [ ] **T005** New module: list, count, find by credential id, insert under the 10 limit (one statement), record use (counter and last used), remove unless last while off (one statement), remove all, the switch (read; write off only while a passkey exists, one statement; write on), the operator user handle (get or mint) · `Infrastructure/OperatorPasskeys.fs` (new, before `WebAuthn.fs`)

**⟶ Wait for T005, then:**

- [ ] **T006** The verifier takes `(credentialId, transports)` pairs to exclude, a uniqueness function, and the stored public key, counter and user handle; the connector's call sites map their records; the JSON helpers of `Ceremonies.fs` become public for reuse; the connector's tests stay green · `Infrastructure/WebAuthn.fs`, `Handlers/Passkeys/Ceremonies.fs`
- [ ] **T007** `verify` returns false while password sign-in is off, before hashing; `endOtherSessions` rotates the stamp · `Infrastructure/AdminAuth.fs`

**⟶ Wait for T006 and T007, then:**

- [ ] **T008** Test helpers: claim and sign in, add an operator passkey (password or passkey re-check), sign in with one, run a confirm ceremony, read the switch · `tests/Umbral.Tests/Infrastructure/OperatorFlows.fs` (new)

**Checkpoint**: everything builds, the full suite is green, nothing visible has changed.

## Phase 3: User Story 1 — Add a passkey and sign in with it (P1, MVP)

Files: `Handlers/Admin/OperatorPasskeys.fs` (new), `Handlers/Admin/Credential.fs`, `Handlers/Admin/SignIn.fs`, `Assets/passkeys.js`, `tests/Umbral.Tests/OperatorPasskeyTests.fs` (new)

### Tests

- [ ] **T009** [US1] Add with a password re-check and with a passkey re-check; a wrong password adds nothing and is logged; the 11th refused; sign in with a passkey opens an admin session and sets last used; a reused sign-in ceremony, a wrong origin, a regressed counter and a missing UV refused and logged; an operator passkey refused at `/passkey/assertion`, a user passkey refused at the operator sign-in; no operator passkey on the Users screen; the passkey sign-in and add completions limited by `RateLimits.AdminAuth`; no credential id in any page or event · `tests/Umbral.Tests/OperatorPasskeyTests.fs`

### Implementation

**Wave 1:**

- [ ] **T010** [US1] The JSON endpoints: sign-in options and completion, confirm options, add options (with the re-check) and completion; the shared re-check reader (`current`, or `ceremony` + `credential`) that the remove and switch handlers will call; routes in `Program.fs` · `Handlers/Admin/OperatorPasskeys.fs`

**⟶ Wait for T010, then:**

- [ ] **T011** [US1] The Credential screen as the design contract section 1 to 3 says: three cards, rows, add form, limit line, the switch card (off button disabled with no passkey), the password card only while on, the confirm blocks for a row and for the switch drawn from a parameter (used by US2 and US3), notices, `passkeys.js` linked · `Handlers/Admin/Credential.fs`
- [ ] **T012** [US1] The sign-in page: both, or passkey only; `hidden` passkey button; `noscript` lines · `Handlers/Admin/SignIn.fs`
- [ ] **T013** [US1] Script hooks `data-operator-passkey-signin`, `data-operator-passkey-add`, `data-operator-confirm`, `data-operator-confirm-passkey`, reveal of hidden ceremony buttons · `Assets/passkeys.js`

**Checkpoint**: an operator can add a passkey and sign in with it.

## Phase 4: User Story 2 — Switch password sign-in off (P1)

Files: `Handlers/Admin/OperatorPasswordSignIn.fs` (new), `tests/Umbral.Tests/OperatorPasswordSwitchTests.fs` (new)

### Tests

- [ ] **T014** [US2] With no passkey, off refused and logged; a post with no ceremony answers 422 with the confirm block; off with a ceremony ends other sessions; while off the correct password is refused at sign-in (same answer as a wrong one), as a re-check and as a replacement, and no page shows a password field; on again with a ceremony and the old password works · `tests/Umbral.Tests/OperatorPasswordSwitchTests.fs`

### Implementation

- [ ] **T015** [US2] `POST /admin/credential/password-signin`: 422 confirm redraw, passkey re-check only, the switch through T005, stamp rotation on off, events; route in `Program.fs` · `Handlers/Admin/OperatorPasswordSignIn.fs`

**Checkpoint**: password sign-in can be turned off and on.

## Phase 5: User Story 3 — Remove a passkey (P2)

Files: `Handlers/Admin/OperatorPasskeyRemove.fs` (new), `tests/Umbral.Tests/OperatorPasskeyRemoveTests.fs` (new)

### Tests

- [ ] **T016** [US3] A post with no re-check answers 422 with that row's confirm block and logs nothing; remove with a password and with another passkey; the removed passkey's sign-in refused; other sessions ended; the last one refused while off with the next action, and removed while on; an unknown id refused · `tests/Umbral.Tests/OperatorPasskeyRemoveTests.fs`

### Implementation

- [ ] **T017** [US3] `POST /admin/credential/passkeys/{id}/remove`: 422 confirm redraw, re-check, remove through T005, stamp rotation, events; route in `Program.fs` · `Handlers/Admin/OperatorPasskeyRemove.fs`

**Checkpoint**: a lost device can be removed.

## Phase 6: User Story 4 — Recover from the host (P2)

Files: `resetCredential` and `main` in `Program.fs`, `tests/Umbral.Tests/SubcommandTests.fs`

- [ ] **T018** [US4] Tests: with the switch off and two passkeys, `reset-credential` prints a password, the switch is on, passkeys kept, sessions ended; with `--forget-passkeys` none remain and the output says so; events written · `tests/Umbral.Tests/SubcommandTests.fs`
- [ ] **T019** [US4] `reset-credential [--forget-passkeys]`: switch on, optional removal, the output lines, events · `Program.fs`

**Checkpoint**: no lost device locks the operator out.

## Phase 7: Polish

**Wave 1 — independent:**

- [ ] **T020** [P] `CsrfTests` walks the new admin POSTs (form and JSON) · `tests/Umbral.Tests/CsrfTests.fs`
- [ ] **T021** [P] README: "Forgotten operator credential" with `--forget-passkeys`; admin sign-in and first-run text; backup table names the operator passkeys; schema version 8 under Upgrading; the security log's new events. CHANGELOG under `## Unreleased` with an Upgrade entry for schema 8 · `README.md`, `CHANGELOG.md`
- [ ] **T022** [P] Regenerate the stylesheet if a view gained a class · `Assets/umbral.css` (via `Assets/build-css.sh`)

**⟶ Wait for Wave 1, then:**

- [ ] **T023** Full `dotnet test` green, idle memory within 150 MiB (`IdleMemoryTests`), against the Success Criteria
- [ ] **T024** Reviews: captures and `notes.md` in `.alta/reviews/2026-10-24-operator-passkeys/` for `umbral-designer`; doc notes for `umbral-documentation`; branch and range to `umbral` for the code review; answers committed; all three clear · `.alta/reviews/2026-10-24-operator-passkeys/`

## Dependencies & Execution Order

- Setup: none. Foundational: Wave 1 (T001–T004) → T005 → T006, T007 → T008. It blocks every story.
- US1 (T009–T013): T010 before T011–T013. US2 and US3 call T010's re-check reader and T011's confirm blocks, so they follow US1. US2 and US3 are independent of each other; US4 needs only Foundational.
- Polish after every story: T020–T022 in any order, then T023, then T024.
- F# tasks run one writer at a time (shared `obj/bin`); README and CHANGELOG (T021) can run beside them.
