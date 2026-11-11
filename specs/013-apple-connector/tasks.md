# Tasks: Sign in with Apple

**Input**: [plan.md](plan.md), [apple-connector.spec.md](apple-connector.spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/routes-and-screens.md](contracts/routes-and-screens.md).

Paths are under `src/Umbral/` unless they start with `tests/`, `docs/`, `site/` or `changelog.d/`.

## Phase 1: Foundation

- [x] **T001** `Apple` kind (`Value`, `DisplayName`, `parseKind`, `endpointsFor`, `setupCaveat`), `AppleKeyIds`, `Connector.AppleKey`, `ProviderEndpoints.AnswersByFormPost`, `appleIssuer`, `parseAppleId`, `parseServicesId`; every `match` on the kind and every record literal in `src/` and `tests/` compiles; unit tests of the two parsers · `Domains/Connectors.fs`, `tests/Umbral.Tests/AppleDomainTests.fs`
- [x] **T002** Columns `apple_team_id`, `apple_key_id`; `UpgradeToVersion10`; `SchemaVersion = 10`; the store reads and writes them (create, update, save); tests: a version 9 database upgrades, a fresh one has the columns, an `apple` row round-trips, other kinds read `None` · `Infrastructure/Database.fs`, `Infrastructure/ConnectorStore.fs`, `tests/Umbral.Tests/DatabaseTests.fs`, `tests/Umbral.Tests/OidcConnectorStoreTests.fs` or a new file

## Phase 2: The round trip (User Story 3)

- [x] **T003** `ResponseMode`, the form-post cookie (name, purpose, `None; Secure`), `start`/`complete`/`peek`/`returnUrlOf` take the mode, `complete` takes the answer's `state` and `error`; state-machine tests for the form-post mode (happy path, wrong state spends the correlation, replay, expiry, slug, a correlation of one mode not redeemable in the other, cookie attributes) · `Infrastructure/OAuthRoundTrip.fs`, `tests/Umbral.Tests/OAuthRoundTripTests.fs`
- [x] **T004** `ExchangeRequest.PostedUser`; `Start` passes the mode of the resolved endpoints; `Callback`: one body for both modes, `POST /connect/{slug}/callback` with the form read (64 KB cap, a body that is not a form is an empty answer), the kind's mode checked against the request's (`response_mode_mismatch`), `user_cancelled_authorize` as declined; the `CsrfTests` module comment names the route · `Infrastructure/ProviderAdapters.fs`, `Handlers/Connect/Start.fs`, `Handlers/Connect/Callback.fs`, `tests/Umbral.Tests/CsrfTests.fs`

## Phase 3: The adapter (User Story 2)

- [x] **T005** `Oidc.fs`: `mapDiscovery`, `offersPkce`, `exchangeCode` public; `validateIdToken` extracted and public, returning the validated payload; `OidcAdapter` calls them; the OIDC tests pass unchanged · `Infrastructure/Providers/Oidc.fs`
- [x] **T006** `Apple.fs`: `normaliseKey` (PEM, P-256, fixed error word), `clientSecret` (claims of FR-009, five minutes, no cached key), `claimIsTrue`, `nameOf` (200 characters, no control characters, never the email), `check` (sign, then discovery), `AppleAdapter` (`Resolve` from discovery, `response_mode=form_post`, exchange with the signed secret, identity from the validated payload); registered in `Program.fs` · `Infrastructure/Providers/Apple.fs` (new), `Umbral.fsproj`, `Program.fs`
- [x] **T007** The fake Apple: discovery, keys and token endpoint at `https://appleid.apple.com` behind `PrimaryHandler`; it verifies the client secret against the test's public key and records it; it mints an `id_token` whose `email_verified` and `is_private_email` can be a string, a boolean, another value or absent; a helper that plays Apple's page and posts the form · `tests/Umbral.Tests/Infrastructure/FakeApple.fs` (new)
- [x] **T008** Tests: the client secret's header, claims and signature, a new one per request, no `nbf`; a key that is not PEM, not EC, or P-384 is refused with the fixed word · `tests/Umbral.Tests/AppleClientSecretTests.fs` (new)
- [x] **T009** Tests through the real pipeline with the fake Apple: the authorize URL; a full sign-in of a registered app; the first sign-in stores the name and the second keeps it; `email_verified` as `true`, `"true"`, `false`, `"false"`, `"TRUE"`, `1`, absent; a relay address; no email; Cancel; a wrong `state` (and the right one afterwards); no cookie; replay; a GET with the values in the query; a POST to a redirect-mode connector's callback; an `id_token` with a wrong signature, audience, issuer, nonce, or expired; `invalid_client`; a forged `user` field does not change the email; neither the key nor a client secret is in any security event · `tests/Umbral.Tests/AppleSignInTests.fs` (new)

## Phase 4: The Connectors screen (User Story 1)

- [x] **T010** The words and notice codes of the contract · `Views/Texts.fs`
- [x] **T011** `.um-textarea`; the Apple entry and pane (Setup section with the caveat, the Settings form, masked key, Replace); `saveApple` and its route (validation, 422 redraw with the key never drawn back, the key through the secret store with an empty hint, the Enable check at save, saved disabled when Apple is unreachable, the slug taken, stranded-apps and last-enabled rules as the other panes); the add form and `create` refuse the kind · `Handlers/Admin/AppleConnector.fs` (new), `Handlers/Admin/Connectors.fs`, `Assets/`, `Umbral.fsproj`
- [x] **T012** Enable runs `Apple.check` with the key read from the store; the three outcomes of the contract · `Handlers/Admin/ConnectorToggle.fs`
- [x] **T013** Tests: the pane not set up and set up; a save stores the row and the key and shows no character of it; each refusal (422, field marked, values kept, key absent from the page); replace and keep; saved disabled when Apple is unreachable; Enable refused for an unusable stored key and for Apple unreachable, accepted otherwise; Disable asks nothing; no second Apple connector through the add form or a crafted POST; no antiforgery token, no change · `tests/Umbral.Tests/AppleAdminTests.fs` (new)

## Phase 5: The button

- [x] **T014** `.um-btn-apple`; the picker draws the Apple connector with the fixed title, no link icon, the logo slot, never the accent, the "Last used" badge; one sentence in `DESIGN.md`; tests (title with another display name, the class with and without an accent, last used) · `Handlers/Connect/Picker.fs`, `Views/Components.fs`, `Views/Texts.fs`, `Assets/`, `DESIGN.md`, `tests/Umbral.Tests/`

## Phase 6: Docs

- [x] **T015** [P] `docs/connectors/apple.md`; Apple in the "Other providers" line of the six walkthroughs, in the README's introduction, connector row and list, in `docs/native-apps.md`, in `site/sidebar.mjs`; schema version 10 in `docs/run/backup.md`; `changelog.d/013-apple-connector.md` (Added, Upgrade) · `docs/`, `README.md`, `site/sidebar.mjs`, `changelog.d/`

## Phase 7: Verify

- [x] **T016** Full suite green, 0 warnings; the site builds; the changelog check passes; the walk at 1280 and 360, dark and light, for the design review; the three reviews
