module Umbral.Tests.ConfigFileTests

open System
open System.IO
open System.Net
open System.Threading.Tasks
open Microsoft.AspNetCore.DataProtection
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open OpenIddict.Abstractions
open Xunit
open Umbral.Domains.ConfigDocument
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

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
                    [ { ClientId = "grafana"
                        Name = "Grafana"
                        Confidential = true
                        RedirectUris = [ "https://grafana.example.com/cb" ]
                        PostLogoutRedirectUris = []
                        Choice = Umbral.Domains.Connectors.AllShared } ]

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
    let yaml =
        ConfigFile.export
            []
            [ { ClientId = "grafana"
                Name = "Grafana"
                Confidential = true
                RedirectUris = [ "https://g.example.com/cb" ]
                PostLogoutRedirectUris = []
                Choice = Umbral.Domains.Connectors.AllShared } ]

    // The one asymmetry in the round trip, and it is forced: OpenIddict hashes
    // an app secret on write, so there is nothing to read back. Said where
    // someone will read it rather than discovered on a restore.
    Assert.Contains("NOT exported", yaml)

// ---------------------------------------------------------------------------
// Applying the `apps:` section
//
// Everything below starts the real application with a configuration file, so
// an app the file declares is exercised the way an operator's would be: through
// the real OpenIddict manager and, for the one that matters most, a real
// authorization-code login.
// ---------------------------------------------------------------------------

let private appsFile (secretVariable: string) (redirect: string) =
    $"""version: 1

connectors:
  - slug: fake
    kind: fake
    client_id: fake
    client_secret: ${{UMBRAL_FAKE_SECRET}}

apps:
  - client_id: grafana
    name: Grafana
    client_secret: ${{{secretVariable}}}
    redirect_uris:
      - {redirect}
  - client_id: mobile
    name: Mobile
    redirect_uris:
      - com.example.app://callback
"""

let private grafanaSecret = "grafana-secret-from-the-environment"

let private withFile (yaml: string) (variables: (string * string) list) (body: Fixtures.TestApp -> Task<unit>) =
    task {
        let path = Path.Combine(Path.GetTempPath(), "umbral-cfg-" + Guid.NewGuid().ToString "N" + ".yaml")
        File.WriteAllText(path, yaml)

        try
            use! app =
                Fixtures.createAppWith
                    ([ "UMBRAL:CONFIG_FILE", path; "UMBRAL_FAKE_SECRET", "unused-fake-secret" ]
                     @ variables
                     @ fakeSettings)
                    ignore

            do! body app
        finally
            if File.Exists path then File.Delete path
    }

let private applicationsOf (app: Fixtures.TestApp) =
    app.Services.GetRequiredService<IOpenIddictApplicationManager>()

/// The application, or a failure naming the client id: the manager hands back
/// null for a miss, which every later call on it would reject less helpfully.
let private findApp (manager: IOpenIddictApplicationManager) (clientId: string) =
    task {
        match! manager.FindByClientIdAsync clientId with
        | null -> return failwith $"no application with client id {clientId}"
        | application -> return application
    }

let private countApplications (app: Fixtures.TestApp) =
    task {
        let! count = scalar app "SELECT COUNT(*) FROM OpenIddictApplications" []
        return int count
    }

/// What a restart does: read the file again, with whatever the environment holds now.
let private applyAgain (app: Fixtures.TestApp) (yaml: string) (variables: (string * string) list) =
    task {
        let environment = ("UMBRAL_FAKE_SECRET", "unused-fake-secret") :: variables

        let read (name: string) : string | null =
            environment |> List.tryFind (fst >> (=) name) |> Option.map snd |> Option.toObj

        let resolved =
            match ConfigFile.resolveSecrets read (parseOk yaml) with
            | Ok document -> document
            | Error missing -> failwith (ConfigFile.describeMissing missing)

        return! ConfigFile.applyApps app.DbConfig app.Clock (applicationsOf app) resolved
    }

