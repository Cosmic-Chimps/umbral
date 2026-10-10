# Feature Specification: Federated Login for Every App, with Optional Per-App Connectors

**Feature Branch**: `003-per-app-connectors`

**Created**: 2026-10-10

**Status**: Draft

**Input**: User description: "Umbral as the federated login for all of an operator's apps: shared connectors by default, optional per-app connectors. Today one connector is shared by every app, so the provider's consent screen shows the name of the upstream OAuth application (e.g. "Frunchy") for every app. Decision: the upstream OAuth app represents Umbral, not one app. Scope: (1) guidance on naming the upstream app after the Umbral instance, and the sign-in page names the app being signed in to; (2) an App may be limited to a chosen set of connectors, all enabled connectors by default; (3) several connectors of the same provider kind, so one app can have its own upstream app and consent name, with the same person still mapping to the same Umbral user. No new top-level concept, not an authorization model, YAML round-trips, existing apps unchanged, five-minute path unaffected."

## Background

A connector is Umbral's identity at the provider. Every app that uses it shares
it, so the provider's consent screen shows the connector's upstream application
name whatever app the person is heading to. An operator who named that upstream
application after their first app ("Frunchy") sees "Frunchy wants to access your
account" when signing in to every other app. The model is right: Umbral is the
federated login for all of the operator's apps. The guidance and the sign-in
page are what let the operator down. A minority of apps genuinely need their own
name on the consent screen, and this feature lets them have it without giving up
the shared default.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Everyone can tell where they are signing in (Priority: P1)

An operator sets up a Discord connector. The Connectors screen and the setup
guide tell them to name the Discord application after their Umbral instance,
for example "Cosmic Chimps Login", because that name appears on Discord's
consent screen for every app. An end user who clicks "Sign in" in Frunchy lands
on Umbral's sign-in page, which says they are signing in to Frunchy, and then
sees Discord ask on behalf of "Cosmic Chimps Login".

**Why this priority**: It fixes the confusion for every deployment with no new
setting, and keeps the five-minute path exactly as it is.

**Independent Test**: Register two apps sharing one connector, start a sign-in
from each, and confirm the sign-in page names the right app each time and the
Connectors screen shows the naming advice.

**Acceptance Scenarios**:

1. **Given** an app named "Frunchy", **When** an end user starts a sign-in from
   it and reaches the sign-in page, **Then** the page names "Frunchy" as the app
   they are signing in to.
2. **Given** a second app named "Recipes" sharing the same connector, **When** a
   sign-in starts from it, **Then** the sign-in page names "Recipes", not
   "Frunchy".
3. **Given** the sign-in page is opened without coming from an app, **When** it
   renders, **Then** it shows the general sign-in title and names no app.
4. **Given** an operator on the Connectors screen, **When** they read a
   connector's setup instructions, **Then** they are told to name the
   provider's application after their Umbral instance rather than after one app,
   and why.

---

### User Story 2 - Limit an app to some connectors (Priority: P2)

The operator's family photo app should only offer Google. On the Apps screen
they switch that app from "all shared connectors" to a chosen set and tick
Google. People signing in to the photo app see only "Sign in with Google";
every other app still shows Discord, GitHub and Google.

**Why this priority**: It is the building block for User Story 3, and is useful
alone for an app whose audience all uses one provider.

**Independent Test**: With three enabled connectors, limit one app to one of
them, then confirm its sign-in page offers only that one while another app
still offers all three.

**Acceptance Scenarios**:

1. **Given** an app set to "all shared connectors", **When** a sign-in starts
   from it, **Then** every enabled shared connector is offered, as today.
2. **Given** an app limited to Google, **When** a sign-in starts from it,
   **Then** only Google is offered.
3. **Given** an app limited to Google and an end user already signed in to
   Umbral through Discord, **When** they start a sign-in to that app, **Then**
   Umbral offers the sign-in page with Google rather than reusing the Discord
   session.
4. **Given** an app limited to Google, **When** the operator disables the
   Google connector, **Then** Umbral first warns that the photo app would be
   left with no way to sign in, naming the app.

---

### User Story 3 - One app with its own name on the consent screen (Priority: P3)

Frunchy is public and its users should see "Frunchy" on Discord's consent
screen. The operator adds a second Discord connector, backed by a Discord
application named "Frunchy", marks it as not shared, and limits Frunchy to it.
Frunchy's users see "Frunchy wants to access your account"; every other app
keeps the shared "Cosmic Chimps Login" connector. A person who used both apps
with the same Discord account is one Umbral user.

