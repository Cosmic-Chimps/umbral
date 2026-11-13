# Aspire sample: notes for the reviews

Task `2026-11-11-samples-an-aspire-apphost-that-runs-umbral-and-the-asp-net-c-r5hd`,
branch `alta/friendly-jaguar-xpjv` (worktree `~/.alta/worktrees/umbral/friendly-jaguar-xpjv`),
on main at `0bc654d`.

## What changed

New:

- `samples/aspire/`: the AppHost (`AppHost.cs`, `Umbral.Sample.Aspire.AppHost.csproj`,
  `Properties/launchSettings.json`, `aspire.config.json` written by the CLI on the first run,
  which names the AppHost project) and `README.md`.
- `.github/workflows/samples-aspire.yml`: builds the AppHost (and so the ASP.NET Core sample it
  references) with warnings as errors, on a change under `samples/aspire/` or
  `samples/aspnetcore/`.
- `changelog.d/samples-aspire.md` (Added).

Changed:

- `samples/README.md`: a row in the table and one sentence after it (it runs another sample, it
  is not a new stack).
- `README.md`: the samples sentence ("eight", the Aspire AppHost).
- `.github/workflows/samples.yml`: `!samples/aspire/**` in both `paths` lists, and the header
  comment.

## Decisions taken as given, and two additions

