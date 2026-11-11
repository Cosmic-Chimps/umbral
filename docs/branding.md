# Brand an app's sign-in page

An app can show its own logo and colour on the sign-in page people see when it
sends them to Umbral. You set both on the app's card on **Apps**, under
**Sign-in page branding**. There is no global setting and no extra screen. An
app with no branding looks exactly as it did before.

**Where it shows, and where it does not.** Only on that app's sign-in page (the
one with the buttons), including the page people come back to when a sign-in
fails. Never on the admin screens: the app's card previews the colour, but the
screen around it stays Umbral's. Never on the error page Umbral shows when an
app's request cannot be sent back to the app (a redirect address you did not
register, say). That request failed its checks, and if it could carry an app's
logo, anyone could put a trusted app's logo on an error page they built.

**"Secured by Umbral" cannot be removed.** A branded page drops Umbral's name
bar and footer and shows a small **Secured by Umbral** line with the Gnomon at
the bottom instead (next to the theme control, which stays, so people can still
pick light or dark). It is always there, and no setting hides it or recolours it. It is how
a person tells Umbral's real sign-in page from a lookalike: if branding could
remove it, it would prove nothing.

## The logo

On the app's card, under **Logo**, choose a file and click **Save logo**.

- **Formats:** PNG, JPEG or WebP. Not SVG, because an SVG can carry script, and
  not a web address: Umbral never fetches a logo from anywhere else, so no third
  party learns who is signing in.
- **Replace** it by saving another file; the old logo is gone at once.
  **Remove logo** asks first, and puts the app back on no logo.

| Limit | Value | Checked by |
| --- | --- | --- |
| The file you choose | 2 MB | your browser, before anything is sent |
| The image you choose | 4096 pixels on a side | your browser |
| The logo Umbral stores | 32 to 512 pixels on a side, at most 1.25 MB | Umbral |
| One upload request | 3 MB | Umbral (more is refused, whatever it holds) |

**Cropping needs JavaScript.** When you choose a file, a crop rectangle appears
over it. Drag it to move it, drag a corner to resize it, or use the arrow keys
(hold Shift to resize); **Use whole image** puts it back. When you save, your
browser cuts the crop out, scales it down to fit 512 pixels, turns it into a PNG
and uploads that. Umbral never decodes your image. It checks the PNG's structure
and writes a new file from it, keeping the picture and dropping everything else
in it (camera details, location, comments), and stores that file.

**Without JavaScript** nothing can crop or convert, so upload a PNG that already
fits: 32 to 512 pixels on a side, not interlaced, at most 1.25 MB. Anything else
is refused and the message says what to change. A JPEG, for example, gets:
*Umbral stores logos as PNG. Turn on JavaScript on this page to crop, scale and
convert a PNG, JPEG or WebP image, or upload a PNG of at most 512 × 512 pixels.*
A refused upload changes nothing: the logo that was there stays.

## The accent colour

Type it in the **Accent colour** field as `#` and six hexadecimal digits, for
example `#d9480f`. Upper or lower case is fine. `#fff`, a colour name or
`rgb(...)` is refused with: *Enter the colour as # followed by six hexadecimal
digits, for example #d9480f.* With JavaScript on, a colour picker next to the
field fills it in for you. Leave the field empty and save to clear the colour.

The colour replaces Umbral's gold on that app's sign-in page: the buttons and
the focus outlines. It is the same colour in the dark and the light theme, whichever
one the person has. Nothing else changes, and the Gnomon in **Secured by Umbral**
keeps its own colours.

**You do not choose the text colour.** Umbral picks black or white for text on
your colour, whichever reads better, so button text is always readable
(at least 4.5 to 1) whatever colour you pick.

**A low-contrast warning, not a refusal.** Because the colour shows on both
themes, Umbral checks it against both backgrounds. If it is hard to see on either
(under 3 to 1 against the dark card or the light one), it is saved anyway and you
are told: *Saved. This colour is hard to see on Umbral's dark or light
background, so outlines and highlights in it will be faint. Buttons stay
readable.* A very light colour warns on the light theme, a very dark one on the
dark theme, and some mid-tones such as `#f26b1d` just miss on the light one;
`#d9480f` and `#c2410c` are safe on both. The card's sample button shows what
people will see.

## Where it is kept

Logos and colours live in `umbral.db`, so the [backup](run/backup.md)
you already take covers them. Deleting an app deletes its branding with it.

An Umbral from before branding upgrades its database by itself, and every app
starts with no branding, so no page changes. See
[Upgrading](run/backup.md#upgrading) for what an upgrade does to the database and how to go
back.
