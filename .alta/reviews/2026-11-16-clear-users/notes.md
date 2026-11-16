# Task c1rs: `Umbral clear-users`

The task file (`.alta/tasks/2026-11-16-cli-clear-users-c1rs.md` in the main checkout) holds the
decisions. These notes record what the implementation chose where the task left room, and the
measurement it asked for.

## Choices

- **The store** is `src/Umbral/Infrastructure/ClearUsers.fs`: `count` (for the run without
  `--yes`) and `clear`, one immediate transaction that deletes child tables first and writes the
  `users_cleared` event inside it, so the clearing and its record commit together or not at all.
- **Order:** `OpenIddictTokens`, `OpenIddictAuthorizations`, `passkey_ceremonies`,
  `passkey_invitations`, `passkeys`, `linked_accounts`, `legacy_sessions_ended`, `removed_links`,
  `users`, then `security_events` with `--security-log`, then the event.
- **Every passkey invitation goes**, including the unused ones for a new person (`user_id` NULL):
  each was made for a person, and its note usually names them. An operator who wants one again
  makes a new link.
- **OpenIddict's tables may not exist yet.** The command runs beside the live instance and does not
  apply OpenIddict's migrations (the live process did, at its start). On a data directory that
  never started there is nothing in them to delete, so the store skips a table that isn't there.
- **Exit codes:** 0 after a clearing, 1 without `--yes` (nothing changed), 1 on a failure (rolled
  back).

## Measurement (10 000 users)

2026-11-16, on the maintainer's Mac (Apple silicon), a Release build of this branch:

- A data directory started once (so OpenIddict's tables exist), then seeded by SQL with 10 000
  users, 10 000 linked accounts, 2 000 passkeys, 10 000 authorizations, 20 000 tokens (600-byte
  payloads) and 10 000 security events: a 49 MB database.
- The instance was started again and left running (health check answering) while
  `dotnet Umbral.dll clear-users --yes --security-log` ran beside it.
- **1.06 s wall clock, process start included**, exit 0, output
  `Deleted 10000 users, 10000 linked accounts, 2000 passkeys, 20000 tokens.` and
  `Security log cleared.` The same command on an empty instance takes 1.13 s, so the start of the
  process is nearly all of it and the transaction itself is a fraction of a second. Well inside
  Caravana's one-minute limit.
- Afterwards the live instance still answered `/healthz` 200; `users` and `OpenIddictTokens` were
  empty, the app was kept, and the log held one row, `users_cleared`, actor `system`, detail
  `users=10000;security_log=cleared`.
- Without `--yes`: 0.90 s, exit 1, `This would delete 10000 users …`, nothing changed.

The transaction takes the write lock at its start (BEGIN IMMEDIATE) and waits up to the
connection's `busy_timeout` (5 s) for a write of the live process. A longer wait fails the command
with exit 1 and changes nothing; running it again is safe.
