---
description: "Task list for 008 passkey connector"
---

# Tasks: The Passkey Connector

**Input**: Design documents from `specs/008-passkey-connector/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included. The constitution requires passkey registration and
sign-in to be tested through the real endpoints with a software
authenticator, including a wrong origin, a reused challenge and a missing
user verification; SC-003, SC-005, SC-006, SC-007 and SC-008 are test
outcomes. Within each story, tests come first and must fail before the task
that makes them pass.

**Organization**: by user story, after a foundation that adds the kind, the
schema, the stores, the verifier and the software authenticator with no
visible change. Stories 1 and 2 form the MVP together: an invited person
enrols and signs in, and every attack on the ceremony is refused.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1–US7 from spec.md
- F# compile order matters: every new `.fs` file needs its `<Compile Include>`
  at the right place in `src/Umbral/Umbral.fsproj` or
  `tests/Umbral.Tests/Umbral.Tests.fsproj` (plan.md "Compile order").

## Conventions every task follows

- `dotnet build -c Release` (warnings are errors) and the tests the task
  names; the full suite is `dotnet test -c Release`.
- Every operator-facing or end-user string lives in `src/Umbral/Views/Texts.fs`
  and is read by some view (MetaTests); every operator message says what to
  do next (Principle VI). Wording is the contracts'.
- Every SQL text is a literal or a `[<Literal>]` constant (MetaTests).
- No challenge, credential id, public key, invitation secret or link, raw
  `clientDataJSON`, or library exception message in any log line or security
  event (SC-007). Reason words only (research R6).
- Umbral code never parses attestation objects or verifies signatures: the
  library does (Principle III). The test project may build them.
- An instance with no passkey connector behaves exactly as before: existing
  tests are not edited to pass, except where a record gains a field.
- Every new handler module's `endpoint` goes into `Program.endpoints`
  (MetaTests names a missing one).

---

## Phase 1: Setup

- [x] T001 Add `<PackageReference Include="Fido2" Version="4.3.1" />` to `src/Umbral/Umbral.fsproj` with a comment (research R1: the verifier the constitution names; exact pin because the reason words map from its error codes; `Fido2.AspNet` not used), the `<EmbeddedResource Include="Assets/passkeys.js" LogicalName="passkeys.js" />`, and the `<Compile Include>` entries for the new files in plan.md order: `Domains/Passkeys.fs` after `Domains/Connectors.fs`; `Infrastructure/Passkeys.fs`, `Infrastructure/PasskeyInvitations.fs`, `Infrastructure/WebAuthn.fs` after `Infrastructure/UserStore.fs`; `Handlers/Passkeys/Ceremonies.fs`, `Handlers/Passkeys/SignIn.fs`, `Handlers/Passkeys/Invitation.fs`, `Handlers/Passkeys/Account.fs` after `Handlers/Connect/Picker.fs`; `Handlers/Admin/PasskeyConnector.fs` after `Handlers/Admin/ConnectorDelete.fs` and before `Handlers/Admin/Connectors.fs`; `Handlers/Admin/UserPasskeys.fs` after `Handlers/Admin/UserDelete.fs`. Create each file as an empty module so the build stays green. Run `dotnet restore` and confirm `dotnet list src/Umbral package --include-transitive` shows `Fido2.Models`, `System.Formats.Cbor` and `NSec.Cryptography`
- [x] T002 Register the test files in `tests/Umbral.Tests/Umbral.Tests.fsproj`: `Infrastructure/SoftwareAuthenticator.fs` and `Infrastructure/PasskeyFlows.fs` after `Infrastructure/Journeys.fs`; `PasskeyDomainTests.fs`, `WebAuthnVerifierTests.fs`, `PasskeyCeremonyTests.fs`, `PasskeyInvitationTests.fs`, `PasskeyAccountTests.fs`, `PasskeyAdminTests.fs` before `SmokeTests.fs`. Create each as an empty module

---

## Phase 2: Foundational (blocking)

**Purpose**: the kind, the schema, the stores, the verifier, the test
authenticator, the event types and the text skeleton. No behaviour changes;
the suite stays green.

- [x] T003 [P] Write `tests/Umbral.Tests/PasskeyDomainTests.fs` for `Domains/Passkeys.fs` per data-model.md: `parseEnrolment` (`"invitations"`, `"open"`, anything else → None) and `Value` round trip; `validateDisplayName` (trim; empty, white space, 65 chars → Error naming the limits; 1 and 64 → Ok); `validateNickname` (empty → Ok None; 64 → Ok Some; 65 → Error); `defaultNickname clock` contains the date; `newInvitationSecret` is 43 chars base64url and differs each call; `hashInvitationSecret` is 64 lowercase hex and deterministic; `invitationLink origin secret` is `{origin}/invite/{secret}`; `InvitationLifetime = 7 days`, `CeremonyLifetime = 5 minutes`, `MaxPerUser = 10`; `Reason.Word` for every case is lowercase snake with no spaces; `refusalDetail reason stage` is `passkey_refused;reason=…;stage=…`
- [x] T004 Add `ProviderKind.Passkey` to `src/Umbral/Domains/Connectors.fs` (`Value = "passkey"`, `DisplayName = "Passkey"`, in `parseKind`; `endpointsFor` returns the empty record like `Fake`; `setupCaveat` → None; `allKinds` unchanged) and the field `PasskeyEnrolment: PasskeyEnrolment` on `Connector`, with the type `PasskeyEnrolment = InvitationsOnly | OpenSignUp` (member `Value` = `"invitations"` | `"open"`) defined in `Domains/Connectors.fs` above `Connector` so the record can use it. Update every `Connector` construction site in `src/` and `tests/` with `PasskeyEnrolment = InvitationsOnly` (the 21 files `grep -rl "AllowedEmailDomains = " src tests` lists). Build green
- [x] T005 Implement `src/Umbral/Domains/Passkeys.fs` (pure) per data-model.md and research R6, R9 until T003 passes: `parseEnrolment`, the three constants, `validateDisplayName`, `validateNickname`, `defaultNickname`, `newInvitationSecret` (32 random bytes, base64url, add a public `Ids.randomToken bytes` helper to `Domains/Ids.fs` if needed), `hashInvitationSecret` (SHA-256 hex), `invitationLink`, `type Stage = Registration | Assertion | Invitation` with `Value`, `type Reason` with every word of R6 and `Word`, `refusalDetail`
- [x] T006 Schema version 6 in `src/Umbral/Infrastructure/Database.fs` (research R7, data-model.md): the three `CREATE TABLE IF NOT EXISTS` and two indexes in `schema`; `connectors.passkey_enrolment TEXT NOT NULL DEFAULT 'invitations'` in the `CREATE TABLE connectors`; `UpgradeToVersion6` literal (the `ALTER TABLE connectors ADD COLUMN …` and the three creates) appended to `upgradeSteps`; `SchemaVersion = 6`; the `ix_passkey_invitations_open` partial index kept out of `schema` and run after the steps like `issuerIndex` if the create inside `schema` would fail on an old `connectors` (it does not: the index is on the new table, so it can stay in `schema`). Extend `tests/Umbral.Tests/DatabaseTests.fs`: a version 5 database with connector rows upgrades to 6 with `passkey_enrolment = 'invitations'` on every row and the three empty tables; a fresh database is stamped 6; deleting a user cascades to `passkeys` and `passkey_invitations`; deleting a connector cascades to `passkey_invitations`; `passkeys.credential_id` is unique
- [x] T007 Read and write `passkey_enrolment` in every query of `src/Umbral/Infrastructure/ConnectorStore.fs` (`Columns`, `read`, `save`, `create`, `update` keeps it, plus `setEnrolment config clock slug enrolment : Task<bool>`), update `tests/Umbral.Tests/OidcConnectorStoreTests.fs` or add a case that a saved passkey connector reads back with its enrolment
- [x] T008 Implement `src/Umbral/Infrastructure/Passkeys.fs` per data-model.md: `type Passkey` record; `listForUser`, `findByCredentialId`, `findById`, `countForUser`, `credentialIdExists`; `insertIn connection transaction clock passkey`; `recordUse config clock id newCount`; `remove config id : Task<bool>`; `removeOwnIfNotLast config userId id : Task<RemoveOutcome>` (`Removed | NotFound | LastWayIn`, one transaction: refuse when it is the user's only passkey and `linked_accounts` has no row for the user); ceremonies: `type Ceremony` record and `Purpose`, `startCeremony config clock ceremony` (inserts, prunes expired rows first), `consumeCeremony config clock id : Task<Ceremony option>` (`DELETE … WHERE id = @id AND expires_utc > @now RETURNING …`). Every SQL a literal. Unit-test the store parts that have no endpoint of their own in `tests/Umbral.Tests/PasskeyAccountTests.fs` (`removeOwnIfNotLast` branches; consume twice → second None; expired → None)
- [x] T009 [P] Implement `src/Umbral/Infrastructure/PasskeyInvitations.fs` per data-model.md and research R9: `type Invitation` record and `State`; `create config clock slug userId note : Task<Invitation * secret>`; `findBySecret config clock secret : Task<(Invitation * State) option>` (State judged against the clock and the user's existence and enabled flag); `listOpen config clock slug`; `revoke config clock id : Task<bool>`; `consumeIn connection transaction clock id : Task<bool>` (the conditional `UPDATE … RETURNING`); `prune` of used/revoked/expired rows older than 30 days called by `create`. Unit tests in `tests/Umbral.Tests/PasskeyInvitationTests.fs`: created → open; `consumeIn` twice → second false; after the clock passes 7 days → `Expired`; revoked → `Revoked`; for a disabled or deleted user → `UserGone`
- [x] T010 [P] Add to `src/Umbral/Infrastructure/SecurityEvents.fs`: `PasskeyRegistered` (`passkey_registered`), `PasskeyRemoved` (`passkey_removed`), `PasskeyInvitationCreated` (`passkey_invitation_created`), `PasskeyInvitationRevoked` (`passkey_invitation_revoked`) in the union, `Value` and `allEventTypes`
- [x] T011 [P] In `src/Umbral/Infrastructure/EmailDomainRules.fs`, `ruleFor` returns `NoRule` for `connector.Kind = Passkey` without reading the instance list (doc comment: passkeys carry no email; spec 008 FR-010). In `src/Umbral/Infrastructure/SecretMove.fs` treat a connector whose `ClientSecretRef` is `""` as `InPlace` with a line `t.MoveNoSecret slug` (new text) rather than `Failed`; in `src/Umbral/Handlers/Admin/ConnectorDelete.fs` an undecodable reference already means "nothing to delete" (verify, add a comment naming the passkey kind). Test: `tests/Umbral.Tests/SecretMoveTests.fs` gains a case with a passkey connector row (seeded through `ConnectorStore.save` with empty secret fields) that `move-secrets` skips without failing
- [x] T012 Write `tests/Umbral.Tests/Infrastructure/SoftwareAuthenticator.fs` per research R11: `type SoftwareAuthenticator(rpId, origin)` with `member Credentials`; `Register(options: JsonElement, ?overrides) : JsonElement` producing the `toJSON()` shape (`id`, `rawId`, `type: "public-key"`, `authenticatorAttachment: "platform"`, `clientExtensionResults: { credProps: { rk } }`, `response: { attestationObject, clientDataJSON, transports: ["internal"] }`) with a fresh ECDSA P-256 key, authenticator data `SHA256(rpId) ‖ flags ‖ counter ‖ AAGUID(16 zero bytes) ‖ len ‖ credentialId ‖ COSE EC2 key` and a `none` attestation object written with `System.Formats.Cbor.CborWriter`; `Assert(options: JsonElement, credentialId, ?overrides) : JsonElement` with `response: { authenticatorData, clientDataJSON, signature (DER ECDSA over authData ‖ SHA256(clientDataJSON)), userHandle }`. Overrides record with optional fields: `Origin`, `RpId`, `Challenge`, `Flags` (default UP|UV|AT for register, UP|UV for assert), `Counter`, `Rk`, `UserHandle`, `CredentialId`, `SignWith` (another credential's key), `Type`. Base64url helpers. A self-test in `tests/Umbral.Tests/WebAuthnVerifierTests.fs` decodes its own output with `Fido2NetLib.AuthenticatorAttestationRawResponse` and `AuthenticatorAssertionRawResponse` (`System.Text.Json`) without error
- [x] T013 Implement `src/Umbral/Infrastructure/WebAuthn.fs` per plan.md "The verifier" and research R2, R6: `type Registered = { CredentialId: byte[]; PublicKey: byte[]; SignCount: uint32; AaGuid: Guid; Transports: string list; BackupEligible: bool; UserId: string; Discoverable: bool option }`; `type Verifier(origin, config)` with `Lazy<Fido2>`, `RegistrationOptions user existing`, `AssertionOptions ()`, `VerifyRegistration optionsJson credential : Task<Result<Registered, Reason>>` (callback: `not (Passkeys.credentialIdExists …)`; then `Discoverable = Some false` → `Error PasskeyNotDiscoverable`), `VerifyAssertion optionsJson credential stored : Task<Result<uint32, Reason>>` (callback: `stored.UserId` bytes equal the handle); one `mapException : Fido2VerificationException -> Reason` from `Code`. Register it as a singleton in `src/Umbral/Program.fs`. Write `tests/Umbral.Tests/WebAuthnVerifierTests.fs` (direct, no HTTP, on a `Fixtures.createApp` for the store): a good registration and assertion verify; and each override from T012 maps to its reason: wrong origin → `OriginMismatch`, wrong RP id → `RpIdMismatch`, wrong challenge → `ChallengeMismatch`, no UV → `UserVerificationMissing`, no UP → `UserPresenceMissing`, `rk=false` → `PasskeyNotDiscoverable`, counter 5 then 5 → `SignCountRegressed`, counter 0 then 0 → Ok, other key → `InvalidSignature`, other handle → `UserHandleMismatch`, `type: "webauthn.get"` on a registration → `TypeMismatch`
- [x] T014 [P] Add the `Texts` fields and notice codes of data-model.md "Texts" to `src/Umbral/Views/Texts.fs` with the contracts' wording (`contracts/ceremonies.md`, `contracts/admin-surface.md`), including `SignInWithPasskey`, `MoveNoSecret`, and every `knownNotice` code in `noticeEn`. Fields unread until a later task are allowed to fail MetaTests only until the task that reads them; land this task together with T015 so the suite is green at the checkpoint, or stub-read them in the handlers created empty in T001
- [x] T015 [P] Add `passkeys` to `src/Umbral/Views/Assets.fs` (`load "passkeys.js" "/assets/passkeys.js" "text/javascript; charset=utf-8"`, in `all`), create `src/Umbral/Assets/passkeys.js` with the behaviour of contracts/ceremonies.md "The script" (no text of its own; `data-*` hooks; `parse*FromJSON`/`toJSON` with base64url fallback; antiforgery header from `input[name=__RequestVerificationToken]`; cancel → nothing; refusal → `?msg=`; success → `redirect`), and add `Layout.pageWithScripts (scripts: Assets.Asset list)` and `Layout.signInPageWithScripts` to `src/Umbral/Views/Layout.fs` (the `shell` gains a `scripts` parameter; existing entry points pass `[]`). `tests/Umbral.Tests/PageAssetsTests.fs` gains the new asset (served, immutable, ETag)

**Checkpoint**: `dotnet test -c Release` green; nothing visible changed; an
instance without the connector is byte-for-byte the same.

---

## Phase 3: User Story 1 — An invited person signs in with a passkey (P1) 🎯 MVP, with User Story 2 — Every attack is refused (P1)

**Goal**: the operator enables the connector and invites someone; the person
enrols through the link and signs in to an app through the normal OIDC flow;
every refusal case ends with no session, no user and one event.

**Independent Test**: `PasskeyFlows` enables the connector and creates an
invitation through the admin routes, redeems it with the software
authenticator through `/invite/{secret}` and the JSON endpoints, then runs an
app's authorization request through `/signin` → `/connect/passkey/start` →
the assertion endpoints to a code and exchanges it; every override of the
authenticator is refused.

- [x] T016 [US1] Write `tests/Umbral.Tests/Infrastructure/PasskeyFlows.fs`: `enablePasskeyConnector browser enrolment` (POST `/admin/connectors/passkey`), `inviteNewPerson browser note : Task<string (* link *)>` (POST then scrape `data-invitation-link`), `inviteUser browser userId`, `openInvitation browser secret` (GET, scrape the antiforgery token and `data-screen`), `postJson browser path token body : Task<HttpResponseMessage * JsonElement>`, `registerThroughInvitation browser authenticator secret displayName nickname`, `signInWithPasskey browser authenticator credentialId returnUrl : Task<string (* redirect *)>`, `startPasskeySignIn browser clientId scope` (authorize → picker → `/connect/passkey/start`, returns the page and token), `addPasskey browser authenticator nickname`, `signUp browser authenticator displayName returnUrl`
- [x] T017 [US1] Write the US1 tests in `tests/Umbral.Tests/PasskeyCeremonyTests.fs` and `tests/Umbral.Tests/PasskeyInvitationTests.fs` per spec Story 1 scenarios and contracts/ceremonies.md: enable (invitations only) → card enabled, `connector_created` with `enrolment=invitations`; invite → `data-invitation-link` once (a reload shows no link), one open invitation listed, `passkey_invitation_created` `for=new`; open the link → `data-screen="invitation"` with `displayName`; register → `users` row with the name, `passkeys` row, invitation `used_utc` set, session cookie present, redirect `/account/passkeys?msg=passkey-added`, events `user_created` and `passkey_registered` `via=invitation`; the picker of an app offers `data-connector="passkey"` reading "Sign in with a passkey"; `/connect/passkey/start` → `data-screen="passkey-signin"`, `data-invitations-only` present, no `data-passkey-signup`; sign in → redirect to the authorize URL, code, tokens with `sub`, `name`, `idp = "passkey"` and no `email`/`email_verified` (decode the id token with `Journeys.segmentOf`), `sign_in_succeeded`; sign in again → same `sub`; userinfo has no email; the used link → `data-screen="invitation-unusable"` with the contract text and `sign_in_failed` `reason=invitation_used;stage=invitation`; an expired (clock + 8 days) and a revoked link the same page with their words; `/connect/passkey/start` is 404 with no connector and when disabled; `/connect/passkey/callback` is 404
- [ ] T018 [US2] Write the US2 tests in `tests/Umbral.Tests/PasskeyCeremonyTests.fs` per spec Story 2: each override of the authenticator at the assertion endpoint and at the registration endpoint (origin `https://evil.example`, RP id `evil.example`, a reused ceremony id with a fresh response, a response built on a ceremony older than 5 minutes (clock), UV off, UP off, unknown credential id, another user's handle, counter 5 then 5, wrong key, `rk=false` at registration) → 400 `{ "error": "passkey-sign-in-failed" | "passkey-registration-failed" }`, no session cookie, no new `users`/`passkeys` row, exactly one `sign_in_failed` with `passkey_refused;reason=<word>;stage=<stage>`; two refusals produce the same page text at `/connect/passkey/start?msg=passkey-sign-in-failed`; a disabled user → `account-disabled` and `user_disabled`; 31 POSTs to `/passkey/assertion/options` from one source → 429 with `Retry-After` (reuse `RateLimitTests` helpers); a POST without the antiforgery header → 400 and nothing changes; the security_events table and the Trace log contain no challenge, credential id, public key or the string `evil.example` (the library's message) — extend `tests/Umbral.Tests/LogRedactionTests.fs` with a passkey journey including one refusal
- [ ] T019 [US1] Add `WebForms.validateAntiforgery ctx : Task<unit>` (the validation half of `readValidated`) and `WebForms.readJson<'T> ctx limitBytes` (or a `JsonDocument` reader with a 64 KiB cap) to `src/Umbral/Infrastructure/WebForms.fs`
- [ ] T020 [US1] Implement `src/Umbral/Handlers/Passkeys/Ceremonies.fs` per contracts/ceremonies.md for purposes `signin` and `invitation` (the `signup` and `add` branches return `enrolment_closed` / `user_not_found` until Phases 5 and 6): `POST /passkey/assertion/options`, `POST /passkey/assertion`, `POST /passkey/registration/options`, `POST /passkey/registration`; one `refuse ctx stage reason notice`; the registration transaction (user insert for a new person with `Ids.newSubject`, `Passkeys.insertIn`, `PasskeyInvitations.consumeIn`, events) ; `Sessions.signIn ctx userId "passkey" "passkey" None` after a successful assertion or an enrolment; `Passkeys.recordUse`; rate limit `RateLimits.SignIn`; register in `Program.endpoints`. Expose `clientIdOf` from `Handlers/Connect/Picker.fs` as public for the sign-in page's branding
- [ ] T021 [P] [US1] Implement `src/Umbral/Handlers/Passkeys/SignIn.fs` (`GET /connect/passkey/start`) per contracts/ceremonies.md "The passkey sign-in page": 404 when no enabled passkey connector; plain or branded chrome by `returnUrl`'s app; `data-passkey-signin`, the `data-invitations-only` line in invitations mode, the `data-passkey-signup` form in open mode (built now, exercised in Phase 6), `<noscript data-javascript-required>`, `?msg=` notice, the antiforgery input, `Layout.pageWithScripts [Assets.passkeys]`; rate limit `SignIn`; register. In `src/Umbral/Handlers/Connect/Picker.fs` the button reads `t.SignInWithPasskey` for `connector.Kind = Passkey`
- [ ] T022 [P] [US1] Implement `src/Umbral/Handlers/Passkeys/Invitation.fs` (`GET /invite/{secret}`) per contracts/ceremonies.md "The invitation page": look up by `hashInvitationSecret`; `data-screen="invitation"` with the new-person or existing-user form (hidden `invitation` value = the secret for the script), or `data-screen="invitation-unusable"` with the contract text and one `sign_in_failed` `stage=invitation` naming the cause; `noscript`; rate limit `SignIn`; register
- [x] T023 [US1] Implement `src/Umbral/Handlers/Admin/PasskeyConnector.fs` (card and routes) per contracts/admin-surface.md: `card ctx t origin enabledCount chosenSlugs (existing: Connector option) (invitations: Invitation list) (revealedLink: string option) : HtmlElement` (unconfigured and configured states, the enrolment radios, `data-rule="enrolment"`, `data-origin-bound`, the invitation form and the open-invitations list with revoke forms); `POST /admin/connectors/passkey` (create enabled with empty secret fields, or update `is_shared` and enrolment through `ConnectorStore.update`/`setEnrolment`; the unshare stranded-apps warning reused from `AppConnectors.strandedByChange`; events `connector_created`/`connector_updated` with `enrolment=…`; notice `passkey-connector-saved`); `POST /admin/connectors/passkey/invitations` (note ≤ 64, `PasskeyInvitations.create`, the link stashed in a single-use Data-Protected cookie `Umbral.PasskeyInviteReveal.v1` about `connector`, event `for=new`, notice `passkey-invitation-created`); `POST /admin/connectors/passkey/invitations/{id}/revoke`. In `src/Umbral/Handlers/Admin/Connectors.fs`: `rowsFor` adds a `Row` of kind `Passkey` after the provider rows (existing or empty), `card` dispatches to `PasskeyConnector.card` for the kind (reading the open invitations and consuming the reveal cookie in `render`), `mayCreate` answers false for `Passkey`, the add form never offers the kind, and the `allowed-domains-removal` warning filters out the kind. Register the routes; T017 passes
- [ ] T024 [US1] Make T018 pass: review `Ceremonies.fs` against every refusal path (ceremony consumed before verification so a reused id fails; the user looked up by handle before the library runs; `user_disabled` checked after verification; the enabled connector checked first; events for each), and confirm the Trace log and events hold no values (adjust `LogRedaction.capCategories` only if a new category leaks, naming it)

