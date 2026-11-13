# Feature Specification: Trust, or not, an OpenID Connect provider's verified emails

**Feature Branch**: `alta/chipper-duet-8b42` (worktree branch; spec directory `specs/017-oidc-email-trust`)

**Created**: 2026-11-13

**Status**: Implemented (full suite 2825 green at eb9b894); design, documentation and code reviews pending

**Input**: Task `2026-11-13-oidc-connector-trust-its-verified-emails-switch-tr5t`.

## Background

The forge documentation task (`f0rg`, 2026-11-13) found that Forgejo and Gitea
send `email_verified: true` for every active account, whether or not anyone
confirmed the address. On a forge with open registration and no confirmation, a
newcomer can register with someone else's address, sign in to Umbral, and be
linked to that person's existing account: an account takeover across
connectors, the class of the 0.8.0 fix (h762) and Microsoft's nOAuth.

Today the only defence is the documentation and `ConnectorEmailTrustWarning`.
This switch lets an operator add a provider whose registration they do not
control, safely. It decides who can be an Umbral user and how a person is
recognised, nothing about what anyone may do (Principle VII).

## Decisions Already Taken

From the task; not open:

- **Scope**: the generic OpenID Connect connector (kind `oidc`) only. Google,
  GitHub, Discord, Apple and Microsoft keep their own documented rules; the
  Fediverse, Bluesky and passkeys carry no email.
- **Off means**: the address is still stored (an app may still receive
  `email`), always with `email_verified` false for this connector's sign-ins. So
  it never links to an existing user, and never satisfies an allowed-email-domains
  rule.
- **Existing connectors keep trust on**: the schema step sets it on for every
  existing `oidc` row, so an upgrade changes nobody's sign-in. An Upgrade note
  says so and points to the setting.
- **A new connector makes the operator choose**: the add form shows the two
  options with neither preselected and is refused until one is chosen.
- **Changing it later** affects sign-ins from then on. Turning trust off unlinks
  nobody; the screen says so. Every change is logged `connector_updated` with the
  fixed detail `email_trust=on|off`.

## Clarifications

### Session 2026-11-13

- Q: Where is "off" applied? → A: Once, at the one place every provider's
  answer passes before anything is judged or written:
  `Callback.finishSignIn`. The profile's `EmailVerified` is set to false there
  when the connector does not trust its provider's emails, before the domain
  rule, `UserStore.resolve` (linking, the users row, the linked account's
  `asserted_email_verified`) and the session's remembered domain. The adapter
  is unchanged, so the rule cannot be bypassed by another adapter path.
- Q: Remembered sessions admitted through a domain rule this connector can no
  longer meet? → A: Refused at their next authorization or token request.
  `EmailDomainRules.recheck` judges a session, code or refresh token of a
  connector that does not trust its emails as if no verified domain had been
  remembered: refused while a rule (its own or the instance's) applies,
  untouched when none does.
- Q: A row of kind `oidc` with no stored value? → A: Read as off (fail closed).
  The schema step leaves none behind; a row written by a path that forgot the
  column cannot silently trust.
- Q: The stored flag of users already verified by this provider when trust is
  turned off? → A: Agreed with the `umbral` session (owner of the defaults):
  turning trust off also lowers, in the same transaction, the
  `asserted_email_verified` of this connector's links and, by the h762 rule
  (schema step 9), `users.email_verified` for those users unless another linked
  account verifies the stored address. It lowers only and unlinks nobody.
  Without it, an account a stranger registered while trust was on would keep the
  owner's address verified, and the owner's first sign-in through another
  provider would still be joined into it. Turning trust on raises nothing; the
  next trusted sign-in raises the flag as today. A failure while lowering
  leaves the setting unchanged (one transaction). The screen says so before the
  operator turns trust off, and the log detail carries the number of users
  whose flag went down.
- Q: A connector's own domain list with trust off? → A: Refused at save (422),
  a contradiction inside one form. The instance list, set elsewhere, is warned
  about on the pane, in its Set-up rule line and by the list entry's attention
  marker (`umbral-designer`, contract).
- Q: Where on the screens? → A: A group of two cards, "Trusted" and "Not
  trusted", before the allowed email domains field on the add form and the
  pane; the add form's amber warning becomes the group's lead
  (`umbral-designer`, contract).
- Q: Turning trust off, since it changes stored data? → A: The save asks first,
  naming how many addresses become unverified (`umbral-designer`, contract).

## User Scenarios & Testing

### User Story 1: An open forge, added safely (Priority: P1)

An operator adds a Gitea they don't control the registration of and chooses
**off**. A stranger registers there with Alice's address and signs in.

**Acceptance**:
1. The stranger becomes a new user; Alice's user (verified address, through
   Google) is untouched.
