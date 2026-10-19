# Feature Specification: Umbral MVP — Self-Hosted Social Login Provider

**Feature Branch**: `001-umbral-mvp`

**Created**: 2026-10-09

**Status**: Draft

**Input**: User description: "Umbral MVP: a self-hosted OpenID Connect provider that does social login only. An operator runs one container, opens the admin UI, pastes Discord/GitHub/Google OAuth credentials into a Connectors screen, registers their apps on an Apps screen, and their apps get 'Sign in with Discord/GitHub/Google' via standard OIDC authorization-code + PKCE. Three admin screens: Apps, Connectors, Users. Integrating apps need exactly three config values: issuer URL, client ID, client secret. All state in SQLite in one data directory. Whole configuration also expressible as one YAML file that round-trips with the admin UI. Target: five minutes from docker run to first successful login."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - First login in five minutes (Priority: P1)

A homelabber has three hobby apps and is tired of wiring Discord OAuth into each
one. They run the Umbral container, open the admin UI, create a Discord
connector by pasting a client ID and secret from Discord's developer portal,
register their first app, copy three values into that app's config, and watch a
real person sign in with Discord.

This is the whole product in one vertical slice. If only this story ships,
Umbral already replaces per-app OAuth wiring for one provider and one app.

**Why this priority**: It is the product promise and the adoption moment. Every
other story is an improvement on a system that must first work end to end. It is
also the story that proves the hard parts — upstream OAuth, OIDC issuance,
persistence, and the admin UI — are all connected.

**Independent Test**: Start a clean container with an empty data directory,
complete the entire flow through the admin UI only, and confirm an end user
reaches the integrating app signed in, with a stable identity. Time the run; it
must fit in five minutes excluding time spent inside Discord's developer portal.

**Acceptance Scenarios**:

1. **Given** a clean container started with an empty data directory, **When**
   the operator opens the admin UI for the first time, **Then** they are guided
   to establish admin access before anything else is reachable, and no
   administrative screen is usable until they have.
2. **Given** an authenticated operator on the Connectors screen, **When** they
   select Discord and submit a client ID and client secret, **Then** the
   connector is saved and enabled, the screen displays the exact redirect URI
   to register in Discord's developer portal, and no restart is required for the
   connector to work.
3. **Given** an enabled Discord connector, **When** the operator registers an
   app with a name and one redirect URI, **Then** Umbral displays the issuer
   URL, a client ID, and a client secret, and states plainly that the secret is
   shown once and cannot be retrieved later.
4. **Given** a registered app configured with those three values, **When** an
   end user visits the app and chooses to sign in, **Then** they are sent to
   Umbral, offered "Sign in with Discord", redirected to Discord, and returned
   to the app signed in.
5. **Given** that end user has signed in once, **When** they sign in to the
   same app again later, **Then** they are recognised as the same person with
   the same stable identifier, with no duplicate user created.
6. **Given** the operator has not yet created any connector, **When** they open
   the Apps screen, **Then** Umbral tells them a connector is required first and
   links them to the Connectors screen.

---

### User Story 2 - Drop-in OIDC for any app (Priority: P1)

A developer adds Umbral to a new app. They do not read Umbral's documentation
for an Umbral-specific library, because there isn't one. They point whatever
standard OIDC client their framework already ships with at the issuer URL and it
works.

**Why this priority**: Equal to P1 because "three config values, any client
library" is the differentiator against per-app OAuth wiring. If an integrator
needs Umbral-specific code, the product has failed its own premise even if
logins succeed.

**Independent Test**: Configure an off-the-shelf OIDC client library — one not
written for Umbral — with only the issuer URL, client ID, and client secret.
Complete a login. Validate the returned identity token's signature against the
published key set with no manual key handling.

**Acceptance Scenarios**:

1. **Given** a running Umbral, **When** a client fetches the OpenID Connect
   discovery document from the issuer URL, **Then** it receives a valid document
   advertising the authorization, token, user info, and key set endpoints and
   the supported scopes and response types.
