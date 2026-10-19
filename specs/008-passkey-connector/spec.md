# Feature Specification: The Passkey Connector

**Feature Branch**: `alta/rapid-centauri-mjkq` (worktree branch; spec directory `specs/008-passkey-connector`)

**Created**: 2026-10-19

**Status**: Draft

**Input**: User description: "Spec 008: the passkey connector. Sign in with a passkey, with or without a social provider." (task `2026-10-19-passkey-connector-sign-in-with-a-passkey-and-no-social-provi-p4ky`). Constitution 3.0.0, merged on 2026-10-19 as PR #1, redefined Umbral's job as passwordless sign-in through a social provider **or a passkey** and listed this spec as the deferred work.

## Background

Members of the maintainer's group use no social provider. Asked which one
they would sign in with, one answered: "None. I prefer a cryptographic key.
Implement FIDO." Until now Umbral forced a provider on every person: no
Google, GitHub or Discord account meant no account.

A passkey is a public-key credential a person's phone, laptop or hardware key
holds, unlocked by their fingerprint, face or PIN. The device proves to Umbral
that it holds the key; Umbral holds only the public half, which is not a secret.
Nothing is typed, nothing can be phished, and no third party learns who signed
in.

This feature adds passkeys as a way to sign in and a way to become a user,
without adding a concept: a passkey is a **Connector** of a built-in kind, with
Umbral itself as the provider, and a person's passkeys belong to their **User**,
exactly as the provider identities they sign in with do.

## Decisions Already Taken

Decided by the maintainer with the 3.0.0 amendment and in the task that asked
for this spec. They are not open here:

- Passkeys are a Connector of a built-in kind, "Passkey", with Umbral as the
  provider. The operator enables it on the Connectors screen; apps offer it
  through their existing sign-in methods (shared or chosen), like every other
  connector. No new top-level screen, no new noun.
- A person's passkeys belong to their User.
- The operator chooses, on the passkey connector, between **open sign-up**
  (anyone who reaches the sign-in page can register a passkey and become a
  user) and **invitations only**.
- Invitations are **links the operator creates and hands out**; Umbral sends
  no email. They are single-use, they expire, and Umbral stores only a hash of
  them.
- Recovery from a lost device is a new invitation for the **existing** user,
  which lets them register a new passkey on the same account.
- From the start, a signed-in user (through any connector) can add passkeys to
  their own account and remove them. This is the only self-service account
  page. A user can hold several passkeys.
- Verification is done by a maintained WebAuthn library (fido2-net-lib); Umbral
  writes no attestation or assertion checks of its own.
- Every ceremony uses a single-use challenge issued by the server; the origin
  and relying-party identifier are checked against the configured public
  origin; **user verification is required** (a passkey is the whole sign-in).
  Attestation is not required.
- Registering, removing and failing to use a passkey go to the security log as
  credential changes and authentication failures. The ceremony endpoints are
  rate-limited like the other sign-in endpoints.
- Still excluded: MFA (no passkey step after a social login or the other way
  round) and password accounts.

## Clarifications

### Session 2026-10-19

The task named six questions for this spec to settle and asked for a
recommended default for each. The maintainer was not available to answer
interactively, so each is settled below with its recommendation and the
reason. Any of them can be reopened before `/speckit-plan`.

- Q: What claims do a passkey-only user's tokens carry, given there is no
  verified email, and how do allowed-email-domain rules (spec 006) treat them?
  → A: `sub`, `name` (the display name chosen at enrolment) and `idp` with the
  value `passkey`. No `email` and no `email_verified`: both are absent, as they
  already are for a GitHub user who keeps their address private, which the
  README already tells apps to handle. A user who signs in with a passkey but
  also has a verified email from a linked provider keeps that email in their
  tokens. Allowed-email-domain rules do not govern the passkey connector at
  all: it has no email to judge, so the connector has no domain list, the
  instance list does not apply to sign-ins through it, and who can become a
  user through it is decided by its enrolment mode instead (the operator's
  choice the constitution names). An app that must have an email simply does
  not offer the passkey connector.
- Q: What is a passkey-only user's display name and how do they set it?
  → A: They type it at enrolment (required, 1 to 64 characters). It is the
  `name` apps receive and what the Users screen shows. Each passkey also has
  an optional nickname ("Phone", "YubiKey") for telling them apart on the
  passkeys page. The display name cannot be changed afterwards by the user:
  managing passkeys is the only self-service there is (Principle VII). An
  invitation for an existing user does not ask for a name.
- Q: What is the invitation lifetime and where does the operator create one?
  → A: Seven days, fixed, no setting. A new person is invited from the passkey
  connector's card on the Connectors screen; an existing user is invited from
  their row on the Users screen (recovery, or giving a social-only user a way
  to add a passkey). Both show the link once. Open invitations are listed on
  the connector's card with an optional note the operator typed, and can be
  revoked there.
