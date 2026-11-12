/// The Bluesky sign-in end to end (specs/015, T007's smoke test): the real app,
/// the real client, the fake AT Protocol servers behind the guarded client's seam.
/// The full suite of sign-in tests is T008.
module Umbral.Tests.BlueskySignInTests

open System
open System.Net
open System.Text.RegularExpressions
open System.Threading.Tasks
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.FakeAtproto
open Umbral.Tests.Infrastructure.Journeys

let private connector (app: Fixtures.TestApp) enabled =
    ConnectorStore.save
        app.DbConfig
        app.Clock
        { Slug = "bluesky"
          Kind = Bluesky
          DisplayName = "Bluesky"
          ClientId = ""
          ClientSecretRef = ""
          ClientSecretHint = ""
          IssuerUrl = None
          AllowedEmailDomains = []
          PasskeyEnrolment = InvitationsOnly
          FediverseAccess = FediverseAccess.Any
          AppleKey = None
          IsEnabled = enabled
          IsShared = true
          CreatedUtc = app.Clock.GetUtcNow()
          UpdatedUtc = app.Clock.GetUtcNow() }
        None

let private ana =
    { Did = "did:plc:ewvi7nxzyoun6zhxrhs64oiz"
      Handle = "ana.bsky.social"
      DisplayName = Some "Ana"
      AvatarCid = Some "bafkreiavatar"
      Pds = "https://pds.test"
      Issuer = "https://bsky.test" }

let private mallory =
    { Did = "did:plc:z72i7hdynmk6r22z27h6tvur"
      Handle = "mallory.evil.social"
      DisplayName = Some "Mallory"
      AvatarCid = None
      Pds = "https://pds.evil.test"
      Issuer = "https://as.evil.test" }

let private setup () =
    task {
        let fake = new FakeAtproto()
        fake.AddAccount(ana, true)
        let! app = Fixtures.createAppWithBlueskyFake fake fakeSettings ignore
        do! connector app true
        let browser = Browser(app.Client)
        let! prepared = prepare app browser
        return fake, app, prepared
    }

/// From the app's authorize address to the handle POST.
let private start (app: Fixtures.TestApp) (prepared: Prepared) (handle: string) =
    task {
        let browser = Browser(app.Client)
        let! toPicker = browser.Get(authorizeUrl prepared.ClientId "openid profile")
        let pickerUrl = toPicker.Headers.Location |> string
        let returnUrl = Uri.UnescapeDataString(Regex.Match(pickerUrl, "returnUrl=([^&]+)").Groups[1].Value)
        let step = "/connect/bluesky/start?returnUrl=" + Uri.EscapeDataString returnUrl
        let! posted = browser.PostForm(step, "/connect/bluesky/start", [ "handle", handle; "returnUrl", returnUrl ])

        // A refusal shows its text, so a failing test says why.
        if posted.StatusCode <> HttpStatusCode.Found then
            let! html = posted.Content.ReadAsStringAsync()
            let! reason = Flows.scalar app "SELECT detail FROM security_events ORDER BY occurred_utc DESC LIMIT 1" []
            let message = Regex.Match(html, "(Enter a Bluesky|No Bluesky|Accounts on|did not answer|does not offer)[^<]*").Value
            failwith $"the step answered {int posted.StatusCode}: {message} ({reason})"

        return browser, posted
    }

[<Fact>]
let ``a handle served by DNS signs in, with the name, handle and profile and no email`` () =
    task {
        let! fake, app, prepared = setup ()
        let! browser, posted = start app prepared "Ana.Bsky.Social"
        Assert.Equal(HttpStatusCode.Found, posted.StatusCode)
        let location = posted.Headers.Location |> string
        Assert.StartsWith("https://bsky.test/oauth/authorize?", location)
        Assert.Contains("request_uri=", location)

        let callback = Uri(fake.Approve location)
        Assert.Equal("/connect/bluesky/callback", callback.AbsolutePath)
        let! back = browser.Get callback.PathAndQuery
        let replayed = back.Headers.Location |> string
        Assert.Contains("/connect/authorize", replayed)
        let! issued = browser.Get replayed
        let code = Regex.Match(issued.Headers.Location |> string, "code=([^&]+)").Groups[1].Value |> Uri.UnescapeDataString
        let! exchanged = exchangeCode app.Client prepared.ClientId prepared.ClientSecret code
        let! tokens = json exchanged
        let idToken = segmentOf 1 (strOf tokens "id_token")
        Assert.Equal("Ana", strOf idToken "name")
        Assert.Equal("ana.bsky.social", strOf idToken "preferred_username")
        Assert.Equal($"https://bsky.app/profile/{ana.Did}", strOf idToken "profile")
        Assert.Equal("bluesky", strOf idToken "idp")
        Assert.False(fst (idToken.TryGetProperty "email"))

        // The DPoP proofs were checked, and the token's came from the PAR's key.
        let proofs = fake.Proofs |> Array.filter (fun p -> p.Nonce = Some "n1")
        Assert.Equal(2, proofs.Length)
        Assert.Equal(proofs[0].Thumbprint, proofs[1].Thumbprint)
    }

