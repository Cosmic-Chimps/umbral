/// The logo validator (spec 006, research R2): the security boundary of the
/// feature. Whatever an operator's browser, a script or an attacker with a
/// stolen session sends, the server stores only a PNG it has rewritten itself,
/// within the limits, and never decodes a pixel.
///
/// The PNGs come from `PngBuilder`, a separate encoder with its own CRC-32, so a
/// slip in the product's reader or writer is not repeated by the test's.
module Umbral.Tests.PngLogoTests

open System
open System.Diagnostics
open System.Security.Cryptography
open System.Text
open Xunit
open Umbral.Domains.Branding
open Umbral.Domains.PngLogo
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.PngBuilder

let private ok (result: Result<Logo, LogoError>) : Logo =
    match result with
    | Ok logo -> logo
    | Error e -> failwith $"expected a logo, got {e}"

let private refusal (result: Result<Logo, LogoError>) : LogoError =
    match result with
    | Ok logo -> failwith $"expected a refusal, got a {logo.Width}x{logo.Height} logo"
    | Error e -> e

/// The chunks of a PNG in order, each checked: the name, and whether its length
/// stays inside the file and its CRC matches (by the builder's own CRC-32).
let private chunksOf (png: byte[]) : string list =
    Assert.Equal<byte array>(signature, png[0..7])
    let mutable offset = 8
    let names = ResizeArray<string>()

    while offset < png.Length do
        let length = (int png[offset] <<< 24) ||| (int png[offset + 1] <<< 16) ||| (int png[offset + 2] <<< 8) ||| int png[offset + 3]
        Assert.True(offset + 12 + length <= png.Length, "a chunk runs past the end of the file")
        let body = png[offset + 4 .. offset + 7 + length]
        let stored = (uint32 png[offset + 8 + length] <<< 24) ||| (uint32 png[offset + 9 + length] <<< 16) ||| (uint32 png[offset + 10 + length] <<< 8) ||| uint32 png[offset + 11 + length]
        Assert.Equal(crc32 body, stored)
        names.Add(Encoding.ASCII.GetString png[offset + 4 .. offset + 7])
        offset <- offset + 12 + length

    Assert.Equal(png.Length, offset)
    List.ofSeq names

let private sha256Hex (bytes: byte[]) = Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()

// ---------------------------------------------------------------------------
// Accepted
// ---------------------------------------------------------------------------

/// Every colour type, with the bit depths each allows that a canvas or an image
/// editor produces.
let private shapes =
    [ 0, 1; 0, 2; 0, 4; 0, 8; 0, 16
      2, 8; 2, 16
      3, 1; 3, 2; 3, 4; 3, 8
      4, 8; 4, 16
      6, 8; 6, 16 ]

[<Fact>]
let ``every colour type and bit depth is accepted at 32x32`` () =
    for colorType, depth in shapes do
        let bytes = build (png 32 32 colorType depth)
        let logo = ok (validate bytes)
        Assert.Equal(32, logo.Width)
        Assert.Equal(32, logo.Height)

[<Fact>]
let ``the largest image is accepted at 512x512 in each colour type`` () =
    for colorType, depth in [ 0, 8; 2, 8; 3, 8; 4, 8; 6, 8 ] do
        let logo = ok (validate (build (png 512 512 colorType depth)))
        Assert.Equal(512, logo.Width)
        Assert.Equal(512, logo.Height)

[<Fact>]
let ``a wide and a tall image at the limits are accepted with the right size`` () =
    let wide = ok (validate (build (png 512 32 6 8)))
    Assert.Equal((512, 32), (wide.Width, wide.Height))

    let tall = ok (validate (build (png 32 512 6 8)))
    Assert.Equal((32, 512), (tall.Width, tall.Height))

    // Not square, not a multiple of 8: the row arithmetic is by bits.
    let odd = ok (validate (build (png 37 41 0 1)))
    Assert.Equal((37, 41), (odd.Width, odd.Height))

