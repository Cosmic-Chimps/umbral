# Bluesky connector, step 0: feasibility

Task `b5ky`, 2026-11-12. Branch `alta/snowy-eagle-vdg4`, from `main` at `df32802` (Apple and the provider marks are in).

**Verdict: feasible.** Every JWT in the flow can come from a maintained library. The DPoP proofs come from
**`Duende.IdentityModel.OidcClient.Extensions` 7.1.1** (Apache-2.0). Umbral registers as a public client, so the flow
needs no other JWT. The first candidate, `Microsoft.IdentityModel.Dpop`, can't do the job: it only validates proofs,
it can't create them.

The specification used is atproto.com/specs/oauth, read on 2026-11-12.

## 1. The protocol, point by point

| Point | What the specification says | Who does it in Umbral |
|---|---|---|
| DPoP | Required for every client, starting at PAR. Server nonces are mandatory (rotated at least every 5 minutes), and a client retries a request that fails with `use_dpop_nonce`. A fresh proof with a random `jti` goes on every request. ES256 must be supported. Each session has its own key. | **Library:** the proof JWT. **Umbral:** generating the key, choosing `htm` and `htu`, the one retry, and reading the `DPoP-Nonce` header. |
| PAR (RFC 9126) | Required: the server sets `require_pushed_authorization_requests: true`, and every client pushes first. | Umbral: a form POST, then a redirect with `client_id` and `request_uri`. |
| PKCE | Required, S256 only (`plain` is forbidden), with a new challenge for each request. | Umbral already does this for every connector. |
| Client ID | A full `https` URL, with no port, from which the client metadata JSON is fetched. It must equal the fetched URL exactly. Required fields: `client_id`, `grant_types` ⊇ `authorization_code`, `scope` ⊇ `atproto`, `response_types` ⊇ `code`, `redirect_uris`, and `dpop_bound_access_tokens: true`. `application_type` defaults to `web`. | Umbral serves a fixed JSON document at a fixed path under the public origin. |
| Client type | A confidential client must use `private_key_jwt`, with its keys in `jwks`/`jwks_uri` and an ES256 assertion. That assertion is a second JWT. A public client uses `none`, and its session and refresh tokens last at most 2 weeks. | **Public client (`none`).** Umbral spends the upstream tokens once, at sign-in, and keeps none of them. The 2-week limit costs nothing, and no client assertion is needed. |
| Discovery | handle → DID (DNS TXT `_atproto.<handle>`, or `https://<handle>/.well-known/atproto-did`) → DID document (`did:plc` from the PLC directory, `did:web` from `/.well-known/did.json`) → PDS → `/.well-known/oauth-protected-resource`, which must list exactly one authorization server → `/.well-known/oauth-authorization-server`, whose `issuer` must equal the origin it was fetched from. Checking the handle in both directions is mandatory: the DID document must claim the handle back. | Umbral: ordinary HTTP and JSON, of the same kind as `Fediverse.discover`. |
| No `id_token` | The token response carries `sub` (the DID) and `scope` (which must include `atproto`). | Umbral reads two JSON fields. Nothing is signed. |
| Is the server authoritative for the DID | "Critical (mandatory)": resolve `sub` again, take the PDS from its DID document, and check that this PDS names the same authorization server, with an `issuer` equal to the one that issued the token. When the flow started from a handle, `sub` must also equal the DID resolved at the start. | Umbral. **This is the security core of the connector.** The specification says that without it, "a malicious server could authenticate arbitrary accounts". |
| `iss` in the redirect | The server must set `authorization_response_iss_parameter_supported: true`, and the client checks `iss` together with `state`. | Umbral: a string comparison against the issuer stored with `state`. |
| `login_hint` | Recommended: the handle exactly as the person typed it. | Umbral adds it to the PAR body. |
| SSRF | The client must avoid fetches to private or internal addresses, and a hardened HTTP client is "strongly recommended". | Umbral already has one: `Fediverse.guardedHandler` checks the address at connect time (`ConnectCallback`), with a 64 KiB body cap, a 10 s timeout, and no redirects. |
| Email | No email unless the transitional `transition:email` scope is asked for, and transitional scopes are to be phased out. | Umbral doesn't ask for it, and never sends `email` or `email_verified` from this connector. |

The token's own scope is only `atproto`, which is enough to identify the account. The display name comes from the
person's public profile record on their own PDS (`com.atproto.repo.getRecord`, collection `app.bsky.actor.profile`,
rkey `self`). That record is public, so no DPoP request against a resource is needed: the only proofs are those of the
PAR request and the token request.

## 2. The libraries

### `Microsoft.IdentityModel.Dpop` 8.23.0 (MIT): falls short

I listed its public API by reflection over the 8.23.0 assembly. It has `DpopProofValidator.ValidateAsync`,
`DpopValidationOptions`, `DpopValidationResult`, the claim-name and error-code constants, and an `IJtiReplayCache`.
**There is no type that creates a proof.** The library is the resource-server half of DPoP: `ValidateAsync` throws
unless it is given an access token and the expected `cnf.jkt`. It can't sign Umbral's requests, so moving the family
to 8.23 would buy nothing. The family stays at 8.19.2.

