# Documentation site: a Content-Security-Policy (task `fqjn`): implementer's notes

- Task: `2026-11-11-docs-site-enforce-nothing-from-another-host-in-the-browser-w-fqjn`, raised by
  the code review of the documentation site (`.alta/reviews/2026-11-05-docs-site/code-review.md`,
  "Not a finding").
- Branch: `alta/shiny-cadence-bmk8`, on `main` at `d6d2373`. One commit for the change, then these
  notes: `git log d6d2373..`.
- Written by the implementer session on 2026-11-11. The change is small and was made directly, not
  by a child session.

## What changed

| File | Change |
| --- | --- |
| `site/astro.config.mjs` | The site's first `head` entry: `<meta http-equiv="Content-Security-Policy">`. |
| `site/scripts/check-dist.mjs` | Fails the build on a page without the element, on a script or stylesheet before it, and on a policy that allows a host or a scheme other than `data:`. Not asked by the task: see "For the code review". |
| `site/README.md` | A section "The Content-Security-Policy": that it is the one `head` entry, each exception and its reason, what the policy does not do, how to test after a change. A row in "Where things are", a bullet in "What fails the build". |
| `changelog.d/docs-site-csp.md` | One `Changed` bullet. |

The policy, in every one of the 29 built pages (the 404 page included), right after the charset
and the viewport and before the first script and stylesheet (Starlight sorts an `http-equiv` meta
there):

```text
default-src 'self'; script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; base-uri 'none'; form-action 'self'
```

## How the policy was arrived at

Built first with a bare `default-src 'self'` and walked in Chrome: 116 page visits (29 pages, two
themes, two widths). Chrome refused three kinds of things and nothing else:

| Refused under the bare policy | Count | Allowed by |
| --- | --- | --- |
| Inline `<script>` elements (`script-src-elem`) | 1288 | `'unsafe-inline'` in `script-src` |
| `style` attributes (`style-src-attr`) | 14376 | `'unsafe-inline'` in `style-src` |
| `data:` images of the stylesheets (`img-src`) | 281 | `data:` in `img-src` |

Two things the review expected turned out differently, both measured:

