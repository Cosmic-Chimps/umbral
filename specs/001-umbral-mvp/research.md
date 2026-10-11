# Phase 0 Research: Umbral MVP

**Feature**: `001-umbral-mvp` | **Date**: 2026-10-09

Four areas were researched in parallel: the house Oxpecker conventions in the
reference repository, OpenIddict as an OIDC provider, runtime-configurable social
connectors, and the reference repository's auth and data layer. What follows is
the decisions, not the research log.

---

## D1 — Social connectors are hand-rolled, not ASP.NET Core auth handlers

**Decision**: Umbral implements the upstream OAuth 2.0 client flow itself — build
the authorize URL, handle the callback, exchange the code, fetch the profile —
behind one `IProviderAdapter` seam with three implementations. No
`AddAuthentication().AddOAuth(...)`, no `AddGoogle`, no `AddDiscord`.

**Rationale**: FR-010 requires a connector's credentials and enabled state to be
editable at runtime and take effect on the next sign-in with no restart. The
ASP.NET Core authentication stack resolves handler options through
`IOptionsMonitor` *per request*, so runtime reconfiguration is achievable — but
every path there fights the framework:

- `OAuthOptions.Validate()` throws when `ClientId` is empty. A freshly installed
  Umbral has three unconfigured connectors, which is exactly that state.
- `OAuthPostConfigureOptions` constructs a **new `HttpClient` on every options
  rebuild**. Evicting the options cache per request is socket exhaustion;
  evicting per admin save means hand-managing the cache.
- `IAuthenticationSchemeProvider.AddScheme`/`RemoveScheme` are public and work,
  but an ASP.NET Core maintainer states plainly that the multi-tenant scenario
  they serve is *not supported*, and `AddScheme` only registers a descriptor —
  the handler type still has to be resolvable from DI.

Duende sells a commercial component for precisely this problem, and it works by
**replacing the registrations of both `IAuthenticationSchemeProvider` and
`IOptionsMonitorCache<T>`**. That is the size of the real fix.

Hand-rolling removes the problem rather than solving it: a connector is a SQLite
row read inside the request handler, so a secret changed at 15:00 is in effect at
15:00:01 with no cache, no lock, no scheme provider.

**Cost, stated honestly**: Umbral becomes responsible for `state`, PKCE and
`nonce` correctness. This is accepted because the surface is small and
enumerable, and because the mitigation is structural: the state/PKCE/nonce
machinery is written **once** in `Infrastructure/OAuthRoundTrip.fs`, shared by all
three adapters, and tested on its own. No per-provider security code.

**Alternatives considered**:

| Alternative | Why rejected |
| --- | --- |
| `AddOAuth` + `IOptionsMonitorCache.TryRemove` per save | Works, but day-zero empty credentials trip `Validate()`, and the `HttpClient`-per-rebuild behaviour needs hand-management. Buys protocol correctness we can get from shared helpers instead. |
| `OpenIddict.Client.WebIntegration` | Ships Discord, GitHub and Google as first-class providers, already in our dependency tree — genuinely attractive. Rejected because dynamic registration is *explicitly unsupported*: OpenIddict's maintainer calls both documented workarounds a hack. It would also couple the social half to the same library as the token-issuing half, so one version bump risks both. **Revisit at v2** if OpenIddict ships first-class dynamic registrations — the `IProviderAdapter` seam is shaped so that swap is one implementation change. |
| Register all three at startup, gate at challenge time | Handles enable/disable correctly and is part of the design (the picker only renders enabled connectors), but does nothing for the credentials half. Insufficient alone. |

---

## D2 — OpenIddict owns its four tables through EF Core; Umbral's own tables use raw SQLite

**Decision**: `OpenIddict.EntityFrameworkCore` + `Microsoft.EntityFrameworkCore.Sqlite`
for OpenIddict's applications/authorizations/scopes/tokens. Umbral's own tables
(users, linked accounts, connectors, settings, security events) use raw
`Microsoft.Data.Sqlite` with hand-written SQL and an idempotent `initializeSchema`,
following the reference repository's convention. One database file, two lanes.

