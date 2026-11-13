# Design review: the home page's lead names every connector kind

Task l1st. Branch `alta/emerald-anthem-tqws`, `bda34fe`. Reviewed 2026-11-13 by the design
reviewer from the sentence itself and one capture (`home-360-dark`). The README's sentence is
the documentation review's.

## Verdict

**Clear to merge. One nit, which I would take.**

- Naming Apple, Microsoft and Bluesky together, so the sentence is not behind again: right.
- "a Mastodon-compatible server" for the Fediverse: right.
- The paragraph holds at 360 in six lines with no overflow; no layout change.

## Nit

1. **Put the catch-all last.** I asked for the Connectors screen's order, and that is right
   for the named providers. But "any OpenID Connect provider" is not one more name: it is the
   "and anything else" of the sentence, and in the middle of the list it makes the two after it
   read as afterthoughts. Keep the screen's order for the named ones and end on the catch-all:

   *"… a passkey, or an account people already have at Discord, GitHub, Google, Apple,
   Microsoft, a Mastodon-compatible server, Bluesky or any OpenID Connect provider."*

   The list on the Connectors screen keeps its own order; this is prose. If the README's
   sentence follows the same list, the documentation reviewer may want the same ending.

No re-check needed.
