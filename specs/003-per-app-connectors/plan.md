# Implementation Plan: Federated Login for Every App, with Optional Per-App Connectors

**Branch**: `003-per-app-connectors` | **Date**: 2026-10-10 | **Spec**: [per-app-connectors.spec.md](./per-app-connectors.spec.md)

## Summary

The sign-in page learns which app it serves by reading the `client_id` out of
the authorize request it will return to, names that app, and offers only the
connectors the app allows. An app's connector choice lives in two small Umbral
tables beside the OpenIddict application. The connectors table gains a shared
flag. One pure function, `offeredTo`, decides which connectors an app offers,
and the sign-in page, the authorize endpoint (FR-008), the admin warnings
(FR-010) and the Apps screen all call it. Most of FR-012 is already true below
the admin screen: `connectors.slug` is the primary key and nothing makes `kind`
unique. The Connectors screen is what limits each kind to one connector, so the
screen is what changes. The YAML document gets two optional keys and nothing
else, because it is due to be removed.

## Project Structure

```text
src/Umbral/
├── Domains/
│   ├── Connectors.fs          # AppChoice, offeredTo, strandedApps, slug rule, Connector.IsShared
│   └── ConfigDocument.fs      # ConnectorEntry.Shared, AppEntry.Connectors, FR-018 check
├── Infrastructure/
│   ├── Database.fs            # schema v2: connectors.is_shared, app_connector_choices, app_connectors
│   ├── ConnectorStore.fs      # is_shared, create with an operator-chosen slug
│   ├── AppConnectors.fs       # NEW: read and write an app's choice
│   ├── Sessions.fs            # session carries the connector slug apart from idp
│   └── ConfigFile.fs          # parse, apply and export the two new keys
├── Handlers/
│   ├── Connect/Picker.fs      # app name, filtered connectors
│   ├── Connect/Authorize.fs   # FR-008 session check, picker redirect
│   ├── Connect/Callback.fs    # keep returnUrl on failure, kind into idp
│   ├── Admin/Connectors.fs    # one card per connector, add form, display name, shared
│   ├── Admin/ConnectorToggle.fs, ConnectorDelete.fs  # FR-010 per-app warning
│   ├── Admin/Apps.fs          # connector choice on create and edit, shown in the list
│   ├── Admin/AppDelete.fs     # remove the app's choice rows
│   └── Admin/ConfigExport.fs  # export the choice
├── Views/Texts.fs
└── Program.fs                 # new routes
docs/connectors/{discord,github,google}.md, README.md
tests/Umbral.Tests/
├── PerAppConnectorTests.fs    # NEW: picker, FR-008, FR-010, FR-011, FR-015
├── DatabaseTests.fs           # v1 → v2 upgrade
└── ConfigFileTests.fs         # new keys, FR-018, round trip
```

**Structure Decision**: no new project or layer. One new infrastructure module
holds the app's choice, because apps are OpenIddict rows that Umbral does not
own.

## Constitution Check

Evaluated before Phase 0 and again after Phase 1. Both passes agree.

| Gate | Result | Notes |
| --- | --- | --- |
| Concept count | PASS | "Shared" is a property of a Connector, "connector choice" a property of an App. No new noun, no new screen (FR-021). |
| Single artifact | PASS | Two tables and one column in the existing SQLite file. |
| Protocol safety | PASS | FR-008 decides whether to redirect to the picker before OpenIddict signs anything. No token is hand-built. |
| Secure default | PASS | Upgrade and new apps keep today's behaviour. FR-008 only adds a reason to show the picker. |
| Config parity | PASS | Both new settings are in the YAML document (FR-016). The document's planned removal needs a Principle V amendment, which is that later feature's job. |
| Documentation | PASS | Tasks include all three provider guides and the README. |
| Scope | PASS | A connector choice limits how people sign in to an app, never which users may (FR-020). That is not an authorization model under Principle VII. |

## Phase outputs

- [research.md](./research.md): the decisions and what was rejected.
- [data-model.md](./data-model.md): the schema change and the upgrade.
- [contracts/](./contracts/): the routes, page markers, YAML keys and the `idp` claim that tests and integrators code against.
