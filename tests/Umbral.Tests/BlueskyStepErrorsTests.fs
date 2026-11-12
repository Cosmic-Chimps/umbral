/// The Bluesky handle step's refusals and the origins it works on (specs/015, T008).
/// Every refusal is a 422 with the typed value kept in the field, the message under
/// it and one word in the security log; none of them reaches the authorization
/// server with a pushed request.
module Umbral.Tests.BlueskyStepErrorsTests

open System
open System.Net
open System.Text.Json.Nodes
open Microsoft.Extensions.DependencyInjection
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Infrastructure.Providers
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.FakeAtproto
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.BlueskySignInTests

/// The step refused `typed`: 422, the value still in the field, the message, and
/// the one log entry.
let private expectRefusal (app: Fixtures.TestApp) (prepared: Prepared) (typed: string) (message: string) (detail: string) =
    task {
        let! _, posted = attempt app prepared typed
        Assert.Equal(HttpStatusCode.UnprocessableEntity, posted.StatusCode)
        let! html = posted.Content.ReadAsStringAsync()
        Assert.Contains(message, html)
        Assert.Contains($"value=\"{typed}\"", html)
        let! details = failures app
        Assert.Equal(detail, Assert.Single details)
    }

let private noPar (fake: FakeAtproto) =
    Assert.Equal(0, fake.CountOf "/oauth/par")
    Assert.Empty (fake.Server ana.Issuer).Pars

[<Theory>]
[<InlineData("not a handle")>]
[<InlineData("ana")>]
let ``text that is not a handle is refused as not_a_handle`` (typed: string) =
    task {
        let! fake, app, prepared = setup ()
        do! expectRefusal app prepared typed "Enter a Bluesky handle like you.bsky.social." "bluesky:not_a_handle"
        Assert.Empty fake.Requests
    }

[<Fact>]
let ``a domain with no TXT record and no well-known answer is handle_not_found`` () =
    task {
        let! fake, app, prepared = setup ()

        do!
            expectRefusal
                app
                prepared
                "nobody.example.com"
                "No Bluesky account was found for nobody.example.com."
                "bluesky:handle_not_found"

        noPar fake
    }

[<Fact>]
let ``a handle whose TXT points at another account's DID is not claimed back`` () =
    task {
        let! fake, app, prepared = setup ()
        // The record says Ana's DID; Ana's document names only ana.bsky.social.
        fake.SetTxt("_atproto.impostor.evil.social", [| "did=" + ana.Did |])

        do!
            expectRefusal
                app
                prepared
                "impostor.evil.social"
                "No Bluesky account was found for impostor.evil.social."
                "bluesky:handle_not_found"

        noPar fake
    }

[<Fact>]
let ``a handle whose host is private is refused as not_a_handle and the host is never asked`` () =
    task {
        let! fake, app, prepared = setup ()
        fake.MarkPrivate "inside.example.com"

        do!
            expectRefusal
                app
                prepared
                "inside.example.com"
                "Enter a Bluesky handle like you.bsky.social."
                "bluesky:not_a_handle"

        Assert.Equal(0, fake.CountOf "inside.example.com")
    }

[<Fact>]
let ``a PLC directory that answers 500 is server_unreachable`` () =
    task {
        let! fake, app, prepared = setup ()
        fake.PlcFails <- true

        do!
            expectRefusal
                app
                prepared
                "ana.bsky.social"
                "plc.test did not answer. Try again in a moment."
                "bluesky:server_unreachable;server=plc.test"

        noPar fake
    }

let private breakers: (string * (JsonObject -> unit)) list =
    [ "par", (fun m -> m.Remove "require_pushed_authorization_requests" |> ignore)
      "s256", (fun m -> m["code_challenge_methods_supported"] <- JsonArray())
      "es256", (fun m -> m["dpop_signing_alg_values_supported"] <- JsonArray())
      "iss", (fun m -> m.Remove "authorization_response_iss_parameter_supported" |> ignore)
      "atproto", (fun m -> m["scopes_supported"] <- JsonArray())
      "metadata", (fun m -> m.Remove "client_id_metadata_document_supported" |> ignore)
      "issuer", (fun m -> m["issuer"] <- JsonValue.Create "https://elsewhere.test") ]

[<Theory>]
[<InlineData("par")>]
[<InlineData("s256")>]
[<InlineData("es256")>]
[<InlineData("iss")>]
[<InlineData("atproto")>]
[<InlineData("metadata")>]
[<InlineData("issuer")>]
let ``a server whose metadata refuses a condition is server_unsupported`` (breaker: string) =
    task {
        let! fake, app, prepared = setup ()
        (breakers |> List.find (fun (name, _) -> name = breaker) |> snd) (fake.Server ana.Issuer).Metadata

        do!
            expectRefusal
                app
                prepared
                "ana.bsky.social"
                "does not offer the sign-in Umbral uses."
                "bluesky:server_unsupported;server=pds.test"

        noPar fake
    }

