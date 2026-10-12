# Research: External Secret Stores for Connector Secrets

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-10-12

Three read-only investigations fed this file: a map of the code that touches
connector secrets, the AWS, Google Cloud and Azure .NET clients, and the
BellaBaxter packages (decompiled, since their README no longer matches the
code). Package sizes below were measured by publishing throwaway
`linux-arm64` projects; memory figures are estimates until T-RSS measures them.

## What the code does today, and where it disagrees with the spec

The seam from spec 001 is real but thinner than the spec assumes. Each point
below is a decision in this file.

| # | Today | Spec needs | Decided in |
| --- | --- | --- | --- |
| A | One `ISecretStore` is registered (`Program.fs:111`); `LocalSecretStore.Get` returns `None` for any other store id (`Secrets.fs:87`). | FR-011: read from the store the reference names. | R2 |
| B | `Get` returns `string option`; `Put` and `Delete` throw. | FR-014/015: tell unreachable from not found from denied. | R3 |
| C | No handler catches a failed `Put` (`Connectors.fs:675`); the global handler shows a 500. | Story 1 sc. 5: a message on the screen. | R9 |
| D | Only the OIDC add form re-renders with input; the other paths redirect with `?msg=`. | Story 1 sc. 5 said "keeps what the operator typed". | R9 (spec adjusted) |
| E | Secrets are named `connector/{slug}/client_secret`. `/` is not valid everywhere, and a fixed name collides with a soft-deleted one. | FR-009. | R5 |
| F | `Put` runs before `ConnectorStore.create`; a lost race orphans the secret, and with a fixed name would overwrite the live connector's. | Edge case "concurrent edits". | R5, R9 |
| G | `ConnectorStore` writes have no transaction; the event is a separate write. | FR-017 ordering. | R8 |
| H | `ConnectorDelete` deletes the row, then calls `Delete` with no catch: a refused delete shows a 500 and the event is never written. | FR-010. | R9 |
| I | The store is registered with `AddSingleton`; tests cannot replace it (other seams use `TryAdd`). | FR-025. | R11 |
| J | README and Dockerfile say app secrets are encrypted with the key ring; they are hashed. | FR-024 touches that text. | R12 |
| K | `reset-credential` builds the whole app, so it needs every web setting. | The move command inherits this. | R8 |

## R1 — Store selection and settings

**Decision**: one setting, `UMBRAL__SECRET_STORE` = `local` (default) | `aws` |
`gcp` | `azure` | `bellabaxter`, read as configuration key
`UMBRAL:SECRET_STORE` like every other `UMBRAL__…` setting
(`PublicOrigin.fs:24`). Vendor credentials and locations use the vendor's own
variables (spec FR-004). Umbral's own additions:

| Setting | Default | Purpose |
| --- | --- | --- |
| `UMBRAL__SECRET_STORE` | `local` | Store for new and replaced secrets. |
| `UMBRAL__SECRET_STORE_PREFIX` | `umbral` | First part of every name in a manager (FR-006). `^[a-z0-9]([a-z0-9-]{0,30}[a-z0-9])?$`. |
| `UMBRAL__SECRET_CACHE_SECONDS` | `300` | FR-013 cache lifetime. 0 disables caching. |
| `UMBRAL__SECRET_GRACE_SECONDS` | `3600` | FR-014 grace period. 0 makes it strict. |
| `UMBRAL__AZURE_KEY_VAULT_URI` | — | Required for `azure`. `https://<name>.vault.azure.net/`. |
| `UMBRAL__BELLABAXTER_PROVIDER` | — | Required for `bellabaxter` only when the environment has zero or several secret providers (R7). |

The per-call bound of FR-012 is a constant (10 s), not a setting: nobody has
asked to tune it, and every setting is documentation and a test.

