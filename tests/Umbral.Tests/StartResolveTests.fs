/// The start of a sign-in asks the adapter where the provider's endpoints are
/// before it builds the authorize URL (specs/002 T015, research R3). Run against
/// the handler with a hand-made adapter, so the failure path does not depend on
/// any real provider being down.
module Umbral.Tests.StartResolveTests

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.DataProtection
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Time.Testing
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Infrastructure.ProviderAdapters
open Umbral.Tests.Infrastructure

let private endpoints =
    { AuthorizationUrl = "https://idp.example.test/authorize"
      TokenUrl = "https://idp.example.test/token"
      UserInfoUrl = ""
      Scopes = [ "openid" ]
      SupportsPkce = true
      UsesNonce = true }

/// An adapter that answers `Resolve` as told, and remembers the request it was
/// given to build a URL from.
type private ScriptedAdapter(resolved: Result<ProviderEndpoints, ConnectorError>) =
    member val Built: AuthorizeRequest option = None with get, set

    interface IProviderAdapter with
        member _.Kind = Fake
        member _.Resolve _ = Task.FromResult resolved

        member this.BuildAuthorizeUrl request =
            this.Built <- Some request
            Uri $"{request.Endpoints.AuthorizationUrl}?state={request.State}"

        member _.ExchangeAndFetch _ = failwith "the start of a sign-in never exchanges a code"

let private connector: Connector =
    { Slug = "home"
      Kind = Fake
      DisplayName = "Home"
      ClientId = "client-id"
      ClientSecretRef = ""
      ClientSecretHint = ""
      IssuerUrl = None
      AllowedEmailDomains = []
      IsEnabled = true
      IsShared = true
      CreatedUtc = DateTimeOffset.UnixEpoch
      UpdatedUtc = DateTimeOffset.UnixEpoch }

/// Run `Start.handler` for the `home` connector, and hand back what it did.
let private startWith (adapter: ScriptedAdapter) =
    task {
        let path = IO.Path.Combine(IO.Path.GetTempPath(), "umbral-start-" + Guid.NewGuid().ToString "N" + ".db")
        let config: Database.DbConfig = { DataSource = path }

        try
            do! Database.initializeSchema config
            let clock = FakeTimeProvider(DateTimeOffset.UtcNow)
            let! _ = ConnectorStore.create config (clock.GetUtcNow()) connector ("local:x", "cret")

            let services = ServiceCollection()
            services.AddSingleton<Database.DbConfig>(config) |> ignore
            services.AddSingleton<TimeProvider>(clock) |> ignore
            services.AddSingleton<IDataProtectionProvider>(DataProtectionProvider.Create "start-resolve-tests") |> ignore
            services.AddSingleton<ProviderAdapters.Registry>(Registry [ adapter :> IProviderAdapter ]) |> ignore

            match PublicOrigin.parse "https://umbral.test" with
            | Ok origin -> services.AddSingleton<PublicOrigin.PublicOrigin>(origin) |> ignore
            | Error error -> failwith (PublicOrigin.describe error)

            use provider = services.BuildServiceProvider()
            let ctx = DefaultHttpContext()
            ctx.RequestServices <- provider
            ctx.Request.QueryString <- QueryString "?returnUrl=%2Fconnect%2Fauthorize%3Fclient_id%3Dgrafana"

            do! Umbral.Handlers.Connect.Start.handler "home" ctx

            let! detail =
                task {
                    use! connection = Database.openConnection config
                    use command = connection.CreateCommand()
                    command.CommandText <- "SELECT detail FROM security_events WHERE event_type = 'sign_in_failed'"
                    let! value = command.ExecuteScalarAsync()
                    return Option.ofObj value |> Option.map string
                }

            return ctx, detail
        finally
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            for suffix in [ ""; "-wal"; "-shm" ] do
                if IO.File.Exists(path + suffix) then
                    IO.File.Delete(path + suffix)
    }

[<Fact>]
let ``the endpoints the adapter resolved are the ones the authorize url is built from`` () =
    task {
        let adapter = ScriptedAdapter(Ok endpoints)
        let! ctx, detail = startWith adapter

        Assert.Equal(StatusCodes.Status302Found, ctx.Response.StatusCode)
        Assert.StartsWith("https://idp.example.test/authorize?state=", string ctx.Response.Headers.Location)

        // The request carried them, not the connector's static entry (which for
        // `Fake` is a path on this instance).
        Assert.Equal(endpoints, adapter.Built.Value.Endpoints)
        Assert.True detail.IsNone
        // And the round trip began: the correlation cookie is set.
        Assert.Contains("Set-Cookie", ctx.Response.Headers.Keys)
    }

[<Fact>]
let ``a provider that cannot be resolved shows the end user a notice and records the cause`` () =
    task {
        let adapter = ScriptedAdapter(Error(ProviderUnreachable "the connection failed (Connection refused)"))
        let! ctx, detail = startWith adapter

        // Back to the sign-in page of the app they came from, with a code.
        let location = string ctx.Response.Headers.Location
        Assert.StartsWith("/signin?returnUrl=", location)
        Assert.Contains("msg=provider-unreachable", location)

        // The operator gets the cause; the end user's page has none of it.
        Assert.Equal(Some "unreachable: the connection failed (Connection refused)", detail)
        Assert.DoesNotContain("Connection refused", location)

        // No authorize URL was built and no correlation began, so no cookie.
        Assert.True adapter.Built.IsNone
        Assert.DoesNotContain("Set-Cookie", ctx.Response.Headers.Keys)
    }

[<Fact>]
let ``a provider that answers wrongly shows the generic sign-in failure`` () =
    task {
        let adapter = ScriptedAdapter(Error(MalformedResponse "issuer mismatch"))
        let! ctx, detail = startWith adapter

        Assert.Contains("msg=sign-in-failed", string ctx.Response.Headers.Location)
        Assert.Equal(Some "malformed_response: issuer mismatch", detail)
    }