[<Fact>]
let ``a palette image keeps its palette and its transparency`` () =
    let spec =
        { png 32 32 3 8 with
            Extra = [ "tRNS", [| 255uy; 128uy; 0uy; 255uy |], BeforeIdat ] }

    let output = (ok (validate (build spec))).Png
    Assert.Equal<string list>([ "IHDR"; "PLTE"; "tRNS"; "IDAT"; "IEND" ], chunksOf output)
    // Kept as written, not regenerated.
    let at = (findChunk output "tRNS").Value
    Assert.Equal<byte array>([| 255uy; 128uy; 0uy; 255uy |], output[at + 8 .. at + 11])

[<Fact>]
let ``transparency on a greyscale and a truecolour image is kept`` () =
    for colorType, trns in [ 0, [| 0uy; 7uy |]; 2, [| 0uy; 1uy; 0uy; 2uy; 0uy; 3uy |] ] do
        let spec = { png 32 32 colorType 8 with Extra = [ "tRNS", trns, BeforeIdat ] }
        let output = (ok (validate (build spec))).Png
        Assert.Equal<string list>([ "IHDR"; "tRNS"; "IDAT"; "IEND" ], chunksOf output)

[<Fact>]
let ``image data split across several IDAT chunks is accepted and written as one`` () =
    let spec = { png 64 64 6 8 with IdatChunks = 5 }
    let input = build spec
    Assert.Equal(5, chunksOf input |> List.filter ((=) "IDAT") |> List.length)

    let output = (ok (validate input)).Png
    Assert.Equal<string list>([ "IHDR"; "IDAT"; "IEND" ], chunksOf output)

    // The pieces are joined in file order: the same image as one chunk.
    Assert.Equal<byte array>((ok (validate (build (png 64 64 6 8)))).Png, output)

[<Fact>]
let ``a file of a hundred thousand empty IDAT chunks is judged in linear time`` () =
    // 100,000 × 12 bytes stays under maxBytes. Collecting the chunks by
    // appending to a list was quadratic in their number (found in review).
    let empties = List.replicate 100_000 ("IDAT", Array.empty<byte>, BeforeIdat)
    let input = build { png 32 32 6 8 with Extra = empties }
    Assert.True(input.Length <= maxBytes)

    let clock = Diagnostics.Stopwatch.StartNew()
    let output = (ok (validate input)).Png
    clock.Stop()

    Assert.Equal<string list>([ "IHDR"; "IDAT"; "IEND" ], chunksOf output)
    Assert.True(clock.Elapsed < TimeSpan.FromSeconds 2.0, $"took {clock.Elapsed}")

[<Fact>]
let ``every ancillary chunk is dropped and the image survives`` () =
    let extras =
        [ "tEXt", Encoding.ASCII.GetBytes "Comment\000a private note", BeforeIdat
          "iTXt", Encoding.ASCII.GetBytes "Author\000\000\000\000\000someone", BeforeIdat
          "zTXt", Array.append (Encoding.ASCII.GetBytes "k\000\000") (zlib [| 1uy |]), BeforeIdat
          "eXIf", [| 1uy; 2uy; 3uy |], BeforeIdat
          "iCCP", Array.append (Encoding.ASCII.GetBytes "p\000\000") (zlib [| 1uy |]), BeforeIdat
          "gAMA", [| 0uy; 1uy; 0uy; 0uy |], BeforeIdat
          "cHRM", Array.zeroCreate 32, BeforeIdat
          "sRGB", [| 0uy |], BeforeIdat
          "pHYs", [| 0uy; 0uy; 0uy; 1uy; 0uy; 0uy; 0uy; 1uy; 1uy |], BeforeIdat
          "bKGD", [| 0uy; 0uy |], BeforeIdat
          "tIME", [| 7uy; 234uy; 1uy; 1uy; 0uy; 0uy; 0uy |], AfterIdat
          "acTL", [| 0uy; 0uy; 0uy; 2uy; 0uy; 0uy; 0uy; 0uy |], BeforeIdat
          "fcTL", Array.zeroCreate 26, AfterIdat
          "fdAT", [| 0uy; 0uy; 0uy; 1uy; 1uy; 2uy |], AfterIdat
          // A private ancillary chunk: the second letter says "private", the first "ancillary".
          "prVt", [| 1uy |], AfterIdat ]

    let input = build { png 48 48 6 8 with Extra = extras }
    let names = chunksOf input

    for name, _, _ in extras do
        Assert.Contains(name, names)

    let output = (ok (validate input)).Png
    Assert.Equal<string list>([ "IHDR"; "IDAT"; "IEND" ], chunksOf output)

    // And the private note itself is gone, not only its chunk name.
    Assert.DoesNotContain("private note", Encoding.ASCII.GetString output)

