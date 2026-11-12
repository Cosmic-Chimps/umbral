# Tasks: The Bluesky connector

**Input**: [plan.md](plan.md), [bluesky-connector.spec.md](bluesky-connector.spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/routes-and-screens.md](contracts/routes-and-screens.md), `design/contracts/bluesky-connector-ui.md`.

Paths are under `src/Umbral/` unless they start with `tests/`, `docs/`, `site/` or `changelog.d/`.

## Phase 1: Foundation

- [ ] **T001** The packages `Duende.IdentityModel.OidcClient.Extensions` 7.1.1 and `DnsClient` 1.8.0, with comments. Then:
  - the `Bluesky` kind wherever `Fediverse` is matched, in `Domains/Connectors.fs`, `ConnectorStore.fs`, `EmailDomainRules.fs` (NoRule), `Apps.fs` (limited iff Only these servers), and `Handlers/Admin/Connectors.fs`, compiling, with the pane still a placeholder;
  - the suite green.

  Files: `Umbral.fsproj`, `Domains/Connectors.fs`, `Infrastructure/*`, `Handlers/Admin/*`, test literals.
- [ ] **T002** Schema 12: `linked_accounts.provider_server`. Then `BlueskyAccounts.recordServer` and `recheckServer`, the re-check called in `Authorize.fs` and `Token.fs` beside the Fediverse re-check. Tests:
  - a version 11 database upgrades;
  - the column round-trips;
  - the re-check admits for Any, for a listed server and for a null server, and refuses an unlisted one.

  Files: `Infrastructure/Database.fs`, `BlueskyAccounts.fs` (new), `Handlers/Connect/Authorize.fs`, `Token.fs`, `tests/Umbral.Tests/DatabaseTests.fs`, `tests/Umbral.Tests/BlueskyAccountsTests.fs` (new).

## Phase 2: Pure rules

- [ ] **T003** `Domains/Bluesky.fs` (data model table) and `tests/Umbral.Tests/BlueskyDomainTests.fs`:
  - handle syntax and reserved names;
  - DID syntax;
  - TXT selection;
  - DID documents (id mismatch, `alsoKnownAs`, PDS service variants);
  - protected resource (0, 1 or 2 servers);
  - server metadata (each FR-010 refusal, issuer ≠ origin, `http` endpoints);
  - token answer checks;
  - `cleanName`, the avatar CID, `clientIdFor` (`https`, port, `http`, loopback with and without dev);
  - the metadata document's fields.

## Phase 3: The round trip and the client

- [ ] **T004** `Correlation.Extra` as an eighth encoded field. Payloads of six and seven fields still decode. Tests in `OAuthRoundTripTests.fs`.
- [ ] **T005** `Infrastructure/Providers/Bluesky.fs`, the `umbral-bluesky` client and the seams in `Program.fs`. Tests in `tests/Umbral.Tests/BlueskyClientTests.fs` with a stub handler:
  - `htu` without query or fragment;
  - a nonce retry at PAR and at the token request, and only once;
  - the proof's header `typ`, `alg` ES256 and a public-only `jwk`;
  - the `login_hint`;
  - `ath` absent.

## Phase 4: Sign-in (User Stories 1, 3, 4)

- [ ] **T006** `tests/Umbral.Tests/Infrastructure/FakeAtproto.fs`: DNS map, PLC, `did:web`, PDS, AS (PAR, authorize redirect, token, nonces, proof verification), and modes for every refusal.
- [ ] **T007** `Handlers/Connect/Bluesky.fs`: the step GET and POST, the callback (R6), the client metadata route. Also the Texts of the step and its errors, and the endpoints in `Program.fs`.
- [ ] **T008** `tests/Umbral.Tests/BlueskySignInTests.fs`, through the real pipeline. Sign-in:
  - a full sign-in by DNS handle and by HTTPS handle, the app getting `name`, `preferred_username`, `picture` and `profile`, with no `email`;
  - a moved handle gives the same `sub`;
  - a DID typed directly.

  The step refuses each case of the contract's table:
  - not a handle, a private address, not found, and a handle not claimed back;
  - refused, unreachable and unsupported servers;
  - a nonce refused twice.

  The callback refuses: a wrong `state`, a wrong or missing `iss`, a `server_not_allowed` change, the exchange failing, the token type and scope, `did_mismatch`, and **`not_authoritative`** (a server claiming another account's DID).

  The rest: the profile failing yields the handle as the name; the refresh re-check; the metadata document (404 when not set up, exact `client_id`); the cookie holds no readable key; no security event carries a code, a token or a key.

## Phase 5: The Connectors screen (User Story 2)

- [ ] **T009** `Handlers/Admin/BlueskyConnector.fs`; in `Connectors.fs`, order, dispatch, save route and notices; the Enable check in `ConnectorToggle.fs`; the origin-blocked pane; the Texts. Tests in `tests/Umbral.Tests/BlueskyAdminTests.fs`:
  - not set up, enable, list save, a refused entry, the empty list;
  - the origin blocked (pane, crafted save, crafted Enable);
  - the dev loopback note;
  - the order;
  - no antiforgery token, no change.
- [ ] **T010** The picker: the button, its link and the Last used badge, checked and tested (one test).

## Phase 6: Docs

- [ ] **T011** [P]
  - `docs/connectors/bluesky.md`;
  - Bluesky in the "Other providers" line of every walkthrough, the README's introduction and connector list, `site/sidebar.mjs`;
  - schema 12 in `docs/run/backup.md`;
  - `changelog.d/015-bluesky-connector.md` (Added, Upgrade).

## Phase 7: Verify

- [ ] **T012** The full suite green with 0 warnings; the idle-memory test with the connector enabled (figure in `notes.md`); the site builds; the changelog check; the walk at 1280 and 360, dark and light (design note 6); the three reviews.
