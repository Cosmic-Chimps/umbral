# Research: The Passkey Connector

Decisions for the plan, each with what was rejected. Library facts were read
from the fido2-net-lib repository (`main`, 2026-10-19) and NuGet.

## R1. Library and version: fido2-net-lib 4.3.1, the `Fido2` package

**Decision**: `Fido2` 4.3.1 (NuGet, released 2026-10-16, MIT), pinned exactly.
It ships `net8.0` and `net10.0` targets and brings `Fido2.Models`,
`System.Formats.Cbor`, `NSec.Cryptography` (libsodium, native), 
`Microsoft.Extensions.Http` and `Microsoft.IdentityModel.JsonWebTokens` (the
last two are already in the graph through OpenIddict and the OIDC client).
The `Fido2.AspNet` package is not used: it only adds `AddFido2()` DI helpers
and a metadata-service cache, and Umbral constructs the `Fido2` object itself
(R2).

**Rationale**: the constitution names the library. 4.x is the current stable
line with the parameter-object API (`RequestNewCredentialParams`,
`MakeNewCredentialParams`, `GetAssertionOptionsParams`, `MakeAssertionParams`)
and nullable annotations. `5.0.0-preview.8` exists but is a prerelease and
Principle III wants a maintained, released verifier. Exact pin because the
verification error codes (`Fido2ErrorCode`) are what the reason words map
from (R6).

**Alternatives**: ASP.NET Core Identity's passkey support (rejected by the
constitution: no Identity store); WebAuthn.Net (smaller community, its own
storage model); hand-written verification (forbidden).

## R2. One lazily built verifier, configured from the public origin

**Decision**: a singleton `WebAuthn` service holding a `Lazy<Fido2>` built from
`Fido2Configuration` with `ServerDomain` = the public origin's host (the
relying-party id), `ServerName` = "Umbral", `Origins` = { the public origin's
text }, `ChallengeSize` = 32, `Timeout` = 300 000 ms, no metadata service.
Attestation preference `None`; `AuthenticatorSelection` with `ResidentKey =
Required`, `UserVerification = Required`; `Extensions.CredProps = true`.
Assertion options with no `AllowedCredentials` and `UserVerification =
Required`.

**Rationale**: `Fido2` is constructed from configuration and holds no state,
so one instance serves every request; building it lazily on the first
ceremony keeps the library (and libsodium) out of an instance that has no
passkey connector, which is how SC-008's 150 MiB run stays as it is. The
origin check and the RP id come from `UMBRAL__PUBLIC_ORIGIN` and never from
the request (FR-024), and the library compares `clientDataJSON.origin`
against `FullyQualifiedOrigins` exactly. `localhost` works as an RP id for the
laptop trial. `ResidentKey = Required` is the discoverable-credential decision;
`CredProps` lets the server see `rk` (R5). UV `Required` makes the library
throw `UserVerificationRequirementNotMet` when the UV flag is off, at both
ceremonies (verified in `AuthenticatorAttestationResponse.VerifyAsync` line
151 and `AuthenticatorAssertionResponse.VerifyAsync` line 235).

**Alternatives**: `Fido2.AspNet`'s `AddFido2` (eager construction, DI noise);
`UserVerification = Preferred` (would accept a passkey without PIN or
biometric: forbidden by Principle IV).

## R3. Ceremony state: a server-side row, single use by `DELETE … RETURNING`

**Decision**: a `passkey_ceremonies` table holds each pending ceremony: a
random id (the client echoes it), the purpose (`signin`, `signup`,
`invitation`, `add`), the serialised options (`options.ToJson()`), the context
(user id for `add`, invitation id for `invitation`, display name and nickname
typed, the `returnUrl`), and `expires_utc` = created + 5 minutes. Completing
a ceremony runs `DELETE FROM passkey_ceremonies WHERE id = @id AND expires_utc
> @now RETURNING …`: no row means unknown, used or expired, all refused the
same way. Expired rows are pruned on every insert.

**Rationale**: "single-use" needs state the client cannot replay. A
Data-Protected cookie can be deleted after use, but a captured
cookie-plus-response pair could still be replayed; a row that is deleted
atomically on first use cannot. The library then checks that the response's
challenge equals the stored options' challenge, so the row is the only place a
challenge can be honoured from. SQLite with one writer makes the atomic delete
trivial. A 5-minute lifetime is the library's default timeout and the spec's
maximum.

**Alternatives**: cookie only (replayable); looking the row up by a hash of the
challenge parsed out of `clientDataJSON` (works, but makes Umbral parse the
client data before the library does; the id is simpler and the library still
enforces the challenge).

