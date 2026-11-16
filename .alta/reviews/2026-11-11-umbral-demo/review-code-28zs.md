# Code and security review, task 28zs (the demo's reset by a scheduled `clear-users`)

Reviewer: the `umbral` session. Commit `7d15b66` on `alta/honest-aster-f2hw` (base `bb3fc37`).

**Verdict: not clear yet, one blocking item (a runbook step that would make the privacy page
untrue) and two nits.** The design is right, and the fix is one paragraph.

## What holds

- **The backup policy:** `WithCaravanaBackup(..., schedule: "never", keep: 3)`. Caravana 0.21.0
  accepts `never`, and 13.6.4 and later pass the string through. No restore and no pin: the
  golden-snapshot design is fully gone.
- **The schedule lives in the console, and a deploy leaves it alone.** I checked Caravana's source:
  schedules are rows of their own, deleted only by `DeleteSchedule` or when the app is deleted
  (`internal/store/apps.go`), never by a deploy's Compose overlay. Feature 040 (`target: copy`, on
  Caravana `main` `feaf669`, unreleased after 0.21.0) runs the command in a healthy copy, waits
  while the service is deploying or has no healthy copy, and turns a non-zero exit into
  `schedule_failed` on the needs-you list. That fits `clear-users`: exit 0, 2 without `--yes`, 1 on
  failure.
- **`demo/reset/` is deleted whole.** No token, timer, notifier or clone on the host is left in the
  repository.
- **`demo.yml`'s floor of 0.13.0** is right: the reset needs `clear-users`. Until 0.13.0 a manual
  run fails with that message.
- **The pages:** the landing page drops "off for about a minute". The privacy page says accounts,
  sessions, tokens and the security log go nightly, except the `users_cleared` line, while the
  instance keeps running. That matches what `clear-users --security-log` does.

## Blocking

**B1. "Retake it after a change worth keeping" would put visitors into a backup.**
- README step 8 says to retake the manual backup after a change worth keeping (another connector,
  a new logo). Done in the afternoon of a live demo, that backup holds every visitor since 04:00,
  and `keep: 3` keeps it.
- The privacy page says "The only copy of the instance is a backup taken by hand while it was
  empty … no copy holds an account."

Fix: step 8 says to make the instance empty first. Run `/app/Umbral clear-users --yes
--security-log` with **Run a command** on the `umbral` copy, then **Back up now**, in that order,
every time. Say the same in `notes.md` where it says how to run the clearing by hand. Better still,
retake it right after the 04:00 run, when it is empty anyway.

## Nits

- **N1. The schedule isn't in the repository, so forgetting it makes the privacy page untrue
  silently.** It's created once in the console (step 9), and if the app is ever deleted and
  recreated, it's gone. Add to the README's "Acceptance" or "Afterwards": "after every new app in
  Caravana, check that Schedules lists `clear-users` with Run in: the running copy". When the
  Caravana package can declare a schedule, move it into the AppHost (a follow-up, not now).
- **N2. Stop after.** Step 9 says "Stop after: the usual (1 minute in the running copy)".
  `clear-users` takes about 1 s for 10 000 users (c1rs notes), so 1 minute is plenty. Name the
  value explicitly ("1 min") so the runbook doesn't depend on a default.

## Re-check

One re-check after B1. I'll read only the diff since `7d15b66`.

## Re-check at e204490 (2026-11-16)

**Clear.** I read the diff since `7d15b66`.

- **B1, taken.** The runbook now orders it right:
  - step 7 cleans up the test sign-ins with **Run now** on the schedule (users and the security log
    together);
  - step 8 sets the schedule;
  - step 9 takes the backup straight after that clearing.

  Retaking is allowed only on an empty instance: **Run now** first, then **Back up now**, every
  time, or just after 04:00 UTC. "Afterwards" and `notes.md` say the same. The privacy page's "no
  copy holds an account" now holds for anyone who follows the runbook.
- **N1, taken:** "Afterwards" says to check that Schedules lists `clear-users` after an app is
  deleted and recreated, "Acceptance" checks that it runs in the running copy at 04:00 UTC, and
  `notes.md` says the schedule goes with the app.
- **N2, taken:** "Stop after: 1 min (the clearing takes about a second)".

The AppHost's comments follow the renumbered steps, and check-links passes (473).