**Rationale**: OpenIddict ships stores for EF Core, EF6 and MongoDB only.
Implementing `IOpenIddictApplicationStore` and its three siblings by hand is
weeks of work on the most security-critical path in the product — a direct
violation of Principle III ("write as little auth as possible"). Meanwhile
Umbral's own tables are half a dozen flat tables with no relationships worth an
ORM, and the house style is hand-written SQL.

The two lanes do not join. Umbral's user is referenced from OpenIddict only as a
`sub` claim string, never as a foreign key, so nothing needs to span both.

**Cost**: two schema mechanisms, and EF Core's design-time tooling scaffolds C#,
not F#. This means a small **`src/Umbral.Migrations` C# project** containing
nothing but EF migrations for OpenIddict's tables. Recorded in Complexity
Tracking.

**Alternatives considered**: custom OpenIddict stores over raw SQLite (rejected
above); EF Core for the whole schema (rejected — F# entity types plus C#
migrations for every table, fighting both the language and the house style, to
unify two things that never join).

---

## D3 — Reference tokens, so that revocation is real

**Decision**: `UseReferenceAccessTokens()` and `UseReferenceRefreshTokens()`.
Access and refresh tokens are opaque 256-bit identifiers; the payload stays in
the database. The `id_token` remains a normal signed JWT.

**Rationale**: FR-022 says a disabled user's existing tokens MUST stop being
honoured. With self-contained JWTs that is not honestly achievable — a third
party holding an unexpired access token keeps working until it expires, whatever
Umbral does. With reference tokens every use is a database lookup, so revoking
the authorization takes effect immediately and the requirement is met as written.

Two useful side effects: it removes the access-token-encryption question
entirely (OpenIddict encrypts access tokens by default, which produces a
five-segment JWE that confuses every developer who tries to decode it), and it
keeps tokens short.

**Cost**: more writes to SQLite, which matters on an SD card. Mitigated by the
Quartz pruning job (D8) and by documenting that `/data` on an SSD is preferred.

**Alternatives considered**: self-contained JWTs with `DisableAccessTokenEncryption()`
and a short access-token lifetime (the conventional choice; rejected because it
only *bounds* the revocation window rather than closing it, and FR-022 is
written as an absolute).

---

## D4 — The public origin is configuration, never derived from the request

**Decision**: one required setting, `UMBRAL__PUBLIC_ORIGIN`. Umbral refuses to
start without it. Every URL Umbral publishes or compares — the issuer, the
connector redirect URIs, the callback URL shown in the admin UI — is derived from
it alone. `UseForwardedHeaders` is registered for correct client IPs and scheme,
but its output never feeds a redirect URI.

**Rationale**: this is forced, not chosen. The `redirect_uri` sent to a
provider's token endpoint must byte-match the one sent to its authorize endpoint,
which must byte-match what the operator registered in the provider's console. A
value derived from `Request.Scheme` and `Host` behind a reverse proxy is
`http://umbral:8080` while the registered value is `https://auth.example.com`,
and every login fails at the token exchange with an opaque `redirect_uri_mismatch`.
The same mismatch makes OpenIddict advertise the wrong `issuer`, which every
client library rejects.

**Supporting decisions**:

- Validate on parse: absolute URI, scheme `https` (or `http` only for
  `localhost`/`127.0.0.1`/`[::1]`), no path, no query, no fragment; normalise away
  a trailing slash and lowercase the host.
- Also call `options.SetIssuer(publicOrigin)`. Belt and braces.
- The admin UI compares the request's apparent origin against the configured one
  and renders a visible banner when they differ. This turns the commonest silent
  misconfiguration into a self-diagnosing one. It is advisory only.
- `UMBRAL_INSECURE_ALLOW_HTTP=true` gates OpenIddict's
  `DisableTransportSecurityRequirement()`, is refused outside Development, and
  prints a warning banner on every start.

