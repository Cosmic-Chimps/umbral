/// The Bluesky callback's refusals, in the order of research R6 (specs/015, T008).
/// Each one redirects to the sign-in error page with a code, writes one word to the
/// security log, and creates no user.
module Umbral.Tests.BlueskyCallbackTests

open System
open System.Net
open System.Text.RegularExpressions
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.FakeAtproto
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.BlueskySignInTests

let private tokenPost (server: FakeAuthorizationServer) = $"POST {server.Issuer}/oauth/token"

/// The step done and the person agreeing: the browser, and the address the
/// authorization server sends it back to.
let private approved (fake: FakeAtproto) (app: Fixtures.TestApp) (prepared: Prepared) =
    task {
        let! browser, posted = start app prepared "ana.bsky.social"
        let callback = Uri(fake.Approve(posted.Headers.Location |> string))
        return browser, callback.PathAndQuery
    }

/// The callback is refused: back to the app's sign-in page with `code`.
let private assertRefused (response: Net.Http.HttpResponseMessage) (code: string) =
    Assert.Equal(HttpStatusCode.Found, response.StatusCode)
    let location = response.Headers.Location |> string
    Assert.StartsWith("/signin?returnUrl=", location)
    Assert.EndsWith($"&msg={code}", location)

[<Fact>]
let ``a wrong iss is refused before any token request`` () =
    task {
        let! fake, app, prepared = setup ()
        let server = fake.Server ana.Issuer
        server.Approval <- WrongIss "https://evil.test"
        let! users = userCount app
        let! browser, url = approved fake app prepared
        let! back = browser.Get url
        assertRefused back "sign-in-failed"
        let! details = failures app
        Assert.Equal("bluesky:iss_mismatch;server=bsky.test", Assert.Single details)
        Assert.Equal(0, fake.CountOf(tokenPost server))
        let! after = userCount app
        Assert.Equal(users, after)
    }

[<Fact>]
let ``a missing iss is refused before any token request`` () =
    task {
        let! fake, app, prepared = setup ()
        let server = fake.Server ana.Issuer
        server.Approval <- NoIss
        let! users = userCount app
        let! browser, url = approved fake app prepared
        let! back = browser.Get url
        assertRefused back "sign-in-failed"
        let! details = failures app
        Assert.Equal("bluesky:iss_mismatch;server=bsky.test", Assert.Single details)
        Assert.Equal(0, fake.CountOf(tokenPost server))
        let! after = userCount app
        Assert.Equal(users, after)
    }

[<Fact>]
let ``an access_denied answer goes to the access-denied code and creates no user`` () =
    task {
        let! fake, app, prepared = setup ()
        let server = fake.Server ana.Issuer
        server.Approval <- AccessDenied
        let! users = userCount app
        let! browser, url = approved fake app prepared
        let! back = browser.Get url
        assertRefused back "access-denied"
        let! details = failures app
        Assert.StartsWith("bluesky:", Assert.Single details)
        Assert.Equal(0, fake.CountOf(tokenPost server))
        let! after = userCount app
        Assert.Equal(users, after)
    }

[<Fact>]
let ``a tampered state is refused and creates no user`` () =
    task {
        let! fake, app, prepared = setup ()
        let! users = userCount app
        let! browser, url = approved fake app prepared
        let! back = browser.Get(Regex.Replace(url, "state=[^&]+", "state=tampered"))
        assertRefused back "sign-in-failed"
        Assert.Equal(0, fake.CountOf(tokenPost (fake.Server ana.Issuer)))
        let! after = userCount app
        Assert.Equal(users, after)
    }

