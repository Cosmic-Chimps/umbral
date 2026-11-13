# Code review: the Aspire sample (task r5hd)

Branch `alta/friendly-jaguar-xpjv`, `0bc654d..cb86f70` (the sample is `7af4a1d`). Reviewer: the `umbral` session, 2026-11-13.

## Verdict

**Clear to merge.** No blocking items; one nit.

## What was checked

- **The Umbral container.**
  - It is pinned to `ghcr.io/cosmic-chimps/umbral:0.11.0`, never `latest`. The tag exists: the registry answers the manifest with 200, and `v0.11.0` was released on 2026-11-13.
  - `/data` is a named volume (`umbral-aspire-data`), so the operator credential, the apps and the signing keys survive a restart, and the README says that wiping it means claiming again.
  - `UMBRAL__PUBLIC_ORIGIN` is a plain string (not a reference expression), equal to the pinned host port.
  - The laptop pair, `ASPNETCORE_ENVIRONMENT=Development` with `UMBRAL__INSECURE_ALLOW_HTTP=true`, is the quick start's, commented "never on a server". The fake connector is not enabled (`UMBRAL__ALLOW_FAKE_CONNECTOR` isn't set), so Development alone exposes no sign-in shortcut.
  - The health check is `/healthz`, a real route (`Handlers/Health.fs`), and the sample `WaitFor`s it.
- **The sample.**
  - It is a project reference to `../aspnetcore`, so the AppHost runs the real sample.
  - `UMBRAL_ISSUER` comes from the container's `http` endpoint. Your hand walk confirms it equals the public origin exactly, which the issuer check requires.
  - It is added only when both parameters are set, so Aspire never prompts for one, and the client secret is `secret: true`, which the dashboard masks.
- **The ports.** 8080 and 5000 are fixed on purpose, since the issuer and the registered redirect URI must stay exact. `Umbral:Port` and `Sample:Port` in configuration (user secrets) override them, and the comment names macOS's AirPlay on 5000.
- **No secret reaches the repository or a log.**
  - The ID and secret live only in user secrets (`UserSecretsId`).
  - The printed and dashboard steps hold addresses and instructions, never a value.
  - `PromptNotificationAsync` is deliberately not awaited, since it completes only when dismissed.
  - The README and the changelog hold no value.
- **Pins and project.**
  - `Aspire.AppHost.Sdk/13.6.5`: your notes give the reason, 13.6.4 has the NU1903 and NU1904 advisories.
  - There is no package reference, and `ASPIRE010` is suppressed with its reason (DCP and the dashboard are restored from NuGet, so any Aspire CLI or `dotnet run` works).
- **CI.**
  - `samples-aspire.yml`: `contents: read`, checkout without credentials, actions pinned by SHA (the same as the other sample workflows), a 15-minute timeout, and `dotnet build -c Release -warnaserror`.
  - It runs on changes to `samples/aspire/**` or `samples/aspnetcore/**`, so a change to the referenced sample is caught too.
  - `samples.yml` now skips `samples/aspire/**`.
- **Built by the reviewer**, from `git archive cb86f70`: `dotnet build -c Release -warnaserror` in `samples/aspire` succeeded.

## Nit

- **N1. The `docker exec` in the steps can match several containers.** `docker ps -q -f name=umbral` matches every running container whose name contains "umbral". With the samples harness or another Umbral running, it expands to several IDs and `docker exec` fails with an unhelpful error. Anchor it to Aspire's container (`-f name=^umbral-`, if that is how Aspire names it; check), or tell the reader to copy the container name from the dashboard's `umbral` resource. The setup page with the token from the logs is the main path, so this is the fallback only.

## Re-check at `b6f9a07` (2026-11-13)

**Clear to merge.** N1 is taken, and better than I asked. The fallback selects the container with `docker ps -q -f volume=umbral-aspire-data` (`{VolumeName}` in the code), the volume only this AppHost's Umbral mounts. Neither the harness's container nor the quick start's matches it, and it doesn't depend on how Aspire names containers. The line also says what the command does: it claims Umbral and prints a password to sign in with. That password goes to the reader's own terminal, not to any log Aspire keeps. The README carries the same command. `dotnet build -c Release -warnaserror` from `git archive b6f9a07` succeeded.
