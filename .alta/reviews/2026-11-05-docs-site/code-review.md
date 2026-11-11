# Code review: the documentation site's scripts and the Pages workflow (task hmfr)

Branch `alta/roaming-foxglove-m2q5`, `3c87b72..65a2f69` (the site is `ca96f84`). Reviewer: the `umbral` session, 2026-11-11. In scope: `site/scripts/collect.mjs`, `site/scripts/check-dist.mjs`, `.github/workflows/pages.yml`, the pins and `site/package-lock.json`, `site/astro.config.mjs`, `site/site.config.mjs`, `site/sidebar.mjs`, `.dockerignore`. The README split, the new pages and the look are the documentation and design reviews'.

## Verdict

**Two blocking items, both a few lines in `pages.yml`; five nits.** The scripts are sound. I built the site from a clean export and broke each check on purpose: collect refused every bad source, and check-dist has three holes, all closable with one pattern (N1).

## What was checked

- **The build, from a clean export.** I unpacked `git archive 65a2f69` into an empty folder and ran `npm ci && npm run build` on Node 22.23.4. It passed: 28 pages, 169 links to pages and 28 to GitHub, `All internal links are valid.`, and check-dist printed the same line as the notes (48 files, 148 external `<a>` links).
- **collect.mjs refuses, naming the file and line, and writes nothing.** Each of these exits 1 with `nothing written`:
  - an unknown `## ` section in the README;
  - a page in `docs/` missing from the sidebar;
  - a link to a missing heading;
  - a link that leaves the repository (`../../outside.md`);
  - a setext heading;
  - a second `# ` heading inside a page.

  Writing happens only after every check passes, and the `&&` chain in `npm run build` stops `astro build` on a failure.
- **Anchors on a page made from two sources.** Anchors map through `headingIds`, keyed by source and line with the page's own repeat counter. So a heading whose text appears in both the README's Integrate section and `docs/integrate.md` still gets the right `-1` id, and `starlight-links-validator` checks the ids again after Astro has made them.
- **Writes stay in `site/`.** The three output paths are built from `SITE_DIR`, a link that resolves outside the repository is an error, and the generated folders are in `site/.gitignore`.
- **check-dist's existing rules hold.** A `<link rel="preconnect" href="https://…">` added to `index.html` fails it, and so does `fetch("https://evil.example/x")` added to a script. An unparsable URL counts as another host, so it fails closed.
- **Action pins.** Each SHA was compared with its tag via `git ls-remote`, and all five match:
  - checkout v5.1.0 `fbc6f39`, the same as the samples workflows;
  - setup-node v7.1.0 `949feb2`;
  - configure-pages v6.0.0 `45bfe01`;
  - upload-pages-artifact v5.0.0 `fc324d3`;
  - deploy-pages v5.0.1 `368f825`.

  The last four are the newest tags of their actions today.
- **npm pins.** Astro 7.4.10, Starlight 0.42.10 and starlight-links-validator 0.27.0 are pinned exactly. The lockfile has 381 entries, all resolved from `registry.npmjs.org`, each with an integrity hash. The only package with an install script is `fsevents` (macOS only, never installed on the runner). Astro's own `engines` does say `node >=22.23.4`.
- **The workflow's shape.**
  - `contents: read` at the top. `pages: write` and `id-token: write` only in the deploy job, which runs no repository code.
  - A pull request builds and uploads nothing.
  - `configure-pages` is in the deploy job, so a repository without Pages turned on fails only there.
  - `.dockerignore` keeps `site/` out of the image's context.

## Blocking

- **B1. A pull request can cancel a deploy to `main`.** `concurrency: {group: pages, cancel-in-progress: false}` puts every run in one group, pull requests included. GitHub keeps one running and one pending run per group, and a newly queued run cancels the pending one. Here is what that does:
  1. A deploy is running.
  2. A second push to `main` is pending behind it.
  3. A pull request touching `docs/` queues.

  The `main` run is cancelled and never deploys, and pull requests also wait behind deploys. Give each ref its own group, and let pull requests cancel their own older runs:

  ```yaml
  concurrency:
    group: pages-${{ github.ref }}
    cancel-in-progress: ${{ github.event_name == 'pull_request' }}
  ```

  `main`'s runs still queue: a newer one replaces a pending older one, which is right, since each deploys its own commit.

