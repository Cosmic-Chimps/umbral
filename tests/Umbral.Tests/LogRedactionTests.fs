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
///
/// A second journey does the same through an external secret store (specs/005,
/// SC-007): the connector client secret is then written to, read from and
/// deleted in a manager, and every one of those paths — a refused write, a read
/// served from the grace period, a delete the store refuses, the `move-secrets`
/// command — logs and records what went wrong. None of them may carry a value.
module Umbral.Tests.LogRedactionTests

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.DataProtection
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Xunit
open Umbral.Infrastructure
open Umbral.Infrastructure.Secrets
open Umbral.Infrastructure.SecretStores.Registry
open Umbral.Infrastructure.SecretStores.Settings
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.InMemorySecretStore
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.StubOidcProvider

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

/// TRACE, and every category: the point is to see what the frameworks would say
/// if an operator turned verbose logging on to debug an issue. appsettings.json
/// quiets several categories by default, so they are raised here the way an
/// operator would, through configuration.
let private verboseSettings =
    [ for category in
          [ "Default"
            "Microsoft.AspNetCore"
            "Microsoft.EntityFrameworkCore.Database.Command"
            "OpenIddict"
            "Quartz" ] -> $"Logging:LogLevel:{category}", "Trace" ]

let private captureAt (provider: CapturingProvider) (builder: Microsoft.AspNetCore.Builder.WebApplicationBuilder) =
    builder.Logging.SetMinimumLevel LogLevel.Trace |> ignore
    builder.Logging.AddProvider provider |> ignore

/// What every journey asserts once it is over, so that none of them can pass
/// on a capture that saw nothing: the capture is not vacuous, no log entry
/// carries a secret, and no `security_events` row does. A journey that has
/// tokens in it checks for its own OpenIddict output first, as the first one does.
let private assertNoLeaks (app: Fixtures.TestApp) (provider: CapturingProvider) (secrets: Secret list) (otherText: (string * string) list) =
    task {
        let entries = provider.Entries
        Assert.NotEmpty entries

        Assert.True(
            entries |> Array.exists (fun e -> e.Category.StartsWith "Microsoft.AspNetCore"),
            "no ASP.NET Core log output was captured"
        )

        let leaks =
            [ for entry in entries do
                  match leaksIn secrets entry.Everything with
                  | [] -> ()
                  | labels ->
                      // Names what leaked and where, never the value itself:
                      // a failure message is a log too.
                      yield String.Join(", ", labels) + $" in [{entry.Level}] {entry.Category}"
              // Text that is not a log line but is just as pasted around: what
              // a command printed.
              for where, text in otherText do
                  match leaksIn secrets text with
                  | [] -> ()
                  | labels -> yield String.Join(", ", labels) + $" in {where}" ]
            |> List.distinct

        Assert.True(List.isEmpty leaks, "secrets reached a log sink:" + Environment.NewLine + String.Join(Environment.NewLine, leaks))

        // The security log: every column of every row.
        let! rows = securityEventRows app
        Assert.NotEmpty rows

        let eventLeaks = rows |> List.collect (leaksIn secrets) |> List.distinct
        Assert.True(List.isEmpty eventLeaks, "secrets reached security_events: " + String.Join(", ", eventLeaks))
    }

[<Fact>]
let ``no secret, token or code reaches a log sink or the security log`` () =
    task {
        let provider = new CapturingProvider()

        use! app = Fixtures.createAppWith (fakeSettings @ verboseSettings) (captureAt provider)
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

        Assert.True(
            provider.Entries |> Array.exists (fun e -> e.Level <= LogLevel.Debug && e.Category.StartsWith "OpenIddict"),
            "no OpenIddict debug/trace output was captured, so this test is not looking where tokens tend to leak"
        )

        do! assertNoLeaks app provider (List.ofSeq secrets) []
    }

let private issuer = "https://auth.example.test/application/o/umbral"

/// A whole sign-in through the stubbed provider with the connector `slug`, the
/// way the product's own tests do it. True when it ended back at Umbral's
/// authorize endpoint, that is, when the secret was read and accepted.
let private signInThrough (browser: Browser) (stub: StubOidcProvider) (clientId: string) (slug: string) =
    task {
        let! toPicker = browser.Get(authorizeUrl clientId "openid email profile")
        let! toProvider = browser.Get((toPicker.Headers.Location |> string).Replace("/signin", $"/connect/{slug}/start"))
        let authorization = stub.Authorize(Uri(toProvider.Headers.Location |> string))

        let! back =
            browser.Get(
                $"/connect/{slug}/callback?code={Uri.EscapeDataString authorization.Code}&state={Uri.EscapeDataString authorization.State}"
            )

        return (back.Headers.Location |> string).Contains "/connect/authorize"
    }

