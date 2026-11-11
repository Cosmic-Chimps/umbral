# Design review: Sign in with Apple (spec 013), the pane and the sign-in button

Task ap1e. Branch `alta/indigo-pheasant-5jd9`, `25f3718..8fd0a41`. Reviewed 2026-11-11 by the
design reviewer from the design-review notes, the stylesheet and DESIGN.md diff, the detector
and six of the 36 captures (`pane-not-set-up-1280-dark`, `refused-key-360-light`,
`pane-set-up-1280-light`, `picker-accent-1280-light`, `picker-last-used-360-dark`,
`picker-focus-apple-1280-dark`). Apple's guidelines were not read by me; the button's rules
rest on the implementer's reading of them.

## Verdict

**Not yet clear: one blocking item, three nits.**

## What is right

- The pane follows the answers: field order, Team ID and Key ID side by side in mono, the key
  in a `.um-textarea`, the saved key as dots alone with the replace field under it, the caveat
  in the agreed words, errors under the field at 422 with typed values kept and the key not
  drawn back.
- The button: fixed title, black in the light theme and white in the dark, unchanged on an
  accent page while the others take the accent, same size and radius, the Last used badge
  legible on the white fill, the focus ring visible, the logo slot empty and taking no space.
- DESIGN.md records the exception in Buttons and in the Fill Pair rule.
- No overflow at 360 in the states opened.

## Blocking

1. **The naming advice states something not confirmed.** The Set-up note says "People see
   that name on Apple's consent screen". You could not confirm which name Apple shows for a
   Services ID, and the product does not state what it has not checked. For Apple, replace the
   generic advice with a sentence that claims nothing about Apple's screen, for example:
   *"Give the Services ID a description that names this Umbral instance (for example "Umbral —
   home"), so you can tell it apart on Apple's site later."* If someone confirms what Apple
   shows, the stronger sentence can come back with its source.

## Nits

2. **Record the detector's four findings as an ignore.** `.um-btn-apple` sets `#000` and
   `#fff`, which the detector reports as colours outside DESIGN.md. They are deliberate and
   documented. Add value ignores with the reason, so the next run is quiet:
   `impeccable hooks ignore-value design-system-color "#000" --file src/Umbral/Assets/umbral.input.css --reason "Agent: Apple's rules fix the Sign in with Apple button to black and white; DESIGN.md Buttons"`
   and the same for `"#fff"`. If the CLI is not at hand, say so and I will add them.
3. **The reason-less notices.** Fine as a fallback. Check that each still ends in the next
   action ("Check the key and save again", "Try Enable again") and not only in what failed.
4. **The display name's refusal** ("Enter a name for this connector, then save again."): end it
   "then try again.", as the Apps notice now does.

## Your four points

1. Wordings: the display name hint is right; the refusal per nit 4; the secret-store notice
   is right; the reason-less forms per nit 3; the naming advice is the blocking item.
2. Apple first on the picker because of the identifier order: leave it. Nothing should place
   it specially, and Last used already reorders for the person.
3. Outside this change, to queue: `Texts.HomeLead` should name Apple once this merges, and
   PRODUCT.md's lists of connector kinds lack Fediverse and Apple. I will pass both to the
   queue session.
4. The logo slot: as agreed; the artwork is the maintainer's (task pqzq). Until it is in, the
   spec should say the connector is not ready for an App Store submission.

## Re-check

`pane-not-set-up` at 1280 dark and 360 light after the blocking fix, and the commit.

## Answers (implementer, 2026-11-11)

1. **Blocking, taken.** The Apple pane has a set-up note of its own, your sentence:
   "Give the Services ID a description that names this Umbral instance (for example "Umbral —
   home"), so you can tell it apart on Apple's site later." (`Texts.AppleNamingAdvice`, chosen in
   `Connectors.providerPane` for the Apple kind; the other providers keep the generic advice.)
   A test asserts the sentence is on the pane and that "consent screen" is not. The contract
   records it.
2. **Not done here: the detector's CLI is not on this session's PATH** (`impeccable` is not
   found, and `.impeccable/` in the worktree holds only `config.json` and `design.json`). Please
   add the two value ignores for `#000` and `#fff` in `src/Umbral/Assets/umbral.input.css`
   with your reason, as you offered.
3. **Taken.** Three of the four reason-less notices already ended in the next action
   ("Press Enable to try again.", "Try again in a moment.", "Paste the .p8 key again under
   Replace the private key."). The secret-store one did not: it now ends "Check the store,
   then press Enable again.", with and without its reason.
4. **Taken.** "Enter a name for this connector, then try again."

The walkthrough's table of messages and the contract follow the new wording. All captures
were retaken after the fix; the two you asked for are `pane-not-set-up-1280-dark.png` and
`pane-not-set-up-360-light.png`. Affected tests pass (113 in the Apple admin, connector and
meta files); the link check passes.
