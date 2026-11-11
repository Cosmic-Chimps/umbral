/// The Apple pane's "Sign-in button logo" section (specs/014-provider-marks,
/// FR-010 to FR-015).
///
/// Everything goes through the real pane and its two routes. The rule these tests
/// hold: pasted text never reaches a page. A refusal is a 422 with its message under
/// the field, stores nothing, and the textarea comes back empty; only the parsed,
/// rebuilt SVG is ever drawn.
module Umbral.Tests.AppleLogoAdminTests

open System
open System.Net
open System.Text.RegularExpressions
open Xunit
open Umbral.Domains
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

let private paneUrl = "/admin/connectors/apple"
let private saveUrl = "/admin/connectors/apple/logo"
let private removeUrl = "/admin/connectors/apple/logo/remove"

let private texts () = Umbral.Views.Texts.current ()

let private decode (text: string) =
    WebUtility.HtmlDecode text |> Option.ofObj |> Option.defaultValue ""

/// A made-up logo: Apple's artwork may not be bundled, so no test carries it.
let private madeUp = """<svg viewBox="0 0 10 10"><path d="M1 1 L9 9 Z"/></svg>"""

/// A second made-up logo, to tell a replacement from the first.
let private other = """<svg viewBox="0 0 20 20"><path d="M2 2 L18 18 Z"/></svg>"""

type private World =
    { App: Fixtures.TestApp
      Operator: Browser }

/// An instance with its operator signed in and, when asked, the Apple connector set up.
let private start (withApple: bool) =
    task {
        let! app = Fixtures.createApp ()
        let operator = Browser app.Client
        do! claimOperator app operator

        if withApple then
            do! FakeApple.seedConnectorWith app "unused"

        return { App = app; Operator = operator }
    }

let private pane (world: World) =
    task {
        let! html = world.Operator.GetHtml paneUrl
        return decode html
    }

let private savedLogo (world: World) = ConnectorStore.appleLogo world.App.DbConfig

let private post (world: World) (text: string) =
    world.Operator.PostForm(paneUrl, saveUrl, [ "appleLogo", text ])

let private events (world: World) (detail: string) =
    task {
        let! n =
            scalar
                world.App
                "SELECT count(*) FROM security_events WHERE event_type = 'connector_updated' AND connector_slug = 'apple' AND detail = @d"
                [ "@d", detail ]

        return int n
    }

/// The section's own markup.
let private sectionOf (html: string) =
    Regex.Match(html, "<div class=\"um-pane-section\"[^>]*data-apple-logo-section.*?(?=<div class=\"um-pane-actions\"|$)", RegexOptions.Singleline).Value

/// The text of the error under the logo field, if there is one. Takes the page as it was
/// sent: decoding first would read the message's own `<svg` as a tag.
let private errorOf (html: string) =
    let m = Regex.Match(html, "<p[^>]*\\bid=\"appleLogo-error\"[^>]*>(.*?)</p>", RegexOptions.Singleline)

    if m.Success then
        Some((decode (Regex.Replace(m.Value, "<[^>]+>", ""))).Trim())
    else
        None

/// A refused paste: 422, the message under the field, nothing stored, an empty textarea,
/// and no security event.
let private assertRefused (world: World) (pasted: string) (message: string) =
    task {
        let! response = post world pasted
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode)
        let! raw = response.Content.ReadAsStringAsync()
        let html = decode raw

        // The raw page for the message: its own `<svg` and `</svg>` are encoded there.
        Assert.Equal(Some message, errorOf raw)
        Assert.Contains("aria-invalid=\"true\"", html)
        Assert.Matches("<textarea[^>]*id=\"appleLogo\"[^>]*></textarea>", html)

        let! logo = savedLogo world
        Assert.Equal(None, logo)

        let! saved = events world "apple-logo=saved"
        Assert.Equal(0, saved)
        return html
    }

// ---------------------------------------------------------------------------
// The section
// ---------------------------------------------------------------------------

[<Fact>]
let ``no section when the Apple connector is not set up`` () =
    task {
        let! world = start false
        use _ = world.App
        let! html = pane world
        Assert.Contains("data-connector-kind=\"apple\"", html)
        Assert.DoesNotContain("data-apple-logo-section", html)
        Assert.DoesNotContain(texts().AppleLogoSectionTitle, html)
    }

