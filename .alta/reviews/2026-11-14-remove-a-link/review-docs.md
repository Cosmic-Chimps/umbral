# Documentation review: Remove link (spec 018, task 7t7k)

Branch `alta/quiet-memphis-783n`, docs commit 7ba2b7a, read against the spec (decisions 1–4,
FR-001..FR-012, the edge cases), `contracts/screens.md` and the code built so far (up to 38f19a1:
`Callback.fs`, `EndUserSession.fs`, `UserStore.credentialStillHeld`, `Authorize.fs`). The screen
itself is not built yet, so its words were checked against the contract only. Reviewed 2026-11-14
by the documentation reviewer.

## Verdict

**Not clear yet: one blocking item, three nits, and an answer to the code reviewer's question.**
The pages describe the feature well and in the right places. The blocking item is that they promise
the removed person is signed out, without the limit for sessions from before this version, and that
limit hits the very case the feature was built for. One re-check after the fix, ideally once the
screen exists, so the quoted words can be checked against `Texts.fs`.

## What was checked

- **Removed accounts read unverified** at sign-in (`Callback.fs:70-76`, `UserStore.isRemoved`), so
  their next sign-in is a new user that joins nobody by email. That is decision 1 and FR-006. Right.
- **The session check** (FR-009, FR-012): `EndUserSession.recheck` runs at authorize, and a refusal
  treats the person as signed out. With a credential reference it compares by fingerprint or by
  passkey id and user. Right, apart from B1 (sessions with no reference) and N1 (what authorize
  answers).
- **Passkeys go with the link, transitively; older ones have no recorded origin and are listed**
  (decision 3). **The flag is re-judged by the h762 rule** (FR-003). **No undo** (decision 4).
  **The last way in cannot be removed.** All as the spec says.
- **The screen words** quoted (**Linked accounts**, **Remove link**, linked and last-used dates):
  as in `contracts/screens.md`.
- **The changelog's Upgrade bullet** states the limits for sessions from before this version
  exactly as `credentialStillHeld` implements them. B1 is that only the changelog says so.
- `check-links.py`: 408 links in 39 files, all resolve. `changelog.sh check`: in order.

## Blocking

### B1. "Signed out … at the next request" is not true for sessions from before this version

oidc.md says the other person "is signed out of Umbral and of every app at the next request or
token refresh". integrate.md says removing a link "ends everything the removed account signed in
to". Sessions and refresh tokens from before schema 15 carry no credential reference, and
`credentialStillHeld` judges them by connector:

- **through the passkey connector**: always admitted (line 501). A stranger who added a passkey
  while joined, before the upgrade, keeps their pre-upgrade passkey session (14 sliding days) and
  its refresh tokens. That holds after you remove the link **and** that passkey (the passkey has no
  recorded origin, so it is not removed with the link, and removing a passkey does not end
  claimless sessions);
- **through a provider**: admitted while the user still holds any link through that provider (for
  OpenID Connect, that issuer).

The takeover this feature was built for (an open forge while trust was on, spec 017) happened, by
definition, before this version. So the limit is the main case, not an edge case. The changelog's
Upgrade bullet says it. The two pages where an operator acts on it do not.

Say it in both places. In oidc.md, after "…becomes a separate user the next time they sign in.":

