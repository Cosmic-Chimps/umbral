# Quickstart: proving the passkey connector works

## Automated

```bash
dotnet build -c Release          # warnings are errors
dotnet test -c Release           # the whole suite, no Docker, no network
```

The suites this feature adds (see tasks.md): `PasskeyDomainTests`
(pure rules), `PasskeyCeremonyTests` (Stories 1, 2 and 4 through the real
endpoints with the software authenticator), `PasskeyAccountTests` (Story 3),
`PasskeyInvitationTests` (Stories 1 and 5), `PasskeyAdminTests` (Story 6), the
new cases in `DatabaseTests` (v5 → v6), `SecurityEventCoverageTests` (the four
new types and a refusal), `LogRedactionTests` (no credential or secret in the
logs at Trace), `TokenClaims`/`OidcProtocolTests` (a passkey user's tokens),
`AllowedDomainsSignInTests` (a passkey session survives an instance list), and
the third run of `IdleMemoryTests`.

What the software authenticator (`tests/Umbral.Tests/Infrastructure/SoftwareAuthenticator.fs`)
must be able to do, each as a test: register and sign in correctly; sign for
`https://evil.example` (origin); hash another RP id; reuse a ceremony id and a
challenge; omit the UV flag; omit the UP flag; report `credProps.rk = false`;
present an unknown credential id; present a user handle of another user;
present a counter equal to or below the stored one when the stored one is
non-zero; present a counter of 0 twice (accepted); sign with the wrong key.

## By hand, on a laptop

Passkeys work on `http://localhost` in Chrome, Edge, Firefox and Safari.
Chrome's DevTools has a virtual authenticator (More tools → WebAuthn → Enable
virtual authenticator environment → add one with "Supports resident keys" and
"Supports user verification" ticked) for a machine without a fingerprint
reader.

1. Run the quick start of the README (`docker run …` with
   `UMBRAL__PUBLIC_ORIGIN=http://localhost:8080`), claim the instance, register
   an app (`http://localhost:3000/callback`).
2. **Connectors** → **Passkey** card → leave **Invitations only** → **Enable
   passkeys**. The card shows enabled, shared, and **Open invitations: none**.
3. **Invite someone**, note `Ana`. Copy the link shown once. The card lists the
   invitation.
4. Open the link in a private window. Type `Ana`, click **Create my passkey**,
   confirm on the virtual authenticator. You land on **Your passkeys** with one
   passkey. The admin card no longer lists the invitation; **Users** shows Ana
   with a **Passkey** pill and one passkey.
5. In the private window, open the README's step-6 authorize URL. The sign-in
   page shows **Sign in with a passkey**. Click it, then **Continue with your
   passkey**, confirm. The browser lands on `http://localhost:3000/callback?code=…`.
   Exchange the code with the README's `curl`; the `id_token` has `sub`,
   `name: "Ana"`, `idp: "passkey"` and no `email`.
6. Open the invitation link again: **This invitation link can't be used**.
7. **Users** → Ana → **Invite to add a passkey**; open the link in the private
   window (signed in or not): it says it adds a passkey to **Ana**; add one.
   Two passkeys on the page; remove the first one; try to remove the last one:
   refused with the message.
8. Sign in to the app with GitHub (or the fake connector in Development), open
   `http://localhost:8080/account/passkeys`, **Add a passkey**, then sign in to
   the app with it: same `sub` as with GitHub.
9. **Connectors** → **Passkey** → **Anyone can sign up** → Save. In a fresh
   private window, start the app's authorize URL, **Sign in with a passkey**,
   **Create an account with a passkey** with a name: a new user, back at the
   app with a code. **Apps** warns that the connector lets anyone in.
10. Disable the connector: the sign-in page no longer offers it;
    `/connect/passkey/start` is 404; **Your passkeys** says passkeys are not
    enabled but still removes. Enable again: everything works.
11. Read the log:

    ```bash
    sqlite3 -readonly /path/to/umbral.db "select occurred_utc, event_type, outcome, actor, user_id, detail from security_events where connector_slug = 'passkey' or event_type like 'passkey_%' order by id desc limit 30;"
    ```

    Expect `connector_created`, `passkey_invitation_created`, `user_created`,
    `passkey_registered`, `sign_in_succeeded`, `sign_in_failed` with
    `passkey_refused;reason=invitation_used;stage=invitation`,
    `passkey_removed`, and nothing that looks like a key or a link.

## Automating the walk

The steps above were run on 2026-10-19 in a headless Google Chrome driven
through the DevTools Protocol, which is the one way to exercise
`src/Umbral/Assets/passkeys.js` without a person: `WebAuthn.enable`, then
`WebAuthn.addVirtualAuthenticator` with `protocol: ctap2`, `transport:
internal`, `hasResidentKey`, `hasUserVerification`, `isUserVerified` and
`automaticPresenceSimulation`, then the clicks. Two things to know when
repeating it: the Create app form on `/admin/apps` is a dialog, so use
`/admin/apps/new`; and a second passkey for the same account needs a second
virtual authenticator, because the first one holds a credential the options
exclude and the browser refuses without asking the server.

## Memory

```bash
dotnet test -c Release --filter "FullyQualifiedName~IdleMemoryTests"
```

Three lines in the output: local store (ceiling 150 MiB), aws store (160
MiB), passkey connector after one registration and one sign-in (160 MiB). The
figures are asserted on Linux only; on macOS they are printed. Record them in
plan.md once measured on linux/amd64 and linux/arm64 (CI's job summary).
