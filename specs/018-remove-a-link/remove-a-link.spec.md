# Feature Specification: Remove one linked provider account from a user

**Feature Branch**: `alta/quiet-memphis-783n` (worktree branch, stacked on spec 017's `alta/chipper-duet-8b42`; spec directory `specs/018-remove-a-link`)

**Created**: 2026-11-14

**Status**: Draft, for the design and code reviewers' answers before the build

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
2. **Sessions and refresh tokens are judged by the link they were made
   through.** A session cookie lasts 14 sliding days and a refresh token as long
   as an app keeps using it, and neither names the provider account today: the
   stranger would go on signing in as the user. The session gains a `link` claim
   and the authorization (so every code and refresh token) a private
   `umbral_link` claim: an opaque fingerprint of the provider account. The
   authorize and token endpoints refuse a session or a token whose link is no
   longer one of the user's, as they refuse one whose connector was deleted
   (task s1ug). Sessions and tokens issued before this version carry no
   fingerprint: they are judged by their connector instead (the user must still
   hold a link of that connector's kind, and for OpenID Connect of its issuer).
   The real person's own sessions and tokens are untouched.
3. **"The last way in" counts passkeys.** A user with one linked provider
   account and a passkey may lose the link; the passkey is still a way in. This
   is the rule a person removing their own passkey already meets
   (`Passkeys.removeOwnIfNotLast`), from the other side.
4. **It cannot be undone.** There is no screen to trust a removed account
   again. The confirmation says so.

## Clarifications

Open; to be answered by the designer (screen) and the code reviewer (decisions
above) before the build. See `contracts/screens.md` for the questions.

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
2. The stranger's next authorization with their existing Umbral session is
   refused and they are sent to the sign-in page; their app's refresh token is
   refused with `invalid_grant`.
3. The stranger signs in through the forge again: a new user is created with
   Alice's address stored unverified; Alice's user is unchanged; apps see a new
   `sub` for the stranger and the same one for Alice.
4. Alice signs in with GitHub as before, with the same `sub`; her session from
   before the removal still works.
5. The security log has `user_unlinked` (operator, Alice's id, the forge's
   slug, `kind=oidc`), then `sign_in_failed` with `link_removed;stage=authorize`
   for the stranger's session, then `user_created` with `reason=link_removed`.

### User Story 2: The provider is still trusted (Priority: P1)

The same as story 1 without turning trust off first, or with a built-in kind
(a recycled address at Google). The stranger's next sign-in still creates a new
user and does not join Alice again, though the provider still says the address
is verified.

### User Story 3: The last way in (Priority: P1)

Bob has one linked provider account and no passkey. His card shows no remove
button for it. A crafted POST is refused with the notice "That link is Bob's
only way to sign in. Delete the user instead, or invite them to add a passkey
first." (wording for the designer). Carol has one linked account and a
passkey: her card shows the remove button.

### User Story 4: The flag follows (Priority: P2)

Dave's stored address was verified only by the forge link. After the removal,
his `email_verified` is false (the h762 rule): his address can no longer join
anyone by email until a provider that verifies it signs him in. The event says
`lowered=1`.

### Edge Cases

- **Two accounts of one provider** linked to one user (two Microsoft tenants):
  each is its own row with its own remove button; the linked and last-used
  dates tell them apart. Sessions made through the other one are untouched
  (decision 2's fingerprint). A session from before this version through that
  provider survives, because its connector still backs a link: a known limit.
- **A removal while the stranger is mid-sign-in**: if their callback resolves
  after the removal, the record already applies (story 1.3); if before, the
  session it makes carries the removed link and the authorize it redirects to
  refuses it.
- **The link was already removed** (two tabs): "That link was already removed.
  Reload the page to see the user's current links."
- **The user was deleted meanwhile**: the existing `user-not-found` notice.
- **A disabled user**: links can be removed; nothing else changes.
- **The user's display name and picture** are whatever the last sign-in
  brought, possibly the stranger's; the real person's next sign-in refreshes
  them. Not changed by the removal.
- **An access token already issued** to the stranger lasts until it expires,
  as after **Disable** (OpenIddict's default, one hour). The app's next refresh
  is refused.
- **A link of a connector that was deleted**: still listed (labelled by the
  issuer's host for OpenID Connect, as today) and removable.
- **The provider account signs in again after its new user is deleted**: it
  gets another new user, still unverified. The record is permanent.

## Requirements

### Functional Requirements

- **FR-001**: Each linked provider account on a user's card is listed with its
  label (as today's pill), its linked date and its last-used date, and a remove
  button, unless it is the user's last way in (one link and no passkey). The
  layout and words are the designer's (`contracts/screens.md`).
- **FR-002**: The button posts to `/admin/users/{userId}/links/{fingerprint}/remove`
  with the antiforgery token, behind a confirmation naming the provider and the
  user and saying it cannot be undone. The fingerprint is the opaque value of
  FR-008; the page never carries a provider's subject.
- **FR-003**: The removal is one transaction: delete the link only if the user
  keeps a way in (another link or a passkey), in the DELETE's own condition so
  two removals cannot both succeed; record the provider account in
  `removed_links`; set `email_verified = 0` on the user where
  `Database.UnbackedEmailVerified` holds. Outcomes: removed (with whether the
  flag fell), last way in, not found.
- **FR-004**: The handler requires the operator session (`AdminAuth.requireOperator`)
  and validates the antiforgery token before reading the form. It redirects to
  `/admin/users` with a notice: `user-link-removed`, `user-link-last`,
  `user-link-not-found`, or the existing `user-not-found`.
- **FR-005**: A removal records `user_unlinked`: actor operator, outcome
  success, the user's id, the connector slug (the connector the screen labels
  the link with: for OpenID Connect the one holding its issuer, for another
  kind the only connector of that kind; none when there is none or more than
  one), detail `kind=<kind>`, with `;lowered=1` when the flag fell. Never the
  subject, the address or a display name. Refusals record nothing (they change
  nothing, like the passkey removal's).
- **FR-006**: `Callback.finishSignIn` treats a provider account recorded in
  `removed_links` as unverified, at the same place as spec 017's trust clamp
  and before everything that reads the profile. A user created by such a
  sign-in is recorded as `user_created` with detail `reason=link_removed`.
- **FR-007**: The end-user session made by a provider sign-in carries a `link`
  claim, and the authorization built from it a private `umbral_link` claim with
  no destination (never in an access token, id token or userinfo). Passkey
  sessions carry none.
- **FR-008**: The fingerprint is the unpadded base64url SHA-256 of the
  provider kind, a line feed and the provider subject. Pure, in `Domains`.
- **FR-009**: The authorize endpoint (on a remembered session) and the token
  endpoint (on a code or refresh token) refuse, after the connector-kind
  re-check and before the email domain rules: with a fingerprint, when the
  user holds no link with it; without one, when the connector is not the
  passkey connector and the user holds no link of its kind (for `oidc`, whose
  subject starts with the connector's issuer and `#`). The refusal is the
  existing one of each endpoint (session signed out and the request treated as
  signed out; `invalid_grant`), recorded as `sign_in_failed` with detail
  `link_removed;stage=authorize|token`.
- **FR-010**: Schema version 15 adds `removed_links (provider_kind,
  provider_subject, removed_utc)`, primary key `(provider_kind,
  provider_subject)`, no foreign key. Nothing is rewritten.
- **FR-011**: Documentation: `docs/connectors/oidc.md` "Changing the email
  setting later" points to the removal as the remedy and keeps Disable and
  Delete as the blunt ones; `docs/integrate.md` where it lists what the Users
  screen does; `docs/run/backup.md` schema 15; a changelog fragment (Added,
  Security, Upgrade).

### Key Entities

- **Removed link**: a provider account (kind and subject) the operator
  removed from a user. Its addresses are never trusted again here.

## Success Criteria

- **SC-001**: After a removal, the removed provider account's next sign-in
  creates a new user and never joins the user it was removed from, whether or
  not its provider still says the address is verified.
- **SC-002**: The removed account's existing session and refresh tokens are
  refused at their next use; the remaining links' sessions and tokens are not.
- **SC-003**: No request removes a user's last way in.
- **SC-004**: The security log shows who removed what kind of link from whom,
  with no subject or address.

## Out of Scope

- Trusting a removed account again, or showing removed accounts.
- Moving a link to another user, or joining two users.
- An end user removing their own links.
- Changing the user's stored address, name or picture.
- Removing passkeys (the card already does).
