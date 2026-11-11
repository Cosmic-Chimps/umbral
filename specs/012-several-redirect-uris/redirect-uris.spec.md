# Feature Specification: Several redirect URIs per app, editable

**Feature Branch**: `alta/primal-zanzibar-dfrt` (worktree branch; spec directory `specs/012-several-redirect-uris`)

**Created**: 2026-11-11

**Status**: Draft

**Input**: Task `2026-11-11-1-apps-several-redirect-uris-editable-r3du`: "Apps: several redirect URIs per app, editable without recreating the app". `docs/integrate.md` says today that an app has one redirect URI, chosen when it is created, and that changing it means deleting the app and registering it again, with a new client ID and secret.

## Background

The same app usually runs in more than one place: on a developer's machine
(`http://localhost:3000/callback`), on staging and in production. Each sends
its own `redirect_uri`, and Umbral matches it exactly. With one redirect URI
per app, an operator has to register one app per place, and has to delete and
re-register an app (new client ID, new secret, new configuration in the app) to
fix a typo or move a domain.

This feature lets an app have several redirect URIs and several post-logout
redirect URIs, and lets the operator add and remove them on the app's pane,
without touching the client ID or the secret. It changes nothing about what
anyone may do (Principle VII): it only says where Umbral may send a person back.

## Decisions Already Taken

Decided in the task before this spec; not open:

- **Matching stays exact.** No wildcards and no patterns. Every URI still goes
  through `Domains/RedirectUris.acceptable`, unchanged; the authorize and
  end-session endpoints compare the request's value with each registered one,
  character for character.
- **The client ID and the secret survive any change** to the redirect URIs.
- **Every change is logged** in the structured security log (Principle IV):
  which app, which URI was added or removed, and that the operator did it. The
  log is never shown to an app.
- **Removing the last redirect URI is refused**, with a message that says why:
  an app with none cannot sign anyone in.
- **The Apps pane** shows the list, adds a URI with today's validation message,
  removes one after a confirmation, and leaves every other part of the pane
  unchanged. Words live in `Views/Texts.fs`.

## Clarifications

### Session 2026-11-11

Answered from the task's open questions and the code, unattended.

- Q: How many redirect URIs may an app have? → A: **10**, and 10 post-logout
  redirect URIs. Ten covers a developer machine, a few staging hosts,
  production and a native scheme with room to spare, keeps the pane readable,
  and keeps the list a deliberate one (research R2). The 11th is refused with a
  message naming the limit.
- Q: Does a loopback redirect on any port (RFC 8252 §7.3) belong here? → A:
  **No.** It is a different matching rule (the port is ignored for
  `http://127.0.0.1` and `http://[::1]`), which the constitution's "matched
  exactly" and this task's first decision do not cover. It gets its own task
  (research R3). Until then a desktop app registers a fixed loopback port, as
  `docs/native-apps.md` says.
- Q: Do the post-logout redirect URIs become a list too? → A: **Yes**, for the
  same reason (one per place the app runs), with the same add and remove, and
  the same limit. Unlike redirect URIs, the last one may be removed: a
  post-logout URI is optional, and without one a person who signs out lands on
  Umbral's home page.
- Q: Does storage change? → A: **No.** OpenIddict already stores an app's
  redirect URIs and post-logout redirect URIs as lists, and an existing app's
  one URI is already a one-item list. There is no schema step and no upgrade
  note; a test pins that an app made by today's create form keeps signing in
  after a URI is added (research R1).
- Q: Is adding a URI that is already registered an error? → A: It is refused
  with its own message, so the operator is not told "added" when nothing
  changed. "Already registered" uses URI equality (scheme and host compared
  without regard to case), so the list never holds two entries the storage
  would treat as one (research R4).
- Q: Can the create form take several URIs? → A: No. It keeps one redirect URI
  and one optional post-logout URI; more are added on the app's pane. The
  create form's hint says so.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Add a redirect URI to an existing app (Priority: P1)

An operator has an app registered with its production callback. A developer
now runs it locally. The operator opens the app's pane, adds
`http://localhost:3000/callback`, and the developer signs in locally with the
same client ID and secret.

**Why this priority**: it is the whole point: one app, several places.

**Independent Test**: register an app with one URI, add a second, and sign in
through each, exchanging the code with the original secret.

**Acceptance Scenarios**:

1. **Given** an app with one redirect URI, **When** the operator adds a second
   acceptable URI, **Then** the pane lists both, a notice says it was added,
   and the security log has an `app_updated` event naming the app and the URI.
2. **Given** an app with two redirect URIs, **When** a sign-in sends either one,
   **Then** Umbral sends the code back to that URI, and the code exchanges for
   tokens with the client ID and secret the app had before the change.
3. **Given** the add form, **When** the operator enters a URI that
   `RedirectUris.acceptable` refuses (relative, wildcard, fragment, empty),
   **Then** nothing is saved and the notice is today's validation message.
4. **Given** an app with 10 redirect URIs, **When** the operator adds one more,
   **Then** it is refused with a message naming the limit, and the pane shows
   no add form while the list is full.
5. **Given** an app with a URI registered, **When** the operator adds the same
   URI again, **Then** it is refused as already registered.

