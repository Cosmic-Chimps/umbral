# Tasks: Several redirect URIs per app

**Input**: [plan.md](plan.md), [redirect-uris.spec.md](redirect-uris.spec.md), [research.md](research.md), [contracts/routes.md](contracts/routes.md).

Paths are under `src/Umbral/` unless they start with `tests/`, `docs/` or `changelog.d/`.

## Phase 1: Foundational

- [x] **T001** `maxPerApp = 10`; `add registered candidate` → `Ok` new list (registered spelling kept, candidate appended as its `OriginalString`) or `Invalid of string | AlreadyRegistered | Full`; `remove registered value keepOne` → `Ok` list or `NotRegistered | LastOne`; `acceptable` unchanged; unit tests · `Domains/RedirectUris.fs`, `tests/Umbral.Tests/RedirectUriTests.fs`
- [x] **T002** [P] Words and notice codes of the contract, registered in `knownNotice`; the create form's amended hint · `Views/Texts.fs`

## Phase 2: User Stories 1–3 (P1, P2)

- [x] **T003** The four routes: find the app, read its list, apply `add`/`remove`, write through `PopulateAsync` + `UpdateAsync(application, descriptor)`, record `app_updated` with the detail of research R6, redirect with the notice · `Handlers/Admin/AppRedirectUris.fs` (new), `Umbral.fsproj`, `Program.fs`
- [x] **T004** The Connection section's two lists, Remove with confirmation (none on the last redirect URI), hints, add forms (none on a full list); refusal codes drawn as warnings · `Handlers/Admin/Apps.fs`
- [x] **T005** Tests: add, remove, refusals (invalid, duplicate, full, last, not registered, no antiforgery, unknown app); the security events and their details; client ID and secret unchanged (a code exchanged with the original secret after edits); a sign-in through each of two URIs; a removed URI refused at authorize; sign-out through each of two post-logout URIs and a removed one landing on the home page; an app from the create form keeps signing in after an add · `tests/Umbral.Tests/AppRedirectUrisTests.fs` (new), `tests/Umbral.Tests/Umbral.Tests.fsproj`

## Phase 3: Docs

- [x] **T006** [P] The limits paragraph and "matched exactly" bullet; the native-apps "one redirect URI" line; the design contract line; the changelog fragment · `docs/integrate.md`, `docs/native-apps.md`, `design/contracts/apps-connectors-master-detail.md`, `changelog.d/012-several-redirect-uris.md`

## Phase 4: Verify

- [x] **T007** Full suite green; the walk at 1280 and 360, dark and light, for the design review
