# Contract: admin screens, configuration document and schema

**Feature**: `003-per-app-connectors`

Changes to the operator surfaces of
[001 admin-and-config.md](../../001-umbral-mvp/contracts/admin-and-config.md).
Every route, hook and rule listed there stays unless this file changes it. No
screen is added (FR-021).

## Routes

| Method | Path | Change |
| --- | --- | --- |
| GET | `/admin/connectors` | One card per stored connector, plus an empty card for each kind with none |
| POST | `/admin/connectors` | **New.** Add a connector of a kind with an operator-chosen slug |
| POST | `/admin/connectors/{slug}` | Unchanged path. Reads the kind from the stored row, not from the slug. Also saves the display name and the shared setting. For a kind with no connector yet, `{slug}` is the kind's name, so the first-run path is unchanged |
| POST | `/admin/connectors/{slug}/toggle` | Warns before stranding an app (FR-010) |
| POST | `/admin/connectors/{slug}/delete` | Warns before stranding an app (FR-010). Removes the slug from every chosen set (FR-011) |
| GET | `/admin/apps` | Each app row shows its connector choice |
| POST | `/admin/apps` | Accepts the connector choice on create |
| POST | `/admin/apps/{clientId}/connectors` | **New.** Change an existing app's connector choice |
| POST | `/admin/apps/{clientId}/delete` | Also deletes the app's choice rows |
| GET | `/admin/config` | Exports `shared` and `connectors` (see below) |

All of them require the operator session and an antiforgery token, as today.

## Form fields

### `POST /admin/connectors` (add)

| Field | Required | Rule |
| --- | --- | --- |
| `kind` | yes | `discord`, `github` or `google` (and `fake` where allowed) |
| `slug` | yes | `Ids.isValidSlug` (lower-case letters, digits and hyphens, up to 32 characters) and not taken. Fixed once created (FR-013) |
| `displayName` | yes | Shown on the sign-in button |
| `clientId` | yes | As on the card form |
| `clientSecret` | yes | As on the card form |
| `shared` | no | Checkbox, value `true`. Absent means not shared on this form, but the form shows it ticked by default (FR-014) |

### `POST /admin/connectors/{slug}` (card)

Today's `clientId` and `clientSecret`, plus `displayName` and `shared`. The slug
and the kind are not form fields: neither ever changes.

### `POST /admin/apps` and `POST /admin/apps/{clientId}/connectors`

| Field | Values |
| --- | --- |
| `connectorChoice` | `all-shared` or `chosen`. Absent means `all-shared` (FR-005) |
| `connectors` | Repeated, one connector slug per ticked box. Read only when `connectorChoice` is `chosen`. Zero values with `chosen` is allowed and means the app offers nothing |

An unknown slug in `connectors` is refused with notice `app-connector-unknown`.

## Warnings before stranding an app (FR-010)

A toggle off, a delete, saving a card that turns a connector from shared to
not shared, or a change of an app's chosen set first computes
`strandedApps` with the connectors as they would be after the change. If any
app is stranded and the request does not carry `?confirm=yes`, nothing changes
and the handler redirects back with a warning notice. The warning lists every
stranded app by name. The screen's button then carries `?confirm=yes`, as
today's last-connector warning does.

| Action | Notice code | Redirect target |
| --- | --- | --- |
| Disable a connector | `connector-strands-apps` | `/admin/connectors` |
| Mark a connector not shared | `connector-strands-apps` | `/admin/connectors` |
| Delete a connector | `connector-delete-strands-apps` | `/admin/connectors` |
| Change an app's choice | `app-connectors-strand` | `/admin/apps` |

The existing codes `connector-last-enabled` and `connector-delete-last-enabled`
stay for the case where no enabled connector would remain and no app is
stranded (for example, no app is registered yet). When both apply, the
`strands-apps` code is used, because it names the apps.

Which slug or app the warning is about travels in the redirect as
`?msg={code}&slug={slug}` or `?msg={code}&app={clientId}`, so the screen can
recompute the stranded apps and draw the confirming button. The redirect never
carries app names.

## Test hooks

Added to the 001 table. Tests never assert on CSS classes or on texts.

| Hook | On | Meaning |
| --- | --- | --- |
| `data-connector="{slug}"` | A connector card | Unchanged. For an empty card, the kind's name |
| `data-connector-kind="{kind}"` | That card | The provider kind, so two cards of one kind can be told apart from their slugs |
| `data-connector-shared="shared\|not-shared"` | That card | The shared setting (FR-014) |
| `data-naming-advice` | That card | The advice to name the provider's application after the Umbral instance, and why (FR-003) |
| `data-offered-nowhere` | That card | Present when the connector is not shared and no app's chosen set contains it |
| `data-add-connector` | The add form | `POST /admin/connectors` |
| `data-app-choice="all-shared\|chosen"` | An app row | The app's connector choice (FR-009) |
| `data-app-connector="{slug}"` | Inside a `chosen` app row | One per slug in its chosen set |
| `data-stranded-app="{clientId}"` | Inside a warning notice | One per app the action would strand (FR-010) |
| `data-notice="{code}"` | A flash message | Unchanged, with the codes below |

