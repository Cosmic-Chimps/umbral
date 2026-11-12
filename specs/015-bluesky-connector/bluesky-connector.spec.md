# Feature Specification: The Bluesky connector

**Feature Branch**: `alta/snowy-eagle-vdg4` (worktree branch; spec directory `specs/015-bluesky-connector`)

**Created**: 2026-11-12

**Status**: Implemented; design, documentation and code reviews clear on 2026-11-12; not merged

**Input**: Task `2026-11-11-3-connector-bluesky-atproto-b5ky`: "3. Connector: Bluesky (AT Protocol OAuth), after a feasibility check". It is the third of five tasks. Step 0, the feasibility check, passed on 2026-11-12 ([feasibility](../../.alta/reviews/2026-11-11-bluesky/feasibility.md)), and the code reviewer agreed with it the same day.

## Background

The Fediverse connector (spec 010) lets a person sign in with a Mastodon or
GoToSocial account, finding their server as they sign in. Bluesky is the other
half of the open social web. Its accounts live on many servers too, from
Bluesky's own to any self-hosted PDS, and they are named by a handle that is a
domain name: `alice.bsky.social`, or `alice.example.com` for someone who owns
`example.com`. "Sign in with your handle" is the same idea as on the Fediverse.

Bluesky speaks the AT Protocol's profile of OAuth, which differs from
everything Umbral talks to today:

- every token request is bound to a key the client holds (DPoP);
- the authorization request is pushed to the server first (PAR);
- there is no `id_token`: the account's identity, a DID, comes in the token
  response;
- the client is named by the URL of a document it serves about itself.

This feature adds a built-in connector kind, **Bluesky**, that the operator turns
on once. A person types their handle. Umbral finds the server their account
lives on and the server it signs in at, sends them there, checks the answer
against the account's own published identity, and every app behind Umbral
receives that person as an ordinary OIDC user.

## Decisions Already Taken

Decided in the task, the feasibility check, and the code reviewer's answer to it
(2026-11-12). Not open:

- **Model (Principle I).** Bluesky is a Connector kind, built in like Fediverse:
  one per instance, slug `bluesky`. A server is not a concept the operator
  manages. No new noun.
- **Libraries (Principle III).** The DPoP proofs come from
  `Duende.IdentityModel.OidcClient.Extensions` (its proof factory only).
  Umbral registers as a **public client**, so there is no client assertion.
  There is no `id_token`, and the access token is never read. Umbral
  constructs, signs and validates no JWT. Handle lookups in DNS use `DnsClient`.
- **Identity.** The person is their **DID**, never their handle: a handle can
  pass to someone else, a DID can't. The stored link is the DID.
- **The authority check.** Before a person is signed in, Umbral resolves the DID
  in the token response again: from its DID document, to the account's server,
  to the one authorization server that server names. That authorization server
  must be the one that issued the token. Without this check, a server could
  claim anyone's DID.
- **Handles are checked both ways.** The handle must lead to the DID, and the
  DID document must name the handle back.
- **No email.** Bluesky gives none: no `email`, no `email_verified`. A Bluesky
  identity never joins another user by email, and allowed email domain rules
  don't apply to it.
- **Network rules.** Every fetch the connector makes goes through the Fediverse
  connector's guarded client: public addresses only unless the development
  setting allows otherwise, no redirects, a time limit and a size limit. That
  covers the handle's well-known address, the DID document, and the server
  metadata.
- **The DPoP key** is new for every sign-in attempt. It travels only inside the
  attempt's encrypted correlation cookie and is never written to the data
  directory. The cookie dies with the attempt.

## Clarifications

### Session 2026-11-12

Unattended run, decided by the implementing agent. The screens are settled by
`umbral-designer` and recorded in
[contracts/routes-and-screens.md](contracts/routes-and-screens.md).

