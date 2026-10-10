# Feature Specification: Generic OpenID Connect Connector

**Feature Branch**: `002-generic-oidc-connector`

**Created**: 2026-10-10

**Status**: Draft

**Input**: User description: "Generic OpenID Connect connector. Today Umbral's upstream connectors are Discord, GitHub and Google (plus a dev-only Fake). Operators who already run an identity provider — Authentik, Keycloak, Pocket ID, Authelia, Zitadel, Kanidm, GitLab, Microsoft Entra — want people to sign in to Umbral apps through it. The operator adds such a provider by its issuer URL and a slug they choose (the slug is in the callback URL and immutable), plus client ID and client secret; everything else about the provider comes from its published discovery document, so nothing else is operator-editable. Several such providers may be configured at once, each enabled/disabled/deleted and its secret replaced like the built-in connectors, from the admin UI and from the YAML configuration file (config parity, export round-trip). A person signing in through one is identified by the provider's issuer plus its subject, so two providers that issue the same subject never merge into one person, and deleting and re-adding the same provider keeps everyone's identity; the issuer therefore cannot be changed after creation. A verified email from such a provider links to an existing user under the same rule as today (never on an unverified email); the operator is warned that adding a provider means trusting its verified emails. An unreachable provider must not stop Umbral from starting; a misconfigured issuer is reported when the operator saves it, with the next action. Ships with a setup walkthrough. Out of scope: SAML, LDAP, editable endpoints, claim mapping, per-provider authorization rules."

## Context

Umbral's first release (spec 001) ships three upstream providers whose details
are compiled in. That covers the person with a Discord account, but not the
operator who already runs an identity provider for their household or team and
wants their hobby apps to use it. For that operator the choice today is to wire
each app to their identity provider by hand, which is the exact chore Umbral
exists to remove.

Every identity provider in that list speaks the same standard and publishes a
machine-readable description of itself at a well-known address under its
issuer URL. So one connector kind, configured by that URL, covers all of them
without Umbral learning any provider by name.

This is still a **Connector** — the second of Umbral's three concepts — not a
new concept. An operator who understands connectors already understands this.

## Clarifications

### Session 2026-10-10

- Q: Should Umbral accept an identity provider reached over plain http (e.g. on a LAN)? → A: No. https only; `localhost` only in local-trial mode. The refusal tells the operator to put the provider behind https, and the walkthrough shows how with the same reverse proxy that fronts Umbral.
- Q: When the admin UI cannot reach the provider at all while saving, refuse or allow? → A: Save it **disabled**, with a warning naming the reason and telling the operator to enable it once the provider is up. Enabling re-runs the check. A reachable provider that answers wrongly is still refused.
- Q: Is the client secret required when adding an OpenID Connect provider? → A: Yes. Umbral is always a confidential client at the provider; a public (PKCE-only) registration is not supported, and the walkthrough says to choose "confidential" in the provider's console.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign in through my own identity provider (Priority: P1)

An operator runs Authentik at home. They create an application for Umbral in
Authentik, then open Umbral's Connectors screen, choose to add an OpenID
Connect provider, type a display name ("Home"), a short name for the callback
address ("home"), Authentik's issuer URL, and the client ID and secret
Authentik gave them. Umbral checks the issuer on the spot, shows the callback
address to paste back into Authentik, and the next person who opens an app's
sign-in page sees "Sign in with Home" and gets through.

**Why this priority**: This is the feature. Without it nothing else in this spec
has a reason to exist, and with only this story an operator already replaces
per-app wiring to their own identity provider.

**Independent Test**: Against a standards-compliant test identity provider,
add it through the admin UI only, register an app, and complete a sign-in end
to end; the app receives an identity from Umbral, not from the identity
provider.

**Acceptance Scenarios**:

1. **Given** an authenticated operator on the Connectors screen, **When** they
   add an OpenID Connect provider with a display name, a short name, an issuer
   URL, a client ID and a client secret, and the issuer is reachable and
   describes itself correctly, **Then** the provider is saved and enabled, and
   the screen shows the exact callback address to register with the provider.
