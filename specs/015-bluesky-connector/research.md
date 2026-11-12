# Research: the Bluesky connector

Sources were read on 2026-11-12:
- atproto.com/specs/oauth, /specs/handle, /specs/did;
- RFC 9449 (DPoP), RFC 9126 (PAR), RFC 9728 and RFC 8414;
- the packages named below, inspected by reflection and by running them;
- Bluesky's brand and trademark pages.

The library question is settled in [feasibility](../../.alta/reviews/2026-11-11-bluesky/feasibility.md), which the code reviewer confirmed. This file records the rest.

## R1. DPoP proofs: Duende's factory, nothing else of Duende's

**Decision.** `Duende.IdentityModel.OidcClient.Extensions` 7.1.1 (Apache-2.0), using two of its types:
- `JsonWebKeys.CreateECDsaJson "ES256"` makes the key;
- `DefaultDPoPProofTokenFactory(jwk).CreateProofToken(DPoPProofRequest(Url, Method, DPoPNonce))` makes each proof.

Umbral calls them directly, in `Providers/Bluesky.fs`. There is no `OidcClient`, no `ProofTokenMessageHandler`, and no DI registration, so Duende's assemblies load at the first Bluesky sign-in, not at startup (code reviewer, point 6).

**Rejected.**
- `Microsoft.IdentityModel.Dpop` 8.23.0: it only validates proofs.
- `ProofTokenMessageHandler`: it would put DPoP on every request of the HTTP client, the PDS profile read included. Umbral needs a proof only on two POSTs.
- `idunno.AtProto`: about fifteen packages.

**What Umbral passes in.** `Url` is the endpoint with any query and fragment removed (RFC 9449 §4.2). The factory copies it as given, which was checked by running it. `Method` is `POST`. `DPoPNonce` is the last nonce the server sent.

## R2. A public client

`token_endpoint_auth_method: none`. A confidential client must sign a client assertion, a second JWT, and the only gain would be sessions longer than two weeks. Umbral uses the tokens once and drops them, so it gains nothing. `grant_types` is `["authorization_code"]` only; no refresh token is asked for.

## R3. Handles

- **Syntax** (atproto.com/specs/handle):
  - ASCII letters, digits and `-` in labels of 1 to 63 characters;
  - at least two labels, 253 characters at most;
  - the last label does not start with a digit;
  - lower-cased, and a leading `@` and a trailing `.` are dropped.
- **Reserved names.** The spec's reserved top-level labels are refused as "not a handle": `.alt`, `.arpa`, `.example`, `.internal`, `.invalid`, `.local`, `.localhost`, `.onion`. `.test` is refused too, except when the development setting is on (the tests use it).
- **DNS first.** `_atproto.<handle>` TXT records whose value starts `did=`. Exactly one such record must be present. Several such records, or none, means not found by DNS. The query goes through **`DnsClient` 1.8.0** (Apache-2.0, no dependencies on net8.0+), because `System.Net.Dns` can't read TXT records. A TXT answer is a string and opens no connection, so it needs no SSRF guard. It has a 3 s timeout and no retries. DNS-only handles are what self-hosters use (code reviewer, point 4).
- **HTTPS fallback.** `GET https://<handle>/.well-known/atproto-did` through the guarded client. The body, trimmed, must be one syntactically valid DID; it is capped by the guarded client's 64 KiB.
- **Two-way check.** The DID document's `alsoKnownAs` must hold `at://<handle>`. If it doesn't, the handle is "not found", with the same message as a missing one (design note 3c).
- **The seam.** A `TxtResolver` delegate (`name, token -> Task<string array>`) is registered beside `HostResolver`. The tests replace it.

## R4. DIDs and their documents

- **Syntax.**
  - `did:plc:` is followed by 24 characters of base32 `[a-z2-7]`.
  - `did:web:` is followed by a host name (no port, no path; `%3A` is refused).
  - Any other method is "not a handle".
- **`did:plc`.** `GET {plc}/{did}`, where `{plc}` is `https://plc.directory`. The configuration key `UMBRAL:BLUESKY_PLC_DIRECTORY` can override it, and the override must be `https` unless the development setting is on.
- **`did:web`.** `GET https://<host>/.well-known/did.json`.
- **The document.**
  - `id` must equal the DID exactly.
  - `alsoKnownAs` is read as a list of strings.
  - The PDS is the `service` entry with `id` `#atproto_pds` (or `<did>#atproto_pds`) and `type` `AtprotoPersonalDataServer`. Its `serviceEndpoint` must be an `https` origin with no path, query or fragment (`http` is allowed with the development setting on).

## R5. From PDS to authorization server

1. `GET {pds}/.well-known/oauth-protected-resource`: `authorization_servers` must have exactly one entry, an `https` origin.
2. `GET {as}/.well-known/oauth-authorization-server`: `issuer` must equal `{as}` exactly. The checks of FR-010 apply. The endpoints read are `pushed_authorization_request_endpoint`, `authorization_endpoint` and `token_endpoint`. Each must be an `https` URL. They need not share the issuer's host (Bluesky's entryway serves them from `bsky.social`, which is the issuer).
3. **The allowlist** is compared with the issuer's host, normalised as the Fediverse list is.

