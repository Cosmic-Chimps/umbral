# Documentation site and the README split (task `hmfr`): implementer's notes

- Task: `.alta/tasks/2026-11-05-docs-a-documentation-site-starlight-on-github-pages-and-the-hmfr.md`
  (it supersedes the audit's T7).
- Branch: `alta/roaming-foxglove-m2q5`, on `main` at `3c87b72`.
- Commits: `1c19227` (the split), then the site and its workflow, then these notes and the captures.
  `git log 3c87b72..` lists them.
- Written by the implementer session on 2026-11-11. The split and the site were each written by a
  Sonnet child session from a brief and reviewed by the parent before the commit.

## Read this first

1. **The audit's T3, T4 and T5 have not landed.** The task's Order section puts this task after
   them, "so the text that moves is already correct". The maintainer asked for this task on
   2026-11-11 with them still open, so the text moved as it is on `main`: this task corrects
   nothing. T3 still edits the README (the quick start stays there). T4's findings now apply to the
   new files; the table under "Where each section went" says which. T5 is untouched (the
   walkthroughs changed only in link targets).
2. **None of the three reviewers was running** (`umbral-designer`, `umbral-documentation`, `umbral`:
   not in the list of sessions on 2026-11-11). So the theme question could not be put to the
   designer before building, as the task asks. The theme is a proposal built from `DESIGN.md` and
   the product's own tokens, in one stylesheet, and "The look" below says exactly what it sets, so
   the designer can rule on it from the captures. No review has been requested yet: all three are
   still owed, and nothing should merge before they say clear.
3. **The site is not deployed.** Pages is not enabled and the branch is not merged, so the
   acceptance item "the deployed site renders every page" is checked on the built `dist/`, served
   locally under `/umbral/`. The captures are of that. The first real deploy is the maintainer's
   (see "The maintainer's steps").

## Where each section went

Line numbers are `README.md` at `3c87b72` (1570 lines). The README is now 412 lines.

| README section (lines at `3c87b72`) | Now | Site route |
| --- | --- | --- |
| Opening, concepts, what it is not (1–30) | README, unchanged | `/` |
| Contents (32–62) | README, rewritten: this page's sections, then the site's pages by group | not published |
| "An external store loads…" (61–62) | `docs/run/secret-stores.md` | `/run/secret-stores/` |
| Requirements (64–86) | README; two link targets changed | `/` (after the opening) |
| Quick start (88–286) | README, whole; seven link targets changed | `/quick-start/` |
| Integrate an app: values, discovery, samples, snippet (288–342) | README, unchanged, plus one closing paragraph of links | `/integrate/` (first part) |
| Integrate an app: what an app can count on … how people are identified (344–532) | `docs/integrate.md` | `/integrate/` (the rest) |
| How it looks (534–571) | `docs/look.md` | `/look/` |
| Brand an app's sign-in page (573–662) | `docs/branding.md` | `/branding/` |
| Who can sign in (664–755) | `docs/who-can-sign-in.md` | `/who-can-sign-in/` |
| Passkeys: opening (759–764) | not kept twice: the walkthrough's opening says it (table below) | `/connectors/passkey/` |
| Passkeys: the security log (819–823), memory (825–827) | `docs/connectors/passkey.md`, Gotchas (after the documentation review; first in `docs/passkeys.md`) | `/connectors/passkey/` |
| Passkeys: Enable the connector … JavaScript (766–817) | not kept twice: `docs/connectors/passkey.md` says it (table below) | `/connectors/passkey/` |
| Passkeys for the admin area (829–878) | `docs/run/operator-passkeys.md` (after the documentation review; first `docs/passkeys.md`) | `/run/operator-passkeys/` |
| Run it for real, behind HTTPS (880–974) | `docs/run/https.md`; the README keeps one new paragraph | `/run/https/` |
| Deploying on Caravana, with Release and the Caravana backup (976–1229) | `docs/run/caravana.md` | `/run/caravana/` |
| Your data: back it up, with Upgrading (1231–1329) | `docs/run/backup.md`; the README keeps one new paragraph | `/run/backup/` |
| Forgotten operator credential (1331–1377) | `docs/run/operator-credential.md` | `/run/operator-credential/` |
| Moving connector secrets between stores (1379–1400) | `docs/run/secret-stores.md`, under the list of the four stores | `/run/secret-stores/` |
| Settings reference (1402–1422) | `docs/run/settings.md` | `/run/settings/` |
| Troubleshooting (1424–1543) | `docs/run/troubleshooting.md` | `/run/troubleshooting/` |
| Contributing (1545–1566) | README; one link target changed, one sentence added about `site/` | not published |
| License (1568–1570) | README, unchanged | not published |

For T4, when it runs: "Passkeys" findings are in `docs/connectors/passkey.md` and
`docs/run/operator-passkeys.md`; "Run it for real" in `docs/run/https.md`; "Backup" in
`docs/run/backup.md`; "Moving secrets" in `docs/run/secret-stores.md`; "Settings" in
`docs/run/settings.md`; "Troubleshooting" in `docs/run/troubleshooting.md`; "Who can sign in" in
`docs/who-can-sign-in.md`. The Requirements item of W11 is still in the README.

In a moved section, headings went up one level (`###` to `##`), so every old anchor is the same
slug in its new file.

## Nothing was lost: how it was checked

A script took every non-blank line of `git show 3c87b72:README.md`, stripped heading marks and
link targets, and looked for it among the lines of the new `README.md` and of `docs/**/*.md`
(without the audit and `docs/history/`). The lines it did not find are exactly these, and each is
accounted for:

- **32–59**, the old Contents block: replaced by the new one.
- **344**, "What an app can count on:": now the heading `## What an app can count on`.
- **766–817**, the passkey paragraphs the walkthrough already says (next table).
- **904 and 906** (`docs/run/https.md`): "exactly as in the quick start" and "Continue from step 3"
  now link to the README's quick start and its step 3, since they are no longer on the same page.
- **980** (`docs/run/caravana.md`): "nor the compose file above" is "nor the repository's compose
  file".
- **1014–1015** (`docs/run/caravana.md`): "the section above explains why" is
  "[The public origin must match exactly](https.md#the-public-origin-must-match-exactly) explains
  why".
- **1400** (`docs/run/secret-stores.md`): "Details are in the walkthroughs" (which linked to the AWS
  page as a stand-in) is "Details are in the walkthroughs listed above", the list being on that
  page now.

Outside those, the moved text is byte-identical except link targets. The quick start and
Requirements were diffed section against section: nine lines differ, each only in a link target.

### The passkey paragraphs that are not kept twice

The task: "do not keep two pages that say the same thing". `docs/connectors/passkey.md` is
unchanged apart from two link targets.

| README paragraph (lines) | Where `docs/connectors/passkey.md` says it |
| --- | --- |
| The opening (759–764): what a passkey is, Umbral keeps the public half, Umbral is the provider, a person's passkeys belong to their user | The opening ("Umbral is the provider: it keeps only the public half"); "Adding a passkey…" ("An account holds up to 10 passkeys"); "Removing a passkey" ("Deleting a user deletes their passkeys") |
| Enable the connector (766–773) | "1. Enable the connector" |
| Invite someone (775–782) | "2. Invite someone", "3. What the person sees" |
| Signing in (784–786) | "Signing in" |
| Adding a passkey to an account that signs in with a provider (788–791) | "Adding a passkey to an account that uses a provider" |
| Recovery (793–797) | "Recovery" |
| Removing a passkey (799–803) | "Removing a passkey" |
| What an app receives (805–808) | "What an app receives" |
| Bound to the address (810–812) | "Before you start", and Gotchas, "Changing the domain stops every passkey" |
| JavaScript (814–817) | Gotchas, "JavaScript is needed" |

The security-log paragraph's last sentence, "No challenge, credential id, key or invitation link
is ever logged", and the memory paragraph were said nowhere else. They were first kept in
`docs/passkeys.md`; the documentation review lifted the links-only rule for them, and they are now
in the walkthrough's Gotchas: the sentence in "The security log" (which already named the events),
the memory paragraph as a bullet of its own.

## New sentences

Everything not in the list above that is new text:

- README, Contents: "On this page:", and "Everything else is in the documentation, at … with a
  sidebar and search. It is built from the Markdown in `docs/`, which reads the same here on
  GitHub."
- README, Integrate an app: the closing paragraph, "Beyond these: what an app can count on (the
  flow, the scopes and every claim), the endpoints, …", seven links into `docs/integrate.md`. It is
  worded to read on GitHub and on the site, where the links land on the same page.
