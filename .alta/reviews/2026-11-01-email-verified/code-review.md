# Code review: raise email_verified only when the provider verifies the stored address (task h762)

Branch `alta/royal-tiber-52ft`, merged into `main` as `f856d2d` (code `d6f20af`, `2920f02`). Reviewer: the `umbral` session, 2026-11-04. The task named this review as a gate before merge; the merge happened first, so this is a review of the merged code, with its findings fixed in the same branch as the T2 merge.

## Verdict

**Clear.** The fix is right and small, the cleanup only lowers, and the tests cover the cases the task listed. Two nits, both fixed in `merge/docs-t2` (now on `main`), and one observation for later.

## What was checked

- **The `UPDATE` in `UserStore.resolve`.** `email_verified` rises only when `@verified = 1` and the stored `email` is `NULL` (and `COALESCE` is filling it with `@email`) or equals `@email` under `COLLATE NOCASE`. In SQLite the right-hand `email` is the row's value before the statement, so the comparison is against the stored address. The `COLLATE NOCASE` on the right operand is what the linking query uses, so both agree on case.
- **`verifiesAnAddress`.** A provider that says "verified" with no address, or a blank one, verifies nothing stored: the flag stays down on insert and on update. The linked account keeps the raw assertion, which can't match a `NULL` address in the cleanup either.
- **The only write paths.** `email_verified` is written in `UserStore.resolve` (insert and update) and in `Users.insertForTesting`, which is test-only. Passkey and Fediverse profiles carry no email, so they never raise it.
- **The linking query** (`UserStore.fs`, `… WHERE email = @email COLLATE NOCASE AND email_verified = 1`) is unchanged, and with the fix its `email_verified = 1` now means what it assumes: a provider verified that stored address.
- **The version 9 step.** It runs inside `upgradeSchema`'s transaction, guarded by the stored version, and only sets the flag to 0 where no linked account asserts the stored address as verified. It never raises. `upgradeSchema` returns the count, and `ensureSchema` logs it (a warning when it isn't 0) from every command that opens the database, so it runs once whichever starts first.
- **Tests.** `UserStoreTests` has the three-step attack ("a verified address the provider asserts later does not verify the stored first address"), the case-only difference, the `NULL` stored address filled later, the same address verified later (which then links), a provider that stops asserting, and "verified" with no address. `DatabaseTests` has the 8 → 9 upgrade lowering only unproved flags, the step running once, and a fresh database reporting no step.
- **README.** "Integrate an app" and "How people are identified" say the rule, "Upgrading" says the one exception, and Troubleshooting has the read-only `sqlite3` query for joins that already happened, with the right caution that a row proves nothing by itself.
- **Full suite:** the reviewer's own `dotnet test` on `main` at `f856d2d` (the fix merged, before T2): **1793 passed, 0 failed, 0 skipped**.

## Nits

- **N1, fixed here.** The changelog note (`### Security` and `### Upgrade`) was committed into `CHANGELOG.md`'s preamble instead of a `changelog.d/` file, so `prepare` would not have gathered it into 0.8.0. It is now `changelog.d/h762-email-verified.md` (`2606c10`).
- **N2, fixed here.** With spec 011 in the same release, the Upgrade notes said "becomes schema version 8" and "becomes schema version 9" one after the other. The 011 note now says only what operator passkeys need at upgrade; the version line is the email fix's.

## Observation, for later

- The cleanup can lower a legitimate user: someone whose first address was verified at the time, and who later changed their address at the provider. `linked_accounts` keeps only the latest assertion, so after the change no row verifies the stored address, and the step sets the flag to 0. That is the safe direction, the README's Upgrading section says it happens, and the log says how many. If it turns out to bite real users, the remedy is to record each link's first verified address, not to loosen the step.
