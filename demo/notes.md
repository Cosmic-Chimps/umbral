# Operations notes

For whoever keeps the demo running. The [README](README.md) has the runbook; this is what
lives where once it is up.

## On the Caravana host (lacalabaza)

| What | Where |
| --- | --- |
| The reset script and the failure notifier | `/usr/local/lib/umbral-demo/umbral_demo_reset.py` and `notify_failed.sh` |
| **The full API token** | `/etc/umbral-demo/caravana-token`, root, mode 0400. systemd's `LoadCredential` hands it to the service as `$CREDENTIALS_DIRECTORY/caravana-token`; it is in no process environment. Nothing else on the host and nothing in GitHub holds it. |
| The reset's settings | `/etc/umbral-demo/reset.env` (address, app, service, golden backup id, the weekly backup's day), owned by the system user `umbral-demo-reset`, mode 0400 |
| Where a failure is reported | `/etc/umbral-demo/notify.env` (root, 0600): a ntfy topic and its token; empty means the journal only |
| The timer, the service and the notifier | `/etc/systemd/system/umbral-demo-reset.timer`, `.service` (runs as `umbral-demo-reset`) and `umbral-demo-reset-failed.service` (its `OnFailure`, runs as root to read the journal) |
| The checkout the installer ran from | `/opt/umbral`, a checkout of the Umbral repository (re-run `sudo sh /opt/umbral/demo/reset/install.sh` after a `git pull` to update the script and units; it leaves `reset.env` alone) |

Run a reset by hand, and read what happened:

```sh
sudo systemctl start umbral-demo-reset.service
journalctl -u umbral-demo-reset -n 20
systemctl list-timers umbral-demo-reset.timer
```

Rotate the token: create a new one in Caravana, write it to `/etc/umbral-demo/caravana-token`
(`umask 077`), run the service once by hand, then delete the old token.

**When a reset fails.** A restore that started and failed is on Caravana's needs-you list and
in its notifications. A reset that never got that far (the token expired or was revoked, the
golden backup gone from the destination, Caravana unreachable, a backup running at 04:00) is
only in the journal, so the service's `OnFailure` posts the journal's last lines to the ntfy
topic in `notify.env`; with no topic set, check `systemctl status umbral-demo-reset` now and
then. Either way Umbral stays on its current data, with that day's users, until the next
successful reset, and no copy of it is taken meanwhile.

## In Caravana

- App `umbral-demo` (project `default`, environment `production`), services `umbral`, `app`,
  `site`. Volume `umbral-demo-data` on `umbral`, mounted at `/data`.
- The golden snapshot: a backup of `umbral` on the app's Backups tab, its id in `reset.env`
  (`GOLDEN_BACKUP_ID`). The service's backup policy has a schedule that never matches
  (`0 4 31 2 *`), so Caravana takes no copy on its own; the reset script takes one each Sunday
  after a successful restore, and the policy keeps 100 of them, so the snapshot stays in
  retention for about two years. Take a new snapshot before that, and after any change that
  should survive a reset.
- Logs that a reset does not clear: Umbral's and the sample's console output, kept by Nomad on
  the host by size (10 files of 10 MB per stream per copy, no time limit) and removed with the
  copy's allocation; Traefik writes no access log on a Caravana 0.20.0 host. Umbral writes no
  account data and no client address to its console; the privacy page says so, and that holds
  only while it stays true.
- Once this Caravana pins backups (0.21.0 or later): pin the snapshot under `golden`, set
  `GOLDEN_LABEL=golden` in `reset.env` instead of the id. Once the Caravana hosting package can
  declare `restore: {schedule, from}` on the backup policy, declare it in `AppHost.cs`, deploy,
  disable the timer (`systemctl disable --now umbral-demo-reset.timer`), delete the token in
  Caravana and remove `/etc/umbral-demo`.

## What a reset does, and does not

- Stops Umbral, restores every file of `/data` from the snapshot, starts it: about a minute off,
  at 04:00 UTC, the hour the landing page announces.
- Drops every user, session, token and security-log entry made since the snapshot. The
  operator credential, its passkeys, the password-sign-in switch, the connectors and their
  secrets come back as in the snapshot.
- Drops the Fediverse registrations made since the snapshot: Umbral registers itself again on
  a server at the next sign-in from it, and that server then lists "Umbral" twice under its
  authorised applications. The privacy page says so.
- Does not touch the sample or the site, nor the declared app and passkey connector, which the
  AppHost applies again at every start anyway.
- A restore that fails leaves Umbral on its current data; see "When a reset fails" above.

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
| The connectors' client secrets | Umbral's `/data` (encrypted), hence the snapshot and the backups in the destination | Caravana, GitHub, the repository |
| The full API token for the reset | `/etc/umbral-demo/caravana-token` on the host, root only, handed to the service as a credential | GitHub, Bella Baxter, the repository, Caravana's app secrets, any process environment |
| The operator password | a password manager | anywhere else; password sign-in is off |
