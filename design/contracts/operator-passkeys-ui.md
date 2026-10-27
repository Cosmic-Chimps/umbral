# Operator passkeys: how the Credential screen and the operator sign-in look

Design answers for spec 011 (`specs/011-operator-passkeys/`), written 2026-10-27 by the design
reviewer for the implementer's six questions. Behaviour is the spec's; this file says what the
operator sees. Wording is proposed; `Views/Texts.fs` wins once it holds it. Patterns to reuse:
the person's own passkey page (`Handlers/Passkeys/Account.fs`), the Users card's passkey rows
(Remove as a red ghost, task grbb), the strand confirmations (a refused action redraws the page
with its confirm block), and DESIGN.md's One Red Fill and Contrast Floor rules.

## 1. The Credential screen

`/admin/credential`, operator chrome, `max-w-2xl` (today's `max-w-md` is too narrow for a
list). Page heading "Credential" with the lead below; notices under the heading as alerts.
Then **three cards, in this order**, each with an `h2` in headline medium and a lead sentence:

1. **Passkeys.** First because it is the way in the spec exists to add, and the switch below
   depends on it.
   - The list: one row per passkey, rows separated by 40% hairlines, no card-in-card. Row:
     the name (body medium at 500, bone); under it one muted label-medium line "Added
     2026-10-26 · Last used 2026-10-27" or "Added 2026-10-26 · Never used"; at the right,
     **Remove** as a small ghost in the error colour (`btnSm Ghost + " text-error
     hover:text-error"`), as on the Users card.
   - Under the list, the **add form**: the name field (optional, as on the account page),
     the re-check (section 2), and the primary **Add a passkey**. At the limit of 10, the form
     is replaced by one muted line.
   - Empty: one muted line above the form, no empty-state card.
2. **Password sign-in.** A state badge (the existing `stateBadge`, "On" / "Off") beside the
   title; one sentence saying what the state means; one small secondary button that changes
   it (section 3).
3. **Password.** Today's replace form (current, new, confirm, primary **Replace the
   password**). **Shown only while password sign-in is on** (FR-009). While off, the card is
   absent, not disabled; the Password sign-in card's sentence says the password is kept.

Red fills on this screen: at most one per card, and only in a confirm block (section 2).

## 2. The re-check: one pattern, no field on list rows

Every change asks for the current credential in the same action (FR-004). The shape:

- **While password sign-in is on**, a form that changes something carries a **Current
  password** field (`currentPasswordField`, as the replace form has today). Beside its submit
  button, when at least one passkey exists, a secondary small button **Use a passkey instead**
  that runs a ceremony and then submits the form with the passkey confirmation in place of
  the password. The field stays required when the button is not used.
- **While password sign-in is off**, there is no field anywhere. The form's own submit button
  runs the ceremony and then submits (as the account page's add form does through
  `passkeys.js`). The button's label does not change.
