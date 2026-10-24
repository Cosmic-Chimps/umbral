# Feature Specification: The Fediverse connector

**Feature Branch**: `alta/earnest-cetus-9pd3` (worktree branch; spec directory `specs/010-fediverse-connector`)

**Created**: 2026-10-24

**Status**: Draft

**Input**: Task `2026-10-23-fediverse-connector-sign-in-with-a-fediverse-account-f3dv`: "Spec 010: the Fediverse connector — sign in with a Mastodon (fediverse) account". The maintainer chose fediverse sign-in as the next feature on 2026-10-23. How it looks is settled in `design/contracts/fediverse-connector-ui.md`, which is normative for the screens.

## Background

Umbral signs people in without passwords, through a social provider or a passkey
(constitution 3.0.0). Most "social logins" an operator asks for (GitLab,
Keycloak, Authentik, Pocket ID…) already work through the OpenID Connect
connector. The fediverse does not: Mastodon speaks OAuth 2 without OpenID
Connect, and every server is a provider of its own, with its own accounts. No
operator can register Umbral by hand on every server their people might come
from.

This feature adds a built-in connector kind, **Fediverse**, that the operator
turns on once. A person types their handle (`@ana@mastodon.social`) or their
server; Umbral registers itself on that server the first time anyone from it
signs in, and every app behind Umbral receives that person as an ordinary OIDC
user. Frunchy, the maintainer's fediverse app, is the first consumer.

## Decisions Already Taken

Decided in the task before this spec; not open:

- **Model (Principle I).** Fediverse is a Connector kind, built in like Passkey:
  one per Umbral, enabled on the Connectors screen. A server is not a concept the
  operator manages; it is where a person's account lives. No new noun.
- **Flow.** The person gives a handle or a server. Umbral finds the server,
  registers itself there on first use with its callback address and the
  read-only account scope, keeps that registration in the data directory with
  its secret encrypted, then runs the authorization code flow with PKCE, reads
  the person's account, and signs them in. A server without PKCE is refused.
- **Identity.** The person is identified by the account's stable id on its
  server together with the server, never by display name or handle. There is no
  verified email, so a fediverse account never joins another user by email, and
  allowed email domain rules do not apply to it.
- **Which servers.** Mastodon and servers that offer Mastodon's app registration
  and sign-in (GoToSocial, Pleroma, Akkoma). Misskey and Sharkey are out of scope
  for 010 and are refused with a message that names the next action.
- **Security (Principle IV).** Single-use `state`, PKCE, the exact redirect
  address, the server address normalised and checked (no private or loopback
  addresses unless a development setting allows them), a time limit on every
  call to a server, failures in the security log, and the sign-in rate limits.
- **Operator control.** One setting on the connector: **Any server** (default)
  or **Only these servers** (a list). It decides who can be a user, not what
  anyone may do (Principle VII). A refused server gets one neutral message.
- **Screens.** As `design/contracts/fediverse-connector-ui.md` says: a list entry
  and detail pane on the Connectors screen, an ordinary "Sign in with Fediverse"
  button on the sign-in page that leads to an account step page, and the copy of
  its section 3.

## Clarifications

### Session 2026-10-24

