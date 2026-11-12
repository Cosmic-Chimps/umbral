# Data model: the Bluesky connector

## Domain

- `ProviderKind.Bluesky`, with value `bluesky` and display name `Bluesky`. It is a built-in kind: never in the add form, at most one connector, and that connector's slug is `bluesky`. It has no client ID or secret; `client_id` and `client_secret_ref` stay empty, as Fediverse's do.
- The connector reuses `Connector.FediverseAccess` (`OnlyListed`, `Servers`) as its server scope. The record and its columns are kind-neutral in all but name. Renaming them would touch every connector literal for no behaviour, so a comment on the type says both kinds use it. For Bluesky the hosts are authorization-server hosts.
- `ProviderProfile.Server: string option`, new. It is the host the identity signed in through: the issuer's host for Bluesky, and `None` for every other adapter, Fediverse included, since its subject already holds the host.

`Domains/Bluesky.fs` (new, pure):

| Function | Returns |
|---|---|
| `parseInput allowDev text` | `Handle h` or `Did d`, or `None` (R3, R4 syntax) |
| `isHandle`, `normaliseHandle` | |
| `parseDid`, `didWebHost` | |
| `atprotoDidOfTxt values` | the single `did=` value, else `None` |
| `parseDidDocument json` | `{ Id; AlsoKnownAs; Pds }`, or an error word |
| `claimsHandle doc handle` | `bool` |
| `handleOf doc` | `string option` |
| `parseProtectedResource json` | the one AS origin, else `None` |
| `parseServerMetadata origin json` | `{ Issuer; Par; Authorize; Token }`, or `Unsupported reason` (the FR-010 list, R5) |
| `parseTokenAnswer json` | `{ Sub; Scope; TokenType }` |
| `checkTokenAnswer expectedDid answer` | `Ok did`, or a failure word (R6 steps 5 and 6) |
| `cleanName` | as Apple's `nameOf` |
| `avatarCid json`, `displayName json` | |
| `clientIdFor allowDev origin` | the `client_id`, or `OriginUnsuitable` (R8) |
| `clientMetadata origin` | the document as a record |
| `parseServers`, `allows` | the Fediverse functions, reused |

## Storage, schema version 12

```sql
ALTER TABLE linked_accounts ADD COLUMN provider_server TEXT NULL;
```

- **Writes.** `UserStore.resolve` writes `provider_server` from `ProviderProfile.Server` on insert, and updates it on every sign-in when the profile carries one (`COALESCE(@server, provider_server)`).
- **Reads.** `BlueskyAccounts.recheckServer config connectorSlug userId` lives in `Infrastructure/BlueskyAccounts.fs`. It reads `provider_server` for the user's `bluesky` links and refuses when Only these servers lists none of them. A link whose `provider_server` is null admits.
- **Unchanged.**
  - the connector's columns: `fediverse_scope` and `fediverse_servers` hold Bluesky's scope too;
  - `users.preferred_username` and `users.profile_url` (spec 010) hold the handle and the bsky.app address;
  - `linked_accounts.provider_subject` holds the DID.
- `docs/run/backup.md` names schema version 12.

## The attempt (never stored)

`OAuthRoundTrip.Correlation.Extra: string`, empty for every other kind (R9). For Bluesky, it is the JSON:

```json
{ "did": "did:plc:…", "handle": "alice.bsky.social", "tokenEndpoint": "https://…/oauth/token",
  "jwk": "{…private ES256 JWK…}", "nonce": "…" }
```

`Correlation.Server` holds the issuer, for example `https://bsky.social`. The callback reads the issuer from here, never from the query.

## Seams for tests

- `Providers.Fediverse.PrimaryHandler`: already the HTTP seam. The Bluesky client uses the same guarded handler through its own named client, `umbral-bluesky`.
- `Providers.Bluesky.TxtResolver`: a delegate, `string * CancellationToken -> Task<string array>`. The default uses DnsClient, created lazily.
- `Providers.Bluesky.Options`: `{ AllowDev: bool; PlcDirectory: string }`.
