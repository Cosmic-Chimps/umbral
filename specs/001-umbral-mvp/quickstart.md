# Quickstart: validating Umbral MVP

**Feature**: `001-umbral-mvp` | **Date**: 2026-10-09

How to prove the feature works. Scenario 1 is also the thing SC-001 measures, so
it doubles as the release smoke test — run it, with a timer, before every tag.

## Prerequisites

- .NET 10 SDK, Docker (for the image scenarios only)
- For scenarios 1 and 6: a real OAuth application in the provider's developer
  console. Creating it is excluded from the five-minute measurement.
- Everything else runs offline against stubs.

## Running the suite

```bash
dotnet test                                        # from the repo root
dotnet test --filter-class "*ConnectorRoundTripTests*"
dotnet test --filter-method "*links on a verified email*"
```

Microsoft.Testing.Platform filters — `--filter-class` / `--filter-method`, not
VSTest's `--filter`. No Docker needed: each test gets a fresh SQLite file in a
temp directory.

---

## Scenario 1 — First login in five minutes *(SC-001, User Story 1)*

The headline claim. Time it from the `docker run` to the moment the browser lands
back on the app signed in. Stop the clock while in Discord's console.

```bash
docker run -d --name umbral \
  -p 8080:8080 \
  -v umbral-data:/data \
  -e UMBRAL__PUBLIC_ORIGIN=https://auth.example.com \
  ghcr.io/<owner>/umbral:latest
```

1. Open `/admin` → first-run setup. Set the operator credential.
2. Connectors → Discord. **Copy the read-only callback URL shown on the form.**
3. In Discord's developer portal, register that URL, copy the client ID and
   secret back into the form, save.
4. Apps → register one. **Copy the secret now** — it is shown once.
5. Configure the app with the three values and sign in.

**Expect**: a working login. **Expect on a second sign-in**: the same `sub`, and
exactly one user in the Users screen.

**If step 3 fails with `redirect_uri_mismatch`**, `UMBRAL__PUBLIC_ORIGIN` is
wrong. The admin pages should already be showing the origin-mismatch banner —
if they are not, that banner is broken, and that is a bug in its own right.

---

## Scenario 2 — Any OIDC client library *(SC-002, SC-003, User Story 2)*

Proves there is nothing Umbral-specific in the integration.

> **Validated for real on 2026-10-10.** FrunchyBackend — an existing F# app
> with its own Google and Discord sign-in — added Umbral as a third provider
> using `Microsoft.AspNetCore.Authentication.OpenIdConnect`, the framework's
> own handler, and signed a person in. No Umbral-specific library, no
> Umbral-specific code: a package reference, a registration block, and three
> configuration values.
>
> That is the first evidence for SC-002 that does not come from Umbral's own
> test harness, and it is worth more than the rest of this file. Everything
> below still runs; this is the thing that proved it.

```bash
curl -s https://auth.example.com/.well-known/openid-configuration | jq
```

**Expect**: `issuer` exactly equal to `UMBRAL__PUBLIC_ORIGIN`;
`grant_types_supported` is `["authorization_code","refresh_token"]` with no
`implicit` and no `password`; `code_challenge_methods_supported` is `["S256"]`
with no `plain`.

Then drive a stock OIDC client — one that knows nothing about Umbral — through a
full authorization-code + PKCE login, and validate the `id_token` against
`/.well-known/jwks`.

**Expect**: `sub`, `iss`, `aud`, `exp` always; `email`/`email_verified` with the
`email` scope when a provider asserted them; `name`/`picture` with `profile`.

**The failure to watch for**: an `id_token` holding only `sub`, `iss`, `aud`,
`exp` and nothing else. That is a missing `SetDestinations` call, and it produces
no error anywhere. See [contracts/oidc-provider.md](./contracts/oidc-provider.md).

```bash
curl -s https://auth.example.com/connect/userinfo -H "Authorization: Bearer $ACCESS_TOKEN" | jq
```

**Expect**: the same identity as the `id_token`. The access token is an opaque
reference token — if you can decode it as a JWT, the token-format decision has
regressed.

---

## Scenario 3 — Change a connector with no restart *(SC-004, User Story 3)*