- **`'wasm-unsafe-eval'` is not needed for the normal search.** Pagefind compiles its WebAssembly
  in a worker (`pagefind-worker.js`), and the search returned its results under the bare policy in
  all three engines. It is needed for Pagefind's fallback: with the worker's file refused, Pagefind
  runs on the page, and without the keyword the search finds nothing ("Failed to load the Pagefind
  WASM … blocked by CSP" in Chrome, Firefox and WebKit). With it, the fallback returns the same 6
  links. So the keyword stays, for that case.
- **`style-src-attr 'unsafe-inline'` would be enough for the built site** (no `<style>` element is
  refused), but `astro dev` injects 38 `<style>` elements, and `style-src-attr` is the newer
  directive. The policy keeps the plain `style-src 'self' 'unsafe-inline'` the review named, and
  `npm run dev` runs under the same policy as the build, without a violation.

`base-uri 'none'` and `form-action 'self'` are additions of mine: the two directives `default-src`
does not cover. The site has no `<base>` and no form that submits.

## What was tested

Scripts: `walk.mjs` and `probe.mjs` in this folder (playwright-core 1.64.1 in a scratch folder,
against `astro preview` of the final build under `/umbral/`). Engines: the installed Chrome
157.0.8103.39, Playwright's Firefox 157.0 and WebKit 27.2.

**The walk**, per engine, at 1280 and 360, dark and light (the theme chosen with the theme select,
against the opposite system colour scheme, then checked on every page):

| | Chrome | Firefox | WebKit |
| --- | --- | --- | --- |
| Page visits (29 pages × 2 themes × 2 widths) | 116 | 116 | 116 |
| Theme select used, theme held on every page | 4 | 4 | 4 |
| Menu opened and closed at 360 (28 pages × 2 themes) | 56 | 56 | 56 |
| "On this page" dropdown at 360 | 56 | 56 | 56 |
| Copy button of a code block | 64 | not run | not run |
| Search for `redirect_uri_mismatch`, first result opened | 4 | 4 | 4 |
| Policy violations | 0 | 0 | 0 |
| Console errors and warnings, page errors, failed requests | 0 | 0 | 0 |
| Requests to another host | 0 | 0 | 0 |

Each search returned 6 links, the first `/umbral/run/https/` (3 results, as in the site's own
review). A font face loaded on every page.

One thing seen once and not again: in an earlier Firefox run, with the three engines walking at the
same time, one lazy import of the search interface (`ui-core.*.js`) was aborted, with no policy
violation. Three later Firefox runs under the policy and one without the policy element were clean.
I take it for the walker leaving a page while that import was in flight, not for the policy, but I
did not reproduce it to prove that.

**The probe**, same three engines, final policy:

| Attempt at run time | Chrome | Firefox | WebKit |
| --- | --- | --- | --- |
| `fetch` to another origin, from the page | refused (`connect-src`) | refused | refused |
| An image from another origin | refused (`img-src`) | refused | refused |
| A `<script src>` from another origin | refused (`script-src-elem`) | refused | refused |
| `eval` of a string | refused | refused | refused |
| A `<base href>` to another origin | refused (`base-uri`) | refused | refused |
| WebAssembly on the page | compiles | compiles | compiles |
| Search with Pagefind's worker refused | 6 links | 6 links | 6 links |
| `fetch` to another origin, from a worker | refused | **fetched** | **fetched** |

The last row is the limit to know about: a worker takes its policy from its own response, and
GitHub Pages sends none. Firefox and WebKit follow that; Chrome applies the page's policy to what a
worker fetches. So in two engines Pagefind's worker is outside the policy, and `check-dist.mjs`,
which reads `pagefind-worker.js` like any other file, is still what covers it. The README says so.

**The dev server**: `npm run dev`, three pages and the theme select in the three engines: no
violation, no console error (the dev toolbar included).

**The build check**, on altered copies of `dist/`: a page without the element, a host added to
`script-src`, a script put before the element, `default-src *` with `https:` in `img-src`: each
fails with the file and the reason, exit 1. A hash in the place of `'unsafe-inline'` passes.
Untouched: `check-dist: 51 files read (29 html, 6 css, 13 js, 3 svg), nothing requested from
another host`. `.github/scripts/changelog.sh check` passes.

## Not tested

- The deployed site. It is live from `main`, which does not have the policy yet, so there is
  nothing deployed to test before the merge. As far as I know GitHub Pages adds no
  Content-Security-Policy header of its own, so the built pages served locally should behave the
  same; I did not ask the live site for its headers. To repeat on the deployed site after the merge.
- Safari itself and a phone: WebKit here is Playwright's build on macOS.

## Merging with the link preview (`alta/roaming-foxglove-m2q5`, `cebbe8e..bbc6b04`)

That branch is clear and not merged, and it also gives the site `head` entries (the `og:image`
metas and two PNG icon links). The task's "this is the first `head` entry" was written before it,
and this branch is cut from `main`, where it is still true. A trial merge of the two
(`git merge-tree`, nothing written) says what the second one to land has to do:

- `site/astro.config.mjs` conflicts. Keep both constants and **one** `head` key:
  `head: [{ tag: 'meta', attrs: { 'http-equiv': 'Content-Security-Policy', content: contentSecurityPolicy } }, ...linkPreview]`.
  Two `head` keys would be valid JavaScript in which the second silently replaces the first.
- `site/scripts/check-dist.mjs` conflicts. Keep both `checkPreviewImage` and `checkPolicy`, and
  both branches in the loop over the tags.
- `site/README.md` merges by itself and is then wrong in two places: the table's "the site's one
  `head` entry", and the paragraph "It is the `head` entry … and the only one: the site was built
  without any, and this is the one exception." Both need to name the link preview's entries too.

By reading, not by a build of the merge: the two fit. The link preview's metas are not fetched by
the page, its two icons come from the site itself (`img-src 'self'`), and Starlight still sorts the
policy before them. The walk should be run once on the merged build.

## For the design review (`umbral-designer`)

Nothing is meant to look different. `captures/` holds Chrome under the final policy,
`<state>-<width>-<theme>.png`: `start`, `run-https` and `search` at 1280 and 360 in both themes, and
`menu` at 360. To rule on: that the pages, the open menu and the search look as they did in
`.alta/reviews/2026-11-05-docs-site/captures/` (fonts, the mark, the icons that come from `data:`
URLs, the current sidebar entry, the focus ring of the search field).

## For the code review (`umbral`)

1. The policy itself, and the two additions the task did not name (`base-uri 'none'`,
   `form-action 'self'`).
2. `'wasm-unsafe-eval'` kept for the fallback only: say if you would sooner drop it and let the
   fallback fail.
3. The new check in `check-dist.mjs`. The task's "Where" lists only the config and the README; I
   added the check so that a page without the policy, or a policy that names a host, fails the
   build like any other request to another host. Say if it should go.
4. The worker limit above, and whether the README's wording of it is right.
5. `walk.mjs` and `probe.mjs` live in this folder, not in `site/`: the site has no browser
   dependency and I did not add one.
