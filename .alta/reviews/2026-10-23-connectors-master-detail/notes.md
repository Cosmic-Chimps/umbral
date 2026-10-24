# Connectors as a list and a detail pane: notes for the design review

Task: `.alta/tasks/2026-10-23-connectors-screen-as-a-list-and-a-detail-pane-c0nn.md`.
Contract: `design/contracts/apps-connectors-master-detail.md` (on `main`), sections 1 (frame, as amended) and 3
(Connectors).

## Branch and commits

- Branch `alta/urbane-zambezi-t9pn` (worktree `~/.alta/worktrees/umbral/urbane-zambezi-t9pn`), from `main` at
  `1f3a89e` (the Apps task merged).
- `bbdb4a8`: implementation, tests, `umbral.css`, DESIGN.md, CHANGELOG, docs wording.
- The next commit: Revoke as a red ghost (found in the captures: two red fills in the Passkey pane) and this folder.

## The frame is reused unchanged

`Components.frame`, `frameList`, `frameEntries`, `frameEntry`, `framePane`, `paneHeading`, `paneSection`,
`paneActions`, `markerIcon`, `frameNav`, the `.um-frame*`, `.um-entry*`, `.um-pane-*` rules and the frame code in
`umbral.js` are untouched. Two additions on the Connectors side, outside the frame:

- one CSS rule, `.um-entry[data-entry-muted]:not([aria-current="page"]):not(:hover) .um-entry-title` → muted text,
  for the kinds not set up ("drawn muted at rest"); hover and selection draw them as any entry;
- the pinned hairline is a `div` with `my-3 border-t border-outline-variant/40` between two `frameEntries`; the
  slug under the pane's `h2` is a `p.um-hint.um-mono` placed after `paneHeading`, so `paneHeading` keeps its shape.

DESIGN.md "List and detail" names Connectors and says both additions in two sentences.

## What changed

- **List** (`Connectors.fs` `view`): configured connectors (Discord, GitHub, Google, OpenID Connect, then fake, by
  slug within a kind), then the kinds not set up (muted, "Not set up"), then Passkey, then the hairline and
  **Allowed email domains** ("Who may sign in, for every connector"). Marker: the state badge, or the Warning icon
  ("Needs attention") instead of it when the connector is offered nowhere or its store cannot be read. The heading
  row: the count of connectors set up and **Add connector**.
- **Panes**: provider (heading + slug, notices, Set-up, Settings, closing row Disable / Delete); not set up (the
  same without the closing row); Passkey (`PasskeyConnector.pane`: notices with the invitation link shown once,
  Set-up, Settings, Invitations, closing row); add (`/new`, today's form); domains (`/domains`, one untitled
  section). Every `data-*` hook the cards carried is on the pane `section` or inside it.
- **URLs**: `/admin/connectors`, `/admin/connectors/<slug>`, `/admin/connectors/new`, `/admin/connectors/domains`
  (`ConnectorPaths.fs`). Every POST about a connector redirects to its detail URL; delete, unknown connector and
  a secret left behind after a delete go to the list; refusals of the add form to `/new`; Passkey save, invite,
  revoke to `/admin/connectors/passkey`; `/admin/allowed-domains` to `/domains`. Refusals that redraw in place
  (a refused domain list, a refused OpenID Connect add) redraw the pane they came from, 422.
- **htmx**: entries, the pinned entry and Add connector swap `[data-frame]` with `hx-select` and push the URL. The
  Enable/Disable buttons target the frame the same way, without pushing (the URL is already the detail URL), so
  the badge changes in the pane and the entry together.

## Decisions to check

1. **Count** is of connectors set up (Passkey included), not of entries: "4 connectors"; with none,
   "No connector set up yet".
2. **Reserved slugs**: `new` and `domains` are addresses of the screen, so adding a connector with either slug is
   refused as "already taken". A connector stored under one of them before this change would be unreachable
   from its entry; none can exist in practice (nothing prompted for those names), so there is no migration.