2. **Given** a registered app, **When** it performs an authorization code flow
   with PKCE, **Then** Umbral issues an identity token and an access token, and
   the identity token validates against the published key set.
3. **Given** an identity token issued by Umbral, **When** the app inspects it,
   **Then** it contains a stable subject identifier, the issuer, the audience of
   that app, and the profile claims the app requested through standard scopes.
4. **Given** an access token issued by Umbral, **When** the app calls the user
   info endpoint with it, **Then** it receives the same identity consistently
   with the identity token.
5. **Given** an authorization request whose redirect URI does not exactly match
   one registered for that app, **When** Umbral processes it, **Then** the
   request is rejected and the user is not redirected to the unregistered
   destination.
6. **Given** an authorization request that omits PKCE, **When** Umbral processes
   it, **Then** the request is rejected with a standard protocol error.

---

### User Story 3 - Change a connector without downtime (Priority: P2)

A provider's credentials leak, or the operator wants to add a second provider
six months after setup. They change it in the admin UI and the change takes
effect immediately — no file editing, no container restart, no interruption to
people currently signed in.

**Why this priority**: It is the difference between a configuration product and
a deployment product. Needing a restart to rotate a secret is the exact friction
Umbral exists to remove. It is P2 only because a first deployment can succeed
without it.

**Independent Test**: With a running instance and an active login flow working,
disable a connector and confirm it disappears from the login screen within one
request; replace its secret and confirm the next login uses the new credential,
all without restarting the process.

**Acceptance Scenarios**:

1. **Given** an enabled connector, **When** the operator disables it, **Then**
   it no longer appears as a sign-in option on the next login page load, and an
   in-flight attempt against it fails cleanly with an explanatory message.
2. **Given** an enabled connector, **When** the operator replaces its client
   secret, **Then** the next sign-in uses the new secret without a restart.
3. **Given** a saved connector, **When** the operator returns to the Connectors
   screen, **Then** the client secret is not displayed or retrievable in any
   form; the only offered action is replacement.
4. **Given** a connector configured with credentials the provider rejects,
   **When** an end user attempts to sign in with it, **Then** they see a message
   telling them to contact the operator, the operator sees the underlying cause
   recorded, and no technical detail about the failure leaks to the end user.
5. **Given** several enabled connectors, **When** an end user reaches the login
   page, **Then** all enabled connectors are offered and disabled ones are not.

---

### User Story 4 - Know and control who has access (Priority: P2)

Someone signed up who shouldn't have. The operator opens the Users screen, finds
them, and cuts off their access everywhere at once.

**Why this priority**: The reason to self-host is control. Without this the
operator has a login system they cannot govern. P2 because a first deployment
demonstrably works without it, but it is the first thing asked for afterwards.

**Independent Test**: Sign in as two different end users, then from the admin UI
disable one and confirm they can no longer obtain a session at any app while the
other is unaffected.

**Acceptance Scenarios**:

1. **Given** users who have signed in, **When** the operator opens the Users
   screen, **Then** they see each user's display name, the providers that user
   has signed in with, when they first and last signed in, and can search the
   list.
2. **Given** an active user, **When** the operator disables them, **Then** that
   user cannot complete a new sign-in at any app, and their existing sessions
   and tokens stop being honoured.
3. **Given** a disabled user, **When** the operator re-enables them, **Then**
   they can sign in again and are recognised as the same person they were
   before.
4. **Given** a user, **When** the operator deletes them, **Then** their account
   and linked provider accounts are removed, and a later sign-in with the same
   provider account creates a new, distinct user.
5. **Given** any user, **When** the operator views them, **Then** no upstream
   provider token or credential belonging to that user is displayed.

---

### User Story 5 - Reproduce the whole thing from a file (Priority: P3)

**Superseded by [004](../004-remove-config-document/spec.md).**

The operator keeps their homelab in a git repository. They want Umbral's
configuration in there too — and they want to rebuild the instance from that
file on new hardware.

**Why this priority**: It serves the audience's existing habits and is a strong
adoption signal, but a working instance does not depend on it. P3.

