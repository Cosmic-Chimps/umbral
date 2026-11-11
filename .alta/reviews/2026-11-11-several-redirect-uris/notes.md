# Spec 012, several redirect URIs per app: notes for review

- **Task**: `2026-11-11-1-apps-several-redirect-uris-editable-r3du`.
- **Branch**: `alta/primal-zanzibar-dfrt` (worktree `~/.alta/worktrees/umbral/primal-zanzibar-dfrt`).
- **Range**: `d6d2373..2ebb5e6` (main at `d6d2373`).
  - `5883de6`: the spec.
  - `cf47ee2`: the docs and the changelog fragment.
  - `2993ffa`: the rule, words, routes and pane.
  - `30b2f7d`: the tests.
  - `2ebb5e6`: the layout fix and the rebuilt `umbral.css`.
- **Spec**: `specs/012-several-redirect-uris/`. It has the spec, research, routes contract and tasks. The words and the pane are in `contracts/routes.md`.
- **Tests**: the full suite passed at `30b2f7d` (1831 tests per the implementer's run, confirmed by mine). After `2ebb5e6`, which changes only classes, `AppsMasterDetail`, `AppRedirectUris`, `Meta` and `PageAssets` passed.
- **CSS**: `src/Umbral/Assets/build-css.sh` was run and `umbral.css` committed. `--check` passes.

## What changed on the screen

Only the **Connection** section of an app's pane. Client ID and Issuer URL are unchanged. Under them are two blocks:

- **Redirect URIs**:
  - The list is in mono. Each row has a small red ghost **Remove** (Delete icon), with a native confirm.
  - The only URI has no Remove; the hint "An app needs at least one redirect URI…" stands under it.
  - The list hint follows, then the **Add a redirect URI** field with a small secondary **Add** (one row from `sm` up, stacked below).
  - A full list (10) shows "This list is full (10). Remove one to add another." instead of the form.
- **Post-logout redirect URIs**: the same, except that the last one can be removed. With none, the block says "None. After signing out, people land on Umbral's home page."

The confirm is a native `confirm()`, as on every destructive form here, so it is not in the captures. Its texts are:

- Redirect: `Remove https://wiki.example.com/oauth/callback from "Wiki"? Sign-ins that send this address will be refused.`
- Post-logout: `Remove https://wiki.example.com/signed-out from "Wiki"? People who sign out with this address will land on Umbral's home page instead.`
- Remove's accessible name: `Remove https://wiki.example.com/oauth/callback`. The visible label "Remove" is part of it.

The design contract line is amended in `design/contracts/apps-connectors-master-detail.md` §2 item 3. Before, it said "a mono list. No form".

## Captures

Taken 2026-11-11 from a Release build on `http://localhost:18080` with Chrome (Playwright), by `capture.mjs` in this folder. Every state is at 1280 and 360, dark and light. No state overflows horizontally at 360.

The apps were made through the real routes:

- **Grafana**: one URI.
- **Wiki**: three redirect URIs, one of them long with a query, and two post-logout URIs.
- **Desktop tool**: ten loopback URIs.

| State | What it shows |
| --- | --- |
| `create` | The create form, with the amended redirect URI hint ("You can add more on the app's page.") |
| `one-uri` | One redirect URI (no Remove, the last-one hint), and no post-logout URI (the None text) |
| `several` | Wiki: three redirect URIs (the long one wraps beside its Remove), two post-logout URIs |
| `added` | The success notice `app-redirect-uri-added` |
| `removed` | The success notice `app-redirect-uri-removed` |
| `refused-invalid` | The warning `app-redirect-uri-invalid` (today's text, unchanged) |
| `refused-duplicate` | The warning `app-redirect-uri-duplicate` |
| `refused-last` | The warning `app-redirect-uri-last`, on the one-URI app |
| `full` | Ten redirect URIs: the full-list note and no redirect add form; the post-logout add form still there |
| `refused-limit` | The warning `app-redirect-uri-limit` |
| `focus-remove` | 1280 dark only: keyboard focus on the first Remove |

The capture script also posted each refusal for real, and each route answered with the code drawn here: invalid, duplicate (host in capitals), last, limit.

## Words (Texts.fs)

New, as in the contract:

- Labels and hints:
  - `AppRedirectUrisLabel` "Redirect URIs"
  - `AppRedirectUrisHint` "Umbral sends people back only to these addresses, matched exactly. Up to 10."
  - `AppPostLogoutUrisLabel` "Post-logout redirect URIs"
  - `AppPostLogoutUrisHint` "Where Umbral may send people after they sign out, matched exactly. Up to 10."
  - `AppPostLogoutUrisNone` "None. After signing out, people land on Umbral's home page."
- Last-one hint and full-list note:
  - `AppRedirectUriLastHint` "An app needs at least one redirect URI. Add the new one before you remove this one."
  - `AppUriListFull` "This list is full (10). Remove one to add another."
- Add field and buttons:
  - `AppRedirectUriAddLabel` "Add a redirect URI"
  - `AppPostLogoutUriAddLabel` "Add a post-logout redirect URI"
  - `AppUriAddButton` "Add"
  - `AppUriRemoveButton` "Remove"
  - `AppUriRemoveLabel` "Remove <uri>"
- Confirms: `AppRedirectUriRemoveConfirm` and `AppPostLogoutUriRemoveConfirm` (above).
- Notices:
  - `app-redirect-uri-added` "Redirect URI added."
  - `app-redirect-uri-removed` "Redirect URI removed. Sign-ins that send it are refused from now on."
  - `app-post-logout-uri-added` "Post-logout redirect URI added."
  - `app-post-logout-uri-removed` "Post-logout redirect URI removed."
  - `app-redirect-uri-duplicate` "That address is already registered for this app."
  - `app-redirect-uri-limit` "An app can have at most 10 addresses in this list. Remove one, then add the new one."
  - `app-redirect-uri-last` "An app needs at least one redirect URI, or nobody can sign in to it. Add the new one first, then remove the old one."
  - `app-redirect-uri-not-registered` "That address isn't registered for this app any more."

Changed: `AppRedirectUriHint` (create form) gains " You can add more on the app's page."

Unchanged, as the task asks: `app-redirect-uri-invalid`, which is used for both lists.

## Open for the designer

- **Remove's colour**: Remove is a red ghost, like Remove logo, so there is still one red fill on the pane (Delete app).
- **The add field's width**: it fills the column at 1280. A max width like the accent field's (`max-w-xs`) would be too short for a real URI. Your call.
- **The labels**: the two sub-blocks use `um-label` (the small caps label) rather than a section title, since they sit inside Connection.
- **Impeccable detector**: not run on this branch.
