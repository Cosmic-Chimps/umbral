# Design review: the docs screenshots

Task v6pm. Branch `alta/buoyant-taiga-cxt3`, `ea5d6ae..881d768`. Reviewed 2026-11-15 by the
design reviewer from `notes.md`, three of the sixteen images (`sign-in-branded-light`,
`app-created-dark`, `fediverse-step-light`) and one of the eight site captures
(`quick-start-light`). The other images were not opened, and the script was not run.

## Verdict

**Clear to merge. No blocking items; two nits.**

## Your six points

1. **Which screens**: the right eight. Each shows the thing its page is about, and no page
   carries a picture for decoration.
2. **Framing**: right. Operator screens cut to the part the text names, ending on an element's
   edge; the two steps as their card; the sign-in pages whole, which keeps "Secured by Umbral"
   and the theme control in the picture, the two things `look.md` and `branding.md` talk about.
3. **The made-up logo**: fine as a stand-in. Nit 2 below.
4. **Display widths** (640, 720, 556, 448): fine. In `quick-start-light` the two pictures sit
   inside the text column and read at that size.
5. **The border and radius on the site**: right; it separates a light picture from a light
   page without a shadow. On GitHub nothing more is needed: paper against GitHub's white is
   enough of an edge, and the dark picture has its own.
6. **`mastodon.social` in the Fediverse step's hint**: leave it. The picture shows the product
   as it is; the rule against real domains is about what the script types, and it types
   `@ana@social.example`.

Dark and light for all eight, the theme emulated from the system, and steady states taken
after a reload: all right. The secret in `app-created` belongs to a throwaway instance; say in
the README, beside the picture, that it is not a live one, if it does not already.

## Nits

1. **Letter spacing in the captures.** In `sign-in-branded-light` and `app-created-dark` some
   words are spaced unevenly ("T heme", "reg enerate", "applic ation"): the capture browser is
   hinting glyph positions to whole pixels. Launch it with font hinting off and subpixel
   positioning on (Chromium: `--font-render-hinting=none`, and
   `--enable-font-antialiasing` if needed), and retake. The pictures are the product's face in
   the README; the type should look as it does on a real screen.
2. **The stand-in logo is a hard-edged block in the accent's own colour.** Above five buttons
   of the same orange it reads as a sixth block. Draw the jar on a transparent background (or
   give the square rounded corners and a colour other than the accent) so it reads as a logo.

## Re-check

`sign-in-branded-light` and `app-created-dark` after the retake. One look.