2. **Given** an enabled OpenID Connect provider, **When** an end user starts a
   sign-in at any registered app, **Then** the provider is offered on Umbral's
   sign-in page under its display name, and choosing it sends them to the
   provider and back to the app signed in.
3. **Given** a person who signed in once through that provider, **When** they
   sign in again, **Then** they are the same Umbral user with the same stable
   identifier, and no duplicate is created.
4. **Given** the operator mistypes the issuer URL into a non-https address,
   or the provider at that address describes itself under a different issuer
   or lacks a required capability, **When** they save, **Then** nothing is
   saved, the form keeps what they typed, and the message names what is wrong
   and what to check next.
5. **Given** the provider cannot be reached at all when the operator saves,
   **When** they save, **Then** the connector is saved **disabled**, it does
   not appear on the sign-in page, and the screen says why and to enable it
   once the provider is up; **When** they later enable it, **Then** the same
   check runs again and enabling succeeds only if it passes.
6. **Given** an identity provider returns an identity that does not prove it
   came from that provider for this sign-in — wrong issuer, wrong audience,
   expired, not signed by the provider's published keys, or not tied to this
   sign-in attempt — **When** the callback arrives, **Then** the sign-in is
   refused, the end user sees the same plain failure notice as for any other
   connector, and the cause is recorded in the security log.

---

### User Story 2 - Several providers, kept apart (Priority: P2)

An operator runs Pocket ID for family and Keycloak for a small club. Both are
added side by side. A family member and a club member who happen to receive
the same subject identifier from their respective providers remain two
different people in Umbral. Later the operator deletes the Keycloak connector
by mistake and adds it again with the same issuer; club members sign in as the
same people they were before.

**Why this priority**: Correct identity is a security property, not a
convenience, but a single provider (Story 1) is already valuable on its own.

**Independent Test**: Configure two test providers that issue an identical
subject value; sign in through each and confirm two distinct users. Delete one
connector, re-add it with the same issuer, sign in again and confirm the same
user identifier as before.

**Acceptance Scenarios**:

1. **Given** two OpenID Connect providers with different issuers, **When** each
   asserts the same subject value for a person, **Then** Umbral holds two
   distinct users.
2. **Given** an OpenID Connect connector that people have signed in through,
   **When** the operator deletes it and adds a new one with the same issuer
   (under any short name), **Then** each returning person is recognised as the
   same Umbral user.
3. **Given** an existing OpenID Connect connector, **When** the operator looks
   for a way to change its issuer or its short name, **Then** none is offered,
   and the screen says that changing either means deleting and adding the
   provider again.
4. **Given** several OpenID Connect providers, **When** the operator enables,
   disables, deletes one or replaces its secret, **Then** only that provider is
   affected, the change takes effect on the next sign-in with no restart, and
   the same safeguards apply as for the built-in connectors (warning before
   leaving no enabled connector, secret never shown again after saving).

---

### User Story 3 - The same provider, from the configuration file (Priority: P2)

An operator who keeps Umbral's configuration in git declares their identity
provider in the configuration file — kind, short name, issuer, display name,
client ID, and a reference to the secret held in the environment — and starts
the container. The provider is there, as if added from the screen. Exporting
the configuration produces the same entry back.

**Why this priority**: Configuration parity is a constitutional rule
(Principle V); a provider that can only be added by clicking is incomplete.
It is P2 only because Story 1 must work first.

**Independent Test**: Start a clean instance from a file declaring one OpenID
Connect provider with its secret supplied through the environment; sign in
through it; export the configuration and confirm the entry round-trips.

**Acceptance Scenarios**:

1. **Given** a configuration file declaring an OpenID Connect provider, **When**
   Umbral starts, **Then** the provider exists with those values, and applying
   the same file again changes nothing.
2. **Given** a declared provider whose identity provider is down or
   unreachable at that moment, **When** Umbral starts, **Then** it starts
   normally; sign-ins through that provider fail with the usual notice until it
   is reachable again, and every other connector keeps working.
