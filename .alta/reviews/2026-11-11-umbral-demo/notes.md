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
