# Feature Specification: Remove the Configuration Document

**Feature Branch**: `004-remove-config-document`

**Created**: 2026-10-10

**Status**: Draft

**Input**: User description: "Remove the declarative YAML configuration document (export screen, parser, validation, config-file apps, config-file operator credential) and decide what replaces it and how existing deployments migrate." Origin: the project owner, while specifying 003-per-app-connectors on 2026-10-10: "I think we should drop the yaml configuration in the future".

## Context

Since spec 001, Umbral can be configured in two ways that must stay equivalent:
the admin area, and one YAML file (`umbral.yaml`) that Umbral reads at every
start and that it can export (001 FR-027 to FR-030, SC-006; constitution
Principle V, "Config-as-Code Parity"). The file lists connectors and apps with
secrets held as environment references. It can also carry a hash of the
operator credential, so that an instance is claimed without visiting the setup
screen (001 FR-017a).

That equivalence costs something in every feature. Each new setting must be
added to the admin area and also to the file's format, its validation, its
export and its round-trip tests. Feature 002 adds a provider kind with an
issuer. Feature 003 adds an app's connector choice and a connector's shared
setting. Both carry a "configuration parity" block of requirements for that
reason. The file also gives the instance a second source of truth that wins on
every start, so a change made in the admin area is silently undone by a restart
for anything the file lists. And it cannot do what it promises: app secrets are
stored only as hashes, so an export never reproduces an instance completely.

This feature removes the file and makes the admin area the only place
configuration is changed. It adds no concept and no screen. It removes a
surface.

### What the file does today, and what takes its place

| Today, through the file | After this feature |
| --- | --- |
| Rebuild or move an instance without clicking (export, then apply on the new host) | Copy the data directory, which the README already documents as backup and restore. Unlike an export, the copy keeps app secrets, users, sessions and keys. |
| Keep the list of apps and connectors in version control | No replacement. Accepted loss; the README says so (see Assumptions). |
| Claim a new instance without visiting the setup screen (operator credential hash) | The offline reset command, which already claims an unclaimed instance and prints a credential once (001 FR-017b). The setup screen with its log token remains the normal path. |
| Set an app's secret to a value the operator chose | Not available. Secrets already set this way keep working; a new one comes from regeneration on the Apps screen. |
| Set a connector's display name | The Connectors screen (feature 003 makes the display name editable there). |
| Request extra upstream scopes for a connector | Retired. Umbral asks a provider only who a person is, and nothing it gives an app comes from an extra scope. |

### Governance

Removing the file contradicts constitution Principle V and the "Config parity"
question in the Constitution Check. Under the constitution's amendment procedure
this is a MAJOR change (a principle is removed), and it must be made in the same
change set as the feature, not after it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An existing deployment upgrades and keeps working (Priority: P1)

An operator has run Umbral for months with an `umbral.yaml` that declares their
GitHub connector, three apps and the operator credential hash. Their container
updates automatically overnight to the version without the file. In the
morning every app still signs people in, nobody has to sign in again, and the
admin area tells the operator that the file is no longer read and what to do
with it.

**Why this priority**: An upgrade that breaks sign-in for the people behind an
operator's apps is the worst outcome this feature could have. Removing the file
is only acceptable if deployments that relied on it notice nothing except the
notice.

**Independent Test**: Start an instance from a file that declares connectors,
confidential and public apps, and the operator credential hash. Sign people in.
Upgrade with the file still mounted and the referenced environment variables
still set. Confirm that every sign-in, app secret, session and the operator
credential works unchanged, and that the log and the admin area both carry the
notice.

**Acceptance Scenarios**:

1. **Given** an instance whose connectors, apps and operator credential were
   applied from the file, **When** it is upgraded and started with the file
   still in place, **Then** every connector, app (with its type, redirect
   addresses and secret) and the operator credential is unchanged, and every
   person signs in to every app as before with the same user identifier.
2. **Given** a file still exists at the former default location in the data
   directory, or the former file-location setting is still set, **When** Umbral
   starts, **Then** it starts normally, and its log carries a warning that names
   the file or setting, says it is no longer read, says that everything it
   applied before is kept, and gives the next action: make changes in the admin
   area, then delete the file and remove the setting.
3. **Given** the condition of scenario 2, **When** the operator opens any admin
   screen, **Then** the same notice is shown there, and it disappears after the
   file is removed, the setting is removed, and Umbral restarts.
4. **Given** the operator edited the file after the last start under the
   previous version, **When** they upgrade, **Then** those edits are not
   applied, and the notice says that changes made to the file are not applied.
5. **Given** a file that the previous version would have refused (malformed,
   a literal secret, a missing variable), **When** the new version starts,
   **Then** it starts normally and only shows the notice.
