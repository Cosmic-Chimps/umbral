/// The pure rules of the Bluesky connector, branch by branch (specs/015). No
/// network, no database: handles, DIDs, the DID document, the server metadata,
/// the token answer, the profile record and the client ID.
module Umbral.Tests.BlueskyDomainTests

open System
open System.Text.Json
open Xunit
open Umbral.Domains.Bluesky

let private plc = "did:plc:ewvi7nxzyoun6zhxrhs64oiz"
let private pds = "https://pds.example.net"

// ---------------------------------------------------------------------------
// Handles
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("alice.bsky.social", "alice.bsky.social")>]
[<InlineData("@Alice.BSKY.social.", "alice.bsky.social")>]
[<InlineData("  @alice.bsky.social  ", "alice.bsky.social")>]
[<InlineData("a-b.c-d.io", "a-b.c-d.io")>]
[<InlineData("example.com", "example.com")>]
[<InlineData("a.b", "a.b")>]
let ``normaliseHandle gives the one spelling`` (text: string, expected: string) =
    Assert.Equal(Some expected, normaliseHandle false text)
    Assert.True(isHandle false text)

[<Fact>]
let ``a label of 63 characters is accepted and one of 64 is refused`` () =
    let l63 = String('a', 63)
    let l64 = String('a', 64)
    Assert.Equal(Some $"{l63}.com", normaliseHandle false $"{l63}.com")
    Assert.Equal(None, normaliseHandle false $"{l64}.com")
    Assert.Equal(None, normaliseHandle false $"alice.{l64}")

[<Fact>]
let ``253 characters are accepted and 254 are refused`` () =
    let l = String('a', 63)
    let ok = $"{l}.{l}.{l}.{String('b', 61)}"
    Assert.Equal(253, ok.Length)
    Assert.True(isHandle false ok)
    Assert.False(isHandle false (ok + "b"))

[<Theory>]
[<InlineData("alice")>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData("@")>]
[<InlineData("-alice.com")>]
[<InlineData("alice-.com")>]
[<InlineData("alice.-com")>]
[<InlineData("alice..com")>]
[<InlineData(".alice.com")>]
[<InlineData("alice.com..")>]
[<InlineData("alice.123")>]
[<InlineData("alice.1com")>]
[<InlineData("al ice.com")>]
[<InlineData("alice.com/x")>]
[<InlineData("alice_b.com")>]
[<InlineData("alice@bsky.social")>]
[<InlineData("https://alice.com")>]
[<InlineData("alice.com:8080")>]
[<InlineData("@@alice.com")>]
let ``a malformed handle is refused`` (text: string) =
    Assert.Equal(None, normaliseHandle false text)
    Assert.False(isHandle true text)

[<Theory>]
[<InlineData("alice.bsky.sociál")>]
[<InlineData("аlice.com")>] // a Cyrillic а
[<InlineData("alice.bsky.社会")>]
[<InlineData("alice.Kom")>] // the Kelvin sign lower-cases to k
let ``a handle that is not ASCII is refused`` (text: string) =
    Assert.Equal(None, normaliseHandle true text)

[<Fact>]
let ``null is not a handle`` () =
    Assert.Equal(None, normaliseHandle false null)
    Assert.Equal(None, parseInput false null)

[<Theory>]
[<InlineData("alice.alt")>]
[<InlineData("alice.arpa")>]
[<InlineData("alice.example")>]
[<InlineData("alice.internal")>]
[<InlineData("alice.invalid")>]
[<InlineData("alice.local")>]
[<InlineData("alice.localhost")>]
[<InlineData("alice.onion")>]
[<InlineData("alice.ONION")>]
let ``a reserved top-level label is refused, even in development`` (text: string) =
    Assert.Equal(None, normaliseHandle false text)
    Assert.Equal(None, normaliseHandle true text)

[<Fact>]
let ``test is refused unless the development setting is on`` () =
    Assert.Equal(None, normaliseHandle false "alice.test")
    Assert.Equal(Some "alice.test", normaliseHandle true "Alice.TEST")
    Assert.Equal(None, parseInput false "alice.test")
    Assert.Equal(Some(Handle "alice.test"), parseInput true "alice.test")

[<Fact>]
let ``a label that merely contains a reserved word is not refused`` () =
    Assert.True(isHandle false "alice.localhost.com")
    Assert.True(isHandle false "alice.example.org")

// ---------------------------------------------------------------------------
// DIDs
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("did:plc:ewvi7nxzyoun6zhxrhs64oiz")>]
[<InlineData("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa")>]
[<InlineData("did:plc:222222222222222222222222")>]
[<InlineData("did:web:example.com")>]
[<InlineData("did:web:pds.alice-b.example.com")>]
let ``a did:plc or a did:web host is accepted`` (did: string) = Assert.Equal(Some did, parseDid did)

[<Theory>]
[<InlineData("did:plc:ewvi7nxzyoun6zhxrhs64oi")>] // 23
[<InlineData("did:plc:ewvi7nxzyoun6zhxrhs64oizz")>] // 25
[<InlineData("did:plc:ewvi7nxzyoun6zhxrhs64oi1")>] // 1 is not base32
[<InlineData("did:plc:ewvi7nxzyoun6zhxrhs64oi8")>] // 8 is not base32
[<InlineData("did:plc:EWVI7NXZYOUN6ZHXRHS64OIZ")>] // upper case
[<InlineData("did:plc:")>]
[<InlineData("DID:plc:ewvi7nxzyoun6zhxrhs64oiz")>]
[<InlineData("did:web:")>]
[<InlineData("did:web:localhost")>]
[<InlineData("did:web:example.com:8080")>]
[<InlineData("did:web:example.com%3A8080")>]
[<InlineData("did:web:example.com:user:alice")>]
[<InlineData("did:web:example.com/path")>]
[<InlineData("did:web:Example.com")>]
[<InlineData("did:web:127.0.0.1")>]
[<InlineData("did:key:z6Mkabc")>]
[<InlineData("did:ethr:0x1234")>]
[<InlineData("alice.bsky.social")>]
[<InlineData("")>]
let ``any other DID is refused`` (did: string) = Assert.Equal(None, parseDid did)

