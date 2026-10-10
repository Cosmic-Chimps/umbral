module Umbral.Tests.ConfigFileTests

open System
open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.DataProtection
open Xunit
open Umbral.Domains.ConfigDocument
open Umbral.Infrastructure

let private valid =
    """
version: 1

connectors:
  - slug: discord
    kind: discord
    display_name: Discord
    client_id: "1234567890"
    client_secret: ${UMBRAL_DISCORD_SECRET}
    enabled: true

apps:
  - client_id: grafana
    name: Grafana
    redirect_uris:
      - https://grafana.example.com/login/generic_oauth
"""

let private parseOk yaml =
    match ConfigFile.parse yaml with
    | Ok document -> document
    | Error e -> failwith e.Describe

let private parseErr yaml =
    match ConfigFile.parse yaml with
    | Error e -> e.Describe
    | Ok _ -> failwith "expected the document to be rejected"

[<Fact>]
let ``a valid document parses`` () =
    let document = parseOk valid
    Assert.Equal(1, document.Version)
    Assert.Single document.Connectors |> ignore
    Assert.Single document.Apps |> ignore

[<Fact>]
let ``a LITERAL secret is an error, not a warning`` () =
    // This rule is the whole reason the file is safe to keep in git. A warning
    // would be scrolled past; an error cannot be.
    let message =
        parseErr (valid.Replace("${UMBRAL_DISCORD_SECRET}", "actual-secret-value"))

    Assert.Contains("connectors[0].client_secret", message)
    Assert.Contains("literal", message)
    // And it says what to write instead.
    Assert.Contains("${UMBRAL_DISCORD_SECRET}", message)

[<Fact>]
let ``every problem is reported, not just the first`` () =
    // An operator fixing a file by hand should get the whole list rather than
    // discover the next problem on each restart.
    let broken =
        """
version: 2
connectors:
  - slug: "Not A Slug"
    kind: slack
    client_id: ""
    client_secret: literal
apps:
  - client_id: ""
    name: ""
    redirect_uris: []
"""

    let message = parseErr broken
    Assert.Contains("version", message)
    Assert.Contains("connectors[0].slug", message)
    Assert.Contains("connectors[0].kind", message)
    Assert.Contains("connectors[0].client_id", message)
    Assert.Contains("connectors[0].client_secret", message)
    Assert.Contains("apps[0].name", message)
    Assert.Contains("apps[0].redirect_uris", message)
    // And it says nothing was changed, so the operator knows where they stand.
    Assert.Contains("Nothing has been changed", message)

[<Fact>]
let ``a wildcard redirect URI is refused`` () =
    // Umbral matches exactly (FR-003), so a wildcard here would be silently
    // ignored at match time — worse than refusing it now.
    let message =
        parseErr (valid.Replace("https://grafana.example.com/login/generic_oauth", "https://*.example.com/cb"))

    Assert.Contains("wildcard", message)
    Assert.Contains("apps[0].redirect_uris[0]", message)

[<Fact>]
let ``a relative redirect URI is refused`` () =
    let message = parseErr (valid.Replace("https://grafana.example.com/login/generic_oauth", "/callback"))
    Assert.Contains("absolute", message)

[<Fact>]
let ``a duplicate slug is refused`` () =
    let message =
        parseErr (
            valid.Replace(
                "apps:",
                """  - slug: discord
    kind: github
    client_id: "other"
    client_secret: ${UMBRAL_OTHER_SECRET}
apps:"""
            )
        )

    Assert.Contains("more than once", message)

[<Fact>]
let ``malformed YAML is refused without a stack trace`` () =
    let message = parseErr "version: 1\n  connectors: [[[\n"
    Assert.Contains("not valid YAML", message)
    Assert.Contains("Nothing has been changed", message)

[<Fact>]
let ``a missing environment variable names the variable`` () =
    let document = parseOk valid

    match ConfigFile.resolveSecrets (fun _ -> (null: string | null)) document with
    | Ok _ -> failwith "expected the missing variable to be reported"
    | Error missing ->
        let message = ConfigFile.describeMissing missing
        Assert.Contains("UMBRAL_DISCORD_SECRET", message)
        Assert.Contains("connectors[0].client_secret", message)
        Assert.Contains("Nothing has been changed", message)

[<Fact>]
let ``every missing variable is reported together`` () =
    let twoConnectors =
        valid.Replace(
            "apps:",
            """  - slug: github
    kind: github
    client_id: "gh"
    client_secret: ${UMBRAL_GITHUB_SECRET}
apps:"""
        )

    match ConfigFile.resolveSecrets (fun _ -> (null: string | null)) (parseOk twoConnectors) with
    | Ok _ -> failwith "expected the missing variables to be reported"
    | Error missing ->
        // Learning about three forgotten variables once beats learning about
        // them one restart at a time.
        Assert.Equal(2, missing.Length)

[<Fact>]
let ``resolved secrets reach the document`` () =
    let read (name: string) : string | null =
        if name = "UMBRAL_DISCORD_SECRET" then "the-real-secret" else null

    match ConfigFile.resolveSecrets read (parseOk valid) with
    | Error _ -> failwith "expected the variable to resolve"
    | Ok document -> Assert.Equal("the-real-secret", document.Connectors.Head.ClientSecret)

// ---------------------------------------------------------------------------
// Applying
// ---------------------------------------------------------------------------

