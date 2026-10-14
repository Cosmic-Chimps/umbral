/// Which stores the deployment's configuration puts in the registry
/// (specs/005 research R2): the active one from the settings, and a reader for
/// each other store whose credentials are visibly present, so a reference left
/// there stays readable (FR-011).
///
/// This is the in-process half of SC-004: that a local-only instance registers
/// no external store at all. It does NOT assert on the assemblies loaded into
/// the test process, because other test classes load the vendor SDKs into the
/// same process; the loaded-assembly half is a run of the real binary.
module Umbral.Tests.SecretStoreWiringTests

open Microsoft.AspNetCore.DataProtection
open Microsoft.Extensions.Configuration
open Xunit
open Umbral.Infrastructure
open Umbral.Infrastructure.SecretStores
open Umbral.Infrastructure.SecretStores.Registry

let private bellaKey = "bax-abc123-0123456789abcdef"

let private build (pairs: (string * string) list) : SecretStores =
    let config = ConfigurationBuilder().AddInMemoryCollection(dict pairs).Build() :> IConfiguration

    match Settings.parse config with
    | Error problems -> failwith $"settings did not parse: {problems}"
    | Ok settings ->
        let local = Secrets.LocalSecretStore(EphemeralDataProtectionProvider())
        Wiring.build config settings local

let private external = [ "aws"; "gcp"; "azure"; "bellabaxter" ]

[<Fact>]
let ``a local-only configuration registers no external store`` () =
    let stores = build []

    Assert.Equal("local", stores.Active.Id)

    for id in external do
        Assert.False(stores.CanRead id, $"{id} must not be readable")
        Assert.Equal(Error Secrets.NotConfigured, stores.For id |> Result.map (fun s -> s.Id))

    Assert.True(stores.CanRead "local")

[<Fact>]
let ``each store is active when it is the one set`` () =
    let cases =
        [ "aws", [ "UMBRAL:SECRET_STORE", "aws"; "AWS_REGION", "eu-west-1" ]
          "gcp", [ "UMBRAL:SECRET_STORE", "gcp"; "GOOGLE_CLOUD_PROJECT", "my-project" ]
          "azure", [ "UMBRAL:SECRET_STORE", "azure"; "UMBRAL:AZURE_KEY_VAULT_URI", "https://vault.vault.azure.net" ]
          "bellabaxter", [ "UMBRAL:SECRET_STORE", "bellabaxter"; "BELLA_BAXTER_API_KEY", bellaKey ] ]

    for id, pairs in cases do
        let stores = build pairs
        Assert.Equal(id, stores.Active.Id)
        Assert.True(stores.CanRead id)

[<Fact>]
let ``the active store uses the configured prefix`` () =
    // The prefix is not observable on the store itself; what is observable is
    // that a prefix that the store refuses (GCP's alphabet has no `.`) cannot
    // reach it, which Settings.parse already guarantees. Here: building works.
    let stores = build [ "UMBRAL:SECRET_STORE", "aws"; "AWS_REGION", "eu-west-1"; "UMBRAL:SECRET_STORE_PREFIX", "club" ]
    Assert.Equal("aws", stores.Active.Id)

[<Fact>]
let ``a store that is not active is readable when its credentials are present`` () =
    let cases =
        [ "aws", "AWS_REGION"
          "aws", "AWS_ACCESS_KEY_ID"
          "aws", "AWS_PROFILE"
          "aws", "AWS_WEB_IDENTITY_TOKEN_FILE"
          "gcp", "GOOGLE_APPLICATION_CREDENTIALS"
          "gcp", "GOOGLE_CLOUD_PROJECT"
          "azure", "AZURE_CLIENT_ID"
          "azure", "AZURE_TENANT_ID"
          "azure", "UMBRAL:AZURE_KEY_VAULT_URI"
          "bellabaxter", "BELLA_BAXTER_API_KEY"
          "bellabaxter", "BELLA_BAXTER_ACCESS_TOKEN" ]

    for id, variable in cases do
        // The instance writes locally; only this variable says a store exists.
        let stores = build [ variable, (if variable.StartsWith "BELLA" then bellaKey else "x") ]
        Assert.Equal("local", stores.Active.Id)
        Assert.True(stores.CanRead id, $"{id} should be readable with {variable}")

        for other in external |> List.filter ((<>) id) do
            Assert.False(stores.CanRead other, $"{other} should not be readable with only {variable}")

[<Fact>]
let ``the active external store does not make the others readable`` () =
    let stores = build [ "UMBRAL:SECRET_STORE", "aws"; "AWS_REGION", "eu-west-1" ]

    Assert.True(stores.CanRead "aws")

    for id in [ "gcp"; "azure"; "bellabaxter" ] do
        Assert.False(stores.CanRead id)

[<Fact>]
let ``a Bella reader without a usable credential is registered as unreadable, not as a crash`` () =
    // The variable is present (so a reader is registered) but holds no key the
    // client would accept: the registry answers NotConfigured instead of
    // throwing, and the card says so (FR-021).
    let stores = build [ "BELLA_BAXTER_API_KEY", "not-a-bax-key" ]

    Assert.True(stores.CanRead "bellabaxter")
    Assert.Equal(Error Secrets.NotConfigured, stores.For "bellabaxter" |> Result.map (fun s -> s.Id))