- Q: Discoverable credentials (usernameless sign-in) or asking for a name first?
  → A: Discoverable. Umbral has no usernames and display names are not unique,
  so there is nothing to ask for first; asking would need a new identifier,
  which is a new noun. Registration requires a passkey the authenticator can
  present on its own, so "Sign in with a passkey" shows the authenticator's
  own account chooser and nothing else.
- Q: Can a passkey user link a social identity later (the other direction)?
  → A: Not in this feature. A passkey-only user has no verified email, so the
  existing linking rule has nothing to link on, and a social sign-in creates a
  new user. The passkeys page stays the only self-service page. Linking a
  provider from it is noted as possible later work, not built here.
- Q: What is the idle-memory impact of the WebAuthn library?
  → A: Measured, not assumed. An instance with no passkey connector must stay
  under the 150 MiB ceiling of spec 001 (the library is loaded only when a
  ceremony first runs); an instance with the connector enabled and a sign-in
  done is measured by the suite and must stay under the 160 MiB ceiling
  already accepted for an instance that loads an extra client library (spec
  005). The plan records the figures.

Three further decisions taken while writing, for the same reason (the
constitution's "one built-in passkey connector", Principle VII and Principle
IV):

- **One passkey connector per instance**, with the fixed identifier
  `passkey`. Passkeys belong to users, not to connectors, so a second passkey
  connector would share every person with the first and the stricter
  enrolment mode would be reachable through the looser one. An app that must
  not receive open-sign-up users does not offer the connector.
- **Invitations only is the default** enrolment mode when the operator
  enables the connector. Open sign-up is opt-in (Principle IV: the secure
  behaviour needs no action).
- **A user cannot remove their last passkey** while they have no other way to
  sign in (no linked provider identity). Locking oneself out is not a
  self-service action; the operator can still delete the account.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An invited person signs in with a passkey and no provider (Priority: P1)

The operator opens **Connectors**, finds the **Passkey** card, and enables it.
Its enrolment mode is "Invitations only". They click **Invite someone**, type
a note ("Ana"), and get a link, shown once, that is good for seven days. They
send it to Ana over a channel they trust. Ana opens the link on her phone,
types her name, and her phone asks for her fingerprint; a passkey is created.
She is told it worked and sees her passkeys page, with that one passkey on it.
Later, an app behind Umbral sends her to Umbral's sign-in page. She presses
**Sign in with a passkey**, her phone shows her account and asks for her
fingerprint, and she is back in the app, signed in. The app received an
ordinary OpenID Connect sign-in: a stable `sub`, her name, and `idp` saying
`passkey`. On **Users** the operator sees Ana, with "Passkey" as her way in.

**Why this priority**: This is the request that caused the 3.0.0 amendment: a
person who uses no social provider becomes a user and signs in to every app
behind Umbral.

**Independent Test**: Enable the connector, create an invitation, redeem it
with a software authenticator, then run an app's authorization request through
the real sign-in page and the passkey ceremony to an authorization code, and
exchange it for tokens whose claims are checked.

**Acceptance Scenarios**:

1. **Given** the passkey connector is enabled and set to invitations only,
   **When** the operator clicks Invite someone with a note, **Then** a link is
   shown once, an invitation exists that expires in seven days, and a security
   event records it.
2. **Given** a valid, unused invitation for a new person, **When** they open
   it, give a display name and complete the passkey registration with user
   verification, **Then** a user is created with that name, the passkey is
   stored against them, the invitation is used up, they are signed in to
   Umbral and see their passkeys page, and the log records the new user and
   the credential change.
3. **Given** a person with a registered passkey, **When** an app's
   authorization request sends them to the sign-in page and they complete the
   passkey sign-in with user verification, **Then** they return to the app with
   an authorization code, and the tokens carry `sub`, `name` and `idp` =
   `passkey` and no `email` or `email_verified`.
4. **Given** the same person, **When** they sign in again from the same or
   another device holding the same passkey, **Then** they are the same user,
   with the same `sub`.
5. **Given** an invitation that was already used, has expired, or was
   revoked, **When** anyone opens it, **Then** one and the same page says the
   link cannot be used and to ask whoever runs this Umbral for a new one,
   nothing is created, and a failure is recorded.
6. **Given** the connector is set to invitations only, **When** a person
   without an invitation reaches the passkey sign-in, **Then** they can sign in
   with a passkey they already have but cannot create an account, and the page
   tells them to ask for an invitation.

---

### User Story 2 - Every attack on the ceremony is refused (Priority: P1)

