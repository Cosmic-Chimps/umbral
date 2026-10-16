---
description: "Task list for 006 per-app sign-in branding"
---

# Tasks: Per-App Sign-In Branding

**Input**: Design documents from `specs/006-app-signin-branding/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included, for three reasons:

- the spec's success criteria are test-shaped (SC-001, SC-003 to SC-006)
- the constitution's testing discipline requires them
- the server-side rules are the security boundary of the feature (research
  R3)

Within each story, tests come first and must fail before the implementation
task that makes them pass. The crop script has no automated test (research
R11); its manual checks are tasks of their own.

**Organization**: after a foundation that changes nothing visible, the stories
are as follows:

- **US1** shows branding on the sign-in page and sets the accent colour.
- **US2** gets a logo into Umbral: validator, upload, crop.
- **US3** replaces, removes and cleans up.

US1 is testable alone by seeding branding through `BrandingStore`, so it does
not wait for the upload path.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1–US3 from spec.md
- F# compile order matters. Every new `.fs` file needs its `<Compile Include>`
  in the right place in `src/Umbral/Umbral.fsproj` (before `Program.fs`) or
  `tests/Umbral.Tests/Umbral.Tests.fsproj` (before `SmokeTests.fs`). Setup
  registers them all.

## Conventions every task follows

- Run `dotnet build -c Release` (warnings are errors) and the tests the task
  names. The full suite is `dotnet test -c Release`.
- Every operator- and user-facing string lives in `src/Umbral/Views/Texts.fs`,
  and every refusal says what to do next (Principle VI).
- SQL is a string literal or a `[<Literal>]` set on `CommandText`
  (`MetaTests` rule 3). Statements are uppercase.
- No inline `<style>`, `style=""` or inline script. Class names are literal
  strings in views, so the Tailwind scanner sees them. After adding a class,
  run `src/Umbral/Assets/build-css.sh` and commit the regenerated `umbral.css`.
- Admin POSTs: `AdminAuth.requireOperator`, then `WebForms.readValidated`,
  then a redirect with a notice code, as in `Handlers/Admin/Apps.fs`
  `setConnectors`.
- New endpoint modules are added to `Program.endpoints`. `MetaTests` fails
  otherwise, naming the module.

---

## Phase 1: Setup

**Purpose**: empty modules in compile order, so later tasks only fill files in.

- [X] T001 Create these modules, each with a header doc comment naming spec 006 and its research section, and register them in `src/Umbral/Umbral.fsproj`:
  - `Domains/Branding.fs` and `Domains/PngLogo.fs`, directly after `Domains/RedirectUris.fs`
  - `Infrastructure/BrandingStore.fs`, directly after `Infrastructure/AppConnectors.fs`
  - `Handlers/Branding.fs`, directly after `Handlers/Assets.fs`
  - `Handlers/Admin/AppBranding.fs`, directly after `Handlers/Admin/AppDelete.fs`

  Confirm `dotnet build -c Release` passes.
- [X] T002 [P] Create empty test modules and register them in `tests/Umbral.Tests/Umbral.Tests.fsproj`:
  - `Infrastructure/PngBuilder.fs`, after `Infrastructure/StubOidcProvider.fs`
  - before `SmokeTests.fs`: `AccentTests.fs`, `PngLogoTests.fs`, `BrandingStoreTests.fs`, `BrandingEndpointTests.fs`, `SignInBrandingTests.fs`, `AppBrandingAdminTests.fs`

---

## Phase 2: Foundational (blocking)

**Purpose**: the palette can be recoloured, the colour maths exists, and
branding can be stored and read. Nothing visible changes and the whole existing
suite stays green.

**⚠️ No user story work can begin until this phase is complete.**

- [X] T003 Turn the accent into CSS variables (research R4):
  - In `src/Umbral/Assets/tailwind.config.js`, set `primary` to `'rgb(var(--color-primary-rgb) / <alpha-value>)'` and `on-primary` to `'rgb(var(--color-on-primary-rgb) / <alpha-value>)'`. Keep the hex values in a comment.
  - In `src/Umbral/Assets/umbral.input.css` `:root`, define `--color-primary-rgb: 95 179 232;` and `--color-on-primary-rgb: 7 38 59;`, and set `--color-primary: rgb(var(--color-primary-rgb));`. Check whether the `:root` block is generated from the config (the config header says it is the single source) and change it there if so.
  - Regenerate `src/Umbral/Assets/umbral.css` with `src/Umbral/Assets/build-css.sh`, then confirm `build-css.sh --check` passes.
- [X] T004 [P] Extend `tests/Umbral.Tests/PageAssetsTests.fs` with three assertions:
  - the served `umbral.css` defines `--color-primary-rgb:95 179 232` and `--color-on-primary-rgb:7 38 59`
  - `.um-btn-primary` reads both variables
  - no literal `95 179 232` remains outside the variable definition, so every accent use is recolourable
- [X] T005 Implement `src/Umbral/Domains/Branding.fs` per [data-model.md](./data-model.md):
  - `Accent`, with a private constructor
  - `Accent.parse`: trim; accept `^#[0-9a-fA-F]{6}$` only; lowercase it; otherwise `Error NotHexColour`
  - `hex`, `rgb`
  - WCAG 2.x `relativeLuminance` and `contrastRatio`
  - `onAccent`: `Black` when `(L+0.05)/0.05 ≥ 1.05/(L+0.05)`, else `White`
  - `lowContrastOnCard`: contrast against `#11161d` below 3.0
  - the types `AppBranding` and `LogoRef`
  - `Logo` and `LogoError`, defined here so `BrandingStore` does not depend on `PngLogo`
