/// The `gcp` store, over Google Secret Manager's REST API (specs/005 research R4).
///
/// Four calls, written by hand over `HttpClient` and `System.Text.Json`, rather
/// than the generated gRPC client: that package is twenty assemblies and can
/// only be faked with an in-process gRPC server, and it would load on every
/// instance that merely reads a GCP reference. What must not be ours is the
/// token, and `Google.Apis.Auth` keeps that: key file, workload identity
/// federation or the metadata server, all through
/// `GoogleCredential.GetApplicationDefaultAsync`.
///
/// A secret is one Secret Manager *secret* with one version per `Put`, so
/// replacing a value adds a version and the locator never changes. The locator
/// is the versionless resource name, `projects/{p}/secrets/{name}`; it carries
/// the project, so a reference written under another `GOOGLE_CLOUD_PROJECT`
/// still reads and deletes from the project that holds it.
module Umbral.Infrastructure.SecretStores.GcpStore

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open Google.Apis.Auth.OAuth2
open Google.Apis.Auth.OAuth2.Responses
open Umbral.Infrastructure.Secrets

[<Literal>]
let StoreId = "gcp"

/// What a store needs to be built. `Credential`, `HttpMessageHandler` and
/// `BaseUri` exist so a test needs neither an account nor the network
/// (FR-025); a running instance leaves all three `None`.
type GcpStoreOptions =
    {
        /// `GOOGLE_CLOUD_PROJECT`: where new secrets are created.
        ProjectId: string
        /// The secret prefix; the probe reads `{prefix}-probe`.
        Prefix: string
        /// `None` resolves Application Default Credentials on first use.
        Credential: GoogleCredential option
        HttpMessageHandler: HttpMessageHandler option
        /// Default `https://secretmanager.googleapis.com/v1/`.
        BaseUri: Uri option
    }

let private defaultBaseUri = Uri "https://secretmanager.googleapis.com/v1/"

let private scopes = [| "https://www.googleapis.com/auth/cloud-platform" |]

// Both values end up inside a URL path, so they are checked rather than
// escaped: an escaped `/` would still be a different secret than the one meant.
let private projectPattern = Regex(@"^[A-Za-z0-9._:-]+$", RegexOptions.Compiled)
let private prefixPattern = Regex(@"^[A-Za-z0-9_-]+$", RegexOptions.Compiled)

let private locatorPattern =
    Regex(@"^projects/[A-Za-z0-9._:-]+/secrets/[A-Za-z0-9_-]+$", RegexOptions.Compiled)

/// CRC32C (Castagnoli, reflected polynomial 0x82F63B78), which Secret Manager
/// uses to check a payload end to end: it rejects an `addVersion` whose
/// `dataCrc32c` does not match, and sends the checksum back on `access`.
/// Public so a test can pin it to the standard check value.
let crc32c (bytes: byte[]) : uint32 =
    let table =
        Array.init 256 (fun n ->
            let mutable c = uint32 n

            for _ in 0..7 do
                c <- if c &&& 1u <> 0u then (c >>> 1) ^^^ 0x82F63B78u else c >>> 1

            c)

    let mutable crc = 0xFFFFFFFFu

    for b in bytes do
        crc <- table[int ((crc ^^^ uint32 b) &&& 0xFFu)] ^^^ (crc >>> 8)

    ~~~crc

let private failureOfStatus (status: int) =
    match status with
    | 404 -> NotFound
    | 401
    | 403 -> AccessDenied
    // 408 and 429 are the manager saying "not now"; like a 5xx, waiting helps.
    | 408
    | 429 -> Unreachable
    | s when s >= 500 -> Unreachable
    // The reason is the status code and nothing else: Google's error body can
    // echo the secret name.
    | s -> Rejected(string s)

/// A response reduced to what the callers use. The body is read only when the
/// status was a success, and the three callers that need it parse it at once.
type private Reply =
    | Success of status: int * body: string
    | Refused of status: int

