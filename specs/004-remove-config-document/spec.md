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

No release of Umbral has been published, so no deployment uses the file. The
file is removed outright: there is no upgrade path, no warning about a leftover
file, and no upgrade note in the documentation.

### What the file does today, and what takes its place

| Today, through the file | After this feature |
| --- | --- |
| Rebuild or move an instance without clicking (export, then apply on the new host) | Copy the data directory, which the README already documents as backup and restore. Unlike an export, the copy keeps app secrets, users, sessions and keys. |
| Keep the list of apps and connectors in version control | No replacement. Accepted loss (see Assumptions). |
| Claim a new instance without visiting the setup screen (operator credential hash) | The offline reset command, which already claims an unclaimed instance and prints a credential once (001 FR-017b). The setup screen with its log token remains the normal path. |
| Set an app's secret to a value the operator chose | Not available. A secret comes from the Apps screen, generated and shown once. |
| Set a connector's display name | The Connectors screen (feature 003 makes the display name editable there). |
| Request extra upstream scopes for a connector | Removed. Umbral asks a provider only who a person is, and nothing it gives an app comes from an extra scope. |

### Governance

Removing the file contradicts constitution Principle V and the "Config parity"
question in the Constitution Check. Under the constitution's amendment procedure
this is a MAJOR change (a principle is removed), and it must be made in the same
change set as the feature, not after it.

## Clarifications

### Session 2026-10-10

- Q: An operator upgrades with `umbral.yaml` still in place, or the file-location setting still set. What should Umbral do? → A: Warn and start was preferred, but the question does not arise: no release has been published, so no deployment has a file. The file is removed with no migration path, no leftover-file warning and no upgrade notes.
- Q: Without the file, how does a scripted deployment claim a new instance without reading the setup token from the container log? → A: With the offline reset command, which already claims an unclaimed instance and prints a credential once. The README documents it for this. The credential-hash command is removed, and no environment setting carries a hash.
- Q: Only the file can set a connector's extra upstream scopes. What happens to them? → A: They are removed from connectors entirely, for every connector kind including feature 002's OpenID Connect provider. Umbral requests only the scopes each provider needs for identity.
- Q: Specs 002 and 003 still require extending the file. In which order does the work land? → A: 003 first, without its file parts. 003 drops FR-016 to FR-018 and SC-005 and the file tasks of its plan before it is built. 002 drops User Story 3, FR-019, FR-020, FR-022 and SC-007. 004 is built after 003 and relies on it for the editable connector display name.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The admin area is the one place configuration changes (Priority: P1)

An operator disables a connector from the Connectors screen and restarts the
container for an unrelated reason. The connector stays disabled. With the file,
a restart re-enabled it if the file said `enabled: true`. Every setting the
operator could change before is either on an admin screen or removed and
documented as removed.

**Why this priority**: This is the reason for the feature. With one source of
truth, a change stays made, and later features add a setting in one place.

**Independent Test**: Start an instance with a file at the former default
location and the former file-location setting set. Confirm that nothing in the
file is applied. Change a connector and an app from the admin area, restart
several times, and confirm both changes hold. Request the former export address
and confirm it is gone.

**Acceptance Scenarios**:

1. **Given** a file at the former default location in the data directory, or the
   former file-location setting set, **When** Umbral starts, **Then** nothing in
   the file is read or applied, and startup neither fails nor mentions it.
2. **Given** a connector or app changed or deleted in the admin area, **When**
   Umbral restarts any number of times, **Then** the change holds.
3. **Given** an operator signed in to the admin area, **When** they request the
   former export address, **Then** Umbral answers as it does for any unknown
   address, and no admin screen links to an export.
4. **Given** any connector, **When** a person signs in through it, **Then** only
   the scopes Umbral needs for identity are requested.

---

### User Story 2 - Claim a new instance without the file (Priority: P2)

An operator provisions Umbral from a script and does not want to read a token
out of a container log. They start the container and run the offline reset
command once. It claims the unclaimed instance and prints a credential once,
which the script stores. An operator who follows the getting-started page sees
no difference: the setup screen and its log token are unchanged.

**Why this priority**: Unattended claiming is the one file capability that
protects the instance in the minutes after it starts. It already has a
replacement, but nobody will find it unless the documentation says so.

**Independent Test**: Start a clean instance, run the offline reset command,
and sign in with the credential it printed.

**Acceptance Scenarios**:

1. **Given** a clean instance, **When** it starts, **Then** the first-run path
   (setup banner with a token, setup screen) is exactly as before, within the
   five-minute target of 001 SC-001.
2. **Given** an unclaimed instance, **When** the operator runs the offline reset
   command, **Then** the instance is claimed, the credential is printed once,
   and the setup screen no longer asks for a token.
3. **Given** the product, **When** an operator looks for the credential-hash
   command, **Then** it no longer exists.

---

### User Story 3 - Move or rebuild an instance from its data directory (Priority: P2)

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

### User Story 4 - Documentation and governance say one thing (Priority: P3)

A newcomer reads the README, the connector walkthroughs and the constitution.
None of them mentions a configuration file. A contributor writing the next
feature finds no Constitution Check question about the YAML file and no
requirement to extend it.

**Why this priority**: The documentation is the support channel (Principle VI).
A README that still describes the file would send operators to write one that
nothing reads.

**Independent Test**: Search the README, the connector walkthroughs, the
constitution and the vision document for the file and its setting. Nothing is
found, except where the constitution and vision record that it was dropped.

**Acceptance Scenarios**:

