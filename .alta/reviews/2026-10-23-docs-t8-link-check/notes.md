# T8: a link check in CI — notes for the documentation review

Task: `.alta/tasks/2026-10-23-docs-t8-link-check-in-ci-l8nk.md`, audit
`docs/audit-2026-10.md` section 5, T8 (finding M23). Branch
`alta/soaring-annapurna-qdbc`, based on `main` at `0889378`.

## Files

- `.github/scripts/check-links.py` (new, executable, Python 3 stdlib only)
- `.github/workflows/ci.yml` (new job `links`)
- no doc file is touched; the dead link of W15 is left for T1

## What the script checks

Files: `README.md` and `docs/**/*.md`, sorted (or the files given as
arguments). Outside fenced code (```` ``` ```` and `~~~`) and code spans:

- inline links and images `[t](target "title")`, `![a](<target>)`;
- reference definitions `[label]: target`;
- HTML `href="..."` and `src="..."`.

A target with a URL scheme (`https:`, `mailto:`, ...) is skipped: no network
in CI. Any other target is percent-decoded and resolved against the linking
file's folder (a leading `/` against the repository root); the file or folder
must exist. When the target is a Markdown file, its `#anchor` must match,
case-insensitively, one of that file's anchors:

- ATX and setext headings, slugged as GitHub does: rendered text (code-span
  content kept, link text kept, emphasis and HTML tags dropped), lowercased,
  everything but Unicode letters, numbers, marks, space, `-` and `_` removed,
  each space to `-` (no collapsing, so "A — B" is `a--b`); duplicates `-1`,
  `-2`, ...;
- explicit `<a name="...">` and `id="..."`.

An anchor on a non-Markdown file (`#L10`) is not checked. Output is
`path:line: target: reason` for each dead link, exit 1; otherwise one line
`check-links: N links in M files, all resolve`, exit 0; exit 2 for a file
argument that does not exist.

## CI

A job of its own, `links` (one run, not one per architecture of the test
matrix), on `ubuntu-24.04` like the test jobs, which runs
`.github/scripts/check-links.py`. The release workflow calls `ci.yml`, so it
runs there too.

**Keeping main green until T1: option (a).** The step has
`continue-on-error: true`, with a comment saying T1 removes it. Until then the
run is green and the step shows its failure and output. T1 deletes that line
(and its comment) in the same PR that fixes W15.

## How it was verified

1. **On the tree as `main` stands now** (this branch = `main` + the two files
   above), `python3 .github/scripts/check-links.py`, exit 1:

   ```
   docs/connectors/oidc.md:180: ../../README.md#configuration-as-code: no heading for #configuration-as-code in README.md
   ```

   That is W15, and the only dead link, which matches the audit ("the only dead
   link in the docs"). Same output with Python 3.12 (the runner's) and 3.14.
2. **After T1's W15 fix, simulated**: a copy of the tree (`git archive HEAD`)
   with `docs/connectors/oidc.md` lines 177–203 ("Keeping it in the
   configuration file", up to "The issuer and the identifier cannot be
   changed") deleted, exit 0:

   ```
   check-links: 152 links in 11 files, all resolve
   ```
3. **The audit's own tree** (`worktree-docs-audit-2026-10`, which adds
   `docs/audit-2026-10.md` under `docs/`): the same single W15 line. The audit
   adds no dead link of its own.
4. **Negative control**: a partial copy of the tree with the README's
   `## Troubleshooting` heading renamed reports every link to
   `#troubleshooting` (`README.md:47`, `discord.md:97`, `github.md:95,171`,
   `google.md:142`, `oidc.md:335,359`, `passkey.md:184`), and every link to a
   file left out of the copy as "no such file".
5. **Parser cases**, in throwaway files outside the repository: duplicate
   headings (`#x`, `#x-1` resolve; `#x-2` does not), a heading with a code
   span, a link, emphasis and an em dash, a setext heading, `_` inside a word
   kept, `<a name>` and `id=` anchors, a reference definition, an angle-bracket
   target, a percent-encoded path, a root-relative `/b.md`, a folder target,
   `<img src>` and `<a href>`; links inside a code span, a ```` ``` ```` fence
   and a `~~~~` fence holding a shorter `~~~` line are ignored; `https:` and
   `mailto:` are skipped; a missing file argument exits 2.
6. **Linters**: `uvx ruff check` and `uvx ruff format --check` on the script,
   clean (Python, so ruff rather than shellcheck). `actionlint` 1.7.12
   (`uvx --from actionlint-py actionlint`) on `ci.yml` and `release.yml`,
   clean.

Known limits, on purpose: no `?query` handling, no link split across lines,
no YAML front matter (none of these occur in `README.md` or `docs/` today).

## CHANGELOG

None: a CI-only change, nobody who runs Umbral notices it.
