# Review: the Connectors screen as a list and a detail pane

Task: `.alta/tasks/2026-10-23-connectors-screen-as-a-list-and-a-detail-pane-c0nn.md`
Contract: `design/contracts/apps-connectors-master-detail.md`, sections 1 (as amended) and 3.
Branch: `alta/urbane-zambezi-t9pn` from `main` at `1f3a89e`; commits `bbdb4a8` (the work) and
`c027173` (Revoke as a red ghost, this folder). Reviewed 2026-10-23 by the design reviewer from
the diff, the detector, a test run, and the 37 captures.

## Verdict

**Clear to merge. No blocking items; two nits, both pre-existing, that may land on this branch
before the merge or go to a queued task.** All nine decisions in `notes.md` stand.

## What was checked

- The frame is reused unchanged: no diff in `Components.frame*`, `pane*`, the `.um-frame`,
  `.um-entry`, `.um-pane-*` rules or the frame code in `umbral.js`. The two additions are
  outside it and small: one rule muting a not-set-up entry's title at rest (hover and selection
  undo it), and the pinned hairline as a presentational `div` between two entry groups.
- List order against §3: configured connectors in kind order then slug, the kinds not set up
  (muted, "Not set up"), Passkey, then the hairline and **Allowed email domains** with its
  catalogue line. Markers: the state badge, or the Warning icon with "Needs attention" replacing
  it for a connector offered nowhere or with an unreadable store; nothing on a kind not set up
  or on the pinned entry. Checked in `list-first-selected` and `attention`.
- Panes against §3: provider (heading, slug as mono hint, pills; notices; Set-up with the naming
  note, the callback URL with copy, the caveat where there is one, the rule sentence; Settings
  with the form and the full-size primary Save; the closing row with Disable and Delete); a kind
  not set up (the same without a closing row, "Not set up" pill); Passkey (Set-up, Settings,
  Invitations, closing row); add at `/new`; domains at `/domains` with one untitled section
  and its primary Save.
- URLs and redirects: `/admin/connectors`, `/<slug>`, `/new`, `/domains`; every connector POST
  lands on its detail URL, delete and unknown on the list, add refusals on `/new`, passkey
  actions on `/passkey`, the domains save on `/domains`; in-place refusals redraw the pane with
  422; unknown slug is 404 with the list. `new` and `domains` refused as slugs.
- htmx: entries, the pinned entry and Add swap the frame and push; the toggles swap the frame
  without pushing, so the badge changes in list and pane together (`toggle` captures).
- One red fill per pane, including Passkey after `c027173` (Revoke as a red ghost, with its
  hover class). The strand and removal confirmations keep their red fill inside their notice:
  they are the irreversible step, shown only after a refusal, as on Apps.
- Titles name what the URL shows; focus lands on the pane heading after a swap (implementer's
  browser notes, consistent with the Apps task I checked).
- Refusal notices are warnings, as on Apps after its review.
- DESIGN.md "List and detail" names Connectors and records the muted entry and the pinned
  hairline in two sentences. Texts.fs changes are all wording that said "card".
- Captures: no overflow at 360 (the add form, the domains pane, the Discord pane); the pinned
  entry reads as apart from the connectors without looking like a different control; the
  muted Google entry reads as "not yet" rather than disabled.
- Tests, stylesheet, detector: see the last section.

## Nits (pre-existing, made visible by the pane)

1. **"For A new person."** An open invitation's line joins `PasskeyInvitationForLabel` ("For")
   and `PasskeyInvitationForNew` ("A new person") with a space, so the capital A sits
   mid-sentence. Make the second string "a new person", or give the item one string "For a new
   person" and another "For <name>". Catalogue only.
2. **The enrolment radios are native controls.** `enrolmentRadio` draws a bare
   `input type="radio"`, which in the light theme is the system's blue, the one blue on the
   page. The smallest fix is `accent-color: var(--color-primary)` on `input[type="radio"]` and
   `input[type="checkbox"]` in the base layer, which also covers any future native control;
   the fuller one is the `.um-choice-radio` drawing. Either keeps the labels and hints as they
   are.

If you take them here, send me the commit and I will look at the two lines; no full re-check
is needed for them.

## Decisions in notes.md

All nine stand:

1. Count of connectors set up, Passkey included, "No connector set up yet" at zero: right; it
   counts what the operator has done, not rows.
2. `new` and `domains` reserved, refused as "already taken", no migration: right.
3. Refusal notices as warnings: right, and consistent with Apps.
4. Revoke as a red ghost: right. Inviting again undoes it, so it is Remove logo's case, and
   Delete stays the pane's one red fill. The in-notice confirmations keep theirs.
5. Passkey offered nowhere gets the same note as a provider: right; the marker needs something
   to point at.
6. Invite someone as a small secondary, Save connector and Enable passkeys full-size primary:
   right, exactly §1's rule.
7. "Open invitations" as a `.um-label` inside Invitations: right; a second title there would
   outrank the section's.
8. First entry current on the list URL at every width: as Apps, as §5.
9. No item notice on the list URL's pane: right; those three notices are about the list.

## For the Fediverse task

Its connector becomes a pane of this frame per `design/contracts/fediverse-connector-ui.md`
section 1: entry after the kinds not set up and before Passkey; pane sections Set-up (lead,
callback URL, the no-email note), Settings (shared, instance scope, servers, Save), closing
row. Nothing in this review changes that.

## Tests, stylesheet, detector

Run by the reviewer on the branch's worktree at `c027173`, 2026-10-23:
`dotnet test` with the Connector, Passkey, AllowedDomain, Csrf and Rail filters: 421 passed,
0 failed (the implementer reports the full suite at 1425 passing at `bbdb4a8`).
`build-css.sh --check`: clean, `umbral.css` is current.
Impeccable detector on `umbral.input.css`: one advisory, the pre-existing crop-shade colour;
nothing from this change.
