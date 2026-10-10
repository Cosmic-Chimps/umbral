---
title: "Run the idle-RSS test on Linux CI and win back headroom under the 150 MB ceiling"
kind: problem
status: pending
created: 2026-10-10
summary: "Measured idle RSS of the real app is 140 MiB (Linux arm64, VmRSS) with 85 MB of it file-backed; macOS is bimodal 115-165 MB. SC-007 has ~10 MiB of margin and no CI runs IdleMemoryTests on Linux."
---

## Why
`tests/Umbral.Tests/IdleMemoryTests.fs` (T097) starts the built app as its own process. Measured:
- Linux arm64 (Docker, native, Debug build via the test): 140.1 MiB VmRSS. Release build in `aspnet:10.0`: 142-146 MiB (`RssAnon` ~29 MB, `RssFile` ~84 MB, `RssShmem` ~33 MB from the W^X double mapper).
- macOS arm64: 112-168 MB from one start to the next (bimodal), so the strict 150 MiB assertion runs on Linux only.
- linux/amd64 was only run under emulation (244 MiB, meaningless); real x64 is unmeasured.
The ceiling is 150 MiB (157,286,400 bytes); the margin on the measured platform is ~10 MiB. There is no CI workflow in the repo, so the strict assertion runs nowhere automatically.

## What to do
1. Add a CI job on `ubuntu-latest` (and ideally an arm64 runner) that runs `dotnet test` including `IdleMemoryTests`, and record the x64 number.
2. If x64 is closer to the ceiling, look for headroom: assemblies loaded at idle (Quartz, EF Core, System.Private.Xml), `DOTNET_TieredPGO=0` (~3 MB), trimming. `DOTNET_EnableWriteXorExecute=0` only moves shmem to anon, no gain.

## Where
`tests/Umbral.Tests/IdleMemoryTests.fs`, a new `.github/workflows/`, `src/Umbral/Umbral.fsproj`, the future Dockerfile (T098).
