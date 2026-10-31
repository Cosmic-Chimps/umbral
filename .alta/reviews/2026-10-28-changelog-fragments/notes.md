# Release notes as one file per change in `changelog.d/`: notes for review

Task `2026-10-28-changelog-fragments-in-changelog-d-cl6d`. Branch `alta/zippy-kenya-3qj9`, on
top of `main` at `3a235da` (0.7.0 released, `## Unreleased` empty: the precondition held, nothing
had to be moved into a fragment).

## What changed

- `.github/scripts/changelog.sh`: Frunchy's script (`df4ff21` in FrunchyBackend), with `check`,
  `fragment_files`, `gather`, `problems`; `prepare` gathers `changelog.d/*.md` (never `README.md`)
  into `## X.Y.Z - <date>` above the newest released section and deletes the files; `unreleased`
  previews the gathering. **`released BASE_FILE` is kept as Umbral had it** (CI compares with the
  commit before the push, with the `[edit released changelog]` escape), as the task asks; Frunchy and
  Megáfono compare with the release tags instead. So the script is byte-identical to Frunchy's apart
  from that function, the `section` helper's second argument it needs, the usage line and the header
  paragraph; the message of `released` now says to put notes in a `changelog.d/` file.
- `.github/scripts/changelog-test.sh`: Frunchy's tests, with seven shellcheck findings fixed
  (`ls | grep` and `A && B || C` patterns, SC2010, SC2143, SC2015); the logic is the same, 55 checks.
  The fixed file should be copied back to Frunchy and Megáfono so the three stay identical.
