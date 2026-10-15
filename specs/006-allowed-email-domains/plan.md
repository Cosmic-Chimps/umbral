# Implementation Plan: Allowed Email Domains, per Instance or per Connector

**Branch**: `006-allowed-email-domains` (built on `alta/sincere-bunting-jgtq`) | **Date**: 2026-10-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/006-allowed-email-domains/spec.md`

## Summary

An operator can list allowed email domains on the instance and on each
connector. A connector's own list replaces the instance list; with neither, sign-in
is unchanged. A sign-in passes only with a provider-verified email whose domain
is listed exactly. The work is in five places:

1. **A pure rule** (`Domains/EmailDomains.fs`): parse and normalise what the
   operator types, take the domain of an asserted email, pick the list that
   applies, and decide admit or refuse with a reason. All of it is tested
   branch by branch without a database, like `Domains/Linking.fs`.
2. **Storage**: schema version 4 adds `connectors.allowed_email_domains`
   (`TEXT NOT NULL DEFAULT ''`); the instance list is a row in `settings`.
3. **Enforcement in three places**: the callback, before `UserStore.resolve`
   (FR-008); the authorize endpoint, when an existing session is honoured
   (FR-012); the token endpoint, for both grants (FR-013). The session cookie
   and the authorization's principal carry the connector slug and the domain
   of the verified email checked at sign-in, never the address.
4. **The Google hint**: `AuthorizeRequest` gains `HostedDomainHint`, filled by
   `Start.fs`; only the Google adapter writes it, as `hd`.
5. **Admin surface**: an instance card on the Connectors screen with the
   removal warning, a field on the add form and on each connector card, a rule
   label on each card, the mixed-connector warning on the Apps screen, and one
   new security event type for list changes.

Plus the governance amendment (constitution 2.1.0, 001 FR-025) and the docs.

## Technical Context

**Language/Version**: F# on .NET 10 (`global.json`), ASP.NET Core

**Primary Dependencies**: existing only (Oxpecker, htmx, OpenIddict 7.7.1,
Data Protection, Microsoft.Data.Sqlite). `System.Globalization.IdnMapping` for
internationalised domains. No new package.

**Storage**: SQLite in the data directory. One additive column, one settings
key, schema version 3 → 4.

**Testing**: xunit.v3 on Microsoft.Testing.Platform, TestServer via
`Program.buildApp` (`tests/Umbral.Tests/Infrastructure/Fixtures.fs`), the fake
connector for identities with a chosen email and verified flag, the stub OIDC
provider for Google-shaped URLs. `dotnet test -c Release`.

**Target Platform**: Linux container, `linux/amd64` and `linux/arm64`.

**Project Type**: single web service.

**Performance Goals**: the check adds one settings read and no network call
to a sign-in, an authorization or a token request.

**Constraints**: no full email address in any security event (FR-011); no
listed domain in any end-user text (SC-004); every SQL a literal (MetaTests);
every user-facing string in `Views/Texts.fs`.

**Scale/Scope**: a handful of domains per list, a handful of connectors.

## Constitution Check

*Evaluated before Phase 0 and again after Phase 1. Both passes agree.*

| Gate | Result | Notes |
| --- | --- | --- |
| Concept count | PASS | No noun: a list on Connector and an instance setting. The instance list lives on the Connectors screen; the admin stays three screens (FR-016). |
| Single artifact | PASS | One column and one settings row in the data directory. No dependency. |
| Protocol safety | PASS | No token is built by hand. The rule adds private claims to the principal OpenIddict already protects, and refuses through OpenIddict's own `Forbid`. |
| Secure default | PASS | No list means unchanged behaviour. With a list: verified-only, exact match, re-checked at every session reuse and every token issuance; sessions and tokens that cannot prove what was checked are refused while a rule applies (R6, R7). |
| Documentation | PASS | Tasks include the README section and a section in each of the four connector walkthroughs (FR-023, FR-024). |
| Scope | PASS, with amendment | Principle VII excludes "any authorization model". The spec argues this is not one (rule on connector or instance, decides who is a user, not what a user may do) and FR-002 records the reading as a MINOR clarification (2.1.0) and in 001 FR-025, in the same change set. Without that amendment this gate fails. |

Testing discipline: every refusal path runs through the real callback,
authorize and token endpoints with the fake connector; the `hd` parameter is
pinned by a golden URL test.

## Project Structure

### Documentation (this feature)

```text
specs/006-allowed-email-domains/
├── spec.md
├── plan.md                 # this file
├── research.md             # R1–R10
├── data-model.md           # column, setting, claims, events
├── quickstart.md           # validation runbook
├── contracts/
│   ├── sign-in.md          # callback, authorize, token, refusal page, hd
│   └── admin-surface.md    # instance card, connector field, warnings, events
├── checklists/requirements.md
└── tasks.md                # /speckit-tasks
```

### Source Code (repository root)

```text
.specify/memory/constitution.md            # 2.1.0: Principle VII clarification
specs/001-umbral-mvp/spec.md               # FR-025 amended
src/Umbral/
├── Umbral.fsproj                          # Compile entry for EmailDomains.fs after Linking.fs
├── Domains/
│   ├── EmailDomains.fs                    # NEW: parse, domainOf, applying, check, describe
│   └── Connectors.fs                      # Connector.AllowedEmailDomains
├── Infrastructure/
│   ├── Database.fs                        # schema v4: allowed_email_domains column
│   ├── ConnectorStore.fs                  # read/write the column
│   ├── EmailDomainRules.fs                # NEW: instance list in settings; ruleFor connector
│   ├── SecurityEvents.fs                  # + AllowedDomainsChanged; refusal detail builder
│   ├── Sessions.fs                        # email-domain claim on the session
│   ├── ProviderAdapters.fs                # AuthorizeRequest.HostedDomainHint
│   └── Providers/Google.fs                # hd
├── Views/Texts.fs                         # refusal text, card label, field help, warnings, notices
└── Handlers/
    ├── Connect/Callback.fs                # check before resolve; claim on the session
    ├── Connect/Start.fs                   # compute the hint
    ├── Connect/Authorize.fs               # re-check the session; private claims on the identity
    ├── Connect/Token.fs                   # re-check on both grants
    ├── Admin/Connectors.fs                # instance card + POST /admin/allowed-domains, field, label
    └── Admin/Apps.fs                      # mixed-connector warning