**Checkpoint**: the MVP. An invited person signs in with no provider; every
attack is refused and recorded.

---

## Phase 4: User Story 5 — Recovery: a new invitation for an existing user (P2)

**Goal**: the operator invites an existing user from the Users screen; the
link adds a passkey to that account.

**Independent Test**: enrol a user, invite them from their row, redeem with a
second authenticator, sign in with the new passkey: same `sub`.

- [ ] T025 [US5] Write tests in `tests/Umbral.Tests/PasskeyInvitationTests.fs` per spec Story 5: `POST /admin/users/{id}/invite` → link once, invitation bound to the user, `passkey_invitation_created` `for=user` with the user id, listed on the card with `data-invitation-user`; the page names the account (`data-invitation-account`) and has no `displayName`; registration adds a passkey to that user (two rows, no new user), signs them in, uses the invitation up; a sign-in with the new passkey yields the same `sub` as before; the user disabled after the invitation → unusable page with `invitation_user_gone`; deleted → the invitation row is gone and the page is unusable with `invitation_unknown`; a user who signed in only through the fake provider can be invited and then signs in with either
- [ ] T026 [US5] Implement `src/Umbral/Handlers/Admin/UserPasskeys.fs`: `POST /admin/users/{id}/invite` per contracts/admin-surface.md (requires a passkey connector to exist, else `passkey-not-enabled`; the link stashed about `user:<id>`, consumed by the Users screen, which shows it in `data-invitation-link` on that row; notice `passkey-user-invited`); the **Invite to add a passkey** form in `Users.row` (`src/Umbral/Handlers/Admin/Users.fs`, shown while a passkey connector exists), with the `Row` type gaining `Passkeys: Passkey list` and `CanInvite: bool` filled by `page` and by `UserToggle`'s fragment. The `invitation` branch of `Ceremonies.fs` handles a user-bound invitation (no display name; passkey joins the user). T025 passes

