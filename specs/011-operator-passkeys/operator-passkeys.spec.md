# Feature Specification: Operator passkeys

**Feature Branch**: `alta/earthy-orinoco-8bkw` (worktree branch; spec directory `specs/011-operator-passkeys`)

**Created**: 2026-10-26

**Status**: Draft

**Input**: Task `2026-10-24-spec-011-operator-passkeys-0p5k`: "Spec 011: the operator signs in to the admin area with a passkey, and can switch password sign-in off". The maintainer, 2026-10-24: "about the umbral operator. we only have a password we should be able that the operator could add a passkey or a two-factor (6 code random)". Same day, the maintainer chose operator passkeys with a switch to turn password sign-in off, and not a 6-digit code.

## Background

The admin area (Apps, Connectors, Users) is guarded by one operator credential
(spec 001 FR-017): a password, independent of every connector, so a broken
provider can never lock the operator out. It is hashed, rate-limited and
replaceable, but a password can be phished, and it guards everything.

This feature lets the operator add passkeys to that credential, sign in with
one, and switch password sign-in off. A passkey resists phishing, and Umbral
already verifies passkeys for the passkey connector. A 6-digit code (TOTP) was
refused: it does not resist phishing, it adds a stored secret, clock drift and
recovery codes, and MFA is on Principle VII's permanent exclusion list.
Constitution 3.0.1 says the operator credential is not a local account and may
be a password, passkeys, or both.

## Decisions Already Taken

Decided in the task before this spec; not open:

- **Not a new concept (Principle I).** Operator passkeys belong to the operator
  credential, which is not an App, a Connector or a User. They are stored apart
  from users' passkeys, never appear on the Users screen, and never sign anyone
  in to an app.
- **Credential screen.** Add a passkey (a name, then the ceremony, with user
  verification and a discoverable credential required, as the passkey connector
  does), up to 10. A list with each passkey's name, when it was added, when it
  was last used, and Remove. A switch: password sign-in on or off.
- **Re-checking who it is.** Adding a passkey, removing one and changing the
  switch each ask for the current credential first (the password, or a passkey
  ceremony), as replacing the password already does.
- **Switching password sign-in off** is refused while no operator passkey
  exists, and takes a passkey ceremony on the spot. While it is off, the sign-in
  page shows only the passkey sign-in, a password sent anyway gets the same
  answer as a wrong one, and the password field of the Credential screen is
  hidden. The stored password is kept, so switching back on is one action.
  Removing the last passkey while it is off is refused, and the refusal names
  the next action.
- **Sign-in page.** "Sign in with a passkey" with no name typed, beside the
  password form while password sign-in is on. Without JavaScript, the password
  form if it is on, otherwise a line saying a passkey needs JavaScript. The same
  rate limit as the password sign-in.
- **Recovery.** `reset-credential` prints a fresh password once, switches
  password sign-in back on, and ends every admin session. It keeps the operator
  passkeys; `--forget-passkeys` removes them all. Documented in the README's
  "Forgotten operator credential".
- **First run** is unchanged: the setup screen sets the password, and a passkey
  is added afterwards.
- **Security (Principle IV).** Single-use challenges with the connector's
  lifetime, the RP ID is the issuer's host and the origin is checked, the
  signature counter is checked, security-log events for add, remove, switch and
  sign-in (failures included), and no credential material in a log, a URL or a
  page.
- **Storage.** Operator passkeys and the switch in the next schema version,
  covered by the README's backup section.

## Clarifications

### Session 2026-10-26

Answered from the task's decisions and the existing code, unattended. Wording
and layout questions went to `umbral-designer` (see "Design questions").

- Q: How does an action on the Credential screen carry the re-check? → A: The
  action itself carries it: either the current password, typed in the same form
  (only while password sign-in is on), or a passkey confirmation made for that
  action a moment before, whose answer the action's own request carries. The
  ceremony is single-use, lives as long as a connector ceremony (5 minutes), and
  only a signed-in admin session can start or complete one. Nothing like a
  "confirmed for the next N minutes" window exists.
