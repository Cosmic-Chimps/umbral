# Feature Specification: External Secret Stores for Connector Secrets

**Feature Branch**: `005-external-secret-stores`

**Created**: 2026-10-12

**Status**: Draft

**Input**: User description: "Pluggable external secret storage for connector client secrets: let the operator choose, as an option, to keep upstream connector secrets in an external secrets manager — AWS Secrets Manager, Google Cloud Secret Manager, Azure Key Vault, or BellaBaxter — instead of the default local encrypted store. Umbral writes the secret to the manager when the operator saves it in the Connectors form, keeps only a reference, reads it back at sign-in time, and deletes it when the connector is deleted. The local store stays the default and fully supported." Origin: the deferred task recorded with the project owner on 2026-10-09 (`.alta/tasks/2026-10-09-spec-002-pluggable-secret-storage-backends-aws-secrets-manag-dhz6.md`), with Google Cloud Secret Manager added on 2026-10-12.

## Context

A connector holds one secret Umbral must be able to read back: the client
secret of the OAuth application the operator created at Discord, GitHub, Google
or their own OpenID Connect provider. Umbral sends it to that provider on every
sign-in. Today it is encrypted with the instance's key ring and the ciphertext
is kept in the database, both in the data directory. That is the right default
for the audience: one container, `cp` to back up, nothing to reach over the
network during a login.

It is not what an operator with a compliance requirement, a cloud account or a
BellaBaxter organisation wants. They already keep secrets in a manager, with
its access policy, audit log and rotation, and they want Umbral's secrets there
too, with Umbral holding only a pointer.

The shape matters. This is **not** reading configuration from a manager once
at startup. Umbral drives the manager at runtime, in both directions: the
operator pastes a secret into the Connectors form, Umbral writes it to the
manager and keeps a reference, then reads it back when someone signs in, and
removes it when the connector is deleted.

The seam for this already exists. Since spec 001 every connector's secret is
stored as a reference that names the store which wrote it, and the local store
is one implementation of that store interface. This feature adds four more
implementations, the settings that choose between them, and the way to move an
instance's secrets from one store to another.

### What is in and out

| Secret | In scope | Why |
| --- | --- | --- |
| Connector client secret | Yes | Umbral needs the plaintext back to send it upstream. |
| App client secret | No | Stored only as a hash. There is no plaintext for any store to hold. |
| Operator credential | No | Stored only as a hash, only ever compared. |
| Token signing and encryption keys, key ring | No | Separate concern; they stay in the data directory (Principle IV). |

### Principle II, stated so nobody has to re-derive it

Principle II forbids a **required** runtime dependency outside the container.
An external secret store is **optional**: a deployment that sets nothing keeps
the local store and behaves exactly as before, with no new setting, no network
dependency and no change in what the data directory holds. Choosing an external
store is the operator trading self-containment for their own policy, knowingly
and by explicit setting. That trade is permitted; making it the default, or
making any part of Umbral depend on one, would not be.

### Plaintext storage was considered and rejected

Keeping a connector secret in plaintext, in the database or in a setting, was
raised in 2026-10-09 and rejected. Principle IV requires secrets encrypted at
rest by default, and an environment variable holding the secret already covers
the docker-compose and Kubernetes cases without a new storage mode. This
feature adds no plaintext mode, not even a development-only one.

## Clarifications

### Session 2026-10-12

- Q: Is the store for new and replaced secrets chosen once per instance, or per
  connector in the Connectors form? → A: Once per instance, by a deployment
  setting. The admin area only shows which store holds each secret.
- Q: When a cached secret has expired and the manager cannot be reached, does
  the sign-in fail at once or keep using the expired value? → A: Lenient by
  default: the expired value stays usable from memory for a grace period
  (default 1 hour, operator-settable, 0 makes it strict) while Umbral keeps
  retrying the manager. It is still never written to disk or kept across a
  restart.
