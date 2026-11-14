# Data model: Remove one linked provider account

## Storage

Schema version 15: a new table and a new column. Nothing existing is rewritten.

```sql
CREATE TABLE IF NOT EXISTS removed_links (
    provider_kind    TEXT NOT NULL,
    provider_subject TEXT NOT NULL,
    removed_utc      TEXT NOT NULL,
    PRIMARY KEY (provider_kind, provider_subject)
);

-- in CREATE TABLE passkeys, and as an ALTER in step 15:
added_via TEXT NULL
```

In `Database.schema` (fresh databases) and as upgrade step 15 (one literal,
the CREATE and `ALTER TABLE passkeys ADD COLUMN added_via TEXT NULL;`);
`SchemaVersion = 15`. Step 15 runs only when `passkeys` has no `added_via`
yet: a database from before version 8 gets `passkeys` from `schema` itself,
which runs before the steps and already has the column (and `removed_links`),
and SQLite has no `ADD COLUMN IF NOT EXISTS`.

`removed_links` has no `user_id` and no foreign key, on purpose (spec decision
1): the record must outlive the user the link was removed from. The subject is
the same value `linked_accounts.provider_subject` held (for `oidc`,
`issuer#sub`).

`passkeys.added_via` is the credential reference (below) of the session that
added the passkey on `/account/passkeys` (purpose `Add`). NULL for an
invitation, a sign-up, and every row from before this version.

## The fingerprint and the credential reference (pure, `Domains/Linking.fs`)

```fsharp
/// FR-008. Opaque (not secret) name of one provider account.
let fingerprint (kind: string) (subject: string) : string

/// FR-007. What a session and an authorization carry.
type CredentialReference =
    | LinkCredential of fingerprint: string
    | PasskeyCredential of passkeyId: string

let credentialValue : CredentialReference -> string   // "link:<fp>" | "passkey:<id>"
let parseCredential : string -> CredentialReference option  // None for anything else
```

The fingerprint is `SHA256(UTF8(kind + "\n" + subject))`, base64url, no
padding (43 characters). Never parsed back: a holder finds the account by
computing the fingerprint of each of the user's links (a user has a handful).
The passkey id is the row's own random id (`passkeys.id`, already in the
card's form actions), never the credential id.

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
    | LinkRemoved of kind: string * subject: string * lowered: bool * passkeys: int
    | LinkLastWayIn
    | LinkNotFound

/// FR-003, one transaction.
let removeLink (config) (clock: TimeProvider) (userId) (fingerprint) : Task<RemoveLinkOutcome>

/// FR-006. Whether this provider account was removed from a user before.
let isRemoved (config) (kind: string) (subject: string) : Task<bool>

/// FR-009. True when the session or token may go on, false when refused.
let credentialStillHeld
    (config) (userId: string) (connectorSlug: string option) (credential: string option) : Task<bool>
