/// T096 — SC-005: a restart of the instance leaves every signed-in person signed
/// in and every previously issued token still valid.
///
/// The failure this guards against is the one Keys.fs opens with: a container
/// replaced during an update (`docker compose pull && up -d`) that comes back
/// with new key material, logging everybody out of every app and failing every
/// refresh with `invalid_grant`. It is invisible in any single-process test,
/// because the keys are fine for as long as the process that made them lives.
///
/// So this runs TWO app instances, one after the other, on the SAME data
/// directory — the second created only after the first is disposed — and
/// carries credentials across: cookies the first instance set, tokens it
/// issued, a secret it saved. Nothing is re-established by the second.
module Umbral.Tests.RestartTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Xunit
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Flows

[<Literal>]
let private ConnectorSecret = "fake-connector-secret-value"

/// A browser holding copies of another's cookies: the same person, the same
/// device, a different server process.
let private continuedFrom (source: Browser) (client: HttpClient) =
    let browser = Browser(client)

    for cookie in source.Cookies do
        browser.Cookies[cookie.Key] <- cookie.Value

    browser

/// The published signing keys, by `kid`.
let private signingKeys (client: HttpClient) =
    task {
        let! response = client.GetAsync "/.well-known/jwks"
        let! document = json response

        return
            [ for key in document.GetProperty("keys").EnumerateArray() ->
                  let rsa = RSA.Create()

                  rsa.ImportParameters(
                      RSAParameters(
                          Modulus = base64UrlDecode (strOf key "n"),
                          Exponent = base64UrlDecode (strOf key "e")
                      )
                  )

                  strOf key "kid", rsa ]
            |> Map.ofList
    }

/// Verify an RS256 JWT against a key set. This is what an integrating app does
/// with the id_token it is handed, so it is the honest check that a token issued
/// before the restart is still trusted after it.
let private verifies (keys: Map<string, RSA>) (jwt: string) =
    let kid = strOf (segmentOf 0 jwt) "kid"
    let parts = jwt.Split '.'

    match keys.TryFind kid with
    | None -> false
    | Some rsa ->
        rsa.VerifyData(
            Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]),
            base64UrlDecode parts[2],
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        )

/// What the first instance leaves behind for the second to be tested against.
type private BeforeRestart =
    { Prepared: Prepared
      Operator: Browser
      Person: Browser
      Subject: string
      AccessToken: string
      RefreshToken: string
      IdToken: string
      KeyIds: string list }

