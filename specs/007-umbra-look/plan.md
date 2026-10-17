# Implementation Plan: The Umbra Look, the Gnomon Mark, and a Theme Switch

**Branch**: `alta/subtle-alioth-qrcn` | **Date**: 2026-10-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/007-umbra-look/spec.md`

## Summary

Restyle every page in the chosen **Umbra** direction with the **Gnomon** mark, in
a dark and a light theme. All colour tokens become channel custom properties
with a dark set on `:root` and a light set applied by `data-theme="light"` or by
`prefers-color-scheme: light` under `data-theme="system"` (research R1). The
server writes `data-theme` on `<html>` from an `umbral_theme` cookie, set by a
new antiforgery-checked `POST /theme` (R5). Three OFL fonts are self-hosted as
embedded woff2 (R4). The operator chrome becomes a side rail; Create app gets a
page of its own and a native `<dialog>` on the Apps screen (R7, R8). Feature
006's accent sheet sets the new button-fill tokens too and its low-contrast
warning checks both themes' cards (R9).

## Technical Context

**Language/Version**: F# on .NET 10 (SDK pinned by `global.json`)

**Primary Dependencies**: ASP.NET Core, Oxpecker + Oxpecker.ViewEngine, htmx 4
(unchanged); Tailwind standalone CLI 3.4.19 at build time only (`build-css.sh`)

**Storage**: None new. The theme lives in a browser cookie only.

**Testing**: xUnit in `tests/Umbral.Tests` (TestServer via `Infrastructure/Fixtures.fs`, `Flows.fs`)

**Target Platform**: Linux container (amd64, arm64); browsers with CSS custom
properties, `prefers-color-scheme`, `<dialog>` (all evergreen browsers)

**Project Type**: Server-rendered web service

**Performance Goals**: No extra request before first paint beyond the stylesheet
and the fonts; fonts `font-display: swap`

**Constraints**: CSP `default-src 'self'` unchanged (fonts are same-origin);
no inline style or script; idle RSS < 150 MB (spec 001 SC-007); image growth
≤ 400 KB (fonts are 84 KB)

**Scale/Scope**: ~10 page kinds, 5 shared view modules, 1 new handler, 1 new route
for Create app

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 design (below, unchanged).*

| Gate | Answer |
| --- | --- |
| Concept count | No new noun. The theme is a per-browser display preference that names nothing Umbral manages (spec, Constitution Check). Still three top-level screens; `/admin/apps/new` is the Apps screen's form on its own page, not a screen. |
| Single artifact | Fonts are embedded in the assembly; no runtime dependency; no state in the data directory (the cookie is the browser's). |
| Protocol safety | Nothing touches tokens, signatures or keys. |
| Secure default | `POST /theme` is antiforgery-checked (covered by CsrfTests automatically) and has a strict local-path rule for `returnUrl` (no open redirect). CSP unchanged. Cookie HttpOnly, SameSite Lax, Secure on HTTPS. |
| Documentation | README section on the look and the theme switch (T-docs in tasks). |
| Scope | Not on the Principle VII list. |

Technology constraints: no JavaScript build step, bundler or SPA (the dialog is
a few lines in the existing `umbral.js`); server-rendered throughout.

## Token Mapping and Contrast Table

Values per theme. "On" is the background the pairing is measured against.
Ratios are WCAG 2.x; text needs 4.5, input borders and focus rings 3.0.

| Token | Dark | Light | Used for |
| --- | --- | --- | --- |
| `surface` | `#0b0b14` | `#f4f1ea` | page background |
| `surface-container-lowest` | `#07070e` | `#ebe5d8` | crop stage |
| `surface-container-low` | `#14141f` | `#fffdf8` | cards (`.um-card`) |
| `surface-container` | `#181826` | `#faf7f0` | inputs, choices |
| `surface-container-high` | `#1d1d2b` | `#ebe5d8` | secondary buttons, icon discs, active rail entry |
| `surface-container-highest` | `#26263a` | `#e3ddd0` | hover of secondary buttons |
| `on-surface` | `#ece8df` | `#17151f` | text |
| `on-surface-variant` | `#a7a2b6` | `#5f5a6b` | secondary text |
| `outline` | `#74708a` | `#857e6e` | input, radio and theme-control borders (R3) |
| `outline-variant` | `#2e2e40` | `#ddd6c8` | dividers and card edges (decorative) |
| `primary` | `#f5c26b` | `#8a5a00` | links, `text-primary`, `border-primary`, focus ring, checked choice |
| `on-primary` | `#2a1d05` | `#fffdf8` | text on `bg-primary` |
| `primary-fill` (new) | `#f5c26b` | `#17151f` | primary button fill |
| `on-primary-fill` (new) | `#2a1d05` | `#f5c26b` | primary button text |
| `primary-container` | `#3a2e12` | `#fbefd6` | (kept for compatibility) |
| `secondary` | `#a7a2b6` | `#5f5a6b` | info alert tint |
| `secondary-container` | `#26263a` | `#e3ddd0` | (kept) |
| `success` | `#a8d99a` | `#2f5d1e` | success text and pill |
| `success-container` (new) | `#20301f` | `#e6eedc` | success pill and alert background |
| `warning` | `#f0b45e` | `#6b4500` | warning text and icon |
| `warning-container` (new) | `#2a2111` | `#fbefd6` | warning alert background |
| `warning-outline` (new) | `#6b5328` | `#e2c27f` | warning alert border (decorative) |
| `error` | `#ff8f7e` | `#b3261e` | error text, invalid input border |
| `on-error` | `#3a0b05` | `#ffffff` | text on danger buttons |
| `error-container` | `#3a1613` | `#f9dedb` | error alert background |
| `on-error-container` | `#ffdad4` | `#410e0b` | text on error container |

