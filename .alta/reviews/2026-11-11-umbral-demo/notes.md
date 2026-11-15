# Review notes: the public Umbral demo (task fzzx)

The demo is a repository of its own, authored at `/Users/jjchiw/Documents/code4fun/umbral-demo`
(a local git repository; the maintainer creates `Cosmic-Chimps/umbral-demo` and pushes it).
The reviews live here because the demo repository has no `.alta/`.

## What is there

| Path (in the demo repository) | What |
| --- | --- |
| `apphost/AppHost.cs` | The AppHost: `aspire run` on a laptop, `aspire deploy` to Caravana as the app `umbral-demo`. Umbral via `AddUmbral` (package 0.12.0), the passkey connector and the app `demo` declared, the sample as a project, the site as a Dockerfile. Publish mode: public origin, trusted proxies, three domains, health checks, memory, a weekly volume backup. |
| `app/` | The ASP.NET Core sample copied from `samples/aspnetcore` at 4958e12, titles changed. |
| `site/` | The landing (`public/index.html`) and privacy (`public/privacy/index.html`) pages, `style.css` (the Umbra look, both schemes, fonts served locally), `sws.toml` (static-web-server: CSP, `/health`), `Dockerfile`. |
| `reset/` | The fallback reset: `umbral_demo_reset.py` (restore the golden backup through the API, follow it), systemd `.service` and `.timer` (04:00 UTC), `install.sh`, `reset.env.example`. |
| `README.md` | What it is, the pieces, the laptop run, the runbook (steps 0 to 10), afterwards, acceptance, layout. |
| `notes.md` | Operations: where the token lives on the host, how to run a reset by hand, what a reset drops, where each secret is. |
| `.github/workflows/deploy.yml`, `.github/dependabot.yml` | Deploy on push to `main` with the pinned Aspire CLI; Dependabot bumps the Umbral package (the image tag). |

## Decisions taken while building

- **Three domains, not two.** The task's "two services, two domains" predates its own bullet
  that adds the landing and privacy pages in "a small container in the same AppHost". A
  Caravana domain routes a host to one service, so the pages need a host of their own:
  `umbral-demo.lacalabaza.xyz` (pages), `auth.umbral-demo.lacalabaza.xyz` (Umbral),
  `app.umbral-demo.lacalabaza.xyz` (sample). Three A records. The Google consent screen's
  homepage and privacy policy are on the pages' host.
- **The fallback reset, not the scheduled restore.** Caravana's pin and scheduled restore are
  on its `main` (changelog.d/scheduled-restores.md, API 0.39.0) but not released (lacalabaza
  runs 0.20.0), and `CosmicChimps.Aspire.Hosting.Caravana` 13.6.4 has no call for it. The
  AppHost keeps the golden backup in retention with `keep: 100` on a weekly backup; the README
  says what to do once both ship, and a follow-up task in the Umbral repository tracks it.
- **The weekly backup runs five minutes after the Sunday reset**, so the copies hold the blank
  instance (the privacy page says what the five-minute window means).
- **Passkey sign-up open from the first deploy**, claim right after it (the README says so).
  The docs' "publish with invitations only, claim, then open" would cost a second deploy and
  protects against sign-ups before the claim, which is what the demo offers anyway; the
  setup token is only in the service's log.
- **The demo app's secret is the package's generated parameter** (`umbral-demo-client-secret`,
  `secret: true`, persisted in user secrets; a GitHub secret in CI), not a parameter of the
  AppHost's own: same thing, one fewer name.

## Verified

- `dotnet build` of the AppHost (with the Umbral package packed locally as 0.12.0, since
  0.12.0 is not released yet).
- `aspire publish` with dummy parameters: the Compose output has the three services, the
  `UMBRAL__DECLARATION` JSON intact (braces survive the compose step), the client secret as a
  placeholder, the data volume, `UMBRAL__PUBLIC_ORIGIN` https and `UMBRAL__TRUSTED_PROXIES`.
- The site image: `/`, `/privacy/` (and the `/privacy` redirect), `/health` 200, `/nope` 404,
  the fonts served; the CSP, Referrer-Policy and nosniff headers on every page.