[<Fact>]
let ``a restart leaves people signed in and issued tokens valid`` () =
    task {
        let directory = Fixtures.freshDataDirectory ()

        try
            // -----------------------------------------------------------------
            // Before: sign in, get tokens, save a secret.
            // -----------------------------------------------------------------
            let! first = Fixtures.createAppIn directory fakeSettings ignore

            let! outcome =
                task {
                    try
                        let operator = Browser(first.Client)
                        let! prepared = prepare first operator

                        let person = Browser(first.Client)

                        let! signIn =
                            signInThroughFake person prepared.ClientId "openid email profile offline_access" "fake-user-1" "sam@example.com"

                        let! exchanged = exchangeCode first.Client prepared.ClientId prepared.ClientSecret signIn.Code
                        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode)
                        let! tokens = json exchanged
                        let idToken = strOf tokens "id_token"

                        let! keys = signingKeys first.Client

                        return
                            Choice1Of2
                                { Prepared = prepared
                                  Operator = operator
                                  Person = person
                                  Subject = strOf (segmentOf 1 idToken) "sub"
                                  AccessToken = strOf tokens "access_token"
                                  RefreshToken = strOf tokens "refresh_token"
                                  IdToken = idToken
                                  KeyIds = keys |> Map.keys |> List.ofSeq }
                    with e ->
                        return Choice2Of2 e
                }

            // The whole point: the first instance is GONE before the second
            // exists. Disposing the app releases the SQLite file and the key
            // files, as a stopped process would.
            do! (first :> IAsyncDisposable).DisposeAsync()
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let before =
                match outcome with
                | Choice1Of2 state -> state
                | Choice2Of2 e -> raise e

            // -----------------------------------------------------------------
            // After: a new process on the same directory.
            // -----------------------------------------------------------------
            use! second = Fixtures.createAppIn directory fakeSettings ignore
            let operator = continuedFrom before.Operator second.Client
            let person = continuedFrom before.Person second.Client

            // SC-005, the person: their session cookie still resumes an
            // authorization request. A redirect straight back to the app with a
            // code — not to the picker — means they were NOT asked to sign in.
            let! resumed = person.Get(authorizeUrl before.Prepared.ClientId "openid email profile")
            let landed = resumed.Headers.Location |> string

            Assert.True(
                landed.StartsWith RedirectUri && landed.Contains "code=",
                $"the end-user session did not survive the restart; the authorize request went to {landed}"
            )

            // ...and the code that session just earned is a working one.
            let code =
                Text.RegularExpressions.Regex.Match(landed, "code=([^&]+)").Groups[1].Value
                |> Uri.UnescapeDataString

            let! resumedExchange = exchangeCode second.Client before.Prepared.ClientId before.Prepared.ClientSecret code
            Assert.Equal(HttpStatusCode.OK, resumedExchange.StatusCode)

            // SC-005, the operator: the admin cookie is still good, and so is a
            // form token minted by this process but bound to the old key ring.
            let! admin = operator.Get "/admin/connectors"
            Assert.Equal(HttpStatusCode.OK, admin.StatusCode)

            let! edited =
                operator.PostForm(
                    "/admin/connectors",
                    "/admin/connectors/discord",
                    [ "clientId", "123456789012345678"; "clientSecret", "discord-secret-after-restart-1" ]
                )

            Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode)
            Assert.Contains("connector-saved", edited.Headers.Location |> string)

            // SC-005, the tokens: an access token issued before still works.
            let! info = userInfo second.Client before.AccessToken
            Assert.Equal(HttpStatusCode.OK, info.StatusCode)
            let! claims = json info
            Assert.Equal(before.Subject, strOf claims "sub")

            // ...an id_token issued before verifies against the JWKS the new
            // process publishes, because the keys are the same keys.
            let! keys = signingKeys second.Client
            Assert.Equal<string list>(before.KeyIds, keys |> Map.keys |> List.ofSeq)
            Assert.True(verifies keys before.IdToken, "an id_token issued before the restart no longer verifies")

            // ...and a refresh token issued before still refreshes.
            let! refreshed = refresh second.Client before.Prepared.ClientId before.Prepared.ClientSecret before.RefreshToken
            Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode)
            let! rotated = json refreshed
            Assert.True(verifies keys (strOf rotated "id_token"), "the id_token from the post-restart refresh does not verify")

            // A saved connector secret still decrypts: the Data Protection key
            // ring that sealed it is the one that opens it.
            let! connector = ConnectorStore.findBySlug second.DbConfig "fake"
            let reference = Secrets.SecretRef.Decode connector.Value.ClientSecretRef

            match reference with
            | Error e -> failwith e
            | Ok reference ->
                let store = second.Services.GetRequiredService<Secrets.ISecretStore>()
                let! plaintext = store.Get reference
                Assert.Equal(Some ConnectorSecret, plaintext)

            // The same fact, through the path that matters: a brand-new person
            // signs in through the Fake connector, whose callback has to
            // decrypt that secret to complete — and is the SAME user as before,
            // so the subject is stable across the restart (FR-004).
            let stranger = Browser(second.Client)

            let! again =
                signInThroughFake stranger before.Prepared.ClientId "openid" "fake-user-1" "sam@example.com"

            let! exchangedAgain = exchangeCode second.Client before.Prepared.ClientId before.Prepared.ClientSecret again.Code
            Assert.Equal(HttpStatusCode.OK, exchangedAgain.StatusCode)
            let! againTokens = json exchangedAgain
            Assert.Equal(before.Subject, strOf (segmentOf 1 (strOf againTokens "id_token")) "sub")
        finally
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            try
                if Directory.Exists directory then
                    Directory.Delete(directory, true)
            with _ ->
                ()
    }
