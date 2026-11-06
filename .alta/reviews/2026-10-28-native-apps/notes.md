# Native and mobile apps: notes for the reviews

Task `2026-10-28-docs-native-and-mobile-apps-n4tv`, branch `alta/vibrant-regulus-bmdk`, on `main`
at a70c3bc.

## What changed

- `docs/native-apps.md` (new).
- `README.md`, "Integrate an app": one sentence linking to it, after the samples paragraph.
- `changelog.d/native-apps.md`, Added.

## Each statement and what it was checked against

"Checked live" means: against an Umbral built from this branch, in the samples harness's
containers, with a throwaway Playwright script (not committed) that created three apps of type
**Single-page or native app** and sent authorization and end-session requests, on 2026-11-05.

| Statement | Checked against |
| --- | --- |
| Endpoints come from the discovery document | `Infrastructure/OpenIddictSetup.fs:270-275` (the endpoint URIs OpenIddict publishes) |
| The person signs in with the connectors the app offers; the code comes back to the redirect URI | `Handlers/Connect/Authorize.fs:217-232` (FR-008), README "Endpoints" |
| The claims are the same as for any app | `AppClients.fs:18-27` (one permission set for both types), README "Integrate an app" |
| **Apps**, **Create app**, **Single-page or native app**, **Redirect URI**, **Post-logout redirect URI (optional)**, **Issuer URL**, **Client ID**, **Passkey** | quoted from `Views/Texts.fs:890,991,1056,984,986,995,994`; **Passkey** is the connector's name in the Connectors list (`docs/connectors/passkey.md`) |
| The quoted description of the type | `Views/Texts.fs:990` (`AppTypeSpaDescription`), verbatim |
| No secret: a public client | `Handlers/Admin/Apps.fs:1207-1209` (no secret for `SpaOrNative`), `AppClients.fs:45-46` (`ClientTypes.Public`, `ApplicationTypes.Native`); checked live: no shown-once secret for any of the three apps |
| PKCE with S256 required for every app; a request without it is refused | `OpenIddictSetup.fs:279` (`RequireProofKeyForCodeExchange`), `:73-84` (plain refused), `AppClients.fs:61` (the requirement on every app); checked live: no `code_challenge` answers 400 |
| One redirect URI per app | `Apps.fs:1175-1211` (one `redirectUri` field), README "Limits of the Apps screen" |
| Matched exactly; wildcards refused; another value gets an error page, not a redirect | `Domains/RedirectUris.fs:22-23` (wildcard), OpenIddict's exact comparison; checked live: `com.example.app:/callback` 302 to the sign-in page, `com.example.app://callback` and `com.example.app:/callback/` 400; `https://app.example.com/oauth/callback` 302, the same with `APP.` in the host 400 |
| A custom scheme is accepted | `RedirectUris.fs:25-48` (only file, hostless http(s) and fragments refused), `tests/Umbral.Tests/RedirectUriTests.fs:20`; checked live with the single-slash form |
| Claimed https links: the platform gives them only to the app that proves the domain | platform behaviour (Universal Links, App Links), not Umbral's |
| Umbral's session cookie is a browser-session cookie, at most 14 days without use | `Infrastructure/Sessions.fs:46-62` (sliding, 14 days) and `:74-90` (`SignInAsync` with no `IsPersistent`) |
| A session counts only if it came through a connector the app offers | `Authorize.fs:217-232` |
| `prompt=login` makes Umbral ignore its session | `Authorize.fs:224` |
| `offline_access` gives a refresh token | `OpenIddictSetup.fs:279,301` (refresh flow, the scope registered), `AppClients.fs:24` (refresh grant permission), README "What an app can count on" |
| Revoking the refresh token | `AppClients.fs:21` (revocation permission for every app), README "Signing out" |
| The end-session endpoint is `/connect/logout`; Umbral ends only its own session | `OpenIddictSetup.fs:34,273`, `Handlers/Connect/EndSession.fs` |
| Redirects only to the registered post-logout URI, with `state`; otherwise Umbral's home page | `EndSession.fs:24-35` (OpenIddict validates the URI; `RedirectUri = "/"` otherwise); checked live: registered `com.example.app:/signout` 302 to `com.example.app:/signout?state=z`, an unregistered one and none 302 to `/` |
| A passkey belongs to Umbral's host name | `Infrastructure/WebAuthn.fs:102` (`RPID = origin.Uri.Host`) |
| Loopback redirect matched exactly, port included; `127.0.0.1` and `localhost` differ | checked live: registered `http://127.0.0.1:8400/callback` 302, port 8401 400, `http://localhost:8400/callback` 400 |
| Branding an app's sign-in page | README "Brand an app's sign-in page" |

Platform statements (Google refusing WebViews, `ASWebAuthenticationSession`, Custom Tabs,
`prefersEphemeralWebBrowserSession` and the iOS prompt it avoids, RFC 8252) describe the platforms,
not Umbral, and are worded so that they do not depend on a version.

## A finding for the maintainer

The task expected the system browser to share Umbral's session across apps. Umbral's session
cookie is not persistent (`Sessions.fs`, `SignInAsync` without `IsPersistent`), and a system
sign-in browser may well not keep such a cookie between sign-ins or between apps (developers
report iOS shares only persistent cookies with `ASWebAuthenticationSession`; Apple's page does
not say). The page therefore says what Umbral does and tells the reader to plan for the sign-in
page appearing again. Whether the session cookie should become persistent is proposed as a
separate task; it changes how long a browser stays signed in for every app, not only native ones.

## How it was verified

- The live checks above.
- `.github/scripts/check-links.py`: all links resolve.
- `.github/scripts/changelog.sh check`: in order.

## Changelog

`changelog.d/native-apps.md`, Added.

## Answers to the reviews

Documentation review (review.md, clear, two nits), both taken:

1. "Signing out" step 1 names the revocation endpoint both ways (`revocation_endpoint`,
   `/connect/revoke`).
2. "Staying signed in" says which cookie store each platform's sign-in browser uses (Safari's
   for a non-ephemeral `ASWebAuthenticationSession`, the default browser's for a Custom Tab) and
   that keeping a browser-session cookie is up to that browser. Still hedged, since the
   persistence decision (task c5hn) may change the paragraph; that task carries the doc change.

Code review (code-review.md, clear, one optional nit), taken: the PKCE sentence says the
`plain` method is refused too (`OpenIddictSetup.fs:73-84`).

All three are wording changes inside sentences already reviewed. The link check still passes.
