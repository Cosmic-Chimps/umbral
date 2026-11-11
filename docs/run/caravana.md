# Deploying on Caravana

[Caravana](https://github.com/Cosmic-Chimps/caravana) is a self-hosted platform
for running apps (a PaaS) on your own servers, built on
[Nomad](https://www.nomadproject.io/). This page is for you if you already run
it; if you do not, [Run it for real, behind HTTPS](https.md) is the way to
deploy Umbral. Caravana's Traefik already terminates TLS and routes by domain,
so on a Caravana host you use neither Caddy nor the repository's compose file. You create the app in Caravana by name and the release
workflow does the rest: pushing a tag builds the image, defines the app the
first time, and deploys that image through Caravana's API.

## Create the app in Caravana

In the console choose **New app**, enter the name `umbral`, and pick **Create
without deploying**. A name is enough: the app has no services yet, and the first
release gives it its definition.

| Setting | Value |
| --- | --- |
| Name | `umbral` (or any name, with the Bella Baxter secret `CARAVANA_APP_ID` set to it) |

The first release defines the app from
[`deploy/caravana-app.json`](../../deploy/caravana-app.json), so you do not type the
settings. It sets:

- the image: `ghcr.io/<owner>/umbral` (`<owner>` is the GitHub user or
  organization that owns the repository, in lowercase), tag `latest`;
- container port `8080`, with an HTTP health check on `/healthz`;
- one volume, `umbral-data`, mounted at `/data`;
- your domain (the Bella Baxter secret `CARAVANA_DOMAIN`, for example
  `auth.example.com`), routed to that port;
- `UMBRAL__PUBLIC_ORIGIN=https://<your domain>`;
- `UMBRAL__TRUSTED_PROXIES=10.99.0.1`.

To change what the first definition contains (more memory, another variable),
edit that file before the first release. It is only used when the app has no
services yet: once the app is defined, the workflow ignores the file (and says
so in the log), so change the app in the Caravana console, and later releases
keep your changes. A release replaces only the image. The default memory (256 MB) is enough: Umbral
idles under 150 MB (under 160 MB with an external secret store).

`UMBRAL__PUBLIC_ORIGIN` must match the domain exactly, scheme included;
[The public origin must match exactly](https.md#the-public-origin-must-match-exactly)
explains why.

`UMBRAL__TRUSTED_PROXIES=10.99.0.1` tells Umbral to believe `X-Forwarded-*`
only from Traefik. On a host set up by Caravana's installer, Traefik reaches
Umbral from the `caravana0` address, `10.99.0.1`, on every host. Other apps on
the same host can reach Umbral's port directly (Caravana does not isolate apps
from each other yet). Without this setting, they could claim any client address
and get past the sign-in limits. Their direct requests arrive from their own
container address or from the bridge gateway (`172.26.64.1`), never from
`10.99.0.1`, so do not trust the gateway.

Every release deploys the exact image digest the workflow just built
(`ghcr.io/<owner>/umbral@sha256:...`), so each rollout is a specific image and
Caravana can roll it back. The `:latest` and version tags are still pushed, for
people who pull the image by hand.

**The GHCR package.** A new GHCR package is usually private. You have
two choices:

- **Make it public** (the simpler choice). On GitHub, open the package (your
  profile or organization, Packages, `umbral`), then Package settings, Change
  visibility. Caravana then pulls anonymously. The image holds no secrets: every
  secret lives in `/data`.
- **Keep it private** and give Caravana a registry login: Settings, Registries,
  with a GitHub token that has `read:packages`. The trade-off is that Caravana
  puts the registry token in the app's container environment, so Umbral's
  container can read it.

If the first rollout cannot pull the image, the workflow fails with the error
Caravana reports; fix the package visibility or the registry login and run the
release again.

## Connect the repository

The release workflow reads the Caravana settings and token from
Bella Baxter and signs in to it with the job's GitHub
OIDC token (workload identity). No secret is stored in GitHub, so there is
nothing in the repository to leak or rotate.

**In Bella Baxter**, create a project and an environment (the examples use the
project `umbral-workflow` and the environment `production`) holding these
secrets:

| Secret | Value |
| --- | --- |
| `CARAVANA_URL` | The console domain, for example `https://caravana.example.com`, with no trailing slash. |
| `CARAVANA_DOMAIN` | The domain Umbral is served on, for example `auth.example.com`, without `https://`. Needed for the first release, which defines the app with it; ignored afterwards. |
| `CARAVANA_TOKEN` | A deploy token for the app (Caravana 0.8.1 and later): Settings, API tokens, "Only for one app" (project `default`, app `umbral`), or "Create a deploy token for this app" on the app's Configure page. It starts with `cvat_` and can only deploy, define, promote and read that app; anything else is refused with `token_scope`. On an older Caravana (0.7.0 to 0.8.0), use a full API token instead: Settings, API tokens, or `sudo caravana admin create-token --name umbral-ci` on the host. |
| `CARAVANA_APP_ID` | Optional. The app's name, if it is not `umbral`. |
| `CARAVANA_BACKUP_SCHEDULE` | Optional. A five-field cron line in UTC, for example `0 3 * * *`. The first release then defines the app with a volume backup (see [Back up `/data` with Caravana](#back-up-data-with-caravana)). Ignored once the app is defined. |

`CARAVANA_WEBHOOK_TOKEN`, which earlier versions of the workflow read, is no
longer used: delete it from Bella Baxter.

Then let GitHub Actions read that environment. On the environment, open
Identity, Add trust domain, and pick the GitHub template:

- Issuer: `https://token.actions.githubusercontent.com`.
- Claim rules (all must match): `repository` equals `<owner>/umbral`, and
  `environment` equals `production`. Once the repository exists, prefer
  `repository_id` equals its numeric id: it survives a rename or a transfer.
- Role: Consumer (read only). The default token lifetime of 15 minutes is enough.
- Accepted audience: the console suggests `bella-baxter/<environmentId>`. If you
  keep it, set the repository variable `BELLA_OIDC_AUDIENCE` (below) to exactly
  that. If you leave it empty, the audience is `bella-baxter`, and the variable
  stays unset.

**In GitHub**, under Settings, Secrets and variables, Actions, Variables:

| Variable | Value |
| --- | --- |
| `BELLA_BAXTER_TENANT` | Your Bella Baxter tenant code, for example `code4fun-17d1c3`. |
| `BELLA_BAXTER_PROJECT` | The project, for example `umbral-workflow`. Without it the release builds the image and skips the deploy. |
| `BELLA_BAXTER_ENV` | The environment. Optional: `production` if unset. |
| `BELLA_OIDC_AUDIENCE` | Optional. The accepted audience, only if the trust domain uses one other than `bella-baxter`. |

No GitHub secrets are needed.

Also create an **environment** named `production` (Settings, Environments). The
deploy job runs in it, and two things depend on it:

- Bella Baxter's trust domain requires `environment` to equal `production`, so
  only jobs running in that environment can read the secrets.
- Under Deployment branches and tags, choose Selected branches and tags and add
  the tag pattern `v*` and the branch `main`. Bella Baxter cannot limit a token
  to certain tags, so these rules are what keep another branch or an unrelated
  tag from deploying. Both are needed: a pushed tag runs on `v*`, but a release
  started with the **Run workflow** button (below) runs on `refs/heads/main`, so
  without the `main` rule the deploy job is refused after the tag is pushed.
  Bella Baxter's trust domain needs no change: it matches the repository and the
  environment's name, not the ref.

You can also add required reviewers there to approve each rollout by hand.

The first time, go in this order: create the app in Caravana (above), put the
secrets in Bella Baxter (`CARAVANA_URL`, `CARAVANA_DOMAIN`, `CARAVANA_TOKEN`;
and, if you want backups from the start, set a backup destination in Caravana
and `CARAVANA_BACKUP_SCHEDULE`), add the trust domain, set the GitHub variables
and the `production` environment, then run a release. The first release defines the
app from `deploy/caravana-app.json`. Every later release deploys the new image
and keeps the changes you made in the console.

## Release

While you work, add what changed for the people who run Umbral as one file in
[`changelog.d/`](../../changelog.d/README.md) (its README says how: what changed in
plain words, under Added, Changed, Fixed, Security or Upgrade). Nobody edits
[`CHANGELOG.md`](../../CHANGELOG.md) for a change: it holds only released sections. To
release, use the button: Actions, **release**, **Run workflow**, branch `main`,
version `1.2.3` (no `v`). From a terminal, the same thing is
`gh workflow run release.yml --ref main -f version=1.2.3`.

The workflow ([`release.yml`](../../.github/workflows/release.yml)) then:

1. runs the same tests as CI on the tip of `main`, so a red test never leaves a
   tag;
2. gathers the files in `changelog.d/` into `## 1.2.3 - <today>` in
   `CHANGELOG.md` and deletes them
   ([`changelog.sh`](../../.github/scripts/changelog.sh)), commits that to `main` as
   `chore(release): 1.2.3`, tags that commit `v1.2.3` and pushes both in one
   atomic push, so the tag always contains its own changelog section. It stops
   here, before anything is built, if `changelog.d/` has no notes (no empty
   releases) or a malformed one, if the tag already exists, or if `main` moved
   while the tests ran (run it again);
3. builds `linux/amd64` and `linux/arm64` and pushes `ghcr.io/<owner>/umbral:1.2.3`
   and `:latest`;
4. deploys the exact image digest it just built through Caravana's API, with
   the [`Cosmic-Chimps/caravana-action`](https://github.com/Cosmic-Chimps/caravana-action)
   action, and waits, up to 10 minutes, until Caravana reports the rollout
   healthy. If the app has no services yet (the first release), it defines the
   app from `deploy/caravana-app.json` with that image; afterwards the file is
   ignored and only the image is replaced. It fails if the rollout fails or is
   reverted, prints the deployment's warnings and writes them to the job
   summary. Caravana 0.8.1 and later also shows the commit, branch and run that
   deployed ("from CI ... run #N") in the app's history;
5. publishes the GitHub release, with that changelog section as its notes
   (and GitHub's list of commits after it). It does not wait for the rollout,
   so a failed rollout still has a published image and a release;
6. deletes image versions older than the last 10.

Pushing a tag still works (`git tag v1.2.3 && git push origin v1.2.3`): the
workflow then prepares the changelog in its own checkout, builds and publishes
the tag, and afterwards commits the gathered section, and the deletion of the
files it came from, to `main` as `docs(changelog): release 1.2.3`, but only if
`main` is still the tagged commit. If `main` has moved on, it warns instead,
and you run `.github/scripts/changelog.sh prepare 1.2.3` on `main` by hand,
with any files added to `changelog.d/` since the tag set aside and put back
afterwards. The tag never moves. The button is better: it commits the changelog
first, so the tag never points at a `CHANGELOG.md` without its section.

Re-running the failed jobs of a release is safe: the same image is deployed
again as a new deployment, and nothing else changes. Re-running all jobs of a
button run is refused, because the tag exists by then; start a new run (with a
new version) or, if no release was published, delete the tag first.

Things to know:

- **A deploy briefly stops the app.** `/data` is a volume that only one copy can
  use, so Caravana stops the old copy before it starts the new one: a few seconds
  of downtime, and a rollout takes 15 to 30 seconds. Pick a quiet moment, or put
  a required reviewer on the `production` environment.
- **Prereleases do not deploy.** A version with a hyphen, such as `1.2.0-rc.1`,
  publishes its image but does not move `latest` and does not deploy. The
  button only tags it, on the tested commit of `main`. Pull
  it by tag to try it. It gets a GitHub prerelease whose notes list what is
  waiting in `changelog.d/`, and it leaves `CHANGELOG.md` and `changelog.d/`
  alone, so the real release can still claim those notes.
- **A branch's note is its own file, so a merge cannot misplace it.** Before,
  every branch wrote under a shared `## Unreleased` heading, and one merged
  after a release put its entries inside the released section. Now CI fails a
  push that changes a released section or brings `## Unreleased` back
  (`changelog.sh released` and `check`). Nobody edits a released section.
- **Re-running a release is safe for the notes too.** If the GitHub release
  already exists, the run replaces its notes with the changelog section instead
  of failing.
- **Only the last 10 versions are kept**, so Caravana's "Redeploy this version"
  reaches 10 releases back; for an older one, whose image was deleted, Caravana
  refuses it. The pruning
  is careful with multi-arch images: it deletes an old version together with its
  own per-architecture parts and nothing else
  ([`prune-images.sh`](../../.github/scripts/prune-images.sh); run it with `DRY_RUN=1`
  to see what it would delete). It does not run when the deploy failed, so the
  image a failed rollout reverts to is never deleted under it.

## Back up `/data` with Caravana

Caravana can back up the `umbral-data` volume to the installation's S3-compatible
backup destination. The backup is part of the app's definition: the console cannot
turn it on, and a backup policy is refused while no destination is set.

1. Set the backup destination first: Caravana, Settings, Backup destination.
   Without it Caravana refuses the definition and names the field.
2. Then either set the Bella Baxter secret `CARAVANA_BACKUP_SCHEDULE` (for
   example `0 3 * * *`) **before the first release**, which defines the app with
   `"backup": {"engine": "volume", "schedule": "<your schedule>", "keep": 7}`; or,
   for an app that is already deployed, send its definition once more with a
   `backup` object through the API, with an API token. The stored app is the
   `app` field of what `GET /v1/apps/umbral` answers; add the `backup` to the
   service and post it back (values of secrets stay as they are):

   ```bash
   BASE=https://caravana.example.com     # your CARAVANA_URL
   curl -s -H "Authorization: Bearer $TOKEN" "$BASE/v1/apps/umbral" |
     jq '.app | .services[0].backup = {engine: "volume", schedule: "0 3 * * *", keep: 7}' |
     curl -s -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
       --data @- "$BASE/v1/apps/umbral/deployments"
   ```

   The schedule is a five-field cron line in UTC; leave it out for `0 3 * * *`.
   `keep` is how many backups stay (7 if left out).

The backup **stops Umbral for a few seconds** while `/data` is archived, so the
SQLite file and the keys are copied consistently. Schedule it for a quiet hour.
Restoring is a Caravana operation too; to copy `/data` by hand instead, see
[Your data: back it up](backup.md), with Umbral stopped.
