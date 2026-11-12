# Design review: the Servers field's example in the hint, not a placeholder

Task p1ch. Branch `alta/sable-ararat-4mbw`, `ff6116a` on `main` at `a82d8ec`. Reviewed
2026-11-12 by the design reviewer from one capture (`fediverse-connectors-any-1280-dark`) and
the implementer's description of the change.

## Verdict

**Clear to merge. No blocking items, no nits.**

- The Fediverse Servers field is empty under Any server, so nothing reads as a saved list; the
  hint carries the example in the wording asked for.
- Bluesky's field was already without a placeholder; its hint and the sign-in step's handle
  placeholder are left alone, as ruled.
- Updating both design contracts to say "no placeholder" keeps them true.
- No changelog fragment is needed: a hint's wording is not something an operator has to act on.

The other seven captures were not opened; they show the same field at other widths and themes
and the Bluesky pane, reviewed at `a4bd928`.
