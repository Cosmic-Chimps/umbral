# Tasks: Remove one linked provider account

**Input**: [plan.md](plan.md), [remove-a-link.spec.md](remove-a-link.spec.md), [data-model.md](data-model.md), [contracts/screens.md](contracts/screens.md).

Paths are under `src/Umbral/` unless they start with `tests/`, `docs/` or `changelog.d/`.

## Phase 1: Storage

- [ ] **T001** `Linking.fingerprint`; `removed_links` in `Database.schema`, step 15, `SchemaVersion = 15`; `UserStore`: `linkedAccountDetails`, `removeLink`, `isRemoved`, `linkStillHeld`; `SecurityEvents.UserUnlinked` and `linkRemovedDetail`. Tests: `DatabaseTests` (14 → 15 creates the table, rewrites nothing); fingerprint (stable, 43 chars, differs by kind and by subject); `removeLink` (removes and records; refuses the last link with no passkey and leaves everything; allows it with a passkey; not found for a foreign or unknown fingerprint; lowers a flag only the removed link backed, keeps one another link backs; the record survives deleting the user); `linkStillHeld` (each row of the data model's table).

## Phase 2: Sign-in and re-checks

- [ ] **T002** `Sessions` and `OpenIddictSetup` claims; `Callback.finishSignIn` clamp, event detail and session link; Ceremonies; `Authorize` (identity, destinations, re-check); `Token` re-check. Tests with the stub providers: a removed account signs in again → new user, address unverified, not joined, `user_created reason=link_removed`, even with the provider still trusted; the stranger's existing session is refused at authorize (`link_removed;stage=authorize`) and their refresh token at token (`invalid_grant`, `stage=token`); the remaining link's session and refresh token still work; the `umbral_link` claim is in no access token, id token or userinfo; a session with no link claim through a connector the user no longer holds a link of is refused, one through a connector they hold is honoured.

## Phase 3: Screen

- [ ] **T003** After the designer's answers: the list on the card, `UserLinks` handler, route, Texts, notices, event. Tests: list drawn only with two ways in, with dates; no list for one link and no passkey; POST removes, records `user_unlinked` with slug and `kind=`, no subject; last-way-in POST refused with the notice and nothing changed; not found; unknown user; antiforgery refused; operator session required.

## Phase 4: Docs, captures, reviews

- [ ] **T004** Docs (FR-011).
- [ ] **T005** Captures in `.alta/reviews/2026-11-14-remove-a-link/` and `notes.md`.
- [ ] **T006** Full suite green; design, documentation and code reviews; one re-check each.
