# Documentation review: `Umbral clear-users` (task c1rs)

Branch `alta/lofty-rudder-4rmj`, docs commit 279951c, read against the code at 03d43ba
(`Program.fs` `clearUsers`, `Infrastructure/ClearUsers.fs`, `Handlers/Connect/Authorize.fs`).
Reviewed 2026-11-16 by the documentation reviewer.

## Verdict

**Not clear yet: two blocking items, three nits.** The page is short, in the right order (what it
does, look first, what goes and stays, the log, the output, what people notice), and its output
lines, exit codes and `users_cleared` detail are exactly the code's. The first blocking item is a
security property the page lists as an ordinary deletion. It is really a question for the code and
the maintainer, and I'd fix it in code. One re-check after the fixes.

## What was checked

- **The output and exit codes** (`Program.fs:933-968`): the dry-run sentence word for word and exit 1;
  "Deleted N users, N linked accounts, N passkeys, N tokens." with singular and plural handled;
  "Security log cleared." only with `--security-log`; exit 0; on failure, a reason on stderr and exit
  1 with nothing changed (one transaction). Right.
- **What goes** (`ClearUsers.fs:68-82`): OpenIddict tokens and authorizations, passkey ceremonies,
  every invitation, passkeys, linked accounts, `legacy_sessions_ended`, `removed_links`, users; the
  security log only when asked. **What stays**: the operator credential and passkeys, connectors,
  `fediverse_servers`, apps with branding and choices, `declared_items`, settings and the key files.
  The page lists both sides faithfully. B1 is about one item on the first list.
- **The event**: `users_cleared`, actor `system`, detail `users=<n>` with `;security_log=cleared`,
  recorded in the same transaction. Right.
- **Refresh and access tokens**: the token rows are deleted, so a refresh gets `invalid_grant`, and a
  reference access token is not found at userinfo (`401`). Right.
- **`docker exec` without `-it`** (your open point 2): agreed. The command prints no secret, and a
  scheduled job has no terminal.
- **backup.md's pointer, README, sidebar, changelog fragment**: right. `check-links.py`: 472 links
  resolve. `changelog.sh check`: in order.

## Blocking

### B1. Deleting `removed_links` undoes spec 018's decision 1

The page lists "the records of the links you removed" among the deletions, as if it were
housekeeping. It is not. Spec 018, decision 1, keeps that record **permanently** and **not tied to
any user**, for a stated reason: a provider account removed as a stranger's must never have its
addresses trusted again, "or the real person's next new provider would join the stranger". After
`clear-users`:

1. The real person signs in again. They are a new user, and their address is verified by their own
   provider.
2. The stranger signs in again through the same forge, still claiming that address. With the record
   gone, the address counts as verified again, so if that connector is still **Trusted** they are
   linked into the real person's new account: the takeover spec 018 closed.

The record is not something a user holds: it has no user column, it describes a provider account,
and spec 018 made it outlive users on purpose. My recommendation is in code, for the code reviewer
and the maintainer: **keep `removed_links`** (drop it from `DeleteUsers`, and list it under "Kept" in
`ClearUsers.fs` and on the page: "the provider accounts you removed from a user, which Umbral still
never trusts"). Keeping it costs nothing on a demo. If the maintainer decides it must go after all,
the page has to say what that reopens, in the words above, and advise setting the connector to **Not
trusted** first.

### B2. Caravana's "Run a command" and scheduled commands do not exist yet

The page tells a Caravana operator to use **Run a command** on the running copy, or a scheduled
command. You say yourself (open point 1) that the names come from Caravana's task 04, and that
Caravana's docs do not have them. A reader on today's Caravana looks for a button that is not there.
Until Caravana releases it, either drop the sentence, or make it conditional and true: "On a
Caravana host, run it in the container from the host (`docker exec`, as above); a later Caravana
release adds **Run a command** for this." Then replace it with the released wording, and the
version, when it ships. Ask the `caravana` session which it will be.

## Nits

1. **What an app gets on its next authorization.** "Signed out the next time that app sends them to
   Umbral" is gentler than what happens. A session naming a deleted user makes `/connect/authorize`
   answer the app with `error=login_required` (`Authorize.fs:369`), which the app has to handle as
   "sign in again". Say that, and link
   [When a person is disabled or deleted](../integrate.md#when-a-person-is-disabled-or-deleted),
   which describes the same answer.
2. **A passkey person, with invitations only.** "A passkey a person made is gone. They register a new
   one." holds with **Anyone can sign up**. With invitations only, every invitation is gone too, so
   they need a new link from you: add "…or, with invitations only, wait for a new invitation from you".
3. **"to reset a public demo every night".** The demo in this repository resets by restoring a
   snapshot, not with this command. Write "to reset a public instance every night" or similar, so
   nobody reads it as the demo's own method. Or name the trade-off in one line: clearing keeps the
   newest setup; restoring also undoes setup changes made since the snapshot.
