/// The accent colour and the text colour chosen for it (spec 006, research R4):
/// what may be typed, what is stored, and that the text on an accent is always
/// readable (SC-003), for every colour and not only the ones a table lists.
module Umbral.Tests.AccentTests

open System
open Xunit
open Umbral.Domains.Branding

let private hexOf (input: string) =
    match Accent.parse input with
    | Ok accent -> Accent.hex accent
    | Error e -> failwith $"'{input}' was refused: {e}"

let private accentOf (input: string) =
    match Accent.parse input with
    | Ok accent -> accent
    | Error e -> failwith $"'{input}' was refused: {e}"

[<Theory>]
[<InlineData("#f26b1d")>]
[<InlineData("#F26B1D")>]
[<InlineData(" #f26b1d ")>]
[<InlineData("#F26b1D")>]
[<InlineData("\t#f26b1d\n")>]
let ``a hash and six hex digits, either case, is stored lowercase without the hash`` (input: string) =
    Assert.Equal("f26b1d", hexOf input)

[<Theory>]
[<InlineData("f26b1d")>]
[<InlineData("#fff")>]
[<InlineData("#f26b1dff")>]
[<InlineData("orange")>]
[<InlineData("rgb(242,107,29)")>]
[<InlineData("#g26b1d")>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData("#")>]
[<InlineData("##f26b1d")>]
[<InlineData("#f26b1 d")>]
// Non-ASCII digits are letters to Char.IsDigit; the rule is the ASCII hex digits.
[<InlineData("#f26b1١")>]
let ``anything else is refused as not a hex colour`` (input: string) =
    Assert.Equal(Error NotHexColour, Accent.parse input |> Result.map Accent.hex)

[<Fact>]
let ``a null input is refused rather than throwing`` () =
    Assert.Equal(Error NotHexColour, Accent.parse (Unchecked.defaultof<string>) |> Result.map Accent.hex)

[<Fact>]
let ``rgb returns the three channels`` () =
    Assert.Equal((242, 107, 29), Accent.rgb (accentOf "#f26b1d"))
    Assert.Equal((0, 0, 0), Accent.rgb (accentOf "#000000"))
    Assert.Equal((255, 255, 255), Accent.rgb (accentOf "#FFFFFF"))

[<Fact>]
let ``luminance of black and white are the ends of the scale`` () =
    Assert.Equal(0.0, relativeLuminance (0, 0, 0))
    Assert.Equal(1.0, relativeLuminance (255, 255, 255), 10)
    Assert.Equal(21.0, contrastRatio (0, 0, 0) (255, 255, 255), 10)
    // Symmetric: the ratio does not depend on which colour is named first.
    Assert.Equal(contrastRatio (10, 20, 30) (200, 100, 50), contrastRatio (200, 100, 50) (10, 20, 30), 12)

[<Fact>]
let ``luminance follows the WCAG 2 definition for a mid colour`` () =
    // #808080: each channel 128/255 = 0.50196, linearised ((c + 0.055) / 1.055) ^ 2.4 = 0.21586.
    Assert.Equal(0.21586, relativeLuminance (128, 128, 128), 4)

[<Fact>]
let ``known colours get the text colour a person would pick`` () =
    Assert.Equal(Black, Accent.onAccent (accentOf "#f26b1d"))
    Assert.Equal(White, Accent.onAccent (accentOf "#0a0a10"))
    Assert.Equal(White, Accent.onAccent (accentOf "#1f6fa8"))
    // Umbral's own accent is light enough for dark text, as its on-primary says.
    Assert.Equal(Black, Accent.onAccent (accentOf "#5fb3e8"))
    Assert.Equal(Black, Accent.onAccent (accentOf "#ffffff"))
    Assert.Equal(White, Accent.onAccent (accentOf "#000000"))

[<Fact>]
let ``the text colour is chosen where black and white have equal contrast`` () =
    // The crossover: (L + 0.05) / 0.05 = 1.05 / (L + 0.05), so L = sqrt(0.0525) - 0.05.
    let crossover = sqrt 0.0525 - 0.05
    Assert.Equal(Black, onAccentFor (crossover + 1e-6))
    Assert.Equal(White, onAccentFor (crossover - 1e-6))
    // At the crossover the worse choice is still about 4.58:1 (research R4).
    Assert.Equal(4.5826, (crossover + 0.05) / 0.05, 3)