## R4. The browser side: one extra embedded script, standard JSON shapes

**Decision**: a second embedded asset, `passkeys.js`, served like `umbral.js`
and linked only on the three pages that run a ceremony (the passkey sign-in
page, the invitation page, the passkeys page). It posts JSON to the ceremony
endpoints with the antiforgery token in the `RequestVerificationToken` header
(read from the page's hidden input). It uses
`PublicKeyCredential.parseCreationOptionsFromJSON`,
`parseRequestOptionsFromJSON` and `credential.toJSON()` when the browser has
them, with a small base64url fallback for browsers that do not. A user
cancellation (`NotAllowedError`, `AbortError`) returns to the page quietly;
any other failure shows the page's own text. CSP is unchanged
(`default-src 'self'`).

**Rationale**: fido2-net-lib's `CredentialCreateOptions` and
`AssertionOptions` serialise to the WebAuthn JSON form (`challenge`, `user.id`
and credential ids as base64url), and its `AuthenticatorAttestationRawResponse`
and `AuthenticatorAssertionRawResponse` deserialise exactly what `toJSON()`
produces (`id`, `rawId`, `type`, `authenticatorAttachment`,
`clientExtensionResults`, `response.{attestationObject, clientDataJSON,
transports, authenticatorData, signature, userHandle}`).
`clientExtensionResults` and `transports` are `[Required]` on the raw
response, so the fallback always sends them (`{}` and `[]`). A separate file
keeps the WebAuthn code off every other page; it is still "no build step, no
framework".

**Alternatives**: appending to `umbral.js` (every page would carry it);
inline script (forbidden by CSP); a framework (forbidden by the constitution).

## R5. Discoverable credentials, and what to do when the authenticator cannot

**Decision**: registration asks for `residentKey: required` and the `credProps`
extension. After verification, if `clientExtensionResults.credProps.rk` is
`false`, the registration is refused (`passkey_not_discoverable`) and nothing
is stored; if it is `true` or absent, the credential is accepted. Sign-in
sends no `allowCredentials`; the authenticator returns `userHandle`, which
Umbral uses to find the user, and the library's
`IsUserHandleOwnerOfCredentialIdCallback` confirms the credential belongs to
that user (the library also refuses a missing `userHandle` when
`allowCredentials` is empty).

**Rationale**: the spec's usernameless decision. `rk` is the only signal a
server gets about discoverability, and most browsers report it when asked.

**Alternatives**: `residentKey: preferred` (silently non-discoverable
credentials that can never sign in usernameless).

## R6. Mapping the library's refusals to closed reason words

**Decision**: every `Fido2VerificationException` is caught in one place and
mapped from its `Code` (`Fido2ErrorCode`) to a closed word for the security
event's `Detail`: `origin_mismatch`, `rpid_mismatch`, `challenge_mismatch`,
`user_verification_missing`, `user_presence_missing`, `invalid_signature`,
`sign_count_regressed`, `credential_not_unique`, `user_handle_mismatch`,
`type_mismatch`, `malformed_response`, and `verification_failed` for anything
else. Umbral's own refusals add `ceremony_unknown_or_expired`,
`unknown_credential`, `user_not_found`, `user_disabled`,
`connector_disabled`, `passkey_not_discoverable`, `passkey_limit_reached`,
`display_name_invalid`, `nickname_invalid`, `enrolment_closed`,
`invitation_unknown`, `invitation_used`, `invitation_expired`,
`invitation_revoked`, `invitation_user_gone`, `javascript_required`. The
detail is `passkey_refused;reason=<word>;stage=<registration|assertion|invitation>`.
The exception message is never written anywhere: the library includes the
offending origin in it.

**Rationale**: FR-031 and SC-007. The security log is the operator's only
diagnostic, and a closed vocabulary keeps it greppable and free of values.

## R7. Storage: schema version 6

**Decision**: three tables and one column, in `Database.schema` and an
`UpgradeToVersion6` step: `passkeys`, `passkey_invitations`,
`passkey_ceremonies`, and `connectors.passkey_enrolment TEXT NOT NULL DEFAULT
'invitations'`. Details in [data-model.md](./data-model.md). The credential id
and the public key are `BLOB`; the passkey row has its own random id, which is
what URLs carry (a credential id is a stable identifier of the person's
authenticator and should not sit in a form action).

**Rationale**: the schema is hand-written SQL in one file with append-only
upgrade steps (spec 004); every spec since has added a step. A fresh database
gets the newest shape from `schema` and skips the steps.

**Alternatives**: a settings row for the enrolment mode (would not be deleted
with the connector); storing credentials in `linked_accounts` (a passkey is
not a provider identity: it has a key, a counter and a nickname, and a user
may hold several).

## R8. The Passkey kind inside the connector model

**Decision**: `ProviderKind.Passkey` (`Value = "passkey"`, `DisplayName =
"Passkey"`) is added to the union. The connector row has empty `client_id`,
`client_secret_ref` and `client_secret_hint`, `NULL` issuer, an empty domain
list, and the new `passkey_enrolment`. `Connector` gains `PasskeyEnrolment:
PasskeyEnrolment` (`InvitationsOnly | OpenSignUp`, stored `invitations` |
`open`), meaningful only for this kind, like `IssuerUrl` for `oidc`.
`allKinds` stays the three provider cards; the Connectors screen adds the
Passkey row itself. No `IProviderAdapter` exists for the kind: `Start` and
`Callback` answer 404 for `/connect/passkey/callback` as for any kind without
an adapter, and `/connect/passkey/start` is a literal route of its own, which
endpoint routing prefers over the `{slug}` pattern. `EmailDomainRules.ruleFor`
returns `NoRule` for the kind, so the callback-equivalent, the authorize
re-check and the token re-check all admit it; the Apps warning and the
removal warning special-case the kind (spec FR-010). `SecretMove` and
`ConnectorDelete` must treat an empty secret reference as "nothing to move or
delete" (to verify in the implementation: both decode the reference first).
`mayCreate` answers false for `Passkey` so the generic save and add paths do
not create it; the passkey card posts to its own route.

**Rationale**: Principle I: a Connector kind, nothing new. Reusing the row
keeps `offeredTo`, the per-app choice, the stranded-apps warnings, enable and
delete, and the `connector` session claim working unchanged.

## R9. Invitations: a 256-bit secret, hashed with SHA-256, in the URL path

**Decision**: the secret is 32 random bytes as base64url (`Ids.random`-style);
the link is `{origin}/invite/{secret}`; the row stores `SHA-256(secret)` as
hex. Lifetime 7 days. Redemption is two steps: opening the link and starting
the ceremony only *read* the row (valid when unused, unrevoked, unexpired and
its user, if any, exists and is enabled); completing the registration runs,
in the same transaction as the passkey insert (and the user insert for a new
person), `UPDATE passkey_invitations SET used_utc = @now WHERE id = @id AND
used_utc IS NULL AND revoked_utc IS NULL AND expires_utc > @now`; zero rows
means the transaction rolls back and the ceremony is refused. The link is
shown once through the same single-use Data-Protected cookie pattern as an
app's secret (`Apps.SecretReveal`), with its own purpose and a 2-minute
lifetime.

**Rationale**: a high-entropy random secret needs no salt (the same reasoning
OpenIddict applies to reference tokens); the conditional update is what makes
"exactly one of two simultaneous redemptions succeeds" true. The URL path, not
the query, so the secret is not logged by proxies' query logging habits any
differently than other paths; Umbral's own request logging is capped (spec
001 SC-008), and the invite route's logging category is checked by the
redaction test.

