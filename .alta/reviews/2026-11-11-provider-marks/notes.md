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
