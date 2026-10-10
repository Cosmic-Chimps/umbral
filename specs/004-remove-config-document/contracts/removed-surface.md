# Contract: What Stops Existing

Tests and documentation check against this list. After this feature, nothing
here is accepted, served, printed or documented. The only exceptions are the
superseded notes in older specs and the constitution's record of the
amendment.

## HTTP

| Route | Before | After |
| --- | --- | --- |
| `GET /admin/config` | Operator-only download of `umbral.yaml` | `404`, body `Not found`, signed in or not (catch-all, FR-002) |

No admin page links to an export. None did before either; the README gave the
address.

## Process

| Surface | Before | After |
| --- | --- | --- |
| `Umbral hash-credential` | Read a credential on stdin, printed its hash | Removed (FR-003). The argument is no longer special. |
| `UMBRAL__CONFIG_FILE` / `UMBRAL:CONFIG_FILE` | Path of the file | Not read (FR-004) |
| `<data directory>/umbral.yaml` | Read and applied at every start | Never opened, modified or deleted (FR-001) |
| Environment references from the file (`${UMBRAL_*_SECRET}`, `UMBRAL_OPERATOR_HASH`) | Resolved while applying the file | Never read |
| Startup failure on a bad file | Process exits, naming the entry | Cannot happen |

## Unchanged, and relied on

| Surface | Contract |
| --- | --- |
| `Umbral reset-credential` | Replaces the credential, or claims an unclaimed instance, and prints a generated credential once. The documented way to claim an instance unattended (FR-010). |
| `Umbral healthcheck` | Unchanged. |
| First-run banner and `/admin/setup` | Unchanged (spec User Story 2, scenario 1). |
| `UMBRAL__PUBLIC_ORIGIN`, `UMBRAL__DATA_DIRECTORY` | Unchanged. The only environment settings left in the README's settings reference, plus the development-only `UMBRAL__INSECURE_ALLOW_HTTP`. |

## Upstream authorization request

The `scope` parameter Umbral sends to a provider is exactly the kind's required
set, with nothing appended (FR-007).

## YAML keys

All of them are gone: `version`, `connectors[]` (`slug`, `kind`,
`display_name`, `client_id`, `client_secret`, `enabled`, `extra_scopes`),
`apps[]` (`client_id`, `name`, `client_secret`, `redirect_uris`,
`post_logout_redirect_uris`), `operator.credential_hash`.