**Alternatives**: a short code typed by hand (needs a lookup form, and a
6-digit code is guessable within the rate limit); email (excluded).

## R10. The user handle and what the authenticator shows

**Decision**: `Fido2User.Id` = UTF-8 bytes of `users.id` (the opaque random
`sub`); `Fido2User.Name` = the user's email when they have one, else the
display name; `Fido2User.DisplayName` = the display name. On a sign-in the
`userHandle` is decoded back to the user id.

**Rationale**: WebAuthn needs a stable, opaque handle with no personal data;
`sub` already is one and every app already has it. `name` is what an
authenticator shows in its account chooser, so it should be the thing the
person recognises.

**Alternatives**: a second random handle per user (one more column for no
gain).

## R11. The software authenticator for the tests

**Decision**: a test-side `SoftwareAuthenticator` type (F#, in
`tests/Umbral.Tests/Infrastructure/`) that holds an ECDSA P-256 key per
credential and produces: a registration response with a `none` attestation
object (CBOR map `{fmt: "none", attStmt: {}, authData}`, written with
`System.Formats.Cbor`, available transitively through the `Fido2` package),
authenticator data (`SHA-256(rpId)` ‖ flags ‖ counter ‖ AAGUID ‖ credential id
‖ COSE EC2 key `{1: 2, 3: -7, -1: 1, -2: x, -3: y}`), `clientDataJSON`
(`{type, challenge, origin}`), `clientExtensionResults` with `credProps.rk`;
and an assertion response signing `authData ‖ SHA-256(clientDataJSON)` with
DER-encoded ECDSA (`DSASignatureFormat.Rfc3279DerSequence`). Every field is
overridable: the origin, the RP id, the flags (UP, UV), the counter, the
`rk` value, the user handle, the challenge, the credential id, the key used to
sign. It never touches the network: tests call Umbral's JSON endpoints with
its output through the TestServer.

