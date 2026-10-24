# Tasks: The Fediverse connector

**Input**: [fediverse-connector.spec.md](./fediverse-connector.spec.md), [plan.md](./plan.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/routes-and-screens.md](./contracts/routes-and-screens.md)

> **Scale note.** About 30 files in five areas. F# compiles in order and the
> test project shares `obj/bin`, so tasks that edit F# run one at a time even
> where a wave allows otherwise; `[P]` marks logical independence. Two files
> are touched by more than one phase on purpose, one line each: `Program.fs`
> (an endpoint line) and the two `.fsproj` files (a `Compile` line), added by
> the task that creates the module.

Tests are required (constitution: every connector testable without real
credentials; protocol behaviour through the real endpoints). Tests are written
with or before the code they cover and must fail without it.

## Phase 1: Setup

- [x] **T001** Create `tests/Umbral.Tests/Infrastructure/StubMastodon.fs`: an `HttpMessageHandler` that plays one or more fake servers by host (Mastodon 4.3 mode with OAuth metadata; GoToSocial mode with NodeInfo only; Akkoma mode, NodeInfo `akkoma`, no metadata; Misskey mode; down mode that times out or returns 500; WebFinger answers per handle; `POST /api/v1/apps`; `/oauth/token` for `authorization_code` and `client_credentials`, checking `code_verifier` against the challenge it saw; `verify_credentials`; a switch that makes the current client unknown (`invalid_client`)); request log and counts. Add it to `tests/Umbral.Tests/Umbral.Tests.fsproj` after `StubOidcProvider.fs` · tests/Umbral.Tests/Infrastructure/StubMastodon.fs

## Phase 2: Foundational (blocks every story)

**Wave 1 — independent (different files):**

- [x] **T002** [P] `ProviderKind.Fediverse` (`Value` `fediverse`, `DisplayName` `Fediverse`, `parseKind`, an empty `endpointsFor` entry, `setupCaveat` None) and `FediverseAccess = { OnlyListed: bool; Servers: string list }` as a field of `Connector`; fix every `Connector` record literal in `src/` and `tests/` the compiler names (`FediverseAccess = FediverseAccess.Any` or an equivalent default value), and every exhaustive match on `ProviderKind` (`mayCreate` → false, `EmailDomainRules.ruleFor` → `NoRule`, Picker label → default arm) · src/Umbral/Domains/Connectors.fs
- [x] **T003** [P] Make the domain check in `EmailDomains` public (a `normaliseDomain : string -> string option` returning the ASCII, lower-case, validated domain, or the existing `isDomain`), with no behaviour change to `parse` · src/Umbral/Domains/EmailDomains.fs

**⟶ Wait for Wave 1 to finish, then:**

**Wave 2:**

- [x] **T004** Create the pure module (research R5, R1, R3, R10): `parseInput allowDev text` → `Handle(user, domain) | Server host | NotAServer`; `normaliseServer allowDev text` → `Result<host, ServerEntryError>` (`NotAServer entry | Wildcard entry`); `parseServers allowDev text` (commas or spaces, de-duplicated, first refusal fails all); `allows access host`; `isPublicAddress (IPAddress)` (refuse loopback, RFC 1918, link-local, CGNAT 100.64/10, ULA fc00::/7, multicast, unspecified, IPv4-mapped forms, 0.0.0.0/8, benchmark 198.18/15, documentation ranges); `support` rule from parsed metadata or NodeInfo software name; `fediverseSubject host id` and `parseFediverseSubject`; `scopeValue`/`parseScope` (`any`/`listed`). Add to `src/Umbral/Umbral.fsproj` after `Domains/EmailDomains.fs` · src/Umbral/Domains/Fediverse.fs
- [x] **T005** Tests for T004, every branch: the four accepted input forms, upper case, trailing dot and slash, `:443`, other ports, paths, IDN, IP literals, `localhost`, wildcards, the development allowance; `isPublicAddress` over each range; the support rule (Mastodon metadata with and without S256 or registration endpoint, GoToSocial, Akkoma, Pleroma, Misskey, Sharkey, unknown); subject round trip; `allows` with an empty list · tests/Umbral.Tests/FediverseDomainTests.fs

**⟶ Wait for Wave 2 to finish, then:**

**Wave 3:**