type GcpSecretStore(options: GcpStoreOptions) =

    let baseUri =
        let uri = defaultArg options.BaseUri defaultBaseUri
        // A relative path resolves against the last segment unless the base
        // ends in `/`, which would silently drop `v1`.
        if uri.AbsoluteUri.EndsWith '/' then uri else Uri(uri.AbsoluteUri + "/")

    let http =
        match options.HttpMessageHandler with
        | Some handler -> new HttpClient(handler, false)
        | None -> new HttpClient()

    // Resolved once, but only a success is kept: a metadata server that was not
    // up yet at the first call must not turn into a store that stays denied.
    let credentialLock = new SemaphoreSlim(1, 1)
    let mutable credential: ITokenAccess option = None

    let resolveCredential (ct: CancellationToken) : Task<ITokenAccess> =
        task {
            match credential with
            | Some c -> return c
            | None ->
                do! credentialLock.WaitAsync ct

                try
                    match credential with
                    | Some c -> return c
                    | None ->
                        let! google =
                            match options.Credential with
                            | Some c -> Task.FromResult c
                            | None -> GoogleCredential.GetApplicationDefaultAsync ct

                        let scoped = google.CreateScoped scopes :> ITokenAccess
                        credential <- Some scoped
                        return scoped
                finally
                    credentialLock.Release() |> ignore
        }

    /// One authenticated call. Every vendor, network and credential failure is
    /// an `Error` here, so the four operations below only deal with statuses.
    let send (verb: HttpMethod) (path: string) (body: string option) (ct: CancellationToken) : Task<Result<Reply, StoreFailure>> =
        task {
            try
                let! access = resolveCredential ct
                let! token = access.GetAccessTokenForRequestAsync(null, ct)

                use request = new HttpRequestMessage(verb, Uri(baseUri, path))
                request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token)

                body
                |> Option.iter (fun json -> request.Content <- new StringContent(json, Encoding.UTF8, "application/json"))

                use! response = http.SendAsync(request, ct)
                let status = int response.StatusCode

                if response.IsSuccessStatusCode then
                    let! text = response.Content.ReadAsStringAsync ct
                    return Ok(Success(status, text))
                else
                    return Ok(Refused status)
            with
            // The caller's own cancellation (or the registry's bound, which is
            // linked to it) is not ours to turn into a failure: the registry
            // decides which of the two it was.
            | :? OperationCanceledException when ct.IsCancellationRequested -> return raise (OperationCanceledException ct)
            // A timeout of the HttpClient itself, not asked for by the caller.
            | :? OperationCanceledException -> return Error Unreachable
            | :? HttpRequestException -> return Error Unreachable
            // No Application Default Credentials on this machine: the grant is
            // missing, which waiting will not fix, so it reads as denied.
            | :? InvalidOperationException -> return Error AccessDenied
            // Google refused to issue a token (revoked key, bad assertion).
            | :? TokenResponseException -> return Error AccessDenied
            | ex -> return Error(Rejected(ex.GetType().Name))
        }

    /// `Ok body` for any success, the mapped failure otherwise.
    let call verb path body ct =
        task {
            match! send verb path body ct with
            | Ok(Success(_, text)) -> return Ok text
            | Ok(Refused status) -> return Error(failureOfStatus status)
            | Error failure -> return Error failure
        }

    let locatorOf (reference: SecretRef) =
        if reference.Store <> StoreId then
            // The registry routes a reference to the store that wrote it, so
            // reaching here is a programming error (the contract allows a throw).
            invalidArg (nameof reference) "reference belongs to another store"

        if locatorPattern.IsMatch reference.Locator then Ok reference.Locator else Error(Rejected "locator")

    let projectOf () =
        if projectPattern.IsMatch options.ProjectId then
            Ok options.ProjectId
        else
            Error NotConfigured

    /// The JSON the manager sent back for `:access`, as plaintext. The checksum
    /// is checked when Google sent one: a value corrupted in transit must not
    /// become a client secret that fails at the provider with no explanation.
    let plaintextOf (json: string) =
        try
            use doc = JsonDocument.Parse json
            let payload = doc.RootElement.GetProperty "payload"
            let data = payload.GetProperty("data").GetString() |> Option.ofObj |> Option.defaultValue ""
            let bytes = Convert.FromBase64String data

            let checksumMatches =
                match payload.TryGetProperty "dataCrc32c" with
                | true, crc ->
                    let sent =
                        match crc.ValueKind with
                        | JsonValueKind.String -> UInt32.TryParse(crc.GetString() |> Option.ofObj |> Option.defaultValue "")
                        | _ -> crc.TryGetUInt32()

                    match sent with
                    | true, value -> value = crc32c bytes
                    | _ -> false
                | _ -> true

            if checksumMatches then
                Ok((UTF8Encoding(false, true)).GetString bytes)
            else
                Error(Rejected "checksum")
        with
        | :? JsonException
        | :? InvalidOperationException
        | :? System.Collections.Generic.KeyNotFoundException
        | :? FormatException
        | :? ArgumentException -> Error(Rejected "malformed_response")

    interface ISecretStore with
        member _.Id = StoreId

        member _.Put(name, plaintext, ct) =
            task {
                match projectOf () with
                | Error failure -> return Error failure
                | Ok project ->
                    let secrets = $"projects/{project}/secrets"
                    let id = name.Cloud

                    // 409 is "already exists": the retried or moved-twice case,
                    // which is exactly when a version should be added to the
                    // same secret.
                    let! created =
                        send
                            HttpMethod.Post
                            $"{secrets}?secretId={Uri.EscapeDataString id}"
                            (Some """{"replication":{"automatic":{}}}""")
                            ct

                    let created =
                        match created with
                        | Ok(Success _) -> Ok()
                        | Ok(Refused 409) -> Ok()
                        | Ok(Refused status) -> Error(failureOfStatus status)
                        | Error failure -> Error failure

                    match created with
                    | Error failure -> return Error failure
                    | Ok() ->
                        let bytes = Encoding.UTF8.GetBytes plaintext

                        let body =
                            JsonSerializer.Serialize(
                                {| payload =
                                    {| data = Convert.ToBase64String bytes
                                       dataCrc32c = string (crc32c bytes) |} |}
                            )

                        match! call HttpMethod.Post $"{secrets}/{id}:addVersion" (Some body) ct with
                        | Ok _ ->
                            return
                                Ok
                                    { Store = StoreId
                                      Locator = $"{secrets}/{id}" }
                        | Error failure -> return Error failure
            }

        member _.Get(reference, ct) =
            task {
                match locatorOf reference with
                | Error failure -> return Error failure
                | Ok locator ->
                    match! call HttpMethod.Get $"{locator}/versions/latest:access" None ct with
                    | Ok json -> return plaintextOf json
                    | Error failure -> return Error failure
            }

        member _.Delete(reference, ct) =
            task {
                match locatorOf reference with
                | Error failure -> return Error failure
                | Ok locator ->
                    match! send HttpMethod.Delete locator None ct with
                    | Ok(Success _) -> return Ok()
                    // Already gone is the outcome a delete wants.
                    | Ok(Refused 404) -> return Ok()
                    | Ok(Refused status) -> return Error(failureOfStatus status)
                    | Error failure -> return Error failure
            }

        member _.Probe ct =
            task {
                match projectOf (), prefixPattern.IsMatch options.Prefix with
                | Error failure, _ -> return Error failure
                | _, false -> return Error NotConfigured
                | Ok project, true ->
                    // A read of a name that is not expected to exist: reaching
                    // the manager with a token it accepts is the whole test, and
                    // a 404 for the probe secret proves both. Anything that
                    // does answer is not read.
                    match! send HttpMethod.Get $"projects/{project}/secrets/{options.Prefix}-probe/versions/latest:access" None ct with
                    | Ok(Success _) -> return Ok()
                    | Ok(Refused 404) -> return Ok()
                    | Ok(Refused status) -> return Error(failureOfStatus status)
                    | Error failure -> return Error failure
            }

let create (options: GcpStoreOptions) : ISecretStore = GcpSecretStore options :> ISecretStore
