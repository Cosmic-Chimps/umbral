# Aspire hosting package: notes for the reviews

Task `2026-11-14-aspire-integration-addumbral-the-hosting-package-a5pr`, branch
`alta/sturdy-partridge-kwqh` (worktree `~/.alta/worktrees/umbral/sturdy-partridge-kwqh`), on main at
`7d90362`.

## What changed

New:

- `src/CosmicChimps.Aspire.Hosting.Umbral/`: the package (`UmbralResource`, `UmbralOrigin` (internal: the
  origin rules in one place, shared by the container and `WithReference`), the extensions, `README.md`
  as the NuGet readme, `icon.png` = `site/public/apple-touch-icon.png`, 180x180).
- `tests/CosmicChimps.Aspire.Hosting.Umbral.Tests/`: 35 model-level tests, xunit v3 on
  Microsoft.Testing.Platform, no Docker.
- `docs/run/aspire.md` ("Run Umbral from an Aspire AppHost"), in the site's sidebar under "Run it".
- `changelog.d/aspire-hosting.md` (Added).

Changed:

- `Umbral.slnx`: both projects, so CI's `dotnet build` and `dotnet test` take them.
- `.github/workflows/ci.yml`: the `memory` job's two `dotnet test --filter-class` runs name
  `tests/Umbral.Tests/Umbral.Tests.fsproj`; across the solution, the new project matches no test
  and Microsoft.Testing.Platform exits 8.
- `.github/workflows/release.yml`: the `nuget` job and step 4 of the header.
- `.github/workflows/samples-aspire.yml`: also runs on `src/CosmicChimps.Aspire.Hosting.Umbral/**`.
- `samples/aspire/`: `AddUmbral(...).WithDataVolume("umbral-aspire-data")` and `WithReference(umbral)`
  through a ProjectReference (`IsAspireProjectResource="false"`); the notification and steps stay.
  README updated.
- `samples/README.md`, `README.md`: the Aspire entry names the package.

## Decisions beyond the task's letter

1. **Where the default tag comes from.** `-p:Version` when given (the release job), otherwise the
   first `## X.Y.Z` heading of `CHANGELOG.md`, which holds released sections only. No file to
   hand-edit. A build fails if neither gives a tag. `Version` defaults to the same value, so a local
   `dotnet pack` is the last release's package. The assembly also carries `UmbralImageTagSource`
   (`Version` or `CHANGELOG.md`) for the test.
   Consequence: the sample now follows the last release instead of a hand-pinned `0.11.0` (today the
   same value).
2. **The laptop pair's hosts.** Umbral's own loopback rule (`PublicOrigin.fs`: `localhost`, `127.0.0.1`,
   `[::1]`), not `localhost` alone.
3. **`UmbralResource` is not `IResourceWithServiceDiscovery`.** Aspire's service-discovery
   `WithReference` cannot apply to it; the tests pin that no `services__` variable appears.
4. **`NUGET_USER`.** `NuGet/login` needs the nuget.org account that owns the trusted-publishing
   policy. It is the repository variable `NUGET_USER` (a name, not a secret); without it the job is
   skipped, so a fork's release does not fail.
5. **A parameter origin in publish mode** is passed through unchecked (its value is not known at
   publish). Umbral refuses plain http on a non-loopback host when it starts, and without the pair
   it refuses http entirely.
6. **`--skip-duplicate`** on the push, so a re-run of a release finds the package there and goes on.

## Verification

- `dotnet test --project tests/CosmicChimps.Aspire.Hosting.Umbral.Tests/...`: 35 passed. Root
  `dotnet build -c Release`: 0 warnings (warnings are errors).
- `samples/aspire`: `dotnet build -c Release -warnaserror` clean.
- **Walk** (2026-11-14, ports 18080/5001 via user secrets, fresh `umbral-aspire-data` volume):
  first run started Umbral only, container `ghcr.io/cosmic-chimps/umbral:0.11.0` with
  `UMBRAL__PUBLIC_ORIGIN=http://localhost:18080`, `ASPNETCORE_ENVIRONMENT=Development`,
  `UMBRAL__INSECURE_ALLOW_HTTP=true`; `.alta/reviews/2026-11-11-samples-aspire/setup.mjs` claimed it,
  enabled passkeys and created the app; second run with the client ID and secret, `walk.mjs`: all
  checks `ok` (home, public, `/private` to Umbral's sign-in at `localhost:18080`, passkey sign-in,
  `sub`/`name`/`idp`, sign out, `/private` to Umbral again).
- **Publish refused:** `dotnet run -- --operation publish --publisher manifest` on the sample fails the
  `publish-manifest` step with "Umbral resource 'umbral' has no public origin. Call
  WithPublicOrigin(...) with the https address people reach it at before publishing." (`aspire publish`
  alone does nothing on an AppHost with no compute environment.)
- **Published manifest** of a scratch AppHost with `WithPublicOrigin("https://umbral-demo.example.com")`,
  `WithTrustedProxies("10.99.0.1")` and an app with `WithReference(umbral)`: Umbral's `env` is only
  `UMBRAL__PUBLIC_ORIGIN` and `UMBRAL__TRUSTED_PROXIES`; the app's is
  `UMBRAL_ISSUER=https://umbral-demo.example.com`. A test (`The_publish_manifest_has_no_Development_and_no_laptop_pair`)
  pins the same on the manifest writer.
- **Release dry run (local):** the `nuget` job's pack and check with `VERSION=0.12.0-rc.1` gave
  `CosmicChimps.Aspire.Hosting.Umbral.0.12.0-rc.1.nupkg`, nuspec version `0.12.0-rc.1`, baked tag
  `0.12.0-rc.1`; without a Version, `0.11.0` and `0.11.0`. The workflow names no NuGet key (the key
  is the login step's output). actionlint (with shellcheck) is clean on `release.yml` and
  `samples-aspire.yml`. The job itself runs only in a real release.
- `check-links.py`, the site's `npm run collect`, `changelog.sh check`: pass.

## The maintainer's one-time steps (not done by the implementer)

Before the first release that carries the `nuget` job:

1. **nuget.org trusted publishing policy**: on nuget.org, signed in as the account that will own the
   package, *Trusted Publishing* > add a policy for GitHub Actions: owner `Cosmic-Chimps`, repository
   `umbral`, workflow file `release.yml`, environment `nuget`.
2. **The `nuget` environment** in the repository's settings (*Settings* > *Environments* > *New
   environment* `nuget`). Like `production`, its deployment rule must allow the tag pattern `v*` and
   the branch `main` (a Run workflow's ref is `refs/heads/main`). Required reviewers are optional.
3. **The repository variable `NUGET_USER`**: the nuget.org account name of step 1 (*Settings* >
   *Secrets and variables* > *Actions* > *Variables*). Without it the job is skipped.
4. Optionally, **reserve the `CosmicChimps.` prefix** on nuget.org (a request to the NuGet team), so
   the packages show as verified.
