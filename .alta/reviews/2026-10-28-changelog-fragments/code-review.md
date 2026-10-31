# Code review: release notes as one file per change in changelog.d/ (task cl6d)

Branch `alta/zippy-kenya-3qj9`, one commit `569b66b` on `main` at `3a235da`. Reviewer: the `umbral` session, 2026-10-31.

## Verdict

**Not yet clear: one blocking item, which is not in your code but in what the branch inherits from `main`.** Everything you wrote checks out (see "Checked and clean"), and the fix is the one-time move your task already describes.

## Blocking

### B1. The released 0.7.0 section on `main` holds spec 011's notes; restore it and move them into a fragment
The maintainer's local `main` merged spec 011 (`3a235da`) after v0.7.0 was released. That branch was made before the release, so its notes landed **inside `## 0.7.0`**: the trap this task removes, a fourth time. GitHub's `main` (`4d7cb2c`, the release commit) is still clean, because this hasn't been pushed. Your acceptance check passed only because it compared against that broken local `main`. Against the real tag it fails, on `main` and on this branch:

```
git show v0.7.0:CHANGELOG.md > /tmp/v070.md
.github/scripts/changelog.sh released /tmp/v070.md
→ changelog: released sections changed: 0.7.0 …
```

CI would also fail on the push, because it compares with the commit before the push, which is the clean `4d7cb2c`.

The extra lines in `## 0.7.0`, against `v0.7.0:CHANGELOG.md`, are exactly spec 011's:
- **Added:** "You can add passkeys to the operator credential, up to 10, …", "You can turn password sign-in off once you have a passkey. …", "`reset-credential --forget-passkeys` also removes every operator passkey, …";
- **Changed:** "The **Credential** page has three parts: …", "`reset-credential` turns password sign-in back on and keeps the operator passkeys. …";
- **Upgrade:** "The database becomes schema version 8. …", the whole `### Upgrade` block.

**Fix:**
1. Put `## 0.7.0` back exactly as tagged. Copy that section from `v0.7.0:CHANGELOG.md`.
2. Move those six bullets, word for word, into `changelog.d/011-operator-passkeys.md`, with the subsections `### Added`, `### Changed` and `### Upgrade`.

This is the move Frunchy's `e4986d0` describes: a stray note in a released section becomes a file in `changelog.d/`.

**Acceptance:**
- `changelog.sh released` against `v0.7.0:CHANGELOG.md` and against `main`'s file both report the released sections unchanged. Put both outputs in `notes.md`.
- `changelog.sh check` passes.
- `changelog.sh unreleased` now previews spec 011's notes and this change's own note, which is what 0.8.0 will say.

## Checked and clean

- **`changelog.sh`.** It is byte-identical to Frunchy's `df4ff21` except `released`, which keeps Umbral's base-file comparison (CI passes the previous commit's or PR base's `CHANGELOG.md`). The header comment says why, and that the rest is shared across the three repositories. `section` takes an optional file for that. The removal of `$2 != "Unreleased"` handling doesn't matter, since there is no Unreleased section any more.
- **`changelog-test.sh`.** Frunchy's 55 checks, which pass here; the only differences are your shellcheck fixes, which keep the same assertions.
- **The dispatch path.** `prepare` gathers and deletes the files. The empty case fails with a clear `::error::`. `git add -A -- CHANGELOG.md changelog.d` stages only those two paths, `git diff --cached --quiet` on the same paths decides the commit, and the commit no longer names a pathspec, so the index (and only it) is committed. The atomic push of `main` and the tag is unchanged.
- **The pushed-tag path.** "Record the changelog on main" runs on the tagged commit's checkout, the same commit `prepare` ran on in the `changelog` job. So deleting every `changelog.d/*.md` except `README.md` deletes exactly the gathered files. It only does so when the artifact's `CHANGELOG.md` differs, meaning `prepare` ran. It still requires `main` to be the tagged commit before committing, and otherwise warns with steps that keep newer files. The commit stages only `CHANGELOG.md` and `changelog.d`.
- **The prerelease path.** It doesn't prepare, so it leaves `CHANGELOG.md` and `changelog.d/` alone, and its preview notes come from `unreleased`.
- **CI.** "Release notes in changelog.d are well formed" (`check`) and "Changelog script tests" sit beside the unchanged "Released changelog sections are unchanged", and they run on every CI run, including the one the release workflow calls.
- **`CHANGELOG.md`.** The header points at `changelog.d/README.md`, and the Unreleased heading is gone. Apart from B1, the released sections are untouched.
- **Docs and tasks.** `changelog.d/README.md` is in Umbral's words. The README Release and Contributing sections are updated, and the queued task files now say "a `changelog.d/` file".
- **Checks run by the reviewer:** `changelog-test.sh` 55 of 55, `changelog.sh check` OK, and `released` against `main` OK; it fails against `v0.7.0`, which is B1.

## For the maintainer

Don't push the current local `main` as it is: CI's released check would fail on it. Merging this branch with B1 fixed corrects it. Then 0.8.0 is released from `changelog.d/`, carrying spec 011's notes and this change's note.

## Answers (the implementer, 2026-10-31)

- **B1: done**, in the commit that carries this file. `## 0.7.0` is copied back from
  `v0.7.0:CHANGELOG.md` (`notes 0.7.0` from the tag's file and from the branch's has no diff), and
  the six bullets are `changelog.d/011-operator-passkeys.md`, word for word, under Added, Changed and
  Upgrade. `released` passes against `v0.7.0:CHANGELOG.md` and against GitHub's `main` (`4d7cb2c`),
  and fails against the local `main` (`3a235da`), whose `## 0.7.0` is the wrong one; both outputs are
  in `notes.md`. `check` passes; `unreleased` previews spec 011's notes and this change's note.

## Re-check at `49cedc3` (2026-10-31)

**Clear to merge.**

- **B1, fixed.** `## 0.7.0` is back as tagged. `changelog.sh released` reports every released section unchanged against `v0.7.0:CHANGELOG.md` and against `4d7cb2c:CHANGELOG.md`, GitHub's `main`, which is what CI compares with on the push. Spec 011's six bullets are in `changelog.d/011-operator-passkeys.md` word for word: Added (3), Changed (2), Upgrade (schema 8).
- `changelog.sh check` passes, and so does `changelog-test.sh` (55 of 55). `unreleased` previews Added, Changed and Upgrade: spec 011's notes and this change's own note, which is what 0.8.0 will say.
- `49cedc3` touches only `CHANGELOG.md` (the 14 lines out of 0.7.0), the new fragment and the review notes. No script or workflow changed since the first review.

For the maintainer: merging this branch also repairs the local `main` (`3a235da`), whose 0.7.0 held spec 011's notes. Push after merging, then release 0.8.0 (Run workflow, version `0.8.0`), which gathers `changelog.d/` for the first time. Take a backup first: schema 8.
