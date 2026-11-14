/// Spec 019: a declaration read at start becomes apps and the passkey connector,
/// in one transaction with its events, or changes nothing.
///
/// Every test is one or more starts on one data directory: the first instance is
/// disposed before the next starts, as a stopped process would be, and what
/// survived is what a restart would find. State is read from `security_events`,
/// `declared_items`, `connectors` and OpenIddict itself, never from a screen.
module Umbral.Tests.DeclarationApplyTests

open System
open System.IO
open System.Net
open System.Text.RegularExpressions
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open OpenIddict.Abstractions
open Xunit
open Umbral.Domains
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

// 32 characters or more, with a marker no event or log line may ever carry.
let private webSecret = "SECRETWEB-0123456789-abcdefghijklmnop"
let private otherSecret = "SECRETOTHER-0123456789-abcdefghijklmn"
let private secrets: LogRedactionTests.Secret list =
    [ { Label = "web secret"; Value = webSecret }
      { Label = "other secret"; Value = otherSecret } ]

let private refusedSummary =
    "The declaration in UMBRAL__DECLARATION was refused; nothing was changed. Fix the problems above and start again."

// ---------------------------------------------------------------------------
// Building declarations
// ---------------------------------------------------------------------------

type private AppSpec =
    { Key: string
      Name: string
      Type: string
      Redirects: string list
      Post: string list
      Connectors: string }

let private web =
    { Key = "web"
      Name = "Web app"
      Type = "web"
      Redirects = [ "https://web.example.com/cb" ]
      Post = [ "https://web.example.com/out" ]
      Connectors = "\"all-shared\"" }

let private spa =
    { Key = "spa"
      Name = "Spa app"
      Type = "spa"
      Redirects = [ "https://spa.example.com/cb" ]
      Post = []
      Connectors = "\"all-shared\"" }

let private quoted (items: string list) =
    items |> List.map (fun item -> $"\"{item}\"") |> String.concat ", "

let private appJson (app: AppSpec) =
    $"""{{ "key": "{app.Key}", "name": "{app.Name}", "type": "{app.Type}", "redirectUris": [{quoted app.Redirects}], "postLogoutRedirectUris": [{quoted app.Post}], "connectors": {app.Connectors} }}"""

/// A declaration, with `signUp` of the passkey connector when it is declared.
let private declaration (signUp: string option) (apps: AppSpec list) =
    let passkeys =
        match signUp with
        | Some mode -> $"""\"passkeys\": {{ "signUp": "{mode}" }}, """.Replace("\\\"", "\"")
        | None -> ""

    $"""{{ "version": 1, {passkeys}"apps": [ {apps |> List.map appJson |> String.concat ", "} ] }}"""

/// The settings of a start: the declaration and the secret variables.
let private settingsFor (json: string) (secretVariables: (string * string) list) =
    ("UMBRAL:DECLARATION", json)
    :: [ for key, value in secretVariables -> $"UMBRAL:APP_SECRETS:{key}", value ]

// ---------------------------------------------------------------------------
// Running starts
// ---------------------------------------------------------------------------

let private withDirectory (body: string -> Task<unit>) =
    task {
        let directory = Fixtures.freshDataDirectory ()
        Directory.CreateDirectory directory |> ignore

        try
            do! body directory
        finally
            SqliteConnection.ClearAllPools()

            try
                if Directory.Exists directory then
                    Directory.Delete(directory, true)
            with _ ->
                ()
    }

let private dbOf (directory: string) : Database.DbConfig =
    { DataSource = Path.Combine(directory, "umbral.db") }

/// One run of the instance, with its log captured, gone again before the next.
let private runOn
    (directory: string)
    (settings: (string * string) list)
    (body: Fixtures.TestApp -> LogRedactionTests.CapturingProvider -> Task<'a>)
    =
    task {
        let logs = new LogRedactionTests.CapturingProvider()
        use! app = Fixtures.createAppIn directory settings (fun builder -> builder.Logging.AddProvider logs |> ignore)
        return! body app logs
    }

/// A start that must be refused or fail: the exception, and the log it left.
let private startFails (directory: string) (settings: (string * string) list) =
    task {
        let logs = new LogRedactionTests.CapturingProvider()

        let! outcome =
            task {
                try
                    let! app = Fixtures.createAppIn directory settings (fun builder -> builder.Logging.AddProvider logs |> ignore)
                    return Choice1Of2 app
                with error ->
                    return Choice2Of2 error
            }

        match outcome with
        | Choice1Of2 app ->
            do! (app :> IAsyncDisposable).DisposeAsync()
            return failwith "the start should have failed"
        | Choice2Of2 error -> return error, logs
    }

/// A start with no declaration: it creates the database and nothing else.
let private bare (directory: string) =
    runOn directory [] (fun _ _ -> Task.FromResult())

// ---------------------------------------------------------------------------
// Reading state
// ---------------------------------------------------------------------------

let private query (config: Database.DbConfig) (statement: string) (read: SqliteDataReader -> 'a) =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- statement
        use! reader = command.ExecuteReaderAsync()
        let rows = ResizeArray<'a>()

        while! reader.ReadAsync() do
            rows.Add(read reader)

        return List.ofSeq rows
    }

let private scalarOf (config: Database.DbConfig) (statement: string) =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- statement
        let! value = command.ExecuteScalarAsync()
        return if isNull value then "" else string value
    }

