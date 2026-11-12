/// Validating the SVG an operator pastes as the Apple button's logo, and
/// rebuilding it as a clean one (spec 014, research R5; FR-011, FR-012).
///
/// This is the security boundary of the feature. Apple's "Sign in with Apple"
/// artwork may not be bundled with Umbral, so the operator of the Apple
/// connector pastes Apple's own SVG into the admin pane. Pasted SVG is never put
/// into a page as it is: it can carry `<script>`, `on*` handlers, `foreignObject`
/// and `href` references, and the sign-in page is where people type their
/// credentials. So the text is read as XML with no DTD, walked against an
/// allowlist, and what is kept is a handful of validated values: the `viewBox`
/// and, for each path, its `d`, its `fill-rule` and its `transform`. `render`
/// writes the SVG that is stored and drawn from those values alone, so nothing
/// the operator typed is ever echoed except through a pattern it had to match.
///
/// Apple's files are Sketch exports: a background `rect`, a `title`, groups that
/// carry `fill-rule` and sometimes a `transform`, and the mark itself as one or
/// more `path`s. The background, the title, ids and colours are dropped: the
/// button draws the mark in `currentColor` on the button's own background.
///
/// Pure, with no I/O. No exception escapes: whatever text arrives, the answer is
/// an `AppleLogo` or an `AppleLogoError`.
module Umbral.Domains.AppleLogo

open System
open System.Globalization
open System.IO
open System.Text
open System.Text.RegularExpressions
open System.Xml
open System.Xml.Linq

/// Why a pasted text was refused. The admin pane says each in its own words and
/// never draws the text back.
type AppleLogoError =
    /// Not XML, or XML with a DTD, or no `svg` root, or no usable `viewBox`.
    | NotSvg
    /// An SVG, but with more than a logo needs: an element, an attribute or a
    /// value outside the allowlist, or no path to draw.
    | MoreThanShapes
    /// More than `MaxChars` characters.
    | TooLarge

/// One shape of the mark. `D` matched the path-data alphabet and `Transform`
/// was re-serialised from numbers, so neither can hold a quote, `<` or `&`.
type LogoPath =
    { D: string
      FillRule: string option
      Transform: string option }

/// A logo that came out of `parse`: the only way to make one. The fields are
/// private so that no other code can build a value `render` would write out
/// unchecked.
type AppleLogo =
    private
        { ViewBox: string
          Paths: LogoPath list }

/// The largest pasted text, and the largest stored text, in characters. An
/// Apple file is about 1 KB; 32 KiB is generous, and it bounds the work a
/// hostile paste can cause because longer text is refused before it is parsed.
[<Literal>]
let MaxChars = 32768

/// The most paths a logo may have. Apple's marks have one or two.
let private maxPaths = 64

/// The longest `d` of one path.
let private maxPathData = 16384

/// The deepest nesting of elements. Sketch writes three levels; this stops a
/// pasted text of nothing but `<g>` from recursing for thousands of frames.
let private maxDepth = 16

let private svgNamespace = "http://www.w3.org/2000/svg"

/// The characters XML calls white space, and nothing else. `\s` would also
/// match what an XML parser does not normalise, and a stored `d` has to come
/// back from the parser unchanged.
let private space = "[ \t\r\n]"

/// An SVG number. `[0-9]`, because `\d` matches digits of other scripts. The
/// fraction is one optional group, so a long run of digits has one way to match:
/// `[0-9]+\.?[0-9]*` could split it at every place and backtracked
/// quadratically on 32 KiB of digits followed by a letter (code review, N1).
let private numberPattern = @"[-+]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][-+]?[0-9]+)?"

let private numberRegex =
    Regex($"^{numberPattern}\\z", RegexOptions.CultureInvariant)

/// The alphabet of path data: the commands, digits, the exponent, the sign and
/// the separators. Nothing that could close an attribute or open a tag.
let private pathDataRegex =
    Regex("^[MmZzLlHhVvCcSsQqTtAa0-9eE.,+\\- \t\r\n]+\\z", RegexOptions.CultureInvariant)