**Why this priority**: Only some deployments need it, and it costs the operator
an extra provider application per app.

**Independent Test**: Create two Discord connectors with different upstream
applications, dedicate one to an app, sign in to both apps with one Discord
account, and confirm the right connector was used each time and one user exists.

**Acceptance Scenarios**:

1. **Given** a Discord connector already exists, **When** the operator adds
   another Discord connector, **Then** Umbral asks for its own identifier and
   credentials and shows its own callback address to register at Discord.
2. **Given** a Discord connector marked as not shared, **When** a sign-in starts
   from an app set to "all shared connectors", **Then** that connector is not
   offered.
3. **Given** Frunchy limited to the dedicated Discord connector, **When** an end
   user signs in to Frunchy, **Then** the sign-in goes through the dedicated
   connector.
4. **Given** a person who signed in to another app with the shared Discord
   connector, **When** they sign in to Frunchy with the same Discord account
   through the dedicated connector, **Then** they are the same Umbral user, with
   the same identifier the apps receive.

---

### Edge Cases

- **A limited app's only connector is disabled or deleted.** The operator is
  warned first, naming the affected apps. If they go ahead, that app's sign-in
  page says no sign-in method is available. Deleting a connector removes it from
  every app's chosen set, but an app stays limited: it never falls back to "all
  shared connectors" on its own.
- **A connector is not shared and no app chooses it.** It is offered nowhere. The
  Connectors screen says so, so the operator is not left wondering why nobody
  uses it.
- **An end user lands on an app's sign-in page holding only a provider the app
  does not offer.** They sign in with one the app offers. They join their
  existing Umbral user only under the existing verified-email linking rule;
  otherwise they get a new user. This is the existing rule, unchanged.
- **Someone opens a connector's start address by hand for an app that does not
  offer it.** The resulting Umbral session does not get them into that app: the
  app's own connector set is checked when the app asks Umbral who they are.
- **Two connectors of the same kind on one sign-in page.** Allowed. Each button
  shows its connector's display name, which the operator sets to tell them
  apart.
- **A configuration document whose app names an unknown connector in its chosen
  set.** The whole document is refused, naming the app entry and the unknown
  connector.
- **The identifier of an additional connector.** It appears in the callback
  address registered at the provider, so it is chosen once and never changes.
  The first connector of each kind keeps the kind's name as today.

## Requirements *(mandatory)*

### Functional Requirements

#### Clarity of the shared model

- **FR-001**: When a sign-in comes from a registered app, Umbral's sign-in page
  MUST name that app, using the app's name.
- **FR-002**: When the sign-in page is reached without an app, it MUST show the
  general title and MUST NOT name an app.
- **FR-003**: The Connectors screen MUST tell the operator, for each connector,
  that the provider shows the upstream application's name to people signing in
  to every app that uses the connector, and MUST advise naming it after the
  Umbral instance.
- **FR-004**: The README, the quickstart and each provider's setup guide MUST
  give the same advice, and MUST explain how to give one app its own name on the
  consent screen.

#### Per-app connector choice

- **FR-005**: Each App MUST have a connector choice: either "all shared
  connectors" or a chosen set of connectors. New apps MUST default to "all
  shared connectors".
- **FR-006**: For an app set to "all shared connectors", the sign-in page MUST
  offer every enabled connector that is shared.
- **FR-007**: For an app with a chosen set, the sign-in page MUST offer exactly
  the enabled connectors in that set.
- **FR-008**: When an app asks Umbral who someone is, Umbral MUST honour an
  existing Umbral session only if it was established through a connector that
  app offers. Otherwise Umbral MUST show that app's sign-in page.
- **FR-009**: The Apps screen MUST let the operator set each app's connector
  choice, and the app list MUST show which choice each app has.
- **FR-010**: Umbral MUST warn the operator, naming the affected apps, before
  disabling or deleting a connector, or changing an app's chosen set, in a way
  that leaves an app with no enabled connector.
- **FR-011**: Deleting a connector MUST remove it from every app's chosen set.
  An app with a chosen set MUST keep that choice even when the set becomes
  empty.

#### Several connectors of one kind

- **FR-012**: The operator MUST be able to create several connectors of the
  same provider kind, each with its own credentials, display name and
  identifier, and the Connectors screen MUST show each one's own callback
  address.
