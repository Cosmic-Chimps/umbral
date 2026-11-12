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

## The code (`df32802..ddcf64d`, 2026-11-12)

How it was reviewed:
- Two independent read-only reads by concern: identity, authority and the DPoP key, then the outbound requests and the admin side.
- My own read of the callback (`Handlers/Connect/Bluesky.fs`, steps 2–9).
- I verified every finding below against the code myself before writing it down.
- The full suite, run from a git checkout of the branch's files (results at the end).

### Verdict

**Four blocking items, each small; seven nits.** The core holds:
- the callback runs in R6's order, and every step fails closed;
- `iss` is compared exactly;
- the fresh authority check fetches the DID document, its PDS, the protected resource and the server metadata with no cache, and requires `meta.Issuer = issuer`;
- the document's `id` must equal the DID, the metadata's `issuer` its own origin, and the protected resource must list one server;
- the DPoP key is per attempt and lives only in the encrypted cookie;
- Duende signs every proof, `htu` carries no query or fragment, and the nonce retry happens once;
- the subject is the DID, and no email is ever sent;
- every fetch goes through the guarded client.

The blocking items are around that core: what is shown about the person, a re-check that fails open, and how much an anonymous visitor can make Umbral do.

### Blocking

- **B1. The handle sent to apps is not checked in both directions when it comes from the document.**
  - **Where:** `Handlers/Connect/Bluesky.fs`, step 8. `handle` is the stored handle only when the fresh document still claims it. Otherwise it is `Rules.handleOf doc`, the first `at://` entry of `alsoKnownAs` (`Domains/Bluesky.fs`), with nothing resolving it back to the DID.
  - **Where it ends up:** it becomes `Username`, which `UserStore` stores and `Authorize.fs` sends to apps as `preferred_username`. It is also the fallback `name`.
  - **The scenario:** anyone who controls their own DID document (a `did:web`, or a `did:plc` on their own PDS) writes `alsoKnownAs: ["at://pfrazee.com"]` and types their DID. Every app then receives `preferred_username: pfrazee.com`.
  - AT Protocol requires a handle to be verified both ways, and so did point 4 of my step-0 review. The test `a DID typed directly signs in, and the handle shown is the document's` asserts the one-way path (zero `atproto-did` lookups).
  - **Fix:** a handle taken from `handleOf` is used only if resolving it (TXT, then HTTPS) gives exactly this DID. Otherwise `Username = None` and the name falls back to the profile name or the DID. The stored handle came from a forward resolution at the start, so `claimsHandle` on the fresh document is enough for it. Change the test, and add one where the document claims a handle that resolves to another DID.
- **B2. "Only these servers" admits a link with no server recorded, so the re-check fails open.**
  - **Where:** `BlueskyAccounts.fs`. Under `OnlyListed`, `hosts.Count = 0` returns `None` (admitted). In the callback, `BlueskyAccounts.recordServer` runs after `Callback.finishSignIn` has already issued the session.
  - **The scenario:** a database error, or a crash between the two calls, leaves the link without a server, and every later authorize or refresh is admitted whatever the list says.
  - R11 says no Bluesky link can predate schema 12, so a missing server is never legitimate. `BlueskyAccountsTests` asserts that it admits.
  - **Fix:**
    1. Record the server before `finishSignIn`, or in the same transaction as the link.
    2. For the `bluesky` kind under `OnlyListed`, treat a missing server as refused.
    3. Change the test.
- **B3. The DNS cache has no bound, and an anonymous visitor fills it.**
  - **Where:** `Providers/Bluesky.fs`, `LookupClientOptions(..., UseCache = true)`, with `MaximumCacheTimeout` left unset, so each answer stays for its own TTL.
  - **The scenario:** an attacker publishes a wildcard TXT record with a long TTL and large answers (TCP fallback allows up to 64 KiB), then starts sign-ins with `x1.evil.example`, `x2.evil.example`, and so on. A `did=` value isn't even needed. Each name is a new cache entry kept for the TTL, on a server whose ceiling is 150 MiB. The rate limit is per address or /64, which an attacker with a block of addresses multiplies.
  - **Fix:** `UseCache = false`, since handles aren't hot and the system resolver caches anyway. Or cap it with `MaximumCacheTimeout` of a few minutes. Consider `UseTcpFallback = false` too: only one `did=` value is read.
