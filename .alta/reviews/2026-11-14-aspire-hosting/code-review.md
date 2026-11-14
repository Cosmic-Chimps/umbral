# Code and security review, task a5pr (CosmicChimps.Aspire.Hosting.Umbral)

Reviewer: the `umbral` session. Commit `0d972e6` on `alta/sturdy-partridge-kwqh` (base `7d90362`),
read against the task file and `notes.md`.

**Verdict: not clear yet, one blocking item and three nits.** The design is right, and the
blocking item is a few lines.

## What I checked, and it holds

- **The run/publish split** (`UmbralOrigin.ResolveAsync`, one place for the container and
  `WithReference`):
  - Run mode sets `Development` and `UMBRAL__INSECURE_ALLOW_HTTP` only for an http origin on a
    loopback host. That is Umbral's own loopback rule from `PublicOrigin.fs` (`localhost`,
    `127.0.0.1`, `[::1]`).
  - Publish mode never sets the pair. It refuses a missing origin and a non-https string origin,
    and the messages name the call.
  - A parameter origin passes through. That's acceptable: without the pair, Umbral refuses http
    on a non-loopback host when it starts (`PublicOrigin.fs:110`), so a wrong value fails closed.
  - Your manifest run and `The_publish_manifest_has_no_Development_and_no_laptop_pair` pin it.
- **`WithReference`:**
  - `UMBRAL_ISSUER` is the public origin from the same resolver, never the internal endpoint.
  - `UmbralResource` is not `IResourceWithServiceDiscovery` or `IResourceWithConnectionString`, so
    none of Aspire's own `WithReference` overloads applies: no ambiguity and no `services__`
    variable.
- **`WithTrustedProxies`:** comma-joined, which matches `TrustedProxies.fs:122` (split on `,`,
  trimmed). An entry with a comma inside is refused.
- **The image tag:** `-p:Version` first, else the first `## X.Y.Z` of `CHANGELOG.md` (released
  sections only since cl6d), else the build fails. Never `latest`. `Version` follows it. The
  metadata is read at runtime, and a test pins the source.
- **The `nuget` job:**
  - `needs: [image, changelog]`, so the package is never pushed before its image.
  - It checks out the tag with `persist-credentials: false`.
  - Its permissions are `contents: read` and `id-token: write` only.
  - It runs in environment `nuget`.
  - `NuGet/login` is pinned to `8d19675…`, which I checked is the `v1.2.0` tag's commit.
  - The key is the login step's short-lived output, passed through `env`, never stored.
  - The pack-name check catches a version mismatch before the push.
  - `--skip-duplicate` for re-runs is right.
- **`ci.yml`:** naming the test project in the `memory` job is the right fix for Microsoft.Testing
  Platform's exit 8.
- **Tests:** run here, 35/35 passed.

## Blocking

**B1. `UMBRAL_ISSUER` can differ from the `iss` Umbral puts in tokens.**
- `WithPublicOrigin(string)` stores the trimmed string as typed.
- Umbral normalises its origin (`PublicOrigin.fs:106-121`): it lowercases the scheme and host,
  drops a default port, and has no trailing slash.
- So `WithPublicOrigin("https://Auth.Example.com")` or `"https://auth.example.com:443"` gives the
  container a value Umbral accepts and normalises, while every app receives the un-normalised
  string as `UMBRAL_ISSUER`. Issuer validation then fails at the first sign-in.
- That breaks the one promise `UmbralOrigin` exists for: that the container and the apps cannot
  disagree.

Fix: store `uri.GetLeftPart(UriPartial.Authority)`. `Uri` lowercases the scheme and host and leaves
out the default port, which is what Umbral does. Tests:
- `"https://Auth.Example.com:443/"` gives `https://auth.example.com` in both
  `UMBRAL__PUBLIC_ORIGIN` and `UMBRAL_ISSUER`;
- a non-default port is kept: `https://auth.example.com:8443`.

## Nits

**N1. Say what a parameter origin must look like.** It can't be normalised at publish, and the
app receives it raw. In `WithPublicOrigin(parameter)`'s doc comment and in `docs/run/aspire.md`,
add: "lowercase, no default port, no trailing slash, e.g. `https://auth.example.com`; it must be
exactly the issuer Umbral will report."

**N2. In run mode, a loopback origin on another port.** `AddUmbral("umbral")` plus
`.WithPublicOrigin("http://localhost:9000")` makes the issuer name a port nothing listens on: the
endpoint is 8080. Refuse a loopback origin whose port differs from the endpoint's port, or take
the port from the origin. Either is fine.

**N3. A missing `NUGET_USER` on the real repository skips the package silently.** That's right
for forks. But if the variable is forgotten here, a release ships the image without the package,
and the only sign is a skipped job.
- Suggestion: `if: vars.NUGET_USER != '' || github.repository == 'Cosmic-Chimps/umbral'`, plus a
  first step that fails with a clear error when the variable is empty.
- Or keep the condition as it is, and add a line to the header comment and the maintainer's
  steps saying that a skipped `nuget` job means the variable is missing.

## Re-check

One re-check after B1 (and whichever nits are taken). I'll read only the diff since `0d972e6`.