The task's decisions are all in: C# AppHost on .NET 10; Umbral as a container from
`ghcr.io/cosmic-chimps/umbral` at the pinned tag `0.11.0` (today's release; never `latest`),
with a named volume for `/data`, `UMBRAL__PUBLIC_ORIGIN` and the laptop pair; the ASP.NET
Core sample as a project reference with `UMBRAL_ISSUER` from Umbral's endpoint and the two
parameters, the secret one `secret: true`; fixed ports; claiming and registering manual, with the
steps shown when the client ID is not set; no publish mode; not walked by the harness.

Two things go beyond the letter of the task:

1. **The ports can be overridden** (`Umbral:Port`, `Sample:Port`, defaults 8080 and 5000),
   through the same user secrets. They stay fixed, not random, which is what the decision is
   about; the override exists because a laptop may already use one of them. This one does: port
   8080 is held by the maintainer's Lima VM (`caravana-lab`) and 5000 by macOS AirPlay Receiver,
   so the walk below ran on 18080 and 5001. The README says so under "If a port is taken".
2. **The steps are shown in the dashboard, not only printed.** With `aspire run`, the AppHost's
   console does not reach the terminal: it goes to the CLI's log file
   (`~/.aspire/logs/cli_*.log`, checked on the first run, where the text was). So the AppHost also
   sends the steps as a notification through Aspire's `IInteractionService`
   (`PromptNotificationAsync`, markdown on, a link to `/admin/setup`), which the dashboard shows
   as a banner at the top of the resources page (`dashboard-first-run-steps.png`). The console
   line stays for `dotnet run`.

Also: the volume is `umbral-aspire-data`, not the quick start's `umbral-data`, so a quick-start
instance built from a newer source (a newer schema) is never opened by this pinned image, which
would refuse to start on it.

## Pins

- `Aspire.AppHost.Sdk/13.6.5` (published 2026-11-10). The task had 13.6.4 in mind, the version of
  the Aspire CLI on this machine, but 13.6.4 fails `-warnaserror` on two NuGet advisories
  (NU1903 on `Aspire.Hosting`/`Aspire.Hosting.AppHost`, GHSA-f7vh-h5g3-7jrh; NU1904 on
  `Aspire.Dashboard.Sdk`, GHSA-4g86-54fm-h9h8), both fixed in 13.6.5. Aspire 13's SDK references
  `Aspire.Hosting.AppHost` itself, so the csproj has no package reference.
- ASPIRE010 is suppressed in the csproj: it says the project does not use the CLI's bundle. With
  the bundle off, the orchestrator (DCP) and the dashboard are restored from NuGet at the SDK's
  version, so the AppHost runs with any Aspire CLI and with `dotnet run` (Frunchy's AppHost does
  the same).
- Umbral image `0.11.0`; the workflow's actions at the SHAs the other samples workflows use
  (`actions/checkout` v5.1.0, `actions/setup-dotnet` v5.4.0); .NET from `global.json`.

## Doc files and what each statement was checked against

### `samples/aspire/README.md`

| Statement | Checked against |
| --- | --- |
| **Setup token**, **Operator credential**, **Connectors**, **Passkey**, **Anyone can sign up**, **Enable passkeys**, **Apps**, **Create app**, **Name**, **App type**, **Web application**, **Redirect URI**, **Post-logout redirect URI (optional)**, **Client ID**, **Client secret** | quoted from `src/Umbral/Views/Texts.fs` (`SetupTokenLabel` 1134, `CredentialLabel` 1130, `NavConnectors` 1114, `PasskeyEnrolmentOpenLabel` 1546, `PasskeyEnableButton` 1542, `NavApps` 1113, `AppCreateButton` 1236, `AppNameLabel` 1206, `AppTypeLabel` 1232, `AppTypeWebOption` 1300, `AppRedirectUriLabel` 1207, `AppPostLogoutRedirectUriLabel` 1209, `AppClientIdLabel` 1239, `AppSecretLabel` 1241); the same labels are in the AppHost's steps |
| The laptop pair and plain `http://localhost` are for a laptop only | `README.md` quick start, `docs/run/settings.md` line 10 |
| The setup token is in Umbral's console logs | `src/Umbral/Infrastructure/AdminAuth.fs` 329-332 (the box), seen in the `umbral` resource's console logs and in `docker logs` during the walk |
| `reset-credential` claims an unclaimed instance | `README.md` "Claim an instance from a script" |
| The two redirect URIs are the handler's defaults, matched exactly | `samples/aspnetcore/README.md`; registered as such in the walk |
| The secret is shown once | `samples/aspnetcore/README.md`; the walk reads it from `[data-secret-once]` on the app's page |
| The dashboard lists `umbral` and `sample`, both Running | `dashboard-both-resources.png` |
| The pages, sign-in and sign-out | the walk (below); the bullets are the ASP.NET Core README's |
| "How it works" bullets | `AppHost.cs` |
| AirPlay Receiver on 5000 | `lsof -nP -iTCP:5000 -sTCP:LISTEN` on this Mac: `ControlCenter` |

### `samples/README.md`, `README.md`

The row and the sentences name the sample and say it runs the ASP.NET Core sample (a **Web
application**, `Texts.fs` 1300) rather than adding a stack.

## How it was verified

Versions: macOS 26.6.2, Docker 29.9.3, .NET SDK 10.0.402 (from `global.json`, 10.0.400 rolling
forward), Aspire CLI 13.6.4, Aspire SDK 13.6.5, Umbral image 0.11.0, Google Chrome 157.0.8103.51
driven by Playwright 1.64.1 (`setup.mjs` and `walk.mjs` in this folder; node 22.23.4).

The walk, by hand through the real screens (scripted with Playwright, as the earlier walks were),
with `Umbral:Port` 18080 and `Sample:Port` 5001 in user secrets (see above):

1. `aspire run` from `samples/aspire`, no client ID set: Umbral started from the pinned image
   (`umbral-aspire-data` created, `/healthz` 200), the dashboard showed one resource and the
   steps as a banner (`dashboard-first-run-steps.png`; the ports in it are the overridden ones).
   The console text was in `~/.aspire/logs/cli_*.log` and not on the terminal.
2. The steps, as written: setup page with the token from the container's log, an operator
   credential; Connectors → Passkey → **Anyone can sign up** → **Enable passkeys**; Apps →
   **Create app** (Web application, the two URIs on port 5001); client ID and secret into user
   secrets.
3. The AppHost stopped and started again with the client ID removed (a second first run): the
   banner again, and `/admin/setup` answered 302 to `/admin/signin`: the claim survived.
4. Client ID set back, `aspire run` again: two resources, both Running
   (`dashboard-both-resources.png`); the sample's issuer from the endpoint matched discovery's
   `issuer` (`http://localhost:18080/`).
5. The contract's walk on `http://localhost:5001`: `/` (heading, Public, Private, Sign in) →
   `/public` → `/private` sent to Umbral's sign-in page, "Sign in to Aspire sample"
   (`umbral-signin.png`) → **Sign in with a passkey** → **Your name** → **Create an account
   with a passkey** on a virtual authenticator (CDP `WebAuthn.addVirtualAuthenticator`) →
   `/private` shows `sub`, `name` = Aspire Person, `idp` = passkey, no `email` row
   (`private-claims.png`) → **Sign out** → `/` shows **Sign in** → `/private` sends to Umbral
   again. All green.
6. The secret: not in the repository (`git grep`), not in this session's 153 Aspire CLI log
   files, not in the captures. The dashboard marks the parameter secret. After the walk the
   volume was removed and the AppHost's user secrets cleared, so the laptop holds nothing of it.

Other checks: `dotnet build -c Release -warnaserror` in `samples/aspire`, 0 warnings;
`.github/scripts/check-links.py`: 400 links in 39 files, all resolve;
`.github/scripts/changelog.sh check`: in order; actionlint 1.7.12 (Docker) on
`samples-aspire.yml` and `samples.yml`: clean. The workflow itself runs on GitHub on the push of
this branch.

## Changelog

`changelog.d/samples-aspire.md`, Added.
