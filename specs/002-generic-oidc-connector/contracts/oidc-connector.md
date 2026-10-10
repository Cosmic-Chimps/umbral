# Contract: OpenID Connect connector

**Feature**: `002-generic-oidc-connector`

Three surfaces change: the admin routes and screen, the configuration file, and
what Umbral sends to and accepts from an identity provider. Routes and
behaviour not listed here are as in spec 001's
[admin-and-config](../../001-umbral-mvp/contracts/admin-and-config.md) and
[connectors](../../001-umbral-mvp/contracts/connectors.md) contracts.

## Admin routes

| Method | Path | Change |
| --- | --- | --- |
| GET | `/admin/connectors` | An `oidc` connector is a card like any other, with its issuer read-only; the kind has no empty card and is added through spec 003's **Add another connector** form (Provider → "OpenID Connect provider"). |
| POST | `/admin/connectors` | **Extended** (spec 003's add route): accepts `kind = oidc` with an `issuer` field and runs the discovery check (below). For `oidc`, errors re-render the form with the input kept. |
| POST | `/admin/connectors/{slug}` | Unchanged route. For an `oidc` slug, updates display name, client ID, client secret (replace only) and extra scopes. Slug and issuer are not accepted fields. |
| POST | `/admin/connectors/{slug}/toggle` | For an `oidc` connector, **enabling** runs the discovery check first and refuses with the reason. Disabling never checks. |
| POST | `/admin/connectors/{slug}/delete` | Unchanged. Deleting leaves users and linked accounts in place (spec 001 behaviour), so re-adding the same issuer reconnects them. |

All are operator-only, antiforgery-protected, htmx-optional (spec 001 rules).

### The add form (spec 003's "Add another connector", kind OpenID Connect provider)

| Field | Rule | Error shown (input kept) |
| --- | --- | --- |
| Display name | required, ≤ 64 chars | "Give the provider a name people will recognise on the sign-in page." |
| Short name | spec 003's rule: `Ids.isValidSlug`, unused, not another kind's name | names the rule broken and the allowed form, e.g. "Use up to 32 lowercase letters, digits or hyphens, like `home` or `club-keycloak`." |
| Issuer URL | absolute https, no query or fragment (localhost exception) | "The issuer must start with https://. Put the provider behind https — the walkthrough shows how with the reverse proxy that already fronts Umbral." |
| Client ID | required | |
| Client secret | required (Clarification Q3) | "Umbral signs in as a confidential client, so it needs the client secret your provider generated." |
| Extra scopes | optional, space-separated (⟨scopes⟩ — dropped if spec 004 is confirmed) | |
| Shared | spec 003's checkbox, ticked by default | |

The form shows, before submission, the email-trust warning (FR-009) and, once
the short name is typed or after save, the callback URL
`<UMBRAL__PUBLIC_ORIGIN>/connect/<slug>/callback` read-only with a Copy button.

### Discovery check outcomes (save and enable)

| Outcome | Save | Enable |
| --- | --- | --- |
| Document fetched and every check passes | Saved **enabled**; notice with the callback URL to register | Enabled |
| Provider unreachable (network error, timeout, non-2xx, not a JSON object) | Saved **disabled**; warning: "Umbral could not reach `<issuer>`: `<reason>`. The provider was saved but is disabled — enable it once `<issuer>/.well-known/openid-configuration` opens in a browser." | Refused with the same reason |
| Reachable but `issuer` differs | Refused: "That address describes itself as `<actual>`, not `<typed>`. Use exactly the issuer your provider shows, including any path." | Refused (same text) |
| Reachable but a required endpoint is missing or not https | Refused, naming the field | Refused |
| No asymmetric signing algorithm advertised | Refused. HS-only: "This provider only signs with a shared secret. Switch its signing key to RS256 or ES256 — see the walkthrough." Anything else (e.g. EdDSA): names the advertised algorithms and the accepted ones. | Refused |
| Neither `client_secret_basic` nor `client_secret_post` supported | Refused, naming the capability | Refused |

Every outcome records a `connector_created`/`connector_updated`/`connector_enabled`
event or, for refusals, nothing (nothing changed).

## Configuration file

```yaml
connectors:
  - slug: home                                   # immutable, in the callback URL
    kind: oidc
    issuer: https://auth.example.com/application/o/umbral   # immutable
    display_name: Home
    client_id: umbral
    client_secret: ${UMBRAL_HOME_SECRET}
    enabled: true
    extra_scopes: []
```

- `issuer` is **required** for `kind: oidc` and an **error** on any other kind.
- Startup validation adds: slug rule, issuer shape, no two entries with one
  issuer. Each error names the entry (`connectors[2].issuer`) and the fix.
- Apply never contacts the provider. The file's `enabled` value is written as is.
- Applying a file whose entry changes the `issuer` of an existing slug is an
  error ("issuer cannot change; delete the connector or use a new slug"), since
  the issuer is part of every person's identity.
- Export writes `issuer:` for every `oidc` connector; secrets as
  `${UMBRAL_<SLUG>_SECRET}` placeholders, as today.

## Protocol: Umbral as a client of the identity provider

**Authorization request** (`GET authorization_endpoint`):

| Parameter | Value |
| --- | --- |
| `response_type` | `code` |
| `client_id` | the connector's client ID |
| `redirect_uri` | `<origin>/connect/<slug>/callback` (exact, from configuration) |
| `scope` | `openid email profile` + extra scopes |
| `state` | single-use, from the shared round trip |
| `nonce` | single-use, from the shared round trip |
| `code_challenge`, `code_challenge_method` | `S256`, unless the document lists methods without `S256` |

**Token request** (`POST token_endpoint`): `grant_type=authorization_code`,
`code`, `redirect_uri`, `code_verifier` (when PKCE was sent); client
authentication by `client_secret_basic` (default) or `client_secret_post`
(research R5). Redirects are never followed.

**Accepted identity** — all must hold, else the sign-in fails with the
non-technical notice and a `sign_in_failed` event naming the reason:

| Check | Reason recorded |
| --- | --- |
| `id_token` present | `id_token_missing` |
| signature valid against the provider's JWKS, asymmetric algorithm | `id_token_invalid: signature` / `: algorithm` |
| `iss` = configured issuer (slash-insensitive) | `id_token_invalid: issuer` |
| `aud` contains the client ID; `azp` = client ID when `aud` has several | `id_token_invalid: audience` |
| `exp` in the future (2 min skew) | `id_token_invalid: expired` |
| `nonce` = the round trip's | `id_token_invalid: nonce` |
| userinfo `sub` = `id_token` `sub`, when userinfo is called | `userinfo_subject_mismatch` |

**Profile mapping**: `sub` → identity (`<issuer>#<sub>`); `email`; `email_verified`
(only the JSON literal `true` counts); `name`, else `preferred_username`;
`picture`. The `idp` claim Umbral issues to apps is the provider **kind**,
`oidc`, as spec 003 made it for every connector (its research R3). Not the slug:
002 lets a connector be deleted and re-added under another slug while people
keep their identity, so the slug is not a stable value to hand an app.
