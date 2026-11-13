# Documentation review: deleting or replacing a connector (task s1ug)

Branch `alta/brave-puffin-wbmj`, docs commit 69595db, read against the code at 5d09cb0, the
design note and `code-review.md`. Reviewed 2026-11-13 by the documentation reviewer.

## Verdict

**Not clear yet: three blocking items, three nits.** The three changed spots describe the code
correctly as far as they go. But one sentence promises more than the identity rules give. The
changelog's "replaced" covers only another provider. And the one page that tells operators to
delete a connector as a routine step (oidc.md) does not say what that now does to apps. One
re-check after the fixes.

## What was checked

- **The rule** against `ConnectorStore.recheckKind` and `Token.fs`. A token with a connector
  claim is refused when the slug is gone (`Deleted`) or names another kind (`OtherKind`), before
  the email-domain check. Re-adding the same slug and kind admits it again (test ``a refresh is
  honoured when its connector was deleted and added back under the same slug and kind``). A
  disabled connector still exists, so it is unchanged. Right.
- **The log detail** (`SecurityEvents.connectorChangedDetail`):
  `connector_changed;reason=deleted;stage=…` and
  `connector_changed;reason=other_kind;stage=…;kind=…`. The docs quote the prefixes (N2).
- **integrate.md** `/connect/token` row: right.
- **The changelog fragment** under Security: right, except "replaced" (B2).
- **The other pages** a reader would check, as you asked. The "Disabling or deleting a connector"
  lines in the walkthroughs are about stranded apps and stay true. passkey.md ("they work again if
  you enable the connector again"), the Fediverse and Apple delete lines and the
  who-can-sign-in.md "already signed in" paragraph stay true. oidc.md's "delete the connector and
  add it again" does not tell the whole story now (B3).
- **The delete confirmation** (`ConnectorDeleteConfirm`, `…Oidc`): still true. "Nobody can sign in
  with {name} until you add it again" now also covers refreshes (N3).
- **Links**: `check-links.py` reports 383 links in 38 files, all resolve. **Changelog**:
  `changelog.sh check` reports "in order".

## Blocking

### B1. Troubleshooting: "they come back as the same user" is true only through the same provider

The new entry says: "The app sends the person to sign in again, through a connector it offers;
their account is kept, so they come back as the same user." Accounts are keyed on the provider
kind and the provider's id for the person (`user_identities`, primary key
`(provider_kind, provider_subject)`; integrate.md "How people are identified"). Signing in again
through a connector of the **same provider** gives the same user. For an OpenID Connect provider,
it must also have the same issuer. Through **another** provider they are the same user only when a
verified email links them; otherwise they get a new user and a new `sub`. When the deleted
connector was the app's only one of that provider, the app now offers only other providers, so
"the same user" is the case the sentence gets wrong. Suggested:

> The app sends the person to sign in again. Their account is kept: signing in through a connector
> of the same provider (for OpenID Connect, the same issuer) returns them to the same user, with
> the same `sub`. Through another provider they are the same user only if a verified email links
> them (see [How people are identified](../integrate.md#how-people-are-identified)).

### B2. The changelog's "a deleted or replaced connector": replaced by another provider only

The fragment says "a refresh through a deleted or replaced connector answers `invalid_grant`".
`recheckKind` compares kinds only. A connector deleted and re-created under the same name **and
the same provider** carries the old sign-ins over and judges them by its own rules. For OpenID
Connect, that holds even with a different issuer and different allowed email domains. The code
review's N1 asks for exactly this to be written down. The Security section must not read as
broader than the fix. Say "a refresh through a deleted connector, or one whose name now belongs to
a connector of another provider, answers `invalid_grant`". Then add one sentence: "A connector
re-created under the same name and provider is treated as the same connector: sessions and
refresh tokens from before go on, judged by its settings."

### B3. oidc.md: "delete the connector and add it again" now affects apps

oidc.md (lines 198-202) tells the operator to delete and re-add the connector to change its
issuer or identifier, and promises that "Nobody loses their account". That is still true for
accounts. But it is the one place the docs make deleting a connector a routine step, so it must
say what happens to apps signed in through it. Add after "starts everyone as a new user.":

> Apps feel it: once the connector is deleted, a refresh through it answers `invalid_grant`, and
> the app sends the person to sign in again. Re-added under the **same identifier**, the old
> sessions and refresh tokens are accepted again, and they are judged by the new connector's
> settings, including its allowed email domains. That is so even if the issuer is different.
> Under a new identifier they are not, and everyone signs in once more.

This is also the right home for the code review's N1. The troubleshooting entry can link to it.

## Nits

1. **Say what disabling does.** The entry answers "I deleted a connector". The next question is
   "should I have disabled it?". One sentence: "Disabling a connector does not do this: it stops
   new sign-ins, and tokens through it still refresh."
2. **The full log detail.** The entry gives `connector_changed;reason=deleted` or
   `reason=other_kind`. The event's detail goes on with `;stage=token` or `;stage=authorize`, and
   with `;kind=<the provider it is now>` for `other_kind`. Write
   `connector_changed;reason=deleted;stage=…` and `…;reason=other_kind;stage=…;kind=…`, so a
   reader searching the log finds the line.
3. **The delete confirmation (optional).** It stays true. If you want it to warn about apps,
   append: "Apps signed in through it must sign people in again." It is the operator's last
   look before the act, but it is not required.
