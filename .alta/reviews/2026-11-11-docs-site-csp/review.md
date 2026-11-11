# Design review: the docs site under its Content-Security-Policy

Task fqjn. Branch `alta/shiny-cadence-bmk8`, `d6d2373..59679a8`. Reviewed 2026-11-11 by the
design reviewer from the diff's file list and the captures, against the cleared look in
`.alta/reviews/2026-11-05-docs-site/`.

## Verdict

**Clear. No blocking items, no nits.**

## What was checked

- The change touches `site/astro.config.mjs`, `site/scripts/check-dist.mjs` and
  `site/README.md` only. No stylesheet, token, asset or markup changed, so nothing should look
  different, and nothing does.
- `start-1280-dark`: the three faces load (display title, text body, lowercase wordmark), the
  Gnomon in the header, the sidebar's current entry as a tonal step with gold text, the icons
  drawn (search, GitHub, theme select, carets).
- `search-1280-light`: the dialog, the result icons, and the search field's gold border and
  gold focus ring.
- `menu-360-dark`: the open menu with its close icon, the current entry, the group carets.
- `run-https-360-light`: inline code, the code block with Starlight's theme and its frame,
  links underlined in the light theme's gold.

I looked at four of the fourteen captures, one per kind of state. The policy itself, the
script that checks the built pages, and the three-browser walk are the code review's.
