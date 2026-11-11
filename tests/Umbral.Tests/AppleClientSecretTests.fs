/// The parts of the Apple adapter that need no network (specs/013): the key the
/// operator pastes, the client secret Umbral signs from it, the two claims whose
/// values Apple documents as "a string or Boolean", the name in the form post, and
/// the admin check that must not call Apple when the key is no good.
module Umbral.Tests.AppleClientSecretTests

open System
open System.Net.Http
open System.Security.Cryptography
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Time.Testing
open Microsoft.IdentityModel.JsonWebTokens
open Microsoft.IdentityModel.Tokens
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure.OidcDiscovery
open Umbral.Infrastructure.Providers.Apple

let private keyIds: AppleKeyIds = { TeamId = "TEAM123456"; KeyId = "KEY1234567" }
let private servicesId = "com.example.umbral.web"

let private startOfTest = DateTimeOffset(2026, 11, 11, 10, 0, 0, TimeSpan.Zero)

let private p256 () = ECDsa.Create(ECCurve.NamedCurves.nistP256)

let private pemOf (key: ECDsa) = key.ExportPkcs8PrivateKeyPem()

let private signedWith (key: ECDsa) (clock: TimeProvider) =
    match clientSecret clock keyIds servicesId (pemOf key) with
    | Some token -> token
    | None -> failwith "expected a client secret"

let private payloadOf (token: string) =
    let segment = token.Split('.')[1]
    JsonDocument.Parse(Base64UrlEncoder.Decode segment).RootElement

let private headerOf (token: string) =
    let segment = token.Split('.')[0]
    JsonDocument.Parse(Base64UrlEncoder.Decode segment).RootElement

let private publicHalf (key: ECDsa) : SecurityKey =
    ECDsaSecurityKey(ECDsa.Create(key.ExportParameters false))

let private verify (token: string) (key: SecurityKey) =
    let parameters = TokenValidationParameters()
    parameters.ValidIssuer <- keyIds.TeamId
    parameters.ValidAudience <- "https://appleid.apple.com"
    parameters.IssuerSigningKey <- key
    parameters.ValidAlgorithms <- [ "ES256" ]
    // The fake clock is not the library's, so the lifetime is asserted by value
    // below instead.
    parameters.ValidateLifetime <- false
    JsonWebTokenHandler().ValidateTokenAsync(token, parameters)

// ---------------------------------------------------------------------------
// The client secret
// ---------------------------------------------------------------------------

[<Fact>]
let ``the client secret verifies with the public half of the key`` () =
    task {
        use key = p256 ()
        let token = signedWith key (FakeTimeProvider startOfTest)

        let! result = verify token (publicHalf key)
        Assert.True(result.IsValid, string result.Exception)
    }

[<Fact>]
let ``the client secret does not verify with another key`` () =
    task {
        use key = p256 ()
        use other = p256 ()
        let token = signedWith key (FakeTimeProvider startOfTest)

        let! result = verify token (publicHalf other)
        Assert.False result.IsValid
    }

[<Fact>]
let ``the header says ES256 and carries the key id`` () =
    use key = p256 ()
    let header = headerOf (signedWith key (FakeTimeProvider startOfTest))

    Assert.Equal("ES256", header.GetProperty("alg").GetString())
    Assert.Equal(keyIds.KeyId, header.GetProperty("kid").GetString())

[<Fact>]
let ``the payload has exactly iss, sub, aud, iat and exp, and no nbf`` () =
    use key = p256 ()
    let clock = FakeTimeProvider startOfTest
    let payload = payloadOf (signedWith key clock)

    let names = payload.EnumerateObject() |> Seq.map (fun p -> p.Name) |> Set.ofSeq
    Assert.Equal<Set<string>>(Set.ofList [ "iss"; "sub"; "aud"; "iat"; "exp" ], names)

    Assert.Equal(keyIds.TeamId, payload.GetProperty("iss").GetString())
    Assert.Equal(servicesId, payload.GetProperty("sub").GetString())
    Assert.Equal("https://appleid.apple.com", payload.GetProperty("aud").GetString())
    Assert.Equal(startOfTest.ToUnixTimeSeconds(), payload.GetProperty("iat").GetInt64())
    Assert.Equal(startOfTest.ToUnixTimeSeconds() + 300L, payload.GetProperty("exp").GetInt64())