[<Fact>]
let ``an app declared in the file exists after startup, with the shape the screen would give it`` () =
    withFile (appsFile "UMBRAL_GRAFANA_SECRET" RedirectUri) [ "UMBRAL_GRAFANA_SECRET", grafanaSecret ] (fun app ->
        task {
            let manager = applicationsOf app

            let! grafana = findApp manager "grafana"
            Assert.NotNull grafana
            let! clientType = manager.GetClientTypeAsync grafana
            Assert.Equal(OpenIddictConstants.ClientTypes.Confidential, clientType)
            let! redirects = manager.GetRedirectUrisAsync grafana
            Assert.Equal<string list>([ RedirectUri ], List.ofSeq redirects)
            let! requirements = manager.GetRequirementsAsync grafana
            // PKCE is not optional for an app the screen creates, so it is not
            // optional for one the file creates.
            Assert.Contains(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange, requirements)

            // No secret line: a public client, and it is never given one.
            let! mobile = findApp manager "mobile"
            let! mobileType = manager.GetClientTypeAsync mobile
            Assert.Equal(OpenIddictConstants.ClientTypes.Public, mobileType)

            // The secret is stored as OpenIddict's hash, never as the value.
            let! stored = scalar app "SELECT ClientSecret FROM OpenIddictApplications WHERE ClientId = 'grafana'" []
            Assert.DoesNotContain(grafanaSecret, stored)
        })

[<Fact>]
let ``applying the apps twice changes nothing and duplicates nothing`` () =
    withFile (appsFile "UMBRAL_GRAFANA_SECRET" RedirectUri) [ "UMBRAL_GRAFANA_SECRET", grafanaSecret ] (fun app ->
        task {
            // The first apply ran at startup. A restart re-applies the file.
            let! hashBefore = scalar app "SELECT ClientSecret FROM OpenIddictApplications WHERE ClientId = 'grafana'" []
            let! changed = applyAgain app (appsFile "UMBRAL_GRAFANA_SECRET" RedirectUri) [ "UMBRAL_GRAFANA_SECRET", grafanaSecret ]
            Assert.Equal(0, changed)

            let! count = countApplications app
            Assert.Equal(2, count)

            // The stored hash is untouched: an unchanged secret is verified,
            // not re-hashed (a re-hash would also log a rotation per restart).
            let! hashAfter = scalar app "SELECT ClientSecret FROM OpenIddictApplications WHERE ClientId = 'grafana'" []
            Assert.Equal(hashBefore, hashAfter)

            let! rows =
                scalar app "SELECT COUNT(*) FROM security_events WHERE event_type IN ('app_updated', 'app_secret_rotated')" []

            Assert.Equal("0", rows)
        })

[<Fact>]
let ``an app from the file completes a real login with the secret from the environment`` () =
    withFile (appsFile "UMBRAL_GRAFANA_SECRET" RedirectUri) [ "UMBRAL_GRAFANA_SECRET", grafanaSecret ] (fun app ->
        task {
            let operator = Browser(app.Client)
            do! claimOperator app operator

            let! signedIn = signInViaFake (Browser(app.Client)) "grafana" "openid" "fake-user-1" "sam@example.com"

            let code =
                match signedIn with
                | Ok code -> code
                | Error where -> failwith $"sign-in did not complete: {where}"

            // The wrong secret is refused; the one in the environment works.
            let! wrong = exchangeCode app "grafana" "not-the-secret-at-all" code
            Assert.NotEqual(HttpStatusCode.OK, wrong.StatusCode)

            let! signedInAgain = signInViaFake (Browser(app.Client)) "grafana" "openid" "fake-user-1" "sam@example.com"
            let! ok = exchangeCode app "grafana" grafanaSecret (match signedInAgain with Ok c -> c | Error w -> failwith w)
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode)
        })