/// Every one of the 2^24 colours. The luminance is worked out here, from a
/// table of the 256 linearised channel values and the WCAG weights, and not
/// by the code under test, so a slip in the product's maths is not repeated
/// by the check on it. Only the choice (`onAccentFor`) is the product's.
[<Fact>]
let ``text on any accent colour has at least 4.5 to 1 contrast (SC-003)`` () =
    let linear =
        Array.init 256 (fun i ->
            let c = float i / 255.0
            if c <= 0.03928 then c / 12.92 else Math.Pow((c + 0.055) / 1.055, 2.4))

    let mutable worst = Double.MaxValue
    let mutable worstColour = 0
    let mutable fails = 0

    for colour in 0..0xFFFFFF do
        let l =
            0.2126 * linear[(colour >>> 16) &&& 0xFF]
            + 0.7152 * linear[(colour >>> 8) &&& 0xFF]
            + 0.0722 * linear[colour &&& 0xFF]

        // Black is luminance 0 and white is luminance 1.
        let contrast =
            match onAccentFor l with
            | Black -> (l + 0.05) / 0.05
            | White -> 1.05 / (l + 0.05)

        if contrast < worst then
            worst <- contrast
            worstColour <- colour

        if contrast < 4.5 then
            fails <- fails + 1

    Assert.True((fails = 0), $"{fails} colours have text below 4.5:1; the worst is #{worstColour:x6} at {worst:F3}")
    // Guards the loop: it reached the crossover, where the worst case is.
    Assert.InRange(worst, 4.5, 4.7)

[<Fact>]
let ``the product's luminance agrees with the table on a spread of colours, and onAccent with onAccentFor`` () =
    // A prime stride visits colours of every kind without repeating a pattern.
    for colour in 0..4099..0xFFFFFF do
        let rgb = ((colour >>> 16) &&& 0xFF, (colour >>> 8) &&& 0xFF, colour &&& 0xFF)
        let accent = accentOf $"#{colour:x6}"
        Assert.Equal(rgb, Accent.rgb accent)
        Assert.Equal(onAccentFor (relativeLuminance rgb), Accent.onAccent accent)

        // The text colour the accent gets is at least 4.5:1 against it, through the product's own ratio.
        let text = match Accent.onAccent accent with Black -> (0, 0, 0) | White -> (255, 255, 255)
        Assert.True(Accent.contrastWith accent text >= 4.5, $"#{colour:x6}")

[<Fact>]
let ``an accent that would vanish into either card is flagged, the others are not`` () =
    // FR-017, amended by 007 (FR-023): below 3:1 against the dark card, #14141f,
    // or the light card, #fffdf8, in whichever theme the visitor has.
    // Ratios by the WCAG formula, dark card then light card:
    //   #0a0a10  1.08:1 and 18.6:1  warns, on the dark card
    //   #14141f  1.00:1 and 17.7:1  warns, on the dark card
    //   #ffffff  17.7:1 and 1.02:1  warns, on the light card (was fine on spec 006's dark card)
    //   #5fb3e8  7.2:1  and 2.27:1  warns, on the light card
    //   #f26b1d  6.00:1 and 2.995:1 warns, on the light card, by a hair: spec 006's own example
    //   #d9480f  4.25:1 and 4.23:1  fine on both
    //   #c2410c  3.53:1 and 5.09:1  fine on both
    Assert.True(Accent.lowContrastOnCard (accentOf "#0a0a10"))
    Assert.True(Accent.lowContrastOnCard (accentOf "#14141f"))
    Assert.True(Accent.lowContrastOnCard (accentOf "#ffffff"))
    Assert.True(Accent.lowContrastOnCard (accentOf "#fffdf8"))
    Assert.True(Accent.lowContrastOnCard (accentOf "#5fb3e8"))
    Assert.True(Accent.lowContrastOnCard (accentOf "#f26b1d"))
    Assert.False(Accent.lowContrastOnCard (accentOf "#d9480f"))
    Assert.False(Accent.lowContrastOnCard (accentOf "#c2410c"))

[<Fact>]
let ``the contrast figures quoted for the two cards are the ones the product computes`` () =
    let dark = (0x14, 0x14, 0x1f)
    let light = (0xff, 0xfd, 0xf8)
    let ratio hex card = Accent.contrastWith (accentOf hex) card

    Assert.InRange(ratio "#f26b1d" dark, 5.95, 6.05)
    Assert.InRange(ratio "#f26b1d" light, 2.99, 3.0)
    Assert.InRange(ratio "#d9480f" dark, 4.2, 4.3)
    Assert.InRange(ratio "#d9480f" light, 4.2, 4.3)
    Assert.InRange(ratio "#c2410c" dark, 3.5, 3.6)
    Assert.InRange(ratio "#c2410c" light, 5.0, 5.2)
    Assert.InRange(ratio "#5fb3e8" light, 2.2, 2.35)
    Assert.InRange(ratio "#ffffff" light, 1.0, 1.05)
    Assert.InRange(ratio "#0a0a10" dark, 1.0, 1.1)
