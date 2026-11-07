# Samples: the Next.js sample, implementer's notes

Task `2026-10-26-samples-nextjs-n3xt`, branch `alta/golden-ibex-wbsb` on `main` (`4657c66`).
Files for the documentation review: `samples/nextjs/README.md`, the table row in `samples/README.md`, the list sentence in `README.md` ("Integrate an app"), `changelog.d/samples-nextjs.md`.

Each statement of the sample's README, against the code (the code is normative), the libraries (Next.js 16.4.5, `next-auth` 5.0.0-beta.32 with `@auth/core` 0.41.3, paths under `samples/nextjs/node_modules/`) or the UI strings (quoted from `src/Umbral/Views/Texts.fs`).

## What to create in Umbral (quoted strings)

| README says | Where |
| --- | --- |
| **Apps**, **Create app** | `Texts.fs:973`, `:991` |
| **Name**, **Redirect URI**, **Post-logout redirect URI (optional)**, **App type** | `Texts.fs:983`, `:984`, `:986`, `:987` |
| **Web application** | `Texts.fs:1055` `AppTypeWebOption` |
| **Client ID**, **Client secret**, **Issuer URL** | `Texts.fs:994`, `:996`, `:995` |
| "The secret is shown once" | `Texts.fs:997` `AppSecretOnceWarning` |
| sign-in page titled **Sign in to** and the app's name | `Texts.fs:970` `PickerTitleForApp` |
| Redirect URI `http://localhost:3000/api/auth/callback/umbral`, Auth.js's default | `app/api/auth/[...nextauth]/route.ts` (Auth.js's handlers under the default `basePath` `/api/auth`, `next-auth/lib/env.js` `setEnvDefaults`); provider id `umbral` in `auth.ts:25` |
| Post-logout `http://localhost:3000/signout-callback`, the sample's own route | `app/signout-callback/route.ts`; sent by `app/actions.ts:31` |
| Both registered by the harness | `samples/test/samples.conf`: `nextjs 3000 web /api/auth/callback/umbral /signout-callback` |

## How to run it

- Three required variables: `instrumentation-node.ts:3-8` (a missing one prints "The environment variable X is required." and exits 1, no value printed; run only by the server, not by `next build`: `instrumentation.ts`). `PORT` default 3000: `Dockerfile` `ENV PORT=3000`, and `next dev`/`next start` default to 3000.
- `AUTH_URL` default `http://localhost:` and the port: `instrumentation-node.ts:14`. `AUTH_SECRET` random at every start unless set: `:18`. Both are Auth.js's own variable names (`next-auth/lib/env.js`), so the contract's "optional variables of its own stack".
- `.env.local` read by Next.js and git-ignored: `samples/nextjs/.gitignore` (`.env*`).
- Behind a proxy: with `AUTH_URL` set, `next-auth/lib/env.js` `reqWithEnvURL` rewrites the request's origin to it, and `@auth/core/lib/utils/env.js` `createActionURL` uses it instead of `x-forwarded-host`/`host`; `trustHost` then needs no `AUTH_TRUST_HOST` (`env.js:40`). Next.js compares a server action's `Origin` with `x-forwarded-host` or `host`: `next/dist/server/app-render/action-handler.js:347,434-446`.
- Quick-start anchor `#quick-start-a-first-login-on-your-own-machine`, as the other samples.

## What you should see

`samples/test/run.sh nextjs` passed on 2026-11-07, twice (once by the child session that wrote the code, once by me): `/` with Public, Private, Sign in; `/public`; `/private` to Umbral's sign-in screen; claims `sub`, `name`, `idp` and no `email` row for an identity without an address; Sign out back to `/`; `/private` to Umbral again (so Umbral's session ended).

## How it works

| README says | Where |
| --- | --- |
| `type: "oidc"`, `issuer` only, endpoints from discovery; end-session from the same document | `auth.ts:28`; `app/actions.ts:20-23`; no endpoint written anywhere in `samples/nextjs` |
| PKCE S256, `state`, `nonce` checked | `auth.ts:32`; `@auth/core/lib/actions/signin/authorization-url.js:78` (S256); nonce checked in `callback/oauth/callback.js` (`expectedNonce`) |
| Scopes | `auth.ts:33` |
| Claims as they come, from the jwt callback; `profile()` maps `sub` and `name`, no email; Auth.js lowercases email and replaces the id | `auth.ts:36-38`, `:45-52`; `@auth/core/lib/actions/callback/oauth/callback.js` `getUserAndAccount` (`id: crypto.randomUUID()`, `email: ...toLowerCase()`); the id_token claims are the profile for `type: "oidc"` (same file, `getValidatedIdTokenClaims`) |
| `/private` redirects to `/signin`, which comes back to `/private` from a fixed list | `app/private/page.tsx`; `app/signin/route.ts` |
| Encrypted JWT cookie, no database, holds claims and id_token; id_token not served at `/api/auth/session` | `auth.ts:43`, `:50-51`, `:55-58` |
| `HttpOnly`, `SameSite=Lax`, `Secure` when `AUTH_URL` is https | `@auth/core/lib/utils/cookie.js:48-55`; `lib/init.js:69` (`url.protocol === "https:"`, the URL rewritten to `AUTH_URL`) |
| Sign-out: server action, origin-checked; reads id_token, `signOut`, end-session with `id_token_hint` and the post-logout URI from `AUTH_URL` | `app/actions.ts:13-32`; Umbral: `src/Umbral/Handlers/Connect/EndSession.fs` |
| Logs: no request logged | Next.js's standalone server has no access log; a failing harness run prints the sample's log, and a run against an unreachable issuer logged only `[auth][error] TypeError: fetch failed` |

## Choices to look at

- **`AUTH_URL` rather than `AUTH_TRUST_HOST`.** The task named `AUTH_TRUST_HOST` for a proxy; with it Auth.js builds the redirect URI from the request's headers. `AUTH_URL` gives the address from configuration, which the task asks for the post-logout URI anyway, and makes `AUTH_TRUST_HOST` unnecessary.
- **`AUTH_SECRET` optional**, random per start, like the FastAPI sample's `SESSION_SECRET`: the harness passes only the contract's variables.
- **`profile()` maps `sub` and `name`, and `idp` is carried with the other claims by the `jwt` callback**, not through `profile()`: Auth.js's user is not the claims as they come (random id, lowercased email).

## Lists and changelog

- `samples/README.md` table row: **Web application** (`Texts.fs:1055`).
- `README.md` "Integrate an app": "Today there are four", ASP.NET Core, FastAPI and Next.js as **Web application**s.
- `changelog.d/samples-nextjs.md`: `### Added`, one bullet; `.github/scripts/changelog.sh check` passes.
