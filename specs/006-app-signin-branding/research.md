# Research: Per-App Sign-In Branding

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-10-15

## What the code does today, and what it means for this feature

| # | Today | Consequence | Decided in |
| --- | --- | --- | --- |
| A | Every page Umbral renders gets `default-src 'self'; img-src 'self' data:; …` (`Views/Layout.fs:44`). No inline `<style>` and no `style=""` attribute can apply. | The accent colour cannot be written into the page. It has to arrive as a same-origin stylesheet. | R4 |
| B | Tailwind compiles the palette into literal colours: `.um-btn-primary{background-color:rgb(95 179 232/…)}` (`Assets/tailwind.config.js`, `umbral.css`). | Overriding `--color-primary` alone recolours almost nothing. The palette entries for `primary` and `on-primary` have to become CSS variables first. | R4 |
| C | The picker's connector buttons use the **secondary** style (`Picker.fs`, `Components.Secondary`). The accent colour appears only in focus rings and links. | "Orange sign-in buttons" (Story 1) means the buttons switch to the primary style when an app has an accent colour. | R5 |
| D | The picker learns the app only from `returnUrl`'s `/connect/authorize?client_id=` (`Picker.clientIdOf`). Failures come back to `/signin` with the same `returnUrl`. | Branding follows the same lookup and is branded on failure too, at no extra cost (FR-002, FR-003). | R5 |
| E | Umbral's tables use hand-written SQL with an idempotent schema and numbered upgrade steps (`Database.fs`, `SchemaVersion = 3`). Deleting an app removes its Umbral rows by hand (`AppDelete.fs`, `AppConnectors.deleteFor`). | A new table, a step 4, and one more delete in `AppDelete`. | R6 |
| F | Admin POSTs go through `WebForms.readValidated` (antiforgery), and end in a redirect with a `?msg=` notice. | The branding forms follow the same pattern. A file upload is new: the request size and form buffering need limits. | R3, R8 |
| G | The admin page has no per-app edit page. Each app is a card on `/admin/apps` with its own forms (`/admin/apps/{clientId}/connectors`, `/secret`, `/delete`). | Branding is one more section on that card (FR-018). No new screen. | R8 |
| H | `AppUpdated` is already the event for app changes, with a `Detail` code (`"connector_choice"`). | Branding changes reuse it with their own codes (FR-022). | R9 |
| I | There is no browser test harness. The suite is TestServer and xunit. | The crop script is validated manually (quickstart). The server enforces every rule, so it is fully covered by TestServer tests. | R11 |

## R1 — Where the image work happens

**Decision**: the operator's browser crops, scales and converts the image to
PNG. Umbral's server accepts only that PNG, validates its structure strictly,
and rewrites it with only the chunks it needs (R2). Umbral gets no image
library.

The admin script reads the chosen file locally (a `data:` URL, which the CSP
already allows), shows it with a crop rectangle, and on submit draws the crop
onto a canvas no larger than 512 × 512. It exports that canvas as PNG and puts
it into the file input in place of the original, then submits the form
normally. Browsers apply JPEG orientation when they draw, so a phone photo is
cropped the way it looks.

**Rationale**:

- **No decoder of untrusted images runs in Umbral's process.** The server only
  walks PNG chunks and inflates one zlib stream with a known bound. It never
  decodes pixels.
- **No memory spike on a Pi.** Decoding a 4096 × 4096 image takes 64 MB of
  pixels. The server never holds more than about 1 MB of inflated data
  (Principle II, FR-026).
- **No new dependency.** Umbral will be MIT-licensed (decided 2026-10-15, after
  this research was first written). That makes ImageSharp 3.x usable under
  Apache 2.0 terms. The decision stands on the other reasons: memory, attack
  surface, and one fewer package.
- The browser decodes the operator's own file, on the operator's own machine.

**Alternatives considered**:

| Option | Why not |
| --- | --- |
| `SixLabors.ImageSharp` (managed decode, crop, resize) | The Six Labors Split License grants Apache 2.0 terms to open-source projects, so MIT-licensed Umbral qualifies on 3.x. Version 4 reportedly requires a licence file at build time, which would pin Umbral to 3.x. It also needs about 64 MB transient memory per maximum-size upload on a Pi, and it is a format parser of untrusted input in-process, even if a managed one. It remains the fallback if the browser-side approach fails in practice. |
| `SkiaSharp` (MIT, native) | Native decoders parse untrusted input in-process, as the libwebp heap overflow of 2023 (CVE-2023-4863) showed. It adds about 10 MB of native assets per architecture, and it is a second native dependency next to SQLite. |
| `Magick.NET` | Large, with a format surface far wider than three formats. Same native-code concern. |
| Keep the upload as received | Violates FR-013: it keeps metadata, and the stored bytes are whatever the operator's tool wrote. |

**Consequence for the spec**: the "scripts disabled" edge case changes. Without
a script there is nothing to crop or convert, so the server accepts only a PNG
that already meets its limits, and the form's hint says so. The spec was
updated to match (edge case "A browser where cropping is unavailable", FR-011).

## R2 — Validating and rewriting the PNG

**Decision**: a small module, `Domains/PngLogo.fs`, pure and fully
unit-tested. It is not cryptography and not a pixel decoder. Its steps:

1. Check the 8-byte PNG signature. Anything else is `NotPng`. This covers JPEG,
   WebP, SVG, GIF and HTML when no script converted them.
2. Walk the chunks, checking each length against the remaining bytes and each
   CRC-32. CRC-32 is a 20-line table implementation:
   `System.IO.Hashing.Crc32` is a separate package and not worth one.
3. Require `IHDR` first, with width and height between 32 and 512, a bit depth
   valid for the colour type (types 0, 2, 3, 4 and 6), compression 0, filter 0
   and interlace 0. Interlaced is refused with its own message, because canvas
   never produces it and supporting Adam7 doubles the size arithmetic.
4. Keep `PLTE` (required for type 3) and `tRNS`. Concatenate the `IDAT` data.
   Stop at `IEND`. Drop every other ancillary chunk: `tEXt`, `iTXt`, `zTXt`,
   `eXIf`, `iCCP`, `gAMA`, `pHYs`, `tIME`, `acTL`/`fcTL`/`fdAT` (animation keeps
   frame 0, which is the default image). Refuse any unknown critical chunk
   (first letter uppercase).
5. Inflate the `IDAT` stream with `ZLibStream`, reading at most `expected + 1`
   bytes, where `expected = height × (1 + ceil(width × bitsPerPixel / 8))`.
   Exactly `expected` bytes are required. Every row's filter byte must be 0–4.
   This rejects truncated images and decompression bombs without decoding a
   pixel.
6. Write a new PNG from those parts: the signature, `IHDR`, optional `PLTE` and
   `tRNS`, one `IDAT` holding the original compressed data, and `IEND`, each with
   a fresh CRC.

The stored logo is the rewritten bytes, never the request body (FR-013). Its
SHA-256, in lowercase hex, is its identity and its address (R7).

Dropping `iCCP` and `gAMA` can shift colours slightly in an image that relied
on them. A canvas export has neither, so this matters only for a PNG uploaded
without a script.

## R3 — Limits and where each one is enforced

| Limit | Value | Enforced by | Message |
| --- | --- | --- | --- |
| Source file size | 2 MB | Admin script, before reading the file | States the limit and the file's size |
| Source dimensions | 4096 px either side | Admin script, after the browser has loaded the image | States the limit and the image's dimensions |
| Source format | PNG, JPEG, WebP by content | Admin script: the first bytes are checked, then the browser must load the image. Server: only PNG (R2) | Names the accepted formats |
| Crop | at least 32 px either side, in source pixels | Admin script, and the server through the stored minimum | States the minimum |
| Stored logo | 32–512 px either side, at most 1.25 MB | Server (R2) | States the limit and the actual value |
| Request body | 3 MB for the logo endpoint only | Server: endpoint size-limit metadata, plus form options buffering the body in memory up to that size | Over the limit: redirect with `logo-too-large`, never a bare 413 |