Someone replays a captured sign-in response, presents a response signed for a
different website, presents a stale challenge, uses an authenticator that did
not verify the user, or presents a credential Umbral never saw. Each attempt
ends on the sign-in page with the same short failure, creates no session and no
user, and leaves one failure in the security log naming why.

**Why this priority**: A passkey is the whole sign-in. Every one of these
refusals is required by the constitution (Principle IV and the testing
discipline); the feature is unsafe without them.

**Independent Test**: Drive each case through the real endpoints with a
software authenticator that can be made to misbehave (wrong origin, wrong
relying-party identifier, reused or expired challenge, user-verification flag
off, unknown credential, signature counter gone backwards, wrong user handle).

**Acceptance Scenarios**:

1. **Given** a sign-in response whose origin is not the configured public
   origin, **When** it is presented, **Then** it is refused, no session exists,
   and a failure event names the origin check.
2. **Given** a sign-in response built on a challenge that was already used
   once, **When** it is presented, **Then** it is refused and recorded.
3. **Given** a challenge older than its lifetime, **When** a response built on
   it is presented, **Then** it is refused and recorded.
4. **Given** a response from an authenticator that did not verify the user,
   **When** it is presented at registration or sign-in, **Then** it is refused
   and recorded; no passkey is stored and no session exists.
5. **Given** a response for a credential Umbral does not hold, or one that
   belongs to another user than the one the authenticator names, **When** it is
   presented, **Then** it is refused and recorded, and the page does not say
   which of the two it was.
6. **Given** a credential whose signature counter has gone backwards,
   **When** it is presented, **Then** it is refused and recorded.
7. **Given** a registration whose relying-party identifier does not match the
   configured public origin's host, **When** it is presented, **Then** it is
   refused.
8. **Given** more sign-in or registration attempts from one address than the
   sign-in rate limit allows, **When** the next arrives, **Then** it is refused
   with the same "too many attempts" page the other sign-in endpoints show.
9. **Given** a disabled user, **When** they complete a passkey sign-in,
   **Then** no session is established, the account-disabled message is shown,
   and the failure is recorded, exactly as for a provider sign-in.

---

### User Story 3 - A person adds passkeys to their account and removes them (Priority: P2)

Sam signs in to an app with GitHub. Wanting to stop depending on GitHub, Sam
opens Umbral's passkeys page (linked from the home page and named in the
README), is asked to sign in (through any connector the instance offers),
and sees the page: no passkeys yet, GitHub listed as the way they sign in. Sam
clicks **Add a passkey**, names it "Laptop", completes the ceremony, and the
passkey appears with its date. Sam adds a second one from a phone. Later Sam
removes "Laptop" after confirming. Each addition and removal is a credential
change in the security log. Ana, who has only one passkey and no provider, is
not allowed to remove it: the page says to add another first or ask the
operator to delete the account.

**Why this priority**: The 3.0.0 amendment promises that someone who signs in
with a provider can add a passkey to the same account, and that users manage
their own passkeys. Recovery (Story 5) and the operator's view (Story 6)
build on this page.

**Independent Test**: Sign a user in through the fake provider, open the
passkeys page, add two passkeys with the software authenticator, sign in with
one of them to an app, remove one, and check the list, the events and the
last-passkey refusal for a passkey-only user.

**Acceptance Scenarios**:

1. **Given** a person with no Umbral session, **When** they open the passkeys
   page, **Then** they are sent to the sign-in page, and after signing in
   through any connector they come back to the passkeys page.
2. **Given** a signed-in user, **When** they add a passkey with an optional
   nickname and complete registration with user verification, **Then** the
   passkey is listed with its nickname and creation time, and the log records
   a credential change naming the user.
3. **Given** a user who added a passkey, **When** they sign in to an app with
   it, **Then** they are the same user, with the same `sub`, and `idp` says
   `passkey`; their email claims, if they have a verified email from a
   provider, are unchanged.
4. **Given** a user with two passkeys, **When** they remove one after
   confirming, **Then** it is gone, signing in with it is refused as an unknown
   credential, and the log records a credential change.
5. **Given** a user whose only way to sign in is one passkey, **When** they try
   to remove it, **Then** it is refused with a message that says what to do
   instead.
6. **Given** a user with ten passkeys, **When** they try to add another,
   **Then** it is refused and the message says to remove one first.
7. **Given** a browser with JavaScript off, **When** the person opens the
   passkeys page or the passkey sign-in, **Then** the page says passkeys need
   JavaScript on this page and what to do.

---

### User Story 4 - Open sign-up: anyone can register a passkey (Priority: P2)