- Q: Does switching password sign-in off accept the password as its re-check?
  → A: No. Turning it off takes a passkey ceremony on the spot, which is the
  re-check; the password is not asked for. Turning it back on takes the current
  credential like any other action, which while it is off means a passkey.
- Q: Which requests count against the admin rate limit? → A: Every request that
  checks a password or completes an operator passkey ceremony (sign-in and
  re-check) is limited exactly as the password sign-in is today (`RateLimits.AdminAuth`,
  10 per 10 minutes per address). A request that only starts a ceremony gets
  the connector's ceremony limit (`RateLimits.SignIn`): it checks nothing, and
  putting it under the admin limit would halve the number of passkey sign-ins
  the limit allows.
- Q: Does an action end other admin sessions? → A: Removing a passkey and
  switching password sign-in off end every other admin session, like replacing
  the password: the usual reason for either is that a device or the password is
  no longer trusted. Adding a passkey and switching password sign-in on do not.
  The session that made the change stays signed in.
- Q: What can a passkey's name be? → A: The same rules as a user's passkey
  nickname: optional, bounded in length, and a dated default when left empty.
- Q: Can an operator passkey be used at a user's passkey sign-in, or a user's
  passkey at the operator sign-in? → A: Neither. Each sign-in only accepts
  credentials from its own store, and an operator passkey carries a user handle
  that no user can have.
- Q: Does the password replacement form stay while password sign-in is off? →
  A: No. It is hidden, and a replacement sent anyway is refused. The stored
  password is kept unchanged until password sign-in is on again.
- Q: Which ceremonies update "last used"? → A: Every successful operator passkey
  ceremony: a sign-in and a re-check.
- Q: How is "a passkey ceremony on the spot" for switching off different from
  any re-check? → A: It is not; the point is that it is a passkey and not the
  password, so the operator proves a passkey works before losing the password.

### Design questions (for `umbral-designer`)

Layout and wording of the Credential screen and the sign-in page belong to the
designer. Asked on 2026-10-26, answered the same day in
`design/contracts/operator-passkeys-ui.md`, which is normative for the screens.
Two behaviours come from it: Remove and the switch first post with no re-check,
and the server answers 422 with the screen redrawn around a confirm block that
carries the re-check (a strand-warning confirm); and "Credential" stays the
screen name, meaning the set of ways in, while the password is called the
password.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Add a passkey and sign in with it (Priority: P1)

The operator, signed in with the password, opens the Credential screen, names a
passkey, confirms with the password, and completes the browser's passkey
dialog. The passkey appears in the list. Later, on the sign-in page, they choose
"Sign in with a passkey", pick it in the browser's dialog without typing
anything, and land in the admin area.

**Why this priority**: It is the feature: a phishing-resistant way into the admin area.

**Independent Test**: With a software authenticator, add a passkey through the
real endpoints, sign out, sign in with it, and reach an admin screen.

**Acceptance Scenarios**:

1. **Given** a signed-in operator with no passkeys, **When** they add a passkey
   named "Laptop" and confirm with the correct password, **Then** the list shows
   "Laptop", when it was added, and that it was never used.
2. **Given** an operator passkey, **When** someone completes "Sign in with a
   passkey" with it, **Then** they hold an admin session and the passkey's last
   used time is now.
3. **Given** the add form, **When** the password typed is wrong, **Then**
   nothing is added and the failure is in the security log.
4. **Given** 10 operator passkeys, **When** the operator tries to add an 11th,
   **Then** it is refused with the reason.
5. **Given** a sign-in ceremony already completed once, **When** the same answer
   is sent again, **Then** it is refused.

### User Story 2 - Switch password sign-in off (Priority: P1)

The operator with at least one passkey turns password sign-in off. Umbral asks
for a passkey ceremony there and then. From then on the sign-in page offers only
the passkey, and a password, even the right one, does not get in.

**Why this priority**: A password that cannot be used cannot be phished.

