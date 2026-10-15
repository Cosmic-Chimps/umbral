# Data Model: External Secret Stores

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

**No schema change.** The `connectors` table already stores
`client_secret_ref` as `"<store>:<locator>"`, opaque outside the store that
wrote it (`Database.fs:82-88`). This feature only adds values that column can
hold. No upgrade step, no schema version bump.

## Stored

### `connectors.client_secret_ref` (existing column, new values)

| Store id | Locator | Example |
| --- | --- | --- |
| `local` | Data Protection ciphertext (unchanged) | `local:CfDJ8…` |
| `aws` | The secret's ARN | `aws:arn:aws:secretsmanager:eu-west-1:123456789012:secret:umbral-github-k3f9x2qa-AbCdEf` |
| `gcp` | Full resource name | `gcp:projects/my-project/secrets/umbral-github-k3f9x2qa` |
| `azure` | Versionless secret id | `azure:https://umbral-kv.vault.azure.net/secrets/umbral-github-k3f9x2qa` |
| `bellabaxter` | `{project}/{environment}/{provider}/{key}` | `bellabaxter:umbral/production/vault/UMBRAL_UMBRAL_GITHUB_K3F9X2QA` |

Rules:

- `SecretRef.Decode` splits at the **first** `:` (`Secrets.fs:44`), so the
  colons inside an ARN or a URI stay in the locator. Unchanged.
- Nothing outside the owning store parses a locator, with one exception: the
  card's store label reads only the store id (FR-020).
- The locator carries everything needed to find the secret again except
  credentials (research R4). A changed `AWS_REGION`, `GOOGLE_CLOUD_PROJECT`,
  vault URI or `BELLA_BAXTER_ENV` changes where **new** secrets go, never where
  an existing reference points.

### `connectors.client_secret_hint` (existing, unchanged)

Last four characters of the value last saved through Umbral, for every store.
A value rotated directly in the manager does not update it (spec, Edge Cases).

### `connectors.created_utc` (existing, new use)

Input to the name discriminator (below). It is written once at creation and
never updated (`ConnectorStore.fs:90`), which is what makes the derived name
stable for a connector's whole life and different for a re-created one.

### `security_events` (existing table, new kinds)

| Event type (stored value) | When | Outcome | `Detail` |
| --- | --- | --- | --- |
| `secret_store_failed` | A write, read, delete or probe against an external store failed | Failure | `store=<id>;op=<put\|get\|delete\|probe>;cause=<unreachable\|access_denied\|not_found\|not_configured\|rejected>[;stale=true]` |
| `connector_secret_moved` | The move command switched a connector's reference | Success | `from=<id>;to=<id>` |

The connector slug goes in the event's existing `ConnectorSlug` field, as for
the other connector events (absent for a probe). `stale=true` marks a failed refresh served from the grace
period (FR-014). No `Detail` ever holds a value, a locator, or a credential.
Both kinds join `allEventTypes`, so `SecurityEventCoverageTests` requires a test
that emits each.

Existing events keep their meaning: `ConnectorCreated`, `ConnectorUpdated` and
`ConnectorDeleted` are recorded as today, with `Detail = store=<id>` naming the
store that holds the secret (FR-022; `ConnectorUpdated` only when the secret
changed). A sign-in that cannot read its secret
still records `SignInFailed`, with `Detail` becoming
`connector_secret_unreadable;store=<id>;cause=<cause>` (FR-015).

## In memory only

### `SecretName`

The logical name of a connector's secret: `(prefix, slug, discriminator)`.

- `prefix`: `UMBRAL__SECRET_STORE_PREFIX`, default `umbral`,
  `^[a-z0-9]([a-z0-9-]{0,30}[a-z0-9])?$`.
- `slug`: the connector's slug, `^[a-z0-9-]{1,32}$`.
- `discriminator`: first 8 characters of the lower-case base32 (`[a-z2-7]`)
  encoding of SHA-256(`slug + "|" + created_utc`).

Each store renders it (research R5): `{prefix}-{slug}-{disc}` for AWS, GCP and
Azure (≤ 74 characters); `UMBRAL_{PREFIX}_{SLUG}_{DISC}` upper-cased with `-` →
`_` for BellaBaxter. The local store ignores it.

### `StoreFailure`

`Unreachable | AccessDenied | NotFound | NotConfigured | Rejected of reason`.
Only `Unreachable` lets the cache serve a stale value.

### `SecretStoreSettings`

Parsed once at startup from configuration (research R1): the active store id,
the prefix, cache lifetime, grace period, and the active store's location
(region, project, vault URI, or Bella URL, project, environment, provider).
Credentials are never part of it: each vendor's credential chain reads them
itself, so they never pass through Umbral's code or logs.

### Cache entry

Per encoded reference: the value (`Secrets.Protected`), `fetchedAt`,
`lastFailedRefreshAt`. States:

```text
            read ok                       age ≥ lifetime
  (absent) ────────► FRESH ─────────────────────────────► EXPIRED
     ▲                  ▲                                    │
     │                  └──────── refresh ok ────────────────┤
     │                                                       │ refresh Unreachable
     │                                                       ▼
     │                                         STALE (served while age < lifetime + grace,
     │                                                refresh retried ≥ 30 s apart)
     │                                                       │
     └──── evict: replace / delete / move / NotFound / AccessDenied / grace over
```

Lost on restart by construction (FR-013, Story 2 scenario 6).

### `SecretStoreStatus`

Singleton holding the outcome of the startup probe of the active store
(`Ok | Failed of StoreFailure * message`) and when it ran. Read by the
Connectors screen for its page notice (Story 4 scenario 3). Updated again by
every write to the active store: a failed one (unreachable, access denied, not
configured, rejected) raises the notice, a successful one clears it, so the
notice goes away once the operator has fixed the cause and saved something,
without a restart.

## Derived for display

| Card shows | Derived from |
| --- | --- |
| "Kept in <store name>" | store id of `client_secret_ref` |
| Warning "this secret is in <store>, which this instance cannot read" | registry cannot build that store (`NotConfigured`), checked without a network call |
| Masked secret `••••••••abcd` | `client_secret_hint` (unchanged) |
