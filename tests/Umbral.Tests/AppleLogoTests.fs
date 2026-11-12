/// The pasted Apple logo: what `AppleLogo.parse` accepts, what it refuses and
/// what `render` writes (specs/014 FR-011, FR-012, FR-014).
///
/// The shapes in these tests are made up. Apple's artwork may not be bundled,
/// so no test carries it; the Sketch layout around the shapes is the real
/// thing, because that is what the parser has to read.
module Umbral.Tests.AppleLogoTests

open System
open Xunit
open Umbral.Domains
open Umbral.Domains.AppleLogo
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure

/// An SVG of the shape Sketch exports, with a made-up path.
let private sketch =
    """<?xml version="1.0" encoding="UTF-8"?>
<svg width="56px" height="56px" viewBox="0 0 56 56" version="1.1" xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink">
    <title>Left Black Logo Large</title>
    <g id="Black" stroke="none" stroke-width="1" fill="none" fill-rule="evenodd">
        <rect id="Rectangle" fill="#000000" x="0" y="0" width="56" height="56"></rect>
        <path d="M28.2226562,18.3846154 C29.0546875,18.3846154 30.0976562,17.8048315 30.71875,17.0317864 Z" id="" fill="#FFFFFF" fill-rule="nonzero"></path>
    </g>
</svg>"""

/// A root with a valid `viewBox` around `body`.
let private wrap (body: string) =
    $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">{body}</svg>"""

let private morePath = """<path d="M0 0 L10 10 Z"/>"""

/// `Assert.Equal` on lists of options has no single overload under nullness.
let private same (expected: 'a) (actual: 'a) =
    Assert.True((expected = actual), $"expected %A{expected}, got %A{actual}")

let private accepted (text: string) =
    match parse text with
    | Ok logo -> logo
    | Error error -> failwith $"expected a logo, got {error}"

let private refusedWith (expected: AppleLogoError) (text: string | null) =
    match parse text with
    | Ok logo -> failwith $"expected {expected}, got a logo: {render logo}"
    | Error error -> Assert.Equal(expected, error)

// ---------------------------------------------------------------------------
// What is accepted
// ---------------------------------------------------------------------------

[<Fact>]
let ``a Sketch export gives one path, the rect dropped, and the path's own fill-rule wins`` () =
    let logo = accepted sketch

    Assert.Equal("0 0 56 56", viewBox logo)

    match paths logo with
    | [ path ] ->
        Assert.Equal("M28.2226562,18.3846154 C29.0546875,18.3846154 30.0976562,17.8048315 30.71875,17.0317864 Z", path.D)
        Assert.Equal(Some "nonzero", path.FillRule)
        Assert.Equal(None, path.Transform)
    | other -> failwith $"expected one path, got {other.Length}"

    let svg = render logo
    Assert.StartsWith("""<svg viewBox="0 0 56 56" fill="currentColor" aria-hidden="true" focusable="false"><path d=""", svg)
    Assert.EndsWith("""fill-rule="nonzero"/></svg>""", svg)

    for dropped in [ "rect"; "title"; "#000000"; "#FFFFFF"; "id="; "width"; "height"; "Black" ] do
        Assert.DoesNotContain(dropped, svg)

[<Fact>]
let ``a fill-rule set on the group is inherited by a path that sets none`` () =
    let logo =
        accepted (wrap $"""<g fill-rule="evenodd">{morePath}<g fill-rule="nonzero">{morePath}</g></g>""")

    same [ Some "evenodd"; Some "nonzero" ] (paths logo |> List.map (fun p -> p.FillRule))

[<Fact>]
let ``a fill-rule that is neither word is read as not set`` () =
    let logo = accepted (wrap """<g fill-rule="evenodd"><path d="M0 0 Z" fill-rule="inherit"/></g>""")
    same [ Some "evenodd" ] (paths logo |> List.map (fun p -> p.FillRule))

    let none = accepted (wrap """<path d="M0 0 Z" fill-rule="EVENODD"/>""")
    same [ None ] (paths none |> List.map (fun p -> p.FillRule))

[<Fact>]
let ``transforms of nested groups and the path are composed outermost first`` () =
    let logo =
        accepted (
            wrap
                """<g transform="translate(1 2)"><g transform="scale(2)"><path d="M0 0 Z" transform="matrix(1 0 0 1 3 4)"/></g></g>"""
        )

    same [ Some "translate(1 2) scale(2) matrix(1 0 0 1 3 4)" ] (paths logo |> List.map (fun p -> p.Transform))