```bash
docker logs -f umbral &        # watch: there must be no restart
```

1. With a working login, disable the connector. Reload `/signin`.
   **Expect**: the button is gone on the very next page load.
2. Re-enable it, replace the client secret with the provider's current one.
   **Expect**: the next sign-in uses the new secret.
3. Return to the Connectors screen.
   **Expect**: the secret is masked to its last four characters, and the only
   action offered is **Replace** — never **Edit**, and never a reveal.
4. Set a deliberately wrong secret and attempt a login.
   **Expect**: the end user sees a message telling them to contact the operator
   and nothing technical; the cause is in the security log.

**Expect throughout**: the process never restarted, and nobody already signed in
was logged out.

---

## Scenario 4 — Disable a user *(SC-009, User Story 4)*

With two users signed in:

1. Disable user A.
   **Expect**: A cannot complete a new sign-in at any app; B is unaffected.
2. Attempt a refresh with A's refresh token.
   **Expect**: `invalid_grant`.
3. Use A's access token at `/connect/userinfo`.
   **Expect**: rejected — immediately, not at expiry. This is what reference
   tokens buy, and it is the point of the decision.
4. Re-enable A. **Expect**: A signs in and keeps the same `sub`.
5. Delete A, then sign in again with the same Discord account.
   **Expect**: a new user with a **different** `sub`.

---

## Scenario 5 — Restart and config round-trip *(SC-005, SC-006, User Story 5)*

```bash
docker restart umbral
```

**Expect**: everyone signed in stays signed in; tokens issued before the restart
still validate. A failure here means the keys are not persisting to `/data/keys`.

```bash
curl -s https://auth.example.com/admin/config -b "$ADMIN_COOKIE" > umbral.yaml
docker run -d --name umbral2 -p 8081:8080 -v umbral2-data:/data \
  -e UMBRAL__PUBLIC_ORIGIN=https://auth2.example.com \
  -e UMBRAL_DISCORD_SECRET=... \
  -v $PWD/umbral.yaml:/data/umbral.yaml \
  ghcr.io/<owner>/umbral:latest
```

**Expect**: identical connectors and apps, logins working, **no admin UI
interaction**. Apply the file a second time and expect nothing to change and no
duplicates.

**Expect, and confirm it is documented**: imported apps have *fresh* secrets.
App secrets are hashed and cannot be exported — the one asymmetry in the
round-trip, and it must be in the docs rather than discovered here.

Then break the file deliberately — a literal secret instead of `${VAR}`:

**Expect**: startup fails, naming the offending entry and the fix. Nothing partly
applied.

---

## Scenario 6 — Provider edge cases *(FR-011, offline)*

Driven by committed fixtures captured from real providers; no credentials needed.

| Case | Expect |
| --- | --- |
| GitHub user with a private email | `/user/emails` is called; the primary verified address is used |
| GitHub token endpoint returning form-encoded | Parsed correctly |
| GitHub HTTP **200** carrying `{"error": "..."}` | Treated as a failure |
| Discord user with `avatar: null` | A default avatar URL, both index formulas |
| Discord `a_`-prefixed hash | Animated URL |
| Google `email_verified: false` | User created, **never linked** to an existing one |
| Both `iss` spellings from Google | Both accepted |

And the round-trip state machine, with no provider involved at all: missing
cookie, missing `state`, mismatched `state`, expired correlation, replayed
callback, `?error=access_denied`, slug mismatch. **Expect**: every one rejected,
and the replay rejected specifically because the cookie was deleted before the
exchange.

---

## Scenario 7 — Resource ceiling *(SC-007)*

```bash
docker stats umbral --no-stream
docker buildx imagetools inspect ghcr.io/<owner>/umbral:latest
```

**Expect**: idle RSS under 150 MB; `linux/amd64` and `linux/arm64` both present.

---

## Scenario 8 — Documentation *(SC-011, FR-039, FR-040)*

Not automatable, and the one most worth doing honestly: hand the README to
someone who has not seen Umbral and watch them reach a working login without
asking a question or opening another page. Every question they ask is a README
bug.

Then read every operator-facing string in `Views/Texts.fs` against SC-010: each
must name the next action, not only what went wrong.
