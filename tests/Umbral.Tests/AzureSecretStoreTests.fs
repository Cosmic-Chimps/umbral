/// The Azure Key Vault store, against canned Key Vault responses (FR-025): no
/// account, no network. The SDK's pipeline sends through a fake
/// `HttpMessageHandler` and signs with a static `TokenCredential`.
module Umbral.Tests.AzureSecretStoreTests

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
open Azure.Core
open Azure.Identity
open Xunit
open Umbral.Infrastructure.Secrets
open Umbral.Infrastructure.SecretStores
open Umbral.Infrastructure.SecretStores.AzureStore

let private none = CancellationToken.None

let private vault = Uri "https://v.vault.azure.net/"

let private name =
    SecretName.derive "umbral" "github" (DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero))

/// A credential with a fixed token, so nothing reaches Entra.
type private StaticCredential() =
    inherit TokenCredential()

    override _.GetToken(_: TokenRequestContext, _: CancellationToken) =
        AccessToken("test-token", DateTimeOffset.UtcNow.AddHours 1.)

    override _.GetTokenAsync(_: TokenRequestContext, _: CancellationToken) =
        ValueTask<AccessToken>(AccessToken("test-token", DateTimeOffset.UtcNow.AddHours 1.))

/// A credential that cannot produce a token, the way a chain with nothing
/// configured or a revoked service principal cannot.
type private FailingCredential(failure: unit -> exn) =
    inherit TokenCredential()
    override _.GetToken(_: TokenRequestContext, _: CancellationToken) : AccessToken = raise (failure ())
    override _.GetTokenAsync(_: TokenRequestContext, _: CancellationToken) : ValueTask<AccessToken> = raise (failure ())

/// One request the vault's endpoint received after authentication.
type private Request = { Method: string; Path: string; Body: string }

/// Plays Key Vault. Key Vault's client authenticates by challenge: its first
/// request carries no token and expects a 401 whose `WWW-Authenticate` names
/// the tenant and resource, and only then does it ask the credential for a
/// token and repeat the request. The fake does the same, so the SDK's real
/// authentication policy runs; the resource has to be a parent domain of the
/// vault's host or the client refuses to send the token anywhere.
/// `received` holds only the requests that carried the token.
type private Vault(respond: Request -> HttpResponseMessage) =
    inherit HttpMessageHandler()

    let received = List<Request>()

    member _.Received = lock received (fun () -> List.ofSeq received)

    override _.SendAsync(message, cancellationToken) =
        task {
            if isNull message.Headers.Authorization then
                let challenge = new HttpResponseMessage(HttpStatusCode.Unauthorized)

                challenge.Headers.TryAddWithoutValidation(
                    "WWW-Authenticate",
                    "Bearer authorization=\"https://login.microsoftonline.com/tenant\", resource=\"https://vault.azure.net\""
                )
                |> ignore

                return challenge
            else
                let! body =
                    match message.Content with
                    | null -> Task.FromResult ""
                    | content -> content.ReadAsStringAsync cancellationToken

                let request =
                    { Method = message.Method.Method
                      Path = (nonNull message.RequestUri).AbsolutePath
                      Body = body }

                lock received (fun () -> received.Add request)
                return respond request
        }

let private json (status: HttpStatusCode) (body: string) =
    let response = new HttpResponseMessage(status)
    response.Content <- new StringContent(body, Encoding.UTF8, "application/json")
    response

/// The body Key Vault answers a successful set or get with. Plain text with
/// markers, because JSON braces and F# interpolation do not mix.
let private secretBody (host: string) (secretName: string) (value: string) =
    """{"value":"@VALUE","id":"https://@HOST/secrets/@NAME/abc123","attributes":{"enabled":true,"created":1760261400,"updated":1760261400,"recoveryLevel":"Recoverable+Purgeable"}}"""
        .Replace("@VALUE", value)
        .Replace("@HOST", host)
        .Replace("@NAME", secretName)

let private errorBody (code: string) =
    """{"error":{"code":"@CODE","message":"vendor text that must never be repeated: s3cr3t-in-message"}}"""
        .Replace("@CODE", code)

let private storeOver (handler: Vault) (credential: TokenCredential) =
    create
        { VaultUri = vault
          Prefix = "umbral"
          Credential = Some credential
          Transport = Some(handler :> HttpMessageHandler) }

