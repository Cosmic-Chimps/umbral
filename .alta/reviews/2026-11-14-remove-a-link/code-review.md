# Code review, task 7t7k (spec 018, remove one linked provider account)

Reviewer: the `umbral` session. Range `3288b9f..dfba0fd` (code `da740e8..9472ef8`), read against
`specs/018-remove-a-link/` and `notes.md`.

**Verdict: not clear yet, one blocking item (the userinfo window) and two nits.** Everything the spec
rounds asked for is built as agreed. The blocking item is small.

## What I checked, and it holds

**Removal is one transaction** (`UserStore.removeLink`):
- Reads the user's links in the transaction and matches the fingerprint. No match is `LinkNotFound`,
  rolled back.
- Then the recursive delete of passkeys added through the link (`added_via = link:<fp>`, then
  `passkey:<id>` transitively, always `user_id = @userId`).
- Then the guarded link `DELETE`, whose own condition counts what remains (another link or a
  passkey).
- Zero rows rolls back the passkeys too. The test "refuses as last way in when the only passkey was
  added through the link" pins it, with the passkey still there afterwards.
- Then `removed_links`, `legacy_sessions_ended`, and the h762 lowering through
  `Database.UnbackedEmailVerified`, all before the commit.
- `BeginTransaction()` on Microsoft.Data.Sqlite is `BEGIN IMMEDIATE`, so two concurrent removals
  serialise rather than racing between the read and the delete.

**Credential check** (`credentialStillHeld`):
- `link:<fp>` must be one of the user's links now.
- `passkey:<id>` matches on `id AND user_id`.
- Anything that doesn't parse, an empty value included, is refused.
- No reference: a user recorded in `legacy_sessions_ended` is refused first, for every kind, the
  passkey connector included. Otherwise it falls back to the connector, as specified.
- No slug never reaches here: `EndUserSession.recheck` refuses it first as `Unrecorded`.

**One session check** (`EndUserSession`):
- `recheck` (s1ug kind re-check, then the credential check) is used by authorize and by token.
- `current` (user exists, is enabled, then `recheck`, signing out on any refusal) is used by
  `/account/passkeys`, its remove post, Add start, and Add finish. Add finish judges the finishing
  request and requires the ceremony's user. `added_via` is that request's session reference.

**Claims:**
- `Sessions.signIn` takes the credential as a required `string`. The three callers pass
  `link:<fp>` (Callback) or `passkey:<id>` (assertion, registration).
- `umbral_credential` has no destination, so it never reaches the token response, id_token,
  userinfo or a refresh response (`UserLinkSignInTests`).
- It survives a refresh: `UserLinkRecheckTests` refreshes before the removal, and the token from
  that refresh is then refused.

**Callback:** `isRemoved` is read beside 017's trust clamp, keyed the way `UserStore.resolve` keys
links. `user_created` carries `reason=link_removed`.

**Passkey removals:** both the operator's and the owner's record the user in
`legacy_sessions_ended` in their own transaction.

**Admin handler** (`UserLinks.handler`):
- `requireOperator` and `readValidated` (antiforgery) come before the form is read.
- The first post only redraws, at 422. Only `confirm=remove` removes.
- The event carries the slug and `kind=;passkeys=;lowered=`, never a subject or address.
- Refusals record nothing.

Run here, filtered (UserLink*, Passkey*, DatabaseTests, LinkingRule, SecurityEventCoverage,
ConnectorDelete): 326/326 passed. The implementer reports the full suite at 2891/2891 at `9472ef8`.
After that the only code is `eb38935` (`Users.fs` drawing and CSS, the design nits), which I read
and which changes no behaviour, so this review covers `58a3ff1`.

## Blocking

**B1. The access-token window is not "as after Disable", and it can be closed cheaply.**
- The spec's known limit says that an access token already issued to the stranger "lasts until it
  expires, as after Disable (one hour); userinfo serves it until then". That's wrong for Disable:
  `UserInfo.fs` reads the user fresh and returns 401 for a disabled user.
- After a removal, userinfo goes on serving the user's name, picture and email to the stranger's
  token for up to an hour.
- Your own note settles the introspection question:
  - access tokens are reference tokens (`OpenIddictSetup.fs:355`), so their payload stays in
    Umbral's database;
  - apps are not granted the introspection permission (`AppClients.fs:22`).

  So an app never sees what an access token carries, and userinfo is the only place an access
  token is accepted.

Fix:
- Give `umbral_credential`, the connector claim and the idp claim the `access_token` destination
  (still never `id_token`).
- In `UserInfo`, after the enabled check, run `EndUserSession.recheck` (stage `userinfo`) and
  return 401 `invalid_token` on a refusal.
- That closes s1ug's window at userinfo too.
- Tests:
  - after `removeLink`, the stranger's access token gets 401 at userinfo while the remaining
    link's token still gets 200;
  - none of the three claims appears in the token response or the id_token;
  - an access token from before the upgrade (no claims) for a recorded user gets 401.
- Then drop the limit from the spec and the docs.

If you'd rather not, the minimum is to correct the limit's text. It must not say "as after
Disable". The docs should say that userinfo serves the stranger's existing access token for up to
an hour after the removal.

## Nits

**N1.** A pre-upgrade (claimless) session that adds a passkey after the upgrade stores it with a
NULL `added_via`. So a link removal doesn't take it, and the last-way-in guard counts it. The
confirm block's "check these" list (NULL origin, added after the link) does show it, so the
operator can see it. Say in the spec's edge cases that this is how such a passkey is caught.

**N2.** A refused Add start records two `sign_in_failed` events (in notes.md). Pass the session
check's verdict through so that `refuse` records nothing for `Refused`, because `current` already
did. Not required.

## Re-check

One re-check after B1. For the code, I'll read only the diff since `dfba0fd`.