The task proposed a default for each question; they are recorded as the answers
(unattended run, decided by the implementing agent per the task's instruction).

- Q: Does the account step take a handle, a server, or both? → A: Both:
  `@ana@mastodon.social`, `ana@mastodon.social`, `mastodon.social` and
  `https://mastodon.social` all work.
- Q: Which profile claims does an app get for a fediverse user? → A: `name` (the
  account's display name, or its username when that is empty),
  `preferred_username` (`user@server`), `picture` (the avatar address) and
  `profile` (the account's page). No `email`.
- Q: What happens when a server deletes Umbral's registration? → A: Umbral
  registers again, once, without the person noticing more than a short wait.
- Q: How long is a server registration kept, and are unused ones cleaned up? →
  A: Kept as long as the connector's data is; no clean-up in 010.
- Q: When a handle's domain is not the server's own address (an account
  `@ana@example.com` hosted on `social.example.com`)? → A: Umbral asks the
  handle's domain where the account lives (the standard account lookup every
  fediverse server answers) and signs in at the server it names.
- Q: What happens to the server registrations when the operator deletes the
  Fediverse connector? → A: They are kept, like the users who signed in through
  it, so turning it on again reuses them.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign in to an app with a fediverse account (Priority: P1)

Ana has an account on mastodon.social. She opens Frunchy, which sends her to
Umbral. The sign-in page offers **Sign in with Fediverse** among the other
buttons. She presses it, types `@ana@mastodon.social` on the next page and
presses Continue. Her own server asks her to authorise Umbral to read her
account; she agrees, and she is back in Frunchy, signed in as Ana, with her
display name and avatar. The next day she signs in the same way and is the same
user.

**Why this priority**: It is the feature. Everything else adjusts it.

**Independent Test**: With the connector on and an app offering it, sign in
through a fake Mastodon-compatible server and complete the app's OIDC flow: the
app gets a user with `name`, `preferred_username`, `picture` and `profile`, no
`email`; a second sign-in gives the same `sub`.

**Acceptance Scenarios**:

1. **Given** the Fediverse connector is enabled and offered to an app, **When**
   a person opens that app's sign-in page, **Then** it shows a "Sign in with
   Fediverse" button in the same style and place as the other connectors.
2. **Given** the person presses it, **When** the account step opens, **Then** it
   shows one field, "Your account or server", a Continue button, and, when the
   app offers other connectors, a link back to them; it works with JavaScript off.
3. **Given** the person enters `@ana@mastodon.social` and no one from that
   server has signed in before, **When** they continue, **Then** Umbral registers
   itself on the server, keeps the registration, and sends the person to the
   server's authorisation page asking only to read their account.
4. **Given** the person authorises Umbral, **When** the server sends them back,
   **Then** Umbral reads their account, creates or finds their user, signs them
   in, and the app receives them through its normal OIDC flow.
5. **Given** someone else from the same server signs in later, **When** they
   continue from the account step, **Then** Umbral reuses the stored registration
   and does not register again.
6. **Given** Ana changes her display name or handle on her server, **When** she
   signs in again, **Then** she is the same Umbral user; her display name follows
   the new value.

---

### User Story 2 - The operator turns the connector on and chooses which servers (Priority: P1)

Jo runs Umbral for a small group. On the Connectors screen Jo selects
**Fediverse**, reads the callback address and the note that fediverse accounts
have no verified email, keeps **Any server** and presses **Enable Fediverse**.
Later Jo limits it to the group's own two servers by choosing **Only these
servers** and typing `social.example.org, fosstodon.org`.

**Why this priority**: Without the operator turning it on, no one can use it;
the server list is the operator's one control.

**Independent Test**: On the Connectors screen, enable the connector with Any
server, then switch to Only these servers with two entries; a sign-in from a
listed server works and one from another server is refused with the neutral
message.

**Acceptance Scenarios**:

1. **Given** the connector is not set up, **When** the operator opens the
   Connectors screen, **Then** the list shows a Fediverse entry after the
   provider entries and before Passkey, marked "Not set up".
2. **Given** the operator opens it, **When** the pane shows, **Then** it has the
   set-up facts (what it is, the callback address with a copy button, the
   no-email note), the settings (shared, the server choice with Any server
   selected, the Servers field) and an Enable Fediverse button.
3. **Given** the operator enables it, **When** the screen comes back, **Then** the
   entry shows its state like any other connector, and the pane offers Disable
   and Delete with the usual warnings when it is the last way into an app.
4. **Given** the operator chooses Only these servers and enters
   `Social.Example.org, fosstodon.org`, **When** they save, **Then** the list is
   stored and shown back lower-case and normalised.
5. **Given** an entry is not a server address, or has a wildcard, **When** the
   operator saves, **Then** nothing is saved and the field shows the operator's
   message naming that entry.
6. **Given** Only these servers is chosen, **When** a person from a server not on
   the list continues on the account step, **Then** Umbral does not contact that
   server and shows "Accounts on {server} cannot sign in here. Use another way to
   sign in."

---

### User Story 3 - Problems the person can fix are explained on the account step (Priority: P2)

Ben types `ben@misskey.io`, then `not a handle`, then a server that is down.
Each time the account step comes back with what he typed, the field marked as
wrong, and one sentence under it that names the server and says what to do.

**Why this priority**: The fediverse is many servers of varying software and
health; without clear messages a person is stuck with a dead end.

**Independent Test**: Post each bad input to the account step and check the
page re-renders with the right catalogue message under the field and the typed
value kept.

**Acceptance Scenarios**:

1. **Given** the input is neither a handle nor a server address, **When** the
   person continues, **Then** the step says "Enter a handle like
   @you@mastodon.social, or a server address."
2. **Given** the server runs Misskey, Sharkey, or any software without
   Mastodon's app registration or without PKCE, **When** the person continues,
   **Then** the step says the server does not offer the sign-in Umbral uses,
   names the software that works, and suggests another account or another way
   to sign in.
3. **Given** the server does not answer in time or answers with an error,
   **When** the person continues, **Then** the step says "{server} did not
   answer. Try again in a moment."
4. **Given** the input names a private, loopback or link-local address, and the
   development setting is off, **When** the person continues, **Then** the step
   shows the "not a handle or a server" message and Umbral sends nothing to that
   address.
5. **Given** the person refuses at their server, or the callback carries a wrong
   or reused state, **When** they come back, **Then** they see the existing
   sign-in error page, as with every provider.

---

### User Story 4 - A server forgets Umbral (Priority: P3)

An admin of a small server cleans up its app list and deletes Umbral's
registration. The next person from that server who signs in sees nothing
unusual: Umbral notices the registration is gone, registers again once, and
carries on.

**Why this priority**: Rare, but without it every person from that server is
locked out until the operator intervenes, and the operator has no screen for
servers.

**Independent Test**: With a stored registration that the fake server no
longer accepts, a sign-in from that server re-registers once and completes.

**Acceptance Scenarios**:

1. **Given** the stored registration is no longer accepted by the server,
   **When** a person from it continues on the account step, **Then** Umbral
   registers again, replaces the stored registration, and the sign-in proceeds.
2. **Given** the fresh registration is refused as well, **When** this happens,
   **Then** Umbral does not loop: the step shows "{server} did not answer. Try
   again in a moment." and the failure is in the security log.

### Edge Cases

- Two people from a new server continue at the same moment: one registration is
  kept and both sign-ins work.
- The input carries a path, a port, upper case, a trailing dot or slash, or an
  internationalised domain: the server address is normalised to one spelling
  before any lookup, the allowlist check and storage.
- The handle's domain redirects or points elsewhere: Umbral follows no HTTP
  redirect to another host; it uses only the account lookup's answer, which is
  itself checked like typed input.
- A server's account answer lacks an id, or the id is empty: the sign-in fails
  with the generic error page and a security log entry; no user is created.
- The connector is disabled between the account step and the callback: the
  callback fails as for any disabled connector.
- The operator switches to Only these servers while a person from an unlisted
  server is at their server's authorisation page: the callback refuses them.
- The operator chooses Only these servers with an empty list: nobody can sign in
  through the connector, and the pane says so.
- An app limited to a chosen set of connectors that does not include Fediverse:
  the button is not on its sign-in page, and `/connect/fediverse/start` reached
  from that app's request does not let the person in to that app (the authorize
  check already in place applies).
- A fediverse user later signs in with Google using some email: they become, or
  link to, whatever user that email decides; the fediverse user is never joined
  by email.

## Requirements *(mandatory)*

### Functional Requirements

**The connector**

- **FR-001**: Umbral MUST offer a built-in connector kind "Fediverse", at most one per instance, with the fixed identifier `fediverse`, which the operator sets up, enables, disables, shares and deletes on the Connectors screen like the Passkey connector.
- **FR-002**: The Fediverse connector MUST carry a server scope: Any server (the default) or Only these servers with a list of server addresses.
- **FR-003**: Saving the server list MUST normalise each entry (lower case, no scheme, path, port of 443, trailing dot or slash; internationalised names in their ASCII form) and MUST refuse, with nothing saved, an entry that is not a server address or contains a wildcard, naming that entry in the message.
- **FR-004**: The Connectors screen MUST show the Fediverse connector as a list entry after the provider entries and before Passkey, with a detail pane holding the set-up facts (lead, callback address with copy, no-email note), the settings (shared, server scope, servers, save) and, when set up, the Enable/Disable and Delete actions with the existing last-connector warnings, as `design/contracts/fediverse-connector-ui.md` describes.

**Starting a sign-in**

- **FR-005**: When the connector is enabled and offered to the app, the sign-in page MUST show an ordinary "Sign in with Fediverse" button, Last used badge included, that leads to the account step.
- **FR-006**: The account step MUST be a page with one field that accepts a handle (`@user@domain` or `user@domain`) or a server (`domain` or `https://domain`), keeps the app's chrome and return address, and works without JavaScript.
- **FR-007**: For a handle, Umbral MUST find the server that hosts the account by asking the handle's domain (the standard account lookup); for a server, it MUST use that server.
- **FR-008**: Before contacting a server, Umbral MUST refuse an address that resolves to a private, loopback, link-local or otherwise non-public network address, unless the instance runs with the development setting that allows it; the refusal shows the "not a handle or a server" message.
- **FR-009**: When the server scope is Only these servers, Umbral MUST refuse a server not on the list before contacting it, with the message "Accounts on {server} cannot sign in here. Use another way to sign in."
- **FR-010**: Umbral MUST refuse a server that does not offer Mastodon's app registration, does not support PKCE with S256, or runs Misskey or Sharkey, with the unsupported-server message naming the server.
- **FR-011**: The first time anyone signs in from a server, Umbral MUST register itself there with its callback address and the `read:accounts` scope only, and MUST store that registration in the data directory with its client secret encrypted at rest.
- **FR-012**: Later sign-ins from the same server MUST reuse the stored registration.
- **FR-013**: When a server no longer accepts the stored registration, Umbral MUST register again once and replace it; a second refusal MUST end the attempt with the "did not answer" message, without further retries.
- **FR-014**: Every call Umbral makes to a server MUST have a time limit and MUST NOT follow redirects; a timeout or error MUST show "{server} did not answer. Try again in a moment."
- **FR-015**: Every problem the person can fix MUST re-render the account step with the typed value kept, the field marked invalid, the message under it naming the server as text, and focus on the field.

**Completing a sign-in**

- **FR-016**: Umbral MUST run the authorization code flow with PKCE (S256) and a single-use `state` bound to the server it started with, and MUST send the exact redirect address it registered.
- **FR-017**: On return, Umbral MUST check the state, exchange the code at the server the sign-in started with, read the person's account, and identify the person by the account's id on that server together with the server.
- **FR-018**: A fediverse identity MUST never be linked to another user by email, and allowed email domain rules MUST NOT apply to it.
- **FR-019**: The callback MUST re-check the server scope and refuse a server no longer allowed, with the existing sign-in error page.
- **FR-020**: A fediverse user's profile MUST be refreshed from the account on every sign-in: display name (the username when the display name is empty), avatar, `user@server` handle and profile address.
- **FR-021**: An app MUST receive a fediverse user through the normal OIDC flow with `name`, `preferred_username` (`user@server`), `picture` and `profile` under the `profile` scope, and no `email`.

**Security and operations**

- **FR-022**: Sign-in failures through the Fediverse connector (refused server, unsupported server, unreachable server, registration failure, state or exchange failure) MUST be recorded in the security log with the server named and no token or secret.
- **FR-023**: A new server registration MUST be recorded in the security log as a credential change.
- **FR-024**: The account step and callback MUST be rate-limited like the other sign-in endpoints, and the account step's form MUST carry antiforgery protection.
- **FR-025**: The documentation MUST include a Fediverse walkthrough (what it is, which servers work and that Misskey and Sharkey do not, how a person signs in, the server list, what a server learns), the README's connector list and "Who can sign in", and a CHANGELOG entry under Unreleased.

### Key Entities

- **Fediverse connector**: the one connector of kind Fediverse. Carries the usual enabled and shared state plus the server scope and server list.
- **Server registration**: what Umbral got from one server when it registered there: the server's normalised address, the client id, the encrypted client secret, the server's authorisation and token addresses, when it was registered and last used. One per server; not shown to the operator in 010.
- **Fediverse identity**: a link from a User to an account, keyed by the server and the account's id on it. Carries no email.
- **User profile additions**: the `user@server` handle and the profile address, for users who signed in through the fediverse.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A person with an account on a supported server goes from pressing "Sign in with Fediverse" to signed in to the app in two Umbral screens (the account step, then back to the app), plus their server's own authorisation page.
- **SC-002**: The operator enables the connector in one save from the Connectors screen, with no restart and no per-server setup.
- **SC-003**: 100% of sign-ins from a server after its first reuse the stored registration (one registration per server in the tests).
- **SC-004**: Every refusal the person can fix (bad input, refused server, unsupported server, server down) shows a message that names the server and the next action; none leads to an error page.
- **SC-005**: No request leaves Umbral for a private or loopback address in a non-development instance, for any input on the account step.
- **SC-006**: The connector is fully exercised by automated tests with no real fediverse server: registration on first use and reuse after, re-registration, a refused and an unsupported server, the allowlist, PKCE and state, the stable subject, and an app receiving the user through OIDC.
- **SC-007**: Idle memory with the connector enabled stays within the 150 MiB budget measured by the existing memory test.

## Assumptions

- The registration's client name is "Umbral" and its website is the instance's public origin, so a server admin can tell which Umbral it is.
- PKCE support is read from the server's published OAuth server metadata (Mastodon 4.3 and later publish it); a server that publishes none is unsupported. If that excludes a GoToSocial or Akkoma version that otherwise works, the docs say which versions are known to work.
- The server's software name is read from its public NodeInfo to refuse Misskey-family servers with the specific message; a server with no NodeInfo is judged on its OAuth metadata alone.
- Server registrations always use Umbral's local encrypted store, even when connector secrets are configured to go to an external secret store: they are created by Umbral itself, one per server, and an operator would not want one secret per server in a cloud manager.
- The account step remembers nothing in the browser beyond the existing Last used hint, which names the connector, not the server.
- Disabling the connector does not delete registrations; deleting it keeps them too (Clarifications).

## Verbatim Constraints

- Connector identifier and kind: `fediverse`; routes `/connect/fediverse/start` (GET renders the account step, POST takes the handle) and `/connect/fediverse/callback`.
- Requested scope: `read:accounts` only.
- Claims: `name`, `preferred_username`, `picture`, `profile`; no `email`.
- Data hooks: `data-connector="fediverse"`, `data-connector-kind="fediverse"`, `data-connector-state`, `data-connector-shared`, `data-instance-scope`, `data-instance-list`.
- Screen copy: the strings proposed in sections 1 to 3 of `design/contracts/fediverse-connector-ui.md` (e.g. "Where is your account?", "Your account or server", "Continue", "Other ways to sign in", "Any server", "Only these servers", "Servers", "Enable Fediverse", and the error lines), landing in `Views/Texts.fs`.