let private storeWith (respond: Request -> HttpResponseMessage) =
    let handler = new Vault(respond)
    storeOver handler (StaticCredential()), handler

let private ok (result: Result<'a, StoreFailure>) =
    match result with
    | Ok value -> value
    | Error failure -> failwith $"expected Ok, got {failure.Cause}"

let private reference locator = { Store = "azure"; Locator = locator }

let private locatorOfName = $"https://v.vault.azure.net/secrets/{name.Cloud}"

// -- put ---------------------------------------------------------------------

[<Fact>]
let ``put sets the secret under the rendered name and returns the versionless id`` () =
    task {
        let store, handler = storeWith (fun r -> json HttpStatusCode.OK (secretBody "v.vault.azure.net" name.Cloud "s3cr3t-value"))
        let! put = store.Put(name, "s3cr3t-value", none)

        let reference = ok put
        Assert.Equal("azure", reference.Store)
        // The version the vault minted ("abc123") is not part of the locator:
        // it has to keep reading the newest version after a replace.
        Assert.Equal(locatorOfName, reference.Locator)

        let sent = handler.Received |> List.exactlyOne
        Assert.Equal("PUT", sent.Method)
        Assert.Equal($"/secrets/{name.Cloud}", sent.Path)
        Assert.Contains("s3cr3t-value", sent.Body)
    }

[<Fact>]
let ``putting the same name again adds a version and returns the same reference`` () =
    task {
        let mutable version = 0

        let store, _ =
            storeWith (fun r ->
                version <- version + 1
                json HttpStatusCode.OK ((secretBody "v.vault.azure.net" name.Cloud "v").Replace("abc123", $"version{version}")))

        let! first = store.Put(name, "one", none)
        let! second = store.Put(name, "two", none)
        Assert.Equal(ok first, ok second)
    }

[<Fact>]
let ``put in a name that is soft-deleted is rejected with a fixed reason`` () =
    task {
        let store, _ = storeWith (fun _ -> json HttpStatusCode.Conflict (errorBody "ObjectIsDeletedButRecoverable"))
        let! put = store.Put(name, "s3cr3t-value", none)
        Assert.Equal(Error(Rejected "soft_deleted"), put)
    }

// -- get ---------------------------------------------------------------------

[<Fact>]
let ``get returns the latest value of the secret the locator names`` () =
    task {
        let store, handler = storeWith (fun _ -> json HttpStatusCode.OK (secretBody "v.vault.azure.net" name.Cloud "s3cr3t-value"))
        let! got = store.Get(reference locatorOfName, none)
        Assert.Equal(Ok "s3cr3t-value", got)

        let sent = handler.Received |> List.exactlyOne
        Assert.Equal("GET", sent.Method)
        // No version segment: the latest is asked for.
        Assert.Equal($"/secrets/{name.Cloud}/", sent.Path)
    }

[<Fact>]
let ``get of a reference to another vault reads that vault`` () =
    task {
        let hosts = List<string>()

        let handler =
            { new HttpMessageHandler() with
                member _.SendAsync(message, _) =
                    lock hosts (fun () -> hosts.Add (nonNull message.RequestUri).Host)

                    if isNull message.Headers.Authorization then
                        let challenge = new HttpResponseMessage(HttpStatusCode.Unauthorized)

                        challenge.Headers.TryAddWithoutValidation(
                            "WWW-Authenticate",
                            "Bearer authorization=\"https://login.microsoftonline.com/tenant\", resource=\"https://vault.azure.net\""
                        )
                        |> ignore

                        Task.FromResult challenge
                    else
                        Task.FromResult(json HttpStatusCode.OK (secretBody "other.vault.azure.net" "n" "from-other")) }

        let store =
            create
                { VaultUri = vault
                  Prefix = "umbral"
                  Credential = Some(StaticCredential() :> TokenCredential)
                  Transport = Some handler }

        let! got = store.Get(reference "https://other.vault.azure.net/secrets/n", none)
        Assert.Equal(Ok "from-other", got)
        Assert.All(hosts, fun host -> Assert.Equal("other.vault.azure.net", host))
    }

[<Fact>]
let ``get of a secret that is not there is not found`` () =
    task {
        let store, _ = storeWith (fun _ -> json HttpStatusCode.NotFound (errorBody "SecretNotFound"))
        let! got = store.Get(reference locatorOfName, none)
        Assert.Equal(Error NotFound, got)
    }

[<Fact>]
let ``a locator that is not a vault secret id is rejected without a request`` () =
    task {
        let store, handler = storeWith (fun _ -> failwith "no request expected")
        let! got = store.Get(reference "not a uri at all", none)
        Assert.Equal(Error(Rejected "malformed_locator"), got)
        Assert.Empty handler.Received
    }

[<Fact>]
let ``a reference of another store is not this store's to read`` () =
    task {
        let store, _ = storeWith (fun _ -> failwith "no request expected")
        let! got = store.Get({ Store = "aws"; Locator = locatorOfName }, none)
        Assert.Equal(Error(Rejected "malformed_locator"), got)
    }

// -- delete ------------------------------------------------------------------

[<Fact>]
let ``delete starts the delete and never purges`` () =
    task {
        let store, handler =
            storeWith (fun r ->
                json
                    HttpStatusCode.OK
                    ("""{"recoveryId":"https://v.vault.azure.net/deletedsecrets/@NAME","deletedDate":1760261400,"scheduledPurgeDate":1768037400,"id":"https://v.vault.azure.net/secrets/@NAME/abc123"}"""
                        .Replace("@NAME", name.Cloud)))

        let! deleted = store.Delete(reference locatorOfName, none)
        Assert.Equal(Ok(), deleted)

        let sent = handler.Received |> List.exactlyOne
        Assert.Equal("DELETE", sent.Method)
        Assert.Equal($"/secrets/{name.Cloud}", sent.Path)
        // Purging is permanent and is the operator's decision, never Umbral's.
        Assert.DoesNotContain(handler.Received, fun r -> r.Path.Contains "deletedsecrets")
    }

[<Fact>]
let ``delete of a secret that is already gone is ok`` () =
    task {
        let store, _ = storeWith (fun _ -> json HttpStatusCode.NotFound (errorBody "SecretNotFound"))
        let! deleted = store.Delete(reference locatorOfName, none)
        Assert.Equal(Ok(), deleted)
    }

// -- probe -------------------------------------------------------------------

[<Fact>]
let ``probe reads the probe name and a missing probe secret is healthy`` () =
    task {
        let store, handler = storeWith (fun _ -> json HttpStatusCode.NotFound (errorBody "SecretNotFound"))
        let! probed = store.Probe none
        Assert.Equal(Ok(), probed)
        Assert.Equal("/secrets/umbral-probe/", (handler.Received |> List.exactlyOne).Path)
    }

[<Fact>]
let ``probe with a present probe secret is healthy`` () =
    task {
        let store, _ = storeWith (fun _ -> json HttpStatusCode.OK (secretBody "v.vault.azure.net" "umbral-probe" "x"))
        let! probed = store.Probe none
        Assert.Equal(Ok(), probed)
    }

[<Fact>]
let ``probe refused by the vault is access denied`` () =
    task {
        let store, _ = storeWith (fun _ -> json HttpStatusCode.Forbidden (errorBody "Forbidden"))
        let! probed = store.Probe none
        Assert.Equal(Error AccessDenied, probed)
    }

// -- failure mapping ---------------------------------------------------------

let private failureOf (status: HttpStatusCode) (code: string) =
    task {
        let store, _ = storeWith (fun _ -> json status (errorBody code))
        return! store.Get(reference locatorOfName, none)
    }

[<Fact>]
let ``401 and 403 from the vault are access denied`` () =
    task {
        // After the challenge the SDK signed the request, so this 401 is the
        // vault refusing the token, not the first round of the handshake.
        let! unauthorized = failureOf HttpStatusCode.Unauthorized "Unauthorized"
        let! forbidden = failureOf HttpStatusCode.Forbidden "Forbidden"
        Assert.Equal(Error AccessDenied, unauthorized)
        Assert.Equal(Error AccessDenied, forbidden)
    }

[<Fact>]
let ``a failed credential is access denied`` () =
    task {
        let! authFailed =
            let handler = new Vault(fun _ -> failwith "no request expected")
            let store = storeOver handler (FailingCredential(fun () -> AuthenticationFailedException "bad secret s3cr3t"))
            store.Get(reference locatorOfName, none)

        let! unavailable =
            let handler = new Vault(fun _ -> failwith "no request expected")
            let store = storeOver handler (FailingCredential(fun () -> CredentialUnavailableException "nothing configured"))
            store.Get(reference locatorOfName, none)

        Assert.Equal(Error AccessDenied, authFailed)
        Assert.Equal(Error AccessDenied, unavailable)
    }

[<Fact>]
let ``429 and 5xx are unreachable once the retries are spent`` () =
    task {
        let attempts = ref 0

        let store, _ =
            storeWith (fun _ ->
                Interlocked.Increment attempts |> ignore
                let response = json HttpStatusCode.TooManyRequests (errorBody "Throttled")
                // Tells the SDK's retry policy not to wait: the test is about
                // the count and the mapping, not about the back-off.
                response.Headers.TryAddWithoutValidation("retry-after-ms", "0") |> ignore
                response)

        let! throttled = store.Get(reference locatorOfName, none)
        Assert.Equal(Error Unreachable, throttled)
        // One try and two retries: Retry.MaxRetries = 2 (R3).
        Assert.Equal(3, attempts.Value)

        let! unavailable = failureOf HttpStatusCode.ServiceUnavailable "ServiceUnavailable"
        Assert.Equal(Error Unreachable, unavailable)
    }

[<Fact>]
let ``no connection at all is unreachable`` () =
    task {
        let handler =
            { new HttpMessageHandler() with
                member _.SendAsync(_, _) = raise (HttpRequestException "no route to host") }

        let store =
            create
                { VaultUri = vault
                  Prefix = "umbral"
                  Credential = Some(StaticCredential() :> TokenCredential)
                  Transport = Some handler }

        let! got = store.Get(reference locatorOfName, none)
        Assert.Equal(Error Unreachable, got)
    }

[<Fact>]
let ``another refusal is rejected with a fixed code and none of the vendor text`` () =
    task {
        let! got = failureOf HttpStatusCode.BadRequest "BadParameter"
        Assert.Equal(Error(Rejected "http_400"), got)
    }

[<Fact>]
let ``no failure carries the vendor message or the plaintext`` () =
    task {
        // Every path above that returns a failure was fed `s3cr3t-in-message`
        // in the vendor's error text; none of its reasons may contain it.
        for status, code in
            [ HttpStatusCode.NotFound, "SecretNotFound"
              HttpStatusCode.Conflict, "ObjectIsDeletedButRecoverable"
              HttpStatusCode.Conflict, "Other"
              HttpStatusCode.BadRequest, "BadParameter" ] do
            let! got = failureOf status code

            match got with
            | Error(Rejected reason) -> Assert.DoesNotContain("s3cr3t", reason)
            | _ -> ()
    }

// -- hang --------------------------------------------------------------------

[<Fact>]
let ``a vault that never answers is unreachable through the registry's bound`` () =
    task {
        let handler =
            { new HttpMessageHandler() with
                member _.SendAsync(_, cancellationToken) =
                    task {
                        do! Task.Delay(Timeout.Infinite, cancellationToken)
                        return new HttpResponseMessage()
                    } }

        let store =
            create
                { VaultUri = vault
                  Prefix = "umbral"
                  Credential = Some(StaticCredential() :> TokenCredential)
                  Transport = Some handler }

        let registry =
            Registry.SecretStores(
                LocalSecretStore(Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create "Umbral.Tests"),
                "azure",
                Map [ "azure", lazy (Ok store) ],
                TimeSpan.FromMilliseconds 200.
            )

        let bounded = registry.Active
        // Azure's own network timeout may also fire first; either way the
        // answer to the caller is the same.
        let! got = bounded.Get(reference locatorOfName, none)
        Assert.Equal(Error Unreachable, got)
        let! put = bounded.Put(name, "s3cr3t-value", none)
        Assert.Equal(Error Unreachable, put)
    }

// -- credential --------------------------------------------------------------

[<Fact>]
let ``the production credential is the narrow chain`` () =
    // Built without touching the network; asking it for a token would.
    Assert.IsType<ChainedTokenCredential>(defaultCredential ()) |> ignore
