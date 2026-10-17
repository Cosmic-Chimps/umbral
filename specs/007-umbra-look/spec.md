# Feature Specification: The Umbra Look, the Gnomon Mark, and a Theme Switch

**Feature Branch**: `alta/subtle-alioth-qrcn` (worktree branch; spec directory `specs/007-umbra-look`)

**Created**: 2026-10-17

**Status**: Draft

**Input**: User description: "Redesign: the Umbra look, the Gnomon mark, dark and light themes and a theme switch" (task `2026-10-17-redesign-umbra-gnomon-mark-dark-and-light-themes-and-a-theme-k3gn`). The maintainer reviewed three directions on 2026-10-17 and chose **B · Umbra** with the **Gnomon** mark. Reference artboards: `design/umbra/*.dc.html` in the main checkout (brand sheet, sign-in dark and light, Apps with a working theme switch, Create app as a modal, App detail with the secret shown once, Connectors in light, Users, first-run setup). The Stitch export in `design/stitch_umbral_mvp_social_login_provider.zip` is not the reference.

## Background

Umbral's look was never designed. It borrowed a dark palette that looks like
every code-hosting site, used whatever fonts the visitor's system had, and its
mark was a generic shield with a tick. Nothing about it said what Umbral is.

The chosen direction, **Umbra**, starts from the name. *Umbral* means threshold,
and an *umbra* is the shadow an object casts. The mark, a **gnomon**, draws
exactly that: a sun, an upright, and the shadow the upright casts on a
threshold line. The identity providers are the light, Umbral stands in it, and
every app receives one consistent shadow: the same sign-in, whoever is shining.

The look comes in a dark and a light theme. A person's system setting chooses
between them unless they pick one on the page, and the pick is remembered by
their browser alone.

This feature changes how Umbral looks. It adds no concept, no setting the
operator has to make, and no data on the server.

## Decisions Already Taken

These were decided by the maintainer before this spec and are not open:

- Direction **B · Umbra** and the **Gnomon** mark (FR-001 to FR-004).
- The two palettes (dark and light) whose values are listed in FR-005 and
  FR-006.
- Three typefaces, all under the SIL Open Font License: a display face
  (Bricolage Grotesque, extra bold) for headings and the lowercase `umbral`
  wordmark, a text face (Schibsted Grotesk) for everything else, and a mono face
  (Spline Sans Mono) for client IDs, URLs and secrets.
- Default theme follows the system; an override of System, Light or Dark is
  remembered in the browser.
- The operator's navigation becomes a side rail. Creating an app can happen in
  a dialog over the Apps list.
- Where a mockup and the product's text catalogue (`Views/Texts.fs`) disagree
  on wording, the text catalogue wins. Names, emails, client IDs, secrets and
  the "Home" connector in the mockups are sample data.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Umbral looks like itself, in the theme the person's system asks for (Priority: P1)

A homelabber opens their Umbral instance on a laptop set to dark mode. Every
page, the first-run setup, the operator sign-in, the Apps, Connectors, Users and
Credential screens, the end-user sign-in page and every message page, shows the
Umbra look: deep ink background, warm off-white text, corona-gold highlights,
the Gnomon mark beside a lowercase `umbral` wordmark, and the Umbra typefaces.
The browser tab shows the Gnomon on a dark tile. On their phone, which is set to
light mode, the same pages appear in the light theme: warm paper background,
near-black text, darker gold for links. Nothing on any page is fetched from
another site to draw it.

**Why this priority**: This is the redesign. Without it the theme switch and
the rail have nothing to switch or frame.

**Independent Test**: Load every page kind with the browser in dark mode and in
light mode, with no theme chosen; check each one renders in the matching theme
with the Gnomon mark and the Umbra fonts, and that no request leaves the
instance.

**Acceptance Scenarios**:

1. **Given** a browser that prefers dark and no theme chosen, **When** any page
   loads, **Then** it renders in the dark theme with the dark Gnomon mark.
2. **Given** a browser that prefers light and no theme chosen, **When** any page
   loads, **Then** it renders in the light theme with the light Gnomon mark.