3. **Given** a file entry with a missing or malformed issuer, a short name that
   breaks the rules, or a secret written in clear text, **When** Umbral starts,
   **Then** it refuses to start, names the entry and the fix, and applies
   nothing from the file.
4. **Given** OpenID Connect providers added from the screen, **When** the
   operator exports the configuration, **Then** each appears in the file format
   with its secret as an environment reference, and importing that export into
   a clean instance reproduces them.

---

### User Story 4 - Linking by email, with the operator warned (Priority: P3)

A person signed in with GitHub last month. Today they sign in through the
operator's identity provider, which asserts the same email address and marks it
verified. Umbral recognises them as the same person, exactly as it would for
two built-in providers. When the operator added the identity provider, the
screen told them that a verified email from it would be trusted this way.

**Why this priority**: The linking rule already exists (spec 001, FR-012) and
must simply hold for the new kind; the new part is the warning.

**Independent Test**: Create a user through one connector with a verified
email; sign in through an OpenID Connect provider asserting the same verified
email and confirm one user with two linked accounts; repeat with the email
marked unverified, or with the verification flag present but not a true
yes/no value, and confirm a new, separate user.

**Acceptance Scenarios**:

1. **Given** an existing user with a verified email, **When** a person signs in
   through an OpenID Connect provider that asserts that same email as verified,
   **Then** the two are linked to one user.
2. **Given** the same situation but the provider does not assert the email as
   verified — including when it sends the verification flag in a form that is
   not an unambiguous "true" — **When** the person signs in, **Then** no link is
   made and a separate user is created.
3. **Given** the operator is adding an OpenID Connect provider, **When** the
   form is shown, **Then** it states that verified emails from this provider
   will be trusted to link accounts, and that a provider letting people claim
   addresses they do not own should not be added.

### Edge Cases

- **Issuer with or without a trailing slash.** The operator types it either
  way; the provider's self-description uses one form. Umbral treats the two as
  the same issuer and never as a mismatch.
- **Provider served over plain http.** Refused, with the reason and the fix
  (put the provider behind https), except for `localhost` while Umbral itself
  runs in its explicitly enabled local-trial mode.
- **Provider that does not support the protections Umbral uses** (for example
  it advertises no supported way to prove the sign-in started at Umbral, or no
  way to receive the client secret Umbral can send). Refused at save time with
  a message naming the missing capability.
- **Provider rotates its signing keys.** Sign-ins keep working without operator
  action.
- **Provider unreachable when the operator saves it.** Saved disabled with a
  warning (FR-005a); enabling it re-checks.
- **Provider unreachable during a sign-in.** The end user sees the plain
  failure notice; the security log records that the provider could not be
  reached. Other connectors are unaffected.
- **Short name collides** with a built-in provider's name, with the reserved
  development provider, or with an existing connector. Refused at save time.
- **Short name with characters that cannot appear in a URL path segment as
  typed** (uppercase, spaces, slashes). Refused at save time with the allowed
  form stated.
- **Provider returns no email**, or no display name. The sign-in still
  succeeds; the user gets the best available name, as for the built-in kinds.
- **Provider identity signed with a shared secret rather than published keys.**
  Refused, and the setup walkthrough says how to switch the provider to
  published-key signing.
- **The configuration file declares a provider and the operator also edits it
  on the screen.** The file wins at the next start for that entry, exactly as
  for built-in connectors today.

## Requirements *(mandatory)*

### Functional Requirements

#### Adding and managing a provider

- **FR-001**: The operator MUST be able to add an OpenID Connect provider by
  supplying a display name, a short name, an issuer URL, a client ID, and a
  client secret, and optionally extra permissions (scopes) to request. The
  client secret is required: Umbral is always a confidential client at the
  provider, and a save without one is refused with that reason.
- **FR-002**: Everything else Umbral needs about the provider MUST come from
  the provider's own published self-description under its issuer URL. No other
  provider detail MUST be operator-editable.
- **FR-003**: The short name MUST be lowercase letters, digits and hyphens,
  between 2 and 32 characters, MUST NOT equal a built-in provider's name or the
  development provider's, and MUST be unique among connectors.
