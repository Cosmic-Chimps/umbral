/// The two public branding endpoints (spec 006, contracts/public-endpoints.md):
/// the accent stylesheet, a pure function of its path, and the logo, looked up
/// by the hash of its bytes.
///
/// Both are served to someone who is not signed in, so every case here is one
/// of three kinds: the exact bytes and headers of an answer, the revalidation
/// that returns a 304, and the many ways a path can be wrong, each of which is a
/// plain 404 that reveals nothing about which apps are branded.
module Umbral.Tests.BrandingEndpointTests

open System
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Text
open Xunit
open Umbral.Domains.Branding
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure

let private header (response: HttpResponseMessage) (name: string) =
    match response.Headers.TryGetValues name with
    | true, values -> values |> Option.ofObj |> Option.defaultValue Seq.empty |> List.ofSeq
    | _ -> []

let private revalidate (app: Fixtures.TestApp) (url: string) (etag: string) =
    task {
        use request = new HttpRequestMessage(HttpMethod.Get, url)
        request.Headers.TryAddWithoutValidation("If-None-Match", etag) |> ignore
        return! app.Client.SendAsync request
    }

// ---------------------------------------------------------------------------
// GET /branding/accent/{rrggbb}.css
// ---------------------------------------------------------------------------

[<Fact>]
let ``the accent stylesheet for an orange is one rule with black text on it`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/branding/accent/f26b1d.css"
        let! body = response.Content.ReadAsStringAsync()

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)

        // Exactly this, with decimal channels separated by spaces. The
        // resolved `--color-*` properties are redefined as well as the
        // channels: a custom property's var() is resolved on the element that
        // declares it, so `:root`'s own would otherwise be inherited as
        // Umbral's gold. Spec 007 added the four `primary-fill` properties,
        // which the primary button and the theme control's pressed option read:
        // the accent fills them in both themes, with its own text colour.
        Assert.Equal(
            "[data-accent=\"f26b1d\"]{--color-primary-rgb:242 107 29;--color-on-primary-rgb:0 0 0;--color-primary:rgb(242 107 29);--color-on-primary:rgb(0 0 0);--color-primary-fill-rgb:242 107 29;--color-on-primary-fill-rgb:0 0 0;--color-primary-fill:rgb(242 107 29);--color-on-primary-fill:rgb(0 0 0)}",
            body
        )
    }

[<Fact>]
let ``a dark accent gets white text`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/branding/accent/0a0a10.css"
        let! body = response.Content.ReadAsStringAsync()

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        Assert.Contains("--color-primary-rgb:10 10 16;", body)
        Assert.Contains("--color-on-primary-rgb:255 255 255;", body)
        Assert.Contains("--color-on-primary:rgb(255 255 255)", body)
        Assert.Contains("--color-primary-fill-rgb:10 10 16;", body)
        Assert.Contains("--color-on-primary-fill-rgb:255 255 255;", body)
        Assert.Contains("--color-primary-fill:rgb(10 10 16);", body)
        Assert.Contains("--color-on-primary-fill:rgb(255 255 255)", body)
    }

[<Fact>]
let ``the accent stylesheet is text/css, nosniff, immutable and carries its format version in the ETag`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/branding/accent/f26b1d.css"

        Assert.Equal("text/css; charset=utf-8", string response.Content.Headers.ContentType)
        Assert.Equal<string list>([ "nosniff" ], header response "X-Content-Type-Options")

        let cacheControl = string response.Headers.CacheControl
        Assert.Contains("public", cacheControl)
        Assert.Contains("max-age=31536000", cacheControl)
        Assert.Contains("immutable", cacheControl)

        Assert.Equal("\"f26b1d-2\"", string response.Headers.ETag)
    }

[<Fact>]
let ``the accent stylesheet answers a revalidation with a 304 and the same headers`` () =
    task {
        use! app = Fixtures.createApp ()

        let! notModified = revalidate app "/branding/accent/f26b1d.css" "\"f26b1d-2\""
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode)
        Assert.Equal<string list>([ "nosniff" ], header notModified "X-Content-Type-Options")
        Assert.Equal("\"f26b1d-2\"", string notModified.Headers.ETag)
        let! body = notModified.Content.ReadAsStringAsync()
        Assert.Equal("", body)

        // The weak form a proxy may add, and `*`, count too.
        let! weak = revalidate app "/branding/accent/f26b1d.css" "W/\"f26b1d-2\""
        Assert.Equal(HttpStatusCode.NotModified, weak.StatusCode)
        let! star = revalidate app "/branding/accent/f26b1d.css" "*"
        Assert.Equal(HttpStatusCode.NotModified, star.StatusCode)

        // Another colour's ETag is not this one's.
        let! other = revalidate app "/branding/accent/f26b1d.css" "\"0a0a10-2\""
        Assert.Equal(HttpStatusCode.OK, other.StatusCode)

        // A browser that holds the body from before 007 added the fill
        // properties (format version 1) is told it is stale.
        let! stale = revalidate app "/branding/accent/f26b1d.css" "\"f26b1d-1\""
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode)
    }

[<Theory>]
[<InlineData("F26B1D")>]
[<InlineData("f26b1")>]
[<InlineData("f26b1dd")>]
[<InlineData("zzzzzz")>]
[<InlineData("f26b1d.css")>]
[<InlineData("%23f26b1d")>]
[<InlineData("")>]
let ``an accent path that is not six lowercase hex digits is a 404`` (value: string) =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync $"/branding/accent/{value}.css"
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)
    }

[<Fact>]
let ``an accent path without the css extension is a 404`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/branding/accent/f26b1d"
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)
    }

