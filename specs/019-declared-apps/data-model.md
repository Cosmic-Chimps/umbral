# Data model: declared apps (schema 16)

## New table

```sql
CREATE TABLE IF NOT EXISTS declared_items (
    kind              TEXT NOT NULL CHECK (kind IN ('app', 'connector')),
    id                TEXT NOT NULL,      -- the client ID (= key) or the connector slug ('passkey')
    is_declared       INTEGER NOT NULL,   -- 1 while the declaration names it
    first_declared_utc TEXT NOT NULL,
    changed_utc       TEXT NOT NULL,      -- last declare or un-declare
    PRIMARY KEY (kind, id)
);
```

- No foreign key: apps live in OpenIddict's tables, and the row is history
  (D5: "declared once" must outlive an un-declare). Deleting a declared app is
  refused while declared; an un-declared app deleted in the admin area deletes
  its row (`AppDelete`), so a later app made in the admin area with that client
  ID cannot be confused with it. Connector delete likewise.
- Step 16 creates the table; it rewrites nothing. `SchemaVersion = 16`.

## Reads

| Question | Rule |
| --- | --- |
| Is this app owned by the declaration? | a row `('app', clientId)` with `is_declared = 1` |
| May the declaration take this client ID? | no OpenIddict app with it, or a row `('app', clientId)` (any `is_declared`) |
| May the declaration take the passkey connector? | no `passkey` connector, or a row `('connector', 'passkey')`, or (C2, adopt) it is enabled, shared and has the declared enrolment |
| What to un-declare? | rows with `is_declared = 1` not named by the declaration (all of them when there is none) |

## The comparison

A declared app is "unchanged" when all of these are equal to the stored ones:
display name, client type (`web` = confidential), redirect URIs and
post-logout redirect URIs (as ordered lists of the stored, normalised form),
the connector choice (`AppConnectors.choiceFor`), and, for `web`, the secret
validates against the stored hash. The passkey connector is unchanged when it
exists, is enabled and shared, and its enrolment is the declared one.