Validation follows the existing pattern: a pure `parse` returning `Result`, a
`describe` that says what to do next (`PublicOrigin.fs:48-73`), and
`requireSettings` failing the start with that text. An unknown store name, a
missing `AWS_REGION` / `GOOGLE_CLOUD_PROJECT` / `UMBRAL__AZURE_KEY_VAULT_URI`,
no BellaBaxter key or token, or a malformed prefix refuses the start (FR-005).

**Rationale**: explicit beats inferred (spec Clarifications). Upper-snake keys
match the existing convention.

**Alternatives**: inferring the store from which credentials are present —
rejected in the spec. A configurable timeout — deferred until asked for.

## R2 — Several stores at once: a registry, not one singleton

**Decision**: replace the single `ISecretStore` registration with a
`SecretStores` registry:

- `Active` — the store new and replaced secrets go to (R1).
- `For(storeId)` — the store that reads, replaces and deletes a given
  reference. `local` is always present. An external store is created lazily the
  first time a reference names it, from the vendor's credentials plus what its
  locator already holds (R4). So a connector whose secret stayed in AWS after
  the instance switched to `local` still signs in, as long as AWS credentials
  are still available (FR-011), and a local-only instance never loads a vendor
  assembly (SC-004, SC-006).
- A reference whose store cannot be built (credentials gone) yields
  `NotConfigured` (R3), which the card shows (FR-021).

**Rationale**: FR-011 and the move command both need a store other than the
active one. Lazy creation keeps the default free.

**Alternatives**: registering all five eagerly — loads MSAL, the AWS SDK and
the BellaBaxter resilience stack on every Pi for nothing.

## R3 — The store interface reports why

**Decision**: the interface becomes cancellable and returns `Result`:

```text
Put    : SecretName * plaintext * CancellationToken -> Task<Result<SecretRef, StoreFailure>>
Get    : SecretRef * CancellationToken            -> Task<Result<string, StoreFailure>>
Delete : SecretRef * CancellationToken            -> Task<Result<unit, StoreFailure>>
Probe  : CancellationToken                        -> Task<Result<unit, StoreFailure>>

StoreFailure = Unreachable | AccessDenied | NotFound | NotConfigured | Rejected of reason
```

`Unreachable` covers network errors, timeouts, throttling after the SDK's own
retries, and 5xx. `Rejected` is a request the manager refused for a reason that
retrying will not fix (a name in soft-deleted state, a malformed value). Each
adapter maps its vendor's errors (table in R4). The local store maps its
decryption failure to `NotFound` with reason "key ring", keeping today's
operator message.

Every call runs under a token linked to `CancelAfter(10 s)` (FR-012). Vendor
SDK retry counts are capped at 2 so the bound is reached by the token, not by a
retry storm.

**Rationale**: FR-014 must not serve a stale value for a secret that was
deleted or whose access was revoked; it can only tell those apart from an
outage if the store says which.

## R4 — The four adapters

