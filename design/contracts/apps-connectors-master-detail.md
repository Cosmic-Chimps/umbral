# Apps and Connectors as a list and a detail

Design contract, 2026-10-23, for the redesign of the two operator screens whose
items today render as full cards one under another. Written by the design
reviewer after reading `Handlers/Admin/Apps.fs`, `Connectors.fs`,
`PasskeyConnector.fs`, `AppBranding.fs`, the redirects, DESIGN.md and
constitution 3.0.0. Three choices in it are the maintainer's (confirmed
2026-10-23): the allowed-domains entry pinned below the connector list, the first
app selected by default on wide screens, and Users left as it is.

Mode: **Operate**. The operator comes here a few times a year to wire one app or
one connector; the screen's job is to put that one item in front of them with
nothing else competing. Brand lives in the details (the rail, the pills, the
gold selection), not in expression.

Two CodeAlta tasks, in this order, one contract:

1. **Apps.** Builds the shared frame (list column, detail pane, back link, the
   section title, the closing action row) and moves the Apps screen onto it.
   This task **absorbs** `.alta/tasks/2026-10-22-app-card-section-headings-and-final-action-row-spacing-h3cd.md`:
   the detail pane defines the section title and the closing row, so that task is
   superseded and should be closed, not run.
2. **Connectors.** Reuses the frame. Nothing in it may change the frame's
   classes or behaviour; if Connectors needs something the frame lacks, add it
   to the frame in a way Apps also gets.

Users is untouched. Section "Later: Users" says what it would need to match.

---

## 1. The frame (shared by both screens)

### Topology

- **Wide (1024px and up, Tailwind `lg`):** inside the page's `max-w-page`
  column, under the page heading, a two-column grid: the **list** at a fixed
  320px on the left, the **detail pane** filling the rest, 32px apart. The
  page heading (`h1` in display, lead in body medium) spans both columns as
  today. The rail is already a column at 768px, which leaves too little for
  three columns, so between 768px and 1023px the screen behaves as on a phone.
- **Narrow (below 1024px):** one column. The list URL shows the list only; a
  detail URL shows the detail only, with a **back link** above its heading:
  a ghost small button with the chevron-left icon and the screen's name
  ("Apps", "Connectors"), from the text catalogue. No tabs, no drawer, no
  accordion.

### The list column

- A heading row: the count in label medium muted ("4 apps"; "6 connectors"),
  and the screen's one **create** action at the right as a small primary
  button with the Add icon. The count and the button come from the text
  catalogue.
- Entries are links (`a`), one per item, stacked with 4px between them, each
  at least 44px tall, 12px radius, 14px by 10px padding, like the rail's
  entries, so the two columns read as one navigation system. Each entry
  holds at most: a first line (the name, body medium at 500, bone), a second
  line (label medium, muted; mono where it is an identifier), and at the
  right edge one status marker (a pill or an icon, see each screen). Nothing
  else: no form, no copy field, no alert, no third line.
- **Rest:** the first line in bone, the second muted, no fill. **Hover:** the
  container step, both lines unchanged. **Selected:** the high step, first
  line in gold at 600, `aria-current="page"`. **Focus:** the global 2px gold
  ring. Selected and hover never both fill: selected wins. (Amended
  2026-10-23 after the Apps review: the earlier "rest: text muted" line
  contradicted the entry definition above it.)
- A **pinned entry** (Connectors only) sits below the connector entries after
  a 40% hairline with 12px above and below, drawn like any entry.
- **Empty list:** the column shows the existing empty-state component (icon,
  title, body) in place of the entries; the create button stays in the
  heading row.
- The list never scrolls on its own; the page scrolls.

### The detail pane

- Not a card inside the page: the pane is a `section` on the page surface
  with no border and no fill of its own. Its contents are the item's
  heading, then sections.
