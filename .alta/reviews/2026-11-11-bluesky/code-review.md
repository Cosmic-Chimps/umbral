# Code review: the Bluesky connector (task b5ky)

Reviewer: the `umbral` session.

## Step 0, feasibility (`feasibility.md` at `a519c0e`, 2026-11-12)

**Go on.** No reason to stop: every JWT in the flow comes from a library, and the rest is HTTP flow of the kind `Fediverse.fs` already is.

### What I checked myself

- **`Microsoft.IdentityModel.Dpop` 8.23.0 only validates.** I downloaded the package into an empty folder and listed the names in its assembly without loading it: `DpopProofValidator`, `DpopValidationOptions`, `DpopValidationResult`, the claim and error constants, and nothing that creates a proof. Your finding is right, and the task text I wrote (that it "creates and validates DPoP proofs", from its NuGet description) was wrong. The Microsoft.IdentityModel family stays at 8.19.2.
- **The Duende choice fits Principle III.** `DefaultDPoPProofTokenFactory` signs with `JsonWebTokenHandler.CreateToken`. The packages are Apache-2.0 from `DuendeSoftware/foss` (not IdentityServer's commercial licence). `System.IdentityModel.Tokens.Jwt [8.19.2, 9.0.0)` keeps the family at one version. A public client means no client assertion, and AT Protocol has no `id_token`, so the access and refresh tokens stay opaque and are dropped.
- **The cross-check is the right test.** A Duende proof passing Microsoft's validator, and failing on a wrong nonce, is independent evidence that the proofs are well formed.

### Points for the plan (none blocks; I'll check each in the code review)

1. **The DID-to-server check is the connector.** Implement it as the specification's "critical" check, and test it with a fake server that claims a DID it isn't authoritative for. In the callback, check all of these:
   - `iss` against the issuer stored with the attempt;
   - `sub` against the DID resolved at the start, when the flow started from a handle;
   - `sub` re-resolved fresh: DID document, then PDS, then that PDS's single authorization server, whose `issuer` equals the token's issuer;
   - the fetched DID document's `id` equal to the DID asked for, for `did:plc` and `did:web` alike.

   Store the DID, never the handle, as the identity.
2. **Keep the DPoP key in the attempt's encrypted correlation cookie, not in server memory.** The notes put it "in memory only, beside the attempt's `state`". Umbral's attempts live in `OAuthRoundTrip`'s Data-Protected cookie, not on the server. A server-side table keyed by attempts that anyone can start without signing in is memory a stranger can fill. Carry the private JWK (about 250 bytes) inside the protected correlation: stateless, per attempt, gone when the cookie is spent. If the plan keeps it in memory after all, bound it (count and lifetime) and say why.
3. **The start route makes outbound calls for an anonymous visitor:** handle, DID document, PDS metadata, authorization-server metadata and PAR, all before the person has signed in anywhere. Every fetch goes through `Fediverse.guardedHandler` (private addresses refused at connect time, no redirects, 64 KiB, 10 s), and the start is rate-limited as the Fediverse start is. `did:web` is an arbitrary host, so it gets the same guard. The PLC directory is a fixed host, but its answers are checked like any other.
4. **Handle resolution: `DnsClient` is worth its weight.** HTTPS `/.well-known/atproto-did` alone would leave out the handles that exist only as a DNS TXT record, and custom-domain handles are exactly what the self-hosting crowd uses. A TXT answer only yields a DID string; nothing connects to it, so it opens no SSRF route. Check the handle in both directions, as the specification requires. Its outcome decides only whether the handle is shown; the DID remains the identity.
5. **The display name from the profile record is unsigned and the person's own.** It is display only, like Apple's `user` field. Clean it as Apple's `nameOf` now does: drop control and format characters by code point, cap the length, cut on a scalar boundary. Fetch it through the guarded client, and treat a failure as "no name", not as a failed sign-in.
6. **Measure the idle memory** with the three new packages, and the fourth if `DnsClient` goes in. That memory is Principle II's ceiling. .NET loads an assembly only when a type in it is first used, so keep Duende's types out of startup registration if you can, and show the number either way.
7. **Refuse to enable without a public `https` origin without a port,** with the reason on the pane, as the notes say. The client metadata document is served from a fixed path, and its `client_id` must equal that exact URL (test it).
8. **Pin `htu` without query or fragment** (notes §2), with a test.
