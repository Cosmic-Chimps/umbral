# Code review: a passkey's Remove on the Users card is a red ghost

Branch `alta/clever-byzantium-j92s`, `main..3fb7214`. Reviewer: the `umbral` session, 2026-10-26.

## Verdict

**Clear to merge.** No blocking items, no nits.

## What was checked

- **`Users.fs`:** the Remove button's class changes from `btnSm Danger` to `btnSm Ghost + " text-error hover:text-error"`. That is the same string `Apps.fs:476` (Remove logo) and `PasskeyConnector.fs:270` (Revoke) use. The form, its antiforgery input, its route and `confirmSubmit` are untouched, so behaviour is the same.
- **The stylesheet:** `.text-error` and `hover\:text-error` are already in the committed `umbral.css`, so it doesn't need regenerating.
- **The test:** `the Users rows show the passkey pill and list` now asserts exactly one `um-btn-danger` per card (Delete), and that the passkey's button is `um-btn-ghost` with `text-error`.
- **Tests run by the reviewer:** `dotnet test --filter "FullyQualifiedName~PasskeyAdmin|FullyQualifiedName~Users"` at `3fb7214`: 24 passed, 0 failed.

## Merging

This branch and `alta/faithful-atlas-chdr` (task r3cv) both change `row` in `Users.fs` and the same test file. `git merge-tree` shows that both merge cleanly. The only conflict is `CHANGELOG.md`, where both add a bullet under Unreleased › Changed: keep both bullets.