tests/Umbral.Tests/
├── EmailDomainsTests.fs                   # NEW: the pure rule
├── AllowedDomainsSignInTests.fs           # NEW: Stories 1, 2, 3 through the endpoints
├── AllowedDomainsAdminTests.fs            # NEW: Stories 2, 4 on the screens
├── ProviderAdapterTests.fs                # + hd golden URLs
├── DatabaseTests.fs                       # + v3 → v4 upgrade
└── SecurityEventCoverageTests.fs          # + the new type and the refusal
docs/connectors/{google,github,discord,oidc}.md, README.md
```

**Structure Decision**: no new layer. The rule is a pure module beside
`Linking.fs`, because it is the same kind of thing: a decision about identity
that must be reviewable without a database.

## Implementation order

Each step leaves the suite green and an instance with no list unchanged.

1. **Governance**: constitution 2.1.0 and 001 FR-025.
2. **Rule and storage**: `EmailDomains.fs` with its tests; schema v4;
   `ConnectorStore` and `EmailDomainRules`.
3. **Callback** (Story 1): check, refusal text, event, session claim.
4. **Re-checks** (Story 3): authorize and token, private claims.
5. **Hint** (Story 5).
6. **Admin** (Stories 2 and 4): instance card and warning, connector field and
   label, Apps warning, change events.
7. **Docs**, then the full suite.

Steps 4–5 and step 6 touch different files apart from `Texts.fs` and
`SecurityEvents.fs`, so they can be built in parallel on separate worktrees
and merged; docs can be written in parallel with all of them.

## Phase outputs

- [research.md](./research.md): decisions R1–R10 and what was rejected.
- [data-model.md](./data-model.md): the column, the setting, the claims, the events.
- [contracts/](./contracts/): sign-in behaviour and admin surface.
- [quickstart.md](./quickstart.md): how to prove it.

Agent context: this repository has no `update-agent-context` script and no
agent context file to update; nothing to run.

## Complexity Tracking

| Deviation | Why needed | Simpler alternative rejected because |
| --- | --- | --- |
| A restriction on who can sign in, next to Principle VII's "no authorization model" | The request (spec Context): an operator must keep strangers out of a company connector | Declining: leaves "every Gmail user becomes a user" as the only option. Per-app rule: is the per-app allowlist FR-025 names (spec, "Per connector, not per app"). |
