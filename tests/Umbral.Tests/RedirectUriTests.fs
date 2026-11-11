module Umbral.Tests.RedirectUriTests

open System
open Xunit
open Umbral.Domains

// ---------------------------------------------------------------------------
// The redirect-URI rule
//
// Used by the Apps admin form. It lived in two places until the copies
// diverged, so these cases pin the behaviour the one remaining copy must keep.
// ---------------------------------------------------------------------------

[<Theory>]
// Frunchy's real callback: http on localhost must be usable, or no one can
// develop an integration.
[<InlineData("http://localhost:5099/signin-umbral")>]
[<InlineData("https://grafana.example.com/login/generic_oauth")>]
// A native app's custom scheme is legitimate and must not be refused.
[<InlineData("com.example.app://callback")>]
let ``an acceptable redirect URI is accepted`` (value: string) =
    match RedirectUris.acceptable value with
    | Ok _ -> ()
    | Error why -> failwith $"{value} should be accepted: {why}"

[<Theory>]
// On Unix this parses as an absolute file:// uri, which is the trap.
[<InlineData("/signin-umbral", "absolute")>]
[<InlineData("https://*.example.com/cb", "wildcard")>]
[<InlineData("https://app.example.com/cb#frag", "fragment")>]
[<InlineData("", "empty")>]
let ``an unacceptable redirect URI is refused, and says why`` (value: string) (because: string) =
    match RedirectUris.acceptable value with
    | Ok _ -> failwith $"{value} should be refused"
    | Error why -> Assert.Contains(because, why, StringComparison.OrdinalIgnoreCase)

// ---------------------------------------------------------------------------
// Adding to and removing from an app's list (spec 012)
// ---------------------------------------------------------------------------

[<Fact>]
let ``an acceptable address is appended after the existing ones`` () =
    let existing = [ "https://a.example.com/cb"; "https://b.example.com/cb" ]

    match RedirectUris.add existing "  https://c.example.com/cb  " with
    | Ok list -> Assert.Equal<string list>(existing @ [ "https://c.example.com/cb" ], list)
    | Error e -> failwith $"should be added: {e}"

[<Fact>]
let ``an unacceptable address passes the problem of check through`` () =
    let value = "https://*.example.com/cb"

    match RedirectUris.add [] value, RedirectUris.check value with
    | Error(RedirectUris.Invalid problem), Error expected -> Assert.Equal(expected, problem)
    | other -> failwith $"expected Invalid, got {other}"

[<Theory>]
[<InlineData("https://a.example.com/cb")>]
// Only the host differs, in case: the same address to every provider.
[<InlineData("https://A.Example.com/cb")>]
let ``an address already registered is refused`` (candidate: string) =
    match RedirectUris.add [ "https://a.example.com/cb" ] candidate with
    | Error RedirectUris.AlreadyRegistered -> ()
    | other -> failwith $"expected AlreadyRegistered, got {other}"

[<Fact>]
let ``a full list refuses a new address`` () =
    let full = [ for i in 1 .. RedirectUris.maxPerApp -> $"https://app{i}.example.com/cb" ]
    Assert.Equal(10, RedirectUris.maxPerApp)

    match RedirectUris.add full "https://extra.example.com/cb" with
    | Error RedirectUris.Full -> ()
    | other -> failwith $"expected Full, got {other}"

    // A duplicate is still reported as a duplicate on a full list.
    match RedirectUris.add full "https://app1.example.com/cb" with
    | Error RedirectUris.AlreadyRegistered -> ()
    | other -> failwith $"expected AlreadyRegistered, got {other}"

[<Fact>]
let ``removing a registered address leaves the others in order`` () =
    let existing = [ "https://a.example.com/cb"; "https://b.example.com/cb"; "https://c.example.com/cb" ]

    match RedirectUris.remove existing "https://b.example.com/cb" true with
    | Ok list -> Assert.Equal<string list>([ "https://a.example.com/cb"; "https://c.example.com/cb" ], list)
    | Error e -> failwith $"should be removed: {e}"

[<Fact>]
let ``removing an address that is not registered is refused`` () =
    match RedirectUris.remove [ "https://a.example.com/cb" ] "https://other.example.com/cb" true with
    | Error RedirectUris.NotRegistered -> ()
    | other -> failwith $"expected NotRegistered, got {other}"

[<Fact>]
let ``the last address is refused when one must stay, allowed when not`` () =
    let only = [ "https://a.example.com/cb" ]

    match RedirectUris.remove only "https://a.example.com/cb" true with
    | Error RedirectUris.LastOne -> ()
    | other -> failwith $"expected LastOne, got {other}"

    match RedirectUris.remove only "https://a.example.com/cb" false with
    | Ok list -> Assert.Empty list
    | Error e -> failwith $"should be removed: {e}"

[<Theory>]
// A port-less loopback address accepts any port; the same one with port 80 only
// that port. `Uri` equality calls them equal (80 is http's default), the
// loopback rule does not, so either may be added beside the other.
[<InlineData("http://127.0.0.1:80/callback", "http://127.0.0.1/callback")>]
[<InlineData("http://127.0.0.1/callback", "http://127.0.0.1:80/callback")>]
[<InlineData("http://[::1]:80/callback", "http://[::1]/callback")>]
let ``a loopback address with and without a port are two registrations`` (existing: string) (candidate: string) =
    match RedirectUris.add [ existing ] candidate with
    | Ok list -> Assert.Equal<string list>([ existing; candidate ], list)
    | Error e -> failwith $"should be added: {e}"

