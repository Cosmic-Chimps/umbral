# Research: Remove the Configuration Document

Every decision below was taken against the code at `5f834b7` (branch
`002-generic-oidc-connector`). Feature 003 lands first (spec Clarifications,
Q4) and touches some of the same files, so the inventory in R1 is re-run
against the merged 003 before implementation starts.

## R1. What the file reaches into

**Decision**: delete three modules and one test file outright. Edit the 26
files that only *use* them (12 in `src`, 14 in `tests`).

Deleted whole:

| File | Why it exists |
| --- | --- |
| `src/Umbral/Domains/ConfigDocument.fs` | Document types, validation, `${VAR}` references. Also hosts `acceptableRedirectUri` (see R2). |
| `src/Umbral/Infrastructure/ConfigFile.fs` | YAML parsing, secret resolution, apply, export, `locate`, `tryRead`. |
| `src/Umbral/Handlers/Admin/ConfigExport.fs` | `GET /admin/config`. |
| `tests/Umbral.Tests/ConfigFileTests.fs` | Parser, validation, apply, round trip. Two theories on the redirect URI rule move (R2). |

Edited, by reason:

| Reason | Files |
| --- | --- |
| Startup apply, `hash-credential`, route | `src/Umbral/Program.fs` (lines 43, 328-376, 393, 518-566, 573-574) |
| File credential | `src/Umbral/Infrastructure/AdminAuth.fs`: `isCredentialHash`, `FileCredentialKey`, `FileCredentialResult`, `applyCredentialHash` go. `hashCredential` stays: the setup and credential screens and `reset-credential` use it. |
| `ConfigApplied` event | `src/Umbral/Infrastructure/SecurityEvents.fs` (type, string, `allEventTypes`) |
| Default file path | `src/Umbral/Infrastructure/DataDirectory.fs` (`ConfigFile` member) |
| Extra scopes | `src/Umbral/Domains/Connectors.fs` (field, `ScopeString`, comment at line 53), `src/Umbral/Infrastructure/ConnectorStore.fs`, `src/Umbral/Infrastructure/Database.fs`, `src/Umbral/Handlers/Admin/Connectors.fs` |
| Redirect URI rule | `src/Umbral/Handlers/Admin/Apps.fs` (R2) |
| Stale comments | `src/Umbral/Infrastructure/AppClients.fs` (line 4) |
| Export texts | `src/Umbral/Views/Texts.fs`: `ConfigExportTitle`, `ConfigExportLead`, `ConfigExportDownloadButton`, `ConfigExportSecretNote` (never rendered: the export is a download) |
| Package | `src/Umbral/Umbral.fsproj` (three `Compile` items, `YamlDotNet`), `tests/Umbral.Tests/Umbral.Tests.fsproj` (R3) |
| Tests | `CredentialTests.fs` (the FR-017a block from line 231 and both `hash-credential` facts), `SecurityEventCoverageTests.fs` (R5), `SecurityEventsTests.fs` (line 98), `SubcommandTests.fs` (line 37), `MetaTests.fs` (R3), and the `ExtraScopes = []` record fields in `FakeConnectorGuardTests.fs`, `ProviderAdapterTests.fs`, `ConnectorRuntimeTests.fs`, `OidcProtocolTests.fs`, `ConnectorDeleteTests.fs`, `FirstLoginTests.fs`, `Infrastructure/Flows.fs`, `Infrastructure/Journeys.fs` |

**Alternatives considered**: keeping `ConfigDocument.fs` for its redirect URI
rule. Rejected: the module would be named after a thing that no longer exists.

## R2. The redirect URI rule outlives the file

`Handlers/Admin/Apps.fs` validates redirect URIs by calling
`Domains.ConfigDocument.acceptableRedirectUri`. Its comment explains why: there
used to be two copies, and the file's copy caught `/callback` parsing as a
`file://` URI on Unix while the screen's copy did not.

**Decision**: move the function unchanged into a new pure module,
`src/Umbral/Domains/RedirectUris.fs` (`RedirectUris.acceptable`). Compile it
where `ConfigDocument.fs` was. `Apps.fs` calls it. The two theories at
`ConfigFileTests.fs:626-648` move to a new `tests/Umbral.Tests/RedirectUriTests.fs`
unchanged, so the rule keeps its tests.

**Alternatives considered**: inlining the rule in `Apps.fs`. Rejected: it is
domain logic with no I/O, and it has its own tests. `AppClients.fs` was also
rejected: it is Infrastructure and talks to OpenIddict.

## R3. YamlDotNet stays for the tests

`MetaTests.fs` opens `YamlDotNet.RepresentationModel` to scan committed compose
and workflow files for literal secrets. The test project gets the package only
transitively, through `src/Umbral`.

**Decision**: remove `YamlDotNet` from `src/Umbral/Umbral.fsproj` and add it
directly to `tests/Umbral.Tests/Umbral.Tests.fsproj`. In `MetaTests.fs`, drop
`umbral.yaml` from the scan's example data and from the `ConfigFile.fs`
comment. Keep the YAML rule: compose and workflow files are still scanned.

