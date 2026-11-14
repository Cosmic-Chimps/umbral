# Implementation Plan: Declare apps and the passkey connector at start

**Spec**: [declared-apps.spec.md](declared-apps.spec.md) · **Data model**: [data-model.md](data-model.md) · **Contracts**: [declaration](contracts/declaration.md), [package](contracts/package.md), [screens](contracts/screens.md)

## Summary

`Program.initialize` gains one step after OpenIddict's migrations: read
`UMBRAL__DECLARATION` and the `UMBRAL__APP_SECRETS__*` keys, check the whole
declaration (pure, in `Domains/Declaration.fs`), stop the start on any problem,
otherwise apply it in one transaction (`Infrastructure/Declarations.fs`) and
log one line. A table `declared_items` (schema 16) records what is and was
declared. The Apps and Passkey panes read it to draw the **Declared** mark and
the owned fields as values; the routes that change an owned field refuse. The
Aspire package gains `AddApp`, `WithPasskeys` and `WithReference(app)`, and
writes the two variables. `samples/aspire` uses them.

## Technical Context

F# on .NET 10, Oxpecker, SQLite, OpenIddict 7 (EF Core stores), xUnit with the
in-process test server. `System.Text.Json` for the declaration (already in the
framework). C# for the package, Aspire.Hosting 13.6.5, its xUnit tests beside
a5pr's. No new package.

## Research

- **R1, reading the variables.** `IConfiguration` key `UMBRAL:DECLARATION` and
  the section `UMBRAL:APP_SECRETS`, the way every `UMBRAL__` setting is read, so
  the test fixture can pass them as settings. Configuration keys are
  case-insensitive; the section's child keys are compared after lowercasing.
- **R2, one transaction across OpenIddict and Umbral's tables.** OpenIddict's
  EF Core stores write through the scoped `UmbralContext`. The step opens a
  scope, begins a transaction on that context
  (`context.Database.BeginTransactionAsync`), and writes Umbral's own rows
  (`declared_items`, `app_connector_choices`/`app_connectors`, the connector,
  `security_events`) on the context's own `SqliteConnection` with
  `GetDbTransaction()`. A second connection must not write meanwhile: SQLite
  would wait on its own writer lock. So the store functions used here take a
  connection and transaction (`AppConnectors.setChoiceIn`,
  `ConnectorStore.createIn`/`setEnrolmentIn`/`setEnabledIn`, beside
  `SecurityEvents.recordIn`), and the existing ones become thin wrappers over
  them. The build checks first that OpenIddict's `CreateAsync`/`UpdateAsync`
  commit inside the ambient EF transaction (a test that rolls back and finds no
  app). If they do not, the fallback is one unit per item, which still
  converges at the next start, and the reviewer is told.
- **R3, the secret check.** `IOpenIddictApplicationManager.ValidateClientSecretAsync`
  compares against the PBKDF2 hash; `UpdateAsync(app, secret)` re-hashes. Both
  are what `AppSecret.fs` and creation already rely on.
- **R4, stopping the start.** `initialize` raises an exception whose message
  is the refusal lines (as the fake-connector gate does with `failwith`);
  `main` already turns that into a non-zero exit with the message in the log.
  The test fixture asserts on the exception.
- **R5, Aspire.** `AddParameter(name, ParameterDefault, secret, persist)` with
  `GenerateParameterDefault` exists in 13.6.5. Endpoint-derived redirect URIs
  use the referencing resource's `EndpointReference` in the Umbral environment
  callback, which runs after endpoints are allocated. In publish mode the
  callback throws when a URI would come from an endpoint.

## Constitution Check