6. **Given** the security log holds entries recorded when the file was applied,
   **When** the operator reads the log after the upgrade, **Then** those entries
   are still there and readable.

---

### User Story 2 - The admin area is the one place configuration changes (Priority: P1)

The same operator disables a connector from the Connectors screen and restarts
the container for an unrelated reason. The connector stays disabled. With the
file, a restart re-enabled it because the file said `enabled: true`. Every
setting the operator could change before is either on an admin screen or
retired and documented as retired.

**Why this priority**: This is the reason for the feature. With one source of
truth, a change stays made, and later features add a setting in one place.

**Independent Test**: Change a connector and an app from the admin area,
restart several times, and confirm both changes hold. Request the former export
address and confirm it is gone.

**Acceptance Scenarios**:

1. **Given** a connector or app that was originally applied from the file,
   **When** the operator changes or deletes it in the admin area and Umbral
   restarts any number of times, **Then** the change holds.
2. **Given** an operator signed in to the admin area, **When** they request the
   former export address, **Then** Umbral answers as it does for any unknown
   address, and no admin screen links to an export.
3. **Given** a connector that had extra upstream scopes set from the file,
   **When** a person signs in through it after the upgrade, **Then** only the
   scopes Umbral needs for identity are requested, and the person is
   recognised as the same user.
4. **Given** a confidential app whose secret was set from the file, **When**
   its integrating app presents that secret after the upgrade, **Then** it is
   accepted. **When** the operator regenerates the secret on the Apps screen,
   **Then** the new secret is shown once and the old one stops working, as for
   any app.

---

### User Story 3 - Claim a new instance without the file (Priority: P2)

An operator provisions Umbral from a script and does not want to read a token
out of a container log. Previously they put a credential hash in the file.
Now they start the container and run the offline reset command once. It claims
the unclaimed instance and prints a credential once, which the script stores.
An operator who follows the getting-started page sees no difference: the setup
screen and its log token are unchanged.

**Why this priority**: Unattended claiming is the one file capability that
protects the instance in the minutes after it starts. It already has a
replacement, but nobody will find it unless the documentation says so.

**Independent Test**: Start a clean instance, run the offline reset command,
and sign in with the credential it printed. Separately, run the former
credential-hash command and confirm it explains that it was removed and starts
nothing.

**Acceptance Scenarios**:

1. **Given** a clean instance with no file, **When** it starts, **Then** the
   first-run path (setup banner with a token, setup screen) is exactly as
   before, within the five-minute target of 001 SC-001.
2. **Given** an unclaimed instance, **When** the operator runs the offline reset
   command, **Then** the instance is claimed, the credential is printed once,
   and the setup screen no longer asks for a token.
3. **Given** a clean instance started with a file that carries an operator
   credential hash, **When** it starts, **Then** the instance stays unclaimed,
   the setup banner is printed as without a file, and the notice of User Story
   1 is also printed.
4. **Given** the former credential-hash command, **When** an operator runs it,
   **Then** it prints that the command was removed and names the setup screen
   and the offline reset command as the ways to set a credential. It exits with
   a failure status, starts no server, and touches no data directory.

---

### User Story 4 - Move or rebuild an instance from its data directory (Priority: P2)

An operator moves Umbral from a Raspberry Pi to a new mini PC. They stop the
container, copy the data directory, start the same image on the new machine
with the same public origin, and point DNS at it. Every connector, app, app
secret, user and signed-in session is there. They did not re-enter any
provider secret or update any app, which an export required.

**Why this priority**: This is what export and apply were for. The data
directory already does it better, but the README presents it as backup, not
as the way to move an instance.

**Independent Test**: Copy a running instance's data directory, as documented,
to a second host. Start it with the same public origin and confirm that it
serves the same sign-ins with no admin interaction.

**Acceptance Scenarios**:

1. **Given** a copy of an instance's data directory taken as documented,
   **When** the same image starts from it on another host with the same public
   origin, **Then** every connector, app, app secret, user and session works
   with no admin interaction.

---

### User Story 5 - Documentation and governance say one thing (Priority: P3)

A newcomer reads the README, the connector walkthroughs and the constitution.
None of them mentions a configuration file, except the upgrade note for people
who had one. A contributor writing the next feature finds no Constitution Check
question about the YAML file and no requirement to extend it.

**Why this priority**: The documentation is the support channel (Principle VI).
A README that still describes the file would send operators to write one that
nothing reads.

**Independent Test**: Search the README, the connector walkthroughs, the
constitution and the vision document for the file. The only hit is the upgrade
note.

**Acceptance Scenarios**:

1. **Given** the shipped documentation, **When** a reader looks for how to
   configure Umbral, **Then** they find only the admin area and the environment
   settings (public origin, data directory), plus one upgrade section for
   operators who had a file.
