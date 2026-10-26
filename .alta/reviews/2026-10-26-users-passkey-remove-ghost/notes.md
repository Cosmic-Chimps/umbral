# Users: a passkey's Remove is a red ghost: notes for review

Task `2026-10-26-users-a-passkey-s-remove-is-a-ghost-button-in-the-error-colo-grbb`, raised by
`umbral-designer` in `.alta/reviews/2026-10-24-users-recovery-link/review.md` ("The One
Red Fill Rule", DESIGN.md). Branch `alta/clever-byzantium-j92s`, on top of `main` at `6f0467e`.

## Change

`src/Umbral/Handlers/Admin/Users.fs`, the passkey list in `row`: the per-passkey **Remove** is
`btnSm Ghost + " text-error hover:text-error"` (as Remove logo and Revoke) instead of
`btnSm Danger`. Same form, same route, same confirm (`UserPasskeyRemoveConfirm`). The card's
**Delete** stays the one red fill. No copy change, no new CSS class (`text-error` and
`hover:text-error` are already in `umbral.css`), so `umbral.css` is not rebuilt.

## Test

`tests/Umbral.Tests/PasskeyAdminTests.fs`, ``the Users rows show the passkey pill and list``: Ana's
card has exactly one `um-btn-danger`, and the button in her passkey row has `um-btn-ghost` and
`text-error`. No other test counted danger buttons on the Users screen. `PasskeyAdminTests`: 11
passed.

## CHANGELOG, under `## Unreleased`, `### Fixed`

> On the **Users** screen, a passkey's **Remove** is a quiet button in the error colour instead of
> a second red one beside the user's **Delete**: removing a passkey can be undone by adding
> another, deleting the user cannot.

## Captures (`<state>-<width>-<theme>.png`, 1280 and 360, dark and light)

| State | What |
| --- | --- |
| `user-with-passkey` | `/admin/users?search=Ana`: Ana, one passkey ("Phone"), Remove beside it |

Fresh Release instance on :18080, `/tmp/umcap/capture-grbb.mjs` (Ana registered through an
invitation with a Chrome virtual authenticator). Measured in each capture: one `.um-btn-danger` in
the card, Remove has a transparent background and the error colour (`rgb(255, 143, 126)` dark,
`rgb(179, 38, 30)` light), no horizontal overflow. The button still reads "Invite to add a
passkey" because task r3cv (`alta/faithful-atlas-chdr`) is not merged into `main` yet.