**Independent Test**: Add a passkey, turn the switch off with a passkey
ceremony, then post the correct password to the sign-in and see it refused.

**Acceptance Scenarios**:

1. **Given** no operator passkey, **When** the operator tries to switch password
   sign-in off, **Then** it is refused with the next action (add a passkey).
2. **Given** a passkey, **When** the operator switches password sign-in off and
   completes a passkey ceremony, **Then** password sign-in is off and every
   other admin session has ended.
3. **Given** password sign-in off, **When** the correct password is posted to
   the sign-in, **Then** the answer is the same as for a wrong password and no
   session is opened.
4. **Given** password sign-in off, **When** the operator opens the sign-in page
   or the Credential screen, **Then** neither shows a password field.
5. **Given** password sign-in off, **When** the operator switches it back on
   with a passkey re-check, **Then** the old password works again.

### User Story 3 - Remove a passkey (Priority: P2)

The operator removes a passkey they no longer have (a lost phone), after
confirming with the password or another passkey. Other admin sessions end.

**Why this priority**: Without it, a lost device stays a key to the admin area.

**Independent Test**: Add two passkeys, remove one with a password re-check,
and see it gone, its sign-in refused, and another session signed out.

**Acceptance Scenarios**:

1. **Given** two passkeys, **When** the operator removes one with a correct
   re-check, **Then** it is gone and a sign-in with it is refused.
2. **Given** password sign-in off and one passkey, **When** the operator tries
   to remove it, **Then** it is refused with "turn password sign-in on first".
3. **Given** password sign-in on and one passkey, **When** the operator removes
   it, **Then** it is removed.

### User Story 4 - Recover from the host (Priority: P2)

An operator who lost every device, or the password while it was off, runs
`reset-credential` on the host. It prints a new password, turns password
sign-in back on and ends every admin session. With `--forget-passkeys` it also
removes every operator passkey, for a stolen device.

**Why this priority**: FR-017's promise: no lost device locks the operator out for good.

**Independent Test**: Run the command against a data directory with passkeys
and the switch off, with and without the flag, and check the outcome.

**Acceptance Scenarios**:

1. **Given** password sign-in off and two passkeys, **When** `reset-credential`
   runs, **Then** a new password is printed once, password sign-in is on, the
   passkeys are kept, and every admin session has ended.
2. **Given** the same, **When** `reset-credential --forget-passkeys` runs,
   **Then** additionally no operator passkey remains, and the output says so.

### Edge Cases

- A passkey removed in one tab while another tab is signing in with it: the
  sign-in is refused.
- Two requests that would each remove one of the last two passkeys while
  password sign-in is off: exactly one succeeds.
- Switching off while the only passkey is being removed in another tab: password
  sign-in never ends up off with no passkey.
- A user's passkey offered at the operator sign-in, or an operator passkey at
  a user's passkey sign-in: refused, logged, no session of either kind.
- A confirmation ceremony replayed for a second action: refused, it is single-use.
- The authenticator's signature counter goes backwards: refused and logged.
- An upgrade from schema 7: no passkeys and password sign-in on, so nothing
  changes until the operator adds a passkey.
- JavaScript off: the password form works while it is on; with it off, the page
  says a passkey needs JavaScript.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The operator MUST be able to add a passkey to the operator credential from the Credential screen, giving it a name, with user verification and a discoverable credential required.
