# Idle memory: the operator-passkey run at its ceiling on arm64 (task mem2)

Notes by the implementing session, 2026-11-23. Every figure comes from GitHub's runners (`ubuntu-24.04-arm` and `ubuntu-24.04`): Release builds, `VmRSS`, read by the gated test class (`IdleMemoryTests`) with the image's environment. They were read from the job logs (`gh api --allow-escape-sequences repos/Cosmic-Chimps/umbral/actions/jobs/<id>/logs`).

## How it was measured

- A throwaway workflow on `diag/idle-memory-mem2` checks out a main commit by SHA, builds it in Release, and runs that commit's own `IdleMemoryTests` class three times on one runner.
- During each run, a shell loop copies `/proc/<pid>/smaps` of every `Umbral.dll` child every 0.5 s. The last copy is the child at idle, just before the test kills it. A script groups its `Rss` by mapping: JIT code, GC and native anonymous memory, glibc's `[heap]`, framework DLLs, Umbral's and package DLLs, and so on.
- Round 1 is `actions/runs/45244476494`: 14 commits on the first-parent line of main on arm64, and two of them on x64.

## Round 1: the bisect (idle resident set, MiB, three runs each)

| # | Commit | What it adds | Operator passkey (arm64) | Local store (arm64) |
| --- | --- | --- | --- | --- |
| 01 | `3c57932` | m3m0 shipped (gen0 budget) | 145.0, 144.8, 145.3 | 138.5, 138.0, 138.6 |
| 02 | `3c87b72` | CORS (fw24), persistent session (c5hn), samples | 145.9, 145.7, 146.0 | 139.4, 139.4, 139.1 |
| 03 | `8df9daa` | 012 redirect URIs, spgx, 013 Apple | 146.2, 148.0, 148.0 | 139.2, 139.1, 139.6 |
| 04 | `a82d8ec` | 015 Bluesky | 146.3, 146.4, 146.5 | 139.8, 139.4, 140.2 |
| 05 | `570c8bc` | 016 Microsoft | 146.9, 147.9, 148.4 | 139.8, 140.2, 139.5 |
| 06 | `8a1d88c` | before 017 (s1ug, fl4t, d3tl, Aspire sample, l1st) | 148.4, 148.3, 148.1 | 139.4, 139.7, 139.6 |
| 07 | `3288b9f` | 017 email trust | 148.7, 146.6, 146.9 | 140.2, 139.9, 139.8 |
| 08 | `7d90362` | 018 remove a link | 148.5, 148.9, 147.1 | 139.9, 140.3, 140.0 |
| 09 | `eeae146` | a5pr hosting package | 146.5, 147.1, 146.6 | 140.4, 140.2, 139.7 |
| 10 | `4958e12` | **019 declarations** | **150.4, 149.1, 150.7** | **141.8, 142.4, 142.2** |
| 11 | `8f305cf` | before c1rs (v6pm, 3694, njtn) | 149.2, 150.7, 150.5 | 142.4, 142.4, 141.9 |
| 12 | `db6a5b7` | c1rs clear-users | 150.9, 149.0, 150.9 | 142.2, 142.2, 142.3 |
| 13 | `9dc29b6` | main, 2026-11-16 | 150.8, 150.7, 150.8 | 141.9, 142.4, 142.2 |
| 14 | `b76473a` | main today | 149.0, 150.7, 151.2 | 142.0, 141.8, 142.1 |

On x64:

| Commit | Operator passkey | Local store |
| --- | --- | --- |
| `3c57932` (m3m0) | 141.6, 141.6, 142.1 | 136.0, 136.3, 136.3 |
| `b76473a` (main today) | 148.4, 148.7, 148.6 | 141.0, 141.1, 141.4 |

The x64 operator-passkey run is not failing yet, but it is as close as arm64 was on 2026-11-05: 1.3 to 1.6 MiB to spare.

## What grew

The operator-passkey run is bimodal within one job: it lands on one of two levels about 2 MiB apart (03, 05, 07 and 08 show both). The local store is steady to ±0.5 MiB, so it is the better series for finding a step.

