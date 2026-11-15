# Docs screenshots (task v6pm): notes for the reviews

- **Task:** `2026-11-11-docs-screenshots-of-the-sign-in-page-and-the-operator-screen-v6pm`
- **Branch:** `alta/buoyant-taiga-cxt3`, on main `4958e12`. Not pushed, not merged.
- **Commits:** `ea5d6ae` (the site build carries images), `f63eee8` (the script, the sixteen images, their
  places in the docs), then this folder.
- **Reviews asked:** design (`review.md`), documentation (`review-docs.md`), code (`code-review.md`), each in
  this folder. One re-check each.

## What was built

| Part | Where |
| --- | --- |
| The capture script | `site/scripts/screenshots/` (`run.sh`, `docker-compose.yml`, `Dockerfile`, `capture.mjs`, `optimise.mjs`, `in-container.sh`, `package.json` and its lock) |
| The images | `docs/images/`, sixteen PNG files: eight screens, each `-dark` and `-light` |
| Their places | `README.md` (opening, quick start steps 4 and 5), `docs/look.md`, `docs/branding.md`, `docs/connectors/passkey.md`, `docs/connectors/fediverse.md`, `docs/run/operator-passkeys.md` |
| The site build | `site/scripts/collect.mjs`, `site/scripts/check-dist.mjs`, `.github/scripts/check-links.py`, `site/.gitignore`, `site/src/styles/umbra.css` |
| How to run it | `site/README.md`, "The screenshots"; `npm run screenshots` in `site/` |

One command: `site/scripts/screenshots/run.sh`. About 26 seconds once the two images are built.

## The eight screens

| File | What it shows | Frame | Shown in |
| --- | --- | --- | --- |
| `sign-in` | The sign-in page of the app `Larder` with five buttons | whole page, 760 px wide | README opening, `look.md` |
| `sign-in-branded` | The same page with the app's logo and `#d9480f` | whole page, 760 px | `look.md`, `branding.md` |
| `connector-github` | The GitHub pane on a fresh instance: header and **Set-up**, with the redirect URI | the pane's header and Set-up section | quick start step 4 |
| `app-created` | The app's pane right after **Create app**: notice, shown-once warning, secret, client ID, issuer | the pane's header through the Client ID row, viewport 1440 px | quick start step 5 |
| `passkey-step` | **Sign in with a passkey**, invitations only | the card | `passkey.md`, Signing in |
| `your-passkeys` | A person's **Your passkeys** page with one passkey | whole page, 760 px | `passkey.md`, What the person sees |
| `fediverse-step` | **Where is your account?** with `@ana@social.example` typed | the card | `fediverse.md`, How a person signs in |
| `operator-credential` | **Credential**: the Passkeys card with one passkey, and Password sign-in | the heading through the Password sign-in card | `operator-passkeys.md` |

All are captured at twice the pixel density and shown at the `width` of the `<img>` (640, 720, 556 or 448).

## Decisions, and why

- **The script is under `site/scripts/screenshots/`, not `docs/images/capture/`.** `docs/` holds only what a
  reader reads: `collect.mjs` walks `docs/**/*.md` into pages, and a README beside the tooling would become
  one. `site/` is where the documentation's tooling already lives (collect, check-dist, the link preview's
  picture), with a README that says how each picture is made.
- **No fake connector at all.** The task allowed it for the screens that need a signed-in person. It was not
  needed: the operator invites a person on the Passkey connector, and the person redeems the link with a
  Chrome virtual authenticator, which lands them on **Your passkeys**. So the instance runs with the quick
  start's three settings and nothing else, `UMBRAL__ALLOW_FAKE_CONNECTOR` still appears only in
  `samples/test/docker-compose.yml`, and the script stops if the **Provider** list offers `fake`.
- **Dark and light for all eight.** The set is 609 KB (budget 1.5 MB; the largest file is 55 KB, limit
  200 KB), so no screen had to be one theme only, and a reader on a dark page never gets a light picture.
