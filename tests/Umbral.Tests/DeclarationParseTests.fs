module Umbral.Tests.DeclarationParseTests

open System
open Xunit
open Umbral.Domains
open Umbral.Domains.Declaration

// 32 characters, with a marker no problem line may ever print.
let private secret = "SECRETVALUE-0123456789-abcdefghi"
let private shortSecret = "SHORTSECRET-1234"

let private webApp = """{ "key": "web", "type": "web", "redirectUris": ["http://localhost:5000/signin-oidc"] }"""
let private spaApp = """{ "key": "spa", "type": "spa", "redirectUris": ["http://localhost:5001/cb"] }"""

let private doc (rest: string) = $"""{{ "version": 1{rest} }}"""

let private withApps (apps: string list) =
    doc $""", "apps": [ {String.Join(", ", apps)} ]"""

let private problems (json: string) (secrets: (string * string) list) =
    match parse json secrets with
    | Ok d -> failwith $"expected a refusal, got %A{d}"
    | Error lines -> lines

let private ok (json: string) (secrets: (string * string) list) =
    match parse json secrets with
    | Ok d -> d
    | Error lines -> failwith (String.Join("\n", lines))

let private webSecret = [ "web", secret ]

let private noSecretValue (lines: string list) =
    for line in lines do
        Assert.DoesNotContain("SECRETVALUE", line)
        Assert.DoesNotContain("SHORTSECRET", line)

[<Fact>]
let ``the empty declaration has no passkeys and no apps`` () =
    let d = ok (doc "") []
    Assert.Equal<Declaration>({ Passkeys = None; Apps = [] }, d)

[<Fact>]
let ``a full valid declaration parses to the expected record`` () =
    let json =
        """{
  "version": 1,
  "passkeys": { "signUp": "open" },
  "apps": [
    {
      "key": "web",
      "name": "  Sample web app ",
      "type": "web",
      "redirectUris": ["  http://localhost:5000/signin-oidc ", "https://app.example.com/cb"],
      "postLogoutRedirectUris": ["http://localhost:5000/signout-callback-oidc"],
      "connectors": ["passkey", "github"]
    },
    { "key": "spa", "type": "spa", "redirectUris": ["http://localhost:5001/cb"], "connectors": "all-shared" }
  ]
}"""

    let d = ok json [ "WEB", secret ]
    Assert.Equal(Some { SignUp = Connectors.OpenSignUp }, d.Passkeys)
    Assert.Equal(2, d.Apps.Length)

    let web = d.Apps[0]
    Assert.Equal("web", web.Key)
    Assert.Equal("Sample web app", web.Name)
    Assert.Equal(Web, web.Type)
    Assert.Equal<string list>([ "http://localhost:5000/signin-oidc"; "https://app.example.com/cb" ], web.RedirectUris)
    Assert.Equal<string list>([ "http://localhost:5000/signout-callback-oidc" ], web.PostLogoutRedirectUris)
    Assert.Equal(Connectors.Only(set [ "passkey"; "github" ]), web.Connectors)
    Assert.Equal(secret, web.Secret.Value.Value)

    let spa = d.Apps[1]
    Assert.Equal("spa", spa.Key)
    Assert.Equal(Spa, spa.Type)
    Assert.Equal(Connectors.AllShared, spa.Connectors)
    Assert.Equal<string list>([], spa.PostLogoutRedirectUris)
    Assert.True(spa.Secret.IsNone)

[<Fact>]
let ``defaults: name is the key, sign-up is invitations, connectors are all shared`` () =
    let d = ok (doc (""", "passkeys": {}, "apps": [ """ + spaApp + " ]")) []
    Assert.Equal(Some { SignUp = Connectors.InvitationsOnly }, d.Passkeys)
    Assert.Equal("spa", d.Apps[0].Name)
    Assert.Equal(Connectors.AllShared, d.Apps[0].Connectors)

