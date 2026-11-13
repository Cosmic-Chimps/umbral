# Implementation Plan: Trust, or not, an OpenID Connect provider's verified emails

**Spec**: [oidc-email-trust.spec.md](oidc-email-trust.spec.md) · **Data model**: [data-model.md](data-model.md) · **Contract**: [contracts/screens.md](contracts/screens.md)

## Summary

One column on `connectors` (schema 14), read as `Connector.EmailTrust`. Applied
once, in `Callback.finishSignIn`, where every provider's answer passes before
the domain rule, linking, the users row, the link and the session. The re-check
of remembered sign-ins ignores the remembered domain of a connector that does
not trust its emails. Turning trust off lowers, in the save's own transaction,
the flags only this provider earned (the h762 rule). The add form and the pane
gain the group of two cards; the screens refuse an own domain list with trust
off, warn about the instance list, and confirm before turning trust off.

## Technical Context

F# on .NET 10, Oxpecker, SQLite, xUnit with the in-process test server and the
stub OIDC provider. No new package.

## Constitution Check

| Gate | Assessment |
| --- | --- |
| I. Three concepts | PASS. A setting of a Connector. |
| II. One artifact | PASS. One column, schema 14. |
| III. Standards | PASS. `email_verified` is still read as OIDC Core says; the operator decides whether to believe it. |
| IV. Secure by default | PASS. A new connector has no default: the operator must choose. NULL reads as off. Off can only lower a flag. Every change logged. |
| VI. Documentation | PASS. oidc.md, who-can-sign-in.md, backup page's schema version, changelog with an Upgrade note. |
| VII. Default answer is no | PASS. Decides who is recognised as whom and who may sign in; nothing about what anyone may do. |

## Structure

| File | Change |
| --- | --- |
| `Domains/Connectors.fs` | `Connector.EmailTrust`, `trustsProviderEmail` |
| `Infrastructure/Database.fs` | `UnbackedEmailVerified` literal (step 9 rebuilt from it, same text), column in `CREATE TABLE`, step 14, `SchemaVersion = 14` |
| `Infrastructure/ConnectorStore.fs` | read and `create` the column; `saveOidc`; `previewTrustOff` (same statements, rolled back) |
| `Handlers/Connect/Callback.fs` | `finishSignIn` clamps the profile |
| `Infrastructure/EmailDomainRules.fs` | `recheck` ignores the remembered domain |
| `Handlers/Admin/Connectors.fs` | the group in the add form and the pane; `createOidc` and the save path; refusals; the confirm block; the instance-list alert, list marker and rule line; log details |
| `Views/Texts.fs` | the contract's words |
| tests | `DatabaseTests`, `OidcConnectorStoreTests`, `OidcEmailTrustSignInTests` (new), `OidcEmailTrustAdminTests` (new), existing OIDC admin tests given a choice |
| docs | `docs/connectors/oidc.md`, `docs/who-can-sign-in.md`, `docs/run/backup.md` (schema 14), `changelog.d/017-oidc-email-trust.md` |