[<Fact>]
let ``set up with no logo the section has the lead, the no-logo line and the textarea`` () =
    task {
        let! world = start true
        use _ = world.App
        let! html = pane world
        let t = texts ()
        let section = sectionOf html

        Assert.Contains("data-apple-logo-section", html)
        Assert.DoesNotContain("data-apple-logo-saved", html)
        Assert.Contains(t.AppleLogoSectionTitle, section)
        Assert.Contains(t.AppleLogoLead, section)
        Assert.Contains(t.AppleLogoNone, section)
        Assert.Matches("<label[^>]*>" + Regex.Escape t.AppleLogoLabel + "</label>", section)
        Assert.Contains(t.AppleLogoHint, section)
        Assert.Matches("<textarea[^>]*id=\"appleLogo\"[^>]*rows=\"6\"", section)
        Assert.Contains($">{t.AppleLogoSave}</button>", section)
        Assert.Contains("action=\"/admin/connectors/apple/logo\"", section)

        // Nothing to preview, nothing to remove.
        Assert.DoesNotContain("data-apple-logo-sample", html)
        Assert.DoesNotContain(removeUrl, html)
        Assert.DoesNotContain(t.AppleLogoRemove, html)

        // The section sits between Settings and the closing row.
        Assert.True(html.IndexOf "data-apple-logo-section" > html.IndexOf "action=\"/admin/connectors/apple\"")
        Assert.True(html.IndexOf "data-apple-logo-section" < html.IndexOf "data-connector-actions")
    }

// ---------------------------------------------------------------------------
// Saving and removing
// ---------------------------------------------------------------------------

[<Fact>]
let ``a good paste is saved and the pane shows the preview, the replace field and Remove with a confirm`` () =
    task {
        let! world = start true
        use _ = world.App
        let! response = post world madeUp
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("apple-logo-saved", location response)

        let! logo = savedLogo world
        Assert.Equal(Some(AppleLogo.parse madeUp |> Result.defaultWith (fun _ -> failwith "parse")), logo)

        let! html = world.Operator.GetHtml(location response)
        let html = decode html
        let t = texts ()
        let section = sectionOf html

        Assert.Contains(t.Notice "apple-logo-saved", html)
        Assert.Contains("data-apple-logo-saved", html)
        Assert.DoesNotContain(t.AppleLogoNone, section)

        // The preview: the sample Apple button with the rebuilt SVG in its slot.
        Assert.Matches("<button[^>]*um-btn-apple[^>]*data-apple-logo-sample[^>]*>", section)
        Assert.Contains("<svg viewBox=\"0 0 10 10\" fill=\"currentColor\"", section)
        Assert.Contains("Sign in with Apple", section)

        // The textarea is now "Replace the logo", empty, with its hint.
        Assert.Matches("<label[^>]*>" + Regex.Escape t.AppleLogoReplaceLabel + "</label>", section)
        Assert.Contains(t.AppleLogoReplaceHint, section)
        Assert.Matches("<textarea[^>]*id=\"appleLogo\"[^>]*></textarea>", section)

        // Remove logo: its own form, confirmed first.
        Assert.Contains(t.AppleLogoRemove, section)
        Assert.Matches("<form[^>]*action=\"/admin/connectors/apple/logo/remove\"[^>]*data-confirm=\"" + Regex.Escape(t.AppleLogoRemoveConfirm) + "\"", section)
    }

[<Fact>]
let ``a second paste replaces the logo`` () =
    task {
        let! world = start true
        use _ = world.App
        let! _ = post world madeUp
        let! response = post world other
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)

        let! logo = savedLogo world
        Assert.Equal(Some(AppleLogo.parse other |> Result.defaultWith (fun _ -> failwith "parse")), logo)
    }

[<Fact>]
let ``a blank paste keeps the saved logo and changes nothing`` () =
    task {
        let! world = start true
        use _ = world.App
        let! _ = post world madeUp
        let! before = events world "apple-logo=saved"
        let! response = post world "   "
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.DoesNotContain("msg=", location response)

        let! logo = savedLogo world
        Assert.True logo.IsSome
        let! after = events world "apple-logo=saved"
        Assert.Equal(before, after)
    }

[<Fact>]
let ``a blank paste with no logo saved is the not-an-SVG refusal`` () =
    task {
        let! world = start true
        use _ = world.App
        let! _ = assertRefused world "" (texts().AppleLogoNotSvg)
        ()
    }