[<Fact>]
let ``a changed secret variable takes effect on the next apply, and the old secret stops working`` () =
    withFile (appsFile "UMBRAL_GRAFANA_SECRET" RedirectUri) [ "UMBRAL_GRAFANA_SECRET", grafanaSecret ] (fun app ->
        task {
            let! changed =
                applyAgain app (appsFile "UMBRAL_GRAFANA_SECRET" RedirectUri) [ "UMBRAL_GRAFANA_SECRET", "a-brand-new-grafana-secret" ]

            Assert.Equal(1, changed)

            let manager = applicationsOf app
            let! grafana = findApp manager "grafana"
            let! old = manager.ValidateClientSecretAsync(grafana, grafanaSecret)
            let! fresh = manager.ValidateClientSecretAsync(grafana, "a-brand-new-grafana-secret")
            Assert.False old
            Assert.True fresh

            // Recorded by name and by actor, with no value anywhere in it.
            let! rotated =
                scalar app "SELECT actor || '|' || COALESCE(detail, '') FROM security_events WHERE event_type = 'app_secret_rotated'" []

            Assert.Equal("system|source=config_file", rotated)
        })

[<Fact>]
let ``a changed redirect URI is updated and the app still authenticates`` () =
    withFile (appsFile "UMBRAL_GRAFANA_SECRET" RedirectUri) [ "UMBRAL_GRAFANA_SECRET", grafanaSecret ] (fun app ->
        task {
            let moved = "https://grafana.example.com/moved"
            let! changed = applyAgain app (appsFile "UMBRAL_GRAFANA_SECRET" moved) [ "UMBRAL_GRAFANA_SECRET", grafanaSecret ]
            Assert.Equal(1, changed)

            let manager = applicationsOf app
            let! grafana = findApp manager "grafana"
            let! redirects = manager.GetRedirectUrisAsync grafana
            Assert.Equal<string list>([ moved ], List.ofSeq redirects)

            // Updating attributes must not blank or re-hash the secret.
            let! stillValid = manager.ValidateClientSecretAsync(grafana, grafanaSecret)
            Assert.True stillValid

            let! updated = scalar app "SELECT detail FROM security_events WHERE event_type = 'app_updated'" []
            Assert.Equal("source=config_file;changed=redirect_uris", updated)
        })

[<Fact>]
let ``an app the file does not list is left alone`` () =
    withFile (appsFile "UMBRAL_GRAFANA_SECRET" RedirectUri) [ "UMBRAL_GRAFANA_SECRET", grafanaSecret ] (fun app ->
        task {
            let operator = Browser(app.Client)
            do! claimOperator app operator
            let! screenClientId, _ = registerApp operator "Made on the screen"

            // A restart re-applies the file. The file describes what must
            // exist; it does not delete what an operator added by hand.
            let! _ = applyAgain app (appsFile "UMBRAL_GRAFANA_SECRET" RedirectUri) [ "UMBRAL_GRAFANA_SECRET", grafanaSecret ]

            let! _ = findApp (applicationsOf app) screenClientId
            let! count = countApplications app
            Assert.Equal(3, count)
        })

[<Fact>]
let ``a secret variable that is too short is refused, naming the variable`` () =
    match
        ConfigFile.resolveSecrets
            (fun _ -> ("short": string | null))
            (parseOk (appsFile "UMBRAL_GRAFANA_SECRET" RedirectUri))
    with
    | Ok _ -> failwith "expected the short secret to be refused"
    | Error problems ->
        let message = ConfigFile.describeMissing problems
        Assert.Contains("apps[0].client_secret", message)
        Assert.Contains("UMBRAL_GRAFANA_SECRET", message)
        Assert.Contains("16", message)

