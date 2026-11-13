# Data model: Trust in an OpenID Connect provider's verified emails

## Storage

Schema version 14: `connectors.email_trust INTEGER NULL`.

| Kind | Value |
| --- | --- |
| `oidc` | `1` (trust on) or `0` (trust off). NULL reads as off. |
| every other kind | NULL, ignored. |

Upgrade step 14 (one literal, two statements):

```sql
ALTER TABLE connectors ADD COLUMN email_trust INTEGER NULL;
UPDATE connectors SET email_trust = 1 WHERE kind = 'oidc';
```

A fresh database gets the column from `CREATE TABLE`; every `oidc` row it ever
has is written by `ConnectorStore.create` with the operator's choice.

## The connector

`Connector.EmailTrust: bool option`: `Some true` / `Some false` for a row of kind
`oidc` with a value, `None` otherwise.

`Connectors.trustsProviderEmail (connector: Connector) : bool` (pure, in
`Domains/Connectors.fs`):

| Kind | Result |
| --- | --- |
| `Oidc` | `connector.EmailTrust = Some true` (None is off) |
| any other | `true` (each keeps its own rule for `email_verified`) |

## Applying it

`Callback.finishSignIn`, first thing: when `not (trustsProviderEmail connector)`,
the profile becomes `{ profile with EmailVerified = false }`. Everything after
(domain rule, `UserStore.resolve`, the session's admitted domain) sees that
profile.

`EmailDomainRules.recheck`: for a connector found by slug that does not trust its
provider's emails, the remembered domain is ignored: `checkDomain rule None`
(refused with the level of the rule while one applies, admitted when none does).

## Saving an OIDC connector

`ConnectorStore.saveOidc config clock slug displayName clientId isShared allowedDomains newSecretRef (trust: bool) : Task<int option>`
saves what the OIDC pane edits (the fields `update` writes, and the setting).
None: no `oidc` connector has that slug. Some n: saved, `n` users lowered. One
transaction:

1. Read the stored `email_trust` and `issuer_url` of the `oidc` row (none: None).
2. The `update` statement's columns, plus `email_trust = @trust`, `WHERE slug = @slug AND kind = 'oidc'`.
3. Only when `trust` is false and the stored value was not `0` (on, or NULL):
   - `UPDATE linked_accounts SET asserted_email_verified = 0 WHERE provider_kind = 'oidc' AND substr(provider_subject, 1, length(@prefix)) = @prefix`, with `@prefix` = `<issuer>#`;
   - `UPDATE users SET email_verified = 0 WHERE <h762 rule> AND id IN (SELECT user_id FROM linked_accounts WHERE provider_kind = 'oidc' AND substr(provider_subject, 1, length(@prefix)) = @prefix)`; its count is `n`.
4. Commit. Any failure rolls back everything, the setting included.

`n` is 0 when nothing was lowered. The handler logs the setting's part only when
the stored value (as `trustsProviderEmail` reads it) differs from the saved one, and the `lowered` part whenever users were lowered (a NULL row saved off).

The h762 rule is one literal, `UnbackedEmailVerified` in `Database.fs`:
`email_verified = 1 AND NOT EXISTS (SELECT 1 FROM linked_accounts la WHERE la.user_id = users.id AND la.asserted_email_verified = 1 AND la.asserted_email = users.email COLLATE NOCASE)`.
Schema step 9 is rebuilt from it with the same text, and `saveOidc` uses it,
so the two cannot drift.

## Log

| Event | Detail part |
| --- | --- |
| `connector_created` (kind `oidc`) | `email_trust=on` / `email_trust=off` |
| `connector_updated`, setting changed to on | `email_trust=on` |
| `connector_updated`, setting changed to off | `email_trust=off;lowered=<n>` |

Joined with `;` to the other parts of the same event (`store=<id>`). No part is
written when the setting did not change.