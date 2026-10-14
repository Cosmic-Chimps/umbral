/// Every row of specs/005-external-secret-stores/contracts/settings.md: what
/// each variable defaults to, what refuses the start, and that the refusal names
/// the variable and says what to set (Principle VI).
///
/// `parse` reads `IConfiguration`, so these hand it an in-memory one and never
/// touch the process environment. Vendor variables go in under their plain
/// names, exactly as the environment provider presents them.
module Umbral.Tests.SecretStoreSettingsTests

open System
open Microsoft.Extensions.Configuration
open Xunit
open Umbral.Infrastructure
open Umbral.Infrastructure.SecretStores.Settings
open Umbral.Tests.Infrastructure

let private config (pairs: (string * string) list) : IConfiguration =
    ConfigurationBuilder().AddInMemoryCollection(dict pairs).Build() :> IConfiguration

let private ok pairs =
    match parse (config pairs) with
    | Ok settings -> settings
    | Error problems -> failwith $"expected success, got: {problems}"

let private problems pairs =
    match parse (config pairs) with
    | Error found -> found
    | Ok settings -> failwith $"expected failure, got: {settings.StoreId}"

let private describe (problem: SettingProblem) =
    (Umbral.Views.Texts.current ()).SecretStoreProblem problem

/// The only problem there is, described: the tests below mostly assert that a
/// single mistake produces a single message naming the right variable.
let private onlyMessage pairs =
    match problems pairs with
    | [ one ] -> describe one
    | many -> failwith $"expected exactly one problem, got {many.Length}: {many}"

let private awsOk = [ "UMBRAL:SECRET_STORE", "aws"; "AWS_REGION", "eu-west-1" ]

let private bellaKey = "bax-abc123-0123456789abcdef"
let private bellaWithKey = [ "UMBRAL:SECRET_STORE", "bellabaxter"; "BELLA_BAXTER_API_KEY", bellaKey ]

let private bellaWithToken =
    [ "UMBRAL:SECRET_STORE", "bellabaxter"
      "BELLA_BAXTER_ACCESS_TOKEN", "a-jwt"
      "BELLA_BAXTER_PROJECT", "umbral"
      "BELLA_BAXTER_ENV", "prod" ]

// ---------------------------------------------------------------------------
// Defaults and the store setting
// ---------------------------------------------------------------------------

[<Fact>]
let ``no variable at all is the local store with the documented defaults`` () =
    // FR-002: a deployment that sets none of them behaves as before.
    let settings = ok []
    Assert.Equal(Local, settings.Store)
    Assert.Equal("local", settings.StoreId)
    Assert.Equal("umbral", settings.Prefix)
    Assert.Equal(TimeSpan.FromSeconds 300., settings.CacheLifetime)
    Assert.Equal(TimeSpan.FromSeconds 3600., settings.GracePeriod)

[<Fact>]
let ``blank values count as not set`` () =
    let settings = ok [ "UMBRAL:SECRET_STORE", "  "; "UMBRAL:SECRET_STORE_PREFIX", ""; "AWS_REGION", " " ]
    Assert.Equal("local", settings.StoreId)
    Assert.Equal("umbral", settings.Prefix)

[<Theory>]
[<InlineData("local", "local")>]
[<InlineData("LOCAL", "local")>]
[<InlineData("Aws", "aws")>]
[<InlineData(" gcp ", "gcp")>]
let ``the store name is case-insensitive`` (value: string, id: string) =
    let settings =
        ok
            [ "UMBRAL:SECRET_STORE", value
              "AWS_REGION", "eu-west-1"
              "GOOGLE_CLOUD_PROJECT", "my-project" ]

    Assert.Equal(id, settings.StoreId)

[<Fact>]
let ``an unknown store refuses the start naming the variable and the choices`` () =
    match problems [ "UMBRAL:SECRET_STORE", "vault" ] with
    | [ UnknownStore "vault" as problem ] ->
        let message = describe problem
        Assert.Contains("UMBRAL__SECRET_STORE", message)
        Assert.Contains("bellabaxter", message)
    | other -> failwith $"unexpected: {other}"

