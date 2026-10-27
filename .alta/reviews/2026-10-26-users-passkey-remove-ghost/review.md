# Review: a passkey's Remove on the Users card as a red ghost

Task: grbb, the follow-up from the r3cv review (One Red Fill rule).
Branch: `alta/clever-byzantium-j92s`, `3fb7214` on `main` at `6f0467e`. Reviewed 2026-10-26 by
the design reviewer from the diff, a test run and the four captures.

## Verdict

**Clear to merge. No blocking items, no nits.**

## What was checked

- The one change asked for: the passkey Remove goes from the danger fill to
  `btnSm Ghost + " text-error hover:text-error"`, the confirm kept. Delete is the card's one
  red fill. Same pattern as Remove logo and Revoke.
- The test asserts one `um-btn-danger` per card and that the passkey row's button is a ghost in
  the error colour.
- Captures at 1280 and 360, both themes: Remove reads as the quiet choice on the passkey line,
  the red keeps it from reading as neutral, Delete is the only fill. No overflow.
- CHANGELOG bullet present. No stylesheet change.

## Note for whoever merges

This branch is on `main` before the recovery-link branch (`alta/faithful-atlas-chdr`, r3cv,
cleared 2026-10-26), which edits the same block of `Users.fs` a few lines below. Whichever
lands second will need a trivial rebase; the captures here still show "Invite to add a passkey"
on a row with a passkey for that reason, which r3cv fixes.

## Tests and stylesheet

Run by the reviewer on the branch's worktree at `3fb7214`, 2026-10-26:
`dotnet test` with the User filter: 98 passed, 0 failed.
`build-css.sh --check`: clean, `umbral.css` untouched by this branch.