2. The stranger's stored email is unverified; apps receive `email_verified`
   false for them, in the `id_token` and from userinfo.
3. Alice signing in through Google later is not joined into the stranger's user.

### User Story 2: A provider the operator runs (Priority: P1)

An operator chooses **on** for their own Authentik. Behaviour is today's: a
verified address links to the one existing user with it, and meets a domain rule.

### User Story 3: Upgrading (Priority: P1)

An instance with two OIDC connectors upgrades. Both keep trust on; nobody's
sign-in changes. The Upgrade note in the changelog names the setting.

### User Story 4: Turning trust off later (Priority: P2)

An operator learns their provider marks unconfirmed addresses verified and turns
trust off. From the next sign-in, that provider's addresses are unverified.
Nobody already linked is unlinked. A person whose session was admitted through a
domain rule is refused at their next authorization (the rule now needs a
verified address this provider can't give).

### Edge Cases

- Off and a userinfo answer saying `email_verified: true`: still unverified (the
  rule applies to the profile, whatever its source).
- Off and the provider sends no email: nothing changes, as today.
- Off and a domain rule (own or instance list): nobody can sign in through this
  connector; the screen says so.
- A refused add form (no choice) keeps every typed value but the secret, and
  draws no secret back.
- A row of kind `oidc` with NULL: off.

## Requirements

### Functional Requirements

- **FR-001** A connector of kind `oidc` has a setting, *trust the provider's
  verified emails*, on or off. No other kind has it.
- **FR-002** With it off, every profile from that connector reaches the domain
  rule, linking, the users row, the linked account and the session with
  `EmailVerified = false`; the address itself is kept.
- **FR-003** With it on, behaviour is today's.
- **FR-004** Schema 14 adds the column; the step sets it on for every existing
  `oidc` row and leaves it NULL for every other kind. NULL on an `oidc` row reads
  as off.
- **FR-005** The add form shows the two options, neither preselected. Without a
  choice the form is refused (422) with the reason under the group, every typed
  value kept but the client secret, and nothing secret drawn back.
- **FR-006** The connector's pane shows the setting with its current value and
  saves it with the other settings. A hint says that a change applies to the
  next sign-ins and unlinks nobody.
- **FR-007** A change is logged `connector_updated` with the detail
  `email_trust=on`, or `email_trust=off;lowered=<n>` where `n` is the number of
  users whose `email_verified` went down (joined with `;` to any other detail of
  the same save, as the store and the Microsoft audience are). A creation's
  `connector_created` detail carries it too.
- **FR-008** `EmailDomainRules.recheck` judges a remembered sign-in through a
  connector that does not trust its emails as having no verified domain.
- **FR-009** Turning trust off (on → off, or a NULL row saved off) lowers, in
  the transaction that writes the setting: (a) `linked_accounts.asserted_email_verified`
  to 0 for this connector's links (kind `oidc`, subject starting `<issuer>#`);
  (b) `users.email_verified` to 0 for those links' users, by the h762 rule (no
  other link verifies the stored address). A failure in either leaves the
  setting unchanged.
- **FR-010** Domain rule with trust off, the group, the hint and the
  confirmation: as [contracts/screens.md](contracts/screens.md) says.
- **FR-011** Docs: `docs/connectors/oidc.md` (the setting beside the email
  warning, in the forge section, and a fourth option in "Before you add a
  Forgejo or Gitea of your own": turn trust off); `docs/who-can-sign-in.md`
  where it describes linking or domain rules; the backup page's schema version
  if it names one; a `changelog.d/` file with the Upgrade note.

### Key Entities

See [data-model.md](data-model.md).

## Success Criteria

- **SC-001** Tests show, with trust off: a provider-verified address stored
  unverified (users row and link), not linked to an existing user with the same
  verified email, refused by a domain rule, `email_verified` false in the
  `id_token`; with trust on, today's behaviour.
- **SC-002** Turning trust off lowers a user verified only through this
  connector and keeps one also verified by another link; a failure while
  lowering leaves the setting unchanged; a NULL `oidc` row reads as off. The add form refused without a choice; the schema step sets on for
  existing `oidc` rows and NULL for others; the log detail on create and change;
  a refused save draws nothing secret back; the session re-check.
- **SC-003** The full suite is green.
- **SC-004** Design, documentation and code reviews clear.

## Out of Scope

- The switch for any other kind.
- Unlinking accounts already linked (the Troubleshooting page covers finding
  them).