| | AWS Secrets Manager | Google Cloud Secret Manager | Azure Key Vault | BellaBaxter |
| --- | --- | --- | --- | --- |
| Package (pinned) | `AWSSDK.SecretsManager` 4.0.100.16 | `Google.Apis.Auth` 1.77.0 + hand-written REST over `HttpClient` | `Azure.Security.KeyVault.Secrets` 4.11.2 + `Azure.Identity` 1.21.0 | `BellaBaxter.Client` 0.1.1-preview.131 |
| Extra DLLs / size | 2 / 1.2 MB | 5 / 1.1 MB | ~9 / ~2.4 MB beyond the shared framework | ~10 / ~1.5 MB beyond what Umbral already ships (Kiota; Polly and Http.Resilience are already in the build) |
| Credentials | SDK default chain: env keys, `AWS_PROFILE`, web identity, ECS/EKS, IMDS | `GoogleCredential.GetApplicationDefaultAsync()` scoped to `cloud-platform`: key file, workload identity federation, metadata server | `ChainedTokenCredential(EnvironmentCredential, WorkloadIdentityCredential, ManagedIdentityCredential)`; user-assigned when `AZURE_CLIENT_ID` is set without a secret, else system-assigned | `BellaClientFactory.CreateWithHmacApiKey` or `CreateWithBearerToken` (with `…AndZke` when `BELLA_BAXTER_PRIVATE_KEY` is set), built once |
| Location | `AWS_REGION` | `GOOGLE_CLOUD_PROJECT` | `UMBRAL__AZURE_KEY_VAULT_URI` | `BELLA_BAXTER_URL`, `BELLA_BAXTER_PROJECT`, `BELLA_BAXTER_ENV` (resolved through `Keys.Me` when an API key omits them) |
| Write | `CreateSecret`; on `ResourceExistsException`, `PutSecretValue` | `POST secrets?secretId=`; on 409, `:addVersion` | `SetSecret` (creates or adds a version) | `PUT …/secrets/{key}`; on 404, `POST`; a 409 on POST is answered with `PUT` again |
| Read | `GetSecretValue` (`AWSCURRENT`) | `GET …/versions/latest:access` | `GetSecret` (latest) | `GET …/secrets/{key}`, value in `AdditionalData["secrets"][key]` |
| Delete | `DeleteSecret` with `RecoveryWindowInDays = 7` | `DELETE` (immediate) | `StartDeleteSecret`, never purge | `DELETE`; 404 is success |
| Locator | the secret's ARN | `projects/{p}/secrets/{name}` | `https://{vault}/secrets/{name}` (no version) | `{project}/{env}/{provider}/{key}` |
| Not found | `ResourceNotFoundException` | 404 | 404 | 404 (`ProblemDetails`) |
| Denied | `AccessDeniedException`, `UnrecognizedClient`, `InvalidSignature`, `ExpiredToken`, `DecryptionFailure` | 401, 403; no ADC found | 401, 403; `CredentialUnavailable`, `AuthenticationFailed` | 401, 403 (`ApiException.ResponseStatusCode`) |
| Unreachable | `HttpRequestException`, `AmazonClientException`, throttling, timeout | 429, 5xx, `HttpRequestException`, timeout | status 0, 429, 5xx, timeout | 429, 5xx, `HttpRequestException`, Polly timeout or broken circuit, timeout |
| Test seam | `ServiceURL` + `HttpClientFactory`, `BasicAWSCredentials` | own `HttpClient` + `GoogleCredential.FromAccessToken` | `SecretClientOptions.Transport`, a test `TokenCredential` | `outerHandler` of the factory |

The locator holds everything needed to find the secret again apart from
credentials (region and account are in the ARN, project in the GCP name, vault
in the Azure id, project and environment in the Bella tuple). That is what
makes R2's lazy readers possible and what keeps a changed `AWS_REGION` or
`BELLA_BAXTER_ENV` from silently pointing a connector at a different secret.

**Google over REST, not gRPC.** `Google.Cloud.SecretManager.V1` brings 20 DLLs
(gRPC, protobuf descriptors for three services, an HTTP/2 channel) for four
calls, and can only be faked with an in-process gRPC server. The REST API is
stable and documented; `Google.Apis.Auth` handles the part that must not be
ours, the token (Principle III's spirit). `Google.Apis.SecretManager.v1` (the
generated REST client) was considered and is equivalent; four hand-written calls
with `System.Text.Json` avoid a generated-model DLL and Newtonsoft on our path.

**Azure with a narrow chain.** `DefaultAzureCredential` also tries Visual
Studio, the CLI, PowerShell and a browser, which in a container means child
processes and slow timeouts. A hand-written Entra token client was rejected:
that is security-sensitive code MSAL already does.

**BellaBaxter through `BellaBaxter.Client`, not `BellaBaxter.AspNet.Configuration`.**
The configuration package's env-var mapping lives inside `AddBellaSecrets`, its
client builder is `internal`, and `AddBellaSecrets` starts polling the moment it
is added. It cannot write. Umbral reads the same variables with the same
precedence (`BELLA_BAXTER_PROJECT` else `BELLA_PROJECT`, `BELLA_BAXTER_ENV` else
`BELLA_ENV`) and the same validation (`BaxterUrl` set, key or token set; with a
token both slugs required), and uses `BellaApiAddress.Problem` for the URL
check. The operator-facing contract is therefore identical to Frunchy's. The
API key must look like `bax-{id}-{hex}`; Umbral checks that at startup because
the client only fails on the first request. Version: preview.131, the latest on
nuget.org; 126→131 did not change the factory or the secret builders. Pinned
exactly; previews move.