- [X] T006 [P] Write `tests/Umbral.Tests/AccentTests.fs`:
  - **Parse table**:
    - `#f26b1d`, `#F26B1D` and ` #f26b1d ` give `f26b1d`
    - `f26b1d`, `#fff`, `#f26b1dff`, `orange`, `rgb(242,107,29)`, `#g26b1d` and the empty string give `Error`
  - **Exhaustive (SC-003)**: for all 16,777,216 colours, the contrast of `onAccent` against the colour is ≥ 4.5. Keep it under a few seconds by looping over ints and computing luminance per channel from a 256-entry table.
  - **Known values**: `#f26b1d` gives `Black`; `#0a0a10` and `#1f6fa8` give `White`.
  - **Warning threshold**: `lowContrastOnCard` is true for `#0a0a10` and false for `#f26b1d` and `#5fb3e8`.
- [X] T007 Add `app_branding` to `src/Umbral/Infrastructure/Database.fs` exactly as in [data-model.md](./data-model.md):
  - add the table to `schema`
  - set `SchemaVersion` to 4
  - add `[<Literal>] UpgradeToVersion4`, the same `CREATE TABLE IF NOT EXISTS` statement, as step 4 in `upgradeSteps`
  - mention spec 006 in the comment
- [X] T008 [P] Extend `tests/Umbral.Tests/DatabaseTests.fs`:
  - a fresh database is at version 4 with `app_branding`
  - a database stamped version 3, without the table, is upgraded to 4 with the table empty and existing rows untouched
  - the CHECK constraints reject an uppercase `accent_color`, a 5-character one, a `logo_png` without `logo_sha256`, and a logo without its width
- [X] T009 Implement `src/Umbral/Infrastructure/BrandingStore.fs` per [data-model.md](./data-model.md):
  - `forApp`, which never selects `logo_png`
  - `allForApps`, returning a map keyed by client id
  - `logoBytes`
  - `setLogo` (upsert)
  - `removeLogo`, `setAccent (Accent option)`: each deletes the row when nothing is left
  - `deleteFor`

  Every write takes `now: DateTimeOffset` for `updated_at`, and every multi-statement change runs in one transaction. Follow `Infrastructure/AppConnectors.fs` for connection and reader style.
- [X] T010 [P] Write `tests/Umbral.Tests/BrandingStoreTests.fs`, walking the lifecycle in data-model.md:
  - set the accent, then set a logo: one row with both
  - remove the logo: the row keeps the accent
  - clear the accent: no row
  - `setLogo` twice: only the second hash resolves through `logoBytes`
  - `allForApps` returns every branded app
  - `deleteFor` removes the row
  - two apps cannot share a `logo_sha256` (the second `setLogo` with the same bytes for another app fails); document in the test that identical uploads to two apps are not expected

  `Logo` values are built directly. Their bytes need not be a valid PNG here.

**Checkpoint**: storage and colour maths exist. `dotnet test -c Release` is green, and no page changed.

---

