/// The one rule for which connectors an app offers (FR-006, FR-007, FR-010,
/// FR-011), as pure functions: no host, no database.
module Umbral.Tests.AppChoiceRuleTests

open System
open Xunit
open Umbral.Domains.Connectors

let private connector slug kind enabled shared : Connector =
    { Slug = slug
      Kind = kind
      DisplayName = slug
      ClientId = "id"
      ClientSecretRef = "ref"
      ClientSecretHint = "hint"
      IssuerUrl = None
      AllowedEmailDomains = []
      IsEnabled = enabled
      IsShared = shared
      CreatedUtc = DateTimeOffset.UnixEpoch
      UpdatedUtc = DateTimeOffset.UnixEpoch }

let private slugs (connectors: Connector list) = connectors |> List.map _.Slug

let private all =
    [ connector "discord" Discord true true
      connector "discord-frunchy" Discord true false
      connector "github" GitHub true true
      connector "google" Google false true ]

// ---------------------------------------------------------------------------
// offeredTo
// ---------------------------------------------------------------------------

[<Fact>]
let ``AllShared offers the enabled shared connectors and nothing else`` () =
    // discord-frunchy is not shared and google is disabled.
    Assert.Equal<string list>([ "discord"; "github" ], offeredTo AllShared all |> slugs)

[<Fact>]
let ``Only offers the enabled members of the set, shared or not`` () =
    let choice = Only(Set.ofList [ "discord-frunchy"; "github" ])
    Assert.Equal<string list>([ "discord-frunchy"; "github" ], offeredTo choice all |> slugs)

[<Fact>]
let ``Only does not offer a member that is disabled`` () =
    // google is in the set but disabled: it stays in the set and is simply
    // not offered until it is enabled again (FR-007).
    let choice = Only(Set.ofList [ "google"; "discord" ])
    Assert.Equal<string list>([ "discord" ], offeredTo choice all |> slugs)

[<Fact>]
let ``an unknown slug in the set widens nothing`` () =
    let choice = Only(Set.ofList [ "no-such-connector" ])
    Assert.Empty(offeredTo choice all)

[<Fact>]
let ``an empty set offers nothing and does not become AllShared`` () =
    Assert.Empty(offeredTo (Only Set.empty) all)

[<Fact>]
let ``AllShared with no connectors offers nothing`` () =
    Assert.Empty(offeredTo AllShared [])

[<Fact>]
let ``offeredTo keeps the order of the connectors it was given`` () =
    let reversed = List.rev all
    Assert.Equal<string list>([ "github"; "discord" ], offeredTo AllShared reversed |> slugs)

// ---------------------------------------------------------------------------
// strandedApps
// ---------------------------------------------------------------------------

[<Fact>]
let ``strandedApps names the apps left with nothing`` () =
    let apps =
        [ "a", "Alpha", AllShared
          "b", "Bravo", Only Set.empty
          "c", "Charlie", Only(Set.ofList [ "github" ])
          "d", "Delta", Only(Set.ofList [ "google" ]) ]

    // Bravo has an empty set; Delta's only member is disabled.
    Assert.Equal<(string * string) list>([ "b", "Bravo"; "d", "Delta" ], strandedApps apps all)

[<Fact>]
let ``strandedApps keeps the input order`` () =
    let apps =
        [ "z", "Zulu", Only Set.empty
          "a", "Alpha", Only Set.empty
          "m", "Mike", Only Set.empty ]

    Assert.Equal<string list>([ "z"; "a"; "m" ], strandedApps apps all |> List.map fst)

[<Fact>]
let ``strandedApps sees an AllShared app stranded once nothing shared is enabled`` () =
    // The connectors as they WOULD be after disabling both shared ones.
    let after =
        all |> List.map (fun c -> if c.Slug = "discord" || c.Slug = "github" then { c with IsEnabled = false } else c)

    let apps =
        [ "a", "Alpha", AllShared
          "f", "Frunchy", Only(Set.ofList [ "discord-frunchy" ]) ]

    // Frunchy still offers its own not-shared connector.
    Assert.Equal<(string * string) list>([ "a", "Alpha" ], strandedApps apps after)

[<Fact>]
let ``strandedApps is empty when every app still offers something`` () =
    let apps = [ "a", "Alpha", AllShared; "c", "Charlie", Only(Set.ofList [ "github" ]) ]
    Assert.Empty(strandedApps apps all)

[<Fact>]
let ``strandedApps with no apps is empty`` () = Assert.Empty(strandedApps [] [])