[<Fact>]
let ``an empty connector list is a valid Only set`` () =
    let json = withApps [ """{ "key": "spa", "type": "spa", "redirectUris": ["http://localhost/cb"], "connectors": [] }""" ]
    Assert.Equal(Connectors.Only Set.empty, (ok json []).Apps[0].Connectors)

[<Fact>]
let ``signUp invitations is accepted`` () =
    let d = ok (doc """, "passkeys": { "signUp": "invitations" }""") []
    Assert.Equal(Some { SignUp = Connectors.InvitationsOnly }, d.Passkeys)

[<Theory>]
[<InlineData("not json")>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData("""{ "version": 1, """)>]
let ``invalid JSON is refused`` (json: string) =
    let lines = problems json []
    Assert.Single lines |> ignore
    Assert.StartsWith("declaration:", lines[0])

[<Fact>]
let ``a root that is not an object is refused`` () =
    let lines = problems "[1]" []
    Assert.Equal<string list>([ "declaration: it must be a JSON object, with \"version\": 1." ], lines)

[<Fact>]
let ``a missing version is refused`` () =
    let lines = problems "{}" []
    Assert.Equal<string list>([ "declaration: \"version\" is missing; this Umbral reads declaration version 1." ], lines)

[<Theory>]
[<InlineData("2")>]
[<InlineData("0")>]
[<InlineData("\"1\"")>]
[<InlineData("1.5")>]
[<InlineData("null")>]
let ``a version other than 1 is refused`` (version: string) =
    let lines = problems $"""{{ "version": {version} }}""" []
    Assert.Single lines |> ignore
    Assert.Contains("this Umbral reads declaration version 1", lines[0])

[<Fact>]
let ``an unknown property is refused at every level`` () =
    let lines =
        problems
            ("""{ "version": 1, "extra": 1, "passkeys": { "mode": "open" }, "apps": [ { "key": "spa", "type": "spa", "redirectUris": ["http://localhost/cb"], "colour": "red" } ] }""")
            []

    Assert.Equal<string list>(
        [
            "declaration: unknown property \"extra\". Remove it."
            "passkeys: unknown property \"mode\". Remove it."
            "apps[0] (key \"spa\"): unknown property \"colour\". Remove it."
        ],
        lines
    )

[<Fact>]
let ``a property written twice is refused`` () =
    let lines = problems """{ "version": 1, "version": 1 }""" []
    Assert.Equal<string list>([ "declaration: the property \"version\" is written twice. Keep one." ], lines)

[<Theory>]
[<InlineData("\"yes\"")>]
[<InlineData("true")>]
[<InlineData("\"Open\"")>]
let ``a bad signUp is refused`` (value: string) =
    let lines = problems (doc $""", "passkeys": {{ "signUp": {value} }}""") []
    Assert.Single lines |> ignore
    Assert.Contains("Write \"invitations\" or \"open\"", lines[0])

[<Fact>]
let ``passkeys that is not an object is refused`` () =
    let lines = problems (doc """, "passkeys": "open" """) []
    Assert.Equal<string list>([ "passkeys: must be an object, not \"open\"." ], lines)

[<Fact>]
let ``apps that is not a list is refused`` () =
    let lines = problems (doc """, "apps": {} """) []
    Assert.Equal<string list>([ "declaration: \"apps\" must be a list, not {}." ], lines)

[<Fact>]
let ``an app that is not an object is refused`` () =
    let lines = problems (doc """, "apps": [ 3 ] """) []
    Assert.Equal<string list>([ "apps[0]: must be an object, not 3." ], lines)

[<Fact>]
let ``a missing key is refused`` () =
    let lines = problems (withApps [ """{ "type": "spa", "redirectUris": ["http://localhost/cb"] }""" ]) []
    Assert.Equal<string list>([ "apps[0]: \"key\" is missing. It becomes the client ID." ], lines)