- [x] **T006** Schema version 7 (data-model "Schema version 7"): `fediverse_servers` table, `connectors.fediverse_scope`, `connectors.fediverse_servers`, `users.preferred_username`, `users.profile_url` in `schema` and in `UpgradeToVersion7`; `SchemaVersion = 7` · src/Umbral/Infrastructure/Database.fs
- [x] **T007** Read and write `FediverseAccess` on connector rows (`Columns`, the row reader, `save`/`create`/`update`), plus `setFediverseAccess config clock slug access` · src/Umbral/Infrastructure/ConnectorStore.fs
- [x] **T008** `ProviderProfile.Username` and `ProfileUrl` (None for every existing adapter); `Users.User.PreferredUsername` and `ProfileUrl` read from the new columns; `UserStore.resolve` writes them on insert and refreshes them with `COALESCE` on update · src/Umbral/Infrastructure/ProviderAdapters.fs, src/Umbral/Infrastructure/Users.fs, src/Umbral/Infrastructure/UserStore.fs, the adapters' profile literals
- [x] **T009** `Correlation.Server` (research R9): seven fields encoded, six or seven decoded (six → `Server = ""`), `start` takes `server`; `Connect/Start.fs` passes `""` · src/Umbral/Infrastructure/OAuthRoundTrip.fs, src/Umbral/Handlers/Connect/Start.fs
- [x] **T010** Security event type `FediverseServerRegistered` (`fediverse_server_registered`) in the type, its `Value` and `allEventTypes` · src/Umbral/Infrastructure/SecurityEvents.fs
- [x] **T011** Every catalogue string of contract section 5, with the keys proposed there, plus the two notices (`fediverse-connector-saved`, `fediverse-connector-invalid`) in `noticeEn`/`knownNotice` · src/Umbral/Views/Texts.fs
- [x] **T012** Tests: a version-6 database upgrades to 7 with defaults; connector rows round-trip `FediverseAccess`; a correlation with a server round-trips and a six-field payload still decodes; existing suites still pass · tests/Umbral.Tests/DatabaseTests.fs, tests/Umbral.Tests/OAuthRoundTripTests.fs, tests/Umbral.Tests/OidcConnectorStoreTests.fs

**Checkpoint**: the solution builds and the whole existing suite passes with the new kind, columns and fields in place.

## Phase 3: User Story 1 - Sign in to an app with a fediverse account (P1, MVP)

Files: src/Umbral/Infrastructure/FediverseServers.fs, src/Umbral/Infrastructure/Providers/Fediverse.fs, src/Umbral/Handlers/Connect/Callback.fs, src/Umbral/Handlers/Connect/Fediverse.fs, src/Umbral/Handlers/Connect/Authorize.fs, src/Umbral/Handlers/Connect/UserInfo.fs, src/Umbral/Infrastructure/OpenIddictSetup.fs, Program.fs (client and endpoint lines)

### Tests

- [x] **T013** [US1] End to end through the real endpoints with `StubMastodon`: picker shows "Sign in with Fediverse" and links to the step; GET step renders the form (field `account`, hidden `returnUrl`, antiforgery, the attributes of contract section 4); POST a handle registers once (`client_name` Umbral, the exact callback, `read:accounts`), stores the row with a protected secret, records `fediverse_server_registered`, redirects with `code_challenge_method=S256`, the state and `scope=read:accounts`; the callback exchanges with the verifier at the same server and signs in; the app's OIDC flow returns `name`, `preferred_username` (`user@host`), `picture`, `profile`, no `email`; a second person from the same server reuses the registration (one `POST /api/v1/apps`); a second sign-in after a display-name change is the same `sub` with the new name; a reused or mismatched state fails; a server-mode GoToSocial sign-in works; a handle on a split domain is resolved by WebFinger · tests/Umbral.Tests/FediverseSignInTests.fs

### Implementation

**Wave 1:**

- [x] **T014** [US1] Registration store (data-model "Server registration"): `find`, `insertIfAbsent` (`ON CONFLICT(host) DO NOTHING` then read back), `replace`, `delete`, `markChecked`, `markUsed`; the secret protected with purpose `Umbral.FediverseServers.v1` and returned as `Secrets.Protected`. Add to `Umbral.fsproj` after `ConnectorStore.fs` · src/Umbral/Infrastructure/FediverseServers.fs
- [x] **T015** [US1] The server client (research R2, R6, R8; contract section 2): named client `umbral-fediverse`; `PrimaryHandler` delegate with the default guarded `SocketsHttpHandler` (no redirects, 5 s connect, `ConnectCallback` keeping public addresses unless the development allowance); per-call 10 s; 64 KiB bounded reads; `lookup` (WebFinger), `discover` (metadata, then NodeInfo), `register`, `checkClient`, `authorizeUrl` (pure), `exchange`, `account`. Errors as a union the handler maps to the reasons of contract section 6 · src/Umbral/Infrastructure/Providers/Fediverse.fs

**⟶ Wait for Wave 1 to finish, then:**

**Wave 2:**

