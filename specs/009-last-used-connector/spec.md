# Feature Specification: "Last used" on the sign-in page

**Feature Branch**: `alta/loyal-oder-ccph` (worktree branch; spec directory `specs/009-last-used-connector`)

**Created**: 2026-10-21

**Status**: Implemented (browser pass in Chrome only)

**Input**: User description: "Spec 009: 'Last used' on the sign-in page — remember which sign-in method this browser used" (task `2026-10-21-last-used-sign-in-method-badge-on-the-sign-in-page-l4su`). The maintainer asked for the badge on 2026-10-21 and decided against showing the email.

## Background

People who use more than one provider forget which one they signed in with last
time. With Umbral's automatic linking rule, picking the wrong one is not only a
nuisance: a provider whose email does not match, or is not verified, gives the
person a second Umbral user, and the app behind Umbral sees a stranger.

Many sign-in pages answer this with a "Last used" hint. Cloudflare's shows the
provider, the email and a "Last used" badge above an "or" divider. Umbral shows
the method and the badge, never the email: the sign-in page is seen before
anyone is authenticated, so whatever it shows, anyone at a shared or borrowed
computer sees too.

This feature adds a hint to a screen that exists. It adds no concept, no setting
the operator has to make, and no data on the server.

## Decisions Already Taken

These were decided by the maintainer before this spec and are not open:

- Only the connector's identifier (its slug, e.g. `google`, `discord`, `home`,
  `passkey`) is remembered. Never the email, display name, user id or any other
  personal data.
- It is remembered in one cookie on the browser, in the spirit of the theme
  cookie (`umbral_theme`), for about a year. It is a hint, not a credential: not
  signed, not encrypted, and a forged value only moves a button.
- It is written only after a successful sign-in through that connector, never at
  the start of one, after a failed or refused one, or for an operator's sign-in
  to the admin screens.
- The remembered connector's button comes first with a **Last used** badge, then
  an "or" divider, then the other buttons in their usual order, and only when the
  connector is one this page offers and is enabled. Otherwise the page is exactly
  as it is today.
- It works with an app's branding (spec 006: accent colour and logo) and with
  both Umbra themes (spec 007); the badge stays readable on the accent.
- The badge's words live in the product's text catalogue, and the badge is part
  of the button's accessible name.

## Clarifications

### Session 2026-10-21

The task proposed a default for each of these questions; they were recorded as
the answers. The maintainer has not confirmed them interactively.

- Q: Should signing out clear the hint? → A: No. The hint is about the browser,
  not the session; signing out of an app (or of Umbral) leaves it in place.
- Q: Should the person be able to dismiss the hint ("Not you? / use another")? →
  A: No control. The "or" divider and the other buttons right under it are the
  way to choose another method.
- Q: With a single connector offered, show the badge? → A: No. There is nothing
  to choose, so the page is exactly as today.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The page shows which method this browser used last (Priority: P1)

Ana signs in to her family's photo app through Umbral, which offers Google,
Discord and a passkey. Last week she used Discord. Today the sign-in page shows
**Sign in with Discord** first, with a small **Last used** badge on it, then an
"or" divider, then Google and the passkey button. She presses Discord and is in,
as the same user as last week.

**Why this priority**: It is the whole feature: the hint is what prevents the
second user.

**Independent Test**: Sign in through one connector, return to the sign-in page
of an app that offers two or more, and see that connector first with the badge
and the divider.

**Acceptance Scenarios**:

1. **Given** a browser that signed in through Discord, and an app that offers
   Google, Discord and a passkey, **When** the person opens that app's sign-in
   page, **Then** Discord's button is first and carries a "Last used" badge, an
   "or" divider follows it, and Google and the passkey follow in their usual
   order.