**Independent Test**: Export the configuration of a fully configured instance,
start a second clean instance, apply the exported file with secrets supplied
from the environment, and confirm apps and connectors are identical and logins
work against the second instance.

**Acceptance Scenarios**:

1. **Given** a configured instance, **When** the operator exports its
   configuration, **Then** they receive a single human-readable file describing
   every app and connector, with no secret written in clear text and with
   secrets expressed as references to environment variables.
2. **Given** such a file and the referenced environment variables, **When** a
   clean instance starts with it, **Then** it comes up with the same apps and
   connectors, and logins succeed with no admin UI interaction.
3. **Given** an instance already configured from that file, **When** the same
   file is applied again, **Then** nothing changes and no duplicate app or
   connector is created.
4. **Given** a malformed or internally inconsistent file, **When** an instance
   starts with it, **Then** startup fails with a message naming the offending
   entry and the problem, and no partial configuration is applied.
5. **Given** a change made in the admin UI after a file was applied, **When**
   the operator exports again, **Then** the change is present in the exported
   file.

---

### Edge Cases

- **A provider returns no email address.** GitHub users can keep their email
  private; Discord accounts can be unverified. Umbral must still create a usable
  user, and apps must not break on a missing email claim.
- **The same person signs in with two different providers.** They used Google in
  March and GitHub in June, with the same email address. Per FR-012 this is one
  user when both providers marked that email verified, and two users otherwise —
  so the same pair of sign-ins can legitimately produce either outcome depending
  on what the providers asserted.
- **Two existing users already share a verified email.** It can happen: two
  accounts were created while one provider withheld the email, and a later change
  makes both verified. FR-012 requires a match against exactly one user, so this
  creates a third user rather than guessing. The operator can delete the strays.
- **A provider account's email changes to one already used by another Umbral
  user.** Umbral must not silently hand one person's account to another.
- **The container restarts mid-session.** People signed in before the restart
  must stay signed in, and tokens issued before it must still validate.
- **Umbral sits behind a reverse proxy.** The public issuer URL differs from the
  address the process binds to. Every URL Umbral publishes or compares — issuer,
  redirect URIs, provider callbacks — must use the public origin, or the
  operator gets a mismatch error they cannot diagnose.
- **The operator loses an app's client secret.** It is unrecoverable by design,
  so there must be an obvious path to issue a new one without re-registering the
  app.
- **An app is deleted while its users hold valid tokens.** Those tokens must
  stop being honoured.
- **A provider is unreachable or rate-limiting.** The end user sees a plain
  message, not a stack trace, and can retry or pick another provider.
- **Two sign-ins race for the same new user.** Only one user account is created.
- **The data directory is read-only or out of space.** Umbral fails loudly at
  startup rather than running and losing writes.
- **An authorization code is replayed.** The second use is rejected and the
  event is recorded.
- **The last enabled connector is disabled.** Nobody can sign in anywhere; the
  operator must be warned before it takes effect, not after.

## Requirements *(mandatory)*

### Functional Requirements

#### Identity provision

- **FR-001**: Umbral MUST act as an OpenID Connect provider, publishing a
  discovery document and a public key set, and MUST support the authorization
  code flow with PKCE as its only authorization flow.
- **FR-002**: Umbral MUST reject authorization requests that do not use PKCE,
  and MUST reject implicit and resource-owner-password requests.
- **FR-003**: Umbral MUST match an app's redirect URI exactly against a
  registered value, with no wildcard or prefix matching.
- **FR-004**: Umbral MUST issue identity tokens carrying a subject identifier
  that is stable for the lifetime of that user, unique per Umbral instance, and
  not derived from any upstream provider's identifier in a way that exposes it.
- **FR-005**: Umbral MUST expose a user info endpoint returning the same
  identity as the identity token for a given access token.
- **FR-006**: Umbral MUST NOT require an integrating app to supply, or know,
  anything beyond the issuer URL, a client ID, and a client secret.
- **FR-007**: Umbral MUST continue to honour tokens and sessions issued before a
  process restart.
- **FR-008**: Umbral MUST reject a reused authorization code and record the
  attempt.

#### Social connectors

