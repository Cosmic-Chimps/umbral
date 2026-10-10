/// T093 — no secret, token or authorization code reaches a log sink.
///
/// Umbral has no support team, so its logs get pasted into issues and chat
/// threads. A credential in one of them is a credential disclosed.
///
/// The method is blunt on purpose. A capturing `ILoggerProvider` sits at TRACE —
/// the most verbose level there is, which includes OpenIddict's and ASP.NET
/// Core's own debug logging, where tokens tend to leak — while a real operator
/// and a real person drive the whole journey. Every concrete secret the test
/// saw along the way is then searched for, verbatim and URL-encoded, in every
/// message, every structured-state value, every scope and every exception
/// (stack trace included). The `security_events` table is searched too: it is
/// the other place Umbral writes a record of what happened.
///
/// Not covered, deliberately: the first-run setup banner. It prints the setup
/// token to stdout on purpose (FR-016) — that is how the operator learns it —
/// and does so through `Console.WriteLine`, not a logger. What IS asserted is
/// that the token never reaches an `ILogger`.
module Umbral.Tests.LogRedactionTests

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Net
open System.Text
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Xunit
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys

// `Captured`, `CapturingProvider`, `Secret`, `leaksIn` and `securityEventRows` are
// public so the OpenID Connect end-to-end test (OidcConnectorTests) searches its
// own sign-in with this same capture and the same rules.

/// One log call, flattened to every string it carries.
type Captured =
    { Category: string
      Level: LogLevel
      /// The formatted message, the structured state's keys and values, every
      /// scope open at the time, and the exception with its stack trace. A
      /// secret in any of those is a secret in a sink: a JSON console logger or
      /// OpenTelemetry exporter writes the structured state, not the message.
      Everything: string }

type private CapturingLogger(category: string, sink: ConcurrentQueue<Captured>, scopes: unit -> (IExternalScopeProvider | null)) =
    interface ILogger with
        member _.IsEnabled _ = true
        member _.BeginScope _ = null

        member _.Log<'TState>(level, eventId: EventId, state: 'TState, error: exn | null, formatter: Func<'TState, exn | null, string>) =
            let text = StringBuilder()
            text.AppendLine(formatter.Invoke(state, error)).AppendLine(string eventId.Name) |> ignore

            match box state with
            | :? IEnumerable<KeyValuePair<string, obj | null>> as pairs ->
                for pair in pairs do
                    text.Append(pair.Key).Append('=').AppendLine(string pair.Value) |> ignore
            | _ -> ()

            match scopes () with
            | null -> ()
            | provider ->
                provider.ForEachScope(
                    (fun (scope: obj | null) (builder: StringBuilder) ->
                        match scope with
                        | :? IEnumerable<KeyValuePair<string, obj | null>> as pairs ->
                            for pair in pairs do
                                builder.Append(pair.Key).Append('=').AppendLine(string pair.Value) |> ignore
                        | other -> builder.AppendLine(string other) |> ignore),
                    text
                )

            match error with
            | null -> ()
            | e -> text.AppendLine(e.ToString()) |> ignore

            sink.Enqueue
                { Category = category
                  Level = level
                  Everything = text.ToString() }

type CapturingProvider() =
    let sink = ConcurrentQueue<Captured>()
    let mutable scopes: (IExternalScopeProvider | null) = null
    member _.Entries = sink.ToArray()

    interface ILoggerProvider with
        member _.CreateLogger category =
            CapturingLogger(category, sink, (fun () -> scopes)) :> ILogger

        member _.Dispose() = ()

    interface ISupportExternalScope with
        member _.SetScopeProvider provider = scopes <- provider

[<Literal>]
let MinimumSecretLength = 16

/// A secret the test saw, and what to call it when it turns up where it must not.
type Secret = { Label: string; Value: string }

/// Where a value may legitimately travel in a different spelling: a query
/// string percent-encodes it, so a log line quoting a URL carries the encoded
/// form and a plain `Contains` on the raw one would miss it.
let spellings (value: string) =
    [ value; Uri.EscapeDataString value; WebUtility.UrlEncode value |> Option.ofObj |> Option.defaultValue value ]
    |> List.distinct

let leaksIn (secrets: Secret list) (haystack: string) =
    secrets
    |> List.filter (fun s -> spellings s.Value |> List.exists (fun v -> haystack.Contains(v, StringComparison.Ordinal)))
    |> List.map _.Label

