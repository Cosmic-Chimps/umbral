# Umbral demo

The public demo of Umbral, a self-hosted OpenID Connect provider that signs people in without passwords: a throwaway instance at
`https://auth.umbral-demo.lacalabaza.xyz` with every connector, the ASP.NET Core sample at
`https://app.umbral-demo.lacalabaza.xyz` showing the claims it receives, and a landing page at
`https://umbral-demo.lacalabaza.xyz`. Every night at 04:00 UTC Umbral clears its users while it
keeps running: connectors and the app stay, users go.

It is one [.NET Aspire](https://aspire.dev) AppHost, deployed to
[Caravana](https://github.com/Cosmic-Chimps/caravana) with
`CosmicChimps.Aspire.Hosting.Caravana`. It runs Umbral with the hosting package
[`CosmicChimps.Aspire.Hosting.Umbral`](../src/CosmicChimps.Aspire.Hosting.Umbral/) and the
[ASP.NET Core sample](../samples/aspnetcore/), both from this repository by project reference,
so the demo follows each release of Umbral with no copy and no version bump. On a laptop,
`aspire run` starts the same three services on plain http.

This README is for whoever runs it. The runbook below is in order; the maintainer's steps
outside this repository (DNS, the Caravana app, Bella Baxter, the OAuth applications) are
written out.

## The pieces

| Service | What | Domain | Port | Health |
| --- | --- | --- | --- | --- |
| `umbral` | `ghcr.io/cosmic-chimps/umbral` at the last release (the first `## X.Y.Z` of [`CHANGELOG.md`](../CHANGELOG.md)) | `auth.umbral-demo.lacalabaza.xyz` | 8080 | `/healthz` |
| `app` | [`samples/aspnetcore`](../samples/aspnetcore/), titled "Umbral demo" (`SAMPLE_TITLE`), built by `aspire deploy` | `app.umbral-demo.lacalabaza.xyz` | 5000 on a laptop | `/public` |
| `site` | [`site/`](site/), the landing and privacy pages, static, behind static-web-server | `umbral-demo.lacalabaza.xyz` | 5100 on a laptop | `/health` |

All of it is in [`apphost/AppHost.cs`](apphost/AppHost.cs). The three domains are constants
at its top; nothing else names them except the links of the landing page.

**Production settings.** Publishing sets `UMBRAL__PUBLIC_ORIGIN` to the https origin and
`UMBRAL__TRUSTED_PROXIES=10.99.0.1` (Caravana's Traefik), and never the laptop pair
(`ASPNETCORE_ENVIRONMENT=Development` with `UMBRAL__INSECURE_ALLOW_HTTP`), so Umbral runs in
Production. There is no fake connector and no demo mode: this is the released image as
anyone runs it. The sample gets `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, so it sees https
behind the same Traefik.

**Secrets.** Every secret is an Aspire secret parameter and reaches Caravana as a secret
(never in the Compose file). There are two: the Caravana token the deploy uses, and the demo
app's client secret, `umbral-demo-client-secret`, which Umbral receives as
`UMBRAL__APP_SECRETS__demo` and the sample as `UMBRAL_CLIENT_SECRET`. The connectors' client
secrets are typed into Umbral's admin area and live in its `/data` volume, not in Caravana.
Each `aspire deploy` replaces the app's secrets with what the AppHost sends, so a value set by
hand in the Caravana console is lost at the next deploy: keep them all in the AppHost.

**What is declared.** The AppHost declares the passkey connector (sign-up open to anyone)
and the app `demo` (client ID `demo`, redirect URIs on the sample's domain). Umbral creates
both when it starts and marks them **Declared** in the admin area; they change here, by a
deploy, not there. GitHub, Google, Discord and Fediverse are set up once in the admin area
(step 6) and survive every reset: the nightly clearing deletes the users and what they hold,
nothing else.

## Run it on a laptop

Docker, the .NET 10 SDK, and the pinned Aspire CLI, from this folder (`demo/`):

```sh
dotnet tool restore
dotnet aspire run
```

Umbral starts at `http://localhost:8080` with the quick start's laptop pair (plain http on
localhost only), the sample at `http://localhost:5000`, the pages at `http://localhost:5100`.
Open the sample, click **Private**, and sign in with a passkey: the passkey connector and
the app are declared, so there is nothing to click in Umbral. The landing page's links point
at the public addresses, not at the laptop. `Umbral:Port`, `Sample:Port` and `Site:Port` in
the AppHost's user secrets change the ports (on macOS, AirPlay Receiver listens on 5000);
`Umbral:ImageTag` runs another image tag, on a laptop only.

The declaration needs Umbral 0.12.0 or later, which the default image is since that release. To
run an unreleased build, build this repository's image as `dev`
(`docker build -t ghcr.io/cosmic-chimps/umbral:dev .` at its root) and set `Umbral:ImageTag`
to `dev`.

## Deploy it: the runbook

In this order. Steps 1 to 3 and 6 are done outside this repository, by hand. The deploy
workflow is [`demo.yml`](../.github/workflows/demo.yml).

### 0. Before

- Umbral 0.13.0 or later is released, its image at that tag on GHCR: the nightly reset runs
  `Umbral clear-users`, which 0.13.0 brought. The demo runs the last release: the first
  `## X.Y.Z` of `CHANGELOG.md`, which the hosting package takes as its image tag, or the
  version of the release that started the workflow. The workflow refuses a version before
  0.13.0.
- A Caravana whose schedules can run in the running copy (feature 040, API 0.43.0 or later;
  not released as of 2026-11-16: name the version here when it ships) on the host, with a
  **backup destination** set (Settings, Backup destination): the AppHost declares a backup
  policy on `umbral` (step 9), and Caravana refuses one without a destination.
- For a deploy from your machine: Docker, the .NET 10 SDK, `dotnet tool restore` in `demo/`,
  and `docker login ghcr.io` with a token that can write packages.

### 1. DNS: three A records, DNS only

At the DNS provider of `lacalabaza.xyz`, three A records to the Caravana host's address:

| Name | For |
| --- | --- |
| `umbral-demo` | the landing page |
| `auth.umbral-demo` | Umbral |
| `app.umbral-demo` | the sample |

**DNS only, not proxied**: Caravana's Traefik gets each host its Let's Encrypt certificate
with an HTTP challenge, which must reach Traefik itself. Other names are fine: change the
three constants at the top of `AppHost.cs`, the links in `site/public/index.html`, and the
callback URLs you register in step 6.

### 2. Caravana: the app and a deploy token

In the Caravana console: **New app**, name `umbral-demo`, project `default`, environment
`production`, **Create without deploying**. Then a deploy token for it: Settings, API tokens,
"Only for one app" (project `default`, app `umbral-demo`), or "Create a deploy token for this
app" on the app's page. It starts with `cvat_` and can only deploy and read that app.

The app's id is `umbral-demo`; the AppHost names it (`AppName`), so a deploy from any
machine updates this same app.

### 3. The deploy's settings

For a deploy from your laptop, in the AppHost's user secrets:

```sh
cd apphost
dotnet user-secrets set Parameters:caravana-url https://caravana.example.com
dotnet user-secrets set Parameters:caravana-token cvat_...
dotnet user-secrets set Parameters:registry-endpoint ghcr.io
dotnet user-secrets set Parameters:registry-repository cosmic-chimps/umbral-demo
```

For the deploy from GitHub Actions ([`demo.yml`](../.github/workflows/demo.yml): after each
release, and by hand with Run workflow on `main`), no secret is stored in GitHub. The job reads
three values from Bella Baxter with its OIDC token, as the release's deploy does
([Connect the repository](../docs/run/caravana.md#connect-the-repository) has the details of
each screen):

- **In Bella Baxter**, an environment of the demo's own, for example `demo` in the project the
  release already uses (or a project of its own), with three secrets:

  | Secret | Value |
  | --- | --- |
  | `CARAVANA_URL` | The console's address, for example `https://caravana.example.com`. |
  | `CARAVANA_TOKEN` | The deploy token of step 2 (`cvat_`, app `umbral-demo` only). |
  | `UMBRAL_DEMO_CLIENT_SECRET` | The demo app's client secret, 48 random characters, for example `openssl rand -base64 48 \| tr -d '/+=' \| cut -c1-48`. |

  On that environment, a trust domain from the GitHub template: `repository` equals
  `Cosmic-Chimps/umbral` (or `repository_id`), and `environment` equals `demo`; role
  Consumer. The release's environment keeps its own trust domain (`environment` equals
  `production`), so neither job can read the other's secrets.
- **In GitHub**, Settings, Secrets and variables, Actions, Variables: `DEMO_BELLA_BAXTER_PROJECT`
  (the project; without it the workflow does nothing) and, when the environment is not named
  `demo`, `DEMO_BELLA_BAXTER_ENV`. `BELLA_BAXTER_TENANT` and `BELLA_OIDC_AUDIENCE` are the
  release's, shared.
- **In GitHub**, Settings, Environments: the environment `demo`, under Deployment branches and
  tags, Selected branches and tags, the branch `main` only. Both ways in run on `main` (a run
  after a release is a `workflow_run`, whose ref is the default branch), so no tag pattern is
  needed.

The workflow refuses to deploy when a secret is missing or the client secret is shorter than
32 characters. The client secret is also what your laptop's user secrets hold after a first
`aspire deploy` from there (`Parameters:umbral-demo-client-secret`, generated and persisted):
copy that value into Bella Baxter, so both ways of deploying send the same one. A deploy that
sends a different secret rotates it in Umbral and in the sample together: it works, but it
restarts the sample and is recorded as a rotation in the security log.

The images go to `ghcr.io/cosmic-chimps/umbral-demo/app` and `.../site`, packages of their own:
the release's prune works on the `umbral` package alone, so it never touches them, and
nothing prunes them. New GHCR packages are private: make both public (the images hold no
secret), or give Caravana a registry login under Settings, Registries.

### 4. The first deploy

From `demo/` on your machine, or with **Run workflow** on `demo` (branch `main`):

```sh
dotnet aspire deploy
```

It builds and pushes the two images, sends the app to Caravana and waits until the three
services are healthy. Then `https://umbral-demo.lacalabaza.xyz` shows the landing page,
`https://auth.umbral-demo.lacalabaza.xyz/healthz` answers, and
`https://app.umbral-demo.lacalabaza.xyz/private` sends you to Umbral's sign-in page, which
offers **Passkey** only until step 6.

**Claim it now, before you do anything else.** Umbral applied the declaration (passkey
sign-up open) before anyone claimed it, which is what the demo wants, but the admin area is
unclaimed until you take it. The setup token is in the `umbral` service's log in Caravana
(the app's Logs tab, the first lines). Open `https://auth.umbral-demo.lacalabaza.xyz/admin/setup`,
paste it, and choose a long operator password from a password manager.

### 5. The operator side: a passkey, and passwords off

Signed in to the admin area, open **Credential** in the side rail: **Add a passkey** (your
phone or your laptop's authenticator), then **Turn password sign-in off**. From then on
`/admin/signin` takes a passkey and nothing else, as the acceptance test asks. The password
is kept, so that `reset-credential` (run through Caravana's terminal on the service) is the
way back in if every device is lost. Details: [Passkeys for the admin area](https://cosmic-chimps.github.io/umbral/run/operator-passkeys/).

### 6. The connectors

Each in Umbral's admin area, **Connectors**, and at the provider. Umbral shows the exact
**Redirect URI to register with the provider** on each new connector's page, with a Copy
button: copy it from there, never type it. Leave **Shared with every app** ticked on each,
as it is by default, so the declared app (which offers every shared connector) gets it.

| Connector | At the provider | Walkthrough |
| --- | --- | --- |
| GitHub | An OAuth App (Settings, Developer settings, OAuth Apps) with the callback URL from Umbral; copy its client ID and generate a client secret. | [GitHub](https://cosmic-chimps.github.io/umbral/connectors/github/) |
| Google | A Google Cloud project with an OAuth client (Web application) and the redirect URI from Umbral. The consent screen must be **published** (Audience, Publish app): in Testing only listed accounts can sign in. `openid email profile` are non-sensitive scopes, so publishing needs no verification, but it does need a homepage and a privacy policy on the demo's domain: `https://umbral-demo.lacalabaza.xyz/` and `https://umbral-demo.lacalabaza.xyz/privacy/`, with `lacalabaza.xyz` as an authorised domain under Branding. | [Google](https://cosmic-chimps.github.io/umbral/connectors/google/) |
| Discord | An application in the Developer Portal, OAuth2, with the redirect from Umbral. | [Discord](https://cosmic-chimps.github.io/umbral/connectors/discord/) |
| Fediverse | Nothing to create. Who can sign in: **Any server**. Umbral registers itself on each Mastodon or GoToSocial server at its first sign-in. | [Fediverse](https://cosmic-chimps.github.io/umbral/connectors/fediverse/) |
| Passkey | Nothing to do: it is declared by the AppHost with **Anyone can sign up** and shows as Declared. | [Passkeys](https://cosmic-chimps.github.io/umbral/connectors/passkey/) |

The app **Umbral demo** is on **Apps**, marked Declared. Its name, type, redirect URIs and
sign-in methods come from the AppHost; its branding (a logo, a colour) can be set there.

### 7. Try it, then clean up

Through the sample's **Private** page, sign in once through each of the five ways, the
passkey on a phone, and check that each sign-in ends on the page listing `sub`, `name`,
`idp`, and `email` with `email_verified` where the provider shares an address. Cancel one at
the provider: you land back on the sign-in page. Sign out: back to the sample's front page.

Then clean up: set the nightly schedule (step 8) and run it once with **Run now**. It deletes
those users and empties the security log of your test sign-ins, with their addresses, in one
step, and leaves exactly the state every night returns to. The Fediverse registrations are kept
with the connector and do no harm.

### 8. The reset

Caravana runs `/app/Umbral clear-users --yes --security-log` in Umbral's running copy every
night at 04:00 UTC, as the landing page says, while Umbral keeps serving. A run that fails is on
Caravana's needs-you list and in its notifications.

Set it once, in the Caravana console, **Schedules**, **New schedule**: app `umbral-demo`,
service `umbral`, name `clear-users`, the command above, when `0 4 * * *`, **Run in: The
running copy**, **Stop after: 1 min** (the clearing takes about a second). A schedule in the
running copy always skips a slot while its last run is still going. The schedule is not part of the
deploy: an `aspire deploy` leaves it alone, and the hosting package has no call to declare
one, so the console is where it lives. [`notes.md`](notes.md) says what the clearing keeps and
how to run it by hand.

### 9. Back up now

On the app's **Backups** tab in Caravana, service `umbral`, **Back up now**, right after the
clearing of step 7, while the instance is empty. It stops Umbral for a few seconds, archives
`/data` and starts it again. This backup is for disaster recovery only, the way back if the
volume is lost: claimed, operator passkey, password sign-in off, the five connectors, the
declared app, no user. The policy's schedule is `never`, so Caravana takes no copy on its own,
and it keeps the last three taken by hand.

Retake it after a change worth keeping (another connector, a new logo), and only on an empty
instance, so that no backup ever holds a visitor: **Run now** on `clear-users` first, then
**Back up now**, in that order, every time, or take it just after 04:00 UTC. After an Umbral
upgrade an old backup still works: Umbral migrates its database forward at each start.

### 10. The line in the README

Once the demo is live, and not before, two edits, in one pull request:

- One line in the opening of [`README.md`](../README.md), after the first paragraph. The docs site's start page
  is generated from that README's preamble, so the one edit does both:

  ```markdown
  **Try it:** <https://umbral-demo.lacalabaza.xyz>, a public instance that is wiped every night at 04:00 UTC.
  ```

- [`PRODUCT.md`](../PRODUCT.md) lists "no public demo instance" among the absences not to be filled in
  ("Evidence on Hand"), and the design and writing work is judged against it. Drop it, or
  replace it with "a public demo instance at <https://umbral-demo.lacalabaza.xyz>, wiped every
  night; surfaces may link it". The wording is the maintainer's, as PRODUCT.md's commitments
  are.

### Afterwards

- **An Umbral release** deploys the demo: `demo.yml` runs once the release workflow has
  succeeded, from the release's tag, with its image. A failed demo deploy leaves the release
  green; run `demo` by hand once fixed.
- **A change to the demo's files or the sample** merged to `main` reaches the demo at the next
  release, or at once with **Run workflow** on `demo`. By hand, the AppHost, the hosting package
  and the sample are `main`'s; Umbral's image stays the last release.
  A deploy that changes only the sample or the site restarts only that service: Nomad replaces
  the task groups whose spec changed.
- **A change to a connector** is made in the admin area; the nightly clearing keeps it. Take a
  new backup when it is worth keeping, on an empty instance only (step 9).
- **The schedule is not in the repository.** After the app is deleted and created again in
  Caravana, or on any doubt, check that **Schedules** lists `clear-users` on `umbral-demo`
  with Run in: the running copy (step 8); without it the privacy page is untrue and nothing
  says so.
- **A change to the app or the passkey connector** is made in `AppHost.cs` and deployed.
- **`/admin` with a password** must stay refused: the clearing keeps the operator credential
  and its switch as they are. A `reset-credential` turns password sign-in back on; turn it off
  again.

## Acceptance

- On the deployed demo, a visitor signs in through each of the five connectors and sees the
  claims; the passkey path works on a phone; a refused or cancelled sign-in lands back on the
  sign-in page.
- Caravana's **Schedules** lists `clear-users` on `umbral-demo`, in the running copy of
  `umbral`, at 04:00 UTC.
- After the 04:00 run, **Users** is empty, every connector and the app are still there, the
  security log holds one `users_cleared` row, Umbral never stopped, no backup was taken, and a
  new sign-in works.
- `/admin` cannot be reached with a password; no secret is in this repository or its logs.
- The landing and privacy pages load nothing from another host (the site's
  Content-Security-Policy makes the browser refuse it).
- This repository's README links the live URL.

## Layout

| Path | What |
| --- | --- |
| `apphost/` | The AppHost: `aspire run` on a laptop, `aspire deploy` to Caravana. It references `../samples/aspnetcore` and `../src/CosmicChimps.Aspire.Hosting.Umbral`. |
| `site/` | The landing and privacy pages, their style and fonts, the web server's config and Dockerfile. |
| `notes.md` | Operations notes: where the nightly reset is set, how to run it by hand, what it keeps. |
| `.config/`, `aspire.config.json` | The pinned Aspire CLI, and the AppHost it runs. |
| `../.github/workflows/demo.yml` | The deploy workflow. `../.github/dependabot.yml` bumps the site's base image. |

The docs site does not publish this folder: `site/scripts/collect.mjs` at the repository's root
takes only the files it lists.

## License

MIT, as the rest of the repository. The fonts under `site/public/fonts/` are under the SIL Open Font License
(the `OFL-*.txt` files beside them).
