# Documentation review: the demo's nightly reset by `clear-users` (task 28zs)

Branch `alta/honest-aster-f2hw`, commit 7d15b66 on bb3fc37. Read with `git show 7d15b66` and the
whole of the new `demo/notes.md`. Checked against `docs/run/clear-users.md` and its code (c1rs),
`.github/workflows/demo.yml` and `CHANGELOG.md`. Reviewed 2026-11-16 by the documentation reviewer.
Not committed, as asked.

## Verdict

**Not clear yet: one blocking item, three nits.** The rewrite is simpler and safer than what it
replaces. There is no full API token on the host any more, no timer and no notifier. The demo now
lives entirely in Caravana and the repository, and notes.md's last line says so. The privacy and
landing pages match the runbook and each other. The blocking item is one sentence in step 8 that
can make the privacy page untrue. One re-check after the fix.

## What was checked

- **What the clearing does and keeps** (README opening and step 9, notes.md, the privacy page)
  against clear-users. It deletes users, linked accounts, passkeys and invitations, tokens and the
  records of removed links, in one transaction, beside the live process. With `--security-log` the
  log goes and one `users_cleared` event remains ("except one line saying that the clearing
  happened"). It keeps the operator credential and its switch, connectors, `fediverse_servers`,
  apps, declared items, settings and keys. Exit codes 0, 1 and 2 (notes.md) match the code. Right.
- **The Fediverse paragraph**: the registration is kept, so "made once per server, not once per
  night". True, since `fediverse_servers` is kept. Right.
- **Versions**: 0.12.0 is released (2026-11-15), and `clear-users` is in `changelog.d`, so in 0.13.0.
  The laptop paragraph, step 0 and `demo.yml`'s floor (0.13.0, line 159) agree. Right.
- **The landing page note**, "keeps running while that happens", agrees with the privacy page and
  step 9. Right.
- **Acceptance**: an empty Users screen, one `users_cleared` row, Umbral never stopped, no backup
  taken. That is what the schedule does. Right.
- **Gone with `demo/reset/`**: no remaining reference to the reset script, the installer,
  `/opt/umbral` or the full token anywhere in `demo/`. The `.gitignore` comment is updated.
  `check-links.py`: 473 links resolve.

## Blocking

### B1. Retaking the backup during the day breaks the privacy page's promise

The privacy page now says: "The only copy of the instance is a backup taken by hand while it was
empty … so no copy holds an account." Step 8 then says "Retake it after a change worth keeping
(another connector, a new logo)", and "Afterwards" says the same for a connector change. A change
is made during the day. A backup taken then holds that day's visitors, with their names, addresses
and avatar addresses, and stays in the destination among the last three for as long as it takes to
replace it. That is the copy the privacy page says does not exist.

Say how to take it empty, in step 8 and in "Afterwards": "Take it on an empty instance: run the
schedule by hand first (**Run now** on `clear-users`), then **Back up now**, or take it just after
04:00 UTC." notes.md's Backups line should then say "taken by hand, each right after a clearing".

## Nits

1. **Step 7 could use the same move.** "Delete those users on Users" leaves your test sign-ins in
   the security log, with your IP, and that log goes into step 8's backup. Running the schedule by
   hand (**Run now**) deletes the users and empties the log in one step, and gives exactly the state
   every night returns to. Suggest it as the way to clean up.
2. **"the release after 0.21.0"** (step 0) guesses Caravana's next version number. Name the feature
   instead ("a Caravana whose schedules can run in the running copy, API 0.43.0 or later"), and add
   the version when it ships. The same goes for **Run a command**, **Run now** and **Schedules** in
   notes.md: they are that release's.
3. **A clearing that fails** keeps the day's users until the next good one. notes.md says so, but
   the privacy page's "Every night at 04:00 UTC every account is deleted" makes no allowance for it.
   Optional: "…is deleted (if a night's clearing fails, at the next one that succeeds)". This is the
   same honesty the earlier text had about a failed reset.

## Re-check at e204490 (2026-11-16)

**Clear.** B1 and the three nits are fixed (`7d15b66..e204490`). Not committed, as asked.

- **B1**: steps 8 and 9 are swapped, so the schedule exists before the backup. Step 9 takes the backup
  right after step 7's clearing, and says to retake it only on an empty instance: **Run now** first,
  then **Back up now**, every time, or just after 04:00 UTC. "Afterwards" and notes.md's Backups line
  repeat the rule, and notes.md says the privacy page's promise holds only while the order is kept.
  Right.
- **N1**: step 7 cleans up with **Run now** on the schedule, which deletes the test users and the log
  of their sign-ins in one step. **N2**: step 0 and notes.md name feature 040 and API 0.43.0, "not
  released as of 2026-11-16". Leaving **Run a command** and **Schedules** unversioned is right if they
  shipped earlier, as you say. **N3**: the privacy page allows for a failed night. Right.
- Every step reference after the swap (README lines 91, 225, 286, 289; notes.md lines 13, 21) points at
  the right step. `check-links.py`: 473 links resolve.

A good addition: "Afterwards" and Acceptance now say the schedule is not in the repository and goes
with the app if the app is deleted, so check that it is there. That is the one thing that could make
the privacy page silently untrue.
