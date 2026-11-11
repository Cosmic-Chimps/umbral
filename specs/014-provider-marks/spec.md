# Spec 014: provider marks on the sign-in buttons

- Task: `2026-11-11-sign-in-page-provider-logos-on-the-sign-in-buttons-for-every-b6d7` (b6d7),
  which replaced the Apple-only task `pqzq`.
- Branch: `alta/bold-dove-crws`. Created 2026-11-11.
- Design: `design/contracts/provider-marks.md` (the design reviewer, 2026-11-11).
- Research: `research.md` (each provider's rules and licence, read 2026-11-11).

## Why

Every connector's button on the sign-in page leads with the same link icon, so people find
their provider by reading the label only. Spec 013 left an empty slot for Apple's logo
because Apple's artwork could not be bundled without checking its licence.

## What the research decided

| Connector | Mark | Reason (research) |
|---|---|---|
| GitHub | The Invertocat, pure white or black | R3: allowed to say the app integrates with GitHub, white or black only |
| Passkey | Umbral's Key icon | R6: the FIDO icon is licensed per service provider, no derivatives |
| Google | Link icon | R2: the "G" must be full colour on Google's own button looks and font |
| Discord | Link icon | R4: a sign-in button is not a permitted use; the developer licence is not sublicensable |
| Fediverse | Link icon | R5: no single provider |
| OpenID Connect | Link icon | R7: an operator-supplied mark would be a setting with no requirement behind it |
| Apple | Empty slot; see Part B | R1: Apple's licence forbids redistributing the artwork |

## Part A: the marks Umbral ships (this change)

- **FR-001** The GitHub connector's button leads with the GitHub mark, chosen by the
  connector's kind, never its display name.
- **FR-002** The mark is pure white on the secondary button in the dark theme and pure black in
  the light theme; on an accent fill it takes the label's colour, which Umbral already makes
  black or white. It is never Umbral's bone, ink or gold and never an accent colour.
- **FR-003** The passkey connector's button leads with the Key icon.
- **FR-004** Every other kind keeps the link icon; the Apple button keeps its slot.
- **FR-005** Every mark is inline SVG served by Umbral, `aria-hidden`, 1.25em like every icon;
  every button keeps its size, order, badge and focus ring.

## Part B: Apple's logo, supplied by the operator

Umbral cannot bundle Apple's logo (R1). The design contract describes the alternative: an
optional **Sign-in button logo** section in the Apple pane where the operator pastes the SVG
they downloaded from Apple Design Resources under their own Developer Program membership;
Umbral keeps only the `viewBox` and the path data and draws it in the existing slot at the
button's full height.

This is a new setting, so under Principle VII it needs the maintainer's explicit yes before it
is built. The case for it: App Store Review Guideline 4.8 is the reason most operators add
the Apple connector, App Review evaluates every custom Sign in with Apple button, and without
Apple's logo `docs/connectors/apple.md` has to tell operators not to rely on the button for a
review. The case against: a new stored value (a schema change), a pasted-SVG parser on the
admin side, and a second place an operator has to visit Apple's site.

**Security requirement (code review of part A, 2026-11-11):** the pasted SVG never goes through
`raw`: it can carry script, `on*` handlers, `foreignObject` and `href` references. Either it is
parsed to an allowlist (the `viewBox` and path data only) before it is stored and only those
values reach the page, or it is served as an `<img>` from an Umbral route with its own CSP and
`nosniff`. Tests include a hostile SVG.

**Decision:** the maintainer said yes on 2026-11-11.

- **FR-010** The Apple pane has a section **Sign-in button logo** (design contract, answer 3)
  once the Apple connector exists, with its own form: a textarea, Save logo, and, once a logo
  is saved, a preview of the button, Replace, and Remove logo (a red ghost with a confirm).
- **FR-011** Umbral accepts pasted text of at most 32 KiB that parses as XML with an `svg` root
  and no DTD, whose elements are only `svg`, `g`, `path`, `rect`, `title`, `desc` and `defs`
  (empty), and in which no attribute is an event handler (`on…`) or a reference (`href`,
  `xlink:href`). Anything else is refused with the contract's reason, under the field, at 422,
  and the pasted text is never drawn back.
- **FR-012** What is stored is rebuilt by Umbral: the `viewBox` (four numbers) and each path's
  `d` (path-data characters only), `fill-rule` (`nonzero` or `evenodd`) and `transform` (only
  `translate`, `scale` and `matrix` with numbers). `rect` elements are the file's background
  and are dropped; `title`, `desc`, ids, styles and colours are dropped. The stored text is
  parsed again with the same rules before it reaches a page.
- **FR-013** On the sign-in page the logo fills the Apple button's slot at the button's full
  height, in `currentColor`, with no gap to the title; without a logo the button is unchanged.
- **FR-014** The logo goes with the Apple connector: deleting the connector deletes it. Schema
  version 11 adds `connectors.apple_logo` (NULL for every existing row).
- **FR-015** Saving and removing the logo is operator-only and antiforgery-checked like every
  admin form, and is recorded like the other changes to a connector.