[<Fact>]
let ``the accent stylesheet needs no session and reads no database`` () =
    task {
        // Every colour it can emit is one the operator could have set anyway,
        // so an accent no app uses is still served: nothing says who is branded.
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/branding/accent/123456.css"
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    }

// ---------------------------------------------------------------------------
// GET /branding/logo/{sha256}.png
// ---------------------------------------------------------------------------

/// Any bytes will do: serving does not read them as an image.
let private seedLogo (app: Fixtures.TestApp) (clientId: string) (bytes: byte[]) =
    task {
        let sha = Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()

        do!
            BrandingStore.setLogo
                app.DbConfig
                (app.Clock.GetUtcNow())
                clientId
                { Png = bytes
                  Sha256 = sha
                  Width = 64
                  Height = 32 }

        return sha
    }

let private pngLike = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy; 1uy; 2uy; 3uy |]

[<Fact>]
let ``a logo is served by the hash of its bytes with every header the contract names`` () =
    task {
        use! app = Fixtures.createApp ()
        let! sha = seedLogo app "grafana" pngLike

        let! response = app.Client.GetAsync $"/branding/logo/{sha}.png"
        let! body = response.Content.ReadAsByteArrayAsync()

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        Assert.Equal<byte array>(pngLike, body)
        Assert.Equal("image/png", string response.Content.Headers.ContentType)
        Assert.Equal<string list>([ "nosniff" ], header response "X-Content-Type-Options")
        Assert.Equal<string list>([ "default-src 'none'; sandbox" ], header response "Content-Security-Policy")
        Assert.Equal<string list>([ "same-origin" ], header response "Cross-Origin-Resource-Policy")

        let cacheControl = string response.Headers.CacheControl
        Assert.Contains("public", cacheControl)
        Assert.Contains("max-age=31536000", cacheControl)
        Assert.Contains("immutable", cacheControl)

        Assert.Equal($"\"{sha}\"", string response.Headers.ETag)
    }

[<Fact>]
let ``a logo answers a revalidation with a 304`` () =
    task {
        use! app = Fixtures.createApp ()
        let! sha = seedLogo app "grafana" pngLike

        let! notModified = revalidate app $"/branding/logo/{sha}.png" $"\"{sha}\""
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode)
        Assert.Equal<string list>([ "nosniff" ], header notModified "X-Content-Type-Options")
        Assert.Equal<string list>([ "default-src 'none'; sandbox" ], header notModified "Content-Security-Policy")
        Assert.Equal<string list>([ "same-origin" ], header notModified "Cross-Origin-Resource-Policy")
        Assert.Equal($"\"{sha}\"", string notModified.Headers.ETag)

        let! star = revalidate app $"/branding/logo/{sha}.png" "*"
        Assert.Equal(HttpStatusCode.NotModified, star.StatusCode)

        let! other = revalidate app $"/branding/logo/{sha}.png" "\"something-else\""
        Assert.Equal(HttpStatusCode.OK, other.StatusCode)
    }

[<Fact>]
let ``a hash no app holds, an uppercase hash and a short one are 404s`` () =
    task {
        use! app = Fixtures.createApp ()
        let! sha = seedLogo app "grafana" pngLike

        // Well formed, held by nobody.
        let! unknown = app.Client.GetAsync $"/branding/logo/{String('0', 64)}.png"
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode)

        // The right hash in the wrong case is not the address.
        let! upper = app.Client.GetAsync $"/branding/logo/{sha.ToUpperInvariant()}.png"
        Assert.Equal(HttpStatusCode.NotFound, upper.StatusCode)

        let! short = app.Client.GetAsync $"/branding/logo/{sha.Substring 1}.png"
        Assert.Equal(HttpStatusCode.NotFound, short.StatusCode)

        let! long = app.Client.GetAsync $"/branding/logo/{sha}0.png"
        Assert.Equal(HttpStatusCode.NotFound, long.StatusCode)

        let! notHex = app.Client.GetAsync $"/branding/logo/{String('g', 64)}.png"
        Assert.Equal(HttpStatusCode.NotFound, notHex.StatusCode)

        let! noExtension = app.Client.GetAsync $"/branding/logo/{sha}"
        Assert.Equal(HttpStatusCode.NotFound, noExtension.StatusCode)
    }

[<Fact>]
let ``a revalidation does not turn a logo that is gone into a 304`` () =
    task {
        use! app = Fixtures.createApp ()
        let! sha = seedLogo app "grafana" pngLike
        let! _ = BrandingStore.removeLogo app.DbConfig (app.Clock.GetUtcNow()) "grafana"
        ()

        // A browser that cached the logo asks again with its ETag. The answer
        // is that it no longer exists, not "unchanged" (FR-020).
        let! response = revalidate app $"/branding/logo/{sha}.png" $"\"{sha}\""
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)
    }

[<Fact>]
let ``a replaced logo's old address stops resolving and the new one serves`` () =
    task {
        use! app = Fixtures.createApp ()
        let! first = seedLogo app "grafana" pngLike
        let second = Array.append pngLike [| 9uy |]
        let! secondSha = seedLogo app "grafana" second

        let! old = app.Client.GetAsync $"/branding/logo/{first}.png"
        Assert.Equal(HttpStatusCode.NotFound, old.StatusCode)

        let! current = app.Client.GetAsync $"/branding/logo/{secondSha}.png"
        let! body = current.Content.ReadAsByteArrayAsync()
        Assert.Equal<byte array>(second, body)
    }