- **FR-009**: Umbral MUST ship working connectors for Discord, GitHub, and
  Google, each requiring only a client ID and client secret from the operator.
- **FR-010**: Umbral MUST allow a connector to be created, enabled, disabled,
  and have its credentials replaced at runtime, with the change taking effect on
  the next sign-in and without restarting the process.
- **FR-011**: Umbral MUST capture, for each successful upstream sign-in, the
  provider's stable identifier for that account, and whatever display name,
  email address, and avatar the provider supplies, tolerating the absence of
  any of them except the stable identifier.
- **FR-012**: When a person signs in with a provider account not seen before,
  Umbral MUST link it to an existing user when, and only when, the provider
  asserts an email address that is marked verified by that provider and that
  matches the verified email of exactly one existing user. In every other case —
  no email, an unverified email, or an email matching no user or more than one —
  Umbral MUST create a new user. Umbral MUST NOT link on an unverified email
  under any circumstance.
- **FR-012a**: A user MUST be able to hold several linked provider accounts, and
  the Users screen MUST show which providers a user has linked.
- **FR-012b**: Deleting a user MUST delete all of its linked provider accounts.
  Umbral MUST NOT offer unlinking of an individual provider account in this
  feature.
- **FR-013**: Umbral MUST display, on the Connectors screen, the exact redirect
  URI the operator has to register in each provider's developer console, derived
  from the configured public origin.
- **FR-014**: Umbral MUST use a single-use, bound value to protect every
  upstream authorization round trip against cross-site request forgery, and MUST
  reject a callback whose value is missing, unknown, or already used.

#### Administration

- **FR-015**: Umbral MUST present exactly three administrative screens — Apps,
  Connectors, Users — and MUST NOT introduce a user-facing concept beyond App,
  Connector, and User.
- **FR-016**: Umbral MUST require the operator to establish admin access on
  first run before any administrative screen is usable, and MUST NOT ship a
  default or well-known administrative credential.
- **FR-017**: Administrative access MUST be governed by a single operator
  credential established at first run, independent of every connector, so that a
  failed, expired, or misconfigured provider can never lock the operator out of
  the screen needed to fix it. There is exactly one operator credential and no
  operator accounts, roles, or delegation.
- **FR-017a**: The operator credential MUST be settable from the configuration
  file as well as from the first-run screen, held as a verification value from
  which the original cannot be recovered, and MUST be replaceable by an operator
  who is already signed in.
  **Superseded by [004](../004-remove-config-document/spec.md), the configuration-file part.**
- **FR-017b**: Umbral MUST provide an offline way to reset the operator
  credential for an operator with access to the data directory, so that a
  forgotten credential does not require rebuilding the instance.
- **FR-018**: Umbral MUST let the operator register an app with a name and one
  or more exact redirect URIs, and MUST generate its client ID and client
  secret.
- **FR-019**: Umbral MUST display a generated client secret exactly once, MUST
  NOT make it retrievable afterwards, and MUST offer regeneration.
- **FR-020**: Umbral MUST let the operator delete an app, after which tokens
  issued to it stop being honoured.
- **FR-021**: Umbral MUST let the operator list, search, disable, re-enable, and
  delete users, and MUST show for each user their display name, the providers
  they have used, and their first and last sign-in times.
- **FR-022**: A disabled user MUST NOT be able to complete a sign-in at any app,
  and their existing sessions and tokens MUST stop being honoured.
- **FR-023**: Umbral MUST warn the operator before an action that would leave no
  enabled connector, and before deleting an app or user.
- **FR-024**: Every state-changing administrative request MUST be protected
  against cross-site request forgery.
- **FR-025**: Any enabled Umbral user MAY sign in to any registered app. Umbral
  answers only "who is this person"; deciding what that person may do is the
  app's job. Umbral MUST NOT offer a per-app user allowlist or any other
  authorization model. **Amended by [006](../006-allowed-email-domains/spec.md).**
  The operator may limit sign-in to verified email domains, per connector or per
  instance, never per app. That decides who can be a user, not what a user may do.

#### Configuration and deployment