/// Every security event, oldest first: type, actor, client ID, connector slug, detail.
let private events (config: Database.DbConfig) =
    query
        config
        "SELECT event_type, actor, coalesce(client_id, ''), coalesce(connector_slug, ''), coalesce(detail, '') FROM security_events ORDER BY id"
        (fun r -> r.GetString 0, r.GetString 1, r.GetString 2, r.GetString 3, r.GetString 4)

let private eventCount (config: Database.DbConfig) =
    task {
        let! count = scalarOf config "SELECT count(*) FROM security_events"
        return int count
    }

/// `declared_items` as (kind, id, is_declared), by kind then id.
let private items (config: Database.DbConfig) =
    query
        config
        "SELECT kind, id, is_declared FROM declared_items ORDER BY kind, id"
        (fun r -> r.GetString 0, r.GetString 1, r.GetInt32 2)

let private applicationCount (config: Database.DbConfig) =
    task {
        let! count = scalarOf config "SELECT count(*) FROM OpenIddictApplications"
        return int count
    }

/// What a restart must not touch: every declared row's time, every connector's
/// time and every application's concurrency token.
let private fingerprint (config: Database.DbConfig) =
    task {
        let! declared = query config "SELECT kind, id, is_declared, first_declared_utc, changed_utc FROM declared_items ORDER BY kind, id" (fun r -> $"{r.GetString 0}|{r.GetString 1}|{r.GetInt32 2}|{r.GetString 3}|{r.GetString 4}")
        let! connectors = query config "SELECT slug, is_enabled, is_shared, passkey_enrolment, created_utc, updated_utc FROM connectors ORDER BY slug" (fun r -> $"{r.GetString 0}|{r.GetInt32 1}|{r.GetInt32 2}|{r.GetString 3}|{r.GetString 4}|{r.GetString 5}")
        let! applications = query config """SELECT "ClientId", "ClientSecret", "ConcurrencyToken", "RedirectUris" FROM "OpenIddictApplications" ORDER BY "ClientId" """ (fun r -> $"{r.GetString 0}|{r.GetValue 1}|{r.GetValue 2}|{r.GetValue 3}")
        return declared, connectors, applications
    }

