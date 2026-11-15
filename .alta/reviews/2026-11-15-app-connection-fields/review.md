# Design review: Client ID and Issuer URL, one per row

Task 3694. Branch `alta/rare-prelude-mgc9`, `406b8b0`, stacked on v6pm. Reviewed 2026-11-15 by
the design reviewer from two of the four captures (`pane-1280-dark`, `pane-360-light`). The
docs images were not opened.

## Verdict

**Clear to merge. No blocking items; one nit. One follow-up for the queue.**

- At 1280 each value shows whole in its own row, with its Copy at the right edge, aligned with
  the secret's field above. The pane reads as a column of fields, which it is.
- At 360 nothing changes, as expected.
- The contract's item 3, amended to "one per row" with the date and the reason: confirmed as
  written.

## Your two questions

1. **A real issuer cut at 360**: acceptable for this change, and not the end state. These two
   values are only read and copied, so they should be `Components.copyText` (wrapping mono text
   with Copy under it), as the home page's issuer and the Bluesky pane's metadata address are.
   That shows any value whole at every width. Follow-up for the queue, since the docs image
   `app-created` changes with it.
2. **Rhythm**: nit 1.

## Nit

1. **Group the pair.** Client ID and Issuer URL are two fields of one group; the redirect lists
   are the next group. Put the pair 12px apart (`space-y-3` inside one wrapper), and keep 20px
   between the pair and the lists.

No re-check needed.
