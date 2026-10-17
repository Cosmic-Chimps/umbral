---
description: "Task list for 007 the Umbra look, the Gnomon mark, themes and the theme switch"
---

# Tasks: The Umbra Look, the Gnomon Mark, and a Theme Switch

**Input**: Design documents from `specs/007-umbra-look/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/theme.md](./contracts/theme.md), [quickstart.md](./quickstart.md)

**Tests**: Included. The task brief asks for them (the theme cookie absent,
light, dark and invalid; `<html>` carrying the theme; the favicon; no external
font host), and the theme endpoint is a redirect target, which is a security
boundary (open redirect, CSRF). Tests come first within a story where the
behaviour is server-side.

**Organization**: by dependency, not strictly by priority. The look (US1) is
the foundation everything else is drawn on. The rail (US3) comes before the
theme control (US2), because the control lives in the rail: building it in the
old header first would be thrown away.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1–US5 from spec.md
- F# compile order matters: a new `.fs` file needs its `<Compile Include>` in
  `src/Umbral/Umbral.fsproj` (before `Program.fs`) or
  `tests/Umbral.Tests/Umbral.Tests.fsproj` (before `SmokeTests.fs`).

## Conventions every task follows

- `dotnet build -c Release` (warnings are errors) and the tests the task names;
  the full suite is `dotnet test -c Release`.
- Every user-facing string lives in `src/Umbral/Views/Texts.fs`; where a mockup
  in `design/umbra/` and `Texts.fs` disagree, `Texts.fs` wins.
- No inline `<style>`, `style=""`, or inline script (CSP). Class names are
  literal strings in views. After changing a view, `tailwind.config.js` or
  `umbral.input.css`, run `src/Umbral/Assets/build-css.sh` and commit
  `umbral.css`; `build-css.sh --check` must pass.
- New endpoint modules go into `Program.endpoints` (MetaTests checks).
- Mockups (`/Users/jjchiw/Documents/code4fun/Umbral/design/umbra/*.dc.html`) are
  the visual reference, not code to copy; their names, emails, IDs and secrets
  are samples.

---

## Phase 1: Setup

- [X] T001 Add the fonts and their licences under `src/Umbral/Assets/fonts/`: `bricolage-grotesque-latin-800-v1001.woff2`, `schibsted-grotesk-latin-400-600-v1100.woff2`, `spline-sans-mono-latin-400-500-v1004.woff2` (prepared as research R4 describes), and `OFL-bricolage-grotesque.txt`, `OFL-schibsted-grotesk.txt`, `OFL-spline-sans-mono.txt`. Add a short `README.md` in that folder: source, subset, weight range, how to regenerate, and the rule that a changed file gets a new name.
- [X] T002 Embed the three woff2 files in `src/Umbral/Umbral.fsproj` (`EmbeddedResource` with `LogicalName` = file name) and add them to `src/Umbral/Views/Assets.fs` as assets at `/assets/fonts/<file>` with content type `font/woff2`, included in `Assets.all` (so `Handlers/Assets.fs` serves them immutable with an ETag). Add `Assets.fonts` (the list) for tests.
- [X] T003 [P] `Dockerfile`: copy `src/Umbral/Assets/fonts/OFL-*.txt` into `/app/licenses/fonts/` in the runtime stage, next to the existing `LICENSE` copy.
- [X] T004 [P] Create `src/Umbral/Handlers/ThemeChoice.fs` (empty module with a header comment naming spec 007, research R5) after `Handlers/Branding.fs` in the fsproj, and `tests/Umbral.Tests/ThemeTests.fs` registered before `SmokeTests.fs`.

---

## Phase 2: Foundation — tokens, type, mark (blocks every story)

- [X] T005 Rewrite the token tables in `src/Umbral/Assets/tailwind.config.js` per plan.md "Token Mapping": a `dark` and a `light` table with every token (existing names plus `primary-fill`, `on-primary-fill`, `success-container`, `warning-container`, `warning-outline`); every colour as `--color-X-rgb` channels plus `--color-X: rgb(var(--color-X-rgb))`; every Tailwind colour `rgb(var(--color-X-rgb) / <alpha-value>)`. `addBase` writes the dark set (with `color-scheme: dark`) on `:root`, the light set (with `color-scheme: light`) on `:root[data-theme="light"]` and inside `@media (prefers-color-scheme: light)` on `:root[data-theme="system"]` (research R1). Add the mark properties (`--mark-sun`, `--mark-base`, `--mark-shadow`, `--mark-shadow-opacity`, `--mark-upright`) to both sets. Font families: `sans` Schibsted Grotesk, `display` Bricolage Grotesque, `mono` Spline Sans Mono, each with system fallbacks; `--font-family`, `--font-display`, `--font-mono` properties. Update the file's header comment (Umbra, two themes).
- [X] T006 `src/Umbral/Assets/umbral.input.css`: three `@font-face` rules (same-origin `url('/assets/fonts/…')`, `format('woff2')`, `font-display: swap`, Google's `latin` `unicode-range`, `font-weight: 800` / `400 600` / `400 500`); `.um-btn-primary` reads `--color-primary-fill-rgb` / `--color-on-primary-fill-rgb`; inputs, `.um-choice-radio` and the theme control border use `outline` (R3); pills and alerts use the new container tokens (`.um-pill-success` = `bg-success-container text-success`, `.um-alert-warning` = `bg-warning-container border-warning-outline`, `.um-alert-error` = `bg-error-container`…); headings: a plain rule after the utilities giving `.text-display`, `.text-headline-lg` and `.um-wordmark` `font-family: var(--font-display); font-weight: 800`; `.um-mark-sun|base|shadow|upright` rules outside any `@layer` reading the `--mark-*` properties (fill for sun and shadow, stroke for base and upright, `opacity: var(--mark-shadow-opacity)` on shadow). Restyle `.um-card` (rounded-lg, `outline-variant` edge), `.um-btn` (rounded-full or the mockups' radius, Schibsted 600) to match `B-Apps.dc.html` / `B-SignIn.dc.html`. Keep `[hidden]{display:none!important}` and the crop rules.
- [X] T007 `src/Umbral/Views/Theme.fs`: replace `markSvg` with the Gnomon (research R6): `Mark.full`, `Mark.small`, `Mark.tiny` as inline SVG strings, `viewBox="0 0 48 48"`, `aria-hidden="true" focusable="false"`, parts with the `um-mark-*` classes and no colours; `faviconHref` = `Mark.tiny` geometry with fixed dark colours (sun `#f5c26b`, shadow `#a7a2b6` at 0.7, upright `#ece8df`) on a `#0b0b14` rounded tile, base64 `data:image/svg+xml`. Add `type ThemeChoice = System | Light | Dark`, `ThemeChoice.ofRequest (ctx)` (cookie `umbral_theme`, exact `light`/`dark`), `attr`, `parseChoice` (data-model.md), and the cookie name as a `[<Literal>]`. Add icons `Computer`, `LightMode`, `DarkMode` (Material Symbols Outlined paths, already in research; same viewBox). Update the module header comment.
- [X] T008 Regenerate `umbral.css` (`build-css.sh`) and update the facts in `tests/Umbral.Tests/PageAssetsTests.fs` that pinned the old palette: the accent channel defaults are now `245 194 107` (dark primary) and `42 29 5`; the primary button reads `--color-primary-fill-rgb` and `--color-on-primary-fill-rgb`; the "no hard-coded accent" fact checks the gold values (`245 194 107`, `#f5c26b`, `138 90 0`, `#8a5a00`) appear only in their token definitions. Run `dotnet test -c Release --filter PageAssetsTests`.

**Checkpoint**: the build is green; pages render with the new tokens.

---

## Phase 3: User Story 1 — Umbral looks like itself, in the system's theme (P1) 🎯 MVP

**Independent test**: every page kind renders with the Gnomon, Umbra fonts and `data-theme="system"`; nothing loads from another host.

- [ ] T009 [P] [US1] Tests in `tests/Umbral.Tests/ThemeTests.fs`: with no cookie, `/`, `/admin/setup`, `/signin` and a 404 page render `<html … data-theme="system"` with `<meta name="color-scheme" content="dark light">` and both media `theme-color` metas; with `umbral_theme=light` → `data-theme="light"`, one `theme-color` `#f4f1ea`; `dark` → `dark`; `Light`, `blue`, empty → `system` (FR-015).
- [ ] T010 [P] [US1] Tests in `tests/Umbral.Tests/PageAssetsTests.fs`: each font asset is served at its path, `font/woff2`, immutable, 304 on revalidation; every `url(` in `umbral.css` resolves to an entry of `Assets.all`; no rendered page (setup, sign-in, operator screens, picker, message pages — reuse the existing page walkers) nor the stylesheet contains `fonts.googleapis.com`, `fonts.gstatic.com` or an `@import`; the favicon `link` is a `data:image/svg+xml` whose decoded SVG contains the tile `#0b0b14` and the Gnomon (`#f5c26b` sun) and not the old shield path.
- [X] T011 [US1] `src/Umbral/Views/Layout.fs`: `<html>` gets `data-theme` from `ThemeChoice.ofRequest`; `color-scheme` and `theme-color` metas per contracts/theme.md; favicon from the new `faviconHref`; footers use `Mark.small`; the mark-colour comment rewritten (no accent sets `--mark-*`).
- [X] T012 [P] [US1] `src/Umbral/Views/Components.fs`: `brand` shows `Mark.small` (28–30 px) and the wordmark in lower case with `um-wordmark` (CSS `lowercase`, display face); `t.SiteName` stays "Umbral" for titles. `src/Umbral/Handlers/Home.fs`: `Mark.full` at 64 px.
- [ ] T013 [US1] Walk every screen against the mockups and adjust classes only where the tokens alone do not give the Umbra look (page headings in the display face, mono for client IDs/URLs/secrets via `um-mono`, card spacing): `Handlers/Admin/Apps.fs`, `Connectors.fs`, `Users.fs`, `Credential.fs`, `Setup.fs`, `SignIn.fs`, `Handlers/Connect/Picker.fs`, `Layout.messagePageWith`. No structural or text changes. Regenerate the CSS.
- [ ] T014 [US1] Run `dotnet test -c Release`; fix any test that pinned old markup only where the change is the redesign's (record each such change in the commit message).

**Checkpoint**: US1 works alone (themes follow the system; a hand-set cookie already switches them).

---

## Phase 4: User Story 3 — side rail and Create app dialog (P2)

**Independent test**: admin screens show the rail with `aria-current`; Create app works as a dialog and as `/admin/apps/new`.

- [ ] T015 [P] [US3] Tests (new `tests/Umbral.Tests/RailTests.fs`, registered before `SmokeTests.fs`): on each of `/admin/apps`, `/admin/connectors`, `/admin/users`, `/admin/credential` the page has an `<aside>` with a `<nav aria-label=…>` holding links to the four screens and the sign-out form, exactly one `aria-current="page"` on the right link; non-operator pages (setup, operator sign-in, `/signin`, a 404) have no `<aside>`, no `/admin/` links and no sign-out; `GET /admin/apps/new` signed out redirects to sign-in like the other screens, signed in renders `data-screen="app-new"` with `form[action="/admin/apps"]` and a Cancel link to `/admin/apps`; the Apps screen has `a[href="/admin/apps/new"][data-dialog="create-app"]` and a `<dialog id="create-app">` containing the form; posting from either creates the app (existing create tests cover the POST).
- [ ] T016 [US3] `src/Umbral/Views/Layout.fs`: operator chrome becomes the rail (research R7): `aside` with brand, `nav` (Apps, Connectors, Users, Credential, Sign out as a POST form) with `aria-current="page"` from the request path, and a slot below the nav for the theme control (filled in T020); stacks above `main` below `md`; no top header for operator pages. Plain and SignIn chrome unchanged except as T011. Style per `B-Apps.dc.html` (active entry `bg-surface-container-high text-primary`).
- [ ] T017 [US3] `src/Umbral/Handlers/Admin/Apps.fs`: `GET /admin/apps/new` (behind `requireOperator`) renders `createForm` (or the "needs a connector" empty state) on an operator page with `data-screen="app-new"`, a heading from `Texts.fs` and a Cancel link; the Apps screen replaces the inline form with a "Create app" link (`data-dialog="create-app"`) and a `<dialog id="create-app" aria-labelledby="create-app-title">` holding the form and a `button type="button" data-dialog-close` Cancel. Any new strings go into `Texts.fs`.
- [ ] T018 [US3] `src/Umbral/Assets/umbral.js`: on click of `a[data-dialog]`, if the named `dialog` exists and has `showModal`, prevent the navigation and `showModal()`; `[data-dialog-close]` closes its dialog; no inline script. `umbral.input.css`: dialog and `::backdrop` styles; no animation under `prefers-reduced-motion: reduce`.
- [ ] T019 [US3] Run `dotnet test -c Release`; fix tests that assumed the create form sits directly in the Apps screen only where the redesign moved it.

---

## Phase 5: User Story 2 — the theme switch (P1)

**Independent test**: with JavaScript off, each option from the rail and from a footer sets or clears the cookie and returns to the same page in that theme.

- [ ] T020 [P] [US2] Tests in `tests/Umbral.Tests/ThemeTests.fs` for `POST /theme` (contracts/theme.md): without an antiforgery token → refused, no `Set-Cookie` (CsrfTests also covers it automatically); with a token: `light` and `dark` → 303 to `returnUrl`, `Set-Cookie umbral_theme=<v>` with `max-age=31536000`, `path=/`, `samesite=lax`, `httponly`; `system` → cookie deleted; `blue` → no `Set-Cookie`, 303; `returnUrl` table of the contract (`//evil.example`, `/\evil.example`, `https://evil.example/`, missing, CR/LF) → `Location: /`; a `/signin?returnUrl=…` value comes back unchanged; works without an operator session. Rendering: the control is on the operator rail and on the footer of `/`, `/admin/setup`, `/admin/signin`, `/signin` (branded and unbranded) and a 404; `aria-pressed="true"` is on the option matching the cookie (system when absent or invalid); the hidden `returnUrl` is the request's path and query.
- [ ] T021 [US2] `src/Umbral/Views/Texts.fs`: `ThemeLabel` "Theme", `ThemeSystem` "Follow system", `ThemeLight` "Light", `ThemeDark` "Dark", `ThemeNoteSystem` "Following your system.", `ThemeNoteLight` "Light, on this browser.", `ThemeNoteDark` "Dark, on this browser." (from `B-Apps.dc.html`, without the mockup's "(dark)", which the server cannot know).
- [ ] T022 [US2] `src/Umbral/Views/Components.fs`: `themeSwitch (ctx) (t) (choice)` per contracts/theme.md markup (form, antiforgery input, hidden `returnUrl`, `role="group"` labelled, three submit buttons with icons, `aria-label`, `aria-pressed`, the note with `data-theme-note`); 44 px targets, visible focus. `src/Umbral/Views/Layout.fs`: place it in the rail slot (T016) and in the Plain and SignIn footers (next to `t.Footer` / `t.SecuredByUmbral`).
- [ ] T023 [US2] `src/Umbral/Handlers/ThemeChoice.fs`: `POST /theme` per contracts/theme.md (`WebForms.readValidated`, `parseChoice`, cookie options, local-path rule as a small pure function `isLocalPath` exposed for tests, 303). Register in `Program.endpoints`. Do not reuse `Picker.sanitizeReturnUrl` (research R5).
- [ ] T024 [US2] Run `dotnet test -c Release` (ThemeTests, CsrfTests, PageAssetsTests, SignInBrandingTests in particular).

---

## Phase 6: User Story 4 — feature 006 in both themes (P2)

- [ ] T025 [P] [US4] Tests: `tests/Umbral.Tests/AccentTests.fs` — `lowContrastOnCard` is true below 3:1 against `#14141f` or `#fffdf8` (`#0a0a10` warns: 1.08:1 on the dark card; `#ffffff` now warns: 1.02:1 on the light card; `#5fb3e8` now warns: 2.27:1 on the light card; `#f26b1d`, spec 006's example, now warns too: 6.00:1 on dark but 2.995:1 on light; `#d9480f` (4.25:1 dark, 4.23:1 light) and `#c2410c` (3.53:1, 5.09:1) must not warn). Tests elsewhere that save `#f26b1d` and expect `app-accent-saved` now get `app-accent-low-contrast`: update them and say so in the commit; `tests/Umbral.Tests/BrandingEndpointTests.fs` — the accent sheet sets `--color-primary-fill-rgb` and `--color-on-primary-fill-rgb` (and the resolved forms) to the accent and its text colour, and its ETag changed format version; `tests/Umbral.Tests/SignInBrandingTests.fs` — the branded footer (`data-secured-by`) contains the Gnomon (`um-mark-upright`), no `[data-accent]` rule sets `--mark-`, and the theme control is present; still no `<header>`/`<nav>` on a branded page.
- [ ] T026 [US4] `src/Umbral/Handlers/Branding.fs`: `accentRule` also sets the four fill properties; bump `AccentFormatVersion`. `src/Umbral/Domains/Branding.fs`: `lowContrastOnCard` checks both cards (constants named for the tokens). `src/Umbral/Views/Texts.fs`: the `app-accent-low-contrast` notice says the colour is hard to see on Umbral's dark or light background. Update `specs/006-app-signin-branding/contracts/admin-branding.md` and `public-endpoints.md` where they quote the old text or the old rule (note "amended by 007").
- [ ] T027 [US4] Check the Apps screen's per-app preview (`data-branding-sample`) still renders under `data-accent` in both themes and the admin chrome takes no accent. Run `dotnet test -c Release`.

---

## Phase 7: User Story 5 — documentation (P3)

- [ ] T028 [US5] `README.md`: a section "How it looks" (or within the quick start) describing the Umbra look, the Gnomon, the two themes, the theme control (rail and sign-in footer), that it follows the system by default, and that the choice is one cookie in the browser (`umbral_theme`, a year), nothing on the server; mention the self-hosted fonts and their licences (`src/Umbral/Assets/fonts/`, `/app/licenses/fonts/` in the image). Update the "Brand an app's sign-in page" section: the accent applies in both themes, the warning covers both backgrounds, "Secured by Umbral" shows the Gnomon. Update the Contents list.
- [ ] T029 [P] [US5] Update the module header comments that describe the old look (`Theme.fs`, `Layout.fs`, `Components.fs`, `tailwind.config.js`, `umbral.input.css`, `build-css.sh` if it names the palette).

---

## Phase 8: Polish and verification

- [ ] T030 `build-css.sh --check` passes; `dotnet build -c Release`; `dotnet test -c Release` all green (record the count).
- [ ] T031 Idle memory: `IdleMemoryTests` still passes (SC-006); note the font bytes (84 KB) in the commit.
- [ ] T032 Manual browser pass per quickstart.md (dark, light, JavaScript off, 320 px, keyboard focus on the theme control and the dialog). Record the result here; leave unchecked if it could not be done.
- [ ] T033 Update `spec.md` status and record deviations from this plan in `plan.md`.

## Dependencies

Setup (T001–T004) → Foundation (T005–T008) → US1 (T009–T014) → US3 (T015–T019) → US2 (T020–T024) → US4 (T025–T027) → US5 (T028–T029) → Polish.
US4 and US5 can run after US1 in parallel with US3/US2 only if they do not touch `Layout.fs`/`Components.fs` at the same time; run them sequentially.
