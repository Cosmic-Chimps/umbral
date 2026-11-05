# Code review: the passkey invite reads as a recovery link (task r3cv)

Branch `alta/faithful-atlas-chdr`, `6f0467e..acb35eb` (the change is `5f5e38b`). Reviewer: the `umbral` session, 2026-10-26.

## Verdict

**Clear to merge.** No blocking items, no nits.

## What was checked

- **`Users.fs` `row`:** `recovery = not data.Passkeys.IsEmpty` picks:
  - the button text ("Create a recovery link" or "Invite to add a passkey");
  - the hint, which is drawn only for recovery and linked with `aria-describedby`, its id built from the user id, as the existing `invite-{id}` ids are;
  - the revealed field's label ("Recovery link" or "Invitation link").

  The form's route (`/admin/users/{id}/invite`), its antiforgery input, `CanInvite` and the one-time reveal (`RevealedLink`) are unchanged, so behaviour is the same.
- **The Enable/Disable redraw:** `UserToggle.fs:82-92` already loads the user's passkeys into the row, so the htmx redraw keeps the right wording. `Users.fs:355` does the same for the screen.
- **`Texts.fs`:** four new strings, all in the catalogue, and nothing reused for two meanings any more (the field label no longer borrows the button's text).
- **The test:** the new test covers a passkey user (button, hint, `aria-describedby`, the revealed label) and a provider user (the old button, no hint), through the real invite POST, and checks there is one revealed link.
- **Tests run by the reviewer:** a full `dotnet test` at `acb35eb`: 1689 passed, 0 failed, 0 skipped.

## Merging

The implementer's session has ended. Commit this file and the designer's `review.md` on the branch before merging. Merge this branch before `alta/clever-byzantium-j92s` (the red-ghost Remove): the two only conflict in `CHANGELOG.md`, where both bullets are kept.