**Alternatives considered**: dropping the YAML part of the secret scan.
Rejected: `docker-compose.yml` and workflows still hold settings, and the scan
protects them.

## R4. `extra_scopes` in the database

Umbral's own tables are created by one idempotent `CREATE TABLE IF NOT EXISTS`
script (`Database.fs`), with no migration framework. Feature 003 introduces
schema version 2 and an upgrade step for its own columns.

**Decision**: remove `extra_scopes` from the `CREATE TABLE connectors`
statement, from `ConnectorStore`'s column list, insert and update, and from the
`Connector` record. Add no upgrade step. No release has been published, so the
only databases with the column are development ones, where an unread nullable
column is harmless. `ScopeString` becomes the kind's required scopes only.

**Alternatives considered**: an upgrade step that runs
`ALTER TABLE connectors DROP COLUMN extra_scopes` on top of 003's version
mechanism. Rejected for now, because no deployment has the column. **Revisit
if a release is published between 003 and 004.**

## R5. Security event coverage without the file

`SecurityEventCoverageTests.fs` uses the file in two ways. Three facts test
that applying the file records system events, which is now deleted behaviour.
The fake-connector fact (line 297) and the journey from line 342 use the file
only as a quick way to create connectors and apps. The coverage meta-test
(T092) requires every type in `allEventTypes` to have a writer.

**Decision**: delete the three file facts and the `withConfigFile` and
`withConfigFileAnd` helpers. Rewrite the other two so they create their
connector and app the way the remaining tests do: through the Connectors and
Apps screens, or the `Flows` and `Journeys` helpers that write the store
directly. Removing `ConfigApplied` from `allEventTypes` keeps T092 honest.

## R6. The removed subcommand and the removed route

**Decision**: no special handling. `hash-credential` disappears from `main`'s
dispatch. `/admin/config` disappears from `endpoints`, so the catch-all
`app.Run` answers 404 "Not found" for it, signed in or not (FR-002). The spec
asks for no tombstone message, because nothing was published (Clarifications,
Q1).

Note: without the branch, `Umbral hash-credential` starts the web host like any
unknown argument. That is the existing behaviour for every unknown argument,
not new behaviour.

## R7. Constitution amendment

**Decision**: version `1.0.0` to `2.0.0` (MAJOR: a principle is removed).

- Principle V's heading stays as **"V. (Removed in 2.0.0)"**, with one paragraph
  saying why and pointing at spec 004. The other principles are not renumbered:
  "Principle VI" and "Principle VII" are cited in code comments, specs and
  tests.
- The "Config parity" row is removed from the Constitution Check table.
- The sync impact report records the change and its effect on the templates
  (none of the templates names the YAML file).
- `docs/vision.md` line 44: the "1 YAML for config-as-code" row is marked as
  dropped, with a one-line reason.

**Alternatives considered**: renumbering VI to V and VII to VI. Rejected:
every existing "Principle VII" reference would silently point at the wrong
principle.

## R8. Documentation

| Where | Change |
| --- | --- |
| `README.md` | Remove the "Configuration as code" section and its "The operator credential in the file" subsection, the table-of-contents entry, the `umbral.yaml` row of the data-directory table, the `UMBRAL__CONFIG_FILE` row of the settings reference, and "A broken `umbral.yaml`…" in troubleshooting. Add a short "Claim an instance from a script" note (run `reset-credential` once after the first start). Retitle or lead the backup section so it also says this is how to move or rebuild an instance. |
| `docs/connectors/{discord,github,google}.md` | Remove "The same thing in the configuration file", and the `extra_scopes` sentence in Discord and GitHub. |
| `specs/001-umbral-mvp/spec.md` | A "Superseded by 004" note beside FR-017a, FR-027 to FR-030, SC-006, SC-008, the "Configuration document" entity and User Story 5 (FR-014). |
| `specs/001-umbral-mvp/quickstart.md`, `data-model.md` | A note on Scenario 5's export half, the `extra_scopes` column, `config_applied` and "The configuration document" section. |
| Specs 002 and 003 | Not here: the pending task `2026-10-10-drop-the-configuration-file-requirements-from-specs-002-and-tnch` does it before 003 is built. |

Historical spec artifacts get notes, not rewrites: they record what was
decided then.

## R9. Order and conflicts with feature 003

Feature 003's plan edits `ConfigDocument.fs`, `ConfigFile.fs`,
`ConfigExport.fs` and `ConfigFileTests.fs`. Once the follow-up task strips
those, 003 still edits `Connectors.fs`, `ConnectorStore.fs`, `Database.fs`,
`Admin/Connectors.fs`, `Texts.fs` and `Program.fs`, all of which this feature
also edits.

**Decision**: start implementing from 003's merged result, and re-run the R1
inventory greps first (see quickstart V0). FR-008 (editable display name) is
then already met by 003, and is verified rather than built.
