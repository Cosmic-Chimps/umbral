# Quickstart: Validating Per-App Sign-In Branding

**Feature**: [spec.md](./spec.md) | Contracts: [public endpoints](./contracts/public-endpoints.md),
[admin branding](./contracts/admin-branding.md) | [data model](./data-model.md)

Steps 1–3 are automated and run in CI. Steps 4–6 are manual browser checks,
done once before the feature is called done, because the repository has no
browser test harness (research R11).

## 1. Nothing changes for an unbranded instance (SC-001, SC-006, SC-007)

```bash
src/Umbral/Assets/build-css.sh --check      # umbral.css regenerated after the palette change
dotnet build -c Release
dotnet test -c Release
```

Expected:

- the whole existing suite passes
- the unbranded picker test finds no `data-accent`, `data-signin-logo` or
  `data-secured-by`
- the upgrade test opens a version 3 database, sees version 4 and no
  `app_branding` rows, and the picker for every app is unchanged
- `IdleMemoryTests` stays under 150 MB

## 2. The server's rules (FR-009 to FR-017, SC-003, SC-004)

```bash
dotnet test -c Release --filter-class "*PngLogo*"
dotnet test -c Release --filter-class "*Accent*"
dotnet test -c Release --filter-class "*Branding*"
```

Expected:

- **PNG validator**:
  - accepts every colour type at 32 and 512 px, and rewrites each without
    metadata chunks
  - refuses JPEG, WebP, SVG and HTML bytes; interlaced images; bad CRC;
    truncated files; an unknown critical chunk; a zlib bomb; 31 px; 513 px; and
    over 1.25 MB, each with its own error
- **Accent**:
  - all 16,777,216 colours get ≥ 4.5:1 on-accent contrast
  - `#F26B1D` is stored as `f26b1d`
  - `f26b1d`, `#fff`, `orange` and `rgb(1,2,3)` are refused
- **Admin endpoints**:
  - refused without the operator or without a CSRF token
  - a refused upload leaves the stored logo byte-identical
  - replacing a logo changes its address, and the old address returns 404
  - deleting the app removes the row and makes the logo address return 404
  - each change writes one `AppUpdated` event with its `Detail`
- **Public endpoints**: the headers exactly as in the contract; 304 on
  `If-None-Match`; 404 for an uppercase colour or a non-hex hash
- **Picker**:
  - a branded app shows the markers from the contract
  - its failure notice keeps the branding
  - `returnUrl` for app B with a link mentioning app A shows B's branding
  - an unknown client is unbranded
  - `/connect/error` is unbranded for a registered client
  - the CSP header equals `Layout.contentSecurityPolicy`, and no `src` or
    `href` leaves the origin

## 3. Schema guard

A version 4 database opened by a build with `SchemaVersion = 3` refuses to
start, which the existing guard test covers. Re-run `DatabaseTests` and expect
it to pass.

## 4. Brand an app in a browser (Stories 1–2, SC-002)

Run locally as the README's "Run it" step does (Development and
`UMBRAL__INSECURE_ALLOW_HTTP=true`), with the fake connector enabled. Register
two apps, Frunchy and Recipes, and an OIDC test client (`oidcdebugger.com` or
any local client) for each.

1. **Apps → Frunchy → Branding**: choose a 3000 × 2000 JPEG photo of a logo.
   The crop panel appears. Drag the rectangle around the logo, then **Save
   logo**. Time it from opening the Apps screen: it should take under 2
   minutes (SC-002).
2. The card shows the cropped logo at sign-in size, at most 512 px stored
   (open the image address and check its dimensions).
3. Set the accent to `#f26b1d`. The sample button turns orange with black
   text. The rest of the admin page keeps its own blue.
4. Start a sign-in from Frunchy:
   - no header bar
   - the logo at the top of the card
   - orange connector buttons
   - "Secured by Umbral" at the bottom
5. Start a sign-in from Recipes: the page is exactly as before the feature.
6. In the browser's network panel for step 4: every request goes to Umbral's
   own origin (SC-005).

## 5. Refusals and edge cases in the browser

| Try | Expect |
| --- | --- |
| An SVG file | Inline error naming PNG, JPEG and WebP. Nothing is sent |
| A 9 MB PNG | Inline error with the 2 MB limit and the file's size |
| A 5000 × 5000 PNG under 2 MB | Inline error with 4096 px and the image's size |
| `notes.txt` renamed to `logo.png` | Inline error: not an image Umbral accepts |
| A JPEG from a phone held upright (EXIF orientation 6) | The crop panel shows it upright, and the saved logo is upright |
| An animated PNG or WebP | Saved as its first frame |
| A transparent PNG | Transparency kept on the dark card |
| A crop of 20 × 20 source pixels | The handles stop at 32 × 32 |
| Accent `#0a0a10` | Saved, with the low-contrast warning. Button text white and readable |
| Accent `orange` | Refused, with the `#f26b1d` example |
| JavaScript disabled, a 256 × 256 PNG | Saved whole |
| JavaScript disabled, a JPEG | Refused, with the "turn on JavaScript" hint |

Repeat steps 4.1 and 4.4 in current Chromium, Firefox and Safari.

## 6. Replace, remove, delete (Story 3)

1. Save a new logo for Frunchy, then reload Frunchy's sign-in page with the
   cache intact. The new logo shows.
2. **Remove logo**: the page keeps the orange buttons, has no logo, and still
   shows "Secured by Umbral".
3. Clear the accent: Frunchy's sign-in page is now identical to Recipes'.
4. Brand Frunchy again, note the logo address, and delete Frunchy. The address
   returns 404.
5. Run `sqlite3 /data/umbral.db "select event_type, client_id, detail from security_events order by id desc limit 8;"`.
   Each change above appears once, as `app_updated` with its `branding_*`
   detail.