[<Fact>]
let ``a null DID is refused`` () =
    Assert.Equal(None, parseDid null)
    Assert.Equal(None, didWebHost null)

[<Fact>]
let ``didWebHost gives the host of a did:web only`` () =
    Assert.Equal(Some "alice.example.com", didWebHost "did:web:alice.example.com")
    Assert.Equal(None, didWebHost plc)
    Assert.Equal(None, didWebHost "did:web:localhost")

// ---------------------------------------------------------------------------
// parseInput
// ---------------------------------------------------------------------------

[<Fact>]
let ``parseInput reads a handle or a DID`` () =
    Assert.Equal(Some(Handle "alice.bsky.social"), parseInput false "@Alice.BSKY.social.")
    Assert.Equal(Some(Did plc), parseInput false plc)
    Assert.Equal(Some(Did plc), parseInput false $"  @{plc}  ")
    Assert.Equal(Some(Did "did:web:example.com"), parseInput false "did:web:example.com")

[<Fact>]
let ``a DID keeps its case and is never lower-cased`` () =
    // An upper-case DID is not a DID, and it is not a handle either.
    Assert.Equal(None, parseInput false (plc.ToUpperInvariant()))
    Assert.Equal(None, parseInput false "did:plc:abc")
    Assert.Equal(None, parseInput false "did:web:localhost")

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData("alice")>]
[<InlineData("did:key:z6Mk")>]
[<InlineData("alice@example.com")>]
let ``parseInput refuses what is neither`` (text: string) = Assert.Equal(None, parseInput false text)

// ---------------------------------------------------------------------------
// TXT
// ---------------------------------------------------------------------------

[<Fact>]
let ``one did= value gives the DID`` () =
    Assert.Equal(Some plc, atprotoDidOfTxt [| $"did={plc}" |])

[<Fact>]
let ``quotes and spaces around the value are dropped`` () =
    Assert.Equal(Some plc, atprotoDidOfTxt [| $"\"did={plc}\"" |])
    Assert.Equal(Some plc, atprotoDidOfTxt [| $"  \"did={plc}\"  " |])

[<Fact>]
let ``other records are ignored`` () =
    Assert.Equal(Some plc, atprotoDidOfTxt [| "v=spf1 -all"; $"did={plc}"; "" |])

[<Fact>]
let ``no did= value is None`` () =
    Assert.Equal(None, atprotoDidOfTxt [||])
    Assert.Equal(None, atprotoDidOfTxt [| "v=spf1 -all" |])
    Assert.Equal(None, atprotoDidOfTxt [| plc |])

[<Fact>]
let ``two did= values is None, even the same one twice`` () =
    Assert.Equal(None, atprotoDidOfTxt [| $"did={plc}"; "did=did:plc:aaaaaaaaaaaaaaaaaaaaaaaa" |])
    Assert.Equal(None, atprotoDidOfTxt [| $"did={plc}"; $"did={plc}" |])

[<Fact>]
let ``a did= value that is not a DID is None`` () =
    Assert.Equal(None, atprotoDidOfTxt [| "did=did:key:z6Mk" |])
    Assert.Equal(None, atprotoDidOfTxt [| "did=" |])
    Assert.Equal(None, atprotoDidOfTxt [| "did=did:web:localhost" |])

// ---------------------------------------------------------------------------
// The DID document
// ---------------------------------------------------------------------------

let private document (service: string) =
    $"""{{ "id": "{plc}", "alsoKnownAs": ["at://alice.bsky.social"], "service": [ {service} ] }}"""

let private pdsService (id: string) (kind: string) (endpoint: string) =
    $"""{{ "id": "{id}", "type": "{kind}", "serviceEndpoint": "{endpoint}" }}"""

let private parseDoc (allowDev: bool) (json: string) = parseDidDocument allowDev plc json

let private docError (allowDev: bool) (json: string) =
    match parseDoc allowDev json with
    | Error word -> word
    | Ok doc -> failwith $"should be refused but gave {doc}"

[<Fact>]
let ``a document gives its id, names and PDS`` () =
    let json = document (pdsService "#atproto_pds" "AtprotoPersonalDataServer" "https://pds.example.net")

    match parseDoc false json with
    | Ok doc ->
        Assert.Equal(plc, doc.Id)
        Assert.Equal<string list>([ "at://alice.bsky.social" ], doc.AlsoKnownAs)
        Assert.Equal("https://pds.example.net", doc.Pds)
    | Error word -> failwith word

[<Fact>]
let ``the PDS id may be the DID plus the fragment`` () =
    let json = document (pdsService $"{plc}#atproto_pds" "AtprotoPersonalDataServer" pds)
    Assert.Equal(Ok pds, parseDoc false json |> Result.map _.Pds)

[<Fact>]
let ``another service id is not the PDS`` () =
    let json = document (pdsService "#other" "AtprotoPersonalDataServer" pds)
    Assert.Equal("no_pds", docError false json)
    let other = document (pdsService "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa#atproto_pds" "AtprotoPersonalDataServer" pds)
    Assert.Equal("no_pds", docError false other)

[<Fact>]
let ``the wrong type is not the PDS`` () =
    let json = document (pdsService "#atproto_pds" "SomethingElse" pds)
    Assert.Equal("no_pds", docError false json)

[<Fact>]
let ``the PDS is found among other services`` () =
    let json =
        document (
            pdsService "#bsky_notif" "BskyNotificationService" "https://notify.example.net"
            + ","
            + pdsService "#atproto_pds" "AtprotoPersonalDataServer" pds
        )

    Assert.Equal(Ok pds, parseDoc false json |> Result.map _.Pds)

[<Fact>]
let ``no service at all is no_pds`` () =
    Assert.Equal("no_pds", docError false $"""{{ "id": "{plc}" }}""")
    Assert.Equal("no_pds", docError false $"""{{ "id": "{plc}", "service": "x" }}""")
    Assert.Equal("no_pds", docError false $"""{{ "id": "{plc}", "service": [] }}""")

[<Theory>]
[<InlineData("https://pds.example.net/", "https://pds.example.net")>]
[<InlineData("https://PDS.Example.net", "https://pds.example.net")>]
[<InlineData("https://pds.example.net:443", "https://pds.example.net")>]
let ``the PDS is returned as an origin, without a trailing slash`` (endpoint: string, expected: string) =
    let json = document (pdsService "#atproto_pds" "AtprotoPersonalDataServer" endpoint)
    Assert.Equal(Ok expected, parseDoc false json |> Result.map _.Pds)

