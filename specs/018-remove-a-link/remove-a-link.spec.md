# Feature Specification: Remove one linked provider account from a user

**Feature Branch**: `alta/quiet-memphis-783n` (worktree branch, stacked on spec 017's `alta/chipper-duet-8b42`; spec directory `specs/018-remove-a-link`)

**Created**: 2026-11-14

**Status**: Clarified (design and code reviewers, 2026-11-14 at 0057780); in build

**Input**: Task `2026-11-14-users-screen-remove-one-wrong-linked-provider-account-withou-7t7k`.

## Background

Umbral joins a provider account to an existing user when the provider vouches
for the user's stored address (FR-012 of spec 001, `Domains.Linking`). When a
provider vouches wrongly, a stranger is joined into someone's user and signs in
as them in every app:

- an open forge that marks every address verified, while it was **Trusted**
  (spec 017; the forge finding f0rg);
- a recycled address: a new employee gets a former employee's address at a
  provider that verifies it;
- two people sharing one inbox.

Today the Users screen offers only **Disable** and **Delete**. Disable stops
both people, because both sign in as that user. Delete removes every link, so
the real person comes back as a new account and every app sees a new `sub`.
The documentation can only describe these blunt remedies (spec 017's docs
review, `.alta/reviews/2026-11-13-email-trust/review-docs.md`).

This feature lets the operator remove the one wrong link. It decides who is
recognised as whom, nothing about what anyone may do (Principle VII).

## Decisions Already Taken

From the task; not open:

- The action is on the Users screen, on the user's card, per linked provider
  account, behind a confirmation.
- It is refused for the user's last way in.
- The removed provider account becomes a stranger to Umbral: a new user at its
  next sign-in.
- One security event, `user_unlinked`, with the connector slug and never the
  provider's subject or an address.
- The user's stored `email_verified` is judged again by the h762 rule
  (`Database.UnbackedEmailVerified`) after the removal.

## Decisions taken in this spec (for the code reviewer)

These follow from "becomes a stranger", which the task asks for. Without them
the removal would not do what it says.

1. **The removed provider account's addresses are never trusted again on this
   instance.** Deleting the link row alone is undone at the next sign-in: the
   provider still vouches for the same address, and linking by email joins the
   account to the same user again. So the removal records the provider account
   (kind and subject) in a new table, `removed_links`. A sign-in from a recorded
   account is treated like a sign-in through a connector whose trust is off
   (spec 017): its `email_verified` is false before the domain rule, linking,
   the users row and the session. It therefore joins nobody by email, its own
   new user stores the address unverified, it never meets an allowed email
   domains rule, and apps get `email_verified: false` for it. The record is not
   tied to the user it was removed from and outlives it: if it were deleted with
   that user, the stranger's own user would regain a verified copy of the real
   person's address, and the real person's next new provider would join the
   stranger.
2. **Sessions and refresh tokens are judged by the credential they were made
   with** (a provider link or a passkey). A session cookie lasts 14 sliding
   days and a refresh token as long as an app keeps using it, and neither names
   the credential today: the stranger would go on signing in as the user. Every
   end-user session gains a `credential` claim and the authorization (so every
   code and refresh token) a private `umbral_credential` claim, holding a
   credential reference: `link:<fingerprint of the provider account>` for a
   provider sign-in, `passkey:<passkey row id>` for a passkey sign-in, sign-up
   or invitation. The authorize and token endpoints refuse a session or a token
   whose credential is no longer one of the user's, as they refuse one whose
   connector was deleted (task s1ug). Sessions and tokens issued before this
   version carry no reference: they are judged by their connector instead (the
   user must still hold a link of that connector's kind, and for OpenID Connect
   of its issuer; a passkey one is admitted). The real person's own sessions
   and tokens are untouched. A side effect, wanted: removing a passkey (by the
   operator or its owner) now also ends the sessions and refresh tokens made
   with it, which spec 008 and 011 did not.
3. **The stranger's passkeys go with the link** (code review before the build,
   blocking). Signed in as the user, the stranger can add a passkey of their
   own on `/account/passkeys`; removing only the link would leave them that way
   in, and would even count it as the user's "other way in". So each passkey
   records the credential reference of the session that added it
   (`passkeys.added_via`; NULL for an invitation, a sign-up and every passkey
   from before this version). The removal deletes, in the same transaction, the
   passkeys added through the removed link and, transitively, those added
   through them. **The last way in** is then counted on what remains: another
   link or a remaining passkey. Passkeys with no recorded origin that were added
   after the link was made are shown in the confirmation with their dates, so
   the operator can judge them with the existing passkey removal.
4. **It cannot be undone.** There is no screen to trust a removed account
   again. The confirmation says so.
