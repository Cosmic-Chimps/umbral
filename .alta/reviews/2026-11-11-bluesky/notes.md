# Spec 015, the Bluesky connector: notes for review

- **Task:** `2026-11-11-3-connector-bluesky-atproto-b5ky`.
- **Branch:** `alta/snowy-eagle-vdg4` (worktree `~/.alta/worktrees/umbral/snowy-eagle-vdg4`).
- **Range:** `df32802..HEAD`. `df32802` is local `main`, which already holds Apple (`alta/indigo-pheasant-5jd9`) and the provider marks (`alta/bold-dove-crws`). Schema 12 follows 11 directly, and nothing else has to merge first.

| Commit | What |
|---|---|
| `a519c0e` | Step 0: [feasibility.md](feasibility.md) |
| `9d56c27`, `55939ac` | `specs/015-bluesky-connector/`: spec, research R1–R12, plan, data model, contract, tasks, and the design note `design/contracts/bluesky-connector-ui.md` |
| `8e8fa6a`, `c5b3907` | Docs: `docs/connectors/bluesky.md`, the navigation lines, the README, `who-can-sign-in`, `integrate`, `backup` (schema 12), `settings` (PLC directory), `site/sidebar.mjs`, `changelog.d/015-bluesky-connector.md` |
| `706a7d6` | The `Bluesky` kind, the two packages, schema 12 (`linked_accounts.provider_server`), `BlueskyAccounts.recordServer` and `recheckServer` |
| `e5a29e4` | `Domains/Bluesky.fs`: the pure rules (handles, DIDs, TXT, DID documents, metadata, token answer, `cleanName`, client ID) |
| `8381af7` | `Correlation.Extra`; `Providers/Bluesky.fs`, the AT Protocol client with DPoP |
| `10d83bb` | `Handlers/Connect/Bluesky.fs`: the step, the callback, the metadata route; the test fake `FakeAtproto.fs` |
| `1cab402` | 42 sign-in, step, callback and session test cases; the fix to the re-check's log word |
| `a5eb911` | `Handlers/Admin/BlueskyConnector.fs`: the pane, its save, the Enable check |

## Verification
- **Tests.** The full suite, run without `IdleMemoryTests` (as CI does), passed at `a5eb911` with 2461 tests and 0 failures, on the implementing child's run. One admin test was added after that (16 of 16 pass). The Release run at the review head is in the last section. The build has 0 warnings.
- **Checks.**
  - `.github/scripts/changelog.sh check` passes.
  - `.github/scripts/check-links.py` passes: 342 links in 37 files.
  - The site builds (`npm run build`): 31 pages, and nothing is requested from another host.
- **CSS.** No class was added, so `umbral.css` is unchanged.
- **Packages.**
  - `Duende.IdentityModel.OidcClient.Extensions` 7.1.1 brings in `Duende.IdentityModel.OidcClient` 7.1.1 and `Duende.IdentityModel` 8.1.0.
  - `DnsClient` 1.8.0 is added too.
  - The `Microsoft.IdentityModel.*` family and `System.IdentityModel.Tokens.Jwt` all stay at 8.19.2 (`dotnet list package --include-transitive`).

### Idle memory (SC-006), Linux arm64 in Docker
Measured with [measure-idle.sh](measure-idle.sh): the image's own GC settings, a loopback origin with the development flags (so a Bluesky connector can be enabled), five requests, 20 s idle, then `VmRSS` of PID 1. The images were built from `git archive` of each commit.

| Image | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| `main` (`df32802`) | 146,080 kB | 146,136 kB | 146,292 kB |
| branch (`a5eb911`) | 145,960 kB | 144,940 kB | 146,592 kB |
| branch, Bluesky connector enabled | 145,588 kB | 144,052 kB | 143,904 kB |

- **No change.** Every run is about 141–143 MiB, under the 150 MiB ceiling, and the branch is not measurably larger than `main`.
- **The new libraries are not loaded at idle.** In every run, `/proc/1/maps` maps neither DnsClient nor any Duende assembly. As a control, the same check finds `Umbral.dll`.
- **The connector was really enabled** in the third row: `GET /connect/bluesky/start` answered 200, which it does only for an enabled connector.
- **What keeps them out at startup:**
  - DnsClient's `LookupClient` is built on the first lookup, behind a field typed `obj | null`, because an F# program runs every file's top-level bindings at start.
  - Duende's types are touched only inside `proofFor` and `newKey`.
  - Nothing of either is registered in DI.
- **What CI adds.** CI's `IdleMemoryTests` on amd64 and arm64 is the authoritative check, and it runs after the first push.

## For the design review
The designer's answers of 2026-11-12 are in `design/contracts/bluesky-connector-ui.md`, and the captures are listed in [captures.md](captures.md) (49 PNGs, taken with [capture.mjs](capture.mjs)). No state overflows at 360.

**The mark.** The button uses the link icon. Bluesky's brand guidelines allow the butterfly only in Blue500, white or black, never recoloured. Its trademark policy allows it as a social icon linking to a profile. Neither page covers a sign-in button, so by the provider-marks rule the case is unclear, and the link icon applies (research R7).