[<Fact>]
let ``a transform is written back from its numbers, not as it was typed`` () =
    let logo = accepted (wrap """<path d="M0 0 Z" transform="  translate( 2,3 )  ,scale(+1.50)"/>""")
    same [ Some "translate(2 3) scale(1.5)" ] (paths logo |> List.map (fun p -> p.Transform))

[<Fact>]
let ``a viewBox is written back from its four numbers`` () =
    let logo = accepted """<svg viewBox="0,0, 56.0   56" xmlns="http://www.w3.org/2000/svg"><path d="M0 0 Z"/></svg>"""
    Assert.Equal("0 0 56 56", viewBox logo)

[<Fact>]
let ``an svg without a namespace is accepted`` () =
    let logo = accepted """<svg viewBox="0 0 8 8"><path d="m0 0 l8 8 z"/></svg>"""
    Assert.Equal(1, (paths logo).Length)

[<Fact>]
let ``path data keeps its numbers and loses odd white space`` () =
    let logo = accepted (wrap "<path d=\"  M0 0&#10;&#9;L1e-3 -2.5\r\n Z \"/>")
    same [ "M0 0 L1e-3 -2.5 Z" ] (paths logo |> List.map (fun p -> p.D))

let private accepts =
    [ "the Sketch export", sketch
      "groups, transforms and fill rules",
      wrap """<g fill-rule="evenodd" transform="translate(1 2)"><path d="M0 0 L5 5 Z"/><path d="M1 1 Z" transform="scale(2)"/></g>"""
      "an empty defs, a title and a desc", wrap """<defs/><title>t</title><desc>d</desc><path d="M0 0 Z"><title>inner</title></path>"""
      "a comment and a processing instruction", wrap """<!-- c --><?pi data?><path d="M0 0 Z"/>""" ]

[<Fact>]
let ``parse of what render wrote gives the same logo`` () =
    for name, text in accepts do
        let logo = accepted text

        match parse (render logo) with
        | Ok again -> Assert.True((logo = again), $"round trip changed {name}")
        | Error error -> failwith $"{name}: the stored text was refused: {error}"

        // And rendering is a fixed point, so what is stored is canonical.
        Assert.Equal(render logo, render (accepted (render logo)))

[<Fact>]
let ``what render writes never has script, a handler, a reference or foreignObject`` () =
    for name, text in accepts do
        let svg = render (accepted text)

        for forbidden in [ "<script"; "foreignObject"; "href"; "xlink"; "<rect"; "<style"; "<image"; "<use"; "<text"; "<a " ] do
            Assert.False(svg.Contains forbidden, $"{name}: {svg} contains {forbidden}")

        // Only the attributes render writes.
        let attributes =
            System.Text.RegularExpressions.Regex.Matches(svg, """\s([A-Za-z:-]+)=""")
            |> Seq.map (fun m -> m.Groups[1].Value)
            |> Set.ofSeq

        Assert.True(
            Set.isSubset attributes (Set.ofList [ "viewBox"; "fill"; "aria-hidden"; "focusable"; "d"; "fill-rule"; "transform" ]),
            $"{name}: unexpected attributes {attributes}"
        )

// ---------------------------------------------------------------------------
// What is refused
// ---------------------------------------------------------------------------

[<Fact>]
let ``text that is not XML is not an svg`` () =
    for text in [ ""; "   "; "hello"; "<svg viewBox=\"0 0 1 1\"><path d=\"M0 0 Z\"></svg>"; "{ \"svg\": 1 }"; "<svg" ] do
        refusedWith NotSvg text

[<Fact>]
let ``HTML is not an svg`` () =
    refusedWith NotSvg "<!DOCTYPE html><html><body><svg viewBox=\"0 0 1 1\"></svg></body></html>"
    refusedWith NotSvg """<html><body><p>hi</p></body></html>"""

[<Fact>]
let ``a root that is not svg, or is svg of another namespace, is not an svg`` () =
    refusedWith NotSvg """<html viewBox="0 0 1 1"><path d="M0 0 Z"/></html>"""
    refusedWith NotSvg """<SVG viewBox="0 0 1 1"><path d="M0 0 Z"/></SVG>"""
    refusedWith NotSvg """<svg xmlns="http://example.com/x" viewBox="0 0 1 1"><path d="M0 0 Z"/></svg>"""