[<Fact>]
let ``the same callback replayed a second time is refused`` () =
    task {
        let! fake, app, prepared = setup ()
        let! browser, url = approved fake app prepared
        let! first = browser.Get url
        Assert.Contains("/connect/authorize", first.Headers.Location |> string)
        let! users = userCount app

        // The first callback used the cookie up: there is no round trip to return
        // to, so the bare page is all there is.
        let! second = browser.Get url
        Assert.Equal(HttpStatusCode.Found, second.StatusCode)
        Assert.Equal("/signin?msg=sign-in-failed", second.Headers.Location |> string)
        let! after = userCount app
        Assert.Equal(users, after)
        Assert.Equal(1, fake.CountOf(tokenPost (fake.Server ana.Issuer)))
    }

[<Theory>]
[<InlineData("bearer")>]
[<InlineData("scope")>]
let ``a token answer that is not a DPoP atproto token is token_invalid`` (mode: string) =
    task {
        let! fake, app, prepared = setup ()
        (fake.Server ana.Issuer).Token <- if mode = "bearer" then TokenType "Bearer" else NoAtprotoScope
        let! users = userCount app
        let! browser, url = approved fake app prepared
        let! back = browser.Get url
        assertRefused back "sign-in-failed"
        let! details = failures app
        Assert.Equal("bluesky:token_invalid;server=bsky.test", Assert.Single details)
        let! after = userCount app
        Assert.Equal(users, after)
    }

[<Fact>]
let ``a token endpoint that fails is exchange_failed`` () =
    task {
        let! fake, app, prepared = setup ()
        (fake.Server ana.Issuer).Token <- ServerError
        let! users = userCount app
        let! browser, url = approved fake app prepared
        let! back = browser.Get url
        assertRefused back "sign-in-failed"
        let! details = failures app
        Assert.Equal("bluesky:exchange_failed;server=bsky.test", Assert.Single details)
        let! after = userCount app
        Assert.Equal(users, after)
    }

[<Fact>]
let ``a DID whose own PDS names another authorization server is not_authoritative`` () =
    task {
        let! fake, app, prepared = setup ()
        let! users = userCount app
        let! browser, url = approved fake app prepared

        // The server answered for Ana, but her document now points at a PDS that
        // trusts another server: the one that answered is not hers.
        fake.AddAccount({ ana with Pds = "https://pds2.test"; Issuer = "https://as2.test" }, true)

        let! back = browser.Get url
        assertRefused back "sign-in-failed"
        let! details = failures app
        Assert.Equal("bluesky:not_authoritative;server=bsky.test", Assert.Single details)
        let! after = userCount app
        Assert.Equal(users, after)
    }

[<Fact>]
let ``a list narrowed after the step refuses the callback with account-not-allowed`` () =
    task {
        let! fake, app, prepared = setup ()
        let! users = userCount app
        let! browser, url = approved fake app prepared

        let! _ =
            ConnectorStore.setFediverseAccess app.DbConfig app.Clock "bluesky" { OnlyListed = true; Servers = [ "as.other.test" ] }

        let! back = browser.Get url
        assertRefused back "account-not-allowed"
        let! details = failures app
        Assert.Equal("bluesky:server_not_allowed;server=bsky.test", Assert.Single details)
        Assert.Equal(0, fake.CountOf(tokenPost (fake.Server ana.Issuer)))
        let! after = userCount app
        Assert.Equal(users, after)
    }

[<Fact>]
let ``a connector disabled after the step refuses the callback`` () =
    task {
        let! fake, app, prepared = setup ()
        let! users = userCount app
        let! browser, url = approved fake app prepared
        do! connector app false
        let! back = browser.Get url
        assertRefused back "sign-in-failed"
        Assert.Equal(0, fake.CountOf(tokenPost (fake.Server ana.Issuer)))
        let! after = userCount app
        Assert.Equal(users, after)
    }

// ---------------------------------------------------------------------------
// The fresh authority check, step 7, case by case
// ---------------------------------------------------------------------------