5. **A removal ends the user's sessions from before this version** (docs
   review, B1). Sessions and refresh tokens issued before schema 15 carry no
   credential reference, and the fallback by connector admits them while the
   user holds another link through that provider, and always through the
   passkey connector. The takeover this feature answers happened, by
   definition, before this version, so that fallback would leave the main case
   open. So every removal of a sign-in method (a link here; a passkey by the
   operator or its owner) records the user in `legacy_sessions_ended`, and
   from then on a session or token of that user with no credential reference is
   refused. The person who stays signs in once more; sessions made after the
   upgrade are judged by their own reference, as before.

## Clarifications

### Session 2026-11-14

- **Screen** (`umbral-designer`): a "Linked accounts" list above the passkeys,
  drawn only when a link can be removed, with the dates; "Remove link" as a red
  ghost with a confirm, and a sentence added to DESIGN.md's One Red Fill rule
  for row actions that cannot be undone; the refusals in Warning alerts (and
  `user-not-found` with them). Words in `contracts/screens.md`.
- **Security decisions** (`umbral`, code reviewer, before the build): 1, 2
  and 4 sound. Blocking on 3: the stranger can add a passkey of their own while
  signed in as the user, and passkey sessions carried no credential claim.
  Answered by decision 3 as now written (`added_via`, the transitive removal,
  last way in counted on what remains) and by widening decision 2 to every
  end-user session (`link:` or `passkey:` references). Taken from the same
  review: name the refresh race and the access-token window as known limits;
  the fingerprint is opaque, not secret; the docs say what a removal costs the
  real owner if it was the wrong link, and to tell the app owners.
- **The passkey pages** (`umbral`, re-read of 126f1c0, blocking): FR-009
  checked the credential only at authorize and token, but `/account/passkeys`
  (`Account.currentUser`) and the passkey ceremony of purpose `Add` (start
  and finish) accept any session whose user exists and is enabled. The
  stranger's old cookie could add a fresh passkey there after the removal, and
  remove the owner's. Answered by FR-012: one end-user session check, used by
  all four places. `passkey:<id>` matches on the id **and** the user.
  Re-read at 0057780: clear from the code reviewer's side. The email domain and
  server rules stay at authorize and token only (managing one's own passkeys
  is not signing in to an app; a passkey added by someone a rule refuses is
  still judged by that rule at authorize).
- **The confirmation** (`umbral-designer`, second answer): the server's
  confirm block, not a browser dialog. One dialog cannot carry the passkey
  lists readably, and a `data-confirm` asks nothing without JavaScript, which
  matters now that the removal takes passkeys with it. Words in
  `contracts/screens.md`.
