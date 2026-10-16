# Implementation Plan: Per-App Sign-In Branding

**Branch**: `006-app-signin-branding` | **Date**: 2026-10-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/006-app-signin-branding/spec.md`

## Summary

Each App can have a logo and an accent colour that brand its sign-in page
(`/signin`). Admin pages, the protocol error page and every unbranded page stay
exactly as they are. The design rests on three decisions:

1. **The browser does the image work, the server only validates.** The admin
   page crops, scales (≤ 512 px) and converts the operator's PNG, JPEG or WebP
   to PNG on a canvas. The server accepts only PNG. It walks and CRC-checks the
   chunks, bounds-checks the inflated image data without decoding pixels,
   drops every metadata chunk, and stores a rewritten PNG. No image library, no
   native decoder of untrusted input, and no memory spike on a Pi (research R1,
   R2).
2. **The accent colour arrives as a same-origin stylesheet.** The pages' CSP
   forbids inline styles. `/branding/accent/{rrggbb}.css` is generated from its
   path and scoped to `[data-accent="…"]`. The Tailwind palette's `primary` and
   `on-primary` become CSS channel variables so the stylesheet can recolour
   them. Text on the accent is black or white by WCAG contrast, never below
   4.58:1 (R4).
3. **Logos live in SQLite and are addressed by their hash.** A new
   `app_branding` table (schema version 4) holds the PNG as a BLOB. The logo is
   served at `/branding/logo/{sha256}.png`, immutable, `nosniff`, sandboxed.
   Replacing a logo changes its address (R6, R7).

The spec was adjusted at plan time in three places, all from R1/R5:

- the no-script edge case
- FR-006's wording, so the unbranded page's existing header and footer count
  as its Umbral mark
- FR-011: "before processing" instead of "before decoding"

## Technical Context

**Language/Version**: F# on .NET 10 (`global.json`), ASP.NET Core. Plain
JavaScript in `Assets/umbral.js`.

**Primary Dependencies**: existing only (Oxpecker, Oxpecker.ViewEngine, htmx 4,
OpenIddict 7.7, Microsoft.Data.Sqlite, Tailwind CLI at build time).
`System.IO.Compression.ZLibStream` and `System.Security.Cryptography.SHA256`
are inbox. **No new package.**

**Storage**: SQLite `umbral.db` in the data directory. New Umbral-lane table
`app_branding`, `SchemaVersion` 3 → 4 (data-model.md).

**Testing**: xunit.v3 on Microsoft.Testing.Platform, TestServer via
`Program.buildApp` (`tests/Umbral.Tests/Fixtures`), `FakeTimeProvider`.
Test-side PNG builder (no binary fixtures). `dotnet test -c Release`. The crop
script is checked manually (quickstart §4–6).

**Target Platform**: Linux container, `linux/amd64` and `linux/arm64`,
framework-dependent. Admin browser: current Chromium, Firefox and Safari.

**Project Type**: single server-rendered web service.

**Performance Goals**: picker adds one indexed read by client id, and none
without an app. Logo and accent responses are cached `immutable`. A logo upload
holds at most about 3 MB of request body plus about 1 MB of inflated data.

**Constraints**:

- idle RSS < 150 MB (SC-007)
- CSP unchanged on every page
- no inline style or script
- no JS build step
- unbranded `/signin` markup unchanged (SC-001)
- `TreatWarningsAsErrors`
- SQL literal only (MetaTests)

**Scale/Scope**: a handful of apps per instance, one operator, logos ≤ 1.25 MB
each.

## Constitution Check

*Evaluated before Phase 0 and again after Phase 1. Both passes agree.*

| Gate | Result | Notes |
| --- | --- | --- |
| Concept count | PASS | Logo and accent are attributes of App, edited on the app's card on `/admin/apps`, and deleted with the app. No noun, screen or top-level section (FR-018). |
| Single artifact | PASS | No new package or runtime dependency. Logos live in `umbral.db` inside the data directory (FR-024). The upload is buffered in memory, not in a temp file outside it (R3). |
| Protocol safety | PASS | No token, signature or key code is touched. The PNG validator is a format parser, not cryptography. SHA-256 is the BCL's and serves only as a content address. |
| Secure default | PASS | Unbranded by default. Uploads are restricted by content to rewritten PNG, with metadata stripped and bomb-bounded. Logos are served `nosniff`, `sandbox`, `same-origin`. The page loads nothing cross-origin. The app is resolved for branding the same way as for its name, so a crafted link cannot borrow a logo. The unredirectable error page is never branded. CSRF and operator checks apply on all three POSTs. Changes are recorded as `AppUpdated` events. |
| Documentation | PASS | Tasks include a README "Brand an app's sign-in page" section, and every refusal message says what to do next (Principle VI, FR-027). |
| Scope | PASS | Not on the Principle VII list. Two fixed attributes, not a theme editor or a login-flow builder. Global branding, custom CSS, fonts and wording are explicitly out (spec Assumptions). |

Testing discipline: no OIDC protocol behaviour changes. The picker is tested
through the real `/connect/authorize` → `/signin` path. The bug-fix rule
applies to anything found in the manual pass.

## Project Structure

### Documentation (this feature)

```text
specs/006-app-signin-branding/
├── spec.md                       # adjusted at plan (R1, R5)
├── plan.md                       # this file
├── research.md                   # R1–R11
├── data-model.md                 # app_branding, domain types, store, events
├── quickstart.md                 # validation runbook
├── contracts/
│   ├── public-endpoints.md       # /branding/logo, /branding/accent, /signin markers
│   └── admin-branding.md         # three POSTs, notices, card markers, crop script
├── checklists/requirements.md
└── tasks.md                      # /speckit-tasks
```

### Source Code (repository root)

```text
src/Umbral/
├── Umbral.fsproj                         # Compile entries in dependency order (below)
├── Domains/
│   ├── Branding.fs                       # NEW: Accent (parse, hex, rgb, onAccent, contrast), AppBranding, LogoRef
│   └── PngLogo.fs                        # NEW: CRC-32, chunk walk, IHDR rules, bounded inflate, rewrite, SHA-256
├── Infrastructure/
│   ├── Database.fs                       # + app_branding in schema; SchemaVersion 4; upgrade step 4
│   ├── BrandingStore.fs                  # NEW: forApp, allForApps, logoBytes, setLogo, removeLogo, setAccent, deleteFor
│   └── WebForms.fs                       # NoticeReason takes the "about" key (slug | app)
├── Views/
│   ├── Texts.fs                          # SecuredByUmbral, branding labels, hints, 11 notices
│   ├── Components.fs                     # brandingSection pieces; connector button kind as a parameter
│   └── Layout.fs                         # signInPage (branded shell); operatorPage takes stylesheets
├── Assets/
│   ├── tailwind.config.js                # primary/on-primary as rgb(var(--…-rgb) / <alpha-value>)
│   ├── umbral.input.css                  # :root channel variables; --color-primary derived from them
│   ├── umbral.css                        # regenerated by build-css.sh
│   └── umbral.js                         # + logo crop/convert, accent picker sync
├── Handlers/
│   ├── Branding.fs                       # NEW: GET /branding/logo/{sha}.png, GET /branding/accent/{hex}.css
│   ├── Admin/AppBranding.fs              # NEW: POST …/branding/logo, …/logo/delete, …/accent
│   ├── Admin/Apps.fs                     # card gains the branding section; page links accent sheets
│   ├── Admin/AppDelete.fs                # + BrandingStore.deleteFor
│   └── Connect/Picker.fs                 # reads branding; branded vs unbranded view
└── Program.fs                            # endpoints list gains Branding and AppBranding
tests/Umbral.Tests/
├── Infrastructure/PngBuilder.fs          # NEW: test-side PNG encoder (all colour types, chunks, interlace flag)
├── PngLogoTests.fs                       # NEW
├── AccentTests.fs                        # NEW: parse table, exhaustive 4.5:1, low-contrast threshold
├── BrandingStoreTests.fs                 # NEW: lifecycle, row deletion, unique hash
├── BrandingEndpointTests.fs              # NEW: logo/accent headers, 304, 404
├── AppBrandingAdminTests.fs              # NEW: three POSTs, refusals, events, delete-with-app
├── SignInBrandingTests.fs                # NEW: picker markers, failure path, crafted link, unknown client, error page, CSP/origin
├── DatabaseTests.fs                      # + v3 → v4 upgrade
└── CsrfTests.fs, MetaTests.fs, PageAssetsTests.fs   # extended or re-run as they enumerate endpoints and assets
README.md                                 # "Brand an app's sign-in page" (Apps are documented in the README; docs/ has no Apps page)
```

Compile order: `Domains/Branding.fs` and `Domains/PngLogo.fs` go after
`Domains/RedirectUris.fs`. `Infrastructure/BrandingStore.fs` goes after
`AppConnectors.fs`. `Handlers/Branding.fs` goes after `Handlers/Assets.fs`.
`Handlers/Admin/AppBranding.fs` goes after `Handlers/Admin/AppDelete.fs`.

**Structure Decision**: no new project or layer. The validator and colour maths
are pure `Domains/` modules, like `RedirectUris.fs`. The store follows
`AppConnectors.fs`. The public endpoints follow `Handlers/Assets.fs`.

## Implementation order

Each step leaves the suite green and the unbranded path unchanged.

1. **Palette as variables**: change `tailwind.config.js` and
   `umbral.input.css`, then regenerate `umbral.css`. Assert that the computed
   colours are unchanged (`PageAssetsTests`) and that `build-css.sh --check`
   passes.
2. **Domains**: `Branding.fs` and `PngLogo.fs`, with `PngBuilder`,
   `AccentTests` and `PngLogoTests`.
3. **Storage**: schema v4, `BrandingStore`, `AppDelete` cleanup,
   `BrandingStoreTests`, and the upgrade test.
4. **Public endpoints**: `Handlers/Branding.fs` and its tests.
5. **Picker**: the branded shell, the view branch and texts, and
   `SignInBrandingTests`.
6. **Admin**: `AppBranding.fs` POSTs with form limits, `NoticeReason`
   generalisation, notices, events, the card section, and
   `AppBrandingAdminTests` and `CsrfTests`.
7. **Script**: crop/convert and accent sync in `umbral.js`, then the manual
   quickstart §4–6.
8. **Docs**: the README section. Re-run the full suite and `IdleMemoryTests`.

## Phase outputs

- [research.md](./research.md): what the code does today (A–I), decisions
  R1–R11, and what was rejected.
- [data-model.md](./data-model.md): the table, its lifecycle, domain types, the
  store and events.
- [contracts/](./contracts/): public endpoints and sign-in markers; the admin
  surface and the crop script.
- [quickstart.md](./quickstart.md): how to prove it, automated and manual.

Agent context: this repository has no `update-agent-context` script and no
agent context file to update, so there is nothing to run.

## Complexity Tracking

| Deviation | Why needed | Simpler alternative rejected because |
| --- | --- | --- |
| A hand-written PNG structure validator (about 200 lines) | FR-010 and FR-013 require judging content and storing Umbral's own bytes. Without a library, this is the smallest code that does both | An image library: ImageSharp 3.x is usable now that Umbral is MIT-licensed, but decoding takes about 64 MB per upload on a Pi and it would be one more untrusted-input parser and package. It stays the fallback. SkiaSharp and Magick.NET run native decoders on untrusted input in-process (R1) |
| Cropping and conversion need JavaScript in the admin page | The browser decodes and scales, so the server never decodes pixels | A server-side decoder brings the costs above. Without a script, a ready-made PNG still works, so the admin page is not script-only |
| The Tailwind palette's `primary` and `on-primary` become CSS variables | The CSP forbids inline styles, and compiled literal colours cannot be overridden | A CSP nonce or hash per response: a per-page policy, more moving parts, and a second CSP shape to test |