- `changelog.d/README.md` (the rules, in Umbral's words) and `changelog.d/changelog-fragments.md`
  (this change's own note, under Changed).
- `CHANGELOG.md`: the `## Unreleased` heading is gone and the header points at `changelog.d/README.md`.
  The released sections are byte-identical (`released` against `main`, below).
- `.github/workflows/ci.yml`: the steps "Release notes in changelog.d are well formed" (`check`) and
  "Changelog script tests", after "Released changelog sections are unchanged" (unchanged, comment
  reworded).
- `.github/workflows/release.yml`:
  - dispatch: `prepare` gathers and deletes; the commit step does `git add -A -- CHANGELOG.md
    changelog.d` and commits when the index has a change; an empty `changelog.d/` fails with
    Frunchy's `::error::` message;
  - pushed tag: the `changelog` job prepares in its own checkout as before; "Record the changelog on
    main" copies the artifact's `CHANGELOG.md` and, only when that differs from the tagged commit's
    (so `prepare` ran), deletes every note file of `changelog.d/` (at the tagged commit, which is
    what `prepare` gathered) and stages both; then the same "only if main is still the tagged commit"
    rule, otherwise a warning that now says what to do with files added after the tag;
  - prerelease: unchanged path; its preview says "from the notes waiting in changelog.d".
- README: the Release section (how to write a note, step 2, the pushed-tag paragraph, the prerelease
  and merge bullets) and the Contributing line. No CLAUDE.md in Umbral. The specs and
  `docs/audit-2026-10.md` keep their historical "under Unreleased" wording.
- The queued task files in the main checkout's `.alta/tasks/` (11 files, untracked) now say
  "a `changelog.d/` file (see its README)".

## Acceptance

`bash .github/scripts/changelog-test.sh`: `changelog-test: all 55 checks passed`.

`.github/scripts/changelog.sh check` on the branch:

```
changelog: changelog.d/ and CHANGELOG.md are in order
```

`git show main:CHANGELOG.md > /tmp/base.md && .github/scripts/changelog.sh released /tmp/base.md`:

```
changelog: every released section is unchanged
```

shellcheck (`koalaman/shellcheck:stable`) on both scripts: no findings. actionlint 1.7.12
(`rhysd/actionlint`, with its shellcheck on the `run:` steps): `Found 0 errors in 2 files`.
`.github/scripts/check-links.py`: `185 links in 14 files, all resolve`.

### Throwaway copy

A temporary tree with this branch's `CHANGELOG.md`, the README and two sample fragments,
`012-sample-feature.md` (Added, Upgrade) and `fix-sample.md` (Fixed, Added), through `CHANGELOG`
and `CHANGELOG_DIR`:

```
$ changelog.sh unreleased
### Added

- A sample feature, with a line that
  continues indented.
- Another added thing, from the second file.

### Fixed

- A sample fix.

### Upgrade

- Take a backup first.

$ changelog.sh prepare 9.9.9
changelog: changelog.d/ is now "## 9.9.9 - 2026-10-31" in .../CHANGELOG.md; commit it, then tag v9.9.9
exit 0

$ ls changelog.d
README.md

$ sed -n '15,29p' CHANGELOG.md
## 9.9.9 - 2026-10-31

### Added

- A sample feature, with a line that
  continues indented.
- Another added thing, from the second file.

### Fixed

- A sample fix.

### Upgrade

- Take a backup first.

$ changelog.sh check
changelog: changelog.d/ and CHANGELOG.md are in order
exit 0
```

With a `## Unreleased` heading put back above `## 9.9.9`:

```
$ changelog.sh check
changelog: CHANGELOG.md has a "## Unreleased" section; notes now live in changelog.d/. Move its bullets into a new changelog.d/<name>.md (one "### Added/Changed/Fixed/Security/Upgrade" subsection per kind) and delete the section.
exit 1
```

With a bad fragment `bad-one.md` (`# Title`, `### Misc`, a stray line):

```
$ changelog.sh check
changelog: changelog.d/bad-one.md:1: heading "# Title" is not one of ### Added, ### Changed, ### Fixed, ### Security, ### Upgrade
changelog: changelog.d/bad-one.md:3: heading "### Misc" is not one of ### Added, ### Changed, ### Fixed, ### Security, ### Upgrade
changelog: changelog.d/bad-one.md:6: text that is not a "- " bullet or an indented continuation line
exit 1
```

## After the code review (B1): the one-time move

The reviewer found that the maintainer's local `main` (`3a235da`, the spec 011 merge after v0.7.0)
holds spec 011's six bullets inside the released `## 0.7.0` section: the trap this task removes,
once more. The first `released` run above passed only because it compared with that `main`. GitHub's
`main` (`4d7cb2c`) is clean. The fix is Frunchy's `e4986d0` move:

- `## 0.7.0` is copied back from `v0.7.0:CHANGELOG.md` (the section is now identical to the tag's:
  `notes 0.7.0` from both files has no diff);
- the six bullets are `changelog.d/011-operator-passkeys.md`, word for word: Added (3), Changed (2),
  Upgrade (schema 8).

`changelog.sh released` against the tag and against GitHub's `main`:

```
$ git show v0.7.0:CHANGELOG.md > /tmp/v070.md && .github/scripts/changelog.sh released /tmp/v070.md
changelog: every released section is unchanged
$ git show origin/main:CHANGELOG.md > /tmp/origin.md && .github/scripts/changelog.sh released /tmp/origin.md
changelog: every released section is unchanged
```

Against the maintainer's local `main` (`3a235da`) it now fails, as it must: that file's `## 0.7.0`
is the wrong one, and this branch corrects it.

```
$ git show main:CHANGELOG.md > /tmp/main.md && .github/scripts/changelog.sh released /tmp/main.md
changelog: released sections changed: 0.7.0. Released notes are never edited; put this
changelog: branch's new notes in a file in changelog.d/ (see its README). To correct a released
changelog: section on purpose, put "[edit released changelog]" in the commit message or the
changelog: pull request's title.
```

CI compares a push with the commit before it, `4d7cb2c` on GitHub, so pushing `main` with this
branch merged passes; pushing the local `main` as it is would fail. `check` passes, and `unreleased`
now previews spec 011's notes and this change's note: what 0.8.0 will say.

## For the maintainer

- Merge this branch before pushing `main`: it carries the corrected `## 0.7.0`.
- A branch started before this change that still adds bullets under `## Unreleased` will fail CI's
  `check` after the merge (the message says what to do); `released` still catches a bullet that
  lands inside a released section.
- The fixed `changelog-test.sh` is worth copying back to Frunchy and Megáfono, so a fix keeps
  carrying over in both directions.