[<Fact>]
let ``a bad app entry stops startup, names the entry, and applies nothing`` () =
    task {
        let dataDirectory = Path.Combine(Path.GetTempPath(), "umbral-bad-" + Guid.NewGuid().ToString "N")
        Directory.CreateDirectory dataDirectory |> ignore

        // The first app and the connector are fine; the SECOND app's redirect
        // is a wildcard. Whole-document validation means the good entries
        // before it are not written either (FR-030).
        let yaml =
            (appsFile "UMBRAL_GRAFANA_SECRET" RedirectUri)
                .Replace("com.example.app://callback", "https://*.example.com/cb")

        File.WriteAllText(Path.Combine(dataDirectory, "umbral.yaml"), yaml)

        let app =
            Umbral.Program.buildApp Array.empty (fun builder ->
                builder.Configuration.AddInMemoryCollection(
                    dict
                        [ "UMBRAL:PUBLIC_ORIGIN", Fixtures.TestOrigin
                          "UMBRAL:DATA_DIRECTORY", dataDirectory
                          "UMBRAL_GRAFANA_SECRET", grafanaSecret
                          "UMBRAL_FAKE_SECRET", "unused-fake-secret" ]
                )
                |> ignore)

        try
            let! thrown =
                task {
                    try
                        do! Umbral.Program.initialize app
                        return None
                    with ex ->
                        return Some ex.Message
                }

            match thrown with
            | None -> failwith "startup should have been refused"
            | Some message ->
                Assert.Contains("apps[1].redirect_uris[0]", message)
                Assert.Contains("wildcard", message)
                Assert.Contains("Nothing has been changed", message)

            // And it is true: neither app, nor the connector, was written.
            use! connection = Database.openConnection (app.Services.GetRequiredService<Database.DbConfig>())
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT (SELECT COUNT(*) FROM OpenIddictApplications) + (SELECT COUNT(*) FROM connectors)"
            let! count = command.ExecuteScalarAsync()
            Assert.Equal(0L, count :?> int64)
        finally
            (app :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
            Directory.Delete(dataDirectory, true)
    }

[<Fact>]
let ``an export of apps from the product round-trips back through the file`` () =
    withFile (appsFile "UMBRAL_GRAFANA_SECRET" RedirectUri) [ "UMBRAL_GRAFANA_SECRET", grafanaSecret ] (fun app ->
        task {
            let operator = Browser(app.Client)
            do! claimOperator app operator

            let! export = operator.Get "/admin/config"
            let! yaml = export.Content.ReadAsStringAsync()

            // Secrets are never in it; each web app names a variable instead,
            // and the public client has none.
            Assert.DoesNotContain(grafanaSecret, yaml)
            Assert.Contains("client_secret: ${UMBRAL_GRAFANA_SECRET}", yaml)

            let document = parseOk yaml
            let byId = document.Apps |> List.map (fun a -> a.ClientId, a) |> Map.ofList
            Assert.Equal(Some "${UMBRAL_GRAFANA_SECRET}", byId["grafana"].ClientSecret)
            Assert.Equal(None, byId["mobile"].ClientSecret)
            Assert.Equal<string list>([ RedirectUri ], byId["grafana"].RedirectUris)

            // Applying what was exported, given the same environment, changes nothing.
            let! changed = applyAgain app yaml [ "UMBRAL_GRAFANA_SECRET", grafanaSecret ]
            Assert.Equal(0, changed)
        })

// ---------------------------------------------------------------------------
// The two optional keys of feature 003: `shared` and `connectors` (FR-016 to
// FR-018). Nothing else about the document changed, and `version` stays 1.
// ---------------------------------------------------------------------------

/// Two fake connectors, one of them not shared, and three public apps: one
/// with no `connectors` key, one with a chosen set, one with an empty set.
/// Public apps keep the file free of secret variables beyond the connectors'.
let private choicesFile =
    """version: 1

connectors:
  - slug: fake
    kind: fake
    client_id: fake
    client_secret: ${UMBRAL_FAKE_SECRET}
  - slug: fake-private
    kind: fake
    display_name: Private
    client_id: fake-private
    client_secret: ${UMBRAL_FAKE_PRIVATE_SECRET}
    shared: false

apps:
  - client_id: everyone
    name: Everyone
    redirect_uris:
      - https://everyone.example.com/cb
  - client_id: chosen
    name: Chosen
    redirect_uris:
      - https://chosen.example.com/cb
    connectors:
      - fake-private
  - client_id: nothing
    name: Nothing
    redirect_uris:
      - https://nothing.example.com/cb
    connectors: []
"""

let private choicesVariables = [ "UMBRAL_FAKE_PRIVATE_SECRET", "unused-private-secret" ]

[<Fact>]
let ``shared and connectors default when the keys are left out`` () =
    let document = parseOk valid

    // FR-016: a document written before the feature means what it meant then.
    Assert.True document.Connectors.Head.Shared
    Assert.Equal(None, document.Apps.Head.Connectors)

[<Fact>]
let ``shared and connectors are read when the keys are there`` () =
    let document = parseOk choicesFile
    Assert.Equal<bool list>([ true; false ], document.Connectors |> List.map _.Shared)

    Assert.Equal<string list option list>(
        [ None; Some [ "fake-private" ]; Some [] ],
        document.Apps |> List.map _.Connectors
    )

[<Fact>]
let ``an empty connectors list is a chosen set, not the absence of one`` () =
    let document = parseOk choicesFile
    let byId = document.Apps |> List.map (fun a -> a.ClientId, a) |> Map.ofList

    // The two must never be confused: the second would widen an app that was
    // deliberately limited to nothing.
    Assert.Equal(Some [], byId["nothing"].Connectors)
    Assert.NotEqual(byId["everyone"].Connectors, byId["nothing"].Connectors)

[<Fact>]
let ``a connectors entry that names no connector of the file is refused, with its path`` () =
    let message =
        parseErr (choicesFile.Replace("    connectors:\n      - fake-private", "    connectors:\n      - fake\n      - discord-frunchy"))

    // FR-018: the app entry and the connector, and what to do about it.
    Assert.Contains("apps[1].connectors[1]", message)
    Assert.Contains("\"discord-frunchy\"", message)
    Assert.Contains("not a connector defined in this file", message)
    Assert.Contains("Nothing has been changed", message)

[<Fact>]
let ``a slug repeated in one app's connectors is refused, with its path`` () =
    let message =
        parseErr (choicesFile.Replace("    connectors:\n      - fake-private", "    connectors:\n      - fake-private\n      - fake-private"))

    Assert.Contains("apps[1].connectors", message)
    Assert.Contains("\"fake-private\" appears more than once", message)

[<Fact>]
let ``the same slug in two different apps is fine`` () =
    let twice =
        choicesFile.Replace("    connectors: []", "    connectors:\n      - fake-private")

    Assert.Equal(Some [ "fake-private" ], (parseOk twice).Apps[2].Connectors)

[<Fact>]
let ``applying the connectors sets shared, and a removed key sets it back`` () =
    withDb (fun config secrets ->
        task {
            let resolve yaml =
                match ConfigFile.resolveSecrets (fun _ -> ("a-real-secret": string | null)) (parseOk yaml) with
                | Ok document -> document
                | Error _ -> failwith "unreachable"

            let! _ = ConfigFile.applyConnectors config TimeProvider.System secrets (resolve choicesFile)

            let! before = ConnectorStore.findBySlug config "fake-private"
            Assert.False before.Value.IsShared

            // The key removed from the file: the setting goes back to the
            // default rather than being left where the last file put it.
            let! _ =
                ConfigFile.applyConnectors config TimeProvider.System secrets (resolve (choicesFile.Replace("    shared: false\n", "")))

            let! after = ConnectorStore.findBySlug config "fake-private"
            Assert.True after.Value.IsShared
        })

[<Fact>]
let ``applying the choices twice writes no second event and rewrites nothing`` () =
    withFile choicesFile choicesVariables (fun app ->
        task {
            // The first apply ran at startup. A restart applies the file again,
            // with the same environment.
            let! eventsBefore = scalar app "SELECT COUNT(*) FROM security_events" []

            let environment = ("UMBRAL_FAKE_SECRET", "unused-fake-secret") :: choicesVariables

            let read (name: string) : string | null =
                environment |> List.tryFind (fst >> (=) name) |> Option.map snd |> Option.toObj

            let resolved =
                match ConfigFile.resolveSecrets read (parseOk choicesFile) with
                | Ok document -> document
                | Error _ -> failwith "unreachable"

            let secrets = app.Services.GetRequiredService<Secrets.ISecretStore>()
            let! _ = ConfigFile.applyConnectors app.DbConfig app.Clock secrets resolved
            let! changed = applyAgain app choicesFile choicesVariables

            Assert.Equal(0, changed)

            // `shared` and `connectors` are in the change lists, and an
            // unchanged value must not be what makes them fire.
            let! eventsAfter = scalar app "SELECT COUNT(*) FROM security_events" []
            Assert.Equal(eventsBefore, eventsAfter)

            let! choices = AppConnectors.allChoices app.DbConfig
            Assert.Equal(2, choices.Count)
        })

[<Fact>]
let ``a changed choice is applied, recorded once, and a removed key goes back to all shared`` () =
    withFile choicesFile choicesVariables (fun app ->
        task {
            let! initial = AppConnectors.allChoices app.DbConfig
            Assert.Equal(Umbral.Domains.Connectors.Only(set [ "fake-private" ]), initial["chosen"])
            Assert.Equal(Umbral.Domains.Connectors.Only Set.empty, initial["nothing"])
            Assert.False(initial.ContainsKey "everyone")

            let edited =
                choicesFile
                    .Replace("    connectors:\n      - fake-private", "    connectors:\n      - fake")
                    .Replace("    connectors: []\n", "")

            let! changed = applyAgain app edited choicesVariables
            Assert.Equal(2, changed)

            let! choices = AppConnectors.allChoices app.DbConfig
            Assert.Equal(Umbral.Domains.Connectors.Only(set [ "fake" ]), choices["chosen"])
            Assert.False(choices.ContainsKey "nothing")

            let! details = scalar app "SELECT GROUP_CONCAT(detail, '|') FROM security_events WHERE event_type = 'app_updated'" []
            Assert.Equal("source=config_file;changed=connectors|source=config_file;changed=connectors", details)
        })

[<Fact>]
let ``an export leaves both keys out at their defaults`` () =
    withDb (fun config secrets ->
        task {
            let! _ = ConfigFile.applyConnectors config TimeProvider.System secrets (resolved ())
            let! connectors = ConnectorStore.listAll config

            let yaml =
                ConfigFile.export
                    connectors
                    [ { ClientId = "grafana"
                        Name = "Grafana"
                        Confidential = false
                        RedirectUris = [ "https://grafana.example.com/cb" ]
                        PostLogoutRedirectUris = []
                        Choice = Umbral.Domains.Connectors.AllShared } ]

            // Nothing new appears for an instance that never used the feature.
            Assert.DoesNotContain("shared:", yaml)
            Assert.DoesNotContain("    connectors:", yaml)
        })

[<Fact>]
let ``an export then apply on a fresh instance reproduces both settings`` () =
    task {
        let mutable exported = ""

        // The first instance: configured from the file, exported from the product.
        do!
            withFile choicesFile choicesVariables (fun app ->
                task {
                    let operator = Browser(app.Client)
                    do! claimOperator app operator
                    let! response = operator.Get "/admin/config"
                    let! yaml = response.Content.ReadAsStringAsync()
                    exported <- yaml
                })

        // Written only where it differs from the default; `[]` is kept.
        Assert.Contains("    shared: false", exported)
        Assert.Contains("    connectors: []", exported)
        Assert.Equal(1, exported.Split("shared:").Length - 1)

        let reparsed = parseOk exported
        let bySlug = reparsed.Connectors |> List.map (fun c -> c.Slug, c.Shared) |> Map.ofList
        Assert.Equal<bool list>([ true; false ], [ bySlug["fake"]; bySlug["fake-private"] ])

        // FR-017: a fresh instance applying the export ends up with the same choices.
        do!
            withFile exported choicesVariables (fun app ->
                task {
                    let! priv = ConnectorStore.findBySlug app.DbConfig "fake-private"
                    Assert.False priv.Value.IsShared

                    let! shared = ConnectorStore.findBySlug app.DbConfig "fake"
                    Assert.True shared.Value.IsShared

                    let! choices = AppConnectors.allChoices app.DbConfig
                    Assert.Equal(Umbral.Domains.Connectors.Only(set [ "fake-private" ]), choices["chosen"])
                    Assert.Equal(Umbral.Domains.Connectors.Only Set.empty, choices["nothing"])
                    Assert.False(choices.ContainsKey "everyone")
                })
    }

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
