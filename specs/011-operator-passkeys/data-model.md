# Data model: Operator passkeys

## `operator_passkeys` (new, schema version 8)

| Column | Type | Notes |
| --- | --- | --- |
| `id` | TEXT PK | 128 random bits, base64url; what a form action carries |
| `credential_id` | BLOB NOT NULL UNIQUE | never in a URL, a page or a log |
| `public_key` | BLOB NOT NULL | COSE key as the library returned it |
| `sign_count` | INTEGER NOT NULL | the counter of the last verified answer |
| `name` | TEXT NOT NULL | the user nickname rules (`Passkeys.validateNickname`, dated default) |
| `transports` | TEXT NOT NULL DEFAULT '' | space-separated, echoed in `excludeCredentials` |
| `created_utc` | TEXT NOT NULL | ISO 8601 |
| `last_used_utc` | TEXT NULL | every successful sign-in or re-check |

Rules, each one statement:

- Insert only while fewer than 10 rows exist (FR-002).
- Delete one only when password sign-in is on or another row remains (FR-011).
- `reset-credential --forget-passkeys` deletes them all (FR-014).

No foreign key: the operator is not a user.

## `settings` keys (existing table)

| Key | Value | Notes |
| --- | --- | --- |
| `operator_password_signin` | `on` / `off` | absent means `on`; written `off` only while a passkey exists (FR-007) |
| `operator_user_handle` | `operator-<random>` | minted on the first registration (research R3) |

Existing keys used as they are: `operator_credential` (the hash, kept while
off), `operator_session_stamp` (rotated on remove, switch off and reset).

## `passkey_ceremonies` (existing table)

New purposes: `operator-signin`, `operator-add` (`nickname` holds the name
typed), `operator-confirm`. Other columns stay NULL for operator ceremonies.

## Upgrade

Schema 7 → 8 runs `CREATE TABLE IF NOT EXISTS operator_passkeys`. An upgraded
instance has no passkeys and no switch row, so password sign-in is on.

## Security events (new types)

| Type (stored value) | Success detail | Failure detail |
| --- | --- | --- |
| `operator_passkey_added` | — | `limit_reached`, a failed re-check, or `Passkeys.refusalDetail` for a refused registration |
| `operator_passkey_removed` | `offline_reset count=N` when by the host | `last_passkey_password_off`, `not_found`, a failed re-check |
| `operator_password_signin_changed` | `on` / `off` / `offline_reset` | `no_passkey`, a failed re-check |
| `operator_signed_in` (existing) | `passkey` | — |
| `operator_sign_in_failed` (existing) | — | `Passkeys.refusalDetail` for a refused passkey sign-in |

A failed re-check is `recheck_password_mismatch` (wrong password, or any
password while password sign-in is off) or `Passkeys.refusalDetail` of the
confirm ceremony's refusal. Each refused change writes one event, of its own
type. A change posted with no re-check at all is not a failure: it is answered
with the confirm block and writes nothing.

Reasons are the closed words of `Passkeys.Reason`. No credential id,
key, challenge or password in any detail.
