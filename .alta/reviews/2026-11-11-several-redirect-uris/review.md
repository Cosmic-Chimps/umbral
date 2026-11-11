# Design review: several redirect URIs on an app's pane (spec 012)

Task r3du. Branch `alta/primal-zanzibar-dfrt`, `d6d2373..3650f8f`. Reviewed 2026-11-11 by the
design reviewer from `notes.md`, the detector, the stylesheet check and six of the captures
(`several` at 1280 dark and 360 light, `one-uri` 1280 light, `full` 360 dark, `focus-remove`,
`refused-duplicate` 360 light). The other notice states use the pane's standard alerts and
were not opened.

## Verdict

**Clear to merge. No blocking items; three nits, each optional.**

## The three points left to me

- **Remove's colour: a red ghost, yes.** Removing an address can be undone by adding it again,
  so it is Remove logo's and Revoke's case, and Delete app stays the pane's one red fill.
- **The add field's width: the full column, as built.** A redirect URI is long; `max-w-xs`
  would hide what is being typed.
- **The labels: `um-label`, yes.** The two lists are fields inside Connection, not sections.

## What was checked

- Only the Connection section changed. Client ID and Issuer URL as before; then the two lists
  in mono with a Remove per row, the hints, and the add field with its small secondary Add.
- The only redirect URI has no Remove and says why; an empty post-logout list says where
  people land; a full list replaces its add form with the full-list note.
- A long URI wraps beside its Remove at 1280 and at 360 with nothing overflowing; Remove stays
  vertically centred on its row.
- Focus ring visible on Remove (`focus-remove`); Remove's accessible name carries the URI.
- Refusals are Warning alerts under the pane heading, successes Success alerts.
- The contract's §2 item 3 is amended to match.
- Detector on `umbral.input.css`: only the pre-existing crop-shade advisory. `build-css.sh
  --check`: clean. No component class added.

## Nits

1. **The list hint reads as the add field's preamble.** It sits after the list and 8px above
   the "Add a redirect URI" label, and on a one-URI app it follows the last-one hint, so two
   hints stack (`one-uri-1280-light`). Make it the add field's own hint: under the field, tied
   to the input with `aria-describedby`. On a full list, where there is no field, leave it
   under the list as now. Same for the post-logout list.
2. **At 360 the Add button fills the row.** The pane's other small buttons keep their own
   width at 360 (Save sign-in methods, Save logo). Let Add do the same, left-aligned under the
   field.
3. **A refused address is not kept in the field.** After `refused-duplicate` the field is
   empty, which is fine for a duplicate; if the same holds for `app-redirect-uri-invalid`, the
   operator retypes a long address to fix one character. If the redirect can carry the typed
   value back for the invalid case only (it is not a secret), do; if that reaches beyond this
   task, leave it and I will queue it.

No re-check needed for nits 1 and 2 beyond `one-uri` and `several` at 360 light.

## Answers (implementer, 2026-11-11)

1. Taken. Each list's hint is now the add field's own hint, under the field, with
   `aria-describedby="<field id>-hint"`. On a full list it stays under the list, above the
   full-list note. All captures were retaken. `one-uri-360-light` and `several-360-light` show it.
2. Taken. Add is `self-start sm:self-auto`: its own width and left-aligned at 360, and beside
   the field from `sm` up.
3. Left for you to queue. Carrying the typed value back means stashing it through the
   single-use `NoticeReason` cookie (never the URL) and reading it back in the pane. That is a
   small change of its own, beyond this task.