## R6. The authority check (the code reviewer's point 1)

In the callback, in this order. A failure at any step ends the sign-in, and the security event names the step.

1. `state` (OAuthRoundTrip, single use, slug `bluesky`).
2. `iss` from the query equals the stored issuer (`iss_mismatch`). A missing `iss` also fails.
3. The allowlist, checked again against the stored issuer's host (`server_not_allowed`).
4. Token request at the stored token endpoint with the stored key, retried once on `use_dpop_nonce` (`exchange_failed`).
5. `token_type` equals `DPoP` (case-insensitive), `scope` (space-separated) holds `atproto`, and `sub` is a valid DID (`token_invalid`).
6. `sub` equals the stored DID (`did_mismatch`).
7. `sub` is resolved again from nothing cached: DID document, PDS, protected resource, authorization server metadata. That `issuer` must equal the stored issuer (`not_authoritative`).
8. The handle is checked again: if the fresh document no longer names the stored handle, the sign-in goes on, and the handle shown is the first `at://` entry in `alsoKnownAs` that is a valid handle, else none.

Step 6 is redundant with step 7 when the start was a handle, but it is cheap and catches a server that answers for an account other than the one asked for, before more network work.

## R7. The butterfly

Bluesky's brand guidelines allow the butterfly in Blue500, white or black, never recoloured, with clear space. Its trademark policy allows it as a social icon linking to a Bluesky profile. Neither page covers a sign-in button, and they direct other questions to Bluesky's trademark address. By the provider-marks rule (`design/contracts/provider-marks.md`), "unclear" means the **link icon**. A maintainer can write to Bluesky and reopen this.

## R8. The client ID and the origin

- The `client_id` is `{origin}/connect/bluesky/client-metadata.json`, and the origin must be `https` with no port. `PublicOrigin` already parses the origin; `Bluesky.clientIdFor origin` returns `Ok url`, or `Error reason`.
- **Development.** With the development setting on and a loopback origin (`http://127.0.0.1:<port>` or `http://[::1]:<port>`), the `client_id` is AT Protocol's localhost client:

  ```
  http://localhost?redirect_uri=<the callback, encoded>&scope=atproto
  ```

  The authorization server derives its metadata from that URL, so no document is fetched. This is how a maintainer can try a real Bluesky account from a laptop. `http://localhost` origins are not used: AT Protocol wants the redirect at `127.0.0.1`.
- **The document** is built from records and serialised with `System.Text.Json`. It is served with `Content-Type: application/json` and `Cache-Control: public, max-age=300`, and is not served while the connector isn't set up.

## R9. The correlation (the code reviewer's point 2)

`OAuthRoundTrip.Correlation` gains one field, `Extra: string`, empty for every other kind. It is encoded as an eighth field, and payloads of six and seven fields still decode. For Bluesky, `Server` holds the issuer and `Extra` holds a small JSON object:
- `did`, `handle`;
- `tokenEndpoint`;
- `jwk` (the private key's JWK);
- `nonce` (the authorization server's last DPoP nonce).

The cookie is Data Protection-encrypted and HttpOnly, its path is `/connect`, and it lives 10 minutes. The key never reaches server memory beyond the request and never reaches the disk. Size: an ES256 JWK is about 250 bytes, and with the rest the cookie stays under 1.5 KB, well within the 4 KB limit. An attacker who starts attempts costs Umbral nothing but CPU, bounded by the sign-in rate limit.

## R10. The profile

`GET {pds}/xrpc/com.atproto.repo.getRecord?repo=<did>&collection=app.bsky.actor.profile&rkey=self` goes through the guarded client, with no auth and no DPoP.
- `value.displayName` is cleaned as Apple's `nameOf` does: no control or format characters, 200 characters at most.
- `value.avatar.ref.$link` must be a CID of `[a-z0-9]` characters, 128 at most, and gives `picture`: `{pds}/xrpc/com.atproto.sync.getBlob?did=<did>&cid=<cid>`.
- Any failure means no name and no picture.

`profile` is `https://bsky.app/profile/<did>`.

## R11. The re-check after sign-in

The DID doesn't contain the server, unlike Fediverse's `host#id` subject. So schema version 12 adds `linked_accounts.provider_server TEXT NULL`. The Bluesky callback writes the issuer's host there after every sign-in (`BlueskyAccounts.recordServer`). `BlueskyAccounts.recheckServer` mirrors `FediverseServers.recheckServer` and is called in the same two places (`Authorize.fs`, `Token.fs`). A link with no server (none can exist before this feature) admits.

## R12. Idle memory (the code reviewer's point 6)

DnsClient and Duende load only on the first Bluesky sign-in. Neither is touched at startup:
- the `TxtResolver`'s default instance is created lazily;
- the `LookupClient` is built on first use.

The plan's verification runs the existing idle-memory test, and the notes record the figure with the connector enabled and after one fake sign-in.