- A local `aspire run` with the `dev` image built from this worktree (see the task's report).
- Captures of the pages in `captures/`, both colour schemes, desktop and phone widths.

## Reviews

- Documentation (`umbral-documentation`): `review-docs.md` here.
- Design (`umbral-designer`): `review-design.md` here.
- Code and security (`umbral`): `review-code.md` here.

## Moved into the repository (task dm1r, 2026-11-15)

The demo now lives in this repository under `demo/`; the separate repository is retired.
`/Users/jjchiw/Documents/code4fun/umbral-demo` was never pushed and nothing refers to it any
more: the maintainer deletes it. The files came from its head, `a44398a` (the task names
`dc2e695`; the three commits after it only change the privacy page's wording, all reviewed
clear), history squashed into this branch's commit.

| Was (demo repository) | Now |
| --- | --- |
| `apphost/`, `site/`, `reset/`, `README.md`, `notes.md`, `.config/`, `aspire.config.json` | the same under `demo/`, plus `demo/Directory.Build.props` (keeps the repository's F# build settings out, as `samples/` does) and `demo/.gitignore` (`*.env`) |
| `app/`, a copy of `samples/aspnetcore` | gone: the AppHost references `samples/aspnetcore` and sets `SAMPLE_TITLE=Umbral demo`, a new optional variable of the sample (default: its old title). The demo's two extra sentences (the front page's line, "The claims Umbral sent this app" on Private) are dropped. |
| package `CosmicChimps.Aspire.Hosting.Umbral` 0.12.0 | ProjectReference to `src/CosmicChimps.Aspire.Hosting.Umbral` (`IsAspireProjectResource="false"`): its default tag is the first `## X.Y.Z` of `CHANGELOG.md`. `Umbral:ImageTag` is still honoured in run mode only. |
| `.github/workflows/deploy.yml`, on push to `main`, GitHub secrets | `.github/workflows/demo.yml`: after a release, or Run workflow on `main`; the three values from Bella Baxter |
| `.github/dependabot.yml` (nuget, docker, actions) | `.github/dependabot.yml` of this repository, new: docker for `/demo/site` and github-actions; no NuGet entry |
| links to `Cosmic-Chimps/umbral-demo` (landing page, privacy page, the reset unit) | this repository: `tree/main/demo`, `tree/main/demo/apphost`, and the privacy page's question link is `issues/new?labels=demo` (the maintainer creates the label) |
| host checkout `/opt/umbral-demo` | `/opt/umbral`, `sudo sh /opt/umbral/demo/reset/install.sh`; the reset itself is unchanged |

### Deviations from the task's decisions, and why

- **The trigger is `workflow_run` on `release`, not `release: published`.** release.yml's
  `release` job publishes the GitHub release with `GITHUB_TOKEN`, and GitHub starts no workflow
  for an event `GITHUB_TOKEN` caused (only `workflow_dispatch` and `repository_dispatch` are
  exempt), so `release: published` would never fire. `demo.yml` runs when a release run ends in
  success, from this repository, started by a tag push or Run workflow; it then deploys the
  latest release if that release was published since the run was created (a prerelease is
  never latest; `created_at` rather than `run_started_at`, so a re-run of failed jobs after the
  release was published still deploys). release.yml is unchanged. The
  other way, a `gh workflow run demo.yml` step at the end of release.yml, would need
  `actions: write` there and a change to release.yml.
- **The environment `demo` needs the branch `main` only.** A `workflow_run` always runs with the
  default branch as its ref, so no run of `demo.yml` has a `v*` ref; leaving `v*` out allows
  less.
- **The version is passed explicitly.** A pushed tag's own commit has no section of its own in
  `CHANGELOG.md` (release.yml prepares it in its checkout and records it on `main` afterwards),
  so building the tag would take the previous version. The workflow sets the MSBuild property
  `Version` (an environment variable of the deploy step) to the release's version, or on a Run
  workflow to the first `## X.Y.Z` of `main`'s `CHANGELOG.md`, and refuses anything before
  0.12.0 (the declaration needs it; `CHANGELOG.md` says 0.11.0 today).
- **The Bella Baxter variables are the demo's own**: `DEMO_BELLA_BAXTER_PROJECT` (the opt-in,
  as `BELLA_BAXTER_PROJECT` is for the release) and `DEMO_BELLA_BAXTER_ENV` (default `demo`), so
  the demo's values sit in an environment whose trust domain requires `environment` = `demo`
  and the release job cannot read them, nor the demo job the release's.
- **`check-links.py` now scans `demo/`'s Markdown by default**, as it does `samples/`, so CI
  catches a broken link in the runbook (its relative links now point into the repository).

### Verified

- `dotnet build -warnaserror` of `demo/apphost`: 0 warnings.
- `aspire publish` with dummy parameters, against the old demo's publish at `a44398a` (package
  0.12.0 from the local cache): the only differences are the Umbral tag (`0.11.0`, from
  `CHANGELOG.md`) and the new `SAMPLE_TITLE`. With `Version=0.12.0` in the environment, as the
  workflow sets it, the tag is `0.12.0` and `SAMPLE_TITLE` is the only difference. The
  declaration JSON is intact, `UMBRAL_DEMO_CLIENT_SECRET` is a placeholder (empty in `.env`, the
  dummy value in neither file), the origin is https, the trusted proxy is set.
- `dotnet run` of the AppHost with the `dev` image on other ports: the three services healthy,
  the sample's front page titled "Umbral demo", `/private` redirects to Umbral with
  `client_id=demo`.
- The site image: `/`, `/privacy/`, `/health` 200, `/privacy` 308, `/nope` 404, font and CSS 200;
  CSP, Referrer-Policy and nosniff present; the new links in place.
- actionlint 1.7.12 (Docker, with shellcheck) on `demo.yml` and `release.yml`: clean; shellcheck
  on the script inside `bella run`: clean.
- `check-links.py`: 468 links in 42 files (now with `demo/`), all resolve. The docs site builds
  (33 pages) and nothing in it comes from `demo/`: `collect.mjs` takes only README.md, `docs/`,
  `samples/README.md` and `CHANGELOG.md`.
- Not run: `demo.yml` itself (it needs the Bella Baxter values, the `demo` environment and a
  release of 0.12.0 or later).