The operator runs a community instance and wants anyone to join. On the
Passkey card they switch enrolment to "Anyone can sign up" and save. A
stranger sent to the sign-in page by an app presses **Sign in with a
passkey**, chooses **Create an account with a passkey**, types a name,
completes the registration, and is signed in and sent on to the app. The Apps
screen warns, as it does for a connector with no email-domain rule, that an
app offering this connector lets anyone in.

**Why this priority**: The operator's choice the constitution names. It is
smaller than Story 1 because invitations are the default and the safe one.

**Independent Test**: Switch the mode, run a registration from the sign-in
page with no invitation, and check the user, the session and the authorization
code; switch back and check that the same path is refused.

**Acceptance Scenarios**:

1. **Given** the connector set to open sign-up, **When** a person with no
   account chooses to create one from the sign-in page, gives a name and
   completes registration, **Then** a user is created, they are signed in, and
   they return to the app's request with a code.
2. **Given** the connector set to invitations only, **When** the same request
   is made, **Then** the create-account path is not offered and a direct
   attempt is refused.
3. **Given** an app offering the passkey connector in open sign-up mode,
   **When** the operator looks at the Apps screen, **Then** the app is warned
   that the connector lets anyone in; in invitations-only mode there is no
   warning.
4. **Given** either mode, **When** the operator changes it, **Then** a
   security event records the connector change, and existing users and
   passkeys are unaffected.

---

### User Story 5 - Recovery: a new invitation for an existing user (Priority: P2)

Ana loses her phone. She tells the operator. On **Users**, on Ana's row, the
operator clicks **Invite to add a passkey**, gets a link shown once, and
sends it to Ana. On her new phone Ana opens the link, which says it adds a
passkey to the account "Ana", and registers one. She is signed in and lands on
her passkeys page, where she removes the old phone's passkey. Her `sub` is
unchanged, so every app knows her as before.

**Why this priority**: Without it a lost device is a lost account, and the
operator's only tool would be deleting the user, which changes their `sub`
in every app.

**Independent Test**: Enrol a user, create an invitation for that user, redeem
it with a second software authenticator, sign in to an app with the new
passkey and check the `sub` is the one from before; then remove the first
passkey.

**Acceptance Scenarios**:

1. **Given** an existing user, **When** the operator invites them from the
   Users screen, **Then** a link is shown once, the invitation is bound to that
   user, and the log records it with the user.
2. **Given** that invitation, **When** the person opens it, **Then** the page
   names the account the passkey will be added to and asks for no display
   name.
3. **Given** that invitation, **When** the person completes registration,
   **Then** the passkey belongs to the existing user, no user is created, the
   person is signed in as that user, and the invitation is used up.
4. **Given** the user was disabled or deleted after the invitation was
   created, **When** the link is opened, **Then** it cannot be used, and the
   page is the same as for any unusable invitation.
5. **Given** a user who signed in only with a provider, **When** the operator
   invites them and they redeem the link, **Then** they now have a passkey on
   the same account and can sign in with either.

---

### User Story 6 - The operator sees and manages passkeys and invitations (Priority: P3)

On **Users**, each row shows the person's ways of signing in; a user with
passkeys shows "Passkey" among them and, under it, each passkey with its
nickname, creation and last-use times, and a **Remove** button for when a
device is reported stolen. On the Passkey connector's card the operator sees
the open invitations (note, who they are for, when they expire) and can
revoke one. Disabling the connector stops passkey sign-ins and enrolments at
once; deleting it removes its invitations but not anyone's passkeys, which
work again if the connector is enabled again. Deleting a user deletes their
passkeys and invitations.

**Why this priority**: Operations the operator needs eventually, but the
feature works without them on day one.

**Independent Test**: Walk the Users and Connectors screens after Stories 1, 3
and 5, remove a passkey as the operator, revoke an invitation, disable and
delete the connector, and delete a user, checking the data and events each
time.

**Acceptance Scenarios**:

1. **Given** a user with passkeys, **When** the operator views Users, **Then**
   each passkey is listed with nickname, created and last-used times, and
   nothing else about the device.
2. **Given** that row, **When** the operator removes a passkey after
   confirming, **Then** it is gone, the log records a credential change with
   the operator as actor, and the user's other passkeys and identities remain.
3. **Given** open invitations, **When** the operator revokes one, **Then** the
   link no longer works and the log records it.
4. **Given** the connector is disabled, **When** anyone tries to sign in or
   enrol with a passkey, or to add one on the passkeys page, **Then** it is not
   available, exactly as for a disabled provider connector; removing a passkey
   still works.
5. **Given** the connector is deleted, **When** it is created again, **Then**
   everyone's passkeys work again and the old invitations do not.
6. **Given** a user is deleted, **When** the deletion completes, **Then** their
   passkeys and any invitation for them are gone, and a passkey sign-in with
   one of them is refused as unknown.