- [x] **T016** [US1] Factor the sign-in tail of `Callback.handler` (from "profile in hand" to the redirect) into a public `finishSignIn` taking an email-rule switch, with `Callback.handler` calling it unchanged in behaviour · src/Umbral/Handlers/Connect/Callback.fs
- [x] **T017** [US1] Claims: `preferred_username` and `profile` set (or removed) in `setProfileClaims` and in userinfo under the `profile` scope; `Claims.Profile` in the profile destinations and in `claims_supported` · src/Umbral/Handlers/Connect/Authorize.fs, src/Umbral/Handlers/Connect/UserInfo.fs, src/Umbral/Infrastructure/OpenIddictSetup.fs
- [x] **T018** [US1] Register the `umbral-fediverse` client and the default `Fediverse.PrimaryHandler` in `configureServices` (development allowance from the existing `allowInsecureHttp`) · src/Umbral/Program.fs

**⟶ Wait for Wave 2 to finish, then:**

**Wave 3:**

- [x] **T019** [US1] The handlers (research R13; contract sections 1 and 4): GET `/connect/fediverse/start` renders the step in the picker's chrome (404 when the connector is absent or disabled); POST validates antiforgery, parses, checks address, scope, support, registers or reuses (re-registering once when the stored `redirect_uri` is not the current callback, or when the 24 h check says `invalid_client`), starts the round trip with the server, redirects; every fixable problem re-renders with 422, the typed value, `aria-invalid`, the message, `autofocus`; GET `/connect/fediverse/callback` completes the round trip, re-checks scope (`account-not-allowed`), exchanges at the correlation's server (deleting the row on `invalid_client`), reads the account, builds the profile, calls `finishSignIn` with no email rule; every failure in the security log with the reason and server; all three routes on the `sign-in` rate limit. Add to `Umbral.fsproj` after `Connect/Callback.fs` and the endpoint list to `Program.fs` · src/Umbral/Handlers/Connect/Fediverse.fs

**Checkpoint**: with the connector row inserted directly, a person signs in through a fake server and an app receives them (T013 passes).

## Phase 4: User Story 2 - The operator turns it on and chooses servers (P1)

Files: src/Umbral/Handlers/Admin/FediverseConnector.fs, src/Umbral/Handlers/Admin/Connectors.fs, src/Umbral/Handlers/Admin/Apps.fs

### Tests

- [ ] **T020** [US2] Admin: list order (configured providers, not-set-up kinds, `fediverse`, `passkey`, `domains`) and the muted entry when not set up; the pane before set-up (lead, callback with copy, no-email note, Any server checked, Servers field, Enable Fediverse) and after (state, Disable, Delete, last-connector warnings); data hooks of contract section 4; save creates enabled; `listed` with `Social.Example.org, fosstodon.org` stores normalised; a wildcard or a non-address re-renders 422 naming the entry and saves nothing; empty list under `listed` shows the empty note; unsharing that strands an app asks to confirm; bad `scope` word → `fediverse-connector-invalid`. Sign-in side: a listed server signs in, an unlisted one gets "Accounts on {server} cannot sign in here." with no request sent; switching to listed between the step and the callback refuses at the callback with `account-not-allowed`. Update the order assertion in `ConnectorsMasterDetailTests.fs` · tests/Umbral.Tests/FediverseAdminTests.fs, tests/Umbral.Tests/ConnectorsMasterDetailTests.fs

### Implementation

**Wave 1:**

- [ ] **T021** [US2] The pane and its save handler, mirroring `PasskeyConnector.fs` (design contract section 1, contract sections 1 and 4): heading with pills; Set-up section (lead, callback URL with copy, no-email note); Settings form (shared check, `scope` radios as `.um-choice` cards in `fieldset[data-instance-scope]`, the `servers` field with `data-instance-list`, the empty-list note, Save or Enable Fediverse); the action row (toggle, delete) when set up; POST `/admin/connectors/fediverse` with the 422 re-render for a refused entry. Add to `Umbral.fsproj` after `PasskeyConnector.fs` · src/Umbral/Handlers/Admin/FediverseConnector.fs. *Deviation (2026-10-24): this file holds the pane only; the save handler lives in `Connectors.fs` (T022), because a refused server list is drawn again through the screen's render and its `Drafts.Card`, which compile after this file.*

**⟶ Wait for Wave 1 to finish, then:**

**Wave 2:**