- **FR-026**: Umbral MUST run as a single container with no external database,
  cache, or message broker, and MUST keep all persistent state in one mounted
  data directory.
- **FR-027**: Umbral MUST be configurable from a single declarative file
  covering every app and connector, and applying that file MUST be idempotent.
  **Superseded by [004](../004-remove-config-document/spec.md).**
- **FR-028**: That file MUST NOT require a secret to be written in clear text;
  secrets MUST be expressible as references resolved from the environment.
  **Superseded by [004](../004-remove-config-document/spec.md).**
- **FR-029**: Umbral MUST be able to export its current configuration in exactly
  the format it accepts, so that export and import round-trip.
  **Superseded by [004](../004-remove-config-document/spec.md).**
- **FR-030**: Umbral MUST fail startup, with a message naming the offending
  entry, when the configuration file is malformed or inconsistent, and MUST NOT
  apply it partially.
  **Superseded by [004](../004-remove-config-document/spec.md).**
- **FR-031**: Umbral MUST require the operator to declare its public origin, and
  MUST use that origin for every URL it publishes or compares.
- **FR-032**: Umbral MUST expose a health indicator suitable for a container
  orchestrator to probe.

#### Security and operations

- **FR-033**: Umbral MUST encrypt at rest every upstream provider client secret
  and every app client secret, and MUST NOT make either retrievable through the
  admin interface once saved.
- **FR-034**: Umbral MUST persist its token signing material in the data
  directory, protected at rest, so that it survives restart.
- **FR-035**: Umbral MUST record a structured security event for each sign-in
  success, sign-in failure, admin authentication, credential change, app
  registration change, and user state change.
- **FR-036**: Umbral MUST NOT expose an upstream provider's tokens or
  credentials to an integrating app or to any administrative screen.
- **FR-037**: Error messages shown to an end user MUST NOT disclose
  configuration detail, and MUST tell the person what to do next.
- **FR-038**: Umbral MUST limit the rate of repeated failed sign-in and admin
  authentication attempts from a single source.

#### Documentation

- **FR-039**: Umbral MUST ship a provider setup walkthrough for each of the
  three connectors, covering where to create the upstream OAuth application,
  which redirect URI to register, and which scopes to request.
- **FR-040**: Umbral MUST ship a getting-started document that takes a reader
  from nothing to a working login without leaving the page.

### Key Entities

- **App**: A piece of software that delegates sign-in to Umbral. Has a name, a
  client ID, a stored secret, and one or more exact redirect URIs. Operators
  create and delete apps; nothing else refers to an app except the tokens issued
  to it.
- **Connector**: A configured upstream social provider. Has a provider kind
  (Discord, GitHub, Google), a client ID, a stored secret, and an enabled flag.
  Operators create, enable, disable, and re-credential connectors.
- **User**: A person who has signed in at least once. Has a stable subject
  identifier, a display name, an optional email address and avatar, an enabled
  flag, and first and last sign-in times. Users are never created by an
  operator, only by signing in.
- **Linked account**: The association between a User and one upstream provider
  account, holding the provider kind, that provider's stable identifier, and the
  email the provider asserted together with whether the provider marked it
  verified. A User may hold several. This is a detail shown inside a User, not a
  fourth top-level concept.
- **Operator credential**: The single secret that grants access to the three
  admin screens, held as a non-recoverable verification value.
- **Security event**: A timestamped record of an authentication or
  administrative action, with its outcome and enough context to investigate.
- **Configuration document**: The declarative description of every App and
  Connector, with secrets held by reference.
  **Superseded by [004](../004-remove-config-document/spec.md).**

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator who has never used Umbral goes from starting the
  container to watching a real social login succeed in under five minutes,
  excluding time spent inside the provider's own developer portal.
- **SC-002**: Integrating an app requires exactly three configuration values and
  no Umbral-specific client code; a standard OIDC client library completes a
  login with no modification.
- **SC-003**: A conformance check of the published discovery document and
  authorization code flow passes against the OpenID Connect specification, with
  identity tokens validating against the published key set.