let private separators = Regex("[ \t\r\n,]+", RegexOptions.CultureInvariant)

let private whitespaceRuns = Regex($"{space}+", RegexOptions.CultureInvariant)

/// One transform function, found at the point where the last one ended. The
/// arguments are `[^()]*` and are checked afterwards, number by number.
let private transformFunction =
    Regex($"\\G[ \t\r\n,]*(translate|scale|matrix){space}*\\(([^()]*)\\)", RegexOptions.CultureInvariant)

/// Raised inside the walk to stop at the first thing outside the allowlist, and
/// caught in `parse`.
exception private Refused of AppleLogoError

let private refuse (error: AppleLogoError) : 'a = raise (Refused error)

let private isXmlSpace (c: char) =
    c = ' ' || c = '\t' || c = '\r' || c = '\n'

/// Text that is only XML white space: the line breaks between elements, which
/// the reader keeps when a file says `xml:space="preserve"`.
let private isBlank (text: XText) = text.Value |> Seq.forall isXmlSpace

/// Numbers separated by white space and/or commas, each a finite number.
/// None when any token is not.
let private parseNumbers (text: string) : float list option =
    let tokens = separators.Split(text.Trim([| ' '; '\t'; '\r'; '\n' |]))

    let numbers =
        tokens
        |> Array.map (fun token ->
            if numberRegex.IsMatch token then
                match Double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture) with
                | true, value when Double.IsFinite value -> Some value
                | _ -> None
            else
                None)

    if numbers |> Array.forall Option.isSome then
        Some(numbers |> Array.choose id |> List.ofArray)
    else
        None

/// A number as the shortest text that reads back as the same number.
let private format (value: float) = value.ToString("R", CultureInfo.InvariantCulture)

let private joinNumbers (numbers: float list) =
    numbers |> List.map format |> String.concat " "

/// The four numbers of a `viewBox`, written back from the parsed values. A
/// width or height that is not above zero draws nothing, so it is not a logo.
let private parseViewBox (text: string) : string =
    match parseNumbers text with
    | Some [ minX; minY; width; height ] when width > 0.0 && height > 0.0 -> joinNumbers [ minX; minY; width; height ]
    | _ -> refuse NotSvg

/// A `transform` made only of `translate`, `scale` and `matrix` with numbers,
/// written back from the parsed values (never the raw string). Anything else,
/// `rotate` and `skew` included, is outside what Apple's files need.
let private parseTransform (text: string) : string =
    let functions = ResizeArray<string>()
    let mutable position = 0
    let mutable go = true

    while go do
        let m = transformFunction.Match(text, position)

        if m.Success then
            let name = m.Groups[1].Value

            let arguments =
                match parseNumbers m.Groups[2].Value with
                | Some numbers -> numbers
                | None -> refuse MoreThanShapes

            let count = List.length arguments

            let allowed =
                match name with
                | "matrix" -> count = 6
                | _ -> count = 1 || count = 2

            if not allowed then refuse MoreThanShapes
            functions.Add $"{name}({joinNumbers arguments})"
            position <- m.Index + m.Length
        else
            go <- false

    // Whatever is left must be white space: a stray `; x` is not a transform.
    let rest = text.Substring position

    if functions.Count = 0 || not (rest |> Seq.forall isXmlSpace) then
        refuse MoreThanShapes

    String.concat " " functions

let private attribute (name: string) (element: XElement) : string option =
    match element.Attribute(XName.Get name) with
    | null -> None
    | a -> Some a.Value

/// A `width` or `height` of the root: one number above zero, with or without
/// `px` (Sketch writes `56px`). Any other unit is not a size this can use.
let private sizeOf (name: string) (root: XElement) : float option =
    match attribute name root with
    | Some text ->
        let trimmed = text.Trim([| ' '; '\t'; '\r'; '\n' |])

        let number =
            if trimmed.EndsWith("px", StringComparison.Ordinal) then trimmed.Substring(0, trimmed.Length - 2) else trimmed

        match parseNumbers number with
        | Some [ value ] when value > 0.0 -> Some value
        | _ -> None
    | None -> None

