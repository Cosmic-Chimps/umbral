# Code review: native and mobile apps (docs/native-apps.md, task n4tv)

Branch `alta/vibrant-regulus-bmdk`, one commit `2d68bda` on `main` at `a70c3bc`. Reviewer: the `umbral` session, 2026-11-05. The page is documentation; this review checks that what it says about Umbral is what the code does.

## Verdict

**Clear to merge.** No blocking items; one nit and two follow-ups for the maintainer, neither of which changes the page.

## Each statement, checked against the code

- **PKCE with `S256` for every app, a request without it refused.** `OpenIddictSetup.fs:279` (`RequireProofKeyForCodeExchange`) and the `RejectPlainCodeChallengeMethod` handler registered at `:282`. The implementer's live check (no `code_challenge` answers 400) agrees.
- **Single-page or native app is a public client with no secret.** `AppClients.fs:46` (`ClientTypes.Public`); the create handler issues no secret for that type. The quoted description is `Texts.fs`'s `AppTypeSpaDescription`, verbatim.
- **One redirect URI per app.** `AppClients.fs:53` adds the one the create form takes; nothing edits the list afterwards, as the README's "Limits of the Apps screen" says.
- **Matched exactly, wildcards refused, another value gets an error page.** `Domains/RedirectUris.fs` refuses wildcards, hostless `http(s)` and fragments and accepts a custom scheme (`RedirectUriTests.fs:20` covers `com.example.app://callback`); OpenIddict compares the rest as strings. The live checks (single-slash form accepted, double-slash and trailing slash refused, an upper-case host refused, port 8401 and `localhost` refused for a `127.0.0.1:8400` registration) are what that code gives.
- **Sign-out.** `EndSession.fs:24-35`: OpenIddict validates `post_logout_redirect_uri` against the app's registered one, carries `state` back, and anything else lands on `/`; Umbral's own session only. The live check agrees. `/connect/logout` is `OpenIddictSetup.fs:34,273`.
- **The session.** `Sessions.fs:61-62`: sliding, 14 days; `SignInAsync` without `IsPersistent`, so a browser-session cookie. "At most 14 days without use" is the right reading of the two together. A session counts only through a connector the app offers: `Authorize.fs:218-229`; `prompt=login` makes Umbral skip it: `Authorize.fs:224`.
- **`offline_access` and revocation.** `AllowRefreshTokenFlow` and the scope at `OpenIddictSetup.fs:279,301`; the refresh grant and the revocation endpoint are in the one permission set every app gets (`AppClients.fs:18-27`).
- **A passkey belongs to Umbral's host.** `WebAuthn.fs:102`, `RPID = origin.Uri.Host`.
- **Desktop loopback, port included, `127.0.0.1` and `localhost` different.** Follows from the exact match; checked live by the implementer.
- The platform statements (RFC 8252, `ASWebAuthenticationSession`, Custom Tabs, Google refusing an embedded WebView, `prefersEphemeralWebBrowserSession` and the iOS prompt) describe the platforms, not Umbral, and are stated without version-specific claims.
- **Checks run by the reviewer:** `check-links.py` (211 links in 17 files, all resolve) and `changelog.sh check`, both clean on the branch.

## Nit

- **N1.** "Umbral requires PKCE with `S256` for every app, and refuses an authorization request without it" is right; it might add "or with the `plain` method", since that is the second thing Umbral refuses and the handler exists for it. Optional.

## Follow-ups for the maintainer, not for this page

- **Should Umbral's session cookie be persistent?** The implementer's finding, in `notes.md`: the cookie is a browser-session cookie, and a system sign-in browser may not keep such a cookie between sign-ins or between apps, so a phone user may see Umbral's page on every app sign-in even while signed in at the provider. Making it persistent (14 days, as the ticket already is) changes how long every browser stays signed in to Umbral, for web apps too, so it is a product decision and a task of its own, if wanted.
- **Loopback redirects on any port.** RFC 8252 §7.3 recommends that a server accept any port on a loopback redirect, since a desktop app often cannot reserve one. Umbral matches the port exactly, which the page says plainly and works around. A task could relax the match for loopback hosts only, if a desktop sample or a user needs it.