[<Fact>]
let ``an svg without a usable viewBox is not an svg`` () =
    refusedWith NotSvg """<svg xmlns="http://www.w3.org/2000/svg"><path d="M0 0 Z"/></svg>"""
    refusedWith NotSvg """<svg viewBox="0 0 56"><path d="M0 0 Z"/></svg>"""
    refusedWith NotSvg """<svg viewBox="0 0 56 56 56"><path d="M0 0 Z"/></svg>"""
    refusedWith NotSvg """<svg viewBox="0 0 a 56"><path d="M0 0 Z"/></svg>"""
    refusedWith NotSvg """<svg viewBox="0 0 1e999 56"><path d="M0 0 Z"/></svg>"""
    refusedWith NotSvg """<svg viewBox="0 0 0 56"><path d="M0 0 Z"/></svg>"""
    refusedWith NotSvg """<svg viewBox="0 0 -5 56"><path d="M0 0 Z"/></svg>"""
    refusedWith NotSvg """<svg viewBox=""><path d="M0 0 Z"/></svg>"""
    // Without a viewBox, a width or a height alone, or one in another unit, is no box.
    refusedWith NotSvg """<svg width="10" xmlns="http://www.w3.org/2000/svg"><path d="M0 0 Z"/></svg>"""
    refusedWith NotSvg """<svg width="10mm" height="12mm"><path d="M0 0 Z"/></svg>"""
    refusedWith NotSvg """<svg width="0" height="12"><path d="M0 0 Z"/></svg>"""

[<Fact>]
let ``without a viewBox, the width and the height are the box`` () =
    // The layout of a logo file with no viewBox (as some copies of logos are), with a made-up path.
    let logo =
        accepted
            """<svg xmlns="http://www.w3.org/2000/svg" xml:space="preserve" width="814" height="1000">
  <path d="M788.1 340.9c-5.8 4.5-108.2 62.2-108.2 190.5z"/>
</svg>"""

    Assert.Equal("0 0 814 1000", viewBox logo)
    Assert.Equal("0 0 56 40", viewBox (accepted """<svg width="56px" height=" 40px "><path d="M0 0 Z"/></svg>"""))
    // A viewBox wins over the size.
    Assert.Equal("0 0 8 8", viewBox (accepted """<svg width="56" height="56" viewBox="0 0 8 8"><path d="M0 0 Z"/></svg>"""))

[<Fact>]
let ``a DTD is refused, entities included, and nothing is expanded`` () =
    let billion =
        """<?xml version="1.0"?>
<!DOCTYPE svg [
  <!ENTITY a "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa">
  <!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">
  <!ENTITY c "&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;">
]>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1 1"><title>&c;</title><path d="M0 0 Z"/></svg>"""

    refusedWith NotSvg billion

    refusedWith
        NotSvg
        """<!DOCTYPE svg SYSTEM "http://127.0.0.1:1/x.dtd"><svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1 1"><path d="M0 0 Z"/></svg>"""

[<Fact>]
let ``elements outside the allowlist are refused`` () =
    for name, body in
        [ "script", """<script>alert(1)</script>"""
          "script with cdata", """<script><![CDATA[alert(1)]]></script>"""
          "foreignObject", """<foreignObject><div xmlns="http://www.w3.org/1999/xhtml">x</div></foreignObject>"""
          "image", """<image href="https://example.com/a.png"/>"""
          "use", """<use xlink:href="#a" xmlns:xlink="http://www.w3.org/1999/xlink"/>"""
          "style", """<style>path { fill: red }</style>"""
          "a", """<a><path d="M0 0 Z"/></a>"""
          "text", """<text>hi</text>"""
          "circle", """<circle cx="1" cy="1" r="1"/>"""
          "nested svg", """<svg><path d="M0 0 Z"/></svg>"""
          "clipPath", """<clipPath id="c"><path d="M0 0 Z"/></clipPath>"""
          "defs with content", """<defs><path id="a" d="M0 0 Z"/></defs>"""
          "an element of another namespace", """<x:y xmlns:x="http://example.com/x"/>"""
          "an html element", """<iframe xmlns="http://www.w3.org/1999/xhtml" src="javascript:1"/>"""
          "a script in a path", """<path d="M0 0 Z"><script>alert(1)</script></path>"""
          "a path in a rect", """<rect><path d="M0 0 Z"/></rect>"""
          "an element in a title", """<title><script>alert(1)</script></title>""" ] do
        match parse (wrap (morePath + body)) with
        | Error MoreThanShapes -> ()
        | other -> failwith $"{name}: expected MoreThanShapes, got {other}"