/// `fill-rule` is kept only as exactly one of its two words. Anything else is
/// read as not set, as a browser reads an invalid presentation attribute.
let private fillRuleOf (element: XElement) : string option =
    match attribute "fill-rule" element with
    | Some "nonzero" -> Some "nonzero"
    | Some "evenodd" -> Some "evenodd"
    | _ -> None

/// Event handlers and references are refused wherever they are, on any element,
/// whatever the namespace: this is what makes a script or a fetch impossible
/// even if a later change were to widen the allowlist of elements.
let private checkAttributes (element: XElement) =
    for a in element.Attributes() do
        if not a.IsNamespaceDeclaration then
            let name = a.Name.LocalName

            if name.StartsWith("on", StringComparison.OrdinalIgnoreCase) then
                refuse MoreThanShapes

            if name.Equals("href", StringComparison.OrdinalIgnoreCase) then
                refuse MoreThanShapes

            if
                name.Equals("style", StringComparison.OrdinalIgnoreCase)
                && a.Value.Contains("url(", StringComparison.OrdinalIgnoreCase)
            then
                refuse MoreThanShapes

/// Only the SVG namespace, or none: a Sketch file declares the SVG one and
/// `xlink`, and `xlink` is only ever used by `href`.
let private checkNamespace (element: XElement) =
    let ns = element.Name.NamespaceName

    if ns <> "" && ns <> svgNamespace then
        refuse MoreThanShapes

/// `d` must stay inside the path-data alphabet, start with a move, and be short.
/// It is stored with its white space collapsed to single spaces so that it is
/// what an XML parser would hand back from the attribute.
let private parsePathData (element: XElement) : string =
    match attribute "d" element with
    | Some d when
        d.Length <= maxPathData
        && pathDataRegex.IsMatch d
        && (let t = d.TrimStart([| ' '; '\t'; '\r'; '\n' |]) in t.Length > 0 && (t[0] = 'M' || t[0] = 'm'))
        ->
        whitespaceRuns.Replace(d, " ").Trim()
    | _ -> refuse MoreThanShapes

/// The nodes of an element that cannot contain anything but text: the parts of a
/// file that are dropped.
let private checkOnlyText (element: XElement) =
    for node in element.Nodes() do
        match node with
        | :? XElement -> refuse MoreThanShapes
        | _ -> ()

/// `path` and `rect` hold nothing but, at most, a `title` or `desc`.
let private checkLeaf (element: XElement) =
    for node in element.Nodes() do
        match node with
        | :? XElement as child ->
            checkAttributes child
            checkNamespace child

            match child.Name.LocalName with
            | "title"
            | "desc" -> checkOnlyText child
            | _ -> refuse MoreThanShapes
        | :? XText as text when isBlank text -> ()
        | :? XText -> refuse MoreThanShapes
        | _ -> ()

/// Walks one element and the ones inside it, adding each path found to `paths`
/// with the `fill-rule` and the transforms of the groups around it. `rules` is
/// the nearest `fill-rule` set above; `transforms` are the groups' transforms,
/// outermost first.
let rec private walk
    (depth: int)
    (rule: string option)
    (transforms: string list)
    (paths: ResizeArray<LogoPath>)
    (element: XElement)
    =
    if depth > maxDepth then refuse MoreThanShapes
    checkAttributes element
    checkNamespace element

    let children (rule: string option) (transforms: string list) =
        for node in element.Nodes() do
            match node with
            | :? XElement as child -> walk (depth + 1) rule transforms paths child
            // Text outside `title` and `desc`, CDATA included. White space
            // alone is layout, kept only because of `xml:space="preserve"`.
            | :? XText as text when isBlank text -> ()
            | :? XText -> refuse MoreThanShapes
            | _ -> ()

    match element.Name.LocalName with
    | "svg" when depth = 0 -> children (fillRuleOf element |> Option.orElse rule) transforms
    | "g" ->
        let transforms =
            match attribute "transform" element with
            | Some text -> transforms @ [ parseTransform text ]
            | None -> transforms

        children (fillRuleOf element |> Option.orElse rule) transforms
    | "path" ->
        if paths.Count >= maxPaths then refuse MoreThanShapes
        checkLeaf element
        let d = parsePathData element

        let transforms =
            match attribute "transform" element with
            | Some text -> transforms @ [ parseTransform text ]
            | None -> transforms

        paths.Add
            { D = d
              FillRule = fillRuleOf element |> Option.orElse rule
              Transform = if transforms.IsEmpty then None else Some(String.concat " " transforms) }
    // The file's background: dropped.
    | "rect" -> checkLeaf element
    | "title"
    | "desc" -> checkOnlyText element
    // Defines nothing, so there is nothing to refer to.
    | "defs" -> if element.Nodes() |> Seq.isEmpty |> not then refuse MoreThanShapes
    | _ -> refuse MoreThanShapes