- Q: How is a store configured? → A: From environment variables only, using
  each vendor's standard variables, the same way Frunchy configures BellaBaxter
  (`BELLA_BAXTER_API_KEY` and the other variables read by
  `BellaBaxter.AspNet.Configuration`). See FR-004 and its table. One explicit
  Umbral setting names the store; Umbral does not guess it from which
  credentials happen to be present, because a host's ambient identity (an
  instance role, a managed identity) has no variable to detect and a stray
  `AWS_*` variable would silently move secrets.
- Q: Can Umbral just load the connector secrets as configuration, as Frunchy
  does with `AddBellaSecrets`? → A: Not as the mechanism of this feature. A
  configuration provider only reads, at startup and by polling; it cannot
  write the secret the operator pastes into the Connectors form, nor delete it
  with the connector. The BellaBaxter store reuses the same packages and the
  same environment variables for its credentials and client, and writes
  through the client. Loading Umbral's own settings from BellaBaxter is a
  separate question and out of scope here. (Planning found that the
  configuration package's variable mapping and client builder are internal,
  so Umbral reads the same variables itself and uses `BellaBaxter.Client`
  directly; the operator-facing contract is identical. Research R4.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Keep new connector secrets in my secret manager (Priority: P1)

An operator runs Umbral on a host that already has access to their AWS, Google
Cloud, Azure or BellaBaxter account. They set Umbral's deployment settings to
use that manager and restart the container. From then on, whenever they create
a connector or replace a connector's secret on the Connectors screen, the
secret is written to their manager under a name they can recognise, and
Umbral's data directory holds only a reference to it. People sign in through
that connector exactly as before. When the operator deletes the connector, the
secret is removed from the manager.

**Why this priority**: This is the feature. Without it nothing else here has a
reason to exist, and on its own it already serves a fresh deployment.

**Independent Test**: Configure one external store (a test double of it, or a
real account), create a GitHub connector with a known secret, complete a
sign-in through it, confirm the manager holds the secret and the data directory
holds no form of it, then delete the connector and confirm the manager no
longer holds it.

**Acceptance Scenarios**:

1. **Given** an instance configured for an external store, **When** the
   operator creates a connector with a client secret, **Then** the secret is
   written to the manager, the connector is saved with a reference to it, and
   the Connectors screen shows the masked secret and which store holds it.
2. **Given** that connector, **When** a person signs in through it, **Then**
   Umbral reads the secret from the manager and the sign-in completes as it
   would with the local store.
3. **Given** that connector, **When** the operator replaces its secret,
   **Then** the manager holds the new value, the next sign-in uses it, and the
   old value is no longer used.
4. **Given** that connector, **When** the operator deletes it, **Then** Umbral
   asks the manager to delete the secret and the connector is gone.
5. **Given** an instance configured for an external store, **When** the
   operator saves a connector and the manager refuses the write (unreachable,
   permission denied, quota), **Then** nothing is saved, no error page is
   shown, and the screen's message names the store, the cause, and what to
   check. The operator enters the secret again.
6. **Given** an instance with no secret store setting, **When** it starts,
   **Then** it uses the local store and behaves exactly as before this feature.

---

### User Story 2 - Sign-ins keep working when the manager has a bad moment (Priority: P1)

The secret manager is a network service. It can be slow, rate-limited or
briefly unreachable, and a product pitched as "one container on a Pi" would
otherwise turn every such moment into "nobody can sign in anywhere". Umbral
keeps a secret it has just read in memory for a bounded time, so that a busy
morning does not call the manager on every sign-in and a short outage does not
stop sign-ins that had been working.

**Why this priority**: Without it the external store makes Umbral strictly less
available than the default. It must ship with Story 1, not after it.

**Independent Test**: With a test double of a store, sign in once, make the
store unreachable, sign in again within the cache window and confirm success;
then wait past the bound and confirm the sign-in fails with the operator-facing
cause recorded.

**Acceptance Scenarios**:

1. **Given** a connector whose secret was read less than the cache lifetime
   ago, **When** a person signs in, **Then** Umbral does not contact the
   manager.
2. **Given** a connector whose cached secret has expired less than the grace
   period ago and a manager that is unreachable, **When** a person signs in,
   **Then** the sign-in succeeds with the cached value and the failed refresh
   is logged.
3. **Given** the grace period has also passed, or is set to 0, and the manager
   is still unreachable, **When** a person signs in, **Then** the sign-in
   fails as in scenario 4.
4. **Given** a secret that cannot be read at all, **When** a person signs in,
   **Then** the person sees the existing sign-in failure page with a code, and
   the security log records the connector, the store and the cause, without
   the secret.
5. **Given** the operator replaces a connector's secret, **When** the next
   sign-in happens, **Then** it uses the new value, regardless of anything
   cached.
6. **Given** Umbral restarts, **When** it comes back, **Then** no secret read
   from an external manager survives the restart anywhere on disk.

---

### User Story 3 - Move an existing instance's secrets in, and back out (Priority: P2)

An operator who has run Umbral with the local store for months decides to move
to their manager, or one who used a manager wants to leave it. They change the
setting, restart, and run one offline command that moves every connector's
secret into the store now configured. Each connector is moved on its own: a
connector that fails is left exactly as it was, still working from its old
store, and the command says which ones and why. Nothing is ever lost, even if
the command is interrupted.

**Why this priority**: Needed for any instance that exists before the switch,
but a fresh deployment does not need it, and replacing each secret by hand on
the Connectors screen is a workable fallback for a handful of connectors.

**Independent Test**: With three connectors in the local store, configure an
external store and run the move; confirm all three sign in and the data
directory no longer holds their ciphertext. Configure the local store again and
run the move; confirm all three sign in and the manager no longer holds them.
Interrupt a move halfway and confirm every connector still signs in.

**Acceptance Scenarios**:

1. **Given** connectors whose secrets live in a store other than the configured
   one, **When** the operator runs the move command, **Then** each secret is
   written to the configured store, read back and compared, the connector's
   reference is switched, and only then is the old copy deleted.
2. **Given** the move fails for one connector, **When** the command finishes,
   **Then** that connector still references and signs in from its old store,
   the others are moved, and the command exits non-zero naming the connector
   and the cause.
3. **Given** the command is interrupted at any point, **When** it is run again,
   **Then** it completes the remaining connectors and leaves no connector
   without a readable secret.
4. **Given** all secrets are already in the configured store, **When** the
   command runs, **Then** it changes nothing and says so.
5. **Given** the operator wants to see before acting, **When** they run the
   command in dry-run form, **Then** it lists which connectors would move from
   where to where, and changes nothing.

---

### User Story 4 - Know where my secrets are, and why something fails (Priority: P3)

The operator can tell, from the Connectors screen, which store holds each
connector's secret, and is told plainly when the configured store is
misconfigured or unreachable or when a connector's secret lives in a store the
instance can no longer read.

**Why this priority**: Makes the feature supportable by the operator alone
(Principle VI), but the feature works without it.

**Independent Test**: Configure an external store with wrong credentials,
start Umbral, and confirm the startup log and the Connectors screen both say
what is wrong and what to do; confirm each connector card names its store.

**Acceptance Scenarios**:

1. **Given** a connector, **When** the operator views the Connectors screen,
   **Then** its card names the store holding its secret (for example
   "Kept in Azure Key Vault").
2. **Given** a store setting that is incomplete (for example the store is named
   but its required location setting is missing), **When** Umbral starts,
   **Then** it refuses to start and the message names the missing setting.
3. **Given** a complete store setting but a manager Umbral cannot reach or is
   not allowed to use, **When** Umbral starts, **Then** it starts, logs the
   cause, and the Connectors screen shows a notice saying saving and new
   sign-ins through affected connectors will fail until it is fixed.
4. **Given** a connector whose secret lives in a store the instance is no
   longer configured to read, **When** the operator views it, **Then** its card
   says so and tells them to re-enter the secret or configure that store again.

---

### Edge Cases

- **Deleted then re-created connector.** Some managers keep a deleted secret
  in a recovery window during which its name cannot be reused. Creating a
  connector with the same slug again within that window MUST still succeed.
- **Secret changed or deleted directly in the manager.** A value rotated in
  the manager is used from the next read after the cache expires. A secret
  deleted there makes sign-ins through that connector fail with a message
  telling the operator to re-enter the secret. The masked hint on the
  Connectors screen reflects the value last saved through Umbral.
- **Deletion refused by the manager.** The connector is still deleted; the
  operator is told the secret's name in the manager so they can remove it by
  hand, and the security log records it.
- **Two Umbral instances sharing one manager**, or one instance restored from a
  copy of another's data directory. Names in the manager are prefixed by a
  setting so that two instances can be kept apart. A restored copy that keeps
  the same prefix shares the secrets with the original; deleting a connector on
  one removes the secret for both. The documentation says so.
- **Backup.** With an external store the data directory no longer contains the
  connector secrets. A restore needs access to the same manager. The
  documentation's backup section says so.
- **Manager naming rules differ** (allowed characters, length). The name Umbral
  gives a secret MUST be valid in the manager it is written to and still
  contain the prefix and the connector's slug, so a person can recognise it in
  the manager's console.
- **Concurrent edits.** Two saves of the same connector's secret in quick
  succession leave the connector referencing the value of the save that
  completed last, and never a reference to a value that was not stored.
- **Very slow manager.** A read or write that does not answer within a bounded
  time is treated as a failure, so that a hung manager cannot hold a sign-in or
  a form submission open indefinitely.
- **Store switched without moving.** Connectors keep working from the store
  their reference names, as long as the instance can still read that store
  (the local store can always be read). New and replaced secrets go to the
  configured store.

## Requirements *(mandatory)*

### Functional Requirements

**Choosing a store**

- **FR-001**: Umbral MUST support five secret stores for connector secrets:
  the local encrypted store, AWS Secrets Manager, Google Cloud Secret Manager,
  Azure Key Vault, and BellaBaxter.
- **FR-002**: The local store MUST remain the default. A deployment that sets
  no secret store setting MUST behave exactly as before this feature, with no
  new required setting and no outbound network call to any manager.
- **FR-003**: The store used for new and replaced secrets MUST be chosen once
  per instance, by one explicit deployment setting (indicatively
  `UMBRAL__SECRET_STORE` = `local` | `aws` | `gcp` | `azure` | `bellabaxter`;
  the final name is settled in the plan). The Connectors form MUST NOT offer a
  choice of store.
- **FR-004**: Everything that locates and authenticates to a manager MUST come
  from environment variables, using the vendor's standard variable names so
  that an operator's existing setup works unchanged, and MUST NOT be entered in
  the admin area or stored in the database. Where a vendor's standard
  credential chain also accepts the host's ambient identity (instance role,
  workload identity, managed identity), Umbral MUST accept it too, so the
  credential variables are optional on such a host. At minimum:

  | Store | Credentials (vendor standard) | Location |
  | --- | --- | --- |
  | AWS Secrets Manager | `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` (optionally `AWS_SESSION_TOKEN`, `AWS_PROFILE`) | `AWS_REGION` |
  | Google Cloud Secret Manager | `GOOGLE_APPLICATION_CREDENTIALS` (path to a mounted service account key) | `GOOGLE_CLOUD_PROJECT` |
  | Azure Key Vault | `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` | the vault's URI, in an Umbral setting |
  | BellaBaxter | `BELLA_BAXTER_API_KEY` (or `BELLA_BAXTER_ACCESS_TOKEN`), as read by `BellaBaxter.AspNet.Configuration` in Frunchy | `BELLA_BAXTER_URL`, `BELLA_BAXTER_PROJECT`, `BELLA_BAXTER_ENV` |

  Umbral-specific settings (the store, the Azure vault URI, the name prefix,
  the cache lifetime, the grace period, and the BellaBaxter provider when the
  environment has more than one) follow the existing `UMBRAL__…` convention.