- [ ] **T022** [US2] The Connectors screen: `rowsFor` puts Fediverse after the not-set-up kinds and before Passkey and keeps it out of the generic stored list; the pane dispatch calls `FediverseConnector.pane`; the instance-domains removal warning skips Fediverse; the save handler `POST /admin/connectors/fediverse` (literal route in `Connectors.endpoint`), with the 422 re-render of a refused list through `refuseDomains` and a `Drafts.Card` for slug `fediverse` · src/Umbral/Handlers/Admin/Connectors.fs
- [ ] **T023** [US2] [P] The Apps screen counts a Fediverse connector as limited exactly when `OnlyListed` (research R11) · src/Umbral/Handlers/Admin/Apps.fs

**Checkpoint**: the operator enables the connector from the screen and narrows it; T013 and T020 pass.

## Phase 5: User Story 3 - Fixable problems on the account step (P2)

Implemented by T015 and T019 (US1 owns those files). This phase proves each message.

Files: none beyond its tests.

### Tests

- [ ] **T024** [US3] Each fixable problem re-renders 422 with the typed value, `aria-invalid`, `autofocus` and the exact `Texts.fs` line: not a handle; Akkoma, Pleroma, Misskey, Mastodon without S256 and without a registration endpoint (unsupported, server named); a down server and a timeout ("did not answer"); `127.0.0.1`, `localhost`, `10.0.0.5`, a name resolving to a private address (not a handle; no request reaches the stub); an endpoint in the metadata on another host (unsupported); the access denial at the server lands on `/signin?msg=access-denied`; a missing or bad antiforgery token is refused. A unit test drives the real guarded handler against a loopback listener and shows the connection refused, and allowed with the development allowance · tests/Umbral.Tests/FediverseStepErrorsTests.fs

**Checkpoint**: every message of contract section 5 is reached by a test.

## Phase 6: User Story 4 - A server forgets Umbral (P3)

Implemented by T015 and T019. This phase proves it.

Files: none beyond its tests.

### Tests

- [ ] **T025** [US4] With a stored registration checked more than 24 hours ago (fake clock) that the stub no longer knows: the check gets `invalid_client`, Umbral registers once, replaces the row, records the event, and the sign-in completes; when the new registration is refused too, the step says "did not answer" and only one re-registration was attempted; a registration checked within 24 hours is not checked; a registration stored with another callback address (public origin changed) is replaced without a check; `invalid_client` at the code exchange deletes the row and the next attempt registers again · tests/Umbral.Tests/FediverseReRegistrationTests.fs

**Checkpoint**: FR-013 holds.

## Phase 7: Polish

**Wave 1 — independent (different files):**

- [ ] **T026** [P] Walkthrough per T10 of `docs/audit-2026-10.md` (on branch `worktree-docs-audit-2026-10`): what it is and which servers work (Mastodon 4.3+, GoToSocial; Akkoma, Pleroma, Misskey, Sharkey refused and why), Before you start, the steps (nothing to create at a provider; what Umbral registers and stores where), how a person signs in, the server list and its refusal, What people see, What Umbral takes, What the server learns, Allowed email domains, Troubleshooting, Gotchas; every quoted UI string verbatim from `Texts.fs` · docs/connectors/fediverse.md
- [ ] **T027** [P] README (opening, connector table, contents link, "Who can sign in", the `idp` value) and the CHANGELOG entry under `## Unreleased` · README.md, CHANGELOG.md

**⟶ Wait for Wave 1 to finish, then:**

**Wave 2:**

- [ ] **T028** Regenerate `umbral.css` with `build-css.sh` if any view gained a class, and check it is unchanged otherwise · src/Umbral/Assets/umbral.input.css, src/Umbral/Assets/umbral.css
- [ ] **T029** Validate against the Success Criteria: full `dotnet test`, the link check, idle memory with the connector enabled (SC-007), and a browser walk of the step and pane; record figures in the review notes · .alta/reviews/2026-10-23-fediverse-connector/notes.md
- [ ] **T030** Design review captures (design contract section 5) and doc review notes (audit section 6); send to `umbral-designer` and `umbral-documentation`; answer their reviews · .alta/reviews/2026-10-23-fediverse-connector/

## Dependencies & Execution Order

- Setup (T001) → Foundational (T002–T012) → US1 (T013–T019) → US2 (T020–T023) → US3 (T024) and US4 (T025) → Polish (T026–T030).
- Foundational: Wave 1 (T002, T003) blocks Wave 2 (T004, T005), which blocks Wave 3 (T006–T012).
- US1: Wave 1 (T014, T015) blocks Wave 2 (T016–T018), which blocks Wave 3 (T019); T013 is written first and passes after T019.
- US2: T021 blocks T022 and T023; T020 passes after T022.
- US3 and US4 only add tests and can run in either order after US2.
- Polish: T026 and T027 can start as soon as US2 is done (strings are final); T028–T030 last.
