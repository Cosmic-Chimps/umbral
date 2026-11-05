# Idle memory: the operator-passkey run on CI (task m3m0)

Notes by the implementing session, 2026-11-05. All figures in this file come from GitHub's runners (`ubuntu-24.04`, x64 with 4 vCPU and a 32 or 48 MiB L3; `ubuntu-24.04-arm`, arm64 with 4 vCPU and a 128 MiB L3). They are Release builds measured with `VmRSS`, as the gated test reads it. None of them comes from a laptop.

## The finding: the run was never over 150 MiB with the shipped settings

The release run that "failed on the operator-passkey run" (`actions/runs/41951561093`, at `ff94a65`) shows the following in its job logs, on both architectures:

- The gated step (`dotnet test -c Release`, the whole suite with the memory class in it) **passed**: 1795 tests, 0 failed.
- The step that failed was the **comparison** step, `Idle memory with DOTNET_TieredCompilation=1`. The failure message (`operator passkey ... 154.4 MiB`, x64; `158.0 MiB`, arm64) and every figure in the task's table were printed under `DOTNET_TieredCompilation: 1` (log lines 215–240 of both `test` jobs).
- At that commit, a failure in the comparison step failed the job. `0a83ba6` ("the tiered-compilation comparison never fails the memory job") has fixed that since.

The task's table therefore shows tiered compilation **on**. That is also why the later rerun with `DOTNET_TieredCompilation=1` (`41956010737`) read "nearly the same": both sets of figures are tiered-on. The conclusion drawn from them, that tiered compilation no longer saves much, does not hold. It still saves 6 to 9 MiB on these runners (see "Knobs").

With the shipped settings (tiered compilation off, workstation non-concurrent GC), the operator-passkey run reads **146 MiB on x64 and 148.3 to 148.8 MiB on arm64**. That is under 150 MiB, but with only 1.2 to 1.7 MiB to spare on arm64.

## How it was measured

- An opt-in diagnostic mode of `IdleMemoryTests` (`UMBRAL_IDLE_MEMORY_DETAIL=1`, Linux only).
  - It records `VmRSS` and a `/proc/<pid>/smaps` breakdown at each stage of a run: after the warm-up requests, after each ceremony, and at idle.
  - It also tries `dotnet-counters` at idle. Its GC counters read 0 when no collection happens in the window, so the breakdown below rests on smaps.
- A throwaway workflow runs the class under a matrix of settings, twice per job.
- Both are on the branch `diag/idle-memory-m3m0` (`b40eb2e`, `537f89c`), pushed for these runs and **not** on the task branch. Delete that branch once this task is settled.
- Runs: `actions/runs/41966502388` (round 1) and `actions/runs/41967320890` (round 2).

## Breakdown (shipped settings, round 1, MiB)

Operator-passkey run, x64:

| Mapping | warm | password sign-in | passkey added | passkey sign-in | idle |
| --- | ---: | ---: | ---: | ---: | ---: |
| **VmRSS** | 139.0 | 139.4 | 145.2 | 146.0 | **146.1** |
| JIT code (doublemapper) | 27.1 | 27.3 | 30.3 | 30.5 | 30.5 |
| anonymous (GC heap and native) | 13.9 | 14.1 | 15.4 | 15.9 | 16.0 |
| framework DLLs | 52.3 | 52.3 | 52.8 | 52.8 | 52.8 |
| Fido2 DLLs | 0 | 0 | 0.5 | 0.5 | 0.5 |
| System.Formats.Cbor, NSec | 0 | 0 | 0.4 | 0.4 | 0.4 |
| app and package DLLs | 15.6 | 15.6 | 15.6 | 15.6 | 15.6 |
| `[heap]` (glibc brk) | 10.0 | 10.0 | 10.0 | 10.0 | 10.0 |
| libcoreclr / libclrjit | 9.7 | 9.7 | 9.7 | 9.7 | 9.7 |

On arm64 the run goes from 141.7 (warm) to 148.8 (idle), with the same shape: JIT code +3.5, anonymous +2.0, assemblies +1.4.

What this says:

- **The WebAuthn ceremonies cost about 7 MiB, on both architectures.**
  - About 3.3 MiB is JIT-compiled code of Fido2, Cbor and the handlers.
  - About 2 MiB is anonymous memory: the GC heap and native allocations.
  - About 1.4 MiB is assembly pages.
  - The first ceremony (the add) pays nearly all of it. The sign-in after it adds under 1 MiB.
- **libsodium is never loaded.** No libsodium mapping appears in any run. NSec's managed DLL is mapped (0.1 MiB), but the native library is not opened by these ceremonies (ES256 does not use it).
- **The library is already loaded only when a ceremony starts.** `WebAuthn.Verifier` builds `Fido2` lazily (`Infrastructure/WebAuthn.fs`), and the warm column shows no Fido2 mapping. Loading it later saves nothing more.
- **Spec 011 adds nothing measurable on top of the connector run.** Connector run idle: x64 145.9 to 146.6, arm64 148.3 to 148.8. Operator run idle: x64 145.8 to 146.3, arm64 148.3 to 148.8. Within these figures the operator's password sign-in costs 0.4 MiB.

## Knobs (operator-passkey run, idle, MiB; ranges over 2 to 4 runs)