[<Theory>]
[<InlineData("https://pds.example.net/xrpc")>]
[<InlineData("https://pds.example.net//")>]
[<InlineData("https://pds.example.net/?a=b")>]
[<InlineData("https://pds.example.net?a=b")>]
[<InlineData("https://pds.example.net/#x")>]
[<InlineData("https://pds.example.net#x")>]
[<InlineData("https://user@pds.example.net")>]
[<InlineData("https://user:pass@pds.example.net")>]
[<InlineData("https://")>]
[<InlineData("pds.example.net")>]
[<InlineData("ftp://pds.example.net")>]
[<InlineData("")>]
let ``a PDS that is not an origin is no_pds`` (endpoint: string) =
    let json = document (pdsService "#atproto_pds" "AtprotoPersonalDataServer" endpoint)
    Assert.Equal("no_pds", docError true json)

[<Theory>]
[<InlineData("https://pds.example.net:8443")>]
[<InlineData("https://pds.example.net:444")>]
[<InlineData("https://127.0.0.1:8443")>]
let ``a PDS on a port other than the default is no_pds in production`` (endpoint: string) =
    let json = document (pdsService "#atproto_pds" "AtprotoPersonalDataServer" endpoint)
    Assert.Equal("no_pds", docError false json)

[<Fact>]
let ``a PDS on another port is accepted with the development setting`` () =
    let json = document (pdsService "#atproto_pds" "AtprotoPersonalDataServer" "https://pds.example.net:8443")
    Assert.Equal(Ok "https://pds.example.net:8443", parseDoc true json |> Result.map _.Pds)

[<Fact>]
let ``http is the PDS only with the development setting`` () =
    let json = document (pdsService "#atproto_pds" "AtprotoPersonalDataServer" "http://127.0.0.1:2583")
    Assert.Equal("no_pds", docError false json)
    Assert.Equal(Ok "http://127.0.0.1:2583", parseDoc true json |> Result.map _.Pds)

[<Fact>]
let ``a serviceEndpoint that is not a string is no_pds`` () =
    let json =
        document """{ "id": "#atproto_pds", "type": "AtprotoPersonalDataServer", "serviceEndpoint": 5 }"""

    Assert.Equal("no_pds", docError false json)

[<Fact>]
let ``an id that is not the DID asked for is id_mismatch`` () =
    let json =
        """{ "id": "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa", "service": [ { "id": "#atproto_pds", "type": "AtprotoPersonalDataServer", "serviceEndpoint": "https://pds.example.net" } ] }"""

    Assert.Equal("id_mismatch", docError false json)
    Assert.Equal("id_mismatch", docError false """{ "service": [] }""")
    Assert.Equal("id_mismatch", docError false """{ "id": 5 }""")

[<Fact>]
let ``the id is compared exactly`` () =
    let json = document (pdsService "#atproto_pds" "AtprotoPersonalDataServer" pds)
    Assert.Equal("id_mismatch", docError false (json.Replace(plc, plc.ToUpperInvariant())))

[<Theory>]
[<InlineData("")>]
[<InlineData("not json")>]
[<InlineData("{")>]
[<InlineData("[]")>]
[<InlineData("\"did\"")>]
[<InlineData("null")>]
let ``malformed JSON is malformed`` (json: string) = Assert.Equal("malformed", docError false json)

[<Fact>]
let ``a null document is malformed`` () =
    Assert.Equal(Error "malformed", parseDidDocument false plc null)

[<Fact>]
let ``alsoKnownAs is optional and non-strings are ignored`` () =
    let service = pdsService "#atproto_pds" "AtprotoPersonalDataServer" pds

    let without = $"""{{ "id": "{plc}", "service": [ {service} ] }}"""
    Assert.Equal(Ok [], parseDoc false without |> Result.map _.AlsoKnownAs)

    let mixed =
        $"""{{ "id": "{plc}", "alsoKnownAs": [5, null, "at://alice.bsky.social", {{ "a": 1 }}, "at://b.bsky.social"], "service": [ {service} ] }}"""

    Assert.Equal(
        Ok [ "at://alice.bsky.social"; "at://b.bsky.social" ],
        parseDoc false mixed |> Result.map _.AlsoKnownAs
    )

    let notAList = $"""{{ "id": "{plc}", "alsoKnownAs": "at://alice.bsky.social", "service": [ {service} ] }}"""
    Assert.Equal(Ok [], parseDoc false notAList |> Result.map _.AlsoKnownAs)

// ---------------------------------------------------------------------------
// The handle in the document
// ---------------------------------------------------------------------------

let private docWith (names: string list) : DidDocument =
    { Id = plc
      AlsoKnownAs = names
      Pds = pds }

[<Fact>]
let ``claimsHandle needs at:// and the handle, without regard to case`` () =
    let doc = docWith [ "at://Alice.Bsky.Social" ]
    Assert.True(claimsHandle doc "alice.bsky.social")
    Assert.True(claimsHandle (docWith [ "at://alice.bsky.social" ]) "alice.bsky.social")

[<Fact>]
let ``claimsHandle is false for another handle, another scheme or no names`` () =
    Assert.False(claimsHandle (docWith [ "at://bob.bsky.social" ]) "alice.bsky.social")
    Assert.False(claimsHandle (docWith [ "https://alice.bsky.social" ]) "alice.bsky.social")
    Assert.False(claimsHandle (docWith [ "alice.bsky.social" ]) "alice.bsky.social")
    Assert.False(claimsHandle (docWith [ "at://alice.bsky.social.evil.com" ]) "alice.bsky.social")
    Assert.False(claimsHandle (docWith []) "alice.bsky.social")

[<Fact>]
let ``handleOf is the first at:// entry that is a valid handle, normalised`` () =
    Assert.Equal(Some "alice.bsky.social", handleOf (docWith [ "at://Alice.Bsky.Social" ]))

    Assert.Equal(
        Some "bob.bsky.social",
        handleOf (docWith [ "https://x.example.com"; "at://not a handle"; "at://bob.bsky.social"; "at://c.bsky.social" ])
    )

