# Research: The Umbra Look (spec 007)

Each entry: the decision, why, and what else was considered.

## R1 · Two themes from one stylesheet, chosen on `<html>`

**Decision**: Every colour token becomes a CSS custom property holding
space-separated channels (`--color-surface-rgb: 11 11 20`) plus a resolved
`--color-surface: rgb(var(--color-surface-rgb))`, and every Tailwind colour reads
`rgb(var(--color-X-rgb) / <alpha-value>)`. The dark set is written on `:root`
(it is also the fallback). The light set is written twice, in one generated
rule each:

```css
:root[data-theme="light"] { color-scheme: light; …light tokens… }
@media (prefers-color-scheme: light) {
  :root[data-theme="system"] { color-scheme: light; …light tokens… }
}
```

The server always writes `data-theme="system" | "light" | "dark"` on `<html>`
(R5). `dark` needs no rule of its own: it is the `:root` default.

**Why**: The theme is decided before the first paint, by the server and the
stylesheet only, so there is no flash and no script (FR-014, SC-003). Channel
variables keep every opacity utility (`bg-primary/10`) working in both themes,
which is how spec 006 made the accent overridable (its research R4); this
generalises the same trick to all tokens. Generating both blocks from one token
table in `tailwind.config.js` keeps one source of truth.

**Alternatives**: Tailwind's `dark:` variant (`darkMode: 'class'`) doubles every
colour class in every view and cannot express "follow the system unless
overridden" without script. A second stylesheet per theme doubles the CSS and
needs a `<link>` swap. A script that reads `localStorage` before paint is the
usual answer and is exactly what FR-014 rules out (and the CSP forbids inline
script).

## R2 · Token mapping: one extra pair for the light theme's buttons

**Decision**: Keep every existing token name and give it a value in each theme.
Add four names:

| New token | Purpose |
| --- | --- |
| `primary-fill`, `on-primary-fill` | The primary button's fill and its text. Dark: gold on brown (same as `primary`). Light: ink `#17151f` with gold `#f5c26b` text. |
| `success-container` | The success badge background (`#20301f` dark, `#e6eedc` light). |
| `warning-container`, `warning-outline` | The warning alert background and border (light `#fbefd6` / `#e2c27f`). |

`primary` stays "the accent as text and lines": links, `text-primary` icons,
`border-primary`, focus rings, the checked choice. In the light theme that is
the darker gold `#8a5a00`, because `#f5c26b` on paper is 1.6:1.

**Why**: The light palette has two golds and an ink button; one `primary` token
cannot be both the button fill and the link colour. Adding a fill pair is the
smallest change: only `.um-btn-primary` (and the selection highlight) read it.

**Alternatives**: Making `primary` ink in light and adding a `link` token
would touch every `text-primary` in the views and still need the accent sheet
to set both.

## R3 · Contrast (FR-007, SC-002)

Computed with the WCAG 2.x formula (the one in `Domains/Branding.fs`).

**Input borders**: the palette's line colours are decorative and far below
3:1 (`#2e2e40` on `#14141f` is 1.38:1; `#ddd6c8` on `#fffdf8` is 1.42:1). Inputs,
choice cards' radios and the theme control's outline therefore use `outline`,
set to `#74708a` (dark) and `#857e6e` (light), the palette's own hues made
lighter/darker until every surface they sit on gives at least 3:1. The palette
line colours stay on `outline-variant` for dividers and card edges, which are
not needed to identify a control.

The full table is in plan.md (Contrast table).

## R4 · Fonts: self-hosted, Latin, weight-limited woff2

**Decision**: Three files under `src/Umbral/Assets/fonts/`, embedded in the
assembly and served by `Handlers/Assets.fs` like the CSS:

| File | Source | Content | Size |
| --- | --- | --- | --- |
| `bricolage-grotesque-latin-800-v1001.woff2` | Google Fonts, `latin` subset of `wght@800` | static "96pt ExtraBold", v1.001 | 21.8 KB |
| `schibsted-grotesk-latin-400-600-v1100.woff2` | Google Fonts `latin` variable file (wght 400–900), instanced to wght 400–600 with fontTools `varLib.instancer` | variable, v1.100 | 41.4 KB |
| `spline-sans-mono-latin-400-500-v1004.woff2` | Google Fonts `latin` variable file (wght 300–700), instanced to wght 400–500 | variable, v1.004 | 20.7 KB |