// ---------------------------------------------------------------------------
// Loopback redirect on any port (RFC 8252 §7.3)
//
// A native app registers `http://127.0.0.1/callback` and asks for it with
// whatever port it could bind. Only the port may differ, and only for the two
// loopback IP literals; the cases below are the ways a looser rule goes wrong.
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("http://127.0.0.1/callback", "http://127.0.0.1:53124/callback")>]
[<InlineData("http://[::1]/callback", "http://[::1]:8400/callback")>]
[<InlineData("http://127.0.0.1/cb?x=1", "http://127.0.0.1:1/cb?x=1")>]
[<InlineData("http://127.0.0.1", "http://127.0.0.1:65535")>]
let ``a loopback registration accepts the same address on any port`` (registered: string) (requested: string) =
    Assert.True(RedirectUris.loopbackOnAnyPort registered requested)

[<Theory>]
// localhost is a name, not an address (RFC 8252 §8.3).
[<InlineData("http://localhost/callback", "http://localhost:53124/callback")>]
[<InlineData("https://127.0.0.1/callback", "https://127.0.0.1:53124/callback")>]
[<InlineData("https://[::1]/callback", "https://[::1]:53124/callback")>]
// Path and query are the registered text, exactly.
[<InlineData("http://127.0.0.1/callback", "http://127.0.0.1:53124/callback2")>]
[<InlineData("http://127.0.0.1/callback", "http://127.0.0.1:53124/Callback")>]
[<InlineData("http://127.0.0.1/callback", "http://127.0.0.1:53124/callback/")>]
[<InlineData("http://127.0.0.1/callback", "http://127.0.0.1:53124")>]
[<InlineData("http://127.0.0.1/cb?x=1", "http://127.0.0.1:53124/cb?x=2")>]
[<InlineData("http://127.0.0.1/cb?x=1", "http://127.0.0.1:53124/cb")>]
[<InlineData("http://127.0.0.1/cb", "http://127.0.0.1:53124/cb?x=1")>]
// A registration that names a port asked for that port.
[<InlineData("http://127.0.0.1:8400/callback", "http://127.0.0.1:8401/callback")>]
[<InlineData("http://[::1]:8400/callback", "http://[::1]:8401/callback")>]
// The port itself.
[<InlineData("http://127.0.0.1/cb", "http://127.0.0.1:0/cb")>]
[<InlineData("http://127.0.0.1/cb", "http://127.0.0.1:65536/cb")>]
[<InlineData("http://127.0.0.1/cb", "http://127.0.0.1:08400/cb")>]
[<InlineData("http://127.0.0.1/cb", "http://127.0.0.1:/cb")>]
[<InlineData("http://127.0.0.1/cb", "http://127.0.0.1:123456/cb")>]
[<InlineData("http://127.0.0.1/cb", "http://127.0.0.1:-1/cb")>]
[<InlineData("http://127.0.0.1/cb", "http://127.0.0.1:+80/cb")>]
[<InlineData("http://127.0.0.1/cb", "http://127.0.0.1: 80/cb")>]
// A different host dressed as the same one.
[<InlineData("http://127.0.0.1/cb", "http://127.0.0.1:80@evil.example/cb")>]
[<InlineData("http://127.0.0.1/cb", "http://127.0.0.1.evil.example:80/cb")>]
[<InlineData("http://127.0.0.1.evil.example/cb", "http://127.0.0.1.evil.example:80/cb")>]
[<InlineData("http://127.0.0.1@evil.example/cb", "http://127.0.0.1:80@evil.example/cb")>]
[<InlineData("HTTP://127.0.0.1/cb", "HTTP://127.0.0.1:80/cb")>]
[<InlineData("http://127.0.0.2/cb", "http://127.0.0.2:80/cb")>]
[<InlineData("http://127.0.0.1/cb", "http://127.0.0.2:80/cb")>]
[<InlineData("http://127.0.0.1/cb", "http://[::1]:80/cb")>]
// Exact matching is the caller's first check; this rule is only for the rest.
[<InlineData("http://127.0.0.1/callback", "http://127.0.0.1/callback")>]
[<InlineData("http://127.0.0.1:8400/callback", "http://127.0.0.1:8400/callback")>]
// Not a loopback registration at all.
[<InlineData("", "")>]
[<InlineData("", "http://127.0.0.1:80")>]
[<InlineData("http://127.0.0.1", "")>]
[<InlineData("https://app.example.com/cb", "https://app.example.com:8443/cb")>]
let ``a loopback registration refuses everything else`` (registered: string) (requested: string) =
    Assert.False(RedirectUris.loopbackOnAnyPort registered requested)

// ---------------------------------------------------------------------------
// The problem behind a refusal
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("", "empty")>]
[<InlineData("https://*.example.com/cb", "wildcard")>]
// On Unix a leading slash is a file URI, and is not an absolute URL for us.
[<InlineData("/cb", "not-absolute")>]
[<InlineData("not a url", "not-absolute")>]
[<InlineData("https:///cb", "not-absolute")>]
[<InlineData("https://a.example.com/cb#x", "fragment")>]
let ``check names the problem of an unacceptable address`` (value: string) (code: string) =
    match RedirectUris.check value with
    | Error problem -> Assert.Equal(code, RedirectUris.problemCode problem)
    | Ok _ -> failwith $"{value} should be refused"

[<Fact>]
let ``every problem round-trips through its code, and an unknown code is none`` () =
    let all =
        [ RedirectUris.Empty
          RedirectUris.Wildcard
          RedirectUris.NotAbsolute
          RedirectUris.NoHost
          RedirectUris.Fragment ]

    for problem in all do
        Assert.Equal(Some problem, RedirectUris.problemOfCode (RedirectUris.problemCode problem))

    Assert.Equal(None, RedirectUris.problemOfCode "nope")
    Assert.Equal(all.Length, all |> List.map RedirectUris.problemCode |> List.distinct |> List.length)
