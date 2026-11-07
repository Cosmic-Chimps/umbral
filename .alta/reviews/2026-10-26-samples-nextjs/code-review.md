# Code review: the Next.js sample (task n3xt)

Branch `alta/golden-ibex-wbsb`, `4657c66..7af1c1f` (the sample is `a66a766`). Reviewer: the `umbral` session, 2026-11-07. I read every file and ran the harness myself.

## Verdict

**Clear to merge.** No blocking items; one nit, optional.

## What was checked, against the task's care points

- **`AUTH_SECRET`.** Never in code: `instrumentation-node.ts` takes it from the environment or makes a random one at every start (`||=`, so Compose's empty value counts as unset), like FastAPI's `SESSION_SECRET` and within the contract's new rule for optional stack variables. The README says how to make one and to keep it out of the repository.
- **`AUTH_URL` instead of `AUTH_TRUST_HOST`.** The right call. With `AUTH_URL` set, next-auth treats the host as trusted and rewrites the request URL to it, so every URL Auth.js sends out (the redirect URI above all) comes from configuration, never from `Host` or `X-Forwarded-*`. Behind a TLS proxy without it, the app builds `http://localhost:3000` URIs and Umbral refuses the sign-in as unregistered: it fails closed, and the README says exactly that, plus that the proxy must still pass `Host` for Next.js's server-action origin check.
- **The cookie.** Auth.js's session is an encrypted JWT (keyed from `AUTH_SECRET`), `HttpOnly`, `SameSite=Lax`, and `Secure` (with the `__Secure-` prefix) whenever `AUTH_URL` is https; only the `http://localhost` trial goes without. `getToken` in the sign-out action asks for the same cookie name (`secureCookie` from the same `AUTH_URL`).
- **The `id_token` stays server-side.** It is kept in the encrypted session for `id_token_hint`, and the `session` callback leaves it out of what Auth.js serves at `/api/auth/session`, which returns only `expires` and the five claims.
- **Claims.** `profile()` maps `sub` and `name` and invents nothing; the five claims are copied raw from the validated `id_token` in the `jwt` callback, which avoids Auth.js's replacement of `user.id` and lowercasing of `email`. JSX escapes every value.
- **PKCE, `state` and `nonce`.** `checks: ["pkce", "state", "nonce"]`, the generic `oidc` provider with discovery only (`issuer`).
- **Sign-in and sign-out.** `/signin` takes `to` from a fixed list (`/private` or `/`), never the query passed through. Sign-out is a server action, which Next.js protects with its `Origin` check against `Host`; it reads the `id_token` before clearing Auth.js's cookie, fetches `end_session_endpoint` from discovery, and builds `post_logout_redirect_uri` from `AUTH_URL`, so it cannot be steered elsewhere; `/signout-callback` redirects to `/` from `AUTH_URL` too.
- **No secret in the repo or the logs.** The client secret comes from the environment only; the production server logs no request.
- **Container and pins.** Next's standalone output on `node:24-alpine`, run as `node`, telemetry off, nothing written at run time (so no bind-mount ownership trap). Every package exact in `package.json` and locked; the README says next-auth v5 is still a beta and that it is pinned.
- **The harness.** `samples/test/run.sh nextjs` at `7af1c1f`, with `umbral:samples` rebuilt from that commit: setup and the walk both passed.

## Nit, optional

- **N1. The app's session outlives Umbral's.** Auth.js's default session lasts 30 days, while the other samples' sessions and Umbral's own last 14. Setting `session: { strategy: "jwt", maxAge: 14 * 24 * 60 * 60 }` keeps the samples consistent; or leave it and say in the README's "Session cookie" bullet that 30 days is Auth.js's default. Either is fine.

## Implementer's answer

- **N1. Taken.** `auth.ts` sets `session: { strategy: "jwt", maxAge: 14 * 24 * 60 * 60 }`, with a comment that Auth.js's default is 30; the README's "Session cookie" bullet says 14 days, as in the other samples (ASP.NET Core's cookie and Starlette's `SessionMiddleware` both default to 14). Type check clean, harness rerun green.
