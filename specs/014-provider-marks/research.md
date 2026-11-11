# Research: provider marks on the sign-in buttons (task b6d7)

Every page below was read on 2026-11-11. Quotes are the provider's words. The
question for each provider is the same: may Umbral, an MIT-licensed repository
that operators deploy themselves, ship the provider's mark as an inline SVG on
its sign-in button, styled as Umbral styles its buttons (a 44px pill in token
colours, or the app's accent colour on a branded page)?

## R1. Apple

**Rules for a custom button.** Human Interface Guidelines, "Sign in with Apple",
<https://developer.apple.com/design/human-interface-guidelines/sign-in-with-apple>
(change log: last change 2022-09-14):

- "Use only the logo artwork downloaded from Apple Design Resources; never
  create a custom Apple logo."
- "Match the height of the logo file to the height of the button." "Don't crop
  the logo file." "Don't add vertical padding."
- Titles: only "Sign in with Apple", "Sign up with Apple" or "Continue with
  Apple". "Logo and title colors. Within a button, both items must be either
  black or white; don't use custom colors." The background "needs to remain
  black or white". The corner radius may match the other buttons of the UI.
- "Make a Sign in with Apple button no smaller than other sign-in buttons."
- "App Review evaluates all custom Sign in with Apple buttons."

Umbral's button (spec 013) already follows the colour, title and size rules.

**Licence of the artwork.** The logo is the download "Sign in with Apple Logo"
on <https://developer.apple.com/design/resources/>
(`Logo-Sign-in-with-Apple.dmg`). The disk image opens on Apple's "Developer
Artwork License Agreement for Sign in with Apple" (EA1677, 1/30/2020), which
the person downloading must accept. It says:

- 2A: "a limited, non-transferable, non-exclusive license to use the Developer
  Artwork in the user interface of your software applications that run on
  Apple's iOS, macOS, watchOS, or tvOS operating system software (and
  corresponding web-based or other versions …)"; "only if you are an Apple
  Developer Program member".
- 2B: "may not otherwise be used, extracted, copied, modified, distributed, or
  repackaged … You may not rent, lease, lend, trade, transfer, sell, sublicense
  or otherwise redistribute the Developer Artwork".
- 3: "may not be separated from the bundle and distributed on a standalone
  basis."

**Conclusion.** Umbral cannot bundle Apple's logo. Committing it to this
repository would redistribute and sublicense the artwork to everyone who clones
it, under MIT, which 2B and 3 forbid; the maintainer accepting the licence does
not change that. Drawing the logo instead is forbidden by the guidelines. The
logo cannot be loaded from Apple either: the system-provided web button needs
`appleid.auth.js` from `appleid.cdn-apple.com`, and Umbral's pages load nothing
from another host (content security policy, `PageAssetsTests`).

The operator is a different case. Every operator of an Apple connector is an
Apple Developer Program member (the connector needs one), can accept the
licence and download the artwork themselves, and the sign-in page is the user
interface of their software. What replaces the bundled logo is decided in the
design contract (`design/contracts/provider-marks.md`).

## R2. Google

"Sign in with Google branding guidelines",
<https://developers.google.com/identity/branding-guidelines> (no date on the
page), and the assets `signin-assets.zip` linked from it:

- "The button must always include the standard color for the Google "G"."
  "It must be the standard color version (the standard color gradient super G
  logo) and appear on a white background." "you can't change the size or color
  of the Google "G" logo."
- "Don't Use monochrome versions of the Google "G" for the button." "Don't Put
  the standard color Google "G" icon on a colored background other than light,
  dark, or neutral."
- The three allowed looks fix the fill, the stroke and the text: Light
  `#FFFFFF`, 1px `#747775`, text `#1F1F1F`; Dark `#131314`, 1px `#8E918F`, text
  `#E3E3E3`; Neutral `#F2F2F2`, no stroke, text `#1F1F1F`. Font: Google Sans
  Medium 14/20. Shape: rectangle or pill.