**Documented product limitation**: Google will not accept a redirect URI that is a
raw IP address or a non-`https` non-localhost host. `http://192.168.1.50:8080/...`
— the most natural homelab URL there is — **cannot be registered with Google at
all**. Discord and GitHub are permissive about `http://localhost`. The Connectors
screen must say this at the moment Google is chosen, and the connector list is
ordered Discord, GitHub, Google so the easy wins come first.

---

## D5 — Keys are generated once into the data directory

**Decision**: at first boot, generate two self-signed 4096-bit RSA certificates
into `/data/keys` — `signing.pfx` and `encryption.pfx`, five-year validity, mode
0600 — and register them with `AddSigningCertificate`/`AddEncryptionCertificate`.
The ASP.NET Core Data Protection key ring is persisted to the same directory with
`PersistKeysToFileSystem` and `SetApplicationName("Umbral")`.

**Rationale**: `AddDevelopmentSigningCertificate()` stores keys in the host user's
X.509 store, which in a container lives in the writable layer. `docker compose
pull && up -d` on a Pi replaces the container, the keys vanish, and every issued
token becomes undecryptable — users logged out of every app, refresh tokens
failing with `invalid_grant`. That is a guaranteed first-update outage, so FR-007
and SC-005 make this a correctness requirement rather than an optimisation.

The Data Protection key ring is a separate concern with the same failure mode: it
protects the admin cookie, the antiforgery tokens and the connector secrets
(D6). Losing it logs the operator out and makes stored client secrets
undecryptable.

**Consequence for the operator**: back up `/data`. The README and the Dockerfile
must both say the volume is required.

**Alternatives considered**: operator-supplied certificates (fine for a
sophisticated deployment, a setup step that will be skipped on an appliance —
supported as an override, not the default); raw keys in a settings row (hand-rolled
key storage, loses the `NotBefore`/`NotAfter` ordering OpenIddict uses for
rotation).

---

## D6 — Secrets at rest: Data Protection for upstream, PBKDF2 for everything we verify

**Decision**:

| Secret | Treatment |
| --- | --- |
| Upstream connector client secret | Encrypted with `IDataProtector` under the purpose `Umbral.ConnectorSecrets.v1`. Umbral must be able to *use* it, so it must be reversible. |
| App (OIDC client) secret | Hashed by OpenIddict — PBKDF2-HMAC-SHA256, 10,000 iterations, 128-byte salt. Not a choice: `ObfuscateClientSecretAsync` does this and `GetClientSecretAsync` returns the hash. Unrecoverable by construction. |
| Operator credential | PBKDF2-HMAC-SHA256, iteration count tuned so a Raspberry Pi 4 spends 0.5–1 s. Verified at admin sign-in only, so the cost is paid once per session. |

**Rationale**: the distinction is whether Umbral needs the plaintext back.
Keycloak's lesson is instructive: it shipped readable-in-database secrets and
bolted a vault indirection on years later, which is why that feature is awkward
and has had gaps. Encrypting from the first commit costs nothing; retrofitting is
where the bugs live.

**Consequences for the UI**, which must be designed around them rather than
discovered later: no screen can ever show a saved secret, because for two of the
three it is not recoverable. Connector secrets display as a masked value with a
**Replace** action, never **Edit**. App secrets display exactly once, on the page
immediately after creation, with a copy button and explicit "this will not be
shown again" copy, and a **Regenerate** action that warns the app will break until
it is redeployed.

---

## D7 — Linking identities, and the one rule that must not bend

**Decision**: key a linked account on `(providerKind, providerSubject)` — the
provider's immutable identifier. Link to an existing user **only** when the
provider marks the email verified and it matches exactly one existing user whose
email is also provider-verified. Never link on an unverified email, and never key
on an email, a username, or a login.

**Rationale**: this is the account-takeover vector in every social-login system.
If a provider lets someone claim `victim@example.com` without verifying it and
Umbral links on email, Umbral hands over the victim's account. Requiring
verification on both sides closes it, because all three providers require inbox
control before setting the verified flag.

Keying on the immutable id matters independently: **GitHub's `login` is mutable
and reusable**. Keying on it means a renamed account becomes a new user, and a
squatted handle becomes an *existing* one.

