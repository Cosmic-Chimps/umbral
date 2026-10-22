# Research: "Last used" on the sign-in page

## R1. Where the cookie is written

**Decision**: inside `Sessions.signIn` (`src/Umbral/Infrastructure/Sessions.fs`),
right after the session cookie is issued, with the `slug` it is given.

**Rationale**: `Sessions.signIn` is called in exactly three places, each after
every check has passed:

- `Handlers/Connect/Callback.fs`: after the state check, the exchange, the
  allowed email domains and the disabled-user check.
- `Handlers/Passkeys/Ceremonies.fs` `assertion`: after the WebAuthn verification
  and the disabled-user check.
- `Handlers/Passkeys/Ceremonies.fs` `registration`: after the registration is
  saved, and only when the ceremony is not `Add` (a signed-in person adding a
  passkey starts no session).

Failures, refusals and the start of a round trip never call it. The operator's
admin sign-in goes through `AdminAuth` and its own scheme. Writing the cookie
there makes FR-002 and FR-003 hold for every present path and for any future
connector kind, without three copies of the same call.

**Alternatives considered**: a call in each of the three handlers. Rejected: a
fourth sign-in path would have to remember to add it, and FR-003 would rest on
review rather than on structure.

## R2. The cookie

**Decision**: `umbral_last_connector`, value the slug as stored, `Path=/`,
`SameSite=Lax`, `HttpOnly`, `Secure` when the request is HTTPS,
`Max-Age` 365 days, `IsEssential` (as `umbral_theme`). Not signed or encrypted.

**Rationale**: the theme cookie set the pattern (`Handlers/ThemeChoice.fs`).
`Lax` because the picker is reached by a top-level navigation from an app.
`HttpOnly` because the server renders the picker and no script needs it. A
slug is `[a-z0-9-]` (connector slug rule), so it needs no encoding. Signing it
would protect nothing: a forged value can only move one of the buttons the page
already shows.

The options move from `ThemeChoice.fs` into `Infrastructure/BrowserCookies.fs`
so both cookies share them; `ThemeChoice` keeps its behaviour.

## R3. Reading and matching

**Decision**: `Picker.arrange (last: string option) (offered: Connector list)`
returns `Some remembered, others` when `offered` has two or more connectors and
one of them has exactly the slug `last` (ordinal comparison); otherwise
`None, offered` unchanged. `offered` is the list the picker already computes
(`offeredTo choice all`, enabled connectors only), so "disabled" and "not
offered" need no extra rule.

**Rationale**: the cookie value is only ever compared, never rendered (FR-008).
The other buttons keep their order because `List.filter` keeps it.

## R4. Markup and accessibility

**Decision**: the remembered button is the same `a` as the others, with
`data-last-used=""`, an `aria-label` of `"<visible label>, last used"`
(`Texts.PickerLastUsedName`), and a `span.um-last-used` badge after the label. Then a
`div.um-or` holding `Texts.PickerOr` ("or"). Then the other buttons.

**Rationale**: `aria-label` makes the badge part of the accessible name, and it
starts with the visible label, so speech input ("click Sign in with Google")
still works (WCAG 2.5.3, label in name).

## R5. Colour

**Decision**: `.um-last-used` is a pill drawn in `currentColor` (border and
text, transparent fill); `.um-or` is `text-on-surface-variant` with
`border-outline-variant` rules either side.

**Rationale**: in a button the current colour is the button's text colour:
`on-surface` for a secondary button, the accent sheet's `on-primary-fill` for a
branded one. The badge therefore has exactly the contrast the button's label
has, in both themes and on any accent (spec 006 already chooses that text
colour for contrast). The divider uses theme tokens that the accent sheets do
not touch.

## R6. Signing out

**Decision**: `/connect/logout` and the admin sign-out do not touch the cookie
(Clarifications). No code change: they never did.
