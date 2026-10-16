/// Validating an uploaded logo and rewriting it as a clean PNG (spec 006,
/// research R2).
///
/// This is the security boundary of the feature. The operator's browser crops
/// and converts the image (research R1), but nothing the server receives is
/// trusted to have come from that script: a stolen session, a hand-made request
/// and a browser with scripting off all arrive here too. So the server accepts
/// only a PNG, walks its structure strictly, and stores a file it wrote itself:
/// the signature, `IHDR`, the palette and transparency if there are any, one
/// `IDAT` holding the original compressed data, and `IEND`, each with a fresh
/// CRC. Every other chunk, the metadata of whatever tool made the file included,
/// is dropped, and the request body is never stored (FR-013).
///
/// It is not an image decoder. It never turns a pixel into anything: the one
/// zlib stream is inflated into a buffer of exactly the size the header
/// promises, plus one byte to see that nothing more comes. A stream that would
/// inflate to more is a bomb, and is refused without having been inflated.
///
/// Pure, with no I/O. No exception escapes: whatever bytes arrive, the answer is
/// a `Logo` or a `LogoError`.
module Umbral.Domains.PngLogo

open System
open System.IO
open System.IO.Compression
open System.Security.Cryptography
open Umbral.Domains.Branding

/// The largest side a stored logo may have. The sign-in page shows it at most
/// 96 px tall, and the browser scales anything larger down before it is sent.
let maxSide = 512

/// The smallest side: below this a logo is a smudge on a sign-in card.
let minSide = 32

/// The largest file, uploaded or stored: 1.25 MB. A canvas export of a
/// 512 × 512 image is a fraction of this. It also bounds the work a hostile
/// request can cause, because a file over it is refused before its chunks are
/// read.
let maxBytes = 1_310_720

/// What the admin script accepts before it will touch an image: a source file of
/// at most 2 MB and at most 4096 pixels on a side (research R3). They are not
/// the server's limits, which are the three above: a 4096 × 4096 image is 64 MB
/// of pixels in a browser tab, and the script refuses it first and says why. The
/// numbers live here so the page tells the script what they are and no number is
/// written twice.
let maxSourceBytes = 2 * 1024 * 1024

let maxSourceSide = 4096

let private signature = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |]

// ---------------------------------------------------------------------------
// CRC-32
// ---------------------------------------------------------------------------

/// The CRC-32 table of the PNG polynomial (reflected 0xEDB88320). A table of
/// twenty lines is cheaper than a package for `System.IO.Hashing`.
let private crcTable: uint32[] =
    Array.init 256 (fun n ->
        let mutable c = uint32 n

        for _ in 1..8 do
            c <- if c &&& 1u <> 0u then 0xEDB88320u ^^^ (c >>> 1) else c >>> 1

        c)

/// CRC-32 of `length` bytes of `bytes` from `start`. Callers have already
/// checked that the range lies inside the array.
let private crc32 (bytes: byte[]) (start: int) (length: int) : uint32 =
    let mutable crc = 0xFFFFFFFFu

    for i in start .. start + length - 1 do
        crc <- crcTable[int ((crc ^^^ uint32 bytes[i]) &&& 0xFFu)] ^^^ (crc >>> 8)

    crc ^^^ 0xFFFFFFFFu

let private readUInt32 (bytes: byte[]) (at: int) : uint32 =
    (uint32 bytes[at] <<< 24) ||| (uint32 bytes[at + 1] <<< 16) ||| (uint32 bytes[at + 2] <<< 8) ||| uint32 bytes[at + 3]

let private writeUInt32 (stream: Stream) (value: uint32) =
    stream.WriteByte(byte (value >>> 24))
    stream.WriteByte(byte (value >>> 16))
    stream.WriteByte(byte (value >>> 8))
    stream.WriteByte(byte value)

// ---------------------------------------------------------------------------
// The header
// ---------------------------------------------------------------------------

type private Header =
    { Width: int
      Height: int
      BitDepth: int
      ColorType: int
      /// The 13 bytes as they were, written back unchanged.
      Raw: byte[] }

