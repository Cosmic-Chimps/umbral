# Contract: deployment settings

**Feature**: `005-external-secret-stores` | FR-002 to FR-006, FR-013, FR-014

Every setting is an environment variable. None is entered in the admin area or
stored in the database (FR-004). A deployment that sets none of them behaves
exactly as before this feature (FR-002).

## Umbral's settings

Configuration key in brackets; the environment variable is the key with `:`
written as `__`, as for `UMBRAL__PUBLIC_ORIGIN`.

| Variable | Default | Values | Refuses the start when |
| --- | --- | --- | --- |
| `UMBRAL__SECRET_STORE` (`UMBRAL:SECRET_STORE`) | `local` | `local`, `aws`, `gcp`, `azure`, `bellabaxter` (case-insensitive) | any other value |
| `UMBRAL__SECRET_STORE_PREFIX` | `umbral` | 1–32 of `[a-z0-9-]`, not starting or ending with `-` | malformed |
| `UMBRAL__SECRET_CACHE_SECONDS` | `300` | integer ≥ 0; 0 = no cache | not an integer, negative |
| `UMBRAL__SECRET_GRACE_SECONDS` | `3600` | integer ≥ 0; 0 = strict | not an integer, negative |
| `UMBRAL__AZURE_KEY_VAULT_URI` | — | `https://…` absolute URI | store is `azure` and it is missing, not https, or has a path |
| `UMBRAL__BELLABAXTER_PROVIDER` | — | a provider slug in the Bella environment | never at parse time; see BellaBaxter below |

The prefix, cache and grace settings are read whatever the store, so a mistake
is caught before it matters.

## Vendor settings, per store

Read by the vendor's own credential chain unless marked *Umbral*. Credential
variables are optional where the host provides an ambient identity.

### `aws`

| Variable | Required | Note |
| --- | --- | --- |
| `AWS_REGION` | yes (*Umbral checks presence*) | Region new secrets are created in |
| `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` | unless ambient | Optionally `AWS_SESSION_TOKEN` |
| `AWS_PROFILE`, `AWS_WEB_IDENTITY_TOKEN_FILE`, ECS/EKS, IMDS | alternatives | The SDK's default chain |
| `AWS_EC2_METADATA_DISABLED=true` | recommended off AWS | Avoids a slow IMDS probe |
| `AWS_ENDPOINT_URL_SECRETS_MANAGER` | no | Honoured by the SDK; used by the idle-memory test's stub |

### `gcp`

| Variable | Required | Note |
| --- | --- | --- |
| `GOOGLE_CLOUD_PROJECT` | yes (*Umbral checks presence*) | Project ID new secrets are created in |
| `GOOGLE_APPLICATION_CREDENTIALS` | unless ambient | Path to a mounted service-account key or workload identity federation file |

### `azure`

| Variable | Required | Note |
| --- | --- | --- |
| `UMBRAL__AZURE_KEY_VAULT_URI` | yes (*Umbral*) | No vendor-standard variable exists |
| `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` | unless ambient | Service principal. Certificate variables also accepted by `EnvironmentCredential` |
| `AZURE_FEDERATED_TOKEN_FILE` | alternative | Workload identity (AKS) |
| `AZURE_CLIENT_ID` alone | alternative | User-assigned managed identity; none = system-assigned |

### `bellabaxter`

Same variables and precedence as `BellaBaxter.AspNet.Configuration` in Frunchy.
`BellaBaxter:*` configuration keys are **not** read: environment only.

| Variable | Required | Note |
| --- | --- | --- |
| `BELLA_BAXTER_API_KEY` or `BELLA_BAXTER_ACCESS_TOKEN` | one of them | API key must look like `bax-{id}-{hex}` |
| `BELLA_BAXTER_URL` | no | Default `https://api.bella-baxter.io`; https unless loopback |
| `BELLA_BAXTER_PROJECT` (else `BELLA_PROJECT`) | with a token | With an API key, resolved from the key when absent |
| `BELLA_BAXTER_ENV` (else `BELLA_ENV`) | with a token | Same |
| `BELLA_BAXTER_PRIVATE_KEY` | for zero-knowledge environments | PEM P-256 device key |
| `UMBRAL__BELLABAXTER_PROVIDER` | when the environment has zero or several secret providers | Checked at the startup probe; with an ambiguous environment the probe fails with a message naming this variable, and saving refuses with the same text |

## Startup behaviour

| Situation | Result |
| --- | --- |
| No setting | Local store. No vendor assembly loaded, no outbound call. |
| Malformed or incomplete setting (table above) | Start refused. One message per problem, naming the variable and what to set, in the style of `PublicOrigin.describe`. The offline commands refuse the same way. |
| Complete setting, manager unreachable or access refused | Starts. Warning logged with cause and next step; `secret_store_failed` event with `op=probe`; Connectors screen notice (contract [admin-surface.md](./admin-surface.md)). |
| Complete setting, the probe's own read of a name that does not exist | Healthy (the adapter reads the 404 as "reachable and allowed"). Nothing shown. A "not found" that the adapter does not read that way, such as BellaBaxter's project or environment missing, is a failure with its own text. |

`/healthz` does not check the manager.
