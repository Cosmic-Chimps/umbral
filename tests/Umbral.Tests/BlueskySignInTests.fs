/// The Bluesky sign-in end to end (specs/015, T007's smoke test): the real app,
/// the real client, the fake AT Protocol servers behind the guarded client's seam.
/// The full suite of sign-in tests is T008.
module Umbral.Tests.BlueskySignInTests

open System
open System.Net
open System.Text.RegularExpressions
open System.Text.Json
open System.Threading.Tasks
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.FakeAtproto
open Umbral.Tests.Infrastructure.Journeys

let connector (app: Fixtures.TestApp) enabled =
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

let ana =
    { Did = "did:plc:ewvi7nxzyoun6zhxrhs64oiz"
      Handle = "ana.bsky.social"
      DisplayName = Some "Ana"
      AvatarCid = Some "bafkreiavatar"
      Pds = "https://pds.test"
      Issuer = "https://bsky.test" }

let mallory =
    { Did = "did:plc:z72i7hdynmk6r22z27h6tvur"
      Handle = "mallory.evil.social"
      DisplayName = Some "Mallory"
      AvatarCid = None
      Pds = "https://pds.evil.test"
      Issuer = "https://as.evil.test" }

/// The app with Ana on the fake, the connector on, and a registered client app.
let setupWith (settings: (string * string) list) (customize: Microsoft.AspNetCore.Builder.WebApplicationBuilder -> unit) =
    task {
        let fake = new FakeAtproto()
        fake.AddAccount(ana, true)
        let! app = Fixtures.createAppWithBlueskyFake fake settings customize
        do! connector app true
        let browser = Browser(app.Client)
        let! prepared = prepare app browser
        return fake, app, prepared
    }

let setup () = setupWith fakeSettings ignore

/// The step's POST, whatever it answers: a new browser, the app's authorize
/// request (asking for `scope`), and the handle typed.
let attemptWith (app: Fixtures.TestApp) (prepared: Prepared) (scope: string) (handle: string) =
    task {
        let browser = Browser(app.Client)
        let! toPicker = browser.Get(authorizeUrl prepared.ClientId scope)
        let pickerUrl = toPicker.Headers.Location |> string
        let returnUrl = Uri.UnescapeDataString(Regex.Match(pickerUrl, "returnUrl=([^&]+)").Groups[1].Value)
        let step = "/connect/bluesky/start?returnUrl=" + Uri.EscapeDataString returnUrl
        let! posted = browser.PostForm(step, "/connect/bluesky/start", [ "handle", handle; "returnUrl", returnUrl ])
        return browser, posted
    }

let attempt (app: Fixtures.TestApp) (prepared: Prepared) (handle: string) =
    attemptWith app prepared "openid profile" handle

/// From the app's authorize address to the handle POST, which must redirect.
let startWith (app: Fixtures.TestApp) (prepared: Prepared) (scope: string) (handle: string) =
    task {
        let! browser, posted = attemptWith app prepared scope handle

        // A refusal shows its text, so a failing test says why.
        if posted.StatusCode <> HttpStatusCode.Found then
            let! html = posted.Content.ReadAsStringAsync()
            let! reason = Flows.scalar app "SELECT detail FROM security_events ORDER BY occurred_utc DESC LIMIT 1" []
            let message = Regex.Match(html, "(Enter a Bluesky|No Bluesky|Accounts on|did not answer|does not offer)[^<]*").Value
            failwith $"the step answered {int posted.StatusCode}: {message} ({reason})"

        return browser, posted
    }

let start (app: Fixtures.TestApp) (prepared: Prepared) (handle: string) =
    startWith app prepared "openid profile" handle

/// The sign-in's result: the browser (with its session), both tokens' claims and the codes.
type Completed =
    { Browser: Browser
      Tokens: JsonElement
      IdToken: JsonElement
      /// What the authorization server issued.
      ProviderCode: string
      /// What Umbral issued to the app.
      AppCode: string }