The stable identifiers are `id` (Discord snowflake, a **string** in JSON), `id`
(GitHub, int64) and `sub` (Google).

---

## D8 — OpenIddict server configuration

Settled points, with the reasons that are not obvious:

- **Passthrough on the authorization and end-session endpoints only.** Passthrough
  makes OpenIddict validate the request and then let it flow on to Oxpecker
  routing instead of answering itself. It is framework-agnostic — MVC's
  `SignIn`/`Forbid` are thin wrappers over `HttpContext.SignInAsync`/`ForbidAsync`,
  which an Oxpecker handler calls directly. Nothing here needs MVC.
- **No token-endpoint passthrough in the first slice.** Left off, OpenIddict
  handles `authorization_code` and `refresh_token` itself by reusing the principal
  stored in the code — PKCE verification, client authentication, rolling refresh
  token rotation, the lot, for zero code. Passthrough is added later, when refresh
  must re-check whether a user has since been disabled.
- **Claim destinations are mandatory and silent.** A claim with no destination is
  dropped from both tokens. Forgetting `identity.SetDestinations(...)` produces an
  `id_token` containing only `sub`, `iss`, `aud`, `exp` and no error anywhere.
  This is the single most common OpenIddict defect and it must be covered by a
  test that asserts the claim content of a real issued token.
- **`RequireProofKeyForCodeExchange()` globally**, and
  `Requirements.Features.ProofKeyForCodeExchange` on every registered app.
- **`options.UseQuartz()`** with `OpenIddict.Quartz`, because the token table
  grows forever otherwise and reference tokens (D3) make it grow faster.
- **Audience and resource validation changed to on-by-default in OpenIddict 7.0.**
  Pure OIDC clients do not send those parameters so nothing breaks today, but the
  first client that does will fail opaquely. Disable both explicitly with a
  comment rather than leaving it to chance.
- **Naming changed in 6.0** and every pre-2024 tutorial is stale:
  `SetEndSessionEndpointUris` (not `SetLogoutEndpointUris`), `SetUserInfoEndpointUris`
  (capital I), `Permissions.Endpoints.EndSession` (value `ept:end_session`).
- **The endpoint URIs passed to `Set*EndpointUris` are relative** (`"connect/authorize"`)
  and resolve against `PathBase`; the Oxpecker route is the absolute path
  (`/connect/authorize`). When they disagree, `GetOpenIddictServerRequest()`
  returns null — that null is the signature of this specific mistake.
- **`app.UseAuthentication()` must come before the Oxpecker endpoint middleware**,
  because OpenIddict's server integration runs inside it as an
  `IAuthenticationRequestHandler`.

Verified defaults worth knowing: authorization code 5 min, access token 1 h,
**id_token 20 min**, refresh token 14 days with rolling rotation on and a 30 s
reuse leeway.

---

## D9 — F# friction, and how it is contained

There is no production F# + OpenIddict integration to copy. The API is not
hostile to F#, but the friction is real and mechanical:

- Builder lambdas return the builder; F# lambdas must return `unit`, so the
  configuration is a wall of `|> ignore`. **Contained in one file**,
  `Infrastructure/OpenIddictSetup.fs`, so it does not spread.
- `params string[]` overloads need explicit arrays: `RegisterScopes([| ... |])`.
- `SetDestinations` takes `Func<Claim, IEnumerable<string>>`; F# has no
  `yield return`, so the selector is a `seq { }` wrapped in an explicit `Func<_,_>`.
- Managers return `ValueTask`. F#'s `async { }` cannot bind those; `task { }` can.
  **Use `task { }` throughout, never `async { }`.** Oxpecker handlers are
  `Task`-based anyway, so this is the natural grain. A `ValueTask` may be awaited
  only once.
- `IAsyncEnumerable` returns need `.ToListAsync()` (from the BCL on `net10.0`) or
  `FSharp.Control.TaskSeq`.