3. **Given** any page, **When** it loads, **Then** its headings and the wordmark
   use the display typeface, body text the text typeface, and client IDs, URLs
   and secrets the mono typeface, all served by the instance itself.
4. **Given** any page, **When** its markup and stylesheet are inspected,
   **Then** they reference no font, style or script on any other host.
5. **Given** any page, **When** the browser tab is looked at, **Then** the icon
   is the Gnomon on a dark tile, in both themes.

---

### User Story 2 - A person picks a theme, and the choice sticks (Priority: P1)

The operator prefers the light theme on their dark-mode laptop. Below the
navigation in the side rail they see a small "Theme" control with three
options: follow the system, light, dark. They choose Light; the page comes back
in the light theme, and a short line under the control says the light theme is
in use on this browser. Every page they open afterwards, today and next month,
is light, without a flash of the dark theme first. Someone signing in to an app
finds the same control in the sign-in page's footer and can choose for
themselves. Choosing "follow the system" again forgets the choice. All of this
works with JavaScript turned off.

**Why this priority**: A light theme that a person cannot choose is only half
delivered: people whose system is dark but who read better on light (or the
other way round) need the override, and it must not cost a flash on every page.

**Independent Test**: With JavaScript off, choose each option from the rail and
from the sign-in footer, reload and open other pages, and check the theme holds
and the page arrives already in that theme. Then choose "follow the system" and
check the system theme returns.

**Acceptance Scenarios**:

1. **Given** no theme chosen, **When** a page renders, **Then** the page says
   it follows the system, and the system option of the control is marked as the
   current one.
2. **Given** a person chooses Light (or Dark), **When** the choice is sent,
   **Then** they come back to the page they were on, in that theme, and every
   later page from that browser renders in that theme from the first paint.
3. **Given** a chosen theme, **When** the person chooses "follow the system",
   **Then** the choice is forgotten and pages follow the system again.
4. **Given** a remembered value that is not one of the three options (edited by
   hand, or left by a future version), **When** a page renders, **Then** it is
   ignored and the page follows the system.
5. **Given** JavaScript is turned off, **When** a person uses the control,
   **Then** it works the same way.
6. **Given** the control on the end-user sign-in page, **When** the person
   chooses a theme, **Then** they return to that same sign-in page, for the same
   app, with their place in the sign-in kept.
7. **Given** a theme choice, **When** the server's data is inspected, **Then**
   nothing about it is stored there: it lives in that one browser.

---

### User Story 3 - The operator moves around a side rail, and creates apps in a dialog (Priority: P2)

On a wide screen the operator's screens have a side rail on the left: the mark
and wordmark at the top, then Apps, Connectors, Users, Credential and Sign out,
with the current screen highlighted, and the theme control below them. On a
phone the rail sits on top of the page instead, above the screen's content.
On the Apps screen, "Create app" opens a dialog over the list with the same
form as before; Cancel or Escape closes it and the list is still there.
Without JavaScript, "Create app" opens the same form as a page of its own.

**Why this priority**: It is the layout of the chosen direction and makes room
for the theme control, but Umbral works without it: the screens are reachable
either way.

**Independent Test**: Open each admin screen at desktop and phone widths and
check the rail's position and the highlighted entry. On Apps, create an app from
the dialog, then turn JavaScript off and create one from the page.

**Acceptance Scenarios**:

1. **Given** a signed-in operator on a wide screen, **When** any admin screen
   loads, **Then** a side rail lists Apps, Connectors, Users, Credential and
   Sign out, marks the current screen, and holds the theme control below them.
2. **Given** a phone-width screen, **When** an admin screen loads, **Then** the
   rail is stacked above the content and every entry is still reachable.
3. **Given** the Apps screen with JavaScript on, **When** the operator chooses
   Create app, **Then** the create form opens in a dialog over the list, keyboard
   focus moves into it, and Cancel or Escape closes it.
4. **Given** JavaScript off, **When** the operator follows Create app, **Then**
   a page with the same form opens, and submitting it creates the app exactly as
   the dialog does.