- **SC-004**: Replacing a connector's credentials or toggling it on or off takes
  effect on the next sign-in with no process restart and no dropped session.
- **SC-005**: A restart of the instance leaves every signed-in person signed in
  and every previously issued token still valid.
- **SC-006**: A clean instance started from an exported configuration file plus
  its referenced environment variables is functionally identical to the instance
  it came from, with no admin UI interaction.
  **Superseded by [004](../004-remove-config-document/spec.md).**
- **SC-007**: The running instance holds under 150 MB resident at idle and serves
  both 64-bit Intel and 64-bit ARM hosts, so that a Raspberry Pi-class machine is
  a supported deployment. *Amended 2026-10-19: with an external secret store the
  ceiling is 160 MB; see [005](../005-external-secret-stores/spec.md) SC-006.
  The default (local store) stays under 150 MB.*
- **SC-008**: No secret — upstream or app — is readable from the admin UI, the
  configuration export, or the logs after it has been saved.
  **Superseded by [004](../004-remove-config-document/spec.md), the configuration export part.**
- **SC-009**: A disabled or deleted user loses access to every app within one
  sign-in attempt and holds no usable token afterwards.
- **SC-010**: Every operator-facing failure message names the thing that is wrong
  and the next action to take; this is checkable by review against the full list
  of error messages.
- **SC-011**: A reader following the getting-started document alone, with no
  other source, reaches a working login.

## Assumptions

### Decisions taken without an explicit answer

Three questions had no safe default and were put to the project owner. Work
proceeded on the answers below so the plan would not stall; each is marked for
confirmation at plan review, and each is cheap to reverse at this stage.

| Requirement | Assumed answer | Why this one | Cost if reversed |
| --- | --- | --- | --- |
| FR-012 | Link on a verified email asserted by both sides | "I clicked GitHub instead of Google and lost everything" is the commonest social-login complaint. Requiring a provider-verified email on both sides closes the account-takeover vector that makes email matching dangerous. | One table and one branch in the sign-in path |
| FR-017 | A single operator credential set at first run | Forced, not chosen: a clean install has no connectors, so "first person to sign in through a connector becomes operator" has nothing to sign in with. Independence from connectors also prevents lock-out when a provider credential expires. | Rewrites the first-run flow and User Story 1 |
| FR-025 | Any enabled user may use any app | Smallest surface, matches "toggle providers once", keeps Umbral out of authorization. Per-app connector restriction is the obvious later addition. | One field on App plus UI |

FR-017 sits awkwardly with the constitution's Principle VII, which excludes
"password-based local accounts". That exclusion is aimed at end users; a single
operator credential is a different thing. The plan must either narrow Principle
VII's wording to say so or record the deviation in Complexity Tracking.

### Context assumptions

- **Scale.** The target deployment serves a single operator's handful of apps
  and tens to low hundreds of users. Nothing here is designed for multi-thousand
  concurrent sign-ins.
- **Trust.** The operator is trusted completely. There is exactly one class of
  administrator and no delegation; this follows from the project's exclusion of
  any authorization model.
- **Transport.** The operator terminates TLS, usually at a reverse proxy in
  front of Umbral. Umbral is not responsible for obtaining certificates, but
  must behave correctly behind that proxy.
- **Providers.** Discord, GitHub, and Google keep their current OAuth 2.0 and
  OpenID Connect behaviour. A provider changing its API is an operational risk
  accepted by the project, not designed around.
- **Provider applications.** The operator registers the upstream OAuth
  application themselves in each provider's developer console. Umbral cannot do
  this for them, and the minutes spent there are excluded from SC-001.
- **Sessions.** An end user signing in to a second app during the same browser
  session is not asked to pick a provider again; Umbral's own session carries
  them through. This is standard single sign-on behaviour and is assumed rather
  than specified separately.
- **Out of scope for this feature**, per the project's principles: SAML, LDAP,
  any role or permission model, multi-factor authentication, local
  password accounts, custom login flow builders, multi-tenancy, and an
  administrative audit trail beyond the structured security log.
- **Branding.** The login page is Umbral's own and is not customisable per app
  in this feature.