---

## Phase 5: User Story 3 — A person adds passkeys to their account and removes them (P2)

**Goal**: the passkeys page.

**Independent Test**: sign in through the fake provider, open
`/account/passkeys`, add two passkeys, sign in to an app with one, remove
one, check the last-passkey refusal for a passkey-only user.

- [ ] T027 [US3] Write tests in `tests/Umbral.Tests/PasskeyAccountTests.fs` per spec Story 3 and contracts/ceremonies.md "The passkeys page": no session → 302 `/signin?returnUrl=%2Faccount%2Fpasskeys`, and after a fake-provider sign-in with that `returnUrl` the person lands on the page; the page lists `data-ways-in` with the provider's name; add with nickname "Laptop" → `data-passkey` row with the nickname and the added date, `passkey_registered` `via=add`; a second one from another authenticator; signing in to the app with the passkey → same `sub` as with the fake provider, `idp = "passkey"`, the user's email claims unchanged; remove → row gone, a sign-in with it refused as `unknown_credential`, `passkey_removed` `by=user`; removing a passkey of another user → `passkey-not-found`, nothing changes; a passkey-only user's last passkey → `passkey-last-cannot-remove`, row stays; ten passkeys → `data-passkeys-limit` and the eleventh refused `passkey_limit_reached`; a disabled user's session → cleared and redirected; the page carries the `noscript` note; with the connector deleted → `data-passkeys-not-enabled` and removing still works; the page wears the plain chrome even when the session came through a branded app (no `data-accent`)
- [ ] T028 [US3] Implement `src/Umbral/Handlers/Passkeys/Account.fs` (`GET /account/passkeys`, `POST /account/passkeys/{id}/remove`) per contracts/ceremonies.md, the `add` branch of `Ceremonies.fs` (session required, limit, enabled connector), and the "Your passkeys" link in `src/Umbral/Handlers/Home.fs` (`data-passkeys-link`, when a passkey connector row exists). Register the routes. T027 passes

