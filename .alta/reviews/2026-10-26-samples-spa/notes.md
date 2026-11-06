# Samples: the browser-only app (public client), implementer's notes

Task `2026-10-26-samples-spa-sp4b`, branch `alta/shiny-ledge-2q9g` on top of fw24 (`8d507b3`, CORS), commit `3499c04`.
Files for the documentation review: `samples/spa/README.md`, the table row in `samples/README.md`, the list sentence in `README.md` ("Integrate an app"), `changelog.d/samples-spa.md`.

Each statement of the sample's README, against the code (the code is normative) or the UI strings (quoted from `src/Umbral/Views/Texts.fs`).

## What to create in Umbral (quoted strings)

| README says | Where |
| --- | --- |
| **Apps** | `Texts.fs:973` `AppsTitle = "Apps"` |
| **Create app** | `Texts.fs:991` `AppCreateButton = "Create app"` |
| **Name** | `Texts.fs:983` `AppNameLabel = "Name"` |
| **Redirect URI** | `Texts.fs:984` |
| **Post-logout redirect URI (optional)** | `Texts.fs:986` |
| **App type** | `Texts.fs:987` |
| **Single-page or native app** | `Texts.fs:1056` `AppTypeSpaOption` |
| **Client ID** | `Texts.fs:994` |
| **Issuer URL** | `Texts.fs:995` |
| sign-in page titled **Sign in to** and the app's name | `Texts.fs:970` `PickerTitleForApp` |
| "There is no secret" for this app type | `src/Umbral/Infrastructure/AppClients.fs:46` (`ClientTypes.Public`), `src/Umbral/Handlers/Admin/Apps.fs:864` (the secret block is shown only when not public); `Texts.fs:720` says so on the Apps screen |
| Redirect URI `http://localhost:5173/signin-callback`, post-logout `.../signout-callback` | `samples/spa/src/main.ts:50-51`; registered by the harness from `samples/test/samples.conf` (`spa 5173 spa /signin-callback /signout-callback`); "matched exactly" is OpenIddict's behaviour, documented in README.md "What an app can count on" |

## Why a browser app has no secret

- PKCE binds the exchange: `src/Umbral/Infrastructure/OpenIddictSetup.fs:287` (`RequireProofKeyForCodeExchange`), `:290` (plain refused, S256 only), `AppClients.fs:61` (requirement on every app).
- The verifier stays in the browser's `sessionStorage`: `main.ts:62` (`stateStore` on `sessionStorage`); without it the library's default is `localStorage` (`oidc-client-ts` 3.5.0, `dist/esm/oidc-client-ts.js:1070-1074`).
- No `UMBRAL_CLIENT_SECRET` is read: `main.ts`, `vite.config.ts`, `nginx.conf.template`, `docker-compose.yml` (grep: no match). The built bundle mentions `client_secret` only inside the library's own settings code; the sample never sets it.

## How to run it

- Variables: `vite.config.ts` reads `UMBRAL_ISSUER`, `UMBRAL_CLIENT_ID`, `PORT` (default 5173); `nginx.conf.template:9,16` the same; `docker-compose.yml` requires the two and defaults the port.
- "The container only serves files; the browser talks to Umbral": `nginx.conf.template` serves `/config.json` and static files only; every call to Umbral is in `main.ts` (fetch of discovery and the token endpoint by the library), answered by the CORS policy of `src/Umbral/Program.fs:365,475` (fw24).
- Quick-start link: README.md "Quick start" heading, anchor `#quick-start-a-first-login-on-your-own-machine` (same anchor as samples/aspnetcore/README.md).

## What you should see

Verified by the harness walk (`samples/test/walk.spec.ts`), run locally: `COMPOSE_PROJECT_NAME=umbral-samples-spa-shiny samples/test/run.sh spa` passed twice on 2026-11-05 (1 passed, 5.8 s): `/` with Public, Private, Sign in; `/public`; `/private` to Umbral's sign-in screen; claims `sub`, `name`, `idp` and no `email` row for an identity without an address; Sign out back to `/`; `/private` to Umbral again.

## How it works

| README says | Where |
| --- | --- |
| Discovery, `authority` only | `main.ts:45`; no other endpoint named anywhere in `samples/spa` |
| `response_type: 'code'`, PKCE, `state` and `nonce` checked by the library | `main.ts:55`; oidc-client-ts does PKCE for `code` and validates `state` and `nonce` in its response validator (library) |
| Scopes `openid profile email` | `main.ts:56` |
| Tokens and the sign-in in flight in `sessionStorage` | `main.ts:61-62` |
| No silent renew, no refresh token | `main.ts:66`; Umbral registers `offline_access` (`OpenIddictSetup.fs:309`) and the sample never asks for it, so OpenIddict issues no refresh token |
| `/config.json` from the environment | `vite.config.ts` (dev and preview), `nginx.conf.template:16` |
| Sign-out: end-session endpoint with `id_token_hint` and the post-logout redirect URI from configuration, then `/signout-callback` lands on `/` | `main.ts:94` (`signoutRedirect()`, no per-call argument: the library sends the stored `id_token` and `post_logout_redirect_uri` from `main.ts:51`), `main.ts:171`; Umbral: `src/Umbral/Handlers/Connect/EndSession.fs:25,32` (ends its session, OpenIddict validates the URI against the app) |
| No cookie | `samples/spa` sets none; nginx serves static files |
| Claims as they come: `sub`, `name`, `idp`, `email`, `email_verified` | `main.ts:127`; README.md:285 (the id_token carries them; `email` and `email_verified` together or not at all per samples/README.md) |

## Lists and changelog

- `samples/README.md` table row: app type **Single-page or native app** (`Texts.fs:1056`).
- `README.md` "Integrate an app": "Today there are two" plus the new link; the fw24 bullet above it says a browser app needs nothing configured.
- `changelog.d/samples-spa.md`: `### Added`, one bullet; `.github/scripts/changelog.sh check` passes.
