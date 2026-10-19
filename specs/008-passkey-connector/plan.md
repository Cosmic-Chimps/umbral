# Implementation Plan: The Passkey Connector

**Branch**: `alta/rapid-centauri-mjkq` (worktree branch; spec directory `specs/008-passkey-connector`) | **Date**: 2026-10-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/008-passkey-connector/spec.md`

## Summary

Add passkeys as a built-in Connector kind, `passkey`, with Umbral as the
relying party. fido2-net-lib 4.3.1 verifies every registration and sign-in
(research R1, R2); Umbral owns the ceremony state (a single-use row, R3), the
credential and invitation storage (schema v6, R7), the pages and JSON
endpoints (R12), one extra embedded script (R4), and the operator's surface.
A passkey sign-in ends in `Sessions.signIn … "passkey" "passkey" None` and the
unchanged authorize and token handlers issue ordinary OIDC tokens with
`idp = passkey`. The work is in six places:

1. **Domain and storage**: `ProviderKind.Passkey`, `PasskeyEnrolment`, the
   pure rules (names, nickname, invitation secret and hash, reason words) in
   `Domains/Passkeys.fs`; schema v6; `Infrastructure/Passkeys.fs` (credentials
   and ceremonies) and `Infrastructure/PasskeyInvitations.fs`.
2. **The verifier**: `Infrastructure/WebAuthn.fs`, a lazily built `Fido2`
   configured from the public origin, with options builders and a `verify`
   that maps `Fido2VerificationException` to closed words (R2, R6).
3. **Ceremonies and pages**: `Handlers/Passkeys/Ceremonies.fs` (JSON),
   `Handlers/Passkeys/SignIn.fs` (`/connect/passkey/start`),
   `Handlers/Passkeys/Invitation.fs` (`/invite/{secret}`),
   `Handlers/Passkeys/Account.fs` (`/account/passkeys`), and `passkeys.js`.
4. **Operator surface**: `Handlers/Admin/PasskeyConnector.fs` (the card, the
   save route, invitations), additions to `Admin/Users.fs` and a new
   `Admin/UserPasskeys.fs` (remove, invite), the Apps and Connectors warnings.
5. **Cross-cutting**: `EmailDomainRules.ruleFor` for the kind, `Picker`'s
   button, `Home`'s link, `SecurityEvents` types, `Texts`, `Assets`, `Layout`
   (a page with the extra script), `SecretMove` and `ConnectorDelete` with an
   empty reference, `RateLimits` on the new routes.
6. **Tests and docs**: the software authenticator, the five new suites, the
   third idle-memory run, README, `docs/connectors/passkey.md`,
   `docs/vision.md`, `CHANGELOG.md`.

## Technical Context

**Language/Version**: F# on .NET 10 (SDK 10.0.400 via `global.json`), ASP.NET Core

**Primary Dependencies**: existing (Oxpecker 2.1, htmx 4, OpenIddict 7.7.1,
Data Protection, Microsoft.Data.Sqlite) plus **`Fido2` 4.3.1** (exact pin;
brings `Fido2.Models`, `System.Formats.Cbor`, `NSec.Cryptography`). No
`Fido2.AspNet`. Browser side: the WebAuthn API and the standard JSON
conversion methods, with a base64url fallback; no build step.

**Storage**: SQLite in the data directory. Three tables and one column, schema
version 5 → 6 (data-model.md).

**Testing**: xunit.v3 on Microsoft.Testing.Platform, TestServer through
`Program.buildApp` (`tests/Umbral.Tests/Infrastructure/Fixtures.fs`, `Flows.fs`,
`Journeys.fs`), the fake connector for provider sign-ins, and a new
`SoftwareAuthenticator` (R11). `dotnet test -c Release`.

**Target Platform**: Linux container, `linux/amd64` and `linux/arm64`;
browsers with the WebAuthn API (all evergreen browsers; `localhost` for the
laptop trial).

**Project Type**: single server-rendered web service.

**Performance Goals**: a ceremony is two requests and one SQLite transaction;
no network call. The verifier is built once, on first use.

**Constraints**: idle RSS under 150 MiB without the connector and under 160
MiB with it, measured (SC-008, R13); CSP `default-src 'self'` unchanged; no
inline script; every SQL a literal (MetaTests); every string in `Texts.fs`
and read somewhere (MetaTests); no challenge, credential id, key, exception
message or invitation secret in any log or event (SC-007); warnings are
errors.

**Scale/Scope**: a few dozen users, up to 10 passkeys each, a handful of open
invitations; ~8 new source files, ~5 new test files, ~60 new text fields.

## Constitution Check

*Evaluated before Phase 0 and again after Phase 1. Both passes agree.*

| Gate | Result | Notes |
| --- | --- | --- |
| Concept count | PASS | A Connector kind and a User's credentials (constitution I, 3.0.0). Invitations live on the connector's card and the user's row; the passkeys page is the person's own, not an admin screen. Three top-level screens unchanged. |
| Single artifact | PASS | Keys, counters and hashes in `umbral.db`; the library in the image; nothing outside the container. Image grows by the library and libsodium for the two architectures; measured in R13. |
| Protocol safety | PASS | fido2-net-lib parses and verifies every response (R2, R6); OpenIddict still issues every token; Umbral builds options and stores results, nothing cryptographic. The test authenticator's crypto lives in the test project only. |
| Secure default | PASS | Invitations only is the default enrolment; UV required, attestation none, single-use server challenge, exact origin and RP id from the configured origin, hashed single-use expiring invitations, rate limits, antiforgery, events: none of it is a setting (R2, R3, R9, R12). |
| Documentation | PASS | Tasks include README, `docs/connectors/passkey.md`, `docs/vision.md`, `CHANGELOG.md` (spec FR-037). |
| Scope | PASS | In scope since 3.0.0; no MFA (a passkey is never asked after a provider sign-in or after a passkey); no passwords; enrolment mode decides who is a user and nothing else; the passkeys page is the one self-service page (constitution VII). |

Testing discipline: every ceremony behaviour runs through the real endpoints
with the software authenticator, including the three cases the constitution
names; a passkey user's tokens are decoded from a real issuance.

Technology constraints: F#, Oxpecker, htmx, server-rendered, SQLite,
OpenIddict, fido2-net-lib, no build step: all kept.

## Project Structure

### Documentation (this feature)

```text
specs/008-passkey-connector/
├── spec.md
├── plan.md                 # this file
├── research.md             # R1–R14
├── data-model.md           # tables, connector fields, claims, events, texts
├── quickstart.md           # validation runbook
├── contracts/
│   ├── ceremonies.md       # sign-in page, JSON endpoints, invitation page, passkeys page, script, claims
│   └── admin-surface.md    # the card, invitations, Users rows, Apps warning, the log
├── checklists/requirements.md
└── tasks.md                # /speckit-tasks
```

### Source Code (repository root)

```text
src/Umbral/
├── Umbral.fsproj                          # PackageReference Fido2 4.3.1; Compile entries; EmbeddedResource passkeys.js
├── Domains/
│   ├── Connectors.fs                      # ProviderKind.Passkey; Connector.PasskeyEnrolment; parseKind; DisplayName
│   └── Passkeys.fs                        # NEW (after Connectors.fs): PasskeyEnrolment, limits, name rules, secret/hash/link, Reason words, detail builder
├── Infrastructure/
│   ├── Database.fs                        # schema v6: passkeys, passkey_invitations, passkey_ceremonies, connectors.passkey_enrolment
│   ├── ConnectorStore.fs                  # read/write passkey_enrolment; `setEnrolment`
│   ├── SecurityEvents.fs                  # + 4 types; passkeyRefusalDetail
│   ├── EmailDomainRules.fs                # ruleFor: Passkey -> NoRule
│   ├── SecretMove.fs / Handlers/Admin/ConnectorDelete.fs  # empty reference: skip, nothing to delete
│   ├── Passkeys.fs                        # NEW (after UserStore.fs): credentials CRUD, counter update, ceremonies insert/consume/prune, last-passkey rule
│   ├── PasskeyInvitations.fs              # NEW: create, open list, find by hash, revoke, consume (conditional update), prune
│   └── WebAuthn.fs                        # NEW (after PasskeyInvitations.fs): Lazy<Fido2>, options builders, verify -> Result<_, Reason>
├── Views/
│   ├── Texts.fs                           # + passkey texts and notices
│   ├── Assets.fs                          # + passkeys (embedded passkeys.js)
│   └── Layout.fs                          # `pageWithScripts` (plain chrome + extra scripts); signInPage variant with scripts
├── Assets/passkeys.js                     # NEW: the ceremony script (contracts/ceremonies.md)
└── Handlers/
    ├── Home.fs                            # "Your passkeys" link when a passkey connector exists
    ├── Connect/Picker.fs                  # "Sign in with a passkey" label for the kind
    ├── Passkeys/Ceremonies.fs             # NEW: POST /passkey/assertion/options|assertion|registration/options|registration
    ├── Passkeys/SignIn.fs                 # NEW: GET /connect/passkey/start
    ├── Passkeys/Invitation.fs             # NEW: GET /invite/{secret}
    ├── Passkeys/Account.fs                # NEW: GET /account/passkeys, POST /account/passkeys/{id}/remove
    ├── Admin/PasskeyConnector.fs          # NEW (before Admin/Connectors.fs): the card view; POST /admin/connectors/passkey, …/invitations, …/invitations/{id}/revoke
    ├── Admin/Connectors.fs                # Passkey row in rowsFor; dispatch to PasskeyConnector.card; removal warning skips the kind; mayCreate false
    ├── Admin/Users.fs                     # passkey pill, passkey list, invite button in `row`
    ├── Admin/UserPasskeys.fs              # NEW: POST /admin/users/{id}/invite, POST /admin/users/{id}/passkeys/{pid}/remove
    ├── Admin/Apps.fs                      # unrestrictedFor: Passkey open -> unrestricted, invitations -> restricted
    └── (Program.fs)                       # endpoints list; WebAuthn singleton