[<Fact>]
let ``handleOf is None when no entry is a handle`` () =
    Assert.Equal(None, handleOf (docWith []))
    Assert.Equal(None, handleOf (docWith [ "https://alice.bsky.social" ]))
    Assert.Equal(None, handleOf (docWith [ "at://alice" ]))
    Assert.Equal(None, handleOf (docWith [ "at://alice.onion" ]))
    Assert.Equal(None, handleOf (docWith [ "at://did:plc:ewvi7nxzyoun6zhxrhs64oiz" ]))

// ---------------------------------------------------------------------------
// The protected resource
// ---------------------------------------------------------------------------

[<Fact>]
let ``exactly one authorization server gives its origin`` () =
    Assert.Equal(
        Some "https://bsky.social",
        parseProtectedResource false """{ "authorization_servers": ["https://bsky.social"] }"""
    )

    Assert.Equal(
        Some "https://bsky.social",
        parseProtectedResource false """{ "resource": "https://pds", "authorization_servers": ["https://Bsky.Social/"] }"""
    )

[<Fact>]
let ``none or two authorization servers is None`` () =
    Assert.Equal(None, parseProtectedResource false """{ "authorization_servers": [] }""")
    Assert.Equal(None, parseProtectedResource false """{}""")

    Assert.Equal(
        None,
        parseProtectedResource false """{ "authorization_servers": ["https://a.example.com", "https://b.example.com"] }"""
    )

    // The same one twice is still two entries.
    Assert.Equal(
        None,
        parseProtectedResource false """{ "authorization_servers": ["https://a.example.com", "https://a.example.com"] }"""
    )

[<Theory>]
[<InlineData("""{ "authorization_servers": ["https://a.example.com/path"] }""")>]
[<InlineData("""{ "authorization_servers": ["https://a.example.com?x=1"] }""")>]
[<InlineData("""{ "authorization_servers": ["https://u@a.example.com"] }""")>]
[<InlineData("""{ "authorization_servers": ["http://a.example.com"] }""")>]
[<InlineData("""{ "authorization_servers": [5] }""")>]
[<InlineData("""{ "authorization_servers": "https://a.example.com" }""")>]
[<InlineData("""[]""")>]
[<InlineData("not json")>]
[<InlineData("")>]
let ``an authorization server that is not an https origin is None`` (json: string) =
    Assert.Equal(None, parseProtectedResource false json)

[<Fact>]
let ``http is the authorization server only with the development setting`` () =
    let json = """{ "authorization_servers": ["http://127.0.0.1:2583"] }"""
    Assert.Equal(None, parseProtectedResource false json)
    Assert.Equal(Some "http://127.0.0.1:2583", parseProtectedResource true json)

[<Fact>]
let ``a null protected resource is None`` () = Assert.Equal(None, parseProtectedResource false null)

// ---------------------------------------------------------------------------
// The server metadata
// ---------------------------------------------------------------------------

let private issuer = "https://bsky.social"

/// A valid metadata document, with the fields a test wants to change.
let private metadata (overrides: (string * string) list) =
    let baseline =
        [ "issuer", "\"https://bsky.social\""
          "require_pushed_authorization_requests", "true"
          "pushed_authorization_request_endpoint", "\"https://bsky.social/oauth/par\""
          "authorization_endpoint", "\"https://bsky.social/oauth/authorize\""
          "token_endpoint", "\"https://bsky.social/oauth/token\""
          "code_challenge_methods_supported", "[\"S256\"]"
          "dpop_signing_alg_values_supported", "[\"RS256\", \"ES256\"]"
          "authorization_response_iss_parameter_supported", "true"
          "scopes_supported", "[\"atproto\", \"transition:email\"]"
          "client_id_metadata_document_supported", "true"
          "token_endpoint_auth_methods_supported", "[\"none\", \"private_key_jwt\"]" ]

    let kept = baseline |> List.filter (fun (key, _) -> not (List.exists (fun (k, _) -> k = key) overrides))

    let fields =
        (kept @ (overrides |> List.filter (fun (_, v) -> v <> "<absent>")))
        |> List.map (fun (key, value) -> $"\"{key}\": {value}")

    "{ " + String.concat ", " fields + " }"

let private metadataResult (allowDev: bool) (overrides: (string * string) list) =
    parseServerMetadata allowDev issuer (metadata overrides)

let private metadataError (overrides: (string * string) list) =
    match metadataResult false overrides with
    | Error word -> word
    | Ok m -> failwith $"should be refused but gave {m}"

[<Fact>]
let ``valid metadata gives the issuer and the three endpoints`` () =
    match metadataResult false [] with
    | Ok m ->
        Assert.Equal("https://bsky.social", m.Issuer)
        Assert.Equal("https://bsky.social/oauth/par", m.Par)
        Assert.Equal("https://bsky.social/oauth/authorize", m.Authorize)
        Assert.Equal("https://bsky.social/oauth/token", m.Token)
    | Error word -> failwith word

[<Fact>]
let ``an endpoint on another host is endpoint_invalid`` () =
    Assert.Equal("endpoint_invalid", metadataError [ "token_endpoint", "\"https://entryway.example.net/oauth/token\"" ])
    Assert.Equal("endpoint_invalid", metadataError [ "pushed_authorization_request_endpoint", "\"https://other.example.net/oauth/par\"" ])
    Assert.Equal("endpoint_invalid", metadataError [ "authorization_endpoint", "\"https://other.example.net/oauth/authorize\"" ])
    // A look-alike: the issuer's name as a prefix of another host.
    Assert.Equal("endpoint_invalid", metadataError [ "token_endpoint", "\"https://bsky.social.evil.test/oauth/token\"" ])

[<Fact>]
let ``an endpoint on another port or scheme is endpoint_invalid`` () =
    Assert.Equal("endpoint_invalid", metadataError [ "token_endpoint", "\"https://bsky.social:8443/oauth/token\"" ])
    Assert.Equal("endpoint_invalid", metadataError [ "pushed_authorization_request_endpoint", "\"https://bsky.social:8443/oauth/par\"" ])
    Assert.Equal("endpoint_invalid", metadataError [ "authorization_endpoint", "\"http://bsky.social/oauth/authorize\"" ])

