/// The pure rules of the Fediverse connector, branch by branch (specs/010). No
/// network, no database: server spelling, the server list, public addresses, the
/// support rule and the subject.
module Umbral.Tests.FediverseDomainTests

open System.Net
open Xunit
open Umbral.Domains.Connectors
open Umbral.Domains.Fediverse

let private server (allowDev: bool) (text: string) =
    match normaliseServer allowDev text with
    | Ok host -> host
    | Error error -> failwith $"'{text}' should normalise but gave {error}"

let private refused (allowDev: bool) (text: string) =
    match normaliseServer allowDev text with
    | Ok host -> failwith $"'{text}' should be refused but gave {host}"
    | Error error -> error

// ---------------------------------------------------------------------------
// normaliseServer
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("mastodon.social")>]
[<InlineData("  mastodon.social  ")>]
[<InlineData("@mastodon.social")>]
[<InlineData("https://mastodon.social")>]
[<InlineData("HTTPS://Mastodon.Social")>]
[<InlineData("https://mastodon.social/")>]
[<InlineData("mastodon.social/")>]
[<InlineData("MASTODON.SOCIAL")>]
[<InlineData("mastodon.social.")>]
[<InlineData("mastodon.social:443")>]
[<InlineData("https://mastodon.social:443/")>]
let ``one server has one spelling`` (text: string) =
    Assert.Equal("mastodon.social", server false text)

[<Fact>]
let ``an internationalised name becomes ASCII`` () =
    Assert.Equal("xn--bcher-kva.example", server false "bücher.example")
    Assert.Equal("xn--bcher-kva.example", server false "https://Bücher.example/")

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData("mastodon.social/about")>]
[<InlineData("https://mastodon.social/about")>]
[<InlineData("mastodon.social?x=1")>]
[<InlineData("mastodon.social#top")>]
[<InlineData("ana@mastodon.social")>]
[<InlineData("mastodon .social")>]
[<InlineData("mastodon.social:8080")>]
[<InlineData("mastodon.social:0")>]
[<InlineData("mastodon.social:99999")>]
[<InlineData("mastodon.social:abc")>]
[<InlineData("http://mastodon.social")>]
[<InlineData("localhost")>]
[<InlineData("localhost:3000")>]
[<InlineData("127.0.0.1")>]
[<InlineData("10.0.0.1")>]
[<InlineData("[::1]")>]
[<InlineData("[::1]:8080")>]
[<InlineData("::1")>]
[<InlineData("mastodon")>]
[<InlineData("-bad-.example")>]
[<InlineData("[::1")>]
let ``what is not a server is refused`` (text: string) =
    match refused false text with
    | NotAServer entry -> Assert.Equal(text.Trim(), entry)
    | other -> failwith $"expected NotAServer, got {other}"

[<Theory>]
[<InlineData("*.social")>]
[<InlineData("mastodon.*")>]
[<InlineData("*")>]
let ``a wildcard is its own refusal`` (text: string) =
    Assert.Equal(Wildcard text, refused false text)
    Assert.Equal(Wildcard text, refused true text)

[<Fact>]
let ``null is not a server`` () =
    Assert.Equal(Error(NotAServer ""), normaliseServer false null)

[<Fact>]
let ``the development setting admits http, ports, localhost and IP literals`` () =
    Assert.Equal("localhost:3000", server true "http://localhost:3000")
    Assert.Equal("localhost", server true "localhost")
    Assert.Equal("127.0.0.1:8080", server true "127.0.0.1:8080")
    Assert.Equal("[::1]:8080", server true "[::1]:8080")
    Assert.Equal("[::1]", server true "::1")
    Assert.Equal("mastodon.local.example:8443", server true "http://Mastodon.Local.Example:8443/")

[<Fact>]
let ``the development setting still drops port 443 and still refuses a path`` () =
    Assert.Equal("mastodon.social", server true "mastodon.social:443")
    Assert.Equal(NotAServer "mastodon.social/about", refused true "mastodon.social/about")
    Assert.Equal(NotAServer "mastodon.social:0", refused true "mastodon.social:0")

// ---------------------------------------------------------------------------
// parseInput
// ---------------------------------------------------------------------------

