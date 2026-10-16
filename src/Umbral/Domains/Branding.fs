/// Per-app sign-in branding: the accent colour and the colour maths behind it
/// (spec 006, research R4), and the types a stored logo is described by.
///
/// Pure, with no I/O, so the rules can be read and tested without a database or
/// a page in the way. The accent is a value only `Accent.parse` can make, so
/// every colour that reaches a stylesheet, a `data-accent` attribute or a table
/// row went through the same check.
module Umbral.Domains.Branding

open System

/// Why a colour was refused. One case, because there is one thing to tell the
/// operator: the shape to type.
type AccentError =
    /// `#rrggbb` was expected, for example `#f26b1d`.
    | NotHexColour

/// Six lowercase hex digits, no `#`. Private, so a string that was never
/// checked cannot be one.
type Accent = private Accent of string

/// The colour of text drawn on the accent. Only the two extremes: with pure
/// black and white the worse of the two is never below 4.58:1 (research R4),
/// which is what SC-003 asks for, and no in-between colour can say that for
/// every accent.
type OnAccent =
    | Black
    | White

/// The accent's rgb channels in `0..255`.
type Rgb = int * int * int

/// The card an accent sits on in the sign-in page and the admin sample:
/// `surface-container-low` in `tailwind.config.js`.
[<Literal>]
let private CardBackground = "#11161d"

/// FR-017: an accent below this contrast against the card is saved but the
/// operator is warned, because buttons and focus rings in it would vanish. 3:1
/// is WCAG's threshold for non-text elements.
[<Literal>]
let private LowContrast = 3.0

/// A channel's contribution to luminance, for each of its 256 values. WCAG 2.x:
/// `c / 12.92` for the darkest, otherwise `((c + 0.055) / 1.055) ^ 2.4`, with
/// `c` the channel over 255. A table, because the 16.7 million colours the
/// exhaustive test walks would otherwise each pay for three `Math.Pow` calls.
let private linear: float[] =
    Array.init 256 (fun i ->
        let c = float i / 255.0
        if c <= 0.03928 then c / 12.92 else Math.Pow((c + 0.055) / 1.055, 2.4))

/// WCAG 2.x relative luminance: 0 for black, 1 for white.
let relativeLuminance ((r, g, b): Rgb) : float =
    0.2126 * linear[r] + 0.7152 * linear[g] + 0.0722 * linear[b]

/// WCAG 2.x contrast ratio of two colours, `1..21`. Symmetric.
let contrastRatio (a: Rgb) (b: Rgb) : float =
    let la = relativeLuminance a
    let lb = relativeLuminance b
    (max la lb + 0.05) / (min la lb + 0.05)

/// The text colour for an accent of this luminance: whichever of black and
/// white has the higher contrast, black when they tie. Black's contrast is
/// `(L + 0.05) / 0.05` and white's `1.05 / (L + 0.05)`.
///
/// Takes the luminance, not the colour, so the exhaustive test can ask it
/// about every colour without building 16.7 million `Accent`s.
let onAccentFor (luminance: float) : OnAccent =
    if (luminance + 0.05) / 0.05 >= 1.05 / (luminance + 0.05) then Black else White

module Accent =
    /// The accent in a form field: a `#`, then six hex digits in either case,
    /// around nothing but whitespace the browser or a paste may add. Anything
    /// else is refused, including `#fff`, a bare `f26b1d` and a CSS function:
    /// the stored form and the stylesheet path are both six digits, and one
    /// accepted shorthand would be a second spelling to canonicalise.
    ///
    /// Checked by hand and not with a regex: `Char.IsAsciiHexDigit` is exactly
    /// `[0-9a-fA-F]`, with no Unicode digit and no `$`-before-newline to
    /// reason about.
    let parse (input: string) : Result<Accent, AccentError> =
        // IsNullOrWhiteSpace also covers a field that never arrived.
        if String.IsNullOrWhiteSpace input then
            Error NotHexColour
        else
            let trimmed = input.Trim()

            if trimmed.Length = 7 && trimmed[0] = '#' && Seq.forall Char.IsAsciiHexDigit (trimmed.Substring 1) then
                Ok(Accent(trimmed.Substring(1).ToLowerInvariant()))
            else
                Error NotHexColour

    /// The stored and stylesheet form, `f26b1d`.
    let hex (Accent value) : string = value

    let rgb (Accent value) : Rgb =
        let channel start = Convert.ToInt32(value.Substring(start, 2), 16)
        channel 0, channel 2, channel 4

    /// Contrast of the accent against another colour.
    let contrastWith (accent: Accent) (other: Rgb) : float = contrastRatio (rgb accent) other

    /// Black or white text, whichever reads better on the accent (FR-016).
    let onAccent (accent: Accent) : OnAccent = onAccentFor (relativeLuminance (rgb accent))

    /// FR-017: whether the accent would be hard to see on the card.
    let lowContrastOnCard (accent: Accent) : bool =
        let card =
            Convert.ToInt32(CardBackground.Substring(1, 2), 16),
            Convert.ToInt32(CardBackground.Substring(3, 2), 16),
            Convert.ToInt32(CardBackground.Substring(5, 2), 16)

        contrastWith accent card < LowContrast

/// A validated, rewritten logo (`PngLogo.validate`): the bytes to store, their
/// SHA-256 as lowercase hex (the logo's address), and the pixel size read from
/// `IHDR`. Defined here and not in `PngLogo` so the store does not depend on
/// the validator.
type Logo =
    { Png: byte[]
      Sha256: string
      Width: int
      Height: int }

/// Why an upload was refused. Each case maps to a notice that says what to do
/// next (Principle VI); the cases carrying a size let the notice name it.
type LogoError =
    | Empty
    /// More than 1.25 MB once stored, or past the request limit.
    | TooLarge of bytes: int64
    /// No PNG signature: JPEG, WebP, SVG, or anything sent without the script.
    | NotPng
    /// A bad length, CRC, IHDR field, IDAT size or filter byte.
    | Corrupt
    | Interlaced
    /// An unknown critical chunk.
    | UnsupportedChunk of name: string
    /// Under 32 pixels on a side.
    | TooSmall of width: int * height: int
    /// Over 512 pixels on a side.
    | TooBig of width: int * height: int

/// An app's logo as the pages see it: the address and the size, not the bytes.
type LogoRef =
    { Sha256: string
      Width: int
      Height: int }

/// An app's branding as the sign-in page and the Apps screen read it.
type AppBranding =
    { Accent: Accent option
      Logo: LogoRef option }