- "Brand use not expressly covered by the page is not allowed without prior
  written consent from Google."
- The current "super G" in the assets is a conic gradient drawn through an SVG
  `foreignObject` with a blur filter, not a flat four-colour path.

**Conclusion.** Umbral's button as styled today cannot carry the "G": its fill
is a token step or the app's accent, neither of which is one of Google's three
looks, and a monochrome "G" in `currentColor` is forbidden. Carrying it would
make Google a second exception like Apple's: Google's own fills and strokes per
theme, the Google Sans font shipped with Umbral, and the gradient artwork. The
design contract decides between that and keeping the link icon.

## R3. GitHub

GitHub brand, "Logo", <https://brand.github.com/foundations/logo>
(`github.com/logos` redirects there; no date on the page):

- Permitted: "Use a permitted GitHub logo to inform others that your project
  integrates with GitHub." "Use a permitted GitHub logo to link to GitHub."
- Colours: "The Invertocat and our wordmark should only appear in white, black,
  or in few cases grey or green." "Do not modify the permitted GitHub logos,
  including changing the color, dimensions …"
- "Do not use any GitHub logo as the icon or logo for your business/
  organization, offering, project …"; "less prominently than your own company or
  product name or logo".

The Invertocat's path is also published by GitHub in Primer Octicons
(`mark-github`), MIT for the code, with the note that the GitHub logos follow
the logo guidelines.

**Conclusion.** Allowed: a "Sign in with GitHub" button says the app integrates
with GitHub. The mark must be pure white or pure black, never Umbral's bone,
ink or gold text colour and never an accent colour.

## R4. Discord

Discord branding, <https://discord.com/branding> (no date on the page):

- "Use the Discord Marks to inform people that you are a Discord user and/or
  that you have a Discord server."
- "Please do not edit, change, distort, recolor, or reconfigure the Discord
  logo." The logo may be used "in color, black or white".
- "You must have permission from Discord before using any of the Discord Marks
  or Brand Assets except as permitted here."

Discord Developer Terms of Service, section 8 (article updated 2026-11-11):
"Discord grants you a limited, revocable, non-transferable, non-assignable,
non-sublicensable, non-exclusive license … solely to display Discord's Brand
Features for required attribution(s) or in an incidental manner while promoting
or advertising your Application, only in accordance with the Terms and
Discord's Brand Guidelines."

**Conclusion.** Not allowed as far as these pages say: a sign-in button is not
among the permitted uses on the branding page, and the developer terms license
the operator (the developer of the Discord application), not Umbral, for
attribution only, with no right to sublicense. Discord keeps the link icon.

## R5. Fediverse

There is no single provider: the connector signs in with any
Mastodon-compatible server (Mastodon and GoToSocial). The Mastodon logo names
one product and is Mastodon's trademark. The community's pentagram symbol was
dedicated to the public domain by its author (as reported by
<https://fedi.tips/?p=928>), but it is multicoloured and not one most people
recognise as "my account".

**Conclusion.** No mark; the design contract decides between the link icon and
a neutral icon of Umbral's.

## R6. Passkey

FIDO Alliance, "Get the FIDO Passkey Icon",
<https://fidoalliance.org/get-the-passkey-icon/>: "Service providers providing
sign-ins with passkeys should use the passkey icon", downloaded through a form,
and "By completing the form on the following page, you are agreeing to this
Agreement". The agreement: the icon is a FIDO trademark (section 2), use only
under the FIDO Logos Usage Guidelines (3.7), "No right to create modifications
or derivatives" (3.6).

**Conclusion.** The agreement is between FIDO and each service provider that
fills in the form, and it says nothing of redistributing the icon in software
others deploy. Umbral keeps an icon of its own: `Theme.Key` (Material Symbols,
Apache-2.0), already in the icon set.

## R7. OpenID Connect

The provider is whatever the operator added. An operator-supplied mark per OIDC
connector would be a new setting with no requirement behind it, so the answer
is no (constitution VII). It keeps the link icon.
