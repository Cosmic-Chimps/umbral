# Review: app card section title and the closing action row

Task: `.alta/tasks/2026-10-22-app-card-section-headings-and-final-action-row-spacing-h3cd.md`
Branch: `alta/carefree-sunflower-jc4t`, commits `0906ce6` and `ea32c56` on `main` at `0889378`.
Reviewed 2026-10-23 by the design reviewer from the diff, the detector, a test run, and the
eight captures in this folder.

## Verdict

**Clear to merge.** No blocking items, no nits.

This task was written before the master-detail contract
(`design/contracts/apps-connectors-master-detail.md`, PR #5) and the contract meant to absorb
it. The branch did what the contract specifies, with the same names: `.um-section-title` as
body large at 600 in bone, the `h3` on it, the closing action row behind the same 40% hairline
the sections use. So it should **merge first**, and the Apps master-detail task builds on it
instead of redefining either piece. Nothing here needs to change for that.

## What was checked

- Diff against the task: one new component class, no utilities in the view; the branding
  heading uses it; the action row gains `border-t border-outline-variant/40 pt-4` and a
  `data-app-actions` hook; `umbral.css` regenerated.
- The "Sign-in methods" legend stays `.um-label`, which is right: it is a fieldset legend
  shared with the Create app form, not a section title. The master-detail task gives that
  group a section title of its own in the pane.
- DESIGN.md and the sidecar updated in step: label small is the field label, body large at 600
  is the section title, card sections and the closing row share the hairline. The edits are
  small and true to the shipped code, which is how the file is meant to be kept.
- Contrast: bone on the low step is the body text pair, already at the floor.
- Captures at 1280 and 360, both themes, with and without a logo: the title sits one step
  above LOGO and ACCENT COLOUR and one below the card's name; the action row closes the card;
  one filled red button per card.
- CHANGELOG: one Fixed bullet under Unreleased.
- Impeccable detector on `umbral.input.css`: see the last line.
- Tests and stylesheet check: see the last line.

## Tests, stylesheet, detector

Run by the reviewer on the branch's worktree at `ea32c56`, 2026-10-23:
`dotnet test tests/Umbral.Tests --filter "FullyQualifiedName~AppBranding"`: 43 passed, 0 failed.
`build-css.sh --check`: clean, `umbral.css` is current.
Impeccable detector on `umbral.input.css`: one advisory, `design-system-color` on the crop
box's shade `rgb(0 0 0 / 0.6)`, which predates this branch (2026-10-15) and is a deliberate
overlay, not a palette colour. Not a finding against this task; a later pass may record it as an
ignore-value with that reason.
