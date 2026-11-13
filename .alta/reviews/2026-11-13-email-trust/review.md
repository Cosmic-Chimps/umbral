# Design review: trust in a provider's verified email addresses (spec 017)

Task tr5t. Branch `alta/chipper-duet-8b42` at `c971183`. Contract:
`specs/017-oidc-email-trust/contracts/screens.md`. Reviewed 2026-11-13 by the design reviewer
from four of the 41 captures (`add-refused-1280-dark`, `pane-confirm-off-360-light`,
`pane-instance-off-1280-light`, `add-discord-1280-light`). What trust off does to sign-ins and
to stored addresses is the spec's and the code review's.

## Verdict

**Not yet clear: one blocking item, one nit.**

## What is right

- The group sits before Allowed email domains on the add form and on the pane: the legend, the
  lead in plain text, two cards side by side from 768px and stacked at 360, in the words given.
- Add with no choice: refused at 422 with the message under the group, typed values kept, the
  secret not carried and its field saying so.
- The pane's hint under the group, with the sentence about stored addresses.
- The instance list with trust off: the list entry takes the attention marker in place of the
  state badge, the Warning alert sits under the heading, and the Set-up rule line says the same.
- The confirm step: the count line as a Warning alert under the heading, a small Danger **Turn
  trust off** and a ghost Cancel, the form kept below. The red fill is right here: turning
  trust back on does not undo it.

## Your three points

1. **Your sentences**: accepted as written. The instance-case rule line, the count line for
   several, for one, and for none ("Turning trust off makes no stored email address
   unverified. Nobody is unlinked.").
2. **The group on the add form for Discord, GitHub and Google**: the blocking item below.
3. **The confirm block's place and buttons**: right.

## Blocking

1. **The add form shows the trust choice for kinds it does nothing for, without saying so.**
   In `add-discord-1280-light` an operator adding a second Discord connector is asked whether
   to trust its verified addresses, and whatever they pick is ignored. Keep the group drawn for
   every kind, as the Issuer URL field is (no script, no split form), and say it the way that
   field does: start the lead with *"Only for an OpenID Connect provider."*, so it reads "Only
   for an OpenID Connect provider. Some providers mark an address as verified without checking
   it. …". On the existing OpenID Connect pane the lead stays as it is.

## Nit

2. **The lead's spacing.** There is a wide gap between the legend and the lead and almost none
   between the lead and the cards, so the lead reads as attached to nothing. Legend, then the
   lead 6px under it as a hint, then the cards 12px under the lead.

## Re-check

`add-discord` and `add-nochoice` at 1280 in one theme.

## Re-check, 2026-11-14, at `02aabcc`

**Clear to merge.** Looked at `add-discord-1280-light`; the other retakes were not opened.

- Blocking item fixed: the add form's lead begins "Only for an OpenID Connect provider.", so an
  operator adding a Discord, GitHub or Google connector is told the choice is not for them.
- Nit fixed: the lead sits close under the legend with space before the cards, and reads as the
  group's own.
- The extra sentence from the code review, on the confirm line when a new client secret was
  typed ("The new client secret was not saved: type it again after you turn trust off."): right.
  It says what was lost and when to redo it. Accepted from the description, not captured.