- **The theme is the system's, emulated; nothing on the page is edited.** With no `umbral_theme` cookie
  Umbral follows `prefers-color-scheme`, so the script uses Playwright's `emulateMedia`. That switches a page
  without a reload (the app's secret is shown once), and the theme control in the pictures truthfully says
  "Following your system." The script fails if the two themes come out the same.
- **Operator screens are cut to the part the text is about.** A whole operator page is 1180 px wide and up to
  2141 px tall; at the width of a README column its text is too small to read. The cut ends on an element's
  edge, never through a line.
- **`app-created` is taken at 1440 px.** At 1180 and 1280 the **Client ID** or **Issuer URL** field is too
  narrow for its value and cuts it. The script picks the smallest of 1180, 1280, 1440, 1600 at which both
  show whole.
- **Steady states.** `your-passkeys` and `operator-credential` are taken after a reload, without the one-time
  notice: the page as its owner finds it later. (On **Your passkeys** the notice "Your passkey is ready." is
  drawn as an error; that is a product bug, filed as follow-up task `njtn`, and not something to publish.)
- **PNG, 256-colour palette, no dither** (sharp, in the browser container). The screens are flat colour and
  text; I saw no banding. WebP was not needed for the budget, and PNG shows everywhere.
- **Only `docs/images/` is published.** `collect.mjs` copies the images the pages reference, and only those,
  to `public/images/`; an embedded image from anywhere else in the repository stops the build. One place for
  pictures, and a page cannot quietly load one from GitHub.
- **A border on the site.** A light screenshot on the light site has the page's own colour, and its name bar
  and Theme control read as the site's. `umbra.css` gives `picture img` in a page a 1 px border in the
  "borders that must be seen" tone and a radius. GitHub cannot be styled; there the page is white or
  `#0d1117`, a step away from Umbral's paper and near-black.
- **No `changelog.d/` note.** Nothing changes for whoever runs Umbral: no code of the product is touched.

## Acceptance

| Item | Result |
| --- | --- |
| The script regenerates every image | **Byte-for-byte** for 14 of the 16 files between two runs on this machine (arm64, 2026-11-15). `app-created-dark/light` differ on every run: the client ID and the secret are random. `your-passkeys` and `operator-credential` show the date the passkey was added, so they change when the day does. Those four are **visually identical** apart from those values. On another CPU architecture I expect visually identical, not byte-for-byte; not tried. |
| No container or volume left | After a run: no container and no network named `umbral-screenshots-*`, no volume. The two images (`umbral:screenshots` and the browser's) stay, as the samples harness's do. |
| Every image has an `alt` | Ten `<picture>` blocks, each with an `alt` of 158 to 210 characters that says what the picture shows. |
| Pairs switch with the system theme | On the built site: `site-capture.mjs` here loads the seven pages with the system set to dark, then light, and checks that each picture loaded the file of that theme (20 of 20). On GitHub: the same `<picture>` form GitHub documents; not seen there, since the branch is not pushed. |
| No fake connector, real domain, real person, provider console | No fake connector (see above). The data is made up: `Larder`, `Sam Rivera`, `@ana@social.example`, `http://localhost`. **One thing to rule on:** the Fediverse step's own hint reads "A handle like @you@mastodon.social, or a server like mastodon.social." That is the product's text, quoted as is in `docs/connectors/fediverse.md`; the picture shows it because the page does. |
| `check-links.py` | `check-links: 454 links in 40 files, all resolve` |
| `npm run build` in `site/` | `collect: 32 pages … 3 fonts and 16 images copied`; `All internal links are valid`; `check-dist: 55 files read …, nothing requested from another host` |
| The images appear on the built site | `dist/images/` holds the 16 files; captures of four pages in `site/` here, both themes. |
| `docs/images/` under 1.5 MB | 609 KB |

## Known limits

- **The site's Theme select does not change a picture.** `<picture>` follows the system, as on GitHub. A
  reader whose system is dark and who picks Light on the site sees dark screenshots on a light page.
- **Quick start step 4's list is now a loose list** (a blank line before the picture), so its five items get
  paragraph spacing on GitHub and on the site.
- **The script is not run in CI.** Like the link preview's picture, the files are committed. A change of a
  label the walk clicks would only show the next time someone runs it; it then fails with the page's text.
- **`capture.mjs` finds the Client ID row by its label text**, the one frame selector that is not an id or a
  `data-*` attribute, because that row has neither.
- **Provider marks.** Only GitHub's button carries its mark; Discord and Google show the generic link icon, as
  a fresh instance does.

## What each review should rule on

- **Design (`umbral-designer`):** which eight screens (swap one if another shows more); the framing of each,
  above all the three cut operator screens and whether the sign-in page should be the whole page (name bar,
  footer, Theme control) or the card; the made-up logo; the display widths; the border on the site and
  whether GitHub needs anything; the `mastodon.social` hint.
- **Documentation (`umbral-documentation`):** where each picture sits; the ten `alt` texts; the new sentence
  in `docs/look.md` ("An app can show its own logo and colour."); `site/README.md` ("The screenshots", the
  collect and check-dist rows, "What fails the build"); the loose list in step 4.
- **Code (`umbral`):** `capture.mjs` (the walk, the frames, that the secret is never read or logged),
  `run.sh` and the Compose file (teardown, no fake connector), `optimise.mjs`; `collect.mjs` (`imageHref`,
  `endsImageAlt`, the `srcset` rule), `check-dist.mjs` (`checkOwnImage`), `check-links.py` (`srcset`).

## After the reviews (2026-11-15)

All three reviews are clear with no blocking item: design (`review.md`, two nits), documentation
(`review-docs.md`, `dcc6021`, four nits), code (`code-review.md`, `6486058`, two nits). Taken:

| Nit | What changed |
| --- | --- |
| Design 1, uneven letter spacing ("T heme") | `capture.mjs` launches Chromium with `--font-render-hinting=none`; all sixteen images retaken. The set went from 506 KB to 609 KB (more antialiasing tones). |
| Design 2, the logo reads as a sixth button | The jar is drawn on a transparent background, with an amber lid. |
| Design, the secret in `app-created` | A line under the picture in the README says the secret belonged to a throwaway instance. |
| Docs 1, `look.md`'s picture followed the side rail | The first paragraph is split: the look and the mark, the picture, then the operator screens. |
| Docs 2, the demo's "Try it" line | Nothing to change here. When fzzx step 10 adds the line to the README, it goes after the first paragraph and before this picture. |
| Docs 3, commit then checkout | `site/README.md` now gives the two steps in order. |
| Docs 4, the Theme select | Stays a stated limit. |
| Code N1, the failure screenshot's guard | Skipped when the page holds `[data-secret-once]`, whatever step failed. |
| Code N2, SVG | Dropped from `IMAGE_FILE` in `collect.mjs`; `site/README.md` says why. |

Left for `umbral-designer`, outside this task (seen by the documentation review): on the passkey step
**Continue with your passkey** carries the link icon, while **Sign in with a passkey** carries the key.

After these: `run.sh` exit 0, `check-links.py` passes, `npm run build` passes with 16 images copied and
nothing requested from another host. Design re-check asked on `sign-in-branded-light` and `app-created-dark`.

## Files here

- `site-capture.mjs`: loads the built site's seven pages in both themes, checks every picture, captures.
- `site/`: four of those pages, dark and light (index, quick start, How it looks, Passkeys for the admin area).
