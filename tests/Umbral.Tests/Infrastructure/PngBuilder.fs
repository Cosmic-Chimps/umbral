/// A test-only PNG encoder for the logo validator's tests (spec 006, research
/// R2, R11).
///
/// It writes PNGs of any shape the validator has to meet: every colour type,
/// ancillary chunks before or after the image data, image data split across
/// chunks, and the many ways a file can be wrong (a bad CRC, a wrong payload
/// size, a missing chunk). The image rows are filter type 0, compressed with
/// `ZLibStream`.
///
/// Its CRC-32 is written here, bit by bit, and shares nothing with the table
/// version in `Domains/PngLogo.fs`: a mistake in one is caught by the other,
/// where a shared copy would agree with itself and pass.
module Umbral.Tests.Infrastructure.PngBuilder

open System
open System.IO
open System.IO.Compression
open System.Text

/// The PNG signature, spelled out rather than shared with the product.
let signature = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |]

/// CRC-32 (the zlib/PNG polynomial, reflected), one bit at a time.
let crc32 (bytes: byte[]) : uint32 =
    let mutable crc = 0xFFFFFFFFu

    for b in bytes do
        crc <- crc ^^^ uint32 b

        for _ in 1..8 do
            crc <- if crc &&& 1u = 1u then (crc >>> 1) ^^^ 0xEDB88320u else crc >>> 1

    crc ^^^ 0xFFFFFFFFu

let private beUint32 (value: uint32) =
    [| byte (value >>> 24); byte (value >>> 16); byte (value >>> 8); byte value |]

/// One chunk: length, name, data, CRC of name and data.
let chunk (name: string) (data: byte[]) : byte[] =
    let nameBytes = Encoding.ASCII.GetBytes name
    Array.concat [ beUint32 (uint32 data.Length); nameBytes; data; beUint32 (crc32 (Array.append nameBytes data)) ]

/// The same chunk with its CRC flipped.
let chunkWithBadCrc (name: string) (data: byte[]) : byte[] =
    let good = chunk name data
    good[good.Length - 1] <- good[good.Length - 1] ^^^ 0xFFuy
    good

type Placement =
    | BeforeIhdr
    | BeforeIdat
    | AfterIdat

type Spec =
    { Width: int
      Height: int
      ColorType: int
      BitDepth: int
      Compression: int
      Filter: int
      Interlace: int
      /// PLTE contents; written for colour type 3 only unless a test says otherwise.
      Palette: byte[] option
      /// Extra chunks, by name and data.
      Extra: (string * byte[] * Placement) list
      /// How many IDAT chunks the compressed data is cut into.
      IdatChunks: int
      /// The chunk whose CRC is wrong, by name.
      BadCrc: string option
      /// Replaces the inflated image data: for a bomb, or data of the wrong size.
      Inflated: byte[] option
      /// Row filter byte for every row, normally 0.
      FilterByte: byte
      OmitIend: bool }

let private channels (colorType: int) =
    match colorType with
    | 0 -> 1
    | 2 -> 3
    | 3 -> 1
    | 4 -> 2
    | 6 -> 4
    | other -> failwith $"colour type {other}"

/// A valid PNG of this shape, with the defaults a test then overrides.
let png (width: int) (height: int) (colorType: int) (bitDepth: int) : Spec =
    { Width = width
      Height = height
      ColorType = colorType
      BitDepth = bitDepth
      Compression = 0
      Filter = 0
      Interlace = 0
      // Four entries, or as many as the bit depth can index when that is fewer.
      Palette =
        (if colorType = 3 then
             Some(Array.init (3 * min 4 (1 <<< bitDepth)) (fun i -> byte (i * 40)))
         else
             None)
      Extra = []
      IdatChunks = 1
      BadCrc = None
      Inflated = None
      FilterByte = 0uy
      OmitIend = false }

/// What the inflated data of a valid image has to be: each row a filter byte
/// and `ceil(width * bits per pixel / 8)` bytes of pixels.
let expectedLength (spec: Spec) : int =
    let bitsPerPixel = channels spec.ColorType * spec.BitDepth
    spec.Height * (1 + (spec.Width * bitsPerPixel + 7) / 8)

/// A valid row-by-row payload: a filter byte, then pixels that vary, so the
/// compressed form is not trivially small.
let imageData (spec: Spec) : byte[] =
    let rowBytes = (spec.Width * channels spec.ColorType * spec.BitDepth + 7) / 8
    let data = Array.zeroCreate<byte> (spec.Height * (1 + rowBytes))

    for row in 0 .. spec.Height - 1 do
        let start = row * (1 + rowBytes)
        data[start] <- spec.FilterByte

        for i in 0 .. rowBytes - 1 do
            data[start + 1 + i] <- byte ((row * 31 + i * 7) &&& 0xFF)

    data

let zlib (data: byte[]) : byte[] =
    use output = new MemoryStream()

    do
        use stream = new ZLibStream(output, CompressionLevel.Optimal, true)
        stream.Write(data, 0, data.Length)

    output.ToArray()

let private splitInto (parts: int) (data: byte[]) : byte[] list =
    if parts <= 1 || data.Length < parts then
        [ data ]
    else
        let size = data.Length / parts

        [ for i in 0 .. parts - 1 ->
              let start = i * size
              let length = if i = parts - 1 then data.Length - start else size
              data[start .. start + length - 1] ]

/// The compressed data of the image the spec describes.
let compressedData (spec: Spec) : byte[] =
    zlib (spec.Inflated |> Option.defaultWith (fun () -> imageData spec))

let ihdrData (spec: Spec) : byte[] =
    Array.concat
        [ beUint32 (uint32 spec.Width)
          beUint32 (uint32 spec.Height)
          [| byte spec.BitDepth
             byte spec.ColorType
             byte spec.Compression
             byte spec.Filter
             byte spec.Interlace |] ]

/// The file the spec describes.
let build (spec: Spec) : byte[] =
    let write (name: string) (data: byte[]) =
        if spec.BadCrc = Some name then chunkWithBadCrc name data else chunk name data

    let extras (placement: Placement) =
        [ for name, data, where in spec.Extra do
              if where = placement then
                  yield write name data ]

    let idats =
        splitInto spec.IdatChunks (compressedData spec) |> List.map (write "IDAT")

    Array.concat
        [ signature
          Array.concat (extras BeforeIhdr)
          write "IHDR" (ihdrData spec)
          (match spec.Palette with
           | Some palette -> write "PLTE" palette
           | None -> [||])
          Array.concat (extras BeforeIdat)
          Array.concat idats
          Array.concat (extras AfterIdat)
          (if spec.OmitIend then [||] else write "IEND" [||]) ]

/// The offset of the first chunk of this name, at its length field.
let findChunk (png: byte[]) (name: string) : int option =
    let target = Encoding.ASCII.GetBytes name
    let mutable offset = 8
    let mutable found = None

    while found.IsNone && offset + 12 <= png.Length do
        let length = (int png[offset] <<< 24) ||| (int png[offset + 1] <<< 16) ||| (int png[offset + 2] <<< 8) ||| int png[offset + 3]

        if png[offset + 4 .. offset + 7] = target then
            found <- Some offset
        else
            offset <- offset + 12 + length

    found

/// The file with a chunk's length field set to `length`, as a corrupt file would hold it.
let withLength (png: byte[]) (name: string) (length: int) : byte[] =
    let at = (findChunk png name).Value
    let copy = Array.copy png
    Array.blit (beUint32 (uint32 length)) 0 copy at 4
    copy

/// Common cases.
let valid (width: int) (height: int) : byte[] = build (png width height 6 8)
