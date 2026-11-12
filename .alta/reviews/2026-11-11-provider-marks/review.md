# Design review: provider marks on the sign-in buttons, part A

Task b6d7. Branch `alta/bold-dove-crws`, `aad649e..b329dbe`. Contract:
`design/contracts/provider-marks.md`. Reviewed 2026-11-11 by the design reviewer from the
stylesheet diff, DESIGN.md and three of the 13 captures (`picker-1280-dark`,
`picker-360-light`, `picker-accent-1280-light`). The last-used and focus captures were not
opened; those behaviours are unchanged by this part and were reviewed with spec 013.

## Verdict

**Part A: clear to merge. No blocking items, no nits.**

## What was checked

- GitHub leads with the Invertocat at the icons' size, white on the dark secondary button,
  black on the light one, and the accent's white on an accent fill. The fills are set
  explicitly for the secondary button, as the contract asked.
- The passkey button leads with the Key icon; Discord and Google keep the link icon; Apple
  is unchanged, title alone, black or white, and does not take the accent.
- All buttons keep one size, radius and typeface, and the labels stay centred with their
  marks.
- DESIGN.md's Icons section carries the rule. No new strings. The existing `#000` / `#fff`
  ignores cover the stylesheet.

## Part B

The operator-supplied Apple logo is not built and waits for the maintainer's yes. Its
captures (`apple-pane-*`, `apple-logo-refused`, the picker with the logo) and its review come
with it. For those, give the accent app an app logo as well, so the branded page is seen
whole.

# Part B: the Apple logo pasted by the operator

Commits `12ce8b7..dc93dcf`. Reviewed 2026-11-12 from four captures (`apple-pane-logo-1280-dark`,
`apple-logo-refused-360-light`, `picker-accent-1280-light`, `picker-last-used-360-dark`). The
logo in every capture is a stand-in shape, not Apple's artwork, so how the real file sits in
the button is not something this review can settle.

## Verdict

**Not yet clear: one blocking item, one nit.**

## What is right

- The pane's new section sits between Settings and the closing row with its own form, lead,
  textarea, hint and small secondary Save logo, in the contract's words; without a logo, the
  "No logo yet" line.
- Once saved: the preview panel with the button as people see it in the current theme, the
  field as Replace the logo with its hint, Remove logo as a red ghost. Delete stays the pane's
  one red fill.
- The refusal is under the field at 422 with the field marked and the pasted text not drawn
  back.
- On the picker the logo leads the Apple button at full height, the button stays black or
  white on an accent page, and the app's logo above is untouched.
- A blank paste keeping the saved logo with no notice: right.

## Blocking

1. **The Last used badge touches the title.** With the button's gap set to 0, the gap between
   the title and the badge went too: `picker-last-used-360-dark` reads "Sign in with
   Apple[Last used]" with no space. Only the space between the logo and the title should
   close. Keep the button's own 8px gap and take it back on the slot (a negative right margin
   of 8px on `.um-apple-logo` when it holds a logo), or wrap logo and title in one group with
   no gap. Check the badge on the black button in the light theme as well.

## Nit

2. **Save logo and Remove logo sit on two rows.** In `apple-pane-logo-1280-dark` Remove logo
   is alone under Save logo. Put them on one row (`flex flex-wrap items-center gap-2`), as the
   branding section's Save logo and Remove logo are.

## Provisional, for the maintainer

The group's centring and the logo's distance from the title were judged on a stand-in drawn in
a 31 by 44 box. When Apple's real file is pasted on a live instance, look once at the button in
both themes; if the logo sits too far from or too close to the title, the adjustment is the
slot's margin, nothing else.

## Re-check

`picker-last-used` at 360 dark and 1280 light, and `apple-pane-logo` at 1280 dark.