// ---------------------------------------------------------------------------
// Prefix, cache and grace: read whatever the store
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("a")>]
[<InlineData("umbral")>]
[<InlineData("my-umbral-2")>]
[<InlineData("0123456789012345678901234567890a")>] // 32
let ``a well-formed prefix is accepted`` (prefix: string) =
    Assert.Equal(prefix, (ok [ "UMBRAL:SECRET_STORE_PREFIX", prefix ]).Prefix)

[<Theory>]
[<InlineData("-umbral")>]
[<InlineData("umbral-")>]
[<InlineData("Umbral")>]
[<InlineData("um_bral")>]
[<InlineData("um bral")>]
[<InlineData("012345678901234567890123456789012")>] // 33
let ``a malformed prefix refuses the start even with the local store`` (prefix: string) =
    let message = onlyMessage [ "UMBRAL:SECRET_STORE_PREFIX", prefix ]
    Assert.Contains("UMBRAL__SECRET_STORE_PREFIX", message)

[<Fact>]
let ``cache and grace seconds are read as whole seconds`` () =
    let settings = ok [ "UMBRAL:SECRET_CACHE_SECONDS", "0"; "UMBRAL:SECRET_GRACE_SECONDS", "90" ]
    Assert.Equal(TimeSpan.Zero, settings.CacheLifetime)
    Assert.Equal(TimeSpan.FromSeconds 90., settings.GracePeriod)

[<Theory>]
[<InlineData("-1")>]
[<InlineData("abc")>]
[<InlineData("1.5")>]
[<InlineData("99999999999")>]
let ``a bad cache value refuses the start naming the variable`` (value: string) =
    Assert.Contains("UMBRAL__SECRET_CACHE_SECONDS", onlyMessage [ "UMBRAL:SECRET_CACHE_SECONDS", value ])

[<Theory>]
[<InlineData("-1")>]
[<InlineData("abc")>]
[<InlineData("1.5")>]
[<InlineData("99999999999")>]
let ``a bad grace value refuses the start naming the variable`` (value: string) =
    Assert.Contains("UMBRAL__SECRET_GRACE_SECONDS", onlyMessage [ "UMBRAL:SECRET_GRACE_SECONDS", value ])

[<Fact>]
let ``every problem is reported at once`` () =
    let found =
        problems
            [ "UMBRAL:SECRET_STORE", "azure"
              "UMBRAL:SECRET_STORE_PREFIX", "Bad Prefix"
              "UMBRAL:SECRET_CACHE_SECONDS", "x"
              "UMBRAL:SECRET_GRACE_SECONDS", "-4" ]

    Assert.Equal(4, found.Length)
    let text = found |> List.map describe |> String.concat "\n"

    for variable in
        [ "UMBRAL__AZURE_KEY_VAULT_URI"
          "UMBRAL__SECRET_STORE_PREFIX"
          "UMBRAL__SECRET_CACHE_SECONDS"
          "UMBRAL__SECRET_GRACE_SECONDS" ] do
        Assert.Contains(variable, text)

// ---------------------------------------------------------------------------
// aws and gcp: presence of the location only
// ---------------------------------------------------------------------------

[<Fact>]
let ``aws with a region is accepted and credentials are not read`` () =
    let settings = ok (awsOk @ [ "AWS_ACCESS_KEY_ID", "AKIA..."; "AWS_SECRET_ACCESS_KEY", "s3cr3t" ])
    Assert.Equal(Aws "eu-west-1", settings.Store)
    Assert.Equal("aws", settings.StoreId)

[<Fact>]
let ``aws without a region refuses the start`` () =
    let message = onlyMessage [ "UMBRAL:SECRET_STORE", "aws" ]
    Assert.Contains("AWS_REGION", message)
    Assert.Contains("UMBRAL__SECRET_STORE", message)