- **FR-005**: An incomplete store setting MUST stop Umbral at startup with a
  message that names the missing or invalid setting. A complete setting for a
  manager that is unreachable or refuses access MUST NOT stop Umbral; it MUST be
  logged with its cause and surfaced on the Connectors screen.
- **FR-006**: A deployment MUST be able to give the name prefix Umbral uses in
  the manager, with a documented default, so that several instances can share
  one manager.

**Writing, reading and deleting**

- **FR-007**: When a connector is created, or its secret replaced, with an
  external store configured, Umbral MUST write the secret to the manager
  before saving the connector, and MUST save the connector only if the write
  succeeded. On failure nothing changes and the form says why.
- **FR-008**: For a secret in an external store, the database MUST hold only
  the reference and the existing masked hint (last four characters). No
  ciphertext or plaintext of the secret may be written to the data directory.
- **FR-009**: The secret's name in the manager MUST be derived from the prefix
  and the connector's slug, recognisable by a person, and valid under the
  naming rules of the manager it is written to. Re-creating a connector with
  the slug of a recently deleted one MUST succeed.
- **FR-010**: When a connector is deleted, Umbral MUST ask the store that holds
  its secret to delete it. If that fails, the connector MUST still be deleted,
  and the operator MUST be shown the secret's name in the manager to remove by
  hand.