- `SetClaim` has 15+ overloads; a `null` argument is ambiguous. Pass typed values.
- Named argument `type` collides with the keyword: ``manager.FindAsync(``type`` = ...)``.

**Risk control**: build the OpenIddict half **first as a ~150-line C# spike** —
copy the Velusia sample's two files, point them at SQLite, register one client,
get a token out with `curl` — then port to F# + Oxpecker. Porting working code is
much cheaper than debugging an unfamiliar library and an unfamiliar interop
pattern at the same time. This is Step 1 of the plan, and the spike is deleted
afterwards.

---

## D10 — The reference repository's conventions, and where they do not transfer

Adopted wholesale:

- **Vertical slices.** One file per endpoint under `Handlers/`, laid out top to
  bottom: view → slice-private SQL → handler → `let endpoint = GET [ route ... ]`.
  Slices never call each other's data access. Shared UI moves to `Views/` only on
  second use.
- **F# compile order is the dependency graph**: Domains → Infrastructure → Views
  (Texts → Theme → Components → Layout) → Handlers → Program.fs last. Adding an
  endpoint is a three-step ritual — new file, `<Compile Include>` before
  `Program.fs`, and the `endpoint` added to `Program.endpoints`. Miss the third and
  the route silently 404s with no error anywhere, so **a meta-test asserts every
  handler module's `endpoint` appears in the list**.
- **`Program.buildApp args customize`** as the composition seam, so integration
  tests host the real application on a `TestServer`. Built on day one; retrofitting
  it means rewriting `Program.fs` and every test.
- **Antiforgery's two-step gotcha**: `UseAntiforgery()` between `UseRouting` and
  the endpoint middleware, *and* the handler checks `IAntiforgeryValidationFeature`
  before reading the form, because ASP.NET refuses to read a form whose token
  already failed and throws something confusing instead of a clean 400.
- **Policies as tests, not etiquette**: the reference repo has a test that reads
  the production sources and fails on non-literal SQL, and another that fails if a
  committed settings file contains a non-empty client secret. Both apply directly.
- **`data-*` attributes are the test hook contract**, never CSS classes, so a
  restyle cannot break the suite.
- **No frontend build step.** Tokens are F# data in `Views/Theme.fs` that generate
  both the CSS custom properties and the Tailwind CDN config; icons are a closed
  union used to subset the icon font. No npm, no bundler, no `wwwroot`.
  **Amended 2026-10-10:** the tokens moved to `Assets/tailwind.config.js` and the
  CSS is compiled once by the standalone Tailwind CLI (`Assets/build-css.sh`, no
  Node or npm) and committed; icons are inline SVG, and the stylesheet, htmx and
  Umbral's script are embedded resources served by Umbral itself — still no
  bundler and no `wwwroot`. Third-party script on an identity provider's
  credential pages violated constitution IV, and an offline homelab got an
  unstyled UI (principle II).

Corrections and deviations, each deliberate:

- **The reference repo loads htmx and never uses it.** There is not one htmx
  attribute in it; its interactivity is Alpine.js and full-page POST→redirect.
  There is therefore **no in-repo htmx pattern to copy**, and Umbral establishes
  one (D11). Also: `hxPartial` arrived in Oxpecker.Htmx 4.1 and is **not available**
  in the pinned 4.0.
- **The reference repo builds its provider list by iterating configuration at
  startup**, so enabling a provider there needs a restart. That is the single
  biggest place the reference architecture does not transfer, and it is why D1
  exists.
- **Its no-ORM rule does not bind Umbral.** That rule belongs to the reference
  repository's own constitution; Umbral's constitution constrains the storage
  engine, not the access library. D2 takes EF Core for OpenIddict's tables only.
- **Localization**: the reference repo is bilingual with a ~200-field `Texts`
  record. Umbral ships English only, but keeps `Views/Texts.fs` as the single home
  for user-facing strings, because retrofitting that is miserable and because
  Principle VI's "every error says what to do next" is far easier to audit when
  every string is in one file.

