module Umbral.Tests.SignInFlowTests

open System
open System.Net
open Xunit
open Umbral.Handlers.Connect
open Umbral.Tests.Infrastructure

// ---------------------------------------------------------------------------
// The open-redirect guard
//
// `returnUrl` reaches the picker straight from a query string, so it is
// attacker-supplied by construction. Anything but a path on this instance would
// make the sign-in page a redirector — the classic phishing primitive, and far
// more convincing on a login domain than anywhere else.
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("/connect/authorize?client_id=x", "/connect/authorize?client_id=x")>]
[<InlineData("/", "/")>]
[<InlineData("/admin/apps", "/admin/apps")>]
let ``a local path is kept`` (input: string) (expected: string) =
    Assert.Equal(expected, Picker.sanitizeReturnUrl input)

[<Theory>]
[<InlineData("https://evil.example.com/steal")>]
[<InlineData("http://evil.example.com")>]
// Protocol-relative: the browser reads this as a host, not a path.
[<InlineData("//evil.example.com/steal")>]
[<InlineData("javascript:alert(1)")>]
[<InlineData("evil.example.com")>]
[<InlineData("")>]
let ``anything that could leave this instance becomes the root`` (input: string) =
    Assert.Equal("/", Picker.sanitizeReturnUrl input)

[<Fact>]
let ``a missing returnUrl becomes the root`` () =
    Assert.Equal("/", Picker.sanitizeReturnUrl null)

// ---------------------------------------------------------------------------
// The picker
// ---------------------------------------------------------------------------

[<Fact>]
let ``the picker says so when no connector is configured`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/signin"
        let! body = response.Content.ReadAsStringAsync()

        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        // The end user cannot fix this, so the message has to point at the
        // person who can rather than imply they did something wrong.
        Assert.Contains("data-no-connectors", body)
    }

[<Fact>]
let ``an unknown connector start is a 404`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/connect/nope/start"
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)
    }

// ---------------------------------------------------------------------------
// The callback, with no provider involved at all
// ---------------------------------------------------------------------------

[<Fact>]
let ``a callback with no correlation cookie is refused`` () =
    task {
        use! app = Fixtures.createApp ()

        // Arriving cold — a replayed link, or someone poking at the route.
        let! response = app.Client.GetAsync "/connect/discord/callback?code=abc&state=xyz"

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("/signin", response.Headers.Location |> string)
        Assert.Contains("msg=sign-in-failed", response.Headers.Location |> string)
    }

[<Fact>]
let ``a callback with no state is refused`` () =
    task {
        use! app = Fixtures.createApp ()
        let! response = app.Client.GetAsync "/connect/discord/callback?code=abc"
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("msg=sign-in-failed", response.Headers.Location |> string)
    }

[<Fact>]
let ``a cold access_denied is NOT reported as a decline`` () =
    task {
        use! app = Fixtures.createApp ()

        // A real cancellation carries the correlation cookie, because /start
        // set it. Arriving without one means the query string is unattested —
        // anyone can type this URL — so nothing in it may be believed, not even
        // the claim that a person pressed cancel. The correlation is checked
        // before the provider's error for exactly this reason.
        //
        // The cookie-present decline path is covered by the round-trip state
        // machine tests, where it can be driven honestly.
        let! response = app.Client.GetAsync "/connect/discord/callback?error=access_denied&state=xyz"

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("msg=sign-in-failed", response.Headers.Location |> string)
    }

[<Fact>]
let ``the failure redirect never carries the provider's own words`` () =
    task {
        use! app = Fixtures.createApp ()

        let! response =
            app.Client.GetAsync
                "/connect/discord/callback?error=invalid_client&error_description=The%20client%20secret%20is%20wrong&state=xyz"

        // FR-037: the operator gets the cause in the security log; the end user
        // gets a code. A provider's text can name a misconfiguration.
        let location = response.Headers.Location |> string
        Assert.DoesNotContain("client", location)
        Assert.DoesNotContain("secret", location)
    }
