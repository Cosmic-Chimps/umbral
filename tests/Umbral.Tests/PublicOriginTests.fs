module Umbral.Tests.PublicOriginTests

open Xunit
open Umbral.Infrastructure.PublicOrigin

let private ok raw =
    match parse raw with
    | Ok origin -> origin
    | Error e -> failwith $"expected success, got: {describe e}"

let private err raw =
    match parse raw with
    | Error e -> e
    | Ok origin -> failwith $"expected failure, got: {origin.Text}"

[<Fact>]
let ``an https origin is accepted`` () =
    Assert.Equal("https://auth.example.com", (ok "https://auth.example.com").Text)

[<Fact>]
let ``a trailing slash is normalised away`` () =
    // This one matters more than it looks: the origin is concatenated with
    // paths, and "https://x//connect/..." is not what a provider has registered.
    Assert.Equal("https://auth.example.com", (ok "https://auth.example.com/").Text)

[<Fact>]
let ``an uppercase host is lowercased`` () =
    Assert.Equal("https://auth.example.com", (ok "https://AUTH.Example.COM").Text)

[<Fact>]
let ``surrounding whitespace is ignored`` () =
    Assert.Equal("https://auth.example.com", (ok "  https://auth.example.com  ").Text)

[<Fact>]
let ``a non-default port is kept`` () =
    Assert.Equal("https://auth.example.com:8443", (ok "https://auth.example.com:8443").Text)

[<Fact>]
let ``a default port is dropped`` () =
    // https://x:443 and https://x are the same origin, but only one of them
    // string-matches what the provider stored.
    Assert.Equal("https://auth.example.com", (ok "https://auth.example.com:443").Text)

[<Fact>]
let ``http is accepted for localhost`` () =
    Assert.Equal("http://localhost:8080", (ok "http://localhost:8080").Text)

[<Fact>]
let ``http is accepted for the loopback address`` () =
    Assert.Equal("http://127.0.0.1:8080", (ok "http://127.0.0.1:8080").Text)

[<Fact>]
let ``http is refused for a remote host`` () =
    match err "http://auth.example.com" with
    | InsecureSchemeOnRemoteHost host -> Assert.Equal("auth.example.com", host)
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``an unset value is refused`` () =
    Assert.Equal(NotSet, err "")
    Assert.Equal(NotSet, err "   ")

[<Fact>]
let ``a relative value is refused`` () =
    match err "auth.example.com" with
    | NotAbsolute _ -> ()
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``a sub-path is refused`` () =
    match err "https://example.com/umbral" with
    | HasPath path -> Assert.Equal("/umbral", path)
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``a query string is refused`` () =
    Assert.Equal(HasQueryOrFragment, err "https://example.com?x=1")

[<Fact>]
let ``a non-http scheme is refused`` () =
    match err "ftp://example.com" with
    | UnsupportedScheme scheme -> Assert.Equal("ftp", scheme)
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``every message names the variable and an action`` () =
    // Principle VI and SC-010: an operator-facing failure says what to do next.
    let errors =
        [ NotSet
          NotAbsolute "x"
          UnsupportedScheme "ftp"
          InsecureSchemeOnRemoteHost "example.com"
          HasPath "/x"
          HasQueryOrFragment ]

    for e in errors do
        let message = describe e
        Assert.Contains("UMBRAL__PUBLIC_ORIGIN", message)
        Assert.True(message.Length > 40, $"message is too terse to act on: {message}")

[<Fact>]
let ``the callback uri is what the operator must register`` () =
    let origin = ok "https://auth.example.com"
    Assert.Equal("https://auth.example.com/connect/discord/callback", (callbackUri origin "discord").ToString())

[<Fact>]
let ``the callback uri is right when the origin carries a port`` () =
    let origin = ok "http://localhost:8080"
    Assert.Equal("http://localhost:8080/connect/github/callback", (callbackUri origin "github").ToString())

[<Fact>]
let ``reading from an empty environment reports NotSet`` () =
    Assert.Equal(Error NotSet, fromEnvironment (fun _ -> null))

[<Fact>]
let ``configuration is read by section key or by variable name`` () =
    // An operator sets UMBRAL__PUBLIC_ORIGIN; a developer runs
    // `dotnet user-secrets set "UMBRAL:PUBLIC_ORIGIN"`. .NET maps `__` to `:`,
    // so both are the same setting and both must resolve.
    let only (key: string) (value: string) : string -> string | null =
        fun k -> if k = key then value else null

    let bySection = fromConfiguration (only ConfigurationKey "https://auth.example.com")
    let byVariable = fromConfiguration (only EnvironmentVariable "https://auth.example.com")

    match bySection, byVariable with
    | Ok a, Ok b ->
        Assert.Equal("https://auth.example.com", a.Text)
        Assert.Equal("https://auth.example.com", b.Text)
    | _ -> failwith "both spellings of the setting must resolve"

[<Fact>]
let ``configuration with neither key set reports NotSet`` () =
    Assert.Equal(Error NotSet, fromConfiguration (fun _ -> null))
