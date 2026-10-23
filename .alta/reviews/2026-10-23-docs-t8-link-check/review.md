# Review: T8, a link check in CI

Task: `.alta/tasks/2026-10-23-docs-t8-link-check-in-ci-l8nk.md`, audit
`docs/audit-2026-10.md` T8 (finding M23).
Branch: `alta/soaring-annapurna-qdbc`, commit `2be21fb` on `main` at `0889378`.
Reviewed 2026-10-23 by the documentation reviewer session, from the diff, the
implementer's `notes.md`, and my own runs of the script.

## Verdict

**Clear to merge.** No blocking item. Two nits, neither needs a re-check, and
one note for the maintainer that belongs in T1's task file, not here.

## What was checked

Against the audit's acceptance criteria for T8:

- **Fails on the W15 anchor before T1.** Run on the branch as it stands:
  exit 1, one line, `docs/connectors/oidc.md:180: ../../README.md#configuration-as-code:
  no heading for #configuration-as-code in README.md`. The audit's "only dead
  link in the docs" claim and the script agree.
- **Passes after.** On a `git archive` copy of the branch with `oidc.md`
  lines 177–203 deleted (T1's W15 change): exit 0, "152 links in 11 files, all
  resolve". I repeated the implementer's simulation rather than take it from
  the notes.
- **Runs in CI.** A job `links` on `ubuntu-24.04`, and `release.yml` reuses
  `ci.yml` through `workflow_call`, so it runs on a release too. No network:
  every target with a URL scheme is skipped.
- **The audit's own tree.** `README.md`, `docs/audit-2026-10.md`, `oidc.md`
  and `passkey.md` from PR #6's branch: the same single W15 line. The audit adds
  no dead link.
- **Parser cases the notes do not list**, in throwaway files: the apostrophe
  in "Brand an app's sign-in page" slugs to `brand-an-apps-sign-in-page` as on
  GitHub, and the wrong form `brand-an-app-s-…` is reported; a heading with a
  code span (`### The `logo` field`) resolves as `the-logo-field`; a link in a
  table cell is checked; a link to a missing file is reported with the path.
- The script is `100755` in the index, so the bare `run:` line works. Python
  3.14 here and the notes say 3.12 on the runner; nothing version-specific in
  the code.
- `continue-on-error: true` as the bridge until T1 (option a) with a comment
  that names T1: fine. The step still shows its failure and output in the run.

## Nits

1. **A link inside an HTML comment is reported.** `<!-- [x](missing.md) -->`
   in a test file came back as a dead link. GitHub renders no comment, so that
   is a false positive. No file in `README.md` or `docs/` has an HTML comment
   today (`grep -rln '<!--'` is empty), so nothing fails; blank `<!-- … -->`
   spans the way code spans are blanked when it is convenient, or leave it and
   let the first such comment be the trigger. (`DESIGN.md` has a comment block,
   but it is out of the check's scope.)
2. **With file arguments, paths print absolute.** `shown()` relativises only
   under `ROOT`, so `check-links.py README.md` from another checkout prints
   full paths. Cosmetic: CI uses the default mode, which prints
   repository-relative paths. Relativising to the current directory when the
   file is outside `ROOT` would make the argument form read the same.

## For the maintainer (outside this task)

T1's acceptance in the audit says "no link or anchor is dead (T8's check)".
That holds only once the `continue-on-error: true` line and its comment are
removed, which the comment says T1 does. Put that removal in T1's task file
as an explicit step and acceptance criterion, so it is not left behind.

## notes.md

Complete under section 6 of the audit: files, what is checked, how it was
verified (the W15 run, the simulated T1 run, the audit tree, a negative
control, parser cases, linters), the known limits, and why there is no
CHANGELOG entry (CI-only; nobody who runs Umbral notices). Agreed on all of
it.
