# Implementation Plan: External Secret Stores for Connector Secrets

**Branch**: `005-external-secret-stores` | **Date**: 2026-10-12 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/005-external-secret-stores/spec.md`

## Summary

A connector's secret can be kept in AWS Secrets Manager, Google Cloud Secret
Manager, Azure Key Vault or BellaBaxter instead of the local encrypted store,
chosen once per instance by `UMBRAL__SECRET_STORE` and configured from each
vendor's standard environment variables (BellaBaxter's exactly as Frunchy reads
them). No schema change: the `client_secret_ref` column already holds
`"<store>:<locator>"`. The work is in four places:

1. **The seam grows up.** `ISecretStore` returns `Result` with a reason
   (unreachable, denied, not found, not configured, rejected) and takes a
   cancellation token; one singleton becomes a registry that holds the active
   store and builds any other store lazily from what its locator already says.
2. **Four adapters**, each a few calls behind the seam: the AWS SDK, Google
   over REST with `Google.Apis.Auth` for tokens, Azure's Key Vault client with a
   narrow credential chain, and `BellaBaxter.Client`.
3. **A memory-only cache with a grace period** in front of external stores, so
   a manager's bad moment does not stop sign-ins (1 hour by default).
4. **A `move-secrets` command** that moves each connector's secret in or out,
   one connector at a time, deleting the old copy only after the switch.

The admin paths also stop turning a store failure into a 500, and secrets get
names (`umbral-<slug>-<8 chars>`) that never collide with a soft-deleted one.

## Technical Context

**Language/Version**: F# on .NET 10 (`global.json`), ASP.NET Core

**Primary Dependencies**: existing (Oxpecker, htmx, OpenIddict 7.7, Data
Protection, Microsoft.Data.Sqlite). New, pinned exactly:
`AWSSDK.SecretsManager` 4.0.100.16, `Google.Apis.Auth` 1.77.0,
`Azure.Security.KeyVault.Secrets` 4.11.2, `Azure.Identity` 1.21.0,
`BellaBaxter.Client` 0.1.1-preview.131 (research R4)

**Storage**: SQLite in the data directory, unchanged schema. External managers
are optional and hold only connector secrets.

**Testing**: xunit.v3 on Microsoft.Testing.Platform, TestServer via
`Program.buildApp` (`tests/Umbral.Tests/Fixtures.fs`), `FakeTimeProvider`,
process-level `SubcommandTests` and `IdleMemoryTests`.
`dotnet test -c Release`.

**Target Platform**: Linux container, `linux/amd64` and `linux/arm64`,
framework-dependent publish (not trimmed, not AOT).

**Project Type**: single web service with offline subcommands.

**Performance Goals**: warm-cache sign-in adds no call to the manager; a cold
one adds one, bounded at 10 s (SC-005, FR-012).

**Constraints**: idle RSS < 150 MB with any store active (SC-006); a local-only
instance loads no vendor assembly (SC-004); no secret in any log (SC-007);
`TreatWarningsAsErrors` applies to new packages.

**Scale/Scope**: a handful of connectors per instance; one operator.

## Constitution Check

*Evaluated before Phase 0 and again after Phase 1. Both passes agree.*

| Gate | Result | Notes |
| --- | --- | --- |
| Concept count | PASS | The store is a deployment setting. The admin area gains a label and a warning on the connector card and a page notice; no noun, screen or section (FR-020). |
| Single artifact | PASS, recorded | Default: no new runtime dependency, all state in `/data` (FR-002, SC-004). An external manager is optional and operator-chosen; the spec's Context explains why that is allowed by Principle II. Recorded under Complexity Tracking because the Technology Constraints list the stack. |
| Protocol safety | PASS | No token, signature or key is hand-built. Cloud authentication goes through each vendor's credential library (AWS SDK, `Google.Apis.Auth`, `Azure.Identity`, Bella's HMAC handler); research R4 rejects hand-rolled Entra and SigV4. OpenIddict is untouched. |
| Secure default | PASS | Default unchanged. With a manager: TLS only, least-privilege grants documented (R10), no plaintext on disk, cache in memory only and gone on restart, secrets never logged (SC-007 test), revoked access or a deleted secret fails at once rather than riding the grace period (FR-014). |
| Documentation | PASS | Tasks include `docs/secret-stores/{aws,gcp,azure,bellabaxter}.md`, the README settings and backup sections, and the correction of the "app secrets are encrypted" text (R12). |
| Scope | PASS | Not on the Principle VII list. Storage of a secret Umbral already holds, not a new capability. |

Testing discipline: every store is tested with no account (FR-025) through
transport seams; the sign-in path is tested end to end through the real
callback with an in-memory store.

## Project Structure

### Documentation (this feature)

```text
specs/005-external-secret-stores/
├── spec.md
├── plan.md                 # this file
├── research.md             # R1–R12
├── data-model.md           # no schema change; reference values, events, cache states
├── quickstart.md           # validation runbook
├── contracts/
│   ├── settings.md         # environment variables, startup behaviour
│   ├── admin-surface.md    # card markers, notices, security events
│   ├── move-secrets-cli.md # the offline command
│   └── secret-store.md     # the internal seam tests code against
├── checklists/requirements.md
└── tasks.md                # /speckit-tasks
```

### Source Code (repository root)

```text
src/Umbral/
├── Umbral.fsproj                          # 5 packages; new Compile entries in order below
├── Infrastructure/
│   ├── Secrets.fs                         # SecretName, StoreFailure, ISecretStore (Result + token + Probe), LocalSecretStore
│   ├── SecretStores/
│   │   ├── Settings.fs                    # NEW: parse/describe UMBRAL__SECRET_* and each vendor's required variables
│   │   ├── Aws.fs                         # NEW
│   │   ├── Gcp.fs                         # NEW: REST + Google.Apis.Auth
│   │   ├── Azure.fs                       # NEW
│   │   ├── BellaBaxter.fs                 # NEW: env mapping as Frunchy, provider resolution
│   │   └── Registry.fs                    # NEW: SecretStores (Active, For, Read, Evict, CanRead), cache, SecretStoreStatus
│   ├── SecurityEvents.fs                  # + SecretStoreFailed, ConnectorSecretMoved
│   ├── ConnectorStore.fs                  # create takes `now`; guarded reference switch inside a transaction with its event
│   └── SecretMove.fs                      # NEW: the move, as a function (after ConnectorStore.fs)
├── Views/Texts.fs                         # store labels, two notices, card warning, page notice, CLI lines
├── Handlers/
│   ├── Admin/Connectors.fs                # Put via registry with derived name; refuse on failure; label, warning, notice
│   ├── Admin/ConnectorDelete.fs           # row + event first, store delete caught
│   └── Connect/Callback.fs                # Read through registry; cause in the event
└── Program.fs                             # settings in requireSettings; TryAddSingleton registry; probe in initialize; `move-secrets`
tests/Umbral.Tests/
├── SecretStoreSettingsTests.fs            # NEW
├── SecretStoreAdapterTests.fs             # NEW: four adapters × canned responses
├── SecretCacheTests.fs                    # NEW
├── ExternalSecretConnectorTests.fs        # NEW: Stories 1, 2, 4 through the product
├── SecretMoveTests.fs                     # NEW: SC-003 interrupt matrix
├── Infrastructure/InMemorySecretStore.fs  # NEW: test store, fail-on-Nth
├── SubcommandTests.fs, IdleMemoryTests.fs, LogRedactionTests.fs, SecretsTests.fs  # extended
docs/secret-stores/{aws,gcp,azure,bellabaxter}.md   # NEW
README.md, Dockerfile (comment only)
```

**Structure Decision**: no new project or layer. The vendor adapters get a
folder of their own under `Infrastructure/`, like `Providers/` for connectors,
because they are the same kind of thing: one file per external service behind
one seam.

## Implementation order

Each step leaves the suite green and the default path unchanged.

1. **Seam**: `Secrets.fs` interface change, local store adapted, registry with
   only `local`, `TryAddSingleton`, callers moved to the registry
   (Callback, Connectors, ConnectorDelete). Fixes gaps C and H for the local
   store already. No behaviour change.
2. **Settings** and start refusal; `SecretStoreStatus`; probe in `initialize`.
3. **Cache** with grace, over an in-memory store in tests.
4. **Names**: derived `SecretName`; `ConnectorStore.create` takes `now`; lost
   race deletes its own write.
5. **Adapters**, one at a time, each with its canned-response tests: AWS, Azure,
   GCP, BellaBaxter.
6. **Admin surface**: label, warning, notices, events.
7. **Move command** and its interrupt matrix.
8. **Idle memory, leak journey**, then **docs**.

## Phase outputs

- [research.md](./research.md): decisions R1–R12 and what was rejected.
- [data-model.md](./data-model.md): reference values, new events, cache states.
- [contracts/](./contracts/): settings, admin surface, move command, store seam.
- [quickstart.md](./quickstart.md): how to prove it, with and without accounts.

Agent context: this repository has no `update-agent-context` script and no
agent context file to update; nothing to run.

## Complexity Tracking

| Deviation | Why needed | Simpler alternative rejected because |
| --- | --- | --- |
| Five new package references (AWS, Google auth, Azure ×2, BellaBaxter) beyond the Technology Constraints stack | The feature is talking to these four managers; each package is the vendor's own credential and client code (Principle III's spirit: not ours to hand-roll) | Hand-written REST everywhere: Entra token acquisition and SigV4 are security-sensitive. gRPC for Google: 20 DLLs and harder to fake (R4). |
| An optional store outside the data directory | Operators with a manager want secrets there (spec Context) | Local only: the status quo the feature exists to change. Principle II forbids a *required* dependency; this one is off by default and loaded lazily. |
| `BellaBaxter.Client` is a preview package | It is the only client, and the owner's own product | Waiting for a stable release blocks a quarter of the feature; pinned exactly, its use confined to `BellaBaxter.fs`, and its behaviour pinned by adapter tests. |