Mark properties (not Tailwind colours): `--mark-sun`, `--mark-base`,
`--mark-shadow`, `--mark-shadow-opacity`, `--mark-upright` (research R6).

### Contrast proof

| Pairing | Dark | Light | Need |
| --- | --- | --- | --- |
| `on-surface` on `surface` / card / input / high | 16.02 / 14.94 / 14.34 / 13.60 | 16.00 / 17.75 / 16.87 / 14.38 | 4.5 |
| `on-surface-variant` on `surface` / card / input / high | 7.92 / 7.38 / 7.09 / 6.72 | 5.89 / 6.53 / 6.21 / 5.29 | 4.5 |
| `on-surface-variant` on `highest` | 5.97 | 4.91 | 4.5 |
| `primary` (link) on `surface` / card / input / high | 11.95 / 11.14 / 10.70 / 10.14 | 5.25 / 5.83 / 5.54 / 4.72 | 4.5 |
| `on-primary-fill` on `primary-fill` | 10.04 | 11.01 | 4.5 |
| `on-primary` on `primary` | 10.04 | 5.83 | 4.5 |
| `error` on `surface` / card / high | 8.84 / 8.25 / 7.51 | 5.79 / 6.43 / 5.21 | 4.5 |
| `on-error` on `error` | 7.71 | 6.54 | 4.5 |
| `on-error-container` on `error-container` | 12.45 | 12.77 | 4.5 |
| `error` on `error-container` | 7.27 | 5.14 | 4.5 |
| `success` on `success-container` / card | 8.68 / 11.36 | 6.52 / 7.64 | 4.5 |
| `warning` on `warning-container` / card | 8.61 / 9.91 | 7.44 / 8.34 | 4.5 |
| `on-surface` on `warning-container` / `success-container` | 12.97 / – | 15.83 / 15.15 | 4.5 |
| `outline` (input border) on `surface` / card / input / high | 4.13 / 3.85 / 3.70 / 3.51 | 3.58 / 3.97 / 3.77 / 3.21 | 3.0 |
| focus ring (`primary`) on `surface` / card | 11.95 / 11.14 | 5.25 / 5.83 | 3.0 |

Palette values replaced for a purpose: the brief's input outline `#ddd6c8`
(1.42:1 on the light card) and the dark line `#2e2e40` (1.38:1) stay as
`outline-variant`; inputs use `outline` (R3). Every other value is the brief's.

## Project Structure

### Documentation (this feature)

