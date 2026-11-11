# Provider marks on the sign-in buttons

Design contract for task b6d7 (spec 014), written 2026-11-11 by the design reviewer in answer
to the implementer's four questions. What each provider's rules and licences allow is taken
from `specs/014-provider-marks/research.md`; the reviewer did not read those rules. Where this
file and that research disagree, the research wins and this file is amended.

## The rule

A sign-in button leads with one mark. Which mark:

1. **A provider's own mark**, only when its terms let Umbral ship it (or, for Apple, let the
   operator supply it) **and** its rules fit Umbral's button as it is: one colour, the label's
   colour, on the button's own fill. Today that is GitHub, and Apple when the operator has
   added the logo.
2. **Umbral's Key icon** for the passkey connector, the same icon the operator sign-in page
   already uses for a passkey.
3. **The link icon** for everything else: Google, Discord, the Fediverse, any OpenID Connect
   provider.

No operator-supplied marks for other kinds, no second button look for Google, no brand colour
anywhere but Apple's fixed black and white. The uneven set is honest: it shows which providers
let their mark be used this way. DESIGN.md's Icons section gets two sentences saying this.

## Answers

### 1. GitHub as the only bundled brand mark: yes

The Invertocat on the GitHub button, inline SVG, 1.25em like every icon, in `currentColor`.
That gives black or white in every case the rules allow: the secondary button's text is bone
in the dark theme and ink in the light theme, which are not pure white and black, so set the
mark's fill explicitly to `#fff` (dark) and `#000` (light) on the secondary button, and to the
label's black or white on an accent fill. Record those two values as detector ignores scoped
to the stylesheet, as for Apple's. Only for kind GitHub, whatever the connector's display
name. If the research turns out to forbid it, GitHub falls back to the link icon and nothing
else changes.

### 2. Google: the link icon

No second exception. Google's button needs its own fills and strokes, a fourth typeface
(Umbral ships three, about 84 KB, and loads nothing from another host) and a multicolour mark
on a system whose one accent is gold. The cost is a sign-in page that stops looking like one
product. The link icon stays.

### 3. Apple: the operator adds the logo

**This is a new setting, so it needs the maintainer's yes in the spec** (Principle VII); the
design below assumes it.

**In the pane.** A section of its own, **Sign-in button logo**, between Settings and the
closing row, with its own small form, so adding or removing a logo never touches the
credentials. Not a file control: a **textarea**, as for the key (mono, 6 rows, `.um-textarea`,
autocomplete, spellcheck and autocapitalize off), so the pane stays free of multipart forms.

- Lead (body medium, muted): *Apple requires its own logo on this button and does not let
  Umbral ship it. Add it from your Apple Developer account, and the button shows it.*
- Field label: *Apple logo (SVG)*
- Hint: *Download the logo from Apple Design Resources, open the SVG in a text editor and
  paste all of it. Umbral keeps only its shape and draws it in the button's own black or
  white.*
- Button: small secondary, *Save logo*.
- **Once saved:** above the field, a preview drawn like the branding section's: a
  `surface-container` panel holding the sample Apple button as people see it, in the current
  theme. Under it the textarea becomes *Replace the logo* with the hint *Leave blank to keep
  the saved logo.*, **Save logo**, and **Remove logo** as a small ghost in the error colour
  with a confirm (*Remove the logo? The button will show its title alone.*). It is reversible,
  so it is never the red fill.
- **Refusals**, under the field at 422, the pasted text not drawn back:
  - not an SVG: *That is not an SVG file. Paste the whole file from Apple Design Resources,
    from <svg to </svg>.*
  - more than shapes: *That SVG has more than a shape in it. Umbral accepts only the logo's
    outline. Paste the file as Apple provides it.*
  - too large: *That file is too large to be a logo.*
- Notices: *Logo saved.* / *Logo removed. The button shows its title alone.*
- Without a logo the pane shows a muted line in the section, *No logo yet. The button shows
  "Sign in with Apple" alone.*, and the walkthrough keeps its App Store warning.

**On the button.** The logo fills the slot at the button's full height (44px), width by its
own proportions, in `currentColor`. The artwork carries its own padding, so this button has no
gap between logo and title (the others keep 8px). Logo and title stay centred as a group; the
pill and the 20px side padding stay. Judge the result from the captures: if the built-in
padding makes the group look off-centre, the fix is a negative margin on the slot, not a
change to the button.

For the code review, not this contract: what "only its shape" means (the allowed elements and
attributes), and that nothing from the pasted text reaches the page except the `viewBox` and
path data.

### 4. Everything else: unchanged, confirmed

- The **Last used** badge stays as it is on every button, the marked ones included.
- On a **branded page**: the app's logo above the buttons is untouched; with an accent, the
  others take the accent and their mark takes the label's colour; Apple stays black or white.
- **Both themes**: only the leading mark differs per button. Height, width, radius, typeface,
  order and focus ring are the same for all.
- Each mark is decorative (`aria-hidden`); the button's name is its label.

## Captures for review

In `.alta/reviews/2026-11-11-provider-marks/`, `<state>-<width>-<theme>.png`, 1280 and 360,
both themes unless said:

- `picker`: an app offering Apple (with logo), GitHub, Google, a passkey and one more.
- `picker-accent`: the same on an app with an accent and a logo.
- `picker-apple-no-logo`: Apple without a logo, title alone.
- `picker-last-used`: a marked button as the last used.
- `picker-focus` (1280 dark): focus on the Apple button with its logo.
- `apple-pane-no-logo`, `apple-pane-logo` (the preview, Replace, Remove), `apple-logo-refused`.

Plus `notes.md` with the new strings, the two detector ignores, and the DESIGN.md sentences.
