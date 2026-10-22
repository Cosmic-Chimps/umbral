# Tasks: "Last used" on the sign-in page

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [contracts/last-used-cookie.md](contracts/last-used-cookie.md)

**Tests**: required (constitution: protocol behaviour through real endpoints; the task asks for cookie and picker tests).

## Phase 1: Foundational (the cookie)

- [x] T001 Create `src/Umbral/Infrastructure/BrowserCookies.fs` (compiled before `Sessions.fs` in `src/Umbral/Umbral.fsproj`): the hint-cookie options moved from `Handlers/ThemeChoice.fs` (`Path=/`, `Lax`, `HttpOnly`, `IsEssential`, `Secure` when HTTPS, optional `MaxAge`), `Year = 365 days`, `LastConnectorCookie = "umbral_last_connector"`, `rememberConnector ctx slug`, `lastConnector ctx : string option` (research R2)
- [x] T002 `src/Umbral/Handlers/ThemeChoice.fs` uses `BrowserCookies.options` and `BrowserCookies.Year`; no behaviour change (`ThemeTests` stay green)
- [x] T003 `src/Umbral/Infrastructure/Sessions.fs` `signIn` calls `BrowserCookies.rememberConnector ctx slug` (research R1)

## Phase 2: User Story 1, 3 and 4 — the picker (P1, P2, P3)

- [x] T004 [US1] `src/Umbral/Views/Texts.fs`: `LastUsedBadge` ("Last used"), `LastUsedName: string -> string` ("<label>, last used"), `PickerOr` ("or")
- [x] T005 [US1] `src/Umbral/Handlers/Connect/Picker.fs`: public `arrange (last: string option) (offered: Connector list) : Connector option * Connector list` (research R3); the view renders the remembered button first with `data-last-used`, `aria-label` and the `span.um-last-used` badge, then `div.um-or` (`data-or-divider`), then the others (research R4, contract)
- [x] T006 [US4] `src/Umbral/Assets/umbral.input.css`: `.um-last-used` (pill in `currentColor`) and `.um-or` (divider in theme tokens) in `@layer components` (research R5); run `src/Umbral/Assets/build-css.sh` and keep the regenerated `umbral.css`

## Phase 3: Tests

- [ ] T007 [US2] `tests/Umbral.Tests/LastUsedConnectorTests.fs`, cookie: set with the slug only (no `@`, not the user id; `httponly`, `samesite=lax`, `path=/`, a year) after a provider callback, a passkey sign-in, a passkey open sign-up and an invitation; not set by `/connect/{slug}/start`, a declined or failed callback, a refused sign-in (allowed email domains), a disabled user, the admin sign-in, adding a passkey while signed in; `/connect/logout` does not clear it
- [ ] T008 [US1][US3][US4] same file, picker: `arrange` unit cases; with two or more offered connectors and the cookie naming one, that button first with the badge, aria-label and the divider; the page unchanged (same body as with no cookie) for a missing, unknown, odd, disabled, not-offered slug and for a single offered connector; the cookie's value never in the page; a branded app (accent) and both themes (`umbral_theme=light`/`dark`) render the badge

## Phase 4: Docs and polish

- [x] T009 README: a paragraph after the `umbral_theme` one (what the cookie holds, no email, when it is set, signing out keeps it)
- [x] T010 CHANGELOG.md: `### Added` bullet under `## Unreleased`
- [ ] T011 `dotnet test -c Release` green; `src/Umbral/Assets/build-css.sh --check` clean (both are CI gates)
- [ ] T012 Browser check of the picker with a hint: unbranded and branded, dark and light

## Dependencies

T001 → T002, T003 → T007. T004 → T005 → T006 → T008. T009–T010 any time. T011–T012 last.
