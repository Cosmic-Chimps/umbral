# Code review, task tr5t (spec 017, OIDC email trust)

Reviewer: the `umbral` session. Range `8a1d88c..c971183` (code `37663bf..eb9b894`), read against
`specs/017-oidc-email-trust/` and `notes.md`.

**Verdict: the code is clear, with no blocking items and three nits.** The task as a whole is not
clear to merge yet. The design review (`review.md`, one blocking) and the documentation review
(`review-docs.md`, B1) still have open items, and both need a re-check.

## The checklist from the task

**1. Off can never produce a verified address.** Checked, and it holds.

- `Callback.finishSignIn` clamps `EmailVerified` to false in its first lines, before anything
  reads the profile.
- All three callers go through it: `Callback.fs:293` (OAuth/OIDC), Fediverse and Bluesky.
- `UserStore.resolve` is called only from `finishSignIn` (`Callback.fs:101`). So the link's
  `asserted_email_verified`, the new user's flag, the raise in `UPDATE users` (UserStore.fs:178,
  needs `@verified = 1`) and linking by email (UserStore.fs:98, needs `profile.EmailVerified`)
  all see false.
- The session's admitted domain comes from the same profile.
- Apps get the flag from the stored user. `off: the id_token and userinfo … false` pins that.
- The adapter is untouched, so a userinfo `email_verified: true` cannot get around the clamp.
- `trustsProviderEmail` returns false for an `oidc` row read as `None`, so NULL fails closed.
  `read` reads the column only for `Oidc` rows. `emailTrustParameter` writes NULL for every
  other kind.

**2. Schema 14.**

- The step adds the column and sets 1 on `oidc` rows only. The `CREATE TABLE` has the column.
- `SchemaVersion` is 14, and the step is in `upgradeSteps`.
- Step 9 is rebuilt from `UnbackedEmailVerified`, and `upgrade step 9 is byte-identical …` pins
  the shipped text.
- Nothing else writes the column. The `save` upsert's `ON CONFLICT DO UPDATE` list leaves
  `email_trust` alone, so no generic save can switch trust off without the lowering or switch it
  on without the pane.
- Every other `UPDATE connectors` (`update`, `saveMicrosoft`, the secret moves, enable/disable)
  leaves it alone too.

**3. Lowering, in one transaction.**

- `saveOidc` reads the row, updates it, and lowers links and then users in the same transaction.
- `readOidcTrust` is in the transaction, so the stored value it compares against cannot be stale.
- The order is right: links go first, so `UnbackedEmailVerified` no longer counts them as backing.
- The prefix is `<issuer>#`, which matches `oidcSubject` (Oidc.fs:541 builds it from the stored
  `IssuerUrl`). `issuer_url` is unique (`ux_connectors_issuer`), so one connector's lowering
  cannot touch another's links. `#` cannot occur in an issuer, so one issuer that is a prefix of
  another (`https://a` and `https://a/t`) does not match.
- The trigger test shows that a failure leaves the setting and every flag unchanged.
- `previewTrustOff` runs the same statements and rolls back.

**4. Re-check of remembered sign-ins.**

- `EmailDomainRules.recheck` judges an untrusted connector as `checkDomain rule None`, reported
  as `EmailNotVerified`.
- A deleted connector keeps `trusted = true`. That is safe, because `recheckKind` (s1ug) refuses
  it first.
- Tests cover a session, a refresh token, the instance list, and no rule (allowed).

**5. Handler.**

- On the add form a missing choice is refused, and so is an own list with trust off.
- The pane refuses any value other than `on` or `off`, keeps the stored value when the field is
  absent, and refuses an own list with trust off, including a list that is stored but not retyped.
- Turning trust off from on or NULL waits for `confirm=trust-off` behind the antiforgery token.
- The log details match the data model. `lowered=<n>` is also logged for a NULL row saved off,
  which is a deviation I accept: a lowering never goes unlogged.

Run here: the spec 017 store, sign-in and admin tests plus `DatabaseTests` and the email-domain
tests, filtered: 174/174 passed at `0a41312`. The implementer reports the full suite at
2825/2825 at `eb9b894`.

## Nits

**N1. A sign-in in flight when trust is turned off can still raise the flag.**
`Callback.fs:193` reads the connector, then the code exchanges the code with the provider, then
`finishSignIn` writes. If the operator's save commits during that exchange, the sign-in finishes
with the trust that was read before. It raises the link and the user after the lowering has run,
and nothing lowers them again. The window is one provider round trip, and an attacker cannot
choose when the operator saves, so this is not blocking.
Either:
- re-read the trust by slug in `finishSignIn` just before the clamp, which leaves no network call
  in the window, or
- add a sentence about it to the data model's known limits.

I prefer the re-read. It is three lines and a test that flips the setting between the two reads.

**N2. A secret typed on the same save as trust off is silently dropped.**
The first post asks for confirmation and draws the pane with the "type it again" hint. But the
confirm block is a separate form with no secret field. Confirming saves everything except the new
secret and reports "saved". An operator who rotated the secret upstream then gets failing
sign-ins.
Either:
- name it in the confirm block ("The new client secret was not saved; enter it again after
  confirming."), or
- refuse the combination as the strand warning does.

**N3. A save that unshares and turns trust off drops the trust change (known limit, accepted).**
The operator confirms the trust-off count, then meets the strand warning, then confirms the
unshare, and the trust stays on. The warning does say nothing was saved, and that matches what
happens to a typed name or list. Since the operator has already confirmed "trust off" once, add
one sentence to the strand notice when the post carried a trust change ("The trust change was not
saved either; save it again."). Or leave it, and the known limit in `notes.md` stands.

## Re-check

One re-check after the design blocking item, the docs B1 and whichever nits are taken. For the
code, I will read only the diff since `c971183`.

## Re-check at 02aabcc (2026-11-14)

**Clear to merge from the code side.** I read only what changed since `e69a516`
(`git diff e69a516..02aabcc -- src tests`).

- **N1, taken.** `finishSignIn` reads the connector again by slug before the clamp. It trusts the
  provider only when both reads say so. A connector that is gone at the second read keeps the
  earlier value, which is safe because `recheckKind` refuses a deleted connector afterwards. No
  network call is left between the re-read and the writes. The new test,
  `trust turned off during the code exchange is honoured by that sign-in`, turns trust off from
  the stub's token endpoint and asserts that the user and the link both end unverified. The
  implementer showed that the test fails without the re-read.
- **N2, taken.** When the refused post carried a new secret, the confirm alert ends with
  `ConnectorEmailTrustConfirmSecret`. Tests check that the sentence appears with a typed secret
  and is absent without one.
- **N3, left** as the known limit in `notes.md`. Accepted.
- Also in the commit, and outside the code: `ConnectorEmailTrustAddLead`, which is the add
  form's lead only (the test asserts the pane keeps the plain lead), and the
  `.um-choice-lead` spacing. Those are for the designer's and the docs session's re-checks.

Run here at `02aabcc`: the spec 017 sign-in, admin and store tests, `DatabaseTests` and the
email-domain tests, filtered: 220/220 passed. The implementer's full-suite count is still to come.
