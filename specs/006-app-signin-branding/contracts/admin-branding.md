# Contract: Branding on the Apps Screen

**Feature**: [../spec.md](../spec.md) | **Research**: R3, R8, R9

All three endpoints:

- go through `AdminAuth.requireOperator` (otherwise the usual redirect to
  sign-in) and `WebForms.readValidated` (otherwise 400, as `CsrfTests` asserts
  for every POST)
- look the app up by client id. An unknown one gives
  `redirectWithNotice "/admin/apps" "app-not-found"`
- end in a redirect to `/admin/apps?msg={code}&app={clientId}` with an
  `#app-{clientId}` anchor, so the operator returns to the same card
- change nothing when they refuse

Notices that need values (sizes, dimensions) carry them in the Data-Protected,
single-use `WebForms.NoticeReason` cookie, never in the query string. A link
cannot make the Apps screen state a size that was never measured.
`NoticeReason` is generalised to take the "about" key (`app` here, `slug`
today).

## `POST /admin/apps/{clientId}/branding/logo`

`multipart/form-data`, field `logo` (one file) plus the antiforgery token.

The request size limit is 3 MB, and the form is buffered in memory (R3).

| Input | Result | Notice |
| --- | --- | --- |
| Valid PNG (R2) | Logo set or replaced. Event `branding_logo_set` | `app-logo-saved` |
| No file, or a 0-byte file | refused | `app-logo-missing` |
| No PNG signature | refused | `app-logo-not-png`: "Umbral stores logos as PNG. Turn on JavaScript on this page to crop, scale and convert a PNG, JPEG or WebP image, or upload a PNG of at most 512 × 512 pixels." |
| Bad structure: CRC, length, IHDR fields, IDAT size, filter byte | refused | `app-logo-corrupt`: "This PNG is damaged. Export it again from your image editor." |
| Interlaced | refused | `app-logo-interlaced`: "Save the PNG without interlacing, or turn on JavaScript to convert it." |
| Unknown critical chunk | refused | `app-logo-corrupt` |
| Under 32 px on a side | refused | `app-logo-too-small`, with the actual size |
| Over 512 px on a side | refused | `app-logo-too-big`, with the actual size: "…turn on JavaScript to scale it down, or export it at 512 pixels or less." |
| Over 1.25 MB after the checks, or over the request limit | refused | `app-logo-too-large`, with the size when known |

A body over the request limit never surfaces as a bare 413. The handler catches
the size exception and redirects with `app-logo-too-large`.

## `POST /admin/apps/{clientId}/branding/logo/delete`

The form carries `data-confirm` with `Texts.AppLogoRemoveConfirm name`.

| State | Result | Notice |
| --- | --- | --- |
| Has a logo | Removed. Row deleted if no accent. Event `branding_logo_removed` | `app-logo-removed` |
| No logo | Nothing to do, and no event | `app-logo-removed` |

## `POST /admin/apps/{clientId}/branding/accent`

Field `accent`.

| Input (trimmed) | Result | Notice |
| --- | --- | --- |
| `#rrggbb`, either case | Saved lowercase. Event `branding_accent_set` | `app-accent-saved`, or `app-accent-low-contrast` when below 3:1 on the card (FR-017): "Saved. This colour is hard to see on Umbral's dark or light background, so outlines and highlights in it will be faint. Buttons stay readable." *(Amended by 007: the card is both themes' card, `#14141f` and `#fffdf8`, and the wording names both backgrounds. `#f26b1d` is now 2.995:1 on the light card and warns; the example colour in the hint and the invalid-colour notice became `#d9480f`, safe on both cards.)* |
| empty | Cleared. Row deleted if no logo. Event `branding_accent_cleared` (only if one was set) | `app-accent-cleared` |
| anything else (`f26b1d`, `#fff`, `orange`, `rgb(…)`) | refused | `app-accent-invalid`: "Enter the colour as # followed by six hexadecimal digits, for example #d9480f." |

## The branding section of an app card

Inside the existing app card, under the connector choice.

Markers:

- `data-branding="{clientId}"`: the section
- `data-branding-logo`: the current logo as an `<img>` at sign-in size, or the
  text "No logo"
- `data-branding-sample`: a disabled primary button reading
  `Texts.SignInWith "…"`, inside `data-accent="{hex}"` when an accent is set
- `data-logo-form`: the upload form, `enctype="multipart/form-data"`. Its file
  input has `data-logo-input`. The hint `Texts.AppLogoHint` names the formats,
  the limits and the script requirement. `data-logo-crop` is an empty container
  that the script fills
- the remove form, shown only when there is a logo
- `data-accent-form`: the text input `accent`, with value `#hex` or empty, and
  `data-accent-picker`, an `<input type="color">` with no `name`. The script
  keeps the picker and the text field in sync. Without the script the picker is
  hidden (`hidden` attribute removed by the script)

The Apps page `<head>` links `/branding/accent/{hex}.css` once for each distinct
accent in use. `Layout.operatorPage` gains a `stylesheets: string list`
parameter, and every other caller passes `[]`.

## Crop script (`umbral.js`) behaviour

There is no server contract beyond the file it submits. Manual checks are in
quickstart.md.

1. On `change` of `[data-logo-input]`:
   - check `file.size ≤ 2 MB`, else show the inline error and clear the input
   - check the first bytes are PNG, JPEG (`FF D8 FF`) or WebP (`RIFF....WEBP`),
     else show the error
   - load the file as a `data:` URL into an `<img>`; a failed load shows
     "not an image Umbral accepts"
   - check `naturalWidth` and `naturalHeight ≤ 4096`, else show the error
2. Show the image scaled to fit the panel. Draw the crop rectangle, initially
   the whole image:
   - drag to move it, drag the corner handles to resize it
   - arrow keys move it by 1 px, Shift+arrow resizes it
   - "Use whole image" resets it
   - the minimum is 32 × 32 source pixels
3. On submit: draw the crop on a canvas scaled so that neither side exceeds
   512, then `toBlob('image/png')`. Put the result into the input through
   `DataTransfer` as `logo.png`, then let the form submit. If `toBlob` or
   `DataTransfer` is missing, submit the original file. The server then decides
   (R3).