[<Fact>]
let ``the two handle forms`` () =
    Assert.Equal(Some(Handle("ana", "mastodon.social")), parseInput false "@ana@mastodon.social")
    Assert.Equal(Some(Handle("ana", "mastodon.social")), parseInput false "ana@mastodon.social")

[<Fact>]
let ``the two server forms`` () =
    Assert.Equal(Some(Server "mastodon.social"), parseInput false "mastodon.social")
    Assert.Equal(Some(Server "mastodon.social"), parseInput false "https://mastodon.social")

[<Fact>]
let ``a handle keeps its user as typed and normalises the domain`` () =
    Assert.Equal(Some(Handle("Ana_B.c-d", "mastodon.social")), parseInput false "  @Ana_B.c-d@Mastodon.Social. ")
    Assert.Equal(Some(Handle("ana", "xn--bcher-kva.example")), parseInput false "ana@bücher.example")

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData("@")>]
[<InlineData("@@mastodon.social")>]
[<InlineData("ana@@mastodon.social")>]
[<InlineData("a b@mastodon.social")>]
[<InlineData("-ana@mastodon.social")>]
[<InlineData(".ana@mastodon.social")>]
[<InlineData("an!a@mastodon.social")>]
[<InlineData("@ana@")>]
[<InlineData("ana@localhost")>]
[<InlineData("ana@10.0.0.1")>]
[<InlineData("ana@mastodon.social/about")>]
[<InlineData("ana@https://mastodon.social")>]
[<InlineData("ana@*.social")>]
[<InlineData("http://mastodon.social")>]
[<InlineData("mastodon.social/about")>]
[<InlineData("a@b@c.example")>]
let ``anything else is None`` (text: string) =
    Assert.Equal(None, parseInput false text)

[<Fact>]
let ``a user name of 64 characters is the longest`` () =
    let longest = "a" + String.replicate 63 "b"
    Assert.Equal(Some(Handle(longest, "mastodon.social")), parseInput false $"{longest}@mastodon.social")
    Assert.Equal(None, parseInput false $"{longest}c@mastodon.social")

[<Fact>]
let ``null input is None`` () =
    Assert.Equal(None, parseInput false null)

[<Fact>]
let ``the development setting changes what parseInput accepts`` () =
    Assert.Equal(Some(Server "localhost:3000"), parseInput true "http://localhost:3000")
    Assert.Equal(Some(Handle("ana", "localhost:3000")), parseInput true "ana@localhost:3000")
    Assert.Equal(None, parseInput false "ana@localhost:3000")

// ---------------------------------------------------------------------------
// parseServers, serialise, display
// ---------------------------------------------------------------------------

let private servers (text: string) =
    match parseServers false text with
    | Ok list -> list
    | Error error -> failwith $"'{text}' should parse but gave {error}"

[<Fact>]
let ``commas and any whitespace separate`` () =
    Assert.Equal<string list>(
        [ "mastodon.social"; "gotosocial.example"; "a.io"; "b.io" ],
        servers "mastodon.social,gotosocial.example a.io\n\tb.io"
    )

    Assert.Equal<string list>([ "a.io"; "b.io" ], servers "a.io,\r\n  b.io ,")

[<Fact>]
let ``entries are normalised and duplicates go, keeping the first order`` () =
    Assert.Equal<string list>(
        [ "b.io"; "a.io" ],
        servers "https://B.io @a.io b.io/ A.IO"
    )

[<Fact>]
let ``empty text is an empty list`` () =
    Assert.Equal<string list>([], servers "")
    Assert.Equal<string list>([], servers "  , \n ")
    Assert.Equal(Ok [], parseServers false null)

[<Fact>]
let ``one bad entry fails all and is named`` () =
    Assert.Equal(Error(NotAServer "nope"), parseServers false "a.io nope b.io")
    Assert.Equal(Error(Wildcard "*.io"), parseServers false "a.io *.io nope")
    Assert.Equal(Error(NotAServer "a.io/x"), parseServers false "a.io/x")

[<Fact>]
let ``the development setting applies to every entry`` () =
    Assert.Equal(Ok [ "localhost:3000"; "a.io" ], parseServers true "http://localhost:3000, a.io")
    Assert.Equal(Error(NotAServer "localhost:3000"), parseServers false "a.io localhost:3000")