2. **Given** the constitution, **When** a contributor runs the Constitution
   Check for a new feature, **Then** no question asks whether the feature is
   expressible in a YAML file.

---

### Edge Cases

- **The file-location setting points at a file that does not exist.** The
  notice is still shown, because the setting is still set and has no effect.
- **The file is in the data directory.** Umbral never modifies, moves or deletes
  it. Backups keep copying it until the operator deletes it. It is harmless.
- **Environment variables the file referenced** (connector secrets, the
  operator hash) stay set on the container. Umbral never read them except
  through the file, so they have no effect. The upgrade section says they can be
  removed.
- **An operator relied on "the file wins on every start"** to undo admin
  changes. That no longer happens. The upgrade section says so.
- **An automatic updater upgrades the container.** Startup is not refused
  because of a leftover file, so the instance keeps serving.
- **A fake development connector exists only because the file declared it.**
  It stays in the database like any other connector, and the existing refusal to
  start with a fake connector on an instance that does not allow one still
  applies.
- **Feature order.** If feature 002 or 003 has shipped its configuration-file
  requirements before this feature, this feature removes those keys with the
  rest of the file. If this feature ships first, those requirements are
  withdrawn before they are built (FR-019).
- **A deployment is moved with a different public origin.** This is outside this
  feature. Issued tokens and the redirect addresses registered with providers
  depend on the origin, with or without a file.

## Requirements *(mandatory)*

### Functional Requirements

#### Removal

- **FR-001**: Umbral MUST NOT read, parse, validate or apply a configuration
  document, at startup or at any other time.
- **FR-002**: The configuration export MUST be removed. Requesting its former
  address MUST be answered as an unknown address is, whether or not the
  requester is signed in, and no screen may link to an export.
- **FR-003**: The credential-hash command MUST be removed. Running it MUST print
  that it was removed and name the setup screen and the offline reset command as
  the ways to set the operator credential. It MUST exit with a failure status,
  MUST NOT start the server, and MUST NOT read or write a data directory.
- **FR-004**: The setting that named the file's location MUST have no effect
  beyond triggering the notice of FR-006.

#### Upgrade of existing deployments

- **FR-005**: Every connector, app and operator credential that a previous
  version applied from the file MUST be unchanged after the upgrade. This covers
  an app's secret, type and redirect addresses. From then on each MUST be
  treated exactly like one created in the admin area: editable, deletable, and
  never overwritten by a restart.
- **FR-006**: When, at startup, a file exists at the former default location in
  the data directory or the former file-location setting is set, Umbral MUST
  start normally and MUST log a warning. The warning names the file or setting,
  says it is no longer read, says that what it applied before is kept and that
  changes made to it since are not applied, and gives the next action: make
  changes in the admin area, then delete the file and remove the setting.
- **FR-007**: While the condition of FR-006 holds, the admin screens MUST show
  the same notice. No new screen may be added for it.
- **FR-008**: Umbral MUST NOT modify, move or delete the file.
- **FR-009**: The upgrade MUST need no operator action to keep a working
  instance: no migration command, no edit, no second restart.
- **FR-010**: Security log entries recorded when the file was applied before the
  upgrade MUST remain in the log and readable. No new entry MAY name the file as
  its source.

#### Settings that only the file could set

- **FR-011**: Extra upstream scopes for a connector MUST be retired. Umbral MUST
  request only the scopes it needs for identity, including for connectors that
  had extra scopes stored before the upgrade. Who a person is, and the user they
  map to, MUST NOT change as a result.
- **FR-012**: A connector display name applied from the file MUST be kept, and
  MUST be changeable on the Connectors screen. Feature 003 provides that. If
  this feature ships first, it provides it.
- **FR-013**: An app secret set from the file MUST keep working until the
  operator regenerates it. Regeneration then behaves as for any app: shown
  once, never retrievable (001 FR-019). Setting an app secret to an
  operator-chosen value MUST NOT be offered.

#### Claiming an instance

- **FR-014**: The operator credential MUST be settable only from the first-run
  setup screen, from the credential screen by a signed-in operator, and by the
  offline reset command. The offline command claims an unclaimed instance (001
  FR-017b).
- **FR-015**: An instance started for the first time with a file present MUST
  stay unclaimed and MUST print the setup banner, exactly as without a file.

#### Governance and documentation

- **FR-016**: The constitution MUST be amended in the same change set: Principle
  V removed, the "Config parity" question removed from the Constitution Check,
  the version bumped MAJOR with the rationale stated, and the sync impact report
  updated. The vision document's config-as-code goal MUST be marked as dropped,
  with the reason.
