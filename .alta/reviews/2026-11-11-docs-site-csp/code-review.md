# Code review: the docs site's Content-Security-Policy (task fqjn)

Branch `alta/shiny-cadence-bmk8`, `d6d2373..59679a8` (the change is `de283e3`). Reviewer: the `umbral` session, 2026-11-11. I read the diff, built the site from `git archive` on Node 22.23.4, altered copies of `dist/` against the new check, and made and built the merge with the unmerged link-preview branch. The browser walks (Chrome, Firefox, WebKit) are yours and the designer's to read; I didn't repeat them.

## Verdict

**One blocking item, which is a merge, not a defect; one nit.** The policy and the check are right. The blocking item is that this branch conflicts with the link preview (`bbc6b04` on `alta/roaming-foxglove-m2q5`, cleared, not yet on `main`). The obvious resolution loses one of the two features without any error.

## What was checked

- **The build.** It passes: 29 of 29 HTML files carry the element, and check-dist is clean. In each page the policy is the third element of `<head>`, after the charset and the viewport and before the title, every link, the first `<script>` and the first stylesheet.
- **The new check, tried against altered copies of `dist/`.**
  - Refused: `blob:` added to `img-src`, `https:` added to `default-src` (two problems: the source, and "does not start from default-src 'self'"), and an external `modulepreload` before the policy (by the existing host rule).
  - Passed: a same-origin `preload` before the policy. That's fine, since the policy would allow it anyway, and an external one is caught by the host rule.
  - Passed: `'unsafe-eval'` (see N1).

## Your four questions

1. **The policy: clear.** `default-src 'self'` with each exception commented with what it's for. `base-uri 'none'` and `form-action 'self'` are right to add: `default-src` covers neither, and a meta-delivered policy honours both. (It ignores `frame-ancestors`, `report-uri` and `sandbox`, which you rightly didn't use.) `'unsafe-inline'` weakens the policy against injected script, but that isn't what it's for here: the pages are built from this repository's Markdown, and the goal is "no other host", which `script-src 'self'` and the default `connect-src` keep even for inline code.
2. **`'wasm-unsafe-eval'`: keep it.** It lets the page compile WebAssembly; it opens no host. Without it, search breaks in exactly the browsers where Pagefind's worker can't start, which are the hardest for a reader to diagnose.
3. **The check in check-dist: keep it.** It's what keeps the policy true after a later edit or a Starlight upgrade, the same reason the host check exists. It's small and reads well.
4. **The worker limit: accepted, as documented.** A worker gets its policy from its own response, and Pages sends none, so Firefox and WebKit let a worker fetch elsewhere. `check-dist.mjs` still scans `pagefind-worker.js` at build time (the host pattern and the URL list), which covers what the browser can't.

## Blocking

- **B1. Merge the link preview into this branch, resolved as below, before this one merges.** `git merge-tree` between this branch and `alta/roaming-foxglove-m2q5` reports conflicts in `site/astro.config.mjs` and `site/scripts/check-dist.mjs`. Both branches add a `head:` key to the same object literal and a function before `checkHtml`. The trap is in `astro.config.mjs`: keeping both sides of the conflict leaves **two `head:` keys**, which JavaScript accepts with no error, keeping only the last. Either the policy or the preview tags would then disappear from every page, and only the check that happens to still run would notice. The resolution I built and checked:
  - **`astro.config.mjs`:** keep both constants (`contentSecurityPolicy` and `linkPreview`), and one key:

    ```js
    head: [{ tag: 'meta', attrs: { 'http-equiv': 'Content-Security-Policy', content: contentSecurityPolicy } }, ...linkPreview],
    ```

    The policy goes first. Update the header comment, whose two versions each say "one `head` entry".
  - **`check-dist.mjs`:** keep both functions whole. Git splits the conflict at their shared closing braces, so close `checkPreviewImage` before `POLICY_SOURCE` begins. Both calls in `checkHtml` merge cleanly.
  - **`site/README.md`** merges without a conflict, but, as your notes at `69cee1e` say ("Merging with the link preview"), its two sentences that call the policy the site's one `head` entry are then wrong. Reword them in the same merge.

  Built from that resolution: the build passes, check-dist is clean, and each page has the policy first, then the PNG icon and `og:image`, then the scripts and stylesheets. The link preview is cleared by all its reviews, so merging it into this branch now gives the maintainer one clean merge. If the maintainer merges the link preview to `main` first, merge `main` here instead, with the same resolution.

## Nit

- **N1. List the keywords the check allows, instead of accepting anything in quotes.** `POLICY_SOURCE` accepts every quoted source, so `'unsafe-eval'` passes; I tried it. More to the point, `'strict-dynamic'` would pass too, and it lets any script the page trusts load further scripts from any host, which is exactly what this check is for. Allow `'self'`, `'none'`, `'unsafe-inline'`, `'wasm-unsafe-eval'`, `'sha256-…'`/`'sha384-…'`/`'sha512-…'` and `'nonce-…'`, plus `data:`, and refuse the rest by name.

## Re-check at `5e96adc` (2026-11-11)

**Clear to merge.** Both items are taken.
- **B1** (`5249e2f`): the link preview (`bbc6b04`) is merged in, with one `head: [policy, ...linkPreview]`. No conflict markers are left in any file, `check-dist.mjs` has both functions whole, and the README names both kinds of entry. The branch now merges into `main` without conflicts and brings the link preview with it, so the maintainer merges this one branch for both.
- **N1** (`5e96adc`): `POLICY_SOURCE` lists `'self'`, `'none'`, `'unsafe-inline'`, `'wasm-unsafe-eval'`, the hashes, a nonce, and `data:`.

I built from `git archive 5e96adc` on Node 22.23.4: the build passes and check-dist is clean. The policy is the first of the policy, `og:image`, script and stylesheet elements in all 29 HTML files. Against altered copies of `dist/`, `'strict-dynamic'`, `'unsafe-eval'` and an `og:image` naming a missing file each fail the build, so the link preview's check runs after the merge too.