[<Fact>]
let ``gcp with a project is accepted`` () =
    let settings = ok [ "UMBRAL:SECRET_STORE", "gcp"; "GOOGLE_CLOUD_PROJECT", "my-project" ]
    Assert.Equal(Gcp "my-project", settings.Store)
    Assert.Equal("gcp", settings.StoreId)

[<Fact>]
let ``gcp without a project refuses the start`` () =
    Assert.Contains("GOOGLE_CLOUD_PROJECT", onlyMessage [ "UMBRAL:SECRET_STORE", "gcp" ])

[<Fact>]
let ``vendor variables of another store are not required`` () =
    // Local needs nothing, and aws does not need the Google project.
    Assert.Equal("aws", (ok awsOk).StoreId)

// ---------------------------------------------------------------------------
// azure
// ---------------------------------------------------------------------------

[<Fact>]
let ``azure with an https vault address is accepted`` () =
    let settings =
        ok [ "UMBRAL:SECRET_STORE", "azure"; "UMBRAL:AZURE_KEY_VAULT_URI", "https://umbral-kv.vault.azure.net/" ]

    Assert.Equal("azure", settings.StoreId)

    match settings.Store with
    | Azure uri -> Assert.Equal("https://umbral-kv.vault.azure.net/", uri.AbsoluteUri)
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``azure accepts the vault address without a trailing slash`` () =
    let settings =
        ok [ "UMBRAL:SECRET_STORE", "azure"; "UMBRAL:AZURE_KEY_VAULT_URI", "https://umbral-kv.vault.azure.net" ]

    Assert.Equal("azure", settings.StoreId)

[<Fact>]
let ``azure without the vault address refuses the start`` () =
    let message = onlyMessage [ "UMBRAL:SECRET_STORE", "azure" ]
    Assert.Contains("UMBRAL__AZURE_KEY_VAULT_URI", message)
    Assert.Contains("https://", message)

[<Theory>]
[<InlineData("http://umbral-kv.vault.azure.net/")>]
[<InlineData("umbral-kv.vault.azure.net")>]
[<InlineData("https://umbral-kv.vault.azure.net/secrets")>]
[<InlineData("https://umbral-kv.vault.azure.net/?x=1")>]
let ``azure with a vault address that is not an https address alone refuses the start`` (value: string) =
    let message = onlyMessage [ "UMBRAL:SECRET_STORE", "azure"; "UMBRAL:AZURE_KEY_VAULT_URI", value ]
    Assert.Contains("UMBRAL__AZURE_KEY_VAULT_URI", message)

[<Fact>]
let ``the vault address is not read for another store`` () =
    Assert.Equal("local", (ok [ "UMBRAL:AZURE_KEY_VAULT_URI", "not a uri" ]).StoreId)

// ---------------------------------------------------------------------------
// bellabaxter
// ---------------------------------------------------------------------------

let private bella (settings: SecretStoreSettings) =
    match settings.Store with
    | BellaBaxter location -> location
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``bellabaxter with an api key is accepted with the default address`` () =
    let settings = ok bellaWithKey
    Assert.Equal("bellabaxter", settings.StoreId)
    let location = bella settings
    Assert.Equal("https://api.bella-baxter.io", location.BaseUrl)
    Assert.Equal(None, location.Project)
    Assert.Equal(None, location.Environment)
    Assert.Equal(None, location.Provider)
    Assert.True(location.PrivateKeyPem.IsNone)

    match location.Auth with
    | ApiKey key -> Assert.Equal(bellaKey, key.Reveal)
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``bellabaxter with a token, a project and an environment is accepted`` () =
    let location = bella (ok bellaWithToken)
    Assert.Equal(Some "umbral", location.Project)
    Assert.Equal(Some "prod", location.Environment)

    match location.Auth with
    | AccessToken token -> Assert.Equal("a-jwt", token.Reveal)
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``bellabaxter with neither a key nor a token refuses the start`` () =
    let message = onlyMessage [ "UMBRAL:SECRET_STORE", "bellabaxter" ]
    Assert.Contains("BELLA_BAXTER_API_KEY", message)
    Assert.Contains("BELLA_BAXTER_ACCESS_TOKEN", message)