---

## Phase 6: User Story 4 — Open sign-up (P2)

**Goal**: anyone can create an account with a passkey when the operator says so.

**Independent Test**: switch the mode, register from the sign-in page with no
invitation, check the user, the session and the code; switch back and check
the refusal.

- [ ] T029 [US4] Write tests in `tests/Umbral.Tests/PasskeyCeremonyTests.fs` per spec Story 4: save with `enrolment=open` → `connector_updated` `enrolment=open`; `/connect/passkey/start` shows `data-passkey-signup` and no `data-invitations-only`; sign-up with a name → user created, session, redirect to the authorize URL, code, tokens with the name; a sign-up attempt in invitations mode (direct POST with `mode: "signup"`) → `passkey-registration-failed` with `enrolment_closed`, no user; an empty or 65-char name → `display_name_invalid`; the Apps screen shows `data-warning="unrestricted-connectors"` naming "Passkey" for an app offering it in open mode together with a restricted connector, and not in invitations mode (extend `tests/Umbral.Tests/AllowedDomainsAdminTests.fs` or write it here); a passkey session and a passkey refresh token survive an instance email-domain list (`tests/Umbral.Tests/AllowedDomainsSignInTests.fs`)
- [ ] T030 [US4] Implement the `signup` branch of `Ceremonies.fs`, and in `src/Umbral/Handlers/Admin/Apps.fs` make `unrestrictedFor` treat a `Passkey` connector with `OpenSignUp` as having no rule and with `InvitationsOnly` as having one. T029 passes