- **Q:** What does the person type? **A:** A handle (`alice.bsky.social`,
  `@alice.bsky.social`, `alice.example.com`) or a DID (`did:plc:…`,
  `did:web:…`). A bare server is not accepted: `bsky.social` is itself a valid
  handle, so "a server" can't be told apart from "an account".
- **Q:** What does "Only these servers" list? **A:** The hosts of authorization
  servers, the place a person signs in at. Every Bluesky-hosted account signs
  in at `bsky.social`, while its data sits on one of many
  `*.host.bsky.network` hosts. Listing data hosts would be unusable, and
  `bsky.social` is the name the operator knows. A self-hosted PDS is usually
  its own authorization server.
- **Q:** Is the list checked again after sign-in? **A:** Yes, as for the
  Fediverse (spec 010's B4): at a remembered sign-in and at a token refresh,
  against the authorization server the person last signed in through. That
  server is stored with the link, because the DID does not contain it.
- **Q:** Which claims does an app get? **A:** Under `profile`:
  - `name`: the profile's display name, or the handle when there is none;
  - `preferred_username`: the handle;
  - `picture`: the avatar, when there is one;
  - `profile`: `https://bsky.app/profile/<did>`.

  No `email`. The display name and avatar are read from the person's public
  profile record on their own server, which needs no scope beyond `atproto`.
  When it can't be read, the sign-in goes on without it.
- **Q:** What does `idp` say? **A:** `bluesky`, the connector's slug, as for
  every connector.
- **Q:** What if the instance isn't at a public `https` origin without a port?
  **A:** AT Protocol fetches the client's metadata from its `client_id` URL,
  and that URL can't carry a port. So the pane shows why the connector can't be
  enabled there, and Enable is refused. The one exception is development: with
  the development setting on and a loopback origin, Umbral uses AT Protocol's
  `http://localhost` development client, so a real walk is possible on a
  laptop.
- **Q:** Which DID methods? **A:** `did:plc` (from the PLC directory) and
  `did:web` (from the domain's `/.well-known/did.json`, hostname only), the two
  AT Protocol allows. Any other method is refused as "not a handle".
- **Q:** The PLC directory's address? **A:** `https://plc.directory`, fixed. It
  can be changed only by configuration (`UMBRAL:BLUESKY_PLC_DIRECTORY`), for
  tests and private mirrors. It is not on the screen.
- **Q:** What does the connector keep after sign-in? **A:** Nothing from the
  server: the tokens are used once and dropped. Bluesky never learns about a
  sign-out.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign in to an app with a Bluesky handle (Priority: P1)

Alice has the handle `alice.bsky.social`. She opens an app behind Umbral, presses
**Sign in with Bluesky**, types her handle and presses Continue. Bluesky asks her
to sign in and to let Umbral identify her. She agrees, and she is back in the
app, signed in, with her display name and avatar. A month later she moves to her
own domain, `alice.example.com`. She signs in with the new handle, and she is the
same user.

**Why this priority**: It is the feature.

**Independent Test**: With the connector enabled and offered to an app, sign in
through a fake PDS and a fake authorization server, then complete the app's OIDC
flow. The app gets `name`, `preferred_username`, `picture` and `profile`, and no
`email`. A second sign-in under a different handle for the same DID gives the
same `sub`.

**Acceptance Scenarios**:

1. **Given** the connector is enabled and offered to an app, **When** a person
   opens that app's sign-in page, **Then** it shows a "Sign in with Bluesky"
   button in the same style and place as the other connectors, with the Last
   used badge when it applies.
2. **Given** the person presses it, **When** the step opens, **Then** it shows
   one field for the handle, a Continue button, and a link back to the other
   ways when the app offers others. It works with JavaScript off.
3. **Given** a handle that resolves and is confirmed by its DID document,
   **When** the person continues, **Then**:
   - Umbral pushes the authorization request to the account's authorization
     server, with PKCE, DPoP, the `atproto` scope and the handle as
     `login_hint`;
   - it then sends the browser there.
4. **Given** the person agrees at their server, **When** they come back,
   **Then** Umbral checks the answer, exchanges the code with a DPoP proof
   (retrying once with the server's nonce), confirms the DID and its authority,
   reads the public profile, creates or finds the user by DID, and signs them in.
5. **Given** the same DID signs in again under a new handle, **When** the
   sign-in completes, **Then** it is the same user, and `preferred_username`
   follows the new handle.

---

### User Story 2 - The operator turns the connector on and chooses which servers (Priority: P1)

Jo runs Umbral at `https://auth.example.org`. On the Connectors screen Jo selects
**Bluesky**, reads the client metadata address and the no-email note, keeps **Any
server** and presses **Enable Bluesky**. Later Jo limits it to Bluesky's own
hosting and the group's self-hosted PDS: **Only these servers**,
`bsky.social, pds.example.org`.

**Why this priority**: Without the operator, no one can use it.

**Independent Test**: Enable the connector with Any server, then switch to Only
these servers with two entries. A sign-in through a listed authorization server
works. One through another server is refused with the neutral message, and so is
a remembered session or a refresh after the list stops naming its server.

**Acceptance Scenarios**:

1. **Given** the connector is not set up, **When** the operator opens the
   Connectors screen, **Then** the list shows a Bluesky entry after Fediverse
   and before Passkey, marked "Not set up".
2. **Given** the operator opens it, **When** the pane shows, **Then** it has:
   - the set-up facts: what it is, the client metadata address with copy (the
     one address servers fetch; the operator registers nothing anywhere), and
     the no-email note;
   - the settings: shared, Any server or Only these servers, the Servers
     field, and Save;
   - an Enable Bluesky button.
3. **Given** the operator saves Only these servers with
   `BSKY.social, pds.example.org`, **When** the screen comes back, **Then** the
   list is stored and shown lower-case and normalised. An entry that is not a
   server address, or has a wildcard, is refused, naming the entry.
4. **Given** the instance's public origin is `http://…` or carries a port, and
   the development setting is off, **When** the operator opens the pane,
   **Then** it shows the lead and a warning saying why Bluesky can't work at
   that address, with no settings form and no actions, and a crafted Enable or
   save is refused. If an enabled connector later finds itself at such an
   address, its entry carries the attention marker, the pane shows the same
   warning above its sections, and the step sends people to the sign-in error
   page.
5. **Given** Only these servers is chosen, **When** a person whose account signs
   in at another server continues, **Then** Umbral stops before contacting that
   authorization server, and shows "Accounts on {server} cannot sign in here.
   Use another way to sign in."
6. **Given** the operator enables, disables or deletes the connector, **When**
   it is the last way into an app, **Then** the usual warnings apply.

---

### User Story 3 - Problems the person can fix are explained on the step (Priority: P2)

Ben types `ben` (not a domain), then `nobody.example.com` (a domain with no
account), then a handle whose server is down. Each time the step comes back with
what he typed, the field marked, and one sentence under it saying what to do.

**Why this priority**: Handles are domain names; typos and dead servers are
common.

**Independent Test**: Post each bad input to the step and check the page
re-renders with the right message under the field and the typed value kept.

**Acceptance Scenarios**:

1. **Given** the input is not a handle or a DID, **When** the person continues,
   **Then** the step says to enter a handle, with an example.
2. **Given** the handle resolves to no DID, or the DID document does not name
   the handle back, or the DID can't be read, **When** the person continues,
   **Then** the step says the handle wasn't found and to check its spelling.
3. **Given** a server on the way (the handle's domain, the PLC directory, the
   PDS, the authorization server) does not answer in time or answers with an
   error, **When** the person continues, **Then** the step names that server
   and says to try again in a moment.
4. **Given** the handle's domain or any server on the way resolves to a private,
   loopback or link-local address, and the development setting is off, **When**
   the person continues, **Then** the step shows the "not a handle" message, and
   nothing is sent to that address.
5. **Given** the authorization server lacks what AT Protocol requires (PAR,
   S256, ES256 DPoP, the `iss` parameter, the `atproto` scope, client metadata
   documents), **When** the person continues, **Then** the step says "The
   server for {handle} does not offer the sign-in Umbral uses. Use another way
   to sign in."
6. **Given** the person refuses at their server, or the callback's `state` or
   `iss` is wrong, **When** they come back, **Then** they see the existing
   sign-in error page.

---

### User Story 4 - A server can't claim someone else's identity (Priority: P1)

Mallory runs an authorization server and makes it answer with Alice's DID.
Signing in through it never makes anyone Alice.

**Why this priority**: It is the one check that, missing, hands every account to
any server.

**Independent Test**: A fake authorization server that issues a token response
naming a DID whose document points at a different PDS and authorization server.
The sign-in is refused, the refusal is in the security log, and no user is
created or linked.

**Acceptance Scenarios**:

1. **Given** the token response's `sub` differs from the DID resolved at the
   start, **When** the callback runs, **Then** the sign-in is refused.
2. **Given** `sub` equals the DID resolved at the start, but resolving it again
   now names a different authorization server, **When** the callback runs,
   **Then** the sign-in is refused.
3. **Given** the redirect's `iss` differs from the authorization server the
   attempt started with, **When** the callback runs, **Then** the sign-in is
   refused before any code exchange.
4. **Given** the token response lacks the `atproto` scope, or its `token_type`
   is not `DPoP`, **When** the callback runs, **Then** the sign-in is refused.

### Edge Cases

- **The server wants a nonce.** It answers the PAR or token request with
  `use_dpop_nonce` and a `DPoP-Nonce` header. Umbral retries once with that
  nonce. A second refusal ends the attempt with the "did not answer" message (at
  PAR) or the error page (at the token request).
- **The handle's domain redirects.** Umbral follows no redirect. The DNS TXT
  answer and the well-known answer are the only sources.
- **DNS and HTTPS disagree** on the handle's DID: the DNS TXT record wins, as AT
  Protocol says. When both fail, the handle is not found.
- **The DID document names several PDSes, or none:** only the
  `#atproto_pds` service of type `AtprotoPersonalDataServer` counts; none
  means the handle is not usable.
- **The resource metadata names zero or several authorization servers:**
  unsupported server.
- **The authorization server's `issuer` differs from the origin it was fetched
  from:** unsupported server.
- **The connector is disabled** between the step and the callback: the callback
  fails as for any disabled connector.
- **The operator narrows the list** while a person is at their server: the
  callback refuses them.
- **Only these servers with an empty list:** nobody can sign in, and the pane
  says so.
- **A DID with upper-case letters or a trailing dot in the handle:** handles
  are compared lower-case without a trailing dot. DIDs are compared exactly.
- **A profile record that is too large, malformed or absent:** the sign-in goes
  on with the handle as the name and no picture.
- **The display name holds control or format characters**, or is longer than
  the cap: it is cleaned and cut as Apple's `nameOf` does.

## Requirements *(mandatory)*

### Functional Requirements

**The connector**

- **FR-001**: Umbral MUST offer a built-in connector kind "Bluesky", at most one per instance, with the fixed slug `bluesky`. The operator sets it up, enables, disables, shares and deletes it on the Connectors screen, like the Fediverse connector.
- **FR-002**: The Bluesky connector MUST carry a server scope, Any server (the default) or Only these servers, with a list of authorization server hosts. The list is normalised and refused as the Fediverse list is.
- **FR-003**: The Connectors screen MUST show the Bluesky entry after Fediverse and before Passkey, with a pane as the contract describes.
- **FR-004**: Enabling the connector MUST be refused, with the reason on the pane, when the public origin is not `https` without a port. The one exception is a loopback origin with the development setting on.
- **FR-005**: Umbral MUST serve its client metadata document at `/connect/bluesky/client-metadata.json` while the connector is set up, and answer 404 otherwise. The document's `client_id` MUST be exactly that document's public URL. It names:
  - the callback as its only redirect URI;
  - scope `atproto`;
  - grant type `authorization_code` and response type `code`;
  - `token_endpoint_auth_method` `none`, `dpop_bound_access_tokens` `true` and `application_type` `web`;
  - `client_name` "Umbral" and `client_uri` the public origin.

**Starting a sign-in**

- **FR-006**: When the connector is enabled and offered to the app, the sign-in page MUST show an ordinary "Sign in with Bluesky" button that leads to the step page.
- **FR-007**: The step MUST accept a handle (with or without a leading `@`, compared lower-case without a trailing dot) or a `did:plc`/`did:web` DID. It keeps the app's chrome and return address, carries antiforgery protection, and works without JavaScript.
- **FR-008**: For a handle, Umbral MUST resolve the DID from the `_atproto` DNS TXT record, falling back to `https://<handle>/.well-known/atproto-did`. It MUST then confirm that the DID document names `at://<handle>` in `alsoKnownAs`, and treat a mismatch as "not found".
- **FR-009**: Umbral MUST read the DID document (`did:plc` from the PLC directory, `did:web` from `https://<host>/.well-known/did.json`) and check that its `id` equals the DID. It takes the PDS from the `#atproto_pds` service, then the single authorization server from the PDS's `/.well-known/oauth-protected-resource`. That server's metadata comes from `/.well-known/oauth-authorization-server`, and its `issuer` MUST equal the origin it was fetched from.
- **FR-010**: Umbral MUST refuse an authorization server whose metadata lacks any of:
  - `require_pushed_authorization_requests` true, with a PAR endpoint;
  - `S256` among the code challenge methods;
  - `ES256` among the DPoP algorithms;
  - `authorization_response_iss_parameter_supported` true;
  - `atproto` among the scopes;
  - `client_id_metadata_document_supported` true;
  - `none` among the token endpoint auth methods.
- **FR-011**: Every fetch in FR-008 and FR-009, and in the profile read, MUST go through the guarded client: no private, loopback or link-local address unless the development setting allows it, no redirects, a time limit, and a size limit.
- **FR-012**: When the scope is Only these servers, Umbral MUST refuse an authorization server not on the list before sending it anything.
- **FR-013**: Umbral MUST push the authorization request with:
  - a fresh ES256 DPoP key and proof;
  - PKCE S256;
  - a single-use `state`;
  - the `atproto` scope, the exact callback, and the typed identifier as `login_hint`.

  It MUST retry once with the server's DPoP nonce on `use_dpop_nonce`. It then redirects the browser with only `client_id` and `request_uri`.
- **FR-014**: The attempt's correlation (state, PKCE verifier, issuer, token endpoint, the DID resolved at the start, the handle, the DPoP private key and the last nonce) MUST travel only in the encrypted correlation cookie, which expires with the attempt.
- **FR-015**: Every problem the person can fix MUST re-render the step with the typed value kept, the field marked invalid, the message under it, and focus on the field.

**Completing a sign-in**

- **FR-016**: On return, Umbral MUST check `state` (single use, bound to the `bluesky` slug), and that `iss` equals the issuer stored in the correlation. It does both before any exchange.
- **FR-017**: Umbral MUST exchange the code at the stored token endpoint, with the PKCE verifier and a DPoP proof made with the stored key, retrying once on `use_dpop_nonce`. Each proof's `htu` is the endpoint URL without query or fragment.
- **FR-018**: Umbral MUST refuse a token response:
  - without `sub`;
  - whose `scope` lacks `atproto`;
  - whose `token_type` is not `DPoP`;
  - whose `sub` differs from the DID resolved at the start.
- **FR-019**: Umbral MUST resolve `sub` again (DID document, then PDS, then its single authorization server and that server's metadata) and refuse the sign-in unless that server's `issuer` equals the stored issuer.
- **FR-020**: The callback MUST re-check the server scope against the issuer and refuse a server no longer allowed.
- **FR-021**: Umbral MUST identify the person by DID (`linked_accounts.provider_subject`), store the issuer's host with the link, and refresh the handle and profile on every sign-in.
- **FR-022**: The display name MUST have control and format characters removed and be capped as Apple's name is. When the profile can't be read, the name is the handle and there is no picture.
- **FR-023**: A Bluesky identity MUST never be linked to another user by email, and allowed email domain rules MUST NOT apply to it.
- **FR-024**: A remembered sign-in and a token refresh for a user whose Bluesky link names an authorization server no longer allowed MUST be refused, as the Fediverse check does.
- **FR-025**: An app MUST receive a Bluesky user with `name`, `preferred_username`, `picture` (when known) and `profile` under the `profile` scope, and no `email`.

**Security and operations**

- **FR-026**: Every sign-in failure MUST be recorded in the security log, naming the server and carrying no token, key or code. The failures are: refused server, unsupported server, unreachable server, handle not confirmed, nonce failure, state or `iss` mismatch, exchange failure, DID mismatch, and authority failure.
- **FR-027**: The step and the callback MUST be rate-limited like the other sign-in endpoints.
- **FR-028**: The documentation MUST include `docs/connectors/bluesky.md` (a walkthrough in the Fediverse page's shape), Bluesky in every walkthrough's navigation line, the README's connector list, `site/sidebar.mjs`, and a `changelog.d/` file.

### Key Entities

- **Bluesky connector**: the one connector of kind Bluesky. It carries the usual enabled and shared state, plus the server scope and list, in the columns the Fediverse connector uses.
- **Bluesky identity**: a link from a User to a DID, with the host of the authorization server last used. It carries no email.
- **Attempt**: the encrypted correlation, which carries the DPoP key, the issuer, the token endpoint and the expected DID. It is never stored.

## Success Criteria *(mandatory)*

- **SC-001**: A person goes from pressing "Sign in with Bluesky" to signed in with two Umbral screens (the step, then back to the app) plus their server's own pages.
- **SC-002**: The operator enables the connector in one save, with no restart and nothing to register anywhere.
- **SC-003**: The tests show the authority check refusing a server that claims a DID it is not authoritative for, a `sub` that differs from the started DID, and a wrong `iss`.
- **SC-004**: No request leaves Umbral for a private or loopback address in a non-development instance, for any input on the step and any answer from a server on the way.
- **SC-005**: The connector is fully exercised by automated tests with no real Bluesky server. The tests cover:
  - handle resolution by DNS and by HTTPS;
  - the two-way check;
  - PAR and token with a nonce retry;
  - a moved handle keeping the user;
  - the allowlist at the step, the callback and a refresh;
  - an app receiving the user through OIDC.
- **SC-006**: Idle memory with the connector enabled stays within the 150 MiB budget measured by the existing memory test, with the new packages loaded.

## Assumptions

- The client name is "Umbral", and the client URI is the instance's public origin.
- Tokens from the authorization server are never stored or used after the profile read; Umbral asks for no refresh token.
- Bluesky's butterfly is not drawn on the button: the provider-marks rule keeps the link icon unless the brand terms allow the mark (research R7).
- `did:web` is accepted only as a bare host (no path, no port), as AT Protocol requires.

## Verbatim Constraints

- Connector kind and slug: `bluesky`.
- Routes:
  - `/connect/bluesky/start`: GET renders the step, POST takes the handle;
  - `/connect/bluesky/callback`;
  - `/connect/bluesky/client-metadata.json`.
- Scope: `atproto` only.
- Claims: `name`, `preferred_username`, `picture`, `profile`; no `email`.
- Data hooks: `data-connector="bluesky"`, `data-connector-kind="bluesky"`, `data-connector-state`, `data-connector-shared`, `data-instance-scope`, `data-instance-list`.