[<Fact>]
let ``the output is a different file from the input when the input carried anything extra`` () =
    let input = build { png 32 32 6 8 with Extra = [ "tEXt", Encoding.ASCII.GetBytes "k\000v", BeforeIdat ] }
    let output = (ok (validate input)).Png

    Assert.True(output.Length < input.Length)
    Assert.NotEqual<byte array>(input, output)

[<Fact>]
let ``the output re-validates to the same bytes`` () =
    for colorType, depth in shapes do
        let first = ok (validate (build { png 40 36 colorType depth with IdatChunks = 3 }))
        let second = ok (validate first.Png)
        Assert.Equal<byte array>(first.Png, second.Png)
        Assert.Equal(first.Sha256, second.Sha256)

[<Fact>]
let ``the hash is the lowercase hex SHA-256 of the output, never of the input`` () =
    let input = build { png 32 32 6 8 with Extra = [ "tEXt", Encoding.ASCII.GetBytes "k\000v", BeforeIdat ] }
    let logo = ok (validate input)

    Assert.Equal(64, logo.Sha256.Length)
    Assert.Equal(logo.Sha256.ToLowerInvariant(), logo.Sha256)
    Assert.Equal(sha256Hex logo.Png, logo.Sha256)
    Assert.NotEqual<string>(sha256Hex input, logo.Sha256)

[<Fact>]
let ``two different images get different addresses`` () =
    let a = ok (validate (valid 32 32))
    let b = ok (validate (valid 33 32))
    Assert.NotEqual<string>(a.Sha256, b.Sha256)

// ---------------------------------------------------------------------------
// Not a PNG
// ---------------------------------------------------------------------------

[<Fact>]
let ``what is not a PNG is NotPng, whatever it is`` () =
    let jpeg = Array.append [| 0xFFuy; 0xD8uy; 0xFFuy; 0xE0uy; 0uy; 16uy |] (Encoding.ASCII.GetBytes "JFIF\000\001\001\000\000\001\000\001\000\000")
    let webp = Array.concat [ Encoding.ASCII.GetBytes "RIFF"; [| 36uy; 0uy; 0uy; 0uy |]; Encoding.ASCII.GetBytes "WEBPVP8 "; Array.zeroCreate 30 ]
    let svg = Encoding.UTF8.GetBytes """<svg xmlns="http://www.w3.org/2000/svg" width="40" height="40"><script>alert(1)</script></svg>"""
    let html = Encoding.UTF8.GetBytes "<!DOCTYPE html><html><body>not an image</body></html>"
    let gif = Array.append (Encoding.ASCII.GetBytes "GIF89a") (Array.zeroCreate 30)
    // The PNG signature with one byte wrong, and a short prefix of it.
    let nearly = Array.copy (valid 32 32)
    nearly[1] <- 0x51uy

    for what, bytes in
        [ "a JPEG", jpeg
          "a WebP", webp
          "an SVG", svg
          "an HTML page", html
          "a GIF", gif
          "a PNG with a wrong signature", nearly
          "three bytes", [| 0x89uy; 0x50uy; 0x4Euy |]
          "one byte", [| 0x89uy |] ] do
        Assert.True((refusal (validate bytes) = NotPng), $"{what} was not refused as NotPng")

[<Fact>]
let ``nothing is Empty`` () =
    Assert.Equal(Empty, refusal (validate [||]))

[<Fact>]
let ``a file that is just the signature is corrupt, not a PNG that is missing`` () =
    Assert.Equal(Corrupt, refusal (validate signature))

// ---------------------------------------------------------------------------
// Corrupt
// ---------------------------------------------------------------------------

let private corrupt (what: string) (bytes: byte[]) =
    let result = validate bytes
    Assert.True((refusal result = Corrupt), $"{what}: expected Corrupt, got {result}")

