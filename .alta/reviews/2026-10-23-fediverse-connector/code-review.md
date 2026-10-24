# Code review: spec 010, the Fediverse connector

Branch `alta/earnest-cetus-9pd3`. Reviewed at `9e58a4b`; still valid at `91d7757` (since then only `umbral.input.css`/`umbral.css`, docs and the `main` merge). Reviewer: the `umbral` session, 2026-10-24, for correctness and security (constitution Principle IV), from the diff against spec 010, plan, research and contract. Each finding below was checked against the code on the branch.

## Verdict

**Not yet clear: four blocking items, four nits.** The design is sound, and a lot was done right (list under "Checked and clean"); the blocking items are all small, local fixes plus one rule to extend.

## Blocking

### B1. A configured proxy bypasses the address guard
`src/Umbral/Infrastructure/Providers/Fediverse.fs`, `guardedHandler` (around line 108). The `SocketsHttpHandler` keeps `UseProxy = true` (the default), and nothing in `src` turns it off. On Linux, `HttpClient.DefaultProxy` reads `HTTPS_PROXY`/`ALL_PROXY`. Through a proxy, `ConnectCallback` connects to the **proxy's** endpoint, so `isPublicAddress` judges the proxy, not the fediverse server. The only check left on the target is the DNS pre-check (`Handlers/Connect/Fediverse.fs`, `resolvesPublic`), which is exactly the rebinding gap the handler's own comment says connect-time checking closes.
- **Behind a private proxy:** an operator who runs Umbral behind one (`HTTPS_PROXY=http://10.0.0.5:3128`) gets every fediverse call refused as `non_public_address`.
- **Behind a public proxy:** a name that passes the pre-check is then resolved by the proxy to `169.254.169.254` or an internal host.

**Fix:** `UseProxy = false` on this handler, and a test that the handler has no proxy, even with `HTTPS_PROXY` set in the environment.

### B2. What a server returns is stored with no length limit
`Providers/Fediverse.fs` (the registration answer: `client_id`, `client_secret`, around lines 378–383), `Domains/Fediverse.fs` (the metadata endpoints kept as text, around 317–332), the account read (around 520–530: `id`, `username`, `display_name`, `avatar`, `url`). The only bound is the 64 KiB body.
- **What it costs:** an anonymous person with wildcard DNS (`*.evil.example`) to a server that returns valid metadata and a 60 KB `client_id` and `client_secret` stores a new row of about 150–200 KB per `POST /connect/fediverse/start` with a fresh subdomain. At the sign-in limit (30 a minute per address) that is several GB a day from one IPv4 address, more from IPv6 /64s, and registrations are never pruned.
- **Account fields:** they become claims in every access token (`name`, `preferred_username`, `profile`, `picture`), so a hostile server can also make its users' tokens too large for apps' header limits.

**Fix:** refuse (`Malformed`) any value over a bound:
- `client_id` and `client_secret` at 512 bytes;
- endpoint URLs, `avatar` and `url` at 2048 characters;
- `id` at 128;
- `display_name` at 256.

Prefer refusing to truncating. Add a test for each bound. Optional, say in `notes.md` whether you take it: prune registrations never used (`last_used_utc IS NULL`) after N days.

### B3. The server's `username` is not checked, so one server's user can pose as another's
`Providers/Fediverse.fs` (the account read, around 523–527, and `toProfile`, around 561), `Domains/Fediverse.fs` (`preferredUsername`). The handle pattern is applied to what the person types, never to what the server answers. If `evil.example` answers `verify_credentials` with `username: "alice@mastodon.social"`, every app receives `preferred_username = "alice@mastodon.social@evil.example"`, which an app that splits on the first `@`, or a page that shows it, reads as alice of mastodon.social. Bidi controls (U+202E), zero-width characters and newlines work too. This breaks the claim's contract (`user@server`).

**Fix:** refuse (`Malformed`) a `username` outside Mastodon's pattern (`[A-Za-z0-9_]+([.-]+[A-Za-z0-9_]+)*`, at most 64 characters; reuse `userPattern` if it is that). With the same fix, keep `profile` only when its host is the server's host (else drop the claim, as a non-https one is dropped).

