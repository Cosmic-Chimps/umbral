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