- **Remove on a row** carries no field. Pressing it posts the remove; the server, finding no
  credential, redraws the Credential screen with that row **expanded into a confirm block**
  (422), the way a strand warning redraws with its confirm: a line "Remove the passkey
  'Phone'?", the Current password field (while on), the small **danger** button **Remove
  passkey** (the card's one red fill, present only in this state), **Use a passkey instead**
  (while on and another passkey exists; while off, the Remove passkey button itself runs the
  ceremony), and a ghost **Cancel** link back to `/admin/credential`. Focus lands on the
  block's first control. The other rows stay as they are.
- **The switch** works the same way: its button posts; the server redraws the Password sign-in
  card with a confirm block. Turning **off**: a line saying what changes, the primary button
  **Confirm with a passkey** (a ceremony, the only accepted re-check, FR-007), Cancel. Turning
  **on**: the Current password is not available (it is off), so the same **Confirm with a
  passkey** button. Without JavaScript the confirm block shows the no-JavaScript line instead
  of the button.
- **Refusals** are alerts under the page heading, in the Warning kind, each ending in the next
  action (section 4). After a success, the plain screen with its Success alert.

Without JavaScript: every password path works unchanged; every button that would run a
ceremony is replaced by the no-JavaScript line, following `SignIn.javascriptNote` and the
`hidden`-until-scripted convention `passkeys.js` already uses.

## 3. The switch is a status and a button, not a toggle

A toggle promises an instant flip; this one takes a round trip, a ceremony and a confirm, so
it is a **state badge plus one button**. Button labels name the result: **Turn password
sign-in off** when on; **Turn password sign-in on** when off. Both small secondary, never the
red fill: turning password sign-in off is reversible and makes the account safer, not less so.
While no passkey exists the off button is disabled at 50% with a hint under it saying to add a
passkey first; the server refuses the post anyway (FR-007).

## 4. Wording

"Credential" stays the name of the screen and the rail entry: it now means the operator's
ways in as a set. Inside the screen, the password is called the password, and the
`reset-credential` command keeps its name.

Screen:
- Title: *Credential*. Lead: *How you sign in to the admin area: with your password, with a
  passkey, or either.*

Passkeys card:
- Title: *Passkeys*. Lead: *A passkey signs you in with your phone, laptop or security key,
  and cannot be phished. Up to 10.*
- Empty line: *No passkey yet.*
- Limit line: *This credential has 10 passkeys, the most it can hold. Remove one to add
  another.*
- Add form: field label *Name this passkey (optional)*, hint *So you can tell it apart later:
  "Phone", "YubiKey". Left empty, it is named by its date.*, button *Add a passkey*.
- Row: *Added {date}*, *Last used {date}*, *Never used*; Remove button *Remove*.
- Remove confirm block: *Remove the passkey "{name}"? It will not sign you in any more.*;
  buttons *Remove passkey*, *Use a passkey instead*, *Cancel*.
- Current password field label: *Current password*.
- Use-a-passkey button: *Use a passkey instead*. Confirm-with-passkey button: *Confirm with a
  passkey*.

Password sign-in card:
- Title: *Password sign-in*. Badge *On* / *Off*.
- Sentence when on: *You can sign in with your password or with a passkey.*
- Sentence when off: *Only a passkey signs you in. Your password is kept, and comes back
  unchanged when you turn this on.*
- Buttons: *Turn password sign-in off* / *Turn password sign-in on*.
- Hint under the disabled off button: *Add a passkey first.*
- Off confirm block: *With password sign-in off, only a passkey opens the admin area, on
  every browser. Keep a passkey somewhere safe. Confirm with a passkey to turn it off.*
- On confirm block: *Turning password sign-in on brings your password back as it was.
  Confirm with a passkey.*

Password card:
- Title: *Password*. Lead: *Replace the password you sign in with.* Fields as today; button
  *Replace the password*.

Refusals (Warning alerts):
- Switch off with no passkey: *Add a passkey before turning password sign-in off.*
- Remove the last passkey while off: *This is the only passkey, and password sign-in is off.
  Turn password sign-in on first, or add another passkey.*
- Wrong or missing re-check: *That is not the current password.* (the existing wording) and,
  for a failed ceremony: *The passkey did not confirm. Try again.*
- At the limit: the limit line above, as a notice.

Success notices:
- *Passkey added.* / *Passkey removed. Every other admin session was signed out.* /
  *Password sign-in is off. Every other admin session was signed out.* / *Password sign-in is
  on.* / *Password replaced.* (today's).

Operator sign-in page (`/admin/signin`):
- Title *Sign in* (today's). Lead with both: *Use your password, or a passkey.* Lead with a
  passkey only: *Sign in with your passkey.*
- Passkey button: *Sign in with a passkey* (the existing `SignInWithPasskey`).
- No-JavaScript line (passkey only, or under the password form when both): *A passkey needs
  JavaScript. Turn it on for this page, or sign in with your password.* and, when password
  sign-in is off, *A passkey needs JavaScript. Turn it on for this page.*

Authenticator: the credential's user name *operator* and display name *Umbral operator*; the
browser's picker also shows the RP (the issuer's host), which is what tells two Umbral
instances apart.

## 5. The operator sign-in page

Card `max-w-sm` as today. **With both**: the password form first (field, primary full-width
**Sign in**), then `.um-or` ("or"), then **Sign in with a passkey** as a full-width secondary
button with the Key icon. The password form keeps focus and the primary fill because it is
what every existing operator reaches for; the passkey button is one press away and needs no
focus. **With a passkey only**: the passkey button alone, primary, full width; no field, no
divider. The no-JavaScript line sits at the bottom of the card in both cases, in `noscript`.
Errors: the existing Failure alert above the form, same words for a wrong password and a
refused password while off (FR-008).

## 6. From DESIGN.md, the parts that matter here

- One Red Fill per card: Remove rows are ghosts in the error colour; the only red fill is the
  confirm block's Remove passkey, and only while that block is shown.
- Contrast Floor: the state badge and the error-colour ghost are existing pairs; nothing new
  to measure.
- Cards are the item containers; rows inside a card are separated by hairlines, never by
  nested cards.
- Notices under the page heading; a refusal is a Warning, a success a Success.
- Every form works without JavaScript; a ceremony button is hidden until scripted and a
  `noscript` line says why.
- Focus after a 422 redraw goes to the confirm block's first control; the page title is
  "Credential | Umbral" throughout.

## 7. Captures for review

In `.alta/reviews/2026-10-24-operator-passkeys/`, `<state>-<width>-<theme>.png` at 1280 and
360, both themes: `credential-no-passkey`, `credential-with-passkeys` (three rows, one never
used), `add-passkey` (the form with the Current password field and Use a passkey instead),
`remove-confirm` (a row expanded), `switch-off-confirm`, `password-off` (two cards, no password
anywhere), `switch-on-confirm`, `remove-last-refused`, `signin-both`, `signin-passkey-only`,
`signin-no-js` (1280 only), `focus-confirm` (1280 dark only: focus in the confirm block).
Plus `notes.md` with every new string and the detector output if the CLI is at hand.
