# Contract: admin screens, notices and security events

**Feature**: `005-external-secret-stores` | FR-007, FR-010, FR-015, FR-020 to FR-022

Changes to the Connectors screen of
[003 admin-and-config.md](../../003-per-app-connectors/contracts/admin-and-config.md).
No route, form field, screen or top-level section is added (FR-020). The
Connectors form offers no choice of store (FR-003).

## Routes

| Method | Path | Change |
| --- | --- | --- |
| GET | `/admin/connectors` | Card label, card warning, page notice (below) |
| POST | `/admin/connectors` | Secret written to the active store before the row; refused with a notice on store failure |
| POST | `/admin/connectors/{slug}` | Same, for first save and for replacing a secret |
| POST | `/admin/connectors/{slug}/delete` | Row deleted first; a store refusal no longer causes a 500 |

## Page markers (for tests)

| Marker | Where | Meaning |
| --- | --- | --- |
| `data-secret-store="<id>"` | each connector card | Store id from the reference: `local`, `aws`, `gcp`, `azure`, `bellabaxter` |
| `data-secret-unreadable` | a card, on its `um-alert-warning` note | The reference names a store this instance cannot build (FR-021) |
| `data-secret-store-problem` | page notice next to the origin banner | The startup probe or the latest call to the active store failed (Story 4 sc. 3) |

The label text is `Texts`-owned: "Kept in this instance (encrypted)", "Kept in
AWS Secrets Manager", "Kept in Google Cloud Secret Manager", "Kept in Azure Key
Vault", "Kept in BellaBaxter".

## Notices

Notice codes are added to `noticeEn` and `knownNotice` (`Texts.fs`). No text is
taken from the URL; a notice that needs a value (a store name, a secret's name)
is rendered from the code plus server-side state, as existing notices do.

| Code | After | Text says |
| --- | --- | --- |
| `connector-secret-store-failed` | A create or replace whose write to the store failed. Nothing was saved. | Which store, the cause (could not reach / access denied / rejected), and what to check: credentials, permissions per the walkthrough, network. The secret must be entered again. |
| `connector-secret-left-behind` | A delete whose store delete failed. The connector is gone. | The secret's name in the manager and that it should be removed there by hand. |
| `connector-secret-replaced-left-behind` | A replace that moved the secret to the active store and could not remove the old copy from the other one. The connector is saved. | The connector was saved, and the old copy's name and store, to be removed by hand. |
| `connector-secret-busy` | A replace or delete refused because a move holds the connector's lease. Nothing was saved. | "A secret move is working on this connector. Try again in a minute." |

The text of a store failure is chosen by the failure, not only by its cause
word: a `Rejected` with a closed reason the product knows (`soft_deleted` — a
secret deleted in the console and still recoverable; `provider_ambiguous` —
names `UMBRAL__BELLABAXTER_PROVIDER`; `location_not_found` — names
`BELLA_BAXTER_PROJECT`, `BELLA_BAXTER_ENV` and `UMBRAL__BELLABAXTER_PROVIDER`)
says what to do about it, in the startup warning, the page notice and the
refusal alike. Any other `Rejected` reason is never shown.

The OpenID Connect add form keeps its re-render-with-input behaviour and shows
the first text inline. The other add path and the card save redirect with the
code, as they do for their other refusals (research R9).

## Security events

Two new kinds, stored values `secret_store_failed` and
`connector_secret_moved`; `Detail` formats in
[data-model.md](../data-model.md#security_events-existing-table-new-kinds).

| Path | Events |
| --- | --- |
| Create | `ConnectorCreated` with `Detail = store=<id>` of the store holding the secret (FR-022). |
| Replace | `ConnectorUpdated`, with `Detail = store=<id>` when the secret changed. |
| Delete | `ConnectorDeleted` with `Detail = store=<id>` of the store that held the secret. |
| Create, store write fails | `secret_store_failed` (`op=put`), actor Operator. No `ConnectorCreated`. |
| Replace, store write fails | `secret_store_failed` (`op=put`). No `ConnectorUpdated`. |
| Delete, store delete fails | `ConnectorDeleted` (Success, as today) **and** `secret_store_failed` (`op=delete`). |
| Sign-in, secret unreadable | `secret_store_failed` (`op=get`) actor User, and `SignInFailed` with `connector_secret_unreadable;store=;cause=`. |
| Sign-in served from the grace period | `secret_store_failed` (`op=get;…;stale=true`), at most one per entry per 30 s; the sign-in itself records nothing new. |
| Startup probe fails | `secret_store_failed` (`op=probe`), actor System. |
| Move command switches a reference | `connector_secret_moved`, actor System, in the same transaction as the switch. |
| Move command fails on a connector | `secret_store_failed` with the failing `op`, actor System. |

## End users

Unchanged. A sign-in that cannot read its secret ends on the existing
`sign-in-failed` page with its code; the cause is only in the log (FR-015).