Total 83.9 KB. `@font-face` rules in `umbral.input.css`, `font-display: swap`,
`unicode-range` of Google's `latin` subset, `font-weight: 400 600` (and `400
500`) on the variable faces. Licences `OFL-*.txt` beside them and copied into the
image as `/app/licenses/fonts/`.

**Why**: FR-009 forbids any other host (Principle II, and an identity
provider's pages load nothing from third parties). The weight-limited variable
files are smaller than three static instances and contain only the weights the
spec allows. The version in the file name is the cache key: the stylesheet is
compiled ahead of time and cannot carry a content hash in its `url()`s, so a
changed font file must get a new name (a test checks every `url()` in the CSS
resolves to a served asset).

**Alternatives**: Static instances per weight (three Schibsted files, ~75 KB);
system fonts (not the chosen look); Google's CDN (forbidden).

How the files were made (for whoever updates them), in a throwaway venv:
fetch `https://fonts.googleapis.com/css2?family=Bricolage+Grotesque:wght@800&family=Schibsted+Grotesk:wght@400;500;600&family=Spline+Sans+Mono:wght@400;500`
with a current browser User-Agent, download the `/* latin */` URLs, then
`instancer.instantiateVariableFont(font, {"wght": (400, 600)})` and save with
`flavor = "woff2"`. The licences are `ofl/<family>/OFL.txt` in
`github.com/google/fonts`.

## R5 · The theme cookie and the `/theme` endpoint

**Decision**: `POST /theme`, form fields `theme` (`system` | `light` | `dark`)
and `returnUrl`, antiforgery-checked with `WebForms.readValidated` like every
form. `light`/`dark` set `umbral_theme` (Path `/`, SameSite Lax, HttpOnly,
Secure when the request is HTTPS, Max-Age 365 days); `system` deletes it; any
other value changes nothing. The answer is `303 See Other` to `returnUrl` when
it is a local path, else `/`.

Reading: `Theme.ofRequest ctx` returns `System | Light | Dark` from the cookie,
exact lower-case match only; anything else is `System` (FR-015).

**Local path rule** (FR-016): starts with `/`; the second character is neither
`/` nor `\`; no control character or whitespace anywhere. Browsers treat `/\host`
as `//host`, so the existing `Picker.sanitizeReturnUrl` (which only rejects
`//`) is not reused (see Follow-ups).

**`returnUrl` value**: what the page writes is the current request's path and
query when the request is a GET; for a page rendered as the answer to a POST (a
form error re-rendered in place), the path alone would not be GET-able, so it
writes `/admin` under `/admin` and `/` elsewhere.

**Why**: A POST form with three submit buttons works without JavaScript,
keeps the CSRF rule uniform (the CsrfTests table covers every mutating route
outside `/connect/` and `/dev/`), and makes no GET request change anything.
HttpOnly because no script reads it; Lax because the choice should apply when a
person arrives from an app's "Sign in" link (a top-level navigation).

**Alternatives**: GET links (`/theme?set=light`) are simpler but change state on
GET, which prefetchers and crawlers trigger. `localStorage` plus script: no
server rendering, so a flash, and it fails FR-014 without JavaScript.

## R6 · The Gnomon as themeable inline SVG

**Decision**: `Theme.fs` holds three drawings: `Mark.full` (≥ 33 px: sun r 4.5,
base line, shadow `M22 39H44L22 33Z`, upright `M22 39V13` stroke 4), `Mark.small`
(≤ 32 px: sun r 5.5, no base, shadow `M22 40H45L22 32Z`, upright stroke 5.5) and
`Mark.tiny` (16 px: sun r 7, shadow `M22 42H46L22 31Z`, upright `M22 42V14`
stroke 8), from `B-Brand.dc.html`. Each part carries a class (`um-mark-sun`,
`um-mark-base`, `um-mark-shadow`, `um-mark-upright`) and no colour of its own; the
stylesheet colours them from `--mark-*` properties set per theme (dark: sun
`#f5c26b`, base `#3a3a52`, shadow `#a7a2b6` at 0.5, upright `#ece8df`; light: sun
`#b8801f`, base `#ddd6c8`, shadow `#5f5a6b` at 0.4, upright `#17151f`).

