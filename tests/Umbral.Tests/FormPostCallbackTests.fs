/// `POST /connect/{slug}/callback`: the route a provider that answers with
/// `response_mode=form_post` (Sign in with Apple) comes back on, through the
/// real pipeline (specs/013 research R3).
///
/// The route takes no antiforgery token, because the form is the provider's
/// page and not Umbral's. What these tests hold it to is what stands in the
/// token's place: no correlation cookie of the form-post mode, no sign-in, and
/// every way a request can be malformed ends as the ordinary refusal and never
/// as a server error. The whole Apple flow is tested with a fake Apple in
/// AppleSignInTests; there is no form-post provider to sign in through here.
///
/// These posts carry no form-post correlation, so they reach
/// `no_correlation_cookie` whether or not the limits work. That the body is NOT
/// read when it is over a limit, with the right cookie and the right state, is
/// held in AppleSignInTests, where a real sign-in has set the cookie.
module Umbral.Tests.FormPostCallbackTests

open System
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.RegularExpressions
open Xunit
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

/// A body that does not know its length, so a request carries no
/// `Content-Length` and the size limit has to be enforced while reading.
type private Chunked(bytes: byte[], contentType: string) as this =
    inherit HttpContent()
    do this.Headers.ContentType <- MediaTypeHeaderValue contentType

    override _.SerializeToStreamAsync(stream, _context) =
        stream.WriteAsync(bytes, 0, bytes.Length)

    override _.TryComputeLength(length: byref<int64>) =
        length <- 0L
        false

let private instance () =
    task {
        let! app = Fixtures.createAppWith fakeSettings ignore
        do! seedFakeConnector app
        return app
    }

let private post (browser: Browser) (content: HttpContent) =
    let message = new HttpRequestMessage(HttpMethod.Post, "/connect/fake/callback")
    message.Content <- content
    browser.Send message

let private form (fields: (string * string) list) =
    new FormUrlEncodedContent(fields |> List.map (fun (k, v) -> Collections.Generic.KeyValuePair(k, v)))

let private count (app: Fixtures.TestApp) (detail: string) =
    Flows.scalar
        app
        "SELECT count(*) FROM security_events WHERE event_type = 'sign_in_failed' AND detail = @detail"
        [ "@detail", detail ]

/// The ordinary refusal of a failed sign-in: back to the sign-in page with the
/// failure code, no session, nothing signed in.
let private assertRefused (app: Fixtures.TestApp) (browser: Browser) (response: HttpResponseMessage) =
    task {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        let target = location response
        Assert.Contains("/signin", target)
        Assert.Contains("msg=sign-in-failed", target)
        Assert.False(browser.Cookies.ContainsKey Umbral.Infrastructure.Sessions.CookieName, "no session was started")

        let! succeeded = Flows.scalar app "SELECT count(*) FROM security_events WHERE event_type = 'sign_in_succeeded'" []
        Assert.Equal("0", succeeded)
    }

[<Fact>]
let ``a form post with no correlation cookie is refused as a failed sign-in`` () =
    task {
        use! app = instance ()
        let browser = Browser app.Client

        let! response = post browser (form [ "state", "xyz"; "code", "abc" ])

        // Not a 400, 404 or 405: the route exists, and answers as the GET does.
        do! assertRefused app browser response

        let! failures = count app "no_correlation_cookie"
        Assert.Equal("1", failures)
    }

[<Fact>]
let ``a form post needs no antiforgery token and is not answered with the antiforgery error`` () =
    task {
        use! app = instance ()
        let browser = Browser app.Client

        // `PostWithoutToken` is the cross-site scenario the admin routes are
        // held to; here it is the shape of the request Apple makes.
        let! response = browser.PostWithoutToken("/connect/fake/callback", [ "state", "xyz"; "code", "abc" ])

        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode)
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode)
        do! assertRefused app browser response
    }

[<Fact>]
let ``the correlation cookie of a redirect sign-in cannot finish it by a form post`` () =
    task {
        use! app = instance ()
        let browser = Browser app.Client

        // A real start: the browser now holds the Lax cookie and knows the state.
        let! toProvider = browser.Get "/connect/fake/start"
        Assert.True(browser.Cookies.ContainsKey "umbral_connect", "the redirect mode set its own cookie")
        Assert.False(browser.Cookies.ContainsKey "__Secure-umbral_connect_post", "and not the form-post one")

        let state = Regex.Match(Uri.UnescapeDataString(location toProvider), "state=([^&]+)").Groups[1].Value
        Assert.NotEmpty state

        // The right state and the browser's own cookie, delivered by POST: the
        // POST looks only at the form-post cookie, which this browser has not got.
        let! response = post browser (form [ "state", state; "code", "abc" ])

        do! assertRefused app browser response

        let! failures = count app "no_correlation_cookie"
        Assert.Equal("1", failures)
    }

[<Fact>]
let ``a post that is not a form is refused the same way, without a server error`` () =
    task {
        use! app = instance ()
        let browser = Browser app.Client

        let json = new StringContent("""{"state":"xyz","code":"abc"}""", Encoding.UTF8, "application/json")
        let! response = post browser json

        do! assertRefused app browser response

        let! failures = count app "no_correlation_cookie"
        Assert.Equal("1", failures)
    }

[<Fact>]
let ``a body larger than 64 KB with no correlation is refused the same way, without a server error`` () =
    task {
        use! app = instance ()
        let browser = Browser app.Client

        // Declared length: the body is not read at all.
        let big = String('x', 70 * 1024)
        let! response = post browser (form [ "state", big; "code", "abc" ])
        do! assertRefused app browser response

        // No declared length: the limit is enforced while reading.
        let body = Encoding.UTF8.GetBytes("state=" + big)
        let! chunked = post browser (new Chunked(body, "application/x-www-form-urlencoded"))
        do! assertRefused app browser chunked

        let! failures = count app "no_correlation_cookie"
        Assert.Equal("2", failures)
    }

[<Fact>]
let ``a form with too many fields and no correlation is refused the same way`` () =
    task {
        use! app = instance ()
        let browser = Browser app.Client

        let fields = [ for i in 1..20 -> $"f{i}", "v" ]
        let! response = post browser (form fields)

        do! assertRefused app browser response
    }
