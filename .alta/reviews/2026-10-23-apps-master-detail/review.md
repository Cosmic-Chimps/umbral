# Review: the Apps screen as a list and a detail pane

Task: `.alta/tasks/2026-10-23-apps-screen-as-a-list-and-a-detail-pane-m4st.md`
Contract: `design/contracts/apps-connectors-master-detail.md`, sections 1 and 2.
Branch: `alta/savvy-severn-f6f6`, commits `351d134` (merge of main 0.4.0 and h3cd), `484dfa3`
(the work), `66dc102` (this folder). Reviewed 2026-10-23 by the design reviewer from the diff,
the detector, a test run, and the 34 captures.

## Verdict

**Re-check, 2026-10-23, at `3baf6b3`: clear to merge.**

- Item 1 (blocking) fixed: `pageTitle` names the app on its URL ("<app> · Apps | Umbral"),
  "Create app" on the create URL, "Apps" on the list; the "own URL" test asserts all three; the
  implementer saw the tab title follow an htmx click in Chrome, which is htmx 4's default.
- Item 2 fixed: the reveal keeps the success notice, the once-only warning and the Client
  secret; the Client ID is gone from it (`detail-secret` captures replaced, checked).
- Item 3 fixed: the dialog token is out of DESIGN.md's frontmatter and the three sentences are
  reworded; the shapes line now names the list's entries.
- Item 4 taken: `noticeKind` draws the six refusal codes as Warning in all three places a notice
  is drawn, with a test; `create-refused` captures added and checked (a warning above the form
  at 360 light).
- Tests and stylesheet at `3baf6b3`: see the last section.

For the Connectors task: nothing in the frame changed in the fix round.

---

First review, 2026-10-23, at `66dc102`:

**Not yet clear: one blocking item, three nits.** The frame is right, and the eight decisions in
`notes.md` all stand (two of them correct the contract, see the end). Fix item 1, apply the nits
you agree with, replace the captures that change (`detail-secret`, `create` after a failed
create if you take nit 3), and I re-check once.

## What was checked

- Diff against the contract: the frame grid at `lg` with a 320px list and 32px gap; entries
  drawn like the rail's with two lines and one marker; the selected entry `aria-current="page"`
  on the high step with its title in gold at 600; the pane a `section` labelled by its `h2`
  with `tabindex="-1"`; sections on the 40% hairline with 24px above and 20px below; the
  closing row the same; the pressed Create button; the back link as a small ghost, `lg:hidden`,
  first in the pane. Pane order: heading, item notices, Connection, Sign-in methods, Sign-in
  page branding (whole, as merged), the closing row.
- URLs and redirects: `/admin/apps`, `/admin/apps/new`, `/admin/apps/{id}`; every app POST
  lands on the detail URL, delete on the list, a failed create on the create URL; unknown id is
  404 with the list and no selection; the `#app-` anchor scheme is gone.
- htmx: `frameNav` sets `hx-get`, `hx-target` and `hx-select` to `[data-frame]`, `outerHTML`,
  `hx-push-url`; the full page is returned. `umbral.js` listens for `htmx:after:swap`, which is
  the htmx 4 name (checked in the bundled file), moves focus to the pane heading or the first
  entry, and restarts the accent forms. The dialog code and styles are gone with nothing left
  pointing at them.
- Accessibility: the list is a `nav` with its label; the marker is `role="img"` with a name;
  focus ring on an entry visible in `focus-entry`; one `aria-current` on a detail URL.
- Captures: at 1280 the two columns read as one navigation with the rail; at 360 the list
  alone, then the pane with the back link; the long redirect URI wraps inside the pane; the
  empty list shows the create form in the pane; no enabled connector and no apps replaces the
  frame; one red fill per pane; the item notice sits under the heading in the pane, not at the
  page top.
- DESIGN.md: the Dialog component section is gone and a "List and detail" section added that
  matches the shipped code. See nit 2 for what was left behind.
- Tests, stylesheet, detector: see the last section.

## Blocking

