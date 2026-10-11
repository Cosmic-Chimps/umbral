# Data Model: Remove the Configuration Document

This feature adds no entity and no table. It removes one column, one setting and
one event type, and it retires the configuration document as an entity.

## Connector (`connectors` table)

| Column | Change |
| --- | --- |
| `extra_scopes` | **Removed** from the `CREATE TABLE` statement and from every read and write. |

The domain record `Connector` loses `ExtraScopes`. `ScopeString` becomes the
kind's required scopes, which are unchanged:

| Kind | Scopes requested |
| --- | --- |
| Discord, GitHub, Google, Fake | `Endpoints.Scopes` for the kind, unchanged from 001 |

**Upgrade**: none. A development database created before this feature keeps
the column. It is nullable and nothing reads it (research R4). If a release is
published before this feature lands, add an upgrade step that drops it.

## Settings (`settings` table)

| Key | Change |
| --- | --- |
| `operator_credential_file_hash` | **No longer written or read.** It recorded the last hash the file applied. A development row is left alone. |
| `operator_credential`, `operator_credential_set_utc`, `operator_session_stamp`, `schema_version` | Unchanged. |

## Security event types

| Type | Change |
| --- | --- |
| `config_applied` | **Removed** from the event type union and from `allEventTypes`. Nothing writes it. |
| `connector_created`, `connector_updated`, `app_created`, `app_updated`, `app_secret_rotated`, `operator_credential_changed` | Unchanged, but never again written with `detail = "source=config_file"`. |

`security_events.event_type` is plain text and nothing parses it back into the
union, so development rows holding `config_applied` stay readable.

## Configuration document

**Retired.** It was the declarative description of every app and connector,
plus the operator credential hash. It has no successor: the database in the
data directory is the only state (spec, "What the file does today").

## OpenIddict applications

Unchanged. Apps created from the file in development are ordinary OpenIddict
rows and stay so.
