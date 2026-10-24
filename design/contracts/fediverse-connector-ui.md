# The Fediverse connector: how it looks

Design answers for spec 010, written 2026-10-23 by the design reviewer before the
spec run, so the four design questions in the task have an answer to build to.
Product decisions (the model, the flow, security, the allowlist's meaning) are
the task's and the spec's; this file only says what the operator and the person
signing in see. Wording below is proposed; `Views/Texts.fs` is where it lands
and wins once it is there.

## 1. On the Connectors screen

**Today's screen (cards).** The Fediverse connector is a built-in kind like
Passkey: one per instance, never in the add form, with a card whether or not it
is set up. Its card goes **after the provider cards and the empty provider
kinds, before Passkey**: a provider kind whose provider is wherever the person's
account lives, so it sits with the providers; Passkey, where Umbral is the
provider, keeps the last place.

The card follows the Passkey card's shape exactly (`PasskeyConnector.card`):

1. Heading row: "Fediverse" in headline medium; pills: Shared or Not shared and
   Enabled or Disabled when configured, Not configured otherwise.
2. A lead sentence (body medium, muted): what it is, in one line. Proposed:
   *People sign in with an account on a Mastodon-compatible server. Umbral
   registers itself with each server the first time someone from it signs in.*
3. The set-up facts, as the provider cards show them: the **Callback URL** with
   copy (it is what Umbral registers on every server, and what an operator
   recognises in a server's app list), and an info note: *A fediverse account
   has no verified email, so allowed email domains do not apply to it and it
   never joins another account by email.*
4. The form (`space-y-4`): the shared check (`sharedBox`), then the **instance
   scope**, a radio pair drawn like the passkey enrolment radios
   (`enrolmentRadio`): *Any server* (default) with hint *Anyone with an account
   on a server that works like Mastodon.*, and *Only these servers* with hint
   *Only accounts on the servers listed below.* Under the pair, always visible
   (no script hides it), the **Servers** field: a text field drawn like the
   allowed-domains field (`Components.fieldWith`, placeholder
   `mastodon.social, fosstodon.org`), hint *Server addresses, separated by
   commas or spaces. Used only with "Only these servers".* A refused entry is
   drawn under the field in the operator's words, naming the entry as text,
   the way `parseDomains` does. Then the primary button: *Save connector*, or
   *Enable Fediverse* when not yet configured, with the CheckCircle icon.
5. When configured, the action row as on Passkey: Enable or Disable (small
   ghost, htmx to the card) and Delete (small danger, confirm), with the
   last-enabled warnings.

**Compatibility with the master-detail contract (section 3).** When the
Connectors task rebuilds the screen, the entry reads: line 1 "Fediverse"; line
2 `fediverse` in mono, or "Not set up"; status: the state badge, or the
attention marker when offered nowhere. Its pane's sections are the card's parts
in the same order: Set-up (the lead, the callback URL, the no-email note),
Settings (shared, instance scope, servers, Save), closing action row. Build the
card so those three groups are already distinct (`space-y-4` between them, the
form as one group), and the later move is a cut, not a redesign. List order
under the contract: configured providers, not-set-up kinds, **Fediverse,
Passkey**, the pinned Allowed email domains entry.

Data hooks for tests, in the style of the passkey card: `data-connector="fediverse"`,
`data-connector-kind="fediverse"`, `data-connector-state`, `data-connector-shared`,
`data-instance-scope` on the radio fieldset, `data-instance-list` on the field.

## 2. On the sign-in page: the handle entry is a step, not a field on the button

The picker is a column of equal buttons, each a link to `/connect/<slug>/start`.
A field inside that column would break the one thing the page does (choose a way
in), fight the Last used ordering, and need a form inside a list of links. So:

- The picker shows **Sign in with Fediverse** as an ordinary connector button
  (`t.SignInWith` with the display name "Fediverse"; the Link icon as the
  others), in the same place and style, Last used badge included.
- Pressing it opens the **account step**: `GET /connect/fediverse/start?returnUrl=…`
  without a handle renders a page in the same chrome as the picker (the plain
  page, or the app's branded page with its accent and "Secured by Umbral",
  exactly as the picker chose), with one card, `max-w-sm`, `space-y-5`:
  1. `h1` in headline large: *Where is your account?*
  2. One field, `Components.fieldWith`, label *Your account or server*,
     placeholder `@you@mastodon.social`, hint *A handle like
     @you@mastodon.social, or a server like mastodon.social. Mastodon,
     GoToSocial and Akkoma servers work.* `autocomplete="username"`,
     `autocapitalize="none"`, `spellcheck="false"`, `inputmode="email"` (the
     keyboard with @ and .), autofocus.
  3. The primary button, full width like the picker's: *Continue*.
  4. Under it, a ghost link back to the picker with the chevron-left icon:
     *Other ways to sign in*, only when the app offers more than one connector.
- The form posts to `/connect/fediverse/start` with `returnUrl` as a hidden
  field and the antiforgery token; a good handle redirects to the server. The
  step works with JavaScript off and needs none with it on.
- The step is the right place for every problem the person can fix: it renders
  again with the typed value kept, the field marked `aria-invalid`, the message
  under it as `um-field-error`, and focus on the field. The message names the
  server as text (Oxpecker encodes it; never as a link).
- Not in 010: remembering the person's server in the browser. The Last used
  cookie names the connector, not the server, and stays so.

## 3. Copy and errors

Proposed catalogue strings; the first line is the key's purpose.

Under the field, on the step (fixable, so form errors):

- Not a handle or a server: *Enter a handle like @you@mastodon.social, or a
  server address.*
- Unsupported server (no Mastodon apps API, no PKCE, a Misskey-family
  server): *{server} does not offer the sign-in Umbral uses. Mastodon,
  GoToSocial and Akkoma servers do; Misskey and Sharkey do not yet. Use another
  account, or another way to sign in.*
- Refused server (not on the operator's list), one neutral line: *Accounts on
  {server} cannot sign in here. Use another way to sign in.*
- Server did not answer in time, or answered with an error during
  registration: *{server} did not answer. Try again in a moment.*
- A private or loopback address outside development: the same text as "not a
  handle or a server"; the reason is not the person's business.

Elsewhere:

- The person refuses at the server, or the callback's state or code is wrong:
  the existing `/connect/error` path and page, as every provider; no new page.
- The operator's refusal of a server entry in the list, under the field on the
  card, naming the entry: *"{entry}" is not a server address.*; *"{entry}" has a
  wildcard; list each server.*
- The connector's state notices on the Connectors screen reuse the existing
  saved, enabled, disabled and deleted notices with the connector's name.

Tone, as the README: full sentences, the server named, the next action in the
last sentence, no exclamation marks, no "oops".

## 4. The allowlist editor

Not a new control. The radio pair plus the Servers text field above, which is
the allowed-domains editor's shape with a scope switch in front of it. One
field, comma or space separated, lowercase on save, shown back normalised. No
table, no add-and-remove rows, no per-server toggles: the list is short by
nature and the operator edits it twice a year. If a list ever outgrows a text
field, that is a later task.

## 5. What to capture for review

In `.alta/reviews/2026-10-23-fediverse-connector/`, `<state>-<width>-<theme>.png`
at 1280 and 360, both themes:

- `connectors-off`: the Connectors screen with the Fediverse card not set up.
- `connectors-any`: enabled, Any server.
- `connectors-list`: enabled, Only these servers with two entries.
- `connectors-list-refused`: a refused entry drawn under the field.
- `picker`: the sign-in page with the Fediverse button among others.
- `picker-branded` (1280 only): the same on an app with an accent and logo.
- `step`: the account step, empty.
- `step-unsupported`: the step with the unsupported-server error.
- `step-refused`: the step with the refused-server error.
- `step-focus` (1280 dark only): keyboard focus on the field, ring visible.

Plus the detector on `umbral.input.css` if any class was added, the test names,
and every new Texts.fs string in `notes.md`.