[<Fact>]
let ``an endpoint on the issuer's own origin is accepted, whatever its path, default port or case`` () =
    match
        metadataResult
            false
            [ "token_endpoint", "\"https://BSKY.social:443/some/other/token\""
              "authorization_endpoint", "\"https://bsky.social/a/b?c=d\"" ]
    with
    | Ok m ->
        Assert.Equal("https://BSKY.social:443/some/other/token", m.Token)
        Assert.Equal("https://bsky.social/a/b?c=d", m.Authorize)
    | Error word -> failwith word

[<Fact>]
let ``under the development setting the endpoints must still share the issuer's port`` () =
    let dev = "http://127.0.0.1:2583"

    let ok =
        parseServerMetadata
            true
            dev
            (metadata
                [ "issuer", $"\"{dev}\""
                  "pushed_authorization_request_endpoint", $"\"{dev}/oauth/par\""
                  "authorization_endpoint", $"\"{dev}/oauth/authorize\""
                  "token_endpoint", $"\"{dev}/oauth/token\"" ])

    Assert.True(Result.isOk ok)

    let other =
        parseServerMetadata
            true
            dev
            (metadata
                [ "issuer", $"\"{dev}\""
                  "pushed_authorization_request_endpoint", $"\"{dev}/oauth/par\""
                  "authorization_endpoint", $"\"{dev}/oauth/authorize\""
                  "token_endpoint", "\"http://127.0.0.1:9999/oauth/token\"" ])

    Assert.Equal(Error "endpoint_invalid", other)

[<Theory>]
[<InlineData("https://as.example.net:8443")>]
[<InlineData("https://as.example.net:444")>]
let ``an authorization server on a port other than the default is refused in production`` (server: string) =
    Assert.Equal(None, parseProtectedResource false $"""{{ "authorization_servers": ["{server}"] }}""")
    Assert.Equal(Some server, parseProtectedResource true $"""{{ "authorization_servers": ["{server}"] }}""")

[<Fact>]
let ``an authorization server on the default port written out is the plain origin`` () =
    Assert.Equal(
        Some "https://as.example.net",
        parseProtectedResource false """{ "authorization_servers": ["https://as.example.net:443"] }"""
    )

[<Fact>]
let ``an issuer that is not the origin is issuer_mismatch`` () =
    Assert.Equal("issuer_mismatch", metadataError [ "issuer", "\"https://other.example.com\"" ])
    Assert.Equal("issuer_mismatch", metadataError [ "issuer", "\"https://bsky.social/\"" ])
    Assert.Equal("issuer_mismatch", metadataError [ "issuer", "\"https://BSKY.social\"" ])
    Assert.Equal("issuer_mismatch", metadataError [ "issuer", "<absent>" ])
    Assert.Equal("issuer_mismatch", metadataError [ "issuer", "5" ])

[<Fact>]
let ``the issuer is checked before anything else`` () =
    Assert.Equal(
        "issuer_mismatch",
        metadataError [ "issuer", "\"https://other.example.com\""; "scopes_supported", "[]" ]
    )

[<Fact>]
let ``par must be required, with an endpoint`` () =
    Assert.Equal("par_required", metadataError [ "require_pushed_authorization_requests", "false" ])
    Assert.Equal("par_required", metadataError [ "require_pushed_authorization_requests", "<absent>" ])
    Assert.Equal("par_required", metadataError [ "require_pushed_authorization_requests", "\"true\"" ])
    Assert.Equal("par_required", metadataError [ "pushed_authorization_request_endpoint", "<absent>" ])
    // A PAR endpoint that is not a string is no endpoint at all.
    Assert.Equal("par_required", metadataError [ "pushed_authorization_request_endpoint", "5" ])

[<Fact>]
let ``S256 must be among the code challenge methods`` () =
    Assert.Equal("pkce_s256", metadataError [ "code_challenge_methods_supported", "[\"plain\"]" ])
    Assert.Equal("pkce_s256", metadataError [ "code_challenge_methods_supported", "[]" ])
    Assert.Equal("pkce_s256", metadataError [ "code_challenge_methods_supported", "<absent>" ])
    Assert.Equal("pkce_s256", metadataError [ "code_challenge_methods_supported", "\"S256\"" ])
    Assert.Equal("pkce_s256", metadataError [ "code_challenge_methods_supported", "[\"s256\"]" ])

[<Fact>]
let ``ES256 must be among the DPoP algorithms`` () =
    Assert.Equal("dpop_es256", metadataError [ "dpop_signing_alg_values_supported", "[\"RS256\"]" ])
    Assert.Equal("dpop_es256", metadataError [ "dpop_signing_alg_values_supported", "<absent>" ])

[<Fact>]
let ``the iss parameter must be supported`` () =
    Assert.Equal("iss_parameter", metadataError [ "authorization_response_iss_parameter_supported", "false" ])
    Assert.Equal("iss_parameter", metadataError [ "authorization_response_iss_parameter_supported", "<absent>" ])

[<Fact>]
let ``atproto must be among the scopes`` () =
    Assert.Equal("scope_atproto", metadataError [ "scopes_supported", "[\"openid\"]" ])
    Assert.Equal("scope_atproto", metadataError [ "scopes_supported", "<absent>" ])

[<Fact>]
let ``client metadata documents must be supported`` () =
    Assert.Equal("client_metadata", metadataError [ "client_id_metadata_document_supported", "false" ])
    Assert.Equal("client_metadata", metadataError [ "client_id_metadata_document_supported", "<absent>" ])

[<Fact>]
let ``none must be among the token endpoint auth methods`` () =
    Assert.Equal("auth_method_none", metadataError [ "token_endpoint_auth_methods_supported", "[\"private_key_jwt\"]" ])
    Assert.Equal("auth_method_none", metadataError [ "token_endpoint_auth_methods_supported", "<absent>" ])

