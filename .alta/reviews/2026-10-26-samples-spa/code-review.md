# Code review: the browser-only app (public client) sample (task sp4b)

Branch `alta/shiny-ledge-2q9g`, `8d507b3..7e36e25` (the sample is `3499c04`), on top of the CORS change `8d507b3` (`alta/lush-ginkgo-zc7x`, reviewed separately and clear). Reviewer: the `umbral` session, 2026-11-05. I read every file and ran the harness myself.

## Verdict

**Clear to merge, after the CORS branch, with two nits worth taking (N1, N2).**

## What was checked, against the task's list

- **No client secret anywhere.** `src/main.ts`, `vite.config.ts`, `nginx.conf.template` and `docker-compose.yml` read only `UMBRAL_ISSUER`, `UMBRAL_CLIENT_ID` and `PORT`. Configuration reaches the browser through `/config.json`, answered from the environment by nginx and by the Vite dev and preview servers with `Cache-Control: no-store`, so the built bundle holds no setting. The harness registers the app as **Single-page or native app** and hands the sample an empty secret it never reads.
- **Tokens in `sessionStorage`.** `userStore` and, rightly, `stateStore` too (`main.ts`), so the sign-in in flight (`state`, `nonce`, the PKCE verifier) never touches `localStorage`.
- **No silent renew, no refresh token.** `automaticSilentRenew: false`, and the scope is `openid profile email` without `offline_access`.
- **`post_logout_redirect_uri` from configuration**, and `signoutRedirect()` called with no per-call argument; `redirect_uri` the same way. Both built from `window.location.origin`, so there's no open redirect.
- **PKCE, state and nonce.** `response_type: 'code'`, the library's defaults untouched, `authority` only (discovery). After the callback, `history.replaceState` removes the code from the address and the history.
- **`/private`** is a client-side route that calls `signinRedirect` when there is no unexpired user, returning to `/private` through `state`.
- **Markup.** Every node is built with `textContent`, never `innerHTML`, so a claim value is only ever text.
- **Serving and pins.** `nginxinc/nginx-unprivileged:1.29-alpine` (non-root) with an SPA fallback, built by `node:24-alpine`; `oidc-client-ts 3.5.0`, `vite 8.3.6` and `typescript 5.9.3` exact in `package.json` and locked. The `.dockerignore` keeps `node_modules` and `dist` out of the context.
- **The harness.** `COMPOSE_PROJECT_NAME=umbral-samples-spa-review samples/test/run.sh spa` at `7e36e25`, with `umbral:samples` rebuilt from that commit (so with CORS): setup registers the app with no secret; the walk passes end to end (`/`, `/public`, `/private` to Umbral, the fake connector, the claims with no `email` row, Sign out, `/private` to Umbral again).
- **The README.** Its statements match the code: no secret, discovery only, PKCE, `sessionStorage` for both stores, no silent renew, `/config.json`, sign-out with `id_token_hint`, no cookie. The documentation reviewer covers the rest.

## Nits

- **N1, take now: say which Umbral the sample needs.** It works only against an Umbral that answers CORS on the token, discovery, keys, userinfo and revocation endpoints, which `8d507b3` adds and no release has yet. Against an older instance (0.8.x) the browser refuses the token request with a CORS error that names nothing. One sentence in the README under "How to run it", "Needs Umbral <the release that includes this sample> or later: earlier versions answer no CORS on the token endpoint, and the browser refuses the exchange", with the version filled in at release time, or worded as "the same release this sample first appears in".
- **N2, take now: Sign out as a button, not a form.** The home page wraps **Sign out** in `<form method="post" action="/signout">` and intercepts the submit in script. Without script the form posts to nginx, which answers 405 for a static file, so the form is a promise the page can't keep; a `<button>` with the click handler says what it is. The contract's "every form works without JavaScript" is about server-side samples; this one needs script for everything, which the README could say in one line too.

## Observation, not a finding

The nginx configuration sends no `Content-Security-Policy`. A sample that people copy would be a good place for a short one (`default-src 'self'; connect-src 'self' <issuer>`), but the issuer is only known at run time and the contract asks for no styling or extras, so it's a choice, not a gap.

## Merge order, for the maintainer

1. `alta/lush-ginkgo-zc7x` (CORS, `8d507b3`): the sample cannot exchange its code without it.
2. `alta/fancy-tempest-tgh6` (`739d65d`, a Compose project per sample and checkout in `run.sh`): read, small and sound (a deterministic name from the sample and the checkout path, `umbral:samples` still shared); it lets two harness runs coexist and is independent of the others.
3. This branch.

The three ship in one release, and the samples workflow will walk both samples on the merge push.
