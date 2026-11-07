# Samples: the FastAPI sample, implementer's notes

Task `2026-10-26-samples-fastapi-f4pi`, branch `alta/lush-dolphin-6rrb` on `main` (`dad2b15`).
Files for the documentation review: `samples/fastapi/README.md`, the table row in `samples/README.md`, the list sentence in `README.md` ("Integrate an app"), `changelog.d/samples-fastapi.md`.

Each statement of the sample's README, against the code (the code is normative) or the UI strings (quoted from `src/Umbral/Views/Texts.fs`).

## What to create in Umbral (quoted strings)

| README says | Where |
| --- | --- |
| **Apps** | `Texts.fs:973` `AppsTitle = "Apps"` |
| **Create app** | `Texts.fs:991` `AppCreateButton = "Create app"` |
| **Name** | `Texts.fs:983` |
| **Redirect URI** | `Texts.fs:984` |
| **Post-logout redirect URI (optional)** | `Texts.fs:986` |
| **App type** | `Texts.fs:987` |
| **Web application** | `Texts.fs:1055` `AppTypeWebOption` |
| **Client ID**, **Client secret**, **Issuer URL** | `Texts.fs:994`, `:996`, `:995` |
| "The secret is shown once" | `Texts.fs:997` `AppSecretOnceWarning` |
| sign-in page titled **Sign in to** and the app's name | `Texts.fs:970` `PickerTitleForApp` |
| Redirect URI `http://localhost:8000/signin-callback`, post-logout `.../signout-callback`, "the sample's own paths" | `samples/fastapi/main.py:124,158` (routes), built with `request.url_for` at `:89` and `:154`; Authlib takes the redirect URI as an argument and has no default. Registered by the harness from `samples/test/samples.conf` (`fastapi 8000 web /signin-callback /signout-callback`) |

## How to run it

- Variables: `main.py:24-27` (`UMBRAL_ISSUER`, `UMBRAL_CLIENT_ID`, `UMBRAL_CLIENT_SECRET` required; a missing one stops the app with "The environment variable X is required.", no value printed), `PORT` default `8000`. `SESSION_SECRET` optional: `main.py:49`.
- `docker-compose.yml` requires the three, defaults the port, passes `SESSION_SECRET` when set, publishes on `127.0.0.1` only.
- `FORWARDED_ALLOW_IPS`: uvicorn's own setting; `main.py:166` turns `proxy_headers` on, and uvicorn trusts them from `127.0.0.1` only unless `FORWARDED_ALLOW_IPS` says otherwise (uvicorn 0.54.0, `uvicorn/config.py`, `forwarded_allow_ips` default).
- Quick-start link: anchor `#quick-start-a-first-login-on-your-own-machine` (`README.md:35,88`), the same as the other two samples.

## What you should see

Verified by the harness walk (`samples/test/walk.spec.ts`): `samples/test/run.sh fastapi` passed on 2026-11-07 (run by the child session that wrote the sample, then again by me): `/` with Public, Private, Sign in; `/public`; `/private` to Umbral's sign-in screen; claims `sub`, `name`, `idp` and no `email` row for an identity without an address; Sign out back to `/`; `/private` to Umbral again.

## How it works

| README says | Where |
| --- | --- |
| Discovery, `server_metadata_url` only; end-session endpoint from it | `main.py:64`, `:149-150`; no endpoint named anywhere else in `samples/fastapi` |
| PKCE S256; Authlib sends it only when named | `main.py:69`; Authlib 1.9.5 `integrations/base_client/sync_app.py:279-285` (verifier made only when `code_challenge_method` is set), `oauth2/client.py:187-192` (`code_challenge` in the authorize URL), `sync_app.py:267-269` (verifier sent at the token request). Umbral requires S256: `OpenIddictSetup.fs:287,290` |
| Authlib checks `state` and `nonce` | `sync_app.py:263-265` (missing state data → `MismatchingStateError`), `starlette_client/apps.py:125-141` (state looked up in the session; `parse_id_token(nonce=...)`), `oidc/core/claims.py:69-82` (nonce validated). A failure answers 400 with the error code only: `main.py:128-134` |
| Scopes `openid profile email` | `main.py:66` |
| The id_token's claims, no userinfo call | `main.py:136` (`token["userinfo"]` is the parsed id_token; Authlib makes no userinfo request in `authorize_access_token`); README.md "Integrate an app" says the id_token carries the claims |
| Session cookie: signed not encrypted; holds the claims, the id_token, Authlib's sign-in state | Starlette 1.10.0 `middleware/sessions.py` (itsdangerous `TimestampSigner`, base64 JSON); `main.py:140-141`; Authlib stores `_state_umbral_<state>` with nonce and verifier in `request.session` |
| `HttpOnly`, `SameSite=Lax`, `Secure` except the plain-http loopback trial | `sessions.py:34` (always `httponly; samesite=`), `:35-36` (`secure` with `https_only`); `main.py:42,51,55` |
| Random key unless `SESSION_SECRET`; a restart ends sessions | `main.py:49` |
| Sign-out: a POST, app session ended, then end-session with `id_token_hint` and the post-logout URI, back via `/signout-callback` to `/` | `main.py:145-161`; Umbral: `src/Umbral/Handlers/Connect/EndSession.fs:25,32` |
| Claims as they come: `sub`, `name`, `idp`, `email`, `email_verified` | `main.py:72`, `:111-116` (strings as they are, booleans as JSON `true`/`false`, all HTML-escaped) |

## Known, left as is

- uvicorn's access log prints the callback's query string, so the authorization `code` and `state` appear in the sample's log (and in the harness's failure output). The code is single-use, already redeemed when it is logged, and worthless without the PKCE verifier and the client secret. No token, secret or claim is logged.
- Authlib 1.9.5 prints `AuthlibDeprecationWarning: The httpx module is deprecated` at start; harmless, to look at when the pins are refreshed.

## Lists and changelog

- `samples/README.md` table row: **Web application** (`Texts.fs:1055`).
- `README.md` "Integrate an app": "Today there are three", ASP.NET Core and FastAPI as **Web application**s, the browser-only app unchanged.
- `changelog.d/samples-fastapi.md`: `### Added`, one bullet; `.github/scripts/changelog.sh check` passes.