## Notice codes

New codes in `Texts.Notice`. Each names the next action (SC-010 of 001).

| Code | When |
| --- | --- |
| `connector-added` | A connector was added |
| `connector-slug-invalid` | The slug fails `Ids.isValidSlug` |
| `connector-slug-taken` | The slug belongs to another connector |
| `connector-display-name-required` | The display name is empty |
| `connector-strands-apps` | Disabling would leave the listed apps with no way to sign in |
| `connector-delete-strands-apps` | Deleting would leave the listed apps with no way to sign in |
| `app-connectors-strand` | The new chosen set leaves this app with no enabled connector |
| `app-connector-unknown` | A ticked connector no longer exists |
| `app-connectors-saved` | An app's connector choice was saved |

## The configuration document

Two optional keys. Nothing else changes, and `version` stays `1`: a document
without the keys means exactly what it meant before (FR-016).

```yaml
version: 1

connectors:
  - slug: discord
    kind: discord
    display_name: Discord
    client_id: "1234567890"
    client_secret: ${UMBRAL_DISCORD_SECRET}
    enabled: true
                                       # shared: omitted means true

  - slug: discord-frunchy              # a second connector of the same kind
    kind: discord
    display_name: Discord
    client_id: "9876543210"
    client_secret: ${UMBRAL_DISCORD_FRUNCHY_SECRET}
    enabled: true
    shared: false                      # offered only to apps that choose it

apps:
  - client_id: recipes
    name: Recipes
    client_secret: ${UMBRAL_RECIPES_SECRET}
    redirect_uris:
      - https://recipes.example.com/callback
                                       # connectors: omitted means all shared connectors

  - client_id: frunchy
    name: Frunchy
    client_secret: ${UMBRAL_FRUNCHY_SECRET}
    redirect_uris:
      - https://frunchy.example.com/callback
    connectors:                        # a chosen set; [] is a chosen set that is empty
      - discord-frunchy
```

| Key | Type | Absent means |
| --- | --- | --- |
| `connectors[].shared` | boolean | `true` |
| `apps[].connectors` | list of connector slugs | "all shared connectors". An empty list `[]` is a chosen set with nothing in it, not "all" |

In code: `ConnectorEntry.Shared` and `AppEntry.Connectors`.

### Validation

Added to the 001 rules. Still whole-document, before anything is written.

| Rule | Error |
| --- | --- |
| Every slug in `apps[i].connectors` is a slug defined under `connectors` in the same document (FR-018) | `apps[1].connectors[0] "discord-frunchy" is not a connector defined in this file. Add it under connectors, or remove it from this app.` |
| A slug appears at most once in one app's list | `apps[1].connectors "discord-frunchy" appears more than once.` |

The check sees only the document, never the database, so whether a file is valid
does not depend on the instance it is applied to.

### Apply and export

Applying sets each connector's shared setting and each app's choice to what the
document says, including back to the defaults when a key is removed. Applying
the same document twice changes nothing.

`GET /admin/config` emits `shared: false` for every connector that is not
shared, and `connectors:` for every app with a chosen set, including `[]`.
It omits both keys when they hold the default. Export then apply on a fresh
instance reproduces every choice and every shared setting (FR-017, SC-005).

## Schema

`Database.SchemaVersion` becomes `2`.

```sql
-- v1 → v2, inside the existing schema transaction, only when the stored version is 1.
ALTER TABLE connectors ADD COLUMN is_shared INTEGER NOT NULL DEFAULT 1;

CREATE TABLE IF NOT EXISTS app_connector_choices (
    client_id TEXT PRIMARY KEY            -- a row means "chosen set"; no row means "all shared"
);

CREATE TABLE IF NOT EXISTS app_connectors (
    client_id      TEXT NOT NULL REFERENCES app_connector_choices(client_id) ON DELETE CASCADE,
    connector_slug TEXT NOT NULL REFERENCES connectors(slug) ON DELETE CASCADE,
    PRIMARY KEY (client_id, connector_slug)
);
```

After the upgrade every connector is shared and no app has a choice row, so
every app offers exactly what it offered before (FR-019, SC-003). Deleting a
connector removes its `app_connectors` rows through the cascade and leaves
`app_connector_choices` alone, so the app stays limited (FR-011). Deleting an
app deletes its `app_connector_choices` row in `AppDelete`; no foreign key
points into OpenIddict's tables.

`connectors.kind` stays non-unique and `connectors.slug` stays the primary key,
which is what lets several connectors share a kind (FR-012).
