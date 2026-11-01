# T2: "Integrate an app" and "How people are identified" match the code — notes for the reviews

Task: `.alta/tasks/2026-10-23-docs-t2-integrate-an-app-matches-the-code-t2id.md`,
audit `docs/audit-2026-10.md` section 5, T2 (findings W1, W2, W3, M4). Branch
`docs/t2-identity`, based on `main` at `3a235da` (T1 at `15aacab`/`df2d765`, T9
at `e495349` and spec 010 are ancestors).

## Files

- `README.md`: "Integrate an app" (claims bullets; new `### Endpoints`,
  `### Signing out`, `### When a person is disabled or deleted`), "How people
  are identified", Contributing (the vision link).
- `docs/connectors/oidc.md`: "What Umbral takes from your provider", the
  Identity and Email bullets.
- `tests/Umbral.Tests/OidcProtocolTests.fs`, `tests/Umbral.Tests/PasskeyInvitationTests.fs`:
  the claims tests.
- `CHANGELOG.md`: one entry under `## Unreleased`, `### Fixed`.

Line numbers below are the branch's.

## Each finding and what changed

| Finding | Change |
| --- | --- |
| W1, `preferred_username` promised and never emitted | Already half-fixed on `main` by spec 010: the code now emits `preferred_username` and `profile`, set only from a fediverse sign-in (see below), and the README said so. Kept in that shape (not a general claim) and added "the other providers send neither". The other half of W1, "`name` ... when the provider supplied them", is fixed: `name` is "always there", with the three sources (provider, passkey typed name, placeholder). README:356-361. |
| W2, unverified email is emitted, not missing | The bullet now says `email` and `email_verified` come both or neither; neither only when Umbral has no address (fediverse, passkey-only, GitHub without a verified primary); `email_verified` can be `false` (Discord, Google, own OIDC provider). README:346-352. The old "a GitHub user can keep their email private" was also wrong: Umbral reads `/user/emails`, which returns private addresses (see below). |
| W3, OIDC rule read as "no linking by email" | "How people are identified" rewritten: the account key (provider id; for OIDC issuer plus `sub`) is one statement, linking by a verified email is another, and it applies to every connector kind, OIDC included. The ambiguous case is spelled out. README:471-492. `oidc.md` Identity and Email bullets now say the same, and link to the README section. oidc.md:201-215. |
| M4, what an app developer needs next | Endpoints table with what each does; introspection not available to apps; the id_token versus userinfo; signing out; disabled and deleted people; email first-seen. README:353-355, 382-432. Plus the linking edge cases above. |
| (T9) vision link | Contributing: "The vision is in [docs/vision.md](docs/vision.md)." README:1487. |

Where the code and the audit disagreed, the code won:

- The audit (written before spec 010) says "No `preferred_username` until the
  code emits it". It now does, for the fediverse only; the README keeps 010's
  wording, as the task file says.
- The audit says introspection is "registered but apps are not permitted to
  call it". True, and it is also *listed in the discovery document*, which an
  integrator will see; the README says both.
- The audit says logout "accepts only the registered post-logout URI". Run
  against the code (below), an unregistered URI is not refused: the person is
  still signed out and lands on Umbral's home page. The README says that.

## Each behavioural statement and the code

**Claims (README:346-366)**

- Claims set on the id_token: `Handlers/Connect/Authorize.fs:96-127`
  (`setProfileClaims`: `sub`, `name`, `idp` always; `email` + `email_verified`
  together when `user.Email` is `Some`, `:107-115`; `picture`, `:117-119`;
  `preferred_username`, `:121-123`; `profile`, `:125-127`). Destinations by
  scope: `:38-82` (`name`/`preferred_username`/`profile`/`picture` to the
  id_token with `profile`, `:55-59`; `email`/`email_verified` with `email`,
  `:61-65`; `idp` always, `:69-73`).
- Userinfo: `Handlers/Connect/UserInfo.fs:46-76`, same gating; `email_verified`
  a JSON boolean, `:71`; `idp` from the token, `:74-76`.
- `email_verified` is a boolean, not a string: `Authorize.fs:110-114`.
- Neither email claim when there is no address: fediverse,
  `Infrastructure/Providers/Fediverse.fs:574-575`; passkey-only user,
  `Infrastructure/Passkeys.fs:209-213` (`email NULL`); GitHub,
  `Providers/GitHub.fs:121-131,178-195` (only a primary *and* verified address
  from `/user/emails`, else none).