[<Theory>]
[<InlineData("pushed_authorization_request_endpoint", "\"http://bsky.social/oauth/par\"")>]
[<InlineData("pushed_authorization_request_endpoint", "\"/oauth/par\"")>]
[<InlineData("authorization_endpoint", "\"http://bsky.social/oauth/authorize\"")>]
[<InlineData("authorization_endpoint", "\"/oauth/authorize\"")>]
[<InlineData("authorization_endpoint", "<absent>")>]
[<InlineData("authorization_endpoint", "\"https://u@bsky.social/oauth/authorize\"")>]
[<InlineData("token_endpoint", "\"http://bsky.social/oauth/token\"")>]
[<InlineData("token_endpoint", "\"ftp://bsky.social/oauth/token\"")>]
[<InlineData("token_endpoint", "<absent>")>]
[<InlineData("token_endpoint", "\"https://bsky.social/oauth/token#x\"")>]
let ``an endpoint that is not an absolute https URL is endpoint_invalid`` (key: string, value: string) =
    Assert.Equal("endpoint_invalid", metadataError [ key, value ])

[<Fact>]
let ``an endpoint of 2048 characters is accepted and one of 2049 is not`` () =
    let prefix = "https://bsky.social/oauth/token?x="
    let ok = prefix + String('a', 2048 - prefix.Length)
    let tooLong = ok + "a"
    Assert.Equal(2048, ok.Length)
    Assert.True((metadataResult false [ "token_endpoint", $"\"{ok}\"" ]) |> Result.isOk)
    Assert.Equal("endpoint_invalid", metadataError [ "token_endpoint", $"\"{tooLong}\"" ])

[<Fact>]
let ``http endpoints are accepted only with the development setting`` () =
    let dev = "http://127.0.0.1:2583"

    let overrides =
        [ "issuer", $"\"{dev}\""
          "pushed_authorization_request_endpoint", $"\"{dev}/oauth/par\""
          "authorization_endpoint", $"\"{dev}/oauth/authorize\""
          "token_endpoint", $"\"{dev}/oauth/token\"" ]

    Assert.Equal(Error "endpoint_invalid", parseServerMetadata false dev (metadata overrides))
    Assert.True(parseServerMetadata true dev (metadata overrides) |> Result.isOk)

[<Fact>]
let ``malformed metadata is malformed`` () =
    Assert.Equal(Error "malformed", parseServerMetadata false issuer "")
    Assert.Equal(Error "malformed", parseServerMetadata false issuer "[]")
    Assert.Equal(Error "malformed", parseServerMetadata false issuer null)
    Assert.Equal(Error "malformed", parseServerMetadata false issuer "{")

// ---------------------------------------------------------------------------
// The token answer
// ---------------------------------------------------------------------------

let private answer (sub: string) (scope: string) (tokenType: string) : TokenAnswer =
    { Sub = sub
      Scope = scope
      TokenType = tokenType }

[<Fact>]
let ``parseTokenAnswer reads the three fields and the rest is ignored`` () =
    let json =
        $"""{{ "access_token": "x", "token_type": "DPoP", "scope": "atproto", "sub": "{plc}", "expires_in": 60 }}"""

    Assert.Equal(Some(answer plc "atproto" "DPoP"), parseTokenAnswer json)

[<Fact>]
let ``a missing or non-string field is empty`` () =
    Assert.Equal(Some(answer "" "" ""), parseTokenAnswer "{}")
    Assert.Equal(Some(answer "" "" ""), parseTokenAnswer """{ "sub": 5, "scope": null, "token_type": [] }""")

[<Theory>]
[<InlineData("")>]
[<InlineData("not json")>]
[<InlineData("[]")>]
[<InlineData("\"x\"")>]
let ``an answer that is not an object is None`` (json: string) = Assert.Equal(None, parseTokenAnswer json)

[<Fact>]
let ``a null answer is None`` () = Assert.Equal(None, parseTokenAnswer null)

[<Fact>]
let ``a good answer gives the DID`` () =
    Assert.Equal(Ok plc, checkTokenAnswer plc (answer plc "atproto" "DPoP"))

[<Fact>]
let ``dpop in any case is accepted`` () =
    Assert.Equal(Ok plc, checkTokenAnswer plc (answer plc "atproto" "dpop"))
    Assert.Equal(Ok plc, checkTokenAnswer plc (answer plc "atproto" "DPOP"))

[<Fact>]
let ``the scope is read as space-separated words`` () =
    Assert.Equal(Ok plc, checkTokenAnswer plc (answer plc "transition:email atproto" "DPoP"))
    Assert.Equal(Ok plc, checkTokenAnswer plc (answer plc "  atproto   transition:generic " "DPoP"))

[<Fact>]
let ``another token type is token_invalid`` () =
    Assert.Equal(Error "token_invalid", checkTokenAnswer plc (answer plc "atproto" "Bearer"))
    Assert.Equal(Error "token_invalid", checkTokenAnswer plc (answer plc "atproto" ""))

[<Fact>]
let ``a scope without atproto is token_invalid`` () =
    Assert.Equal(Error "token_invalid", checkTokenAnswer plc (answer plc "transition:email" "DPoP"))
    Assert.Equal(Error "token_invalid", checkTokenAnswer plc (answer plc "" "DPoP"))
    Assert.Equal(Error "token_invalid", checkTokenAnswer plc (answer plc "atprotox" "DPoP"))
    Assert.Equal(Error "token_invalid", checkTokenAnswer plc (answer plc "ATPROTO" "DPoP"))

[<Fact>]
let ``a sub that is not a DID is token_invalid`` () =
    Assert.Equal(Error "token_invalid", checkTokenAnswer plc (answer "" "atproto" "DPoP"))
    Assert.Equal(Error "token_invalid", checkTokenAnswer plc (answer "alice.bsky.social" "atproto" "DPoP"))
    Assert.Equal(Error "token_invalid", checkTokenAnswer plc (answer "did:key:z6Mk" "atproto" "DPoP"))

[<Fact>]
let ``a sub that is another DID is did_mismatch`` () =
    Assert.Equal(
        Error "did_mismatch",
        checkTokenAnswer plc (answer "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa" "atproto" "DPoP")
    )

[<Fact>]
let ``the checks run in order, token_invalid before did_mismatch`` () =
    let other = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa"
    Assert.Equal(Error "token_invalid", checkTokenAnswer plc (answer other "atproto" "Bearer"))
    Assert.Equal(Error "token_invalid", checkTokenAnswer plc (answer other "email" "DPoP"))
    Assert.Equal(Error "token_invalid", checkTokenAnswer plc (answer "nope" "atproto" "DPoP"))