let private readerSettings =
    XmlReaderSettings(
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true
    )

let private loadRoot (text: string) : XElement =
    use stringReader = new StringReader(text)
    use reader = XmlReader.Create(stringReader, readerSettings)
    let document = XDocument.Load(reader, LoadOptions.None)

    match document.Root with
    | null -> refuse NotSvg
    | root -> root

/// The `viewBox` as stored: four numbers, written from the parsed values.
let viewBox (logo: AppleLogo) : string = logo.ViewBox

/// The shapes of the mark, in drawing order.
let paths (logo: AppleLogo) : LogoPath list = logo.Paths

/// What `parse` keeps, written as the SVG that is stored and drawn. No `width`
/// or `height`: the page's CSS sizes it. Every value is one `parse` validated.
let render (logo: AppleLogo) : string =
    let svg = StringBuilder()

    svg.Append("<svg viewBox=\"").Append(logo.ViewBox).Append("\" fill=\"currentColor\" aria-hidden=\"true\" focusable=\"false\">")
    |> ignore

    for path in logo.Paths do
        svg.Append("<path d=\"").Append(path.D).Append('"') |> ignore

        path.FillRule
        |> Option.iter (fun rule -> svg.Append(" fill-rule=\"").Append(rule).Append('"') |> ignore)

        path.Transform
        |> Option.iter (fun transform -> svg.Append(" transform=\"").Append(transform).Append('"') |> ignore)

        svg.Append("/>") |> ignore

    svg.Append("</svg>").ToString()

/// Reads a pasted text, or a stored one: the same rules both times, because the
/// stored text is parsed again before it reaches a page (FR-012).
let parse (input: string | null) : Result<AppleLogo, AppleLogoError> =
    match input with
    | null -> Error NotSvg
    | text when String.IsNullOrWhiteSpace text -> Error NotSvg
    | text when text.Length > MaxChars -> Error TooLarge
    | text ->
        try
            let root = loadRoot text

            let ns = root.Name.NamespaceName

            if root.Name.LocalName <> "svg" || (ns <> "" && ns <> svgNamespace) then
                refuse NotSvg

            // Without a `viewBox`, a browser draws the file in a box of its
            // `width` and `height`, so those are the box; with neither, there is
            // no box to scale into the button.
            let viewBox =
                match attribute "viewBox" root with
                | Some text -> parseViewBox text
                | None ->
                    match sizeOf "width" root, sizeOf "height" root with
                    | Some width, Some height -> joinNumbers [ 0.0; 0.0; width; height ]
                    | _ -> refuse NotSvg

            let paths = ResizeArray<LogoPath>()
            walk 0 None [] paths root

            if paths.Count = 0 then
                Error MoreThanShapes
            else
                let logo =
                    { ViewBox = viewBox
                      Paths = List.ofSeq paths }

                // Transforms of the groups are written into each path, which can
                // make the stored text longer than the pasted one. It is stored
                // only if it would parse again, and it does not past `MaxChars`.
                if (render logo).Length > MaxChars then Error TooLarge else Ok logo
        with
        | Refused error -> Error error
        | :? XmlException -> Error NotSvg