/// Samples per pixel, by colour type; `None` for a type PNG does not define.
let private channels (colorType: int) =
    match colorType with
    | 0 -> Some 1
    | 2 -> Some 3
    | 3 -> Some 1
    | 4 -> Some 2
    | 6 -> Some 4
    | _ -> None

/// The bit depths PNG allows for each colour type.
let private depthAllowed (colorType: int) (depth: int) =
    match colorType with
    | 0 -> List.contains depth [ 1; 2; 4; 8; 16 ]
    | 3 -> List.contains depth [ 1; 2; 4; 8 ]
    | 2
    | 4
    | 6 -> List.contains depth [ 8; 16 ]
    | _ -> false

/// A dimension as an `int` for a message: the header holds 32 bits, and one
/// over `Int32.MaxValue` is reported as the largest `int`, not wrapped negative.
let private clampDimension (value: uint32) =
    if value > uint32 Int32.MaxValue then Int32.MaxValue else int value

/// Judge `IHDR` as soon as it is read, so nothing is spent on the rest of a file
/// that cannot be used. The order is the order of the answers: what is not a
/// PNG header at all, then interlacing, then size. The size is judged from the
/// header alone, before any arithmetic on it.
let private readHeader (bytes: byte[]) (at: int) : Result<Header, LogoError> =
    let width = readUInt32 bytes at
    let height = readUInt32 bytes (at + 4)
    let depth = int bytes[at + 8]
    let colorType = int bytes[at + 9]
    let compression = bytes[at + 10]
    let filter = bytes[at + 11]
    let interlace = bytes[at + 12]

    if
        width = 0u
        || height = 0u
        || (channels colorType).IsNone
        || not (depthAllowed colorType depth)
        || compression <> 0uy
        || filter <> 0uy
        || interlace > 1uy
    then
        Error Corrupt
    elif interlace = 1uy then
        // Canvas never produces Adam7, and supporting it doubles the size arithmetic.
        Error Interlaced
    else
        let w = clampDimension width
        let h = clampDimension height

        if w < minSide || h < minSide then
            Error(TooSmall(w, h))
        elif w > maxSide || h > maxSide then
            Error(TooBig(w, h))
        else
            Ok
                { Width = w
                  Height = h
                  BitDepth = depth
                  ColorType = colorType
                  Raw = bytes[at .. at + 12] }

// ---------------------------------------------------------------------------
// The chunk walk
// ---------------------------------------------------------------------------

/// Where a chunk's data lies in the input.
type private Span = { Start: int; Length: int }

/// What the walk keeps. Everything else is skipped.
type private Parts =
    { Header: Header option
      Palette: Span option
      Transparency: Span option
      /// Newest first: prepended as they are met and reversed once at the end.
      /// Appending would cost quadratic time for a file of many tiny `IDAT`s,
      /// and 1.25 MB of 12-byte chunks is about 100,000 of them.
      Idat: Span list
      /// Reached `IEND`.
      Ended: bool }

let private isLetter (b: byte) = (b >= 65uy && b <= 90uy) || (b >= 97uy && b <= 122uy)

/// Whether the chunk name's first letter is uppercase: a critical chunk, one a
/// decoder may not skip.
let private isCritical (name: byte[]) = name[0] < 97uy

