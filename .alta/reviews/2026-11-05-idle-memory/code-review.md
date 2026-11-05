# Code review: idle memory, the operator-passkey run on CI (task m3m0)

Branch `alta/cool-mango-vzs7`, `eb700dc..ded4af1`. Reviewer: the `umbral` session, 2026-11-05.

## Verdict

**Clear to merge.** No blocking items; three nits, one of them worth taking before the merge (N1), and a correction to the record that is mine to make.

## The record, corrected

The task's premise was wrong, and the finding in `notes.md` is right: every figure in the task's table (154.4 and 158.0 MiB for the operator-passkey run) came from the CI comparison step, which runs with `DOTNET_TieredCompilation=1`, not from the gated measurement. In release run 41951561093 the gated step passed and the comparison step failed the job; `0a83ba6` has since made that step non-gating. With the shipped settings the run read 146 MiB on x64 and 148.3 to 148.8 on arm64. The local Docker figure in spec 011's notes (147.6 MiB) agreed with the runners; my "runners read 7 to 10 MiB higher" was the same mistake. The maintainer chose the footprint fix with 1.2 to 1.7 MiB of margin on arm64 in view, which is a sound reason even though the run was under the ceiling.

## What was checked

- **The measurement.** The breakdown and the knob matrix come from GitHub's runners (runs 41966502388, 41967320890, 41972164687, 41973097144; the last two exist and succeeded), twice per job, with `VmRSS` and `smaps`, so the figures are the ones that count. The WebAuthn library costs about 7 MiB, paid by the first ceremony; libsodium is never loaded; the verifier is already lazy; spec 011 adds nothing measurable over the connector run. ReadyToRun was tried and is worse. Those are the facts the test module's comment now states.
- **The setting.** `ENV DOTNET_GCgen0size=0x200000` in the Dockerfile, beside `DOTNET_gcServer=0`, saves about 4 MiB in every configuration (operator passkey 142.0 to 142.1 on x64, 144.5 to 144.6 on arm64, at `f3629dc` on the runners). The round-3 run shows `runtimeconfig.json` ignores it under both names, so the image's `ENV` is the right place, and the README's requirement that Docker is the only supported way to run Umbral means every operator gets it. `docker-compose.yml` and Caravana run the image, so the maintainer's own instance gets it too.
- **The test.** `imageEnvironment` gives the measured child both of the image's GC settings, so it runs as the container does; the new fact fails when the Dockerfile's `ENV` lines differ (the notes say that was tried). Locally at `ded4af1`: the build is clean and the six tests of the class pass (macOS, sanity ceiling only).
- **No ceiling changed.** Spec 011's SC-005 and specs/008's note stand.
- **Changelog:** `changelog.d/idle-memory-gen0.md`, a `Changed` bullet, which is right since the image's environment and its idle figure change.
- **Full suite:** the implementer's run is 1797/1797 on macOS; the reviewer's own run at `ded4af1` is recorded below.

## Nits

- **N1, worth taking now: the drift fact should run in the `test` job.** It lives in `IdleMemoryTests`, which the `memory` job runs, and that job gates only pull requests; this repository merges locally and pushes to `main`, so a Dockerfile change that drops or alters the `ENV` line would only ever show as a warning. `MetaTests.fs` already walks up to `Umbral.slnx` (`repoRoot`) and runs in the `test` job that blocks everything. Move the fact there, put `imageEnvironment` in a small shared module under `tests/Umbral.Tests/Infrastructure/` so both files read one list, and drop the duplicate `repoRoot` from `IdleMemoryTests`.
- **N2, wording.** The changelog bullet's last sentence, "Leave it as it is: removing it from the container's environment brings back the old figures", reads as an instruction to the reader. Say what it is instead: "It is set in the image, so a container gets it without any setting of yours; running the app outside the image is not supported, and would not get it."
- **N3, housekeeping.** Delete the two diagnostic branches on GitHub once this is merged: `diag/idle-memory-m3m0` and `diag/idle-memory-m3m0-final`. They hold the throwaway workflow and the detail mode, which the notes describe well enough to rebuild.

## Observation, not a finding

A 2 MiB gen0 budget means a gen0 collection about every 2 MiB of allocation, each sub-millisecond; a sign-in service will not notice, and the notes say so. It was not measured under load, and nothing requires that now. If a latency complaint ever comes, this is the first knob to look at, which is a good reason for the Dockerfile comment to say what it costs, as it does.