## Phase 3: User Story 1 - An app's sign-in page carries its logo and colour (Priority: P1) 🎯 MVP

**Goal**: an app with branding gets a branded `/signin` (logo, accent, "Secured
by Umbral", no header), and every other page is unchanged. The operator can set
the accent colour from the app's card.

**Independent test**: seed a logo and an accent for one of two apps through
`BrandingStore`. Check both sign-in pages against
[contracts/public-endpoints.md](./contracts/public-endpoints.md), then set an
accent from the Apps screen (quickstart §4.3–4.6).

### Tests for User Story 1

- [X] T011 [P] [US1] Write the accent part of `tests/Umbral.Tests/BrandingEndpointTests.fs` per [contracts/public-endpoints.md](./contracts/public-endpoints.md):
  - `GET /branding/accent/f26b1d.css` returns exactly `[data-accent="f26b1d"]{--color-primary-rgb:242 107 29;--color-on-primary-rgb:0 0 0;--color-primary:rgb(242 107 29);--color-on-primary:rgb(0 0 0)}`
  - `0a0a10` gets on-accent `255 255 255`
  - the headers are `text/css; charset=utf-8`, `nosniff`, immutable, and ETag `"f26b1d-1"`; `If-None-Match` returns 304
  - `F26B1D`, `f26b1`, `f26b1dd` and `zzzzzz` return 404
- [X] T012 [P] [US1] Write the logo-serving part of `tests/Umbral.Tests/BrandingEndpointTests.fs`:
  - seed a `Logo` through `BrandingStore.setLogo`
  - `GET /branding/logo/{sha}.png` returns the bytes with every header in the contract (`image/png`, `nosniff`, `default-src 'none'; sandbox`, `same-origin` CORP, immutable, ETag `"{sha}"`)
  - 304 on `If-None-Match`
  - 404 for an unknown 64-hex hash, an uppercase hash and a 63-character value
- [X] T013 [P] [US1] Write `tests/Umbral.Tests/SignInBrandingTests.fs`. Use `Flows.registerApp` and `Flows.seedFakeConnector`, start each sign-in through the real `/connect/authorize` redirect to `/signin`, and seed branding through `BrandingStore`. Cases:
  - **(a) Unbranded app**:
    - no `data-accent`, `data-signin-logo` or `data-secured-by`
    - the `<header>` wordmark and `Texts.Footer` are present
    - connector buttons are `um-btn-secondary`
  - **(b) Accent only**:
    - `<body data-accent="f26b1d">`
    - `<link href="/branding/accent/f26b1d.css">` after the `umbral.css` link
    - buttons are `um-btn-primary`
    - no `header nav`
    - `data-secured-by` is present
  - **(c) Logo only**:
    - `img[data-signin-logo=clientId]` with `src` `/branding/logo/{sha}.png`, `alt` the app name, and `width`/`height` from the row
    - buttons are `um-btn-secondary`
  - **(d) Failure notice**: `/signin?returnUrl=…&msg=access-denied` for the branded app is still branded and shows the alert
  - **(e) Crafted link**: `/signin` whose `returnUrl` names app B, with an extra `client_id=A` on `/signin` itself, shows B's branding (or none if B has none), never A's
  - **(f) Unknown client**: an unknown `client_id` in `returnUrl` is unbranded
  - **(g) Error page**: `/connect/error`, reached through an authorize request with an unregistered `redirect_uri` for a branded registered client, has no `data-accent` and no `data-signin-logo`
  - **(h) Invariants on every page above**:
    - the `Content-Security-Policy` header equals `Layout.contentSecurityPolicy`
    - every `src` and `href` starts with `/` and not `//` (SC-005)
- [X] T014 [P] [US1] Write the accent part of `tests/Umbral.Tests/AppBrandingAdminTests.fs` per [contracts/admin-branding.md](./contracts/admin-branding.md):
  - **Access**: without an operator session, a redirect to sign-in; without an antiforgery token, 400
  - **Save**: `#F26B1D` gives `app-accent-saved`, the row holds `f26b1d`, and one `AppUpdated` event exists with `detail = 'branding_accent_set'` and the client id
  - **Low contrast**: `#0a0a10` is saved and gives `app-accent-low-contrast`
  - **Invalid**: `f26b1d`, `#fff`, `orange` and `rgb(1,2,3)` give `app-accent-invalid`, change nothing and write no event
  - **Unknown client**: `app-not-found`
  - **Redirect**: the `Location` is `/admin/apps?msg=…&app={clientId}#app-{clientId}`
  - **Apps page**: links `/branding/accent/f26b1d.css` once even when two apps share the colour; the card's `data-branding-sample` sits inside `data-accent="f26b1d"`; the page `<body>` has no `data-accent`

### Implementation for User Story 1

- [X] T015 [US1] Implement both GET endpoints in `src/Umbral/Handlers/Branding.fs` per the contract (research R4, R7). Reuse the `If-None-Match` matching of `Handlers/Assets.fs`: move `matches` to a shared place, or copy it with a comment pointing at the original. Notes:
  - the accent body comes from `Branding.Accent` (parse `#` + path value) and `onAccent`; there is no database read
  - the logo comes from `BrandingStore.logoBytes`
  - validate both path values with the anchored regexes before any lookup

  Add `Branding.endpoint` to `Program.endpoints` in `src/Umbral/Program.fs`.
- [X] T016 [US1] Add these texts to `src/Umbral/Views/Texts.fs` (record field and English value):
  - `SecuredByUmbral` ("Secured by Umbral")
  - `BrandingHeading`, `AccentLabel`, `AccentHint` ("# and six hexadecimal digits, for example #f26b1d. Leave empty to use Umbral's colour."), `AccentSave`, `BrandingSampleLabel`, `NoLogo`
  - notice texts for `app-accent-saved`, `app-accent-low-contrast` and `app-accent-invalid`, with the wording in the admin contract, added to `noticeEn` and `knownNotice`
- [X] T017 [US1] Change `src/Umbral/Views/Layout.fs`:
  - Add `signInPage ctx t title (branding: SignInBranding) content`, a variant of `shell` that:
    - sets the same CSP header
    - links `/branding/accent/{hex}.css` after `Assets.css` when there is an accent
    - sets `data-accent` on `<body>`
    - omits the `<header>`
    - renders a footer with the mark and `t.SecuredByUmbral`, marked `data-secured-by`
  - Keep `page` byte-for-byte as it is. Factor `shell` so the two share the `<head>` without changing `page`'s output.
  - Give `operatorPage` a `stylesheets: string list` parameter, linked after `umbral.css`, and pass `[]` at every existing caller.
- [X] T018 [US1] Change `src/Umbral/Handlers/Connect/Picker.fs`:
  - after the app is resolved, read `BrandingStore.forApp` (no query when there is no app)
  - no branding: render exactly as today through `Layout.page`
  - with branding: render through `Layout.signInPage`, with the logo `<img>` first in the card (`data-signin-logo`, `alt` = app name, `width`/`height`, classes for at most 96 px tall and at most full width)
  - connector buttons are `Components.Primary` when there is an accent and `Secondary` otherwise; `button` takes the kind as a parameter
  - add a comment that branding is resolved from the same `clientIdOf returnUrl` and from nothing else (FR-003)
- [X] T019 [US1] Implement `POST /admin/apps/{clientId}/branding/accent` in `src/Umbral/Handlers/Admin/AppBranding.fs` for setting a colour (clearing comes in US3):
  - parse with `Accent.parse`
  - `BrandingStore.setAccent`
  - an `AppUpdated` event with `Detail = Some "branding_accent_set"`
  - the notice `app-accent-low-contrast` when `lowContrastOnCard`, else `app-accent-saved`
  - redirect to `/admin/apps?msg=…&app={clientId}#app-{clientId}`
  - an empty value redirects with `app-accent-invalid` until T037 handles clearing

  Add `AppBranding.endpoint` to `Program.endpoints`.
- [X] T020 [US1] Add the branding section to each app card in `src/Umbral/Handlers/Admin/Apps.fs`, with the markers in the admin contract:
  - give each card the `id="app-{clientId}"` anchor if it has none
  - `data-branding`
  - `data-branding-logo` showing `NoLogo` for now (US2 adds the image and the upload form)
  - `data-branding-sample`: a disabled `um-btn-primary` reading `t.SignInWith "…"`, wrapped in `data-accent` when an accent is set
  - `data-accent-form` with the `accent` text field (value `#hex` or empty), plus `<input type="color" data-accent-picker hidden>` with no `name`

  Load `BrandingStore.allForApps` once per page and pass the distinct accent stylesheet paths to `Layout.operatorPage`.
- [X] T021 [US1] In `src/Umbral/Assets/umbral.js`, add the accent picker sync. For each `[data-accent-form]`:
  - remove `hidden` from the picker
  - set the picker from the text field when the text field is a valid `#rrggbb`
  - picker `input` writes the lowercase value into the text field
  - text `input` updates the picker when valid

  Plain JavaScript in the file's existing style, with a comment explaining why it is not htmx.
- [X] T022 [US1] Run `src/Umbral/Assets/build-css.sh` for the new classes in `Layout.fs`, `Picker.fs` and `Apps.fs`, and commit `umbral.css`. Run T011–T014 and the whole suite: green.

**Checkpoint**: a seeded or accent-only branded app gets its sign-in page. Unbranded pages, admin pages and the error page are unchanged. MVP for operators who only want a colour.

---

## Phase 4: User Story 2 - Upload, crop and check a logo (Priority: P1)

**Goal**: the operator uploads a PNG, JPEG or WebP, crops it in the browser,
and Umbral stores a validated, rewritten PNG within the limits. Every refusal
explains itself.

**Independent test**: [quickstart.md](./quickstart.md) §2 (`*PngLogo*`, the
logo part of `*Branding*`) and §4.1–4.2 and §5 in a browser.

### Tests for User Story 2

- [X] T023 [P] [US2] Write `tests/Umbral.Tests/Infrastructure/PngBuilder.fs`, a test-only PNG encoder:
  - parameters: width, height, colour type (0, 2, 3, 4, 6), bit depth, interlace flag, extra chunks (name and bytes, inserted before or after `IDAT`), split `IDAT` into N chunks, corrupt the CRC of the named chunk, replace the inflated payload (for bombs and short data)
  - image rows with filter byte 0, compressed with `ZLibStream`
  - its own CRC-32, written separately from the product's, so a shared bug cannot hide
- [X] T024 [P] [US2] Write `tests/Umbral.Tests/PngLogoTests.fs` per research R2.
  - **Accepted**:
    - each colour type at 32×32 and 512×512, plus 512×32 and 32×512
    - type 3 with `PLTE` and `tRNS`
    - split `IDAT`
    - `tEXt`, `iTXt`, `eXIf`, `iCCP`, `gAMA`, `pHYs`, `tIME` and `acTL` present: the output has none of them
    - the output re-validates to the same bytes (idempotent)
    - `Sha256` is the lowercase hex of the output
    - `Width` and `Height` are correct
  - **Refused**:
    - `NotPng` for JPEG (`FF D8 FF E0…`), WebP (`RIFF….WEBPVP8 `), SVG text, HTML and GIF
    - `Empty` for 0 bytes
    - `Corrupt` for:
      - a truncated file
      - a bad CRC
      - a chunk length past the end
      - IHDR not first
      - an invalid bit depth for the colour type
      - compression or filter method not 0
      - inflated data one byte short
      - a filter byte of 5
      - a missing `IEND`
      - type 3 without `PLTE`
    - `Corrupt` for a zlib stream that inflates past `expected` (a bomb: 512×512 RGBA header with an IDAT inflating to 50 MB); it must return quickly and not allocate the 50 MB
    - `Interlaced`
    - `UnsupportedChunk "abCD"` for an unknown critical chunk `ABCD`
    - `TooSmall` at 31 px on either side
    - `TooBig` at 513 px on either side
    - `TooLarge` above `maxBytes`
- [X] T025 [P] [US2] Write the logo part of `tests/Umbral.Tests/AppBrandingAdminTests.fs` (`multipart/form-data` with the antiforgery token):
  - **Valid upload**: a valid 200×100 RGBA PNG gives `app-logo-saved`; the stored `logo_png` equals `PngLogo.validate` of the upload (not the upload bytes when they carried a `tEXt` chunk); one `AppUpdated` event with `branding_logo_set`; the Apps page shows `img` at `/branding/logo/{sha}.png` and the picker shows `data-signin-logo`
  - **Refused uploads**: each of `app-logo-missing` (no file, empty file), `app-logo-not-png` (JPEG bytes), `app-logo-corrupt`, `app-logo-interlaced`, `app-logo-too-small` and `app-logo-too-big` leaves an existing logo byte-identical and writes no event
  - **Notice values**: the too-small and too-big notices show the measured size on the following `GET /admin/apps`, carried by the `NoticeReason` cookie; that `GET` with a hand-written query `…&width=9999` shows no 9999
  - **Oversized body**: a body of 3.5 MB redirects with `app-logo-too-large`, never 413 or 500
  - **Access**: CSRF missing gives 400; no operator gives a redirect to sign-in

### Implementation for User Story 2

- [X] T026 [US2] Implement `src/Umbral/Domains/PngLogo.fs` per research R2 and the data model:
  - `maxSide = 512`, `minSide = 32`, `maxBytes = 1_310_720`
  - a table-driven CRC-32
  - a big-endian chunk walk with every length checked against the remaining bytes before slicing
  - IHDR rules
  - collect `PLTE`, `tRNS` and the concatenated `IDAT`
  - drop ancillary chunks
  - refuse unknown critical chunks
  - `expected = height * (1 + (width * bitsPerPixel + 7) / 8)`
  - inflate through `ZLibStream` into a buffer of `expected + 1`, reading in a loop until 0; the result is valid only if exactly `expected` bytes came out
  - check each row's filter byte is ≤ 4
  - rewrite as signature, IHDR, PLTE?, tRNS?, one IDAT (the original compressed bytes), IEND, with fresh CRCs
  - SHA-256 as lowercase hex
  - pure, with no I/O; no exception escapes, so a malformed input is an `Error`
- [X] T027 [US2] Generalise `WebForms.NoticeReason` in `src/Umbral/Infrastructure/WebForms.fs` so `redirect` and `consume` take the "about" key (`slug` or `app`). Update the existing connector callers, whose behaviour is unchanged. The payload holds the reason as `key=value;key=value`, and the reading screen parses it.
- [X] T028 [US2] Add these texts to `src/Umbral/Views/Texts.fs`:
  - `LogoLabel`, `LogoHint` ("PNG, JPEG or WebP. Umbral stores a PNG of at most 512 × 512 pixels. Without JavaScript, upload a PNG of 32–512 pixels a side, up to 1.25 MB.")
  - `LogoSave`, `LogoUseWhole`, and inline script errors (`LogoErrorFormat`, `LogoErrorFileSize limit actual`, `LogoErrorDimensions limit actual`, `LogoErrorNotImage`), rendered as `data-*` attributes so the script never holds text of its own
  - notices `app-logo-saved`, `app-logo-missing`, `app-logo-not-png`, `app-logo-corrupt`, `app-logo-interlaced`, `app-logo-too-small`, `app-logo-too-big` and `app-logo-too-large`, with the wording in the admin contract; the size-bearing ones take the values from `NoticeReason`
- [X] T029 [US2] Implement `POST /admin/apps/{clientId}/branding/logo` in `src/Umbral/Handlers/Admin/AppBranding.fs`:
  - Endpoint metadata: a 3 MB request size limit, and form options with `MultipartBodyLengthLimit` and `MemoryBufferThreshold` of 3 MB, so the body is never spilled to a temp file (research R3).
  - Verify with a test that the antiforgery middleware's form read honours these limits. If it does not, read the form explicitly with those `FormOptions` before `readValidated` validates, and record which path was needed in research.md R3.
  - Catch `BadHttpRequestException` (413) and `InvalidDataException` from the form read and redirect with `app-logo-too-large`.
  - Read at most `maxBytes + 1` bytes of the `logo` file, then `PngLogo.validate`.
  - On `Ok`: `BrandingStore.setLogo`, an `AppUpdated` event with `branding_logo_set`, and `app-logo-saved`.
  - On `Error`: map it to its notice. Size-bearing refusals go through `NoticeReason.redirect` with `app` and the measured values.
- [X] T030 [US2] Extend the branding section in `src/Umbral/Handlers/Admin/Apps.fs`:
  - `data-branding-logo` shows `<img src="/branding/logo/{sha}.png" alt={name} width height>` at the same size class as the picker, or `NoLogo`
  - `data-logo-form`: `method=post`, `enctype=multipart/form-data`, the antiforgery input, `<input type="file" name="logo" accept="image/png,image/jpeg,image/webp" data-logo-input>`, the hint, an empty `data-logo-crop` container, the error texts as `data-error-*` attributes, and `LogoSave`
  - render the logo notices for the card named by `app=`, reading values through `NoticeReason.consume`
- [X] T031 [US2] Add the crop and convert behaviour to `src/Umbral/Assets/umbral.js` per the "Crop script" section of [contracts/admin-branding.md](./contracts/admin-branding.md):
  - the size check, the magic-byte check (PNG, JPEG, WebP), a `FileReader` `data:` URL into an `<img>`, and the 4096 px check
  - a crop rectangle over the scaled preview, with pointer drag to move, four corner handles to resize, arrow keys to nudge (1 px; Shift resizes), and `LogoUseWhole` to reset; the minimum is 32 source pixels
  - on submit, draw onto a canvas no larger than 512 on either side, then `toBlob('image/png')`, put the result in the input through `DataTransfer` as `logo.png`, and submit; if either API is missing, submit the original
  - errors render inline from the `data-error-*` texts and clear the input

  The crop UI must work under the existing CSP: positioning uses classes and the CSSOM (`element.style.left = …` from script is allowed by CSP; `style=""` in markup is not).
- [X] T032 [US2] Run `src/Umbral/Assets/build-css.sh` for the crop panel classes and commit `umbral.css`. Run T023–T025 and the whole suite: green.
- [ ] T033 [US2] Manual: run [quickstart.md](./quickstart.md) §4.1–4.2 and §5 in current Chromium, Firefox and Safari. Record the browser versions and any deviations in this file's Notes. Fix what fails and add a test where one is possible (constitution: a bug fix adds the test).

**Checkpoint**: Stories 1 and 2 together are the feature as the user asked for it.

---

## Phase 5: User Story 3 - Change or remove branding (Priority: P2)

**Goal**: replace, remove and clear branding, with no stale logo, no orphaned
row and no orphaned bytes.

**Independent test**: [quickstart.md](./quickstart.md) §6, and the tests below.

### Tests for User Story 3

- [ ] T034 [P] [US3] Extend `tests/Umbral.Tests/AppBrandingAdminTests.fs`:
  - **Replace**: upload logo A, then a different logo B. B's address serves B, A's address returns 404, the picker's `img src` changed, and there are two `branding_logo_set` events.
  - **Remove logo**: `POST …/branding/logo/delete` gives `app-logo-removed`, one `branding_logo_removed` event, and the old address returns 404. The row survives when an accent is set and is deleted otherwise. A second remove gives `app-logo-removed` with no new event.
  - **Clear accent**: an empty accent gives `app-accent-cleared` and one `branding_accent_cleared` event; the row is deleted when there is no logo. Clearing when no accent is set gives no event.
  - **Access**: CSRF and operator checks on the delete route.
- [ ] T035 [P] [US3] Extend `tests/Umbral.Tests/SignInBrandingTests.fs`:
  - after removing the logo and clearing the accent, the app's sign-in page meets case (a), unbranded
  - after removing only the logo, the accent still applies and `data-signin-logo` is gone
- [ ] T036 [P] [US3] Extend the app-delete tests (the file that covers `AppDelete`, else `AppBrandingAdminTests.fs`): deleting a branded app removes its `app_branding` row, its logo address returns 404, and only the existing `AppDeleted` event is written.

### Implementation for User Story 3

- [ ] T037 [US3] In `src/Umbral/Handlers/Admin/AppBranding.fs`:
  - add `POST /admin/apps/{clientId}/branding/logo/delete`: `BrandingStore.removeLogo`, an event only if a logo existed, then `app-logo-removed`
  - make the accent handler treat an empty value as clear: `setAccent None`, an event only if one was set, then `app-accent-cleared`
  - add the notices `app-logo-removed` and `app-accent-cleared` and `AppLogoRemoveConfirm name` to `src/Umbral/Views/Texts.fs`
- [ ] T038 [US3] In `src/Umbral/Handlers/Admin/Apps.fs`, render the remove-logo form, with `Components.confirmSubmit (t.AppLogoRemoveConfirm name)`, only when the app has a logo.
- [ ] T039 [US3] In `src/Umbral/Handlers/Admin/AppDelete.fs`, call `BrandingStore.deleteFor config clientId` next to `AppConnectors.deleteFor`, with a comment on the same reasoning (no foreign key into OpenIddict's tables).
- [ ] T040 [US3] Manual: run [quickstart.md](./quickstart.md) §6 and record the result in Notes.

**Checkpoint**: all three stories work and are tested.

---

## Phase 6: Polish & cross-cutting

- [ ] T041 [P] Make sure `tests/Umbral.Tests/CsrfTests.fs` covers the three new POST routes, adding them if it lists routes explicitly. Then run `MetaTests.fs`, `SecurityEventCoverageTests.fs` and `LogRedactionTests.fs` and confirm they pass unchanged.
- [ ] T042 [P] Run `tests/Umbral.Tests/IdleMemoryTests.fs` and confirm idle RSS is unaffected (FR-026, SC-007). Record the figure in Notes.
- [ ] T043 [P] Add a "Brand an app's sign-in page" section to `README.md`, after the Apps section (FR-027), covering:
  - where branding appears and where it does not (the admin screens, the protocol error page)
  - the "Secured by Umbral" mark and why it cannot be removed
  - accepted formats and every limit (2 MB, 4096 px, stored 32–512 px and 1.25 MB)
  - cropping, and that converting and cropping need JavaScript, with the no-script PNG path
  - the accent format, the automatic text colour, and the low-contrast warning
  - that logos are stored in `umbral.db`, so the existing backup advice covers them
  - that a newer database (schema 4) cannot be opened by an older image

  Add a line to the README's table of contents if it has one.
- [ ] T044 Run [quickstart.md](./quickstart.md) §1–3 and the full `dotnet test -c Release`, and record the test count and commit in Notes.
- [ ] T045 Set **Status** in `specs/006-app-signin-branding/spec.md` to `Implemented` once T044 passes, and mark this file's tasks done.

---

## Dependencies & execution order

### Phases

- Setup (T001–T002) → Foundational (T003–T010) → stories → Polish.
- **US1** depends on Foundational only. It seeds branding through `BrandingStore` in tests.
- **US2** depends on Foundational. Its admin and picker assertions (T025) and T030 build on US1's card section (T020) and picker change (T018). Its domain work (T023, T024, T026) can start right after Foundational, in parallel with US1.
- **US3** depends on US1 (accent handler T019, card T020) and US2 (logo endpoint T029).
- Polish after US3. The README section (T043) can be drafted once the limits are final (after T026).

### Within phases

- T003 before T004. T005 before T006. T007 before T008 and T009. T009 before T010.
- US1: T011–T014 written first and failing. T015 before T011/T012 pass. T016 → T017 → T018 → T019 → T020 → T021 → T022.
- US2: T023 before T024 and T025. T026 → T027 → T028 → T029 → T030 → T031 → T032 → T033.
- US3: T034–T036 first, then T037 → T038 → T039 → T040.

### Parallel opportunities

```text
Setup:        T002 alongside T001
Foundational: T004, T006, T008, T010 (tests) each alongside the next implementation task; T005 alongside T003 and T007
US1 tests:    T011, T012, T013, T014 in parallel
US2 early:    T023 + T024 + T026 (PngBuilder, PngLogo tests, PngLogo) in parallel with all of US1
US3 tests:    T034, T035, T036 in parallel
Polish:       T041, T042, T043 in parallel
```

Only one writing agent at a time touches `Texts.fs`, `Apps.fs`,
`AppBranding.fs`, `Layout.fs`, `umbral.js`, `umbral.css` or `Program.fs`.
`Domains/PngLogo.fs` and its tests are the safe place to fan out.

## Implementation strategy

1. **MVP = Foundational + US1**: an app can be branded with its colour from the
   Apps screen, and logos already display when seeded. Shippable as "accent
   colour per app".
2. **+ US2**: logos can be uploaded. This is the release unit the user asked
   for.
3. **+ US3**: replace, remove and clean up, so an operator can undo a mistake
   without re-registering the app.
4. **+ Polish**: the README section makes it done (Principle VI).

## Notes

- Commit after each task or logical group. The phase order here matches the
  plan's "Implementation order", except that the plan's step 7 (script) is
  split between US1 (accent sync) and US2 (crop).
- Licence: Umbral will be MIT (decided 2026-10-15). This does not change the
  design (research R1). Adding the `LICENSE` file is the separate CodeAlta task
  `2026-10-15-choose-and-add-a-licence-for-umbral-n7c8`.