[<Fact>]
let ``a truncated file is corrupt, at any point`` () =
    let whole = valid 40 40

    // Every cut short of the whole file: inside the signature it is not a PNG
    // yet, everywhere after that it is a PNG that ends too soon.
    for length in 9 .. whole.Length - 1 do
        corrupt $"cut at {length}" whole[0 .. length - 1]

[<Fact>]
let ``a bad CRC is corrupt, on any chunk`` () =
    for name in [ "IHDR"; "IDAT"; "IEND" ] do
        corrupt $"bad CRC on {name}" (build { png 32 32 6 8 with BadCrc = Some name })

    corrupt "bad CRC on PLTE" (build { png 32 32 3 8 with BadCrc = Some "PLTE" })
    // A chunk that would be dropped is checked all the same: it is part of the file's integrity.
    corrupt
        "bad CRC on a dropped chunk"
        (build { png 32 32 6 8 with Extra = [ "tEXt", [| 1uy |], BeforeIdat ]; BadCrc = Some "tEXt" })

[<Fact>]
let ``a chunk length that runs past the end is corrupt, even a huge one`` () =
    let whole = valid 32 32

    for length in [ whole.Length; 1_000_000; Int32.MaxValue; -1 (* 0xFFFFFFFF *) ] do
        corrupt $"IDAT length {length}" (withLength whole "IDAT" length)

[<Fact>]
let ``IHDR that is not first is corrupt`` () =
    corrupt "a chunk before IHDR" (build { png 32 32 6 8 with Extra = [ "tEXt", [| 1uy |], BeforeIhdr ] })

[<Fact>]
let ``IHDR twice, or of the wrong size, is corrupt`` () =
    let spec = png 32 32 6 8

    let twice =
        Array.concat [ signature; chunk "IHDR" (ihdrData spec); chunk "IHDR" (ihdrData spec); chunk "IDAT" (compressedData spec); chunk "IEND" [||] ]

    corrupt "IHDR twice" twice

    let short =
        Array.concat [ signature; chunk "IHDR" ((ihdrData spec).[0..11]); chunk "IDAT" (compressedData spec); chunk "IEND" [||] ]

    corrupt "a short IHDR" short

[<Fact>]
let ``a bit depth the colour type does not allow is corrupt`` () =
    for colorType, depth in
        [ 0, 3; 0, 32; 2, 4; 2, 1; 3, 16; 4, 4; 6, 4; 6, 1; 0, 0 ] do
        let spec = { png 32 32 colorType depth with Inflated = Some(Array.zeroCreate 10) }
        corrupt $"type {colorType} at {depth} bits" (build spec)

[<Fact>]
let ``a colour type that does not exist is corrupt`` () =
    for colorType in [ 1; 5; 7; 255 ] do
        let spec = { png 32 32 6 8 with ColorType = colorType; Inflated = Some(Array.zeroCreate 10) }
        let bytes = Array.concat [ signature; chunk "IHDR" (ihdrData spec); chunk "IDAT" (zlib (Array.zeroCreate 10)); chunk "IEND" [||] ]
        corrupt $"colour type {colorType}" bytes

[<Fact>]
let ``a compression or filter method other than 0 is corrupt`` () =
    corrupt "compression 1" (build { png 32 32 6 8 with Compression = 1 })
    corrupt "filter method 1" (build { png 32 32 6 8 with Filter = 1 })

[<Fact>]
let ``an interlace method other than 0 and 1 is corrupt`` () =
    corrupt "interlace 2" (build { png 32 32 6 8 with Interlace = 2 })

[<Fact>]
let ``image data one byte short or one byte long is corrupt`` () =
    let spec = png 32 32 6 8
    let exact = imageData spec

    corrupt "one byte short" (build { spec with Inflated = Some exact[0 .. exact.Length - 2] })
    corrupt "one byte long" (build { spec with Inflated = Some(Array.append exact [| 0uy |]) })
    corrupt "no data at all" (build { spec with Inflated = Some [||] })
    corrupt "a whole row short" (build { spec with Inflated = Some exact[0 .. exact.Length - 1 - 32 * 4] })