/// The whole journey: the step, the person agreeing, the callback, the app's code and
/// the exchange.
let signInWith (fake: FakeAtproto) (app: Fixtures.TestApp) (prepared: Prepared) (scope: string) (handle: string) =
    task {
        let! browser, posted = startWith app prepared scope handle
        let callback = Uri(fake.Approve(posted.Headers.Location |> string))
        let! back = browser.Get callback.PathAndQuery
        let replayed = back.Headers.Location |> string
        Assert.Contains("/connect/authorize", replayed)
        let! issued = browser.Get replayed
        let appCode = Regex.Match(issued.Headers.Location |> string, "code=([^&]+)").Groups[1].Value |> Uri.UnescapeDataString
        let! exchanged = exchangeCode app.Client prepared.ClientId prepared.ClientSecret appCode
        let! tokens = json exchanged

        return
            { Browser = browser
              Tokens = tokens
              IdToken = segmentOf 1 (strOf tokens "id_token")
              ProviderCode = Web.HttpUtility.ParseQueryString(callback.Query)["code"] |> Option.ofObj |> Option.defaultValue ""
              AppCode = appCode }
    }

let signIn (fake: FakeAtproto) app prepared handle = signInWith fake app prepared "openid profile" handle

/// The details of every sign-in failure, oldest first.
let failures (app: Fixtures.TestApp) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT ifnull(detail, '') FROM security_events WHERE event_type = 'sign_in_failed' ORDER BY id"
        use! reader = command.ExecuteReaderAsync()
        let rows = ResizeArray<string>()

        while reader.Read() do
            rows.Add(reader.GetString 0)

        return List.ofSeq rows
    }

/// Every security event's detail, whatever its kind.
let allDetails (app: Fixtures.TestApp) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT ifnull(detail, '') FROM security_events ORDER BY id"
        use! reader = command.ExecuteReaderAsync()
        let rows = ResizeArray<string>()

        while reader.Read() do
            rows.Add(reader.GetString 0)

        return List.ofSeq rows
    }

