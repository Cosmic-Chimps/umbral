# Notes: raise `email_verified` only for the stored address (task h762)

Branch `alta/royal-tiber-52ft`, from `main` at `3a235da`. Security fix, patch release (0.7.1).

## The fix

`UserStore.resolve`, the update for an existing or linked user:

```sql
email_verified = CASE WHEN @verified = 1 AND (email IS NULL OR email = @email COLLATE NOCASE)
                      THEN 1 ELSE email_verified END
```

- On the right-hand side of an `UPDATE`, `email` is the stored value from before the statement, so a NULL
  stored address being filled in by this same statement counts as "the same address".
- The comparison is the linking query's own (`= … COLLATE NOCASE`, ASCII case folding), so "the flag
  is up" and "the linking query matches" agree on what one address is.
- `@verified` is 1 only when the profile asserts a non-blank address as verified (`verifiesAnAddress`),
  for the insert and the update. Before, a provider saying "verified" with no address could create a
  user with `email NULL, email_verified 1`. That never linked (NULL matches nothing), but apps were told
  `email_verified: true` with no `email`. The linked account still records the raw assertion.
- A provider that stops asserting (no address, or unverified) leaves the address and the flag as they
  are, as before: the flag never goes down in `resolve`.

## Other write paths of `users.email_verified`

`grep -rn email_verified src` and `grep -rn 'UPDATE users' src`:

- `Passkeys.insertPasskeyUserIn`: `email NULL, email_verified 0`, literal.
- `Users.insertForTesting`: tests only.
- `Users.fs` `UPDATE users SET is_enabled`: does not touch it.
- Fediverse sign-ins go through `Callback.fs` → `UserStore.resolve`, and Fediverse profiles have no email.
- No other `UPDATE users`.

## The upgrade step (schema version 9)

`UPDATE users SET email_verified = 0 WHERE email_verified = 1 AND NOT EXISTS (a linked account of the
user asserting the stored email, COLLATE NOCASE, with asserted_email_verified = 1)`. It only lowers and
never raises. It runs inside `initializeSchema`'s transaction, only when the stored version is below 9,
like every other step. The count is logged at startup by whichever command upgrades first
(`Umbral.Startup`, a warning when it is not 0).

What it changes on a test database built to have one of each case (scratch SQLite, 2026-11-03):

| User | Situation | Before | After |
| --- | --- | --- | --- |
| V | raised by another address, nobody joined yet | 1 | **0** |
| U | raised, and the real owner's Google already joined | 1 | 1 |
| W | same, attacker switched back to the victim's address unverified | 1 | 1 |
| H | two providers, same address in different case | 1 | 1 |
| P | passkey only, no address | 0 | 0 |

**1 row changed.** U and W stay verified because the real owner's own account now verifies the stored
address. That is why the README Troubleshooting query exists: on the same database it lists U and W
(all four of their links) and neither H nor P. The `DatabaseTests` case for version 9 has its own
fixture (see below).

A legitimate user can also fall to 0: one whose only provider account changed its address at the
provider after the first sign-in (the stored address is first-seen). That is the safe direction, as
the task says: no email link to them any more, and apps see `false` until a provider verifies that
address again.

## Docs

- CHANGELOG `## Unreleased`: `### Security` and `### Upgrade` (`changelog.d/` is not on `main`).
- README: a new bullet under "What an app can count on" (`email_verified` speaks for the address in
  `email`), a sentence in "How people are identified", "Upgrading" at version 9, and a Troubleshooting
  entry with the read-only query.

**Merge step M1.** `docs/t2-identity` (unmerged) has, in "Integrate an app", "`email_verified` can go
from `false` to `true`, never back." Whichever lands second: replace that sentence with this branch's
bullet, and keep one statement of the contract. If `changelog.d/` (cl6d) lands first, move the two
CHANGELOG sections to `changelog.d/email-verified-h762.md`.

## Tests

`UserStoreTests` (end to end through `UserStore.resolve` and SQLite):

- `a verified address the provider asserts later does not verify the stored first address`: the three
  steps of the task. After step 2 the email is still `victim@`, unverified, and step 3 is `CreateNew`.
- `a verified address that differs only in case verifies the stored address`
- `a user created with no address takes the first address later, verified`: NULL stored, then linking works.
- `the same address verified later raises the flag and then links`
- `a provider that stops asserting an address leaves the stored address and flag alone`
- `a provider that says verified with no address creates an unverified user`
- The existing `losing verification at the provider does not unpick an existing link` still passes.

`DatabaseTests`:

- `a version 8 database upgrades to 9 and only the unproved email flags fall`: six users (verified by
  a link in another case, verified by another address, a link asserting the same address unverified,
  NULL address, already 0 with a verifying link, and the seed's Jane with no asserting link).
  `EmailVerifiedLowered = Some 4`, the expected flag for each, and the `linked_accounts` rows unchanged.
- `the version 9 step runs once and does not lower a flag set later`: a second `upgradeSchema` reports
  `None` and leaves a hand-raised flag alone.
- `a fresh database reports no version 9 step`
- The schema version assertions move from 8 to 9, and the "newer database" test from 9 to 10.

Runs: the full suite gave 1793 passed, 0 failed. The DatabaseTests, UserStoreTests, MetaTests and Linking
filter gave 102 passed, 0 failed (2026-11-03).