[<Theory>]
[<InlineData("My App")>]
[<InlineData("UPPER")>]
[<InlineData("under_score")>]
[<InlineData("")>]
[<InlineData("a-key-that-is-way-too-long-for-a-slug-1234")>]
[<InlineData("web\\n")>]
let ``an invalid key is refused`` (key: string) =
    let json = withApps [ $"""{{ "key": "{key}", "type": "spa", "redirectUris": ["http://localhost/cb"] }}""" ]
    let lines = problems json []
    Assert.Single lines |> ignore
    Assert.StartsWith("apps[0]: \"key\"", lines[0])
    Assert.Contains("lowercase letters, digits and hyphens", lines[0])

[<Fact>]
let ``a key that is not a string is refused`` () =
    let lines = problems (withApps [ """{ "key": 5, "type": "spa", "redirectUris": ["http://localhost/cb"] }""" ]) []
    Assert.Equal<string list>([ "apps[0]: \"key\" must be a string, not 5." ], lines)

[<Fact>]
let ``a duplicate key is refused once`` () =
    let lines = problems (withApps [ spaApp; spaApp ]) []

    Assert.Equal<string list>(
        [ "apps[1] (key \"spa\"): the key \"spa\" is already used by apps[0]. Keys must be unique." ],
        lines
    )

[<Fact>]
let ``an empty name is refused`` () =
    let lines = problems (withApps [ """{ "key": "spa", "name": "  ", "type": "spa", "redirectUris": ["http://localhost/cb"] }""" ]) []
    Assert.Equal<string list>([ "apps[0] (key \"spa\"): \"name\" is empty. Leave it out to use the key." ], lines)

[<Fact>]
let ``a name of 100 characters is accepted and 101 is refused`` () =
    let make n = withApps [ $"""{{ "key": "spa", "name": "{String('n', n)}", "type": "spa", "redirectUris": ["http://localhost/cb"] }}""" ]
    Assert.Equal(100, (ok (make 100) []).Apps[0].Name.Length)
    let lines = problems (make 101) []
    Assert.Equal<string list>([ "apps[0] (key \"spa\"): \"name\" is longer than 100 characters." ], lines)

[<Fact>]
let ``a name that is not a string is refused`` () =
    let lines = problems (withApps [ """{ "key": "spa", "name": 7, "type": "spa", "redirectUris": ["http://localhost/cb"] }""" ]) []
    Assert.Equal<string list>([ "apps[0] (key \"spa\"): \"name\" must be a string, not 7." ], lines)

[<Fact>]
let ``a missing type is refused`` () =
    let lines = problems (withApps [ """{ "key": "spa", "redirectUris": ["http://localhost/cb"] }""" ]) []
    Assert.Equal<string list>([ "apps[0] (key \"spa\"): \"type\" is missing. Write \"web\" or \"spa\"." ], lines)

[<Fact>]
let ``an unknown type is refused`` () =
    let lines = problems (withApps [ """{ "key": "spa", "type": "native", "redirectUris": ["http://localhost/cb"] }""" ]) []
    Assert.Equal<string list>([ "apps[0] (key \"spa\"): \"type\" is \"native\". Write \"web\" or \"spa\"." ], lines)

[<Fact>]
let ``missing redirect URIs are refused`` () =
    let lines = problems (withApps [ """{ "key": "spa", "type": "spa" }""" ]) []
    Assert.Equal<string list>([ "apps[0] (key \"spa\"): \"redirectUris\" is missing. List at least one address." ], lines)

[<Fact>]
let ``an empty redirect list is refused`` () =
    let lines = problems (withApps [ """{ "key": "spa", "type": "spa", "redirectUris": [] }""" ]) []
    Assert.Equal<string list>([ "apps[0] (key \"spa\"): \"redirectUris\" is empty. List at least one address." ], lines)

[<Fact>]
let ``redirect URIs that are not a list are refused`` () =
    let lines = problems (withApps [ """{ "key": "spa", "type": "spa", "redirectUris": "http://localhost/cb" }""" ]) []

    Assert.Equal<string list>(
        [ "apps[0] (key \"spa\"): \"redirectUris\" must be a list of addresses, not \"http://localhost/cb\"." ],
        lines
    )