let private withDb (body: Database.DbConfig -> Secrets.ISecretStore -> Task<unit>) =
    task {
        let path = Path.Combine(Path.GetTempPath(), "umbral-cfg-" + Guid.NewGuid().ToString "N" + ".db")
        let config: Database.DbConfig = { DataSource = path }

        let secrets =
            Secrets.LocalSecretStore(DataProtectionProvider.Create "Umbral.Tests") :> Secrets.ISecretStore

        try
            do! Database.initializeSchema config
            do! body config secrets
        finally
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            for suffix in [ ""; "-wal"; "-shm" ] do
                if File.Exists(path + suffix) then File.Delete(path + suffix)
    }

let private resolved () =
    match ConfigFile.resolveSecrets (fun _ -> ("the-real-secret": string | null)) (parseOk valid) with
    | Ok document -> document
    | Error _ -> failwith "unreachable"

[<Fact>]
let ``applying creates the connector`` () =
    withDb (fun config secrets ->
        task {
            let! count = ConfigFile.applyConnectors config TimeProvider.System secrets (resolved ())
            Assert.Equal(1, count)

            match! ConnectorStore.findBySlug config "discord" with
            | None -> failwith "the connector was not created"
            | Some c ->
                Assert.Equal("1234567890", c.ClientId)
                Assert.True c.IsEnabled

                // The secret is usable, not merely stored.
                match Secrets.SecretRef.Decode c.ClientSecretRef with
                | Error e -> failwith e
                | Ok reference ->
                    let! recovered = secrets.Get reference
                    Assert.Equal(Some "the-real-secret", recovered)
        })

[<Fact>]
let ``applying twice changes nothing and duplicates nothing`` () =
    withDb (fun config secrets ->
        task {
            // FR-027: the file is declarative, so applying it is a statement of
            // intent rather than an action with a count.
            let! _ = ConfigFile.applyConnectors config TimeProvider.System secrets (resolved ())
            let! _ = ConfigFile.applyConnectors config TimeProvider.System secrets (resolved ())

            let! all = ConnectorStore.listAll config
            Assert.Single all |> ignore
        })

[<Fact>]
let ``applying preserves when the connector was first created`` () =
    withDb (fun config secrets ->
        task {
            let! _ = ConfigFile.applyConnectors config TimeProvider.System secrets (resolved ())
            let! first = ConnectorStore.findBySlug config "discord"

            do! Task.Delay 10
            let! _ = ConfigFile.applyConnectors config TimeProvider.System secrets (resolved ())
            let! second = ConnectorStore.findBySlug config "discord"

            match first, second with
            | Some a, Some b -> Assert.Equal(a.CreatedUtc, b.CreatedUtc)
            | _ -> failwith "the connector vanished"
        })

// ---------------------------------------------------------------------------
// Exporting
// ---------------------------------------------------------------------------

[<Fact>]
let ``an export round-trips through parse`` () =
    withDb (fun config secrets ->
        task {
            let! _ = ConfigFile.applyConnectors config TimeProvider.System secrets (resolved ())
            let! connectors = ConnectorStore.listAll config

            let yaml =
                ConfigFile.export
                    connectors
                    [ "grafana", "Grafana", [ "https://grafana.example.com/cb" ], [] ]

            // FR-029: what comes out must be something that can go back in.
            let reparsed = parseOk yaml
            Assert.Single reparsed.Connectors |> ignore
            Assert.Equal("discord", reparsed.Connectors.Head.Slug)
            Assert.Single reparsed.Apps |> ignore
        })

[<Fact>]
let ``an export contains no secret`` () =
    withDb (fun config secrets ->
        task {
            let! _ = ConfigFile.applyConnectors config TimeProvider.System secrets (resolved ())
            let! connectors = ConnectorStore.listAll config
            let yaml = ConfigFile.export connectors []

            // SC-008. The export is the likeliest thing an operator pastes into
            // an issue or commits without reading.
            Assert.DoesNotContain("the-real-secret", yaml)
            Assert.Contains("${UMBRAL_DISCORD_SECRET}", yaml)
        })

[<Fact>]
let ``an export says that app secrets are not included`` () =
    let yaml = ConfigFile.export [] [ "grafana", "Grafana", [ "https://g.example.com/cb" ], [] ]

    // The one asymmetry in the round trip, and it is forced: OpenIddict hashes
    // an app secret on write, so there is nothing to read back. Said where
    // someone will read it rather than discovered on a restore.
    Assert.Contains("NOT exported", yaml)

// ---------------------------------------------------------------------------
// The shared redirect-URI rule
//
// Used by both the configuration file and the Apps admin form. It lived in two
// places until the copies diverged, so these cases pin the behaviour both of
// them now depend on.
// ---------------------------------------------------------------------------

[<Theory>]
// Frunchy's real callback: http on localhost must be usable, or no one can
// develop an integration.
[<InlineData("http://localhost:5099/signin-umbral")>]
[<InlineData("https://grafana.example.com/login/generic_oauth")>]
// A native app's custom scheme is legitimate and must not be refused.
[<InlineData("com.example.app://callback")>]
let ``an acceptable redirect URI is accepted`` (value: string) =
    match acceptableRedirectUri value with
    | Ok _ -> ()
    | Error why -> failwith $"{value} should be accepted: {why}"

[<Theory>]
// On Unix this parses as an absolute file:// uri, which is the trap.
[<InlineData("/signin-umbral", "absolute")>]
[<InlineData("https://*.example.com/cb", "wildcard")>]
[<InlineData("https://app.example.com/cb#frag", "fragment")>]
[<InlineData("", "empty")>]
let ``an unacceptable redirect URI is refused, and says why`` (value: string) (because: string) =
    match acceptableRedirectUri value with
    | Ok _ -> failwith $"{value} should be refused"
    | Error why -> Assert.Contains(because, why, StringComparison.OrdinalIgnoreCase)