[<Fact>]
let ``the client secret lives five minutes`` () =
    use key = p256 ()
    let payload = payloadOf (signedWith key (FakeTimeProvider startOfTest))

    Assert.Equal(300L, payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64())
    Assert.Equal(TimeSpan.FromMinutes 5.0, clientSecretLifetime)

[<Fact>]
let ``a later call gives a different token, issued at the later time`` () =
    use key = p256 ()
    let clock = FakeTimeProvider startOfTest
    let first = signedWith key clock
    clock.Advance(TimeSpan.FromSeconds 30.0)
    let second = signedWith key clock

    Assert.NotEqual<string>(first, second)
    Assert.Equal(startOfTest.ToUnixTimeSeconds() + 30L, (payloadOf second).GetProperty("iat").GetInt64())

[<Fact>]
let ``the same key and key id sign twice, the second call not finding a disposed key in a cache`` () =
    use key = p256 ()
    let clock = FakeTimeProvider startOfTest
    let pem = pemOf key

    for _ in 1..3 do
        Assert.True((clientSecret clock keyIds servicesId pem).IsSome)

[<Fact>]
let ``a key pasted with Windows line endings signs once it is normalised`` () =
    use key = p256 ()
    let windows = (pemOf key).Replace("\n", "\r\n") + "\r\n\r\n"

    match normaliseKey windows with
    | None -> failwith "expected the key to be accepted"
    | Some pem -> Assert.True((clientSecret (FakeTimeProvider startOfTest) keyIds servicesId pem).IsSome)

[<Fact>]
let ``garbage, an RSA key and a P-384 key do not sign`` () =
    let clock = FakeTimeProvider startOfTest
    use rsa = RSA.Create 2048
    use p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384)

    Assert.True((clientSecret clock keyIds servicesId "not a key").IsNone)
    Assert.True((clientSecret clock keyIds servicesId "").IsNone)
    Assert.True((clientSecret clock keyIds servicesId (rsa.ExportPkcs8PrivateKeyPem())).IsNone)
    Assert.True((clientSecret clock keyIds servicesId (p384.ExportPkcs8PrivateKeyPem())).IsNone)

// ---------------------------------------------------------------------------
// The key as pasted
// ---------------------------------------------------------------------------

[<Fact>]
let ``a P-256 key is accepted, trimmed, with line feeds only`` () =
    use key = p256 ()
    let pem = (pemOf key).TrimEnd()

    let messy = "\r\n\r\n  " + pem.Replace("\n", "\r\n") + "\r\n\r\n"
    Assert.Equal(Some pem, normaliseKey messy)
    Assert.Equal(Some pem, normaliseKey ("\n\n" + pem + "\n\n"))
    Assert.Equal(Some pem, normaliseKey (pem.Replace("\n", "\r")))

[<Fact>]
let ``what is not a P-256 private key in PEM is refused`` () =
    use rsa = RSA.Create 2048
    use p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384)
    use key = p256 ()

    let body =
        (pemOf key).Split('\n')
        |> Array.filter (fun line -> not (line.StartsWith "-----") && line <> "")
        |> String.concat "\n"

    Assert.Equal(None, normaliseKey null)
    Assert.Equal(None, normaliseKey "")
    Assert.Equal(None, normaliseKey "   \r\n  ")
    Assert.Equal(None, normaliseKey "not a key")
    Assert.Equal(None, normaliseKey (rsa.ExportPkcs8PrivateKeyPem()))
    Assert.Equal(None, normaliseKey (rsa.ExportRSAPrivateKeyPem()))
    Assert.Equal(None, normaliseKey (p384.ExportPkcs8PrivateKeyPem()))
    Assert.Equal(None, normaliseKey (key.ExportSubjectPublicKeyInfoPem()))
    Assert.Equal(None, normaliseKey body)

