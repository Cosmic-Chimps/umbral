# Design review: the rows on Your passkeys

Task ypr0. Branch `alta/hopeful-etna-g28k`, `8f5ec87`. Reviewed 2026-11-16 by the design
reviewer from one of the four captures (`your-passkeys-1280-light`). The retaken docs images
were not opened.

## Verdict

**Clear to merge. No blocking items; two nits, each one class.**

- The dates are one line in the agreed form, for a used and a never-used passkey.
- Remove is a ghost in the error colour behind its confirm; nothing on the page is a red fill.

## Nits

1. **The date line is semibold.** `text-label-md` carries weight 600, so the line reads as
   heavy as a label. Add `font-normal`, as hints do, so it reads as the row's detail.
2. **Your point, the inset Remove.** Leaving it under the dates is right: on this page each
   passkey is its own card, not a row with a right edge. Pull the button back by its own
   padding (`-ml-3.5`) so the word "Remove" starts on the text column.

No re-check needed.
