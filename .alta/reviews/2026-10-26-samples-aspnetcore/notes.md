# Samples 1/4: notes for the reviews

Task `2026-10-26-samples-harness-and-aspnetcore-s4mp`, branch `alta/vibrant-regulus-bmdk`.

## What changed

New:

- `samples/README.md`: the contract every sample follows, and the harness.
- `samples/aspnetcore/`: the ASP.NET Core sample (`Program.cs`, csproj, `Dockerfile`,
  `docker-compose.yml`, `README.md`).
- `samples/test/`: the harness (`run.sh`, `docker-compose.yml`, `Dockerfile`, Playwright
  `setup.spec.ts` and `walk.spec.ts`, `samples.conf`).
- `samples/Directory.Build.props`: stops the repository's build props from reaching the samples.
- `.github/workflows/samples.yml`: one job per line of `samples.conf`.
- `changelog.d/samples.md`.

Changed:

- `README.md`, "Integrate an app": one paragraph pointing at `samples/`.
- `.github/workflows/release.yml`: a `samples` job, which `changelog` now needs.
- `.github/scripts/check-links.py`: also walks `samples/**/*.md`.
- `.dockerignore`: `samples` is not part of Umbral's image context.

## Doc files and what each statement was checked against

### `samples/README.md`

| Statement | Checked against |
| --- | --- |
| `sub`, `name`, `idp` always present; `email` and `email_verified` together or not at all | `README.md` "Integrate an app" (T2 text), `Handlers/Connect/Authorize.fs:96-139` (`setProfileClaims`), the walk in `samples/test/walk.spec.ts` (no `email` row for an identity without one) |
| Sign out at `/connect/logout`, from discovery; Umbral redirects only to the registered post-logout URI | `Infrastructure/OpenIddictSetup.fs:34,273`, `Handlers/Connect/EndSession.fs:24-35`, `README.md` "Signing out" |
| Authorization code with PKCE S256 is the only flow | `Infrastructure/OpenIddictSetup.fs:279` (`AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange()`) |
| Scopes `openid profile email` | `Infrastructure/OpenIddictSetup.fs:301` |
| Umbral accepts plain http for `localhost` only | `Infrastructure/PublicOrigin.fs:77-83,110`; `README.md` "Settings reference" |
| The fake connector needs `ASPNETCORE_ENVIRONMENT=Development` and `UMBRAL__ALLOW_FAKE_CONNECTOR=true` | `Infrastructure/Providers/Fake.fs:37-41` |
| "that pair is set in `test/docker-compose.yml` and in no other compose file, Dockerfile or run instruction" | `grep -rn ALLOW_FAKE_CONNECTOR` on 2026-11-05: the other hits are `Fake.fs` (the check and its message), the tests' in-memory settings, specs and the audit |
| **Create app**, **Apps**, **App type**, **Redirect URI**, **Post-logout redirect URI (optional)**, **Issuer URL**, **Client ID**, **Client secret**, **Web application**, **Single-page or native app** | quoted from `Views/Texts.fs:890,984-997,1055-1056,1066-1067` |
| The workflow runs on a change under `samples/`, on request and before a release | `.github/workflows/samples.yml` (`on`), `release.yml` (`samples` job, `changelog` needs it) |

### `samples/aspnetcore/README.md`

| Statement | Checked against |
| --- | --- |
| **Apps**, **Create app**, **Name**, **Redirect URI**, **Post-logout redirect URI (optional)**, **App type**, **Web application**, **Client ID**, **Client secret**, **Issuer URL** | quoted from `Views/Texts.fs` (lines above, `AppNameLabel` 983, `AppCreateButton` 991) |
| The secret is shown once | `Handlers/Admin/Apps.fs:741-746` (`secretReveal`), `README.md` "Register an app" |
| `http://localhost:5000/signin-oidc` and `/signout-callback-oidc` are the handler's defaults | `OpenIdConnectOptions.CallbackPath` and `SignedOutCallbackPath` defaults; both registered by `samples/test/setup.spec.ts` and exercised by the walk |
| Umbral matches redirect URIs exactly | `README.md` "Integrate an app" ("Redirect URIs are matched exactly") |
| `dotnet run` works against the quick-start Umbral on `http://localhost:8080` | the harness is that setup (Umbral on `localhost:8080` with the quick start's three settings); `Program.cs` relaxes `RequireHttpsMetadata` for an http issuer on a loopback host only |
| A container cannot reach an Umbral on the laptop's `localhost` | Docker networking; the harness avoids it by sharing one network namespace (`samples/test/docker-compose.yml`) |
| Sign-in page titled **Sign in to** and the app's name | `README.md` "Prove it works"; `Handlers/Connect/Picker.fs:209` |
| `sub`, `name`, `idp`, and `email` + `email_verified` when Umbral has an address | as for the contract; shown by `Program.cs` `/private` in that order |
| Handler settings in "How it works" | `samples/aspnetcore/Program.cs` lines 33-55 |

### `README.md` (one paragraph)

Names the one sample and its type (**Web application**, `Texts.fs:1055`), and says CI runs each
sample before every release (`release.yml`).

## How it was verified

- `samples/test/run.sh aspnetcore` on this machine (macOS, Docker 29.9.2, Compose v5.6.1):
  Umbral built from the branch, claimed with `reset-credential`, the fake connector and the app
  registered through the admin screens by `setup.spec.ts`, then the walk: `/` → `/public` →
  `/private` (Umbral's sign-in page) → fake connector, identity without an email → `/private`
  shows `sub`, `name` = `Sample Person`, `idp` = `fake`, no `email` row → **Sign out** → `/` shows
  **Sign in** → `/private` sends to Umbral again. Green (see the final answer of the session for
  the run's tail).
- `cd samples/aspnetcore && dotnet build -c Release`: 0 warnings, 0 errors. Smoke run without
  Umbral: `/` and `/public` answer 200; `/private` fails on discovery, as expected.
- `.github/scripts/check-links.py`: all links resolve, the samples' READMEs included.
- `.github/scripts/changelog.sh check`: in order.
- shellcheck on `run.sh` and actionlint on both workflows: clean (run in Docker).
- The workflow itself has not run on GitHub yet: it runs on the push of this branch.

## Changelog

`changelog.d/samples.md`, Added.