[<Fact>]
let ``a row filter byte above 4 is corrupt`` () =
    corrupt "filter byte 5" (build { png 32 32 6 8 with FilterByte = 5uy })
    corrupt "filter byte 255" (build { png 32 32 6 8 with FilterByte = 255uy })

    // 0 to 4 are the five filters, and any of them is fine.
    for filter in 0uy .. 4uy do
        ok (validate (build { png 32 32 6 8 with FilterByte = filter })) |> ignore

[<Fact>]
let ``a filter byte above 4 on a later row is corrupt`` () =
    let spec = png 32 32 6 8
    let data = imageData spec
    let rowLength = 1 + 32 * 4
    data[20 * rowLength] <- 9uy
    corrupt "row 20" (build { spec with Inflated = Some data })

[<Fact>]
let ``a missing IEND is corrupt`` () =
    corrupt "no IEND" (build { png 32 32 6 8 with OmitIend = true })

[<Fact>]
let ``a palette image without a palette is corrupt`` () =
    corrupt "type 3 without PLTE" (build { png 32 32 3 8 with Palette = None })

[<Fact>]
let ``image data that is not a zlib stream is corrupt, and so is a missing IDAT`` () =
    let spec = png 32 32 6 8
    let garbage = Array.init 200 (fun i -> byte (i * 13 + 7))

    corrupt "garbage in IDAT" (Array.concat [ signature; chunk "IHDR" (ihdrData spec); chunk "IDAT" garbage; chunk "IEND" [||] ])
    corrupt "no IDAT" (Array.concat [ signature; chunk "IHDR" (ihdrData spec); chunk "IEND" [||] ])
    corrupt "an empty IDAT" (Array.concat [ signature; chunk "IHDR" (ihdrData spec); chunk "IDAT" [||]; chunk "IEND" [||] ])

[<Fact>]
let ``a zero-sized image is corrupt`` () =
    corrupt "width 0" (build { png 0 32 6 8 with Inflated = Some [||] })
    corrupt "height 0" (build { png 32 0 6 8 with Inflated = Some [||] })

[<Fact>]
let ``a decompression bomb is refused quickly, without allocating what it inflates to`` () =
    // 512 x 512 RGBA says it holds about 1 MB of image data. The zlib stream
    // actually inflates to 50 MB, which compresses to a few dozen KB.
    let spec = png 512 512 6 8
    let bomb = build { spec with Inflated = Some(Array.zeroCreate 50_000_000) }
    Assert.True(bomb.Length < 200_000, "the test bomb should be small on the wire")

    let allocatedBefore = GC.GetAllocatedBytesForCurrentThread()
    let watch = Stopwatch.StartNew()
    let result = validate bomb
    watch.Stop()
    let allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore

    Assert.Equal(Corrupt, refusal result)
    Assert.True(watch.ElapsedMilliseconds < 2000L, $"took {watch.ElapsedMilliseconds} ms")
    // The bound is `expected + 1`, about 1 MB. Anything near 50 MB would mean it inflated first.
    Assert.True(allocated < 12_000_000L, $"allocated {allocated} bytes on this thread")

// ---------------------------------------------------------------------------
// Refused for what they are
// ---------------------------------------------------------------------------

[<Fact>]
let ``an interlaced image is refused as interlaced`` () =
    Assert.Equal(Interlaced, refusal (validate (build { png 32 32 6 8 with Interlace = 1 })))

[<Fact>]
let ``an unknown critical chunk is refused, naming it`` () =
    // The first letter's case says critical (upper) or ancillary (lower); an
    // image that needs a chunk Umbral does not know cannot be rewritten without it.
    let input = build { png 32 32 6 8 with Extra = [ "ABCD", [| 1uy; 2uy |], BeforeIdat ] }
    Assert.Equal(UnsupportedChunk "ABCD", refusal (validate input))

    // Also when it sits after the image data.
    let after = build { png 32 32 6 8 with Extra = [ "ZZZZ", [||], AfterIdat ] }
    Assert.Equal(UnsupportedChunk "ZZZZ", refusal (validate after))

