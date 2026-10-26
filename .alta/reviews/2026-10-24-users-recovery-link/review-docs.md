# Documentation review: task r3cv, a passkey user's row offers a recovery link

Branch: `alta/faithful-atlas-chdr`, commit `5f5e38b` on `main` at `6f0467e`.
Reviewed 2026-10-26 by the documentation reviewer session, per section 6 of
`docs/audit-2026-10.md`, from the diff and `Views/Texts.fs` on the branch.

## Verdict

**Not yet clear. One blocking item, one nit.** Fix the blocking item, answer in
`notes.md`, and I re-check once.

## What was checked

- **README "Recovery" (lines 678-682).** The three quoted strings are verbatim:
  **Create a recovery link** (`Texts.fs:1211`, `UserRecoveryLinkButton`),
  **Invite to add a passkey** (`:1210`, `UserInvitePasskeyButton`),
  **Recovery link** (`:1214`, `UserRecoveryLinkLabel`). The rule behind them
  matches `Handlers/Admin/Users.fs`: `recovery = not data.Passkeys.IsEmpty`
  picks the button, the hint and the copy field's label.
- **CHANGELOG.** One bullet under `## Unreleased`, `### Changed`, between Added
  and Upgrade; it names both buttons, both labels and the hint, and says the
  link is unchanged. "Line saying it is for a lost device" is the hint
  `UserRecoveryLinkHint` (`:1212-1213`). The 0.5.0 entry that says "Invite to
  add a passkey" stays as released history, which is right: CI refuses a change
  to a released section, and it was true when released.
- **Links.** `check-links.py`: 182 links in 14 files, all resolve.

## Blocking

1. **`docs/connectors/passkey.md`, "Recovery", is now wrong.** It says: "When
   someone loses a device, open **Users**, find their row and click **Invite to
   add a passkey**." Someone who lost a device has a passkey on their row, so
   that row now shows **Create a recovery link** with the hint "For a lost
   device: the link adds a new passkey to the same account. Remove the lost
   passkey afterwards.", and the link is labelled **Recovery link**. The README
   points readers to this walkthrough for "details and gotchas", so the
   detailed text must say what the README now says. The section's last
   sentence ("The same button gives a person who signs in only with a provider
   a passkey of their own") is still true and can stay, with the button named:
   on a row with no passkey it reads **Invite to add a passkey** and the link
   **Invitation link**.

## Nit

1. **README: "Send them the Recovery link."** The label names the copy field;
   the sentence reads as if "Recovery link" were a thing to find elsewhere.
   "Send them the link shown under **Recovery link**" says where it is. Yours
   to take or leave.

---

## Re-check, 2026-10-26, at `a0dda49`

**Clear to merge.**

- Blocking item fixed. `docs/connectors/passkey.md`, "Recovery", now says
  **Create a recovery link**, quotes the hint verbatim (`UserRecoveryLinkHint`,
  `Texts.fs:1212-1213`), says the link is shown once under **Recovery link**,
  and for a provider-only person names **Invite to add a passkey** and
  **Invitation link**. All four strings match the catalogue.
- Nit taken: README:680 "Send them the link shown under **Recovery link**."
- The old button name now appears only where it should: the catalogue, the
  README's provider-case parenthesis, the CHANGELOG (the 0.5.0 history and the
  new Changed bullet naming what it replaced), specs/008 and a design note.
- Link check: 182 links in 14 files, all resolve.