tests/Umbral.Tests/
├── Umbral.Tests.fsproj                    # Compile entries
├── Infrastructure/SoftwareAuthenticator.fs  # NEW (R11)
├── Infrastructure/PasskeyFlows.fs         # NEW: enable connector, invite, register, sign in, through the endpoints
├── PasskeyDomainTests.fs                  # NEW: Domains/Passkeys.fs
├── PasskeyCeremonyTests.fs                # NEW: Stories 1, 2, 4 (sign-in, every refusal, open sign-up, tokens)
├── PasskeyInvitationTests.fs              # NEW: Stories 1, 5 (new person, existing user, unusable, race)
├── PasskeyAccountTests.fs                 # NEW: Story 3 (the passkeys page)
├── PasskeyAdminTests.fs                   # NEW: Story 6 (card, invitations list, Users rows, Apps warning, delete cascade)
├── DatabaseTests.fs                       # + v5 -> v6
├── SecurityEventCoverageTests.fs          # + the four types and a refusal
├── LogRedactionTests.fs                   # + a passkey journey at Trace: no secret, key or credential id
├── AllowedDomainsSignInTests.fs           # + passkey session and token survive an instance list
└── IdleMemoryTests.fs                     # + third run (R13)
README.md, docs/connectors/passkey.md (NEW), docs/vision.md, CHANGELOG.md
```

**Structure Decision**: the same vertical slices as every feature: a pure
domain module beside `Linking.fs` and `EmailDomains.fs`, infrastructure
modules that only read and write, one handler module per route group, every
string in `Texts.fs`. The verifier is one infrastructure module so that the
only place the library is called is one file (Principle III's "as little auth
as possible"). `Handlers/Passkeys/` is a new folder because the routes belong
to neither `/connect/` (OIDC) nor `/admin/`.

## Design notes (Phase 1)

### Compile order

`Domains/Passkeys.fs` after `Domains/Connectors.fs` (it uses `ProviderKind`).
`Infrastructure/Passkeys.fs` and `PasskeyInvitations.fs` after `UserStore.fs`
(they read users); `WebAuthn.fs` after both (it calls their lookups from the
library's callbacks). `Handlers/Passkeys/Ceremonies.fs` before the three
page handlers (they share its JSON helpers). `Handlers/Admin/PasskeyConnector.fs`
before `Admin/Connectors.fs` (which calls its `card`) and after
`Admin/ConnectorDelete.fs`. `Admin/UserPasskeys.fs` after `Admin/Users.fs`
(it re-renders a row for htmx).

### The verifier (`WebAuthn.fs`)

```text
type Reason = ... (research R6)
type Verifier(origin: PublicOrigin, config: DbConfig, clock: TimeProvider)
  member RegistrationOptions : user: Users.User -> existing: Passkey list -> CredentialCreateOptions
  member AssertionOptions    : unit -> AssertionOptions
  member VerifyRegistration  : options: string -> credential: JsonElement -> Task<Result<Registered, Reason>>
  member VerifyAssertion     : options: string -> credential: JsonElement -> stored: Passkey -> Task<Result<uint32 (* new counter *), Reason>>
