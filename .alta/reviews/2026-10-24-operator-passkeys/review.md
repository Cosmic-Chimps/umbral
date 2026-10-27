# Review: operator passkeys (spec 011), the Credential screen and the operator sign-in

Task: `2026-10-24-spec-011-operator-passkeys-0p5k`. Note: `design/contracts/operator-passkeys-ui.md`
(on the branch and in PR #7). Branch: `alta/earthy-orinoco-8bkw`, `853bf39..8b721d8`.
Reviewed 2026-10-27 by the design reviewer from the diff, the detector, a test run and the 47
captures. This review covers what the operator sees; the ceremonies, storage and session rules
are the spec's gates and the code review's.

## Verdict

**String check, 2026-10-27, at `20a81c2`: clear to merge.** The three nits are in as asked:
`PasswordNewLabel` "New password" and `PasswordConfirmLabel` "Confirm the new password" on the
Password card with setup's strings untouched; `SignInPasswordLabel` "Password" on the sign-in
page; the failed passkey sign-in notice as two codes chosen from the switch, the passkey button
naming its own code for the script. Checked in the diff; the captures were not retaken and need
not be for a label change.

First review at `8b721d8`: **clear to merge. No blocking items; three nits, all in `Texts.fs`,
which I would take on the branch before the merge since they are the two calls you left to me
plus one notice.** The four deviations in `notes.md` are all right.

## What was checked

- **Credential screen** against note §1: `max-w-2xl`; heading and lead as given; three cards
  in order. Passkeys: hairline rows with name, "Added · Last used" or "Never used", Remove as
  a red ghost; the add form with the name field, its hint, the Current password field and
  **Add a passkey** with **Use a passkey instead** beside it; "No passkey yet." when empty.
  Password sign-in: the On/Off badge beside the title, the sentence for each state, one small
  secondary button, the off button disabled with "Add a passkey first." while no passkey
  exists. Password: today's form, absent while off (`password-off`: two cards, no password
  field anywhere, the add form without its Current password field).
- **The re-check** against §2: Remove posts and the row expands into its confirm block at 422
  with the question, the Current password field, the small danger **Remove passkey** (the
  card's one red fill, only in that state), **Use a passkey instead**, and a ghost **Cancel**;
  focus on the field (`focus-confirm`). The switch's confirm blocks with the given text and a
  primary **Confirm with a passkey** (focus on it). While off, no field anywhere and the
  buttons run the ceremony. Refusals as Warning alerts under the heading.
- **The sign-in page** against §5: with both, the password form with the primary full-width
  Sign in, then "or", then **Sign in with a passkey** as a full-width secondary with the Key
  icon; with a passkey only, that button alone, primary, no divider; the no-JavaScript lines
  in both states (`signin-no-js`, `signin-no-js-password-on`).
- **Deviations**, all accepted: ceremony buttons `hidden` and `disabled` until scripted (so an
  Enter in the name field cannot post to the wrong handler); the script focusing a revealed
  `autofocus` control; the last passkey while off refused up front, before any ceremony (better
  than the note, which only listed the refusal); the rate limit is not a design matter.
- No stylesheet change; everything is existing components. Page titles "Credential | Umbral"
  and "Operator sign-in | Umbral". No overflow at 360 in any state; one red fill at most per
  card.
- Tests, stylesheet, detector: see the last section.

## Nits (catalogue only)

1. **The Password card's labels.** "New credential" and "Confirm credential" sit under a card
   titled Password with a lead that says password. Give the card its own two strings, "New
   password" and "Confirm the new password", and leave the setup screen's strings untouched
   (first-run setup sets the operator credential and is out of scope). The hint under the
   new-password field stays.
2. **The sign-in field label.** "Operator credential" under a lead that says "Use your
   password, or a passkey." Give the sign-in page its own string, "Password", and leave setup's
   label as it is.
3. **The failed passkey sign-in notice.** "…or use your password if it is on." asks the
   operator to know a state the page knows. Two strings: while on, "That passkey did not sign
   you in. Try again, or use your password."; while off, "That passkey did not sign you in.
   Try again." The not-added notice ("The passkey was not added. Try again.") is fine.

## Tests, stylesheet, detector

Run by the reviewer on the branch's worktree at `8b721d8`, 2026-10-27:
`dotnet test` with the Credential, Operator, AdminSignIn, Csrf and Rail filters: 154 passed,
0 failed.
`build-css.sh --check`: clean, `umbral.css` untouched by this branch.
Impeccable detector on `umbral.input.css`: one advisory, the pre-existing crop-shade colour;
nothing from this change.