The script's checks exist so the operator gets a precise message before
anything is sent. The server's checks are the ones the security of the feature
relies on, and tests target those.

A no-script upload of a 2 MB JPEG reaches the server and is refused as "not a
PNG", with the hint that scripts must be on to convert other formats. This is
the honest answer: the server cannot convert it.

The endpoint sets its own form limits through endpoint metadata
(`FormOptionsMetadata`, `RequestSizeLimitAttribute`/`IRequestSizeLimitMetadata`).
This raises `MemoryBufferThreshold` so the body stays in memory and is not
spilled to a temp file outside the data directory. Implementation verifies that
the antiforgery middleware's form read honours that metadata. If it does not,
the handler reads the form itself with the same options before validating.

**Implementation note (T029)**: what the antiforgery middleware does with a
multipart body was checked with a test, and the answer changed the design.

- **The middleware honours the endpoint's form options.** Metadata only
  (`RequestFormLimitsAttribute` with `MemoryBufferThreshold` and
  `MultipartBodyLengthLimit` at 3 MB, plus `RequestSizeLimitAttribute`) keeps a
  1 MB upload in memory. Dropping `MemoryBufferThreshold` makes the same upload
  spill to `ASPNETCORE_*.tmp` in the temp directory. A control upload of 300 KB
  to `POST /admin/apps`, which has no options of its own, does spill, so the
  probe in `AppBrandingAdminTests.fs` (a startup filter that lists the temp
  files left while the request is still alive) can see it. No explicit
  `FormOptions` read was needed.