- **m3m0 to a5pr (01 to 09), spread over twelve features: local store +1.6 MiB.** The smaps breakdown of the local store moves in three places: framework DLL pages +0.4, JIT code +0.4, and Umbral's own DLL +0.6. Those three are the cost of the code the features added. No single commit stands out, and there is nothing cheap to take back.
- **019 (09 to 10): local store +2.0 MiB, operator passkey +3 MiB, every run but the AWS one.** The breakdown moves in one place: glibc's `[heap]` goes from 9.9–10.2 to 12.7–13.1 MiB. JIT code, the DLLs and the GC's anonymous memory do not move. Rounds 2 and 3 below find what it is.
- **After 019 (10 to 14): nothing more.** c1rs, v6pm and the rest stay within the noise.

### First guess, disproved: two SQLite connections at once

`Declarations.apply` runs at every start, even with no declaration. Its `plan` kept its `declared_items` connection open while `ConnectorStore.listAll` opened a second, so the pool would hold two SQLite connections and two native caches. `21c4d8b` closes the first before the second opens.

Round 2 (`actions/runs/45246933811`, main `b76473a` against `21c4d8b`, two arm64 jobs and one x64 job each, three runs per job) says it changes nothing:

| | Operator passkey | Local store | `[heap]`, every child |
| --- | --- | --- | --- |
| arm64, main | 150.6–150.8; 148.9–149.3 | 141.9–142.8 | 12.6–13.0 (AWS run 9.5) |
| arm64, `21c4d8b` | 150.2–150.6; 150.3–150.6 | 141.7–142.4 | 12.5–12.9 (AWS run 9.5) |
| x64, main | 148.2–148.5 | 141.0–141.2 | 12.5–12.7 |
| x64, `21c4d8b` | 148.2–148.4 | 141.0–141.1 | 12.3–12.7 |

The branch's own `ci.yml`, called three times in a row (`actions/runs/45246934943`), fails `memory (arm64)` all three times (operator passkey 150.9, 150.7 and 150.4) and passes `memory (x64)` (148.2, 148.4, 148.2). `21c4d8b` was dropped from the branch.

### Round 3: where in the declaration step

`actions/runs/45263218365`. Three throwaway variants of `21c4d8b`, each on two arm64 jobs, three runs per job:

| Variant | `[heap]`, non-AWS children | Local store | Operator passkey |
| --- | --- | --- | --- |
| control (`21c4d8b`) | 12.5–12.8 | 141.7–142.8 | 148.9–150.7 |
| v1: no declaration step at start | **9.9–10.1** | **139.8–140.8** | **147.0–149.2** |
| v2: no scope, `UmbralContext` or application manager unless declared | 12.5–12.8 | 141.8–142.5 | 148.6–150.4 |
| v3: v2, and `plan` reads nothing | 12.4–12.7 | 141.6–142.5 | 148.7–150.7 |

The cost is the call itself. In v3, `apply` and `plan` resolve no service and read no row, and the heap is still 2.7 MiB higher. What is left is compiling them. Each is one large F# `task`, so one large state-machine method that the JIT compiles whole on the first call, fully optimised because tiered compilation is off. The JIT's working memory comes from malloc on the starting thread. It is freed afterwards, but glibc's main heap keeps it, so it stays resident. JIT code itself grows only 0.3 MiB.

### The fix (`3e2a063`)