- **UserInfo** (reviewer's suggestion, not taken): giving the credential claim
  the access-token destination would let an app read it at
  `/connect/introspect`, which returns an access token's claims to its
  client. An access token already issued stays valid until it expires (one
  hour): a known limit, as after **Disable** and in task s1ug.

## User Scenarios & Testing

### User Story 1: A stranger joined through an open forge (Priority: P1)

Alice has signed in with GitHub for months. The operator added a forge as an
OpenID Connect provider, **Trusted**. A stranger registered at the forge with
Alice's address and signed in: Umbral joined them to Alice. The operator turns
trust off (spec 017), then opens **Users**, finds Alice with GitHub and the
forge, and removes the forge link.

**Acceptance**:

1. Alice's card lists the forge link with its linked date; pressing its remove
   button asks for confirmation; confirming removes it, and the card shows
   GitHub only, with the notice "Link removed."
2. The passkey the stranger added while signed in through the forge is removed
   with the link, and so is any passkey added while signed in with that one.
   Its sessions and refresh tokens are refused too.
3. The stranger's next authorization with their existing Umbral session is
   refused and they are sent to the sign-in page; their app's refresh token is
   refused with `invalid_grant`.
4. The stranger signs in through the forge again: a new user is created with
   Alice's address stored unverified; Alice's user is unchanged; apps see a new
   `sub` for the stranger and the same one for Alice.
5. Alice signs in with GitHub as before, with the same `sub`; her session from
   before the removal still works.
6. The security log has `user_unlinked` (operator, Alice's id, the forge's
   slug, `kind=oidc;passkeys=1`), then `sign_in_failed` with `credential_removed;stage=authorize`
   for the stranger's session, then `user_created` with `reason=link_removed`.

### User Story 2: The provider is still trusted (Priority: P1)

The same as story 1 without turning trust off first, or with a built-in kind
(a recycled address at Google). The stranger's next sign-in still creates a new
user and does not join Alice again, though the provider still says the address
is verified.

### User Story 3: The last way in (Priority: P1)

Bob has one linked provider account and no passkey. His card shows no list. A
crafted POST is refused with the notice "That is Bob's only way to sign in, so
it was not removed. Delete the user, or invite them to add a passkey first."
Carol has one linked account and a passkey added through an invitation: her
card shows the list, and the removal goes through. Erin has one linked account
and a passkey added while signed in through it: the removal would take both, so
it is refused as her last way in.

### User Story 4: The flag follows (Priority: P2)

Dave's stored address was verified only by the forge link. After the removal,
his `email_verified` is false (the h762 rule): his address can no longer join
anyone by email until a provider that verifies it signs him in. The event says
`lowered=1`.

### Edge Cases

- **Two accounts of one provider** linked to one user (two Microsoft tenants):
  each is its own row with its own remove button; the linked and last-used
  dates tell them apart. Sessions made through the other one are untouched
  (decision 2's reference). Sessions from before this version end with any
  removal (decision 5).
- **Passkeys from before this version** have no recorded origin, so they stay.
  Those added after the link was made are named in the confirmation with their
  dates; the operator removes them with the card's passkey Remove if they are
  not the real person's. Their sessions from before this version end with the
  removal anyway (decision 5).
- **A refresh already past the check** when the removal commits gets one more
  token, refused at its next use: a known limit.
- **Removing a passkey** (operator or owner) now ends the sessions and refresh
  tokens made with it, the owner's current one included if it is that passkey.
  It does not remove the passkeys added through it (only a link removal is
  transitive).
- **A removal while the stranger is mid-sign-in**: if their callback resolves
  after the removal, the record already applies (story 1.3); if before, the
  session it makes carries the removed link and the authorize it redirects to
  refuses it.
- **The link was already removed** (two tabs): "That link was already removed."
- **The user was deleted meanwhile**: the existing `user-not-found` notice.
- **A disabled user**: links can be removed; nothing else changes.
- **The user's display name and picture** are whatever the last sign-in
  brought, possibly the stranger's; the real person's next sign-in refreshes
  them. Not changed by the removal.
- **An access token already issued** to the stranger lasts until it expires,
  as after **Disable** (OpenIddict's default, one hour); userinfo serves it
  until then. The app's next refresh is refused.
- **The wrong link removed** (the real person's): there is no way back. Their
  next sign-in through that account makes a new user with the address
  unverified, and apps see a new `sub`. The confirmation and the docs say so.
- **A link of a connector that was deleted**: still listed (labelled by the
  issuer's host for OpenID Connect, as today) and removable.
- **The provider account signs in again after its new user is deleted**: it
  gets another new user, still unverified. The record is permanent.

## Requirements

### Functional Requirements

- **FR-001**: When a user has two or more ways in (links plus passkeys), the
  card lists each linked provider account with its label (as today's pill), its
  linked date and its last-used date, and a remove button. The layout and words
  are the designer's (`contracts/screens.md`).
- **FR-002**: The row's button posts to
  `/admin/users/{userId}/links/{fingerprint}/remove` with the antiforgery token
  and no `confirm` field. That post changes nothing: the server redraws the
  Users page at 422 with that card's row expanded into a confirm block (the
  product's pattern, as Remove passkey on the Credential screen and Turn trust
  off), which names the provider and the user, lists the passkeys that go with
  the link (decision 3) and the user's passkeys with no recorded origin added
  after the link was made, with their dates, and says it cannot be undone. Its
  Danger button posts the same address with `confirm=remove`; only that post
  removes. Focus goes to the block. The fingerprint is the opaque value of
  FR-008; the page never carries a provider's subject.
- **FR-003**: The removal is one transaction: delete the passkeys added through
  the link, transitively (a recursive query on `added_via`); delete the link
  only if the user keeps a way in (another link or a remaining passkey), in the
  DELETE's own condition so two removals cannot both succeed, and roll
  everything back when it does not; record the provider account in
  `removed_links`; set `email_verified = 0` on the user where
  `Database.UnbackedEmailVerified` holds. Outcomes: removed (with whether the
  flag fell and how many passkeys went), last way in, not found.
- **FR-004**: The handler requires the operator session (`AdminAuth.requireOperator`)
  and validates the antiforgery token before reading the form. It redirects to
  `/admin/users` with a notice: `user-link-removed`, `user-link-last`,
  `user-link-not-found`, or the existing `user-not-found`.
- **FR-005**: A removal records `user_unlinked`: actor operator, outcome
  success, the user's id, the connector slug (the connector the screen labels
  the link with: for OpenID Connect the one holding its issuer, for another
  kind the only connector of that kind; none when there is none or more than
  one), detail `kind=<kind>;passkeys=<n>`, with `;lowered=1` when the flag fell. Never the
  subject, the address or a display name. Refusals record nothing (they change
  nothing, like the passkey removal's).
- **FR-006**: `Callback.finishSignIn` treats a provider account recorded in
  `removed_links` as unverified, at the same place as spec 017's trust clamp
  and before everything that reads the profile. A user created by such a
  sign-in is recorded as `user_created` with detail `reason=link_removed`.
- **FR-007**: Every end-user session carries a `credential` claim with a
  credential reference: `link:<fingerprint>` after a provider sign-in,
  `passkey:<passkey row id>` after a passkey sign-in, sign-up or invitation.
  The authorization built from it carries a private `umbral_credential` claim
  with the same value and no destination (never in an access token, id token,
  userinfo or introspection response). A passkey added on `/account/passkeys`
  stores the reference of the session that added it in `passkeys.added_via`.
- **FR-008**: The fingerprint is the unpadded base64url SHA-256 of the
  provider kind, a line feed and the provider subject. Pure, in `Domains`.
  It is opaque, not secret: the subject is often public.
- **FR-009**: The authorize endpoint (on a remembered session) and the token
  endpoint (on a code or refresh token) refuse, after the connector-kind
  re-check and before the email domain rules: with a reference, when the user
  holds no link with that fingerprint, or no passkey with that id belonging to
  that user (never matched on the id alone); without one,
  when the connector is not the passkey connector and the user holds no link
  of its kind (for `oidc`, whose subject starts with the connector's issuer
  and `#`). The refusal is the existing one of each endpoint (session signed
  out and the request treated as signed out; `invalid_grant`), recorded as
  `sign_in_failed` with detail `credential_removed;stage=authorize|token`.
- **FR-012**: One end-user session check (`Infrastructure/EndUserSession.fs`)
  judges a remembered session wherever one is accepted: the authorize endpoint,
  `/account/passkeys` (the page and its remove post) and the passkey ceremony
  of purpose `Add`, at its start and again at its finish (the ceremony lasts
  five minutes and can straddle a removal; the finish never trusts the start's
  verdict). It runs the connector-kind re-check of task s1ug, then FR-009's
  credential check. On a refusal it signs the session out, records
  `sign_in_failed` with the FR-009 detail or s1ug's `connector_changed`
  detail, and the caller treats the person as signed out: authorize as today,
  the passkey page by sending them to sign in, the ceremony by refusing
  (nothing stored). The account pages also refuse a session whose connector
  was deleted or replaced, a case task s1ug left open there. The email domain
  and server rules stay where they are (authorize and token only): they decide
  who may sign in to an app, not who may manage their own passkeys.
- **FR-010**: Schema version 15 adds `removed_links (provider_kind,
  provider_subject, removed_utc)`, primary key `(provider_kind,
  provider_subject)`, no foreign key; `passkeys.added_via TEXT NULL`; and
  `legacy_sessions_ended (user_id, ended_utc)`, primary key `user_id`,
  foreign key to `users` with `ON DELETE CASCADE`. Nothing is rewritten.
- **FR-013**: `removeLink`, `Passkeys.remove` (the operator's) and
  `Passkeys.removeOwnIfNotLast` (the owner's) record the user in
  `legacy_sessions_ended` in the same transaction as the removal (insert or
  ignore). `credentialStillHeld` with no reference refuses a user recorded
  there, before the connector fallback and for the passkey connector too.
- **FR-011**: Documentation: `docs/connectors/oidc.md` "Changing the email
  setting later" points to the removal as the remedy and keeps Disable and
  Delete as the blunt ones; `docs/integrate.md` where it lists what the Users
  screen does; `docs/run/backup.md` schema 15; a changelog fragment (Added,
  Security, Upgrade). The docs say what a wrong removal costs the real owner,
  that removing a passkey now ends its sessions, and that the stranger may
  already have data at apps under the user's `sub`, which only the app owners
  can deal with.

### Key Entities

- **Removed link**: a provider account (kind and subject) the operator
  removed from a user. Its addresses are never trusted again here.

## Success Criteria

- **SC-001**: After a removal, the removed provider account's next sign-in
  creates a new user and never joins the user it was removed from, whether or
  not its provider still says the address is verified.
- **SC-002**: The removed account's existing session and refresh tokens, and
  those of the passkeys added through it, are refused at their next use, at an
  app and on the account's passkey pages; the remaining credentials' sessions
  and tokens are not.
- **SC-003**: No request removes a user's last way in.
- **SC-004**: The security log shows who removed what kind of link from whom,
  with no subject or address.

## Out of Scope

- Trusting a removed account again, or showing removed accounts.
- Moving a link to another user, or joining two users.
- An end user removing their own links.
- Changing the user's stored address, name or picture.
- Removing passkeys on their own (the card already does), or making that
  removal transitive.