```text
specs/007-umbra-look/
├── plan.md              # This file
├── research.md          # Phase 0 (R1–R9)
├── data-model.md        # Phase 1: the theme choice
├── quickstart.md        # Phase 1: how to see and check it
├── contracts/
│   └── theme.md         # POST /theme, the cookie, <html data-theme>, GET /admin/apps/new, fonts
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/Umbral/
├── Assets/
│   ├── tailwind.config.js     # two token sets → :root, [data-theme=light], media(system); fonts; new tokens
│   ├── umbral.input.css       # @font-face, mark rules, rail, theme control, dialog, button fill
│   ├── umbral.css             # regenerated by build-css.sh (committed)
│   ├── umbral.js              # + [data-dialog] open/close
│   └── fonts/                 # 3 woff2 + 3 OFL licences (new)
├── Views/
│   ├── Texts.fs               # theme control strings; low-contrast notice wording
│   ├── Theme.fs               # Gnomon drawings, favicon, ThemeChoice + ofRequest, new icons
│   ├── Assets.fs              # font assets
│   ├── Components.fs          # brand (lowercase wordmark), themeSwitch, button fill
│   └── Layout.fs              # data-theme, color-scheme/theme-color, rail, footer switch
├── Handlers/
│   ├── Assets.fs              # serves fonts (via Assets.all)
│   ├── ThemeChoice.fs         # POST /theme (new)
│   ├── Branding.fs            # accent sheet sets fill tokens; format version bump
│   ├── Home.fs                # Gnomon
│   └── Admin/Apps.fs          # Create app link + dialog; GET /admin/apps/new
├── Domains/Branding.fs        # lowContrastOnCard checks both cards
├── Umbral.fsproj              # EmbeddedResource fonts; Compile ThemeChoice.fs
└── Program.fs                 # route registration
Dockerfile                     # copy font licences into /app/licenses/fonts
README.md                      # the look and the theme switch
tests/Umbral.Tests/
├── ThemeTests.fs              # new: cookie absent/light/dark/invalid, <html data-theme>, POST /theme, returnUrl rule
├── PageAssetsTests.fs         # fonts served, no external font host, url()s resolve, favicon, updated accent-variable facts
├── AccentTests.fs             # low contrast on both cards
├── SignInBrandingTests.fs     # Gnomon in secured-by footer; theme control present; accent sets fill tokens
└── AdminScreenTests.fs (or a new RailTests.fs)  # rail entries, aria-current, Create app page and dialog
```

**Structure Decision**: Single web project, existing layout. One new handler
module and one new test module; everything else is edits.

## Design Notes per Area

**Layout.shell**
- `let theme = Theme.ofRequest ctx`; `html(lang="en", class'="bg-surface").data("theme", Theme.attr theme)`.
- `meta color-scheme`: `"dark light"` for System, `"light"` / `"dark"` otherwise.
- `meta theme-color`: System → two metas with `media="(prefers-color-scheme: dark)"` `#0b0b14` and `light` `#f4f1ea`; chosen → one.
- Favicon `Theme.faviconHref` (dark tile, tiny mark).
- Operator: `div.flex.min-h-screen.flex-col.md:flex-row` → `aside` (brand, nav with `aria-current`, `Components.themeSwitch` at the bottom) + `main`. Remove the old top header for Operator.
- Plain: header with brand as today; footer = mark (small) + `t.Footer` + theme switch.
- SignIn: no header; footer = `data-secured-by` + small Gnomon + `t.SecuredByUmbral` + theme switch.
- Skip link stays first.

**Components**
- `brand`: lowercase wordmark (`lowercase` class, display font) with `Mark.small` at 28–30 px; `Home` uses `Mark.full` at 64 px.
- `themeSwitch ctx t theme returnUrl`: `form method=post action=/theme` with antiforgery input, hidden `returnUrl`, `div role=group aria-labelledby=theme-label` holding three `button type=submit name=theme value=system|light|dark` each with `aria-label` and `aria-pressed`, icons Computer / LightMode / DarkMode, then the note `p` (`aria-live` not needed: full reload).
- `.um-btn-primary` reads `--color-primary-fill-rgb` / `--color-on-primary-fill-rgb`.

**Typography in CSS**
- `fontFamily`: `sans` → `"Schibsted Grotesk", ui-sans-serif, system-ui, sans-serif`; `display` → `"Bricolage Grotesque", "Schibsted Grotesk", sans-serif`; `mono` → `"Spline Sans Mono", ui-monospace, monospace`.
- `.text-display`, `.text-headline-lg` (and the wordmark) also get `font-family: var(--font-display); font-weight: 800` via a plain rule after utilities. `headline-md` stays in the text face at 600.
- `.um-mono` and `font-mono` → Spline Sans Mono.

**Theme handler (`Handlers/ThemeChoice.fs`)**: see contracts/theme.md.

**Accent**: `accentRule` adds the four fill properties; `AccentFormatVersion` +1;
`lowContrastOnCard` checks `#14141f` and `#fffdf8`; notice text updated.

## Complexity Tracking

No constitution violations to justify.