/// Walk the chunks from just after the signature. Every length is checked
/// against the bytes that remain before it is used to slice or to advance, and
/// every CRC before the chunk is believed. The first problem ends the walk.
///
/// The rules checked here are the file's structure; what the header's values
/// mean for size and for the image data is judged by `readHeader` and `inflate`.
let rec private walk (bytes: byte[]) (at: int) (parts: Parts) : Result<Parts, LogoError> =
    if parts.Ended then
        // Whatever follows IEND is not part of the image and is not kept.
        Ok parts
    elif at = bytes.Length then
        // The file ends where a chunk should begin, and IEND never came.
        Error Corrupt
    elif bytes.Length - at < 12 then
        Error Corrupt
    else
        let length = readUInt32 bytes at

        // The chunk needs its 12 bytes of framing and `length` of data. Compared
        // as unsigned, so a length with the top bit set is simply too long.
        if int64 length > int64 (bytes.Length - at - 12) then
            Error Corrupt
        else
            let length = int length
            let name = bytes[at + 4 .. at + 7]

            if not (Array.forall isLetter name) then
                Error Corrupt
            elif crc32 bytes (at + 4) (4 + length) <> readUInt32 bytes (at + 8 + length) then
                Error Corrupt
            else
                let dataAt = at + 8
                let next = at + 12 + length
                let nameText = Text.Encoding.ASCII.GetString name
                let here = { Start = dataAt; Length = length }

                match nameText, parts.Header with
                | "IHDR", None when length = 13 ->
                    match readHeader bytes dataAt with
                    | Error e -> Error e
                    | Ok header -> walk bytes next { parts with Header = Some header }
                | _, None ->
                    // IHDR must come first, and it did not (or is the wrong size).
                    Error Corrupt
                | "IHDR", Some _ -> Error Corrupt
                | "PLTE", Some header ->
                    if parts.Palette.IsSome || not parts.Idat.IsEmpty then
                        Error Corrupt
                    else
                        match header.ColorType with
                        // Required: it is what the pixel values index.
                        | 3 ->
                            if length = 0 || length % 3 <> 0 || length / 3 > (1 <<< header.BitDepth) then
                                Error Corrupt
                            else
                                walk bytes next { parts with Palette = Some here }
                        // A suggested palette on a truecolour image. Dropped.
                        | 2
                        | 6 -> walk bytes next parts
                        // Not allowed on greyscale.
                        | _ -> Error Corrupt
                | "tRNS", Some header ->
                    if parts.Transparency.IsSome || not parts.Idat.IsEmpty then
                        Error Corrupt
                    else
                        let allowed =
                            match header.ColorType with
                            | 0 -> length = 2
                            | 2 -> length = 6
                            | 3 ->
                                // One alpha value per palette entry at most, and a palette must already be here.
                                match parts.Palette with
                                | Some palette -> length >= 1 && length <= palette.Length / 3
                                | None -> false
                            // Colour types with their own alpha channel have no tRNS.
                            | _ -> false

                        if allowed then
                            walk bytes next { parts with Transparency = Some here }
                        else
                            Error Corrupt
                | "IDAT", Some _ -> walk bytes next { parts with Idat = here :: parts.Idat }
                | "IEND", Some _ ->
                    if length <> 0 then
                        Error Corrupt
                    else
                        walk bytes next { parts with Ended = true }
                | _, Some _ ->
                    // An unknown chunk is skipped when it is ancillary, and the
                    // image is refused when it is critical: a file that needs
                    // something Umbral does not understand cannot be rewritten
                    // without it.
                    if isCritical name then
                        Error(UnsupportedChunk nameText)
                    else
                        walk bytes next parts

// ---------------------------------------------------------------------------
// The image data
// ---------------------------------------------------------------------------

/// Inflate the concatenated `IDAT` stream and check that it holds exactly the
/// rows the header promises, each starting with a filter byte of 0 to 4. No
/// pixel is decoded.
///
/// The buffer is `expected + 1`: if the stream fills it, there was more than the
/// header said, which is a bomb or a damaged file, and it was not inflated
/// further to find out how much. Reading to the end of a stream of exactly
/// `expected` bytes also lets zlib check the stream's own checksum.
let private inflatesToRows (header: Header) (compressed: byte[]) : bool =
    let bitsPerPixel = (channels header.ColorType |> Option.get) * header.BitDepth
    let rowBytes = (int64 header.Width * int64 bitsPerPixel + 7L) / 8L
    let expected = int64 header.Height * (1L + rowBytes)

    // Bounded by the 512-pixel limit on both sides: 2 MB at most (16-bit RGBA).
    let buffer = Array.zeroCreate<byte> (int expected + 1)

    use input = new MemoryStream(compressed)
    use zlib = new ZLibStream(input, CompressionMode.Decompress)
    let mutable total = 0
    let mutable reading = true

    while reading && total < buffer.Length do
        let read = zlib.Read(buffer, total, buffer.Length - total)

        if read = 0 then reading <- false else total <- total + read

    if int64 total <> expected then
        false
    else
        let stride = int rowBytes + 1
        let mutable filtersValid = true

        for row in 0 .. header.Height - 1 do
            if buffer[row * stride] > 4uy then
                filtersValid <- false

        filtersValid