The favicon is `Mark.tiny` with fixed dark colours on a `#0b0b14` rounded tile, as
a `data:` URI (`img-src 'self' data:` already allows it).

**Why**: One inline drawing follows the theme without script and without a
second copy hidden per theme. No `[data-accent]` rule sets `--mark-*`, so an app's
accent cannot recolour the mark (FR-024). The rules sit outside `@layer`, like the
crop panel's, so Tailwind keeps them although no view names the classes as
utilities. SVG presentation attributes do not accept `var()`, hence classes.

**Alternatives**: Two inline SVGs toggled by CSS (duplicated markup on every
page); an `<img>` per theme (cannot follow `prefers-color-scheme` inside a
cached file without `<picture>`, and `<picture>` cannot follow the cookie).

## R7 · Rail layout

**Decision**: `Layout.shell` for `Operator` renders a two-column flex layout:
`<aside>` (brand, `<nav aria-label>` with the five entries, theme control at the
bottom) and `<main>`. Below the `md` breakpoint the aside is a full-width block
above `main`, the nav wraps horizontally, and the theme control sits at the end
of the block. The current entry gets `aria-current="page"` from the request path
(`/admin/apps…` → Apps, and so on). Sign out stays a POST form.

Non-operator pages keep today's `<header>` with the brand (unbranded) or no
header (branded sign-in, spec 006 FR-006), and their footer gains the theme
control.

**Why**: Principle I (three screens) is unchanged; only the frame moves. A
`<nav>` exists only on pages that had one (the SignInBranding tests assert a
branded page has no `<header>` and no `<nav>`), so the theme control is a
`<form>` inside a `role="group"`, never a `<nav>`.

## R8 · Create app: a page and a dialog

**Decision**: New `GET /admin/apps/new` renders the existing `createForm` on its
own operator page, with a Cancel link to `/admin/apps`. The Apps screen's
"Create app" is a link to it, carrying `data-dialog="create-app"`; the screen also
renders the same form inside `<dialog id="create-app" aria-labelledby>`.
`umbral.js` (already loaded on every page, no inline script) intercepts a click
on a `[data-dialog]` link, calls `showModal()` on the named dialog and lets a
`[data-dialog-close]` button close it; Escape and focus handling come from the
native `<dialog>`. The form posts to `POST /admin/apps` either way, and errors
keep redirecting to `/admin/apps?msg=…` as today.

**Why**: FR-021 needs both; the native dialog gives modality, Escape and focus
return without a focus-trap library. Rendering the form twice on the Apps screen
is avoided by rendering it only in the dialog there; tests that look for
`form[action="/admin/apps"]` on the Apps screen keep finding it.

**Alternatives**: An htmx `hx-get` of the page into a dialog (a network round
trip and more script for the same result); a CSS `:target` overlay (no
inertness, no Escape, poor focus handling).

## R9 · Feature 006 in two themes

**Decision**: The accent sheet (`/branding/accent/{hex}.css`) sets, under
`[data-accent="hex"]`, `--color-primary*`, `--color-on-primary*` and now also
`--color-primary-fill*`, `--color-on-primary-fill*`, all to the accent and its
black/white text. It is theme-independent: the same accent in both themes, with
the same legible text (spec 006 FR-016 holds per accent, not per background).
`AccentFormatVersion` is bumped so cached sheets revalidate.

`Accent.lowContrastOnCard` checks both cards, `#14141f` and `#fffdf8`, and warns
when either is below 3:1 (FR-023); the notice text says it may be faint on one of
the two backgrounds.

## Follow-ups noticed (not part of this feature)

- `Picker.sanitizeReturnUrl` accepts `/\host`, which browsers read as `//host`;
  `Callback.fs` redirects to it after a successful sign-in. Proposed as a task.
