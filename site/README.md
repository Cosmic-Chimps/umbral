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
| `scripts/collect.mjs` | Cuts the Markdown into pages, writes their frontmatter and rewrites their links. It leaves out a walkthrough's "Back to the README. Other providers: …" paragraph, which is for readers on GitHub: here the sidebar does that. |
| `scripts/check-dist.mjs` | Reads the built `dist/` and fails if it asks another host for anything. |
| `src/styles/umbra.css` | The look: Umbral's colours and fonts, through Starlight's custom properties. The one stylesheet. |
| `src/assets/`, `public/favicon.svg` | The mark, for the dark and the light theme, and the favicon. |
| `social/card.html`, `public/og.png`, `public/favicon-32.png`, `public/apple-touch-icon.png` | The link preview: the picture and the icons a chat or social app shows beside a link to the site. See below. |

The fonts are Umbral's own, copied from `src/Umbral/Assets/fonts/` by the collect script into
`src/fonts/` and `public/licenses/fonts/` (generated, ignored).

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
installs):

```sh
node scripts/preview-icons.mjs
```

Neither runs in the build: the three PNG files are committed.

An app keeps a preview for a while after it first fetched a link, so a changed picture shows only
later, or at once on an address it has not seen (add `?v=2`).

## What fails the build

- A link in a source that does not become a working link on the site: a file that does not exist, an
  anchor that is not a heading. The message is `source:line: target: reason`. A link to a file that is
  not a page (a sample, a spec, `docker-compose.yml`) becomes a link to it on GitHub.
- A `## ` section in `README.md` that `collect.mjs` does not know, or a published one that is missing.
- A page that is not in `sidebar.mjs`, or an entry of it for a page that does not exist.
- A source that does not start with a `# ` title, or that carries frontmatter.
- A link the built pages do not resolve (`starlight-links-validator`, a second check).
- Anything in `dist/` that asks another host for a file: an external script, stylesheet, font or
  image, a new URL in the JavaScript that `check-dist.mjs` has not been told about, or a host it
  does not know named in a script, a stylesheet or an SVG file.

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