[<Fact>]
let ``serialise and deserialise round trip`` () =
    let list = [ "a.io"; "xn--bcher-kva.example" ]
    Assert.Equal("a.io xn--bcher-kva.example", serialiseServers list)
    Assert.Equal<string list>(list, deserialiseServers (serialiseServers list))
    Assert.Equal<string list>([], deserialiseServers "")
    Assert.Equal<string list>([ "a.io"; "b.io" ], deserialiseServers "  a.io   b.io ")

[<Fact>]
let ``display joins with a comma and a space`` () =
    Assert.Equal("a.io, b.io", displayServers [ "a.io"; "b.io" ])
    Assert.Equal("", displayServers [])

// ---------------------------------------------------------------------------
// allows, scope
// ---------------------------------------------------------------------------

[<Fact>]
let ``any server is allowed when the list does not apply`` () =
    Assert.True(allows FediverseAccess.Any "mastodon.social")
    Assert.True(allows { OnlyListed = false; Servers = [ "a.io" ] } "mastodon.social")

[<Fact>]
let ``a listed server is allowed and another is not`` () =
    let access = { OnlyListed = true; Servers = [ "a.io"; "b.io" ] }
    Assert.True(allows access "a.io")
    Assert.False(allows access "c.io")
    Assert.False(allows access "sub.a.io")

[<Fact>]
let ``listed with an empty list allows nobody`` () =
    Assert.False(allows { OnlyListed = true; Servers = [] } "mastodon.social")

[<Fact>]
let ``scope values round trip`` () =
    Assert.Equal("any", scopeValue FediverseAccess.Any)
    Assert.Equal("listed", scopeValue { OnlyListed = true; Servers = [] })
    Assert.Equal(Some false, parseScope "any")
    Assert.Equal(Some true, parseScope "listed")
    Assert.Equal(None, parseScope "")
    Assert.Equal(None, parseScope "Any")
    Assert.Equal(None, parseScope "all")

// ---------------------------------------------------------------------------
// isPublicAddress
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("0.0.0.0")>]
[<InlineData("0.1.2.3")>]
[<InlineData("10.0.0.1")>]
[<InlineData("10.255.255.255")>]
[<InlineData("100.64.0.1")>]
[<InlineData("100.127.255.255")>]
[<InlineData("127.0.0.1")>]
[<InlineData("127.255.0.9")>]
[<InlineData("169.254.169.254")>]
[<InlineData("172.16.0.1")>]
[<InlineData("172.31.255.255")>]
[<InlineData("192.0.0.1")>]
[<InlineData("192.0.2.5")>]
[<InlineData("192.168.1.1")>]
[<InlineData("198.18.0.1")>]
[<InlineData("198.19.255.255")>]
[<InlineData("198.51.100.7")>]
[<InlineData("203.0.113.9")>]
[<InlineData("224.0.0.1")>]
[<InlineData("239.255.255.255")>]
[<InlineData("240.0.0.1")>]
[<InlineData("255.255.255.255")>]
[<InlineData("::")>]
[<InlineData("::1")>]
[<InlineData("fe80::1")>]
[<InlineData("febf::1")>]
[<InlineData("fec0::1")>]
[<InlineData("fc00::1")>]
[<InlineData("fd12:3456::1")>]
[<InlineData("ff02::1")>]
[<InlineData("2001:db8::1")>]
[<InlineData("::808:808")>]
[<InlineData("2001:0:4136:e378::1")>]
[<InlineData("::ffff:10.0.0.1")>]
[<InlineData("::ffff:127.0.0.1")>]
[<InlineData("::ffff:169.254.169.254")>]
[<InlineData("64:ff9b::a00:1")>]
[<InlineData("64:ff9b::7f00:1")>]
[<InlineData("2002:a00:1::1")>]
[<InlineData("2002:7f00:1::1")>]
let ``a non-public address is refused`` (text: string) =
    Assert.False(isPublicAddress (IPAddress.Parse text), text)