[<Fact>]
let ``a redirect URI that is not a string is refused`` () =
    let lines = problems (withApps [ """{ "key": "spa", "type": "spa", "redirectUris": [5] }""" ]) []
    Assert.Equal<string list>([ "apps[0] (key \"spa\"): redirectUris[0] must be a string, not 5." ], lines)

[<Fact>]
let ``ten redirect URIs are accepted and eleven are refused`` () =
    let uris n = String.Join(", ", [ for i in 1..n -> $"\"https://app.example.com/cb{i}\"" ])
    let make n = withApps [ $"""{{ "key": "spa", "type": "spa", "redirectUris": [{uris n}] }}""" ]
    Assert.Equal(10, (ok (make 10) []).Apps[0].RedirectUris.Length)

    Assert.Equal<string list>(
        [ "apps[0] (key \"spa\"): \"redirectUris\" has 11 addresses; at most 10 are allowed." ],
        problems (make 11) []
    )

[<Fact>]
let ``eleven post-logout URIs are refused`` () =
    let uris = String.Join(", ", [ for i in 1..11 -> $"\"https://app.example.com/out{i}\"" ])

    let lines =
        problems
            (withApps [ $"""{{ "key": "spa", "type": "spa", "redirectUris": ["http://localhost/cb"], "postLogoutRedirectUris": [{uris}] }}""" ])
            []

    Assert.Equal<string list>(
        [ "apps[0] (key \"spa\"): \"postLogoutRedirectUris\" has 11 addresses; at most 10 are allowed." ],
        lines
    )

[<Fact>]
let ``a wildcard redirect URI is refused with the admin area's words`` () =
    let lines = problems (withApps [ """{ "key": "web", "type": "spa", "redirectUris": ["https://*.example.com/cb"] }""" ]) []

    Assert.Equal<string list>(
        [ "apps[0] (key \"web\"): redirectUris[0] \"https://*.example.com/cb\" contains a wildcard. Redirect URIs are matched exactly." ],
        lines
    )

[<Theory>]
[<InlineData("/callback", "is not an absolute URL")>]
[<InlineData("not a url", "is not an absolute URL")>]
[<InlineData("https://app.example.com/cb#frag", "contains a fragment")>]
[<InlineData("", "is empty.")>]
let ``other invalid redirect URIs are refused`` (uri: string, fragment: string) =
    let lines = problems (withApps [ $"""{{ "key": "spa", "type": "spa", "redirectUris": ["{uri}"] }}""" ]) []
    Assert.Single lines |> ignore
    Assert.StartsWith("apps[0] (key \"spa\"): redirectUris[0] ", lines[0])
    Assert.Contains(fragment, lines[0])

[<Fact>]
let ``post-logout URIs are checked by the same rule`` () =
    let lines =
        problems
            (withApps [ """{ "key": "spa", "type": "spa", "redirectUris": ["http://localhost/cb"], "postLogoutRedirectUris": ["/out"] }""" ])
            []

    Assert.Single lines |> ignore
    Assert.StartsWith("apps[0] (key \"spa\"): postLogoutRedirectUris[0] ", lines[0])

[<Fact>]
let ``a duplicate redirect URI is refused, also when written another way`` () =
    let lines =
        problems
            (withApps [ """{ "key": "spa", "type": "spa", "redirectUris": ["https://APP.example.com/cb", "https://app.example.com/cb"] }""" ])
            []

    Assert.Single lines |> ignore
    Assert.StartsWith("apps[0] (key \"spa\"): redirectUris[1] \"https://app.example.com/cb\" is already listed", lines[0])

[<Fact>]
let ``a connectors string other than all-shared is refused`` () =
    let lines = problems (withApps [ """{ "key": "spa", "type": "spa", "redirectUris": ["http://localhost/cb"], "connectors": "some" }""" ]) []

    Assert.Equal<string list>(
        [ "apps[0] (key \"spa\"): \"connectors\" is \"some\". Write \"all-shared\" or a list of connector keys." ],
        lines
    )