### `Duende.IdentityModel.OidcClient.Extensions` 7.1.1 (Apache-2.0): chosen

- `DefaultDPoPProofTokenFactory(jwkJson).CreateProofToken(DPoPProofRequest(Url, Method, DPoPNonce, AccessToken))`
  can be called on its own, without an `OidcClient` instance. That matters, because Duende's client expects an
  `id_token`, and AT Protocol never sends one. Umbral uses only the factory and the key helper.
- `JsonWebKeys.CreateECDsaJson "ES256"` makes a P-256 key as a JWK (the private `d` included), with `alg: ES256`.
- The factory signs with `Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler.CreateToken`, the same handler
  Umbral's Apple connector already uses for its client secret. Umbral neither builds nor signs the proof.
- Checked by running it (`dotnet fsi`, 2026-11-12):
  - header `{"alg":"ES256","typ":"dpop+jwt","jwk":{"kty":"EC","x":…,"y":…,"crv":"P-256"}}`; the public key only.
  - payload `jti`, `htm`, `htu`, `iat`; plus `nonce` when one is given, and `ath` when an access token is given.
  - **Cross-check:** a proof made by Duende's factory passes Microsoft's `DpopProofValidator`, with the expected
    nonce, `ath` and `cnf.jkt` all checked. The same proof fails against a different expected nonce.
- **One thing Umbral must get right:** the factory puts `htu` in exactly as it is given, query and fragment included
  (checked). RFC 9449 §4.2 wants `htu` without them, so Umbral passes the endpoint URL with its query and fragment
  removed. Both endpoints that need a proof (PAR and token) are POSTs without a query, so in practice this is the
  bare endpoint URL. A test will pin it.
- **Dependencies** (nuspecs, net10.0):
  - `Duende.IdentityModel.OidcClient` 7.1.1 → `Duende.IdentityModel` 8.1.0 (both Apache-2.0).
  - `System.IdentityModel.Tokens.Jwt [8.19.2, 9.0.0)`: already in Umbral's graph at 8.19.2, through OpenIddict.
  - `Microsoft.Extensions.Logging.Abstractions`: already in through ASP.NET Core.

  That is three new packages. The Microsoft.IdentityModel family stays at one version, 8.19.2. These are not
  Duende IdentityServer's commercial licence: all three packages are in the Apache-2.0 `DuendeSoftware/foss`
  repository.

### `idunno.AtProto` 8.0.0 (MIT): reference, not a dependency

About fifteen packages (OpenTelemetry, CBOR, Zstd, DnsClient, Duende's client), against Principle II's idle-memory
ceiling. Its source serves as the reference for handle resolution and for the DID-to-authorization-server check.

## 3. Principle III, answered

> Umbral code MUST NOT construct, sign, or validate a JWT by hand.

| JWT in the flow | Produced or checked by |
|---|---|
| DPoP proof, PAR request | Duende `DefaultDPoPProofTokenFactory` |
| DPoP proof, token request (both tries) | Duende `DefaultDPoPProofTokenFactory` |
| Client assertion | none: Umbral is a public client |
| `id_token` | none: AT Protocol has none |
| Access and refresh tokens | opaque to the client: Umbral never parses them, and drops them after sign-in |

Everything else is ordinary HTTP flow that Umbral writes itself, of the same kind as `GitHub.fs` and `Fediverse.fs`:
the handle, DID and metadata lookups, the PAR POST, the redirect, the code exchange with the `use_dpop_nonce` retry,
and the issuer and DID comparisons.

## 4. Open points for the plan (none of them blocks)

1. **DNS TXT for handles.** .NET has no TXT lookup (`System.Net.Dns` does addresses only), and custom-domain handles
   often publish only `_atproto` TXT. Options:
   - `DnsClient` 1.8.0: Apache-2.0, no dependencies on net8.0+, and what idunno uses.
   - Only the HTTPS `/.well-known/atproto-did` method: it covers `*.bsky.social` and every handle hosted by a PDS,
     but not the handles that exist only as a DNS record.

   The plan decides which, and measures the idle memory if it adds `DnsClient`.
2. **The DPoP key's life.** One P-256 key per sign-in attempt. It lives in memory only, beside the attempt's
   `state`, and is never written to the data directory, because Umbral keeps no upstream session. It is dropped when
   the callback is handled or the attempt expires.
3. **`did:plc` resolution** fetches from the PLC directory (`https://plc.directory`), a fixed outside host. `did:web`
   fetches from the DID's own domain, through the guarded client. The plan decides whether the PLC directory URL can be
   configured (for tests and private PLC mirrors).
4. **The client metadata needs a public `https` origin without a port**, so a Bluesky connector can't be enabled on
   an instance served over plain `http` or on a port. The pane will say so. For development, AT Protocol's
   `http://localhost` client ID exception exists, but it creates a *native* client and is optional for servers; the
   tests use a fake authorization server instead.