**What differs from the note, or is not in it:**
1. **Extra texts.**
   - The empty-list note: "The list is empty, so nobody can sign in through Bluesky. Add a server, or choose Any server."
   - The notices: `bluesky-connector-saved` ("Saved. People can sign in with a Bluesky account from the next page load."), `bluesky-connector-invalid` ("Choose Any server or Only these servers, then save again."), and `connector-enable-bluesky-origin` ("Not enabled: Bluesky needs Umbral at an https address without a port, reachable from the internet."), which is drawn as a Warning.
2. **An existing connector on an unusable origin** (the origin changed after it was set up) keeps all its sections under the alert. Disable, Delete and Save stay usable, because Save only changes the list and the shared flag. Enable is refused. Only a connector that doesn't exist yet gets the reduced pane.
3. **Development on a loopback IP** shows an info note where the address would be: "Development: Umbral signs in as AT Protocol's localhost client, so no address is fetched." Most pane captures come from such an instance, because a laptop can't serve https without a port. `connectors-any-https-1280-dark` is the same pane at a real https origin.
4. **Two things the walk noticed:**
   - the address field cuts off `.json` at 1280; Copy takes the whole address;
   - in Any server mode, the grey `bsky.social` placeholder can read as a typed value in the dark theme. The Fediverse pane has the same pattern with its own placeholder.
5. **Not captured,** because no local state produces them: the empty-list warning, the "did not answer" error, and the "does not offer the sign-in" error. Their texts are in captures.md.

## For the code review
[feasibility.md](feasibility.md) is step 0. Each of your eight points, and where it lives:

1. **The authority check.** `Handlers/Connect/Bluesky.fs` `callbackHandler` follows research R6, in this order:
   - `state`;
   - `iss`, where a missing one also fails;
   - the connector and the list;
   - the stored attempt;
   - the DPoP exchange;
   - `checkTokenAnswer` (`token_type`, `scope`, the DID's syntax, `sub` equal to the started DID);
   - a fresh `resolveDid`, `discover`, and `issuer` equal to the stored issuer. Any failure there gives `not_authoritative`, never a pass.

   `parseDidDocument` refuses a document whose `id` is not the DID asked for. Tests: `BlueskyCallbackTests` (`not_authoritative`, `did_mismatch`, `iss_mismatch`, no `iss`) and `BlueskySignInTests` (a server claiming another account's DID creates no user).
2. **The DPoP key.** It travels only in `Correlation.Extra`, inside the Data Protection cookie: it is never kept in memory or on disk. A test checks that the cookie holds neither `"d":` nor the DID in clear text.
3. **Network safeguards.**
   - The `umbral-bluesky` client sits on `Fediverse.guardedHandler` (connect-time address check, no redirects, 10 s, 64 KiB).
   - A `HostCheckHandler` on the same client refuses a host that resolves only to non-public addresses before any request; the tests use it, because their stub replaces the connect-time guard. A resolution timeout counts as "did not answer", never as a 500.
   - The start and the callback carry the `SignIn` rate limit.
4. **DnsClient** reads TXT `_atproto.<handle>`, with a fallback to HTTPS `/.well-known/atproto-did`. The two-way check is `claimsHandle`.
5. **The display name.** `cleanName` drops control and format characters and caps at 200, never inside a surrogate pair (copied from Apple's `nameOf`). A failed profile read gives the handle as the name.
6. **Idle memory:** see above.
7. **The origin.** `Domains.Bluesky.clientIdFor` accepts `https` with the default port, or, only with the development flag, an origin whose host is exactly `127.0.0.1` or `[::1]`. Tests:
   - `BlueskyDomainTests` covers every case;
   - `BlueskyStepErrorsTests` covers `http://localhost:<port>` with dev on, refused as `origin_unsuitable`;
   - `BlueskyAdminTests` covers a crafted save and Enable.
8. **`htu`.** `BlueskyClientTests` pins `htu` without query or fragment, and the sign-in tests check every recorded proof.

**Principle III.** Umbral builds and signs no JWT. Proofs come from `DefaultDPoPProofTokenFactory`, Umbral is a public client, and the access token is never parsed. The test fake reads and verifies proofs with `JsonWebTokenHandler`, in test code only.

**The R11 note.** `provider_server` holds the normalised issuer host written by `recordServer`. `recheckServer` only compares it with the list and never fetches it, so a stored value that isn't a listed host is refused.

**Changes outside the Bluesky files:**
- `OAuthRoundTrip.startWith` exists so the PAR can run before the cookie is set; `start` delegates to it.
- Some helpers in `Providers/Fediverse.fs` became public (`readBounded`, `parseObject`, `optString`, `get`, `postForm`, `involvesNonPublic`, `requestTimeout`, `ResponseTooLarge`).
- The re-check in `Authorize.fs` and `Token.fs` now logs the refusing kind (`bluesky:` or `fediverse:`).

## Open, not mine
- **A real sign-in with a real Bluesky account has not been done.** The tests use a fake AT Protocol stack. The quickest real check is the development loopback mode: run Umbral at `http://127.0.0.1:<port>` with the development flags, then sign in with your own handle. Bluesky's authorization server accepts AT Protocol's localhost client.
- **The butterfly** would need Bluesky's trademark team to agree (research R7).
