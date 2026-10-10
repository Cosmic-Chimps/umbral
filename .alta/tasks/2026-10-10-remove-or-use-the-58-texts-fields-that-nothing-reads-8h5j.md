---
title: "Remove or use the 58 Texts fields that nothing reads"
kind: improvement
status: pending
created: 2026-10-10
summary: "58 fields of the Texts record are never referenced outside Texts.fs, including a whole second set of Connector* labels that duplicate Enable/Disable/ClientIdLabel; the single-file audit of SC-010 now covers text nobody can see."
---

## Why
Found during T103 by listing every `Texts` field and grepping `.FieldName` across `src/Umbral` (outside `Views/Texts.fs`): 58 are unused (e.g. `SetupTooShort`, `SetupMismatch`, `CredentialReplace*`, `Picker*` (the notice codes carry those messages instead), `ConnectorEnableButton`, `ConnectorDeleteConfirm`, `ConnectorCredentialsRejected`, `GoogleLimitationNotice`, `OriginMismatchBanner`, `ConfigExport*`, `NotFoundTitle`/`NotFoundBody`, `CopyButton`, `BackToSignIn`). Some are waiting for work that is not written yet (the connector-delete route, the credential-replace screen FR-017a, a themed 404), so deleting blindly is wrong.

## What to do
After the connector-delete route and any other pending screens land, delete what is still unused, and make the 404 handlers use `NotFoundTitle`/`NotFoundBody` (several end-user 404s write the literal "Not found" instead). Consider a meta-test that fails on an unused field.

## Where
`src/Umbral/Views/Texts.fs`; literal 404s in `src/Umbral/Handlers/Connect/Start.fs` and `Callback.fs`, and `Program.fs` (`app.Run`).
