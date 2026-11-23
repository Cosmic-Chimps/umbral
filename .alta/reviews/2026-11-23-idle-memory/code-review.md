# Code review, task mem2 (operator-passkey idle memory at its arm64 ceiling)

Reviewer: the `umbral` session. Commits `3e2a063` (the fix) and `7885860` (the notes) on
`alta/proud-jasmine-f4s3`, base main `b76473a`.

**Verdict: clear to merge from the code side.** No nits. The one open item, the operator-passkey
ceiling (option a or b), is the maintainer's, as the task said. My view is below.

## The fix: skip the declaration step when there is nothing to do

- `applyDeclaration` calls `Declarations.apply` only when there's a declaration, or when
  `anyDeclared` (`SELECT EXISTS … declared_items WHERE is_declared = 1`) finds something to hand
  back.
- **It's equivalent.** With no declaration, `plan` yields no app step, `leaving` and
  `UndeclarePasskey` come only from rows with `is_declared = 1`, and `nothingToWrite` returns
  before any transaction. With `anyDeclared` false, the old `apply` read the database, wrote
  nothing, warned nothing and logged nothing (`handedBack` false). Skipping it removes only the
  reads and the JIT cost. Both branches are tested: a start that hands back, then one that skips.
  `anyDeclared` is asserted directly.
- With a declaration, or something still declared, the path is unchanged, so C1 (stop the start on
  any failure) and C2 (adopt only an identical passkey connector) from spec 019 still hold.
- `(found :?> int64)` matches what `Microsoft.Data.Sqlite` returns for `EXISTS`.

## The measurements

- **Method:** a bisect of 14 main commits on GitHub's runners, three runs each, read from the job
  logs, with two wrong guesses written down and disproved (two SQLite connections; the variants v2
  and v3). That's the method the task asked for, and it names the cause: spec 019's
  declaration step at every start leaves about 2.7 MiB in glibc's native heap from JIT-ing
  `apply`'s large task methods. Variant v1 isolates it.
- **With the fix, on arm64:**
  - local store 139.7–140.6 MiB (9.4 MiB spare);
  - operator passkey 146.9–149.2 MiB, about 2 MiB back;
  - x64 at least 4.1 MiB spare.
- **The two levels 2 MiB apart** (whether a gen0 collection ran before idle) explain the "passes or
  fails by a fraction" behaviour better than noise did. Good finding.
- The rest of the creep, 1.6 MiB across twelve features, is code pages and JIT, with nothing
  cheap left. That's accepted.

## The near-ceiling warning

- The gated step now captures `dotnet test`'s status, parses the figures the test writes into
  `$GITHUB_STEP_SUMMARY`, prints a `::warning::` for any run within 2 MiB of its ceiling, then
  `exit 1` on failure as before. `$ARCH` comes from the step's `env`.
- No retry and no new `continue-on-error`. The existing `continue-on-error` for non-pull-request
  events on the memory job, and on the comparison step, are unchanged from main.
- Seen working: the warning fired on 148.7 and 148.6 MiB runs.

## The decision (the maintainer's)

My recommendation: **(a), the operator-passkey ceiling at 160 MiB, like SC-008.**
- The Raspberry Pi promise of Principle II is the default instance, the local store, and that stays
  at 150 with 9.4 MiB to spare.
- An operator passkey loads the same WebAuthn library as the passkey connector, which already has
  160 for exactly that cost (the 7 MiB m3m0 measured).
- Under (b) the run keeps 0.8 MiB on its upper level, the warning fires on about two arm64 runs in
  three, and the next small addition to the start path fails pull requests again: the state this
  task was opened to end.

If (a) is chosen, the follow-up is what the notes list: the fact uses `ExternalStoreCeilingBytes`
with its comment and margin, spec 011's SC-005 and spec 008's note change, and umbral-documentation
reviews those.