`Program.applyDeclaration` calls `Declarations.apply` only when there is a declaration, or when `Declarations.anyDeclared` (one `SELECT EXISTS` on `declared_items`) finds something still declared, which `apply` must hand back. With neither, `apply` would plan no step, hand back nothing, warn nothing and log nothing (`plan`'s `leaving` and `UndeclarePasskey` come only from rows with `is_declared = 1`), so skipping it changes no behaviour. The start-level test "with no declaration at all, everything still declared is handed back" covers both branches: its second start hands back, its third skips. It now also asserts `anyDeclared` directly.

### The operator-passkey run reads X or X + 2 MiB

In every round, the operator-passkey run lands on one of two levels, and the difference is all in anonymous memory, the GC heap. In v1, for example, the children at 147.0 MiB have 13.0 to 13.1 MiB anonymous and those at 149.1 MiB have 15.0 to 15.2. Two MiB is the gen0 budget the image sets (`DOTNET_GCgen0size=0x200000`). Whether a gen0 collection ran just before the run went idle decides which level it reads. The local store has no such second level in these runs. A ceiling therefore has to clear the upper level.

## Round 4: the fix on the runners

`actions/runs/45264964344`, `3e2a063`, three arm64 jobs and one x64 job, three runs per job (MiB):

| Run | arm64 (9 runs) | x64 (3 runs) | Ceiling | Least to spare, arm64 |
| --- | --- | --- | --- | --- |
| Local store | 139.7–140.6 | 136.8–137.6 | 150 | 9.4 |
| Operator passkey | 146.9–149.2 | 143.6–145.9 | 150 | **0.8** |
| Passkey connector | 146.1–147.0 | 143.3–144.8 | 160 | 13.0 |
| Fediverse connector | 141.3–143.4 | 138.5–138.8 | 150 | 6.6 |
| AWS store | 149.4–150.1 | 146.2–146.8 | 160 | 9.9 |

`[heap]` is back at 9.9–10.1 MiB in every non-AWS child. Against main today (round 1), the fix takes back about 2 MiB of resident set on the local store (141.8–142.8 to 139.7–140.6) and on the operator-passkey run (148.9–151.2 to 146.9–149.2). It does not take back the 1.6 MiB that the twelve features before 019 added.

The branch's own `ci.yml`, called three times in a row (`actions/runs/45264964269`): `test` and `memory` green on both architectures, three times out of three. Two of the three green `memory (arm64)` runs carry the new warning: "Idle memory near a ceiling (arm64): operator passkey: 148.7 MiB against a 150 MiB ceiling, under 2 MiB to spare" (and 148.6). The third had the run on its lower level, under 148. Round 2's green x64 runs carried it too (148.2, 148.4, 148.2).

## Acceptance

- [x] `notes.md` has the bisected figures and names the growth: 019's declaration step at every start, 2.7 MiB of native heap. Before it, 1.6 MiB spread over twelve features.
- [x] Local-store ceiling (SC-007, 150 MiB) unchanged.
- [x] The near-ceiling warning shows in runs that are close (rounds 2 and 4).
- [x] Three consecutive `memory (arm64)` and `memory (x64)` runs on the branch pass.
- [x] x64: the operator-passkey run is at least 4.1 MiB under 150.
- [ ] arm64: the operator-passkey run at least 3 MiB under its ceiling. **Not met at 150:** its upper level is 148.6 to 149.2 MiB. It needs the maintainer's decision below.

## Decision for the maintainer: the operator-passkey ceiling

With the fix, what is left on arm64 is the operator-passkey run's upper level, the 2 MiB gen0 budget on top of 147 MiB. There is nothing cheap and safe left to take back on its path: the 1.6 MiB the features added is their code's JIT, DLL and framework pages, and the WebAuthn ceremonies cost the 7 MiB that m3m0 measured.

- **(a) The operator-passkey ceiling moves to 160 MiB, like the passkey connector (SC-008).**
  - Margin on arm64: at least 10.8 MiB (worst 149.2). The near-ceiling warning stops firing for this run.
  - Reasoning: it is a second credential path that loads the same WebAuthn library as the connector, not the Raspberry Pi promise. The default instance, the local store, stays at 150 with 9.4 MiB to spare, and so does the Fediverse run.
  - Changes: `IdleMemoryTests.fs` (the fact uses `ExternalStoreCeilingBytes`, its name and the module comment say why and give the margin), spec 011's success criterion (SC-005), and specs/008's note that the library's cost is paid once, whichever use loads it. Documentation review by `umbral-documentation`.
- **(b) Keep 150 MiB.**
  - Margin on arm64: 0.8 to 3.1 MiB, depending on the level a run lands on. The run passes today, but the warning fires on about two arm64 runs in three, and about 1 MiB more growth anywhere on the passkey path fails pull requests again.
  - The tracked cost: the next feature that adds code to every start, or to the WebAuthn path, has to win back what it adds, or this decision comes back.
