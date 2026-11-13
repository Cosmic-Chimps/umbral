# Task tr5t, spec 017: trust, or not, an OpenID Connect provider's verified emails

Branch `alta/chipper-duet-8b42`, rebased onto main `8a1d88c` (no f0rg reproduction in its
history). Range `8a1d88c..eb9b894`:

| Commit | What |
| --- | --- |
| 95e55ff | spec, data model, plan, contract, tasks (`specs/017-oidc-email-trust/`) |
| e0b840c | docs: `docs/connectors/oidc.md`, `docs/who-can-sign-in.md`, `docs/run/backup.md`, `changelog.d/017-oidc-email-trust.md` |
| 37663bf | T001 storage: `Connector.EmailTrust`, `trustsProviderEmail`, schema 14, `saveOidc`, `previewTrustOff`, `Database.UnbackedEmailVerified` |
| a5b5e22 | T002 sign-in: `Callback.finishSignIn` clamp, `EmailDomainRules.recheck` |
| 2cc3473 | T003 add form |
| aa975e1 | T004a pane, refusals, instance-list warnings, log |
| eb9b894 | T004b confirm before trust off |

## Where "off" is enforced

- `Callback.finishSignIn` (first lines): the profile's `EmailVerified` becomes false for a connector
  that does not trust its provider, before the domain rule, `UserStore.resolve` (linking, the users
  row, the link's `asserted_email_verified`) and the session's admitted domain. Every provider's
  answer passes there.
- `EmailDomainRules.recheck`: a remembered sign-in (session, code, refresh token) through such a
  connector is judged with no verified domain: refused while a rule applies, logged
  `email_not_verified`.
- `ConnectorStore.saveOidc`: on → off (or a NULL row saved off) lowers, in the same transaction,
  this issuer's links and then the users the h762 rule finds unbacked. A trigger test shows a
  failure there leaves the setting unchanged.
- Apps get `email_verified` from the stored user, so the id_token and userinfo follow.

## Decisions taken with the `umbral` session and the designer

See the spec's Clarifications and `contracts/screens.md`. Two small deviations recorded in the
data model: the `lowered=<n>` part is logged whenever users were lowered (a NULL row saved off),
and the n = 1 confirm sentence is singular.

## Known limits

- A save that unshares the connector AND turns trust off goes through the strand warning, whose
  confirmation posts no `emailTrust`: the trust change is dropped (the warning says nothing was
  saved). The operator saves again.
- The add form draws the trust group for Discord, GitHub and Google too (captured as
  `add-discord-1280-light.png`); it is required and stored only for OpenID Connect. For the designer.

## After the reviews (2026-11-14)

| Item | What was done |
| --- | --- |
| Design blocking 1 | The add form's lead starts "Only for an OpenID Connect provider."; the pane's lead is unchanged. |
| Design nit 2 | `.um-choice-lead`: the lead sits 6px under the legend and 12px above the cards (`umbral.input.css`, CSS rebuilt). |
| Docs B1 | oidc.md "Changing the email setting later": turning trust off does not undo a link already made; how to look for one on the Users screen; Disable stops both people, Delete gives each an account of their own. The changelog's Security bullet points there. |
| Docs nits 1, 2 | "signed in through this provider under a rule"; created events carry `email_trust=on|off` without `lowered`. |
| Code N1 | `finishSignIn` reads the trust again by slug before the clamp (trusted only when both reads say so). Test `trust turned off during the code exchange is honoured by that sign-in`, which fails without the re-read. |
| Code N2 | The confirm line says a typed secret was not saved. |
| Code N3 | Left as the known limit above. |

Full suite at `02aabcc`: 2826 of 2826 passed (Debug, 17 min 24 s). Re-checks: design 887bfdc, documentation f7b833f, code 391af60, all clear to merge.

## Tests

`OidcEmailTrustSignInTests.fs`, `OidcEmailTrustAdminTests.fs`, `OidcConnectorStoreTests.fs`,
`DatabaseTests.fs`. Full suite at `eb9b894`: 2825 of 2825 passed (Debug, 31 min 48 s).

Captures: `captures.md`.