5. **Given** a page that has no operator signed in (setup, operator sign-in,
   end-user sign-in, message pages), **When** it renders, **Then** it shows no
   rail, no link into the admin screens and no sign-out.

---

### User Story 4 - An app's branding still wins on its sign-in page, in both themes (Priority: P2)

Frunchy has an orange accent and its logo (feature 006). A person signing in
to Frunchy sees Frunchy's logo and orange buttons, with legible text on them,
whether their browser shows the dark or the light theme. The small "Secured by
Umbral" mark at the bottom is now the Gnomon. An app without branding gets the
Umbra gold. The operator, when they set an accent, is warned if it would be hard
to see in either theme.

**Why this priority**: Feature 006 is shipped and must not regress; the light
theme is a new background it has to hold up on.

**Independent Test**: Brand one app; open its sign-in page in each theme and
check the logo, the accent on buttons and highlights, the text on the accent,
and the Gnomon in the footer. Open an unbranded app's sign-in page and check it
uses the Umbra gold.

**Acceptance Scenarios**:

1. **Given** an app with an accent and a logo, **When** its sign-in page renders
   in either theme, **Then** the accent replaces the Umbra gold on its buttons,
   links and highlights, the text on the accent is legible (feature 006,
   FR-016), and the logo is shown.
2. **Given** that page, **When** its footer renders, **Then** it shows the
   Gnomon and "Secured by Umbral", which no branding can recolour or remove,
   and the theme control.
3. **Given** an accent with a contrast below 3:1 against the card of either
   theme, **When** the operator saves it, **Then** it is saved and the operator
   is warned (feature 006, FR-017), naming that it would be hard to see.
4. **Given** any admin screen, **When** it renders, **Then** no app's accent
   recolours its chrome (feature 006, FR-005), except the per-app preview on
   the Apps screen.

---

### User Story 5 - The README shows the look and the theme switch (Priority: P3)

A stranger reading the README sees what Umbral looks like and learns that the
pages follow their system's theme, that a control in the rail and in the sign-in
footer overrides it, and that the choice lives only in their browser.

**Why this priority**: Principle VI: a feature is done when its documentation is.

**Independent Test**: Read the README and find the description (or screenshots)
of the look and the theme switch, with what it stores and where.

**Acceptance Scenarios**:

1. **Given** the README, **When** a reader looks for how Umbral looks or how to
   change the theme, **Then** they find it without leaving the page.

### Edge Cases

- **Remembered theme value is garbage** (hand-edited, truncated, upper-case,
  from a future version): ignored; the page follows the system (US2-4).
- **Theme control used with an expired page**: the person gets the same
  "this form expired" message every form gives, which says what to do next.
- **Where to return after choosing a theme**: only a page on this instance. A
  return address that points elsewhere, in any spelling a browser would treat
  as another site, sends the person to the instance's home instead (no open
  redirect).
- **The end-user sign-in page in the middle of a sign-in**: choosing a theme
  returns to that same page with the same app and the same pending sign-in.
- **A browser that refuses to store the choice**: the page keeps following the
  system; nothing breaks.
- **Very old browsers without system-theme support**: they get the dark theme,
  which was Umbral's only theme until now.
- **Accent close to one theme's card colour** (a near-white accent in light, a
  near-black accent in dark): saved with the low-contrast warning; the text on
  the buttons stays legible because it is chosen per accent.
- **Logo with a transparent background**: shown on both themes' cards as it is;
  the operator sees it on both in the Apps preview.
- **High zoom and narrow screens**: the rail stacks on top; nothing requires
  horizontal scrolling at 320 CSS pixels.
- **Reduced motion**: theme changes and the dialog do not animate when the
  person asks for reduced motion.
- **Print**: no requirement; whichever theme is active prints.

## Requirements *(mandatory)*

### Functional Requirements

**Mark and wordmark**

- **FR-001**: Every place that shows Umbral's mark (the header or rail brand,
  the footers, the "Secured by Umbral" mark of a branded sign-in page) MUST show
  the Gnomon: a sun, an upright, its shadow, and (at large sizes) the threshold
  line, drawn in the colours of the active theme.
