# Operations notes

For whoever keeps the demo running. The [README](README.md) has the runbook; this is what
lives where once it is up.

## In Caravana

- App `umbral-demo` (project `default`, environment `production`), services `umbral`, `app`,
  `site`. Volume `umbral-demo-data` on `umbral`, mounted at `/data`.
- **The reset** is the schedule `clear-users` of the app (**Schedules** in the console): the
  command `/app/Umbral clear-users --yes --security-log`, on `0 4 * * *` (UTC), in the
  running copy of `umbral`, with the usual limit of one minute. It was created once in the
  console (README, step 9) and no deploy touches it: a schedule is not part of the app's
  Compose file or overlay. It skips a slot while its last run is still going.
- **Run it by hand**: **Run now** on the schedule, or **Run a command** on the app's page,
  service `umbral`, the same command. Neither keeps the output; the run's page shows the exit
  code (0 cleared, 1 failed, 2 the `--yes` was missing).
- **Backups**: the `umbral` service's policy has the schedule `never` and keeps 3, so the only
  backups are the ones taken by hand with **Back up now** (README, step 8), of the instance
  while it held no visitor. The last three stay in the destination.
- Logs that the clearing does not touch: Umbral's and the sample's console output, kept by Nomad
  on the host by size (10 files of 10 MB per stream per copy, no time limit) and removed with the
  copy's allocation; Traefik writes no access log on a Caravana host. Umbral writes no account
  data and no client address to its console; the privacy page says so, and that holds only while
  it stays true.

**When a run fails.** It is on Caravana's needs-you list and in its notifications, with the
exit code or why it could not start (no healthy copy, Nomad unreachable, the limit). Umbral
stays on its current data, with that day's users, until the next slot or a run by hand. A slot
while the app is stopped is recorded as skipped.

## What the clearing does, and does not

- Deletes every user, their linked provider accounts, their passkeys and passkey invitations,
  every authorization and token Umbral issued, and the records of removed links, in one
  transaction, beside the live process: Umbral keeps serving. With `--security-log` the
  security log goes too, and one event is then recorded, `users_cleared`, with the number of
  users deleted. Details: [Start over: clear every user](https://cosmic-chimps.github.io/umbral/run/clear-users/).
- Keeps the operator credential, its passkeys and the password-sign-in switch; the connectors
  with their secrets and the Fediverse servers Umbral registered with (so a server is not
  registered twice); the apps; what the declaration owns; the settings and the keys.
- Does not touch the sample or the site, nor the declared app and passkey connector, which the
  AppHost applies again at every start anyway.

## The pages

`site/public/style.css` carries the product's dark and light tables (the repository's
`src/Umbral/Assets/tailwind.config.js`) and the two text faces, served from the site. The six
code tokens on the landing page are set in the system mono face, not the product's Spline Sans
Mono: one font file fewer for six words, a choice the design review recorded.

## Secrets, and where each one is

| Secret | Lives in | Not in |
| --- | --- | --- |
| The deploy token (`cvat_`, one app) | Bella Baxter secret `CARAVANA_TOKEN` of the demo's environment; the maintainer's laptop user secrets | GitHub, the repository |
| The demo app's client secret | Bella Baxter secret `UMBRAL_DEMO_CLIENT_SECRET`; laptop user secrets; Caravana's secrets of the app (sent by each deploy) | GitHub, the repository, the Compose file |
| The connectors' client secrets | Umbral's `/data` (encrypted), hence the backups in the destination | Caravana, GitHub, the repository |
| The operator password | a password manager | anywhere else; password sign-in is off |

Nothing of the demo lives on the Caravana host outside Caravana: no token, no timer, no file.