[<Fact>]
let ``a token without a project refuses the start`` () =
    let pairs = bellaWithToken |> List.filter (fun (k, _) -> k <> "BELLA_BAXTER_PROJECT")
    Assert.Contains("BELLA_BAXTER_PROJECT", onlyMessage pairs)

[<Fact>]
let ``a token without an environment refuses the start`` () =
    let pairs = bellaWithToken |> List.filter (fun (k, _) -> k <> "BELLA_BAXTER_ENV")
    Assert.Contains("BELLA_BAXTER_ENV", onlyMessage pairs)

[<Fact>]
let ``a token with neither reports both`` () =
    let pairs =
        [ "UMBRAL:SECRET_STORE", "bellabaxter"; "BELLA_BAXTER_ACCESS_TOKEN", "a-jwt" ]

    let text = problems pairs |> List.map describe |> String.concat "\n"
    Assert.Contains("BELLA_BAXTER_PROJECT", text)
    Assert.Contains("BELLA_BAXTER_ENV", text)

[<Theory>]
[<InlineData("not-a-key")>]
[<InlineData("bax-onlyonepart")>]
[<InlineData("bax--0123")>]
[<InlineData("bax-abc-")>]
[<InlineData("xab-abc-0123")>]
[<InlineData("bax-abc 123-0123")>]
let ``an api key not shaped bax-id-secret refuses the start without quoting it`` (key: string) =
    let message = onlyMessage [ "UMBRAL:SECRET_STORE", "bellabaxter"; "BELLA_BAXTER_API_KEY", key ]
    Assert.Contains("BELLA_BAXTER_API_KEY", message)
    Assert.DoesNotContain(key, message)

[<Fact>]
let ``the secret part of an api key may hold hyphens`` () =
    // HmacSigningHandler splits on the first two hyphens only.
    Assert.Equal("bellabaxter", (ok [ "UMBRAL:SECRET_STORE", "bellabaxter"; "BELLA_BAXTER_API_KEY", "bax-abc-de-f0" ]).StoreId)

[<Fact>]
let ``a token takes precedence over an api key as in the Bella configuration package`` () =
    let location = bella (ok (bellaWithToken @ [ "BELLA_BAXTER_API_KEY", bellaKey ]))

    match location.Auth with
    | AccessToken _ -> ()
    | other -> failwith $"unexpected: {other}"

[<Fact>]
let ``BELLA_PROJECT and BELLA_ENV are used when the BELLA_BAXTER ones are absent`` () =
    let location =
        bella (
            ok
                [ "UMBRAL:SECRET_STORE", "bellabaxter"
                  "BELLA_BAXTER_ACCESS_TOKEN", "a-jwt"
                  "BELLA_PROJECT", "fallback-project"
                  "BELLA_ENV", "fallback-env" ]
        )

    Assert.Equal(Some "fallback-project", location.Project)
    Assert.Equal(Some "fallback-env", location.Environment)

[<Fact>]
let ``the BELLA_BAXTER variables win over BELLA_PROJECT and BELLA_ENV`` () =
    let location =
        bella (ok (bellaWithToken @ [ "BELLA_PROJECT", "other"; "BELLA_ENV", "other" ]))

    Assert.Equal(Some "umbral", location.Project)
    Assert.Equal(Some "prod", location.Environment)

[<Fact>]
let ``BellaBaxter configuration keys are not read`` () =
    // Environment only, so a stray appsettings section cannot make Umbral
    // write its connector secrets somewhere nobody intended.
    let message =
        onlyMessage
            [ "UMBRAL:SECRET_STORE", "bellabaxter"
              "BellaBaxter:ApiKey", bellaKey
              "BellaBaxter:AccessToken", "a-jwt"
              "BellaBaxter:BaxterUrl", "https://elsewhere.example"
              "BellaBaxter:ProjectSlug", "p"
              "BellaBaxter:EnvironmentSlug", "e" ]

    Assert.Contains("BELLA_BAXTER_API_KEY", message)