- **FR-011**: A sign-in MUST read the secret from the store named by the
  connector's reference, whichever store is currently configured for writes.
  The local store MUST always be readable. An external store MUST be readable
  whenever its settings are present.
- **FR-012**: Every call to a manager MUST be bounded in time; one that does not
  answer within the bound is a failure.

**Caching and failure**

- **FR-013**: Umbral MUST keep a secret read from an external store in process
  memory only, for a cache lifetime with a documented default (5 minutes) the
  operator can change. Within that lifetime a sign-in MUST NOT contact the
  manager. Such a secret MUST NOT be written to disk, logged, or kept across a
  restart. Replacing or deleting a connector's secret through Umbral MUST
  discard its cached value immediately.
- **FR-014**: When a cached secret has expired and the manager cannot be
  reached, Umbral MUST keep using the expired value for a grace period
  (default 1 hour, operator-settable; 0 disables it) while retrying the
  manager, and MUST log each failed refresh. After the grace period, or with
  it set to 0, the sign-in fails as in FR-015. A manager that answers that the
  secret does not exist or that access is denied is not "unreachable": the
  cached value MUST be discarded and the sign-in fails at once.
- **FR-015**: A sign-in that fails because its secret cannot be read MUST show
  the person the existing sign-in failure page, and MUST record in the security
  log the connector, the store and the cause (unreachable, access denied, not
  found, store not configured), never the secret.