[<Fact>]
let ``text outside title and desc is refused`` () =
    refusedWith MoreThanShapes (wrap (morePath + "hello"))
    refusedWith MoreThanShapes (wrap (morePath + "<g>hello</g>"))
    refusedWith MoreThanShapes (wrap (morePath + "<![CDATA[hello]]>"))

[<Fact>]
let ``event handlers and references are refused on any element`` () =
    for name, text in
        [ "onload on svg", """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1 1" onload="alert(1)"><path d="M0 0 Z"/></svg>"""
          "onclick on path", wrap """<path d="M0 0 Z" onclick="alert(1)"/>"""
          "ONCLICK on path", wrap """<path d="M0 0 Z" ONCLICK="alert(1)"/>"""
          "onmouseover on rect", wrap """<rect onmouseover="x" width="1" height="1"/><path d="M0 0 Z"/>"""
          "onbegin on title", wrap """<title onbegin="x">t</title><path d="M0 0 Z"/>"""
          "href on g", wrap """<g href="https://example.com"><path d="M0 0 Z"/></g>"""
          "xlink:href on path",
          wrap """<path xmlns:xlink="http://www.w3.org/1999/xlink" xlink:href="#a" d="M0 0 Z"/>"""
          "href on a rect", wrap """<rect href="x"/><path d="M0 0 Z"/>"""
          "style with url", wrap """<path d="M0 0 Z" style="fill:url(#x)"/>"""
          "style with URL on g", wrap """<g style="fill: URL(#x)"><path d="M0 0 Z"/></g>""" ] do
        match parse text with
        | Error MoreThanShapes -> ()
        | other -> failwith $"{name}: expected MoreThanShapes, got {other}"

[<Fact>]
let ``a style without a reference is only ignored`` () =
    let logo = accepted (wrap """<path d="M0 0 Z" style="fill:#fff"/>""")
    Assert.DoesNotContain("style", render logo)

[<Fact>]
let ``path data outside the alphabet is refused`` () =
    for name, d in
        [ "a double quote", "M0 0 \"onload=\"x"
          "a less-than", "M0 0 <script>"
          "javascript:", "javascript:alert(1)"
          "an ampersand", "M0 0 &amp; Z"
          "a word", "M0 0 L10 10 Zebra"
          "no move first", "L1 1"
          "empty", ""
          "only white space", "   "
          "a digit of another script", "M0 0 L١ 1"
          "no-break space", "M0 0 L1 1"
          "a semicolon", "M0 0; L1 1" ] do
        match parse (wrap $"""<path d="{Security.SecurityElement.Escape d}"/>""") with
        | Error MoreThanShapes -> ()
        | other -> failwith $"{name}: expected MoreThanShapes, got {other}"

    // A path with no `d`, and one with `d` too long.
    refusedWith MoreThanShapes (wrap "<path/>")
    refusedWith MoreThanShapes (wrap $"""<path d="M0 0 {String('1', 16385)}"/>""")

[<Fact>]
let ``a transform outside translate, scale and matrix with numbers is refused`` () =
    for name, transform in
        [ "rotate", "rotate(45)"
          "skewX", "skewX(10)"
          "a trailing statement", "translate(1) ; x"
          "a trailing word", "translate(1) x"
          "a leading word", "x translate(1)"
          "a letter as a number", "translate(a)"
          "a function call", "translate(alert(1))"
          "too few for matrix", "matrix(1 0 0 1 0)"
          "too many for translate", "translate(1 2 3)"
          "no arguments", "translate()"
          "a number too large", "translate(1e999)"
          "a comma first", "translate(,1)"
          "nothing at all", ""
          "only white space", "  " ] do
        match parse (wrap $"""<path d="M0 0 Z" transform="{transform}"/>""") with
        | Error MoreThanShapes -> ()
        | other -> failwith $"{name}: expected MoreThanShapes, got {other}"

    // The same on a group.
    refusedWith MoreThanShapes (wrap """<g transform="rotate(45)"><path d="M0 0 Z"/></g>""")

