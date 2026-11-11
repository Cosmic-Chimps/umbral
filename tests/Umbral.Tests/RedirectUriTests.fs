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
let ``an unacceptable address passes the reason of acceptable through`` () =
    let value = "https://*.example.com/cb"

    match RedirectUris.add [] value, RedirectUris.acceptable value with
    | Error(RedirectUris.Invalid why), Error expected -> Assert.Equal(expected, why)
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