**Moving secrets between stores**

- **FR-016**: Umbral MUST provide an offline command, run in the container like
  the existing credential reset command, that moves every connector's secret
  that is not in the configured store into it. It MUST work in both directions,
  into an external store and back to the local one, and between two external
  stores.
- **FR-017**: For each connector the move MUST write the secret to the target,
  read it back and compare, switch the connector's reference, and only then
  delete the old copy. Failure at any step MUST leave that connector
  referencing its old, still readable copy. Running the command again MUST be
  safe and complete what remains.
- **FR-018**: The move command MUST offer a dry run that lists each connector,
  its current store and its target, and changes nothing.
- **FR-019**: The move command MUST report each connector moved, skipped or
  failed with its cause, and exit non-zero if any failed.

**Visibility and records**

- **FR-020**: Each connector card on the Connectors screen MUST name the store
  holding its secret. The admin area MUST NOT gain a screen, a top-level
  section, or a way to read a secret back (Principles I and IV).
- **FR-021**: A connector whose secret lives in a store the instance cannot
  read MUST be marked on its card, with what to do next.
- **FR-022**: The security log MUST record: a secret written to, replaced in,
  or deleted from an external store (connector, store, outcome); a failed
  write, read or delete with its cause; and each connector moved by the move
  command. No entry may contain a secret or a credential for a manager.

**Documentation (Principle VI)**

