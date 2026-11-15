# Documentation review: the public demo (task fzzx)

The demo repository at `/Users/jjchiw/Documents/code4fun/umbral-demo`, commit 7efe66b, read as text
(README.md, notes.md, `site/public/index.html`, `site/public/privacy/index.html`, `app/README.md`,
`reset/reset.env.example`), with `apphost/AppHost.cs` for the facts. Checked against Umbral's code and
docs on `alta/elegant-indus-smtm` (`docs/run/aspire.md`, `docs/run/operator-passkeys.md`,
`docs/run/operator-credential.md`, `docs/connectors/*.md`, `docs/run/caravana.md`). Reviewed
2026-11-15 by the documentation reviewer. I have not edited the demo repository.

## Verdict

**Not clear yet: three blocking items, all about the privacy page or the Umbral repository, and
four nits.** The runbook is very good: in order, exact about what is done by hand, honest about the
fallback reset and the unreleased Caravana pieces, and right about the facts I checked. The privacy
page is short and plainly written, as it should be. But three of its statements are not what Umbral
stores or keeps, and a privacy page is the one page here that has to be exactly right.

## What was checked

- **The runbook against Umbral's docs.** Production settings with no laptop pair, `TRUSTED_PROXIES`
  for Caravana's Traefik, the declared passkey connector (`PasskeySignUp.Open`) and app `demo` with
  `UMBRAL__APP_SECRETS__demo`, claim right after the first deploy (the spec 019 advice),
  **Add a passkey** and **Turn password sign-in off** on **Credential**. Also `reset-credential`,
  which turns password sign-in back on (`operator-credential.md:12`), as "Afterwards" says. The
  connector table matches the walkthroughs, including Google's published consent screen and Fediverse
  **Any server**. Right.
- **What a reset does** (notes.md, README step 9): it restores `/data`, drops users, sessions, tokens
  and security-log entries since the snapshot, and drops Fediverse registrations made since the
  snapshot. Right.
- **The Fediverse permission** on the privacy page: Umbral registers as "Umbral" with `read:accounts`
  (`Providers/Fediverse.fs:382`). Right.
- **The landing page**: the five ways, the flow, the claims list, cancel and sign out, 04:00 UTC.
  Right. **app/README.md** and **reset.env.example**: right.
- **Secrets** (notes.md's table): no secret in the repository, and the full API token only on the
  host, mode 0400, for its own system user. Right.

## Blocking

### B1. The privacy page leaves out IP addresses, and gets the security log's contents wrong

"A record of sign-ins" says the security log notes "the time, the way in, and your id at the
provider". Umbral's `security_events` row holds the time, the event, the connector, the app, **the
Umbral user id** and **the IP address** (`remote_ip`; `Database.fs:230-241`). The provider id is in
the linked account, not in the log. An IP address is the piece a privacy page must not leave out.
Write: "Umbral's security log notes each sign-in: the time, the way in, the app, your user id here,
and the IP address you came from."

### B2. The privacy page says every log entry goes at the reset, but the access logs do not

"For how long" says "Every account, session, token and log entry goes with it". The restore replaces
`/data`, which holds Umbral's own security log. It does not touch the reverse proxy's access logs
that the page mentions a section earlier ("addresses and paths"), or the services' container logs
kept by Caravana. Those last as long as the host keeps them. Either say how long that is ("The access
logs of the server in front are kept for N days"; the maintainer knows Traefik's and Caravana's log
rotation on lacalabaza), or at least don't promise they go at 04:00. While you are there, give the
weekly copy's lifetime too: "kept up to about two years with the other copies" (`keep: 100`, weekly).
"Until it is deleted with the other copies" sounds like days.

### B3. Step 10 must also change PRODUCT.md in the Umbral repository

PRODUCT.md lists "no public demo instance" among the "Absences, not to be filled in" (line 198), and
design and writing work is judged against it. The day the "Try it" line goes into the README, that
absence becomes false. Step 10 should name both edits: the README line, and in PRODUCT.md's
"Evidence on Hand" either drop "no public demo instance" or replace it with "a public demo instance
at <https://umbral-demo.lacalabaza.xyz>, wiped every night; surfaces may link it". That needs the
maintainer's say, as PRODUCT.md's commitments do.

## Nits

1. **Umbral's own cookies.** The page names only the sample's session cookie. Umbral sets
   `umbral_session` (keeps you signed in to Umbral) and `umbral_last_connector` (puts the way you used
   last first, in that browser), and a theme cookie if you choose one. One line: "Umbral sets a cookie
   that keeps you signed in, and one that remembers which way in you used last, in this browser only."
2. **A private way to ask for removal.** "Open an issue" is the only channel, and asking for your data
   to be removed in a public issue means saying in public that you signed in. Offer an address, or
   GitHub's private vulnerability reporting, beside the issue link. It is the maintainer's call.
3. **"Mark each connector Shared"** (README step 6). The label is **Shared with every app**, and it is
   ticked by default: "leave **Shared with every app** ticked".
4. **The "Try it" line.** The wording and the place (after the first paragraph) are right, and one edit
   does both the README and the site's start page, as you say. Consider "wiped every night at 04:00
   UTC", the same words as the landing page, so the README makes no promise the demo does not state.