- **FR-002**: At 32 CSS pixels and below the mark MUST use the small drawing
  (no threshold line, a thicker upright), and at 16 pixels the smallest drawing
  of the brand sheet, so it stays legible.
- **FR-003**: The browser tab icon MUST be the dark Gnomon on a dark ink tile in
  both themes, so it is visible on light and dark browser chrome alike.
- **FR-004**: The wordmark MUST read `umbral` in lower case, in the display
  typeface; page titles keep "Umbral" as the product name.

**Colour**

- **FR-005**: The dark theme MUST use the Umbra dark palette: ink surface
  `#0b0b14`, containers `#14141f` and `#1d1d2b`, lines `#2e2e40`, text
  `#ece8df`, secondary text `#a7a2b6`, corona gold `#f5c26b` with `#2a1d05` on
  it, error `#ff8f7e`, success badge `#20301f` with `#a8d99a`.
- **FR-006**: The light theme MUST use the Umbra light palette: surface
  `#f4f1ea`, container `#fffdf8`, lines `#e3ddd0`, text `#17151f`, secondary
  text `#5f5a6b`, gold for text and links `#8a5a00`, primary buttons `#17151f`
  with `#f5c26b` text, error `#b3261e`, success `#e6eedc` with `#2f5d1e`,
  warning `#fbefd6` with border `#e2c27f` and text `#6b4500`.
- **FR-007**: In both themes, all text MUST meet a contrast of at least 4.5:1
  against what it sits on, and input borders and focus indicators at least 3:1
  (WCAG 2.x AA). Where a palette value above misses this for an input border,
  a darker (light theme) or lighter (dark theme) value of the same hue MUST be
  used for that purpose, and the plan records it.

**Typography**

- **FR-008**: Headings and the wordmark MUST use the display typeface (extra
  bold); body text, labels and buttons the text typeface (regular, medium,
  semibold); client IDs, URLs, secrets and other machine values the mono
  typeface (regular, medium).
- **FR-009**: The font files MUST be served by the instance itself, cover the
  Latin character set, and include only the weights above. No page MAY request
  a font from another host.
- **FR-010**: The fonts' licences MUST ship with Umbral (in the repository and
  in the image).

**Theme switch**

- **FR-011**: With no theme chosen, pages MUST follow the browser's system
  theme preference.
- **FR-012**: A person MUST be able to choose System, Light or Dark from a
  control in the operator's rail (below the navigation) and in the footer of
  every page that has no rail (the end-user sign-in page, setup, operator
  sign-in, message pages). The control MUST show which option is in effect and
  a short line saying what is in effect, with all wording in the text catalogue.
- **FR-013**: Choosing Light or Dark MUST remember the choice in the person's
  browser for about a year; choosing System MUST forget it. Nothing about the
  choice MAY be stored on the server or in the data directory.
- **FR-014**: The server MUST render the remembered theme into the page itself,
  so the page's first paint is already in the chosen theme, with or without
  JavaScript.
- **FR-015**: A remembered value other than the two themes MUST be ignored.
- **FR-016**: After a choice the person MUST return to the page they chose it
  on, query included; a return address that is not a path on this instance MUST
  be replaced by the instance's home page.
- **FR-017**: The control MUST work with JavaScript turned off, MUST be operable
  by keyboard with visible focus, and MUST expose an accessible name for the
  group and for each option, and which option is current.
- **FR-018**: The theme control MUST carry the same cross-site request
  protection as every other form, and MUST NOT require an operator session.

**Layout**

- **FR-019**: The operator's screens MUST show the navigation (Apps,
  Connectors, Users, Credential, Sign out) as a side rail with the current
  screen marked; at phone widths the rail MUST stack above the content. The
  admin UI MUST stay three top-level screens (Apps, Connectors, Users), with
  Credential and Sign out as before.
- **FR-020**: Pages without an operator session MUST NOT show the rail, admin
  links or sign-out (unchanged from today).