- **FR-023**: The README's settings reference MUST list every new setting. Each
  external store MUST have a walkthrough in `docs/` covering how Umbral
  authenticates to it, the minimum permissions to grant (create, read, update
  and delete under Umbral's prefix only), the settings to set, and how to move
  an existing instance in and out.
- **FR-024**: The README's backup section MUST say that with an external store
  the connector secrets are not in the data directory and a restore needs the
  same manager.

**Verification**

- **FR-025**: Each external store MUST be testable without real cloud or
  BellaBaxter credentials, as the constitution requires of connectors.

### Key Entities

- **Secret store**: where a connector's secret lives. One of local, AWS
  Secrets Manager, Google Cloud Secret Manager, Azure Key Vault, BellaBaxter.
  A deployment setting, not a concept the operator manages in the admin area.
- **Secret reference**: what a connector row holds instead of its secret. Names
  the store that wrote it and, for that store only, how to find the secret
  again (for the local store, the ciphertext; for a manager, its identifier).
  Unchanged in form from spec 001; this feature adds the values it can take.
- **Connector** (existing): gains nothing but the store name shown on its card,
  which is derived from its reference.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator who already has a cloud account or BellaBaxter
  organisation can switch a running instance to that manager, move its existing
  secrets, and complete a sign-in, using only the documentation, in under 15
  minutes.
- **SC-002**: After a connector is saved with an external store, a search of
  the whole data directory finds neither the secret nor any ciphertext of it.
- **SC-003**: The move command, interrupted at every step in turn and then
  re-run, leaves 100% of connectors able to sign in, in both directions.
- **SC-004**: A deployment with no secret store setting passes the entire
  existing test suite unchanged and makes no outbound call to any manager.
- **SC-005**: With a warm cache, a sign-in through a connector in an external
  store takes no longer than one through the local store (no call to the
  manager); with a cold cache it adds at most one call to the manager.
- **SC-006**: With an external store configured, Umbral stays under 160 MB
  idle resident memory. The 150 MB budget of Principle II is the default (local
  store) promise and is unchanged.
  **Amended 2026-10-19**: this was "under the 150 MB budget". Measured on
  linux/arm64, an instance with an external store idles ~10.8 MiB above the
  local one (about 148 MiB against 138 MiB, tiered compilation off), because the
  store's startup probe loads the vendor SDK and its HTTP stack. The maintainer
  chose a separate 160 MiB ceiling for that configuration over dropping the SDK
  or the probe: the Raspberry Pi promise is about the default, and an operator
  who runs a cloud secret manager is rarely on a 1 GB board. Tiered compilation
  is also turned off for the whole app (about 6 MiB). The test is
  `IdleMemoryTests`.
- **SC-007**: No log line, security log entry, error page or admin page
  produced during any test of this feature contains a secret value or a
  manager credential.
- **SC-008**: Each of the four external stores has automated tests for write,
  read, replace, delete, unreachable and access denied, runnable with no
  external account.

## Assumptions

- The operator provides the manager itself and the identity Umbral uses there;
  Umbral does not create vaults, projects or key rings, only secrets inside
  them.
- Adopting a secret that already exists in the manager by entering its
  identifier, instead of pasting the value, is out of scope for this feature.
  The operator always pastes the value; Umbral writes it.
- Each manager's own versioning, encryption at rest, audit log and deletion
  recovery window apply as the operator configured them. Umbral does not purge
  a deleted secret early.
- Rotation of a connector secret stays a manual act: the operator creates a new
  secret at the provider and pastes it into the Connectors form. A value changed
  directly in the manager is picked up, but Umbral does not rotate anything.
- BellaBaxter is configured with the same environment variables, read with the
  same precedence, as `BellaBaxter.AspNet.Configuration` in Frunchy, and is
  driven through `BellaBaxter.Client`, the client that package is built on. The
  configuration package itself is not used: its variable mapping and client
  builder are internal to `AddBellaSecrets`, which only reads (plan, research
  R4). The client is a preview package; the plan pins a version.
- The masked hint (last four characters) stays in the database for every store,
  as it does today. It is not considered a secret.
- No release of Umbral has been published, but instances exist that use the
  local store; the move command is how they adopt an external store.
