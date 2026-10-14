---
description: "Task list for 005 external secret stores"
---

# Tasks: External Secret Stores for Connector Secrets

**Input**: Design documents from `specs/005-external-secret-stores/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included. The spec requires them (FR-025, SC-002 to SC-008) and the
constitution's testing discipline requires every connector-path behaviour to be
tested without real credentials. Within each story, tests come first and must
fail before the implementation task that makes them pass.

**Organization**: by user story, after a foundation that changes the seam with
no behaviour change.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1–US4 from spec.md
- F# compile order matters: every new `.fs` file needs its `<Compile Include>`
  in the right place in `src/Umbral/Umbral.fsproj` or
  `tests/Umbral.Tests/Umbral.Tests.fsproj` (before `Program.fs` /
  `SmokeTests.fs` respectively). Tasks that add a file say where.

## Conventions every task follows

- Run `dotnet build -c Release` (warnings are errors) and the tests the task
  names; the full suite is `dotnet test -c Release`.
- Every operator-facing string lives in `src/Umbral/Views/Texts.fs`; every
  operator message says what to do next (Principle VI).
- No secret value, locator or manager credential in any log line, exception
  message, security event `Detail`, page or CLI output. Plaintext travels as
  `Secrets.Protected` wherever it is held longer than one call.
- Settings follow `PublicOrigin.fs`: pure `parse` → `Result`, `describe` with
  the next step, `UMBRAL:X` configuration keys for `UMBRAL__X` variables.

---

## Phase 1: Setup

**Purpose**: packages and empty modules, so later tasks only fill files in.

- [X] T001 Add exact-pinned package references `AWSSDK.SecretsManager` 4.0.100.16, `Google.Apis.Auth` 1.77.0, `Azure.Security.KeyVault.Secrets` 4.11.2, `Azure.Identity` 1.21.0, `BellaBaxter.Client` 0.1.1-preview.131 with a one-line comment each on why (research R4) in `src/Umbral/Umbral.fsproj`; confirm `dotnet build -c Release` has no NU warnings (they are errors here)
- [X] T002 Create empty modules and register them in compile order in `src/Umbral/Umbral.fsproj`: `Infrastructure/SecretStores/Settings.fs`, `Aws.fs`, `Gcp.fs`, `Azure.fs`, `BellaBaxter.fs`, `Registry.fs` directly after `Infrastructure/Secrets.fs`, and `Infrastructure/SecretMove.fs` directly after `Infrastructure/ConnectorStore.fs`
- [X] T003 [P] Create empty test modules `SecretStoreSettingsTests.fs`, `AwsSecretStoreTests.fs`, `GcpSecretStoreTests.fs`, `AzureSecretStoreTests.fs`, `BellaBaxterSecretStoreTests.fs`, `SecretCacheTests.fs`, `ExternalSecretConnectorTests.fs`, `SecretMoveTests.fs` and `Infrastructure/InMemorySecretStore.fs` (before the test files that use it), registered in `tests/Umbral.Tests/Umbral.Tests.fsproj`

---

## Phase 2: Foundational (blocking)

**Purpose**: the seam reports why, the registry replaces the singleton, and the
existing callers stop turning a store failure into a 500. Behaviour with the
local store is unchanged; the whole existing suite stays green.

**⚠️ No user story work can begin until this phase is complete.**

- [X] T004 Change `src/Umbral/Infrastructure/Secrets.fs` per [contracts/secret-store.md](./contracts/secret-store.md): add `StoreFailure` (`Unreachable | AccessDenied | NotFound | NotConfigured | Rejected of string`), `SecretName` (prefix, slug, discriminator) with `SecretName.derive prefix slug createdUtc` (first 8 chars of lower-case base32 `[a-z2-7]` of SHA-256 over `slug + "|" + createdUtc`, data-model.md) and the two renderings (`{prefix}-{slug}-{disc}`; Bella `UMBRAL_{PREFIX}_{SLUG}_{DISC}` upper-cased, `-`→`_`), and the new `ISecretStore` (`Id`, `Put(SecretName, plaintext, ct)`, `Get(ref, ct)`, `Delete(ref, ct)`, `Probe ct`, all `Task<Result<_, StoreFailure>>`). Adapt `LocalSecretStore`: `Put` ignores the name, decryption failure → `NotFound` (keep the key-ring comment), `Delete`/`Probe` → `Ok`. Keep `SecretRef`, `hint`, `masked`, `Protected` unchanged
- [X] T005 [P] Extend `tests/Umbral.Tests/SecretsTests.fs`: local round trip through the new `Result` API; a ciphertext from another key ring → `Error NotFound`; `SecretName.derive` is stable for the same input, differs for a different `createdUtc`, matches `^[a-z2-7]{8}$`; renderings are ≤ 74 chars for a 32-char prefix and slug, cloud rendering matches `^[a-z0-9-]+$`, Bella rendering matches `^[A-Z][A-Z0-9_]*$`; `SecretRef.Decode` of an ARN or URI locator keeps every `:` after the first
- [X] T006 Add `SecurityEvents.SecretStoreFailed` (stored `secret_store_failed`) and `ConnectorSecretMoved` (stored `connector_secret_moved`) to the union, `Value` and `allEventTypes`, plus a helper `storeDetail store op cause stale` producing the `Detail` format of data-model.md, in `src/Umbral/Infrastructure/SecurityEvents.fs`
- [X] T007 Implement `SecretStores` in `src/Umbral/Infrastructure/SecretStores/Registry.fs` with only the local store for now: `Active`, `For storeId` (`local` always; any other id → `Error NotConfigured` until US1 adds factories), `Read` (no cache yet: direct `Get`), `Evict` (no-op for now), `CanRead`. Every call links the caller's token with `CancelAfter(TimeSpan.FromSeconds 10.)` (FR-012). Register it in `src/Umbral/Program.fs` with `services.TryAddSingleton<SecretStores>` replacing the `AddSingleton<Secrets.ISecretStore>` at `Program.fs:111`
- [X] T008 [P] Write `tests/Umbral.Tests/Infrastructure/InMemorySecretStore.fs`: an `ISecretStore` with a configurable `Id`, a dictionary of name → versions, locators `mem/{rendered name}`, a call log (op, ref) for assertions, and switches to fail the next call / the Nth call / every call with a given `StoreFailure` or to hang until cancelled. Add a fixture helper in `tests/Umbral.Tests/Fixtures.fs` that registers a `SecretStores` built from given stores and an active id via `customize`, before `configureServices` runs
- [X] T009 Move the three callers to the registry with no behaviour change for `local`: `src/Umbral/Handlers/Connect/Callback.fs` uses `SecretStores.Read` and maps `Error` to today's `connector_secret_unreadable` path; `src/Umbral/Handlers/Admin/Connectors.fs` `putSecret` uses `Active.Put` and returns `Result` (callers still treat `Error` as before for now, never as an exception); `src/Umbral/Handlers/Admin/ConnectorDelete.fs` uses `For(ref.Store).Delete` with the result ignored. Run the whole suite: green

**Checkpoint**: the seam reports why, tests can inject stores, nothing visible changed.

---

## Phase 3: User Story 1 - Keep new connector secrets in my secret manager (P1) 🎯 MVP

**Goal**: with `UMBRAL__SECRET_STORE` set, create/replace writes to the manager
first, sign-in reads from it, delete removes it; the data directory holds only a
reference; a refusing store produces a message, never a 500.

**Independent test**: quickstart steps 1–3 (first bullets) and step 5.1–5.5 for
one real store.

### Tests for User Story 1

- [ ] T010 [P] [US1] `tests/Umbral.Tests/SecretStoreSettingsTests.fs`: every row of [contracts/settings.md](./contracts/settings.md) — default `local` with no variables; each invalid value refuses with a message naming the variable; `azure` without `UMBRAL__AZURE_KEY_VAULT_URI`, `aws` without `AWS_REGION`, `gcp` without `GOOGLE_CLOUD_PROJECT`, `bellabaxter` with neither key nor token, with a token but no project/env, with a key not shaped `bax-{id}-{hex}`, with an `http://` non-loopback URL; `BELLA_PROJECT`/`BELLA_ENV` used when the `BELLA_BAXTER_*` ones are absent; `BellaBaxter:*` configuration keys ignored; prefix/cache/grace validation
- [ ] T011 [P] [US1] `tests/Umbral.Tests/AwsSecretStoreTests.fs` against a fake `HttpMessageHandler` returning canned AWS JSON: create → ARN locator; create on `ResourceExistsException` → `PutSecretValue`, same reference; get; delete sends `RecoveryWindowInDays=7` and treats not found as `Ok`; `ResourceNotFoundException` → `NotFound`; `AccessDeniedException`/`UnrecognizedClientException`/`ExpiredTokenException` → `AccessDenied`; `ThrottlingException` after retries, 500 and a hung handler → `Unreachable` (hang ends by the token, not a real 10 s wait); probe on a missing name → `Ok`
- [ ] T012 [P] [US1] `tests/Umbral.Tests/GcpSecretStoreTests.fs` with a fake handler and `GoogleCredential.FromAccessToken`: create `POST …/secrets?secretId=` + `:addVersion` with base64 payload and CRC32C; create on 409 → `:addVersion` only; get `versions/latest:access` decodes base64; delete; 404 → `NotFound`; 401/403 → `AccessDenied`; 429/503/hang → `Unreachable`; locator `projects/{p}/secrets/{name}`
- [ ] T013 [P] [US1] `tests/Umbral.Tests/AzureSecretStoreTests.fs` with `SecretClientOptions.Transport` over a fake handler and a static test `TokenCredential`: set creates or adds a version; get; delete calls start-delete and never purge; 404 → `NotFound`; 401/403 and `AuthenticationFailedException` → `AccessDenied`; 409 `ObjectIsDeletedButRecoverable` → `Rejected`; 429/status 0/hang → `Unreachable`; locator is the versionless secret id
- [X] T014 [P] [US1] `tests/Umbral.Tests/BellaBaxterSecretStoreTests.fs` with the factory's `outerHandler` over a fake handler: provider resolution (single `ProviderMetaType = "Secrets"` provider used; zero or two without `UMBRAL__BELLABAXTER_PROVIDER` → `Rejected` naming that variable); write = PUT, 404 → POST, POST 409 → PUT; get unwraps `AdditionalData["secrets"][key]` per research R7; delete 404 → `Ok`; 401/403 → `AccessDenied`; 429/5xx/`BrokenCircuitException`/hang → `Unreachable`; locator `{project}/{env}/{provider}/{key}`; key rendering upper-case with `UMBRAL_` lead
- [ ] T015 [P] [US1] `tests/Umbral.Tests/ExternalSecretConnectorTests.fs`, product paths with an in-memory store registered as `aws` and `UMBRAL:SECRET_STORE=aws`: create a GitHub connector (each of the three create paths: OIDC add form, `POST /admin/connectors`, first save on an empty card) → the store holds the value under `umbral-<slug>-<8>` and the row's reference starts `aws:`; full sign-in through the stub provider succeeds; scan the data directory files (db, `-wal`, `-shm`, keys) for the plaintext and for any `local:` reference → none (SC-002); replace → new version, next sign-in sends the new value; delete → store call log shows delete; store refuses put → no row, no `ConnectorCreated`, `secret_store_failed` (`op=put`) event, `connector-secret-store-failed` notice, status not 500; store refuses delete → connector gone, `ConnectorDeleted` and `secret_store_failed` (`op=delete`), `connector-secret-left-behind` notice naming the secret; re-create a deleted slug → different name; a create that loses a slug race deletes its own write; no setting at all → reference starts `local:` and no `AWSSDK.*`/`Google.Apis.*`/`Azure.*`/`BellaBaxter.*` assembly is in `AppDomain.CurrentDomain.GetAssemblies()` after create, sign-in and delete (SC-004)

