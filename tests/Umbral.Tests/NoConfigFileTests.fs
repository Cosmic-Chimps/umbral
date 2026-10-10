/// Spec 004: Umbral reads no configuration document.
///
/// The old file was applied on every start, and it overwrote what the operator
/// had done in the admin area since the last one. These facts pin the opposite:
/// a file that is still lying in the data directory, or still named by the old
/// setting, changes nothing and breaks nothing.
///
/// Nothing was published with the file, so there is no migration and no warning
/// about a leftover one. What is asserted is silence.
module Umbral.Tests.NoConfigFileTests

open System
open System.IO
open System.Net
open System.Threading.Tasks
open Xunit
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys

/// What the old version refused at startup (a literal secret and a literal
/// credential hash). If Umbral still read it, the start fails.
let private refusedDocument =
    """version: 1

operator:
  credential_hash: not-a-real-hash

connectors:
  - slug: github
    kind: github
    client_id: "Iv1.0123456789abcdef"
    client_secret: oops-a-literal
    enabled: true
"""

/// What the old version applied on every start: `enabled: true` on the same
/// connector the operator turns off below. The old version put it back on at
/// the next start; the facts below need it to stay off.
let private appliedDocument =
    """version: 1

connectors:
  - slug: github
    kind: github
    client_id: "Iv1.0123456789abcdef"
    client_secret: ${UMBRAL_GITHUB_SECRET}
    enabled: true
"""

/// The file in the data directory where the old version looked for it, and the
/// old setting pointing at it.
let private withOldFile (document: string) (body: string -> string -> (string * string) list -> Task<unit>) =
    task {
        let directory = Fixtures.freshDataDirectory ()
        Directory.CreateDirectory directory |> ignore
        let path = Path.Combine(directory, "umbral.yaml")
        File.WriteAllText(path, document)

        try
            do! body directory path [ "UMBRAL:CONFIG_FILE", path; "UMBRAL_GITHUB_SECRET", "a-secret-from-the-environment" ]
        finally
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            try
                if Directory.Exists directory then
                    Directory.Delete(directory, true)
            with _ ->
                ()
    }

/// One run of the instance: started, handed to `body`, and gone again before
/// the next one starts, as a stopped process would be.
let private runOn (directory: string) (settings: (string * string) list) (body: Fixtures.TestApp -> Task<'a>) =
    task {
        use! app = Fixtures.createAppIn directory settings ignore
        return! body app
    }

let private location (response: Net.Http.HttpResponseMessage) = response.Headers.Location |> string

let private connectorForm =
    [ "kind", "github"
      "slug", "github"
      "displayName", "GitHub"
      "clientId", "Iv1.0123456789abcdef"
      "clientSecret", "a-secret-typed-into-the-form" ]

/// The operator claims the instance, adds a connector on the screen and turns it off.
let private addAndDisable (app: Fixtures.TestApp) =
    task {
        let operator = Browser(app.Client)
        do! claimInstance app operator

        let! created = operator.PostForm("/admin/connectors", "/admin/connectors", connectorForm)
        Assert.Contains("connector-added", location created)

        // The only enabled connector: turning it off asks to be confirmed.
        let! disabled = operator.PostForm("/admin/connectors", "/admin/connectors/github/toggle?confirm=yes", [])
        Assert.Contains("connector-disabled", location disabled)
    }

let private enabledState (app: Fixtures.TestApp) =
    task {
        let! github = ConnectorStore.findBySlug app.DbConfig "github"
        return github |> Option.map (fun c -> c.IsEnabled)
    }

let private fileEvents (app: Fixtures.TestApp) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()

        command.CommandText <-
            "SELECT count(*) FROM security_events
             WHERE event_type = 'config_applied' OR detail LIKE '%config_file%'"

        let! count = command.ExecuteScalarAsync()
        return Convert.ToInt32 count
    }

[<Fact>]
let ``a file in the data directory and the old setting are ignored`` () =
    withOldFile refusedDocument (fun directory path settings ->
        task {
            let before = File.ReadAllBytes path

            do!
                runOn directory settings (fun app ->
                    task {
                        let! connectors = ConnectorStore.listAll app.DbConfig
                        Assert.Empty connectors

                        let! configured = AdminAuth.isConfigured app.DbConfig
                        Assert.False configured
                    })

            Assert.Equal<byte[]>(before, File.ReadAllBytes path)
        })

[<Fact>]
let ``a change made in the admin area survives restarts`` () =
    withOldFile appliedDocument (fun directory _ settings ->
        task {
            do! runOn directory settings addAndDisable

            // Twice: the file used to say `enabled: true` on every start.
            for _ in 1..2 do
                let! state = runOn directory settings enabledState
                Assert.Equal(Some false, state)
        })

[<Fact>]
let ``the configuration export is gone`` () =
    task {
        use! app = Fixtures.createApp ()

        let! anonymous = app.Client.GetAsync "/admin/config"
        Assert.Equal(HttpStatusCode.NotFound, anonymous.StatusCode)

        let operator = Browser(app.Client)
        do! claimInstance app operator

        let! signedIn = operator.Get "/admin/config"
        Assert.Equal(HttpStatusCode.NotFound, signedIn.StatusCode)
    }

[<Fact>]
let ``no security event names the file`` () =
    withOldFile appliedDocument (fun directory _ settings ->
        task {
            do! runOn directory settings addAndDisable

            let! count = runOn directory settings fileEvents
            Assert.Equal(0, count)
        })