[<Theory>]
[<InlineData(31, 32)>]
[<InlineData(32, 31)>]
[<InlineData(1, 1)>]
[<InlineData(31, 600)>]
let ``an image under 32 pixels on either side is TooSmall, with its size`` (width: int, height: int) =
    let result = validate (build { png width height 6 8 with Inflated = Some(Array.zeroCreate 8) })
    Assert.Equal(TooSmall(width, height), refusal result)

[<Theory>]
[<InlineData(513, 32)>]
[<InlineData(32, 513)>]
[<InlineData(1024, 1024)>]
[<InlineData(65535, 65535)>]
let ``an image over 512 pixels on either side is TooBig, with its size`` (width: int, height: int) =
    // The image data is tiny and wrong: the size is judged from IHDR alone,
    // before any arithmetic on it that could overflow or any inflating.
    let result = validate (build { png width height 6 8 with Inflated = Some(Array.zeroCreate 8) })
    Assert.Equal(TooBig(width, height), refusal result)

[<Fact>]
let ``dimensions near the integer limits are refused, never an exception`` () =
    for width, height in [ Int32.MaxValue, Int32.MaxValue; -1 (* 0xFFFFFFFF *), 32; 32, -1; 0x40000000, 0x40000000 ] do
        let spec = { png 32 32 6 8 with Width = width; Height = height; Inflated = Some(Array.zeroCreate 8) }
        let result = validate (build spec)
        Assert.True(Result.isError result, $"{width}x{height} was accepted")

[<Fact>]
let ``a file over the stored limit is TooLarge, with its size`` () =
    // Past the limit is refused before its chunks are walked, so the work done
    // on a hostile request is bounded by the limit and not by what it sent.
    let bytes = Array.append signature (Array.zeroCreate (maxBytes + 1 - signature.Length))
    Assert.Equal(TooLarge(int64 (maxBytes + 1)), refusal (validate bytes))

[<Fact>]
let ``a file exactly at the limit is judged on what it is`` () =
    let bytes = Array.append signature (Array.zeroCreate (maxBytes - signature.Length))
    Assert.Equal(Corrupt, refusal (validate bytes))

[<Fact>]
let ``the limits are the ones the contract names`` () =
    Assert.Equal(512, maxSide)
    Assert.Equal(32, minSide)
    Assert.Equal(1_310_720, maxBytes)

// ---------------------------------------------------------------------------
// Whatever arrives, nothing throws
// ---------------------------------------------------------------------------

/// A function, not a value: each call gets a PNG of its own to damage.
let private seeds () =
    [ valid 32 32
      build (png 33 34 3 4)
      build { png 40 40 6 8 with IdatChunks = 3; Extra = [ "tEXt", [| 1uy; 2uy |], BeforeIdat ] } ]

[<Fact>]
let ``flipping any single byte of a valid file never throws`` () =
    for seed in seeds () do
        for at in 0 .. seed.Length - 1 do
            for flip in [ 0x01uy; 0x80uy; 0xFFuy ] do
                let damaged = Array.copy seed
                damaged[at] <- damaged[at] ^^^ flip
                // Ok or Error, but a result: any exception fails the test.
                validate damaged |> ignore

[<Fact>]
let ``random damage and random bytes never throw`` () =
    let random = Random 20261015

    for seed in seeds () do
        for _ in 1..300 do
            let damaged = Array.copy seed

            for _ in 1 .. random.Next(1, 6) do
                damaged[random.Next damaged.Length] <- byte (random.Next 256)

            validate damaged |> ignore

    for _ in 1..300 do
        let bytes = Array.zeroCreate<byte> (random.Next(0, 400))
        random.NextBytes bytes
        validate bytes |> ignore

        // And the same behind a real signature, so the walk is reached.
        validate (Array.append signature bytes) |> ignore

[<Fact>]
let ``a chunk whose length field is any value never reads outside the file`` () =
    let whole = valid 32 32

    for name in [ "IHDR"; "IDAT"; "IEND" ] do
        for length in [ 0; 1; 12; 13; 14; 100; whole.Length - 20; whole.Length; Int32.MaxValue; Int32.MinValue; -1 ] do
            validate (withLength whole name length) |> ignore