Worth lifting verbatim: the correlation-cookie hardening the reference repo
arrived at the hard way — `SameSite=Lax` (not `None`, which some browsers drop
over plain http; Lax cookies still arrive on the provider's top-level redirect
back) and `SecurePolicy = SameAsRequest`. Umbral runs behind plain http in dev and
reverse proxies in production, which is exactly the situation that produced it.

---

## D11 — htmx, used where it earns its place

**Decision**: the baseline interaction is the reference repo's — a form POST, a
302, and a flash message. htmx is used for the three in-place updates where a
full page reload is actively worse: toggling a connector on or off, saving
connector credentials (the card re-renders showing the masked secret), and
enabling or disabling a user in the list. Every one of these is a form that works
without htmx; htmx only intercepts it.

**Rationale**: the admin UI is three screens of forms, and three screens of forms
do not need a frontend framework. Using htmx everywhere for its own sake would
add a second rendering path to every slice for no user-visible gain. Using it
nowhere would ignore a stated stack choice and leave the Connectors screen
reloading the whole page to flip a switch.

**Mechanics**: a slice that serves both returns a bare `HtmlElement` fragment when
`ctx.TryGetHeaderValue HxRequestHeader.Request` is present, and the same element
piped into `Layout.page` otherwise. Progressive enhancement is not optional here —
it is what makes the htmx path testable with the same `Browser` helper as
everything else.

---

## D12 — Testing

- **xunit v3 on Microsoft.Testing.Platform**, which requires `global.json` with
  `{"test":{"runner":"Microsoft.Testing.Platform"}}` and `<OutputType>Exe</OutputType>`.
  Filters are `--filter-class` / `--filter-method`, not VSTest's `--filter`.
- **No Testcontainers.** The reference repo needs Docker for PostgreSQL; Umbral's
  database is a file, so each test gets a fresh SQLite file in a temp directory.
  This makes the suite faster than the reference repo's and removes a dependency.
- **Provider calls are stubbed at the `HttpMessageHandler` level** against
  **captured real response bodies**, committed as fixtures. Those fixtures are the
  actual asset. They must cover: GitHub's token endpoint returning
  `application/x-www-form-urlencoded`; GitHub `/user` with a null email forcing the
  `/user/emails` path; a verified-but-not-primary GitHub address; a Discord user
  with `avatar: null` (both default-index formulas); a Discord `a_`-prefixed
  animated hash; Google with `email_verified: false`; and a GitHub HTTP **200**
  carrying `{"error": "bad_verification_code"}`.
- **The round-trip state machine is tested once**, not per provider: missing
  cookie, missing `state`, mismatched `state`, expired correlation, replayed
  callback, `?error=access_denied`, and a slug in the path that disagrees with the
  slug in the correlation record.
- **A `Fake` provider kind** for end-to-end tests and demos, hard-gated behind
  `UMBRAL_ALLOW_FAKE_CONNECTOR=true` **and** a non-Production environment, refusing
  to start if both are not true. A fake identity provider that ships enabled is a
  total authentication bypass, so a dedicated test asserts it cannot be enabled in
  Production.
- **Meta-tests**: every handler module's `endpoint` is in `Program.endpoints`; no
  committed settings file holds a non-empty secret; SQL text is literal.

---

## D13 — Container and runtime

- `mcr.microsoft.com/dotnet/aspnet:10.0` (Debian) for `linux/amd64` and
  `linux/arm64`. Alpine is tempting for size but adds a musl RID dimension to the
  SQLite native asset, and a missing `e_sqlite3.so` is a classic ARM container
  failure. Debian first; revisit Alpine once the image is proven.
- `/data` is a declared volume holding `umbral.db`, `keys/` and the Data Protection
  key ring. Backup is `cp` of one directory, per Principle II.
- SQLite needs `PRAGMA journal_mode=WAL` and `PRAGMA busy_timeout=5000` on every
  connection; the default journal mode serialises readers against the writer and
  will produce `SQLITE_BUSY` under a web UI plus token traffic.
- `DOTNET_gcServer=0` and an idle-RSS test, because SC-007 puts a 150 MB ceiling
  on idle residency.
