# Code review: Samples 1/4, the contract, the harness and the ASP.NET Core sample (task s4mp)

Branch `alta/vibrant-regulus-bmdk`, `674e441..f8b5b18` on `main` at `3c57932`. Reviewer: the `umbral` session, 2026-11-05. I read every file of the change and ran the harness myself.

## Verdict

**Clear to merge, with three nits, two of them worth taking before the merge (N1, N2).** The design holds: one network namespace so everything is `localhost` as in the quick start, the fake connector enabled in one file only, secrets handed over in a file that is created at 0600 and deleted, and a walk that checks the contract rather than the sample.

## What was checked, against the task's list

- **The fake connector is reachable only from the test compose.** `ASPNETCORE_ENVIRONMENT=Development` with `UMBRAL__ALLOW_FAKE_CONNECTOR=true` is set in `samples/test/docker-compose.yml` and nowhere else that runs anything (`git grep` on the branch: the other hits are `Fake.fs` itself, the unit tests' in-memory settings, specs, the audit and the docs that say so). The harness's Umbral publishes nothing beyond `127.0.0.1`, and the sample's own compose never sets the pair.
- **PKCE, state and nonce.** `Program.cs`: `ResponseType = "code"`, `UsePkce = true`, discovery only (`Authority`, no endpoint set by hand), and the handler's state and nonce protection left at its defaults. `RequireHttpsMetadata` is relaxed only for an `http` issuer whose host is loopback, which is the one case Umbral accepts plain http.
- **No secret logged or committed.** `run.sh` reads the password from `reset-credential` into a variable and the browser container's environment, never prints it, writes the app's secret only to `out/app.env` (mode 0600, written by `setup.spec.ts`), loads it with `set -a` and deletes it in the `EXIT` trap; `out/` is git-ignored; the failure path prints only Umbral's and the sample's logs, neither of which prints a secret. No secret is in any committed file.
- **Cookie flags.** `HttpOnly`, `SameSite=Lax`, `SecurePolicy=SameAsRequest` (see N2).
- **The workflow.** `permissions: contents: read`; every action pinned by SHA with its version in a comment; `ubuntu-24.04`; the Playwright image `v1.64.1-noble` and the NuGet package `10.0.14` exact; the matrix comes from `samples.conf`, so adding a sample adds a job. `run.sh` runs with `set -euo pipefail` and the trap returns the failing status, so a failing walk fails the job.
- **A failing sample blocks a release.** `release.yml`'s `changelog` job now needs `[test, samples]`, and `samples.yml` has `workflow_call`.
- **Claims and encoding.** `MapInboundClaims = false` keeps `sub`, `name`, `idp`; `/private` HTML-encodes every value; `SaveTokens` is what lets sign-out send `id_token_hint`, which Umbral needs to pick the post-logout URI.
- **The sample's own files.** The Dockerfile restores from the project file alone, publishes Release and runs as `$APP_UID`; the compose refuses to start without the three settings; `samples/Directory.Build.props` stops the repository's F# build properties from reaching the samples; `.dockerignore` keeps `samples/` out of Umbral's image; `check-links.py` walks the samples' Markdown and skips build output.
- **Run by the reviewer.** `samples/test/run.sh aspnetcore` on this Mac, with the host ports not published (see N1): the browser image builds, Umbral starts healthy, `setup.spec.ts` claims the instance, adds the fake connector and registers the app, the sample starts, and `walk.spec.ts` passes: `/`, `/public`, `/private` to Umbral's sign-in, the fake connector with an identity without an email, `/private` with `sub`, `name` = `Sample Person`, `idp` = `fake` and no `email` row, Sign out, and `/private` back to Umbral.

## Nits

- **N1, take now: don't publish the host ports by default.** `samples/test/docker-compose.yml` publishes `127.0.0.1:8080` and the sample's port from the `umbral` service "to look at it from the host while debugging". The walk never needs them (the browser shares the namespace), and they make the harness refuse to start wherever 8080 is taken: on the maintainer's Mac it is, by the Caravana lab's Lima VM, and the first run here failed with "ports are not available". Keep the default file port-free, and put the two `ports` lines in a committed `docker-compose.debug.yml` that `run.sh`'s header mentions (`docker compose -f docker-compose.yml -f docker-compose.debug.yml …`). CI never needs them either.
- **N2, take now: `Secure` on the cookie unless the issuer is plain http on loopback.** `SameAsRequest` is right on `localhost`, but a sample people copy will often sit behind a proxy that terminates TLS and speaks http to the app, where `SameAsRequest` yields a cookie without `Secure`. Mirror the `RequireHttpsMetadata` condition: `o.Cookie.SecurePolicy = insecureLoopback ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always`, with the condition computed once.
- **N3, before the release that follows the merge.** `samples.yml` has not run on GitHub yet (the branch isn't pushed). Its `push` trigger matches `samples/**`, so pushing the merge to `main` runs it once; wait for that run to be green before starting a release, because from the merge on, `release.yml` depends on it.

## Observation, not a finding

`POST /signout` has no antiforgery token. In the sample the worst case is a forced sign-out, and adding `AddAntiforgery` would complicate the one file people read; the contract could say in one line that a real app adds it.
