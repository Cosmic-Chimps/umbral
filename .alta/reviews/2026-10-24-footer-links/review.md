# Review: footer links to the source and the documentation; the home page's issuer URL

Task: `.alta/tasks/2026-10-24-footer-links-to-the-source-and-docs-rp0l.md`
Branch: `alta/chipper-waltz-z7fp`, `a4775be` (the change) and `32ab4af` (notes, captures).
Reviewed 2026-10-26 by the design reviewer from the diff, the detector, a test run and the 14
captures, against the answers given on 2026-10-26.

## Verdict

**Clear to merge. No blocking items, no nits.**

## What was checked

- Footer: a `nav` labelled "About Umbral" after the line, in the same left group, 16px gaps,
  wrapping under the line at 360 (seen on home and admin); two plain anchors to the two
  constants, same tab; `.um-footer-link` in the footer's own colour, always underlined with the
  decoration at 40% of that colour, full decoration and bone on hover and focus-visible, with
  the colour transition the other controls have. Not gold. The decoration token instead of
  `decoration-current/40` is the right workaround for Tailwind 3.4.
- Branded sign-in: no links, "Secured by Umbral" and the theme control alone
  (`signin-branded`), with the reason in `notes.md` for the maintainer and the sentence added to
  DESIGN.md's Layout paragraph as given.
- Home card: `Components.copyText` with the label, the value as wrapping mono text with an id,
  and a Copy button under it carrying `data-copy-value` and the accessible name "Copy the
  issuer URL"; `umbral.js` prefers the attribute and falls back to the input as before, so
  every existing copy field is unchanged. The whole issuer shows at 1280 and 360. The Copy
  button is the full-size secondary rather than the small one I named; next to the card's
  full-size discovery-document button that is the consistent choice, so it stands.
- Strings as given: FooterSource, FooterDocs, FooterAboutLabel, CopyIssuerUrl.
- The "social login" wording in the footer line is queued separately, as agreed.
- Tests, stylesheet, detector: see the last section.

## Tests, stylesheet, detector

Run by the reviewer on the branch's worktree at `32ab4af`, 2026-10-26:
`dotnet test` with the Footer, Home, Rail, Picker and Branding filters: 125 passed, 0 failed.
`build-css.sh --check`: clean, `umbral.css` is current.
Impeccable detector on `umbral.input.css`: one advisory, the pre-existing crop-shade colour;
nothing from `.um-footer-link`.
