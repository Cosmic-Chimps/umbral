# The documentation site

This folder builds <https://cosmic-chimps.github.io/umbral/>, the documentation of Umbral, with
[Astro Starlight](https://starlight.astro.build/). The pages are not written here. They come from the
Markdown in the repository (`README.md`, `docs/`, `samples/README.md`, `CHANGELOG.md`), which reads
the same on GitHub, and `scripts/collect.mjs` turns it into `src/content/docs/` at build time. That
folder is generated and ignored, so to change a page you edit its source Markdown, and nothing here.

## Build and preview

You need the Node version in `.nvmrc` (22.23.4; Astro asks for that or later). The workflow reads
the same file, so CI and a local build run the same Node.

```sh
cd site
npm ci
npm run build     # collect, then astro build, then the check of dist/
npm run dev       # collect, then the dev server (restart it to pick up a changed source)
npm run collect   # the collect script alone
```

`npm run build` leaves the site in `dist/`; `npm run preview` serves it.

## Where things are

| Path | What it is |
| --- | --- |
| `site.config.mjs` | The address of the site and of the repository. The first two lines are the only ones to change if the site moves to a domain of its own. |
| `sidebar.mjs` | The sidebar, and with it the list of pages. |
| `scripts/collect.mjs` | Cuts the Markdown into pages, writes their frontmatter and rewrites their links. It copies the images the pages show from `docs/images/` into `public/images/`. It leaves out a walkthrough's "Back to the README. Other providers: …" paragraph, which is for readers on GitHub: here the sidebar does that. |
| `astro.config.mjs` | Starlight's settings, and the site's `head` entries: the Content-Security-Policy and the link preview (both below). |
| `scripts/check-dist.mjs` | Reads the built `dist/` and fails if it asks another host for anything, if an image of the site is not in `dist/`, or if a page is without the policy. |
| `src/styles/umbra.css` | The look: Umbral's colours and fonts, through Starlight's custom properties. The one stylesheet. |
| `src/assets/`, `public/favicon.svg` | The mark, for the dark and the light theme, and the favicon. |
| `social/card.html`, `public/og.png`, `public/favicon-32.png`, `public/apple-touch-icon.png` | The link preview: the picture and the icons a chat or social app shows beside a link to the site. See below. |
| `scripts/screenshots/` | The script that makes the screenshots in `docs/images/`. See below. |

The fonts are Umbral's own, copied from `src/Umbral/Assets/fonts/` by the collect script into
`src/fonts/` and `public/licenses/fonts/`. The images are the ones the pages reference, copied from
`docs/images/` into `public/images/`. All three are generated and ignored; the other files in
`public/` are committed.

## The link preview

A chat or social app that shows a link to the site draws a picture (`og:image`) and an icon. It
does not draw an SVG, so both are PNG files, committed in `public/`, and `astro.config.mjs` names
them in the page's `head` (meta and link elements only, no script). The picture's source is
`social/card.html`: the mark, the wordmark and the footer's line, in the dark theme's colours and
the shipped fonts.

To make the picture again after changing the card, with any Chrome or Chromium, from `site/`
after `npm run collect` (the card reads the fonts from `src/fonts/`):

```sh
chrome --headless --hide-scrollbars --virtual-time-budget=2000 --window-size=1200,630 \
  --screenshot=public/og.png social/card.html
```

To make the two icons again after changing `public/favicon.svg` (it uses `sharp`, which Astro
installs; if a later Astro no longer does, `npm install --no-save sharp` first):

```sh
node scripts/preview-icons.mjs
```

Neither runs in the build: the three PNG files are committed.

An app keeps a preview for a while after it first fetched a link, so a changed picture shows only
later, or at once on an address it has not seen (add `?v=2`).

## The screenshots

The pictures in the README and under `docs/` are the PNG files in `docs/images/`. They are
committed, and a script makes them, never a person, so after a change of the UI you run it again:

```sh
site/scripts/screenshots/run.sh     # or, from site/: npm run screenshots
```

It needs Docker with Compose and nothing else, and takes about half a minute once the two images
are built. It builds Umbral from the checkout, starts a throwaway instance with the quick start's
settings, claims it, and sets it up through the real UI in a headless browser: three connectors
with dummy credentials, the passkey and Fediverse connectors, an app, its branding, a person and
an operator passkey from a virtual authenticator. It captures eight screens, each in the dark and
the light theme at twice the pixel density, shrinks them to 256-colour PNG files and copies the
sixteen files to `docs/images/`. Then it removes the instance: no container and no volume is left.

| File | What it is |
| --- | --- |
| `run.sh` | The one command. Builds, starts, claims, runs the browser container, copies the files, tears down. |
| `docker-compose.yml` | Umbral and the browser in one network namespace, so everything is `http://localhost:8080`, as in the quick start. |
| `capture.mjs` | The walk, and at its top the framing of each screen: the viewport, and the whole page or the elements to cut out. |
| `optimise.mjs` | Shrinks the captures, and fails the run when a file is over 200 KB or the set over 1.5 MB. |

What the pictures show is made up: the app `Larder`, the person `Sam Rivera`, the handle
`@ana@social.example`. The client secret in `app-created` belongs to the instance the run
deletes. The instance never has the fake connector of the samples harness, and the script stops
if the **Provider** list offers it.

A second run gives the same bytes for every screen whose content is the same. Three screens
change without a change of the UI: `app-created` on every run (the client ID and the secret are
random), and `your-passkeys` and `operator-credential` when the day does (they show the date
the passkey was added). So after a run, `git status` lists those files too: commit the files of
the screens you changed, and put the others back with `git checkout docs/images/`.

A page shows a screen in both themes with a `<picture>` element, which GitHub and the site both
follow by the system's theme, and says what the picture shows in its `alt`:

```html
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="images/sign-in-dark.png">
  <img alt="What the picture shows." src="images/sign-in-light.png" width="640">
</picture>
```

The site's own **Theme** select does not change the picture: it follows the system. To add a
screen, add its step to the walk and its entry to `FRAMES` in `capture.mjs`, raise the count in
`optimise.mjs` and `run.sh`, and reference the two files from a page.

## What fails the build

- A link in a source that does not become a working link on the site: a file that does not exist, an
  anchor that is not a heading. The message is `source:line: target: reason`. A link to a file that is
  not a page (a sample, a spec, `docker-compose.yml`) becomes a link to it on GitHub.
- An image a page shows (`![alt](path)`, `src`, `srcset`) that is not a file under `docs/images/`.
  Those images are copied to the site and the page shows the copy, so nothing is loaded from GitHub;
  a plain link to an image under `docs/images/` goes to the copy as well. A `srcset` holds one
  address with no descriptor (no comma, no `2x`).
- A `## ` section in `README.md` that `collect.mjs` does not know, or a published one that is missing.
- A page that is not in `sidebar.mjs`, or an entry of it for a page that does not exist.
- A source that does not start with a `# ` title, or that carries frontmatter.
- A link the built pages do not resolve (`starlight-links-validator`, a second check).
- A link preview picture (`og:image`, `twitter:image`) that is not on the site's own host, or that
  `dist/` does not hold at that address.
- An `<img src>` or `<source srcset>` on the site's own host that `dist/` does not hold.
- Anything in `dist/` that asks another host for a file: an external script, stylesheet, font or
  image, a new URL in the JavaScript that `check-dist.mjs` has not been told about, or a host it
  does not know named in a script, a stylesheet or an SVG file.
- A page without the Content-Security-Policy element, one where a script or a stylesheet comes
  before it, or a policy that allows a host, a scheme other than `data:`, or a keyword other than
  `'self'`, `'none'`, `'unsafe-inline'` and `'wasm-unsafe-eval'`. A hash or a nonce is accepted.

## The Content-Security-Policy

The site asks no other host for anything. `check-dist.mjs` proves that of the built files; the
policy has the browser refuse it while a page runs, which also covers what a scan of files cannot
see, such as a URL a script puts together at run time.

GitHub Pages sends no header of ours, so the policy is a `<meta http-equiv="Content-Security-Policy">`
in every page. It is the first `head` entry in `astro.config.mjs`. The site was built without any;
the ones it has now are this one and the link preview's, which are meta and link elements only.
Another entry needs as good a reason, and a script or a stylesheet from another host would be
refused by this one anyway.

```text
default-src 'self';
script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval';
style-src 'self' 'unsafe-inline';
img-src 'self' data:;
base-uri 'none';
form-action 'self'
```

Each exception to `default-src 'self'` is something a browser refused without it:

| Source | What needs it |
| --- | --- |
| `'unsafe-inline'` in `script-src` | Starlight's inline scripts, such as the one that sets the theme before the first paint and the ones that restore the sidebar's state. |
| `'wasm-unsafe-eval'` in `script-src` | Pagefind, the search, is WebAssembly. It runs in a worker; when the worker cannot start, it runs on the page, and the search finds nothing without this. |
| `'unsafe-inline'` in `style-src` | The `style` attributes Starlight and the code blocks write (icon sizes, token colours), and the `<style>` elements of the dev server. |
| `data:` in `img-src` | The icons the stylesheets hold as `data:` URLs. |

`base-uri` and `form-action` are the two directives `default-src` does not cover.

What the policy does not do:

- It does not stop an inline script from running: `'unsafe-inline'` is there. It stops what such a
  script would load or send to another host.
- A worker takes its policy from its own response, not from the page, and Pages sends none. Firefox
  and WebKit follow that, so Pagefind's worker is outside the policy there; Chrome applies the page's
  policy to what a worker fetches. `check-dist.mjs` reads the worker's file like any other.
- A `meta` element cannot carry `frame-ancestors`, `sandbox` or a report address. Those need a header.

After a change to the policy, or an update of Starlight, Astro or Pagefind, build, run
`npm run preview` and look at the site in a browser with the console open: every page in the dark
and the light theme, the theme select, the menu at a narrow width, and a search. The console must
show no violation. Do the same once with `npm run dev`.

## The look in numbers

The pairs of text and background of the Umbra mapping, as contrast ratios (WCAG, 4.5:1 for text):

| Pair | Dark | Light |
| --- | --- | --- |
| Text on the page | 16.02 | 16.00 |
| Text on the header and sidebar | 14.94 | 16.87 |
| Text on inline code | 10.86 | 14.38 |
| Muted text on the page | 7.92 | 5.89 |
| Muted text on the header and sidebar | 7.38 | 6.21 |
| Muted text on inline code | 5.37 | 5.29 |
| Link text on the page (accent-high in the dark theme, accent in the light) | 11.95 | 5.25 |
| Link text on the header and sidebar | 11.14 | 5.54 |
| Link text on inline code | 8.10 | 4.72 |
| The current sidebar entry: link text on its tonal step (`#1d1d2b`, `#ebe5d8`) | 10.14 | 4.72 |
| The accent as text on the page | 5.73 | 5.25 |
| The accent as text on the header and sidebar | 5.35 | 5.54 |
| Outline border on the page (3:1 is enough for a border) | 4.13 | 3.58 |
