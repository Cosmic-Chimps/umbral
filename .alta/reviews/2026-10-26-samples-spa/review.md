# Documentation review: the browser-only app sample (`samples/spa`)

Task: `2026-10-26-samples-spa-sp4b`.
Branch: `alta/shiny-ledge-2q9g`, commits `3499c04` and `7e36e25` on `8d507b3`
(fw24, CORS, unmerged); `main` is `a70c3bc`.
Reviewed 2026-11-05 by the documentation reviewer session, per section 6 of
`docs/audit-2026-10.md`, from the diff, `notes.md`, the sample's code, the
samples contract (`samples/README.md`) and Umbral's code on the branch.

## Verdict

**Not yet clear. One blocking item, two nits.** Fix the blocking item,
answer in `notes.md`, and I re-check once.

## What was checked

- **Against the samples contract.** The three pages and their markup (`h1`,
  `Public` and `Private` links, `Sign in` link or a `Sign out` form with a
  button, the claims table with `th` and `td`), the settings (two variables
  and `PORT`, no secret, as the contract allows for this type), the protocol
  (code with PKCE, `state` and `nonce` by the library, the three scopes,
  endpoints from discovery), the README order (what to create, how to run,
  what you should see). All as the contract says. The "Cookies" rule of the
  contract concerns a server session, and the README says so.
- **Quoted UI strings.** **Apps**, **Create app**, **Name**, **Redirect URI**,
  **Post-logout redirect URI (optional)**, **App type**, **Single-page or
  native app**, **Client ID**, **Issuer URL** and "Sign in to" are verbatim
  (`Texts.fs:973, 991, 983, 984, 986, 987, 1056, 994, 995, 970`).
- **The README against `src/main.ts`.** `authority` only; `response_type:
  'code'`; the three scopes; `sessionStorage` for both stores;
  `automaticSilentRenew: false`; `loadUserInfo: false`; the five claims in
  the stated order; `signoutRedirect` with the configured post-logout URI;
  `/config.json` from the environment in both `vite.config.ts` and
  `nginx.conf.template`, with `Cache-Control: no-store`. Every statement in
  "How it works" is in the file. "There is no secret" is `AppClients.fs:46`
  (`ClientTypes.Public`) and the Apps screen (`Apps.fs:864`).
- **The redirect paths** are the sample's own, registered by the harness from
  the new `samples.conf` line (`spa 5173 spa /signin-callback
  /signout-callback`); `oidc-client-ts` has no defaults, as the README says.
- **"No refresh token."** The sample never asks for `offline_access`, so none
  is issued; right.
- **Root README, contract table, fragment.** The sentence under "Integrate an
  app" names both samples and their types; the contract's table row matches
  the ASP.NET Core row's shape; `changelog.d/samples-spa.md` is an `### Added`
  fragment in the prescribed format; `changelog.sh check` passes.
- **Links.** `check-links.py`: 208 links in 17 files, all resolve.

## Blocking

1. **The sample needs an Umbral with CORS, and no document says so.** A
   browser-only app fetches discovery, the keys and the token endpoint
   cross-origin, which only works since fw24 (`8d507b3`, `Program.fs:353-372,
   467-485`: any origin on exactly five paths). That commit is unmerged, and
   the released images (0.8.1) refuse those requests. A reader who follows
   this README against the quick start's image gets a CORS error in the
   console and nothing in Umbral's log, and nothing in `samples/spa/README.md`,
   `samples/README.md` or the fragment names the cause. Two things:
   - In the sample README's "How to run it", one sentence: the sample needs
     Umbral **x.y.z** or later (the version fw24 ships in; say "the next
     release" with the fragment's wording if the number is not fixed), because
     a browser app calls Umbral's endpoints cross-origin and older versions
     refuse that. Under "How it works", one bullet "**Cross-origin.** Umbral
     answers the discovery document, the keys and the token, userinfo and
     revocation endpoints from any origin; the authorization and end-session
     endpoints are navigated to, never fetched" (the five paths from
     `Program.fs`), so a developer debugging a CORS error knows which calls
     are expected to work.
   - The branch order: this task merges after fw24, and `notes.md` should say
     so, since the maintainer merges one at a time. If fw24's fragment
     (`changelog.d/cors.md`) and this one land in the same release, the two
     bullets read well together; if not, this README's version sentence is
     what saves the reader.

## Nits

1. **"Why a browser app has no secret": "stays in the browser's
   `sessionStorage` until the token request".** True of the verifier, and
   the previous paragraph has just said `sessionStorage` is readable in the
   developer tools, so a careful reader asks why the verifier there is fine
   when a secret there is not. One clause: "which is fine, since the verifier
   is single-use and worth nothing without the code that only this browser
   received". Yours to take or leave.
2. **"What you should see", the `/private` bullet** says "sends you to
   Umbral's sign-in page" but the sample first bounces through
   `/signin-callback` and `history.replaceState` on the way back, which the
   ASP.NET Core sample does not. Not visible to the person; nothing to
   change in the README. Noted only so the harness's walk (which asserts the
   final URL ends in `/private`) is understood to cover it.

## notes.md

Complete under section 6: every statement of the README against the code or
the catalogue with line numbers, the harness run, the link and changelog
checks. What it lacks is the one thing the blocking item asks for: the
dependency on fw24 stated as a dependency, with the merge order.