**Alternatives**: hand-written SigV4 for AWS — the SDK is already light.
LocalStack as the AWS test seam — needs an auth token since 2026.03.

## R5 — Names in the manager

**Decision**: a logical `SecretName` = (prefix, connector slug, discriminator),
rendered by each store under its own rules:

| Store | Rendering | Example |
| --- | --- | --- |
| AWS, GCP, Azure | `{prefix}-{slug}-{disc}` | `umbral-github-k3f9x2qa` |
| BellaBaxter | `UMBRAL_{PREFIX}_{SLUG}_{DISC}`, upper case, `-` → `_` | `UMBRAL_UMBRAL_GITHUB_K3F9X2QA` |

- `[A-Za-z0-9-]` is the only alphabet valid in AWS, GCP and Azure (Azure has no
  `_`, GCP no `/`). The longest name is 32 + 1 + 32 + 1 + 8 = 74, under Azure's
  127.
- Bella keys must start with a letter and use `[A-Za-z0-9_]` (the CLI's rule;
  the server's is unknown, so the mapping stays inside it). Slugs are
  `^[a-z0-9-]{1,32}$` (`Ids.fs:50`) and never contain `_`, so the mapping is
  one-to-one. The leading `UMBRAL_` keeps Umbral's keys recognisable in an
  environment that `AddBellaSecrets` apps may also load.
- `disc` is 8 base32 characters (`[a-z2-7]`) of SHA-256 over
  `slug + "|" + created_utc`. Eight characters also avoid AWS's rule that a name
  ending in a hyphen plus six characters is read as an ARN suffix.

**Why a discriminator**: AWS keeps a deleted name for its recovery window and
Azure for its soft-delete retention (default 90 days); a re-created connector
with the same slug would fail to write (FR-009). A connector re-created later
has a different `created_utc`, so a different name, with no purge or force
delete and so no purge permission (R10). **Why derived, not random**: the move
command (R8) and a replace must hit the same name when retried, so that a retry
adds a version instead of leaving an orphan copy. The create handler takes the
timestamp once and passes it to both the name and `ConnectorStore.create`.

**Replace** writes a new version under the connector's existing name when the
reference is already in the active store. The reference does not change, and the
cache entry is dropped (FR-013). When the reference is in another store,
replace writes to the active store, switches the reference, then deletes the
old copy, as a move does.

**Alternatives**: restore and update a soft-deleted name — brings back old
versions and needs extra permissions. Force delete or purge — broader grant,
blocked by purge protection, and irreversible. A suffix only after a collision
— needs every vendor's collision error and still races.

## R6 — Cache and grace period

**Decision**: a `CachingReader` in front of external stores only; the local
store decrypts in microseconds and has no outage.

- Keyed by the encoded reference; holds the value as `Secrets.Protected`, its
  fetch time, and the time of the last failed refresh. Process memory only,
  never serialised (FR-013).
- Fresh (age < cache lifetime): served, no call.
- Expired: one refresh. Success replaces the entry. `Unreachable` with
  age < lifetime + grace serves the stale value and records the failure;
  further refresh attempts for that entry are spaced at least 30 s apart, so an
  outage during a busy hour makes a bounded number of calls and log entries.
  `NotFound` or `AccessDenied` evicts the entry and fails at once (FR-014).
- Replace, delete and move evict the entry through the registry (FR-013).
- Time comes from the registered `TimeProvider`, so tests drive it with the
  existing `FakeTimeProvider`.
- Concurrent misses for one key may both call the manager; at the scale of a
  homelab that is cheaper than a single-flight lock to get right.

**Alternatives**: no cache (spec rejected it); a disk cache (defeats the
point); grace on by default but served without retrying (hides the outage from
the log).

## R7 — BellaBaxter specifics

- **Provider**: a Bella environment holds secrets under one or more providers.
  Like the `bella` CLI, Umbral lists the environment's providers and uses the
  single one whose `ProviderMetaType` is `Secrets`; with zero or several,
  `UMBRAL__BELLABAXTER_PROVIDER` names it, and its absence refuses the start
  with that message.
- **Zero-knowledge environments**: when `BELLA_BAXTER_PRIVATE_KEY` is set,
  the client is built with `ZkeDekHandler` and the PEM is loaded with
  `ECDiffieHellman.ImportFromPem` (the configuration package's loader is
  private). Writes are plaintext JSON over TLS in every mode, as the client and
  CLI do. Whether a ZKE environment accepts such writes is unverified;
  quickstart step 6 checks it against a real server.
- **Retried POST**: the client's resilience handler retries POST, so a create
  can come back 409 after a timeout. The write path is idempotent: 409 → PUT.
- **Delete**: whether Bella deletes softly is unknown; the discriminator makes
  it irrelevant to FR-009.
- **Advice in the walkthrough**: give Umbral its own Bella environment, or at
  least rely on the `UMBRAL_` prefix, so apps that load the same environment with
  `AddBellaSecrets` do not receive Umbral's connector secrets as configuration.

## R8 — The move command

**Decision**: `move-secrets [--dry-run]`, dispatched in `main` next to
`reset-credential` and built the same way (`buildApp`, no `initialize`, no
`Run`), so it reads the same settings and fails on the same incomplete ones.
The logic lives in `Infrastructure/SecretMove.fs` as a function over the
registry, the database and the clock, so SC-003's interrupt-at-every-step test
runs in process with an in-memory store that fails on the Nth call.

Per connector whose reference is not in the active store:

1. `Get` from the source. Failure → report, skip.
2. `Put` to the active store under the connector's derived name (R5).
3. `Get` from the target and compare. Mismatch → report, skip.
4. Switch the reference in one statement guarded by the old value
   (`UPDATE … SET client_secret_ref = @new WHERE slug = @slug AND client_secret_ref = @old`),
   in the same transaction as its security event (`SecurityEvents.recordIn`).
   No row updated → somebody changed it meanwhile; report, skip.
5. Evict the cache entry, then `Delete` from the source. Failure → reported as
   a warning naming the leftover; the connector is already moved.

Every step before 4 leaves the old reference in place; after 4 the new one is
complete. A re-run starts from what the references say, and step 2 hits the
same name, so it adds a version rather than an orphan. Exit 0 when nothing
failed (including "nothing to move"), 1 otherwise. Dry run performs no call
that writes and prints `slug  from → to` for each connector. Output is plain
lines for `docker exec`, like `reset-credential`.

**Alternatives**: moving automatically at startup — an unreviewed bulk write
on every misconfigured restart. An admin-area button — a long-running action
behind a form, and a fourth kind of thing on the Connectors screen.

## R9 — The admin paths

- **Create (all three paths)**: validate, take `now`, `Put` with the derived
  name, then `ConnectorStore.create` with the same `now`. A `Put` failure
  refuses with notice `connector-secret-store-failed`, whose text names the
  store and the cause and says what to check (Principle VI). A create that then
  loses the race deletes the secret it just wrote (best effort), as no
  connector refers to it. The OIDC add form re-renders as it already does; the
  other two keep their redirect-with-notice. The spec's "the form keeps what
  the operator typed" is narrowed to "nothing is saved and the screen says
  why", because two of the three paths are redirects today and the secret
  must be retyped in every case.
- **Replace**: R5. A failure refuses with the same notice; the connector keeps
  its old secret.
- **Delete**: delete the row and record `ConnectorDeleted` first, then
  `Delete` in the store with a catch. Failure → notice
  `connector-secret-left-behind` carrying the secret's name in the manager,
  and a `SecretStoreFailed` event (FR-010).
- **Callback**: `Get` through the registry and cache. On failure the person
  gets today's `sign-in-failed` page; the security event's detail carries
  store and cause (FR-015).

## R10 — Permissions the walkthroughs grant

| Store | Minimum |
| --- | --- |
| AWS | `CreateSecret`, `PutSecretValue`, `GetSecretValue`, `DescribeSecret`, `DeleteSecret` on `arn:aws:secretsmanager:REGION:ACCOUNT:secret:umbral-*`; plus `kms:Encrypt`, `kms:Decrypt`, `kms:GenerateDataKey` only with a customer-managed key. |
| GCP | Custom role: `secretmanager.secrets.create` on the project; `secrets.get`, `secrets.delete`, `versions.add`, `versions.access` with condition `resource.name.startsWith("projects/NUMBER/secrets/umbral-")`. The walkthrough tells the operator to test create, as prefix conditions on create are reported not to match. |
| Azure | A dedicated vault, `Key Vault Secrets Officer` at vault scope. Key Vault cannot scope a role to a name prefix. No purge. |
| BellaBaxter | An API key for Umbral's own project and environment. |

The startup probe (R11) uses only `GetSecretValue`/`access`/`GetSecret` on a
name that does not exist, so it needs no permission beyond these.

## R11 — Startup probe and tests

- **Probe**: in `initialize`, after the schema, the active external store gets
  `Probe`: a read of `{prefix}-probe` (the store's rendering). `NotFound` means
  reachable and allowed; anything else is logged with what to do and kept in a
  `SecretStoreStatus` singleton the Connectors screen reads (FR-005, Story 4).
  It runs under the 10 s bound and never stops the start. `/healthz` stays a
  SQLite check (`Health.fs:8-12` excludes what a restart cannot fix).
- **Seams**: the registry is registered with `TryAddSingleton`, like
  `TimeProvider` (`Program.fs:98`), so a test puts in an in-memory store under
  any store id. Each adapter takes an `HttpMessageHandler` (or the SDK's
  transport) so its tests run against canned vendor responses: 200, 404, 403,
  409, 429, 5xx, hang. No emulator, no credentials (FR-025, SC-008).
- **Memory**: `IdleMemoryTests` gains a run with `UMBRAL__SECRET_STORE=aws`,
  `AWS_ENDPOINT_URL_SECRETS_MANAGER` pointed at a local stub, static fake keys
  and `AWS_EC2_METADATA_DISABLED=true` (SC-006). AWS is the one store whose SDK
  honours an endpoint variable natively; the other three are covered by the
  in-process adapter tests.
- **Leak check**: `LogRedactionTests` gains a journey through an external store
  (create, sign in, replace, outage with grace, move, delete) and searches every
  log and `security_events` for the secret (SC-007).

## R12 — Documentation

- `docs/secret-stores/{aws,gcp,azure,bellabaxter}.md`, in the style of
  `docs/connectors/*.md`: create the place, grant R10's permissions, the
  variables to set, moving an instance in and out, and what to do on each
  startup message.
- README: the settings reference gains R1's rows; "Your data: back it up" says
  that with an external store the connector secrets are not in `/data` and a
  restore needs the same manager (FR-024); the incorrect "app secrets are
  encrypted with the key ring" (`README.md:453-456`, `:521-523`, the
  Dockerfile comment) is corrected to "hashed" while that text is open.
- AWS walkthrough mentions `AWS_EC2_METADATA_DISABLED=true` off AWS, where the
  credential chain otherwise spends a second or two probing IMDS.

## Open items carried into tasks

- Confirm BellaBaxter's single-secret GET shape, ZKE write acceptance and delete
  semantics against a real server (quickstart step 6). The adapter tests pin
  today's reading of the decompiled client.
- Measure idle RSS with each store active on arm64; R4's sizes are on-disk only.