```

`removeLink`, inside one transaction:

1. Read the user's links; find the one whose fingerprint matches. None:
   roll back, `LinkNotFound`.
2. Delete the passkeys added through it, transitively, and count them:

   ```sql
   WITH RECURSIVE gone(id) AS (
       SELECT id FROM passkeys WHERE user_id = @userId AND added_via = @linkRef
       UNION
       SELECT p.id FROM passkeys p JOIN gone g ON p.added_via = 'passkey:' || g.id
        WHERE p.user_id = @userId)
   DELETE FROM passkeys WHERE id IN (SELECT id FROM gone)
   ```

   with `@linkRef = "link:" + fingerprint`.
3. `DELETE FROM linked_accounts WHERE provider_kind = @kind AND
   provider_subject = @subject AND user_id = @userId AND ((SELECT count(*) FROM
   linked_accounts WHERE user_id = @userId) > 1 OR EXISTS (SELECT 1 FROM
   passkeys WHERE user_id = @userId))`. Run after step 2, so it counts only the
   passkeys that remain. Zero rows: roll back (the passkeys of step 2 too),
   `LinkLastWayIn`.
4. `INSERT INTO removed_links (...) VALUES (...) ON CONFLICT DO NOTHING`.
5. `UPDATE users SET email_verified = 0 WHERE id = @userId AND " +
   Database.UnbackedEmailVerified`; `lowered` = rows changed > 0.
6. Commit, `LinkRemoved`.

`credentialStillHeld`:

| Session or token | Rule |
| --- | --- |
| No connector slug | true (the connector re-checks own this case) |
| `link:<fp>` | true when one of the user's links has that fingerprint |
| `passkey:<id>` | true when a passkey with that row id belongs to that user (`id = @id AND user_id = @userId`, never the id alone) |
| A value that does not parse | false (fails closed) |
| No reference, connector is the passkey kind (or not found) | true (the kind re-check refuses a missing connector first) |
| No reference, `oidc` connector | true when the user has an `oidc` link whose subject starts with the connector's issuer + `#` (false with no issuer) |
| No reference, other kind | true when the user has a link of the connector's kind |

For the confirmation (FR-002), the screen needs, per link, the count of
passkeys step 2 would take and the user's passkeys with NULL `added_via`
created after the link's `LinkedUtc`. The screen computes both from the user's
passkeys (`Passkey.AddedVia`, read from the new column) and the link's
reference; no extra query.

## The end-user session check (FR-012, `Infrastructure/EndUserSession.fs`)

Compiled after `UserStore.fs`, `ConnectorStore.fs` and `SecurityEvents.fs`.

```fsharp
/// The connector-kind re-check (s1ug), then credentialStillHeld. None admits;
/// Some detail is the sign_in_failed detail of the refusal.
let recheck (config) (principal: ClaimsPrincipal) (stage: SecurityEvents.RefusalStage) : Task<string option>

/// For the account pages and the Add ceremony: the session's enabled user, or
/// None. A missing session is None with nothing done; a user gone or disabled,
/// or a refusal by `recheck`, signs the session out (the refusal also records
/// sign_in_failed) and is None.
let current (ctx: HttpContext) : Task<Users.User option>
```

`RefusalStage` gains `Account` (`stage=account`) for the passkey pages.
Authorize calls `recheck` where it calls `ConnectorStore.recheckKind` today
(its refusal path and its event stay its own). `Account.currentUser` becomes
`EndUserSession.current` plus its redirect; the Add start uses `current`,
and the Add finish uses `current` and requires the user to be the
ceremony's.

## Claims

| Where | Name | Value |
| --- | --- | --- |
| End-user session cookie | `credential` (`Sessions.CredentialClaim`) | the credential reference |
| Authorization identity (codes, refresh tokens) | `umbral_credential` (`OpenIddictSetup.CredentialClaim`) | copied from the session; no destination |

`Sessions.signIn` takes `credential: string option` as a last argument:
`Callback.finishSignIn` passes the link's reference, the passkey ceremonies the
passkey's (the one used to sign in, or the one just created at a sign-up or an
invitation). `Authorize.buildIdentity` copies `Sessions.currentCredential` into
the identity; `destinationsFor` gives `umbral_credential` no destination,
beside the two existing private claims. The token endpoint keeps it through a
refresh (the identity is rebuilt from the principal's claims).

`Passkeys.insertIn` writes `added_via`; the ceremony of purpose `Add` passes the
session's `Sessions.currentCredential`, every other purpose `None`.

## Security events

| Event | When | Detail |
| --- | --- | --- |
| `user_unlinked` (new) | a removal | `kind=<kind>;passkeys=<n>`, plus `;lowered=1` when the flag fell |
| `sign_in_failed` | FR-009 refusal | `credential_removed;stage=authorize` or `credential_removed;stage=token` |
| `user_created` | FR-006 | `reason=link_removed` |