[<Theory>]
[<InlineData("8.8.8.8")>]
[<InlineData("1.1.1.1")>]
[<InlineData("100.63.255.255")>]
[<InlineData("100.128.0.1")>]
[<InlineData("172.15.0.1")>]
[<InlineData("172.32.0.1")>]
[<InlineData("192.0.1.1")>]
[<InlineData("192.169.0.1")>]
[<InlineData("198.17.0.1")>]
[<InlineData("198.20.0.1")>]
[<InlineData("223.255.255.255")>]
[<InlineData("2606:4700::1111")>]
[<InlineData("2001:4860:4860::8888")>]
[<InlineData("::ffff:8.8.8.8")>]
[<InlineData("64:ff9b::808:808")>]
[<InlineData("2002:808:808::1")>]
let ``a public address is allowed`` (text: string) =
    Assert.True(isPublicAddress (IPAddress.Parse text), text)

// ---------------------------------------------------------------------------
// baseUrl
// ---------------------------------------------------------------------------

[<Fact>]
let ``a server is https`` () =
    Assert.Equal("https://mastodon.social", baseUrl false "mastodon.social")
    Assert.Equal("https://mastodon.social", baseUrl true "mastodon.social")

[<Fact>]
let ``development makes localhost, IP literals and ports http`` () =
    Assert.Equal("http://localhost", baseUrl true "localhost")
    Assert.Equal("http://localhost:3000", baseUrl true "localhost:3000")
    Assert.Equal("http://127.0.0.1", baseUrl true "127.0.0.1")
    Assert.Equal("http://[::1]", baseUrl true "[::1]")
    Assert.Equal("http://[::1]:8080", baseUrl true "[::1]:8080")
    Assert.Equal("http://fake.example:9000", baseUrl true "fake.example:9000")

[<Fact>]
let ``without the development setting a local name is still https`` () =
    Assert.Equal("https://localhost:3000", baseUrl false "localhost:3000")

// ---------------------------------------------------------------------------
// fromMetadata, fromNodeInfo
// ---------------------------------------------------------------------------

let private mastodonMetadata =
    { CodeChallengeMethods = [ "S256" ]
      AuthorizationEndpoint = Some "https://mastodon.social/oauth/authorize"
      TokenEndpoint = Some "https://mastodon.social/oauth/token"
      RegistrationEndpoint = Some "https://mastodon.social/api/v1/apps" }

[<Fact>]
let ``Mastodon-like metadata is supported`` () =
    let expected =
        Some
            { AuthorizeUrl = "https://mastodon.social/oauth/authorize"
              TokenUrl = "https://mastodon.social/oauth/token"
              RegistrationUrl = "https://mastodon.social/api/v1/apps" }

    Assert.Equal(expected, fromMetadata false "mastodon.social" mastodonMetadata)

[<Fact>]
let ``metadata without S256 is refused`` () =
    Assert.Equal(None, fromMetadata false "mastodon.social" { mastodonMetadata with CodeChallengeMethods = [ "plain" ] })
    Assert.Equal(None, fromMetadata false "mastodon.social" { mastodonMetadata with CodeChallengeMethods = [] })

[<Fact>]
let ``metadata with S256 among others is supported`` () =
    Assert.True(
        (fromMetadata false "mastodon.social" { mastodonMetadata with CodeChallengeMethods = [ "plain"; "S256" ] })
            .IsSome
    )

[<Fact>]
let ``a missing endpoint is refused`` () =
    Assert.Equal(None, fromMetadata false "mastodon.social" { mastodonMetadata with RegistrationEndpoint = None })
    Assert.Equal(None, fromMetadata false "mastodon.social" { mastodonMetadata with AuthorizationEndpoint = None })
    Assert.Equal(None, fromMetadata false "mastodon.social" { mastodonMetadata with TokenEndpoint = None })

[<Fact>]
let ``an endpoint on another host is refused`` () =
    Assert.Equal(
        None,
        fromMetadata false "mastodon.social" { mastodonMetadata with TokenEndpoint = Some "https://evil.example/oauth/token" }
    )

    Assert.Equal(
        None,
        fromMetadata
            false
            "mastodon.social"
            { mastodonMetadata with RegistrationEndpoint = Some "https://mastodon.social.evil.example/api/v1/apps" }
    )

    Assert.Equal(
        None,
        fromMetadata
            false
            "mastodon.social"
            { mastodonMetadata with AuthorizationEndpoint = Some "https://mastodon.social:8443/oauth/authorize" }
    )