- **FR-004**: The short name and the issuer MUST be immutable after creation.
  The admin UI MUST say so and that changing either means deleting and adding
  the provider again.
- **FR-005**: When the operator saves a new provider from the admin UI, Umbral
  MUST fetch and check the provider's self-description before saving, and MUST
  refuse to save, keeping the operator's input, with a message naming the
  problem and the next action, when: the issuer URL is not an absolute https
  address (except `localhost` in local-trial mode); the self-description names
  a different issuer; or it lacks a capability Umbral requires.
- **FR-005a**: When the self-description cannot be fetched at all (the
  provider is unreachable, times out, or answers with something that is not a
  self-description), Umbral MUST save the connector **disabled** and show a
  warning naming the reason and telling the operator to enable it once the
  provider is reachable. Enabling an OpenID Connect connector from the admin UI
  MUST run the FR-005 check again and MUST refuse to enable, with the reason,
  if it fails or the provider is still unreachable.
- **FR-006**: After saving, the admin UI MUST display the exact callback
  address to register with the provider, read-only and copyable, as it does for
  built-in connectors.
- **FR-007**: Several OpenID Connect providers MUST be configurable at the same
  time. Each MUST support enable, disable, delete and secret replacement with
  the same behaviour and safeguards as built-in connectors (spec 001 FR-010,
  FR-023, FR-033), taking effect on the next sign-in with no restart.
- **FR-008**: The provider's client secret MUST be stored and protected exactly
  as built-in connector secrets are, and MUST NOT be readable after saving.
- **FR-009**: The add form MUST state that verified emails from this provider
  will be trusted to link accounts (FR-016), and that a provider letting people
  claim email addresses they do not control should not be added.

#### Signing in

- **FR-010**: An enabled OpenID Connect provider MUST appear on the end-user
  sign-in page under its display name, alongside the built-in connectors.
- **FR-011**: The sign-in MUST use the authorization-code flow with the same
  single-use, tamper-evident round-trip protections Umbral applies to every
  connector, including proof that the returning sign-in is the one Umbral
  started, wherever the provider supports it.
- **FR-012**: Umbral MUST accept an identity from the provider only when it
  verifiably comes from that provider's configured issuer, is addressed to this
  connector's client ID, is unexpired, is signed with a key the provider
  publishes, and is bound to the current sign-in attempt. An identity signed
  with a shared secret, or unsigned, MUST be refused.
- **FR-013**: Umbral MUST keep working when the provider rotates its published
  signing keys, without operator action.
- **FR-014**: When the provider's identity lacks an email or a display name and
  the provider offers a profile lookup, Umbral MUST use that lookup, and MUST
  refuse the sign-in if the lookup describes a different subject.
- **FR-015**: A sign-in failure through an OpenID Connect provider MUST show the
  end user the same non-technical notice as any connector failure (spec 001
  FR-037) and MUST record the cause in the security log without any token,
  code or secret.

#### Identity and linking

- **FR-016**: A person MUST be identified by the pair (provider issuer, subject
  the provider asserts). Two providers with different issuers MUST never yield
  the same linked account, even for an identical subject value.
- **FR-017**: Deleting an OpenID Connect connector and adding a new one with the
  same issuer, under any short name, MUST map returning people to their
  existing Umbral users.
- **FR-018**: Email linking MUST follow spec 001 FR-012 unchanged: an email
  links to an existing user only when the provider explicitly asserts it as
  verified with an unambiguous true value; any other form counts as unverified.

#### Configuration parity and operations

- **FR-019**: OpenID Connect providers MUST be declarable in the configuration
  file with kind, short name, issuer, display name, client ID, secret as an
  environment reference, enabled state and extra scopes, and applying the file
  MUST be idempotent (spec 001 FR-027, FR-028).
- **FR-020**: Validation of the configuration file MUST cover the new fields
  (issuer required for this kind and forbidden for others; short-name rules;
  absolute https issuer) and MUST fail startup naming the entry, applying
  nothing (spec 001 FR-030).
- **FR-021**: Starting Umbral MUST NOT depend on any OpenID Connect provider
  being reachable.
