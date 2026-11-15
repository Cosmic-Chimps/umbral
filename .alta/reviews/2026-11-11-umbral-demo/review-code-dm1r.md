# Code and security review, task dm1r (the demo moved into this repository)

Reviewer: the `umbral` session. Range `0d9cffb..48aea11` on `alta/sleek-jungfrau-zcn9`, read against
the task and `notes.md`, "Moved into the repository".

**Verdict: clear to merge from the code and security side, with two nits.** All three deviations
are accepted.

## The three deviations

**1. `workflow_run` on `release` instead of `release: published`: accepted, and right.**
- An event caused by `GITHUB_TOKEN` (here `gh release create` in `release.yml`) starts no workflow,
  so `release: published` would never fire.
- `workflow_run` is the usual pattern, and its usual risk (a run started by a fork's pull request
  executing with secrets) is closed here:
  - `release.yml` has no `pull_request` trigger;
  - the `target` job also requires `head_repository == github.repository` and an event of `push`
    or `workflow_dispatch`, and requires `conclusion == 'success'`;
  - it checks out a release tag, never the triggering run's `head_sha` or anything from a branch
    outside `main`.
- The "published since the run was created" test is sound. Both are ISO-8601 UTC strings from
  GitHub, so comparing them as strings is correct. `created_at` is the run's, which re-runs keep,
  and a prerelease is never `latest`.
- Better than adding `actions: write` to `release.yml` for a `gh workflow run`.

**2. The `demo` environment allows `main` only: accepted.** A `workflow_run` and the guarded
`workflow_dispatch` both run with `refs/heads/main`. See N1 for what the rule doesn't cover.

**3. `Version` passed explicitly, versions before 0.12.0 refused: accepted.**
- A pushed tag's own commit has no `## X.Y.Z` section yet, so the CHANGELOG fallback would name
  the previous release. Passing the version from the tag is the correct source.
- MSBuild sees the environment variable as `$(Version)` from the start of evaluation, which the
  hosting package's csproj reads. Your `aspire publish` check (`umbral:0.12.0`) confirms it.
- The variable is set on the Deploy step only. It also stamps the sample's assembly version,
  which is harmless.
- Refusing anything below 0.12.0 is right: the declaration needs it. Until 0.12.0 ships, a manual
  run fails with that message, which is correct.

## What holds

- **No secret in GitHub.**
  - The Caravana URL, deploy token and client secret come from Bella Baxter with the job's OIDC
    token, in their own environment, whose trust requires `environment == demo`.
  - The README says production keeps its own trust (`environment == production`), so neither job
    can read the other's secrets.
  - The token and the client secret are masked before use.
  - The secrets check (all three present, the client secret at least 32 characters) is kept from
    fzzx.
- **Permissions:**
  - `target`: `contents: read`.
  - `deploy`: `contents: read`, `packages: write`, `id-token: write`, in environment `demo`.
  - `persist-credentials: false`.
  - Actions pinned by SHA, including the Bella Baxter action.
  - Concurrency is one deploy at a time.
- **Opt-in:** without `DEMO_BELLA_BAXTER_PROJECT` nothing runs, so a fork stays quiet.
- **ProjectReferences:**
  - `samples/aspnetcore` directly, with no copy.
  - The hosting package from source, with `IsAspireProjectResource="false"`.
  - `demo/Directory.Build.props` stops the root props from applying to the AppHost, as
    `samples/` does.
- **The prune:** `prune-images.sh` defaults to `PACKAGE=umbral`. The demo's images are the
  separate packages `umbral-demo/app` and `umbral-demo/site`, and neither job touches the other's.
  `release.yml` is unchanged.
- **`SAMPLE_TITLE`** is HTML-encoded before it reaches `<title>` and `<h1>`.
- **Docs site:** `collect.mjs` walks `docs/` only, so `demo/` isn't published. `check-links.py`
  now covers `demo/*.md`.
- **The reset (`demo/reset/`)** is byte-for-byte what was cleared at `dc2e695`, with the paths
  updated.

## Nits

**N1. The `demo` environment's "main only" doesn't constrain the code a release run deploys.**
- The job's ref is `main`, but it checks out the release tag.
- A `v*` tag pushed onto a commit outside `main` would pass `release.yml` (its push path tags any
  commit) and then deploy with the demo's secrets.
- That's the same trust `release.yml` already grants a tag pusher for production, so it's a nit.
  But the "main only" rule reads as if it covered this.
- Fix: in `target`, after reading the tag, fetch it and `main`, and refuse unless
  `git merge-base --is-ancestor "$tag" origin/main`. That's about four lines, and then the rule
  says what it does.

**N2. `releases/latest` after a backport.**
- If an older line were ever released after a newer one (0.12.1 after 0.13.0), `latest` might
  move back, and the demo would deploy the older version.
- Umbral has no backport lines, so this is only a note. A comment in `target` naming the
  assumption is enough.

## For the maintainer

- **`.github/dependabot.yml` is new.** Its `github-actions` entry opens pull requests that bump
  the SHA pins in **every** workflow, `release.yml` included, not just `demo.yml`. That's good for
  task `tx3w`'s pins, but expect those pull requests. Decline the entry if you'd rather bump by
  hand.
- One-time setup:
  - the Bella Baxter `demo` environment, its trust, and its three secrets;
  - the variable `DEMO_BELLA_BAXTER_PROJECT`;
  - the GitHub `demo` environment, `main` only;
  - the `demo` issue label.
- The first deploy happens on its own with the 0.12.0 release, once the variable is set.

## Re-check

None needed for the nits. Take them or leave them as noted.
