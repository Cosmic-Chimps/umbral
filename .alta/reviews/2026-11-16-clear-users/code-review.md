# Code and security review, task c1rs (`Umbral clear-users`)

Reviewer: the `umbral` session. Code `03d43ba` on `alta/lofty-rudder-4rmj` (base `8f305cf`), read
against the task file and `notes.md`.

**Verdict: clear to merge, with two nits.** Nothing blocking. All four choices are accepted.

## What holds

- **One transaction, the right scope.**
  - `BeginTransaction()` is `BEGIN IMMEDIATE`, so the write lock is taken first and waits up to
    `busy_timeout` (5 s) for the live process.
  - The counts are read inside it, so the output reports exactly what went.
  - Everything is deleted explicitly, children first: OpenIddict tokens, then authorizations, then
    `passkey_ceremonies`, `passkey_invitations`, `passkeys`, `linked_accounts`,
    `legacy_sessions_ended`, `removed_links`, `users`. It never relies on `ON DELETE CASCADE`.
  - `users_cleared` is recorded inside the same transaction, after an optional log clear, so a
    cleared log holds exactly that row.
  - Any exception rolls everything back. The trigger test pins it.
  - All SQL is literal (MetaTests).
- **What's kept** matches the task: the operator credential, `operator_passkeys`, connectors,
  `fediverse_servers`, apps, branding, connector choices, `declared_items` and `settings`. The key
  files are untouched, and a test checks them byte for byte.
- **No start-up beside the live process.** `clearUsers` calls only `ensureSchema`, as
  `reset-credential` does: no `initialize`, no OpenIddict migrations and no declaration apply. In
  the live container the schema is already at the image's version, so `upgradeSchema` changes
  nothing.
- **Refusals after a clearing, with no restart.** Tests cover all three, then a new sign-in:
  - a session cookie at authorize (the user is gone, so the session is signed out);
  - a refresh token at the token endpoint (`invalid_grant`);
  - an access token at userinfo (401);
  - a new sign-in creates a new user.
- **The event and the output:**
  - actor `system`, detail `users=<n>`, plus `;security_log=cleared` when the log was cleared;
  - the output is counts only;
  - no id, name, address or subject anywhere. The error path prints `ex.Message`, which for a
    SQLite or I/O failure holds no personal data.
- **Measurement:** 1.06 s for 10 000 users beside a live instance, well inside Caravana's one-minute
  limit for a command.

## The four choices

1. **Every passkey invitation goes, unused ones included: accepted.** Each was made for a person,
   and a demo must not keep a working invitation link across a reset. The docs should say pending
   invitations must be sent again. Check that `docs/run/` says it: it's a docs-review item, and I
   haven't re-read 279951c.
2. **OpenIddict's tables skipped when absent, no migrations: accepted.** Running migrations beside
   the live process would be the real risk, and a data directory that never started has no tokens.
3. **Exit 1 both without `--yes` and on failure: accepted.** See N1.
4. **No separate test for a cached token entry: accepted, with N2.**
   - The refresh-token path is safe even with a cached entry. OpenIddict redeems a refresh token by
     updating its row, which is gone, so the update fails and the grant is refused. `Token.fs` also
     looks up the user, who is gone.
   - The access-token path is safe through userinfo's user lookup.
   - A test that warms the cache first would only prove what is already documented.

## Nits

**N1. A distinct exit code for "not confirmed".** Exit 2 without `--yes` (and 1 on failure) lets a
script or a scheduler tell "forgot the flag" from "failed". Optional; if it's taken, say it in the
docs.

**N2. Name the cached-token reasoning in a comment.** One line in `ClearUsers.fs` or the sign-in
test file: "a token entry still in OpenIddict's cache is refused anyway: redeeming it updates a
row that is gone, and Token.fs and UserInfo.fs look the user up." Then the next reader doesn't
need to work it out again.

## Re-check

None needed. Take the nits or leave them.
