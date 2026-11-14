# Contract: the Declared marking (FR-009)

Answered by the designer (`umbral-designer`) on 2026-11-14.

## What is owned, what stays editable

| Screen | Owned while declared (shown as values, routes refuse) | Stays editable |
| --- | --- | --- |
| Apps, a declared app | name, app type, redirect URIs, post-logout redirect URIs, connector choice, **Regenerate secret**, **Delete** | sign-in branding (logo, accent) |
| Connectors, the passkey connector when declared | enabled (the toggle), **Who can sign up**, **Shared**, **Delete** | invitations (create, revoke) |

Every other app and connector is drawn as today, and so is an app that is no
longer declared (no mark, no notice).

## The mark

- **Pane header:** a muted pill **Declared** (the existing `.um-pill-muted`),
  beside the type pill. Never a warning tone: nothing is wrong.
- **List entry:** no pill (the entry allows one status marker). The second
  line carries it as text: an app reads "Web application · Declared"; the
  passkey connector keeps its state badge and its second line reads
  "passkey · Declared".

## The note

An info note (`um-alert-info`, `role="note"`) under the pane heading, where item
notices go, so it is seen before any section. The variable name is in mono.
Its last sentence is a `.um-link` "How declared apps work" to the documentation
site's page (the repository constant the footer uses, no new host), opening in
the same tab like the footer's links.

- App: "This app is declared in Umbral's start-up settings (`UMBRAL__DECLARATION`, set where Umbral is started, such as an Aspire AppHost). Its name, type, addresses and sign-in methods are changed there, and take effect when Umbral starts again. Branding is changed here. How declared apps work"
- Passkey connector: "The passkey connector is declared in Umbral's start-up settings (`UMBRAL__DECLARATION`). Whether it is on, who can sign up and whether it is shared are changed there, and take effect when Umbral starts again. Invitations are made here. How declared apps work"

## Owned fields: values, not disabled inputs

A disabled field invites the question of how to enable it, and disabled text
misses the contrast floor.

- **Apps pane:** the sections and their titles stay, so the pane keeps the
  shape the operator knows.
  - **Connection:** Client ID and Issuer URL with their copy buttons as today;
    the two URI lists as plain mono lists, with no Remove and no add field.
  - **Sign-in methods:** the summary line or pills only; no cards, no Save.
  - No closing row (both its actions, Regenerate secret and Delete, are owned).
  - No secret reveal.
  - The branding section is unchanged.
- **Passkey pane:** Settings shows three lines of label and value in the pane's
  field style (`um-label` over a body line): "Shared with every app: Yes",
  "Who can sign up: Invitations only" (or "Anyone can sign up"), and the state
  stays in the heading's badge. No Save, no closing row. The Invitations
  section is unchanged.

## Refusals

Both Warning alerts in the pane, after a POST to an owned route (reachable
directly, so refused on the server):

- `app-declared`: "This app is declared in Umbral's start-up settings, so it was not changed. Change it there, then start Umbral again."
- `connector-declared`: "The passkey connector is declared in Umbral's start-up settings, so it was not changed. Change it there, then start Umbral again."

## Captures for the review

At 1280 and 360 px, dark and light, in `.alta/reviews/2026-11-14-declared-apps/`:

- the Apps list with one declared and one ordinary app;
- the declared app's pane, and an ordinary app's pane beside it (unchanged);
- the declared passkey pane with an invitation;
- each refusal notice;
- the pane of an app that is no longer declared.