- README, "Run it for real, behind HTTPS" and "Your data: back it up": one paragraph each, made of
  what the moved sections say (port 8080 and the proxy, Caddy in the compose file, the domain and
  ports 80 and 443, the `UMBRAL_DOMAIN` command; `/data`, what is lost with it, stop before
  copying, test the restore).
- README, Contributing: one sentence on `site/`.
- `docs/integrate.md`: the lead paragraph, which sends a GitHub reader to the README for the three
  values. The site drops it (the README's section is the lead there).
- `docs/run/operator-passkeys.md`: "For the passkeys of the people who sign in to your apps, see
  the Passkeys walkthrough."
- `docs/connectors/passkey.md`, Gotchas: "Passkeys for the admin area are separate. The operator's
  own passkeys, for signing in to the admin area, do not need this connector: see Passkeys for the
  admin area."
- `docs/run/secret-stores.md`: "By default Umbral keeps each connector's client secret in `/data`,
  encrypted (`UMBRAL__SECRET_STORE=local`). It can keep them in a secrets manager instead." (from
  the settings table's row for `UMBRAL__SECRET_STORE`).

## Decisions the task left open, for the documentation review

1. **Two passkey pages, and one more row in the sidebar.** The task lists `docs/passkeys.md` as a
   new file and says existing pages change only in links, but its sidebar has one Passkeys row.
   The walkthrough stays the Connectors row. `docs/passkeys.md` is nearly all "Passkeys for the
   admin area", so it is a row of that name under **Run it**, between "Back up your data" and
   "Forgotten operator credential", which is the same subject (the operator's own way in). If the
   reviewer would rather have one page, the walkthrough can take the two paragraphs and "Passkeys
   for the admin area" can move to `docs/run/`.
2. **`/integrate/` is one page from two sources**: the README's section, then `docs/integrate.md`
   from its first `##`. So the three values and the snippet are written once, in the README, and
   the site has one "Integrate an app" page, as the sidebar asks.
3. **Requirements is on the site's start page**, after the opening.
4. **The Contents block stays where it was**, after the opening, although the task's list names it
   last: a reader should meet the pointer to the site before the quick start, not after the
   licence.
5. **"Secret stores" is a group in the sidebar**: its first row is `docs/run/secret-stores.md`
   ("Overview and moving secrets"), then the four stores. A Starlight group has no page of its own.
6. **`README.md#integrate-an-app`, `#run-it-for-real-behind-https` and `#your-data-back-it-up`
   still resolve**, because those headings stay. Every other old anchor is gone, as the task
   says; the changelog note says so.

## The site

`site/` is an Astro Starlight project; `site/README.md` describes it for whoever maintains it.

- **Pins** (exact, with `package-lock.json`): `astro` 7.4.10, `@astrojs/starlight` 0.42.10,
  `starlight-links-validator` 0.27.0. Nothing else. Astro 7.4.10 itself asks for Node 22.23.4 or
  later, so `package.json` says `>=22.23.4`, and `.nvmrc` says `22.23.4` exactly, which the
  workflow reads (after the code review; first `22` with `check-latest`).
- **`site/site.config.mjs`** holds `site` and `base`, the two lines to change for a custom domain,
  and the repository's address. The Astro config, the collect script and the check import them.
- **`site/scripts/collect.mjs`** (Node, standard library only) writes `site/src/content/docs/` and
  copies the three fonts to `site/src/fonts/` and their licences to `site/public/licenses/fonts/`.
  All three are in `site/.gitignore`: no copy of a page or of a font is committed. Its three output
  paths are built from the `site/` directory, so it writes nowhere else.
- **README slicing was kept; the `docs/quick-start.md` fallback was not needed.** The README's
  `## ` sections are a closed list in the script: three are published (Requirements on the start
  page, the quick start, Integrate an app), five are known and not published (Contents, Run it for
  real, Your data, Contributing, License). Any other `## ` heading in the README stops the build
  and names it, and so does a published one that is missing. That is what makes the slicing safe:
  it cannot drop or misplace a section in silence.
- **Titles and frontmatter.** A page's `title` is its source's `# ` line; the script also writes
  `editUrl`, so "Edit page" opens the real source on GitHub. A source that starts with frontmatter,
  or without a `# ` title, stops the build. `docs/**/*.md` carries none (`grep -L` is not needed:
  the build is the check).
- **Links.** Every relative link in a source is resolved from the source's own place in the
  repository. To a page's source: the page's route, with the anchor checked against the target's
  headings. To `README.md#anchor`: the page that holds that section. To anything else that exists
  in the repository (a sample, a spec, `docker-compose.yml`, `docs/history/`): the file on GitHub.
  To nothing: an error with `source:line`, and nothing is written. On this tree: 127 links to
  pages, 28 to GitHub (169 before the navigation paragraphs were left out, see "After the
  reviews").
- **Every page is in the sidebar.** `site/sidebar.mjs` is the sidebar and the list of pages; the
  script fails on a generated page it does not name and on an entry for a page that does not
  exist. 28 pages.
- **Search** is Pagefind, built by Starlight at build time into `dist/pagefind/`.
- **`.github/workflows/pages.yml`**: actions pinned by commit with the version in a comment, as
  `release.yml` and the samples workflows are (`checkout` v5.1.0, `setup-node` v7.1.0,
  `configure-pages` v6.0.0, `upload-pages-artifact` v5.0.0, `deploy-pages` v5.0.1; SHAs read from
  GitHub's API on 2026-11-11). Push to `main` and pull requests, both on `docs/**`, `README.md`,
  `CHANGELOG.md`, `samples/README.md`, `site/**`, `src/Umbral/Assets/fonts/**` and the workflow
  itself; and on request. A pull request builds and uploads nothing. `configure-pages` is in the
  deploy job, so before Pages is turned on only that job fails. `actionlint` passes. `ci.yml` is
  not changed.

Two paths were added to the task's trigger list: `samples/README.md` (it is the Samples page) and
`src/Umbral/Assets/fonts/**` (the site ships those files).

## The look (for the design review)

The designer could not be asked first (see "Read this first"), so this is what was built, to be
ruled on from the captures. It is one stylesheet, `site/src/styles/umbra.css`, registered with
`customCss`. No component is overridden and there is no custom layout or `head` script.

- **Colours**: Starlight's grey scale and accent set from the product's tokens
  (`src/Umbral/Assets/tailwind.config.js`). Dark: page `#0b0b14`, header and sidebar `#14141f`,
  hairlines and inline code `#2e2e40`, muted text `#a7a2b6`, text `#ece8df`, accent `#3a2e12` /
  `#b8801f` / `#f5c26b`. Light: page `#f4f1ea`, header and sidebar `#faf7f0`, hairlines `#ddd6c8`,
  inline code `#ebe5d8`, muted `#5f5a6b`, text `#17151f`, accent `#fbefd6` / `#8a5a00` / `#5c3c00`.
  Link text is `#f5c26b` in the dark theme and `#8a5a00` in the light, as in the product.
- **One departure from Starlight's defaults**: the hairline of both themes is the fifth grey step,
  because Starlight's dark hairline is its darkest step, which here is nearly the page colour.
- **Fonts**: the three shipped faces, the product's own `@font-face` blocks. Schibsted Grotesk for
  text, Spline Sans Mono for code, Bricolage Grotesque at 800 for the page title and `h2` (the two
  sizes `DESIGN.md` gives the display face); `h3` and below in the text face at 600.
- **The wordmark**: the site title in the display face at 800, `-0.035em`, lowercased by CSS, in
  the text colour (the Lowercase Wordmark Rule).
- **The Gnomon**: `Mark.small` from `src/Umbral/Views/Theme.fs` as two SVG files with the mark's
  own colours for each theme written in, and the favicon tile that `faviconHref` builds. These are
  the only assets committed under `site/`.
- **Dark and light** follow the system, with Starlight's own Auto / Dark / Light select.
- **Contrast**: every pair of text and background the stylesheet sets is at 4.5:1 or better; the
  table is in `site/README.md` (lowest: link text on inline code in the light theme, 4.72).

Open for the designer: the code-block themes are Starlight's defaults; the current sidebar entry
is Starlight's filled pill (gold with dark text in the dark theme, `#8a5a00` with light text in the
light one), which the Fill Pair Rule may want as ink with gold text in the light theme.

## Nothing from another host

- `site/scripts/check-dist.mjs` runs at the end of every build and fails it. In the HTML, a URL to
  another host may only be the `href` of an `<a>`, or text; in a `src`, `srcset`, `<link>`, inline
  style, inline script or refresh it is an error. In the CSS, no `url()` or `@import` to another
  host. In the JavaScript, each URL string must be in a list in the script, each with its reason;
  today that list is the SVG namespace, two dummy bases for `new URL()`, two links in comments of
  Pagefind's highlighter and seven translator credits in Pagefind's language strings. None is a
  request.
- Its line on this tree: `48 files read (29 html, 6 css, 13 js), nothing requested from another
  host; 148 external <a> links (github.com 143, www.keycloak.org 2, goauthentik.io 2,
  console.cloud.google.com 1)`.
- A real browser agrees: while taking the captures below, Chrome's requests were recorded on every
  page load and during a search. **Requests to another host: 0.**

## Acceptance, item by item

| Item | Result |
| --- | --- |
| `npm ci && npm run build` on a clean checkout with Node 22 | Passes with Node 22.23.4 after removing `node_modules`, `dist`, `.astro` and the generated folders. |
| The build fails on a dead internal link | Proved twice with throwaway links, then removed. In a source (`docs/look.md`, a missing file and a missing anchor): `collect.mjs` printed both with `docs/look.md:40`, exit 1, nothing written; `check-links.py` failed on the same two. In a generated page, with `astro build` run directly: `starlight-links-validator` stopped the build. |
| The deployed site renders every page, dark and light, 1280 and 360, with search | **Not checked on the deployed site: there is none yet.** Checked on the built `dist/` served by `astro preview` under `/umbral/`: 28 pages and the 404 built; captures of the start page, the Discord page and "Run it for real" at 1280 and 360 in both themes are in `captures/` (`<page>-<width>-<theme>.png`); a search for `redirect_uri_mismatch` returned 3 results, the first `/umbral/run/https/` (`captures/search-1280-dark.png`). To repeat after the first deploy. |
| `dist/` requests nothing from another host | See the section above. |
| README under 700 lines | 412. |
| The quick-start walk | **Scripted, not walked by hand.** `tests/Umbral.Tests/FirstLoginTests.fs` (an operator claims a fresh instance, configures a connector, registers an app, and a person signs in) passes, 3 of 3. The quick start's text is the text on `main` except seven link targets, so this task changes nothing a reader does. The walk by a person on a clean machine is T3's, and T3 has not run: step 1 still says `git clone <URL of this repository>`. |
| Every section that left the README is on one site page, in the sidebar | The table under "Where each section went"; the collect script's sidebar check enforces it. |
| No sentence lost | "Nothing was lost: how it was checked". |
| `docs/**/*.md` carries no frontmatter | None; the build fails on one. |
| `check-links.py` passes; the site's validator passes | `270 links in 34 files, all resolve`; `All internal links are valid.` |
| The collect script writes only into `site/` | Its three output paths are under `site/`; `git status` after a build shows nothing outside it. |
| Nothing under `src/`, the `Dockerfile` or the image changes | `git diff 3c87b72 -- src tests Dockerfile` is empty; `site` is in `.dockerignore`. |
| The changelog fragment | `changelog.d/docs-site.md`, Added and Changed; `changelog.sh check` passes. |
| Audit T7, one line | Added under the T7 heading; nothing else in the audit changed. |

## The UI strings that cite documentation (step 5)

T6 has not landed and has no task file, so there was nothing to amend. A follow-up task was
created instead, `hhww` ("UI and log: point the Documentation link and every message that cites
docs at the documentation site"): a `DocsUrl` constant beside `RepositoryUrl`, `DocumentationUrl`
(today `RepositoryUrl + "#readme"`, the footer's link) pointing at the site, E14's six strings
naming the site's pages, and **the log message at `src/Umbral/Program.fs:556`**: the schema-9
upgrade warning sends operators to "Troubleshooting in the README", which this branch moved to
`docs/run/troubleshooting.md`. The documentation review found that one (its blocking item, for the
follow-up); it is in `hhww`'s "Why" and "What to do". (`hhww` replaces `c36q`, which was removed
and created again with the added item, since a task cannot be edited.) Nothing under `src/` is
changed here. The task should wait until the site is live, and not long after.

## The maintainer's steps

1. After the three reviews say clear, merge.
2. Once: **Settings → Pages → Build and deployment → Source: GitHub Actions**. No workflow can do
   it. Until then the `deploy` job fails and says Pages is not enabled; the `build` job still
   proves the site.
3. Run the `pages` workflow by hand the first time (Actions, **pages**, **Run workflow**, `main`),
   then open `https://cosmic-chimps.github.io/umbral/` and repeat the capture check.
4. The README's Contents block links to the site, so those links are dead between the merge and
   the first deploy.

## For the three reviews

- **Documentation** (`umbral-documentation`): the six decisions under "Decisions the task left
  open"; the new sentences; the passkey table; the changelog fragment; the two link checks.
- **Design** (`umbral-designer`): "The look" and `captures/`.
- **Code** (`umbral`): `site/scripts/collect.mjs`, `site/scripts/check-dist.mjs`,
  `.github/workflows/pages.yml`, the pins, `site/package-lock.json` (381 packages, all from
  `registry.npmjs.org`).

## After the reviews (2026-11-11)

### Design (`review.md`): one blocking item, three nits, all taken in `site/src/styles/umbra.css`

- **Blocking, the current sidebar entry.** It was Starlight's filled pill. It is now the product's
  rail: the high tonal step (`#1d1d2b` dark, `#ebe5d8` light) with the label in the theme's gold at
  600, on `.sidebar-content a[aria-current='page']` (and its hover and focus), which outranks
  Starlight's own rule in the built CSS. The pair is in the contrast table of `site/README.md`:
  10.14 dark, 4.72 light.
- **h2** is the product's headline large, `1.5rem` / `2rem`, still in the display face.
- **The header mark** is `1.75rem` high.
- **The search field's focus ring** was the browser's own blue (the input had no outline rule); it
  is `2px solid` in the theme's gold.
- Code blocks keep Starlight's themes, as ruled.
- All 13 captures were taken again from the new build.

### Documentation (`review-docs.md`, `8d66e66`): clear, one item for the follow-up, four nits

- **The log message** (`Program.fs:556`): in the follow-up task `hhww`, see "The UI strings that
  cite documentation" above.
- **Nit 1**: the comment in `.github/workflows/release.yml` names `docs/run/caravana.md`.
- **Nit 2, one name for one page**: taken in full. `docs/passkeys.md` is now
  `docs/run/operator-passkeys.md`, titled "Passkeys for the admin area" as its sidebar row and the
  README's Contents call it, and holds that section alone, with one sentence that points to the
  walkthrough for people's passkeys. The security-log sentence and the memory paragraph are in the
  walkthrough's Gotchas, with one bullet that points back. So the first of the "Decisions the task
  left open" above is settled this way: two pages, each on one subject. The route is
  `/run/operator-passkeys/`.
- **Nit 3**: no pull request exists yet (this session does not open one unasked). When one is
  opened, its description should say that the README's links to the site are dead until the
  `pages` workflow has run once, so that the merge and the first run happen in one sitting. It is
  step 4 of "The maintainer's steps".
- **Nit 4**: the README's "Run it for real" paragraph takes the reviewer's wording.

After these: `check-links.py` passes (270 links in 34 files), the site builds clean (28 pages, all
internal links valid, nothing requested from another host), `actionlint` passes on both workflows
touched.

### Design re-check: clear at `cb75872`

The verdict is at the top of `review.md`. The search field keeps both its gold border (Starlight's)
and the gold ring (the focus), as ruled.

### Documentation re-check: clear at `cb75872` (`d2dab14`), with two last requests, both taken

- **One sentence in the walkthrough's opening**: "In Umbral a passkey is a connector, and a person's
  passkeys belong to their user, like the provider accounts they sign in with." It was the one
  statement of the README's old passkey opening that was left nowhere.
- **"Back to the README. Other providers: …" on the site** (the designer's observation, ruled by
  the documentation reviewer): the paragraph stays in the sources, where it is a GitHub reader's
  way back and sideways, and `collect.mjs` leaves it out of the generated page. The rule is
  narrow: a paragraph before the first `## ` that starts "Back to the [README](…). Other providers:"
  or "Other stores:" and holds nothing but links. Ten pages have one (the six connectors, the four
  stores). If such a paragraph ever holds a sentence of content, the build stops and says so,
  rather than dropping it. `docs/native-apps.md` opens "Back to the README" too, but goes on with
  a sentence about the samples, so it does not match and is kept whole. Links to pages went from
  169 to 127: the 42 that left are those navigation links.

### Code (`code-review.md`): two blocking items and five nits, all taken

- **B1, a pull request could cancel a deploy to `main`.** `concurrency` is now one group per ref,
  `pages-${{ github.ref }}`, and only a pull request cancels its own older runs.
- **B2, the Release notes page would never follow a release.** The release pushes `CHANGELOG.md`
  with the job's token, which starts no workflow. `pages.yml` now also runs on `workflow_run` of
  `release`, `completed`. `CHANGELOG.md` stays in the path filter for a hand edit.
- **N1, three fetches `check-dist` did not see.** Added: in every `.js`, `.mjs`, `.css` and `.svg`
  of `dist/`, a host that follows a quote, a bracket, an equals sign or a space, with or without a
  scheme, must be the site's own or in a list of hosts, each with its reason (`www.w3.org`,
  `bugzilla.mozilla.org`, `bugs.webkit.org`, `markjs.io`, `git.io`, `example.com`: the six the
  reviewer found, and the six this build has). The exact-URL list for JavaScript stays. Proved by
  injecting the reviewer's three cases into a built `dist/`: `fetch("//evil.example/x")` in a
  script, `image-set("https://evil.example/a.png" 1x)` in a stylesheet and
  `<image href="https://evil.example/a.png"/>` in `favicon.svg`. Three problems, one per file, exit
  1; restored, it passes: `51 files read (29 html, 6 css, 13 js, 3 svg)`.
- **N2, Node pinned exactly.** `site/.nvmrc` is `22.23.4`; the workflow reads it with
  `node-version-file` and no `check-latest`.
- **N3**: `persist-credentials: false` on the checkout.
- **N4**: the artifact is uploaded only on `main`, the same condition as the deploy job.
- **N5**: the header comment of `collect.mjs` says the line-based scan's limit.
- **Not a finding, a Content-Security-Policy in a `<meta>`**: proposed as a follow-up task, not
  done here.

### All three reviews clear (2026-11-11)

| Review | File | Verdict |
| --- | --- | --- |
| Design, `umbral-designer` | `review.md` | Clear to merge at `cb75872` |
| Documentation, `umbral-documentation` | `review-docs.md` | Clear to merge at `cb75872` (`d2dab14`); still clear at `5155151` (`eaa2757`) |
| Code, `umbral` | `code-review.md` | Clear to merge at `5155151` (`1a33b45`) |

Nothing changed under `site/`, `docs/`, `README.md` or `.github/` after `5155151`: the two commits
on top of it are the reviewers' own files. The branch is not pushed and not merged. What remains
is "The maintainer's steps" above: merge, turn Pages on once, run the `pages` workflow by hand the
first time, and look at the deployed site. Two follow-up tasks wait for the site to be live:
`hhww` (the Documentation link, the UI strings and the log message that cite documentation) and
`fqjn` (a Content-Security-Policy meta element on the site).

## After the first deploy: the link preview (2026-11-11)

The site is live at `https://cosmic-chimps.github.io/umbral/` (merged as `b9c0432`). The maintainer
shared the link in Slack: the preview had the title and the description, an empty grey box where
the picture goes, and no icon.

- **Cause**, read from the deployed page: Starlight writes `twitter:card` as `summary_large_image`
  but no `og:image` or `twitter:image`, and the only icon is `favicon.svg`, which chat and social
  apps do not draw.
- **Fix**, in `site/` only: `public/og.png` (1200 by 630), `public/favicon-32.png` and
  `public/apple-touch-icon.png` (180), named in the pages' `head` by `astro.config.mjs` (six meta
  elements and two link elements; still no script). The picture's address is absolute and built
  from `site.config.mjs`, so a custom domain changes it too.
- **The picture** is `site/social/card.html` drawn by Chrome: `Mark.full` in the dark theme's
  colours, the lowercase wordmark in the display face at 800, and the footer's own line,
  "Self-hosted passwordless sign-in", in the muted text colour on ink. Nothing else. It is a
  proposal for the designer, like the theme was.
- **The icons** are `public/favicon.svg` as PNG, made by `site/scripts/preview-icons.mjs` with
  `sharp`, which Astro already installs (an optional dependency in the lockfile), so no dependency
  is added. Neither the picture nor the icons are made in the build; the PNG files are committed,
  and `site/README.md`, "The link preview", says how to make them again. The picture's command was
  run as written and gave a file identical to the committed one. A first version of the README
  made the icons with headless Chrome too; that is wrong (Chrome will not open a window as small
  as 180 pixels and the icon came out cropped), which is why there is a script.
- **Checked**: the build passes, `check-dist` is clean, and all 29 built pages carry the tags
  (`og:image` is `https://cosmic-chimps.github.io/umbral/og.png`).
- **Not checked**: Slack itself. It can only read the deployed site, and it keeps a preview for a
  while, so after the deploy share the address with `?v=2` to see the new one at once. Whether
  Slack also shows the small icon beside the site's name from a PNG `link rel="icon"` on a project
  site (it cannot be at the host's root, `/favicon.ico`) is to be seen after the deploy.
