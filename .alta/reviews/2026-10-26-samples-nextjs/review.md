# Documentation review: the Next.js sample (`samples/nextjs`)

Task: `2026-10-26-samples-nextjs-n3xt`.
Branch: `alta/golden-ibex-wbsb`, commits `a66a766` (the sample) and `7af1c1f`
(the notes), on `main` at `4657c66`.
Reviewed 2026-11-07 by the documentation reviewer session, per section 6 of
`docs/audit-2026-10.md`, from the diff, `notes.md`, the sample's code, the
samples contract, and the installed Next.js 16.4.5, `next-auth`
5.0.0-beta.32 and `@auth/core` 0.41.3 under `samples/nextjs/node_modules`.

## Verdict

**Clear to merge.** No blocking item. Two nits, neither needs a re-check.

## What was checked

- **Against the samples contract.** `/` has an `h1`, the `Public` and
  `Private` links, and a `Sign in` link or a form whose button reads `Sign
  out`; `/public` and `/private` have their `h1`; `/private` is a table of
  `th` and `td` with the five claims in the contract's order. The protocol
  (code with PKCE, `state` and `nonce` checked, the three scopes, endpoints
  from discovery), the cookie flags and the README's order are all as the
  contract says. `AUTH_URL` and `AUTH_SECRET` are optional variables of the
  stack, the sample runs without them, and the README names each: the rule
  the contract gained in the FastAPI task.
- **Quoted UI strings.** Every bold label, and "Sign in to", are in
  `Texts.fs` at the lines `notes.md` gives.
- **The README against the code.** `auth.ts`: `type: "oidc"` with `issuer`
  only, `checks: ["pkce", "state", "nonce"]`, the three scopes, `profile()`
  mapping `sub` and `name` only, the `jwt` callback keeping the five claims
  and the `id_token`, the `session` callback serving only `expires` and the
  claims. `app/actions.ts`: the sign-out server action reads the `id_token`
  before `signOut`, reads the end-session endpoint from discovery, and builds
  the post-logout URI from `AUTH_URL`. `instrumentation-node.ts`: the three
  required variables with their exit message, `AUTH_URL` and `AUTH_SECRET`
  defaulted with `||=` so Compose's empty value counts as unset.
  `app/signin/route.ts` picks the return path from a fixed list. Every
  statement in "How it works" is in those files.
- **The library claims, in the installed sources.**
  - An `oidc` provider's profile is the validated `id_token` claims, with no
    userinfo call unless `idToken: false`
    (`@auth/core/lib/actions/callback/oauth/callback.js:167-185`), so "/private
    shows the id_token's claims as they come" is right.
  - The session cookie is `httpOnly`, `sameSite: "lax"`, and `secure` when the
    URL is https (`lib/utils/cookie.js:51-54`, `lib/init.js:69`), and the URL
    is `AUTH_URL` once set (`next-auth/lib/env.js:4-6`).
  - Setting `AUTH_URL` makes `trustHost` true (`@auth/core/lib/utils/env.js:40`),
    so "you do not need `AUTH_TRUST_HOST`" is right.
  - Next.js checks a server action's origin against `x-forwarded-host` or
    `host` (`action-handler.js:347, 434`), so the proxy sentence and the
    anti-forgery claim are right.
- **Root README, contract table, fragment.** "Today there are four", types
  right; the table row in the shape of the others; the fragment an `### Added`
  in the prescribed format; `changelog.sh check` passes.
- **Links.** `check-links.py`: 233 links in 20 files, all resolve. I did not
  re-run the harness; two green runs are recorded.

## The two choices

- **`AUTH_URL` from configuration rather than `AUTH_TRUST_HOST`.** Right, and
  the better of the two: the redirect and post-logout URIs come from one
  configured value rather than from request headers, which also keeps the
  post-logout URI from becoming an open redirect. The README says what
  happens without it behind a proxy.
- **`AUTH_SECRET` optional, random per start.** Right, and consistent with
  the FastAPI sample and the contract's new rule. The `openssl rand` line is a
  useful touch.

## Nits

1. **"Logs" bullet: "The production server logs no request".** True, but the
   README's own trial path is `npm run dev`, and Next.js's dev server logs
   every request's URL with its query string (`next/dist/server/dev/log-requests.js:62`),
   so the trial prints the callback's `code` and `state`. Say so in one clause,
   with the reasoning the FastAPI sample uses (the code is single-use, already
   redeemed, and useless without the PKCE verifier and the client secret), so
   a reader who sees it does not think something leaked.
2. **"Session cookie": "Only the trial at `http://localhost` goes without".**
   The other three samples and the contract now say "plain http on a loopback
   address, such as `http://localhost`"; use the same words, since Umbral
   accepts any loopback address and so does the cookie rule here (it follows
   `AUTH_URL`'s scheme).

## notes.md

Complete under section 6: every statement against the code, the libraries
with file and line, or the catalogue; the two harness runs; the checks; and
the choices set out with their reasons. Agreed on all of it.
