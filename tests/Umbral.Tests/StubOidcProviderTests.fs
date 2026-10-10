/// The stub identity provider is test machinery that later suites trust to be
/// a correct provider, so it is held to that here: a token it mints validates
/// with the library against the keys it publishes, and the ways it can be made
/// to lie do make it lie. A stub that is wrong the same way as the adapter
/// would pass every test and prove nothing.
module Umbral.Tests.StubOidcProviderTests

open System
open System.Net.Http
open System.Security.Cryptography
open System.Text
open Microsoft.IdentityModel.JsonWebTokens
open Microsoft.IdentityModel.Tokens
open Xunit
open Umbral.Infrastructure.OidcDiscovery
open Umbral.Tests.Infrastructure.StubOidcProvider

let private issuer = "https://auth.example.test/application/o/umbral"

let private keysOf (stub: StubOidcProvider) =
    task {
        let cache = OidcDiscovery(new HttpClient(stub, false), false)

        match! cache.Check issuer with
        | Discovered document -> return document
        | other -> return failwith $"expected Discovered, got {other}"
    }

let private validate (document: Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration) (token: string) =
    task {
        let parameters =
            TokenValidationParameters(
                ValidIssuer = issuer,
                ValidAudience = "client-1",
                IssuerSigningKeys = document.SigningKeys,
                ValidAlgorithms = asymmetricAlgorithms
            )

        return! JsonWebTokenHandler().ValidateTokenAsync(token, parameters)
    }

[<Fact>]
let ``a token the stub mints validates against the keys it publishes`` () =
    task {
        let stub = create issuer
        let! document = keysOf stub
        let token = stub.MintIdToken(defaultIdToken, "client-1", "nonce-1")

        let! result = validate document token
        Assert.True(result.IsValid, string result.Exception)
        Assert.Equal("user-1", string result.Claims["sub"])
        Assert.Equal("nonce-1", string result.Claims["nonce"])
        Assert.IsType<bool>(result.Claims["email_verified"]) |> ignore
    }

[<Fact>]
let ``a token signed by the attacker key does not validate`` () =
    task {
        let stub = create issuer
        let! document = keysOf stub
        let token = stub.MintIdToken({ defaultIdToken with Key = Some(stub.Attacker :> SecurityKey) }, "client-1", "n")

        let! result = validate document token
        Assert.False result.IsValid
    }

[<Fact>]
let ``an unsigned token and a shared-secret token do not validate`` () =
    task {
        let stub = create issuer
        let! document = keysOf stub

        let unsigned = stub.MintIdToken({ defaultIdToken with Unsigned = true }, "client-1", "n")
        let! none = validate document unsigned
        Assert.False none.IsValid

        let secret = SymmetricSecurityKey(Encoding.UTF8.GetBytes(String('k', 48)))

        let hs256 =
            stub.MintIdToken(
                { defaultIdToken with
                    Key = Some(secret :> SecurityKey)
                    Algorithm = SecurityAlgorithms.HmacSha256 },
                "client-1",
                "n"
            )

        let! shared = validate document hs256
        Assert.False shared.IsValid
    }

[<Fact>]
let ``an expired token minted by the stub is expired`` () =
    task {
        let stub = create issuer
        let! document = keysOf stub

        let token =
            stub.MintIdToken({ defaultIdToken with ExpiresAt = Some(DateTimeOffset.UtcNow.AddHours -1.0) }, "client-1", "n")

        let! result = validate document token
        Assert.False result.IsValid
    }

[<Fact>]
let ``the token endpoint honours the pkce challenge and issues a code once`` () =
    task {
        let stub = create issuer
        let verifier = "the-verifier-of-at-least-forty-three-characters-long"
        let challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes verifier))

        let authorizeUri =
            Uri $"{issuer}/authorize?client_id=client-1&state=s1&nonce=n1&code_challenge={challenge}&redirect_uri=https%%3A%%2F%%2Fumbral.test%%2Fcb"

        let authorization = stub.Authorize authorizeUri
        Assert.Equal("s1", authorization.State)

        use client = new HttpClient(stub, false)

        let redeem (code: string) (codeVerifier: string) =
            task {
                let form =
                    new FormUrlEncodedContent(
                        [ "grant_type", "authorization_code"; "code", code; "code_verifier", codeVerifier; "client_id", "client-1" ]
                        |> List.map (fun (k, v) -> Collections.Generic.KeyValuePair(k, v))
                    )

                return! client.PostAsync(issuer + "/token", form)
            }

        // The wrong verifier is refused, and burns the code.
        let! wrong = redeem authorization.Code "some-other-verifier"
        Assert.False wrong.IsSuccessStatusCode

        let second = stub.Authorize authorizeUri
        let! right = redeem second.Code verifier
        Assert.True right.IsSuccessStatusCode

        let! body = right.Content.ReadAsStringAsync()
        Assert.Contains("id_token", body)

        // Spent.
        let! again = redeem second.Code verifier
        Assert.False again.IsSuccessStatusCode
    }

[<Fact>]
let ``rotating changes the published key and the kid`` () =
    task {
        let stub = create issuer
        let before = stub.SigningKey.KeyId
        let after = stub.Rotate()

        Assert.NotEqual<string>(before, after.KeyId)
        Assert.Equal<string list>([ after.KeyId ], stub.PublishedKeys |> Seq.map _.KeyId |> List.ofSeq)
    }
