# Review: T9, a vision document that is one

Task: `.alta/tasks/2026-10-23-docs-t9-vision-document-v9sn.md`, audit
`docs/audit-2026-10.md` T9 (W24, S6).
Branch: `alta/lithe-camellia-sgxm`, commit `e495349` on `main` at `1f3a89e`.
Reviewed 2026-10-23 by the documentation reviewer session, from the diff, the
implementer's `notes.md`, the constitution and its git history, and my own
runs.

## Verdict

**Clear to merge.** No blocking item. Two nits, neither needs a re-check, and
one note for the maintainer.

## What was checked

- **The acceptance.** `docs/vision.md` has no "Hodor", no chat framing and no
  YAML row (my grep for hodor, yaml, "I understand", "you want" and "verdict"
  finds nothing). The constitution's two references still hold: line 64 names
  the path, which exists, and line 232 cites "the vision" for scope creep as
  the highest-likelihood risk, which the new "What could go wrong" says in
  its first sentence. PRODUCT.md:173 lists the path and describes nothing
  about its content, so it is not stale.
- **The README link the audit assumed.** There is none; the implementer was
  right to say so rather than invent one. See the note below.
- **Every statement against its source.** The table in `notes.md` names a
  constitution line or code line for each; I read them. In particular the
  scope history: 1.0.0 on 2026-10-09 (`b269ea9`, with Principle V
  "Config-as-Code Parity"), 2.0.0 on 2026-10-10 (`34b86df`, spec 004), 2.1.0 on
  2026-10-15 (`4f017a1`, spec 006), 3.0.0 on 2026-10-19 (`2b9e0cc`, spec 008)
  match the version lines in the constitution's git history exactly. The
  exclusion list matches Principle VII word for word, including user
  federation, which the README's own list omits. "Umbral does not send email"
  is Principle VII's invitation bullet. "Three values per app" is Principle
  III. The five-minute aim is Principle II.
- **The history file.** A diff against `main`'s `docs/vision.md` shows only
  the new first line, a rule, and the two `../specs/` to `../../specs/` link
  fixes; nothing else changed. The first line's "committed on 2026-10-09" is
  right (`e143874`). It says what the file is and that "Hodor" was the working
  title, which is all a reader needs.
- **Links.** `check-links.py docs/vision.md docs/history/2026-vision-conversation.md README.md`:
  74 links in 3 files, all resolve. The full run's one failure is W15 in
  `oidc.md`, which T1 removes on its own branch; the two do not touch the same
  files.
- **Voice.** It reads like the README: short declaratives, the product's own
  nouns, no marketing. "What it will not do" and the Keycloak/Authentik
  sentence match the README's "What Umbral is not" in substance without
  copying it.
- **No CHANGELOG entry.** Agreed: nobody who runs Umbral notices.

## Nits

1. **The memory figure** ("idles under 150 MB of memory, 160 MB with an
   external secret store"). Audit W14 notes the tests assert MiB; T4 will
   decide how the README words it. When T4 lands, the vision should use the
   same words, so it does not become the one file that says something
   different. Nothing to change now.
2. **"the admin area has three screens, one for each".** True, and it is the
   constitution's framing; after T1 the README adds "and the Credential page".
   A reader who goes from one to the other will not be confused, but if you
   want them identical, "three screens, one for each, and a page for the
   operator's own credential" costs half a line.

## For the maintainer (outside this task)

The audit's T9 acceptance assumed the README links to the vision. It never
did, so a prospective adopter reading the README cannot find this page. One
line in Contributing ("The vision is in `docs/vision.md`") would fix that. It
is a README edit, so it belongs to a task that already owns the README (T3 or
T7), not to T9.

## notes.md

Complete under section 6 of the audit: files, the acceptance with the greps
and their result, every statement with its constitution or code line, the
link check, what was verified and how, and why there is no CHANGELOG entry.
The table is the most careful one I have seen in this series. Agreed on all
of it.