### Implementation for User Story 1

- [ ] T016 [US1] Implement `src/Umbral/Infrastructure/SecretStores/Settings.fs`: `SecretStoreSettings` (store id, prefix, cache, grace, and the active store's location: AWS region; GCP project; Azure vault URI; Bella URL, project, env, provider, auth = ApiKey | AccessToken, optional private key PEM) with `parse (config: IConfiguration)` reading `UMBRAL:SECRET_STORE`, `UMBRAL:SECRET_STORE_PREFIX`, `UMBRAL:SECRET_CACHE_SECONDS`, `UMBRAL:SECRET_GRACE_SECONDS`, `UMBRAL:AZURE_KEY_VAULT_URI`, `UMBRAL:BELLABAXTER_PROVIDER` from configuration and the vendor variables from the environment (Bella with Frunchy's precedence; never `BellaBaxter:*` keys; URL check through `BellaApiAddress.Problem`), returning every problem at once, and `describe` texts from `Texts.fs`. Call it from `requireSettings` in `src/Umbral/Program.fs` so a bad setting refuses the start and the offline commands alike. Credentials are never read into this record
- [ ] T017 [P] [US1] Implement the AWS store in `src/Umbral/Infrastructure/SecretStores/Aws.fs` per research R4: `AmazonSecretsManagerClient` with region from settings (or from the ARN when reading another region's reference), default credential chain, `MaxErrorRetry = 2`, an injectable `HttpClientFactory`/`ServiceURL` for tests; error mapping table of R4; never catch-all into `Rejected` without the exception type name in the reason (no message text)
- [ ] T018 [P] [US1] Implement the GCP store in `src/Umbral/Infrastructure/SecretStores/Gcp.fs`: `GoogleCredential.GetApplicationDefaultAsync()` scoped to `https://www.googleapis.com/auth/cloud-platform`, token per request with the call's token, an injectable `HttpClient`, four REST calls with `System.Text.Json`, CRC32C on add, mapping of R4
- [ ] T019 [P] [US1] Implement the Azure store in `src/Umbral/Infrastructure/SecretStores/Azure.fs`: `SecretClient(vaultUri, ChainedTokenCredential(EnvironmentCredential(), WorkloadIdentityCredential(), ManagedIdentityCredential(id)))` where `id` is user-assigned from `AZURE_CLIENT_ID` when no client secret is set, else system-assigned; `Retry.MaxRetries = 2`; injectable transport and credential; read of a reference to another vault builds a client for the vault in the locator; mapping of R4; never call purge
- [X] T020 [P] [US1] Implement the BellaBaxter store in `src/Umbral/Infrastructure/SecretStores/BellaBaxter.fs`: one client built once through `BellaClientFactory.CreateWithHmacApiKey`/`CreateWithBearerToken` (the `…AndZke` variants with `ZkeDekHandler` over `ECDiffieHellman.ImportFromPem` when a private key is set), `appClient = "umbral"`, injectable `outerHandler`; project/env from settings or `Api.V1.Keys.Me` once; provider resolved once and cached (R7); upsert PUT→POST→PUT; read unwrapping per R7; mapping of R4
- [ ] T021 [US1] Complete `src/Umbral/Infrastructure/SecretStores/Registry.fs`: factories per store id built lazily on first `For`/`Active` use (so a local-only instance loads no vendor assembly), `Active` from settings, `For` builds a reader for a non-active store from credentials plus the locator (R2) or returns `NotConfigured`; `CanRead` without network. Build the registry from `SecretStoreSettings` in `src/Umbral/Program.fs` (still `TryAddSingleton`)
- [ ] T022 [US1] Make `ConnectorStore.create` take the `now` it writes as `created_utc` (callers pass `clock.GetUtcNow()` once) in `src/Umbral/Infrastructure/ConnectorStore.fs`; no other change to its SQL
- [ ] T023 [US1] Rewrite the secret handling of the three create paths and the replace path in `src/Umbral/Handlers/Admin/Connectors.fs` per research R9: take `now` once; `SecretName.derive prefix slug now`; for replace use the row's `created_utc` and, when the reference is already in the active store, write a new version and keep the reference, otherwise write to the active store, switch the reference, then delete the old copy (best effort, warning notice on failure); `Error` from `Put` → `secret_store_failed` event + `connector-secret-store-failed` (OIDC add form re-renders inline as today; the other paths redirect with the code); a create that loses the race deletes its own write; `Evict` after any replace. Add the notice texts and codes to `src/Umbral/Views/Texts.fs` (`noticeEn`, `knownNotice`) with the store's display name and what to check
- [ ] T024 [US1] In `src/Umbral/Handlers/Admin/ConnectorDelete.fs`: delete the row and record `ConnectorDeleted` first, then `For(ref.Store).Delete` with its `Result`; on `Error` record `secret_store_failed` (`op=delete`) and redirect with `connector-secret-left-behind`, the secret's rendered name shown from server-side state (never from the URL); `Evict` the reference
- [ ] T025 [US1] In `src/Umbral/Handlers/Connect/Callback.fs`: on `Error cause` record `secret_store_failed` (`op=get`, actor User) and `SignInFailed` with `Detail = connector_secret_unreadable;store=<id>;cause=<cause>` (FR-015); the person still sees `sign-in-failed`
- [ ] T026 [US1] Run T010–T015 and the full suite: all green

**Checkpoint**: MVP — an instance can keep new secrets in any of the four managers.

---

## Phase 4: User Story 2 - Sign-ins keep working when the manager has a bad moment (P1)

**Goal**: memory-only cache (300 s) with a grace period (3 600 s) for an
unreachable manager, immediate failure for not found / denied.

**Independent test**: quickstart step 3, cache bullets.

### Tests for User Story 2

- [ ] T027 [P] [US2] `tests/Umbral.Tests/SecretCacheTests.fs` with `FakeTimeProvider` and the in-memory store: fresh hit makes no call; expired + store ok refreshes; expired + `Unreachable` within grace serves stale and records `stale=true`, a second sign-in 10 s later makes no extra call and no extra event, one 31 s later retries; past lifetime + grace fails; `NotFound`/`AccessDenied` on refresh evicts and fails at once; `Evict` forces the next read to call; cache 0 calls every time; grace 0 fails on the first expired outage; local references never go through the cache
- [ ] T028 [P] [US2] Extend `tests/Umbral.Tests/ExternalSecretConnectorTests.fs`: end to end through `/connect/...` callback — sign in, store down, advance 301 s → sign-in succeeds with `secret_store_failed … stale=true`; advance past 3 900 s → `sign-in-failed` with `cause=unreachable`; replace the secret → next sign-in uses the new value regardless of cache; a new app host over the same data directory (restart) has an empty cache (first sign-in calls the store)

### Implementation for User Story 2

- [ ] T029 [US2] Add the cache to `src/Umbral/Infrastructure/SecretStores/Registry.fs` per research R6 and the state diagram in data-model.md: `ConcurrentDictionary<string, Entry>` keyed by encoded reference, value as `Secrets.Protected`, `fetchedAt`, `lastFailedRefreshAt`, time from the registered `TimeProvider`; external stores only; `Read` records `secret_store_failed` with `stale=true` through a callback the registry is given (so Registry.fs does not depend on handlers); `Evict` implemented; nothing written to disk or logged
- [ ] T030 [US2] Run T027, T028 and the full suite: all green

**Checkpoint**: US1 + US2 is the complete P1 delivery.

---

## Phase 5: User Story 3 - Move an existing instance's secrets, in and back out (P2)

**Goal**: `move-secrets [--dry-run]` per [contracts/move-secrets-cli.md](./contracts/move-secrets-cli.md).

**Independent test**: quickstart step 4.

### Tests for User Story 3

- [ ] T031 [P] [US3] `tests/Umbral.Tests/SecretMoveTests.fs`, in process: three local connectors move to an in-memory `aws` and back to `local`, each signs in after each move, source copies deleted; for every N from 1 to the total number of store calls, a store failing on its Nth call then a clean re-run leaves all three readable, no reference to a missing secret, and exactly one secret per connector in the target; a concurrent change of one row between read and switch → that connector `skipped`, row untouched; dry run makes no write or delete call and prints `would move`; second run prints only `skipped` and returns 0; one failure returns 1; a source delete failure prints `warning` and returns 0; one `connector_secret_moved` event per moved connector, none for skipped
- [ ] T032 [P] [US3] Extend `tests/Umbral.Tests/SubcommandTests.fs`: the built `Umbral.dll move-secrets --dry-run` with only environment variables on a temp data directory exits 0 and prints the summary; with `UMBRAL__SECRET_STORE=nonsense` it exits non-zero with the settings message before any connector line

### Implementation for User Story 3

- [ ] T033 [US3] Add `switchSecretRef dbConfig clock slug oldRef newRef event` to `src/Umbral/Infrastructure/ConnectorStore.fs`: one transaction with `UPDATE connectors SET client_secret_ref = @new, updated_utc = @now WHERE slug = @slug AND client_secret_ref = @old` and `SecurityEvents.recordIn` of the given event; returns whether a row changed
- [ ] T034 [US3] Implement `src/Umbral/Infrastructure/SecretMove.fs`: `run (registry) (dbConfig) (clock) (dryRun) (write: string -> unit) : Task<int>` doing research R8 steps 1–5 per connector in slug order, output lines and exit codes exactly as the contract, all texts from `Texts.fs`
- [ ] T035 [US3] Dispatch `move-secrets` in `main` of `src/Umbral/Program.fs` next to `reset-credential` (`buildApp`, schema ensured, no `initialize`, no `Run`), passing `--dry-run` through
- [ ] T036 [US3] Run T031, T032 and the full suite: all green

**Checkpoint**: existing instances can adopt and leave a manager.

---

## Phase 6: User Story 4 - Know where my secrets are, and why something fails (P3)

**Goal**: store label and unreadable warning on each card, startup probe and
page notice.

**Independent test**: quickstart step 3, last three bullets.

### Tests for User Story 4

- [ ] T037 [P] [US4] Extend `tests/Umbral.Tests/ExternalSecretConnectorTests.fs`: each card has `data-secret-store` matching its reference and the `Texts` label; a row whose reference is `azure:` on an instance that cannot build Azure has `data-secret-unreadable` with what to do; startup with an active store whose probe returns `Unreachable` or `AccessDenied` starts, logs a warning, records `secret_store_failed` (`op=probe`, actor System) and shows `data-secret-store-problem`; a following successful save clears it; a probe answering not found shows nothing; `/healthz` stays 200 with the store down

### Implementation for User Story 4

- [ ] T038 [US4] Add `SecretStoreStatus` (singleton, `Ok | Failed of StoreFailure * at`) to `src/Umbral/Infrastructure/SecretStores/Registry.fs`, set by the probe and by any successful `Put` on the active store; run `Active.Probe` in `initialize` in `src/Umbral/Program.fs` after the schema, under the 10 s bound, never failing the start, logging a warning whose text says what to check
- [ ] T039 [US4] Card label (`Components.pill` next to the existing header badges), card warning (`um-alert um-alert-warning`, `role="note"`, `data-secret-unreadable`) and page notice (next to `originBanner`, `data-secret-store-problem`) in `src/Umbral/Handlers/Admin/Connectors.fs`, texts in `src/Umbral/Views/Texts.fs` per [contracts/admin-surface.md](./contracts/admin-surface.md)
- [ ] T040 [US4] Run T037 and the full suite: all green

---

## Phase 7: Polish & cross-cutting

- [ ] T041 [P] Extend `tests/Umbral.Tests/LogRedactionTests.fs` with a journey through an in-memory `aws` store: create, sign in, replace, outage served from grace, failed delete, move to `local`; assert the secret (old and new) appears in no captured log line at any level and no `security_events` row (SC-007)
- [ ] T042 [P] Ensure `tests/Umbral.Tests/SecurityEventCoverageTests.fs` passes with the two new kinds emitted by product paths (add the driving steps if the existing journey does not reach them)
- [ ] T043 [P] Extend `tests/Umbral.Tests/IdleMemoryTests.fs` with a second process run: `UMBRAL__SECRET_STORE=aws`, `AWS_REGION=eu-west-1`, static fake `AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY`, `AWS_EC2_METADATA_DISABLED=true`, `AWS_ENDPOINT_URL_SECRETS_MANAGER` pointing at a minimal local stub that answers the probe with `ResourceNotFoundException`; same 150 MiB Linux ceiling (SC-006); add it to the CI idle-memory filter in `.github/workflows/ci.yml` if that job filters by class
- [ ] T044 [P] Write `docs/secret-stores/aws.md` in the style of `docs/connectors/*.md`: back link, create nothing but grant the R10 IAM policy (prefix `umbral-*`), variables, `AWS_EC2_METADATA_DISABLED` off AWS, move in and out with `move-secrets`, each startup message and its fix, recovery window note
- [ ] T045 [P] Write `docs/secret-stores/gcp.md`: enable the API, the custom role with unconditional `secretmanager.secrets.create` plus the prefix condition on the rest using the project number (and the advice to test create), mounting the key file, variables, move in and out, messages
- [ ] T046 [P] Write `docs/secret-stores/azure.md`: a dedicated vault, `Key Vault Secrets Officer` at vault scope, service principal or managed identity variables, `UMBRAL__AZURE_KEY_VAULT_URI`, soft-delete note, move in and out, messages
- [ ] T047 [P] Write `docs/secret-stores/bellabaxter.md`: the same `BELLA_BAXTER_*` variables as Frunchy, a dedicated environment advised (or the `UMBRAL_` prefix and why), `UMBRAL__BELLABAXTER_PROVIDER`, zero-knowledge `BELLA_BAXTER_PRIVATE_KEY`, move in and out, messages
- [ ] T048 Update `README.md`: walkthrough links next to the connector ones; Settings reference rows for every `UMBRAL__SECRET_*`, `UMBRAL__AZURE_KEY_VAULT_URI`, `UMBRAL__BELLABAXTER_PROVIDER` with a pointer to the walkthroughs for vendor variables (FR-023); "Your data: back it up" says that with an external store connector secrets are not in `/data` and a restore needs the same manager, and that two instances with the same prefix share secrets (FR-024); `move-secrets` next to "Forgotten operator credential"; correct "app secrets are encrypted with the key ring" to "hashed" (README around lines 453–456 and 521–523) and the same claim in the `Dockerfile` comment above `/data`
- [ ] T049 Run quickstart steps 1–4 and the full `dotnet test -c Release`; record the test count in this file's notes
- [ ] T050 Manual, needs accounts: quickstart step 5 for each store and step 6 for BellaBaxter; record results (and any fixture corrections) in `research.md` R7 and the walkthroughs; time steps 1–3 for SC-001

---

## Dependencies & execution order

### Phases

- Setup (T001–T003) → Foundational (T004–T009) → user stories → Polish.
- **US1** depends on Foundational only.
- **US2** depends on US1's registry factories (T021) and callback change (T025).
- **US3** depends on US1 (T021, T022); independent of US2 except that eviction (T029) is a no-op until US2 lands, which the move tolerates.
- **US4** depends on US1 (T016, T021); independent of US2 and US3.
- Polish docs (T044–T048) can start once US1's settings (T016) are fixed; T041–T043 after US2 and US3.

### Within phases

- T004 before T005–T009. T007 before T008's fixture helper and T009.
- T016 before T017–T021. T017–T020 in parallel. T021 after them. T022 before T023. T023–T025 after T021.
- Tests (T010–T015, T027–T028, T031–T032, T037) are written first and fail first.

### Parallel opportunities

```text
Setup:        T003 alongside T001–T002
Foundational: T005, T006, T008 in parallel after T004
US1 tests:    T010, T011, T012, T013, T014, T015 in parallel
US1 stores:   T017 (AWS), T018 (GCP), T019 (Azure), T020 (Bella) in parallel after T016
US2 tests:    T027, T028
US3 tests:    T031, T032
After US1:    US3 and US4 can proceed side by side (different files except Program.fs: serialise T035 and T038)
Polish:       T041–T047 in parallel
```

Only one writing agent at a time touches `Program.fs`, `Connectors.fs`,
`Registry.fs` or `Texts.fs`; the four store files and the four doc files are
the safe places to fan out.

## Implementation strategy

1. **MVP = Foundational + US1**: secrets go to a manager and come back; a
   failing store is a message, not a 500. Shippable for a fresh instance with a
   reliable manager.
2. **+ US2**: the P1 promise is complete; ship this as the release unit.
3. **+ US3**: existing instances can adopt it.
4. **+ US4 and Polish**: supportable by an operator alone; docs make it done
   (Principle VI: not done without them).

## Notes

- Commit after each task or logical group; the plan's step order (plan.md
  "Implementation order") is the same as the phase order here.
- Every task that adds an event, a notice or a text adds it in the file named
  and nowhere else.
