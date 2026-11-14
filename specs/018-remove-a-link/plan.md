# Implementation Plan: Remove one linked provider account

**Spec**: [remove-a-link.spec.md](remove-a-link.spec.md) · **Data model**: [data-model.md](data-model.md) · **Contract**: [contracts/screens.md](contracts/screens.md)

## Summary

One table (schema 15) records the provider accounts the operator removed. A
removal deletes the link in a guarded statement, records the account and lowers
the user's flag by the h762 rule, in one transaction. `Callback.finishSignIn`
treats a recorded account as unverified, beside spec 017's trust clamp, so it
never joins anyone again. Provider sessions and the authorizations made from
them carry a fingerprint of the provider account; the authorize and token
endpoints refuse one whose link is gone. The Users card lists the links with
their dates and a remove button.

## Technical Context

F# on .NET 10, Oxpecker, SQLite, xUnit with the in-process test server and the
stub providers. No new package (`System.Security.Cryptography.SHA256`).

## Constitution Check

| Gate | Assessment |
| --- | --- |
| I. Three concepts | PASS. A link is part of a User; no new concept on screen. |
| II. One artifact | PASS. One table, schema 15. |
| III. Standards | PASS. No protocol change; a refused refresh is `invalid_grant`. |
| IV. Secure by default | PASS. Removal only ever lowers trust; a removed account is distrusted for good; last way in refused in the statement; operator session and antiforgery; every removal logged without subject or address. |
| VI. Documentation | PASS. oidc.md, integrate.md, backup.md schema 15, changelog with an Upgrade note. |
| VII. Default answer is no | PASS. Decides who is recognised as whom; nothing about permissions. No user-facing account management. |

## Structure

| File | Change |
| --- | --- |
| `Domains/Linking.fs` | `fingerprint`, `CredentialReference`, `credentialValue`, `parseCredential` |
| `Infrastructure/Database.fs` | `removed_links` and `passkeys.added_via` in `schema`, step 15, `SchemaVersion = 15` |
| `Infrastructure/UserStore.fs` | `LinkedAccount`, `linkedAccountDetails`, `RemoveLinkOutcome`, `removeLink` (with the transitive passkey removal), `isRemoved`, `credentialStillHeld` |
| `Infrastructure/SecurityEvents.fs` | `UserUnlinked` / `user_unlinked` in the union, `Value` and `allEventTypes`; `credentialRemovedDetail stage` |
| `Infrastructure/Sessions.fs` | `CredentialClaim`, `signIn` gains `credential`, `currentCredential` |
| `Infrastructure/OpenIddictSetup.fs` | `CredentialClaim = "umbral_credential"` |
| `Infrastructure/Passkeys.fs` | `Passkey.AddedVia`, read and written (`insertIn`) |
| `Handlers/Connect/Callback.fs` | `finishSignIn`: the removed-account clamp, `user_created` detail, the session's credential |
| `Handlers/Passkeys/Ceremonies.fs` | the passkey's reference in `Sessions.signIn`; `added_via` for purpose `Add` |
| `Handlers/Connect/Authorize.fs` | `buildIdentity` copies the credential; `destinationsFor`; the FR-009 re-check |
| `Handlers/Connect/Token.fs` | the FR-009 re-check |
| `Infrastructure/EndUserSession.fs` (new) | FR-012: `recheck` and `current`; used by Authorize, `Handlers/Passkeys/Account.fs` and the `Add` ceremony |
| `Handlers/Admin/Users.fs` | the list on the card; `Row.Links` |
| `Handlers/Admin/UserLinks.fs` (new) | the POST handler; in the fsproj after `UserDelete.fs`, the endpoint in `Program.fs` |
| `Views/Texts.fs` | the contract's words, the three notices |
| tests | `DatabaseTests` (14 → 15), `LinkingTests` (fingerprint), `UserLinkRemoveTests.fs` (new: store, screen, handler, sign-in and re-check journeys), `SecurityEventCoverageTests` if it lists every type |
| docs | `docs/connectors/oidc.md`, `docs/integrate.md`, `docs/run/backup.md`, `changelog.d/018-remove-a-link.md` |
