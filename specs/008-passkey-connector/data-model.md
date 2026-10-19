# Data Model: The Passkey Connector

No new concept. A **Connector** of a new built-in kind, credentials and
invitations that belong to a **User**, and short-lived ceremony state.
Schema version 5 → 6 (research R7).

## Connector (changed)

| Field | Stored as | Rule |
| --- | --- | --- |
| `Kind` | `connectors.kind = 'passkey'` | New `ProviderKind.Passkey`. At most one row of this kind; its slug is always `passkey`. |
| `PasskeyEnrolment` | `connectors.passkey_enrolment TEXT NOT NULL DEFAULT 'invitations'` | `invitations` (InvitationsOnly) or `open` (OpenSignUp). Read for the passkey kind only; every other kind keeps the default and ignores it. |
| `ClientId`, `ClientSecretRef`, `ClientSecretHint` | `''` | Empty for the passkey kind. A reference that does not decode means "no secret": `SecretMove` skips the row, `ConnectorDelete` deletes nothing from a store, the card shows no store pill. |
| `IssuerUrl` | `NULL` | As for every non-`oidc` kind. |
| `AllowedEmailDomains` | `''` | Always empty; no field on the card; `EmailDomainRules.ruleFor` answers `NoRule` for the kind. |

Enabled, shared, created and updated as for every connector.

```text
PasskeyEnrolment = InvitationsOnly | OpenSignUp     // Domains/Passkeys.fs
parseEnrolment  : string -> PasskeyEnrolment option  // "invitations" | "open"
```

## Passkey (new table `passkeys`)

```sql
CREATE TABLE IF NOT EXISTS passkeys (
    id               TEXT    NOT NULL PRIMARY KEY,   -- Ids.newSubject-style random id; what URLs carry
    user_id          TEXT    NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    credential_id    BLOB    NOT NULL UNIQUE,        -- the authenticator's credential id
    public_key       BLOB    NOT NULL,               -- COSE key as the library returns it
    sign_count       INTEGER NOT NULL DEFAULT 0,
    nickname         TEXT    NOT NULL,               -- 1..64 chars, default "Passkey added <date>"
    aaguid           TEXT        NULL,               -- authenticator model id, for the next options' excludeCredentials only
    transports       TEXT    NOT NULL DEFAULT '',    -- space-joined, echoed in excludeCredentials
    backup_eligible  INTEGER     NULL,               -- as the library reports it; passed back as StoredBackupEligible
    created_utc      TEXT    NOT NULL,
    last_used_utc    TEXT        NULL
);
CREATE INDEX IF NOT EXISTS ix_passkeys_user ON passkeys (user_id);
```

Rules:

- A user holds at most 10 (`Domains.Passkeys.MaxPerUser`).
- `credential_id` is unique across all users: the library's
  `IsCredentialIdUniqueToUserCallback` queries it, and the `UNIQUE` constraint
  is the second line of defence.
- `sign_count` is replaced by the library's `SignCount` after every successful
  assertion; `last_used_utc` is set at the same time.
- Removing the last passkey of a user with no `linked_accounts` row is refused
  (FR-020). Checked in one transaction with the delete.
- Deleting the user cascades.

## Invitation (new table `passkey_invitations`)

```sql
CREATE TABLE IF NOT EXISTS passkey_invitations (
    id              TEXT NOT NULL PRIMARY KEY,         -- random id; what admin URLs carry
    secret_hash     TEXT NOT NULL UNIQUE,              -- hex SHA-256 of the 32-byte base64url secret in the link
    connector_slug  TEXT NOT NULL REFERENCES connectors (slug) ON DELETE CASCADE,
    user_id         TEXT     NULL REFERENCES users (id) ON DELETE CASCADE,  -- NULL: for a new person
    note            TEXT NOT NULL DEFAULT '',          -- the operator's note, 0..64 chars, shown on the card
    created_utc     TEXT NOT NULL,
    expires_utc     TEXT NOT NULL,                     -- created + 7 days
    used_utc        TEXT     NULL,
    revoked_utc     TEXT     NULL
);
CREATE INDEX IF NOT EXISTS ix_passkey_invitations_open
    ON passkey_invitations (connector_slug) WHERE used_utc IS NULL AND revoked_utc IS NULL;
```

States: **open** (`used_utc IS NULL AND revoked_utc IS NULL AND expires_utc >
now`, and for a user-bound one the user exists and is enabled) → **used**
(`used_utc` set, in the same transaction as the passkey it produced) or
**revoked** (`revoked_utc` set by the operator) or **expired** (time passes).
Only open ones are listed. Used, revoked and expired rows older than 30 days
are pruned when an invitation is created; deleting the connector or the user
cascades.