[<Fact>]
let ``a logo needs at least one path and at most 64`` () =
    refusedWith MoreThanShapes (wrap "")
    refusedWith MoreThanShapes (wrap """<rect width="1" height="1"/><title>t</title>""")
    refusedWith MoreThanShapes (wrap """<g><g/></g>""")

    let paths count =
        wrap (String.replicate count """<path d="M0 0 Z"/>""")

    Assert.Equal(64, (accepted (paths 64) |> AppleLogo.paths).Length)
    refusedWith MoreThanShapes (paths 65)

[<Fact>]
let ``nesting deeper than a logo needs is refused`` () =
    let nested depth =
        wrap (String.replicate depth "<g>" + morePath + String.replicate depth "</g>")

    Assert.Equal(1, (accepted (nested 10) |> AppleLogo.paths).Length)
    refusedWith MoreThanShapes (nested 17)
    // Far past what could be recursed into, and still a refusal, not a crash.
    refusedWith MoreThanShapes (nested 4000)

[<Fact>]
let ``text over 32 KiB is too large and is not parsed`` () =
    Assert.Equal(32768, MaxChars)
    let at = "<svg viewBox=\"0 0 1 1\"><title>"
    let close = "</title><path d=\"M0 0 Z\"/></svg>"

    let withLength n =
        at + String('x', n - at.Length - close.Length) + close

    // At the limit it is parsed; one over, it is not.
    Assert.Equal(MaxChars, (withLength MaxChars).Length)
    accepted (withLength MaxChars) |> ignore
    refusedWith TooLarge (withLength (MaxChars + 1))

    // Too large wins over anything wrong in the text: it is checked first.
    refusedWith TooLarge (String('<', MaxChars + 1))

[<Fact>]
let ``a logo whose rebuilt text would pass the limit is too large, so what is stored always parses`` () =
    // Fourteen nested groups with a long transform, around 64 short paths: the
    // composed transform is written into every path.
    let transform = "matrix(1.123456789 0.123456789 0.123456789 1.123456789 100.123456789 200.123456789)"
    let opening = String.replicate 14 $"""<g transform="{transform}">"""
    let closing = String.replicate 14 "</g>"
    let text = wrap (opening + String.replicate 64 morePath + closing)

    Assert.True(text.Length < MaxChars, "the pasted text must fit, for the test to be about the rebuilt one")
    refusedWith TooLarge text

[<Fact>]
let ``a long run of digits followed by a letter is refused quickly, wherever a number is read`` () =
    // Code review N1: the old number pattern backtracked quadratically, about 29 s
    // for 32,000 digits then a letter. Each case is close to `MaxChars`.
    let digits = String('9', 32000) + "x"

    let cases =
        [ $"""<svg viewBox="0 0 1 {digits}"><path d="M0 0 Z"/></svg>"""
          $"""<svg width="{digits}" height="1"><path d="M0 0 Z"/></svg>"""
          wrap $"""<g transform="translate({digits})"><path d="M0 0 Z"/></g>""" ]

    for text in cases do
        Assert.True(text.Length <= MaxChars, "each case must reach the parser, not the size check")
        let watch = Diagnostics.Stopwatch.StartNew()
        Assert.True(Result.isError (parse text))
        watch.Stop()
        Assert.True(watch.ElapsedMilliseconds < 1000L, $"took {watch.ElapsedMilliseconds} ms")

[<Fact>]
let ``null is not an svg`` () =
    refusedWith NotSvg null

// ---------------------------------------------------------------------------
// Storage (FR-014)
// ---------------------------------------------------------------------------

let private seedApple (app: Fixtures.TestApp) = FakeApple.seedConnectorWith app "unused"

[<Fact>]
let ``a new Apple connector has no logo`` () =
    task {
        let! app = Fixtures.createApp ()
        use _ = app
        do! seedApple app

        let! logo = ConnectorStore.appleLogo app.DbConfig
        Assert.Equal(None, logo)
    }

[<Fact>]
let ``with no Apple connector there is no logo, and setting or clearing one changes nothing`` () =
    task {
        let! app = Fixtures.createApp ()
        use _ = app

        let! none = ConnectorStore.appleLogo app.DbConfig
        Assert.Equal(None, none)

        let! set = ConnectorStore.setAppleLogo app.DbConfig app.Clock (accepted sketch)
        Assert.False set

        let! cleared = ConnectorStore.clearAppleLogo app.DbConfig app.Clock
        Assert.False cleared

        let! still = ConnectorStore.appleLogo app.DbConfig
        Assert.Equal(None, still)
    }