- **FR-002**: Umbral MUST refuse to add an operator passkey beyond 10.
- **FR-003**: The Credential screen MUST list each operator passkey with its name, when it was added, when it was last used, and a Remove action.
- **FR-004**: Adding a passkey, removing one, and changing the password sign-in switch MUST each require the current credential in that same action: the current password (only while password sign-in is on) or the answer of a passkey ceremony made for it, single-use and short-lived.
- **FR-005**: The operator sign-in page MUST offer "Sign in with a passkey", a discoverable-credential ceremony that asks for no name, and MUST keep the password form while password sign-in is on.
- **FR-006**: A successful operator passkey sign-in MUST open the same admin session the password does, and update that passkey's last used time.
- **FR-007**: Switching password sign-in off MUST be refused while no operator passkey exists, and MUST take a passkey ceremony at that moment.
- **FR-008**: While password sign-in is off, every route that checks the password (sign-in, re-check, password replacement) MUST refuse it, and the sign-in MUST answer exactly as for a wrong password.
- **FR-009**: While password sign-in is off, neither the sign-in page nor the Credential screen MUST show a password field.
- **FR-010**: Switching password sign-in off MUST keep the stored password, so switching it back on restores it unchanged.
- **FR-011**: Removing the last operator passkey while password sign-in is off MUST be refused with the next action, and the check MUST hold against simultaneous requests.
- **FR-012**: Removing a passkey and switching password sign-in off MUST end every other admin session; the session that made the change stays signed in.
- **FR-013**: `reset-credential` MUST print a fresh password once, switch password sign-in on, end every admin session, and keep the operator passkeys.
- **FR-014**: `reset-credential --forget-passkeys` MUST additionally remove every operator passkey and say so in its output.
- **FR-015**: Operator passkeys MUST be stored apart from users' passkeys, MUST NOT appear on the Users screen, and MUST NOT sign anyone in through the passkey connector or any app's sign-in.
- **FR-016**: Every operator ceremony MUST use a single-use challenge with the passkey connector's lifetime, the issuer's host as RP ID, the configured public origin as the only accepted origin, and MUST refuse a signature counter that goes backwards.
- **FR-017**: Requests that check a password or complete an operator passkey ceremony MUST share the password sign-in's rate limit.
- **FR-018**: The security log MUST record operator passkey added, removed, the switch changed, and operator sign-in by passkey, each with its failures, with no credential id, public key, challenge or password in any log, URL or page.
- **FR-019**: Without JavaScript, the sign-in page MUST show the password form while password sign-in is on, and otherwise a line saying a passkey needs JavaScript.
- **FR-020**: First-run setup MUST be unchanged: it sets the password, and no passkey is asked for.
- **FR-021**: The next schema version MUST add the operator passkeys and the switch; an upgraded instance starts with no passkeys and password sign-in on.
- **FR-022**: The README MUST document `reset-credential --forget-passkeys` in "Forgotten operator credential", the new data in the backup section, and the schema version; the CHANGELOG MUST carry the change under Unreleased.

### Key Entities

- **Operator passkey**: one public-key credential of the operator credential. A
  random id, the credential id, the public key, the signature counter, a name,
  when it was added, when it was last used. No owner: there is one operator.
- **Password sign-in switch**: on or off; on by default and after every reset.
- **Operator user handle**: one random value that every operator passkey
  carries, so an authenticator groups them and no user's account can match it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With password sign-in off, 0 of all tested routes accept the
  correct password for a session or a re-check.
- **SC-002**: An operator with a passkey signs in with two actions (the button
  and the authenticator's confirmation) and no typing.
- **SC-003**: Every scenario in this spec passes through the real endpoints
  with a software authenticator, including a reused challenge, a wrong origin, a
  regressed counter and a missing user verification.
- **SC-004**: A user's passkey and an operator passkey are refused at each
  other's sign-in in 100% of tests, and no operator passkey appears on the
  Users screen.
- **SC-005**: Idle memory stays within Principle II's 150 MiB.

## Assumptions

- The passkey connector does not need to be enabled for operator passkeys: the
  admin credential is independent of every connector (FR-017 of spec 001).
- The authenticator shows the operator passkey under a fixed label naming Umbral
  and the operator; the exact words are the designer's.
- The word "credential" on existing screens may change to name the password and
  passkeys apart; that wording is the designer's.

## Verbatim Constraints

- `reset-credential`
- `--forget-passkeys`
- `/admin/signin`
- `RateLimits.AdminAuth`
- "Sign in with a passkey"
- "Turn password sign-in on first" (the refusal's next action; final wording the designer's)
