# Documentation review: the operator-passkey idle-memory ceiling (task mem2)

Branch `alta/proud-jasmine-f4s3`, commit 8cab3ab, with the changelog fragment from 3e2a063. Read
against the notes ("Decision for the maintainer", "The maintainer's choice") and the code review.
Reviewed 2026-11-24 by the documentation reviewer.

## Verdict

**Clear, no blocking items, two nits.** Every changed sentence states the maintainer's choice (a)
the same way. An instance whose operator uses a passkey idles under 160 MiB, the passkey connector's
ceiling, because both load the same WebAuthn library. The default instance keeps 150 MiB. The
amendment carries its date and its reason. No re-check is needed.

## What was checked

- **spec 011 SC-005**: amended, with the date (2026-11-24) and the reason (under 1 MiB to spare at
  150 MiB on GitHub's arm64 runner), and "an instance whose operator has no passkey stays within
  Principle II's 150 MiB". Right.
- **spec 011 plan, "Memory"**: the new ceiling, and the runner figures 146.9–149.2 MiB on arm64,
  which are the notes' round figures. Right.
- **spec 008 SC-008**: the added sentence removes the contradiction ("no passkey connector, under 150
  MiB") for an instance whose operator uses a passkey, and points at spec 011. Right.
- **`docs/connectors/passkey.md`, Memory**: "Without the connector it stays under 150 MB, unless the
  operator signs in with a passkey of their own, which loads the same library and has the same 160 MB
  ceiling." Right, and in the docs' MB as elsewhere on that page.
- **`IdleMemoryTests.fs`**: the fact name ("stays under 160 MB resident"), the constant's comment (its
  three runs) and the module comment (why it shares the ceiling, the two gen0 levels 2 MiB apart, the
  figures and the margin of at least 10.8 MiB) agree with the specs and the notes.
- **`changelog.d/idle-memory-declaration-step.md`** (Fixed): "about 2 MiB less", matching round 1
  (local store 141.8–142.8 to 139.7–140.6 MiB). "Since 0.12.0, every start ran the declaration step
  even with no `UMBRAL__DECLARATION` set" is the cause the bisect found. It says what changed for
  someone who runs Umbral, and nothing they must do. Right.
- **What stays at 150**: the README, PRODUCT.md, the constitution and the run pages promise the
  default instance, which keeps 150 MiB with 9.4 MiB to spare. No page promised 150 MB for an operator
  passkey. Right, with N1.
- `check-links.py`: 474 links resolve. `changelog.sh check`: in order.

## Nits

1. **The README's one exception is now one of three.** "Umbral is designed to idle under 150 MB of
   memory (under 160 MB with an external secret store)" names only the secret store. The passkey
   connector has been at 160 since spec 008, and now an operator passkey is too. That is the setup the
   operator-passkeys page recommends ("Add a passkey", then "Turn password sign-in off"). An operator
   who follows it and sees 155 MB may think something is wrong. Consider "(under 160 MB with passkeys
   or an external secret store)" in the README, and the same parenthetical in PRODUCT.md's Operating
   Context and in `docs/vision.md`. The Raspberry Pi promise is unchanged by it. This is wording the
   maintainer owns, so it is not a condition of this review.
2. **PRODUCT.md's constitution line is stale** (not this branch's doing): it says "version 3.1.0,
   2026-11-11", and the constitution is 3.2.0, amended 2026-11-14 (spec 019). One line to fix while
   PRODUCT.md is open for N1, or on its own.