- **B2. The Release notes page never updates after a release.** `CHANGELOG.md` only changes through the release workflow. Its changelog job commits and pushes with `GITHUB_TOKEN`, and a push made with that token starts no workflow, so the `CHANGELOG.md` path filter never fires. The page would stay at whatever release came before the last docs change. A `workflow_run` trigger runs after the release workflow, on `main`, with the changelog commit already there:

  ```yaml
    workflow_run:
      workflows: [release]
      types: [completed]
  ```

  It fires when the whole release run has finished, and both ways of releasing have pushed the changelog to `main` by then: a Run in its `changelog` job, a pushed tag in the `release` job ("Record the changelog on main"). No condition on the run's conclusion is needed. A run that failed before pushing just rebuilds the same notes, and one that failed after pushing still has notes to publish. `github.ref` is `refs/heads/main` for this event, so the deploy job's `if` lets it through, and with B1 it shares `main`'s group. Keep `CHANGELOG.md` in the path filter for a hand edit.

## Nits

- **N1. check-dist misses three things a browser would fetch.** I injected each into the built `dist/`, and check-dist passed:
  - `fetch("//evil.example/x")` in a script: the JS scan only matches `https?://`, not a protocol-relative URL.
  - `<image href="https://…">` in an SVG: `.svg` files are never read (today: the favicon and the two Gnomon files). Low risk, because an SVG shown as `<img>` or as a favicon can't fetch, but a later `<object>` or inlined copy could.
  - `image-set("https://…" 1x)` in CSS: only `url()` and `@import` are scanned.

  One pattern closes all three. In `.js`, `.mjs`, `.css` and `.svg`, take every `["'`(=\s](?:https?:)?//host` and require the host to be on a list, each entry with its reason, as the JS list is today. On this build the hosts it finds are:
  - `www.w3.org`: the SVG namespace, in CSS data URIs, the SVG files and JS;
  - `bugzilla.mozilla.org` and `bugs.webkit.org`: comments in Starlight's CSS;
  - `markjs.io`, `git.io` and `example.com`: already allowed.

  Keep the existing exact-URL list for full `https://` URLs in JS as well.
- **N2. Pin Node exactly.** Today, `node-version: 22` with `check-latest` takes whatever 22.x is newest on each run, and `.nvmrc`'s `22` lets nvm pick an older installed 22 that npm only warns about (`engines` isn't enforced without `engine-strict`). Write the exact version in `site/.nvmrc` (22.23.4, or the newest 22 when you edit it), and use `node-version-file: site/.nvmrc` without `check-latest`. Then CI and a local build run the same Node, and a bump is one line.
- **N3. `persist-credentials: false` on the checkout**, as the samples workflows have it. `npm ci` installs a few hundred packages with the job's token in `.git/config`. The token is read-only here, but the line costs nothing and keeps the workflows alike.
- **N4. A dispatch from another branch uploads an artifact for nothing.** The upload step runs for any event but `pull_request`, while the deploy job needs `main`. Give the upload step the same `github.ref == 'refs/heads/main'` condition. Trivial.
- **N5. Say the line-based scan's limit in a comment.** A code span that wraps onto a second line isn't blanked (your point 2). Usually that fails loudly, as a link that doesn't exist. The one quiet case is a wrapped span holding `](path)` to a file that exists: the path is rewritten inside the code. No source does this, so no code change: one sentence in the header comment beside "The link scan follows check-links.py".

## The four questions

1. **Exact strings or hosts in the JS allowlist?** Keep exact strings for full `https://` URLs: stricter than a host, since a new path on an allowed host still fails. Add the host check of N1 for the quoted `//host` pattern, which covers what the exact list can't see.
2. **The line-based scan.** Accepted as is, with N5's sentence.
3. **The two extra trigger paths.** Both are right: `samples/README.md` is a page, and the fonts ship with the site. B2 is the trigger that was missing.
4. **Node.** Exact, through `.nvmrc` (N2).

