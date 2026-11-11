# Contract: routes and screens of the Apple connector

The screens were settled with `umbral-designer` on 2026-11-11 (answers 1 to 7
to the implementer's questions). Wording is open to the design and
documentation reviews.

## Routes

| Route | What it does |
| --- | --- |
| `GET /connect/apple/start?returnUrl=…` | The existing start route. For Apple it mints the form-post correlation cookie and redirects to Apple with `response_mode=form_post`. |
| `POST /connect/{slug}/callback` | New. The form-post answer: fields `state`, `code`, `user` or `error`. No antiforgery token (research R3). Rate limit `SignIn`. Body at most 64 KB. Always answers with a redirect: to the authorization request on success, to the sign-in page with a failure code otherwise. |
| `GET /connect/{slug}/callback` | Unchanged. For the Apple connector it finds no correlation and fails. |
| `GET /admin/connectors/apple` | The Apple pane (the existing detail route). |
| `POST /admin/connectors/apple` | New, routed before the generic `{slug}` route. Saves the Apple connector. Antiforgery token required. 422 with the form redrawn when a field is refused. |
| `POST /admin/connectors/apple/toggle` | The existing toggle route; for Apple, turning on runs the Enable check. |
| `POST /admin/connectors/apple/delete` | The existing delete route, unchanged. |

Failure codes shown to the person are the existing ones (`sign-in-failed`,
`access-denied`, `account-not-allowed`, `account-disabled`, and what
`Picker.failureCode` gives for a provider error).

Security log details added: none are new words except `apple_key_unusable`
(the stored key could not sign) and `response_mode_mismatch` (an answer arrived
in the mode the connector's kind does not use). The round trip's existing words
(`state_mismatch`, `no_correlation_cookie`, `correlation_replayed`, …) and the
OIDC adapter's `id_token_invalid: <reason>` apply as they are.

## The Connectors screen

**List.** An entry "Apple", after Google and before OpenID Connect providers,
"Not set up" until saved, like the other built-in kinds. It is not in the add
form's kind list.

**Pane, Setup section.** As the other providers, with a note of its own in place
of the generic naming advice (design review, 2026-11-11: the generic one says
what Apple's consent screen shows, which is not confirmed): "Give the Services
ID a description that names this Umbral instance (for example "Umbral — home"),
so you can tell it apart on Apple's site later." Then the copy
field with the return URL (`<public origin>/connect/apple/callback`), the rule
line, and the caveat:

> Apple accepts only an https address on a real domain name: not localhost, not
> an IP address. Sign in with Apple also needs a paid Apple Developer Program
> membership.

**Pane, Settings form**, in this order:

| Field | Name | Notes |
| --- | --- | --- |
| Display name | `displayName` | Hint of its own: "Shown on the admin screens. The sign-in button always says Sign in with Apple." |
| Services ID | `servicesId` | Hint: "The identifier of the Services ID, like com.example.signin. It is what other providers call the client ID. On Apple's site: Identifiers, Services IDs." |
| Team ID | `teamId` | Mono, ten characters. Hint: "Ten characters. On Apple's site: Membership details." Side by side with Key ID from `sm`. |
| Key ID | `keyId` | Mono, ten characters. Hint: "Ten characters. On Apple's site: Keys, then the key's name." |
| Private key (.p8) | `privateKey` | A textarea: mono, 6 rows, `autocomplete="off"`, `spellcheck="false"`, `autocapitalize="off"`, class `.um-textarea` (new: the input's surface, border, radius and focus, with its own height and padding). Hint: "Open the .p8 file in a text editor and paste all of it, including the BEGIN and END lines. Stored encrypted. It can't be shown again once saved, only replaced." |
| Shared box, allowed domains | | As the other panes. |

Once saved: a masked field labelled "Private key" showing dots alone (no
characters of the key), then the textarea labelled "Replace the private key"
with the hint "Leave blank to keep the saved key. A new key has a new Key ID:
change it above too."

**Refusals** redraw the form with status 422, `aria-invalid` and the message
under the field, every typed value kept except the key, which is never drawn
back:

| Field | Message |
| --- | --- |
| Services ID | "Enter the Services ID, like com.example.signin, with no spaces." |
| Team ID | "A Team ID is ten letters and digits, like DEF123GHIJ." |
| Key ID | "A Key ID is ten letters and digits, like ABC123DEFG." |
| Private key (missing on a new connector) | "Paste the .p8 key Apple let you download." |
| Private key (unusable) | "That is not a usable .p8 key. Paste the whole file again, including the BEGIN and END lines." |
| Display name | "Enter a name for this connector, then try again." (the other panes' message speaks of the sign-in button's label, which is fixed here) |
| Allowed domains | The existing messages. |

Refusals that are not about a field stay notices at the top of the pane: the
secret store refused the write (existing), the slug is taken (existing), and:

| Code | Text |
| --- | --- |
| `connector-saved-apple-unreachable` | "Saved, but not enabled: Umbral could not reach Apple (<reason>). Press Enable to try again." |
| `connector-enable-apple-unreachable` | "Not enabled: Umbral could not reach Apple (<reason>). Try again in a moment." |
| `connector-enable-apple-key` | "Not enabled: the stored key cannot sign. Paste the .p8 key again under Replace the private key." |

| `connector-enable-apple-store` | "Not enabled: Umbral could not read the key from the secret store (<store>: <cause>). Check the store, then press Enable again." |

Several fields can be refused at once; each shows its own message.

## The sign-in page

The Apple connector's button (designer's answer 6):

- Title fixed: "Sign in with Apple", whatever the connector's display name.
- Light theme: black fill, white title. Dark theme: white fill, black title. No
  outline.
- Never the app's accent colour: on a branded page the other buttons take the
  accent and this one stays black or white.
- Same height, width, pill radius and typeface as the other buttons, the same
  place in the connector order, hover as a slight opacity change, the usual
  focus ring.
- The "Last used" badge as on the others (it draws in `currentColor`).
- No link icon. A marked place for Apple's logo as inline SVG; empty in this
  change (research R8).
- One class, `.um-btn-apple`, with the two fixed colour pairs, and one sentence
  in `DESIGN.md`'s Buttons section recording it as the single exception to token
  colours, with the reason.

## Captures for the design review

The Apple pane not set up, set up, a refused Team ID and a refused key (422);
the picker with Apple among others in both themes; the picker on a branded page
with an accent in both themes; focus on the Apple button. All at 1280 and 360.
