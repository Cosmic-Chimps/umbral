# Footer links to the source and the documentation; the home page's issuer URL: notes for review

Task `2026-10-24-footer-links-to-the-source-and-docs-rp0l`.
Branch `alta/chipper-waltz-z7fp` (worktree `~/.alta/worktrees/umbral/chipper-waltz-z7fp`),
on top of `main` at `063b163` (the current tip, so there was nothing to merge in).

**Commits.** `063b163..HEAD`: `a4775be` has the change, tests and docs, and the next commit has these notes and
the captures. The reviewers' files follow.

The placement, the style and the wording are `umbral-designer`'s answer of 2026-10-26.
The Fediverse task this one was ordered against (`alta/earnest-cetus-9pd3`) is already merged into
`main`, so the `Texts.fs` additions sit on top of it.

## What changed

- **One address.** `Texts.RepositoryUrl` = `https://github.com/Cosmic-Chimps/umbral` and
  `Texts.DocumentationUrl` = `RepositoryUrl + "#readme"`. Both are `[<Literal>]` values at the top of
  `src/Umbral/Views/Texts.fs`. They are constants, not settings, and the docs audit's T6 (E14) can use
  the same constant.
- **Footer** (`src/Umbral/Views/Layout.fs`): `footerLine` is shared by the Plain and Operator
  chrome. It holds the mark and `t.Footer`, then a `nav aria-label="About Umbral"`
  (`data-footer-links`) holding two plain `<a href>` to the constants. They open in the same tab, with
  no `target` and no `rel`. `gap-x-4 flex-wrap`, so at 360 px the nav wraps under the line as a second
  row. The links use the new `.um-footer-link` class, described under CSS below.
- **Branded sign-in: no links.** The `SignIn` chrome is unchanged: "Secured by Umbral" stays alone,
  as the designer decided. That line is how a person tells the real page from a lookalike, and a link
  out would pull them off the app's page in the middle of signing in. The unbranded sign-in page uses
  the Plain chrome, so it has the links. That page is "the sign-in page" in the task's tests.
  **Maintainer: this is a deliberate exception to "every page".**
- **Issuer URL** (`src/Umbral/Handlers/Home.fs`): the card now uses the new `Components.copyText`
  instead of `copyField`. The label is `p.um-label`, the value is `p#home-issuer.um-mono` with
  `[overflow-wrap:anywhere]`, and under them is the secondary Copy button. The button carries
  `data-copy-value` and `aria-label="Copy the issuer URL"`. `copyField` is unchanged everywhere else.
- **Copy script** (`src/Umbral/Assets/umbral.js`): a `[data-copy-trigger]` with `data-copy-value`
  copies that value. Without it, the button copies the neighbouring `<input>` as before.
- **DESIGN.md** (Layout): the footer holds "the small Gnomon, a line of text, two quiet links to the
  source and the documentation (never on a branded sign-in page), and the theme control".

## Texts.fs additions

| Field | Value |
| --- | --- |
| `FooterSource` | "Source on GitHub" |
| `FooterDocs` | "Documentation" |
| `FooterAboutLabel` | "About Umbral" (the footer nav's name) |
| `CopyIssuerUrl` | "Copy the issuer URL" (the home Copy button's accessible name) |

## CSS

`umbral.input.css` gets
`.um-footer-link { @apply underline decoration-on-surface-variant/40 hover:text-on-surface hover:decoration-on-surface focus-visible:text-on-surface focus-visible:decoration-on-surface; }`
and is added to the reduced-motion transition list. The text colour is inherited from the footer
(`text-on-surface-variant`), so it is not gold. Tailwind 3.4 cannot apply an opacity to
`decoration-current`, so the decoration uses the token. `src/Umbral/Assets/build-css.sh` was run and
`umbral.css` is committed.

## Captures

The instance ran on `http://localhost:18080` with `UMBRAL__PUBLIC_ORIGIN=https://auth.lacalabaza.com`.
Its data was a GitHub and a Google connector, plus two apps: Photo Album, unbranded, and Recipe Box,
with accent `#ff6f61`. The picker was opened directly at `/signin?returnUrl=<authorize URL>`, because
OpenIddict refuses a plain-http authorize request.

`home`, `signin` and `admin` (`/admin/apps`) at 1280 and 360, dark and light; `signin-branded` at 1280.

The metrics were measured in the browser for every capture:
- No horizontal overflow.
- Both links have `href` equal to the constants, no `target`, `text-decoration-line: underline`, and
  the footer's own colour. Dark: `rgb(167,162,182)`, decoration `rgba(167,162,182,0.4)`. Light:
  `rgb(95,90,107)`, decoration at 0.4.
- At 360 the nav wraps under the line, and at 1280 it sits on the same row.
- `signin-branded` has no footer links.
- The home page shows `#home-issuer` as `https://auth.lacalabaza.com` in full, with no clipping
  (`scrollWidth <= clientWidth`).

## Tests

`tests/Umbral.Tests/FooterTests.fs`:
- ``the repository and documentation addresses are the project's``
- ``the home page's footer links to the source and the documentation``
- ``the sign-in page's footer links to the source and the documentation`` (the picker of an unbranded app)
- ``the operator's sign-in page's footer links to the source and the documentation``
- ``an admin page's footer links to the source and the documentation`` (`/admin/apps`)
- ``an app's branded sign-in page keeps "Secured by Umbral" alone``
- ``the home page shows the whole issuer URL as text, with Copy carrying it``

Each link test checks the exact anchor markup: `<a href="<constant>" class="um-footer-link">…</a>`,
exactly two anchors in the nav, and no `target=`. `HomeTests` pass unchanged.

The first full run failed 9 tests, and two existing tests were narrowed. **Reviewers, please check
both:**
- `SignInBrandingTests.assertInvariants` (8 failures): it held every `href` and `src` on a sign-in
  page to this origin. SC-005 of spec 006 is "opening a sign-in page … makes no request to any origin
  other than Umbral's own". An `<a href>` is followed, not fetched, so the footer links make no request.
  The check now also allows exactly `Texts.RepositoryUrl` and `Texts.DocumentationUrl`. Any other
  outside address still fails. `PageAssetsTests.externalOrInlineViolations`, which checks the tags
  that load something (script, link, img…), is unchanged and passes.
- `ThemeTests.the control is on the operator sign-in page and the operator's rail` (1 failure): it
  counted every `<nav>` on an operator page and expected one. It now leaves out the footer's
  `nav[data-footer-links]`, so it still checks what it means to check: the theme control is not a
  second navigation.

Full suite at `a4775be`: 1712 passed, 0 failed, 0 skipped (`dotnet test tests/Umbral.Tests`, 6 min 45 s).

## CHANGELOG

Under `## Unreleased`:
- Added: "Every page links to the source and the documentation: the footer has **Source on GitHub**
  and **Documentation**, except on an app's branded sign-in page, where **Secured by Umbral** stays alone."
- Changed: "The home page's **Building an app?** card shows the whole issuer URL, wrapping if it must,
  with **Copy** under it, instead of a field too narrow to show more than its beginning."

## Impeccable detector

The implementer could not run it: there is no CLI on this machine. The design reviewer ran it on
2026-10-26 and got only the pre-existing `design-system-color` advisory on the logo crop's shade in
`umbral.input.css`, which is not part of this change.

## Reviews

- Design: `umbral-designer`, `review.md`: **clear at `32ab4af`, no blocking items, no nits** (2026-10-26). Nothing to answer.
- Code: `umbral`, `code-review.md` (pending).
