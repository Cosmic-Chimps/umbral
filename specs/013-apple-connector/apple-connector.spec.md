# Feature Specification: Sign in with Apple

**Feature Branch**: `alta/indigo-pheasant-5jd9` (worktree branch; spec directory `specs/013-apple-connector`)

**Created**: 2026-11-11

**Status**: Implemented; design, documentation and code reviews clear on 2026-11-11; not merged

**Input**: Task `2026-11-11-2-connector-sign-in-with-apple-ap1e`: "2. Connector: Sign in with Apple". The second of five tasks; tasks 3 and 4 add connectors too and start after this one merges.

## Background

Umbral has iOS, Android and .NET MAUI samples. An iOS app that offers Google or
GitHub sign-in falls under App Store Review Guideline 4.8 (Login Services, read
on 2026-11-11): an app that uses a third-party or social login service for the
person's primary account "must also offer as an equivalent option another login
service" that limits data collection to name and email, lets people keep their
email address private, and does not collect interactions for advertising without
consent. Sign in with Apple is the usual answer.

Apple is also the provider people find hardest to set up: the "client secret" is
a token to sign, the answer comes as a form post, the name comes once, and the
return URL cannot be `localhost`. A built-in connector with a walkthrough exists
to take that work off the operator.

## Decisions Already Taken

Decided in the task before this spec; not open:

- **Model (Principle I).** Apple is a Connector kind, built in. No new noun.
- **The client secret.** Umbral signs a short-lived one itself for each token
  request (minutes, not months), from the `.p8` key it keeps in the configured
  secret store like every other client secret. The operator never pastes or
  renews a token. Signed with `Microsoft.IdentityModel.*`, never hand-rolled.
- **The settings.** Team ID, Services ID, key ID, and the `.p8` key, which is
  written once and never shown again.
- **The Enable check.** Enabling signs a client secret and fetches Apple's
  discovery document and keys, so a malformed key fails at Enable and not at
  someone's first sign-in.
- **`id_token`.** Validated as for any OpenID Connect provider: issuer
  `https://appleid.apple.com`, audience the Services ID, `nonce`, expiry and the
  signature against the keys Apple publishes. The OIDC connector's code is
  reused where it fits.
- **Email.** `email_verified` and `is_private_email` are read whether Apple sends
  a string (`"true"`) or a boolean; anything else is false.
- **The name.** Apple sends it only on the first authorization, as a `user` form
  field. It is stored at that sign-in and never cleared because a later answer
  has none.
- **Tests** use a fake Apple, never real credentials.

## Clarifications

### Session 2026-11-11

Unattended run; decided by the implementing agent, with the screens settled by
`umbral-designer` (message of 2026-11-11, recorded in
[contracts/routes-and-screens.md](contracts/routes-and-screens.md)).

- Q: How does the sign-in's correlation survive Apple's cross-site POST? → A: In
  a cookie of its own, `SameSite=None; Secure`, minted only for a connector whose
  provider answers by form post, and redeemable only by that POST. Not in
  `state`: a value carried in `state` proves Umbral wrote it, not that this
  browser started the sign-in, and would allow one person's answer to be
  delivered into another person's browser (research R2).