/// The step done, then `change`, then the callback: refused as `not_authoritative`
/// with the token already issued, and no user created.
let private notAuthoritativeAfter (change: FakeAtproto -> unit) =
    task {
        let! fake, app, prepared = setup ()
        let! users = userCount app
        let! browser, url = approved fake app prepared
        change fake

        let! back = browser.Get url
        assertRefused back "sign-in-failed"
        let! details = failures app
        Assert.Equal("bluesky:not_authoritative;server=bsky.test", Assert.Single details)
        // The exchange did happen: it is the fresh check that refuses.
        Assert.Equal(1, fake.CountOf(tokenPost (fake.Server ana.Issuer)))
        let! after = userCount app
        Assert.Equal(users, after)
    }

[<Fact>]
let ``a fresh DID document that answers 500 is not_authoritative`` () =
    notAuthoritativeAfter (fun fake -> fake.PlcFails <- true)

[<Fact>]
let ``a fresh DID document whose id differs is not_authoritative`` () =
    notAuthoritativeAfter (fun fake -> fake.SetDocumentId(ana.Did, "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb"))

[<Fact>]
let ``fresh metadata whose issuer differs only by a trailing slash is not_authoritative`` () =
    notAuthoritativeAfter (fun fake -> (fake.Server ana.Issuer).Metadata["issuer"] <- System.Text.Json.Nodes.JsonValue.Create "https://bsky.test/")

[<Fact>]
let ``fresh metadata whose issuer differs only by case is not_authoritative`` () =
    notAuthoritativeAfter (fun fake -> (fake.Server ana.Issuer).Metadata["issuer"] <- System.Text.Json.Nodes.JsonValue.Create "https://BSKY.test")

[<Fact>]
let ``fresh metadata that now fails a condition is not_authoritative`` () =
    notAuthoritativeAfter (fun fake -> (fake.Server ana.Issuer).Metadata.Remove "require_pushed_authorization_requests" |> ignore)

// ---------------------------------------------------------------------------
// The server is recorded before the session exists, and after it (B2)
// ---------------------------------------------------------------------------

[<Fact>]
let ``the server is recorded before the sign-in is finished and again after it`` () =
    task {
        let! fake, app, prepared = setup ()
        let! _ = signIn fake app prepared "ana.bsky.social"
        let! first = Flows.scalar app "SELECT provider_server FROM linked_accounts WHERE provider_kind = 'bluesky'" []
        Assert.Equal("bsky.test", first)

        // The link exists but has lost its server, and the list now names it.
        let! _ = Flows.scalar app "UPDATE linked_accounts SET provider_server = NULL" []
        let! _ = ConnectorStore.setFediverseAccess app.DbConfig app.Clock "bluesky" { OnlyListed = true; Servers = [ "bsky.test" ] }

        // A log of the writes, in order.
        let! _ = Flows.scalar app "CREATE TABLE write_log (id INTEGER PRIMARY KEY AUTOINCREMENT, what TEXT)" []

        let! _ =
            Flows.scalar
                app
                "CREATE TRIGGER log_server AFTER UPDATE OF provider_server ON linked_accounts
                 BEGIN INSERT INTO write_log (what) VALUES ('server'); END"
                []

        let! _ =
            Flows.scalar app "CREATE TRIGGER log_user AFTER UPDATE ON users BEGIN INSERT INTO write_log (what) VALUES ('user'); END" []

        let! _ = signIn fake app prepared "ana.bsky.social"
        let! log = Flows.scalar app "SELECT group_concat(what, ',') FROM (SELECT what FROM write_log ORDER BY id)" []
        let steps = log.Split ','
        Assert.Equal("server", steps[0])
        Assert.Equal("server", steps[steps.Length - 1])
        Assert.Contains("user", steps)

        let! recorded = Flows.scalar app "SELECT provider_server FROM linked_accounts WHERE provider_kind = 'bluesky'" []
        Assert.Equal("bsky.test", recorded)

        // And the re-check, which refuses a link with no server, now admits it.
        let! userId = Flows.scalar app "SELECT user_id FROM linked_accounts WHERE provider_kind = 'bluesky'" []
        let! refused = BlueskyAccounts.recheckServer app.DbConfig (Some "bluesky") userId
        Assert.Equal(None, refused)
    }