// ---------------------------------------------------------------------------
// The rewrite
// ---------------------------------------------------------------------------

/// A chunk, with a fresh CRC.
let private writeChunk (stream: Stream) (name: string) (data: byte[]) (start: int) (length: int) =
    let nameBytes = Text.Encoding.ASCII.GetBytes name
    writeUInt32 stream (uint32 length)
    stream.Write(nameBytes, 0, 4)
    stream.Write(data, start, length)

    // The CRC covers the name and the data, so it is taken over a copy of both.
    let covered = Array.zeroCreate<byte> (4 + length)
    Array.blit nameBytes 0 covered 0 4
    Array.blit data start covered 4 length
    writeUInt32 stream (crc32 covered 0 covered.Length)

let private rewrite (input: byte[]) (header: Header) (parts: Parts) (compressed: byte[]) : byte[] =
    use output = new MemoryStream()
    output.Write(signature, 0, signature.Length)
    writeChunk output "IHDR" header.Raw 0 13

    // Only a palette image carries its palette; the others' suggested palettes were dropped.
    if header.ColorType = 3 then
        parts.Palette |> Option.iter (fun p -> writeChunk output "PLTE" input p.Start p.Length)

    parts.Transparency |> Option.iter (fun t -> writeChunk output "tRNS" input t.Start t.Length)
    writeChunk output "IDAT" compressed 0 compressed.Length
    writeChunk output "IEND" [||] 0 0
    output.ToArray()

// ---------------------------------------------------------------------------
// The one entry
// ---------------------------------------------------------------------------

let private validateUnchecked (bytes: byte[]) : Result<Logo, LogoError> =
    if bytes.Length = 0 then
        Error Empty
    elif bytes.Length < signature.Length || bytes[0 .. signature.Length - 1] <> signature then
        // Not a PNG, and nothing else is accepted: JPEG, WebP, SVG, GIF or a page
        // of HTML all land here, and the notice says to turn the script on.
        Error NotPng
    elif bytes.Length > maxBytes then
        Error(TooLarge(int64 bytes.Length))
    else
        let empty =
            { Header = None
              Palette = None
              Transparency = None
              Idat = []
              Ended = false }

        match walk bytes signature.Length empty with
        | Error e -> Error e
        | Ok parts ->
            match parts.Header with
            | None -> Error Corrupt
            | Some _ when parts.Idat.IsEmpty -> Error Corrupt
            | Some header when header.ColorType = 3 && parts.Palette.IsNone -> Error Corrupt
            | Some header ->
                let compressed =
                    parts.Idat
                    |> List.rev
                    |> List.map (fun s -> bytes[s.Start .. s.Start + s.Length - 1])
                    |> Array.concat

                if not (inflatesToRows header compressed) then
                    Error Corrupt
                else
                    let png = rewrite bytes header parts compressed

                    Ok
                        { Png = png
                          Sha256 = Convert.ToHexString(SHA256.HashData png).ToLowerInvariant()
                          Width = header.Width
                          Height = header.Height }

/// Check an upload and rewrite it. `Ok` carries the bytes to store, never the
/// ones that came in.
///
/// The `try` is the guarantee that no exception escapes: a bug in a rule above
/// would otherwise surface as a 500 on an operator's upload. Every rule is
/// written not to throw, and a failure to inflate (`InvalidDataException` from
/// a damaged stream) is the one expected exception, so anything that does get
/// here is a file this code could not make sense of, which is `Corrupt`.
let validate (bytes: byte[]) : Result<Logo, LogoError> =
    try
        validateUnchecked bytes
    with _ ->
        Error Corrupt