- **FR-017**: The README MUST no longer describe the file. Its configuration
  section, the file-location row of the settings reference, the file row of the
  data-directory table and the file's mention in troubleshooting MUST be
  removed. In their place, one upgrade section for operators who had a file MUST
  say what happens at upgrade, what is kept, what to delete (the file, the
  setting, the referenced variables), that extra scopes are retired, that a
  restart no longer undoes admin changes, and how to claim an instance
  unattended. The backup and restore section MUST also present copying the data
  directory as the way to move or rebuild an instance.
- **FR-018**: Each connector setup walkthrough MUST drop its "same thing in the
  configuration file" section and its mention of extra scopes.
- **FR-019**: These requirements are superseded by this feature, and their spec
  files MUST say so where the requirement is written: 001 FR-017a (the file
  part), FR-027 to FR-030, SC-006, the export part of SC-008, the "Configuration
  document" entity and User Story 5; 002 User Story 3, FR-019, FR-020, FR-022
  and SC-007; 003 FR-016 to FR-018 and SC-005. A feature not yet implemented
  MUST NOT implement them.
- **FR-020**: Every new message this feature adds (the warning and notice of
  FR-006 and FR-007, the reply of FR-003) MUST say what to do next as well as
  what happened (Principle VI).

### Key Entities

- **Configuration document**: Retired. It was the declarative description of
  every App and Connector, plus the operator credential hash, with secrets held
  by reference.
- **Leftover configuration file**: A file an operator may still have at the
  former location, or named by the former setting. Umbral reports it and never
  reads it.

No entity is added. App, Connector and User are unchanged, except that a
Connector no longer has extra scopes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After an upgrade with the file still in place, 100% of the
  sign-ins that worked before succeed, with the same user identifiers and no
  operator action.
- **SC-002**: On the first start after the upgrade, an operator who still has a
  file is told, both in the log and on every admin screen, that it is no longer
  read and what to do. This is checked by review of both messages.
- **SC-003**: A change made in the admin area survives any number of restarts,
  in 100% of test runs.
- **SC-004**: A clean instance still reaches a first social login within five
  minutes of starting (001 SC-001 unchanged).
- **SC-005**: An instance started from a documented copy of another instance's
  data directory, with the same public origin, serves the same sign-ins,
  including app secrets and signed-in sessions, with no admin interaction. This
  replaces 001 SC-006.
- **SC-006**: A search of the shipped product, the README, the connector
  walkthroughs and the constitution for the file and its setting finds only the
  upgrade section, the notice of FR-006, and the removal reply of FR-003.
- **SC-007**: A later feature that adds a setting changes the admin area only.
  The Constitution Check asks for no file format, validation or round-trip test.

## Assumptions

### Decisions taken without an explicit answer

The task that started this feature asked to decide these questions. None of
them had a single safe default, and the project owner was not asked
interactively. Each is marked for confirmation at plan review, and each is
cheap to reverse before implementation.

| Question | Assumed answer | Why this one | Cost if reversed |
| --- | --- | --- | --- |
| What happens to a leftover file at upgrade? | Start normally, apply nothing, warn in the log and the admin area | Everything the file declared was already applied on every previous start, so the database already holds it. Refusing to start would turn an automatic update into an outage for no gain. | One startup branch that refuses to start until the file is removed |
| What replaces rebuilding an instance from an export? | Copying the data directory, already documented as backup and restore | The copy is complete and an export was not: app secrets, users, sessions and keys survive it. Principle II already makes `cp` the backup. | A new import/export feature, specified on its own |
| What replaces keeping apps and connectors in version control? | Nothing; accepted and documented as a loss | Keeping it is the cost this feature removes. Any partial replacement brings back a second source of truth. | Re-specify a config file under a new principle |
| What replaces the operator credential hash in the file? | The offline reset command, which already claims an unclaimed instance | It exists, it needs more authority than the web page (shell access), and it puts no long-lived credential material in the environment. | An environment setting holding the hash, with the "only when it changes" rule of 001 FR-017a |
| What happens to extra upstream scopes? | Retired; no longer requested, even where stored | Umbral answers only "who is this person", and nothing it gives an app comes from an extra scope. A setting with no screen would be invisible and unchangeable. | One field on the connector form |

### Context assumptions

- The first-run path, the setup token, the credential screen and the offline
  reset command keep their 001 behaviour. Only the file's way to set the
  credential goes away.
- Public and confidential apps can both be created from the Apps screen, so
  that choice loses nothing with the file.
- The environment settings for public origin and data directory stay. They are
  process settings, not configuration of apps and connectors, and the
  constitution's equivalence never covered them.
- Feature 003 makes a connector's display name editable on the Connectors
  screen. This feature is expected to ship after 003. FR-012 covers the other
  order.
- Out of scope: any other import or export format, an admin API, scheduled or
  remote backups, and moving an instance to a different public origin.
