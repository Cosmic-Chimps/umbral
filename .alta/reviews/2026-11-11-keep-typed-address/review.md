# Design review: keep the typed address after an add is refused as invalid

Task k3ep. Branch `alta/jaunty-rainbow-4jk9`, `ad3cef8..b9c066c`, captures at `357350e`.
Reviewed 2026-11-11 by the design reviewer from two captures (`refused-invalid-1280-dark`,
`refused-invalid-post-logout-360-light`), the notice text and the diff's file list. The
cookie, its escaping and its size bound are the code review's.

## Verdict

**Clear to merge as built. No blocking items; two nits, of which the first answers your
question and is worth taking, here or as the next task.**

## What was checked

- The typed address comes back in the add field of the list it was typed in, and only there;
  the other list's field stays empty. The field carries `aria-invalid` and the existing red
  border; no new class. Seen for the redirect list at 1280 dark and the post-logout list at
  360 light.
- The notice is the pane's Warning alert under the heading, as every refusal.
- No focus script, consistent with the rest of the pane.

## Nits

1. **Your question: yes, show the reason, under the field.** The second capture is the case
   for it: the address was refused for a fragment, and the notice says "Enter an absolute URL
   with no wildcards", which is not what is wrong. The field is red with nothing beside it to
   say why. Carry a **reason code** in the cookie (not the sentence, so there is nothing to
   escape and nothing to bound) and draw a `um-field-error` under the field, tied to the input
   with `aria-describedby` beside its hint. The sentences `RedirectUris.acceptable` returns
   quote the whole value, which would repeat a long address under the field that already shows
   it, so give the pane its own short strings in `Texts.fs`:
   - wildcard: *This address has a wildcard. Addresses are matched exactly, so list each one.*
   - not absolute: *This is not a full address. Include the scheme and host, for example
     https://app.example.com/callback.*
   - no host: *This address has no host.*
   - fragment: *This address has a fragment (the part after #), which is never sent back.
     Remove it.*

   The notice at the top stays as the general statement. Captures if taken: `refused-invalid`
   with a wildcard and `refused-invalid-post-logout` with a fragment, 1280 and 360, both themes.
2. **"…then save again."** The notice ends with the create form's verb, and here the button
   is Add. The string is shared with the create form, so do not fork it: end it with "then try
   again.", which is true in both places. Catalogue only.

If nit 1 goes to a task of its own, say so and this one merges as it is.