[<Fact>]
let ``connectors of another kind are refused`` () =
    let lines = problems (withApps [ """{ "key": "spa", "type": "spa", "redirectUris": ["http://localhost/cb"], "connectors": 3 }""" ]) []
    Assert.Single lines |> ignore
    Assert.Contains("must be \"all-shared\" or a list of connector keys", lines[0])

[<Fact>]
let ``a connector slug that is not a slug is refused`` () =
    let lines =
        problems
            (withApps [ """{ "key": "spa", "type": "spa", "redirectUris": ["http://localhost/cb"], "connectors": ["passkey", "Not A Slug", 4] }""" ])
            []

    Assert.Equal(2, lines.Length)
    Assert.StartsWith("apps[0] (key \"spa\"): connectors[1] \"Not A Slug\" is not a connector key", lines[0])
    Assert.Equal("apps[0] (key \"spa\"): connectors[2] must be a string, not 4.", lines[1])

// ---- secrets -------------------------------------------------------------

[<Fact>]
let ``a web app without a secret is refused and names the variable`` () =
    let lines = problems (withApps [ webApp ]) []

    Assert.Equal<string list>(
        [ "apps[0] (key \"web\"): UMBRAL__APP_SECRETS__web is not set. A web app needs a secret of at least 32 characters." ],
        lines
    )

[<Fact>]
let ``the missing-secret line writes hyphens as underscores`` () =
    let app = """{ "key": "my-app", "type": "web", "redirectUris": ["http://localhost/cb"] }"""
    let lines = problems (withApps [ app ]) []
    Assert.Contains("UMBRAL__APP_SECRETS__my_app is not set", lines[0])

[<Fact>]
let ``a secret shorter than 32 characters is refused without its value`` () =
    let lines = problems (withApps [ webApp ]) [ "web", shortSecret ]

    Assert.Equal<string list>(
        [ "apps[0] (key \"web\"): UMBRAL__APP_SECRETS__web is shorter than 32 characters." ],
        lines
    )

    noSecretValue lines

[<Fact>]
let ``a secret of exactly 32 characters is accepted`` () =
    Assert.Equal(32, secret.Length)
    Assert.True((ok (withApps [ webApp ]) webSecret).Apps[0].Secret.IsSome)

[<Fact>]
let ``a spa app with a secret is refused without its value`` () =
    let lines = problems (withApps [ spaApp ]) [ "spa", secret ]

    Assert.Equal<string list>(
        [ "apps[0] (key \"spa\"): UMBRAL__APP_SECRETS__spa is set, but a spa app is public and holds no secret. Remove the variable." ],
        lines
    )

    noSecretValue lines

[<Fact>]
let ``a secret for no declared app is refused once, without its value`` () =
    let lines = problems (withApps [ webApp ]) [ "web", secret; "old", secret ]

    Assert.Equal<string list>(
        [ "UMBRAL__APP_SECRETS__old: no declared web app has the key \"old\". Remove the variable." ],
        lines
    )

    noSecretValue lines

[<Fact>]
let ``a secret with no declaration at all is refused`` () =
    let lines = problems (doc "") [ "old", secret ]
    Assert.Single lines |> ignore
    noSecretValue lines

[<Fact>]
let ``the underscore form of a hyphenated key is accepted`` () =
    let app = """{ "key": "my-app", "type": "web", "redirectUris": ["http://localhost/cb"] }"""
    let d = ok (withApps [ app ]) [ "my_app", secret ]
    Assert.Equal(secret, d.Apps[0].Secret.Value.Value)

[<Fact>]
let ``secret keys match in any case`` () =
    let app = """{ "key": "my-app", "type": "web", "redirectUris": ["http://localhost/cb"] }"""
    Assert.True((ok (withApps [ app ]) [ "MY_APP", secret ]).Apps[0].Secret.IsSome)
    Assert.True((ok (withApps [ app ]) [ "My-App", secret ]).Apps[0].Secret.IsSome)