- **FR-022**: Export MUST emit OpenID Connect providers in exactly the accepted
  format, so export and import round-trip (spec 001 FR-029).
- **FR-023**: Every create, update, enable, disable and delete of an OpenID
  Connect connector, and every sign-in success or failure through one, MUST be
  recorded in the security log like the built-in connectors (spec 001 FR-035).

#### Documentation

- **FR-024**: Umbral MUST ship a setup walkthrough for OpenID Connect providers
  covering: where to find the issuer URL, with concrete pointers for at least
  Authentik, Keycloak, Pocket ID and Authelia; the callback address to
  register; the client type and secret-sending method to choose on the
  provider side; the scopes requested; the email-trust warning; how to put a
  provider that is only reachable over plain http behind https with the same
  reverse proxy that fronts Umbral; and the failures an operator is likely to
  hit, each with the fix.
- **FR-025**: The README's provider list and walkthrough links MUST include
  OpenID Connect providers.

### Key Entities

- **Connector** *(extended)*: gains a fourth kind, "OpenID Connect provider",
  and for that kind an immutable **issuer**. Its short name is chosen by the
  operator instead of being fixed to the kind. All existing connector
  behaviour — display name, client ID, protected secret, extra scopes, enabled
  state — applies unchanged.
- **Linked account** *(rule extended)*: for an OpenID Connect provider, the
  provider identity is the pair (issuer, subject), so it is independent of the
  connector's short name and survives the connector being deleted and re-added.
- **Provider self-description**: what the identity provider publishes about
  itself — its endpoints, supported features and signing keys. Read by Umbral,
  never stored as operator configuration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator who already runs one of Authentik, Keycloak, Pocket ID
  or Authelia can add it and complete a first sign-in through it in under five
  minutes, excluding time spent in the identity provider's own console,
  following only the walkthrough.
- **SC-002**: Sign-ins succeed against at least four different identity
  provider products listed in this spec without any provider-specific setting
  in Umbral.
- **SC-003**: 100% of the forged or mismatched identities listed in User Story 1,
  scenario 5 are refused in automated tests, and none produces a session.
- **SC-004**: An operator who mistypes an issuer learns what is wrong at save
  time in 100% of the cases listed in FR-005, and a provider that was
  unreachable at save time is never offered to end users until a check has
  passed (FR-005a); none of these surfaces first as a failed end-user sign-in.
- **SC-005**: Two providers issuing the same subject value never produce a
  single user, and re-adding a deleted provider with the same issuer reproduces
  100% of its people's previous user identifiers.
- **SC-006**: Umbral starts and serves every other connector normally when a
  configured OpenID Connect provider is unreachable.
- **SC-007**: A clean instance started from an exported configuration plus its
  secrets reproduces every OpenID Connect provider with no manual step.

## Assumptions

- Target providers implement OpenID Connect discovery and the
  authorization-code flow, and can sign identity tokens with published
  asymmetric keys. Every product named in the description can.
- The provider registers Umbral as a confidential client with a client secret
  (settled in Clarifications). Public, PKCE-only registrations are not
  supported, and neither are other client-authentication methods (private-key
  assertions, mutual TLS) in this release.
- Identity providers in a homelab are reachable from the Umbral container over
  https. A provider reachable only over plain http on a LAN is refused, for the
  same reason plain-http public origins are (spec 001 FR-031); running it
  behind the same reverse proxy that fronts Umbral is the documented answer.
- Operators add only identity providers they trust. Umbral cannot detect a
  provider that marks unproven emails as verified; the warning in FR-009 and
  the walkthrough are the mitigation, and no per-provider trust switch is added
  (constitution Principle VII).
- Display-name and avatar handling follow the built-in connectors: the best
  available name, a placeholder when there is none.
- Out of scope, as stated in the input: SAML, LDAP, editable endpoints, claim
  mapping, per-provider authorization rules. Also out of scope: provider-
  initiated sign-in, front- or back-channel logout propagation to the provider,
  and identity tokens encrypted to Umbral.
- The sign-in round trip, session, token issuance and admin protections from
  spec 001 are reused unchanged.