// ---------------------------------------------------------------------------
// The flags Apple sends as a string or a Boolean
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("true", true)>]
[<InlineData("\"true\"", true)>]
[<InlineData("false", false)>]
[<InlineData("\"false\"", false)>]
[<InlineData("\"True\"", false)>]
[<InlineData("\"TRUE\"", false)>]
[<InlineData("\" true\"", false)>]
[<InlineData("\"true \"", false)>]
[<InlineData("1", false)>]
[<InlineData("\"1\"", false)>]
[<InlineData("null", false)>]
[<InlineData("{}", false)>]
[<InlineData("{\"a\":true}", false)>]
[<InlineData("[]", false)>]
[<InlineData("[true]", false)>]
let ``a claim is true only as the JSON true or the string true`` (json: string, expected: bool) =
    use doc = JsonDocument.Parse($"{{\"email_verified\":{json}}}")
    Assert.Equal(expected, claimIsTrue doc.RootElement "email_verified")

[<Fact>]
let ``an absent claim is false, and so is a payload that is not an object`` () =
    use doc = JsonDocument.Parse "{\"other\":true}"
    Assert.False(claimIsTrue doc.RootElement "email_verified")

    use array = JsonDocument.Parse "[true]"
    Assert.False(claimIsTrue array.RootElement "email_verified")

// ---------------------------------------------------------------------------
// The name in the form post
// ---------------------------------------------------------------------------

let private user (json: string) = nameOf (Some json)

[<Fact>]
let ``both names are joined with a space`` () =
    Assert.Equal(
        Some "Maria Ruiz",
        user """{"name":{"firstName":"Maria","lastName":"Ruiz"},"email":"m@example.com"}"""
    )

[<Fact>]
let ``either name alone is the name`` () =
    Assert.Equal(Some "Maria", user """{"name":{"firstName":"Maria"}}""")
    Assert.Equal(Some "Ruiz", user """{"name":{"lastName":"Ruiz"}}""")

[<Fact>]
let ``control characters are dropped and spaces trimmed`` () =
    Assert.Equal(Some "Maria Ruiz", user "{\"name\":{\"firstName\":\"Ma\\tri\\na\",\"lastName\":\"  Ruiz  \"}}")
    Assert.Equal(Some "Maria", user "{\"name\":{\"firstName\":\"\\n Maria \\t\",\"lastName\":\"\\t\\n\"}}")

[<Fact>]
let ``a name of 300 characters is cut to 200`` () =
    let long = String('a', 300)

    match user $"{{\"name\":{{\"firstName\":\"{long}\"}}}}" with
    | Some name -> Assert.Equal(String('a', 200), name)
    | None -> failwith "expected a name"

[<Fact>]
let ``no usable name is None`` () =
    Assert.Equal(None, user "{}")
    Assert.Equal(None, user """{"name":{}}""")
    Assert.Equal(None, user """{"name":"Maria"}""")
    Assert.Equal(None, user """{"name":{"firstName":1,"lastName":null}}""")
    Assert.Equal(None, user "[1,2]")
    Assert.Equal(None, user "\"Maria\"")

[<Fact>]
let ``the email in the form post is never a name`` () =
    Assert.Equal(None, user """{"email":"x@example.com"}""")

[<Fact>]
let ``input that is not JSON, absent or too long is None`` () =
    Assert.Equal(None, user "not json")
    Assert.Equal(None, user "")
    Assert.Equal(None, nameOf None)

    let padding = String(' ', 9000)
    Assert.Equal(None, user ("""{"name":{"firstName":"Maria"}}""" + padding))

// ---------------------------------------------------------------------------
// The admin check
// ---------------------------------------------------------------------------

/// A handler that fails the test when anything asks it for an answer.
type private ForbiddenHandler() =
    inherit HttpMessageHandler()

    member val Calls = 0 with get, set

    override this.SendAsync(_: HttpRequestMessage, _: CancellationToken) : Task<HttpResponseMessage> =
        this.Calls <- this.Calls + 1
        failwith "the check called the network"

[<Fact>]
let ``a key that cannot sign is unusable and Apple is not asked`` () =
    task {
        let handler = new ForbiddenHandler()
        let discovery = OidcDiscovery(new HttpClient(handler), false)
        let clock = FakeTimeProvider startOfTest

        let! result = check discovery clock keyIds servicesId "not a key"

        Assert.Equal(KeyUnusable, result)
        Assert.Equal(0, handler.Calls)
    }
