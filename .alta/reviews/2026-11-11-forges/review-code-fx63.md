# Code and security review, task fx63 (Forgejo's fix, the held-back details restored)

Reviewer: the `umbral` session. Commit `3ff32bb` on `alta/hopeful-arcturus-tfjh` (base main `b0a1472`),
docs only.

**Verdict: not clear yet, one blocking item.** The versions, dates and advisory are right, and
nothing is claimed about Codeberg's build. But the advice is wider than the fix in one way: the fix
stops *new* unconfirmed primary addresses and does nothing about ones made before the upgrade.

## What holds

- **The facts:**
  - Fixed in 15.0.12 and 17.0.2, 2026-11-18, advisory #63, which is public.
  - Affected: v15 before 15.0.12, v17 before 17.0.2, every v16 (end of life 2026-10-29,
    unpatched), and everything older.
  - This matches the advisory, and the fix you cite (PRs 15299 and 15298, "check email activation
    in MakeEmailAddressPrimary") is exactly our finding: the UI allowed only verified addresses,
    and the POST didn't check.
- **"15.0.12 or a later v15, or 17.0.2 or later"** deliberately leaves 16.x out. Good.
- **Codeberg:** "its own documentation; not tried" is kept, the operator is pointed at
  `/api/v1/version`, and nothing says which build Codeberg runs.
- **The Gitea / Forgejo contrast** is back, accurately: Gitea refuses the request, Forgejo before
  the fix accepts it.
- **The restored scripts** match `24d362b` byte for byte. The only credential is a throwaway local
  test account (`ana`) on a disposable instance.
- The changelog fragment, check-links (474) and the site build pass.

## Blocking

**B1. Upgrading doesn't undo addresses already made primary.**
- On any Forgejo that ran an affected version, an account may *already* hold a primary address it
  never confirmed, made before the upgrade. Forgejo sends a primary address of an active account as
  verified, so after the upgrade that account still reaches Umbral as `email_verified: true` for an
  address it doesn't own.
- So "**Trusted** when it runs 15.0.12 or later … and registration requires confirmation" is safe
  for a Forgejo that never ran an affected version with untrusted accounts on it. It isn't
  guaranteed for one that did.

Fix, in "What `email_verified` means on a forge":
- **A Forgejo you run:** Trusted on a fixed version with closed or confirming registration, **and
  once you have checked that no account's primary address is unconfirmed**. Check this if it ever
  ran an affected version with accounts you don't control.
  - Name where to look. Verify the menu on your local Forgejo before writing it: the site
    administration's list of email addresses, which shows whether each is activated and primary.
    In the database, it's the `email_address` table, rows with `is_primary = 1` and
    `is_activated = 0`.
  - Say what to do with one: make the account confirm it, or remove the account.
  - If the instance only ever had accounts you control, there's nothing to check. Say so.
- **Codeberg: Not trusted, without the "until you're satisfied it runs a fixed build" exit.**
  Codeberg ran affected versions, it's an open instance with a very large number of accounts, and
  no operator outside it can check its existing accounts. A fixed build doesn't make its existing
  primary addresses trustworthy. Keep the pointer to its version only as history, or drop it.
- **The changelog fragment:** the same two changes ("…once no account's primary address is
  unconfirmed"; "keep Codeberg **Not trusted**").

## Re-check

One re-check after B1. I'll read only the diff since `3ff32bb`.

## Documentation review

`umbral-documentation` reviews the wording separately. The `umbral` session passes the request on
when that session is running.