- Q: How many Apple connectors? → A: One per instance, with the slug `apple`,
  like Passkey and Fediverse. Apple's button rules fix the button's title to
  "Sign in with Apple", so two Apple connectors offered by one app would be two
  identical buttons (designer's recommendation). A second Services ID is out of
  scope.
- Q: What does Umbral do with `is_private_email`? → A: Reads it by the same rule
  as `email_verified` and acts on nothing: a relay address is a verified address
  like any other (mail to it reaches the person), stored and compared as given.
  Nothing about it is stored.
- Q: Which scopes? → A: `name email`, the two values Apple documents. With any
  scope Apple requires `response_mode=form_post`.
- Q: PKCE? → A: Sent. Apple's documents do not mention it and its discovery
  document lists no methods, which Umbral reads as for any OpenID Connect
  provider (specs/002 research R5): silence means send. The checks Umbral relies
  on are `state`, the correlation cookie and `nonce` (research R5).
- Q: What is saved when Apple cannot be reached at Save? → A: The connector, but
  disabled, with the reason shown, as an OpenID Connect provider is (specs/002
  Clarification Q2). A key that is not usable is refused and nothing is saved.
- Q: Does the sign-in button follow Apple's button rules? → A: Yes, for this kind
  only (designer). Fixed title, black or white, never the app's accent colour.
  Apple's logo artwork is Apple's own file and is not in this change: the button
  ships with the title alone and a place for the file (research R8). Until the
  logo is in, the connector is not ready for an App Store submission, and the
  walkthrough says so.

## User Scenarios & Testing

### User Story 1 - Set up the Apple connector (Priority: P1)

An operator who has an Apple Developer Program membership creates an App ID, a
Services ID and a key on Apple's site, registers Umbral's domain and return URL
there, then enters four values on Umbral's Connectors screen and saves. The
connector is enabled and "Sign in with Apple" appears on the sign-in page.

**Why this priority**: without it nobody can sign in with Apple.

**Independent Test**: on a fresh instance with a fake Apple, save the four
values; the connector is stored enabled, the key is in the secret store and
nowhere else, and the sign-in page offers the button.

**Acceptance Scenarios**:

1. **Given** no Apple connector, **When** the operator opens **Apple** on the
   Connectors screen, **Then** the pane shows the return URL to register
   (`<public origin>/connect/apple/callback`), the caveat about https and the
   membership, and a form with Display name, Services ID, Team ID, Key ID and
   Private key (.p8).
2. **Given** the form filled with a usable key, **When** the operator saves and
   Apple is reachable, **Then** the connector is saved enabled, the key is
   written to the active secret store, and the pane shows the key as dots with no
   characters of it.
3. **Given** a Team ID or Key ID that is not ten letters and digits, an empty
   Services ID, or a key that is not a P-256 private key in PEM form, **When**
   the operator saves, **Then** nothing is saved, the form is drawn again
   (status 422) with the message under the field that is wrong and the other
   values kept; the pasted key is never drawn back.
4. **Given** a usable key and Apple unreachable, **When** the operator saves,
   **Then** the connector is saved disabled and the pane says why.
5. **Given** a saved connector, **When** the operator saves with the key field
   empty, **Then** the stored key is kept; with a new key pasted, the new key
   replaces it once it is known to sign, and the connector stays enabled or
   disabled as it was.
6. **Given** a disabled connector whose stored key cannot sign, or Apple
   unreachable, **When** the operator presses Enable, **Then** it stays disabled
   and the pane says why. Disable never asks Apple anything.

---

### User Story 2 - Sign in with Apple (Priority: P1)

A person presses "Sign in with Apple" on an app's sign-in page, approves at
Apple, and lands back in the app, signed in. The app receives an ordinary OIDC
user with a stable subject, the email Apple shared (their own or a relay
address) and, from the first sign-in on, their name.

**Why this priority**: it is the feature.

**Independent Test**: with the fake Apple, a full authorization code flow of a
registered app through the Apple connector ends with a code the app exchanges,
and the user's claims carry the email and the name.

**Acceptance Scenarios**:

1. **Given** an enabled Apple connector, **When** a person starts a sign-in,
   **Then** Umbral redirects to Apple's authorization endpoint with
   `response_type=code`, `response_mode=form_post`, `scope=name email`, the
   Services ID, the return URL, `state`, `nonce` and a PKCE challenge.
2. **Given** Apple's form post with a valid `code` and the `state` Umbral sent,
   **When** it reaches `POST /connect/apple/callback` from the browser that
   started the sign-in, **Then** Umbral exchanges the code with a client secret
   it signed for this request, validates the `id_token`, signs the person in and
   redirects to the app's authorization request.
3. **Given** a first sign-in whose form post carries
   `user={"name":{"firstName":"Maria","lastName":"Ruiz"},…}`, **Then** the user's
   name is "Maria Ruiz". **Given** a later sign-in with no `user` field,
   **Then** the name is still "Maria Ruiz".
4. **Given** an `id_token` with `email_verified` as the boolean `true` or the
   string `"true"`, **Then** the address is verified. **Given** `false`,
   `"false"`, any other value, or no claim, **Then** it is not, and the person
   is never joined to another user by that address.
5. **Given** a relay address (`…@privaterelay.appleid.com`, `is_private_email`
   true), **Then** the person signs in and the address is stored and released to
   apps as their email.
6. **Given** the person presses Cancel at Apple (a form post with
   `error=user_cancelled_authorize`), **Then** they land on the app's sign-in
   page with the message for a cancelled sign-in, and nothing is stored.

---

### User Story 3 - A forged or replayed answer is refused (Priority: P1)

Nobody can finish a sign-in they did not start in that browser, and no answer
works twice.

**Independent Test**: POSTs to the callback with a wrong `state`, without the
correlation cookie, twice, or with a GET, all end at the sign-in page with the
generic failure, create no session and are recorded.

**Acceptance Scenarios**:

1. **Given** a sign-in in progress, **When** the callback receives a POST whose
   `state` is not the one Umbral sent, **Then** the sign-in is refused, the
   correlation is gone (a second POST with the right `state` is refused too),
   no session exists, and the security log says `state_mismatch`.
2. **Given** no correlation cookie (another browser, or a forged cross-site
   POST), **Then** the POST is refused with `no_correlation_cookie`.
3. **Given** an answer already redeemed, **When** it is posted again, **Then**
   it is refused.
4. **Given** a correlation minted for Apple's form post, **When** the answer
   arrives as a GET with the same values in the query, **Then** it is refused;
   and a POST to the callback of a connector that answers by redirect is
   refused.
5. **Given** an `id_token` signed by another key, for another audience, from
   another issuer, expired, or with another `nonce`, **Then** the sign-in is
   refused and the log names which.

### Edge Cases

- The `user` field is not JSON, has no name, or is very long: the sign-in
  succeeds without a name (or with it cut to 200 characters); control characters
  are dropped. The `email` inside `user` is never read.
- The first authorization was spent on a sign-in that failed afterwards (Apple
  sends the name once per person and Services ID): the user is created as
  "Apple user". The walkthrough says how the person can make Apple send it again.
- No `email` claim (Sign in with Apple at Work & School may send none): the
  person signs in with no email, as a GitHub user with a private address does.
- The body of the POST is not a form or is larger than 64 KB: refused as an
  answer with nothing in it; the correlation is still spent.
- The key is deleted or unreadable in the secret store at sign-in: the existing
  `connector_secret_unreadable` path.
- Apple answers `invalid_client` at the token endpoint (wrong Team ID or Key ID,
  a revoked key, a Services ID that does not match): recorded as
  `rejected: invalid_client`; the walkthrough's troubleshooting names the causes.
- The connector is disabled or deleted between the redirect out and the POST:
  `connector_unavailable`, as today.
- The slug `apple` is already taken by a connector of another kind: saving is
  refused with the existing "slug taken" notice, as for Fediverse.
- The instance's public origin is http: the correlation cookie is `Secure`, so a
  browser does not keep it and the sign-in cannot finish. Apple refuses such a
  return URL before that; the pane's caveat and the walkthrough say so.

## Requirements

### Functional Requirements

**The kind and its settings**

- **FR-001**: There MUST be a built-in connector kind `apple`, display name
  "Apple", at most one per instance, with the slug `apple`.
- **FR-002**: The connector MUST store the Services ID (as its client ID), the
  Team ID and the Key ID on its row, and the `.p8` key in the active secret
  store through the same path as a client secret. No characters of the key are
  kept as a hint.
- **FR-003**: Team ID and Key ID MUST each be exactly ten ASCII letters or
  digits after trimming; the Services ID MUST be non-empty with no whitespace.
- **FR-004**: The key MUST be accepted only if it is a PEM private key on the
  P-256 curve. A refusal MUST NOT echo any part of what was pasted, on the
  screen or in a log.
- **FR-005**: A refused Save MUST store nothing and redraw the form with status
  422, the reason under the field it is about, and every typed value kept except
  the key.
- **FR-006**: Saving a new connector MUST run the Enable check (FR-008). If
  Apple cannot be reached, the connector MUST be saved disabled and the pane
  MUST say why. Replacing the key of an existing connector MUST check that the
  new key signs and MUST leave the connector enabled or disabled as it was: a
  moment in which Apple cannot be reached must not switch off a connector that
  works.
- **FR-007**: The Apple kind MUST NOT be offered by the add form, and a POST
  that tries to create a second connector of the kind MUST be refused.

**The Enable check**

- **FR-008**: Enabling MUST read the key from the secret store, sign a client
  secret with it, and fetch Apple's discovery document and signing keys. Any
  failure MUST leave the connector disabled and say which step failed. Disabling
  MUST NOT make any of these calls.

**The client secret**

- **FR-009**: For every token request Umbral MUST sign a new client secret: a
  JWT with header `alg` `ES256` and `kid` the Key ID, and claims `iss` the Team
  ID, `sub` the Services ID, `aud` `https://appleid.apple.com`, `iat` now and
  `exp` five minutes later, and no other claim.
- **FR-010**: The key MUST be read from the secret store at the moment of use
  and held nowhere else. Neither the key nor a signed client secret may appear
  in a log, a security event, a page or an error message.

**The round trip**

- **FR-011**: The authorization request MUST carry `response_type=code`,
  `response_mode=form_post`, `scope=name email`, `client_id`, `redirect_uri`
  (from the public origin), `state`, `nonce`, and a PKCE S256 challenge.
- **FR-012**: For a connector whose provider answers by form post, the
  correlation (`state`, `nonce`, PKCE verifier, slug, return URL, issue time)
  MUST be kept in a cookie of its own that is `HttpOnly`, `Secure`,
  `SameSite=None`, limited to `/connect`, encrypted with the data-protection
  keys under a purpose of its own, and ten minutes long. The redirect-mode
  cookie MUST stay `SameSite=Lax`.
- **FR-013**: `POST /connect/{slug}/callback` MUST accept an answer only when
  the form-post correlation cookie is present and decrypts, the posted `state`
  equals the stored one (compared in constant time), the correlation has not
  been redeemed, the slug matches, it is not older than ten minutes, and the
  connector's kind answers by form post. The cookie MUST be deleted before any
  of this is judged.
- **FR-014**: A correlation minted for one response mode MUST NOT be redeemable
  in the other. Single use MUST hold for callbacks that arrive at the same
  moment: exactly one of them is accepted.
- **FR-015**: The POST MUST NOT require an antiforgery token, MUST be rate
  limited like the GET callback, and MUST read a body of at most 64 KB.
- **FR-016**: `error=user_cancelled_authorize` MUST be treated as the person
  declining, like `access_denied`, and only when the answer carries the right
  `state`: a provider's `error` MUST NOT be believed before `state` has been
  compared, in either response mode. The error word written to the security log
  MUST be lower-case letters and underscores, at most 64, else the word `other`
  (code review B1).

**The identity**

- **FR-017**: The `id_token` MUST come from Apple's token endpoint only and MUST
  be validated before anything is read from it: signature against the keys in
  Apple's discovery document (asymmetric algorithms only), issuer
  `https://appleid.apple.com`, audience the Services ID, expiry, and `nonce`
  equal to the one sent. An unknown `kid` forces one throttled refresh of the
  keys, as for an OpenID Connect provider.
- **FR-018**: The person's identity MUST be the token's `sub`. The email MUST be
  the token's `email` claim, never the `user` form field's.
- **FR-019**: `email_verified` and `is_private_email` MUST be true only for the
  JSON boolean `true` or the JSON string `"true"` (exactly); every other value,
  type or absence is false.
- **FR-020**: The name MUST be read from the `user` form field
  (`name.firstName`, `name.lastName`) only after the `id_token` validated, cut
  to 200 characters, with control and format characters removed. An answer without a
  usable name MUST leave a stored name as it is.

**The screens**

- **FR-021**: The Connectors screen MUST show the Apple entry and pane as
  [contracts/routes-and-screens.md](contracts/routes-and-screens.md) says.
- **FR-022**: The sign-in button of the Apple connector MUST have the fixed
  title "Sign in with Apple", black with white text in the light theme and white
  with black text in the dark theme, never the app's accent colour, the same
  size as the other buttons, with the "Last used" badge like any other.

**Docs**

- **FR-023**: There MUST be a walkthrough `docs/connectors/apple.md` in the
  shape of the others, listed in every walkthrough's "Other providers" line, the
  README, `docs/native-apps.md` and the site's sidebar; a `changelog.d/` file
  with the feature under Added and the schema step under Upgrade.

### Key Entities

- **Connector (kind `apple`)**: Services ID, Team ID, Key ID, a reference to the
  key in a secret store. See [data-model.md](data-model.md).
- **Form-post correlation**: the same values as today's correlation, in a
  second cookie.

## Success Criteria

- **SC-001**: An operator with the four values from Apple's site sets the
  connector up in one Save, with no token to generate and nothing to renew.
- **SC-002**: A person signs in with Apple through a registered app in the fake
  Apple flow, and the app's user has the email and, from the first sign-in on,
  the name.
- **SC-003**: Every forged, replayed or mismatched answer in User Story 3 is
  refused, creates no session and is in the security log.
- **SC-004**: The key appears in no response body, log line or security event
  in the test suite (asserted for the admin pane, the refusals and the sign-in).
- **SC-005**: The whole test suite passes with no real Apple credentials.

## Out of Scope

- A second Apple connector (a second Services ID) on one instance.
- Apple's logo artwork in the button (research R8; follow-up for the
  maintainer).
- Apple's server-to-server notifications (consent revoked, account deleted,
  email forwarding changed) and token revocation.
- Registering outbound email sources for the relay service: Umbral sends no
  mail. The walkthrough tells an app owner that their own mail to relay
  addresses needs it.
- Native Sign in with Apple inside an iOS app (the App ID as the audience): apps
  sign in through Umbral in the browser, as `docs/native-apps.md` describes.