- **FR-021**: On the Apps screen, Create app MUST open the create form in a
  modal dialog when JavaScript is available, with focus moved into it and
  Escape and Cancel closing it; without JavaScript it MUST open the same form
  as a page of its own. Both MUST submit the same form to the same place with
  the same outcome.

**Feature 006 continuity**

- **FR-022**: An app's accent colour MUST replace the Umbra gold on its sign-in
  pages (buttons, links, highlights, focus indicators) in both themes, with the
  text on the accent chosen for legibility as feature 006 defines (FR-016 of
  spec 006).
- **FR-023**: The low-contrast warning for an accent (spec 006, FR-017) MUST be
  given when the accent is below 3:1 against the card of either theme.
- **FR-024**: The "Secured by Umbral" mark on a branded sign-in page MUST be the
  Gnomon in the active theme's colours, and no accent MAY recolour it.
- **FR-025**: Admin chrome MUST NOT take any app's accent (spec 006, FR-005);
  the per-app preview on the Apps screen keeps doing so inside its card.

**Documentation**

- **FR-026**: The README MUST describe (or show) the Umbra look, the two themes,
  the theme control, where it is, and that the choice lives only in the
  browser.

### Constitution Check (spec level)

- **Concept count (Principle I)**: No new user-facing noun. "Theme" is a
  display preference of one browser, like a zoom level; it names nothing that
  Umbral manages, stores or lets the operator configure, so it is not a concept
  next to App, Connector and User. The admin UI stays three top-level screens.
- **Single artifact (Principle II)**: The fonts ship inside the one image; no
  runtime dependency is added; the theme choice lives in the browser, not in
  the data directory. The image grows only by the font files (FR-009).
- **Secure default (Principle IV)**: The theme form carries cross-site request
  protection (FR-018) and cannot be used as an open redirect (FR-016). The
  content-security policy stays "this origin only".
- **Scope (Principle VII)**: Not on the exclusion list.

### Key Entities

- **Theme choice**: one of System (the default, meaning nothing remembered),
  Light or Dark. Held only by the person's browser. Read on every page request
  to render the page in that theme.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of page kinds (setup, operator sign-in, the four operator
  screens, the end-user sign-in page, branded and unbranded, and message pages)
  render in both themes with the Gnomon mark and the Umbra typefaces.
- **SC-002**: Every text and background pairing defined by the two palettes
  meets 4.5:1, and every input border and focus indicator meets 3:1, as
  recorded in the plan's contrast table.
- **SC-003**: A page in a chosen theme shows no frame of the other theme on
  load, with JavaScript on or off.
- **SC-004**: Choosing a theme takes one action and returns the person to the
  same page within the time of one ordinary page load.
- **SC-005**: Zero requests leave the instance to draw any page (fonts
  included).
- **SC-006**: The instance's idle resident memory stays under 150 MB (spec 001,
  SC-007), and the image grows by no more than 400 KB.
- **SC-007**: Every feature 006 acceptance scenario still passes, in both
  themes.
- **SC-008**: Every admin screen is usable at 320 CSS pixels wide without
  horizontal scrolling.

## Assumptions

- "About a year" for the remembered choice is 365 days.
- The theme control appears on every page without a rail, not only the
  end-user sign-in page, because those pages share one footer and a person on
  the operator sign-in page benefits the same way.
- Browsers without system-theme support get the dark theme, Umbral's look until
  now.
- The existing screens keep their structure and content; they take the new look
  through the shared components. Only the navigation (rail) and the Create app
  entry (dialog and page) change shape. The mockups' App detail, Connectors and
  Users artboards guide the styling of the existing screens, not new screens.
- The favicon is not theme-aware (one dark tile reads on both browser chromes).
- No htmx enhancement of the theme control is required; a full page load after
  a choice is acceptable (SC-004).

## Out of Scope

- New screens, new settings for the operator, an instance-wide default theme
  chosen by the operator, per-app themes beyond feature 006's accent and logo.
- Translations of the new strings (Umbral is English only).
- Changing what any screen does.