---

## Phase 7: User Story 6 — The operator sees and manages passkeys and invitations (P3)

**Goal**: the Users row lists passkeys with Remove; disabling and deleting the
connector and deleting a user behave as specified.

**Independent Test**: after Stories 1, 3 and 5, walk the Users and Connectors
screens.

- [ ] T031 [US6] Write tests in `tests/Umbral.Tests/PasskeyAdminTests.fs` per spec Story 6 and contracts/admin-surface.md: the Users row has the `data-linked-provider="passkey"` pill and `data-user-passkeys` with nickname, added and last-used; `POST /admin/users/{id}/passkeys/{pid}/remove` → gone, `passkey_removed` `by=operator` actor operator, the user's other passkeys and the fake-provider link stay, the operator may remove the last one; the toggle fragment (`HX-Request`) draws the list too; revoking an invitation → `passkey_invitation_revoked`, the link unusable, not listed; revoking a used one → `passkey-invitation-not-open`; disabling the connector → the picker omits it, `/connect/passkey/start` 404, `/passkey/assertion/options` refused `connector_disabled`, adding on the passkeys page refused, removing works; deleting the connector → `passkey_invitations` empty, `passkeys` intact, and after enabling again a sign-in works and the old link does not; deleting a user → their passkeys and invitations gone, a sign-in with their credential → `unknown_credential`; disabling or deleting the connector when it is the only one an app offers → the stranded-apps warning names the app (existing routes)
- [ ] T032 [US6] Implement the Users row passkey list and `POST /admin/users/{id}/passkeys/{pid}/remove` in `src/Umbral/Handlers/Admin/UserPasskeys.fs` and `src/Umbral/Handlers/Admin/Users.fs` (the pill when `Passkeys` is non-empty; `UserToggle`'s fragment fills `Passkeys` too). Confirm `ConnectorToggle`, `ConnectorDelete` and `UserDelete` need no change beyond T011 (cascades do the rest); T031 passes

---

## Phase 8: User Story 7 — Documentation (P3) and cross-cutting

- [ ] T033 [P] [US7] README.md: the opening sentence ("whose only job is signing people in without passwords: with a social provider they already use, or with a passkey"), the concepts table (Connector: "… or Umbral's own passkey connector"), "What Umbral is not" (MFA and passwords still out; one sentence on why a passkey is neither), Requirements ("an account with a provider, or the passkey connector"), the quick-start aside that GitHub is one of two ways, the `idp` values (`passkey`), the "email may be missing" note (a passkey-only user has none), a **Passkeys** section (enable the connector, invitations only vs anyone, invite someone, the link once and 7 days, what the person does, adding a passkey to an existing account at `/account/passkeys`, recovery by inviting the user, removing, what an app receives, the address a passkey is bound to, JavaScript, the memory note "under 160 MB with the passkey connector in use" once T036 has the figure), the walkthrough list and Contents; the schema note becomes version 6
- [ ] T034 [P] [US7] Write `docs/connectors/passkey.md` in the style of `docs/connectors/github.md`: what a passkey is and which devices hold one; Before you start (the public origin, HTTPS or `localhost`, passkeys are bound to the address); 1. Enable the connector (the card, the two modes, the default); 2. Invite someone (the note, the link, the trusted channel, 7 days, once); 3. What the person sees (name, device prompt, the passkeys page); Signing in; Adding a passkey to an account that uses a provider; Recovery (invite from the Users screen); Removing a passkey (the person, or you); What Umbral takes and stores (public key, counter, nickname; no attestation; nothing identifying the device beyond its model id); What an app receives (`idp`, no email, how to cope); Allowed email domains (do not apply; use the enrolment mode; the Apps warning); Gotchas (changing the domain, JavaScript, a security key that cannot store a passkey, synced passkeys and the counter, the rate limit, Chrome's virtual authenticator for testing). Link it from the README and from the other four walkthroughs' "Other providers" lines
- [ ] T035 [P] [US7] `docs/vision.md`: a short note that constitution 3.0.0 widened the scope to passwordless sign-in with a social provider or a passkey, dated 2026-10-19, pointing at spec 008. `CHANGELOG.md` under `## Unreleased`: **Added** (the passkey connector: enrol by invitation or open sign-up, sign in with a passkey, add passkeys to an account, recovery by a new invitation, the passkeys page at `/account/passkeys`, what apps receive, the walkthrough), **Security** (user verification required, single-use challenges, origin check, hashed single-use invitations, rate limits, the new security events), **Upgrade** (schema version 6; older images refuse the database; take a backup first; the memory figure)
- [ ] T036 Extend `tests/Umbral.Tests/IdleMemoryTests.fs` with the third run per research R13 and plan.md "Memory": seed the data directory (schema, a passkey connector enabled with `invitations`, one invitation with a known secret) before starting the child; complete a registration and a sign-in against the child with the software authenticator over HTTP (antiforgery token and cookie from `GET /invite/{secret}`); idle; assert under 160 MiB on Linux; print and append to the job summary. Run it locally and record the macOS figure in plan.md "Memory"; the Linux figures are recorded from CI's job summary after the first push
- [ ] T037 Extend `tests/Umbral.Tests/SecurityEventCoverageTests.fs` so the coverage journey emits the four new types and one `passkey_refused` detail, with the shape assertions (actor, user id, connector slug, detail words) for each
- [ ] T038 Run `dotnet build -c Release` and `dotnet test -c Release`; record the counts here; check every acceptance scenario of spec.md maps to a passing test; check MetaTests (every Texts field read, every endpoint registered, every SQL literal) and CsrfTests (the new POST routes are protected) pass without edits to their rules
- [ ] T039 Manual: walk [quickstart.md](./quickstart.md) "By hand" on a development instance with Chrome's virtual authenticator; note anything the documentation lacks and fix it

---

## Dependencies & Execution Order

- Phase 1 → Phase 2 → everything else.
- Phase 3 (US1 + US2) is the MVP and comes first; it needs T012, T013
  (verifier and authenticator) and T023 (the card creates the connector).
- Phase 4 (US5) needs Phase 3's invitation branch and `Users.row`.
- Phase 5 (US3) and Phase 6 (US4) need Phase 3's `Ceremonies.fs`; they touch
  different files except `Ceremonies.fs` and `Texts.fs`.
- Phase 7 (US6) needs Phases 4 and 5 (Users row, passkeys page).
- Phase 8: T033–T035 can be written from the contracts as soon as Phase 3
  passes; T036–T039 last.

### Parallel example

```text
after T015:  T016 → T017 → T018 (tests)  ‖  T019 → T020 → T021 ‖ T022  → T023 → T024
after T024:  worktree A: T025 → T026 → T031 → T032
             worktree B: T027 → T028 → T029 → T030
             worktree C: T033, T034, T035
then:        merge → T036 → T037 → T038 → T039
```

## Implementation Strategy

1. Phases 1–3: the MVP. An invited person signs in with a passkey; every
   attack is refused.
2. US5 (recovery) next: the operator's safety net.
3. US3 and US4 in parallel, then US6, then docs and the memory figure.
4. Each phase ends with the full suite green.
