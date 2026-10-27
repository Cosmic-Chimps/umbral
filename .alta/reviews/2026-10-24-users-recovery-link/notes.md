# Users: the passkey invite says it is for recovery: notes for review

Task `2026-10-24-users-invite-button-says-recovery-r3cv`.
Branch `alta/faithful-atlas-chdr` (worktree `~/.alta/worktrees/umbral/faithful-atlas-chdr`),
on top of `main` at `6f0467e`.

**Commits.** `6f0467e..HEAD`: `5f5e38b` the change, tests and docs; `a0dda49` the passkey walkthrough fix, these notes and the captures; then the reviewers' files.

Copy only. Same route (`POST /admin/users/{id}/invite`), same invitation, same single reveal,
same notice. The wording is `umbral-designer`'s answer of 2026-10-26.

The security task `2026-10-24-strand-warning-trusts-the-url-slug-s7rd` was pending, not
running, when this started, so there was nothing to wait for.

## Texts.fs, old → new

| Field | Old | New |
| --- | --- | --- |
| `UserInvitePasskeyButton` | "Invite to add a passkey" | unchanged, now only on a row with no passkey |
| `UserRecoveryLinkButton` (new) | (the row showed "Invite to add a passkey") | "Create a recovery link" |
| `UserRecoveryLinkHint` (new) | none | "For a lost device: the link adds a new passkey to the same account. Remove the lost passkey afterwards." |
| `UserRecoveryLinkLabel` (new) | the copy field reused "Invite to add a passkey" | "Recovery link" |
| `UserInvitationLinkLabel` (new) | the copy field reused "Invite to add a passkey" | "Invitation link" |
| `UserPasskeyInvited` (notice) | unchanged, for both | unchanged |

In `src/Umbral/Handlers/Admin/Users.fs`, a row is a recovery row when it lists at least one
passkey. There the button reads `UserRecoveryLinkButton` and has
`aria-describedby="invite-<id>-hint"`, pointing at a `p.um-hint` under it
(`data-user-invite-hint`). The revealed link's label is `UserRecoveryLinkLabel` on a recovery
row and `UserInvitationLinkLabel` otherwise. No new CSS class (`space-y-1`, `um-hint` exist), so
`umbral.css` is not rebuilt.

## Tests

`tests/Umbral.Tests/PasskeyAdminTests.fs`: ``on a row with a passkey the invitation is a recovery
link, on a provider user's row an invitation``. Ana (passkey user) shows the recovery button, the
hint and the `aria-describedby`; Lea (signed in through the fake provider, no passkey) shows
"Invite to add a passkey" with no hint; inviting from each shows "Recovery link" and
"Invitation link" as the label, once. The existing invitation tests (`PasskeyInvitationTests`,
`PasskeyAdminTests`) pass unchanged. Full suite at `5f5e38b`: 1689 passed, 0 failed, 0 skipped (`dotnet test tests/Umbral.Tests`, 11 min).

## CHANGELOG, under `## Unreleased`, `### Changed`

> On the **Users** screen, the row of someone who already has a passkey offers **Create a
> recovery link** instead of **Invite to add a passkey**, with a line saying it is for a lost
> device, and the link it shows is labelled **Recovery link**. A row without a passkey keeps
> **Invite to add a passkey**, and its link is labelled **Invitation link**. The link and what it
> does are unchanged.

## README, "Recovery", and `docs/connectors/passkey.md`, "Recovery"

The README quotes "Create a recovery link", "Invite to add a passkey" and "Recovery link"
verbatim from `Texts.fs`. The walkthrough's Recovery section (added after the docs review) quotes
those, the hint, and "Invitation link" for a provider-only person. The 0.5.0 CHANGELOG entry
keeps the old button name as released history. Spec 008's files and the "Later: Users" sketch in
`design/contracts/apps-connectors-master-detail.md` still name "Invite to add a passkey": records
and a design note, not user docs, so left as they are.

## Captures (`<state>-<width>-<theme>.png`, 1280 and 360, dark and light)

| State | What |
| --- | --- |
| `user-with-passkey` | `/admin/users?search=Ana`: Ana, one passkey, the recovery button and hint |
| `user-without-passkey` | `/admin/users?search=Ben`: Ben, a GitHub user with no passkey |
| `link-revealed` | after **Create a recovery link** on Ana's row: notice, "Recovery link" field |
| `link-revealed-invitation` | after **Invite to add a passkey** on Ben's row: "Invitation link" field |

Made on a fresh Release instance on :18080 with `/tmp/umcap/capture-r3cv.mjs`. Ana and Ben
registered through invitations with a Chrome virtual authenticator; Ben then lost his passkey
(the operator's Remove) and got a GitHub account linked by a row written into
`linked_accounts`, which is what a GitHub sign-in writes. No horizontal overflow in any capture.
The theme control in the captures shows "Following your system" because the theme is set on
`<html data-theme>` by the script.

---

## Answers to the documentation review (`review-docs.md`, 96d8f32)

- **Blocking, `docs/connectors/passkey.md` Recovery names the old button.** Fixed. My search
  missed it because the phrase wraps across two lines there. The section now says **Create a
  recovery link**, quotes the hint, says the link is shown once under **Recovery link**, and names
  **Invite to add a passkey** and **Invitation link** for a person who signs in only with a
  provider. A wrap-aware search over the tracked `.md` and `.fs` files finds the old name only in
  `Texts.fs` (the provider case), the README (the same), the 0.5.0 CHANGELOG entry, spec 008 and
  the design contract's "Later" sketch.
- **Nit, "Send them the Recovery link".** Taken: "Send them the link shown under **Recovery
  link**."