```

`Fido2Configuration`: `ServerDomain` = `origin.Uri.Host`, `ServerName` =
"Umbral", `Origins` = { `origin.Text` }, `ChallengeSize` = 32, `Timeout` =
300 000. `Lazy<Fido2>` so nothing is built at startup. Registration options:
`AuthenticatorSelection(ResidentKey = Required, UserVerification = Required)`,
`AttestationPreference = None`, `Extensions.CredProps = true`,
`ExcludeCredentials` from `existing`. Assertion options: `UserVerification =
Required`, no `AllowedCredentials`. `VerifyRegistration` passes the
`IsCredentialIdUniqueToUserCallback` that queries `passkeys.credential_id`,
then refuses `credProps.rk = false`. `VerifyAssertion` passes
`StoredPublicKey`, `StoredSignatureCounter`, `StoredBackupEligible` and an
`IsUserHandleOwnerOfCredentialIdCallback` that checks the stored passkey's
`user_id` equals the handle. Every `Fido2VerificationException` becomes a
`Reason` from its `Code`; its message is dropped. The raw credential JSON is
deserialised with the library's own models (`AuthenticatorAttestationRawResponse`,
`AuthenticatorAssertionRawResponse`), which carry the base64url converters.

### Ceremony handlers

A JSON POST is read as: `WebForms.validateAntiforgery ctx` (new: the
validation half of `readValidated`, so the header is checked without reading a
form), then `JsonDocument` from the body with a 64 KiB cap. Every refusal
goes through one `refuse ctx stage reason notice` that records the
`sign_in_failed` event and writes `{ "error": notice }` with 400. Success
writes `{ "redirect": … }`. `signin` and `signup` sanitise `returnUrl` with
`Picker.sanitizeReturnUrl`. After a successful assertion:
`Passkeys.recordUse`, the enabled check, `Sessions.signIn`, `sign_in_succeeded`.
After a successful registration: one transaction (user insert when the
purpose mints one, passkey insert, invitation consume, events), then
`Sessions.signIn` for `signup` and `invitation`.

### Pages

`SignIn.fs` renders the passkey sign-in page in the plain chrome, or the
branded chrome when `returnUrl` names a branded app (reusing the picker's
`clientIdOf` and branding lookup: move `clientIdOf` to a shared helper in
`Picker.fs`, made public). `Invitation.fs` looks the secret up by hash and
renders one of the two forms or the unusable page. `Account.fs` requires the
session (`ctx.AuthenticateAsync Sessions.Scheme`), reads the user, lists the
passkeys and the linked kinds, and handles the remove form. All three link
`Assets.passkeys` through `Layout.pageWithScripts`.

### Operator surface

`PasskeyConnector.card ctx t origin enabledCount chosenSlugs existing
invitations revealedLink` renders the card (contracts/admin-surface.md);
`Connectors.rowsFor` adds a `Row` for the kind and `card` dispatches on it.
The invitation link reveal reuses a Data-Protected single-use cookie with
purpose `Umbral.PasskeyInviteReveal.v1` scoped by `about=connector` or
`about=user:<id>`, consumed by the screen that redirected to.

### Rate limits and CSRF

`configureEndpoint _.RequireRateLimiting(RateLimits.SignIn)` on
`/connect/passkey/start`, every `/passkey/*` and `/invite/{secret}`. The JSON
endpoints validate the antiforgery header; the forms carry the field. The CSRF
discovery test finds `POST /passkey/*` and `POST /account/passkeys/{id}/remove`
and holds them to "no token → 400, nothing changed", which the antiforgery
check satisfies before any body is read.

### Memory

`IdleMemoryTests` third run (R13): seed `connectors` (kind `passkey`,
enabled, `invitations`) and one `passkey_invitations` row with a known secret
through `Database.initializeSchema` and the stores before the child starts;
then `GET /invite/{secret}` for the antiforgery token and cookie, `POST
/passkey/registration/options` (`mode: invitation`), `POST
/passkey/registration` with the software authenticator's response, then `POST
/passkey/assertion/options` and `POST /passkey/assertion`; idle; measure.
Ceiling 160 MiB on Linux, printed everywhere. **Measured figures**: to be
recorded here by the implementation task (linux/amd64 and linux/arm64 from
CI's job summary, macOS locally for information).

## Implementation order

Each step leaves the suite green and an instance without the connector
unchanged.

1. **Foundation**: package, kind, domain module and its tests, schema v6 and
   its test, stores, events, `ruleFor`, `SecretMove`/`ConnectorDelete`
   tolerance, `Texts` skeleton. Nothing visible.
2. **Verifier and software authenticator**: `WebAuthn.fs`; the test
   authenticator; a direct test that a good registration and assertion verify
   and that each misbehaviour maps to its reason.
3. **Story 1 and 2 (sign-in)**: ceremony endpoints for `signin`, the sign-in
   page, the picker label, the script; `PasskeyCeremonyTests` for sign-in and
   every refusal; tokens of a passkey user.
4. **Invitations (Stories 1, 5)**: store, admin card with create/revoke,
   invitation page, `invitation` registration, Users row invite;
   `PasskeyInvitationTests`, `PasskeyAdminTests` (card part).
5. **Story 3 (passkeys page)** and **Story 4 (open sign-up)**: `add` and
   `signup` registration, the page, the home link, the Apps warning;
   `PasskeyAccountTests`, the rest of `PasskeyCeremonyTests`.
6. **Story 6**: Users row list and remove, delete cascades, disable/delete
   connector; rest of `PasskeyAdminTests`; coverage, redaction, allowed-domains
   and idle-memory additions.
7. **Docs**: README, walkthrough, vision, changelog. Full suite; record the
   memory figures here.

Steps 4 and 5 touch different handler files (shared: `Texts.fs`,
`Ceremonies.fs`) and can run in parallel on separate worktrees; the docs can
be written in parallel with 4 to 6.

## Phase outputs

- [research.md](./research.md): R1–R14.
- [data-model.md](./data-model.md): tables, fields, claims, events, texts.
- [contracts/ceremonies.md](./contracts/ceremonies.md) and
  [contracts/admin-surface.md](./contracts/admin-surface.md).
- [quickstart.md](./quickstart.md).

Agent context: this repository has no `update-agent-context` script and no
agent context file; nothing to run.

## Complexity Tracking

No violation. One note for the record:

| Item | Why | Simpler alternative rejected because |
| --- | --- | --- |
| A second embedded script (`passkeys.js`) | The WebAuthn ceremony needs ~150 lines of browser code that only three pages use | Adding it to `umbral.js` would load it on every page; inline script is forbidden by the CSP; a framework is forbidden by the constitution. It is still no build step. |