2. **Given** the same browser, **When** a screen reader reads Discord's button,
   **Then** it hears that it is the method used last (for example "Sign in with
   Discord, last used").
3. **Given** a browser that signed in with a passkey, **When** the sign-in page
   offers the passkey with other connectors, **Then** the passkey button is
   first with the badge.
4. **Given** a browser that has never signed in through Umbral, **When** the
   sign-in page opens, **Then** it is exactly as it was before this feature.

---

### User Story 2 - The hint is written only by a sign-in that worked, and holds nothing about the person (Priority: P1)

Ben signs in on a library computer. Afterwards, the next visitor opens the
sign-in page. They can see which method was used on that computer, and nothing
else: not Ben's email, name or account.

**Why this priority**: The page is public. Whatever the hint holds, anyone at
that computer can read, so what it holds is a security property, not a detail.

**Independent Test**: Run each kind of sign-in through the real endpoints and
look at what the browser was told to remember: only a slug, and only after a
success.

**Acceptance Scenarios**:

1. **Given** a successful sign-in through a provider connector, **When** the
   browser is sent back to the app, **Then** it is told to remember that
   connector's slug and nothing else (no `@`, no user id, no name).
2. **Given** a successful sign-in with a passkey (an existing account, an open
   sign-up, or an invitation), **When** the ceremony completes, **Then** the
   browser is told to remember `passkey`.
3. **Given** a person who presses a connector's button, **When** they are sent
   to the provider, **Then** nothing is remembered yet.
4. **Given** a sign-in that fails (the provider declines, the exchange fails,
   the user is disabled) or is refused (the email domain is not allowed),
   **When** the person is sent back to the sign-in page, **Then** nothing is
   remembered and an earlier hint is left as it was.
5. **Given** an operator signing in to the admin screens, **When** they sign
   in, **Then** nothing is remembered.
6. **Given** a signed-in person adding a passkey to their account, **When** the
   passkey is added, **Then** nothing is remembered (it is not a sign-in).

---

### User Story 3 - The hint never shows a method that is not on offer (Priority: P2)

The operator disables Discord, or an app is set to offer only its own
connector. A browser that last used Discord sees the page as it would be without
the hint: no badge, no divider, no Discord button.

**Why this priority**: A hint that points at a button that is not there, or puts
one there, would be worse than no hint.

**Independent Test**: Set the hint by hand to a disabled, unknown or not-offered
slug and open the page.

**Acceptance Scenarios**:

1. **Given** a hint naming a connector that is disabled, **When** the page
   opens, **Then** it is exactly as without a hint.
2. **Given** a hint naming a connector that does not exist, or any other value,
   **When** the page opens, **Then** it is exactly as without a hint, and the
   value never appears on the page.
3. **Given** a hint naming a connector the app does not offer, **When** that
   app's page opens, **Then** it is exactly as without a hint.
4. **Given** a page that offers a single connector, **When** the hint names it,
   **Then** the page is exactly as without a hint.

---

### User Story 4 - The hint fits an app's branding and both themes (Priority: P3)

An app with a coral accent colour and its logo shows the remembered button in
its accent, with a badge that stays readable on the coral, in the dark and the
light theme alike.

**Why this priority**: Branding and themes are already shipped; the hint must
not break them, but it adds no new choice.

**Independent Test**: Open a branded app's page with a hint, in dark and light.

**Acceptance Scenarios**:

1. **Given** an app with an accent colour and a hint, **When** its page opens,
   **Then** the remembered button is in the accent like the others, and the
   badge is drawn in the button's own text colour, so its contrast is the
   button's contrast.
2. **Given** either theme, **When** the page opens with a hint, **Then** the
   badge and the divider use that theme's colours.

### Edge Cases

- The hint names a connector whose slug was reused for a new connector after a
  delete: the new connector is shown first. Harmless: it is the same button the
  person would see anyway, only moved.
- The hint holds an over-long or odd value (spaces, markup, another cookie's
  syntax): it matches no connector, so it is ignored and never written into the
  page.
- A failure notice ("That sign-in did not work") and a hint at the same time:
  both are shown; the notice stays above the buttons.
- A person signs in with Google, then with a passkey: the hint now says passkey.
  Only the latest success counts.
- The page without an app (a bare `/signin`): the hint applies to the shared
  connectors it shows, by the same rules.
- Signing out: the hint stays (Clarifications).
- A browser that refuses cookies: no hint, the page as today.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: After a successful end-user sign-in through a connector, the
  system MUST tell the browser to remember that connector's slug, and nothing
  else, for about a year.
- **FR-002**: A successful sign-in, for FR-001, is one that establishes the
  person's session: a provider callback that signs the person in, a passkey
  sign-in, and a passkey sign-up or invitation that signs the new person in.
- **FR-003**: The system MUST NOT write or change the hint when a sign-in starts,
  fails, is refused, is for a disabled user, when an operator signs in to the
  admin screens, or when a signed-in person adds a passkey.
- **FR-004**: The hint MUST hold no email, name, user id, domain or other data
  about the person, and MUST NOT be recorded in the security log.
- **FR-005**: The hint MUST be sent only to Umbral (its own site, every path),
  MUST NOT be readable by script, MUST be sent when a person follows a link from
  an app to Umbral, and MUST be marked secure whenever Umbral is served over
  HTTPS.
- **FR-006**: When the hint names a connector that the sign-in page offers (it
  is enabled and offered to that app, or shared when no app is named) and the
  page offers two or more connectors, the page MUST show that connector's button
  first, with a "Last used" badge, then an "or" divider, then the other offered
  buttons in their usual order.
- **FR-007**: In every other case (no hint, an unknown, disabled or not-offered
  connector, a single connector offered, no connectors at all), the page MUST be
  exactly as it is without this feature.
- **FR-008**: The value of the hint MUST never be written into the page; only a
  connector that matched it is.
- **FR-009**: The badge MUST be part of the button's accessible name, and the
  button's visible words MUST still be part of that name.
- **FR-010**: The badge and the divider MUST follow the active theme, and on an
  app's accent the badge MUST be drawn in the button's text colour.
- **FR-011**: The badge's and the divider's words MUST come from the text
  catalogue.
- **FR-012**: Signing out MUST NOT remove the hint.
- **FR-013**: The README MUST describe the hint next to the theme cookie (what it
  holds, that it holds no email, when it is written), and the changelog MUST list
  it under Unreleased.

### Key Entities

- **Last-used hint**: one value in one browser, the slug of the connector that
  browser last signed in through. Not stored on the server; not tied to a user.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A person returning on the same browser sees the method they last
  used as the first button on 100% of sign-in pages that offer it among two or
  more methods.
- **SC-002**: The hint holds no personal data: in every automated sign-in test,
  the remembered value is a connector slug with no `@` and no user id.
- **SC-003**: For every page where the hint does not apply, the page is the same
  as without a hint (same buttons, same order, no badge, no divider).
- **SC-004**: No new screen, setting, concept or server-side record exists after
  this feature.

## Assumptions

- The theme cookie's lifetime (a year) and attributes are the model for this
  one.
- "Usual order" is the order the page shows today.
- No new security-log event: the sign-in it follows is already recorded.
- Constitution 3.0.0: no new concept (a hint on an existing screen), no state
  outside the browser or the data directory, nothing identifying in the hint
  (Principle IV).
