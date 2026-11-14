# Tasks: Remove one linked provider account

**Input**: [plan.md](plan.md), [remove-a-link.spec.md](remove-a-link.spec.md), [data-model.md](data-model.md), [contracts/screens.md](contracts/screens.md).

Paths are under `src/Umbral/` unless they start with `tests/`, `docs/` or `changelog.d/`.

## Phase 1: Storage

- [ ] **T001** `Linking.fingerprint` and the credential reference; `removed_links` and `passkeys.added_via` in `Database.schema`, step 15, `SchemaVersion = 15`; `UserStore`: `linkedAccountDetails`, `removeLink` (transitive passkeys), `isRemoved`, `credentialStillHeld`. Tests: `DatabaseTests` (14 → 15 creates the table, rewrites nothing); fingerprint (stable, 43 chars, differs by kind and by subject); `removeLink` (removes and records; refuses the last link with no passkey and leaves everything; allows it with a passkey that has no origin or another origin; refuses it when the only passkey was added through the link, and rolls that passkey back; removes passkeys added through the link and through those, and counts them; not found for a foreign or unknown fingerprint; lowers a flag only the removed link backed, keeps one another link backs; the record survives deleting the user); `credentialStillHeld` (each row of the data model's table).

## Phase 2: Sign-in and re-checks

- [ ] **T002** `Sessions` and `OpenIddictSetup` claims; `SecurityEvents.UserUnlinked` and `credentialRemovedDetail`; `Passkey.AddedVia`; `Callback.finishSignIn` clamp, event detail and session credential; Ceremonies (session credential, `added_via` for `Add`); `EndUserSession` (FR-012) used by `Authorize`, `Account` and the `Add` ceremony (start and finish); `Authorize` (identity, destinations); `Token` re-check. Tests with the stub providers: a removed account signs in again → new user, address unverified, not joined, `user_created reason=link_removed`, even with the provider still trusted; the stranger's existing session is refused at authorize (`credential_removed;stage=authorize`) and their refresh token at token (`invalid_grant`, `stage=token`); the remaining link's session and refresh token still work; a passkey added while signed in through a link records it, and one added after a passkey sign-in records that passkey; removing a passkey ends the sessions made with it; after removeLink, GET /account/passkeys with the stranger's cookie redirects to sign-in and signs out; an Add finish whose session lost its credential stores nothing; removing a passkey ends access to /account/passkeys for sessions made with it; a `passkey:<id>` of another user's passkey is refused; the `umbral_credential` claim is in no access token, id token, userinfo or introspection response; a session with no credential claim through a connector the user no longer holds a link of is refused, one through a connector they hold is honoured.

## Phase 3: Screen

- [ ] **T003** The list on the card, the confirm block (a post without `confirm` redraws at 422 with the block, focus on it; `confirm=remove` removes), `UserLinks` handler, route, Texts, notices and their tones, the passkey Remove confirm text, event. Tests: the first post removes nothing and draws the block with both passkey lists when they apply; list drawn only with two ways in, with dates; no list for one link and no passkey; POST removes, records `user_unlinked` with slug and `kind=`, no subject; last-way-in POST refused with the notice and nothing changed; not found; unknown user; antiforgery refused; operator session required.

## Phase 4: Docs, captures, reviews

- [ ] **T004** Docs (FR-011).
- [ ] **T005** Captures in `.alta/reviews/2026-11-14-remove-a-link/` and `notes.md`.
- [ ] **T006** Full suite green; design, documentation and code reviews; one re-check each.