/// SC-007: a connector secret that lives in an external store is in no log
/// line and no event row, whichever way the store failed. The journey is the
/// product's own: the Connectors screen, the sign-in callback and the offline
/// `move-secrets` command, against an in-memory store registered as `aws`.
///
/// Every failure in it is deliberate, because a failure is where a careless
/// `logger.LogError("... {Value}", ...)` or `Detail = $"... {message}"` hides:
/// a refused write, a replaced secret, a read served from the grace period
/// while the store is down, a delete the store refuses, a move.
[<Fact>]
let ``no connector secret reaches a log sink or the security log through an external store`` () =
    task {
        let provider = new CapturingProvider()
        let store = InMemorySecretStore "aws"
        let stub = create issuer
        let secrets = List<Secret>()

        let remember label (value: string) =
            Assert.True(value.Length >= MinimumSecretLength, $"the test's own secret '{label}' is too short to search for")
            secrets.Add { Label = label; Value = value }

        let settings =
            fakeSettings
            @ verboseSettings
            @ [ "UMBRAL:SECRET_STORE", "aws"; "AWS_REGION", "eu-west-1" ]

        use! app =
            Fixtures.createAppWithProviderHandler
                (stub :> HttpMessageHandler)
                settings
                (fun builder ->
                    captureAt provider builder
                    Fixtures.withSecretStores [ store ] "aws" builder)

        stub.Now <- fun () -> app.Clock.GetUtcNow()

        let operator = Browser(app.Client)
        remember "operator credential" OperatorCredential
        do! claimInstance app operator

        let fakeSecret = "fake-connector-secret-value"
        remember "fake connector secret" fakeSecret
        do! addFakeConnector app fakeSecret
        let! clientId, clientSecret = createClientApp operator
        remember "app client secret" clientSecret

        let oidcFields (slug: string) (secret: string) =
            [ "kind", "oidc"
              "slug", slug
              "displayName", "Home"
              "issuer", issuer
              "clientId", "umbral-client"
              "clientSecret", secret
              "shared", "true" ]

        let githubFields (slug: string) (secret: string) =
            [ "kind", "github"
              "slug", slug
              "displayName", "GitHub " + slug
              "clientId", "gh-client"
              "clientSecret", secret ]

        // --- a write the store refuses: the value was typed, then dropped ----
        let refused = "refused-client-secret-2b7e4d19"
        remember "client secret the store refused" refused
        store.FailEveryCall AccessDenied
        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", oidcFields "home" refused)
        store.Recover()

        // --- create, sign in -----------------------------------------------------
        let original = "original-client-secret-8d41c7aa"
        remember "original connector client secret" original
        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", oidcFields "home" original)

        let! landed = signInThrough (Browser app.Client) stub clientId "home"
        Assert.True(landed, "the first sign-in did not reach the authorize endpoint")
        Assert.Equal(Some original, stub.LastTokenRequest.Value.BasicClientSecret)

        // --- replace: the old value must be as absent as the new -------------
        let replacement = "replacement-client-secret-93ab20f4"
        remember "replacement connector client secret" replacement

        let! _ =
            operator.PostForm(
                "/admin/connectors",
                "/admin/connectors/home",
                [ "displayName", "Home"
                  "clientId", "umbral-client"
                  "clientSecret", replacement
                  "shared", "true" ]
            )

        let! landed = signInThrough (Browser app.Client) stub clientId "home"
        Assert.True(landed, "the sign-in after the replace did not reach the authorize endpoint")
        Assert.Equal(Some replacement, stub.LastTokenRequest.Value.BasicClientSecret)

        // --- the store goes away: served from the grace period ---------------
        // Past the 300 s cache lifetime, inside the 3 600 s grace.
        store.FailEveryCall Unreachable
        app.Clock.Advance(TimeSpan.FromSeconds 301.)

        let! landed = signInThrough (Browser app.Client) stub clientId "home"
        Assert.True(landed, "the sign-in during the outage was not served from the grace period")

        // And past the grace period too: the read fails and the sign-in with it.
        app.Clock.Advance(TimeSpan.FromSeconds 3600.)
        let! landed = signInThrough (Browser app.Client) stub clientId "home"
        Assert.False(landed, "a sign-in past the grace period must not succeed")

        // --- a delete the store refuses (still down) ------------------------------
        let other = "other-connector-secret-6c0d51e8"
        remember "second connector client secret" other
        store.Recover()
        let! _ = operator.PostForm("/admin/connectors", "/admin/connectors", githubFields "work-github" other)
        store.FailEveryCall Unreachable
        let! deleted = operator.PostForm("/admin/connectors", "/admin/connectors/work-github/delete", [])
        Assert.Contains("connector-secret-left-behind", deleted.Headers.Location |> string)
        store.Recover()

        // --- move to local ---------------------------------------------------------
        // The app's own registry is active on `aws` and cannot be changed, so a
        // second one over the same stores stands for the instance an operator
        // restarts with `UMBRAL__SECRET_STORE` unset.
        let local = LocalSecretStore(app.Services.GetRequiredService<IDataProtectionProvider>())
        let toLocal = SecretStores(local, "local", Map [ "aws", lazy (Ok(store :> ISecretStore)) ])
        let printed = List<string>()

        // A move that stops on the first call it makes, so the failure report is
        // written too, then the clean one.
        store.FailEveryCall Unreachable

        let! failedCode =
            SecretMove.run toLocal (app.Services.GetRequiredService<SecretStoreSettings>()) app.DbConfig app.Clock false printed.Add

        Assert.Equal(1, failedCode)
        store.Recover()

        let! code =
            SecretMove.run toLocal (app.Services.GetRequiredService<SecretStoreSettings>()) app.DbConfig app.Clock false printed.Add

        Assert.Equal(0, code)

        // The journey reached what it set out to: both kinds of event, and the
        // move really did move (so the report it printed is one worth searching).
        let! stale = Flows.scalar app "SELECT count(*) FROM security_events WHERE event_type = 'secret_store_failed' AND detail LIKE '%stale=true'" []
        Assert.NotEqual<string>("0", stale)
        let! moved = Flows.scalar app "SELECT count(*) FROM security_events WHERE event_type = 'connector_secret_moved'" []
        Assert.NotEqual<string>("0", moved)

        do! assertNoLeaks app provider (List.ofSeq secrets) [ "the move-secrets output", String.Join(Environment.NewLine, printed) ]
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

/// specs/006, FR-011 and SC-005: a refusal by an email-domain rule names the
/// domain and never the address. The person was refused; their address is not
/// something an operator pasting a log into an issue should be handing out.
///
/// All three stages refuse here, each for a different person: the callback (a
/// verified address at the wrong domain, an unverified one at the right domain),
/// the authorize endpoint (a session that was admitted before the rule was
/// tightened) and the token endpoint (a code and a refresh token issued before
/// it). Every `security_events` row and every captured log entry, TRACE
/// included, is then searched for each full address.
[<Fact>]
let ``no full email address reaches a log sink or the security log when a sign-in is refused`` () =
    task {
        let provider = new CapturingProvider()

        use! app = Fixtures.createAppWith (fakeSettings @ verboseSettings) (captureAt provider)
        let operator = Browser(app.Client)
        let! prepared = prepare app operator

        let bob = "bob.leak@gmail.com"
        let carol = "carol.leak@acme.com"
        let dan = "dan.leak@gmail.com"
        let ed = "ed.leak@gmail.com"
        let addresses = [ bob; carol; dan; ed ]

        // Dan and Ed sign in while nothing limits the connector: Dan keeps an
        // authorization code he has not redeemed, Ed redeems his for tokens.
        let danBrowser = Browser(app.Client)
        let! danSignIn = signInThroughFake danBrowser prepared.ClientId "openid offline_access" "dan" dan
        let edBrowser = Browser(app.Client)
        let! edSignIn = signInThroughFake edBrowser prepared.ClientId "openid offline_access" "ed" ed
        let! edExchanged = exchangeCode app.Client prepared.ClientId prepared.ClientSecret edSignIn.Code
        Assert.Equal(HttpStatusCode.OK, edExchanged.StatusCode)
        let! edTokens = json edExchanged

        // The operator limits the connector to one domain, through the same
        // call the Connectors screen makes.
        let! _ = ConnectorStore.update app.DbConfig app.Clock "fake" "Fake" "fake" true (Some [ "acme.com" ]) None

        // The callback: a verified address at another domain, and an
        // unverified address at the listed one.
        let refuseAtCallback (email: string) (verified: bool) =
            task {
                let browser = Browser(app.Client)
                let! toPicker = browser.Get(authorizeUrl prepared.ClientId "openid")
                let! toProvider = browser.Get((toPicker.Headers.Location |> string).Replace("/signin", "/connect/fake/start"))
                let providerUrl = toProvider.Headers.Location |> string
                let decoded = Uri.UnescapeDataString providerUrl
                let redirect = Text.RegularExpressions.Regex.Match(decoded, "redirect_uri=([^&]+)").Groups[1].Value
                let state = Text.RegularExpressions.Regex.Match(decoded, "state=([^&]+)").Groups[1].Value

                let! approved =
                    browser.PostForm(
                        providerUrl,
                        "/dev/fake-provider/authorize",
                        [ "redirect_uri", redirect
                          "state", state
                          "subject", email
                          "email", email
                          "displayName", "Test User" ]
                        @ (if verified then [ "emailVerified", "on" ] else [])
                    )

                let! back = browser.Get(approved.Headers.Location |> string)
                Assert.Contains("msg=account-not-allowed", back.Headers.Location |> string)
            }

        do! refuseAtCallback bob true
        do! refuseAtCallback carol false

        // The authorize endpoint: Dan's session no longer counts.
        let! authorized = danBrowser.Get(authorizeUrl prepared.ClientId "openid offline_access")
        Assert.StartsWith("/signin?returnUrl=", authorized.Headers.Location |> string)

        // The token endpoint: Dan's code, and Ed's refresh token.
        let! codeRefused = exchangeCode app.Client prepared.ClientId prepared.ClientSecret danSignIn.Code
        Assert.Equal(HttpStatusCode.BadRequest, codeRefused.StatusCode)

        let! refreshRefused = refresh app.Client prepared.ClientId prepared.ClientSecret (strOf edTokens "refresh_token")
        Assert.Equal(HttpStatusCode.BadRequest, refreshRefused.StatusCode)

        // --- the security log ------------------------------------------------
        let! rows = securityEventRows app
        let! detailRows =
            task {
                use! connection = Database.openConnection app.DbConfig
                use command = connection.CreateCommand()
                command.CommandText <- "SELECT detail FROM security_events WHERE event_type = 'sign_in_failed' ORDER BY id"
                use! reader = command.ExecuteReaderAsync()
                let details = List<string>()

                while! reader.ReadAsync() do
                    details.Add(if reader.IsDBNull 0 then "" else reader.GetString 0)

                return List.ofSeq details
            }

        // Not vacuous: one refusal per stage, each naming the domain.
        Assert.Equal(5, detailRows.Length)
        Assert.Equal(2, detailRows |> List.filter _.Contains("stage=callback") |> List.length)
        Assert.Equal(1, detailRows |> List.filter _.Contains("stage=authorize") |> List.length)
        Assert.Equal(2, detailRows |> List.filter _.Contains("stage=token") |> List.length)
        Assert.Equal(4, detailRows |> List.filter _.Contains("domain=gmail.com") |> List.length)
        Assert.Equal(1, detailRows |> List.filter _.Contains("reason=email_not_verified;rule=connector;stage=callback;domain=acme.com") |> List.length)

        let secrets = [ for a in addresses -> { Label = $"the address {a.Split('@')[0]}"; Value = a } ]

        // Every column of every row, not only `detail`.
        let eventLeaks = rows |> List.collect (leaksIn secrets) |> List.distinct
        Assert.True(List.isEmpty eventLeaks, "full addresses reached security_events: " + String.Join(", ", eventLeaks))

        // --- the logs ----------------------------------------------------------
        let entries = provider.Entries
        Assert.True(entries |> Array.exists (fun e -> e.Category.StartsWith "Microsoft.AspNetCore"), "no ASP.NET Core log output was captured")
        Assert.True(entries |> Array.exists (fun e -> e.Category.StartsWith "OpenIddict"), "no OpenIddict log output was captured")

        let leaks =
            [ for entry in entries do
                  match leaksIn secrets entry.Everything with
                  | [] -> ()
                  | labels -> yield String.Join(", ", labels) + $" in [{entry.Level}] {entry.Category}" ]
            |> List.distinct

        Assert.True(List.isEmpty leaks, "full addresses reached a log sink:" + Environment.NewLine + String.Join(Environment.NewLine, leaks))
    }
