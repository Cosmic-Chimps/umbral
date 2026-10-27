---
name: Umbral
description: The Umbra look, warm gold on ink or on paper, with the Gnomon as its mark.
colors:
  # Dark theme, written on :root (the fallback). Light values are in the prose and
  # in tailwind.config.js, which is the normative source for both tables.
  ink: "#0b0b14"
  ink-lowest: "#07070e"
  ink-low: "#14141f"
  ink-container: "#181826"
  ink-high: "#1d1d2b"
  ink-highest: "#26263a"
  bone: "#ece8df"
  bone-muted: "#a7a2b6"
  outline: "#74708a"
  outline-variant: "#2e2e40"
  corona-gold: "#f5c26b"
  on-corona-gold: "#2a1d05"
  gold-container: "#3a2e12"
  success: "#a8d99a"
  success-container: "#20301f"
  warning: "#f0b45e"
  warning-container: "#2a2111"
  warning-outline: "#6b5328"
  error: "#ff8f7e"
  on-error: "#3a0b05"
  error-container: "#3a1613"
  on-error-container: "#ffdad4"
typography:
  display:
    fontFamily: "Bricolage Grotesque, Schibsted Grotesk, ui-sans-serif, system-ui, sans-serif"
    fontSize: "34px"
    fontWeight: 800
    lineHeight: "40px"
    letterSpacing: "-0.035em"
  headline-lg:
    fontFamily: "Bricolage Grotesque, Schibsted Grotesk, ui-sans-serif, system-ui, sans-serif"
    fontSize: "24px"
    fontWeight: 800
    lineHeight: "32px"
    letterSpacing: "-0.03em"
  headline-md:
    fontFamily: "Schibsted Grotesk, ui-sans-serif, system-ui, sans-serif"
    fontSize: "20px"
    fontWeight: 600
    lineHeight: "28px"
    letterSpacing: "-0.01em"
  body-lg:
    fontFamily: "Schibsted Grotesk, ui-sans-serif, system-ui, sans-serif"
    fontSize: "16px"
    fontWeight: 400
    lineHeight: "24px"
  body-md:
    fontFamily: "Schibsted Grotesk, ui-sans-serif, system-ui, sans-serif"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: "20px"
  label-md:
    fontFamily: "Schibsted Grotesk, ui-sans-serif, system-ui, sans-serif"
    fontSize: "13px"
    fontWeight: 600
    lineHeight: "18px"
    letterSpacing: "0.01em"
  label-sm:
    fontFamily: "Schibsted Grotesk, ui-sans-serif, system-ui, sans-serif"
    fontSize: "11px"
    fontWeight: 700
    lineHeight: "14px"
    letterSpacing: "0.06em"
  mono:
    fontFamily: "Spline Sans Mono, ui-monospace, SF Mono, monospace"
    fontWeight: 400
rounded:
  sm: "0.25rem"
  default: "0.5rem"
  md: "0.75rem"
  lg: "1rem"
  full: "9999px"
spacing:
  xs: "0.25rem"
  sm: "0.5rem"
  md: "1rem"
  lg: "1.5rem"
  xl: "2rem"