let private withManager (app: Fixtures.TestApp) (body: IOpenIddictApplicationManager -> Task<'a>) =
    task {
        use scope = app.Services.CreateScope()
        return! body (scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>())
    }

/// Whether `secret` validates for the app, or `None` when there is no such app.
let private validates (app: Fixtures.TestApp) (clientId: string) (secret: string) =
    withManager app (fun manager ->
        task {
            match! manager.FindByClientIdAsync clientId with
            | null -> return None
            | application ->
                let! clientType = manager.GetClientTypeAsync application

                if clientType = OpenIddictConstants.ClientTypes.Public then
                    return Some false
                else
                    let! valid = manager.ValidateClientSecretAsync(application, secret)
                    return Some valid
        })

let private clientTypeOf (app: Fixtures.TestApp) (clientId: string) =
    withManager app (fun manager ->
        task {
            match! manager.FindByClientIdAsync clientId with
            | null -> return None
            | application ->
                let! clientType = manager.GetClientTypeAsync application
                return Option.ofObj clientType
        })

let private redirectsOf (app: Fixtures.TestApp) (clientId: string) =
    withManager app (fun manager ->
        task {
            match! manager.FindByClientIdAsync clientId with
            | null -> return []
            | application ->
                let! uris = manager.GetRedirectUrisAsync application
                return List.ofSeq uris
        })

let private logLines (logs: LogRedactionTests.CapturingProvider) =
    logs.Entries |> Array.filter (fun e -> e.Category = "Umbral.Declaration") |> List.ofArray

let private startLine (logs: LogRedactionTests.CapturingProvider) =
    logLines logs
    |> List.tryFind (fun e -> e.Level = LogLevel.Information && e.Everything.Contains "Declaration applied:")
    |> Option.map (fun e -> Regex.Match(e.Everything, "Declaration applied:[^\r\n]*").Value)

/// Nothing a person could copy a secret from: no log entry, no event row.
let private assertNoSecretsAnywhere (logs: LogRedactionTests.CapturingProvider) (config: Database.DbConfig) =
    task {
        for entry in logs.Entries do
            Assert.Empty(LogRedactionTests.leaksIn secrets entry.Everything)

        let! rows = query config "SELECT * FROM security_events" (fun r -> String.Join(" | ", [ for i in 0 .. r.FieldCount - 1 -> string (r.GetValue i) ]))

        for row in rows do
            Assert.Empty(LogRedactionTests.leaksIn secrets row)
    }

let private declaredEvents (all: (string * string * string * string * string) list) =
    all |> List.filter (fun (_, _, _, _, detail) -> detail.StartsWith "declared" || detail = "undeclared")

// ---------------------------------------------------------------------------
// Create
// ---------------------------------------------------------------------------

[<Fact>]
let ``a declaration creates its apps and the passkey connector, with events`` () =
    withDirectory (fun directory ->
        task {
            let settings = settingsFor (declaration (Some "open") [ web; spa ]) [ "web", webSecret ]

            do!
                runOn directory settings (fun app logs ->
                    task {
                        // The app exists under its key, and the secret is the one given.
                        let! got = validates app "web" webSecret
                        Assert.Equal(Some true, got)
                        let! got = validates app "web" otherSecret
                        Assert.Equal(Some false, got)
                        let! got = clientTypeOf app "web"
                        Assert.Equal(Some OpenIddictConstants.ClientTypes.Confidential, got)

                        // The public app has none.
                        let! got = clientTypeOf app "spa"
                        Assert.Equal(Some OpenIddictConstants.ClientTypes.Public, got)

                        let! got = redirectsOf app "web"
                        Assert.Equal<string list>([ "https://web.example.com/cb" ], got)

                        // The passkey connector: enabled, shared, the declared sign-up mode.
                        let! passkey = ConnectorStore.findBySlug app.DbConfig "passkey"
                        let passkey = Option.get passkey
                        Assert.Equal(Passkey, passkey.Kind)
                        Assert.True passkey.IsEnabled
                        Assert.True passkey.IsShared
                        Assert.Equal(OpenSignUp, passkey.PasskeyEnrolment)

                        let! all = events app.DbConfig

                        Assert.Equal<(string * string * string * string * string) list>(
                            [ "connector_created", "system", "", "passkey", "declared;enrolment=open"
                              "app_created", "system", "web", "", "declared"
                              "app_created", "system", "spa", "", "declared" ],
                            all
                        )

                        let! rows = items app.DbConfig

                        Assert.Equal<(string * string * int) list>(
                            [ "app", "spa", 1; "app", "web", 1; "connector", "passkey", 1 ],
                            rows
                        )

                        Assert.Equal(
                            Some "Declaration applied: 2 apps created, 0 updated, 0 unchanged, 0 no longer declared; passkey connector created.",
                            startLine logs
                        )

                        do! assertNoSecretsAnywhere logs app.DbConfig
                    })
        })

[<Fact>]
let ``a declared client reaches the sign-in page, which offers the passkey connector`` () =
    withDirectory (fun directory ->
        task {
            let settings = settingsFor (declaration (Some "open") [ { web with Redirects = [ RedirectUri ] } ]) [ "web", webSecret ]

            do!
                runOn directory settings (fun app _ ->
                    task {
                        let person = Browser app.Client
                        let! toPicker = person.Get(Journeys.authorizeUrl "web" "openid")

                        // Not an error page: the person is sent to sign in.
                        Assert.Equal(HttpStatusCode.Redirect, toPicker.StatusCode)
                        Assert.StartsWith("/signin?returnUrl=", location toPicker)

                        let! page = person.GetHtml(location toPicker)
                        Assert.Contains("data-connector=\"passkey\"", page)
                    })
        })

// ---------------------------------------------------------------------------
// A second start
// ---------------------------------------------------------------------------

[<Fact>]
let ``a second start with the same declaration writes nothing`` () =
    withDirectory (fun directory ->
        task {
            let settings = settingsFor (declaration (Some "open") [ web; spa ]) [ "web", webSecret ]

            let! before =
                runOn directory settings (fun app _ ->
                    task {
                        let! count = eventCount app.DbConfig
                        let! print = fingerprint app.DbConfig
                        return count, print
                    })

            do!
                runOn directory settings (fun app logs ->
                    task {
                        let! count = eventCount app.DbConfig
                        let! print = fingerprint app.DbConfig
                        Assert.Equal(fst before, count)
                        Assert.Equal(snd before, print)

                        Assert.Equal(
                            Some "Declaration applied: 0 apps created, 0 updated, 2 unchanged, 0 no longer declared; passkey connector unchanged.",
                            startLine logs
                        )

                        // And the secret still validates: nothing was hashed again.
                        let! got = validates app "web" webSecret
                        Assert.Equal(Some true, got)
                    })
        })

// ---------------------------------------------------------------------------
// Each field
// ---------------------------------------------------------------------------

let private changed (variant: string) : AppSpec * string =
    match variant with
    | "name" -> { web with Name = "Renamed" }, "name"
    | "redirect_uris" -> { web with Redirects = [ "https://web.example.com/cb"; "https://web.example.com/cb2" ] }, "redirect_uris"
    | "post_logout_redirect_uris" -> { web with Post = [] }, "post_logout_redirect_uris"
    | "connectors" -> { web with Connectors = "[\"passkey\"]" }, "connectors"
    | other -> failwith $"unknown variant {other}"

[<Theory>]
[<InlineData("name")>]
[<InlineData("redirect_uris")>]
[<InlineData("post_logout_redirect_uris")>]
[<InlineData("connectors")>]
let ``a changed field is one app_updated naming it, and the secret still validates`` (variant: string) =
    withDirectory (fun directory ->
        task {
            let signUp = Some "open"
            let secretVariables = [ "web", webSecret ]
            do! runOn directory (settingsFor (declaration signUp [ web ]) secretVariables) (fun _ _ -> Task.FromResult())
            let spec, field = changed variant

            do!
                runOn directory (settingsFor (declaration signUp [ spec ]) secretVariables) (fun app logs ->
                    task {
                        let! all = events app.DbConfig
                        let updates = all |> List.filter (fun (kind, _, _, _, _) -> kind = "app_updated")

                        Assert.Equal<(string * string * string * string * string) list>(
                            [ "app_updated", "system", "web", "", $"declared;fields={field}" ],
                            updates
                        )

                        Assert.DoesNotContain(all, fun (kind, _, _, _, _) -> kind = "app_secret_rotated")

                        // The descriptor update did not hash the hash.
                        let! got = validates app "web" webSecret
                        Assert.Equal(Some true, got)

                        Assert.Equal(
                            Some "Declaration applied: 0 apps created, 1 updated, 0 unchanged, 0 no longer declared; passkey connector unchanged.",
                            startLine logs
                        )
                    })
        })

[<Fact>]
let ``several changed fields are named together in the order of the contract`` () =
    withDirectory (fun directory ->
        task {
            let secretVariables = [ "web", webSecret ]
            do! runOn directory (settingsFor (declaration None [ web ]) secretVariables) (fun _ _ -> Task.FromResult())

            let spec = { web with Name = "Renamed"; Redirects = [ "https://web.example.com/other" ] }

            do!
                runOn directory (settingsFor (declaration None [ spec ]) secretVariables) (fun app _ ->
                    task {
                        let! all = events app.DbConfig

                        Assert.Contains(
                            ("app_updated", "system", "web", "", "declared;fields=name,redirect_uris"),
                            all
                        )

                        let! got = redirectsOf app "web"
                        Assert.Equal<string list>([ "https://web.example.com/other" ], got)
                    })
        })

// ---------------------------------------------------------------------------
// The secret
// ---------------------------------------------------------------------------

[<Fact>]
let ``a changed secret is a rotation: the old one stops, the new one works`` () =
    withDirectory (fun directory ->
        task {
            do! runOn directory (settingsFor (declaration None [ web ]) [ "web", webSecret ]) (fun _ _ -> Task.FromResult())

            do!
                runOn directory (settingsFor (declaration None [ web ]) [ "web", otherSecret ]) (fun app logs ->
                    task {
                        let! got = validates app "web" webSecret
                        Assert.Equal(Some false, got)
                        let! got = validates app "web" otherSecret
                        Assert.Equal(Some true, got)

                        let! all = events app.DbConfig
                        let after = all |> List.skip 1 // app_created

                        Assert.Equal<(string * string * string * string * string) list>(
                            [ "app_secret_rotated", "system", "web", "", "declared" ],
                            after
                        )

                        do! assertNoSecretsAnywhere logs app.DbConfig
                    })
        })

[<Fact>]
let ``a web app becomes a spa app: the secret goes, and the variable must go with it`` () =
    withDirectory (fun directory ->
        task {
            do! runOn directory (settingsFor (declaration None [ web ]) [ "web", webSecret ]) (fun _ _ -> Task.FromResult())

            do!
                runOn directory (settingsFor (declaration None [ { web with Type = "spa" } ]) []) (fun app _ ->
                    task {
                        let! got = clientTypeOf app "web"
                        Assert.Equal(Some OpenIddictConstants.ClientTypes.Public, got)

                        let! hash = scalarOf app.DbConfig """SELECT coalesce("ClientSecret", '') FROM "OpenIddictApplications" WHERE "ClientId" = 'web'"""
                        Assert.Equal("", hash)

                        let! all = events app.DbConfig

                        Assert.Contains(("app_updated", "system", "web", "", "declared;fields=type"), all)
                    })

            // The variable left set for a spa app is a refusal.
            let! error, logs = startFails directory (settingsFor (declaration None [ { web with Type = "spa" } ]) [ "web", webSecret ])
            Assert.Equal(refusedSummary, error.Message)
            Assert.Contains(logLines logs, fun e -> e.Level = LogLevel.Error && e.Everything.Contains "spa app is public")
        })

[<Fact>]
let ``a spa app becomes a web app: the secret is set from its variable`` () =
    withDirectory (fun directory ->
        task {
            do! runOn directory (settingsFor (declaration None [ { web with Type = "spa" } ]) []) (fun _ _ -> Task.FromResult())

            do!
                runOn directory (settingsFor (declaration None [ web ]) [ "web", webSecret ]) (fun app logs ->
                    task {
                        let! got = clientTypeOf app "web"
                        Assert.Equal(Some OpenIddictConstants.ClientTypes.Confidential, got)
                        let! got = validates app "web" webSecret
                        Assert.Equal(Some true, got)

                        let! all = events app.DbConfig
                        Assert.Contains(("app_updated", "system", "web", "", "declared;fields=type"), all)
                        Assert.DoesNotContain(all, fun (kind, _, _, _, _) -> kind = "app_secret_rotated")

                        do! assertNoSecretsAnywhere logs app.DbConfig
                    })
        })

// ---------------------------------------------------------------------------
// Leaving the declaration
// ---------------------------------------------------------------------------

[<Fact>]
let ``an app no longer named is un-declared and left exactly as it was`` () =
    withDirectory (fun directory ->
        task {
            do! runOn directory (settingsFor (declaration (Some "open") [ web; spa ]) [ "web", webSecret ]) (fun _ _ -> Task.FromResult())

            do!
                runOn directory (settingsFor (declaration (Some "open") [ web ]) [ "web", webSecret ]) (fun app logs ->
                    task {
                        let! all = events app.DbConfig

                        Assert.Equal(
                            ("app_updated", "system", "spa", "", "undeclared"),
                            List.last all
                        )

                        let! rows = items app.DbConfig

                        Assert.Contains(("app", "spa", 0), rows)
                        Assert.Contains(("app", "web", 1), rows)

                        // Not deleted, not disabled, not changed.
                        let! got = clientTypeOf app "spa"
                        Assert.Equal(Some OpenIddictConstants.ClientTypes.Public, got)
                        let! got = redirectsOf app "spa"
                        Assert.Equal<string list>([ "https://spa.example.com/cb" ], got)

                        Assert.Equal(
                            Some "Declaration applied: 0 apps created, 0 updated, 1 unchanged, 1 no longer declared; passkey connector unchanged.",
                            startLine logs
                        )
                    })
        })

[<Fact>]
let ``with no declaration at all, everything still declared is handed back and nothing else happens`` () =
    withDirectory (fun directory ->
        task {
            do! runOn directory (settingsFor (declaration (Some "open") [ web; spa ]) [ "web", webSecret ]) (fun _ _ -> Task.FromResult())

            do!
                runOn directory [] (fun app logs ->
                    task {
                        let! all = events app.DbConfig

                        // Only the three events of the declaration, then the three hand-backs.
                        Assert.Equal(6, all.Length)
                        let handedBack = all |> List.skip 3 |> List.sort

                        Assert.Equal<(string * string * string * string * string) list>(
                            [ "app_updated", "system", "spa", "", "undeclared"
                              "app_updated", "system", "web", "", "undeclared"
                              "connector_updated", "system", "", "passkey", "undeclared" ]
                            |> List.sort,
                            handedBack
                        )

                        let! rows = items app.DbConfig
                        Assert.All(rows, fun (_, _, declared) -> Assert.Equal(0, declared))

                        // No field changed: the connector is as declared, the apps work.
                        let! passkey = ConnectorStore.findBySlug app.DbConfig "passkey"
                        let passkey = Option.get passkey
                        Assert.True(passkey.IsEnabled && passkey.IsShared)
                        Assert.Equal(OpenSignUp, passkey.PasskeyEnrolment)
                        let! got = redirectsOf app "web"
                        Assert.Equal<string list>([ "https://web.example.com/cb" ], got)

                        Assert.Equal(
                            Some "Declaration applied: 0 apps created, 0 updated, 0 unchanged, 2 no longer declared; passkey connector no longer declared.",
                            startLine logs
                        )
                    })

            // A third start with still no declaration has nothing left to hand back.
            do!
                runOn directory [] (fun app logs ->
                    task {
                        let! count = eventCount app.DbConfig
                        Assert.Equal(6, count)
                        Assert.Equal(None, startLine logs)
                    })
        })

[<Fact>]
let ``an item that was un-declared may be declared again`` () =
    withDirectory (fun directory ->
        task {
            let settings = settingsFor (declaration (Some "open") [ web ]) [ "web", webSecret ]
            do! runOn directory settings (fun _ _ -> Task.FromResult())
            do! runOn directory [] (fun _ _ -> Task.FromResult())

            do!
                runOn directory settings (fun app _ ->
                    task {
                        let! rows = items app.DbConfig
                        Assert.Equal<(string * string * int) list>([ "app", "web", 1; "connector", "passkey", 1 ], rows)

                        let! all = events app.DbConfig
                        let again = all |> List.skip 4

                        Assert.Equal<(string * string * string * string * string) list>(
                            [ "app_updated", "system", "web", "", "declared;redeclared"
                              "connector_updated", "system", "", "passkey", "declared;enrolment=open;redeclared" ]
                            |> List.sort,
                            again |> List.sort
                        )

                        let! got = validates app "web" webSecret
                        Assert.Equal(Some true, got)
                    })
        })

[<Fact>]
let ``an app deleted outside the screens is created again`` () =
    withDirectory (fun directory ->
        task {
            let settings = settingsFor (declaration None [ web ]) [ "web", webSecret ]
            do! runOn directory settings (fun _ _ -> Task.FromResult())

            use! connection = Database.openConnection (dbOf directory)
            use delete = connection.CreateCommand()
            delete.CommandText <- """DELETE FROM "OpenIddictApplications" WHERE "ClientId" = 'web'"""
            let! _ = delete.ExecuteNonQueryAsync()

            do!
                runOn directory settings (fun app _ ->
                    task {
                        let! got = validates app "web" webSecret
                        Assert.Equal(Some true, got)
                        let! all = events app.DbConfig
                        Assert.Equal(2, all |> List.filter (fun (kind, _, _, _, _) -> kind = "app_created") |> List.length)
                    })
        })

// ---------------------------------------------------------------------------
// Conflicts and refusals
// ---------------------------------------------------------------------------

/// An app made the way the admin area makes one (through the manager, no
/// declaration), with a client ID the declaration then tries to take.
let private makeByHand (app: Fixtures.TestApp) (clientId: string) =
    withManager app (fun manager ->
        task {
            let descriptor =
                AppClients.buildDescriptor clientId "By hand" false [ Uri "https://hand.example.com/cb" ] [] None

            let! _ = manager.CreateAsync descriptor
            return ()
        })

[<Fact>]
let ``a key equal to an app made in the admin area is refused and nothing is written`` () =
    withDirectory (fun directory ->
        task {
            do! runOn directory [] (fun app _ -> makeByHand app "web")

            // The declaration also names an app that would be fine, and the passkey connector.
            let settings = settingsFor (declaration (Some "open") [ web; spa ]) [ "web", webSecret ]
            let! error, logs = startFails directory settings
            Assert.Equal(refusedSummary, error.Message)

            let line =
                logLines logs |> List.tryFind (fun e -> e.Level = LogLevel.Error && e.Everything.Contains "belongs to an app created in the admin area")

            Assert.True(line.IsSome, "the log names the conflict")
            Assert.Contains("web", line.Value.Everything)

            let config = dbOf directory
            let! got = applicationCount config
            Assert.Equal(1, got)
            let! got = items config
            Assert.Empty got
            let! got = eventCount config
            Assert.Equal(0, got)
            let! passkeys = scalarOf config "SELECT count(*) FROM connectors"
            Assert.Equal("0", passkeys)
        })

[<Fact>]
let ``a parse problem stops the start with each problem in the log and nothing written`` () =
    withDirectory (fun directory ->
        task {
            // A web app with no secret, and a wildcard address.
            let json = declaration (Some "open") [ { web with Redirects = [ "https://*.example.com/cb" ] } ]
            let! error, logs = startFails directory (settingsFor json [])
            Assert.Equal(refusedSummary, error.Message)

            let errors = logLines logs |> List.filter (fun e -> e.Level = LogLevel.Error) |> List.map _.Everything
            Assert.Contains(errors, fun text -> text.Contains "contains a wildcard")
            Assert.Contains(errors, fun text -> text.Contains "UMBRAL__APP_SECRETS__web is not set")

            let config = dbOf directory
            let! got = applicationCount config
            Assert.Equal(0, got)
            let! got = items config
            Assert.Empty got
            let! got = eventCount config
            Assert.Equal(0, got)
        })

// ---------------------------------------------------------------------------
// The passkey connector
// ---------------------------------------------------------------------------

[<Fact>]
let ``a changed sign-up mode updates the connector`` () =
    withDirectory (fun directory ->
        task {
            do! runOn directory (settingsFor (declaration (Some "invitations") []) []) (fun _ _ -> Task.FromResult())

            do!
                runOn directory (settingsFor (declaration (Some "open") []) []) (fun app logs ->
                    task {
                        let! passkey = ConnectorStore.findBySlug app.DbConfig "passkey"
                        Assert.Equal(OpenSignUp, (Option.get passkey).PasskeyEnrolment)

                        let! all = events app.DbConfig

                        Assert.Equal(
                            ("connector_updated", "system", "", "passkey", "declared;enrolment=open"),
                            List.last all
                        )

                        Assert.Equal(
                            Some "Declaration applied: 0 apps created, 0 updated, 0 unchanged, 0 no longer declared; passkey connector updated.",
                            startLine logs
                        )
                    })
        })

let private passkeyConnector (app: Fixtures.TestApp) (enrolment: PasskeyEnrolment) =
    task {
        let now = app.Clock.GetUtcNow()

        let! _ =
            ConnectorStore.create
                app.DbConfig
                now
                { Slug = "passkey"
                  Kind = Passkey
                  DisplayName = "Passkey"
                  ClientId = ""
                  ClientSecretRef = ""
                  ClientSecretHint = ""
                  IssuerUrl = None
                  AllowedEmailDomains = []
                  PasskeyEnrolment = enrolment
                  FediverseAccess = FediverseAccess.Any
                  AppleKey = None
                  MicrosoftAudience = None
                  EmailTrust = None
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = now
                  UpdatedUtc = now }
                ("", "")

        return ()
    }

[<Fact>]
let ``a passkey connector made first and exactly as declared is adopted`` () =
    withDirectory (fun directory ->
        task {
            // As the old sample's steps left it: enabled, shared, anyone can sign up.
            do! runOn directory [] (fun app _ -> passkeyConnector app OpenSignUp)
            let settings = settingsFor (declaration (Some "open") []) []

            do!
                runOn directory settings (fun app logs ->
                    task {
                        let! all = events app.DbConfig

                        Assert.Equal<(string * string * string * string * string) list>(
                            [ "connector_updated", "system", "", "passkey", "declared;adopted" ],
                            all
                        )

                        let! rows = items app.DbConfig
                        Assert.Equal<(string * string * int) list>([ "connector", "passkey", 1 ], rows)

                        Assert.Equal(
                            Some "Declaration applied: 0 apps created, 0 updated, 0 unchanged, 0 no longer declared; passkey connector adopted.",
                            startLine logs
                        )
                    })

            // Adopted once, it is the declaration's: the next start has nothing to do.
            do!
                runOn directory settings (fun app logs ->
                    task {
                        let! got = eventCount app.DbConfig
                        Assert.Equal(1, got)

                        Assert.Equal(
                            Some "Declaration applied: 0 apps created, 0 updated, 0 unchanged, 0 no longer declared; passkey connector unchanged.",
                            startLine logs
                        )
                    })
        })

[<Fact>]
let ``a passkey connector made with other settings is refused and nothing is written`` () =
    withDirectory (fun directory ->
        task {
            // Invitations only, which the declaration (open) would widen.
            do! runOn directory [] (fun app _ -> passkeyConnector app InvitationsOnly)

            let settings = settingsFor (declaration (Some "open") [ web ]) [ "web", webSecret ]
            let! error, logs = startFails directory settings
            Assert.Equal(refusedSummary, error.Message)

            let line = logLines logs |> List.tryFind (fun e -> e.Level = LogLevel.Error && e.Everything.Contains "passkeys:")
            Assert.True(line.IsSome, "the log names the connector")
            Assert.Contains("invitations only", line.Value.Everything)
            Assert.Contains("Remove \"passkeys\" from the declaration", line.Value.Everything)

            let config = dbOf directory
            let! got = items config
            Assert.Empty got
            let! got = eventCount config
            Assert.Equal(0, got)
            let! got = applicationCount config
            Assert.Equal(0, got)
            let! stored = scalarOf config "SELECT passkey_enrolment FROM connectors WHERE slug = 'passkey'"
            Assert.Equal("invitations", stored)
        })

// ---------------------------------------------------------------------------
// Connector keys that do not exist yet
// ---------------------------------------------------------------------------

[<Fact>]
let ``an unknown connector key is accepted with a warning, and applied once the connector exists`` () =
    withDirectory (fun directory ->
        task {
            let spec = { web with Connectors = "[\"github\"]" }
            let settings = settingsFor (declaration None [ spec ]) [ "web", webSecret ]

            // `app_connectors` cannot hold a key no connector has: the app is limited,
            // offers nothing through it yet, and the start is not refused.
            do!
                runOn directory settings (fun app logs ->
                    task {
                        let! choice = AppConnectors.choiceFor app.DbConfig "web"
                        Assert.Equal(Only Set.empty, choice)

                        let warning = logLines logs |> List.tryFind (fun e -> e.Level = LogLevel.Warning)
                        Assert.True(warning.IsSome, "a warning line")
                        Assert.Contains("github", warning.Value.Everything)

                        // The connector is made by hand, as an operator would later.
                        let now = app.Clock.GetUtcNow()

                        let! _ =
                            ConnectorStore.create
                                app.DbConfig
                                now
                                { Slug = "github"
                                  Kind = GitHub
                                  DisplayName = "GitHub"
                                  ClientId = "id"
                                  ClientSecretRef = "ref"
                                  ClientSecretHint = "hint"
                                  IssuerUrl = None
                                  AllowedEmailDomains = []
                                  PasskeyEnrolment = InvitationsOnly
                                  FediverseAccess = FediverseAccess.Any
                                  AppleKey = None
                                  MicrosoftAudience = None
                                  EmailTrust = None
                                  IsEnabled = true
                                  IsShared = true
                                  CreatedUtc = now
                                  UpdatedUtc = now }
                                ("ref", "hint")

                        return ()
                    })

            // While the key stays unknown, a start writes nothing; here it now exists.
            do!
                runOn directory settings (fun app logs ->
                    task {
                        let! choice = AppConnectors.choiceFor app.DbConfig "web"
                        Assert.Equal(Only(set [ "github" ]), choice)

                        let! all = events app.DbConfig

                        Assert.Contains(("app_updated", "system", "web", "", "declared;fields=connectors"), all)
                        Assert.Empty(logLines logs |> List.filter (fun e -> e.Level = LogLevel.Warning))
                    })
        })

[<Fact>]
let ``a start with an unknown connector key still writes nothing the second time`` () =
    withDirectory (fun directory ->
        task {
            let spec = { web with Connectors = "[\"github\"]" }
            let settings = settingsFor (declaration None [ spec ]) [ "web", webSecret ]
            do! runOn directory settings (fun _ _ -> Task.FromResult())

            let! before = eventCount (dbOf directory)

            do!
                runOn directory settings (fun app _ ->
                    task {
                        let! after = eventCount app.DbConfig
                        Assert.Equal(before, after)
                    })
        })

// ---------------------------------------------------------------------------
// C1: any failure while applying stops the start, and leaves nothing
// ---------------------------------------------------------------------------

[<Fact>]
let ``a failure in the middle of the apply stops the start and leaves no app and no row`` () =
    withDirectory (fun directory ->
        task {
            do! bare directory
            let config = dbOf directory

            // The app is created through OpenIddict first; its event then fails.
            // Only a transaction that spans both leaves no app behind.
            do!
                task {
                    use! connection = Database.openConnection config
                    use command = connection.CreateCommand()

                    command.CommandText <-
                        "CREATE TRIGGER induced BEFORE INSERT ON security_events
                         BEGIN SELECT RAISE(ABORT, 'induced'); END;"

                    let! _ = command.ExecuteNonQueryAsync()
                    return ()
                }

            let settings = settingsFor (declaration None [ web ]) [ "web", webSecret ]
            let! error, logs = startFails directory settings
            Assert.Contains("induced", error.ToString())

            do!
                task {
                    use! connection = Database.openConnection config
                    use command = connection.CreateCommand()
                    command.CommandText <- "DROP TRIGGER induced"
                    let! _ = command.ExecuteNonQueryAsync()
                    return ()
                }

            // R2: OpenIddict's CreateAsync wrote inside the transaction, and it rolled back with it.
            let! got = applicationCount config
            Assert.Equal(0, got)
            let! got = items config
            Assert.Empty got
            let! got = eventCount config
            Assert.Equal(0, got)

            for entry in logs.Entries do
                Assert.Empty(LogRedactionTests.leaksIn secrets entry.Everything)
            Assert.DoesNotContain(webSecret, error.ToString())

            // The same declaration then applies, in full.
            do!
                runOn directory settings (fun app _ ->
                    task {
                        let! got = validates app "web" webSecret
                        Assert.Equal(Some true, got)
                        let! got = eventCount app.DbConfig
                        Assert.Equal(1, got)
                    })
        })

[<Fact>]
let ``a failure while handing back leaves every item still declared`` () =
    withDirectory (fun directory ->
        task {
            do! runOn directory (settingsFor (declaration None [ web ]) [ "web", webSecret ]) (fun _ _ -> Task.FromResult())
            let config = dbOf directory
            let! before = items config

            do!
                task {
                    use! connection = Database.openConnection config
                    use command = connection.CreateCommand()

                    command.CommandText <-
                        "CREATE TRIGGER induced BEFORE INSERT ON security_events
                         BEGIN SELECT RAISE(ABORT, 'induced'); END;"

                    let! _ = command.ExecuteNonQueryAsync()
                    return ()
                }

            let! error, _ = startFails directory []
            Assert.Contains("induced", error.ToString())

            do!
                task {
                    use! connection = Database.openConnection config
                    use command = connection.CreateCommand()
                    command.CommandText <- "DROP TRIGGER induced"
                    let! _ = command.ExecuteNonQueryAsync()
                    return ()
                }

            let! after = items config
            Assert.Equal<(string * string * int) list>(before, after)
        })