7. **Given** a change (disabling or deleting the connector, or changing an
   app's choice) that would leave an app with no way to sign in, **When** the
   operator attempts it, **Then** Umbral warns first and names the apps, as it
   does for every other connector.

---

### User Story 7 - The documentation gets a stranger from zero to a passkey sign-in (Priority: P3)

A reader of the README learns in the first sentence that Umbral signs people in
with a social provider or a passkey, finds a passkey section that says how to
enable the connector, invite someone, let people add passkeys to their
accounts, recover a lost device, and what an app receives for a passkey-only
user; and a walkthrough in `docs/connectors/passkey.md` with the details, the
gotchas (the address a passkey is bound to, no email, JavaScript needed, which
devices can hold one). "What Umbral is not" still excludes MFA and passwords
and says why a passkey is neither. `docs/vision.md` notes the 3.0.0 scope.
`CHANGELOG.md` has the entries under Unreleased.

**Why this priority**: Principle VI: a feature is not done until its
documentation is written.

**Independent Test**: Read the README and the walkthrough and perform every
step of Story 1 from them alone.

**Acceptance Scenarios**:

1. **Given** the README, **When** a reader looks for passkeys, **Then** the
   opening sentence, the concepts table, "What Umbral is not", the `idp`
   values, the "email may be missing" note and a passkey section describe
   them, with a link to the walkthrough.
2. **Given** the walkthrough, **When** an operator follows it, **Then** they
   can enable the connector, invite a person, and explain to that person what
   will happen on their device, without other sources.
3. **Given** `docs/vision.md` and `CHANGELOG.md`, **When** read, **Then** the
   3.0.0 scope and the Unreleased entries are there.

### Edge Cases

- **The public origin changes** (new domain): every passkey is bound to the
  old address and stops working. Umbral cannot migrate them. The walkthrough
  says so, and recovery is a new invitation per user. The connector card
  repeats the origin-mismatch banner the Connectors screen already shows.
- **Plain `http://localhost` trial**: passkeys work on `localhost` in
  browsers; a LAN address (`http://192.168.1.50`) does not get passkeys, as it
  gets no provider either. The walkthrough says so.
- **An authenticator cannot store a discoverable passkey** (an old security
  key): when it says so, registration is refused with a message to use another
  device; when it does not say, the registration is accepted.
- **The same authenticator is registered twice** for one user: the second
  registration is refused by the authenticator itself (Umbral tells it which
  credentials the user already has) or, if it slips through, by Umbral as a
  duplicate credential.
- **A credential id already stored for another user**: refused; nothing
  changes.
- **The person cancels the browser prompt**: back on the page they came from,
  with no failure recorded (nothing reached Umbral).
- **A ceremony outlives its challenge** (the person walks away for ten
  minutes): refused as expired; one click starts a fresh one.
- **Two devices redeem one invitation at the same time**: exactly one
  succeeds.
- **An invitation for a new person is redeemed by someone who already has an
  account**: a new user is created, as with any first sign-in without a
  verified email. The walkthrough says invitations for existing people are
  made from the Users screen.
- **The user handle in a sign-in response names a user other than the
  credential's owner**: refused, recorded.
- **A passkey-only user's session is honoured by an app**: only if that app
  offers the passkey connector (spec 003 FR-008), as for every connector.
- **Allowed-email-domain instance list is set**: it does not govern the
  passkey connector; the connector's card says who can sign in through it is
  decided by its enrolment mode; the "Taking a list off" warning does not
  name the passkey connector; a session or token from a passkey sign-in is
  honoured when re-checked.
- **Display name contains only white space or exceeds 64 characters**: refused
  with the limits said.
- **Nickname omitted**: a default naming the date is used.
- **Signature counter**: many synced passkeys always report zero, which is
  accepted; only a counter that goes backwards from a non-zero value is a
  refusal.
- **Reduced motion, 320 pixels wide, keyboard only**: the passkeys page and the
  sign-in step meet the same bar as every other page (spec 007).

## Requirements *(mandatory)*

### Functional Requirements

**The connector**

- **FR-001**: The Connectors screen MUST show a **Passkey** card like the
  cards for the other built-in kinds, creating the one passkey connector with
  the fixed identifier `passkey` when the operator saves it. There MUST be at
  most one passkey connector per instance; the add form MUST NOT offer the
  kind.
- **FR-002**: The passkey card MUST have no client id, client secret, callback
  URL or allowed-email-domains field. It MUST have: enabled state, "Shared
  with every app", the enrolment mode, the open invitations with a way to
  create and revoke them, and a line saying that who can sign in through it is
  decided by its enrolment mode.
- **FR-003**: The enrolment mode MUST be one of **Invitations only** (the
  default) and **Anyone can sign up**. Changing it MUST take effect at once
  and MUST be recorded as a connector change.
- **FR-004**: The connector's name as shown to the operator and on the Users
  screen MUST be "Passkey", and its button on the sign-in page MUST read "Sign
  in with a passkey". Neither is editable.
- **FR-005**: Enabling, disabling, sharing, choosing per app, the "offered
  nowhere" note, the stranded-apps warnings and deletion MUST behave for the
  passkey connector exactly as for every other connector (specs 001 and 003).
  Deleting it MUST delete its invitations and MUST NOT delete anyone's
  passkeys.

**Sign-in**

- **FR-006**: An app's sign-in page MUST offer "Sign in with a passkey" when
  the app offers an enabled passkey connector, in the position and look of the
  other buttons, including an app's accent colour (spec 006).
- **FR-007**: Pressing it MUST start a passkey sign-in that needs no name or
  identifier from the person: the authenticator presents the person's
  accounts for this Umbral and the person chooses one.
- **FR-008**: A successful sign-in MUST establish the person's Umbral session
  through the passkey connector and return them to the app's request exactly
  as a provider sign-in does, so the app receives an ordinary OpenID Connect
  sign-in with no passkey-specific code.
- **FR-009**: The tokens and user-info of a passkey sign-in MUST carry `sub`,
  `name`, `idp` = `passkey`, and `picture` or `email`/`email_verified` only
  when the user has them from a linked provider. A passkey-only user MUST have
  no `email` and no `email_verified` claim.
- **FR-010**: Allowed-email-domain rules (spec 006) MUST NOT apply to the
  passkey connector: no rule refuses a passkey sign-in, a session or a token
  established through it, at the callback, the authorize endpoint or the token
  endpoint, whatever the instance list says. The Apps screen's
  "unrestricted connector" warning (spec 006 FR-010) MUST treat a passkey
  connector in open sign-up as unrestricted and in invitations-only as
  restricted.
- **FR-011**: A disabled user, a deleted user and a disabled connector MUST
  be refused exactly as for a provider sign-in (spec 001 FR-022, spec 003),
  with the same messages and events.

**Enrolment**

- **FR-012**: With open sign-up, the passkey sign-in page MUST also offer
  "Create an account with a passkey", which asks for a display name, registers
  a passkey, creates the user, signs them in and returns them to the app's
  request. With invitations only it MUST NOT offer it, and a direct attempt
  MUST be refused; the page MUST tell the person to ask whoever runs this
  Umbral for an invitation.
- **FR-013**: The operator MUST be able to create an invitation for a new
  person from the passkey card, with an optional note of up to 64 characters,
  and for an existing user from that user's row on the Users screen. The link
  MUST be shown once, in full, with a copy control, and never again.
- **FR-014**: An invitation MUST be single-use, MUST expire seven days after
  creation, MUST be revocable by the operator, and MUST be stored only as a
  hash of its secret; the note, the connector, the user it is for (if any),
  and its times are stored in clear. Umbral MUST NOT send it anywhere.
- **FR-015**: Opening an invitation link MUST show a page that, for a new
  person, asks for a display name and, for an existing user, names the account
  the passkey will be added to; completing registration MUST use the
  invitation up, store the passkey, create the user when there was none, sign
  the person in and show their passkeys page with a success notice.
- **FR-016**: An invitation that is unknown, used, expired or revoked, or whose
  user is disabled or deleted, MUST be answered with one and the same page
  that says the link cannot be used and to ask whoever runs this Umbral for a
  new one, and MUST be recorded as a failure that says which it was. Two
  simultaneous redemptions of one invitation MUST result in exactly one
  success.
- **FR-017**: A display name MUST be required at enrolment of a new person,
  trimmed, 1 to 64 characters; a refusal MUST say the limits.

**The person's passkeys page**

- **FR-018**: A page MUST exist for a signed-in user to see their passkeys
  (nickname, created, last used) and their other ways of signing in, to add a
  passkey with an optional nickname of up to 64 characters, and to remove one
  after confirming. It MUST require an Umbral session established through any
  connector, send a person without one to the sign-in page and bring them
  back afterwards, and MUST wear Umbral's own look, never an app's branding.
- **FR-019**: The page MUST be reachable from Umbral's public home page and
  MUST be named in the README. A person who has just enrolled through an
  invitation MUST land on it.
- **FR-020**: Removing a user's last passkey MUST be refused while the user has
  no linked provider identity, with a message that says to add another first
  or ask whoever runs this Umbral to delete the account.
- **FR-021**: A user MUST be able to hold up to ten passkeys; the eleventh
  MUST be refused with a message to remove one first.
- **FR-022**: Adding a passkey MUST require an enabled passkey connector; when
  there is none the page MUST say passkeys are not enabled on this Umbral.
  Removing MUST work regardless.

**Security**

- **FR-023**: Every registration and every sign-in MUST use a challenge the
  server issued for that ceremony, accepted at most once and only within its
  lifetime of at most five minutes; a reused or expired challenge MUST be
  refused and recorded.
- **FR-024**: The origin of every ceremony response MUST equal the configured
  public origin exactly, and the relying-party identifier MUST be that
  origin's host; a mismatch MUST be refused and recorded. Nothing MUST be
  taken from the request's own host.
- **FR-025**: User verification MUST be required at registration and at
  sign-in; a response without it MUST be refused and recorded. Attestation
  MUST NOT be required and MUST NOT be stored beyond what the library returns
  for the credential itself.
- **FR-026**: Registration MUST ask for a discoverable credential; when the
  authenticator reports it did not create one, the registration MUST be
  refused with a message to use another device.
- **FR-027**: A sign-in with an unknown credential, a credential whose owner
  is not the user the authenticator names, an invalid signature, or a
  signature counter that has gone backwards from a non-zero value MUST be
  refused and recorded; the person MUST see the same failure in every case.
- **FR-028**: All verification of registration and sign-in responses MUST be
  done by the maintained WebAuthn library; Umbral MUST NOT parse attestation
  objects or verify assertion signatures itself.
- **FR-029**: Every state-changing passkey request (starting or completing a
  ceremony, creating or redeeming an invitation, removing a passkey, changing
  the enrolment mode) MUST carry the same cross-site request protection as
  every other form.
- **FR-030**: Starting and completing a ceremony and opening an invitation
  MUST be rate-limited per client address like the other sign-in endpoints,
  with the same refusal page.
- **FR-031**: Registering a passkey, removing one (by the user or the
  operator), creating, revoking and redeeming an invitation MUST be recorded
  in the security log as credential changes naming the user (when known) and
  the actor; every refused ceremony and every unusable invitation MUST be
  recorded as an authentication failure with a closed reason word. No
  challenge, credential id, public key, invitation secret or link MUST reach
  the log or the security events.
- **FR-032**: Changing the public origin MUST NOT be hidden: the walkthrough
  MUST say that passkeys are bound to the address and stop working if it
  changes, and that recovery is a new invitation per user.

**The operator's view**

- **FR-033**: The Users screen MUST show "Passkey" among a user's ways of
  signing in when they have one or more, list each passkey (nickname, created,
  last used) with a Remove control that confirms first, and offer **Invite to
  add a passkey** for every user while a passkey connector exists.
- **FR-034**: The passkey card MUST list open invitations (note, "new person"
  or the user's name, created, expires) with a Revoke control, and MUST NOT
  list used, expired or revoked ones.
- **FR-035**: Deleting a user MUST delete their passkeys and their
  invitations.

**Without JavaScript**

- **FR-036**: Passkey sign-in, enrolment and adding a passkey need the
  browser's credential API, which needs script. With JavaScript off, each of
  those pages MUST say so and what to do (turn it on for this page, or use
  another sign-in method the app offers). Removing a passkey, revoking an
  invitation and every operator action MUST work without JavaScript.

**Documentation**

- **FR-037**: The README MUST be updated: the opening sentence, the concepts
  table, "What Umbral is not" (why a passkey is neither MFA nor a password
  account), the Requirements ("an account with a provider" is no longer the
  only way), the `idp` values and the "email may be missing" note, a passkey
  section (enable, invite, open sign-up, add to an account, recovery, the
  passkeys page, what apps receive, the memory note), and the walkthrough list.
  `docs/connectors/passkey.md` MUST exist as a walkthrough. `docs/vision.md`
  MUST note the 3.0.0 scope. `CHANGELOG.md` MUST have the entries under
  Unreleased.

### Constitution Check (spec level)

- **Concept count (Principle I)**: No new noun. "Passkey" is a Connector kind
  and a User's credential; "invitation" is how the operator admits a person
  through that connector, the way a domain list admits them through another,
  and it lives on the connector's card and the user's row. The admin UI stays
  three top-level screens; the passkeys page is the person's own, not an
  admin screen.
- **Single artifact (Principle II)**: Public keys, counters and invitation
  hashes live in the database in the data directory; a library ships in the
  image; nothing runs outside the container. Idle memory is measured (SC-008).
- **Protocol safety (Principle III)**: The WebAuthn library verifies every
  response (FR-028); OpenIddict issues every token as today (FR-008).
- **Secure default (Principle IV)**: Invitations only by default (FR-003);
  user verification, single-use challenge and origin check are not settings
  (FR-023 to FR-025); hashed, expiring, single-use invitations (FR-014);
  events for every change and failure (FR-031); rate limits (FR-030); CSRF
  protection (FR-029).
- **Documentation (Principle VI)**: FR-037 and Story 7.
- **Scope (Principle VII)**: Passkeys are in scope since 3.0.0. No MFA: a
  passkey is never asked after a provider sign-in or the reverse. No
  passwords. The enrolment mode decides who can be a user and nothing else;
  the passkeys page is the one self-service page.

### Key Entities

- **Passkey connector** (a Connector of kind `passkey`): enabled, shared,
  enrolment mode (invitations only, or anyone can sign up). At most one per
  instance, identifier `passkey`. No credentials of its own.
- **Passkey** (belongs to a User): the credential's identifier, its public
  key, its signature counter, an optional nickname, when it was created and
  last used, and what the library returns about the credential itself (such
  as the authenticator's model identifier and transports) for the
  authenticator's benefit on the next sign-in. Never a secret.
- **Invitation** (belongs to the passkey connector; optionally to a User): a
  hash of its secret, an optional note, the user it is for (none for a new
  person), created, expires, and when it was used or revoked. Used, expired
  and revoked invitations are kept only as long as they are useful for the
  log and may be pruned.
- **Ceremony challenge**: a random value issued for one registration or one
  sign-in, usable once within five minutes.
- **User** (unchanged): a passkey-only user has a display name, no email, no
  avatar, and no linked provider identity.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An invited person goes from opening the link to a passkey
  sign-in into an app in under two minutes and three screens (name, device
  prompt, done), with no email and no provider account.
- **SC-002**: A returning person signs in to an app with a passkey in one
  press and one device prompt; no name or identifier is typed.
- **SC-003**: 100% of the refusal cases in Story 2 and FR-016 end with no
  session, no new user, no stored passkey, and exactly one recorded failure
  naming the reason; no two refusals are distinguishable on the page.
- **SC-004**: A passkey sign-in reaches an app as a standard OpenID Connect
  sign-in: an app integrated for provider sign-ins needs zero changes to
  accept it.
- **SC-005**: A user who signs in with a provider and then with a passkey they
  added is one user with one `sub` in 100% of cases; a recovered user keeps
  their `sub`.
- **SC-006**: Every acceptance scenario in Stories 1 to 6 is covered by an
  automated test that drives the real endpoints with a software
  authenticator, including a wrong origin, a reused challenge and a missing
  user verification (constitution testing discipline).
- **SC-007**: No challenge, credential identifier, public key, invitation
  secret or link appears in any log line or security event during the full
  journey at the most verbose log level (spec 001 SC-008).
- **SC-008**: An instance with no passkey connector idles under 150 MiB
  resident (spec 001 SC-007); an instance with the connector enabled and one
  sign-in done idles under 160 MiB (the ceiling of spec 005 SC-006). Both
  figures are measured by the suite and recorded in the plan.
- **SC-009**: Every existing test passes unchanged, and an instance without a
  passkey connector behaves exactly as before.
- **SC-010**: A reader can enable the connector, invite a person and get them
  signed in from the README and the walkthrough alone (Story 7).

## Assumptions

- "Seven days" for an invitation is 7 × 24 hours from creation; "five
  minutes" for a challenge is the maximum; the plan may choose a shorter
  challenge lifetime.
- The upper limit of passkeys per user is ten.
- The default nickname of a passkey is a fixed phrase with the date it was
  added.
- The display name is what the authenticator shows the person when choosing
  an account, together with their email when the user has one, so people with
  several accounts on one instance can tell them apart.
- The passkeys page is reached at a fixed address of Umbral's, linked from the
  home page; there is no link to it from an app's branded sign-in page, whose
  footer stays "Secured by Umbral" alone (spec 006).
- Passkeys that work on `localhost` over plain http are enough for the
  laptop trial; a real deployment is behind HTTPS as today.
- Browsers that lack the credential API behave as "JavaScript off" (FR-036).
- Provider sign-ins, linking by verified email, allowed-email-domain rules for
  provider connectors, and every other existing behaviour are unchanged.
- The WebAuthn library is loaded lazily, on the first ceremony, so an
  instance without a passkey connector does not pay for it in memory; the plan
  confirms this by measurement.

## Out of Scope

- MFA in any form: a passkey after a provider sign-in, or anything after a
  passkey.
- Password accounts, usernames, email-based sign-in or recovery, and any
  email Umbral would send.
- Linking a provider identity to a passkey-only user (possible later work),
  merging users, and changing a display name after enrolment.
- Several passkey connectors per instance.
- Operator-set lifetimes or limits (invitation lifetime, passkeys per user),
  attestation policies, authenticator allow-lists or metadata lookups.
- Conditional (autofill) passkey sign-in; the button is the way in.
- Migrating passkeys to a new public origin.