```text
Invitation lifetime = 7 days                          // Domains/Passkeys.fs, a constant
newInvitationSecret : unit -> string                  // 32 random bytes, base64url (Ids)
hashInvitationSecret: string -> string                // SHA-256, lowercase hex
invitationLink      : PublicOrigin -> secret -> string  // "{origin}/invite/{secret}"
```

## Ceremony (new table `passkey_ceremonies`)

```sql
CREATE TABLE IF NOT EXISTS passkey_ceremonies (
    id             TEXT NOT NULL PRIMARY KEY,   -- random id the client echoes back
    purpose        TEXT NOT NULL,               -- 'signin' | 'signup' | 'invitation' | 'add'
    options_json   TEXT NOT NULL,               -- CredentialCreateOptions / AssertionOptions .ToJson()
    user_id        TEXT     NULL,               -- 'add' and user-bound 'invitation': the account the passkey joins
    invitation_id  TEXT     NULL,               -- 'invitation'
    display_name   TEXT     NULL,               -- 'signup' and new-person 'invitation': the name typed
    nickname       TEXT     NULL,               -- every registration purpose, as typed (may be empty)
    return_url     TEXT     NULL,               -- 'signin' and 'signup': the authorize request to replay
    created_utc    TEXT NOT NULL,
    expires_utc    TEXT NOT NULL                -- created + 5 minutes
);
```

Consumed by `DELETE … WHERE id = @id AND expires_utc > @now RETURNING …`
(research R3). Rows past `expires_utc` are pruned on every insert. No foreign
keys: a ceremony is minutes old and a deleted user or invitation simply makes
its completion fail its own checks.

## User (unchanged shape)

A passkey-only user: `display_name` typed at enrolment, `email NULL`,
`email_verified 0`, `avatar_url NULL`, no `linked_accounts` row. The Users
screen lists "Passkey" among their ways in from the `passkeys` table, next to
the linked provider kinds.

## Session cookie (unchanged shape)

A passkey sign-in calls `Sessions.signIn ctx userId "passkey" "passkey" None`:
`idp` = `passkey`, `connector` = `passkey`, no `email_domain`.

## Authorization principal and tokens (unchanged shape)

`idp` = `passkey`; `umbral_connector` = `passkey`; no `umbral_email_domain`.
`EmailDomainRules.recheck` finds the connector, `ruleFor` answers `NoRule`,
admitted. `email`/`email_verified` follow `users.email` as today (absent for a
passkey-only user).

## Security events

| Type | Actor | Fields | Detail |
| --- | --- | --- | --- |
| `passkey_registered` (new) | user | user id, connector `passkey`, remote IP | `via=signup` / `via=invitation` / `via=add` |
| `passkey_removed` (new) | user or operator | user id, remote IP | `by=user` / `by=operator` |
| `passkey_invitation_created` (new) | operator | connector `passkey`, user id when for an existing user, remote IP | `for=new` / `for=user` |
| `passkey_invitation_revoked` (new) | operator | connector `passkey`, user id when bound, remote IP | — |
| `user_created` (existing) | user | user id, connector `passkey` | — |
| `sign_in_succeeded` (existing) | user | user id, connector `passkey` | — |
| `sign_in_failed` (existing) | user | connector `passkey`, user id when known | `passkey_refused;reason=<R6 word>;stage=<registration|assertion|invitation>` |
| `connector_created|updated|enabled|disabled|deleted` (existing) | operator | connector `passkey` | `enrolment=invitations|open` on create and update |

Never in any event or log line: a challenge, a credential id, a public key, an
invitation secret or link, a `clientDataJSON`, or a library exception message.

## Texts (new fields, all in `Views/Texts.fs`)

Groups, each read by at least one view (MetaTests): the Passkey card
(heading, enrolment labels and hints, invitation form, the open-invitations
list, revoke), the sign-in page (continue button, create-account form, the
invitations-only line, the JavaScript note, failure notices), the invitation
page (new person, existing user, unusable link), the passkeys page (title,
lead, list columns, add form, remove confirm, last-passkey refusal, limit
reached, not enabled), the Users row (passkey pill, list, remove, invite
button), the home page link, and the `knownNotice` codes:
`passkey-connector-saved`, `passkey-invitation-created`,
`passkey-invitation-revoked`, `passkey-added`, `passkey-removed`,
`passkey-last-cannot-remove`, `passkey-limit-reached`, `passkey-not-enabled`,
`passkey-sign-in-failed`, `passkey-registration-failed`,
`passkey-invitation-unusable`, `passkey-user-invited`.