- **B4. In production, an anonymous visitor can make Umbral call any public host, on any port and path.**
  - **Where:**
    - `Domains/Bluesky.fs`: `originOf` accepts any port for the PDS and the authorization server, and `endpointOf` accepts any https URL for PAR, token and authorize, unrelated to the issuer's origin.
    - `Handlers/Connect/Bluesky.fs`: the list is checked only after `discover` has already made two GETs.
  - **The scenario:** with "Any server" (the pane's default), an attacker points their DID's PDS, or the metadata's PAR endpoint, at `https://some-host:8443/anything`. Umbral connects there, and the distinct failure words tell the visitor whether something answered. The worst target is the server's own public IP: the guard accepts it as public, and a connection from the host to its own address can reach a TLS service bound to all interfaces that the firewall otherwise hides. Even under "Only these servers", the two discovery GETs happen before the refusal.
  - The Fediverse connector already refuses any port but 443 in production (`normaliseServer`). This connector should match it.
  - **Fix:**
    1. In production, `originOf` refuses a non-default port.
    2. PAR, token and authorize must share the issuer's scheme, host and port.
    3. Check the list against the protected resource's authorization server before fetching its metadata.

### Nits

- **N1. Put an overall bound on the outbound work of one attempt.** A start can make one TXT query and up to six HTTP requests, each with its own 10 s and two address lookups, and a callback makes up to six more. Give the start one deadline of about 20 s, pass a token with that deadline to the TXT lookup (it uses `CancellationToken.None` today), and consider a global `ConcurrencyLimiter` on the start and the callback. The Fediverse connector has the same pattern, but this one costs twice as much per attempt.
- **N2. Bound the correlation cookie.** The `DPoP-Nonce` header goes into the cookie with no length limit, beside a token endpoint of up to 2,048 characters and the return URL. Past about 4 KB a browser drops the cookie silently, and the callback fails as `NoCorrelation` with no reason. Cap the nonce (say 512 bytes; longer is unsupported), check the protected payload's length before it is written, and refuse with a reason word.
- **N3. Re-check per signed-in DID, not per user.** `BlueskyAccounts` admits a user if any of their Bluesky links is listed. Today a user can't hold two DIDs (no email, so no linking), but it would become wrong the day linking arrives.
- **N4. A handle can outlive its account.** `preferred_username = COALESCE(@username, preferred_username)` keeps a handle once `Username` becomes `None`. Bluesky handles move, so for this connector clear it when the fresh check gives no verified handle. Otherwise B1's fix can still leave an old, unverified name in place.
- **N5. Catch an exception from making a proof.** One thrown inside `proofFor` escapes `send`'s catch list and becomes a 500. Map it to the same refusal as an unsupported answer.
- **N6. `cleanName` keeps U+2028 and U+2029 (Zl, Zp).** Drop those too. They break lines in apps that render the name. Apple's `nameOf` has the same gap, so a shared helper would fix both.
- **N7. R12 says the default TXT resolver "is created lazily".** It is a top-level value; only `LookupClient` is lazy, and that's what keeps DnsClient unloaded. Correct the wording.

### Test gaps, beyond those in B1 and B2

- `did:web` is never signed in with end to end.
- The fresh check (step 7) is covered only where the PDS names another server. Not covered:
  - a fresh document that fails to load;
  - a fresh document whose `id` differs;
  - an issuer that differs only by a trailing slash or case.

  The "server claiming another account's DID" test stops at step 6 (`did_mismatch`), which is fine because step 6 always runs, but step 7 deserves its own cases.

### Checked and sound

- **The callback, in order:**
  1. `complete`: cookie deleted first, `state` compared in constant time, slug, expiry, single use;
  2. `iss` exact;
  3. the connector and the list again;
  4. the stored attempt;
  5. the exchange;
  6. the token answer's `token_type`, `scope` and `sub`, with `sub` equal to the started DID;
  7. the fresh authority check;
  8. the handle;
  9. the profile.

  A refusal at any step is recorded with a fixed word.
- **The DPoP key.** It is new per attempt and only in the Data-Protected cookie (tested: no `"d":`, DID or handle in clear). An old cookie is `exchange_failed` and a tampered one is `Corrupt`.
- **The proofs.** `htu` is `GetLeftPart Path` and `htm` is POST, with a fresh `jti` each time. The private key is never in the proof's `jwk`. There is one nonce retry, and a second challenge is refused. No key, code, token or nonce reaches the security log (tested).
- **Principle III:** Duende signs every proof, and Umbral builds no JWT and parses no access token.
- **The guarded client and the admin side:**
  - every fetch uses the `umbral-bluesky` client on `Fediverse.guardedHandler`, whose functions this change only made public;
  - no redirects and no proxy; the connect callback connects only to the filtered addresses;
  - IPv4-mapped, NAT64, 6to4 and Teredo addresses are checked by their embedded IPv4;
  - bodies capped at 64 KiB, and 10 s per request;
  - a TXT answer is only ever a DID string;
  - `did` and `cid` are checked before going into URLs.
- **R8.** `clientIdFor` gives https on the default port, or http only when the dev setting is on and the host is exactly `127.0.0.1` or `[::1]`. The dev setting needs `ASPNETCORE_ENVIRONMENT=Development` plus its own variable, and the admin can't reach it. Save and Enable refuse an unsuitable origin.
- **R10.** The profile is fetched through the guard, a failure means no name, and the name is cleaned of Cc and Cf, cut at 200 on a scalar boundary.
- **The admin side.** `saveBluesky` and the toggle sit behind `requireOperator` and `readValidated`. `parseServers` refuses wildcards, ports and IP literals in production. Logs carry fixed words only.
- **Idle memory.** Neither Duende nor DnsClient is touched at startup. Your three runs per variant, at 141–143 MiB, are consistent with that.
