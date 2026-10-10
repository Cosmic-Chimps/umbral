# Contract: the OIDC provider surface

**Feature**: `001-umbral-mvp`

This is the contract an integrating app depends on. It is the only part of Umbral
that other software talks to, so it is the part that must not drift. Everything
here is standard OpenID Connect — the contract is that there is **nothing
Umbral-specific in it** (FR-006, SC-002).

`${ORIGIN}` throughout is `UMBRAL__PUBLIC_ORIGIN`.

## Routes

| Method | Path | Purpose | Handler |
| --- | --- | --- | --- |
| GET | `/.well-known/openid-configuration` | Discovery | OpenIddict |
| GET | `/.well-known/jwks` | Public key set | OpenIddict |
| GET, POST | `/connect/authorize` | Authorization | **Umbral** (passthrough) |
| POST | `/connect/token` | Token | OpenIddict |
| GET, POST | `/connect/userinfo` | User info | **Umbral** (passthrough) |
| POST | `/connect/introspect` | Introspection | OpenIddict |
| POST | `/connect/revoke` | Revocation | OpenIddict |
| GET, POST | `/connect/logout` | End session | **Umbral** (passthrough) |

The three marked **Umbral** are the only handlers written by hand. OpenIddict has
already validated `client_id`, `redirect_uri`, `scope`, `response_type` and PKCE
before they run.

## Discovery document

The shape a client must receive. Values that are promises, not incidental:

```json
{
  "issuer": "${ORIGIN}",
  "authorization_endpoint": "${ORIGIN}/connect/authorize",
  "token_endpoint": "${ORIGIN}/connect/token",
  "userinfo_endpoint": "${ORIGIN}/connect/userinfo",
  "jwks_uri": "${ORIGIN}/.well-known/jwks",
  "end_session_endpoint": "${ORIGIN}/connect/logout",
  "revocation_endpoint": "${ORIGIN}/connect/revoke",
  "introspection_endpoint": "${ORIGIN}/connect/introspect",

  "grant_types_supported": ["authorization_code", "refresh_token"],
  "response_types_supported": ["code"],
  "response_modes_supported": ["query", "form_post", "fragment"],
  "scopes_supported": ["openid", "email", "profile", "offline_access"],

  "code_challenge_methods_supported": ["S256"],
  "id_token_signing_alg_values_supported": ["RS256"],
  "token_endpoint_auth_methods_supported": ["client_secret_basic", "client_secret_post"],

  "claims_supported": [
    "iss", "sub", "aud", "exp", "iat",
    "email", "email_verified", "name", "preferred_username", "picture", "idp"
  ],
  "subject_types_supported": ["public"]
}
```

**`issuer` MUST be derived from `UMBRAL__PUBLIC_ORIGIN` and nothing else.**
Every client library compares the `iss` claim against the discovery document's
`issuer`; a mismatch — the classic reverse-proxy failure — makes every login
fail with an error that names neither the proxy nor the setting.

Note OpenIddict normalises the issuer to a trailing slash, so a configured
origin of `https://auth.example.com` is advertised as `https://auth.example.com/`.
Both the discovery document and the `iss` claim come from the same place, so
they agree, which is what a client actually checks. An earlier draft of this
contract said the two strings must be byte-identical; that was wrong, and the
running server is right.

**`grant_types_supported` MUST NOT contain `implicit` or `password`** (FR-002).

**`code_challenge_methods_supported` MUST NOT contain `plain`.** This one costs
code. The T001 spike established empirically that OpenIddict 7.7.1 advertises
`["plain", "S256"]` and **genuinely accepts `plain` end to end**, and that
`RequireProofKeyForCodeExchange()` is the only PKCE-related method on
`OpenIddictServerBuilder` — there is no builder flag that restricts the method.

Honouring this line therefore requires a custom
`IOpenIddictServerHandler<ValidateAuthorizationRequestContext>` that rejects
`plain` and a configuration change that stops advertising it (T105).

It is worth the code. PKCE with `plain` sends the verifier *as* the challenge,
so an attacker who can read the authorization request learns the verifier and
PKCE protects nothing. Advertising a method we do not want used, and accepting
it when offered, is exactly the kind of quiet default Principle IV exists to
forbid.

## The `id_token`

A signed JWT, never encrypted. What an app can rely on:

```json
{
  "iss": "https://auth.example.com",
  "sub": "k3Jx9fQ2mNpR7vT1wY8bHg",
  "aud": "grafana",
  "exp": 1760000000,
  "iat": 1759998800,

  "email": "sam@example.com",
  "email_verified": true,
  "name": "Sam Rivera",
  "preferred_username": "samr",
  "picture": "https://cdn.discordapp.com/avatars/1234/abcd.png",
  "idp": "discord"
}
```

| Claim | Guarantee |
| --- | --- |
| `sub` | Present always. Stable for the lifetime of the user. Opaque — **not** derived from any provider's identifier (FR-004). Changes only if the user is deleted and signs in again, which is a different person as far as Umbral is concerned. |
| `email`, `email_verified` | Present only with the `email` scope, and only when a provider asserted them. **An app must tolerate their absence** — GitHub users can keep an email private and Discord accounts can be unverified (FR-011). |
| `name`, `preferred_username`, `picture` | Present only with the `profile` scope, and `picture` only when the provider supplied one. |
| `idp` | Umbral's one non-standard claim: the connector slug that authenticated this sign-in. Additive, safe to ignore, and the only honest way for an app to show "signed in with Discord". |

Claims reach a token only when they carry a destination. A claim with no
destination is silently dropped — this is OpenIddict behaviour, not a bug, and it
is the single likeliest defect in the implementation. **A test asserts the claim
content of a real issued token**, not the construction of the identity.

## Access and refresh tokens

Opaque reference tokens — a random identifier, not a JWT (D3). An app must treat
them as bearer strings and not attempt to decode them. This is what lets FR-022
be true: revoking a user's authorization stops their tokens working immediately,
rather than at expiry.

`/connect/userinfo` returns the same identity as the `id_token` for a given
access token (FR-005).

## Errors

Standard OAuth 2.0 / OIDC errors, unchanged:

| Condition | Response |
| --- | --- |
| `redirect_uri` not an exact registered match | `invalid_request`, **and no redirect** — the user stays on Umbral (FR-003). |
| PKCE absent | `invalid_request` (FR-002). |
| Authorization code reused | `invalid_grant`, and a `authorization_code_replayed` security event (FR-008). |
| User disabled since issuance | `invalid_grant` on refresh (FR-022). |
| `prompt=none` with no session | `login_required`. |

Umbral renders its own page for errors it cannot redirect, via
`EnableErrorPassthrough()`. That page must not disclose configuration detail
(FR-037).

## What an integrating app configures

Three values, and nothing else (FR-006):

```
issuer        = https://auth.example.com
client_id     = grafana
client_secret = <shown once at registration>
```

**Conformance is a test, not a claim** (SC-002, SC-003): the suite drives a real
off-the-shelf OIDC client library — one that knows nothing about Umbral —
through a complete authorization-code + PKCE login and validates the resulting
`id_token` against the published key set.
