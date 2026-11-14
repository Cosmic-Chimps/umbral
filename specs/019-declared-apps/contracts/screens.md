# Contract: the Declared marking (FR-009)

Proposals for the designer (`umbral-designer`); the answers replace this
section's open points before the build.

## What is owned, what stays editable

| Screen | Owned while declared (read-only, routes refuse) | Stays editable |
| --- | --- | --- |
| Apps, a declared app | name, app type, redirect URIs, post-logout redirect URIs, connector choice, **Regenerate secret**, **Delete** | sign-in branding (logo, accent) |
| Connectors, the passkey connector when declared | enabled (the toggle), **Who can sign up**, **Shared**, **Delete** | invitations (create, revoke) |

Every other app and connector is drawn as today.

## Proposal

- **The mark.** A neutral badge **Declared** (not a warning tone: nothing is
  wrong) next to the name in the list row and in the pane header, with the
  same badge style the lists already use for states, if one exists.
- **The pane.** One sentence under the header, before the first section:
  "Umbral applies this app from its start-up settings (`UMBRAL__DECLARATION`).
  Change it where Umbral is started, such as the Aspire AppHost, and start
  Umbral again." For the passkey connector: "...applies this connector..."
- **Owned fields** are shown as values, not as disabled inputs: the URI lists
  as plain lists with no add or remove form, the type and the connector choice
  as text. The action row has no **Regenerate secret** and no **Delete**.
- **The client ID** is shown as today (copy button). There is no secret reveal
  for a declared app: its secret is in the AppHost's secret parameter.
- **The branding section** is unchanged.
- **Refusals.** A POST to an owned route (reachable directly) redirects back
  with a Warning alert:
  - `app-declared`: "This app is declared in Umbral's start-up settings. Change it there and start Umbral again."
  - `connector-declared`: "The passkey connector is declared in Umbral's start-up settings. Change it there and start Umbral again."
- **No longer declared.** No mark; the app is an ordinary app again. No notice
  (the change happened at start, with nobody on the screen); the security log
  has `app_updated` `undeclared`.

## Open points for the designer

1. The badge: tone and placement, and whether the list also needs it or the
   pane alone is enough.
2. The sentence: words, and whether to name `UMBRAL__DECLARATION` on screen
   (an operator who never set it would not know the word "declaration"
   otherwise; the docs link could carry it instead).
3. Owned fields as values versus disabled inputs.
4. The passkey pane: the enable toggle and the sign-up radios become text.
5. A link to the docs page (`docs/run/aspire.md#declared-apps`) from the
   sentence.