> If the wrong link was made before you upgraded to this version, sessions and refresh tokens from
> before the upgrade do not record how they were made. Removing the link ends those only when the
> user has no other link through the same provider. One made with a passkey is not ended, even if
> you remove that passkey. See [When a link is removed](../integrate.md#when-a-link-is-removed).

In integrate.md, add a bullet or a short paragraph under "Removing a link" with the same facts.
State what the operator can do about it, if anything, once the maintainer has decided (see below).

**For the maintainer and the code reviewer (a decision, not a docs wording):** the clean fix is in
code. When a link is removed, refuse that user's sessions and tokens that carry no credential
reference. The real person then signs in once more, and the stranger's pre-upgrade way in is
closed. If that is taken, B1 shrinks to one sentence ("sessions from before this version end too:
the person who stays signs in again once").

## Nits

1. **What authorize answers.** integrate.md says an authorization request "comes back with
   `error=login_required` (or goes to the sign-in page)". It is the other way round. A refused
   session is treated as signed out, so the request goes to the sign-in page, and only with
   `prompt=none` does it come back with `error=login_required` (`Authorize.fs:446-457`). Say "goes to
   the sign-in page, or, with `prompt=none`, comes back with `error=login_required`".
2. **"Your app sees no change for them"** is too strong. Two bullets later, `email_verified` may fall
   to `false`, and the display name and picture stay whatever the last sign-in brought, possibly the
   stranger's, until the real person signs in again (spec edge case). Say "keeps the same `sub`",
   and add one clause about the name and picture.
3. **The wrong link removed.** Add what the operator can usefully do: tell the app owners the old
   and the new `sub`, so an app that can move a person's data does it. That pairs with "tell whoever
   runs each app" two paragraphs later.

## The code reviewer's question: should the docs describe a manual way back?

**No.** The permanent record in `removed_links` is the security property: it is what stops the
account from being joined to someone by email again. A documented way around it, such as a database
edit, would be the one instruction an operator could follow to undo the protection, possibly for the
stranger's account rather than the owner's. The docs should keep saying it cannot be undone, say what
it costs the owner (as they do), and add N3. If a way back is ever wanted, it belongs in the product,
behind its own confirmation, not in a how-to.

## Re-check at dfba0fd (2026-11-14)

**Clear to merge.** B1 and the three nits are fixed (`cf35984..dfba0fd`: docs and spec 628bcb5,
b7fb2d4; code 9472ef8; screen 0786250).

- **B1**, now in code as I suggested: every removal of a sign-in method (a link, or a passkey by the
  operator or its owner) records the user in `legacy_sessions_ended` (`UserStore.fs:349`,
  `Passkeys.fs:258`). `credentialStillHeld` refuses a recorded user's claimless sessions and tokens
  first, for every connector kind, the passkey one included (lines 517-525). oidc.md, integrate.md (its
  own bullet), passkey.md and the changelog's Upgrade bullet each say it in one sentence, including
  that the person who stays signs in again once. Right. The `None, None` branch (no reference and no
  connector) still admits, but authorize and token refuse such a session earlier as `Unrecorded`
  (task s1ug), so it is not reachable from them.
- **N1**: the sign-in page, or `error=login_required` with `prompt=none`. Right.
- **N2**: "keeps their `sub`", and the name and picture clause. Right.
- **N3**: tell the app owners the old and the new `sub`. Right.
- **Screen words** against `Texts.fs`: **Linked accounts** (`UserLinksLabel`) and **Remove link**
  (`UserLinkRemoveButton`) are verbatim. The confirm block says it cannot be undone
  (`UserLinkCannotUndo`), and the last-way-in refusal offers "Delete the user, or invite them to add
  a passkey first", as integrate.md does. Right.
- `check-links.py`: 408 links resolve. `changelog.sh check`: in order.

One tidy-up, not a condition: 628bcb5 left `docs/connectors/oidc.md`, `docs/connectors/passkey.md`,
`docs/integrate.md` and `changelog.d/018-remove-a-link.md` without a final newline (the diff shows
"No newline at end of file"). Add it back so the next edit's diff stays clean.

## Addendum: 69bd10a, after the re-check (2026-11-14)

**Still clear.** The code review's B1 made userinfo judge an access token (FR-014). Four docs
changed: integrate.md, oidc.md, run/troubleshooting.md and the changelog. Checked against the code:

- The connector and credential claims now go to the access token, and never the identity token
  (`destinationsFor`), so userinfo tells the stranger's token from the real person's. The test
  ``after a removal the stranger's access token is refused at userinfo and the remaining link's
  access token is served`` (`UserLinkRecheckTests.fs:714`) says so. "Refused at userinfo from then on
  (`401` with `invalid_token`), as after disabling someone" is right, and so is the deleted-connector
  wording in oidc.md and troubleshooting.md.
- An access token from before this version carries neither claim and is refused once the user is
  recorded in `legacy_sessions_ended`, for both people. That matches integrate.md's bullet on older
  sessions ("for both people").
- The design keeps those claims away from apps only while no app may call introspection, which
  integrate.md already states ("no app is allowed to call it: it answers `unauthorized_client`").
  That sentence now carries a security property: keep it true, and keep it in mind if introspection
  is ever opened to apps.
