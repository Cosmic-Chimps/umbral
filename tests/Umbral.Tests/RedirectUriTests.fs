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
