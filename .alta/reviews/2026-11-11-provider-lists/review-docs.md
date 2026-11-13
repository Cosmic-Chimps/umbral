# Documentation review: name every provider wherever the providers are listed (task l1st)

Branch `alta/emerald-anthem-tqws`, commit bda34fe on local main 0bc654d. Reviewed 2026-11-13 by the
documentation reviewer.

## Verdict

**Clear for the documentation, no blocking items and no nits.** There is one condition on the
merge, about the base this branch sits on, not about its content (see "The base").

## What was checked

- **`README.md:6`**: the opening sentence now names Bluesky, like the Connector row, the connector
  links, the quick start and the Connectors section. Right.
- **The lists left as they are** (the table in `notes.md`): I agree with every row. Each is either an
  example ("and so on", "or whichever"), an argument about the audience (`vision.md`), a list of only
  the kinds that need an OAuth app (`PRODUCT.md:69-71`, `oidc.md:10`), the add form's three addable
  kinds (`addableKinds`), or a released changelog section. None should change.
- **`HomeLead`**: no page in `docs/` or the README quotes the old sentence, so nothing else needs to
  follow it. The wording is for umbral-designer. As documentation, "any OpenID Connect provider"
  covers the forges, and the list matches the Connectors screen's order.
- **A changelog fragment for `HomeLead`: not wanted.** It corrects the home page's wording, with no
  behaviour and nothing for an operator to do. The fragments are for what people who run Umbral
  need to know.
- `check-links.py`: 393 links in 38 files, all resolve. `changelog.sh check`: in order.

## The base

This branch was made from local `main` at 0bc654d. That main contains the f0rg branch merged with
its whole history, and that history holds material the f0rg documentation review asked to keep out
of the public repository until the maintainer decides on a report to Forgejo (see that review's B3
and its re-check). GitHub does not have those commits (checked 2026-11-13). Do not push this branch
or local main as they are. Once the maintainer has put f0rg on main as one squashed commit, rebase
this one commit (bda34fe) onto that main. The content above does not change.