[<Fact>]
let ``text that is not XML is refused as not an SVG`` () =
    task {
        let! world = start true
        use _ = world.App
        let pasted = "this is not an svg, it is just words PASTED-b6d7"
        let! html = assertRefused world pasted (texts().AppleLogoNotSvg)
        Assert.DoesNotContain("PASTED-b6d7", html)
    }

[<Fact>]
let ``an SVG with a script in it is refused as more than shapes, and the script is not drawn back`` () =
    task {
        let! world = start true
        use _ = world.App
        let pasted = """<svg viewBox="0 0 1 1"><script>MARKER-b6d7</script><path d="M0 0Z"/></svg>"""
        let! html = assertRefused world pasted (texts().AppleLogoMoreThanShapes)
        Assert.DoesNotContain("MARKER-b6d7", html)
        Assert.DoesNotContain("<script>MARKER", html)
    }

[<Fact>]
let ``a text over the limit is refused as too large`` () =
    task {
        let! world = start true
        use _ = world.App
        let pasted = String('a', AppleLogo.MaxChars + 1)
        let! _ = assertRefused world pasted (texts().AppleLogoTooLarge)
        ()
    }

[<Fact>]
let ``a refusal keeps the logo already saved`` () =
    task {
        let! world = start true
        use _ = world.App
        let! _ = post world madeUp
        let! response = post world "not an svg"
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode)
        let! raw = response.Content.ReadAsStringAsync()
        Assert.Equal(Some(texts().AppleLogoNotSvg), errorOf raw)

        let! logo = savedLogo world
        Assert.Equal(Some(AppleLogo.parse madeUp |> Result.defaultWith (fun _ -> failwith "parse")), logo)
    }

[<Fact>]
let ``Remove clears the logo and the pane shows the no-logo line`` () =
    task {
        let! world = start true
        use _ = world.App
        let! _ = post world madeUp
        let! response = world.Operator.PostForm(paneUrl, removeUrl, [])
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("apple-logo-removed", location response)

        let! logo = savedLogo world
        Assert.Equal(None, logo)

        let! html = world.Operator.GetHtml(location response)
        let html = decode html
        let t = texts ()
        Assert.Contains(t.Notice "apple-logo-removed", html)
        Assert.Contains(t.AppleLogoNone, sectionOf html)
        Assert.DoesNotContain("data-apple-logo-sample", html)
        Assert.DoesNotContain("data-apple-logo-saved", html)
    }

[<Fact>]
let ``saving and removing the logo are recorded as security events`` () =
    task {
        let! world = start true
        use _ = world.App
        let! _ = post world madeUp
        let! saved = events world "apple-logo=saved"
        Assert.Equal(1, saved)

        let! _ = world.Operator.PostForm(paneUrl, removeUrl, [])
        let! removed = events world "apple-logo=removed"
        Assert.Equal(1, removed)

        // What was pasted is not in the log.
        let! leaked = scalar world.App "SELECT count(*) FROM security_events WHERE detail LIKE '%M1 1 L9 9%'" []
        Assert.Equal("0", leaked)
    }

[<Fact>]
let ``without the Apple connector both routes redirect to the list and store nothing`` () =
    task {
        let! world = start false
        use _ = world.App
        let! response = post world madeUp
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("connector-unknown", location response)

        let! removed = world.Operator.PostForm(paneUrl, removeUrl, [])
        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode)
        Assert.Contains("connector-unknown", location removed)

        let! logo = savedLogo world
        Assert.Equal(None, logo)
    }

[<Fact>]
let ``an anonymous request cannot save or remove the logo`` () =
    task {
        let! world = start true
        use _ = world.App
        let! _ = post world madeUp

        let stranger = Browser(world.App.Client)
        let! save = stranger.PostWithoutToken(saveUrl, [ "appleLogo", other ])
        Assert.NotEqual(HttpStatusCode.OK, save.StatusCode)
        Assert.NotEqual(HttpStatusCode.UnprocessableEntity, save.StatusCode)

        let! remove = stranger.PostWithoutToken(removeUrl, [])
        Assert.NotEqual(HttpStatusCode.OK, remove.StatusCode)

        let! logo = savedLogo world
        Assert.Equal(Some(AppleLogo.parse madeUp |> Result.defaultWith (fun _ -> failwith "parse")), logo)
    }
