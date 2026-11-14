# Design review: the Declared marking (spec 019)

Branch `alta/twilight-fornax-23bd`, UI at `1911d94`, stylesheet at `96e38a6`. Contract:
`specs/019-declared-apps/contracts/screens.md`. Reviewed 2026-11-14 by the design reviewer from
three of the 39 captures (`app-declared-pane-1280-dark`, `passkey-declared-pane-360-light`,
`refusal-app-declared-360-light`). What declaring does, and the security decisions, are the
spec's and their own review's.

## Verdict

**Clear to merge. No blocking items; one nit.**

## What was checked

- **The mark**: a muted "Declared" pill in the pane header beside the type pill; on the list
  entry, text on the second line ("Web application · Declared"), with no second marker.
- **The note**: an info note under the heading, the variable in mono, saying what is changed
  where and what stays editable here, ending with the docs link. It reads in both themes and
  wraps at 360.
- **A declared app's pane**: Connection with the copy fields and the two URI lists as plain
  values, Sign-in methods as its summary line, Branding unchanged and editable, no closing row
  and no secret reveal. The pane keeps the shape the operator knows.
- **The declared passkey pane**: Settings as two label-and-value lines, no Save and no closing
  row; Invitations unchanged, with Revoke as a red ghost.
- **A refused change**: a Warning alert in the pane, in the words given.
- Showing "No" on the shared line when it is not shared, though it cannot occur while
  declared: fine. A line that can only say "Yes" would be a decoration.

## Nit

1. **Put the refusal above the standing note.** In `refusal-app-declared-360-light` the info
   note comes first and the Warning second. The Warning answers what the operator just did, so
   it should be the first thing under the heading, with the standing note after it.

No re-check needed.
