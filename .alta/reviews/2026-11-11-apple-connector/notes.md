# Spec 013, Sign in with Apple: notes for review

- **Task**: `2026-11-11-2-connector-sign-in-with-apple-ap1e`.
- **Branch**: `alta/indigo-pheasant-5jd9` (worktree `~/.alta/worktrees/umbral/indigo-pheasant-5jd9`).
- **Range**: `25f3718..HEAD` (main at `25f3718`).
  - `c90f57f`, `460d383`: the spec (`specs/013-apple-connector/`: spec, research R1 to R9, plan, data model, `contracts/routes-and-screens.md`, tasks).
  - `12862f3`: the `Apple` kind, `Connector.AppleKey`, schema version 10.
  - `196b4df`: the form-post round trip and `POST /connect/{slug}/callback`.
  - `39f0d77`: `Apple.fs` (key check, client secret, claim rule, name, Enable check, adapter) and the shared parts of `Oidc.fs`.
  - `7247afc`: the fake Apple and the sign-in tests.
  - `f3f72f7`: the Connectors pane, its Save, the Enable check.
  - `e53e44a`: the docs and the changelog file.
  - `131f776`: the sign-in button.
- **Tests**: the full suite passed at `131f776`: 2058 tests, 0 failed, 0 skipped (the implementer's run, and mine again on 2026-11-11). The build has 0 warnings.
- **CSS**: `src/Umbral/Assets/build-css.sh` was run, `umbral.css` is committed, `--check` passes.
- **Site**: `npm run build` in `site/` passes with the new page (30 pages, internal links valid).
- **Checks**: `.github/scripts/changelog.sh check` and `.github/scripts/check-links.py` pass.

Apple's own rules were read on developer.apple.com on 2026-11-11; `research.md` R1 names the page for each.

## For the design review

The designer's answers of 2026-11-11 (questions 1 to 7) are in `specs/013-apple-connector/contracts/routes-and-screens.md`. What was built:

**The Apple pane** (`Handlers/Admin/AppleConnector.fs`, drawn by `Connectors.providerPane`):

- An entry "Apple" in the list after Google, "Not set up" until saved. Not in the add form.
- Set-up section as the other providers, with the caveat alert (answer 5).
- Settings: Display name (hint of its own), Services ID, Team ID and Key ID side by side from `sm` in mono, the key in a textarea (`.um-textarea`, mono, 6 rows, `autocomplete`, `spellcheck` and `autocapitalize` off), then the shared box and allowed domains.
- Once saved: "Private key" over dots alone, then "Replace the private key" with its hint.
- Refusals: status 422, the form drawn again, `aria-invalid` and the message under each field that is wrong (several at once), typed values kept, the key never drawn back.
- Notices at the top of the pane for what is not about a field: saved but not enabled (Apple unreachable), and three for Enable (Apple unreachable, the stored key cannot sign, the secret store could not be read).

**The sign-in button** (`Handlers/Connect/Picker.fs`, `.um-btn-apple`):

- Title fixed "Sign in with Apple"; black with white text in the light theme, white with black text in the dark theme, also under "system"; never the accent.
- Same height and width as the others (measured in the walk: 44 by 318 px for Apple and for GitHub at 1280).
- Hover is opacity 0.85. The focus ring is the global one, 2px outside the button.
- The "Last used" badge and accessible name as on the others.
- No link icon; an empty slot (`span.um-apple-logo[data-apple-logo]`) that takes no space, for Apple's logo. The artwork is not in this change.
- `DESIGN.md`: one bullet under Buttons and a half-sentence in the Fill Pair Rule.

**Wording that was not in the answers**, for you to judge:

1. Display name hint: "Shown on the admin screens. The sign-in button always says Sign in with Apple."
2. Display name refused: "Enter a name for this connector, then try again." (the other panes' message speaks of the sign-in button's label; ending changed after the design review).
3. `connector-enable-apple-store`: "Not enabled: Umbral could not read the key from the secret store (<store>: <cause>). Check the store, then press Enable again." (the next action added after the design review).
4. Each of the four notices has a form without its reason, shown when the reason cookie is gone (a refresh).
5. The note at the top of the Set-up section. It was the generic naming advice, which says people see the name on the provider's consent screen; that is not confirmed for a Services ID. After the design review the Apple pane has its own: "Give the Services ID a description that names this Umbral instance (for example "Umbral — home"), so you can tell it apart on Apple's site later."

**Two things outside this change that you may want to queue:**

- The home page's lead (`Texts.HomeLead`) lists "Discord, GitHub, Google, a Mastodon-compatible server or any OpenID Connect provider" and does not name Apple.
- `PRODUCT.md` lines 32 and 94 list the connector kinds without Fediverse or Apple.

### Captures

Retaken 2026-11-11 after the design review's fix (all of them, so every pane capture shows the new set-up note), from a Release build on `http://localhost:18080` with Chrome (Playwright), by `capture.mjs` in this folder. `<state>-<width>-<theme>.png`, at 1280 and 360, dark and light, unless said. No state overflows horizontally at 360.

| State | What it shows |
| --- | --- |
| `pane-not-set-up` | The Apple pane before a save. |
| `refused-team-id` | 422 after a Team ID of six characters. |
| `refused-key` | 422 after a key that is not a key. The pasted text is not in the page (checked by the script). |
| `refused-several` | 1280 dark only: Services ID, Team ID, Key ID and the missing key refused at once. |
| `pane-set-up` | After a real save: the key (a throwaway P-256 key) signed, and Apple's real discovery document and keys were fetched, so the connector is enabled. No line of the key is in the page (checked by the script). |
| `list` | The Connectors list with Apple set up. |
| `picker` | The sign-in page of an app with Apple, GitHub and a passkey. |
| `picker-accent` | The same for an app with the accent `#b4532a`: the others take it, Apple stays black or white. |
| `picker-last-used` | Apple as the connector last used. |
| `picker-focus-apple` | 1280 only, both themes: keyboard focus on the Apple button. |

Apple comes first on the picker because connectors are listed by identifier (`apple`, `github`, `passkey`); nothing places it specially.

## For the documentation review

Files: `docs/connectors/apple.md` (new), the "Other providers" line of the six other walkthroughs, `README.md` (introduction, Connector row, link list), `docs/native-apps.md`, `docs/who-can-sign-in.md`, `docs/integrate.md` (the `idp` values), `docs/connectors/oidc.md` (reserved identifiers), `docs/run/backup.md` (schema version 10), `site/sidebar.mjs`, `changelog.d/013-apple-connector.md`.

Each statement of `docs/connectors/apple.md` and where it comes from:

| Statement | Source |
| --- | --- |
| Guideline 4.8's wording | Apple's App Review Guidelines, read 2026-11-11 (spec Background). |
| Membership, Account Holder or Admin for keys, the key downloaded once, two keys per primary App ID | `research.md` R1 (Apple's help pages). "Paid, yearly" is not in so many words on the pages read; the task states it. |
| No `localhost`, no IP address, https | `research.md` R1 (authorize and token request pages). |
| One Apple connector, identifier `apple` | `Handlers/Admin/Connectors.fs:2112` (`saveApple`, fixed slug), `Domains/Connectors.fs` (`allKinds` without Apple). |
| The field "Redirect URI to register with the provider" | quoted from `Texts.fs` (`CallbackUrlLabel`). |
| The Services ID and key steps | Apple's "Configure Sign in with Apple for the web" and "Create a private key to access a service", linked in the page. The App ID steps and the place of the Team ID were not checked on the live site, and the page says so. |
| The five fields, their order and labels | quoted from `Texts.fs:1497` and following; order in `Handlers/Admin/AppleConnector.fs:155` (`fields`). |
| "Save connector", "Enable", "Replace the private key", "Allowed email domains" | quoted from `Texts.fs`. |
| Save checks that the key signs and that Apple answers; saved disabled when Apple cannot be reached; a key that cannot sign is not saved | `Connectors.fs:2112` (`saveApple`), `Providers/Apple.fs:235` (`check`). |
| Enable makes the same checks with the key from the secret store | `Handlers/Admin/ConnectorToggle.fs:28` (`checkApple`). |
| Replacing the key leaves the connector's state | `Connectors.fs:1804` (`applyChanges`); spec FR-006. |
| The key is in the configured secret store, never shown again | `Connectors.fs` (`putNew`, empty hint), `AppleConnector.fs:155` (the textarea is always empty). |
| Every message of the table "What the form says" | quoted from `Texts.fs:1497` and following and `Texts.fs:920` (notices). A script compared each with the file. |
| The app gets a stable subject, the email, and the name from the first sign-in on | `Providers/Apple.fs:273` (`identityOf`), `:188` (`nameOf`); `UserStore.fs:173` (`COALESCE`: a later answer without a name keeps the stored one). |
| A user without a name is "Apple user" | `UserStore.fs:141`. |
| Stopping Sign in with Apple with the app makes Apple ask again | Apple Support, "Manage your apps with Sign in with Apple" (the iPhone steps and "you'll need to share your name and email again"). |
| No email, or an unverified one, can happen with Sign in with Apple at Work & School | `research.md` R1, the `id_token` page: `email` "may be empty for Sign in with Apple at Work & School users", and "The system may not verify email addresses" for them. |
| Relay address is the verified email; not joined with a Google or GitHub user; a real verified address is joined | `Apple.fs:148` (`claimIsTrue`), `Domains/Linking.fs` (linking by verified email only). |
| `email_verified` accepted as boolean `true` or text `"true"` and nothing else | `Apple.fs:148`. |
| A relay address passes a domain rule only if `privaterelay.appleid.com` is listed | `Handlers/Connect/Callback.fs` (`finishSignIn` applies the rule to the email's domain). |
| A new client secret per sign-in, valid five minutes | `Apple.fs:87`, `:97`. |
| The button's title, colours and no accent; no logo yet | `Handlers/Connect/Picker.fs:144`, `:134`; `umbral.input.css:86`. |
| Apple's subject is per developer team | `research.md` R1 (the `sub` claim). |
| Log words `rejected: invalid_client`, `no_correlation_cookie`, `id_token_invalid: …` | `ProviderAdapters.fs` (`ConnectorError.Detail`), `OAuthRoundTrip.fs` (`RoundTripError.Detail`), `Providers/Oidc.fs` (`invalid`); asserted in `tests/Umbral.Tests/AppleSignInTests.fs`. |
| `apple` cannot be another connector's identifier (`oidc.md`) | `Connectors.fs:1714` (`parseKind slug`); test in `AppleAdminTests.fs`. |
| `idp` is `apple` (`integrate.md`) | `Callback.fs` (`Sessions.signIn … connector.Kind.Value`). |
| Schema version 10 adds two empty columns (`backup.md`, changelog) | `Infrastructure/Database.fs:292`, `:445`. |

What the docs do not say, on purpose: anything about PKCE with Apple (research R5: it cannot be confirmed without real credentials), and no claim that the button passes App Review.

## For the code review

What the task asks you to check, and where it is:

| Point | Where |
| --- | --- |
| The JWT signing | `Providers/Apple.fs:97` (`clientSecret`): `JsonWebTokenHandler.CreateToken`, ES256, `kid`, exactly `iss`, `sub`, `aud`, `iat`, `exp`, five minutes, no cached signature provider. Tests: `AppleClientSecretTests.fs`. |
| The key never leaves the secret store or reaches a log | Read at the moment of use in `Callback.fs` (existing path) and `ConnectorToggle.fs:28`; `Apple.fs:68` (`normaliseKey`) and `importKey` use no exception text; the row's hint is empty. Tests: `assertNoSecretsLogged` in `AppleSignInTests.fs`, the page checks in `AppleAdminTests.fs`. |
| The form-post correlation | `OAuthRoundTrip.fs:74` (`settingsFor`: a second cookie, `SameSite=None; Secure`, its own protector purpose), `:291` (`complete`, order unchanged). Tests: `OAuthRoundTripTests.fs`. |
| Its CSRF reasoning | `research.md` R3; the comment on the route, `Callback.fs:373`. The route calls `DisableAntiforgery()`: without it `UseAntiforgery` records a missing token and reading the form throws. `Callback.fs:306` (`readPostedAnswer`: 64 KB, a body that cannot be read is an empty answer, the correlation is still spent), `:199` (`response_mode_mismatch`). Tests: `FormPostCallbackTests.fs`, the refusals in `AppleSignInTests.fs`. |
| `id_token` validation | `Providers/Oidc.fs:274` (`validateIdToken`, moved out of `OidcAdapter.Validate` unchanged; the OIDC tests pass untouched), called from `Apple.fs:287`. |
| The `email_verified` parsing | `Apple.fs:148` (`claimIsTrue`), used in `:273`. The generic adapter's `isJsonTrue` is unchanged. Tests: the theory in `AppleClientSecretTests.fs`, the stored flag and the linking tests in `AppleSignInTests.fs`. |
| The stored name | `Apple.fs:188` (`nameOf`); `UserStore.fs:173` unchanged. Tests: the four name tests in `AppleSignInTests.fs`. |
| The tests | `AppleDomainTests` 11, `AppleClientSecretTests` 21, `AppleSignInTests` 25 (35 cases), `AppleAdminTests` 25, `AppleButtonTests` 7, `FormPostCallbackTests` 6, 12 added to `OAuthRoundTripTests`; `Infrastructure/FakeApple.fs` on `StubOidcProvider` (two settable paths and an optional client check added there). |

Things you would otherwise have to find:

- `ConnectorStore.update` has a new last parameter (`appleKey`); `None` keeps the stored ids.
- The generic `save`'s secret replacement moved into `Connectors.applyChanges` (`Connectors.fs:1804`), shared with `saveApple`; the code is moved, not changed.
- The unshare confirmation (`strandWarning`, `Connectors.fs:384`) carries Apple's hidden fields; without them the confirmation was refused. Test in `AppleAdminTests.fs`.
- PKCE is sent to Apple although Apple's documents do not mention it (research R5). It is the one thing no test with a fake Apple can confirm.
- `is_private_email` is read by `claimIsTrue` into the adapter's private record and acted on nowhere (spec Clarifications).
- `Apple.ExchangeAndFetch` maps `HttpRequestException` to `ProviderUnreachable ex.Message`, as `OidcAdapter` does.

## After the reviews

- The follow-up for Apple's logo artwork was first proposed as task `pqzq`, which `review.md` names. On the maintainer's word it was replaced the same day by one task for every provider's mark: `b6d7`, "Sign-in page: provider logos on the sign-in buttons, for every connector (Apple's included)". Apple's rules and the licence question are a section of it.
- The code review's B1 changed `OAuthRoundTrip.complete` for every provider, not only Apple: `state` is compared before a provider's `error` is believed.
