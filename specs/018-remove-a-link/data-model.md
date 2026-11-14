# Data model: Remove one linked provider account

## Storage

Schema version 15: a new table. Nothing existing is rewritten.

```sql
CREATE TABLE IF NOT EXISTS removed_links (
    provider_kind    TEXT NOT NULL,
    provider_subject TEXT NOT NULL,
    removed_utc      TEXT NOT NULL,
    PRIMARY KEY (provider_kind, provider_subject)
);
```

In `Database.schema` (fresh databases) and as upgrade step 15 (the same
statement as a `[<Literal>]`); `SchemaVersion = 15`.

No `user_id` and no foreign key, on purpose (spec decision 1): the record must
outlive the user the link was removed from. The subject is the same value
`linked_accounts.provider_subject` held (for `oidc`, `issuer#sub`).

## The fingerprint (pure, `Domains/Linking.fs`)

```fsharp
/// FR-008. Opaque name of one provider account, for a form and a claim.
let fingerprint (kind: string) (subject: string) : string
```

`SHA256(UTF8(kind + "\n" + subject))`, base64url, no padding (43 characters).
Never parsed back: a holder finds the account by computing the fingerprint of
each of the user's links (a user has a handful).

## Store functions (`Infrastructure/UserStore.fs`)

```fsharp
type LinkedAccount =
    { Kind: string
      Subject: string
      LinkedUtc: DateTimeOffset
      LastUsedUtc: DateTimeOffset }

/// The user's links with their dates, oldest first (the screen's list).
let linkedAccountDetails (config) (userId) : Task<LinkedAccount list>

type RemoveLinkOutcome =
    | LinkRemoved of kind: string * subject: string * lowered: bool
    | LinkLastWayIn
    | LinkNotFound

/// FR-003, one transaction.
let removeLink (config) (clock: TimeProvider) (userId) (fingerprint) : Task<RemoveLinkOutcome>

/// FR-006. Whether this provider account was removed from a user before.
let isRemoved (config) (kind: string) (subject: string) : Task<bool>

/// FR-009. True when the session or token may go on, false when refused.
let linkStillHeld
    (config) (userId: string) (connectorSlug: string option) (link: string option) : Task<bool>
```

`removeLink`, inside one transaction:

1. Read the user's links; find the one whose fingerprint matches. None:
   roll back, `LinkNotFound`.
2. `DELETE FROM linked_accounts WHERE provider_kind = @kind AND
   provider_subject = @subject AND user_id = @userId AND ((SELECT count(*) FROM
   linked_accounts WHERE user_id = @userId) > 1 OR EXISTS (SELECT 1 FROM
   passkeys WHERE user_id = @userId))`. Zero rows: roll back, `LinkLastWayIn`.
3. `INSERT INTO removed_links (...) VALUES (...) ON CONFLICT DO NOTHING`.
4. `UPDATE users SET email_verified = 0 WHERE id = @userId AND " +
   Database.UnbackedEmailVerified`; `lowered` = rows changed > 0.
5. Commit, `LinkRemoved`.

`linkStillHeld`:

| Session or token | Rule |
| --- | --- |
| No connector slug | true (the connector re-checks own this case) |
| A fingerprint | true when one of the user's links has it |
| No fingerprint, connector is the passkey kind (or not found) | true (the kind re-check refuses a missing connector first) |
| No fingerprint, `oidc` connector | true when the user has an `oidc` link whose subject starts with the connector's issuer + `#` |
| No fingerprint, other kind | true when the user has a link of the connector's kind |

## Claims

| Where | Name | Value |
| --- | --- | --- |
| End-user session cookie | `link` (`Sessions.LinkClaim`) | the fingerprint, provider sign-ins only |
| Authorization identity (codes, refresh tokens) | `umbral_link` (`OpenIddictSetup.LinkClaim`) | copied from the session; no destination |

`Sessions.signIn` takes `link: string option` as a last argument; passkey
callers pass `None`. `Authorize.buildIdentity` copies `Sessions.currentLink`
into the identity; `destinationsFor` gives `umbral_link` no destination, beside
the two existing private claims. The token endpoint keeps it through a refresh
(the identity is rebuilt from the principal's claims).

## Security events

| Event | When | Detail |
| --- | --- | --- |
| `user_unlinked` (new) | a removal | `kind=<kind>`, plus `;lowered=1` when the flag fell |
| `sign_in_failed` | FR-009 refusal | `link_removed;stage=authorize` or `link_removed;stage=token` |
| `user_created` | FR-006 | `reason=link_removed` |
