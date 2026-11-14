# Design review: Remove link on the Users screen (spec 018)

Branch `alta/quiet-memphis-783n` at `dfba0fd`. Contract:
`specs/018-remove-a-link/contracts/screens.md`. Reviewed 2026-11-14 by the design reviewer from
four of the 41 captures (`card-two-links-passkey-1280-dark`, `block-both-360-light`,
`block-focus-1280-dark`, `notice-last-1280-light`). What the removal does to links, passkeys
and sessions is the spec's and the code review's.

## Verdict

**Clear to merge. No blocking items; two nits.**

## What was checked

- The card: a "Linked accounts" list above a "Passkeys" list, each with its small label; a link
  row has the provider's label, "Linked … · Last used …" and a red ghost **Remove link**. A
  card with one way in shows no list. Two links of one provider are told apart by their dates.
- The confirm step: the row expands in place into a block with the sentence, the passkeys
  removed too, the passkeys to check, "This cannot be undone.", a small Danger **Remove link**
  and a ghost Cancel; focus lands on the block. It wraps cleanly at 360 with both lists.
- The last-way-in refusal is a Warning alert at the top of the page, naming the person.
- Delete stays the card's own red fill; the row buttons are ghosts.

## Your three points

1. **The block's red border**: nit 1 below.
2. **Two red fills while the block is open**: fine. It is the confirm step's button, shown only
   in that state, as on the Credential screen and the trust switch.
3. **No "Never used" on link rows**: fine. A link is made by a sign-in, so it always has one.

## Nits

1. **Drop the red border; give the block a tone.** The other confirm blocks are plain, and a
   red outline plus a red button plus the card's Delete is one red too many. Inside a card the
   block does need an edge, so use the preview panel's treatment: `rounded-md
   bg-surface-container p-4`, no border. The focus ring stays as it is.
2. **The two lists write their dates differently.** Link rows read "Linked 2026-06-02 · Last
   used 2026-11-13"; passkey rows on the same card read "Added: 2026-07-15  Last used: Never".
   Bring the passkey rows to the same form, "Added 2026-07-15 · Never used", as the Credential
   screen's passkey rows already have. Catalogue and one view line; if it reaches beyond this
   task, say so and I pass it to the queue.

## Re-check

`block-both` at 1280 in one theme and `card-two-links-passkey` at 360 in one theme, if the nits
are taken.

## Re-check, 2026-11-14, at `eb38935`

**Clear to merge.** Looked at `block-both-1280-light` and `card-two-links-passkey-360-light`.

- Nit 1 taken: the confirm block is a tonal panel with no red border. The outline in the
  capture is the focus ring on the block, which is right.
- Nit 2 taken: the passkey rows read "Added 2026-07-15 · Never used", in the same form as the
  link rows above them.
