# Review: the Users screen's recovery link

Task: `.alta/tasks/2026-10-24-users-invite-button-says-recovery-r3cv.md`
Branch: `alta/faithful-atlas-chdr`, `5f5e38b` (the change) and `a0dda49` (notes, captures, a
walkthrough fix). Reviewed 2026-10-26 by the design reviewer from the diff, a test run and the
16 captures.

## Verdict

**Clear to merge. No blocking items, no nits on this change.** One finding outside the task,
for the maintainer, at the end.

## What was checked

- Strings as sent on 2026-10-26, verbatim in `Texts.fs`: "Create a recovery link", the hint,
  "Recovery link", "Invitation link"; the invite button and the notice unchanged for a
  provider user.
- Markup: on a row with a passkey the button carries `aria-describedby` to the hint `p.um-hint`
  directly under its form, both in a `space-y-1` group; on a provider row no hint and no
  `aria-describedby`; the copy field's label is the noun for each case. No new class,
  `umbral.css` untouched.
- The decision `recovery = not data.Passkeys.IsEmpty` is the right one: someone with a device
  adds passkeys themselves on `/account/passkeys`, so the operator's link on such a row is only
  ever recovery.
- Captures at 1280 and 360, both themes: the hint reads as the button's explanation, not as a
  field hint; "RECOVERY LINK" and "INVITATION LINK" label the revealed field; the notice is the
  same for both and still true.
- Test: one case covering both rows, the hint's id pairing, and both labels.
- Tests and stylesheet: see the last section.

## Outside this task, for the maintainer

The user card shows **two red fills**: Remove on each passkey and Delete on the card.
DESIGN.md's One Red Fill rule keeps the red fill for what cannot be undone; removing a passkey
can be undone by the recovery link this task just named, so it is Remove logo's and Revoke's
case: a small ghost in the error colour, with its confirm. One class change in `Users.fs`
(`btnSm Ghost + " text-error hover:text-error"`) and the test that counts danger buttons, if
one exists. Worth a queued one-liner; not this task's.

## Tests and stylesheet

Run by the reviewer on the branch's worktree at `25bde7a`, 2026-10-26:
`dotnet test` with the User and Passkey filters: 214 passed, 0 failed.
`build-css.sh --check`: clean, `umbral.css` untouched by this branch.
