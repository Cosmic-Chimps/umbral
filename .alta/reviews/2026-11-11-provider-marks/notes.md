# Provider marks (task b6d7, spec 014): part A

Branch `alta/bold-dove-crws`, on main `8df9daa`. Contract: `design/contracts/provider-marks.md`.
Research: `specs/014-provider-marks/research.md`. Spec: `specs/014-provider-marks/spec.md`.

Part A ships the marks Umbral may bundle. **Part B (Apple's logo pasted by the operator) is not
built**: it is a new setting and waits for the maintainer's yes (spec, Part B). The Apple
button is unchanged (title alone, empty slot), so the captures asked for with Apple's logo,
the Apple pane's logo section and its refusals are not here yet.

## What changed

| Kind | Leading mark |
|---|---|
| GitHub | The Invertocat (Primer Octicons `mark-github-24`), `um-icon um-mark-github` |
| Passkey | `Theme.Key` |
| Google, Discord, Fediverse, OIDC | `Theme.Link`, as before |
| Apple | the empty `um-apple-logo` slot, as before |

- `src/Umbral/Views/Theme.fs`: `GitHubMarkPath` with its provenance.
- `src/Umbral/Views/Components.fs`: `githubMark ()`, `aria-hidden`, `currentColor`.
- `src/Umbral/Handlers/Connect/Picker.fs`: `leading` by `connector.Kind`.
- `src/Umbral/Assets/umbral.input.css`: `.um-btn-secondary .um-mark-github` `#fff` (dark) and
  `#000` (light, and system when the OS asks for light); on an accent fill it keeps
  `currentColor`, which Umbral makes black or white. `umbral.css` regenerated with
  `build-css.sh`.
- No new strings.
- Detector ignores: the `#000` and `#fff` ignores in `.impeccable/config.json` already cover
  `src/Umbral/Assets/umbral.input.css` (added for Apple's button), so nothing was added.
- `DESIGN.md`, Icons: two sentences on which mark a sign-in button takes.
- `docs/connectors/apple.md`: the Gotchas warning now says why the logo is missing (Apple's
  licence); both warnings stay.
- `changelog.d/014-provider-marks.md`.

## Tests

`tests/Umbral.Tests/ProviderMarkTests.fs`, 8 tests: the GitHub mark and not the link icon; the
kind decides (a GitHub connector named "Company GitHub" keeps the mark, a Google connector
named "GitHub" gets the link icon); passkey has the Key icon; Google and Discord keep the link
icon; every mark `aria-hidden`; on an accent the GitHub button is primary with the mark; GitHub
last used is first with the badge and the mark; the stylesheet has `.um-mark-github`.
`AppleButtonTests` unchanged (7 of 7).

`dotnet test --filter-not-class "Umbral.Tests.IdleMemoryTests"`: 2092 of 2092 (Debug).

## Captures

From a Release build on `http://localhost:18093` with Chrome (Playwright), by `capture.mjs`
here. The picker is opened as `/signin?returnUrl=/connect/authorize?...`, as the tests do,
because OpenIddict refuses `/connect/authorize` over plain http. One app offers Apple (no
logo), Discord, GitHub, Google and a passkey; the second has the accent `#b4532a` and no logo.

- `picker`, `picker-accent`, `picker-last-used` (GitHub last used): 1280 and 360, dark and light.
- `picker-focus-github-1280-dark`.
- No state overflows at 360.

Computed colours from the walk (label, then mark):

| | GitHub on secondary | GitHub on accent | others |
|---|---|---|---|
| dark | label `rgb(236,232,223)`, mark `rgb(255,255,255)` | `#fff` / `#fff` | mark = label |
| light | label `rgb(23,21,31)`, mark `rgb(0,0,0)` | `#fff` / `#fff` | mark = label |

Every button is 318×44 at 1280 in both themes, with and without the accent.

# Part B: Apple's logo, pasted by the operator

The maintainer said yes on 2026-11-11 (spec FR-010 to FR-015). Commits `12ce8b7` (parser,
schema 11, store) and `61a8ee6` (pane section, routes, picker slot, CSS, texts, docs).

## What changed

- `src/Umbral/Domains/AppleLogo.fs`: `parse` reads the paste with no DTD and walks it against
  an allowlist (`svg`, `g`, `path`, `rect`, `title`, `desc`, empty `defs`; no `on…`, no `href`,
  no `style` with `url(`; depth 16, 64 paths, 32 KiB); keeps the `viewBox` (or the root's
  `width` and `height` when there is none) and each path's `d`, `fill-rule` and numeric
  `transform`; `render` rebuilds the SVG from those values alone. `rect` (the file's
  background), titles, ids and colours are dropped.
- Schema version 11: `connectors.apple_logo`. `ConnectorStore.appleLogo` parses the stored text
  again on every read.
- `Components.appleLogoSlot`: the only place a logo reaches a page, from a parsed value.
- The Apple pane's **Sign-in button logo** section (contract answer 3), its own form,
  `POST /admin/connectors/apple/logo` and `/admin/connectors/apple/logo/remove`, each recording
  `connector_updated` with `apple-logo=saved|removed`. A blank paste with a logo saved keeps it
  (no notice, no event); with none saved it is the not-an-SVG refusal.
- CSS: `.um-apple-logo > svg` is 2.75rem tall (the button's 44px), width by proportion;
  `.um-btn-apple` has `gap: 0`.
- New strings: exactly the contract's (Texts.fs, `AppleLogo*`), notices `apple-logo-saved`,
  `apple-logo-removed`.
- `docs/connectors/apple.md`: step 4 "Add Apple's logo to the button" with the refusal table;
  both missing-logo warnings now point to it. `changelog.d/014-provider-marks.md`: Added,
  Changed, Upgrade (schema 11).

## Tests

`AppleLogoTests` (35: accepted layouts, round trip, every refusal, hostile fixtures, storage),
`AppleLogoAdminTests` (14), `AppleButtonTests` (+2: a stored logo in the slot; a hostile text
written straight into the column never reaches the page), `DatabaseTests` (10 to 11).
Full suite without IdleMemoryTests: 2144 of 2144 (Debug), after the last change.

## Captures

`capture-b.mjs`, same server setup as part A (Release, `http://localhost:18093`, Chrome).
**The logo pasted is a stand-in**, a made-up rounded shape in Apple's file layout (a 31×44 box
with the mark centred in its own padding, a background `rect`, a `title`), because Apple's
artwork may not be committed. Judge the proportions with that in mind: Apple's real file may
sit a little differently.

- `apple-pane-no-logo`, `apple-logo-refused` (a `<script>` paste: 422, the marker text is not
  in the page), `apple-pane-logo` (preview, Replace, Remove): 1280 and 360, both themes.
- `picker-apple-no-logo`: Apple without a logo, before the paste.
- `picker`, `picker-accent` (now with the app's logo), `picker-last-used` (Apple last used):
  1280 and 360, both themes. These replace part A's captures of the same names (part A's are
  in `b329dbe`).
- `picker-focus-1280-dark`: focus on the Apple button with its logo.
- No state overflows at 360. Every button is 318×44; the Apple logo is 31×44, the other marks
  18×18.