**Rationale**: the constitution requires the ceremonies to be tested through
the real endpoints with a software authenticator including a wrong origin, a
reused challenge and a missing UV. fido2-net-lib's own tests build the same
structures the same way (`Fido2Tests.Attestation`), so the shapes are known to
verify.

**Alternatives**: recorded fixtures (cannot follow a fresh challenge); a
headless browser (a dependency the suite does not have, and no virtual
authenticator without it).

## R12. Pages and routes

**Decision** (full contract in [contracts/ceremonies.md](./contracts/ceremonies.md)):

| Route | Who | What |
| --- | --- | --- |
| `GET /connect/passkey/start?returnUrl=` | anyone | the passkey sign-in page: "Continue with your passkey"; with open sign-up also the create-account form |
| `POST /passkey/assertion/options`, `POST /passkey/assertion` | anyone | sign-in ceremony (JSON) |
| `POST /passkey/registration/options`, `POST /passkey/registration` | depends on `mode` | registration ceremony (JSON): `signup` needs open sign-up, `invitation` needs a valid secret, `add` needs an Umbral session |
| `GET /invite/{secret}` | anyone with the link | the invitation page |
| `GET /account/passkeys`, `POST /account/passkeys/{id}/remove` | a signed-in user | the passkeys page |
| `POST /admin/connectors/passkey` | operator | create or save the connector (enabled, shared, enrolment) |
| `POST /admin/connectors/passkey/invitations`, `POST /admin/connectors/passkey/invitations/{id}/revoke` | operator | invitations for new people |
| `POST /admin/users/{id}/invite`, `POST /admin/users/{id}/passkeys/{pid}/remove` | operator | invitation for an existing user; remove a passkey |

Rate limit `RateLimits.SignIn` on everything under `/passkey/`, `/invite/`
and `/connect/passkey/start`. Antiforgery on every POST (header for JSON,
form field otherwise). The passkeys page redirects a visitor without a
session to `/signin?returnUrl=%2Faccount%2Fpasskeys`, which the picker and
every callback already honour as a local path.

**Rationale**: `/connect/{slug}/start` keeps the picker's button unchanged;
the JSON endpoints live outside `/connect/` so the CSRF discovery test holds
them to "no token, no change" like every other state-changing route; the
admin routes are literal and so beat the `{slug}` patterns.

## R13. Idle memory: measured in a third run of the idle test

**Decision**: `IdleMemoryTests` gains a third run, "passkey connector": the
test seeds the data directory with a passkey connector and an invitation
before starting the child process, completes one registration and one sign-in
through the child's HTTP endpoints with the software authenticator, idles, and
asserts under 160 MiB on Linux (the SC-006 ceiling), printing the figure to
the job summary like the other two. The local-store run is unchanged and must
stay under 150 MiB.

**Rationale**: SC-008 and the task's "measure it". NSec loads libsodium when
first used, so only a run that performs a ceremony measures the real cost.
The 160 MiB figure is the ceiling already accepted for an instance that loads
an extra client library; if the measurement comes in higher, the plan is
amended with the figure and the README says so, as spec 005 did.

## R14. Events

**Decision**: four new `SecurityEvents.EventType` cases: `PasskeyRegistered`
(`passkey_registered`, actor user, detail `via=signup|invitation|add`),
`PasskeyRemoved` (`passkey_removed`, actor user or operator),
`PasskeyInvitationCreated` (`passkey_invitation_created`, actor operator,
user id when for an existing user), `PasskeyInvitationRevoked`
(`passkey_invitation_revoked`). Existing types carry the rest: `user_created`
on enrolment, `sign_in_succeeded` after an assertion, `sign_in_failed` with
the R6 detail for every refusal, `connector_created|updated|enabled|disabled|
deleted` for the card. `SecurityEventCoverageTests` must see every new type
emitted.

**Rationale**: FR-031; the constitution calls adding and removing a passkey
a credential change.
