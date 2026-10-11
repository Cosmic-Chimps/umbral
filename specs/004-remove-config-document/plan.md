# Implementation Plan: Remove the Configuration Document

**Branch**: `004-remove-config-document` (spec written on worktree branch `alta/humble-ketch-37rt`) | **Date**: 2026-10-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/004-remove-config-document/spec.md`

## Summary

Delete the configuration document end to end: the domain module, the YAML
parsing, apply and export module, the export route, the startup apply in
`Program.fs`, the `hash-credential` subcommand, the file-credential code in
`AdminAuth`, the `config_applied` event and the file-location setting. Remove
extra scopes from connectors, because only the file could set them. One piece
survives in a new place: the redirect URI rule that the Apps screen shares with
the file moves to `Domains/RedirectUris.fs` with its tests. YamlDotNet leaves
the app and stays in the test project, where the secret scan of committed
compose and workflow files uses it. The constitution goes to 2.0.0 without
Principle V, and the README and connector walkthroughs lose every mention of
the file. No upgrade path is built, because no release has been published.

The work starts from feature 003's merged result (spec Clarifications, Q4).

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`, SDK 10.0.400), warnings as errors

**Primary Dependencies**: ASP.NET Core, Oxpecker, OpenIddict, Microsoft.Data.Sqlite. YamlDotNet is removed from the app and kept for tests only.

**Storage**: SQLite in the data directory. Umbral's tables come from an idempotent `CREATE TABLE IF NOT EXISTS` script; OpenIddict's come from EF Core migrations, which this feature does not touch.

**Testing**: xUnit v3 on Microsoft.Testing.Platform (`dotnet test` from the repo root), integration tests on TestServer

**Target Platform**: Linux container, `linux/amd64` and `linux/arm64`

**Project Type**: Web service with a server-rendered admin UI

**Performance Goals**: No change. Startup gets shorter, because there is no file to parse.

**Constraints**: Idle RSS under 150 MB (Principle II). Removing YamlDotNet from the app can only help.

**Scale/Scope**: About 1,500 lines deleted (three modules and one test file). 26 files edited in source and tests (12 and 14), 9 in docs, governance and spec 001 notes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated before Phase 0 and again after Phase 1. Both passes agree.

| Gate | Result | Notes |
| --- | --- | --- |
| Concept count | PASS | Removes a surface. No noun, no screen. |
| Single artifact | PASS | No dependency added. One NuGet package leaves the shipped app. |
| Protocol safety | PASS | No token, signature or key handling changes. `hashCredential` (PBKDF2 for the operator credential) stays as it is. |
| Secure default | PASS | A clean instance is claimed only through the setup screen with its log token, or `reset-credential` from a shell. Both need more than reaching the web page. No credential material comes from the environment any more. |
| Config parity | **VIOLATION, resolved by amendment** | This feature removes the YAML file that Principle V requires. Under Governance, that is a MAJOR amendment made in the same change set (FR-011, research R7). After the amendment, this gate no longer exists. See Complexity Tracking. |
| Documentation | PASS | README, three connector walkthroughs, vision, constitution, and superseded notes in spec 001 (research R8). |
| Scope | PASS | Not on the Principle VII list. It shrinks the product. |

## Project Structure

### Documentation (this feature)

```text
specs/004-remove-config-document/
├── plan.md              # This file
├── research.md          # R1 inventory … R9 order with 003
├── data-model.md        # column, setting and event type removed
├── quickstart.md        # V0–V8 validation
├── contracts/
│   └── removed-surface.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/Umbral/
├── Domains/
│   ├── ConfigDocument.fs          # DELETE
│   ├── RedirectUris.fs            # NEW: acceptable (moved from ConfigDocument, unchanged)
│   └── Connectors.fs              # drop ExtraScopes; ScopeString = kind's scopes
├── Infrastructure/
│   ├── ConfigFile.fs              # DELETE
│   ├── AdminAuth.fs               # drop isCredentialHash, FileCredential*, applyCredentialHash
│   ├── SecurityEvents.fs          # drop ConfigApplied
│   ├── DataDirectory.fs           # drop ConfigFile member
│   ├── Database.fs                # drop connectors.extra_scopes from CREATE
│   ├── ConnectorStore.fs          # drop extra_scopes column from SQL
│   └── AppClients.fs              # comment only
├── Handlers/Admin/
│   ├── ConfigExport.fs            # DELETE
│   ├── Apps.fs                    # call RedirectUris.acceptable
│   └── Connectors.fs              # drop ExtraScopes = []
├── Views/Texts.fs                 # drop four ConfigExport* fields
├── Program.fs                     # drop route, startup apply, hash-credential
└── Umbral.fsproj                  # compile items, YamlDotNet out
tests/Umbral.Tests/
├── ConfigFileTests.fs             # DELETE
├── RedirectUriTests.fs            # NEW: the two moved theories
├── CredentialTests.fs             # drop FR-017a block and hash-credential facts
├── SecurityEventCoverageTests.fs  # drop file facts; rewrite two to create via screens or helpers
├── SecurityEventsTests.fs, SubcommandTests.fs, MetaTests.fs
├── 8 files with `ExtraScopes = []` # drop the field
└── Umbral.Tests.fsproj            # ConfigFileTests out, RedirectUriTests in, YamlDotNet in
README.md, docs/connectors/{discord,github,google}.md, docs/vision.md
.specify/memory/constitution.md
specs/001-umbral-mvp/{spec,quickstart,data-model}.md   # superseded notes
```

**Structure Decision**: no new project or layer. One new pure domain module
keeps the redirect URI rule, which the Apps screen still needs.

## Implementation order

1. **Rebase on 003** and re-run quickstart V0 to refresh the inventory
   (research R9).
2. **Move the redirect URI rule** to `Domains/RedirectUris.fs` with its tests.
   Build green before anything is deleted.
3. **Delete the file**: `ConfigDocument.fs`, `ConfigFile.fs`, `ConfigExport.fs`,
   `ConfigFileTests.fs`. Remove the startup apply, the route and
   `hash-credential` from `Program.fs`, the file credential from `AdminAuth.fs`,
   `ConfigApplied`, `DataDirectory.ConfigFile` and the Texts fields. Fix the
   tests (research R5). Move YamlDotNet to the test project (R3).
4. **Remove extra scopes** from the domain, store, schema and tests (R4).
5. **Docs and governance**: constitution 2.0.0, vision, README, walkthroughs,
   spec 001 notes (R7, R8).
6. **Validate** with quickstart V0 to V8.

Steps 2 to 4 each end with `dotnet build` and `dotnet test` green.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Principle V (Config-as-Code Parity) and its Constitution Check gate are removed | The parity rule makes every feature extend a file format, its validation, export and round-trip tests (002 and 003 both had to). It gives the instance a second source of truth that undoes admin changes on restart. And it cannot keep its promise: app secrets are hashed, so an export never reproduces an instance. | Keeping the file and exempting new settings: the parity promise would be broken quietly instead of retired openly. Keeping export without import: still a format to maintain, and still incomplete. |

## Phase outputs

- [research.md](./research.md): the inventory, the decisions and what was rejected.
- [data-model.md](./data-model.md): the removed column, setting and event type.
- [contracts/removed-surface.md](./contracts/removed-surface.md): what stops existing and what is relied on.
- [quickstart.md](./quickstart.md): validation V0 to V8.
