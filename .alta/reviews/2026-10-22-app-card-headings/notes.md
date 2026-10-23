# Notes: app card section title and the closing action row

Task: `2026-10-22-app-card-section-headings-and-final-action-row-spacing-h3cd`
Branch: `alta/carefree-sunflower-jc4t`, on `main` at `0889378`.

## Commits

- `0906ce6` fix(apps): the branding heading is a section title; the card's actions sit behind a hairline.
- This folder (notes and captures) is committed after it.

## What changed

1. **Section title.** New component `.um-section-title` in `umbral.input.css`:
   `text-body-lg font-semibold text-on-surface` (16px/24px, 600, bone; ink in light). It sits
   between the card title (headline medium, 20px) and the field labels (`.um-label`, 11px
   uppercase muted), and is not uppercase, so it reads as the group's title rather than a third
   label. The branding `h3` uses it. A shared class, not utilities, so other cards can use it.
   - Measured in Chrome: heading `16px 600 rgb(236, 232, 223)` dark / `rgb(23, 21, 31)` light;
     LOGO / ACCENT COLOUR labels `11px 700 rgb(167, 162, 182)` / `rgb(95, 90, 107)`, uppercase.
     Bone on the low step is the body text pair, so the contrast floor (spec 007 FR-007) holds.
   - It is the only section title on the card. "Sign-in methods" is the `legend` of the
     connector fieldset, a field-group label shared with the Create app form, so it stays
     `.um-label`.
2. **Closing action row.** Picked the hairline: the Regenerate secret / Delete app row gets
   `border-t border-outline-variant/40 pt-4`, the same separator the branding section uses
   above it, so the card reads as sections split by one kind of line. Measured: 16px from the
   accent form to the hairline (the card's `space-y-4`), 17px from the hairline to the buttons
   (16px padding + the 1px border), at both widths and in both themes. One filled red button per
   card (Delete app), counted in the page. The row has `data-app-actions` for tests.
3. **DESIGN.md** (and `.impeccable/design.json`, the sidecar) updated to match: label small is
   the field label; body large at 600 is the section title; card sections are headed by
   `.um-section-title`, and the closing action row has the same hairline.

## Texts.fs

No changes.

## Stylesheet

`src/Umbral/Assets/build-css.sh` run; `umbral.css` committed; `build-css.sh --check` clean.

## Tests

- New: ``the branding section has a section title, and the card's actions sit behind a hairline after it``
  in `AppBrandingAdminTests.fs`. It checks the `h3.um-section-title` per card, no `h3` with
  `um-label`, the hairline on the actions row, and that the row comes after the accent form.
- `--filter FullyQualifiedName~AppBrandingAdminTests`: 43 passed, 0 failed.
- Wider run (AppBrandingAdmin, AdminScreen, Csrf, AppConnectorChoice, PageAssets): 97 passed, 0 failed, at `0906ce6`. The full suite was not run; the change is one class on a heading, one on a row, and a new CSS component.

## CHANGELOG

Under `## Unreleased`, `### Fixed`: "An app card's **Sign-in page branding** heading now reads as
the title of its section rather than a third field label beside **Logo** and **Accent colour**,
and **Regenerate secret** and **Delete app** sit behind a hairline that closes the card, instead
of hanging under the accent form."

## Impeccable detector

`impeccable detect --json src/Umbral/Handlers/Admin/Apps.fs src/Umbral/Assets/umbral.input.css`:
one advisory, `design-system-color` at `umbral.input.css:178`, `rgb(0 0 0 / 0.6)`, the crop
box's shade. It dates from 2026-10-15 (`a03567a`) and is not touched here.

## Captures

Headless Chrome (Playwright, `channel: 'chrome'`) on a fresh local instance at `0906ce6`.
Two apps: "Photo Album" with a 256px logo and the accent `#ff6f61`, and "Recipe Box" with
neither. Whole card, element screenshot:

- `card-<width>-<theme>.png`: Photo Album, at 1280 and 360, dark and light.
- `card-nologo-<width>-<theme>.png`: Recipe Box, the same four.

No page overflow at either width.

## One observation, not in this task

At 360 the redirect URI line (`https://photos.example.com/callback`, mono, no break) runs into
the card's right padding, almost to its border (`card-360-*.png`). It is not from this change. I
proposed it as a follow-up task rather than fix it here.