let userCount (app: Fixtures.TestApp) =
    task {
        let! count = Flows.scalar app "SELECT count(*) FROM users" []
        return Convert.ToInt64 count
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


// ---------------------------------------------------------------------------
// T008: more sign-ins
// ---------------------------------------------------------------------------

let private bob =
    { Did = "did:plc:77777777777777777777aaaa"
      Handle = "bob.example.org"
      DisplayName = Some "Bob"
      AvatarCid = None
      Pds = "https://pds.test"
      Issuer = "https://bsky.test" }

[<Fact>]
let ``a handle served over HTTPS only, with no TXT record, signs in`` () =
    task {
        let! fake, app, prepared = setup ()
        fake.AddAccount(bob, false)
        let! done' = signIn fake app prepared "Bob.Example.Org"
        Assert.Equal("Bob", strOf done'.IdToken "name")
        Assert.Equal("bob.example.org", strOf done'.IdToken "preferred_username")
        Assert.Equal(1, fake.CountOf "GET https://bob.example.org/.well-known/atproto-did")
    }

[<Fact>]
let ``a DID typed directly signs in, and the handle shown is the document's, once it resolves back to the DID`` () =
    task {
        let! fake, app, prepared = setup ()
        let! done' = signIn fake app prepared ana.Did
        Assert.Equal("ana.bsky.social", strOf done'.IdToken "preferred_username")
        Assert.Equal("Ana", strOf done'.IdToken "name")
        // No handle was typed, so none was resolved at the start: the DID went
        // straight to the directory. The document's claim was resolved forward at
        // the callback, once, and only that makes it a handle.
        Assert.Equal(0, fake.CountOf "atproto-did")
        Assert.Equal<string array>([| "_atproto.ana.bsky.social" |], fake.TxtQueries)
        let! avatar = Flows.scalar app "SELECT avatar_url FROM users WHERE display_name = 'Ana'" []
        Assert.Contains("cid=bafkreiavatar", avatar)
    }

[<Fact>]
let ``a handle the document claims over HTTPS is verified over HTTPS`` () =
    task {
        let! fake, app, prepared = setup ()
        fake.AddAccount(bob, false)
        let! done' = signIn fake app prepared bob.Did
        Assert.Equal("bob.example.org", strOf done'.IdToken "preferred_username")
        Assert.Equal(1, fake.CountOf "GET https://bob.example.org/.well-known/atproto-did")
    }

/// A document that claims `claimed` for Ana's DID, a handle of someone else.
let private claimsSomeoneElsesHandle (fake: FakeAtproto) =
    fake.AddAccount(bob, true)
    fake.SetAlsoKnownAs(ana.Did, [ "at://" + bob.Handle ])

[<Fact>]
let ``a document that claims another account's handle gets no preferred_username`` () =
    task {
        let! fake, app, prepared = setup ()
        claimsSomeoneElsesHandle fake
        let! done' = signIn fake app prepared ana.Did

        // The handle resolves to Bob's DID, not to this one: it is not shown.
        Assert.Equal<string array>([| "_atproto." + bob.Handle |], fake.TxtQueries)
        Assert.False(fst (done'.IdToken.TryGetProperty "preferred_username"))
        // The name falls back to the profile's.
        Assert.Equal("Ana", strOf done'.IdToken "name")
        let! stored = Flows.scalar app "SELECT ifnull(preferred_username, '') FROM users WHERE display_name = 'Ana'" []
        Assert.Equal("", stored)
    }

[<Fact>]
let ``a claimed handle that resolves to nothing gets no preferred_username and the name falls back to the DID`` () =
    task {
        let! fake, app, prepared = setup ()
        fake.ProfileFails <- true
        fake.SetAlsoKnownAs(ana.Did, [ "at://nobody.example.org" ])
        let! done' = signIn fake app prepared ana.Did
        Assert.False(fst (done'.IdToken.TryGetProperty "preferred_username"))
        Assert.Equal(ana.Did, strOf done'.IdToken "name")
    }

[<Fact>]
let ``a handle no longer verified is cleared from the user, and the app stops getting it`` () =
    task {
        let! fake, app, prepared = setup ()
        let! first = signIn fake app prepared "ana.bsky.social"
        Assert.Equal("ana.bsky.social", strOf first.IdToken "preferred_username")
        let! users = userCount app

        // The document now claims a handle that is not hers; the person types the DID.
        claimsSomeoneElsesHandle fake
        let! second = signIn fake app prepared ana.Did

        Assert.Equal(strOf first.IdToken "sub", strOf second.IdToken "sub")
        Assert.False(fst (second.IdToken.TryGetProperty "preferred_username"))
        let! stored = Flows.scalar app "SELECT ifnull(preferred_username, 'NULL') FROM users WHERE display_name = 'Ana'" []
        Assert.Equal("NULL", stored)
        let! after = userCount app
        Assert.Equal(users, after)
    }

[<Fact>]
let ``a handle still verified stays on the user when the person types the DID`` () =
    task {
        let! fake, app, prepared = setup ()
        let! _ = signIn fake app prepared "ana.bsky.social"
        let! again = signIn fake app prepared ana.Did
        Assert.Equal("ana.bsky.social", strOf again.IdToken "preferred_username")
    }

// ---------------------------------------------------------------------------
// did:web
// ---------------------------------------------------------------------------

let private wendy =
    { Did = "did:web:wendy.example.org"
      Handle = "wendy.example.org"
      DisplayName = Some "Wendy"
      AvatarCid = None
      Pds = "https://pds.wendy.test"
      Issuer = "https://as.wendy.test" }

[<Fact>]
let ``a did:web account signs in by its handle and by its DID`` () =
    task {
        let! fake, app, prepared = setup ()
        fake.AddAccount(wendy, true)

        let! byHandle = signIn fake app prepared "wendy.example.org"
        Assert.Equal("Wendy", strOf byHandle.IdToken "name")
        Assert.Equal("wendy.example.org", strOf byHandle.IdToken "preferred_username")
        let! subject = Flows.scalar app "SELECT provider_subject FROM linked_accounts WHERE provider_kind = 'bluesky' AND provider_subject LIKE 'did:web:%'" []
        Assert.Equal(wendy.Did, subject)

        // The document came from the DID's own host, not the PLC directory.
        Assert.True(fake.CountOf "GET https://wendy.example.org/.well-known/did.json" >= 2, "the step and the callback each read it")
        Assert.Equal(0, fake.CountOf "plc.test/did:web")

        let! users = userCount app
        let! byDid = signIn fake app prepared wendy.Did
        Assert.Equal(strOf byHandle.IdToken "sub", strOf byDid.IdToken "sub")
        Assert.Equal("wendy.example.org", strOf byDid.IdToken "preferred_username")
        let! after = userCount app
        Assert.Equal(users, after)

        // The server was recorded for the did:web link too.
        let! host = Flows.scalar app $"SELECT provider_server FROM linked_accounts WHERE provider_subject = '{wendy.Did}'" []
        Assert.Equal("as.wendy.test", host)
    }

[<Fact>]
let ``a moved handle signs in as the same user, and the handle shown follows the document`` () =
    task {
        let! fake, app, prepared = setup ()
        let! first = signIn fake app prepared "ana.bsky.social"
        let! users = userCount app

        // The document now names another handle, served over HTTPS.
        fake.AddAccount({ ana with Handle = "ana.example.social" }, false)

        // The old name no longer works: its document does not name it.
        let! _, refused = attempt app prepared "ana.bsky.social"
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode)

        let! second = signIn fake app prepared "ana.example.social"
        Assert.Equal(strOf first.IdToken "sub", strOf second.IdToken "sub")
        Assert.Equal("ana.example.social", strOf second.IdToken "preferred_username")
        let! after = userCount app
        Assert.Equal(users, after)
    }

[<Fact>]
let ``a profile that cannot be read still signs in, named by the handle and with no picture`` () =
    task {
        let! fake, app, prepared = setup ()
        fake.ProfileFails <- true
        let! done' = signIn fake app prepared "ana.bsky.social"
        Assert.Equal("ana.bsky.social", strOf done'.IdToken "name")
        Assert.False(fst (done'.IdToken.TryGetProperty "picture"))
        let! avatar = Flows.scalar app "SELECT avatar_url FROM users WHERE display_name = 'ana.bsky.social'" []
        Assert.Equal("", avatar)
    }

[<Fact>]
let ``a display name with a control character and 300 characters comes back cleaned and at most 200`` () =
    task {
        let! fake, app, prepared = setup ()
        fake.AddAccount({ ana with DisplayName = Some("An\u0007a" + String('b', 299)) }, true)
        let! done' = signIn fake app prepared "ana.bsky.social"
        let name = strOf done'.IdToken "name"
        Assert.True(name.Length <= 200)
        Assert.StartsWith("Anab", name)
        Assert.DoesNotContain("\u0007", name)
    }

[<Fact>]
let ``the pushed request carries the normalised handle, the atproto scope and S256, and the redirect only the request`` () =
    task {
        let! fake, app, prepared = setup ()
        let! _, posted = start app prepared "@Ana.Bsky.Social"
        let par = (fake.Server ana.Issuer).Pars.Values |> Seq.exactlyOne
        Assert.Equal("ana.bsky.social", par.LoginHint)
        Assert.Equal("atproto", par.Scope)
        Assert.Equal("S256", par.ChallengeMethod)
        Assert.Equal("https://umbral.test/connect/bluesky/client-metadata.json", par.ClientId)

        let query = Web.HttpUtility.ParseQueryString(Uri(posted.Headers.Location |> string).Query)
        Assert.Equal<string list>([ "client_id"; "request_uri" ], query.AllKeys |> Seq.choose Option.ofObj |> Seq.sort |> List.ofSeq)
    }

[<Fact>]
let ``a nonce asked for at the pushed request and at the token request is retried, with no query in any proof's htu`` () =
    task {
        let! fake, app, prepared = setup ()
        let server = fake.Server ana.Issuer
        let! browser, posted = start app prepared "ana.bsky.social"

        // The server moves on to another nonce before the code is exchanged.
        server.Nonce <- "n2"

        let callback = Uri(fake.Approve(posted.Headers.Location |> string))
        let! back = browser.Get callback.PathAndQuery
        Assert.Contains("/connect/authorize", back.Headers.Location |> string)
        let! failed = failures app
        Assert.Empty failed

        let nonces endpoint =
            fake.Proofs |> Array.filter (fun p -> p.Endpoint = endpoint) |> Array.map _.Nonce |> List.ofArray

        Assert.Equal<string option list>([ None; Some "n1" ], nonces "par")
        Assert.Equal<string option list>([ Some "n1"; Some "n2" ], nonces "token")
        Assert.All(fake.Proofs, fun p -> Assert.DoesNotContain("?", p.Url))
        Assert.Equal(4, fake.Proofs.Length)
    }

[<Fact>]
let ``a server that refuses every nonce shows the unsupported message`` () =
    task {
        let! fake, app, prepared = setup ()
        (fake.Server ana.Issuer).RefuseNonces <- true
        let! _, posted = attempt app prepared "ana.bsky.social"
        Assert.Equal(HttpStatusCode.UnprocessableEntity, posted.StatusCode)
        let! html = posted.Content.ReadAsStringAsync()
        Assert.Contains("does not offer the sign-in Umbral uses", html)
        let! details = failures app
        Assert.Equal("bluesky:server_unsupported;server=bsky.test", Assert.Single details)
    }