- **FR-013**: A connector's identifier MUST be fixed at creation. The first
  connector of a kind MUST default to the kind's name, so existing callback
  addresses keep working.
- **FR-014**: Each connector MUST be shared or not shared. Connectors MUST
  default to shared. A connector that is not shared MUST be offered only to apps
  whose chosen set contains it.
- **FR-015**: A person who signs in with the same provider account through
  different connectors of the same kind MUST be the same Umbral user, and apps
  MUST receive the same user identifier for them.

#### Configuration and upgrade

- **FR-016**: The configuration document MUST express each app's connector
  choice and each connector's shared setting. Leaving either out MUST mean the
  default: "all shared connectors" for an app, shared for a connector.
- **FR-017**: Exporting the configuration and applying the export to a fresh
  instance MUST reproduce every app's connector choice and every connector's
  shared setting.
- **FR-018**: Umbral MUST refuse a configuration document in which an app's
  chosen set names a connector the document does not define, naming the app
  entry and the connector.
- **FR-019**: After upgrading, every existing app MUST be set to "all shared
  connectors" and every existing connector MUST be shared, so that sign-in
  behaves exactly as before.

#### Boundaries

- **FR-020**: A connector choice MUST only decide how people sign in to an app.
  Any enabled user MUST still be able to sign in to any app through a connector
  that app offers (the 001 rule FR-025 stands).
- **FR-021**: This feature MUST NOT add an administrative screen or a top-level
  concept beyond App, Connector and User.

### Key Entities

- **App** (changed): gains a connector choice, either "all shared connectors"
  or a chosen set of connectors.
- **Connector** (changed): several may now share a provider kind. Each gains a
  shared setting. Its identifier, already separate from its kind, becomes
  something the operator picks for any connector after the first of a kind.
- **Linked account** (unchanged): still keyed on the provider kind and the
  provider's own account identifier, not on the connector. This is what makes
  FR-015 hold.
- **Session** (changed meaning): remembers which connector established it, so
  FR-008 can be checked.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In every sign-in started from a registered app, the sign-in page
  names that app.
- **SC-002**: An operator following the setup guide gives one existing app its
  own consent-screen name in under 10 minutes, including creating the
  provider's application, without changing any other app.
- **SC-003**: After upgrading an existing instance, the set of connectors
  offered for every existing app is identical to before the upgrade.
- **SC-004**: One provider account used through two connectors of the same kind
  yields exactly one Umbral user and one user identifier, in 100% of test runs.
- **SC-005**: Exporting and re-applying a configuration that uses chosen sets
  and not-shared connectors leaves the instance unchanged.
- **SC-006**: The time from starting the container to a first successful sign-in
  stays within the five-minute target set by the MVP.

## Assumptions

### Decisions taken without an explicit answer

Each of these has an alternative a reasonable operator could prefer. They are
marked for confirmation at clarify, and each is cheap to reverse now.

| Requirement | Assumed answer | Why this one | Alternative |
| --- | --- | --- | --- |
| FR-014 | Connectors carry a shared setting; "all shared connectors" excludes the not-shared ones | Adding a dedicated connector for one app must not add a second "Sign in with Discord" button to every other app, or force the operator to limit every other app by hand | "All" means every enabled connector; the operator limits the other apps or accepts duplicate buttons |
| FR-008 | A session is honoured for an app only if it came through a connector that app offers | Otherwise a person already signed in elsewhere never passes through the app's own connector, and the dedicated consent name of User Story 3 never appears | Single sign-on across all apps regardless of connector; the chosen set only filters buttons |
| FR-011 | An app limited to a set stays limited when the set empties | Silently widening a limited app to every shared connector is the surprising failure | Fall back to "all shared connectors" |

### Context assumptions

- The provider account identifiers of Discord, GitHub and Google are the same
  whichever of the operator's upstream applications a person signs in through.
  FR-015 relies on it. Providers that give each application its own identifier
  for a person, possible under the generic OpenID Connect connector (feature
  002), are outside this feature and have to be settled there.
- The configuration document is expected to be removed in a later feature.
  Until then its round-trip promise stands, so FR-016 to FR-018 extend it with
  the least that keeps export and apply lossless, and nothing more.
- Per-app branding of the sign-in page beyond naming the app (logos, colours)
  stays out of scope.
- The sign-in page for the operator's own admin access is unaffected: it uses
  the operator credential, not connectors.