/// Every column of every `security_events` row, as one string per row.
let securityEventRows (app: Fixtures.TestApp) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT * FROM security_events"
        use! reader = command.ExecuteReaderAsync()
        let rows = List<string>()

        while! reader.ReadAsync() do
            rows.Add(String.Join(" | ", [ for i in 0 .. reader.FieldCount - 1 -> string (reader.GetValue i) ]))

        return List.ofSeq rows
    }

[<Fact>]
let ``no secret, token or code reaches a log sink or the security log`` () =
    task {
        let provider = new CapturingProvider()

        // TRACE, and every category: the point is to see what the frameworks
        // would say if an operator turned verbose logging on to debug an issue.
        // appsettings.json quiets several categories by default, so they are
        // raised here the way an operator would, through configuration.
        let customize (builder: Microsoft.AspNetCore.Builder.WebApplicationBuilder) =
            builder.Logging.SetMinimumLevel LogLevel.Trace |> ignore
            builder.Logging.AddProvider provider |> ignore

        let verbose =
            [ for category in
                  [ "Default"
                    "Microsoft.AspNetCore"
                    "Microsoft.EntityFrameworkCore.Database.Command"
                    "OpenIddict"
                    "Quartz" ] -> $"Logging:LogLevel:{category}", "Trace" ]

        use! app = Fixtures.createAppWith (fakeSettings @ verbose) customize
        let browser = Browser(app.Client)
        let secrets = List<Secret>()

        let remember label (value: string) =
            // A short value would match by coincidence and fail the test for a
            // reason that is not a leak. Every real secret here is far longer.
            Assert.True(value.Length >= MinimumSecretLength, $"the test's own secret '{label}' is too short to search for")
            secrets.Add { Label = label; Value = value }

        // --- first-run setup: the token and the operator credential ----------
        let setupToken = app.Services.GetRequiredService<AdminAuth.SetupToken>().Value
        remember "setup token" setupToken
        remember "operator credential" OperatorCredential
        do! claimInstance app browser

        // A wrong credential is still something a person typed, and may be
        // their real one with a slip.
        let guess = "an-almost-right-credential-1"
        remember "mistyped operator credential" guess
        let other = Browser(app.Client)

        let! _ = other.PostForm("/admin/signin", "/admin/signin", [ "credential", guess ])

        // --- connectors: a saved client secret -------------------------------
        let connectorSecret = "discord-client-secret-4f9a1c7e2b8d"
        remember "connector client secret" connectorSecret

        let! saved =
            browser.PostForm(
                "/admin/connectors",
                "/admin/connectors/discord",
                [ "clientId", "123456789012345678"; "clientSecret", connectorSecret ]
            )

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode)

        let fakeSecret = "fake-connector-secret-value"
        remember "fake connector secret" fakeSecret
        do! addFakeConnector app fakeSecret

        // --- an app, and its generated client secret --------------------------
        let! clientId, clientSecret = createClientApp browser
        remember "app client secret" clientSecret

        // --- a whole sign-in --------------------------------------------------
        let! signIn = signInThroughFake browser clientId "openid email profile offline_access" "fake-user-1" "sam@example.com"
        remember "Umbral authorization code" signIn.Code
        remember "provider authorization code" signIn.ProviderCode
        remember "provider round-trip state" signIn.ProviderState
        remember "PKCE code verifier" Verifier

        // --- the token exchange ------------------------------------------------
        let! exchanged = exchangeCode app.Client clientId clientSecret signIn.Code
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode)
        let! tokens = json exchanged
        let accessToken = strOf tokens "access_token"
        let refreshToken = strOf tokens "refresh_token"
        let idToken = strOf tokens "id_token"
        remember "access token" accessToken
        remember "refresh token" refreshToken
        remember "id_token" idToken

        // --- user info and refresh ---------------------------------------------
        let! info = userInfo app.Client accessToken
        Assert.Equal(HttpStatusCode.OK, info.StatusCode)

        let! refreshed = refresh app.Client clientId clientSecret refreshToken
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode)
        let! rotated = json refreshed
        remember "rotated access token" (strOf rotated "access_token")
        remember "rotated refresh token" (strOf rotated "refresh_token")

        // The failure paths come last: a replayed code makes OpenIddict revoke
        // everything issued from it, so doing this earlier would end the journey.
        // A replayed code is the one protocol error Umbral records itself, and
        // the one where OpenIddict logs about the token it just refused.
        let! replay = exchangeCode app.Client clientId clientSecret signIn.Code
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode)

        // A wrong client secret fails authentication; both the attempted and
        // the real value must stay out of the log.
        let wrong = "not-the-real-client-secret-0001"
        remember "wrong client secret" wrong
        let! unauthenticated = exchangeCode app.Client clientId wrong signIn.Code
        Assert.NotEqual(HttpStatusCode.OK, unauthenticated.StatusCode)

        // Presenting a token that is no longer valid.
        let! stale = refresh app.Client clientId clientSecret refreshToken
        Assert.NotEqual(HttpStatusCode.OK, stale.StatusCode)

        // Every cookie value is a bearer credential: the operator session, the
        // end-user session, the correlation cookie, the antiforgery cookie.
        //
        // Only the ones still holding something: a cookie Umbral has finished
        // with is cleared by overwriting it with an empty value, and the empty
        // string is "found" in every log line there is.
        for cookie in browser.Cookies do
            if cookie.Value.Length >= MinimumSecretLength then
                remember $"cookie {cookie.Key}" cookie.Value

        // ---------------------------------------------------------------------
        // The assertions. First, that the capture is not vacuous: a provider
        // that saw nothing would "prove" anything.
        // ---------------------------------------------------------------------
        let entries = provider.Entries
        Assert.NotEmpty entries

        Assert.True(
            entries |> Array.exists (fun e -> e.Level <= LogLevel.Debug && e.Category.StartsWith "OpenIddict"),
            "no OpenIddict debug/trace output was captured, so this test is not looking where tokens tend to leak"
        )

        Assert.True(
            entries |> Array.exists (fun e -> e.Category.StartsWith "Microsoft.AspNetCore"),
            "no ASP.NET Core log output was captured"
        )

        let secrets = List.ofSeq secrets

        let leaks =
            [ for entry in entries do
                  match leaksIn secrets entry.Everything with
                  | [] -> ()
                  | labels ->
                      // Names what leaked and where, never the value itself:
                      // a failure message is a log too.
                      yield String.Join(", ", labels) + $" in [{entry.Level}] {entry.Category}" ]
            |> List.distinct

        Assert.True(List.isEmpty leaks, "secrets reached a log sink:" + Environment.NewLine + String.Join(Environment.NewLine, leaks))

        // The security log: every column of every row.
        let! rows = securityEventRows app
        Assert.NotEmpty rows

        let eventLeaks = rows |> List.collect (leaksIn secrets) |> List.distinct
        Assert.True(List.isEmpty eventLeaks, "secrets reached security_events: " + String.Join(", ", eventLeaks))
    }