- EF Core migrations are applied at startup with `MigrateAsync()`. Safe here
  because a single container is a single writer — there is no multi-instance race
  to lose.

---

## D14 — What the C# spike (T001) actually proved

The spike ran the whole server path against the configuration above and settled
several things that were assumptions until then. Confirmed, with evidence:

- **Claims vanish silently without `SetDestinations`.** Two otherwise identical
  runs: with it, the `id_token` carries `email`, `email_verified`, `name`,
  `picture`, `idp`; without it, only `sub` and OpenIddict's own bookkeeping
  claims survive. The failure from outside is **total silence** — HTTP 200, a
  syntactically valid JWT, a valid signature, nothing in the logs. The only way
  to see it is to decode the token. This is why T039 asserts the claim content
  of a real issued token rather than the construction of the identity.
- **The token endpoint handles both grants with no handler written.** No route is
  mapped at `/connect/token` in the spike, and `authorization_code` and
  `refresh_token` both return full token sets, including rolling refresh-token
  rotation. D8's "no passthrough in the first slice" holds.
- **Reference access tokens are genuinely opaque** — one segment, no dots, a
  random 256-bit identifier.
- **Revocation is immediate.** `TryRevokeAsync` on the authorization, then the
  same access token at `/connect/userinfo` returns 401 with
  `error="invalid_token"` and no propagation delay. FR-022 is satisfiable as
  written, which is the whole reason D3 chose reference tokens.
- **Keys survive a restart.** Same certificate fingerprint after `kill -9`, and
  a pre-restart `id_token` verified by hand against the post-restart JWKS using
  an independent library — so the key material persisted, not just a database row.

Corrections to what this plan said before the spike ran:

| Was | Actually |
| --- | --- |
| `code_challenge_methods_supported` excludes `plain` by configuration | **No builder flag does this.** `plain` is advertised and accepted. Needs a custom handler — T105. |
| `EnableErrorPassthrough()` is safe to enable with the authorize handler | **It is not inert.** With it on and no error handler written, every request OpenIddict would reject cleanly — missing PKCE, bad `redirect_uri` — becomes an `InvalidOperationException` and an HTTP 500. It must land in the same step as `Handlers/Connect/Error.fs`. |
| `GetOpenIddictServerRequest()` is in `OpenIddict.Server.AspNetCore` | It is in **`Microsoft.AspNetCore`**. Nothing in the error message or IntelliSense hints at this; the spike found it by reflecting over the loaded assembly. |
| OpenIddict's built-in user-info handler is a fallback option | It maps a **fixed standard claim set only** and has no concept of a custom claim like `idp`. The hand-written passthrough handler (T034) is required, not preferred. |
| `Permissions.Prompts.*` exists | It does not exist in 7.7.1. Consent is controlled by `ConsentType` alone. |

One trap for the F# port, which D9's warning about `SetClaim`'s 15+ overloads
predicted: `SetClaim(Claims.EmailVerified, "true")` resolves to the **string**
overload and emits `"email_verified": "true"`, a string, not a boolean. An app
doing `if (claims.email_verified)` in JavaScript then treats `"false"` as true.
Use the explicit `bool` overload, and assert the JSON type in T039.

Also confirmed as real and correctly placed: `DisableAudienceValidation()` and
`DisableResourceValidation()` on the server builder, and
`DisableTransportSecurityRequirement()` on the builder returned by
`UseAspNetCore()`.

## Open items carried into the plan

| Item | Status |
| --- | --- |
| **Discord PKCE support** | Not documented by Discord. Two third-party sources say `S256` only and that `plain` is rejected. Treated as probable; PKCE is a per-kind capability flag so it can be switched off without a code change. A task verifies it empirically against a real Discord app. |
| **FR-012 / FR-017 / FR-025** | Resolved in the spec on assumed answers pending owner confirmation at plan review. Each is cheap to reverse now. |
| **Principle VII vs. the operator credential** | The constitution excludes "password-based local accounts"; FR-017 introduces one operator credential. Recorded in Complexity Tracking with a proposed wording narrowing. |
