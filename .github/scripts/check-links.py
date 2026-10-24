#!/usr/bin/env python3
"""Check that the relative links in the Markdown docs resolve.

    .github/scripts/check-links.py [FILE.md ...]

Checks README.md and docs/**/*.md (or the files given) outside code blocks and code spans: inline
links and images, reference definitions, and HTML href/src attributes. A link with a URL scheme
(https:, mailto:, ...) is skipped, since the check makes no network calls. For any other link the
target file or directory must exist, and when it is a Markdown file the #anchor must match one of its
headings (slugged the way GitHub does) or an explicit <a name="..."> / id="..." anchor.

Prints "path:line: target: reason" for each dead link and exits 1; exits 0 when all resolve, and 2
on a bad usage (a file that does not exist). CI runs it from .github/workflows/ci.yml.
"""

import os
import re
import sys
import unicodedata
from pathlib import Path
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parents[2]

FENCE = re.compile(r"^ {0,3}(`{3,}|~{3,})(.*)$")
CODE_SPAN = re.compile(r"(?<!`)(`+)(?!`)(.+?)(?<!`)\1(?!`)")
INLINE_LINK = re.compile(r"\]\(\s*(?:<([^>]*)>|((?:[^\s()]|\([^\s()]*\))*))")
REF_DEF = re.compile(r"^[ >]{0,3}\[[^\]]+\]:\s*(?:<([^>]*)>|(\S+))")
HTML_ATTR = re.compile(r"""\b(?:href|src)\s*=\s*(?:"([^"]*)"|'([^']*)')""")
HTML_ANCHOR = re.compile(
    r"""<a\b[^>]*\bname\s*=\s*["']([^"']+)["']|\bid\s*=\s*["']([^"']+)["']"""
)
ATX = re.compile(r"^ {0,3}#{1,6}[ \t]+(.*?)(?:[ \t]+#+)?[ \t]*$")
SETEXT = re.compile(r"^ {0,3}(?:=+|-+)[ \t]*$")
SCHEME = re.compile(r"^[a-zA-Z][a-zA-Z0-9+.-]*:")

_lines_cache: dict[Path, list[tuple[int, str, str]]] = {}
_anchors_cache: dict[Path, set[str]] = {}


def lines_of(path):
    """(number, raw line, line with code spans blanked) for each line outside fenced code."""
    if path not in _lines_cache:
        out, fence = [], None
        for n, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            m = FENCE.match(raw)
            if fence:
                if (
                    m
                    and m[1][0] == fence[0]
                    and len(m[1]) >= len(fence)
                    and not m[2].strip()
                ):
                    fence = None
            elif m and not (m[1][0] == "`" and "`" in m[2]):
                fence = m[1]
            else:
                blanked = CODE_SPAN.sub(lambda s: " " * len(s[0]), raw)
                out.append((n, raw, blanked))
        _lines_cache[path] = out
    return _lines_cache[path]


def slug(heading):
    """GitHub's anchor for a heading: rendered text, lowercased, punctuation dropped, spaces to hyphens."""
    parts = []
    for i, piece in enumerate(CODE_SPAN.split(heading)):
        if i % 3 == 2:  # the content of a code span: kept as written
            parts.append(piece)
            continue
        if i % 3 == 1:  # the backtick run
            continue
        piece = re.sub(
            r"!?\[([^\]]*)\]\([^)]*\)|\[([^\]]*)\]\[[^\]]*\]",
            lambda s: s[1] or s[2],
            piece,
        )
        piece = re.sub(r"<[^>]+>|\*", "", piece)
        parts.append(re.sub(r"(?<!\w)_+(?=\S)|(?<=\S)_+(?!\w)", "", piece))
    text = "".join(parts).strip().lower()
    return "".join(
        c for c in text if unicodedata.category(c)[0] in "LNM" or c in " -_"
    ).replace(" ", "-")


def anchors_of(path):
    if path not in _anchors_cache:
        found, seen, prev = set(), {}, None
        for n, raw, blanked in lines_of(path):
            heading = None
            if m := ATX.match(raw):
                heading = m[1]
            elif (
                prev
                and prev[0] == n - 1
                and SETEXT.match(raw)
                and not re.match(r"^\s*([#>|*+-]|\d+[.)])", prev[1])
            ):
                heading = prev[1]
            if heading is not None:
                base = slug(heading)
                count = seen.get(base, 0)
                seen[base] = count + 1
                found.add(base if count == 0 else f"{base}-{count}")
            prev = (n, raw) if raw.strip() else None
            for m in HTML_ANCHOR.finditer(blanked):
                found.add((m[1] or m[2]).lower())
        _anchors_cache[path] = found
    return _anchors_cache[path]


def targets(path):
    """(line number, target) for each link of a Markdown file."""
    for n, _, blanked in lines_of(path):
        for pattern in (INLINE_LINK, REF_DEF, HTML_ATTR):
            for m in pattern.finditer(blanked):
                target = next((g for g in m.groups() if g), "")
                yield n, target


def shown(path):
    return path.relative_to(ROOT).as_posix() if path.is_relative_to(ROOT) else str(path)


def check(path, target):
    """The reason a link is dead, or None."""
    base, _, anchor = target.partition("#")
    base = unquote(base)
    if not base:
        dest = path
    else:
        dest = (ROOT / base.lstrip("/")) if base.startswith("/") else path.parent / base
        dest = Path(os.path.normpath(dest))
        if not dest.exists():
            return f"no such file {shown(dest)}"
    if (
        anchor
        and dest.suffix == ".md"
        and dest.is_file()
        and unquote(anchor).lower() not in anchors_of(dest)
    ):
        return f"no heading for #{anchor} in {shown(dest)}"
    return None


def main(args):
    if args:
        files = [Path(a).resolve() for a in args]
        missing = [a for a, f in zip(args, files) if not f.is_file()]
        if missing:
            print(f"check-links: no such file: {', '.join(missing)}", file=sys.stderr)
            return 2
    else:
        files = [ROOT / "README.md", *sorted((ROOT / "docs").rglob("*.md"))]

    dead, total = [], 0
    for path in files:
        for n, target in targets(path):
            if not target or SCHEME.match(target):
                continue
            total += 1
            if reason := check(path, target):
                dead.append((shown(path), n, f"{shown(path)}:{n}: {target}: {reason}"))
    for _, _, line in sorted(dead):
        print(line)
    if dead:
        return 1
    print(f"check-links: {total} links in {len(files)} files, all resolve")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
