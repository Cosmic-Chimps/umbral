# Review: "passwordless sign-in" in the footer line and the home lead

Task: kk8w, the follow-up from rp0l. Branch: `alta/earnest-puffin-5z88`, `9736001` (the two
strings and the changelog bullet) and `aa6039e` (notes, captures), on `main` at `063b163`.
Reviewed 2026-10-26 by the design reviewer from the diff, a test run and the 12 captures.

## Verdict

**Clear to merge. No blocking items, no nits.**

## What was checked

- Both strings verbatim as given on 2026-10-26: the footer "Umbral — self-hosted
  passwordless sign-in"; the home lead in two sentences naming a passkey, Discord, GitHub,
  Google, a Mastodon-compatible server and any OpenID Connect provider, in the Connectors
  screen's order. Texts.fs only; no markup, no stylesheet.
- Captures at 1280 and 360, both themes, home, unbranded sign-in and admin: the footer line
  holds on one line at 360, the lead wraps in the page's measure at 1280 (three lines) and reads
  at 360, nothing overflows. The branded sign-in page is unaffected (it says "Secured by
  Umbral").
- The scope words now match PRODUCT.md and constitution 3.0.0 on the two pages a visitor reads
  first.
- Merge note: rp0l (`alta/chipper-waltz-z7fp`) adds footer links beside this same line and is
  cleared; the two touch neighbouring parts of `Texts.fs` and `Layout.fs`, so the second to
  land rebases, trivially.
- Tests and stylesheet: see the last section.

## Tests and stylesheet

Run by the reviewer on the branch's worktree at `aa6039e`, 2026-10-26:
`dotnet test` with the Home, Theme and Picker filters: 30 passed, 0 failed (the implementer
reports the full suite running).
`build-css.sh --check`: clean, `umbral.css` untouched by this branch.
