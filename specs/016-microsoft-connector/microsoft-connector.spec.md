# Feature Specification: Microsoft accounts (personal, work and school)

**Feature Branch**: `alta/warm-monsoon-8gdd` (worktree branch; spec directory `specs/016-microsoft-connector`)

**Created**: 2026-11-12

**Status**: Implemented; design, documentation and code reviews and re-checks clear on 2026-11-13; not merged

**Input**: Task `2026-11-11-4-connector-microsoft-accounts-m5ft`: "4. Connector: Microsoft accounts (personal, work and school)". The fourth of five; task 3 (Bluesky, spec 015) merged before it started.

## Background

After Google and GitHub, Microsoft is the provider people ask for. The generic
OpenID Connect connector (specs/002) already signs in the people of **one**
organization's Microsoft Entra tenant, whose issuer is fixed. It cannot sign in
personal Microsoft accounts (outlook.com, Xbox, Skype) or "any work or school
account": those go through Microsoft's shared endpoints, whose discovery documents
name an issuer other than their own address, and a strict client refuses them.
Step 1 confirmed it with Umbral's own discovery check
(`.alta/reviews/2026-11-11-microsoft/notes.md`, research R1).

## Decisions Already Taken

Decided in the task; not open:

- **Model (Principle I).** Microsoft is a Connector kind, built in. No new noun.
- **Who may sign in** is the operator's choice: personal accounts only, one
  organization (a tenant ID), any work or school account, or both. It is a rule
  about who can be a user (Principle VII), nothing about what they may do.
- **Issuer validation without loosening it**: the token's `tid` builds the
  expected issuer from the template, compared exactly with `iss`; then `tid` is
  judged by the choice. Signature against the discovery document's keys;
  audience, `nonce` and expiry as usual.
- **Email is never verified on Microsoft's word alone** (nOAuth; the 0.8.0 fix
  h762 was about this claim). Raised only on a signal Microsoft documents for the
  purpose.
- **Settings**: the client ID, the client secret in the secret store, the choice.
- **Tests** use a fake Microsoft, never real credentials.

## Clarifications

### Session 2026-11-12

Unattended run; decided by the implementing agent, with the screens settled by
`umbral-designer` (recorded in [contracts/routes-and-screens.md](contracts/routes-and-screens.md)).

- Q: How many Microsoft connectors? → A: One per instance, slug `microsoft`, with
  a pane of its own, like Apple. The "both" choice covers the case of wanting two.
- Q: Which identifier keys a person? → A: `tid` and `oid`, not `sub` (research
  R4): `sub` is pairwise per app registration, so replacing the registration would
  quietly make every person a new user.
- Q: Which signal raises `email_verified`? → A: The optional claim `xms_edov`
  ("email domain owner verified") as the JSON literal `true`, with an `email`
  present. Nothing else, personal accounts included (research R5).
- Q: How is the tenant given? → A: As a GUID only, the Directory (tenant) ID. The
  personal-accounts tenant is refused there, with a pointer to the first choice.
