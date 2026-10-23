# Apps as a list and a detail pane: notes for the design review

Task: `.alta/tasks/2026-10-23-apps-screen-as-a-list-and-a-detail-pane-m4st.md`.
Contract: `design/contracts/apps-connectors-master-detail.md`, sections 1 (frame) and 2 (Apps), from branch
`design/apps-connectors-master-detail` (PR #5, not merged yet).

## Branch and commits

- Branch `alta/savvy-severn-f6f6` (worktree `~/.alta/worktrees/umbral/savvy-severn-f6f6`).
- `351d134`: merges `origin/main` (0.4.0) and `alta/carefree-sunflower-jc4t` (task h3cd, cleared but not merged
  yet). h3cd's `.um-section-title` and closing row are built on, not duplicated.
- `484dfa3`: this task's implementation, its tests, `umbral.css`, DESIGN.md, CHANGELOG.
- The next commit: this folder.

## What changed

- **Frame** (`Views/Components.fs`): `frame`, `frameList`, `frameEntries`, `frameEntry`, `framePane`, `paneHeading`,
  `paneSection`, `paneActions`, `markerIcon`, `frameNav`. The list and pane hide below `lg` according to the URL
  (`data-frame-view="list|detail"`), and the back link is `lg:hidden`.
- **CSS** (`umbral.input.css`): `.um-frame` (`lg:grid-cols-[20rem_minmax(0,1fr)]`, gap 32px), `.um-frame-list-head`,
  `.um-entry` / `-title` / `-sub` (drawn like the rail entries), `.um-pane-section` (40% hairline, 24px above,
  20px below), `.um-pane-actions`, and `.um-btn-primary[aria-current="page"]` for the pressed Create button.
  `.um-section-title` gains `mb-3` (12px below, contract §1). The `.um-dialog` styles are removed: nothing else used
  them.
- **JS** (`umbral.js`): the dialog opener is removed. After an `htmx:after:swap` that a `[data-frame-nav]` link
  started, focus goes to the pane's `h2` (`tabindex="-1"`), or to the first entry where the pane is hidden. The
  accent pickers start again after every swap.
- **Apps** (`Handlers/Admin/Apps.fs`): one handler serves `/admin/apps`, `/admin/apps/new` and `/admin/apps/{id}`.
  The pane follows the order of contract §2 (Connection, Sign-in methods, the branding section kept whole, the
  closing row). The secret reveal is now a plain block at the top of the pane, not a card inside it.
- **Redirects:** every POST about an app goes to `/admin/apps/<id>?msg=…`. A delete or an unknown app goes to
  `/admin/apps?msg=…`, and a failed create goes to `/admin/apps/new?msg=…`. Changed in `Apps.fs`, `AppBranding.fs`
  and `AppSecret.fs`.

## Decisions to check (where the contract left room, or where I read it narrowly)

1. **No enabled connector but apps exist:** the "add a connector first" empty state shows above the frame, and the
   frame stays without the Create button. The contract says the empty state replaces the whole frame. Read
   literally, that would hide existing apps and their Delete and Regenerate buttons, which the anti-goals forbid ("no
   change to what any form does"). With no apps, the empty state does replace the frame, as written. The
   `need-connector` captures show the no-apps case.
2. **Accent sheet:** the selected app's sheet is a `<link rel="stylesheet">` inside the pane, not in `<head>`. An htmx
   swap keeps only `[data-frame]`, so a sheet in the head would go stale. One sheet at most per page; checked in a
   test and in the browser after a swap.
3. **Entry text at rest:** the first line is bone, as the entry definition says ("body medium at 500, bone"), and
   the second line is muted. §1's "Rest: text muted" is read as the entry's muted second line, like the rail.
4. **Pressed Create button:** drawn as selected (the high step with gold text), the same language as a selected
   entry, rather than as a primary fill.
5. **Sign-in methods:** the section title is the existing "Sign-in methods". The fieldset legend inside it would
   repeat that, so in the pane the legend is `sr-only`; in the create form it stays visible.
6. **Notice kinds are unchanged:** the notices in the create pane, such as `app-name-required`, are still drawn as
   success alerts, as they were at the page top before. This change does not alter alert kinds.
7. **The list URL marks the first app `aria-current` even on a narrow screen**, where only the list shows, as §5
   says.
8. `data-screen="app-new"` is kept on `/admin/apps/new`: tests use it.

## Texts.fs (old → new)

- New: `AppsListLabel` = "Apps list"; `AppsCount` = "1 app" / "N apps"; `AppsUnrestrictedMarker` = "Check sign-in
  methods"; `AppConnectionTitle` = "Connection".
- `app-not-found` notice: "That app no longer exists. Choose one from the list below, or register it again." →
  "That app no longer exists. Choose one from the list, or register it again." (It now shows at the page top, above
  the list.)
- New icon `ChevronLeft` (Theme.fs) for the back link.

## CSS

`src/Umbral/Assets/build-css.sh` was run and `umbral.css` committed in `484dfa3`; `build-css.sh --check` passes.

## Tests

The full suite passes: 1404 succeeded, 0 failed.

New, in `tests/Umbral.Tests/AppsMasterDetailTests.fs`:

- an app's own URL is a page with that app's pane
- the create URL is a page with the form in the pane, and the Create link is the current one
- with no connector the create URL shows only the add a connector first prompt
- an unknown app is a 404 naming app-not-found, with no pane and no entry selected
- creating an app lands on its page and a failed create lands on the create page
- every change to an app lands on that app's page and never carries a fragment or &app=
- deleting an app and any change to an unknown app land on the list
- an item notice is drawn inside the pane, not at the top of the page
- on an app's URL exactly its own entry is current
- on the list URL the first app by name is selected and its pane is the only one
- the list column holds no forms, no inputs and no notices
- a detail page shows one app: one data-app and at most the selected app's accent link
- the secret is shown once on the new app's page and another app's page does not use it up
- entries and the Create link swap the frame with htmx and the page has one frame
- a long redirect URI is in a list item that may break anywhere
- only an app that lets in an unrestricted connector has the warning marker in its entry

Changed, so they read each app on its own page and expect the detail-URL redirects (each test's intent is kept):

- `AppBrandingAdminTests`: `appRedirect`, previously `cardRedirect`. Renamed tests: "an app's pane links its own
  accent once…", "an app pane has the branding section…", "…the pane's actions sit behind a hairline…", "a measured
  value for one app is not shown on another's pane".
- `AppConnectorChoiceTests`: "an app's pane saves both choices and shows them"; the strand redirect is the detail
  URL.
- `RailTests`: "the Apps screen opens Create app as a pane, and the link is also a page", replacing the dialog test.

The `data-*` hooks used by the branding scripts and the tests are unchanged: `data-app`, `data-app-choice`,
`data-branding`, `data-app-actions`, `data-notice`, `data-warning`, `data-secret-once`, `data-logo-*`, `data-accent*`.
The new hooks are `data-frame`, `data-frame-view`, `data-frame-list`, `data-frame-nav`, `data-app-entry` and
`data-pane-heading`.

## CHANGELOG

Under `## Unreleased`, `### Changed`:

> The **Apps** screen is a list and a detail pane: the apps are listed by name on the left, and the selected one's
> connection details, sign-in methods, branding and actions are on the right (one column below 1024px, with a link
> back to the list). Each app has its own address, `/admin/apps/<client id>`, where every change to it lands, its
> notice and a new secret included; **Create app** opens its form in the pane at `/admin/apps/new` instead of a
> dialog. Long redirect URIs now wrap instead of running past the screen on a phone.

`changelog.sh released` against `origin/main` reports every released section unchanged.

## Impeccable detector

`impeccable detect --json` was run on `umbral.input.css`, `Apps.fs` and `Components.fs`. It gave one advisory,
`design-system-color` on `rgb(0 0 0 / 0.6)` at `umbral.input.css:187`. That is the logo crop's shade from
2026-10-15 (`a03567a0`), not part of this change. There were no other findings.

## Captures

34 PNGs, `<state>-<width>-<theme>.png`, full page, in Chrome through Playwright on a fresh local instance. The
instance has GitHub (shared, no domain rule) and Google (not shared, limited to example.com) as connectors, and four
apps: Album Share with a logo and the accent `#ff6f61`, Budget Tracker, Garden Planner (an SPA with a 190-character
redirect URI), and Recipe Box, which offers GitHub and Google, the unrestricted mix.

- `list-first-selected`, `detail-notice` (after Save sign-in methods), `detail-secret` (after Regenerate),
  `create`, `empty`, `need-connector`, `unrestricted` (Recipe Box): 1280 and 360, dark and light.
- `focus-entry`: 1280 dark, with keyboard focus on Budget Tracker.
- `narrow-list`, `narrow-detail` (Garden Planner, the long URI wrapping): 360 only, dark and light.
- `collapsed`: 768 dark, Album Share.

Measured in the browser for every capture: no horizontal overflow at any width, and exactly one red fill (Delete
app) in each app pane. At 360 and 768 the list hides on detail URLs and the pane hides on the list URL.

After a click on an entry with htmx, the URL is pushed and the page has one `[data-frame]`. Focus lands on the
pane's `h2`, and the accent sheet and the sample's colour follow the selected app (`rgb(255, 111, 97)` for Album
Share). The accent picker is shown again after the swap.

## Re-check: fixes for review.md

All four items are fixed in the commit after `66dc102`, on the same branch.

1. **Blocking, the title.** The page title is "<app name> · Apps | Umbral" on an app's URL, "Create app | Umbral" on
   `/admin/apps/new`, and "Apps | Umbral" on the list URL and for an unknown app (`pageTitle` in `Apps.fs` `view`).
   In Chrome, after an htmx click on an entry, `document.title` became "Recipe Box · Apps | Umbral", then "Album
   Share · Apps | Umbral", with no extra script. "an app's own URL is a page with that app's pane" now asserts all
   three titles.
2. **Nit, the reveal.** It keeps the success notice, the once-only warning and the Client secret field, and drops its
   Client ID (`revealed-client-id` is gone). The three test helpers that registered an app (`Flows.registerApp`,
   `Journeys.createClientApp`, `OidcProtocolTests`) now read the client id from the pane's Connection field
   `client-id-<id>`, as an operator would.
3. **Nit, DESIGN.md.** The `dialog` token is gone from the frontmatter, and the three sentences no longer mention the
   dialog: the tonal steps, the shadow paragraph, and the 12px radius list, which now names the list's entries
   instead.
4. **Nit, the refusal colour.** `noticeKind` in `Apps.fs` draws `app-name-required`, `app-redirect-uri-invalid`,
   `app-type-invalid`, `app-connector-unknown`, `app-not-found` and `app-no-secret` as Warning, and the rest as
   Success. It applies at all three places a non-branding notice is drawn. The test "creating an app lands on its
   page…" asserts the warning. This also closes follow-up task `…-4853`.

New and replaced captures: `detail-secret-*` (replaced) and `create-refused-*` (new: the create pane after posting
with no name), at 1280 and 360 in dark and light, so the folder now has 38 PNGs. There is no overflow, and the create
pane has no red fill. Tests: see the commit message for the full-suite result.