### B4. "Only these servers" never reaches people already signed in
`Infrastructure/EmailDomainRules.fs`, `ruleFor` returns `NoRule` for the Fediverse kind, so the recheck a reused session gets at authorize (`Authorize.fs`) and a refresh gets in `Token.fs` admits every fediverse identity; the server list is enforced only at the account step and the callback. If the operator removes a hijacked or hostile server from the list, its people keep their 14-day sliding Umbral session and their apps' refresh tokens.

The allowed-email-domains feature chose the opposite on purpose ("tightening a rule reaches the people already signed in"), and the server list is the same kind of rule (Principle VII: it decides who can be a user). **Decision (the reviewer's, consistent with that precedent):** apply the server list at the same two rechecks. The server is in the identity's subject (`host#id`), so no new storage is needed. A person from a server no longer on the list is refused there exactly as a person whose email domain was removed is. Add tests (session reuse and refresh after the server is removed from the list), and one README sentence in "Who can sign in" saying the list reaches existing sessions, as the domain list does (send that line to `umbral-documentation`).

## Nits (take them if small; no re-check needed for these)

- **N1.** A callback that fails the round-trip check is logged without the server (`Handlers/Connect/Fediverse.fs`, around 385, `fediverse:exchange_failed;{detail}`). FR-022 asks that state failures name the server too. It is in the cookie and can be read without consuming it, as `returnUrlOf` does.
- **N2.** Two sign-ins at once from the same server, while the account step re-registers (callback changed, or `invalid_client`), can make the earlier one's callback delete the fresh registration (around 259, 275, 443). That costs one failed sign-in, not security. Deleting only when the stored client is still the one the round trip used would close it.
- **N3.** `isPublicAddress` (`Domains/Fediverse.fs`, around 262–289) is missing `64:ff9b:1::/48` (local NAT64, RFC 8215), `100::/64` (discard), `2001:10::/28` and `2001:20::/28` (ORCHID), and `::ffff:0:0:0/96` (IPv4-translated). These only matter on hosts with local NAT64 or unusual routing.
- **N4.** Redirects use 302, not 303. This is harmless and matches the rest of the app; noted only for the record.

## Checked and clean

- **Address guard.** It works at connect time on the resolved address, apart from B1. Redirects are off. IP literals typed as the server are refused, in decimal, hex and octal forms too. Also handled: `localhost`, IDN through `IdnMapping`, a trailing dot, ports and `http` (development only, failing closed: it needs both `ASPNETCORE_ENVIRONMENT=Development` and `UMBRAL:INSECURE_ALLOW_HTTP=true`). The WebFinger `self` authority goes back through `normaliseServer`, `checkHost` and the list. The IPv4 ranges include 169.254/16 and 100.64/10, and the IPv6 ranges include fc00::/7, fe80::/10 and mapped addresses.
- **Time and size.** Each call has a 10 s budget, body included. The connect timeout is 5 s and the DNS pre-check 5 s. Bodies are capped at 64 KiB, with no decompression. All three routes use `RateLimits.SignIn`.
- **URLs.** The authorize and token addresses are the stored metadata endpoints, which must be https on the server's own host with no user info. `buildUrl` escapes its parameters, and nothing from the registration answer becomes a URL.
- **Secrets at rest.** They are protected with the purpose `Umbral.FediverseServers.v1`. A secret that can't be read counts as no registration and is replaced. Registrations are keyed by the normalised host. A changed callback address re-registers, and `invalid_client` deletes the row.
- **Schema 7.** The migration runs inside `initializeSchema`'s transaction, guarded by the version stamp. `CREATE TABLE IF NOT EXISTS` makes it safe to run against an existing database.
- **State and PKCE.** Each attempt gets a new state and verifier, in a data-protected, HttpOnly, Lax cookie on `/connect`. It's deleted on completion, compared in constant time, recorded as consumed, and checked for slug and the 10-minute expiry. Only S256 is used, and a server without it is refused (metadata must list `S256`; NodeInfo `gotosocial` is the one exception, and its claim only affects its own users).
- **Mix-up.** The server comes only from the encrypted correlation. The code is exchanged at that server's stored token address, with its client secret and the same callback. A cross-server code can't be redeemed without Umbral's secret for the other server. The slug check keeps connectors apart, and `fediverse` is a reserved slug.
- **Subject.** It is `host#id`, with the host normalised and unable to contain `#`. Lookups use (kind, subject) with parameterised SQL. `acct` is never trusted, and WebFinger only picks the server, which is then checked again.
- **No email linking.** `Email = None`, so `Linking.decide` never reaches the verified-email query. The email-domain rules don't apply, and nothing refuses these users by accident.
- **Claims.** No `email`. `preferred_username` and `profile` go into the id_token only with the `profile` scope, and `setProfileClaims` replaces old values.
- **Account step.** It has antiforgery and works without JavaScript. The security log carries reasons and servers, never tokens, codes or secrets.
- **Existing connectors.** `finishSignIn` is the old callback tail moved as it was. `Start` passes `""` as the server, an old 6-field round-trip cookie still decodes, and new connectors default to `FediverseAccess.Any`. No regression found.

## Tests

A full `dotnet test` on the branch at `9e58a4b`, run by the reviewer: **1656 passed, 0 failed, 0 skipped** (6 min). It agrees with the implementer's 1656/1656 at `67eced4`. The suite has no test for B1–B4, which is why each fix needs one.

## What happens next

Fix B1–B4 on the branch, with tests. Note each fix and the nits you take in `notes.md`. Commit this file with your answers, then message the `umbral` session for the one re-check. No merge until this file says clear. The maintainer merges.

## Re-check at `ad499a8` (2026-10-24)

**Clear to merge.** I read `5780ef5` and `92b1177` against each finding and ran the suite myself.

- **B1, fixed.** `guardedHandler` sets `UseProxy <- false`, so the connect callback always judges the fediverse server's own address.
- **B2, fixed.** A value over its bound makes the answer `Malformed`; nothing is truncated:
  - `client_id` and `client_secret` at 512;
  - account `id` at 128;
  - `display_name` at 256;
  - `avatar`, `url` and the metadata endpoints at 2048.

  A registration row is now a few KB at most. Not pruning rows is accepted: the bound, the sign-in rate limit and the spec's "no clean-up in 010" together make it reasonable.
- **B3, fixed.**
  - `isUsername` is Mastodon's pattern anchored with `\A…\z` (better than the `$` I suggested, which matches before a final newline; a test covers `"alice\n"`) and at most 64 characters.
  - `profile` is kept only when it is https and on the server's own host.
- **B4, fixed.**
  - **How it decides:** `FediverseServers.recheckServer` reads the hosts of the user's own fediverse links, but only when the session's connector is a Fediverse connector set to "only these servers". It admits when one of those hosts is still listed.
  - **Where it runs:** session reuse at authorize (sign out, then sign in again) and refresh in `Token.fs` (`invalid_grant`). Both record `fediverse:server_not_allowed;server=<host>`.
  - **Tests:** `FediverseSessionRecheckTests` covers a refusal at reuse, a refusal at refresh, and a still-listed server.
  - **Docs:** the README and walkthrough sentence about it goes to `umbral-documentation`, and its clearance covers that text.
- **Nits:**
  - N1 is fixed: `OAuthRoundTrip.peek` reads the cookie without consuming it, so a failed callback names its server.
  - N2 is fixed: a registration is deleted only if it was made before the round trip began.
  - N3 is fixed: the five IPv6 ranges are refused.
  - N4 stays as it is, as agreed.
- **Tests:** a full `dotnet test` at `ad499a8`, run by the reviewer, passes **1688 of 1688**, 0 failed, 0 skipped.

**One cosmetic nit, no re-check needed.** `usernamePattern`/`isUsername` (`Domains/Fediverse.fs`), `MaxEndpointLength` (the same file) and the `Max*Length` literals (`Providers/Fediverse.fs`) were inserted between an existing `///` doc comment and the function it documents (`isPublicAddress`, `ownEndpoint`, `register`). Those comments now attach to the new definitions. Move each new definition above the doc comment, or into its own place, when convenient; it can land after the merge.