- **Heading block:** the item's name as `h2` in headline large; beside it, the
  item's pills (type, shared, store, state). Under the name, where it exists,
  the identifier in mono hint (the connector's slug). Nothing else in the
  heading block.
- **Item notices** come next, under the heading and above the first section:
  the success or warning alert for an action on this item, the strand warning,
  the secret reveal. Page-level notices (an item deleted, a banner about the
  origin or the secret store) stay at the page top above the frame, as today.
- **Sections** follow, each separated from the previous by a 40% hairline with
  24px above and 20px below. Each starts with a **section title**: a new
  component class (`.um-section-title`), an `h3` in body large at 600, bone,
  with 12px below. Field labels inside stay `.um-label`. Inside a section,
  groups are 20px apart (`space-y-5`); fields inside a form 12px apart
  (`space-y-3`); a button row is `flex flex-wrap items-center gap-2`. A
  section with a single form puts that form's submit as the row's first
  button, a small secondary button, unless the section is the item's main
  edit form, whose Save is the full-size primary button.
- **The closing action row** is the pane's last element: a 40% hairline with
  24px above, then a wrapped button row holding the item's state and lifetime
  actions (Regenerate secret; Enable or Disable; Delete). The red fill appears
  once here and nowhere else in the pane (DESIGN.md, the One Red Fill rule).
  Every destructive form keeps `data-confirm`.
- A pane never shows another item's anything. Accent sheets: only the selected
  app's, so one `<link>` at most.

### URLs, selection and JavaScript

- `GET /admin/<screen>`: the list. Wide: the first entry is selected and its
  detail shown, the URL unchanged. Narrow: the list alone.
- `GET /admin/<screen>/<id>`: that item selected, its detail shown, the full
  page rendered (list included on wide). This is the canonical address of an
  item and the only target a redirect uses: **every POST about an item
  redirects to its detail URL**, with the notice in the query as today's
  `?msg=` (drop the `&app=…#app-…` anchor scheme; the pane is the landing).
  A POST that deletes the item redirects to the list URL with its notice.
- `GET /admin/<screen>/new`: the create form in the pane, no entry selected,
  the create button in the list heading shown as pressed
  (`aria-current="page"` on it rather than on an entry). After a successful
  create, redirect to the new item's detail URL (for an app, with the secret
  reveal as today).
- An unknown id renders the list with no selection, the existing "does not
  exist" notice at the page top, and status 404.
- **Without JavaScript** everything above is the whole behaviour: entries are
  links, forms post, redirects land on the item.
- **With htmx**, entries and the create button carry `hx-get` of their URL,
  `hx-target` the frame container (`[data-frame]`, the grid holding both
  columns), `hx-select` the same container, `hx-swap="outerHTML"`,
  `hx-push-url="true"`. The server always returns the full page; htmx keeps
  the frame. Every htmx-intercepted form in a pane (today the Enable/Disable
  toggles) targets the same frame the same way, so the list's status marker
  and the pane change together. One rule, no partial endpoints. Focus after a
  swap goes to the pane's `h2` (`tabindex="-1"`), so a keyboard user lands on
  the item they chose.
- The Create app `<dialog>` is retired: the pane is where creating happens,
  with and without JavaScript, and one path is better than two. (Spec 007 said
  creating "can" happen in a dialog; this contract chooses not to.) The
  maintainer may reverse this in the task; if kept, the dialog must still
  post to the same endpoint and redirect to the new item's detail URL.

### Accessibility

- The list is a `nav` with an `aria-label` from the catalogue ("Apps list");
  entries are plain links; the selected one is `aria-current="page"`.
- The pane is a `section` labelled by its `h2`.
- The back link is a link, not a button, and comes first in the pane's tab
  order on narrow screens.
- Contrast and focus per DESIGN.md's Contrast Floor; the selected entry's gold
  on the high step is the same pair the rail uses, already checked.
- Status markers that are icons carry an accessible name (`aria-label` or
  visually hidden text).

---

## 2. Apps

### List entries

Order: by display name, case-insensitive. Each entry:

- Line 1: the display name.
- Line 2: the app type label ("Web application", "SPA or native app") in label
  medium muted; not a pill, so the column stays quiet.
- Status marker at the right: none in the ordinary case. When the app's choice
  lets in connectors no app should (the existing unrestricted warning), the
  Warning icon in the warning colour with the accessible name "Check sign-in
  methods" (catalogue). No other marker.