- Q: Which choice is preselected? → A: None (designer's answer 3a): a wrong
  default would fail at sign-in, far from the form.
- Q: Does narrowing the choice reach people already signed in? → A: Yes, at their
  next authorization, like the Fediverse and Bluesky server lists.
- Q: The button's mark? → A: The link icon, as for Discord (research R8). The
  title is the display name, default "Microsoft".
- Q: What happens to the generic connector's Entra advice? → A: One
  organization's tenant can stay on the generic connector; the OIDC walkthrough
  says when to choose which.

## User Scenarios & Testing

### User Story 1: Personal Microsoft accounts (Priority: P1)

An operator wants friends and family to sign in with their outlook.com accounts.
They register an app in the Microsoft Entra admin center for "Personal accounts
only", paste the client ID and secret into the Microsoft pane, choose "Personal
Microsoft accounts" and enable it.

**Acceptance**:
1. A person with a personal account signs in and becomes a user whose identity is
   the personal tenant and their `oid`.
2. A work account's token (any other `tid`) is refused as an account not allowed.
3. The person's email is stored unverified unless the token has `xms_edov: true`.

### User Story 2: Work or school accounts (Priority: P1)

An operator chooses "Any work or school account", or "one organization" with its
tenant ID.

**Acceptance**:
1. Under "any", a token from any tenant but the personal one is accepted; a
   personal account is refused.
2. Under "one organization", only that tenant's tokens are accepted.
3. A token whose `iss` names a tenant other than its own `tid` is refused, under
   every choice.

### User Story 3: Both (Priority: P2)

"Personal and any work or school account" admits every tenant, with the same
validation.

### User Story 4: Changing the choice (Priority: P2)

The operator narrows "both" to "personal". At their next authorization, a person
signed in with a work account is signed out and refused; a personal account keeps
working. Widening it again lets the same work account back in as the same user.

### Edge Cases

- `tid` missing, not a GUID, upper case, or with extra characters: refused.
- The signing key has no `issuer`, or its `issuer` does not give the token's `iss`
  after substitution (a personal-account key used for a work account): refused.
- An unknown `kid`: one throttled refresh of the keys, then refused.
- `oid` missing: refused.
- `xms_edov` absent, `false`, `"true"`, `1`, or `true` without an `email`: not
  verified.
- A tenant ID that names no tenant: Save keeps the connector disabled with
  Microsoft's answer as the reason.
- An expired client secret: the token endpoint refuses; the person sees
  `sign-in-failed` and the log has `rejected: invalid_client`.
- A `microsoft` row from a database with no audience (NULL): cannot be enabled.

## Requirements

### Functional Requirements

- **FR-001** A built-in kind `microsoft`, at most one connector, slug `microsoft`,
  shown on the Connectors list after Apple, not in the add form.
- **FR-002** Its pane saves: display name, Application (client) ID (a GUID), client
  secret (secret store; required on a new connector, kept when left blank), who may
  sign in (one of four, required), the Directory (tenant) ID (a GUID, required with
  "one organization", not the personal tenant), shared, allowed email domains.
  Refusals as the contract's table, 422, values kept but the secret.
- **FR-003** Save and Enable fetch the discovery document at
  `https://login.microsoftonline.com/<segment>/v2.0` and require its `issuer` to
  equal exactly the issuer research R3 gives for the choice. When the check fails
  (unreachable, or wrong; a tenant ID that names no tenant is answered with HTTP
  400 `invalid_tenant`, which reads as wrong, "No Microsoft tenant has this ID"):
  - a **new** connector is saved disabled, with the contract's notice;
  - an **existing** connector whose choice of who may sign in **changed** is not
    saved at all: the form is redrawn (422) with the reason under the choice, so a
    mistyped tenant ID never reaches a connector that people use (documentation
    review B1, 2026-11-13);
  - an **existing** connector whose choice did **not** change saves the other
    changes and keeps its enabled state, with a notice that says so;
  - Enable stays off with its notice.
- **FR-004** The authorize URL is the discovery document's
  `authorization_endpoint`, with `openid email profile`, PKCE S256, `nonce`,
  `state`.
- **FR-005** The code is exchanged with the client authentication the document
  allows (the OIDC connector's `exchangeCode`). An `id_token` is required.
- **FR-006** The `id_token` is validated as research R2 says: canonical-GUID
  `tid`; `iss` equal to the template with that `tid`, checked by
  Microsoft.IdentityModel with signature (asymmetric, discovery keys, one
  throttled refresh on an unknown `kid`), audience (the client ID), lifetime
  (two minutes' skew); the signing key's `issuer` matching `iss`; `nonce`; `azp`
  with several audiences.
- **FR-007** Then `tid` must be admitted by the choice (R3). Otherwise
  `AccountRefused "tenant_not_allowed;tenant=<tid>"`, shown as
  `account-not-allowed`.
- **FR-008** The identity is `<tid>#<oid>`, lower case; a missing or non-GUID
  `oid` is refused.
- **FR-009** `email` is taken from the `id_token` only. `email_verified` is true
  only when `email` is present and `xms_edov` is the JSON literal `true`. The
  userinfo endpoint is never called.
- **FR-010** The display name is `name`, else `preferred_username`. No avatar.
- **FR-011** At every authorization that reuses a session established by the
  Microsoft connector, the person's Microsoft subjects are re-checked against the
  current choice; when none is admitted, the session is ended and the request
  refused, logged `microsoft:tenant_not_allowed;tenant=<tid>`, at the same place
  as the Fediverse and Bluesky re-checks (authorize and token).
- **FR-012** Changing the choice never changes anyone's identity.
- **FR-013** Every refusal is in the security log with the contract's words; no
  token, secret, `email` or name is ever logged.
- **FR-014** The sign-in button is an ordinary connector button (contract).
- **FR-015** Docs: `docs/connectors/microsoft.md` (registration in the Microsoft
  Entra admin center, the redirect URI, the client secret and its expiry and
  renewal, the optional claim `xms_edov`, who may sign in, Gotchas);
  `docs/connectors/oidc.md` points to it for personal and multi-organization
  sign-in and says when the generic connector is the better choice; the
  navigation line of every walkthrough; README.md; `site/sidebar.mjs`; a
  `changelog.d/` file; the backup page's schema version.

### Key Entities

See [data-model.md](data-model.md): `MicrosoftAudience`, the
`microsoft_audience` column (schema 13), `AccountRefused`.

## Success Criteria

- **SC-001** Tests with a fake Microsoft (discovery with the template issuer, a
  key set with `issuer` per key, a token endpoint) show: a personal account
  accepted when allowed; a work account refused under "personal only" and a
  personal account under "any work or school"; a token whose `iss` does not match
  its own `tid` refused; a tenant outside the configured one refused; a key whose
  issuer does not fit refused; `email_verified` not raised from `email` alone and
  raised with `xms_edov: true`; the re-check after narrowing.
- **SC-002** The generic connector is unchanged: its tests pass as they are.
- **SC-003** The full suite is green.
- **SC-004** Design, documentation and code reviews clear.

## Out of Scope

- Sovereign clouds; Azure AD B2C and Entra External ID (customer) tenants.
- Microsoft's logo on the button (research R8).
- Reading the secret's expiry (needs Microsoft Graph).
- Group or role claims (Principle VII).

## Still Open (for the maintainer)

- A real sign-in with a real app registration, under each choice, with and
  without the `xms_edov` optional claim, to confirm that Microsoft sends it as a
  JSON boolean.
