# Code review: CORS on the endpoints a browser-only app fetches (task fw24)

Branch `alta/lush-ginkgo-zc7x`, one commit `8d507b3` on `main` at `a70c3bc`. Reviewer: the `umbral` session, 2026-11-05. Reviewed because the browser-only sample (task sp4b) depends on it; its implementer session had ended, so this file is committed by the reviewer.

## Verdict

**Clear to merge.** No blocking items; one optional nit.

## What was checked

- **Scope.** One policy, applied through `UseWhen` to exactly five paths: the discovery document, the keys, `/connect/token`, `/connect/userinfo` and `/connect/revoke`. `/connect/authorize`, `/connect/logout`, the admin and passkey pages get nothing, and a test checks that (`pages and endpoints a browser navigates to carry no CORS header`). `PathString` equality is what the endpoint constants need; `Request.Path` excludes a `PathBase`, as the comment says.
- **The policy.** Any origin, `GET` and `POST`, any header, `WWW-Authenticate` exposed (so an app can read userinfo's `invalid_token`), a one-hour preflight cache, and no credentials: `AllowAnyOrigin` cannot be combined with them, so a session cookie can never ride on a cross-origin request to these endpoints, and no response is readable with one.
- **Why `*` is right here.** The token endpoint is meant to be called cross-origin by public clients; PKCE (required, `S256` only) and the exact redirect URI bind a code to the app that started the sign-in, and CORS gates reading a response, not sending a request, so a confidential client's secret gains nothing from a narrower origin list. Per-client origins would need `client_id` read from the form before OpenIddict runs, for no gain. Auth0 does the same; the comment in `Program.fs` says all this.
- **Placement.** After `UseRouting` and before `UseRateLimiter` and `UseAuthentication`: a preflight is answered with 204 before OpenIddict (which serves discovery, keys and revocation inside `UseAuthentication`) could answer it with 400, and a 429 carries the header so a browser app can read it.
- **Tests** (`CorsTests.fs`, six): discovery and keys answer any origin and only a request with an `Origin` gets the header; the token preflight is answered 204 with what a browser sends; the userinfo preflight allows `Authorization`; a refused token request and a refused revocation still carry the header; a whole exchange from another origin works; navigated pages carry no header. The browser-only sample's harness walk (run by the reviewer at `7e36e25`, which includes this commit) is the end-to-end proof.
- **Docs.** The README's "Integrate an app" gains a bullet saying which endpoints answer any origin and that the pages don't; `changelog.d/cors.md` is an `Added` note, which is right since it is new behaviour for every instance.

## Nit, optional

- The comment says a 429 from the rate limiter carries the header; no test exercises it. One request past `RateLimits.SignIn` on `/connect/token` with an `Origin`, asserting the header on the 429, would pin it.

## Merge order

This branch first; then `alta/fancy-tempest-tgh6` (the harness's Compose project per checkout, independent); then `alta/shiny-ledge-2q9g` (the sample), which cannot exchange its code without this.
