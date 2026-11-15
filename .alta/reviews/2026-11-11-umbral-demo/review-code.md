# Code and security review, task fzzx (the public Umbral demo)

Reviewer: the `umbral` session. Demo repository `/Users/jjchiw/Documents/code4fun/umbral-demo` at
`7efe66b`, read against the task file and `notes.md` here.

**Verdict: not clear yet, one blocking item and six nits.** The AppHost, the production settings,
the lock-down and the reset script are right; the blocking item is about what a failed reset
leaves in the backups, against what the privacy page promises.

## What holds

- **AppHost.**
  - Publish mode sets an https `WithPublicOrigin`, `WithTrustedProxies("10.99.0.1")`, one domain
    and one health check per service, and memory limits. The package guarantees no
    `Development` and no `UMBRAL__INSECURE_ALLOW_HTTP` in publish mode (reviewed in a5pr).
  - `Umbral:ImageTag` is honoured only in run mode, so a deploy always runs the released image the
    package names.
  - The redirect URIs in publish mode are the public https addresses, as spec 019 requires.
  - The passkey connector and the app `demo` are declared. The secret is the package's 48-character
    generated parameter, a GitHub secret in CI and never in the repository.
- **The three domains** are a sound reading of the task: one Caravana domain routes to one service.
- **Open passkey sign-up from the first deploy: agreed.**
  - Sign-ups before the claim are exactly what the demo offers.
  - The claim needs the setup token, which is only in the service's log, behind Caravana's
    console.
  - Step 4's "claim it now, before you do anything else" is the right instruction. Spec 019's
    docs sentence ("claim before you publish") is met in spirit: the window is the minutes between
    the deploy and the claim, and only someone with Caravana access can read the token.
- **Operator lock-down (steps 5 to 8):**
  - an operator passkey, then password sign-in off;
  - the password is kept only for `reset-credential` through Caravana's terminal;
  - every connector is shared;
  - users are deleted before the golden snapshot.

  Right. The README also says to take a new snapshot after any change worth keeping, which covers a
  changed operator passkey.
- **The reset script:**
  - standard library only;
  - it checks that the golden backup still exists and `succeeded` before restoring;
  - it follows the restore to its end and exits non-zero with the reason;
  - it never prints the token, and `describe` echoes only Caravana's `error` and `message`.
- **The systemd unit:**
  - its own system user;
  - `NoNewPrivileges`, `ProtectSystem=strict`, `ProtectHome`, `PrivateTmp`, `PrivateDevices`,
    and only `AF_INET`/`AF_INET6`;
  - `Persistent=true` on the timer;
  - `install.sh` never overwrites `reset.env` and creates it with mode 0400.
- **The full token stays off GitHub,** as the task requires.
- **The workflow:**
  - actions pinned by SHA;
  - `contents: read` and `packages: write` only;
  - one deploy at a time;
  - it triggers on push to `main` only, so a Dependabot pull request never sees the secrets.
- **The site:**
  - `default-src 'none'`, no script, styles, images and fonts from this origin only;
  - `frame-ancestors 'none'`, `form-action 'none'`, `base-uri 'none'`;
  - `nosniff`, `Referrer-Policy: same-origin`, no directory listing.
- **No secret in the repository:** a grep for tokens, keys and secrets finds none, and no `.env` or
  user-secrets file is tracked.
- **`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` on the sample** makes it believe forwarded headers
  from any sender in the app's network, not only Traefik. That's accepted here: the worst a forged
  host or scheme can do is make the sample ask Umbral for a redirect URI that isn't registered,
  which Umbral refuses (exact match).

## Blocking

**B1. A failed reset puts a day's users into a backup kept for about two years, and the privacy
page says otherwise.**
- The weekly volume backup runs Sundays at 04:05 whether or not the 04:00 reset happened. If the
  reset failed (an expired or revoked token, the golden backup gone, Caravana busy, the host
  down at 04:00 while the backup still runs), that backup holds every account since the last
  good reset: names, addresses, avatars.
- With `keep: 100` it stays about 100 weeks.
- The privacy page says only someone who signed in "during those five minutes" is in a copy.
- The schedule exists only to keep the golden backup in retention, not because anyone needs
  weekly copies.

Fix. Pick one, and make the privacy page match it:
1. **No scheduled backup.** If `WithCaravanaBackup` accepts a policy without a schedule (manual
   backups only, still with `keep`), use that. Nothing then accumulates to push the golden
   backup out of retention, and there are no other copies. The privacy page then says the only
   copy is the empty one taken by hand.
2. **The backup follows a successful reset.** The reset script, after a restore that
   `succeeded`, starts the backup itself (Caravana's "back up now" call with the same token), on
   Sundays only. The policy's schedule goes, or is set so that it never fires on its own. A failed
   reset then takes no copy.
3. If neither is possible with Caravana 0.20.0 and package 13.6.4, the privacy page must state
   the real bound: "if a nightly reset fails, the weekly copy taken after it can hold the accounts
   made since the last successful reset, for up to two years". Then link the follow-up `28zs`,
   since pinning removes the need for the schedule.

Also, a failed reset should be visible to someone. Caravana lists a failed restore on its
needs-you list, but a reset that never started (an expired token, the golden backup gone, the
script failing before the POST) is only in `journalctl`. Add `OnFailure=` to the unit with a small
notifier, or note in `notes.md` that the needs-you list does not cover a reset that never
started, and how to check.

## Nits

- **N1. The token as an environment variable.** It sits in the process environment, readable through
  `/proc/<pid>/environ` by the same user and root. Use `LoadCredential=caravana-token:/etc/umbral-demo/caravana-token`
  and have the script read `$CREDENTIALS_DIRECTORY/caravana-token`. The rest of `reset.env` can
  stay an `EnvironmentFile`.
- **N2. A full token over http.** Refuse a `CARAVANA_URL` that isn't https, unless the host is
  loopback, before sending the token.
- **N3. `workflow_dispatch` runs from any branch with the deploy secrets.** Anyone with write
  access could deploy an unreviewed branch. Put the job in an environment (`production`) whose
  deployment rule allows `main` only, or add `if: github.ref == 'refs/heads/main'`.
- **N4. A missing `UMBRAL_DEMO_CLIENT_SECRET` rotates the app's secret on every deploy.** Fail the
  workflow when it's empty, rather than documenting the rotation, because each rotation also
  restarts the sample with a new secret.
- **N5. The site's base image is pinned by tag** (`static-web-server:2.44.2`). Pin it by digest
  (`@sha256:…`). Dependabot keeps digests current too.
- **N6. "Every log entry goes with it"** (privacy page). A volume restore clears Umbral's security
  log in `/data`, but not Caravana's copy of the services' stdout, nor Traefik's access log, which
  holds IP addresses. Say which logs go, or name the retention of the others. Check with the
  `caravana` session what they are on lacalabaza.

## Re-check

One re-check after B1. For the demo repository, I'll read only what changed since `7efe66b`.