3. **Refusal notices are warnings**, as on Apps after its review: `connector-unknown`, `connector-not-found`, the
   `…-required`, `slug-invalid`, `slug-taken`, `last-enabled`, `delete-last-enabled`, and the Passkey refusals.
4. **Revoke** an invitation is a red ghost (`btnSm Ghost` + `text-error`), as Remove logo is: inviting again undoes
   it, and Delete stays the pane's one red fill. Measured: exactly one `.um-btn-danger` in every pane captured.
   The strand and removal warnings' confirm buttons ("Disable anyway", "Remove the list anyway") are still red
   fills, inside the notice, as before and as on Apps; they appear only after a refused action.
5. **Passkey offered nowhere** now has the same note in its pane as a provider (the entry's marker points at it);
   the card had only the `data-offered-nowhere` hook.
6. **Invite someone** is a small secondary button (contract §1: a section with a single form), Save connector and
   Enable passkeys stay full-size primary (the item's main edit form).
7. **Open invitations** is a `.um-label` inside the Invitations section, not a second heading.
8. The list URL selects the first entry in the markup at every width; the narrow stylesheet hides the pane.
9. No notice on the list URL's pane: a notice on the list URL is page-level (only delete, unknown connector and a
   secret left behind land there).

## Texts.fs (old → new)

- New: `ConnectorsListLabel` = "Connectors list"; `ConnectorsCount` = "No connector set up yet" / "1 connector" /
  "N connectors"; `ConnectorsAttentionMarker` = "Needs attention"; `ConnectorsDomainsEntryLine` = "Who may sign
  in, for every connector"; `ConnectorSetupTitle` = "Set-up"; `ConnectorSettingsTitle` = "Settings";
  `PasskeyInvitationsTitle` = "Invitations"; notice `connector-not-found` = "That connector no longer exists.
  Choose one from the list, or add it again."
- `AllowedDomainsTitle`: "Who can sign in" → "Allowed email domains" (the entry's name and the pane's `h2`).
- `ConnectorAddLead`, last sentence: "…needs no form here: use its card below." → "…needs no form here: choose it
  in the list."
- notice `connector-added-oidc`: "Copy its callback URL from its card into…" → "Copy its callback URL below
  into…".
- `SecretUnreadable`: "Enter the secret again on this card to keep it…" → "Enter the secret again under Settings
  to keep it…".

## CSS

`src/Umbral/Assets/build-css.sh` was run and `umbral.css` committed in `bbdb4a8`; `build-css.sh --check` passes.

## Tests

Full suite at `bbdb4a8`: 1425 passed, 0 failed. After the Revoke change: the Passkey classes and
`ConnectorsMasterDetailTests`, 147 passed.

New, `tests/Umbral.Tests/ConnectorsMasterDetailTests.fs` (21 cases):

- a configured provider's own URL is a page with that connector's pane
- a kind not set up has its set-up form on its own URL, posting to the kind, with no closing row
- Passkey has a pane before and after it is set up
- the add URL is a page with the add form in the pane, and the add button is the current one
- the domains URL is a page with the instance list in the pane and its pinned entry selected
- an unknown slug is a 404 with a page notice, no pane and no selected entry
- exactly one entry is current on a detail URL, and it is that slug's
- the list URL selects the first entry and shows its pane alone
- the list holds the configured connectors, then the kinds not set up, then passkey, then the domains entry after
  a hairline
- creating a connector lands on its own URL, and a refused create on the add URL
- saving a connector lands on its own URL
- toggling a connector lands on its own URL, and deleting one on the list
- saving, toggling or deleting an unknown connector lands on the list
- the passkey save, invite and revoke land on the passkey URL
- saving the allowed domains lands on the domains URL
- a notice after a save is inside the pane, not at the top of the page
- the slugs of the screen's own addresses are refused and nothing is stored (`new`, `domains`)
- a connector offered nowhere has the attention marker and no state badge, an ordinary one the reverse
- the toggle swaps the frame with htmx, and without htmx it still lands on the detail URL and flips the state
- entries, the pinned entry and the add button swap the frame with htmx, and the page has one frame

Changed so they read each connector on its own URL (intent kept; "card" → "pane" in the names that said it):
`AllowedDomainsAdminTests` (4), `ExternalSecretConnectorTests` (3), `MultipleConnectorsTests` (4),
`OidcConnectorTests` (2), `OidcDomainTests` (1), `PasskeyAdminTests`, `PasskeyCeremonyTests`,
`PasskeyInvitationTests`, `SecurityEventCoverageTests` (the Passkey page instead of the list).

## CHANGELOG

Under `## Unreleased`, `### Changed`, after the Apps bullet:

> The **Connectors** screen is a list and a detail pane too: the connectors on the left (those set up, then the
> providers not set up yet, then **Passkey**, then **Allowed email domains**, the instance-wide list), those set up
> marked Enabled, Disabled, or with a warning sign when they need attention, and the selected one's set-up details,
> settings and actions on the right. Each connector has its own address, `/admin/connectors/<identifier>`, where every change
> to it lands; **Add connector** opens its form at `/admin/connectors/new`, and the allowed email domains are at
> `/admin/connectors/domains`. `new` and `domains` can no longer be used as a connector's identifier.

`changelog.sh released` against `main` reports every released section unchanged.

## Docs

README.md, `docs/connectors/{discord,github,google,oidc,passkey}.md` and `docs/secret-stores/{aws,azure,
bellabaxter,gcp}.md` say "page" and "entry in the list" where they said "card" about the Connectors screen. The
diff went to `umbral-documentation` for review; its two nits ("under **Set-up**" in the quick start, and the
CHANGELOG no longer saying every entry has a badge) are applied in the next commit. `check-links.py` reports one broken anchor,
`oidc.md:180 → README#configuration-as-code`, which is the same on `main`.

## Impeccable detector

`impeccable detect --json` (plugin 4.7.1) on `umbral.input.css`, `Connectors.fs`, `PasskeyConnector.fs`: one
advisory, `design-system-color` on `rgb(0 0 0 / 0.6)` at `umbral.input.css:189`, the logo crop's shade, already on
`main` and reported in the Apps notes. Nothing else.

## Captures

37 PNGs, `<state>-<width>-<theme>.png`, full page, Chrome through Playwright on a fresh local instance with:
Discord set up through its not-set-up page (shared, limited to example.com), GitHub (shared, no rule), GitHub
(team) (`github-team`, not shared, chosen by no app: needs attention), Google not set up, Passkey enabled with one
open invitation ("For Ana"), and the instance list `example.com, acme.com`.

- `list-first-selected`, `detail-provider` (Discord), `detail-not-set-up` (Google), `detail-passkey`,
  `detail-domains`, `add`, `attention` (GitHub (team)), `toggle` (GitHub after an htmx Disable): 1280 and 360, dark
  and light.
- `narrow-list`, `narrow-detail` (Discord): 360, dark and light. `collapsed`: 768 dark, Discord.

Measured in the browser for every capture: no horizontal overflow; one `.um-btn-danger` at most in the pane; at
360 and 768 the list hides on detail URLs and the pane on the list URL; the title names what the URL shows.

htmx, at 1280: a click on the Passkey entry pushes `/admin/connectors/passkey`, the title becomes "Passkey ·
Connectors | Umbral", focus lands on the pane's `h2`, one `[data-frame]`. The pinned entry likewise
(`/admin/connectors/domains`, it is the one `aria-current`). Disable on GitHub's pane: the URL stays
`/admin/connectors/github`, one frame, the entry reads "Disabled", the pane's notice is `connector-disabled`.