// ---------------------------------------------------------------------------
// The profile record
// ---------------------------------------------------------------------------

[<Fact>]
let ``cleanName keeps a plain name and trims it`` () =
    Assert.Equal(Some "Alice B", cleanName (Some "Alice B"))
    Assert.Equal(Some "Alice", cleanName (Some "   Alice \n"))
    Assert.Equal(Some "Alice 🦋", cleanName (Some "Alice 🦋"))

[<Fact>]
let ``cleanName drops control characters`` () =
    Assert.Equal(Some "AliceB", cleanName (Some "Alice\u0000\u0007B"))
    Assert.Equal(Some "Alice B", cleanName (Some "Alice\u0009 B")) // the tab is a control character
    Assert.Equal(Some "Alice[31mB", cleanName (Some "Alice\u001B[31mB")) // the escape goes, the rest is text

[<Fact>]
let ``cleanName drops format characters`` () =
    Assert.Equal(Some "Alice", cleanName (Some "‎Alice"))
    Assert.Equal(Some "Alice", cleanName (Some "Ali‮ce")) // right-to-left override
    Assert.Equal(Some "Alice", cleanName (Some "Al​ice⁦"))
    // A tag character is outside the basic plane and is dropped too.
    Assert.Equal(Some "Alice", cleanName (Some "Alice\U000E0041"))

[<Fact>]
let ``cleanName drops the line and paragraph separators`` () =
    Assert.Equal(Some "AliceB", cleanName (Some "Alice\u2028B"))
    Assert.Equal(Some "AliceB", cleanName (Some "Alice\u2029B"))
    Assert.Equal(Some "Alice", cleanName (Some "\u2028Alice\u2029"))
    Assert.Equal(None, cleanName (Some "\u2028\u2029"))

[<Fact>]
let ``cleanName of nothing usable is None`` () =
    Assert.Equal(None, cleanName None)
    Assert.Equal(None, cleanName (Some ""))
    Assert.Equal(None, cleanName (Some "   "))
    Assert.Equal(None, cleanName (Some "‎‏\u0000"))

[<Fact>]
let ``cleanName cuts to 200 characters`` () =
    let cut = cleanName (Some(String('a', 300)))
    Assert.Equal(Some(String('a', 200)), cut)
    Assert.Equal(Some(String('a', 200)), cleanName (Some(String('a', 200))))

[<Fact>]
let ``cleanName never cuts inside a surrogate pair`` () =
    // 199 letters, then an emoji (two chars) straddling the limit.
    let text = String('a', 199) + "🦋" + "tail"
    let cut = (cleanName (Some text)).Value
    Assert.Equal(String('a', 199), cut)
    Assert.True(cut.Length <= 200)

[<Fact>]
let ``cleanName trims after cutting`` () =
    let text = String('a', 198) + "   " + "bbb"
    Assert.Equal(Some(String('a', 198)), cleanName (Some text))

let private record (value: string) = $"""{{ "uri": "at://x", "cid": "bafy", "value": {value} }}"""

[<Fact>]
let ``displayName reads value.displayName, cleaned`` () =
    Assert.Equal(Some "Alice", displayName (record """{ "displayName": "Alice" }"""))
    Assert.Equal(Some "Alice", displayName (record """{ "displayName": "‎Alice\u0000 " }"""))

[<Fact>]
let ``displayName is None when it is missing, empty or not a string`` () =
    Assert.Equal(None, displayName (record "{}"))
    Assert.Equal(None, displayName (record """{ "displayName": "" }"""))
    Assert.Equal(None, displayName (record """{ "displayName": "‎" }"""))
    Assert.Equal(None, displayName (record """{ "displayName": 5 }"""))
    Assert.Equal(None, displayName """{ "displayName": "Alice" }""") // not under value
    Assert.Equal(None, displayName """{ "value": "Alice" }""")
    Assert.Equal(None, displayName "not json")
    Assert.Equal(None, displayName "")
    Assert.Equal(None, displayName null)

let private avatar (link: string) =
    record $"""{{ "avatar": {{ "$type": "blob", "ref": {{ "$link": {link} }}, "mimeType": "image/jpeg", "size": 1 }} }}"""

[<Fact>]
let ``avatarCid reads value.avatar.ref.$link`` () =
    Assert.Equal(
        Some "bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku",
        avatarCid (avatar "\"bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku\"")
    )

    Assert.Equal(Some "a", avatarCid (avatar "\"a\""))

[<Fact>]
let ``avatarCid accepts 128 characters and refuses 129`` () =
    Assert.Equal(Some(String('a', 128)), avatarCid (avatar $"\"{String('a', 128)}\""))
    Assert.Equal(None, avatarCid (avatar $"\"{String('a', 129)}\""))

[<Theory>]
[<InlineData("\"\"")>]
[<InlineData("\"BAFYUPPER\"")>]
[<InlineData("\"bafy/../x\"")>]
[<InlineData("\"bafy?x=1\"")>]
[<InlineData("\"bafy&x=1\"")>]
[<InlineData("\"bafy cid\"")>]
[<InlineData("\"bafy-cid\"")>]
[<InlineData("\"bafy\\n\"")>]
[<InlineData("\"bafé\"")>]
[<InlineData("5")>]
[<InlineData("null")>]
let ``avatarCid refuses anything but lower-case letters and digits`` (link: string) =
    Assert.Equal(None, avatarCid (avatar link))

[<Fact>]
let ``avatarCid is None when the path is not there`` () =
    Assert.Equal(None, avatarCid (record "{}"))
    Assert.Equal(None, avatarCid (record """{ "avatar": "bafy" }"""))
    Assert.Equal(None, avatarCid (record """{ "avatar": { "ref": "bafy" } }"""))
    Assert.Equal(None, avatarCid (record """{ "avatar": { "ref": {} } }"""))
    Assert.Equal(None, avatarCid """{ "avatar": { "ref": { "$link": "bafy" } } }""")
    Assert.Equal(None, avatarCid "not json")
    Assert.Equal(None, avatarCid null)

// ---------------------------------------------------------------------------
// The client ID
// ---------------------------------------------------------------------------

