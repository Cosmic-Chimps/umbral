# Design review: the look of the documentation site

Task hmfr. Branch `alta/roaming-foxglove-m2q5`, `ca96f84` (the site) and `65a2f69` (notes,
captures). Reviewed 2026-11-11 by the design reviewer from `site/src/styles/umbra.css`, the
config, the Impeccable detector and the 13 captures. Mode: **Read**. The reader is an operator
half-way through a set-up; the site's job is that they find the step and read it without the
chrome asking for attention. This file is also the short design note the maintainer asked for:
the four rulings below are that note.

Building before asking was fine in the circumstances, and building it as one stylesheet of
Starlight's custom properties was the right way to make that cheap.

## Verdict

**Re-check, 2026-11-11, at `cb75872`: clear to merge.** All four items are in `umbra.css` as
asked and seen in the retaken captures: the current sidebar entry is the rail's (a tonal step
with gold text, both themes; `start-1280-dark`, `connector-discord-1280-light`); h2 is the
display face at 24px/32px, so the page title leads; the header mark is 28px beside the
wordmark; the search field's focus ring is gold (`search-1280-dark`). Keep both the gold
border and the gold ring on that field: the ring is the site's focus language, and the border
is Starlight's own. Code blocks stay on Starlight's themes. The design side is done; the docs
and code reviews are the other gates.

First review at `65a2f69`: **not yet clear: one blocking item, three nits.** All four are a few lines in `umbra.css`. No
component, layout or script is needed for any of them, so the maintainer's constraint holds.

## What is right and stays

- Colours taken from the product's tokens through Starlight's grey scale and accent set, both
  themes, with the roles of the light theme's inverted scale written beside each step.
- The three shipped faces through the product's own `@font-face` blocks, copied at build time,
  nothing from another host.
- The hairline on the fifth grey step in both themes; Starlight's own dark hairline would
  vanish on ink. A correct departure.
- The lowercase wordmark by CSS, in the text colour; the Gnomon in its own colours per theme,
  never the accent's; the favicon tile.
- Links underlined in the theme's gold; inline code on the hairline tone; the "On this page"
  current entry in gold text.
- Contrast table in `site/README.md`, lowest pair 4.72.
- Detector on `umbra.css`: no findings.
- 360: no overflow, the title wraps well, the code block scrolls inside itself.

## The four questions

1. **The current sidebar entry: not a fill.** Blocking, item 1 below. A navigation highlight
   is not the Fill Pair's business at all: that rule is about the primary button. The product
   already says how the current place looks (DESIGN.md, Navigation): the high tonal step with
   the label in gold at 600. The docs sidebar should be the product's rail.
2. **Code blocks: leave Starlight's themes.** Their frames already take the site's greys, the
   syntax colours are content, not accent, and Expressive Code holds them to its own contrast
   floor in both themes. A hand-made syntax theme is a maintenance cost with no reader benefit.
3. **The display face on h2: yes, at the product's size.** Nit 2 below. DESIGN.md gives the
   display face two sizes, display and headline large (24px), and that is where h2 belongs.
4. **The header mark: the right drawing, a little large.** Nit 3 below. `Mark.small` is the cut
   for this size.

## Blocking

1. **The current sidebar entry is a filled gold slab.** In `start-1280-dark` it is the
   brightest thing on the page, brighter than the page title, on every page; in the light theme
   (`connector-discord-1280-light`) it is `#8a5a00` with light text, a pair that exists nowhere
   in the product. One accent used sparingly is the system's first characteristic. Make it the
   rail's current entry:

   ```css
   /* The current page, as the product's rail draws it: a tonal step and gold
      text, never a fill. */
   .sidebar-content a[aria-current='page'],
   .sidebar-content a[aria-current='page']:hover,
   .sidebar-content a[aria-current='page']:focus {
     background-color: var(--umbra-current-bg);
     color: var(--sl-color-text-accent);
     font-weight: 600;
   }
   ```

   with `--umbra-current-bg: #1d1d2b` on `:root` and `#ebe5d8` under `[data-theme='light']`
   (the product's `surface-container-high` in each theme). Gold `#f5c26b` on `#1d1d2b` and
   `#8a5a00` on `#ebe5d8` are both existing pairs; the second is the 4.72 already in your table.
   Check the selector against the built markup, and add the two pairs to the contrast table.

## Nits

2. **h1 and h2 are nearly the same size, both at 800.** In `connector-discord-1280-light` the
   title and "Before you start" differ by a few pixels, so a page with eleven sections reads as
   eleven titles. Keep the display face on h2 and set it to the product's headline large:
   `font-size: 1.5rem; line-height: 2rem;` on `.sl-markdown-content h2`. The page title then
   leads, sections mark, h3 stays in the text face at 600.
3. **The header mark is taller than the wordmark beside it.** In the product's lockup the mark
   sits at about the wordmark's line height. Bring the logo to `1.75rem` high
   (`.site-title img { height: 1.75rem; width: auto; }`), so the lockup reads as in the rail.
4. **The search field's focus outline is blue** (`search-1280-dark`), the one colour on the
   site that is neither a tone, gold, nor a status. Find where it comes from (the browser's own
   ring on the Pagefind input, or a Pagefind variable) and make it the accent:
   `outline-color: var(--sl-color-text-accent)` on that input's `:focus-visible`, or the
   Pagefind variable if that is the source.

## Outside the look, for the documentation reviewer

`connector-discord` opens with "Back to the README. Other providers: …". On the site there is
a sidebar for both, and "the README" is no longer where the reader came from. That line is the
docs review's to rule on, not mine.

## Re-check

Recapture `start` and `connector-discord` at 1280 in both themes and `search-1280-dark`, and
send the commit. One look.

## The link preview (added 2026-11-11, at `cebbe8e`)

**Clear to merge. One optional nit.** Looked at `site/public/og.png` (1200 by 630),
`apple-touch-icon.png` (180), `favicon-32.png` and the head tags in `astro.config.mjs`.

The three questions:

1. **One dark card for both of a reader's themes: yes.** Ink is the identity's first face and
   its fallback theme, and a preview sits in someone else's interface, light or dark; ink with
   bone holds in both. No paper variant.
2. **The line: the footer's words, as built.** The card is shared by every page; the preview's
   own title and description already say which page and that it is documentation. "Self-hosted
   passwordless sign-in" says what Umbral is, from the catalogue.
3. **The mark's size and gap: right.** The full cut with the base line is the correct drawing
   at 200px, its base sits on the wordmark's baseline, and the gap reads as the product's
   lockup scaled up. The only gold is the sun.

The icons are the favicon tile unchanged, in the bold cut, which is the cut for those sizes.
The alt text and the declared dimensions are right.

Nit (optional): the tagline starts at the 96px padding, but the mark's base line starts about
20px further in, because the drawing has its own inset in its 48-unit box. Pull the mark left
by that inset (a negative margin of about 20px on the mark in `card.html`) so the base line and
the tagline share a left edge.