[<Fact>]
let ``a saved logo reads back, as the rebuilt text and not as pasted`` () =
    task {
        let! app = Fixtures.createApp ()
        use _ = app
        do! seedApple app

        let logo = accepted sketch
        let! set = ConnectorStore.setAppleLogo app.DbConfig app.Clock logo
        Assert.True set

        let! read = ConnectorStore.appleLogo app.DbConfig
        Assert.Equal(Some logo, read)

        // What is in the row is what `render` wrote: nothing of the paste.
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT apple_logo FROM connectors WHERE slug = 'apple'"
        let! stored = command.ExecuteScalarAsync()
        Assert.Equal(render logo, string stored)
        Assert.DoesNotContain("Left Black Logo Large", string stored)
    }

[<Fact>]
let ``saving a logo moves the connector's updated time, and a second logo replaces the first`` () =
    task {
        let! app = Fixtures.createApp ()
        use _ = app
        do! seedApple app

        let! before = ConnectorStore.findBySlug app.DbConfig "apple"
        app.Clock.Advance(TimeSpan.FromMinutes 5.0)

        let first = accepted (wrap """<path d="M0 0 L1 1 Z"/>""")
        let second = accepted (wrap """<path d="M5 5 L9 9 Z"/>""")
        let! _ = ConnectorStore.setAppleLogo app.DbConfig app.Clock first
        let! _ = ConnectorStore.setAppleLogo app.DbConfig app.Clock second

        let! after = ConnectorStore.findBySlug app.DbConfig "apple"
        Assert.True(after.Value.UpdatedUtc > before.Value.UpdatedUtc)

        let! read = ConnectorStore.appleLogo app.DbConfig
        Assert.Equal(Some second, read)
    }

[<Fact>]
let ``removing the logo reads as none`` () =
    task {
        let! app = Fixtures.createApp ()
        use _ = app
        do! seedApple app

        let! _ = ConnectorStore.setAppleLogo app.DbConfig app.Clock (accepted sketch)
        let! cleared = ConnectorStore.clearAppleLogo app.DbConfig app.Clock
        Assert.True cleared

        let! read = ConnectorStore.appleLogo app.DbConfig
        Assert.Equal(None, read)
    }

[<Fact>]
let ``saving the connector again leaves the logo`` () =
    task {
        let! app = Fixtures.createApp ()
        use _ = app
        do! seedApple app
        let logo = accepted sketch
        let! _ = ConnectorStore.setAppleLogo app.DbConfig app.Clock logo

        // The pane's own Save and the seed both go through `save` and `update`.
        do! seedApple app

        let! changed =
            ConnectorStore.update app.DbConfig app.Clock "apple" "Apple again" "com.example.other" true None None None

        Assert.True changed

        let! read = ConnectorStore.appleLogo app.DbConfig
        Assert.Equal(Some logo, read)
    }

[<Fact>]
let ``deleting the Apple connector deletes its logo`` () =
    task {
        let! app = Fixtures.createApp ()
        use _ = app
        do! seedApple app
        let! _ = ConnectorStore.setAppleLogo app.DbConfig app.Clock (accepted sketch)

        let! _ = ConnectorStore.delete app.DbConfig "apple"

        let! gone = ConnectorStore.appleLogo app.DbConfig
        Assert.Equal(None, gone)

        // A connector made again does not inherit it.
        do! seedApple app
        let! fresh = ConnectorStore.appleLogo app.DbConfig
        Assert.Equal(None, fresh)
    }

[<Fact>]
let ``a stored text that no longer parses reads as no logo and does not throw`` () =
    task {
        let! app = Fixtures.createApp ()
        use _ = app
        do! seedApple app

        for text in [ "<svg"; "<script>alert(1)</script>"; ""; "<svg viewBox=\"0 0 1 1\" onload=\"x\"><path d=\"M0 0 Z\"/></svg>" ] do
            use! connection = Database.openConnection app.DbConfig
            use command = connection.CreateCommand()
            command.CommandText <- "UPDATE connectors SET apple_logo = @logo WHERE slug = 'apple'"
            command.Parameters.AddWithValue("@logo", text) |> ignore
            let! _ = command.ExecuteNonQueryAsync()

            let! read = ConnectorStore.appleLogo app.DbConfig
            Assert.Equal(None, read)
    }