let private clientId (allowDev: bool) (origin: string) = clientIdFor allowDev (Uri origin)

[<Fact>]
let ``https with the default port gives the metadata address`` () =
    let expected = Ok(Metadata "https://auth.example.com/connect/bluesky/client-metadata.json")
    Assert.Equal(expected, clientId false "https://auth.example.com")
    Assert.Equal(expected, clientId false "https://auth.example.com/")
    Assert.Equal(expected, clientId false "https://auth.example.com:443")
    Assert.Equal(expected, clientId true "https://auth.example.com")

[<Fact>]
let ``https with a port is origin_unsuitable`` () =
    Assert.Equal(Error "origin_unsuitable", clientId false "https://auth.example.com:8443")
    Assert.Equal(Error "origin_unsuitable", clientId true "https://auth.example.com:8443")

[<Fact>]
let ``http with a public host is origin_unsuitable, in development too`` () =
    Assert.Equal(Error "origin_unsuitable", clientId false "http://auth.example.com")
    Assert.Equal(Error "origin_unsuitable", clientId true "http://auth.example.com")
    Assert.Equal(Error "origin_unsuitable", clientId true "http://auth.example.com:8080")

[<Fact>]
let ``http://localhost is origin_unsuitable, in development too`` () =
    Assert.Equal(Error "origin_unsuitable", clientId false "http://localhost:5000")
    Assert.Equal(Error "origin_unsuitable", clientId true "http://localhost:5000")
    Assert.Equal(Error "origin_unsuitable", clientId true "http://localhost")

[<Fact>]
let ``a host that merely resembles loopback does not qualify`` () =
    Assert.Equal(Error "origin_unsuitable", clientId true "http://127.0.0.1.example.com:5000")
    Assert.Equal(Error "origin_unsuitable", clientId true "http://127.0.0.2:5000")
    Assert.Equal(Error "origin_unsuitable", clientId true "http://0.0.0.0:5000")
    Assert.Equal(Error "origin_unsuitable", clientId true "http://auth.example.com:5000/127.0.0.1")

[<Fact>]
let ``127.0.0.1 with a port gives the localhost client only in development`` () =
    Assert.Equal(Error "origin_unsuitable", clientId false "http://127.0.0.1:5000")

    let expected =
        "http://localhost?redirect_uri="
        + "http%3A%2F%2F127.0.0.1%3A5000%2Fconnect%2Fbluesky%2Fcallback"
        + "&scope=atproto"

    Assert.Equal(Ok(Loopback expected), clientId true "http://127.0.0.1:5000")

[<Fact>]
let ``the loopback client ID names the port it was given`` () =
    match clientId true "http://127.0.0.1:61234" with
    | Ok(Loopback text) ->
        Assert.Contains("127.0.0.1%3A61234%2Fconnect%2Fbluesky%2Fcallback", text)
        Assert.EndsWith("&scope=atproto", text)
        Assert.StartsWith("http://localhost?redirect_uri=", text)
    | other -> failwith $"unexpected {other}"

[<Fact>]
let ``[::1] gives the localhost client with the bracketed host`` () =
    Assert.Equal(Error "origin_unsuitable", clientId false "http://[::1]:5000")

    let expected =
        "http://localhost?redirect_uri="
        + "http%3A%2F%2F%5B%3A%3A1%5D%3A5000%2Fconnect%2Fbluesky%2Fcallback"
        + "&scope=atproto"

    Assert.Equal(Ok(Loopback expected), clientId true "http://[::1]:5000")

[<Fact>]
let ``https on a loopback host is a metadata address like any other https`` () =
    Assert.Equal(
        Ok(Metadata "https://127.0.0.1/connect/bluesky/client-metadata.json"),
        clientId true "https://127.0.0.1"
    )

// ---------------------------------------------------------------------------
// The client metadata document
// ---------------------------------------------------------------------------

[<Fact>]
let ``the metadata document has exactly the fields it should`` () =
    let origin = Uri "https://auth.example.com"
    let clientId = "https://auth.example.com/connect/bluesky/client-metadata.json"
    let json = clientMetadataJson origin clientId
    use doc = JsonDocument.Parse json
    let root = doc.RootElement

    let names = [ for p in root.EnumerateObject() -> p.Name ]

    Assert.Equal<string list>(
        [ "client_id"
          "client_name"
          "client_uri"
          "application_type"
          "grant_types"
          "response_types"
          "redirect_uris"
          "scope"
          "token_endpoint_auth_method"
          "dpop_bound_access_tokens" ],
        names
    )

    let str (element: JsonElement) =
        element.GetString() |> Option.ofObj |> Option.defaultValue ""

    let text (name: string) = str (root.GetProperty name)
    let list (name: string) = [ for x in root.GetProperty(name).EnumerateArray() -> str x ]

    Assert.Equal(clientId, text "client_id")
    Assert.Equal("Umbral", text "client_name")
    Assert.Equal("https://auth.example.com", text "client_uri")
    Assert.Equal("web", text "application_type")
    Assert.Equal<string list>([ "authorization_code" ], list "grant_types")
    Assert.Equal<string list>([ "code" ], list "response_types")
    Assert.Equal<string list>([ "https://auth.example.com/connect/bluesky/callback" ], list "redirect_uris")
    Assert.Equal("atproto", text "scope")
    Assert.Equal("none", text "token_endpoint_auth_method")
    Assert.Equal(JsonValueKind.True, root.GetProperty("dpop_bound_access_tokens").ValueKind)

[<Fact>]
let ``the metadata document has no trailing slash on the origin`` () =
    let json = clientMetadataJson (Uri "https://auth.example.com/") "id"
    use doc = JsonDocument.Parse json
    Assert.Equal("https://auth.example.com", doc.RootElement.GetProperty("client_uri").GetString())

    let redirects = doc.RootElement.GetProperty("redirect_uris")
    Assert.Equal("https://auth.example.com/connect/bluesky/callback", redirects[0].GetString())

[<Fact>]
let ``clientMetadata is the same document as clientMetadataJson`` () =
    let origin = Uri "https://auth.example.com"
    Assert.Equal(clientMetadataJson origin "id", (clientMetadata origin "id").ToJsonString())