components:
  button-primary:
    backgroundColor: "{colors.corona-gold}"
    textColor: "{colors.on-corona-gold}"
    typography: "{typography.body-md}"
    rounded: "{rounded.full}"
    height: "44px"
    padding: "0 20px"
  button-secondary:
    backgroundColor: "{colors.ink-high}"
    textColor: "{colors.bone}"
    typography: "{typography.body-md}"
    rounded: "{rounded.full}"
    height: "44px"
    padding: "0 20px"
  button-secondary-hover:
    backgroundColor: "{colors.ink-highest}"
  button-ghost:
    textColor: "{colors.bone-muted}"
    typography: "{typography.body-md}"
    rounded: "{rounded.full}"
    height: "44px"
    padding: "0 20px"
  button-ghost-hover:
    backgroundColor: "{colors.ink-container}"
    textColor: "{colors.bone}"
  button-danger:
    backgroundColor: "{colors.error}"
    textColor: "{colors.on-error}"
    typography: "{typography.body-md}"
    rounded: "{rounded.full}"
    height: "44px"
    padding: "0 20px"
  button-sm:
    typography: "{typography.label-md}"
    height: "36px"
    padding: "0 14px"
  input:
    backgroundColor: "{colors.ink-container}"
    textColor: "{colors.bone}"
    typography: "{typography.body-md}"
    rounded: "{rounded.md}"
    height: "44px"
    padding: "0 14px"
  card:
    backgroundColor: "{colors.ink-low}"
    rounded: "{rounded.lg}"
    padding: "24px"
  pill:
    typography: "{typography.label-md}"
    rounded: "{rounded.full}"
    padding: "2px 10px"
  label:
    textColor: "{colors.bone-muted}"
    typography: "{typography.label-sm}"
  section-title:
    textColor: "{colors.bone}"
    typography: "{typography.body-lg}"
  rail-entry-current:
    backgroundColor: "{colors.ink-high}"
    textColor: "{colors.corona-gold}"
    typography: "{typography.body-md}"
    rounded: "{rounded.md}"
    padding: "10px 14px"
---

# Design System: Umbral

Recorded 2026-10-22 from the shipped implementation (spec 007, "The Umbra Look")
by `/impeccable document`. The normative sources are the code: tokens in
`src/Umbral/Assets/tailwind.config.js`, the `.um-*` component layer in
`src/Umbral/Assets/umbral.input.css`, button and field helpers in
`src/Umbral/Views/Components.fs`, the mark in `src/Umbral/Views/Theme.fs`, the
rail and page frames in `src/Umbral/Views/Layout.fs`. This file describes them so
a new screen lands in the same world; when it and the code disagree, the code is
right and this file is stale.

## Overview

**Creative North Star: "The Sundial at the Threshold"**

*Umbral* means threshold; an *umbra* is the shadow a thing casts. The mark draws
exactly that: a sun, an upright, and the shadow it casts on a threshold line. The
identity providers are the light, Umbral stands in it, and every app receives the
same shadow. The whole look follows from the mark: a field of ink (or, in the
light theme, of warm paper), a single corona of gold for what matters, and
nothing else that asks for attention.

The product is a tool the operator opens a few times a year and a page that
other people pass through in seconds. Both want the same thing: quiet, exact
surfaces where the one gold element is the one to act on. Density is moderate,
line lengths are short, and everything that is not text is either a tonal step of
the background or a one-pixel line. The light theme is not an inversion but the
same scene by day: paper instead of ink, ink instead of bone, a darker gold that
still reads as gold, and a primary button that becomes ink with gold text so
the accent never has to carry dark type.

**Key Characteristics:**
- One accent, used sparingly: gold for the primary action, links, focus and the
  current place, and for nothing decorative.
- Two themes always, chosen by the system unless the person picks one; the first
  paint is already in the right theme.
- Depth by tone and hairline, not by shadow.
- Three faces with fixed jobs: a display face for headings and the wordmark, a
  text face for everything else, a mono face for anything a machine reads.
- Pill buttons and softly rounded fields and cards; no sharp corners anywhere
  but the focus ring.
- Everything self-hosted and legible with JavaScript off.

## Colors

Ink and bone with a corona of gold; by day, paper and ink with a darker gold.

