# Notes: the app card's "Sign-in page branding" section

Task: `2026-10-22-sign-in-page-branding-section-layout-and-remove-logo-button-b7rn`
Branch: `alta/roomy-flamingo-ms3r`, on `main` at `542b6be`.

## Commits

- `195ed76` fix(apps): Remove logo shares the Save logo row, as a quiet button (the task).
- `16d445f` fix(apps): the preview's label is a hint, so the section has one heading.
- `c2f0ef0` fix(apps): review items 1–3 (one description id, red on hover, the preview's name).

## Answers to review.md

1. **Blocking, `aria-describedby`:** done as suggested. The hint is
   `p(id = hintId) { t.LogoHint; noscript() { " " + t.LogoHintNoScript } }` and the input names
   `hintId` only. The card test now checks every id in the input's `aria-describedby` against the
   page with `<noscript>…</noscript>` removed, which is what a browser with scripts builds. The
   old markup put the id inside `noscript`, so it would fail that check. I reasoned this from the
   regex and did not run it against the old commit.
2. **Nit, hover:** `hover:text-error` added. In Chrome the computed colour on hover is the
   `--color-error` value in both themes: dark `rgb(255, 143, 126)`, light `rgb(179, 38, 30)`.
   Tailwind emits the `hover:` variant after the unlayered `.um-btn-ghost:hover`, so it wins at
   the same specificity. The ghost's background hover still applies.
3. **Nit, label:** taken. See the wording below.
4. **Nit, heading vs field labels:** left for the maintainer, as review.md says.

## Texts.fs wording

| Key | Old | New |
|---|---|---|
| `LogoHint` | PNG, JPEG or WebP. Umbral stores a PNG of at most 512 × 512 pixels. Without JavaScript, upload a PNG of 32–512 pixels a side, up to N MB. | PNG, JPEG or WebP. Umbral stores a PNG of at most 512 × 512 pixels. |
| `LogoHintNoScript` (new) | — | Without JavaScript, upload a PNG of 32–512 pixels a side, up to N MB. |
| `BrandingSampleLabel` | Preview of the sign-in button | Preview of the sign-in page |

## Stylesheet

`src/Umbral/Assets/build-css.sh` was run after each view change and `umbral.css` is committed
with it. `build-css.sh --check` is clean at `c2f0ef0`.

## Tests

`dotnet test tests/Umbral.Tests --filter "FullyQualifiedName~AppBrandingAdmin|FullyQualifiedName~Csrf"`:
48 passed, 0 failed at `c2f0ef0`. The full suite (1387) passed at `195ed76`. It was not re-run
after `16d445f` or `c2f0ef0`, which change only this section's markup, one text and one class.

- New: ``Remove logo sits in the upload form's button row and submits the delete form, which carries the token``.
- Extended: ``an app card has the branding section with its accent form, and an app with none previews Umbral's colour``
  (the five `data-logo-*` hooks, and the described-id check from item 1).
- Unchanged and passing: ``the remove form is on the card only when the app has a logo, and asks first``.

## CHANGELOG

Under `## Unreleased`, `### Fixed`: "The **Sign-in page branding** section of an app card no longer
looks broken: …" (one bullet).

## Impeccable detector

Not re-run by the implementer. `umbral.input.css` has not changed since the reviewer's run;
`c2f0ef0` adds only the `hover:text-error` utility in the F# view.

## Captures

Taken in headless Chrome (Playwright) on a fresh local instance at `c2f0ef0`. Two apps: "Photo
Album" with a 256 px logo and a coral accent `#ff6f61`, and "Recipe Box" with neither.

- `section-logo-<width>-<theme>.png` and `section-nologo-<width>-<theme>.png`: the branding
  section at 1280 and 360, dark and light.
- `card-360-dark.png`, `card-360-light.png`: the whole card at 360. It has one filled red button
  (Delete app), counted in the page.
- `crop-open-360-dark.png`: a file chosen, crop shown. The row holds Save logo, Use whole image
  and Remove logo. At 360 Remove logo wraps onto its own line with the 8px gap.

Also checked in the same run: Save logo and Remove logo share a line at both widths (same top
edge), nothing overflows the page, and Remove logo shows the confirm and then removes the logo.

## One observation, not in this task

On the card, the Regenerate secret / Delete app row sits 16px under the accent form (the card's
`space-y-4`), closer than the 20px between groups inside the branding section, so at a glance it
can read as part of the branding section. A divider or a larger gap above that row would separate
it. That is a card-level choice, like nit 4.
