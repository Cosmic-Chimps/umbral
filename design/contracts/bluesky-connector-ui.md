# The Bluesky connector: how it looks

Design answers for spec 015 (task b5ky), written 2026-11-12 by the design reviewer for the
implementer's six questions. The base is `design/contracts/fediverse-connector-ui.md`: where
this file is silent, that one applies with "Bluesky" for "Fediverse". Product decisions (what
the step accepts, what the server list holds, the claims, the https requirement) are the
spec's. Wording is proposed; `Views/Texts.fs` wins once it holds it. The reviewer has not read
Bluesky's brand terms or the AT Protocol documents.

## 1. The picker button

"Sign in with Bluesky", by the provider-marks rule (`design/contracts/provider-marks.md`): the
**link icon**, unless the research shows that Bluesky's terms let Umbral ship its mark and that
the mark may be drawn in one colour, the label's, on Umbral's own button. If both hold, the
butterfly leads the button exactly as GitHub's mark does (1.25em, pure white or black on the
secondary button, the label's colour on an accent). If either fails or is unclear, the link
icon. Write which it is, and why, into the research and into `notes.md`.

Place in the picker: by identifier, as every connector. Nothing places it specially.

## 2. The step page

The Fediverse step's shape: the picker's chrome (plain or branded), one `max-w-sm` card.

- `h1`: *What is your Bluesky handle?* (The Fediverse step asks where the account is, because
  there the server is the question. Here the handle is.)
- Field label: *Handle*. Placeholder: `you.bsky.social`.
- Hint: *Like you.bsky.social, or your own domain if you use one. It is on your Bluesky
  profile.*
- Attributes: `autocomplete="username"`, `autocapitalize="none"`, `spellcheck="false"`,
  `inputmode="email"` (the keyboard with `.` and `@`, as on the Fediverse step; people type the
  `@`), autofocus.
- Primary full-width **Continue**; the ghost **Other ways to sign in** with the chevron, only
  when the app offers more than one connector.

## 3. The step's errors

All under the field, the typed value kept, `aria-invalid`, focus back on the field, the handle
and the server drawn as text.

- (a) Not a handle: *Enter a Bluesky handle like you.bsky.social.*
- (b) No account for it: *No Bluesky account was found for {handle}. Check the spelling.*
  (No "we".)
- (c) The account does not confirm the handle: the same text as (b). The difference is not the
  person's to act on.
- (d) Refused server: *Accounts on {server} cannot sign in here. Use another way to sign in.*
- (e) No answer: *{server} did not answer. Try again in a moment.*
- (f) Private address: the same text as (a).
- (g) If the spec has the case, a server that does not offer this sign-in: *The server for
  {handle} does not offer the sign-in Umbral uses. Use another way to sign in.*

Failures after the person leaves for their server use the existing `/connect/error` page.

## 4. The Connectors pane

A pane of the frame, built like the Fediverse pane.

- Lead: *People sign in with their Bluesky handle. Any AT Protocol account works, wherever it
  is hosted.*
- **Set-up**: show **one** address, the client metadata address, with copy. Label: *Client
  metadata address*. Hint: *Each server fetches this address to learn about Umbral. There is
  nothing to paste anywhere, but it must be reachable from the internet.* Do not show the
  callback URL here: the operator registers nothing, it is inside the metadata, and two
  addresses with nothing to do would read as two steps. The walkthrough can name it.
- The no-email note, as on the Fediverse pane, with "Bluesky account".
- **Settings**: the shared check; the scope as `.um-choice` radio cards, *Any server* (*Anyone
  with a Bluesky or other AT Protocol account.*) and *Only these servers* (*Only accounts that
  sign in at the servers listed below.*); the **Servers** field, placeholder `bsky.social`,
  hint *The server where people sign in, separated by commas or spaces. Accounts hosted by
  Bluesky all sign in at bsky.social. Used only with "Only these servers".*; the empty-list
  warning and the refusals as on the Fediverse pane; **Save connector** or **Enable Bluesky**.
- Closing row: Enable or Disable, Delete.

**When the instance's address cannot work** (http, or a port): the pane shows the lead and a
Warning alert under the heading, and **no Settings form and no closing row**:
*Bluesky needs Umbral at an https address without a port, reachable from the internet. This
instance is at {origin}, so Bluesky cannot be enabled here.* The list entry reads "Not set up"
as usual, with no marker. If a connector that was enabled later finds itself at such an
address (the origin changed), the entry takes the attention marker and the pane shows the same
alert above its sections; what happens to sign-ins then is the spec's.

## 5. Order

Connectors list: configured providers, kinds not set up, **Fediverse, Bluesky**, Passkey, then
the pinned Allowed email domains. The picker: identifier order, unchanged.

## 6. Captures for review

In `.alta/reviews/2026-11-11-bluesky/`, `<state>-<width>-<theme>.png`, 1280 and 360, both
themes unless said. The Fediverse list with the name swapped, plus:

- `connectors-off`, `connectors-any`, `connectors-list`, `connectors-list-refused`.
- `connectors-origin-blocked`: the pane on an http or ported instance.
- `picker`: an app offering both Fediverse and Bluesky among others, so the two are seen side
  by side. `picker-branded` (1280).
- `step`, `step-branded` (1280), `step-not-a-handle`, `step-not-found`, `step-refused`,
  `step-focus` (1280 dark).

Plus `notes.md` with every new string, the mark decision from question 1, and the detector's
output if the CLI is at hand.
