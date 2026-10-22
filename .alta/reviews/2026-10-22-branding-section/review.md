# Review: the app card's "Sign-in page branding" section

Task: `.alta/tasks/2026-10-22-sign-in-page-branding-section-layout-and-remove-logo-button-b7rn.md`
Branch: `alta/roomy-flamingo-ms3r`, commits `195ed76` and `16d445f` on `main`.
Reviewed 2026-10-22 by the Claude Code reviewer session, from the diff, the Impeccable
detector, a test run, and the eight captures in `/tmp/b7rn/shots/` (1280 and 360 px, dark and
light, with and without a logo).

## Verdict

**Re-check, 2026-10-22, at `c2f0ef0` plus `a00e9ca`: clear to merge.**

- Item 1 (blocking) fixed: the `noscript` sits inside the hint paragraph and `aria-describedby`
  names one id. The new test strips `noscript` content and checks every described id against
  what remains, which is the right shape for it.
- Item 2 fixed: `hover:text-error` is on the button and compiled into `umbral.css`.
- Item 3 fixed: the label reads "Preview of the sign-in page", changed in `Texts.fs` only.
- Item 4 left for the maintainer, as agreed.
- Captures: `card-360-dark` and `card-360-light` show one filled red button on the card (Delete
  app) with Remove logo as a red ghost beside Save logo; `crop-open-360-dark` shows Save logo and
  Use whole image on one line and Remove logo wrapped below with the 8px gap, which is the
  intended `flex-wrap` behaviour at 360. The section captures at 1280 and 360 in both themes
  match the first review's.
- Tests and stylesheet: see the line at the end of this file.

**For the maintainer, outside this task** (from the implementer's `notes.md`, and I agree): on the
app card the Regenerate secret / Delete app row sits 16px under the accent form, closer than the
20px between groups inside the branding section, so it can read as part of that section. With
nit 4 (the section heading styled like its field labels), that is one card-level task: a
section heading one step above `um-label`, and a hairline or larger gap above the card's final
action row.

---

First review, 2026-10-22, at `195ed76` plus `16d445f`:

**Not yet clear. One blocking item, three nits, two captures to add.** The change does what the
task asked and stays inside the Umbra look. Fix the blocking item, apply the nits you agree
with, add the captures, and I re-check once.

## What was checked

- Diff against the task: Remove logo shares the Save logo row through the `form` attribute on a
  hidden delete form that keeps the antiforgery token and `data-confirm`; it renders only when a
  logo exists; the ghost button carries `text-error`; the section is `space-y-5` with `space-y-3`
  inside the forms; the no-JavaScript sentence moved to a `noscript`; the preview is a
  `surface-container` panel that stacks the logo over a full-width sample button, which is how
  `Picker.fs` composes the sign-in card (`max-w-sm`, `w-full justify-center`, `signInLogoClass`).
  The native file input's button is dressed as a small secondary button (`.um-file`).
- Confirm still fires: `umbral.js` listens for `submit` in the capture phase on the document,
  and a button with `form=` dispatches `submit` on that form. Implicit submission of the upload
  form (Enter in a field) is unaffected, because the Remove button's form owner is the delete
  form.
- Spacing: `hidden` on the delete form keeps it out of `space-y-5`, which excludes `[hidden]`.
- Impeccable detector on `umbral.input.css`: no findings. The views are F#, which the detector
  does not scan; the rendered output was judged from the captures.
- Hooks for tests and scripts: all ten listed in the task are still present, plus
  `data-logo-remove`.
- Captures at 1280 and 360, both themes: the row holds, the hint wraps at 360, the preview panel
  reads as the sign-in card, the accent and Umbral's black-or-white text choice apply to the
  sample, and the dark-theme default (gold on ink) and light-theme default (gold on near-black)
  are right when there is no accent.
- Tests and stylesheet check: see the line at the end of this file.

## Blocking

1. **`aria-describedby` points at an id that does not exist when JavaScript is on.**
   `Apps.fs`, the file input: `aria-describedby="{hintId} {hintId}-noscript"`. The `-noscript`
   paragraph is only in the DOM for browsers without scripts, so every other browser has a
   dangling reference. Screen readers ignore the missing id, but it fails HTML validation and
   the accessibility checks CI may add later (spec 007 FR-017 names accessible names as a
   requirement for this product). Fix: keep one id and put the `noscript` inside the hint
   paragraph, since `noscript` is phrasing content and may sit in a `p`:

   ```fsharp
   p(id = hintId, class' = "um-hint") {
       t.LogoHint
       noscript() { raw " "; t.LogoHintNoScript }
   }
   ```

   Then `aria-describedby = hintId` as before, and the no-JavaScript sentence joins the
   description only when it is rendered. Keep the one-sentence `LogoHint` and the separate
   `LogoHintNoScript` text; only the markup changes. Add an assertion that the input's
   `aria-describedby` names an id that exists in the page.

## Nits

2. **Remove logo loses its red on hover.** `.um-btn-ghost:hover { text-on-surface }` has
   specificity 0,2,0 and beats the `text-error` utility at 0,1,0, so the button turns neutral
   the moment the pointer reaches it. Add `hover:text-error` next to `text-error` (a utility
   with a pseudo-class, same specificity as the component rule, later in the sheet, so it wins).
   Keep the ghost's background hover. Regenerate `umbral.css`.
3. **The preview label still says "button".** `Texts.fs`, `BrandingSampleLabel = "Preview of
   the sign-in button"`, but the panel now previews the logo and the button together. Suggest
   "Preview of the sign-in page" or "As people see it on the sign-in page". Text catalogue rule:
   change it there, nowhere else.
4. **Three labels, one style.** "SIGN-IN PAGE BRANDING", "LOGO" and "ACCENT COLOUR" are all
   `um-label`, so the section heading reads as a third field label. The task said to keep the
   `um-label` h3 and commit `16d445f` deliberately kept one heading, so this is not a fix for
   this task. Noting it for the maintainer: a section heading on the card wants one step more
   than its field labels (for example `text-label-md` or `text-on-surface`), and that is a
   Components-level decision for every card section, not this one.

## Captures to add before the re-check

Name them `<state>-<width>-<theme>.png` and put them in this folder, so they survive `/tmp`.

- `card-360-dark.png` and `card-360-light.png`: the whole app card at 360, not only the section.
  The task is about the card looking broken; I want to see the branding section against the
  Regenerate secret and Delete app row above it, and that the card has exactly one filled red
  button.
- `crop-open-360-dark.png`: a file chosen with the crop shown, so the row holds Save logo, Use
  whole image and Remove logo and wraps at 360. By construction it wraps (`flex-wrap`), but the
  wrap order and the gap are worth one look.

The other states I asked for earlier (refused upload, low-contrast warning, the confirm dialog)
are not needed: the refusal line and the alert are unchanged by this diff, and the confirm is
the browser's own dialog.

## Package notes for next time

`notes.md` was not written, and the captures are in `/tmp`. Everything was readable from the
diff this time, so none of it blocks. For the next task, commit the folder on the branch with
`notes.md` and the captures, as the task now says.

## Tests and stylesheet check

Run by the reviewer on the branch's worktree on 2026-10-22.

At `c2f0ef0` (re-check): `dotnet test tests/Umbral.Tests --filter
"FullyQualifiedName~AppBranding|FullyQualifiedName~Csrf"`: 48 passed, 0 failed.
`build-css.sh --check`: clean, `umbral.css` is current.

At `16d445f` (first review): the branding filter alone, 42 passed, 0 failed; stylesheet check
clean.
