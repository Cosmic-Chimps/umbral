# Documentation review: native and mobile apps (`docs/native-apps.md`)

Task: `.alta/tasks/2026-10-28-docs-native-and-mobile-apps-n4tv.md`.
Branch: `alta/vibrant-regulus-bmdk`, commit `2d68bda` on `main` at `a70c3bc`.
Reviewed 2026-11-05 by the documentation reviewer session, per section 6 of
`docs/audit-2026-10.md`, from the diff, `notes.md` and the code on the branch.

## Verdict

**Clear to merge.** No blocking item. Two nits, neither needs a re-check, and
one note for the maintainer.

## What was checked

- **Quoted UI strings.** **Apps**, **Create app**, **Single-page or native
  app**, **Redirect URI**, **Post-logout redirect URI (optional)**, **Issuer
  URL**, **Client ID** are verbatim (`Texts.fs:890, 991, 1056, 984, 986, 995,
  994`); the block-quoted type description is `AppTypeSpaDescription`
  (`:990`) word for word; **Passkey** is the connector's name.
- **The app type.** A `SpaOrNative` app gets no secret (`Apps.fs:1207-1209`)
  and is a public, native client (`AppClients.fs:45-46`); PKCE `S256` is
  required of every app and `plain` is refused (`OpenIddictSetup.fs:279,
  73-84`). "Refuses an authorization request without it" matches the
  implementer's live check (400 with no `code_challenge`).
- **Redirect URIs.** `RedirectUris.acceptable` (`Domains/RedirectUris.fs`)
  refuses wildcards, non-absolute values, the file scheme, hostless http(s)
  and fragments, and accepts a custom scheme; matching is exact by
  OpenIddict's registration (README "Integrate an app", and the live checks
  in `notes.md`: `:/` versus `://`, a trailing slash, host case, the loopback
  port, `127.0.0.1` versus `localhost`). "Umbral error page, not a redirect"
  is `Handlers/Connect/Error.fs`.
- **Staying signed in.** `Sessions.signIn` calls `SignInAsync` with no
  `IsPersistent` (`Sessions.fs:74-90`), so the cookie is a browser-session
  cookie; the ticket slides and expires after 14 days (`:61-62`). A session
  counts only through a connector the app offers, and `prompt=login` skips it
  (`Authorize.fs:217-232`, `HasPromptValue PromptValues.Login`). The page says
  what the code does and tells the reader to plan for the sign-in page; that
  is the right thing while the persistence question is open (see the note).
  `offline_access` and the refresh grant are registered for every app
  (`OpenIddictSetup.fs:301`, `AppClients.fs:24`).
- **Signing out.** `/connect/logout` ends Umbral's session only and hands the
  redirect to OpenIddict with `/` as the fallback (`EndSession.fs:24-35`); the
  live check shows the registered custom-scheme URI coming back with `state`
  and an unregistered one landing on `/`. Revocation is permitted to every app
  (`AppClients.fs:21`).
- **Passkeys.** The RP ID is the host of the public origin
  (`WebAuthn.fs:102`), so "belongs to Umbral's host name, not to the app" is
  right, and so is "works in every app that offers the passkey connector".
- **Desktop loopback.** Exact match port included, and `127.0.0.1` differs
  from `localhost`: the live checks in `notes.md`. The page is honest that
  this is stricter than RFC 8252's random port.
- **Platform statements** (RFC 8252's external user-agent, Google's WebView
  refusal, Universal Links and App Links, `ASWebAuthenticationSession`,
  `prefersEphemeralWebBrowserSession` and the iOS consent prompt, Custom
  Tabs) are the platforms' documented behaviour, not Umbral's; they read
  right and are stated as the platforms', not as Umbral's.
- **README and fragment.** One sentence after the samples paragraph in
  "Integrate an app", linking the page; `changelog.d/native-apps.md` is an
  `### Added` fragment in the prescribed format; `changelog.sh check` passes.
- **Links.** `check-links.py`: 211 links in 17 files, all resolve, the
  samples links included now that Samples 1/4 is on `main`.

## Nits

1. **"Signing out", step 1: "revoke the refresh token at the revocation
   endpoint".** Step 2 names its endpoint both ways (`end_session_endpoint`,
   `/connect/logout`); step 1 should too: "(`revocation_endpoint` in
   discovery, `/connect/revoke`)". The README's Endpoints table has it; the
   reader of this page should not need to go there.
2. **"Staying signed in", first bullet.** "The system's sign-in browser may
   not keep such a cookie from one sign-in to the next, or between two apps"
   is true and hedged on purpose. One clause on what the two platforms do
   would let a developer plan rather than guess: a non-ephemeral
   `ASWebAuthenticationSession` shares Safari's cookie store, and a Custom
   Tab shares the default browser's, so the session cookie lives as long as
   that browser keeps its session. Optional, since the maintainer's decision
   below may change the paragraph anyway.

## For the maintainer (outside this task)

The implementer proposed a task on whether Umbral's end-user session cookie
should persist (today it is a browser-session cookie with a 14-day sliding
ticket). Whatever is decided, the "Staying signed in" section of this page and
the README's "Integrate an app" describe the current behaviour and must change
with it; that task should carry the doc change, as h762 did.

## notes.md

Complete under section 6: every statement with its source, the quoted strings
with line numbers, the live checks against an Umbral built from the branch
(which settled the redirect and sign-out rules better than reading the code
would), the link and changelog checks, and the one open question raised
rather than papered over. Agreed on all of it.

---

## Addendum, 2026-11-05, at `dbb9568`

**Still clear.** Both nits taken: step 1 of "Signing out" names
`revocation_endpoint` and `/connect/revoke`; "Staying signed in" says which
cookie store each platform's sign-in browser uses and leaves keeping a
browser-session cookie to that browser. The code review's nit (the `plain`
method is refused too) matches `OpenIddictSetup.fs:73-84`. Link check: 211
links in 17 files, all resolve. The documentation review is complete.
