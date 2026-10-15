---
description: "Task list for 006 allowed email domains"
---

# Tasks: Allowed Email Domains, per Instance or per Connector

**Input**: Design documents from `specs/006-allowed-email-domains/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included. The constitution requires every sign-in behaviour to be
tested through the real endpoints without provider credentials (FR-025), and
SC-001, SC-004, SC-005 and SC-006 are test outcomes. Within each story, tests
come first and must fail before the task that makes them pass.

**Organization**: by user story, after a foundation that adds the rule and its
storage with no behaviour change.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1–US5 from spec.md
- F# compile order matters: every new `.fs` file needs its `<Compile Include>`
  in the right place in `src/Umbral/Umbral.fsproj` or
  `tests/Umbral.Tests/Umbral.Tests.fsproj`.

## Conventions every task follows

- Run `dotnet build -c Release` (warnings are errors) and the tests the task
  names; the full suite is `dotnet test -c Release`.
- Every operator-facing or end-user string lives in `src/Umbral/Views/Texts.fs`;
  every operator message says what to do next (Principle VI).
- Every SQL text is a literal or a `[<Literal>]` constant (MetaTests).
- No full email address in any security event, log line or page. End-user
  pages never show a listed domain or the refusal reason.
- An instance with no list behaves exactly as before: existing tests are not
  edited to pass, except where a record gains a field.

---

## Phase 1: Setup

- [X] T001 Create empty modules and register them: `src/Umbral/Domains/EmailDomains.fs` directly after `Domains/Linking.fs`, `src/Umbral/Infrastructure/EmailDomainRules.fs` directly after `Infrastructure/ConnectorStore.fs` in `src/Umbral/Umbral.fsproj`; `tests/Umbral.Tests/EmailDomainsTests.fs`, `AllowedDomainsSignInTests.fs`, `AllowedDomainsAdminTests.fs` in `tests/Umbral.Tests/Umbral.Tests.fsproj` (before `SmokeTests.fs`)

---

## Phase 2: Foundational (blocking)

**Purpose**: the amendment that makes the feature in scope, the pure rule, its
storage and its event shapes. No behaviour changes; the suite stays green.

- [ ] T002 Amend governance per spec FR-002: in `.specify/memory/constitution.md` add under Principle VII a clarification that limiting, per connector or per instance, which verified email domains may sign in decides who can be an Umbral user (like disabling a user) and is not an authorization model, while any per-app rule on who may use an app, or any rule on what a user may do, remains excluded; bump to 2.1.0 (MINOR, materially expanded guidance), set Last Amended 2026-10-15, rewrite the sync impact report. In `specs/001-umbral-mvp/spec.md` add to FR-025 an "**Amended by [006](../006-allowed-email-domains/spec.md).**" sentence saying the operator may limit sign-in to verified email domains per connector or per instance, never per app
- [ ] T003 [P] Write `tests/Umbral.Tests/EmailDomainsTests.fs` for the data-model's `parse`, `applying`, `domainOf`, `check`: separators (comma, space, newline), leading `@`, trailing `.`, upper case, duplicates, IDN (`münchen.de` ≡ `xn--mnchen-3ya.de`), `*` → `Wildcard`, `localhost` / `1.2.3.4` / `-a.com` / 64-char label → `NotADomain`, empty input → `Ok []`; `applying` connector-over-instance and `NoRule`; `domainOf` with several `@`, none, nothing after; `check` order no email → not verified → domain, subdomain refused, case ignored, `NoRule` always admits
- [ ] T004 Implement `src/Umbral/Domains/EmailDomains.fs` per [data-model.md](./data-model.md) and research R2, R3 (pure; `IdnMapping`; plus `display : string list -> string` in Unicode joined by `, ` and `serialise`/`deserialise` to the one-space stored form) until T003 passes
- [ ] T005 Add schema version 4 in `src/Umbral/Infrastructure/Database.fs` (research R4): `allowed_email_domains TEXT NOT NULL DEFAULT ''` in the `CREATE TABLE connectors`, `UpgradeToVersion4` literal appended to `upgradeSteps`, `SchemaVersion = 4`; extend `tests/Umbral.Tests/DatabaseTests.fs` with a v3 database (connector rows present) upgraded to v4 with every list empty
- [ ] T006 Add `AllowedEmailDomains: string list` to `Connector` in `src/Umbral/Domains/Connectors.fs` and read/write it in every query of `src/Umbral/Infrastructure/ConnectorStore.fs` (`Columns`, `read`, `save`, `create`, `update`); update every `Connector` record construction in `src/` and `tests/` with `AllowedEmailDomains = []`
- [ ] T007 Implement `src/Umbral/Infrastructure/EmailDomainRules.fs`: `instanceList config`, `setInstanceList config clock list` (settings key `allowed_email_domains`, SQL literals), `ruleFor config connector` (= `applying` with the instance list), `anyListSet config` (instance or any connector non-empty)
- [ ] T008 In `src/Umbral/Infrastructure/SecurityEvents.fs` add `AllowedDomainsChanged` (`allowed_domains_changed`) to the union, `Value` and `allEventTypes`, and a `refusalDetail reason level stage domain` builder producing `email_domain_refused;reason=…;rule=…;stage=…;domain=…` from closed words and the normalised domain only (research R10)

**Checkpoint**: `dotnet test -c Release` green, nothing visible changed.

---

## Phase 3: User Story 1 — Only my company's Google accounts get in (P1) 🎯 MVP

**Goal**: a connector's list refuses everyone else at the callback.

**Independent Test**: fake connector limited to `acme.com`; verified `ann@acme.com` gets a session, verified `bob@gmail.com` gets the refusal page, no user row, one event.

- [ ] T009 [US1] Write callback tests in `tests/Umbral.Tests/AllowedDomainsSignInTests.fs` (fake connector, list set through `ConnectorStore`): admitted verified listed domain; `ERIN@ACME.COM` admitted; `dave@eu.acme.com`, `bob@gmail.com`, unverified `carol@acme.com`, empty email each refused with failure code `account-not-allowed`, the exact page text of [contracts/sign-in.md](./contracts/sign-in.md), no `users`/`linked_accounts` row, no session cookie, one `sign_in_failed` with the right `reason`, `rule=connector`, `stage=callback`, `domain`; an existing user refused leaves `last_seen_utc` and profile unchanged; a connector with no list and no instance list behaves as before; the page contains neither `acme.com` nor `gmail.com`. Also instance-level (US2 sign-in half): instance list `acme.com` applies to a connector without a list (`rule=instance`), and a connector list `example.org` replaces it
- [ ] T010 [US1] Add the `account-not-allowed` notice text to `src/Umbral/Views/Texts.fs` (contract wording)
- [ ] T011 [US1] Add the `email_domain` claim to `Sessions.signIn` in `src/Umbral/Infrastructure/Sessions.fs` (optional domain parameter) and a `currentEmailDomain` reader (research R6)
- [ ] T012 [US1] In `src/Umbral/Handlers/Connect/Callback.fs`, after `ExchangeAndFetch` and before `UserStore.resolve`: `EmailDomainRules.ruleFor`, `EmailDomains.check` on the profile's email and verified flag; refused → event via `refusalDetail` (stage `callback`, no user id) and `fail ctx "account-not-allowed"`; admitted → pass the verified email's domain to `Sessions.signIn`. T009 passes

**Checkpoint**: Story 1 works alone; the MVP.

---

## Phase 4: User Story 3 — Tightening a rule locks out people already signed in (P2)

**Goal**: sessions and refresh tokens are re-checked at their next use.

**Independent Test**: sign in as verified `bob@gmail.com` through an open connector and get tokens; limit the connector to `acme.com`; re-authorize and refresh.

- [ ] T013 [US3] Write tests in `tests/Umbral.Tests/AllowedDomainsSignInTests.fs`: after tightening, authorize with the existing session → session cookie cleared, redirect to `/signin` (and `login_required` with `prompt=none`), event `stage=authorize`; refresh → `invalid_grant`, event `stage=token`; code redemption checked too; still admitted → refresh works; a session without `email_domain` (issued as before this feature) refused only while a rule applies; a token principal without `umbral_connector` refused only while any list is set; `umbral_connector`/`umbral_email_domain` survive code → refresh → refresh and never appear in an access token, id token or userinfo; a deleted connector's refresh judged by the instance list; the user stays enabled and unchanged
- [ ] T014 [US3] In `src/Umbral/Handlers/Connect/Authorize.fs`: when a session is honoured, look up its connector, check its rule against `Sessions.currentEmailDomain` (absent = no email, present = verified); refused → `Sessions.signOut`, event (stage `authorize`, user id, client id), then the no-session branch. On issuance add `umbral_connector` and `umbral_email_domain` to the identity and map both to no destination in `destinationsFor` (research R7)
- [ ] T015 [US3] In `src/Umbral/Handlers/Connect/Token.fs`, after the enabled-user check, for both grants: check per [contracts/sign-in.md](./contracts/sign-in.md) token table (`EmailDomainRules.ruleFor` or instance-only when the connector is gone; `anyListSet` when the claim is absent); refused → `Authorize.forbid ctx Errors.InvalidGrant`, event stage `token`. T013 passes

---

## Phase 5: User Story 5 — Google shows only company accounts (P3)

**Goal**: `hd` sent for a Google connector whose rule is exactly one domain.

- [ ] T016 [P] [US5] Golden URL tests in `tests/Umbral.Tests/ProviderAdapterTests.fs`: Google with `HostedDomainHint = Some "acme.com"` carries `hd=acme.com`; `None` carries no `hd`; GitHub/Discord/OIDC ignore the hint; plus a `Start` test that a Google connector with one domain (own or instance) redirects with `hd` and with two domains without
- [ ] T017 [US5] Add `HostedDomainHint: string option` to `AuthorizeRequest` in `src/Umbral/Infrastructure/ProviderAdapters.fs`; append `hd` in `src/Umbral/Infrastructure/Providers/Google.fs`; compute it in `src/Umbral/Handlers/Connect/Start.fs` from `EmailDomainRules.ruleFor` (Google kind and exactly one domain); update every other construction site with `None`. T016 passes

---

## Phase 6: User Story 2 — One rule for the whole instance (P2)

**Goal**: the instance card, its removal warning, and the change event.

- [ ] T018 [US2] Write admin tests in `tests/Umbral.Tests/AllowedDomainsAdminTests.fs` per [contracts/admin-surface.md](./contracts/admin-surface.md): the card (`data-card="allowed-domains"`) shows the stored list in Unicode; save valid → stored, notice, one `allowed_domains_changed` without slug and `count=n`; unchanged save → no event; wildcard and non-domain entries → message naming the entry, nothing stored, input kept; emptying with connectors that have no list → warning naming them (`data-opened-connector`), nothing stored, confirm button posts `?confirm=yes` and stores; emptying when every connector has its own list → no warning; antiforgery required
- [ ] T019 [US2] Add the card, notice, warning and input-error texts to `src/Umbral/Views/Texts.fs`
- [ ] T020 [US2] Implement the instance card and `POST /admin/allowed-domains` (operator only, antiforgery, confirm pattern of the strand warnings) in `src/Umbral/Handlers/Admin/Connectors.fs` and register the endpoint in `src/Umbral/Program.fs`. T018 passes

---

## Phase 7: User Story 4 — Limit one app with a connector made for it (P3)

**Goal**: the connector's own field, its rule label, the one-app note and the Apps warning.

- [ ] T021 [US4] Write tests in `tests/Umbral.Tests/AllowedDomainsAdminTests.fs`: the add form (every kind, OpenID Connect included) and each card have field `allowedDomains`, the help naming the instance list or "none", and `data-note="limit-one-app"` with the contract text; creating and saving with a list stores it and records `allowed_domains_changed` with the slug; an invalid entry saves nothing; the unshare confirmation (no field posted) keeps the list; an empty field clears it; each card shows `data-rule` connector/instance/anyone with the contract text; Apps screen: an app offering a restricted and an unrestricted enabled connector shows `data-warning="unrestricted-connectors"` naming the open one, saving that choice succeeds with the warning notice, no warning when the instance list is set or when all offered connectors are restricted; and the 003 FR-008 path: a session from the open connector at an app offering only the restricted one lands on its sign-in page
- [ ] T022 [US4] Add the field, help, note, rule-label and Apps-warning texts to `src/Umbral/Views/Texts.fs`
- [ ] T023 [US4] In `src/Umbral/Handlers/Admin/Connectors.fs`: the field on the add form (both create paths) and on each card's save form, parsing with `EmailDomains.parse`, storing through `ConnectorStore`, recording `allowed_domains_changed` with the slug when the list changes, keeping the list when the field is absent; the `data-rule` label on each card
- [ ] T024 [US4] In `src/Umbral/Handlers/Admin/Apps.fs`: the mixed-connector warning on each row and as a notice after `setConnectors`, using `offeredTo` and `EmailDomainRules.ruleFor` (research R10). T021 passes

---

## Phase 8: Polish & Cross-Cutting

- [ ] T025 [P] README: a "Who can sign in" section in `README.md` covering both levels, which applies, verified-only, no subdomains, what happens to people already signed in (next use, one-hour access tokens), the one-app recipe, and the refusal events in `security_events` (FR-023)
- [ ] T026 [P] `docs/connectors/google.md`: allowed email domains section — Workspace domains are owned by the company; a consumer Google account can also carry a verified address at the domain if the person controls that inbox; the `hd` hint and that it is only a hint; the one-app recipe (FR-024)
- [ ] T027 [P] `docs/connectors/github.md`: section — GitHub verifies any address the person controls; Umbral checks the primary verified address; a domain rule on GitHub trusts whoever can receive mail at the domain; the one-app recipe
- [ ] T028 [P] `docs/connectors/discord.md`: section — Discord's verified flag, accounts without a verified email are refused while a rule applies; the one-app recipe
- [ ] T029 [P] `docs/connectors/oidc.md`: section — a provider that does not assert `email_verified` refuses everyone while a rule applies; trust depends on how the provider verifies; the one-app recipe
- [ ] T030 Extend `tests/Umbral.Tests/SecurityEventCoverageTests.fs` so the coverage journey emits `allowed_domains_changed` and a domain refusal, and `tests/Umbral.Tests/LogRedactionTests.fs` (or the sign-in tests) so no full email address reaches `security_events` or the logs during a refusal
- [ ] T031 Run `dotnet build -c Release` and `dotnet test -c Release`; record the count here; check every acceptance scenario of spec.md maps to a passing test
- [ ] T032 Manual: walk [quickstart.md](./quickstart.md) "By hand" on a development instance

---

## Dependencies & Execution Order

- Phase 1 → Phase 2 → everything else.
- US1 (Phase 3) is the MVP and comes first; US3 needs US1's session claim.
- After US1: **{US3, US5}** (Connect handlers, adapters) and **{US2, US4}**
  (admin handlers) touch different files except `Texts.fs`, and can be built on
  separate worktrees and merged; Phase 8 docs (T025–T029) can be written in
  parallel with all of them.
- T030–T032 last.

### Parallel example

```text
after T012:  worktree A: T013 → T014 → T015 → T016 → T017
             worktree B: T018 → T019 → T020 → T021 → T022 → T023 → T024
             worktree C: T025, T026, T027, T028, T029
then:        merge A, B, C → T030 → T031 → T032
```

## Implementation Strategy

1. Phases 1–3: the MVP. A connector can be limited and refuses strangers.
2. US3: the limit holds for people already signed in.
3. US2, US4, US5 in parallel with US3, then docs.
4. Each phase ends with the full suite green.