| Setting | x64 | arm64 | Against shipped |
| --- | ---: | ---: | --- |
| **Shipped** (`TieredCompilation=false`, `gcServer=0`) | 145.8–146.3 | 148.3–148.8 | — |
| Whole suite (as CI ran it before the `memory` job) | 145.8 | 148.7 | same: the suite around it does not matter |
| `dotnet publish` output (what the container runs) | 145.6–146.0 | 148.3–148.4 | same as the test's build output |
| `DOTNET_TieredCompilation=1` | 153.1–153.9 | 157.5–158.0 | **+7 to +9** |
| `DOTNET_GCgen0size=0x400000` (4 MiB gen0 budget) | 144.2–144.5 | 146.4–147.0 | −1.8 to −2.0 |
| **`DOTNET_GCgen0size=0x200000`** (2 MiB gen0 budget) | 141.9–142.1 | 144.5–145.0 | **−3.7 to −4.2** |
| `DOTNET_GCConserveMemory=5` | 145.8–146.2 | 148.4–148.6 | none |
| `MALLOC_ARENA_MAX=2` | 146.0–146.1 | 148.3–148.4 | none |
| `DOTNET_GCHeapHardLimit=0x6400000` (100 MiB) | 147.6–147.8 | 150.3–150.5 | +1.5 to +1.8 (worse) |
| `PublishReadyToRun=true` (whole app) | 156.8–157.1 | 161.8–162.4 | **+10 to +13 (worse)** |

- `DOTNET_gcServer` is already 0 everywhere (the Dockerfile and the test). Server GC was not tried again.
- **ReadyToRun makes things worse.** It maps much larger images, and their code pages count as resident. Compiling the two libraries alone ReadyToRun would save at most part of their 3.3 MiB of JIT code. That was not built, because the whole-app figure points the wrong way.
- **The gen0 budget is the only knob that helps.**
  - It moves every run by the same amount, local store included: local store x64 135.4–136.3 and arm64 137.8–139.4 at 2 MiB.
  - It is a per-process GC sizing that follows the CPU cache, which is also why runner and laptop figures differ.
  - Cost: more frequent gen0 collections, each smaller. A low-traffic sign-in service does not notice them.
- **`GCgen0size` is an environment setting.** As far as I know it has no `System.GC.*` runtimeconfig name. If it is chosen, it goes in the Dockerfile's `ENV` (next to `DOTNET_gcServer=0`) and in the test's child environment, and an operator who runs the app outside the image has to set it themselves. To verify before relying on it: does `runtimeconfig.json` honour it under any name?

## The fediverse run

- **Over the local store:** +1.5 to 2.3 MiB at idle (x64 140.6–140.8 against 138.5–139.1; arm64 142.8–143.7 against 141.2–141.7).
  - It is the account step's code: JIT +0.6, anonymous +0.6, framework DLLs +0.2, libcoreclr pages +0.3.
  - Nothing of a Fediverse server's is loaded until a sign-in, as expected: the run does not contact a server.
- **Margin: about 6.3 MiB on arm64 and 9 MiB on x64, and it is real.** The "150.0 MiB on arm64" in the task is, again, the tiered-on comparison figure.

## Other findings

- **The runners differ from each other by about 1 MiB.** x64 jobs landed on runners with a 32 MiB and a 48 MiB L3, and their figures are within 0.5 MiB of each other. Two iterations on the same runner are within 1 MiB of each other, and mostly within 0.5. The exception is the local store at a 2 MiB gen0 budget on arm64 (137.8 and 139.4).
- **Runners against laptop Docker.** The local Docker figures in spec 011's notes (arm64, 147.6 MiB operator passkey) agree with the runners (148.3 to 148.8). The "7 to 10 MiB higher on runners" in the task was the tiered-on comparison.
- **The figure in `Umbral.fsproj`'s comment** ("144.0 → 137.7 MiB" on linux/arm64) still describes the saving well. On the runners tiered compilation off saves about 6.5 MiB (x64) and 7.3 MiB (arm64) on the local store, and 7 to 9 MiB on the passkey runs.

## Decision for the maintainer

The run is under its ceiling with the shipped settings, so the task's premise does not hold. The options are now:

1. **Leave the footprint as it is.**
   - Keep 150 MiB for the operator-passkey run.
   - Correct the record in the test module's comment: the real figures, the 7 MiB the WebAuthn library costs, and the 7 to 9 MiB that tiered compilation off still saves.
   - Risk: 1.2 to 1.7 MiB of margin on arm64. The next small dependency or handler could take it, and pull requests would then be gated on it.
2. **Footprint fix: `DOTNET_GCgen0size=0x200000`.**
   - Set it in the Dockerfile `ENV` and in the test's child environment, with the comment correction of option 1.
   - The operator-passkey run goes to about 142 MiB (x64) and 145 MiB (arm64), 5 MiB to spare. Every run gains about 4 MiB, the local store included.
   - An operator outside the image does not get it unless the docs say so: the deployment docs would gain a line, and a `changelog.d/` fragment, since it changes what an operator runs.
3. **The ceiling.** The operator-passkey run shares `ExternalStoreCeilingBytes` (160 MiB) with the connector run. Spec 011 SC-005 and specs/008's note are updated to say the library's cost is paid once, whichever use loads it. The default instance stays under 150.