- Unverified addresses passed on: Discord, `Providers/Discord.fs:147-155`;
  Google, `Providers/Google.fs:196`; own OIDC, `Providers/Oidc.fs:47-51,195`.
- Email first-seen, flag only upwards: `Infrastructure/UserStore.fs:152-161`
  (`email = COALESCE(email, @email)`, `email_verified = CASE WHEN @verified = 1
  THEN 1 ELSE email_verified END`).
- `name` always present: `UserStore.fs:129-133` (placeholder
  `kind.DisplayName + " user"`, `Domains/Connectors.fs:50-58`, so "OpenID
  Connect user" for an OIDC provider that sends neither `name` nor
  `preferred_username`); passkey user's typed name, `Passkeys.fs:204-215`;
  `setProfileClaims` sets it unconditionally, `Authorize.fs:98`.
- `preferred_username` and `profile` only from the fediverse: the only adapter
  that sets `Username`/`ProfileUrl` is `Providers/Fediverse.fs` (grep
  `Username =` over `Providers/`).

**Endpoints (README:382-405)**

- Paths: `Infrastructure/OpenIddictSetup.fs:25-40,270-275`; `/.well-known/jwks`
  is the path `OidcProtocolTests` "the key set is published and usable" reads.
- `/connect/authorize` sends the code straight back for a session through an
  offered connector: `Authorize.fs:217-301,303-398`; otherwise the sign-in
  page, `:400-412`.
- A refresh re-reads the person: `Handlers/Connect/Token.fs:61-77,142`
  (`setProfileClaims` again); disabled or deleted gets `invalid_grant`,
  `:69-77`.
- Userinfo GET and POST, reads the person each call, `401 invalid_token` when
  disabled or deleted: `UserInfo.fs:30-43,81-83`.
- Revocation allowed to apps: `Infrastructure/AppClients.fs:18-27`
  (`Permissions.Endpoints.Revocation`).
- Introspection not allowed: the same list has no
  `Permissions.Endpoints.Introspection`; the endpoint is set,
  `OpenIddictSetup.fs:275`, so discovery lists it. Run (below): `400`,
  `unauthorized_client`.

**Signing out (README:407-419)**

- Ends Umbral's session only, never the provider's: `Handlers/Connect/EndSession.fs:1-6,25`.
- Post-logout redirect validated by OpenIddict's sign-out: `EndSession.fs:27-35`.
- Run (below): registered URI plus `state` → `302` to
  `https://grafana.example.com/bye?state=s1`; an unregistered URI → `302 /`,
  and the session is gone (the next authorize goes to `/signin`); without
  one → `302 /`; userinfo with the earlier access token still `200` after
  logout; `/connect/revoke` → `200`, then userinfo `401`.

**Disabled or deleted (README:420-433)**

- Disabling revokes every authorization: `Handlers/Admin/UserToggle.fs:1-8,57`;
  deleting too: `Handlers/Admin/UserDelete.fs:1-4,56`.
