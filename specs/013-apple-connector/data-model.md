# Data model: Sign in with Apple

## `connectors` (schema version 10)

Two new columns, read for kind `apple` only:

| Column | Type | Meaning |
| --- | --- | --- |
| `apple_team_id` | `TEXT NULL` | The Team ID: `iss` of the client secret. |
| `apple_key_id` | `TEXT NULL` | The Key ID: `kid` of the client secret. |

Both are NULL for every other kind, and both are set for an `apple` row.
`UpgradeToVersion10` is two `ALTER TABLE connectors ADD COLUMN`; the
`CREATE TABLE` in `schema` gets the same columns for a fresh database. No data
is rewritten. An image older than this refuses a version 10 database, as for
every step.

Existing columns as an `apple` row uses them:

| Column | Value |
| --- | --- |
| `slug` | `apple` (one per instance) |
| `kind` | `apple` |
| `client_id` | The Services ID |
| `client_secret_ref` | The reference to the `.p8` key in a secret store |
| `client_secret_hint` | Empty: no characters of a private key are kept |
| `issuer_url` | NULL (the issuer is compiled in) |

## Domain (`Domains/Connectors.fs`)

```fsharp
| Apple                      // ProviderKind; Value "apple", DisplayName "Apple"

/// Only for `Apple`: who signs the client secret.
type AppleKeyIds = { TeamId: string; KeyId: string }

type Connector = { …; AppleKey: AppleKeyIds option; … }   // None for every other kind

type ProviderEndpoints = { …; AnswersByFormPost: bool }   // true for Apple only
```

`endpointsFor Apple`: URLs empty (from discovery, like `Oidc`), scopes
`name`, `email`, PKCE on, nonce on, `AnswersByFormPost = true`.

`appleIssuer = "https://appleid.apple.com"`.

Pure checks, with the messages the pane shows:

- `parseAppleId : string -> string option`: trimmed, exactly ten ASCII letters
  or digits (Team ID and Key ID).
- `parseServicesId : string -> string option`: trimmed, non-empty, no
  whitespace.

## `users`, `linked_accounts`

No change. A person who signs in with Apple is a `linked_accounts` row with
`provider_kind = 'apple'` and `provider_subject` = Apple's `sub`.

## Cookies

| Cookie | Mode | SameSite | Purpose string |
| --- | --- | --- | --- |
| `umbral_connect` | answer by redirect (every kind but Apple) | `Lax` | `Umbral.Connect.v1` (unchanged) |
| `umbral_connect_post` | answer by form post (Apple) | `None; Secure` | `Umbral.Connect.FormPost.v1` |

Same payload, same ten minutes, same path `/connect`, both `HttpOnly`.
