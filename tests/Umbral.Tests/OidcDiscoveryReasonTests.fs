/// The reason discovery gives when the connection itself failed reads in one
/// level: it carries no brackets of its own, because every notice that shows
/// it puts it in brackets (task fl4t, the designer's ruling of 2026-11-13).
module Umbral.Tests.OidcDiscoveryReasonTests

open System
open System.Net
open System.Net.Http
open System.Net.Sockets
open Xunit
open Umbral.Infrastructure.OidcDiscovery

/// The deepest nesting of round brackets in a text.
let private depth (text: string) =
    text
    |> Seq.fold
        (fun (now, deepest) c ->
            match c with
            | '(' -> now + 1, max deepest (now + 1)
            | ')' -> now - 1, deepest
            | _ -> now, deepest)
        (0, 0)
    |> snd

// The messages .NET gives, as an `HttpRequestException` holds them: macOS
// (observed), Linux (the image) and Windows.
[<Theory>]
[<InlineData("Connection refused (127.0.0.1:9)", "Connection refused, 127.0.0.1:9")>]
[<InlineData("Connection refused ([::1]:9)", "Connection refused, [::1]:9")>]
[<InlineData("nodename nor servname provided, or not known (nosuch.invalid:443)",
             "nodename nor servname provided, or not known, nosuch.invalid:443")>]
[<InlineData("Name or service not known (nosuch.invalid:443)", "Name or service not known, nosuch.invalid:443")>]
[<InlineData("No such host is known. (nosuch.invalid:443)", "No such host is known, nosuch.invalid:443")>]
[<InlineData("The SSL connection could not be established, see inner exception.",
             "The SSL connection could not be established, see inner exception")>]
[<InlineData("Connection timed out (10.255.255.1:443)", "Connection timed out, 10.255.255.1:443")>]
[<InlineData("Operation timed out (10.255.255.1:443)", "Operation timed out, 10.255.255.1:443")>]
[<InlineData("Response status code does not indicate success: 404 (Not Found).",
             "Response status code does not indicate success: 404, Not Found")>]
let ``a transport message is flattened to one phrase with no brackets of its own`` (message: string, expected: string) =
    let flat = flatTransportMessage message
    Assert.Equal(expected, flat)
    Assert.Equal(0, depth flat)

[<Fact>]
let ``only the first line of a message is kept`` () =
    Assert.Equal("Connection refused, 127.0.0.1:9", flatTransportMessage "Connection refused (127.0.0.1:9)\n   at Some.Stack.Frame()")

[<Fact>]
let ``a long message is still cut to 160 characters`` () =
    let flat = flatTransportMessage (String('x', 300) + " (host:443)")
    Assert.Equal(String('x', 160) + "…", flat)

[<Theory>]
[<InlineData("")>]
[<InlineData("(127.0.0.1:9)")>]
[<InlineData("Something (with (nested) detail)")>]
let ``a message the rule does not fit is left as it was, words only`` (message: string) =
    // Nothing is invented: what does not end in one plain bracket stays as it was.
    Assert.Equal(message.Trim().TrimEnd('.'), flatTransportMessage message)

/// A loopback port nothing listens on: bound, read, then let go.
let private closedPort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

/// What a real refused connection makes of discovery's reason.
let private refusedReason () =
    task {
        let issuer = $"http://127.0.0.1:{closedPort ()}"
        // `true`: the Development escape hatch, the only way a loopback issuer is asked.
        let discovery = OidcDiscovery(new HttpClient(), true, TimeSpan.FromSeconds 10.0, TimeProvider.System)

        match! discovery.Check issuer with
        | Unreachable reason -> return issuer, reason
        | other -> return failwith $"expected an unreachable provider, got {other}"
    }

[<Fact>]
let ``a refused connection is said flat, with the address after a comma`` () =
    task {
        let! issuer, reason = refusedReason ()
        let endpoint = issuer.Substring "http://".Length
        Assert.StartsWith("the connection failed: ", reason)
        Assert.EndsWith($", {endpoint}", reason)
        Assert.Equal(0, depth reason)
    }

[<Fact>]
let ``every notice that shows a discovery reason has one level of brackets`` () =
    task {
        let! issuer, reason = refusedReason ()
        let t = Umbral.Views.Texts.en

        let notices =
            [ "oidc saved", t.ConnectorAddOidcUnreachable issuer reason
              "oidc enable", t.ConnectorEnableOidcUnreachable issuer reason
              "apple saved", t.AppleSavedUnreachable reason
              "apple enable", t.AppleEnableUnreachable reason
              "microsoft saved", t.MicrosoftSavedUnreachable reason
              "microsoft enable", t.MicrosoftEnableUnreachable reason
              "microsoft unchecked", t.MicrosoftSavedUnchecked reason
              "microsoft not answered", t.MicrosoftNotAnswered reason ]

        for name, notice in notices do
            Assert.True(depth notice = 1, $"{name}: {notice}")
            Assert.Contains($"({reason})", notice)
    }