- The sign-in page message: `Views/Texts.fs:703` ("This account has been
  disabled. Contact whoever runs this Umbral."), sent by the provider callback,
  `Handlers/Connect/Callback.fs:93-98`, and by the passkey ceremony,
  `Handlers/Passkeys/Ceremonies.fs:264`.
- With a session: disabled → `access_denied`, `Authorize.fs:321-339`; deleted
  → `login_required`, `:314-319`. Run (below).
- Signing in again after deletion gives a new `sub`: the user row and its
  `linked_accounts` go (`UserDelete.fs:1-4`), so `UserStore.resolve` finds no
  link and `Ids.newSubject` makes a new one, `UserStore.fs:111-115`.

**Linking (README:471-492; oidc.md:201-215)**

- Account key: `(provider_kind, provider_subject)`, `UserStore.fs:86,174-188`;
  for OIDC the subject is `issuer#sub`, `Domains/Connectors.fs:329`.
- The rule: `Domains/Linking.fs:48-101` (existing link first; no email or
  unverified → new; one verified match → link; none → new; several → new,
  `AmbiguousMatch`). The candidates are users whose stored email is verified:
  `UserStore.fs:94-100` and `Linking.fs:87`.
- Every connector kind: `UserStore.resolve` takes any `ProviderKind`; the add
  form's warning, `Texts.fs:1149-1151` (`ConnectorEmailTrustWarning`, "Add
  only a provider you run or trust").
- `email_verified` must be the JSON `true` for OIDC: `Providers/Oidc.fs:47-51,195`.

## The runs

Logout, introspection, disabled and deleted were checked with throwaway tests
(not committed) in a scratch worktree of `main` at `3a235da`, against the real
app (`Fixtures.createAppWith`):

```
DISCO has introspection: True
INTROSPECT 400 { "error": "unauthorized_client", "error_description": "This client application is not allowed to use the introspection endpoint." ... }
LOGOUT-BAD 302 loc=/
AUTHORIZE-AFTER-BAD-LOGOUT 302 loc=/signin?returnUrl=...
LOGOUT-PLAIN 302 loc=https://grafana.example.com/bye?state=s1
USERINFO-AFTER-LOGOUT {"sub":"...","name":"Sam Rivera","email":"sam@example.com","email_verified":true,"idp":"fake"}
REVOKE 200
USERINFO-AFTER-REVOKE 401
UPDATE (disabled) 302 loc=https://grafana.example.com/callback?error=access_denied&error_description=This account has been disabled.&...&state=x
DELETE (deleted)  302 loc=https://grafana.example.com/callback?error=login_required&error_description=The signed-in user no longer exists.&...&state=x
```

## The test

Two tests, each asserting the id_token **and** userinfo with scopes
`openid email profile`, and pinning the set of identity claims present (out of
`sub`, `name`, `email`, `email_verified`, `picture`, `preferred_username`,
`profile`, `idp`), so adding, dropping or retyping one fails:

- `OidcProtocolTests.fs:526`, "a provider user with an unverified email gets
  email_verified false, and the fallback name": a fake-provider sign-in with an
  email, no verified flag and no display name. Both carry exactly `sub`,
  `name` ("Fake (development) user", the placeholder), `email`,
  `email_verified` (JSON `false`) and `idp`; userinfo has no other property.
  The fake provider's sign-in was generalised (`authorizationRedirectAs`); the
  existing helper calls it with the same fields as before.
- `PasskeyInvitationTests.fs:292`, "a passkey-only user's tokens carry no
  email": Ana, enrolled by invitation, signs in with her passkey. Both carry
  exactly `sub`, `name` ("Ana", the name she typed) and `idp` (`passkey`).

Together with "THE claims test" (verified email, `name` from the provider) and
the fediverse `preferred_username`/`profile` tests already in
`OidcProtocolTests.fs`, every bullet of the README's claim list has a test.

```
$ dotnet test --filter "...unverified email gets|...passkey-only user"
  total: 2  failed: 0  succeeded: 2
$ dotnet test
Test run summary: Passed!
  total: 1786  failed: 0  succeeded: 1786  skipped: 0
```

## Link check

```
$ python3 .github/scripts/check-links.py
check-links: 187 links in 14 files, all resolve
(exit 0)
```

Greps over `docs/` exclude `docs/audit-2026-10.md`.

## CHANGELOG

One entry under `## Unreleased`, `### Fixed`: the README told app developers an
unverified email is left out, and that two OIDC providers never bring people
together; both are corrections of what an integrator relies on, so they are
worth a line even though Umbral's behaviour did not change. (`changelog.d/`,
task cl6d, is not on `main`; if it merges first, this entry becomes a fragment.)

## Found on the way, not in this task

The rule "the flag can only go from `false` to `true`" (`UserStore.fs:158-159`)
raises the flag for whatever address the provider verifies *today*, while
keeping the first-seen address. Reported to the maintainer as a separate task;
the README states what the code does now and should change with the fix.

## Answers to the documentation review (review.md at ec9de57, clear)

1. Nit 1, GitHub without an email: taken. The email bullet now reads "a GitHub
   account with no verified primary address or whose addresses GitHub did not
   return on that sign-in" (`Providers/GitHub.fs:180-189`).
2. Nit 2, the two error codes: taken, with one change of meaning checked
   against the code. A new paragraph says both come back to the app's redirect
   URI as an ordinary OpenID Connect error, that Umbral has already ended the
   session (`Authorize.fs:318` for a deleted user, `:325` for a disabled one),
   so the app treats them as "sign in again", and that a disabled person who
   does is told why on Umbral's page (`Callback.fs:93-98`, `Texts.fs:703`).
   "Sign in again" alone would suggest a disabled person can get back in.