1. **An app's own URL has the screen's title, not the app's.** `Apps.fs` line 993 passes
   `t.AppsTitle` for every URL, so `/admin/apps/{id}` is titled "Apps | Umbral" in the tab,
   history, bookmarks and the screen reader's page announcement, and after an htmx swap the
   document title cannot change because the response's title is the same. The contract's
   premise is that the item's URL is its address; an address needs its name. Fix: the title is
   the app's display name on a detail URL ("Album Share | Umbral", or "Album Share · Apps |
   Umbral" if you prefer the screen in it), "Create app | Umbral" on the create URL, and
   "Apps | Umbral" on the list. htmx 4 takes the `<title>` from the swapped response by
   default, so the tab follows the selection with no extra script; check that it does. Add an
   assertion to "an app's own URL is a page with that app's pane".

## Nits

2. **The secret reveal repeats the Client ID four lines above Connection.** In `detail-secret`
   the reveal block shows Client ID and Client secret, and the Connection section right under
   it shows the Client ID again. That was fine when the reveal was a card above the list and the
   app's card was elsewhere; in the pane it is the same field twice on one screen. Keep the
   success alert, the once-only warning and the **Client secret** field in the reveal; drop its
   Client ID. The test "the secret is shown once on the new app's page" should still hold.
3. **DESIGN.md still carries the dialog it says is gone.** The frontmatter has a `dialog`
   component (line 133), and the prose mentions the dialog at lines 217, 343 and 359. Remove the
   token and reword those three sentences; the sidecar has no dialog component, so it is already
   right.
4. **A failed create is drawn as a success alert.** `/admin/apps/new?msg=app-name-required`
   (and the other refusals that `fail` sends) render through `Components.alert Components.Success`
   (lines 704, 959, 983). You noted this is pre-existing, which it is, and the contract said
   not to change what forms do, which this does not: it is the alert's kind. Now that the form
   and its refusal sit together in the pane, a green tick above "Name is required" is wrong.
   Suggest a small `noticeKind code` in `Apps.fs` that returns Warning for the refusal codes
   (`app-name-required`, `app-type-invalid`, the redirect URI refusals) and Success otherwise,
   like `brandingAlertKind` already does for the branding notices. If you would rather keep this
   out of the task, say so and I will queue it separately; it does not block.

## Decisions in notes.md

All eight stand. Two of them show the contract was wrong or ambiguous, and I will amend it,
not the code:

- Decision 1 (no enabled connector but apps exist: keep the frame without Create, empty state
  above): right. Hiding existing apps' Delete and Regenerate was never intended. Contract §2
  will say so.
- Decision 3 (entry title in bone at rest, second line muted): right, and the captures prove
  it: the list scans better than muted-everything would, and it is what §1's entry definition
  said before its "Rest: text muted" line contradicted it. Contract §1 will say "rest: title in
  bone, second line muted; hover adds the container step".
- Decisions 2 (accent sheet inside the pane), 4 (pressed Create drawn as selected), 5 (sr-only
  legend in the pane), 7 (first app `aria-current` on the list URL even where the pane is
  hidden), 8 (`data-screen="app-new"` kept): accepted as written.

## For the Connectors task

Reuse `Components.frame`, `frameList`, `frameEntries`, `frameEntry`, `framePane`,
`paneHeading`, `paneSection`, `paneActions`, `markerIcon`, the `.um-entry` and `.um-pane-*`
classes and the focus and accent hooks in `umbral.js` as they are. Nothing in this review asks
for a change to them.

## Tests, stylesheet, detector

Run by the reviewer on the branch's worktree, 2026-10-23.

At `3baf6b3` (re-check): `dotnet test` with the Apps, AppBranding, AppSecret, AppDelete, Csrf
and Rail filters: 88 passed, 0 failed (the implementer reports the full suite at 1404 passing).
`build-css.sh --check`: clean.

At `66dc102` (first review): the same filters without Rail: 76 passed, 0 failed.
`build-css.sh --check`: clean, `umbral.css` is current.
Impeccable detector on `umbral.input.css`: one advisory, the pre-existing crop-shade colour;
nothing from this change.