[<Fact>]
let ``a server that answers for another account's DID is refused and creates no user`` () =
    task {
        let! fake, app, prepared = setup ()
        fake.AddAccount(mallory, false)
        (fake.Server mallory.Issuer).Token <- SubOverride ana.Did
        let! before = Flows.scalar app "SELECT count(*) FROM users" []
        let! browser, posted = start app prepared mallory.Handle
        Assert.Equal(HttpStatusCode.Found, posted.StatusCode)
        let callback = Uri(fake.Approve(posted.Headers.Location |> string))
        let! back = browser.Get callback.PathAndQuery
        Assert.Contains("msg=sign-in-failed", back.Headers.Location |> string)
        let! after = Flows.scalar app "SELECT count(*) FROM users" []
        Assert.Equal(before, after)
        let! events =
            Flows.scalar app "SELECT count(*) FROM security_events WHERE detail LIKE 'bluesky:did_mismatch;server=as.evil.test'" []
        Assert.Equal(1L, Convert.ToInt64 events)
    }

[<Fact>]
let ``the client metadata document names the client and its callback`` () =
    task {
        let! _, app, _ = setup ()
        let clientId = "https://umbral.test/connect/bluesky/client-metadata.json"
        let! response = app.Client.GetAsync "/connect/bluesky/client-metadata.json"
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        Assert.Equal("application/json", (Option.ofObj response.Content.Headers.ContentType |> Option.map _.MediaType |> Option.toObj))
        Assert.Equal("public, max-age=300", string response.Headers.CacheControl)
        let! document = json response
        Assert.Equal(clientId, strOf document "client_id")
        Assert.Equal("none", strOf document "token_endpoint_auth_method")
        let redirects = document.GetProperty "redirect_uris"
        Assert.Equal("https://umbral.test/connect/bluesky/callback", redirects[0].GetString())
        // No connector, no document.
        let! other = Fixtures.createApp ()
        let! gone = other.Client.GetAsync "/connect/bluesky/client-metadata.json"
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode)
    }

[<Fact>]
let ``a PDS that resolves only to a private address is refused before anything is sent to it`` () =
    task {
        let! fake, app, prepared = setup ()
        fake.MarkPrivate "pds.test"
        let browser = Browser(app.Client)
        let! toPicker = browser.Get(authorizeUrl prepared.ClientId "openid profile")
        let pickerUrl = toPicker.Headers.Location |> string
        let returnUrl = Uri.UnescapeDataString(Regex.Match(pickerUrl, "returnUrl=([^&]+)").Groups[1].Value)
        let step = "/connect/bluesky/start?returnUrl=" + Uri.EscapeDataString returnUrl
        let! posted = browser.PostForm(step, "/connect/bluesky/start", [ "handle", "ana.bsky.social"; "returnUrl", returnUrl ])
        Assert.Equal(HttpStatusCode.UnprocessableEntity, posted.StatusCode)
        let! html = posted.Content.ReadAsStringAsync()
        Assert.Contains("Enter a Bluesky handle like you.bsky.social.", html)
        Assert.Contains("value=\"ana.bsky.social\"", html)
        Assert.Equal(0, fake.CountOf "https://pds.test")
        let! events = Flows.scalar app "SELECT count(*) FROM security_events WHERE detail = 'bluesky:not_a_handle'" []
        Assert.Equal(1L, Convert.ToInt64 events)
    }
