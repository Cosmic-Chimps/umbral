# Code review: every page links to the source and the documentation (task rp0l)

Branch `alta/chipper-waltz-z7fp`, `063b163..32ab4af` (the code is `a4775be`). Reviewer: the `umbral` session, 2026-10-26.

## Verdict

**Clear to merge.** No blocking items; one nit, no re-check needed.

## What was checked

- **The links take no input.** `RepositoryUrl` and `DocumentationUrl` are `[<Literal>]` constants in `Texts.fs`, and `footerLine` renders two plain `<a href>` from them, opening in the same tab. Nothing from the request reaches them.
- **Where they appear.** The footer line is on the Plain and Operator chrome only. The branded `SignIn` chrome is unchanged, so an app's sign-in page keeps "Secured by Umbral" alone. That matches `DESIGN.md`'s new line, and `FooterTests` covers it.
- **The issuer URL.** `Components.copyText` shows it as HTML-encoded text that wraps (`overflow-wrap:anywhere`), with a Copy button carrying the same value in `data-copy-value` (attribute-encoded by the view engine). It's the same configured public origin the old input held, so nothing new is exposed. The button has an accessible name that says what it copies.
- **`umbral.js`.** The copy handler prefers `data-copy-value` (`getAttribute` returns `null` when it's absent) and otherwise falls back to the neighbouring input as before. So every existing `copyField` behaves the same, and it uses no `innerHTML`.
- **The two narrowed tests.**
  1. `SignInBrandingTests.assertInvariants` allowing exactly the two project URLs as hrefs is right. SC-005 is about what a page fetches when it opens, and an `<a href>` is never fetched. `PageAssetsTests`' resource check, which is what guards SC-005, is unchanged, and the allowance is limited to the two constants, not to any external address.
  2. `ThemeTests` leaving `nav[data-footer-links]` out of its one-landmark count is right. The test is about the theme control not being a `<nav>`, and the footer's link group is a separate, labelled landmark ("About Umbral").
- **Tests run by the reviewer:** `build-css.sh --check` is clean, and a full `dotnet test` at `32ab4af` passes 1712 of 1712, 0 failed, 0 skipped. The branch merges onto `main` (`063b163`) without conflicts.

## Nit (no re-check needed)

- **N1. Referrer.** Umbral sets no site-wide `Referrer-Policy`. Browsers default to `strict-origin-when-cross-origin`, so following a footer link from an admin page tells GitHub the instance's origin (for example, `https://auth.example.com`), though not the path. That is small and common, but cheap to avoid: add `rel="noreferrer"` to the two footer links, or set `Referrer-Policy: strict-origin-when-cross-origin` or `same-origin` for the whole site. The second would also cover any future outbound link, and it's already what the passkey invitation page does locally (`no-referrer`). Take it here or leave it for a later task.