## Not a finding

GitHub Pages can't send headers, but a `<meta http-equiv="Content-Security-Policy">` in Starlight's `head` would make "nothing from another host" enforced by the browser, not only checked at build. It needs `'unsafe-inline'` for Starlight's theme script and `'wasm-unsafe-eval'` for Pagefind, and search would need testing under it. That is worth a follow-up task if you want it, but not for this branch.

## Re-check at `5155151` (2026-11-11)

**Clear to merge.** Both blocking items and all five nits are in, as written in "After the reviews" in `notes.md`. I read the diff of `pages.yml`, `check-dist.mjs`, `collect.mjs`, `.nvmrc` and `package.json` since `65a2f69`, and rebuilt from `git archive 5155151` on Node 22.23.4: 28 pages, 127 links to pages, validator clean, `check-dist: 51 files read (29 html, 6 css, 13 js, 3 svg)`.

- **B1, B2.** One concurrency group per ref, where a pull request cancels only its own older runs. `workflow_run` runs after `release`: its `github.ref` is `refs/heads/main`, so it shares main's group and passes the deploy's and the upload's conditions.
- **N1.** I injected my three cases into the built `dist/` again (a `//host` fetch in a script, `image-set` in a stylesheet, `<image href>` in `favicon.svg`). Each is now a problem in its own file, exit 1.
- **N2 to N5.** As described. `node-version-file: site/.nvmrc` with `22.23.4` exactly.
- **The navigation paragraph (new since the review).** `navigationParagraph()` finds the paragraph correctly: line numbers start at 1, so `lines[to]` is the next line. Both segments come from one source, so the setext check isn't repeated. I tried two edits to `docs/connectors/discord.md`:
  - a sentence after the links: refused with file and line, nothing written;
  - words before "Other providers:": the pattern no longer matches and the paragraph is kept whole.

  Either way, no text is dropped. `native-apps.md` keeps its paragraph, and the discord page leaves its paragraph out.

## The link preview, `cebbe8e` (2026-11-11)

**Clear to merge.** No blocking items; two nits.

What I checked:
- **The build.** `git archive cebbe8e`, built on Node 22.23.4: it passes, and check-dist is clean.
- **The tags.** The three PNGs land in `dist/` under `/umbral/`. Each of the 28 pages has exactly one `og:image`, `https://cosmic-chimps.github.io/umbral/og.png`, built from `site.config.mjs`, so a custom domain moves it too. Starlight's own `og:url`, `og:title` and `twitter:card` are untouched, with no duplicates.
- **What the head gains.** Only `meta` and `link` elements, and no script.
- **The card.** `social/card.html` loads only the two local fonts, and it isn't in `dist/`.
- **The picture.** It is 1200 × 630, and the alt text says what it shows.
- **sharp** is Astro's optional dependency (0.35.5) and has no install script.

Your questions:
- **Global head entries.** Right. There is one picture for the site, and the per-page title, URL and description stay Starlight's.
- **sharp.** Leave it out of `package.json`. The script is run by hand, and if Astro ever stops installing sharp it fails loudly with "Cannot find package 'sharp'". Naming it would only risk a second copy. One line in the README's "The link preview" says the way out: `npm install --no-save sharp`.
- **`meta content` in check-dist.** Yes, and N1 is how.

Nits:
- **N1. check `og:image` and `twitter:image` in check-dist.** These aren't reader requests: a chat app's server fetches them, so the reader's privacy isn't at stake. But a wrong value fails in silence: a changed `base`, a renamed file, or a picture moved to another host leaves Slack with the empty box again, and no build notices. For those two properties (and `og:image:secure_url` if it ever appears), require the site's own host and a file that exists in `dist/` at that path under `base`.
- **N2, for the designer: the 180 px icon has transparent rounded corners.** iOS fills an `apple-touch-icon`'s transparency with black and rounds the corners itself. The usual form is a full square in the tile's colour. It's barely visible on this ink, so it's the designer's call.
