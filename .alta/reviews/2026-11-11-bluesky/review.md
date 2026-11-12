# Design review: the Bluesky connector (spec 015)

Task b5ky. Branch `alta/snowy-eagle-vdg4`, `df32802..ddcf64d`. Note:
`design/contracts/bluesky-connector-ui.md`. Reviewed 2026-11-12 by the design reviewer from
the design-review notes and four of the 49 captures (`connectors-any-https-1280-dark`,
`connectors-origin-blocked-360-light`, `step-not-found-360-dark`, `picker-1280-light`). The
states not opened follow the Fediverse pane and step, reviewed with spec 010.

## Verdict

**Clear to merge. No blocking items; two nits, both from what your walk noticed.**

## What was checked

- **The pane**: entry after Fediverse and before Passkey; heading with the slug and pills;
  Set-up with the lead, the one address (Client metadata address, with copy and its hint) and
  the no-email note; Settings with the shared check, the scope as radio cards, Servers with
  its hint, the primary Save; the closing row with Disable and Delete.
- **An address that cannot work**: lead and the Warning alert, no Settings form, no closing
  row, the entry "Not set up" with no marker. The alert names the instance's address.
- **The picker**: Bluesky with the link icon beside Fediverse's; the two are told apart by
  their labels. The mark decision and its reason are recorded.
- **The step**: the heading, the Handle field with its hint, a full-width Continue, the back
  link; a handle with no account redrawn in place with the value kept, the field marked and
  the message under the hint, in the note's words. Nothing overflows at 360 with a long handle.

## What differs from the note

1. The extra texts (the empty-list note and the three notices): accepted as written.
2. An existing connector on an address that stopped working keeps its sections under the
   alert, with Enable refused: right. Hiding Disable and Delete there would trap the operator.
3. The development note in place of the address: fine; it is true and only seen in
   development.
5. The three states with no capture: accepted; their texts are in `captures.md`.

## Nits

1. **The address is cut off before `.json`.** It is the one thing this section shows, and
   nothing is done with it but read and copy. Use `Components.copyText` (the home page's
   issuer URL: the value as wrapping mono text with Copy under it) instead of the input, so
   the whole address shows at every width.
2. **The `bsky.social` placeholder reads as a typed value.** Under Any server in the dark
   theme it looks like a saved list of one. The hint already names `bsky.social`, so drop the
   placeholder on this field. The Fediverse pane has the same pattern; that one is a separate
   small task (move its example into the hint), which I will pass to the queue.

If you take them, send `connectors-any-https-1280-dark` again; no further re-check.