1. **Given** the shipped documentation, **When** a reader looks for how to
   configure Umbral, **Then** they find only the admin area and the environment
   settings (public origin, data directory).
2. **Given** the constitution, **When** a contributor runs the Constitution
   Check for a new feature, **Then** no question asks whether the feature is
   expressible in a YAML file.

---

### Edge Cases

- **A file is left at the former default location** in a development data
  directory. Umbral never reads, modifies, moves or deletes it.
- **The former file-location setting is set.** It has no effect.
- **Feature order.** Feature 003 is built before this feature, without its
  configuration-file requirements, and feature 002 drops its own. So the file
  this feature removes has no 002 or 003 keys in it (FR-014).
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
- **FR-003**: The credential-hash command MUST be removed.
- **FR-004**: The setting that named the file's location MUST be removed, so
  that it has no effect.
- **FR-005**: The security log MUST no longer record an event for applying the
  file.
- **FR-006**: No operator-facing message, log line, page or command output may
  mention the file, the export or the credential-hash command.

#### Settings that only the file could set

- **FR-007**: Extra upstream scopes MUST be removed from connectors of every
  kind, including feature 002's OpenID Connect provider, and MUST NOT be offered
  on any screen. Umbral MUST request only the scopes each provider needs for
  identity.
- **FR-008**: A connector's display name MUST be changeable on the Connectors
  screen. Feature 003 provides that, and this feature is built after 003.
- **FR-009**: Setting an app secret to an operator-chosen value MUST NOT be
  offered. A secret is generated on the Apps screen, shown once and never
  retrievable (001 FR-019).

#### Claiming an instance

- **FR-010**: The operator credential MUST be settable only from the first-run
  setup screen, from the credential screen by a signed-in operator, and by the
  offline reset command. The offline command claims an unclaimed instance (001
  FR-017b) and is the documented way to claim an instance unattended. No
  environment setting may carry the credential or a hash of it.

#### Governance and documentation

- **FR-011**: The constitution MUST be amended in the same change set: Principle
  V removed, the "Config parity" question removed from the Constitution Check,
  the version bumped MAJOR with the rationale stated, and the sync impact report
  updated. The vision document's config-as-code goal MUST be marked as dropped,
  with the reason.
- **FR-012**: The README MUST no longer describe the file. Its configuration
  section, the file-location row of the settings reference, the file row of the
  data-directory table and the file's mention in troubleshooting MUST be
  removed. The README MUST say how to claim an instance unattended with the
  offline reset command. The backup and restore section MUST present copying
  the data directory as the way to move or rebuild an instance.
- **FR-013**: Each connector setup walkthrough MUST drop its "same thing in the
  configuration file" section and its mention of extra scopes.
- **FR-014**: These requirements are superseded by this feature, and their spec
  files MUST say so where the requirement is written: 001 FR-017a (the file
  part), FR-027 to FR-030, SC-006, the export part of SC-008, the "Configuration
  document" entity and User Story 5; 002 User Story 3, FR-019, FR-020, FR-022
  and SC-007; 003 FR-016 to FR-018 and SC-005. Features 002 and 003 MUST drop
  their requirements before they are built, and 003's plan MUST drop its
  configuration-file tasks.

### Key Entities

- **Configuration document**: Removed. It was the declarative description of
  every App and Connector, plus the operator credential hash, with secrets held
  by reference.

No entity is added. App, Connector and User are unchanged, except that a
Connector no longer has extra scopes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A change made in the admin area survives any number of restarts,
  in 100% of test runs, including with a file at the former default location.
- **SC-002**: A clean instance still reaches a first social login within five
  minutes of starting (001 SC-001 unchanged).
- **SC-003**: An instance started from a documented copy of another instance's
  data directory, with the same public origin, serves the same sign-ins,
  including app secrets and signed-in sessions, with no admin interaction. This
  replaces 001 SC-006.
- **SC-004**: A search of the shipped product, the README and the connector
  walkthroughs for the file, its setting, the export and the credential-hash
  command finds nothing. The constitution and vision mention it only to record
  that it was dropped.
- **SC-005**: A later feature that adds a setting changes the admin area only.
  The Constitution Check asks for no file format, validation or round-trip test.

## Assumptions

### Decisions taken without an explicit answer

The task that started this feature asked to decide these questions. Each is
marked for confirmation at plan review, and each is cheap to reverse before
implementation.

| Question | Assumed answer | Why this one | Cost if reversed |
| --- | --- | --- | --- |
| What replaces rebuilding an instance from an export? | Copying the data directory, already documented as backup and restore | The copy is complete and an export was not: app secrets, users, sessions and keys survive it. Principle II already makes `cp` the backup. | A new import/export feature, specified on its own |
| What replaces keeping apps and connectors in version control? | Nothing; accepted loss | Keeping it is the cost this feature removes. Any partial replacement brings back a second source of truth. | Re-specify a config file under a new principle |

### Context assumptions

- No release of Umbral has been published, so no deployment, stored connector
  or stored app depends on the file (see Clarifications).
- The first-run path, the setup token, the credential screen and the offline
  reset command keep their 001 behaviour. Only the file's way to set the
  credential goes away.
- Public and confidential apps can both be created from the Apps screen, so
  that choice loses nothing with the file.
- The environment settings for public origin and data directory stay. They are
  process settings, not configuration of apps and connectors, and the
  constitution's equivalence never covered them.
- Feature 003 makes a connector's display name editable on the Connectors
  screen, and is built before this feature (see Clarifications).
- Out of scope: any other import or export format, an admin API, scheduled or
  remote backups, and moving an instance to a different public origin.