[<Fact>]
let ``both forms of a key are refused without the values`` () =
    let app = """{ "key": "my-app", "type": "web", "redirectUris": ["http://localhost/cb"] }"""
    let lines = problems (withApps [ app ]) [ "my-app", secret; "my_app", secret ]

    Assert.Equal<string list>(
        [ "apps[0] (key \"my-app\"): UMBRAL__APP_SECRETS__my-app and UMBRAL__APP_SECRETS__my_app are both set. Keep one of them." ],
        lines
    )

    noSecretValue lines

[<Fact>]
let ``the underscore form of a stray secret is not matched to a key with underscores`` () =
    // `my_app` is not a valid key, but a variable for it names no app.
    let lines = problems (withApps [ webApp ]) [ "web", secret; "my_app", secret ]
    Assert.Single lines |> ignore
    Assert.StartsWith("UMBRAL__APP_SECRETS__my_app:", lines[0])
    noSecretValue lines

[<Fact>]
let ``every problem is reported, in document order then stray secrets by name`` () =
    let json =
        """{
  "version": 2,
  "extra": true,
  "passkeys": { "signUp": "maybe" },
  "apps": [
    { "key": "web", "type": "web", "redirectUris": ["/cb"] },
    { "key": "spa", "type": "spa", "redirectUris": ["http://localhost/cb"] }
  ]
}"""

    let lines = problems json [ "zeta", secret; "spa", secret; "alpha", secret; "web", shortSecret ]

    Assert.Equal<string list>(
        [
            "declaration: unknown property \"extra\". Remove it."
            "declaration: \"version\" is 2; this Umbral reads declaration version 1."
            "passkeys: \"signUp\" is \"maybe\". Write \"invitations\" or \"open\"."
            "apps[0] (key \"web\"): redirectUris[0] \"/cb\" is not an absolute URL. Include the scheme and host, for example https://app.example.com/callback."
            "apps[0] (key \"web\"): UMBRAL__APP_SECRETS__web is shorter than 32 characters."
            "apps[1] (key \"spa\"): UMBRAL__APP_SECRETS__spa is set, but a spa app is public and holds no secret. Remove the variable."
            "UMBRAL__APP_SECRETS__alpha: no declared web app has the key \"alpha\". Remove the variable."
            "UMBRAL__APP_SECRETS__zeta: no declared web app has the key \"zeta\". Remove the variable."
        ],
        lines
    )

    noSecretValue lines

[<Fact>]
let ``problems come out the same each time`` () =
    let json = withApps [ webApp; spaApp ]
    let secrets = [ "zeta", secret; "spa", secret; "alpha", secret ]
    Assert.Equal<string list>(problems json secrets, problems json (List.rev secrets))

[<Fact>]
let ``AppSecret never prints its value`` () =
    let s = AppSecret.Create secret
    Assert.Equal("AppSecret(***)", s.ToString())
    Assert.DoesNotContain("SECRETVALUE", sprintf "%A" s)
    Assert.DoesNotContain("SECRETVALUE", sprintf "%O" s)
    Assert.Equal(secret, s.Value)

[<Fact>]
let ``a parsed declaration never prints a secret`` () =
    let d = ok (withApps [ webApp ]) webSecret
    Assert.DoesNotContain("SECRETVALUE", sprintf "%A" d)
    Assert.DoesNotContain("SECRETVALUE", sprintf "%A" d.Apps[0])
    Assert.DoesNotContain("SECRETVALUE", d.Apps[0].Secret.ToString())
    Assert.DoesNotContain("SECRETVALUE", d.ToString())

[<Fact>]
let ``two parses of the same input are equal`` () =
    let json = withApps [ webApp ]
    Assert.Equal<Declaration>(ok json webSecret, ok json webSecret)