[<Fact>]
let ``the url, provider and private key are read when given`` () =
    let location =
        bella (
            ok (
                bellaWithKey
                @ [ "BELLA_BAXTER_URL", "https://bella.example.com"
                    "UMBRAL:BELLABAXTER_PROVIDER", "secrets-1"
                    "BELLA_BAXTER_PRIVATE_KEY", "-----BEGIN PRIVATE KEY-----" ]
            )
        )

    Assert.Equal("https://bella.example.com", location.BaseUrl)
    Assert.Equal(Some "secrets-1", location.Provider)
    Assert.Equal("-----BEGIN PRIVATE KEY-----", location.PrivateKeyPem.Value.Reveal)

[<Theory>]
[<InlineData("http://localhost:5522")>]
[<InlineData("http://127.0.0.1:5522")>]
[<InlineData("https://bella.example.com")>]
let ``https, or http on this machine, is accepted for the bella url`` (url: string) =
    Assert.Equal(url, (bella (ok (bellaWithKey @ [ "BELLA_BAXTER_URL", url ]))).BaseUrl)

[<Theory>]
[<InlineData("http://bella.example.com")>]
[<InlineData("bella.example.com")>]
[<InlineData("ftp://bella.example.com")>]
let ``an http non-loopback or unusable bella url refuses the start`` (url: string) =
    let message = onlyMessage (bellaWithKey @ [ "BELLA_BAXTER_URL", url ])
    Assert.Contains("BELLA_BAXTER_URL", message)
    Assert.Contains("https", message)

[<Fact>]
let ``the bella key, token and private key never appear in a message`` () =
    let all =
        problems
            [ "UMBRAL:SECRET_STORE", "bellabaxter"
              "BELLA_BAXTER_ACCESS_TOKEN", "TOKEN-VALUE-XYZ"
              "BELLA_BAXTER_PRIVATE_KEY", "PEM-VALUE-XYZ"
              "BELLA_BAXTER_URL", "http://bella.example.com" ]
        |> List.map describe
        |> String.concat "\n"

    Assert.DoesNotContain("TOKEN-VALUE-XYZ", all)
    Assert.DoesNotContain("PEM-VALUE-XYZ", all)

[<Fact>]
let ``a protected value does not reveal itself when formatted`` () =
    let location = bella (ok bellaWithKey)
    Assert.DoesNotContain(bellaKey, $"{location.Auth}")

[<Fact>]
let ``every problem message names an environment variable and says what to set`` () =
    let all =
        [ UnknownStore "x"
          MalformedPrefix "X"
          MalformedCacheSeconds "x"
          MalformedGraceSeconds "x"
          AzureVaultUriMissing
          AzureVaultUriInvalid "x"
          AwsRegionMissing
          GcpProjectMissing
          BellaCredentialMissing
          BellaApiKeyMalformed
          BellaTokenWithoutProject
          BellaTokenWithoutEnvironment
          BellaUrlRejected "x" ]

    for problem in all do
        let message = describe problem
        // Upper-case with an underscore: the `__` form or a vendor's own name.
        Assert.Matches("[A-Z]{2,}_[A-Z_]+", message)
        Assert.Matches("(?i)\\b(set|use|give|copy)\\b", message)

// ---------------------------------------------------------------------------
// The start
// ---------------------------------------------------------------------------

[<Fact>]
let ``the app refuses to start with azure and no vault address`` () =
    let build () =
        let task = Fixtures.createAppWith [ "UMBRAL:SECRET_STORE", "azure" ] ignore
        task.GetAwaiter().GetResult() |> ignore

    let ex = Assert.ThrowsAny<Exception> build
    Assert.Contains("UMBRAL__AZURE_KEY_VAULT_URI", ex.Message)
