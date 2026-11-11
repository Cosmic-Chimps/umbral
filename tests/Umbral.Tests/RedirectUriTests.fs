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