/// The test above proves nothing leaked; this one proves WHY, and that it
/// stays true. The leaking categories are capped by a rule, which outranks the
/// minimum level and the operator's own `Logging__LogLevel__*` settings — so
/// an operator who turns everything up to Trace to chase a problem does not
/// turn the leaks on with it.
[<Fact>]
let ``an operator asking for verbose logging cannot switch the leaking categories on`` () =
    task {
        let provider = new CapturingProvider()

        // As hostile a configuration as an operator could write: everything at
        // Trace, and each capped category named explicitly at Trace.
        let settings =
            [ "Logging:LogLevel:Default", "Trace" ]
            @ [ for category in LogRedaction.cappedCategories -> $"Logging:LogLevel:{category}", "Trace" ]

        let customize (builder: Microsoft.AspNetCore.Builder.WebApplicationBuilder) =
            builder.Logging.SetMinimumLevel LogLevel.Trace |> ignore
            builder.Logging.AddProvider provider |> ignore

        use! app = Fixtures.createAppWith settings customize
        let factory = app.Services.GetRequiredService<ILoggerFactory>()

        // The control: without it, "disabled" could just mean the provider or
        // the configuration is not wired up and nothing is ever enabled.
        Assert.True((factory.CreateLogger "Umbral.Anything").IsEnabled LogLevel.Trace)

        for category in LogRedaction.cappedCategories do
            let logger = factory.CreateLogger category
            Assert.False(logger.IsEnabled LogLevel.Information, $"{category} can log at Information")
            Assert.True(logger.IsEnabled LogLevel.Warning, $"{category} lost its warnings too")
    }