Create: the list heading's primary button "Create app", shown only when at
least one connector is enabled (today's rule). With no enabled connector and
no apps, the whole frame is replaced by today's "need a connector" empty
state with its link to Connectors, unchanged. With no enabled connector but
apps that exist, that empty state sits above the frame and the frame stays
without its Create button, so the apps' actions (Regenerate secret, Delete)
remain reachable. (Amended 2026-10-23 after the Apps review.)

Empty list (no apps, a connector enabled): the list column shows today's empty
state; on wide screens the pane shows the create form at once, as if
`/admin/apps/new` had been opened, with the create button pressed.

### Detail pane, in order

1. **Heading block:** name; the type pill (muted).
2. **Item notices:** this app's success or warning alert (connectors saved,
   branding saved, the low-contrast warning, the strand warning with its
   confirm button), and the **secret reveal** after create or regenerate: the
   existing reveal card's content, shown once, at the top of this app's pane
   rather than above the list.
3. **Connection** (new section title, catalogue): Client ID with copy, Issuer
   URL with copy, in a two-column grid from 640px; then the redirect URIs as a
   mono list. No form.
4. **Sign-in methods** (existing title): the summary line or pills of what the
   app offers, the unrestricted warning when it applies, then the choice form
   with its small secondary "Save sign-in methods".
5. **Sign-in page branding** (existing title): exactly the section as merged
   from the branding task: the preview panel, the logo form with its button
   row (Save logo, Use whole image, Remove logo as a red ghost), the accent
   form with Save colour. Only its title's style changes (`.um-section-title`
   instead of `.um-label`).
6. **Closing action row:** Regenerate secret (small secondary, with the Key
   icon; hidden for a public client as today) and Delete app (small danger).

### Stays out of the entries

Client IDs, issuer, redirect URIs, connector pills, the branding preview, every
form and every alert.

---

## 3. Connectors

### List entries

Order, top to bottom:

1. Configured connectors in today's order: Discord, GitHub, Google, then
   OpenID Connect providers by slug, then a fake one where allowed.
2. Kinds not yet set up (Discord, GitHub, Google only, as today's empty
   cards), in that order, drawn muted at rest.
3. Passkey, always, configured or not.
4. After the pinned hairline: **Allowed email domains**, the instance-wide
   setting, at `/admin/connectors/domains`.

Each connector entry:

- Line 1: the display name (for an unconfigured kind, the kind's name).
- Line 2: the slug in mono, muted; for an unconfigured kind, "Not set up"
  (catalogue) in label medium muted; for the domains entry, a short line
  from the catalogue saying what it is ("Who may sign in, for every
  connector").
- Status marker at the right: the existing state badge, Enabled or Disabled,
  for a configured connector; nothing for an unconfigured kind or the domains
  entry. When the connector is offered nowhere, or its secret store cannot be
  read, the Warning icon in the warning colour **replaces** the state badge,
  with the accessible name "Needs attention" (catalogue); the pane explains
  which problem.

Add: the list heading's primary button "Add connector", opening the add form
in the pane at `/admin/connectors/new` (kind select, slug, name, issuer for
OpenID Connect, client id, secret, shared, domains, exactly today's form).
Opening an unconfigured kind's entry shows that kind's set-up form in the
pane at `/admin/connectors/<kind-slug>`, which is where today's empty card
posts; the two forms stay the two forms they are today.

Page-level, above the frame, unchanged: the origin banner and the secret-store
banner.

### Detail pane for a provider connector, in order

1. **Heading block:** name; under it the slug in mono hint; pills: Shared or
   Not shared, the secret store where it is external, Enabled or Disabled
   (for an unconfigured kind: Not configured).
2. **Item notices:** this connector's success or warning alert, the
   "explained" warning from the provider, the strand warning with its confirm
   button, the offered-nowhere note, the unreadable-store note.
3. **Set-up** (new section title, catalogue): the naming advice (info note),
   the Callback URL with copy, the issuer with copy and its fixed-issuer note
   where there is one, the kind's set-up caveat where there is one. Who may
   sign in (the rule line: connector list, instance list, or anyone) closes
   this section as a plain sentence.
4. **Settings** (new section title, catalogue): the edit form as today: name,
   client id, secret (with the store choice where stores exist), the shared
   check, the allowed-domains field with its hint, the limit-one-app note;
   Save connector as the full-size primary button at the form's end.
5. **Closing action row:** Enable or Disable (small ghost, htmx-intercepted,
   targeting the frame) and Delete connector (small danger), with the
   last-enabled confirmations as today.

### Detail pane for Passkey

1. Heading block: "Passkey"; pills as above.
2. Item notices, including the invitation link shown once.
3. **Set-up**: the passkey lead, the origin-bound note.
4. **Settings**: the shared check, the enrolment choice (invitations only or
   anyone), the enrolment rule hint; Save connector or Enable passkey as the
   primary button.
5. **Invitations** (existing wording): the invite form, then the open
   invitations with their Revoke buttons; configured only.
6. Closing action row: Enable or Disable, Delete.

### Detail pane for Allowed email domains

1. Heading block: "Allowed email domains".
2. Item notices: the removal warning with its confirm, the saved notice.
3. One section, no title needed because the heading is the section: the lead,
   the field, Save as the primary button. No closing action row.

### Stays out of the entries

Callback URLs, issuers, every note and caveat, the forms, the invitation list,
the shared pill (it is in the pane; the list shows only state).

---

## 4. States to capture for review

Named `<state>-<width>-<theme>.png`, both themes unless noted, in
`.alta/reviews/<task>/`. Width 1280 and 360 for every state; 768 once, for the
collapsed layout.

**Apps task**

- `list-first-selected`: `/admin/apps` on wide with four or more apps, the
  first selected.
- `detail-notice`: an app's pane after saving sign-in methods (success alert
  in the pane, not at the page top).
- `detail-secret`: the pane right after Regenerate secret, the reveal at the
  top of the pane.
- `create`: `/admin/apps/new`, the create button pressed, the form in the
  pane.
- `empty`: no apps, a connector enabled: empty list, the form in the pane.
- `need-connector`: no enabled connector (the frame replaced).
- `unrestricted`: an app whose entry shows the warning marker.
- `focus-entry` (1280 dark only): keyboard focus on a non-selected entry, the
  ring visible.
- `narrow-list` (360 only): `/admin/apps`.
- `narrow-detail` (360 only): an app's detail with the back link.
- `collapsed` (768 only, dark): `/admin/apps/<id>` showing one column with
  the back link beside the rail column.

**Connectors task**

- `list-first-selected`: wide, with at least one configured connector, one
  unconfigured kind, Passkey, and the domains entry visible.
- `detail-provider`: a configured Discord or GitHub pane, every section.
- `detail-not-set-up`: an unconfigured kind's pane.
- `detail-passkey`: configured, with one open invitation.
- `detail-domains`: the pinned entry's pane.
- `add`: `/admin/connectors/new`.
- `attention`: an entry with the warning marker (offered nowhere), and its
  pane.
- `toggle`: the pane and list after an htmx Disable (the badge changed in
  both).
- `narrow-list`, `narrow-detail` (360 only), `collapsed` (768 only, dark).

Plus, both tasks: the Impeccable detector output on `umbral.input.css` and the
changed views' rendered pages if the implementer can serve them, the test
names, and the Texts.fs additions old to new in `notes.md`.

---

## 5. Tests the tasks should add or change

- Routes: `/admin/apps/{id}`, `/admin/apps/new`, `/admin/connectors/{slug}`
  for every kind, `/admin/connectors/new`, `/admin/connectors/domains`.
- Every existing POST's redirect now lands on the item's detail URL (or the
  list after delete); the `#app-` anchor scheme is gone.
- Exactly one entry carries `aria-current="page"` on a detail URL, none on
  the list URL at narrow widths (the server cannot know the width, so the
  list URL renders the first app selected and the narrow stylesheet hides the
  pane; the test checks the markup rule, not the width).
- The unknown-id page is 404 and names the notice.
- Every form in a pane works without htmx (plain POST through the test
  client), as today's tests already do per form.
- The `data-*` hooks the branding scripts and tests use are unchanged.

---

## 6. Later: Users

Users already lists people. To match, it would need the same frame with the
person's detail (their provider identities, their passkeys with Remove, Invite
to add a passkey, Enable or Disable, Delete) in the pane and the search above
the list. It is out of scope for these two tasks, and nothing in the frame may
assume it will never come.

---

## 7. Anti-goals

- No fourth top-level screen, no new noun, no tabs inside the pane, no
  accordion, no sidebar drawer.
- No client-side routing or state: the URL is the state.
- No partial-render endpoints; the full page with `hx-select` is the whole
  htmx story.
- No change to what any form does, posts, or validates; this is a layout and
  navigation change.
- No second accent, no shadow on the list or the pane, no card-inside-a-card.
