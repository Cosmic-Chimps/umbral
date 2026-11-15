# Documentation review: the demo moved into the repository (task dm1r)

Branch `alta/sleek-jungfrau-zcn9`, commit 48aea11. Read as a diff of the demo repository at a44398a
(`README.md`, `notes.md`) against `demo/README.md` and `demo/notes.md`, and checked against
`.github/workflows/demo.yml`, `.github/dependabot.yml`, `samples/aspnetcore` and
`docs/run/caravana.md`. Reviewed 2026-11-15 by the documentation reviewer.

## Verdict

**Clear, no blocking items, three nits.** The changed steps say what the workflow does, the move
leaves no link to a repository that will not exist, and the secrets story is better than before:
nothing is stored in GitHub. No re-check needed.

## What was checked

- **The trigger.** "After each release, and by hand with Run workflow on `main`" matches
  `demo.yml`. The `workflow_run` on `release` runs only when that run succeeded, came from this
  repository, and was a push or a `workflow_dispatch` (lines 47-51). The job uses the environment
  `demo`, and both triggers run with the ref `main`, which is why step 3's "branch `main` only" with
  no tag pattern is right.
- **The version.** "The workflow refuses a version before 0.12.0" (line 140). The image tag is the
  first `## X.Y.Z` of `CHANGELOG.md`, which the hosting package takes from source (task a5pr). Right.
- **Step 3, Bella Baxter.** `DEMO_BELLA_BAXTER_PROJECT` (nothing runs without it, so a fork is
  quiet), `DEMO_BELLA_BAXTER_ENV` defaulting to `demo` (line 110), and the shared
  `BELLA_BAXTER_TENANT` and `BELLA_OIDC_AUDIENCE`. The trust domain (`environment` equals `demo`,
  role Consumer) is the same shape as the release's in
  [Connect the repository](../../../docs/run/caravana.md#connect-the-repository). With
  `production` for the release, neither job can read the other's secrets, as the README says. The
  32-character check on the client secret matches the declaration's own rule (spec 019, D7). Right.
- **The images paragraph**: the release's prune works on the `umbral` package only. Right.
- **Step 9 and notes.md**: the host checkout is `/opt/umbral`, and the installer path is
  `demo/reset/install.sh`. Right.
- **"Afterwards"**: a release deploys the demo, a failed demo deploy leaves the release green, and
  there is no Dependabot bump for the package (`dependabot.yml` keeps only `/demo/site`'s base
  image). Right.
- **The pieces table and `SAMPLE_TITLE`**: the sample reads it, with the stated default
  (`samples/aspnetcore/Program.cs:17`, its README row). Right.
- **Links left behind by the move**: none to `Cosmic-Chimps/umbral-demo` remains in `demo/`. The
  landing page points at `umbral/tree/main/demo`, and the privacy page's question link opens an
  issue on `Cosmic-Chimps/umbral`. Right.
- `check-links.py`: 468 links in 42 files, all resolve, with `demo/` in its scan.

## Nits

1. **Before 0.12.0, say what "ignores it" costs.** "The default image is 0.11.0, which ignores it"
   leaves out the consequence: no app and no passkey connector are created, so the sample cannot
   sign in. Add "…which ignores it, so the sample cannot sign in".
2. **What Run workflow deploys.** "A change to the demo's files or the sample merged to `main`
   reaches the demo … at once with Run workflow. Either way Umbral stays the last release." By
   hand, the AppHost, the hosting package and the sample are `main`'s, and only the image is the last
   release. Say so: "…the AppHost, the hosting package and the sample are `main`'s; Umbral's image
   stays the last release."
3. **The privacy page's `?labels=demo`.** GitHub applies a label from the URL only for people who can
   triage, so most visitors' issues arrive unlabelled. That is harmless, but create the `demo` label,
   so that a maintainer opening the link gets it.