[<Fact>]
let ``an endpoint with credentials, a relative one or a non-address is refused`` () =
    Assert.Equal(
        None,
        fromMetadata
            false
            "mastodon.social"
            { mastodonMetadata with TokenEndpoint = Some "https://user@mastodon.social/oauth/token" }
    )

    Assert.Equal(None, fromMetadata false "mastodon.social" { mastodonMetadata with TokenEndpoint = Some "/oauth/token" })
    Assert.Equal(None, fromMetadata false "mastodon.social" { mastodonMetadata with TokenEndpoint = Some "not a url" })

[<Fact>]
let ``an http endpoint is refused`` () =
    Assert.Equal(
        None,
        fromMetadata
            false
            "mastodon.social"
            { mastodonMetadata with TokenEndpoint = Some "http://mastodon.social/oauth/token" }
    )

    Assert.Equal(
        None,
        fromMetadata
            false
            "mastodon.social"
            { mastodonMetadata with TokenEndpoint = Some "ftp://mastodon.social/oauth/token" }
    )

[<Fact>]
let ``an endpoint host is compared in lower case`` () =
    let upper =
        { mastodonMetadata with
            AuthorizationEndpoint = Some "https://MASTODON.social/oauth/authorize" }

    Assert.True((fromMetadata false "mastodon.social" upper).IsSome)

[<Fact>]
let ``development admits http on a host with a port`` () =
    let local =
        { CodeChallengeMethods = [ "S256" ]
          AuthorizationEndpoint = Some "http://localhost:3000/oauth/authorize"
          TokenEndpoint = Some "http://localhost:3000/oauth/token"
          RegistrationEndpoint = Some "http://localhost:3000/api/v1/apps" }

    match fromMetadata true "localhost:3000" local with
    | Some endpoints -> Assert.Equal("http://localhost:3000/api/v1/apps", endpoints.RegistrationUrl)
    | None -> failwith "development metadata should be supported"

    Assert.Equal(None, fromMetadata false "localhost:3000" local)
    Assert.Equal(None, fromMetadata true "localhost:4000" local)

[<Fact>]
let ``GoToSocial is supported without metadata`` () =
    let expected =
        Some
            { AuthorizeUrl = "https://social.example/oauth/authorize"
              TokenUrl = "https://social.example/oauth/token"
              RegistrationUrl = "https://social.example/api/v1/apps" }

    Assert.Equal(expected, fromNodeInfo false "social.example" "gotosocial")
    Assert.Equal(expected, fromNodeInfo false "social.example" "GoToSocial")

[<Fact>]
let ``GoToSocial on a development host uses http`` () =
    match fromNodeInfo true "localhost:3000" "gotosocial" with
    | Some endpoints -> Assert.Equal("http://localhost:3000/oauth/token", endpoints.TokenUrl)
    | None -> failwith "should be supported"

[<Theory>]
[<InlineData("akkoma")>]
[<InlineData("pleroma")>]
[<InlineData("misskey")>]
[<InlineData("sharkey")>]
[<InlineData("mastodon")>]
[<InlineData("")>]
[<InlineData("something-new")>]
let ``other software is not supported by NodeInfo`` (software: string) =
    Assert.Equal(None, fromNodeInfo false "social.example" software)

// ---------------------------------------------------------------------------
// subject and profile
// ---------------------------------------------------------------------------

[<Fact>]
let ``the subject round trips`` () =
    let subject = fediverseSubject "mastodon.social" "109287"
    Assert.Equal("mastodon.social#109287", subject)
    Assert.Equal(Some("mastodon.social", "109287"), parseFediverseSubject subject)

[<Fact>]
let ``an account id containing a hash survives`` () =
    let subject = fediverseSubject "a.io" "x#y#z"
    Assert.Equal(Some("a.io", "x#y#z"), parseFediverseSubject subject)

[<Theory>]
[<InlineData("")>]
[<InlineData("nohash")>]
[<InlineData("#123")>]
[<InlineData("host#")>]
let ``a malformed subject is None`` (subject: string) =
    Assert.Equal(None, parseFediverseSubject subject)

[<Fact>]
let ``the preferred username is user at server`` () =
    Assert.Equal("ana@mastodon.social", preferredUsername "ana" "mastodon.social")
