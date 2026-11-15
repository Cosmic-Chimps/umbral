# Design review: the public demo's landing and privacy pages

Task fzzx. Pages in `/Users/jjchiw/Documents/code4fun/umbral-demo` at `7efe66b`
(`site/public/index.html`, `privacy/index.html`, `style.css`). Reviewed 2026-11-15 by the
design reviewer from the two pages' markup, the stylesheet's tokens and three of the eight
captures (`index-dark-desktop`, `index-light-phone`, `privacy-light-desktop`). `404.html` was
not opened, and the pages were not run. Whether each privacy statement is true of the deployed
instance is not something this review can check.

The landing page is a small Persuade surface; the privacy page is Read. Both should carry
Umbral's identity as PRODUCT.md binds it: the lowercase wordmark, the Gnomon in its own
colours, the Umbra look in both themes.

## Verdict

**Not yet clear: three blocking items, three nits.** All are in `style.css` and the two
lockups. The structure, the order and nearly all of the copy stand.

## What is right

- One column, short measure, one gold action. The page says what this is in its first
  sentence and ends its lead with "That is the whole demo."
- The reset is stated on the landing page before anyone signs in, in a warning-toned note,
  and again on the privacy page with the weekly copy spelled out.
- The privacy page answers what, how long, who else, in that order, in plain sentences, and
  tells a Fediverse user what their own server will show them, including the double listing
  after a reset.
- No script, nothing from another host, fonts served by the site.
- Dark scheme: the tokens are the product's.

## Blocking

1. **The light scheme is derived, not the product's.** Use the light table from
   `src/Umbral/Assets/tailwind.config.js`: page `#f4f1ea`, container `#faf7f0`, high `#ebe5d8`,
   highest `#e3ddd0`, text `#17151f`, muted `#5f5a6b`, outline-variant `#ddd6c8`, link gold
   `#8a5a00`, warning text `#6b4500` on `#fbefd6` with border `#e2c27f`. Your gold for links is
   already right; the surfaces and text are close but not the same.
2. **The light button is a brown fill.** DESIGN.md's Fill Pair rule: in the light theme the
   primary button is ink `#17151f` with gold text `#f5c26b`; `#8a5a00` is for links. Split the
   variable: `--button-fill` / `--button-text` (dark `#f5c26b` / `#2a1d05`; light `#17151f` /
   `#f5c26b`), and make the hover a slight brighten in both, not a second colour.
3. **The lockup is not Umbral's.** Two things in `.mark`:
   - The wordmark is the text face in muted grey. It is "umbral": Bricolage Grotesque 800,
     -0.035em, lowercased by CSS, in the text colour (so the markup keeps "Umbral"). On the
     privacy page, "umbral" as the wordmark and "demo" after it in the text face, muted.
   - The Gnomon's colours are written into the SVG, so the light page shows the dark theme's
     pale sun and shadow on paper. Give the parts classes and set them per scheme, as the
     product does: dark sun `#f5c26b`, shadow `#a7a2b6` at 0.5, upright `#ece8df`; light sun
     `#b8801f`, shadow `#5f5a6b` at 0.4, upright `#17151f`.

## Nits

4. **The five ways read as buttons.** The pills under "Five ways in" look pressable and do
   nothing. Make them a plain sentence or a plain list: "GitHub, Google, Discord, a Mastodon or
   GoToSocial account, or a passkey."
5. **Code is in the system mono.** The product's third face is Spline Sans Mono. For six short
   tokens the saving is fair; if you keep the system face, say so in `notes.md` as a choice.
6. **Copy, three small points.** (a) "Cancel at the provider, or refuse, and you land back on
   the sign-in page": "cancel" and "refuse" are the same act; "Cancel at the provider and you
   land back on the sign-in page." (b) The note's bold sentence carries the whole warning
   colour; keep the bold, in the text colour, and let the note's tone do the warning, as the
   product's alerts do. (c) The privacy lead's "It is short because there is little to tell."
   is the one sentence that performs; the page already shows it.

## The copy's tone

It reads as Umbral's docs do: full sentences, what it is and what it is not, the next action
stated. Nothing to change beyond nit 6.

## Re-check

`index` and `privacy` in the light scheme at desktop and phone, and `index-dark-desktop` for
the lockup. One look.

## Re-check, 2026-11-15, at the demo repository's `dc2e695`

**Clear.** Looked at `index-light-desktop`, `privacy-light-phone` and the lockup in
`index-dark-desktop`; the other captures and `404.html` were not opened, and the site was not
run.

- Item 1: the light page is the product's paper, text and gold.
- Item 2: the light button is ink with gold text; the dark one gold with dark text.
- Item 3: the lockup is the lowercase display wordmark in the text colour, with "demo" muted
  beside it on the privacy page, and the Gnomon takes each scheme's own colours.
- Nits 4 to 6: taken as described; the five ways are a sentence, the note's bold is in the text
  colour, the system mono is recorded as a choice.

**The rewritten privacy paragraphs, for tone**: they read as the rest does, plain and exact.
One optional point: "For how long" is now one long paragraph carrying three different things.
Split it in three: the nightly reset; the weekly copy; the console output the host keeps. The
sentence about console output would then read better as two: what it holds, then how long it
is kept. Whether each statement is true is the documentation and code reviews', not mine.