| Gate | Assessment |
| --- | --- |
| I. Three concepts | PASS. Declared is a state of an App or a Connector, shown as a mark; no new noun on screen. |
| II. One artifact | PASS. One table, schema 16; the declaration is input like every `UMBRAL__` variable, not state. |
| III. Standards | PASS. Apps are made and secrets hashed by OpenIddict, as from the admin area. |
| IV. Secure by default | PASS. Off unless given; refused rather than merged; secrets only from variables, hashed, never logged; every change a security event; the same URI rules as the admin area. |
| V. (removed) | Amended text, MINOR 3.2.0 (spec D11): the admin area stays the one place configuration changes except what a declaration owns, which it shows read-only. |
| VI. Documentation | PASS. aspire.md, settings.md, integrate.md, package README, samples README, changelog with an Upgrade note. |
| VII. Default answer is no | PASS. No export, no secret-bearing connectors, no users, no operator credential. |

## Structure

| File | Change |
| --- | --- |
| `Domains/Declaration.fs` (new) | Types (`Declaration`, `DeclaredApp`, `DeclaredPasskeys`), `parse : string -> Map<string,string> -> Result<Declaration, Problem list>` with every check of `contracts/declaration.md` except the database ones, and the problem lines |
| `Infrastructure/Database.fs` | `declared_items` in `schema`, step 16, `SchemaVersion = 16` |
| `Infrastructure/Declarations.fs` (new) | `isDeclared`, `declaredClientIds`, `conflicts` (database checks), `apply` (R2) returning the counts; un-declare |
| `Infrastructure/AppConnectors.fs`, `ConnectorStore.fs` | `...In` variants on a given connection and transaction (R2) |
| `Infrastructure/SecurityEvents.fs` | nothing new in the union; details `declared...` / `undeclared` |
| `Program.fs` | the step in `initialize` after `MigrateAsync`; the refusal; the start line |
| `Handlers/Admin/Apps.fs` | the mark and the owned fields as values (contract from the designer); `app-declared` notice; refusal in `setConnectors` |
| `Handlers/Admin/AppRedirectUris.fs`, `AppSecret.fs`, `AppDelete.fs` | refuse a declared app (`app-declared`); `AppDelete` deletes the history row of an un-declared app |
| `Handlers/Admin/PasskeyConnector.fs`, `ConnectorToggle.fs`, `ConnectorDelete.fs` | the mark; refuse a declared passkey connector (`connector-declared`) |
| `Views/Texts.fs` | the designer's words |
| `.specify/memory/constitution.md` | 3.2.0, Principle V paragraph |
| `src/CosmicChimps.Aspire.Hosting.Umbral/` | `UmbralAppResource.cs`, `PasskeySignUp`, `UmbralAppType`, the builder methods, `WithReference(app)`, the declaration writer |
| `tests/Umbral.Tests/DeclarationTests.fs` (new) | parse (each refusal), apply (create, unchanged writes nothing, update per field, secret rotation, type change, un-declare, re-declare, no variable un-declares), conflicts (app, passkey), refusal writes nothing, events (details, no secret), admin refusals and the mark, a sign-in through a declared app end to end (stub passkey or fake connector) |
| `tests/Umbral.Tests/DatabaseTests.fs` | 15 → 16 |
| `tests/CosmicChimps.Aspire.Hosting.Umbral.Tests/` | the environment of Umbral and of the project, JSON shape, secret as a reference in the manifest, publish refusal of endpoint URIs, key rule, no variables without `AddApp`/`WithPasskeys` |
| `samples/aspire/` | `AppHost.cs` with `WithPasskeys(Open)` and `AddApp("sample")`, the steps reduced to claiming for the admin area, README |
| docs | `docs/run/aspire.md`, `docs/run/settings.md`, `docs/integrate.md`, `docs/run/backup.md` (schema 16), package README, `samples/README.md` if it lists steps, `changelog.d/019-declared-apps.md` |

## Complexity Tracking

| Item | Why | Simpler alternative rejected |
| --- | --- | --- |
| A declaration read at start, after spec 004 removed the config file | The task: the AppHost must be able to make the apps it starts | Create-only on first start: a changed redirect URI in the AppHost would silently do nothing (task, question 2) |
| Owned fields read-only in the admin area | Otherwise each start undoes the operator's edit, the fault spec 004 removed | Last writer wins: spec 004's fault |