### Primary
- **Corona Gold** (#f5c26b dark, #8a5a00 light): the one accent. As text and
  lines it is links, the focus ring, the checked choice, the current rail entry
  and the selection highlight. As a fill it is the primary button, in the dark
  theme only (see the Fill Pair rule). The token name is `primary`.
- **On Corona Gold** (#2a1d05 dark, #fffdf8 light): text on a gold fill
  (`on-primary`).
- **Gold Container** (#3a2e12 dark, #fbefd6 light): a tinted field behind gold
  text, rare (`primary-container`). Most tinted states use `primary/10` instead.

### Neutral
- **Ink** (#0b0b14) / **Paper** (#f4f1ea): the page (`surface`).
- **Five tonal steps** (`surface-container-lowest` to `-highest`): dark
  #07070e, #14141f, #181826, #1d1d2b, #26263a; light #ebe5d8, #fffdf8,
  #faf7f0, #ebe5d8, #e3ddd0. Cards sit on `-low`, inputs on `-container`,
  hovered and current things on `-high` and `-highest`. In the light theme the
  lowest step is darker than the page, the low step lighter, so a card on
  paper is a sheet of brighter paper.
- **Bone** (#ece8df dark, #17151f light): text (`on-surface`).
- **Bone, muted** (#a7a2b6 dark, #5f5a6b light): secondary text, labels,
  hints, ghost buttons (`on-surface-variant`; also `secondary`).
- **Outline** (#74708a dark, #857e6e light): borders on inputs and secondary
  buttons, the radio ring. **Outline, variant** (#2e2e40 dark, #ddd6c8 light):
  hairlines on cards, dividers, the rail's edge.

### Status
- **Success** (#a8d99a on #20301f dark; #2f5d1e on #e6eedc light).
- **Warning** (#f0b45e on #2a2111 with border #6b5328 dark; #6b4500 on #fbefd6
  with border #e2c27f light).
- **Error** (#ff8f7e dark, #b3261e light), with `on-error` for text on a red
  fill and `error-container` / `on-error-container` for the error alert.

### The mark's own colours
`--mark-sun`, `--mark-base`, `--mark-shadow`, `--mark-shadow-opacity` and
`--mark-upright` (dark: #f5c26b, #3a3a52, #a7a2b6 at 0.5, #ece8df; light:
#b8801f, #ddd6c8, #5f5a6b at 0.4, #17151f). No accent sheet sets them.

### Named Rules
**The Fill Pair Rule.** The primary button uses `primary-fill` / `on-primary-fill`,
not `primary` / `on-primary`. In the dark theme both pairs are gold on umber. In
the light theme the fill pair is ink (#17151f) with gold text (#f5c26b) while
links stay the darker gold (#8a5a00), so a gold fill never has to carry dark text
at 4.5:1. An app's accent sheet overrides both pairs and nothing else.

**The One Red Fill Rule.** On any card, at most one button has the red `danger`
fill, and it is the action that cannot be undone (Delete app, Delete connector).
A destructive action that can be undone, such as removing a logo that can be
uploaded again, is a ghost button in the error colour.

**The Contrast Floor.** Spec 007 FR-007: every text at 4.5:1 or better against
what it sits on, every input border and focus indicator at 3:1 or better, in
both themes. Where a palette value misses this for a border, the plan records
a darker (light) or lighter (dark) value of the same hue for that use. An
app's accent is checked against both themes and saved with a warning, never
refused; its button text is black or white, whichever reads better, chosen by
Umbral.

**The Mark Keeps Its Colours.** The Gnomon takes its colours from `--mark-*`,
which no accent rule touches, so an app's colour never recolours Umbral's mark.
The "Secured by Umbral" line cannot be hidden or recoloured.

## Typography

**Display Font:** Bricolage Grotesque, extra bold only (with Schibsted Grotesk,
then system sans)
**Body Font:** Schibsted Grotesk, 400 to 600 (with system sans)
**Label/Mono Font:** Spline Sans Mono, 400 and 500 (with system mono)

**Character:** A tight, slightly eccentric display face over a plain, even text
face. Headings are set heavy and close; everything else is unhurried. The mono
face marks what a machine, not a person, will read.

All three are self-hosted as a Latin subset (about 84 KB in all) under the SIL
Open Font License, served from the instance, `font-display: swap`. No page may
load a font from anywhere else.

### Hierarchy
- **Display** (800, 34px/40px, -0.035em): the page title, one per page.
- **Headline large** (800, 24px/32px, -0.03em): a message page's heading, a
  card's main heading. Display and headline large are the only two sizes that
  use the display face.
- **Headline medium** (600, 20px/28px, -0.01em): a card's title in the text
  face.
- **Body large** (400, 16px/24px): the page's base size; at 600 in bone, the
  title of a section inside a card, `.um-section-title`.
- **Body medium** (400, 14px/20px): most interface text, buttons, inputs, leads.
  Leads keep to `max-w-prose`.
- **Label medium** (600, 13px/18px, +0.01em): small buttons, hints (at 400),
  pills, field errors, the theme control's label.
- **Label small** (700, 11px/14px, +0.06em, uppercase): the field label,
  `.um-label`.
- **Mono** (`.um-mono`, 400 or 500): client IDs, URLs, secrets, tokens, hashes.

### Named Rules
**The Lowercase Wordmark Rule.** The wordmark reads `umbral`, in the display
face at 800 with -0.035em tracking, lowercased by CSS so the text stays "Umbral"
for titles and screen readers.

**The Machine Text Rule.** Anything a person will copy into a configuration
file or compare character by character is set in the mono face.

**The Catalogue Rule.** Every word on a page comes from `Views/Texts.fs`. When a
mockup and the catalogue disagree, the catalogue wins.

## Layout

The operator screens have a side rail: on wide screens a sticky 256px column at
the left edge, full height, holding the brand, the four entries (Apps,
Connectors, Users, Credential), Sign out, and at the bottom the theme control;
below 768px it stacks above the page as a band with the entries wrapping on one
line, so there is never a menu to open. The page beside or below it is
`max-w-page` (1120px), padded 16px on narrow screens and 32px from 768px up, with
32px of vertical padding, 48px from 768px.

Every page without a rail (sign-in, setup, operator sign-in, message pages) has a
sticky 64px name bar with the brand at the left and a footer with the small
Gnomon, a line of text, two quiet links to the source and the documentation
(never on a branded sign-in page), and the theme control. A branded sign-in page
drops the name bar and shows "Secured by Umbral" in the footer instead. The sign-in card
itself is `max-w-sm` (384px), centred in at least 60vh, its contents 20px apart
and its buttons full width; a message card is `max-w-md`, centred, text
centred.

Rhythm is on a 4px base. Inside a form, fields are 12px apart (`space-y-3`);
groups within a card section are 20px apart (`space-y-5`); cards and sections
on a page are 24px to 32px apart. A button row is `flex flex-wrap items-center
gap-2`, 8px between buttons, so it wraps at 360px without special cases.

The two breakpoints that matter are 768px (`md`, where the rail becomes a
column and padding grows) and the card's own maximum width. Ship checks are
done at 1280px and 360px, in both themes.

## Elevation & Depth

Depth is tonal and flat. A surface is a step of the background tone with a
one-pixel hairline (`--border-micro`, the outline-variant colour), never a
shadow at rest: the card is the low step with a hairline, the input the
container step with a full outline, a hovered rail entry or secondary button
one step higher. Two shadow tokens exist (`--shadow-level-1`, `0 1px 2px 0
rgb(0 0 0 / 0.4)`; `--shadow-level-2`, `0 10px 28px -8px rgb(0 0 0 / 0.55)`)
and are reserved for things that float; today nothing uses them. The
name bar on a rail-less page is the one translucent surface
(`bg-surface/90` with `backdrop-blur-md`).

### Named Rules
**The Flat Rest Rule.** No shadow on a surface at rest. A shadow, if one ever
appears, answers a state: something lifted, dragged or floating.

**The Hairline Rule.** Separation is a 1px line in `outline-variant`, often at
40% (`border-outline-variant/40`) for the rail's edge, the name bar and the
footer. Thicker lines appear only as the 2px focus ring and the 2px crop box.

## Shapes

Softly rounded, with the pill as the signature. Buttons, pills, the theme
control's track and its options, the radio and the crop handles are fully
round (9999px). Inputs, choice cards, the rail's entries and the list's entries are
12px (`rounded-md`, or Tailwind's default `rounded-xl`, the same value); cards
are 16px (`rounded-lg`). Alerts are 8px, the focus ring and the check box 4px.
Nothing is square. Borders are
1px except the 2px rings on radio, check, crop box and focus. The browser-tab
icon is the Gnomon on a 12px-rounded ink tile.

## Components

Quiet and exact. Each control is the plainest shape that does its job, and the
one gold element on a surface is the one to use.

### Buttons
- **Shape:** a pill (9999px), 44px tall, 20px side padding, body medium
  semibold, icon and label 8px apart, label never wraps. Small: 36px tall,
  14px padding, label medium.
- **Primary:** the fill pair (gold on umber in dark; ink with gold text in
  light). Hover brightens by 10%. One per surface.
- **Secondary:** the high tonal step with a 1px outline, text in bone; hover
  steps to highest. The default for ordinary actions (Save, Create, Regenerate).
- **Ghost:** no fill, text in muted bone; hover gains the container step and
  full bone. For the quiet choice (Use whole image, Cancel) and, with
  `text-error`, for a reversible destructive action.
- **Danger:** the red fill with `on-error` text; hover brightens. One per card,
  for what cannot be undone, always behind a native confirm.
- **Disabled:** 50% opacity and `not-allowed`, except the branding sample on an
  app's card, which is a picture of a button and stays at full opacity.
- **Focus:** the global ring, 2px gold outline offset 2px.
- **Transitions:** 160ms on colour and background, 200ms on shadow, only when
  the person has not asked for reduced motion.

### Inputs / Fields
- **Style:** 44px tall, 12px radius, the container step with a 1px outline,
  body medium in bone, placeholder in muted bone, caret in gold.
- **Label:** `.um-label`, label small uppercase in muted bone, 6px above.
  **Hint:** label medium regular in muted bone, 6px below. **Error:** label
  medium in the error colour with an icon, 6px below; the input gets
  `aria-invalid` and a red border.
- **Focus:** border turns gold, no second ring. **Read-only:** text in muted
  bone. **File input:** the native "Choose file" button dressed as a small
  secondary button (`.um-file`).
- **Choice cards** (`.um-choice`): a radio inside a 12px card on the container
  step with a hairline; checked becomes a gold border, a 1px gold ring and a
  10% gold tint. **Check** (`.um-check`): an 18px square, 4px radius, 2px
  outline; checked fills gold with a tick in `on-primary`, drawn per theme.

### Cards / Containers
- **Corner Style:** 16px.
- **Background:** the low tonal step.
- **Shadow Strategy:** none; a hairline (`--border-micro`).
- **Internal Padding:** 24px, 32px from 768px.
- **Sections inside a card or a pane** are separated by a 40% hairline top
  border with 24px above and 20px below, headed by a `.um-section-title`
  (body large at 600, 12px below). A pane's closing action row (Regenerate
  secret, Delete app) gets the same hairline and 24px above it.

### List and detail
The frame for the screens whose items are not cards one under another (Apps,
Connectors). From 1024px, a 320px list and the pane fill the page
column, 32px apart; below it, one column: the list URL shows the list, an
item's URL shows the pane with a ghost back link (chevron and screen name).
The list is a `nav` of entries drawn like the rail's: 44px tall, 12px radius,
14px by 10px padding, 4px apart, the name in body medium at 500 over a muted
label-medium line, at most one status marker at the right. Hover takes the
container step; the selected entry the high step with its name in gold at 600
and `aria-current="page"`. The create button sits in the list's heading row
beside the count, and is drawn as selected while its form is the pane. The
pane is a `section` on the page surface, not a card: the item's name as
headline large with its pills, then notices, then sections, then the closing
action row. Focus goes to the pane's heading after an htmx swap.
An entry for something not set up yet keeps its name muted at rest. A
pinned entry (Connectors' allowed email domains) sits below the others after a
40% hairline with 12px above and below.

### Alerts
An 8px rounded block, 16px padding, body medium, icon and text 12px
apart, in four kinds: success, error, warning and info, each a tinted container
with a border in its colour at 25% to 40%. Error text uses `on-error-container`.

### Pills and badges
`.um-pill`: a pill in label medium with a 1px border; primary (gold tint and
text), success, or muted (high step, muted bone). `.um-last-used`: the "Last
used" badge on a sign-in button, drawn in `currentColor` with a 1px border so it
has the button label's contrast on any accent. `.um-or`: a label-medium "or"
between two hairlines.

### Navigation
- **Rail entries:** 44px minimum, 12px radius, 14px by 10px padding, body
  medium at 500 in muted bone; hover gains the container step and full bone;
  the current entry (`aria-current="page"`) is the high step with the label in
  gold at 600. Sign out is an entry-shaped form button.
- **Theme control:** a pill track (low step, 1px outline, 2px padding) of three
  44px round options with an icon each; the one in effect is the fill pair
  with `aria-pressed="true"`; a line below says what is in effect. It is a
  plain form and works without JavaScript.
- **Skip link:** a primary button that appears only on focus, fixed at the top
  left.

### The Gnomon (signature)
A 48-unit square: a sun (circle at 11,11), a threshold line (the base), an
upright at x=22, and a triangular shadow from the upright's foot to the right.
Three cuts: the full mark, a small one without the base for 20px uses (the
footer, "Secured by Umbral"), and a bold one for the browser tab, on an ink tile
with 12px corners. Always drawn as inline SVG with `aria-hidden`, coloured by
`.um-mark-*` classes from `--mark-*`. It sits beside the lowercase wordmark in
the brand lockup.

### Icons
Material Symbols path data (viewBox `0 -960 960 960`) inlined as SVG, 1.25em
square, in `currentColor`, never from an icon font or a remote sheet.

## Do's and Don'ts

### Do:
- **Do** take every colour, size and radius from the tokens (`bg-primary`,
  `text-on-surface-variant`, `rounded-lg`), so both themes and an app's accent
  sheet keep working.
- **Do** write class names as literal strings in views; the Tailwind scanner
  reads `Views/` and `Handlers/` and cannot see a name built by interpolation.
- **Do** regenerate `umbral.css` with `src/Umbral/Assets/build-css.sh` after
  touching a view, the tokens or `umbral.input.css`, and commit it; CI and the
  image build run `--check` and fail on a stale file.
- **Do** check every new surface at 1280px and 360px in both themes, with the
  system theme and with a chosen one.
- **Do** keep text at 4.5:1 and borders and focus at 3:1 in both themes, and
  measure from the tokens, not by eye.
- **Do** make every control work with JavaScript off and every form carry the
  antiforgery token; a destructive form carries `data-confirm`.
- **Do** put a new reusable look into `.um-*` in `umbral.input.css` and a
  helper in `Components.fs`, not into a one-off class string.
- **Do** take wording from `Views/Texts.fs` and add new strings there.

### Don't:
- **Don't** use a second accent, a gradient, a glow or a coloured shadow. Gold
  is the only colour that is not a tone of the surface or a status.
- **Don't** put a shadow on a surface at rest.
- **Don't** give a card more than one red-filled button, or fill red for an
  action that can be undone.
- **Don't** set a colour on the Gnomon, hide or move "Secured by Umbral", or
  let an app's accent reach anything but the primary pairs.
- **Don't** load a font, script, stylesheet, image or icon from another host;
  the Content-Security-Policy refuses it and the product promises it.
- **Don't** put `style=""` in markup; the policy allows inline style only when
  a script writes it.
- **Don't** use the display face below headline large, or the mono face for
  prose.
- **Don't** build a menu or a drawer for the rail; it wraps on narrow screens
  by design.
