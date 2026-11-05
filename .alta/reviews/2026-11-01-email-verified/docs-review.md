# Documentation review: task h762, `email_verified` only for the stored address

Reviewed 2026-11-05 on `main` at `cbc1a89` (0.8.1), after the merge (`f856d2d`,
code `d6f20af..2920f02`; docs T2 at `9282863`), by the documentation reviewer
session, per section 6 of `docs/audit-2026-10.md`. The code review by the
`umbral` session is in `code-review.md` beside this file.

## Verdict

**Clear.** No blocking item. One nit worth a follow-up, one not.

## What was checked

Each passage against the code on `main`:

- **"Integrate an app", the email bullet (README:354-359).** "`email_verified`
  becomes `true` only when a provider verifies **that same** address, and it
  does not go back to `false` afterwards" is `UserStore.fs:178-179`: the flag
  rises only when `@verified = 1` and the stored email is NULL or equal to the
  asserted one, case-insensitively, and the `CASE` never lowers it. "One
  exception: the upgrade to schema version 9 may set it back to `false` once,
  for a user whose stored address no provider has verified" is
  `UpgradeToVersion9` (`Database.fs:430-435`), which lowers the flag where no
  linked account asserts the stored address as verified, and only lowers.
- **"How people are identified" (README:491-493).** "The address Umbral keeps
  for a user is the first one a provider gave" is `email = COALESCE(email,
  @email)` (`UserStore.fs:177`); "it counts as verified only once a provider
  has verified that address, not another one" is the same `CASE`. The linking
  side still requires both addresses verified (`findVerifiedEmailMatches`,
  `UserStore.fs:48`).
- **"Upgrading" (README:1294-1301).** Schema version 9 (`Database.fs:287`);
  the exception sentence matches the step; "the log says how many" is the
  warning at `Database.fs:502`, which carries the count and points at the
  Troubleshooting entry by its heading.
- **Troubleshooting, "A user may hold someone else's provider account"
  (README:1486-1513).** I ran the query on a scratch database with five users:
  a healthy two-provider user whose two addresses differ only in case (not
  listed); a hijack where the attacker's Discord now asserts its own address
  (listed, both rows); a hijack where the attacker switched back to the
  victim's address unverified (listed); a single-provider user (not listed);
  and a healthy two-provider user whose GitHub returned no address on its last
  sign-in (listed; see nit 1). The query is read-only, the column names match
  `Database.fs` and the `linked_accounts` insert (`UserStore.fs:201-208`), and
  passkeys do not create `linked_accounts` rows, so a provider user with a
  passkey is not a false positive.
- **The changelog.** The note is in the released `## 0.8.0` section of
  `CHANGELOG.md` (Security and Upgrade; `changelog.d/` holds only its README
  on `main`), in the voice of the other sections. It names the cause, the
  shape of the attack, the fix, "every version from 0.1.0", the backup, the
  one-time lowering with its warning, and that old joins are not undone, with
  the pointer to the Troubleshooting entry. Nothing in it contradicts the code.
- **The claim set in the README's bullet list** stays true after the fix:
  `email` and `email_verified` still come together or not at all
  (`Authorize.fs:107-115`), and the T2 tests still describe the emitted set.

## Nits

1. **README:1509, "a person may simply have changed their address at one
   provider".** The query also lists a user whose provider stopped returning
   an address at the last sign-in: GitHub does this when its email lookup
   fails or the scope is refused (`GitHub.fs:183-189`), and the `ON CONFLICT`
   update then records `asserted_email NULL, asserted_email_verified 0`
   (`UserStore.fs:205-208`), which the query counts as "does not assert the
   address". One clause after the existing one, "or a provider may not have
   returned an address the last time they signed in", keeps the operator from
   reading such a row as a hijack. Worth the follow-up branch.
2. **README:1301 and 358, "(see the CHANGELOG)".** The note is in the 0.8.0
   section; "see the 0.8.0 notes in the CHANGELOG" would take the reader
   straight to it once more releases sit above it. Optional.

## notes.md

Complete under section 6: the fix with the SQL and its reasoning, every other
write path of the flag, the upgrade step and where its count is logged, and
the verification. Agreed on all of it.