- **But the middleware reads before the handler can catch anything.** It wraps
  whatever the read throws, `InvalidDataException` (the multipart limit) or
  Kestrel's `BadHttpRequestException` (413), in an
  `AntiforgeryValidationException` ("Unable to read the antiforgery request
  token from the posted form"), which `readValidated` turns into the expired-form
  400. So a 3.5 MB body would have been a 400 page that says the form expired,
  and a request over Kestrel's limit the same, never the `app-logo-too-large`
  notice.
- **So the logo route sets `DisableAntiforgery()` and validates in the
  handler.** `WebForms.readValidated` already validates directly when the
  middleware did not run, so the same check happens, one step later, inside a
  `try` that maps those two inner exceptions to `app-logo-too-large`. A wrong or
  missing token is still the 400 (`CsrfTests` holds the route to it like every
  other POST). The endpoint metadata is applied to the request before the
  handler runs, so Kestrel's 3 MB limit is in force when the handler reads. The
  test simulates Kestrel's limit, which TestServer does not enforce, and checks
  that the limit seen at the endpoint is 3,145,728.
- The handler reads at most `PngLogo.maxBytes + 1` bytes of the file, so a file
  of any size under the request limit costs about 1.25 MB of memory to refuse.

## R4 — Delivering the accent colour under the CSP

**Decision**:

- `tailwind.config.js`: `primary` becomes `rgb(var(--color-primary-rgb) / <alpha-value>)`
  and `on-primary` becomes `rgb(var(--color-on-primary-rgb) / <alpha-value>)`.
  `:root` in `umbral.input.css` defines both channel variables with today's
  values (`95 179 232`, `7 38 59`), and `--color-primary` becomes
  `rgb(var(--color-primary-rgb))`, so every existing `var(--color-primary)` use
  follows. `umbral.css` is regenerated with `build-css.sh`. An unbranded page
  computes the same colours as before (SC-001).
- A new public endpoint, `GET /branding/accent/{rrggbb}.css`, generates this
  from the path:

  ```css
  [data-accent="f26b1d"]{--color-primary-rgb:242 107 29;--color-on-primary-rgb:0 0 0;--color-primary:rgb(242 107 29);--color-on-primary:rgb(0 0 0)}
  ```

  The path must match `^[0-9a-f]{6}$`, or the endpoint returns 404. Because the
  body is a pure function of the path, it needs no database read and is cached
  `immutable`. Every colour it can emit is one the operator could have set
  anyway.
- The selector is scoped to an attribute, not `:root`. The admin page can then
  link the same stylesheet to show a sample button (FR-019) without recolouring
  its own interface (FR-005).

**Text colour on the accent (FR-016)**: WCAG 2.x relative luminance. Umbral
uses black when `(L+0.05)/0.05 ≥ 1.05/(L+0.05)`, otherwise white. With pure
black and white the worse of the two choices is never below 4.58:1, which
meets SC-003. Umbral's current `on-primary` (`#07263b`) would drop to about
4.3:1 near the crossover, so a branded page uses black and white. An unbranded
page keeps `#07263b` on `#5fb3e8`.

**Warning (FR-017)**: the contrast between the accent and the card background,
`surface-container-low` `#11161d`, is computed on save. Below 3:1 gives the
`app-accent-low-contrast` notice. The value is saved either way.

**Alternatives**: a CSP hash or nonce for an inline `<style>`. That means a
per-response policy, and the policy no longer has the one-line shape tests
assert. Server-rendered `style=""` is blocked by the same policy.

## R5 — The sign-in page, branded and unbranded

**Decision**:

- `Picker.handler` reads the app's branding once it knows the client id. That
  is one indexed query, and none when there is no app.
- **Unbranded** (no app, unknown client, or no branding): exactly today's
  markup. The header wordmark and the footer "Umbral — self-hosted social
  login" are the page's Umbral mark. FR-006 is worded to say so.
- **Branded** (logo or accent or both): `Layout.signInPage`, a variant of
  `page`.
  - It omits the header bar with the Umbral wordmark.
  - It puts the logo at the top of the card: `<img>`, alt text the app's name,
    at most 96 px tall and as wide as the card.
  - It replaces the footer text with the mark and "Secured by Umbral". The
    mark's colour is fixed, so the accent cannot reach it (FR-006).
  - With an accent, it sets `data-accent` on `<body>`, links
    `/branding/accent/{hex}.css` after `umbral.css`, and renders the connector
    buttons in the primary style. With a logo only, the buttons stay secondary.
- The protocol error page and every admin page stay as they are (FR-005).
- The `<head>` `theme-color` stays `#0d1117`.

## R6 — Storage

**Decision**: one Umbral table in `umbral.db`. Schema version 4, with step 4
being the same `CREATE TABLE IF NOT EXISTS`, so a downgrade fails loudly as
the existing guard intends.

```sql
CREATE TABLE IF NOT EXISTS app_branding (
    client_id     TEXT PRIMARY KEY,
    accent_color  TEXT NULL CHECK (accent_color IS NULL OR (length(accent_color) = 6 AND accent_color NOT GLOB '*[^0-9a-f]*')),
    logo_png      BLOB NULL,
    logo_sha256   TEXT NULL UNIQUE,
    logo_width    INTEGER NULL,
    logo_height   INTEGER NULL,
    updated_at    TEXT NOT NULL,
    CHECK ((logo_png IS NULL) = (logo_sha256 IS NULL))
);
```

A row exists only while an app has a logo or an accent. Removing the last one
deletes the row. `AppDelete` deletes the row next to `AppConnectors.deleteFor`.

**Why a BLOB and not a file**: the image and its row change together in one
transaction. A failed write cannot leave an orphan file or a row pointing at
nothing. Backup is already "copy the data directory" (FR-024). There is no
path handling. A few apps at ≤ 1.25 MB each is negligible for SQLite.

**Alternatives**: files under `/data/branding/{sha}.png`. That needs two-phase
writes, cleanup on failure, and checks that a path stays inside the directory,
all for no gain at this size.

## R7 — Serving logos

**Decision**: `GET /branding/logo/{sha256}.png`, with no authentication, no
antiforgery and no rate limit, like `Handlers/Assets.fs`. It looks up
`logo_sha256` (a unique index). Unknown or malformed gives 404. Headers:

| Header | Value |
| --- | --- |
| `Content-Type` | `image/png` |
| `X-Content-Type-Options` | `nosniff` |
| `Content-Security-Policy` | `default-src 'none'; sandbox` |
| `Cross-Origin-Resource-Policy` | `same-origin` |
| `Cache-Control` | `public, max-age=31536000, immutable` |
| `ETag` | `"{sha256}"`; `If-None-Match` answers 304 |

The address is the content hash. A replaced logo therefore gets a new address,
and no browser can show a cached old one (FR-020). The address reveals nothing
about the app (no client id in it). Once a logo is replaced or its app is
deleted, the old address returns 404.

## R8 — The admin surface

**Decision**: a "Branding" section on each app card on `/admin/apps`. It holds:

- The current logo at its sign-in size, or "No logo". A sample primary button
  ("Sign in with …") inside `data-accent="{hex}"`, or Umbral's own colour when
  none is set. `operatorPage` gains an optional list of extra stylesheets, and
  the Apps page links one per distinct accent in use.
- **Logo form**: `multipart/form-data` POST to `/admin/apps/{clientId}/branding/logo`
  with `<input type="file" name="logo" accept="image/png,image/jpeg,image/webp">`.
  With the script, a crop panel appears: the image, a draggable and resizable
  rectangle with arrow-key nudging, and a "Use whole image" reset. Without the
  script, the hint says "PNG, 32–512 px, up to 1.25 MB. Turn on JavaScript to
  crop or to use JPEG or WebP."
- **Remove logo**: POST `/admin/apps/{clientId}/branding/logo/delete`, with
  `data-confirm`.
- **Accent form**: POST `/admin/apps/{clientId}/branding/accent`, a text field
  `accent` (`#rrggbb` or empty to clear). With the script, an
  `<input type="color">` without a name is kept in sync with the text field. It
  has no `name` because a colour input cannot be empty.

All three require `requireOperator` and `readValidated`, and end in a redirect
with a notice anchored to the app's card (contracts/admin-branding.md).

The crop script goes into `umbral.js` as plain JavaScript, no build step,
driven by `data-logo-*` attributes like the existing behaviours. It handles
pointer events and keyboard input, about 200 lines.

## R9 — Security log

**Decision**: `AppUpdated`, `Success`, `Operator`, `ClientId`, `RemoteIp`, with
these `Detail` values: `branding_logo_set`, `branding_logo_removed`,
`branding_accent_set`, `branding_accent_cleared`. A refused upload records
nothing: it is a form error, not a change. This matches how invalid app forms
are handled today. No new `EventType` is needed, so `SecurityEventCoverageTests`
is unaffected.

## R10 — Upgrade and downgrade

Version 3 → 4 creates the empty table. Every app is unbranded, so every page is
unchanged (SC-006). A version 3 build refuses a version 4 database, which is the
existing, documented behaviour.

## R11 — Testing approach

- **Unit**: `PngLogo` against PNGs generated in the test (a tiny encoder helper
  using `ZLibStream`, so there are no binary fixtures): every colour type, the
  32 and 512 bounds, interlaced, bad CRC, truncated, an unknown critical chunk,
  a zlib bomb, metadata chunks removed, and the output re-parses. JPEG, WebP,
  SVG and HTML bytes are refused. Contrast: all 16,777,216 colours meet 4.5:1
  (SC-003; well under a second).
- **TestServer**:
  - branded and unbranded picker, including the failure notice
  - the crafted-`returnUrl` case
  - an unknown client
  - CSP unchanged and no cross-origin URL in the page (SC-005)
  - the logo and accent endpoints' headers, 304 and 404
  - the admin forms: CSRF, operator required, refusals leave state untouched,
    replace changes the address, delete-with-app
  - security events
  - the upgrade from a version 3 database
- **Existing**: `IdleMemoryTests` unchanged (FR-026, SC-007), `MetaTests` (new
  SQL is literal), `PageAssetsTests` (CSS regenerated).
- **Manual** (quickstart): the crop panel in a current Chromium, Firefox and
  Safari, an EXIF-rotated JPEG, a WebP, and an animated PNG.