### User Story 2 — Remove a redirect URI (Priority: P1)

The staging host is retired. The operator removes its redirect URI, after a
confirmation, and a sign-in that still sends it is refused.

**Independent Test**: register two URIs, remove one, and check that authorize
refuses it while the other still works.

**Acceptance Scenarios**:

1. **Given** an app with two redirect URIs, **When** the operator removes one
   and confirms, **Then** the pane lists the other, a notice says it was
   removed, and the security log has an `app_updated` event naming the URI.
2. **Given** a removed URI, **When** a sign-in sends it, **Then** Umbral answers
   with its error page and does not redirect to it.
3. **Given** an app with one redirect URI, **Then** the pane offers no Remove
   for it and says that an app needs at least one; **When** a removal is posted
   anyway, **Then** it is refused with that message and nothing changes.
4. **Given** a removal of a URI the app does not have (already removed in
   another tab), **Then** nothing changes and a notice says so.

### User Story 3 — Several post-logout redirect URIs (Priority: P2)

Each place the app runs sends people back to itself after signing out.

**Independent Test**: register two post-logout URIs, sign out through each, then
remove one and sign out through it.

**Acceptance Scenarios**:

1. **Given** an app with two post-logout URIs, **When** a sign-out sends either
   one, **Then** Umbral ends the session and redirects to it.
2. **Given** a removed post-logout URI, **When** a sign-out sends it, **Then**
   the session ends and the person lands on Umbral's home page, as with any
   unregistered address today.
3. **Given** an app with one post-logout URI, **When** the operator removes it,
   **Then** it is removed (none is allowed), and the pane says what happens
   without one.
4. Adding follows User Story 1 (validation, limit of 10, duplicates), and every
   change is logged with its own detail.

### Edge Cases

- A URI with `;`, `=` or a query string: the log detail escapes it so it
  cannot add fields of its own.
- The client is public (SPA or native): the same list and forms; there is no
  secret to keep.
- The app was deleted in another tab: any of the four posts lands on the list
  with "That app no longer exists".
- A post without the antiforgery token is refused, as every admin POST is.
- Two URIs that differ only in the case of the host: the second is refused as
  already registered (R4).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An app MUST be able to hold from 1 to 10 redirect URIs, and from 0
  to 10 post-logout redirect URIs.
- **FR-002**: The app's pane MUST list every redirect URI and every post-logout
  redirect URI, in the order they were added.
- **FR-003**: The operator MUST be able to add a URI to either list. The value
  MUST pass `RedirectUris.acceptable`; a refusal MUST show today's validation
  message and save nothing.
- **FR-004**: Adding a URI already in the list (URI equality) MUST be refused
  with its own message.
- **FR-005**: Adding to a list that holds 10 MUST be refused with a message
  naming the limit; while a list is full, its add form MUST NOT be shown.
- **FR-006**: The operator MUST be able to remove a URI from either list after a
  confirmation that names the URI and the app.
- **FR-007**: Removing an app's last redirect URI MUST be refused with a message
  saying an app needs at least one; the pane MUST NOT offer Remove on the last
  one. Removing the last post-logout URI MUST be allowed.
- **FR-008**: A change MUST NOT change the app's client ID, its secret (hash), its
  type, its name, its permissions, its sign-in methods or its branding.
- **FR-009**: The authorize endpoint MUST accept a `redirect_uri` equal,
  character for character, to one of the app's registered redirect URIs, and
  refuse any other, a removed one included, with Umbral's error page.
- **FR-010**: The end-session endpoint MUST redirect only to a
  `post_logout_redirect_uri` equal to one of the app's registered post-logout
  URIs; any other ends the session and lands on Umbral's home page.
- **FR-011**: Every successful add and remove MUST be recorded as an
  `app_updated` security event with actor `operator`, the app's client ID,
  the remote address, and a detail naming the change and the URI
  (`redirect_uri_added`, `redirect_uri_removed`,
  `post_logout_redirect_uri_added`, `post_logout_redirect_uri_removed`). A
  refused change changes nothing and records nothing.
- **FR-012**: Every new route MUST require the operator session and the
  antiforgery token, and MUST end in a redirect to the app's pane (or to the
  list when the app no longer exists).
- **FR-013**: Existing apps MUST keep their redirect URI and post-logout URI and
  keep signing in unchanged; no schema change.
- **FR-014**: `docs/integrate.md`, `docs/native-apps.md` and a `changelog.d/`
  file MUST describe the lists, the limit and editing.

### Key Entities

- **App** (OpenIddict application): gains nothing new; its existing redirect
  URI and post-logout redirect URI lists become editable.

## Success Criteria *(mandatory)*

- **SC-001**: An app registered once signs people in from two places (two
  redirect URIs) with one client ID and one secret.
- **SC-002**: Changing a redirect URI never requires deleting the app.
- **SC-003**: A removed URI is refused at authorize on the very next request.
- **SC-004**: Every add and remove appears in the security log with the app and
  the URI.

## Out of Scope

- Loopback redirects on any port (RFC 8252 §7.3): a follow-up task.
- Wildcards or patterns of any kind (Principle IV).
- Editing an existing URI in place: remove it and add the new one.
- Changing an app's name or type after creation.