[<Fact>]
let ``a PDS that names two authorization servers is server_unsupported`` () =
    task {
        let! fake, app, prepared = setup ()
        fake.SetResource(ana.Pds, """{"authorization_servers":["https://bsky.test","https://other.test"]}""")

        do!
            expectRefusal
                app
                prepared
                "ana.bsky.social"
                "does not offer the sign-in Umbral uses."
                "bluesky:server_unsupported;server=pds.test"

        noPar fake
    }

[<Fact>]
let ``a host lookup that times out shows the unreachable message, not an error page`` () =
    task {
        let! fake, app, prepared =
            setupWith fakeSettings (fun builder ->
                builder.Services.AddSingleton<Fediverse.HostResolver>(
                    Fediverse.HostResolver(fun _ _ -> raise (OperationCanceledException())))
                |> ignore)

        let! _, posted = attempt app prepared "ana.bsky.social"
        Assert.Equal(HttpStatusCode.UnprocessableEntity, posted.StatusCode)
        let! html = posted.Content.ReadAsStringAsync()
        Assert.Contains("did not answer. Try again in a moment.", html)
        let! details = failures app
        Assert.StartsWith("bluesky:server_unreachable;server=", Assert.Single details)
        noPar fake
    }

[<Fact>]
let ``with only other servers listed, a server not on the list is refused before anything is pushed`` () =
    task {
        let! fake, app, prepared = setup ()

        let! _ =
            ConnectorStore.setFediverseAccess app.DbConfig app.Clock "bluesky" { OnlyListed = true; Servers = [ "as.other.test" ] }

        do!
            expectRefusal
                app
                prepared
                "ana.bsky.social"
                "Accounts on bsky.test cannot sign in here."
                "bluesky:server_not_allowed;server=bsky.test"

        noPar fake
    }

[<Fact>]
let ``a post with no antiforgery token is refused and pushes nothing`` () =
    task {
        let! fake, app, prepared = setup ()
        let browser = Browser(app.Client)
        let! posted = browser.PostWithoutToken("/connect/bluesky/start", [ "handle", "ana.bsky.social"; "returnUrl", "/" ])
        Assert.Equal(HttpStatusCode.BadRequest, posted.StatusCode)
        noPar fake
        Assert.Empty fake.Requests
    }

// ---------------------------------------------------------------------------
// Origins (FR-004)
// ---------------------------------------------------------------------------

/// Ana's servers over http, as the development setting reads them.
let private devAna = { ana with Pds = "http://pds.test"; Issuer = "http://bsky.test" }

let private devApp (origin: string) =
    task {
        let fake = new FakeAtproto()
        fake.AddAccount(devAna, true)

        let! app =
            Fixtures.createAppWithBlueskyFake
                fake
                (fakeSettings @ [ "UMBRAL:INSECURE_ALLOW_HTTP", "true"; "UMBRAL:PUBLIC_ORIGIN", origin ])
                ignore

        app.Client.BaseAddress <- Uri origin
        do! connector app true
        return fake, app
    }

let private postStep (app: Fixtures.TestApp) =
    task {
        let browser = Browser(app.Client)
        return! browser.PostForm("/admin/setup", "/connect/bluesky/start", [ "handle", "ana.bsky.social"; "returnUrl", "/" ])
    }

[<Fact>]
let ``a 127.0.0.1 origin with development on uses the localhost client and serves no metadata document`` () =
    task {
        let! fake, app = devApp "http://127.0.0.1:5173"
        let! posted = postStep app
        Assert.Equal(HttpStatusCode.Found, posted.StatusCode)
        Assert.StartsWith("http://bsky.test/oauth/authorize?", posted.Headers.Location |> string)

        let par = (fake.Server devAna.Issuer).Pars.Values |> Seq.exactlyOne
        Assert.StartsWith("http://localhost?redirect_uri=", par.ClientId)
        Assert.Equal("http://127.0.0.1:5173/connect/bluesky/callback", par.RedirectUri)

        let! document = app.Client.GetAsync "/connect/bluesky/client-metadata.json"
        Assert.Equal(HttpStatusCode.NotFound, document.StatusCode)
    }

[<Fact>]
let ``a localhost origin is refused as unsuitable even with development on`` () =
    task {
        let! fake, app = devApp "http://localhost:5173"
        let! posted = postStep app
        Assert.Equal(HttpStatusCode.Found, posted.StatusCode)
        Assert.Contains("msg=sign-in-failed", posted.Headers.Location |> string)
        let! details = failures app
        Assert.Equal("bluesky:origin_unsuitable", Assert.Single details)
        Assert.Empty fake.Requests
        Assert.Empty (fake.Server devAna.Issuer).Pars
    }

[<Fact>]
let ``the client metadata document is 404 when there is no Bluesky connector`` () =
    task {
        let fake = new FakeAtproto()
        let! app = Fixtures.createAppWithBlueskyFake fake fakeSettings ignore
        let! response = app.Client.GetAsync "/connect/bluesky/client-metadata.json"
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)
    }
