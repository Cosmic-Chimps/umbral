# Tasks: Trust, or not, an OpenID Connect provider's verified emails

**Input**: [plan.md](plan.md), [oidc-email-trust.spec.md](oidc-email-trust.spec.md), [data-model.md](data-model.md), [contracts/screens.md](contracts/screens.md).

Paths are under `src/Umbral/` unless they start with `tests/`, `docs/` or `changelog.d/`.

## Phase 1: Storage

- [x] **T001** `Connector.EmailTrust` and `trustsProviderEmail` (`Domains/Connectors.fs`), every record construction the compiler names given `EmailTrust = None` (or the choice, for `oidc` creations). `Database.fs`: `UnbackedEmailVerified`, step 9 rebuilt from it, the column, step 14, version 14. `ConnectorStore`: read, `create`, `saveOidc`, `previewTrustOff`. Tests: `DatabaseTests` (13 → 14 sets `1` for `oidc` rows and NULL for others; step 9's text unchanged), `OidcConnectorStoreTests` (round trip; NULL `oidc` reads off; `saveOidc` off lowers a user verified only through this connector, keeps one verified by another link, leaves another connector's links alone, returns the count; on → on and off → off lower nothing; a failure while lowering (a test trigger that aborts the users update) leaves the setting and every flag unchanged; `previewTrustOff` returns the count and changes nothing).

## Phase 2: Sign-in

- [x] **T002** `Callback.finishSignIn` clamp; `EmailDomainRules.recheck`. Tests `OidcEmailTrustSignInTests.fs` with the stub OIDC provider: off → new user stored unverified, link `asserted_email_verified = 0`, not linked to an existing verified user with the same address, refused by its own and by the instance domain rule, `email_verified` false in the `id_token` and userinfo, verified from userinfo still unverified; on → linked and admitted as today; a session admitted through a domain rule while on is refused at the next authorize after trust is turned off, and a refresh token at the token endpoint; with no rule it goes on.

## Phase 3: Screens

- [x] **T003** The add form: the group, `createOidc` reads `emailTrust` (refusal without one; refusal of an own list with `off`), stores it, `connector_created` detail. Texts. Existing tests that add an OIDC connector post a choice. Tests `OidcEmailTrustAdminTests.fs`: no choice → 422, typed values kept, secret not drawn back; own list with off → 422 under the domains field; each choice stored; log detail.
- [x] **T004** The pane: the group with the stored value and the hint; save through `saveOidc`; own-list refusal; the confirm block for on → off (`previewTrustOff`, `?confirm=trust-off`); the instance-list alert, list marker and rule line; the no-list rule line; `connector_updated` detail. Tests in `OidcEmailTrustAdminTests.fs`.

## Phase 4: Docs, captures, reviews

- [x] **T005** Docs (FR-011).
- [x] **T006** Captures in `.alta/reviews/2026-11-13-email-trust/` (contract's list) and `notes.md`.
- [ ] **T007** Full suite green; design, documentation and code reviews; one re-check each.
